using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SupportAtos.Infrastructure.Persistence;

namespace SupportAtos.DevSetup;

// Secrets never enter command arguments or diagnostics. No exception/SQL logging.
internal static class SetupEntry
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) throw new SetupFailure("Este utilitario de setup exige Windows.");
            if (args.Length != 2) throw new SetupFailure("Uso interno: DevSetup <prepare|docker|status|migrate|bootstrap|inspect> <raiz>.");
            var setup = new LocalSetup(Path.GetFullPath(args[1]));
            await setup.Execute(args[0]);
            return 0;
        }
        catch (SetupFailure ex) { Console.Error.WriteLine(ex.Message); return 1; }
        catch { Console.Error.WriteLine("Preparacao interrompida. Confira Docker, conexao e arquivos locais; nenhum detalhe sensivel foi registrado. Execute setup.ps1 -CheckOnly."); return 1; }
    }
}

[SupportedOSPlatform("windows")]
internal sealed partial class LocalSetup(string root)
{
    private string Api => Path.Combine(root, "src/backend/src/SupportAtos.Api");
    private string Compose => Path.Combine(root, "src/backend/docker-compose.yml");
    private IConfigurationRoot Configuration() => WebApplication.CreateBuilder(new WebApplicationOptions
    {
        ApplicationName = typeof(SupportAtos.Api.SupportAtosDesignTimeDbContextFactory).Assembly.GetName().Name,
        ContentRootPath = Api,
        EnvironmentName = "Development",
        Args = []
    }).Configuration;

    public async Task Execute(string action)
    {
        switch (action)
        {
            case "connection": await ConfigureDockerConnection(true); break;
            case "session": await ConfigureDockerConnection(false); break;
            case "prepare":
                await PrepareConnection();
                await PrepareJwt();
                break;
            case "docker": await PrepareDocker(); break;
            case "inspect":
                Connection();
                ValidateJwt(Configuration());
                Console.WriteLine("Conexao Docker e configuracao JWT validas (sem acesso ao banco).");
                break;
            case "status": await DatabaseStatus(false); break;
            case "status-current": await DatabaseStatus(true); break;
            case "certificate":
                using (var certificate = X509Certificate2.CreateFromPemFile(
                    Environment.GetEnvironmentVariable("SUPPORTATOS_HTTPS_CERT")!,
                    Environment.GetEnvironmentVariable("SUPPORTATOS_HTTPS_KEY")!))
                using (var chain = new X509Chain())
                {
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    if (!certificate.HasPrivateKey || certificate.NotAfter <= DateTime.Now
                        || certificate.NotBefore > DateTime.Now
                        || certificate.GetNameInfo(X509NameType.DnsName, false) != "localhost" || !chain.Build(certificate))
                        throw new SetupFailure("Par HTTPS invalido, expirado ou nao confiavel. Restaure/confie no certificado local; nenhum arquivo foi sobrescrito.");
                }
                Console.WriteLine("Par HTTPS localhost valido e confiavel; arquivos preservados.");
                break;
            case "migrate": await Migrate(); break;
            case "bootstrap": await Bootstrap(); break;
            default: throw new SetupFailure("Acao de setup desconhecida.");
        }
    }

    private NpgsqlConnectionStringBuilder Connection()
        => SetupSafety.LocalConnection(Configuration().GetConnectionString("DefaultConnection") ?? "");

    private async Task SaveMissing(Dictionary<string, string> values)
    {
        // Re-read immediately before writing; never replace a configured local secret.
        var config = Configuration();
        foreach (var key in values.Keys)
        {
            if (SetupSafety.HasLocalValue(config, key))
                throw new SetupFailure($"Configuracao local ja existe para {key}; nenhuma substituicao automatica foi realizada.");
        }
        await Run("dotnet", ["user-secrets", "set", "--project", Api], input: JsonSerializer.Serialize(values));
    }

    private async Task PrepareConnection()
    {
        var config = Configuration();
        var value = config.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(value) && !value.Contains("__SUA_SENHA__", StringComparison.Ordinal))
        {
            SetupSafety.LocalConnection(value);
            Console.WriteLine("Conexao existente preservada: localhost:5433/supportatos (admin).");
            return;
        }
        Console.WriteLine("Destino fixo: 127.0.0.1:5433 / supportatos / admin.");
        Console.WriteLine("Volume existente: informe a senha real. Volume novo: escolha uma senha exclusiva.");
        var password = ReadSecret("Senha PostgreSQL (sem eco): ");
        if (password.Length < 1 || password != ReadSecret("Confirme a senha: "))
            throw new SetupFailure("Senhas vazias ou diferentes. Execute novamente; nenhuma senha foi alterada no PostgreSQL.");
        var connection = new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = 5433, Database = "supportatos", Username = "admin", Password = password };
        await SaveMissing(new() { ["ConnectionStrings:DefaultConnection"] = connection.ConnectionString });
        Console.WriteLine("Conexao salva nos User Secrets da API; senha do banco nao foi alterada.");
    }

    private static void ValidateJwt(IConfiguration config)
    {
        SetupSafety.MatchingKeys(config["Auth:PrivateKeyPem"] ?? "", config["Sso:PublicKeyPem"] ?? "");
        if (string.IsNullOrWhiteSpace(config["Auth:Issuer"]) || config["Auth:Issuer"] != config["Sso:Issuer"]
            || string.IsNullOrWhiteSpace(config["Auth:Audience"]) || config["Auth:Audience"] != config["Sso:Audience"]
            || !string.IsNullOrWhiteSpace(config["Sso:JwksUrl"]))
            throw new SetupFailure("Auth/Sso divergentes ou JWKS externo configurado. Preserve os valores e revise a configuracao local antes de continuar.");
    }

    private async Task PrepareJwt()
    {
        var config = Configuration();
        var privatePem = config["Auth:PrivateKeyPem"];
        if (!string.IsNullOrWhiteSpace(privatePem))
        {
            ValidateJwt(config);
            Console.WriteLine("Configuracao JWT existente validada e preservada.");
            return;
        }
        var keys = Path.Combine(root, "src/backend/tools/SupportAtos.TokenMinter/keys");
        var privatePath = Path.Combine(keys, "dev-private.pem");
        var publicPath = Path.Combine(keys, "dev-public.pem");
        if (File.Exists(privatePath) != File.Exists(publicPath))
            throw new SetupFailure("Par JWT incompleto. Restaure o arquivo ausente; nenhum arquivo existente sera sobrescrito.");
        if (!File.Exists(privatePath))
        {
            // Refuse to replace a local validation key, even when signing is not configured.
            if (SetupSafety.HasLocalValue(config, "Sso:PublicKeyPem"))
                throw new SetupFailure("Chave publica local ja configurada sem chave privada. Restaure o par; nao sera gerado um substituto.");
            Directory.CreateDirectory(keys);
            using var rsa = RSA.Create(2048);
            // CreateNew protects existing files, including a concurrent setup.
            using (var file = new FileStream(privatePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file)) writer.Write(rsa.ExportPkcs8PrivateKeyPem());
            using (var file = new FileStream(publicPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file)) writer.Write(rsa.ExportSubjectPublicKeyInfoPem());
        }
        privatePem = await File.ReadAllTextAsync(privatePath);
        var publicPem = await File.ReadAllTextAsync(publicPath);
        SetupSafety.MatchingKeys(privatePem, publicPem);
        var values = new Dictionary<string, string> { ["Auth:PrivateKeyPem"] = privatePem };
        if (config["Sso:PublicKeyPem"] != publicPem)
        {
            if (SetupSafety.HasLocalValue(config, "Sso:PublicKeyPem"))
                SetupSafety.MatchingKeys(privatePem, config["Sso:PublicKeyPem"]!);
            else values["Sso:PublicKeyPem"] = publicPem;
        }
        foreach (var name in new[] { "Issuer", "Audience" })
        {
            if (string.IsNullOrWhiteSpace(config[$"Auth:{name}"])) values[$"Auth:{name}"] = config[$"Sso:{name}"] ?? "";
            else if (config[$"Auth:{name}"] != config[$"Sso:{name}"])
                throw new SetupFailure("Auth/Sso divergentes. Nenhum valor existente sera substituido.");
        }
        await SaveMissing(values);
        ValidateJwt(Configuration());
        Console.WriteLine("Par JWT preparado sem sobrescrever arquivos existentes.");
    }

    private async Task PrepareDocker(string? selectedConnection = null)
    {
        var connection = selectedConnection is null ? Connection() : SetupSafety.LocalConnection(selectedConnection);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COMPOSE_PROJECT_NAME"))
            && Environment.GetEnvironmentVariable("COMPOSE_PROJECT_NAME") != "backend")
            throw new SetupFailure("COMPOSE_PROJECT_NAME diferente de backend. Revise o override; o setup nao selecionara outro volume.");
        var info = JsonDocument.Parse(await Run("docker", ["compose", "-f", Compose, "config", "--format", "json"]));
        var volume = info.RootElement.GetProperty("volumes").GetProperty("supportatos-db-data").GetProperty("name").GetString();
        if (volume != "backend_supportatos-db-data") throw new SetupFailure("Compose selecionou outro volume. Interrompido para preservar dados.");
        var names = await Run("docker", ["ps", "-a", "--format", "{{.Names}}"]);
        var exists = names.Split('\n', StringSplitOptions.TrimEntries).Contains("supportatos-db");
        if (exists)
        {
            using var inspection = JsonDocument.Parse(await Run("docker", ["inspect", "supportatos-db"]));
            var container = inspection.RootElement[0];
            if (ConnectionConfiguration.ExistingContainer(container))
            {
                Console.WriteLine("Container PostgreSQL existente reutilizado, sem recriacao. Binding antigo pode permanecer ate atualizacao autorizada.");
                return;
            }
            Confirm("INICIAR supportatos-db", "Container existente parado; iniciar sem recriar?");
        }
        // Never recreate existing containers, even when their config differs.
        await Run("docker", ["compose", "-f", Compose, "up", "-d", "--wait", "--wait-timeout", "60", "--no-recreate", "supportatos-db"],
            environment: new() { ["POSTGRES_PASSWORD"] = connection.Password });
        Console.WriteLine("PostgreSQL preparado pelo Compose oficial, sem recriar containers existentes.");
    }

    private SupportAtosDbContext Context(string connection)
        => new(new DbContextOptionsBuilder<SupportAtosDbContext>().UseNpgsql(connection).Options);

    private async Task VerifyDatabase(string connection)
    {
        await using var db = new NpgsqlConnection(connection);
        try { await db.OpenAsync(); }
        catch (PostgresException ex) when (ex.SqlState == "28P01")
        {
            throw new SetupFailure("Senha PostgreSQL recusada. Corrija a conexao nos User Secrets com a senha real do volume; nao recrie o banco nem altere POSTGRES_PASSWORD para tentar recupera-la.");
        }
        catch (PostgresException ex) when (ex.SqlState == "3D000")
        {
            throw new SetupFailure("Banco supportatos ausente no cluster acessado. Confira o volume oficial antes de criar ou alterar bancos.");
        }
        catch (NpgsqlException)
        {
            throw new SetupFailure("Conexao PostgreSQL indisponivel. Confira Docker Desktop, container supportatos-db e porta 5433; nao pare o PostgreSQL Windows.");
        }
        await using var command = new NpgsqlCommand("SELECT current_database(), current_user, current_setting('server_version_num')::int, system_identifier::text FROM pg_control_system()", db);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        if (reader.GetString(0) != "supportatos" || reader.GetString(1) != "admin" || reader.GetInt32(2) / 10000 != 17)
            throw new SetupFailure("Identidade/versao do PostgreSQL inesperada. Nenhuma migration foi executada.");
        var dockerIdentity = (await Run("docker", ["exec", "supportatos-db", "psql", "-U", "admin", "-d", "supportatos", "-At", "-c", "SELECT system_identifier FROM pg_control_system()"])).Trim();
        if (reader.GetString(3) != dockerIdentity)
            throw new SetupFailure("A conexao local nao corresponde ao cluster do container oficial. Nenhuma escrita foi realizada.");
    }

    private async Task DatabaseStatus(bool requireCurrent)
    {
        var connection = Connection().ConnectionString;
        await VerifyDatabase(connection);
        await using var context = Context(connection);
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToArray();
        Console.WriteLine($"Destino verificado: localhost:5433/supportatos (admin, PostgreSQL 17). Migrations pendentes: {pending.Length}.");
        if (requireCurrent && pending.Length != 0) throw new SetupFailure("Schema pendente. Execute setup.ps1 e autorize as migrations antes de iniciar.");
    }

    private async Task Migrate()
    {
        var connection = Connection().ConnectionString;
        await VerifyDatabase(connection);
        await using var context = Context(connection);
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToArray();
        if (pending.Length == 0) { Console.WriteLine("Schema atualizado; nenhuma migration executada."); return; }
        Console.WriteLine($"Migrations pendentes: {pending.Length}. Confira backup em banco existente.");
        Confirm("MIGRAR 127.0.0.1:5433/supportatos", "Aplicar migrations SOMENTE ao destino Docker verificado?");
        // Pin the exact verified connection in the child; no subsequent config drift.
        await Run("dotnet", ["tool", "run", "dotnet-ef", "--", "database", "update", "--configuration", "Release", "--no-build", "--project", Path.Combine(root, "src/backend/src/SupportAtos.Infrastructure"), "--startup-project", Api, "--", "--environment", "Development"],
            environment: new() { ["ConnectionStrings__DefaultConnection"] = connection, ["DOTNET_ENVIRONMENT"] = "Development", ["ASPNETCORE_ENVIRONMENT"] = "Development" });
        Console.WriteLine("Migrations concluidas no destino confirmado.");
    }

    private async Task Bootstrap()
    {
        await DatabaseStatus(true);
        var connection = Connection().ConnectionString;
        await using var context = Context(connection);
        if (await context.Set<SupportAtos.Domain.Entities.AutenticacaoBootstrap>().AnyAsync()
            || await context.Usuarios.AnyAsync(u => u.Papel == SupportAtos.Domain.Enums.PapelUsuario.Admin))
        {
            Console.WriteLine("Admin ou marcador existente: bootstrap nao executado. Use a conta existente; nao remova o marcador.");
            return;
        }
        Console.WriteLine("Primeiro Admin ainda necessario. O bootstrap pedira os dados e sua propria confirmacao.");
        Console.Write("Executar agora? Digite ADMIN; Enter para deixar pendente: ");
        if (Console.ReadLine() != "ADMIN") { Console.WriteLine("Pendente: execute setup.ps1 novamente e escolha ADMIN apos conferir o destino."); return; }
        await Run("dotnet", [Path.Combine(Api, "bin/Release/net10.0/SupportAtos.Api.dll"), "--bootstrap-admin"],
            environment: new() { ["ConnectionStrings__DefaultConnection"] = connection, ["DOTNET_ENVIRONMENT"] = "Development", ["ASPNETCORE_ENVIRONMENT"] = "Development" }, interactive: true);
    }

    private static void Confirm(string phrase, string message)
    {
        if (Console.IsInputRedirected) throw new SetupFailure("Confirmacao exige terminal interativo. Nenhuma alteracao autorizada por entrada redirecionada.");
        Console.WriteLine(message);
        Console.Write($"Digite {phrase} para confirmar (Enter cancela): ");
        if (Console.ReadLine() != phrase) throw new SetupFailure("Operacao cancelada; execute o setup novamente quando desejar continuar.");
    }

    private static string ReadSecret(string prompt)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected) throw new SetupFailure("Senha exige terminal interativo sem redirecionamento.");
        Console.Write(prompt);
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return buffer.ToString(); }
            if (key.Key == ConsoleKey.Backspace && buffer.Length > 0) buffer.Length--;
            else if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
        }
    }

    private async Task<string> Run(string executable, string[] arguments, string? input = null,
        Dictionary<string, string?>? environment = null, bool interactive = false)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = root, UseShellExecute = false,
            RedirectStandardOutput = !interactive, RedirectStandardError = !interactive, RedirectStandardInput = input is not null };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null) foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(start) ?? throw new SetupFailure($"Nao foi possivel iniciar {executable}.");
        var output = interactive ? Task.FromResult("") : process.StandardOutput.ReadToEndAsync();
        var error = interactive ? Task.FromResult("") : process.StandardError.ReadToEndAsync();
        if (input is not null) { await process.StandardInput.WriteAsync(input); process.StandardInput.Close(); }
        await process.WaitForExitAsync();
        var result = await output;
        await error; // Drain without exposing provider diagnostics or secrets.
        if (process.ExitCode != 0) throw new SetupFailure($"Falha em {executable} (codigo {process.ExitCode}). Confira permissao, Docker e configuracao local. Saida omitida para proteger segredos.");
        return result;
    }
}
