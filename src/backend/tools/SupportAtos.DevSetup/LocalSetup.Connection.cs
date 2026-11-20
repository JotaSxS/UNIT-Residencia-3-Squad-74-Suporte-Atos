using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Security.AccessControl;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;
using Npgsql;

namespace SupportAtos.DevSetup;

internal sealed partial class LocalSetup
{
    private string SecretsPath
    {
        get
        {
            var id = typeof(SupportAtos.Api.SupportAtosDesignTimeDbContextFactory).Assembly
                .GetCustomAttribute<UserSecretsIdAttribute>()!.UserSecretsId;
            return Path.Combine(Environment.GetEnvironmentVariable("APPDATA")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", id, "secrets.json");
        }
    }

    private async Task ConfigureDockerConnection(bool configure)
    {
        var ticket = Environment.GetEnvironmentVariable("SUPPORTATOS_SETUP_SESSION_FILE");
        var sessions = Path.GetFullPath(Path.Combine(ProtectedLocalFiles.Root, "sessions"));
        if (string.IsNullOrWhiteSpace(ticket)
            || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(ticket)), sessions, StringComparison.OrdinalIgnoreCase))
            throw new SetupFailure("Canal privado de sessao ausente. Execute setup.ps1/start.ps1, nao este comando diretamente.");
        var config = Configuration();
        var originalBytes = File.Exists(SecretsPath) ? await File.ReadAllBytesAsync(SecretsPath) : null;
        var original = originalBytes is null ? null : Encoding.UTF8.GetString(originalBytes).TrimStart('\uFEFF');
        var stored = ConnectionConfiguration.StoredConnection(original);
        var effective = config.GetConnectionString("DefaultConnection");
        Console.WriteLine($"User Secrets: {ConnectionConfiguration.Classify(stored)}.");
        Console.WriteLine($"Conexao efetiva: {ConnectionConfiguration.Classify(effective)}.");
        var environments = new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine }
            .ToDictionary(target => target.ToString(), ConnectionConfiguration.EnvironmentValues);
        var sources = config.Providers.Select(provider => new
        {
            Source = provider.GetType().Name,
            Value = provider.TryGet(ConnectionConfiguration.Key, out var value) ? value : null
        }).Where(source => source.Value is not null).ToArray();
        foreach (var scope in environments)
            foreach (var pair in scope.Value)
                Console.WriteLine($"Ambiente {scope.Key} / {pair.Key}: {ConnectionConfiguration.Classify(pair.Value)}.");

        var selected = ConnectionConfiguration.IsDocker(stored) ? stored
            : ConnectionConfiguration.IsDocker(effective) ? effective : null;
        if (!configure && selected is null) throw new SetupFailure("Docker ainda nao configurado. Execute setup.ps1 e confirme a troca; nenhuma conexao Windows sera utilizada.");

        var replacingSecrets = configure && (selected is null || stored != selected);
        var conflictingSession = environments["Process"].Any(pair => pair.Value != selected);
        var persistentLegacy = environments.Where(scope => scope.Key != "Process").SelectMany(scope => scope.Value)
            .Any(pair => !ConnectionConfiguration.IsDocker(pair.Value));
        if (replacingSecrets || conflictingSession || persistentLegacy)
        {
            Console.WriteLine("Destino exclusivo: 127.0.0.1:5433/supportatos (admin). PostgreSQL Windows nao sera acessado ou modificado.");
            Console.WriteLine("Backup DPAPI sera criado antes da substituicao. Variaveis permanentes nao serao alteradas; overrides dos filhos serao temporarios.");
            Confirm(configure ? "CONFIGURAR DOCKER supportatos" : "USAR DOCKER NESTA SESSAO",
                configure ? "Autoriza salvar a conexao Docker nos User Secrets e substituir overrides de conexao nesta sessao?"
                    : "Autoriza usar os User Secrets Docker nos processos desta sessao, em lugar dos overrides antigos?");
        }

        if (selected is null)
        {
            Console.WriteLine("Container/volume existente: informe a senha real do usuario admin. Volume novo: escolha uma senha exclusiva.");
            var password = ReadSecret("Senha Docker (sem eco): ");
            if (password.Length == 0 || password != ReadSecret("Confirme a senha: "))
                throw new SetupFailure("Senhas vazias ou diferentes. Nenhum segredo foi substituido.");
            selected = new NpgsqlConnectionStringBuilder
            {
                Host = "127.0.0.1", Port = 5433, Database = "supportatos", Username = "admin", Password = password
            }.ConnectionString;
        }
        // Canonical destination, preserving the password and explicit safe connection options.
        var canonical = SetupSafety.LocalConnection(selected);
        canonical.Host = "127.0.0.1";
        selected = canonical.ConnectionString;
        if (configure) await PrepareDocker(selected);
        await VerifyDatabase(selected); // Read-only; never tries any legacy destination.

        if (replacingSecrets || conflictingSession || persistentLegacy)
        {
            var backup = Path.Combine(ProtectedLocalFiles.Root, "backups", Guid.NewGuid().ToString("N") + ".dpapi");
            ProtectedLocalFiles.Write(backup, JsonSerializer.SerializeToUtf8Bytes(new
            {
                Version = 1, CreatedUtc = DateTimeOffset.UtcNow, SecretsPath,
                OriginalSecretsBase64 = originalBytes is null ? null : Convert.ToBase64String(originalBytes),
                Environments = environments, Sources = sources
            }));
            Console.WriteLine($"Backup local criptografado (DPAPI/usuario atual): {backup}");
        }
        if (replacingSecrets)
        {
            var current = File.Exists(SecretsPath) ? await File.ReadAllBytesAsync(SecretsPath) : null;
            if ((current is null) != (originalBytes is null) || (current is not null && !current.SequenceEqual(originalBytes!)))
                throw new SetupFailure("User Secrets mudou durante a preparacao. Backup preservado; execute novamente sem setups concorrentes.");
            var updated = ConnectionConfiguration.UpdateSecrets(original, selected);
            Directory.CreateDirectory(Path.GetDirectoryName(SecretsPath)!);
            var temporary = SecretsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, updated, new UTF8Encoding(false));
                if (File.Exists(SecretsPath))
                    new FileInfo(temporary).SetAccessControl(new FileInfo(SecretsPath).GetAccessControl());
                File.Move(temporary, SecretsPath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Console.WriteLine("Somente ConnectionStrings:DefaultConnection atualizado; demais User Secrets preservados.");
        }

        // Protected one-use IPC: the parent receives no password through stdout/arguments.
        ProtectedLocalFiles.Write(ticket, Encoding.UTF8.GetBytes(selected));
        Console.WriteLine("Conexao Docker verificada e preparada para todos os processos filhos.");
    }
}
