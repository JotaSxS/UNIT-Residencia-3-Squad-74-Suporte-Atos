using System.Security.Cryptography;
using System.Diagnostics;
using Npgsql;
using Microsoft.Extensions.Configuration;
using SupportAtos.DevSetup;

namespace SupportAtos.Integration.Tests;

// Pure safety checks: no processes, database, Docker, secrets or certificate store.
public class DevelopmentSetupSafetyTests
{
    [Theory]
    [InlineData("inspect")]
    [InlineData("prepare")]
    public async Task ExistingSyntheticConfigurationIsReusableWithoutWritingOrDisclosingSecrets(string action)
    {
        var root = Path.Combine(Path.GetTempPath(), "supportatos-setup-test-" + Guid.NewGuid().ToString("N"));
        var api = Path.GetFullPath(Path.Combine(root, "src/backend/src/SupportAtos.Api"));
        Directory.CreateDirectory(api);
        var settings = Path.Combine(api, "appsettings.json");
        await File.WriteAllTextAsync(settings, "{}");
        using var key = RSA.Create(2048);
        var privatePem = key.ExportPkcs8PrivateKeyPem();
        try
        {
            // Two invocations demonstrate the no-write path is repeatable.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var start = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = root, UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                start.ArgumentList.Add(typeof(SetupSafety).Assembly.Location);
                start.ArgumentList.Add(action);
                start.ArgumentList.Add(root);
                start.Environment["ConnectionStrings__DefaultConnection"] = "Host=127.0.0.1;Port=5433;Database=supportatos;Username=admin;Password=synthetic-not-a-real-password";
                start.Environment["Auth__PrivateKeyPem"] = privatePem;
                start.Environment["Sso__PublicKeyPem"] = key.ExportSubjectPublicKeyInfoPem();
                start.Environment["Auth__Issuer"] = start.Environment["Sso__Issuer"] = "synthetic";
                start.Environment["Auth__Audience"] = start.Environment["Sso__Audience"] = "synthetic";
                start.Environment["Sso__JwksUrl"] = "";
                using var process = Process.Start(start)!;
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                var output = await stdout + await stderr;
                Assert.Equal(0, process.ExitCode);
                Assert.DoesNotContain("synthetic-not-a-real-password", output);
                Assert.DoesNotContain("BEGIN", output);
                Assert.Equal([settings], Directory.GetFiles(root, "*", SearchOption.AllDirectories));
            }
        }
        finally
        {
            // Exact fixture paths only; no recursive deletion or machine secrets touched.
            File.Delete(settings);
            Directory.Delete(api);
            Directory.Delete(Path.Combine(root, "src/backend/src"));
            Directory.Delete(Path.Combine(root, "src/backend"));
            Directory.Delete(Path.Combine(root, "src"));
            Directory.Delete(root);
        }
    }

    [Fact]
    public void ExistingLocalValueIsProtectedAndMissingValueCanBePrepared()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:PrivateKeyPem"] = "synthetic-existing-value"
        }).Build();
        Assert.True(SetupSafety.HasLocalValue(config, "Auth:PrivateKeyPem"));
        Assert.False(SetupSafety.HasLocalValue(config, "Auth:Audience"));
    }

    [Theory]
    [InlineData("Host=localhost;Port=5432;Database=supportatos;Username=admin;Password=synthetic")]
    [InlineData("Host=remote.example;Port=5433;Database=supportatos;Username=admin;Password=synthetic")]
    [InlineData("Host=127.0.0.1;Port=5433;Database=other;Username=admin;Password=synthetic")]
    [InlineData("Host=localhost;Port=5433;Database=supportatos;Username=postgres;Password=synthetic")]
    [InlineData("Host=localhost;Port=5433;Database=supportatos;Username=admin;Password=__SUA_SENHA__")]
    [InlineData("Host=localhost;Port=5433;Database=supportatos;Username=admin;Password=synthetic;Search Path=other")]
    [InlineData("Host=localhost;Port=5433;Database=supportatos;Username=admin;Password=synthetic;Options=-c search_path=other")]
    [InlineData("invalid;Password=must-not-appear")]
    public void RefusesOtherDestinationsWithoutDisclosingConnection(string connection)
    {
        var error = Assert.Throws<SetupFailure>(() => SetupSafety.LocalConnection(connection));
        Assert.DoesNotContain("synthetic", error.Message);
        Assert.DoesNotContain("must-not-appear", error.Message);
    }

    [Fact]
    public void SpecialCharactersInPasswordRemainInTheSameLocalDestination()
    {
        var original = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1", Port = 5433, Database = "supportatos", Username = "admin",
            Password = "quotes\"; ' $() spaces and unicode: ç"
        };
        var parsed = SetupSafety.LocalConnection(original.ConnectionString);
        Assert.Equal(original.Password, parsed.Password);
        Assert.Equal("supportatos", parsed.Database);
        Assert.Equal(5433, parsed.Port);
    }

    [Fact]
    public void AcceptsMatchingExistingRsaPair()
    {
        using var key = RSA.Create(2048);
        SetupSafety.MatchingKeys(key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }

    [Fact]
    public void RejectsDifferentExistingKeysWithoutDisclosingPem()
    {
        using var first = RSA.Create(2048);
        using var second = RSA.Create(2048);
        var error = Assert.Throws<SetupFailure>(() => SetupSafety.MatchingKeys(
            first.ExportPkcs8PrivateKeyPem(), second.ExportSubjectPublicKeyInfoPem()));
        Assert.DoesNotContain("BEGIN", error.Message);
    }

    [Fact]
    public void PublicKeyCannotSubstituteForSigningPrivateKey()
    {
        using var key = RSA.Create(2048);
        Assert.Throws<SetupFailure>(() => SetupSafety.MatchingKeys(
            key.ExportSubjectPublicKeyInfoPem(), key.ExportSubjectPublicKeyInfoPem()));
    }
}
