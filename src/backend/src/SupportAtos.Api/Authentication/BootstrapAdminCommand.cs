using System.Text;
using Microsoft.Extensions.Hosting;
using SupportAtos.Api.Providers;
using SupportAtos.Application;
using SupportAtos.Application.Abstractions;
using SupportAtos.Application.DTOs;
using SupportAtos.Application.Exceptions;
using SupportAtos.Application.Ports;
using SupportAtos.Application.Services;
using SupportAtos.Domain.Exceptions;
using SupportAtos.Infrastructure;
using SupportAtos.Infrastructure.Security;

namespace SupportAtos.Api.Authentication;

public static class BootstrapAdminCommand
{
    public static async Task<int> ExecutarAsync(IConfiguration configuration, IHostEnvironment environment)
    {
        // Não aceita senha via argumentos, ambiente ou pipe: evita histórico e captura de stdout.
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("Bootstrap exige terminal interativo, sem redirecionamento.");
            return 1;
        }

        Npgsql.NpgsqlConnectionStringBuilder destino;
        try
        {
            var conexao = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(conexao)) throw new InvalidOperationException();
            destino = new Npgsql.NpgsqlConnectionStringBuilder(conexao);
            if (string.IsNullOrWhiteSpace(destino.Database)) throw new InvalidOperationException();
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Configure ConnectionStrings:DefaultConnection antes de executar o bootstrap.");
            return 1;
        }

        Console.WriteLine("Bootstrap explícito do primeiro Admin. Não aplica migrations.");
        Console.WriteLine($"Destino: {destino.Host}:{destino.Port}; banco: {destino.Database}.");
        Console.Write("Nome: ");
        var nome = Console.ReadLine() ?? string.Empty;
        Console.Write("E-mail: ");
        var email = Console.ReadLine() ?? string.Empty;
        Console.Write($"Senha ({(environment.IsDevelopment() ? 6 : 15)} a 128 caracteres, sem eco): ");
        var senha = LerSenha();
        Console.Write("Confirme a senha: ");
        if (senha != LerSenha())
        {
            Console.Error.WriteLine("As senhas não coincidem.");
            return 1;
        }
        Console.Write($"Digite INICIALIZAR {destino.Database} para confirmar: ");
        if (Console.ReadLine() != $"INICIALIZAR {destino.Database}") return 1;

        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders()); // Sem logs EF/SQL ou segredos no procedimento.
        services.AddHttpContextAccessor();
        services.AddSingleton(environment);
        services.AddScoped<ICurrentUser, HttpContextCurrentUserProvider>();
        services.AddApplication();
        services.AddScoped<IHashSenha, Argon2idHashSenha>();
        services.AddScoped<IRefreshTokenGerador, RefreshTokenGerador>();
        try
        {
            services.AddInfrastructure(configuration);
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope(); // Não inicia host nem executa seeders.
            await scope.ServiceProvider.GetRequiredService<FuncionarioAutenticacaoService>()
                .BootstrapAsync(new BootstrapAdminInput(nome, email, senha));
            Console.WriteLine("Primeiro Admin criado. Bootstrap encerrado permanentemente.");
            return 0;
        }
        catch (ConflictException)
        {
            Console.Error.WriteLine("Bootstrap indisponível: já executado, administrador ou e-mail existente.");
            return 1;
        }
        catch (Exception ex) when (ex is ValidationException or DomainException)
        {
            Console.Error.WriteLine("Dados inválidos. Confira nome, e-mail e requisitos de senha.");
            return 1;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Bootstrap falhou. Verifique a conexão e as migrations; nenhum segredo foi registrado.");
            return 1;
        }
    }

    private static string LerSenha()
    {
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return buffer.ToString(); }
            if (key.Key == ConsoleKey.Backspace && buffer.Length > 0) buffer.Length--;
            else if (!char.IsControl(key.KeyChar))
            {
                // Retém um caractere extra para a validação rejeitar entrada longa, sem truncá-la para uma senha válida.
                if (buffer.Length < 129) buffer.Append(key.KeyChar);
            }
        }
    }
}
