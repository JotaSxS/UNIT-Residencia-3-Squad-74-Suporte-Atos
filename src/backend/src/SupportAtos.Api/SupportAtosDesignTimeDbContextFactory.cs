using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SupportAtos.Infrastructure.Persistence;

namespace SupportAtos.Api;

/// <summary>
/// Uses the API's default configuration providers without starting its host,
/// authentication services, seeders or database connection.
/// Precedence (highest first): command line, environment, Development user secrets,
/// appsettings.{Environment}.json, appsettings.json.
/// EF does not use launchSettings.json; pass -- --environment Development for secrets.
/// </summary>
public sealed class SupportAtosDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SupportAtosDbContext>
{
    public SupportAtosDbContext CreateDbContext(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(SupportAtosDesignTimeDbContextFactory).Assembly.GetName().Name,
            ContentRootPath = FindApiDirectory()
        });

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "A connection string 'DefaultConnection' não está configurada. " +
                "Use User Secrets da API com -- --environment Development ou " +
                "ConnectionStrings__DefaultConnection no ambiente. Nenhuma conexão foi aberta.");
        }

        if (builder.Environment.IsDevelopment())
            DevelopmentDatabaseConfiguration.ParseDockerConnection(connectionString);

        var options = new DbContextOptionsBuilder<SupportAtosDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new SupportAtosDbContext(options);
    }

    private static string FindApiDirectory()
    {
        // Support invocation from the repository, backend and project directories,
        // independently of the shell's working directory.
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                foreach (var relative in new[] { ".", "src/SupportAtos.Api", "src/backend/src/SupportAtos.Api" })
                {
                    var candidate = Path.GetFullPath(Path.Combine(directory.FullName, relative));
                    if (File.Exists(Path.Combine(candidate, "SupportAtos.Api.csproj")))
                        return candidate;
                }
            }
        }

        throw new InvalidOperationException(
            "Diretório da API não encontrado. Execute o EF a partir do checkout do SupportAtos.");
    }
}
