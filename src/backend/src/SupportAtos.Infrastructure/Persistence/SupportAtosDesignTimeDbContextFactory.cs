using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SupportAtos.Infrastructure.Persistence;

/// <summary>
/// Factory de design-time para o <see cref="SupportAtosDbContext"/>.
/// Usada exclusivamente por ferramentas do Entity Framework Core
/// (<c>dotnet ef migrations</c>, <c>dotnet ef database update</c>).
/// </summary>
/// <remarks>
/// CONFIGURAÇÃO EXIGIDA:
/// Esta factory não possui fallback silencioso. A connection string deve ser
/// fornecida obrigatoriamente por uma das seguintes formas:
///
/// 1. Variável de ambiente: <c>ConnectionStrings__DefaultConnection</c>
///    Exemplo (PowerShell):
///      $env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=supportatos;Username=postgres;Password=sua_senha"
///
/// This factory reads only the environment variable, never API user-secrets.
///
/// Não armazene senhas em arquivo de configuração versionado (appsettings.json).
/// A connection string deve apontar para uma instância PostgreSQL existente;
/// esta factory não cria bancos de dados nem aplica migrations automaticamente.
/// </remarks>
public class SupportAtosDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SupportAtosDbContext>
{
    public SupportAtosDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "A connection string 'ConnectionStrings__DefaultConnection' não está definida. " +
                "Defina ConnectionStrings__DefaultConnection; esta factory nao le user-secrets.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<SupportAtosDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new SupportAtosDbContext(optionsBuilder.Options);
    }
}
