using Microsoft.EntityFrameworkCore;
using SupportAtos.Api;

namespace SupportAtos.Integration.Tests;

// No web host, fixture, database or machine configuration is modified by these tests.
public class DesignTimeConfigurationTests
{
    [Fact]
    public void CommandLineConnectionOverridesOtherProvidersWithoutOpeningDatabase()
    {
        const string connection = "Host=127.0.0.1;Port=5433;Database=supportatos;Username=admin;Password=synthetic";
        using var context = new SupportAtosDesignTimeDbContextFactory().CreateDbContext(
            ["--environment", "Development", "--ConnectionStrings:DefaultConnection", connection]);

        Assert.Equal(connection, context.Database.GetConnectionString());
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.Contains("20261119205912_AdicionarBootstrapEAtivacaoFuncionarios", context.Database.GetMigrations());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void ExplicitEmptyConnectionFailsWithoutFallingBackToMachineConfiguration(string environment)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new SupportAtosDesignTimeDbContextFactory().CreateDbContext(
                ["--environment", environment, "--ConnectionStrings:DefaultConnection", ""]));

        Assert.Contains("DefaultConnection", exception.Message);
        Assert.Contains("Nenhuma conexão foi aberta", exception.Message);
    }

    [Theory]
    [InlineData(5433)]
    [InlineData(5434)]
    public void DevelopmentPolicyAcceptsDockerDestinationOnConfiguredPort(int port)
    {
        var connection = $"Host=127.0.0.1;Port={port};Database=supportatos;Username=admin;Password=synthetic";

        var parsed = DevelopmentDatabaseConfiguration.ParseDockerConnection(connection, port);

        Assert.Equal(port, parsed.Port);
        Assert.Equal("supportatos", parsed.Database);
    }

    [Fact]
    public void DevelopmentPolicyRejectsPortDifferentFromConfiguredPort()
    {
        const string connection = "Host=127.0.0.1;Port=5433;Database=supportatos;Username=admin;Password=synthetic";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DevelopmentDatabaseConfiguration.ParseDockerConnection(connection, 5434));

        Assert.Contains("127.0.0.1:5434", exception.Message);
        Assert.DoesNotContain("synthetic", exception.Message);
    }
}
