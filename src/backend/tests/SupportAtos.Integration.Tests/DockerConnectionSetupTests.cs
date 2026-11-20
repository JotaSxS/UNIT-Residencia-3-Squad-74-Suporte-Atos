using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using SupportAtos.Api;
using SupportAtos.DevSetup;
using System.Diagnostics;

namespace SupportAtos.Integration.Tests;

public class DockerConnectionSetupTests
{
    private const string Docker = "Host=127.0.0.1;Port=5433;Database=supportatos;Username=admin;Password=synthetic-only";

    [Theory]
    [InlineData("{\"ConnectionStrings:DefaultConnection\":\"Host=localhost;Port=5432;Database=supportatos;Username=postgres;Password=old-only\",\"Auth:PrivateKeyPem\":\"untouched-key\",\"Other\":42}")]
    [InlineData("{\"ConnectionStrings\":{\"DefaultConnection\":\"Host=localhost;Port=5432;Database=supportatos;Username=postgres;Password=old-only\",\"Other\":\"untouched\"},\"Auth\":{\"PrivateKeyPem\":\"untouched-key\"}}")]
    public void ReplacesOnlyConnectionAndPreservesOtherSecretValues(string original)
    {
        var updated = ConnectionConfiguration.UpdateSecrets(original, Docker);
        Assert.Equal(Docker, ConnectionConfiguration.StoredConnection(updated));
        Assert.Contains("untouched-key", updated);
        Assert.DoesNotContain("old-only", updated);
        Assert.Equal(updated, ConnectionConfiguration.UpdateSecrets(updated, Docker));
        using var before = JsonDocument.Parse(original);
        using var after = JsonDocument.Parse(updated);
        if (before.RootElement.TryGetProperty("Other", out _)) Assert.Equal(42, after.RootElement.GetProperty("Other").GetInt32());
        else Assert.Equal("untouched", after.RootElement.GetProperty("ConnectionStrings").GetProperty("Other").GetString());
    }

    [Fact]
    public void CleanCloneCreatesOnlyTheOfficialConnection()
    {
        var json = ConnectionConfiguration.UpdateSecrets(null, Docker);
        using var document = JsonDocument.Parse(json);
        Assert.Single(document.RootElement.EnumerateObject());
        Assert.Equal(Docker, ConnectionConfiguration.StoredConnection(json));
    }

    [Theory]
    [InlineData("ConnectionStrings__DefaultConnection")]
    [InlineData("ConnectionStrings:DefaultConnection")]
    [InlineData("ASPNETCORE_ConnectionStrings__DefaultConnection")]
    [InlineData("DOTNET_ConnectionStrings__DefaultConnection")]
    [InlineData("CUSTOMCONNSTR_DefaultConnection")]
    [InlineData("SQLCONNSTR_DefaultConnection")]
    [InlineData("SQLAZURECONNSTR_DefaultConnection")]
    [InlineData("MYSQLCONNSTR_DefaultConnection")]
    public void DetectsAllEnvironmentProviderAliases(string key) => Assert.True(ConnectionConfiguration.IsConnectionEnvironmentKey(key));

    [Fact]
    public void DiagnosticNeverReturnsOldCredentials()
    {
        const string old = "Host=localhost;Port=5432;Database=supportatos;Username=postgres;Password=old-only";
        var diagnostic = ConnectionConfiguration.Classify(old);
        Assert.Contains("divergente", diagnostic);
        Assert.DoesNotContain("old-only", diagnostic);
        Assert.DoesNotContain("postgres", diagnostic);
        Assert.False(ConnectionConfiguration.IsDocker(old));
        Assert.Throws<InvalidOperationException>(() => DevelopmentDatabaseConfiguration.ParseDockerConnection(old));
        var error = Assert.Throws<InvalidOperationException>(() => new SupportAtosDesignTimeDbContextFactory().CreateDbContext(
            ["--environment", "Development", "--ConnectionStrings:DefaultConnection", old]));
        Assert.DoesNotContain("old-only", error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExistingOfficialContainerIsRecognizedWithoutAnyDockerCommand(bool running)
    {
        using var json = JsonDocument.Parse(Container("backend_supportatos-db-data", running));
        Assert.Equal(running, ConnectionConfiguration.ExistingContainer(json.RootElement));
    }

    [Fact]
    public void RefusesAnotherVolumeInsteadOfRecreatingIt()
    {
        using var json = JsonDocument.Parse(Container("another-volume", true));
        Assert.Throws<SetupFailure>(() => ConnectionConfiguration.ExistingContainer(json.RootElement));
    }

    [Fact]
    public void ProtectedBackupRoundTripsExactOriginalAndCannotOverwriteExistingFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), "supportatos-protected-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "fixture.dpapi");
        var original = Encoding.UTF8.GetBytes("synthetic-original-connection-and-jwt");
        try
        {
            ProtectedLocalFiles.Write(path, original);
            Assert.Equal(original, ProtectedLocalFiles.Read(path));
            Assert.DoesNotContain("synthetic-original", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            Assert.Throws<IOException>(() =>
            {
                if (OperatingSystem.IsWindows()) ProtectedLocalFiles.Write(path, Encoding.UTF8.GetBytes("replacement"));
            });
            Assert.Equal(original, ProtectedLocalFiles.Read(path));
            var acl = new DirectoryInfo(directory).GetAccessControl();
            Assert.True(acl.AreAccessRulesProtected);
            var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
            Assert.Single(rules);
            Assert.Equal(WindowsIdentity.GetCurrent().User, rules[0].IdentityReference);
            // .NET 10 -> Windows PowerShell 5.1 IPC uses the same CurrentUser DPAPI format.
            var command = "Add-Type -AssemblyName System.Security; $b=[System.Security.Cryptography.ProtectedData]::Unprotect(" +
                "[IO.File]::ReadAllBytes('" + path.Replace("'", "''") + "'),$null,[System.Security.Cryptography.DataProtectionScope]::CurrentUser); " +
                "if([Text.Encoding]::UTF8.GetString($b) -cne 'synthetic-original-connection-and-jwt'){exit 1}; exit 0";
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-EncodedCommand");
            start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
            using var process = Process.Start(start)!;
            Assert.True(process.WaitForExit(15000));
            Assert.Equal(0, process.ExitCode);
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }

    [Fact]
    public async Task NoConsentRefusesLegacyReplacementBeforeDockerOrSecretWrites()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "supportatos-no-consent-" + Guid.NewGuid().ToString("N"));
        var api = Path.GetFullPath(Path.Combine(root, "src/backend/src/SupportAtos.Api"));
        Directory.CreateDirectory(api);
        var settings = Path.Combine(api, "appsettings.json");
        const string original = "{\"ConnectionStrings:DefaultConnection\":\"Host=localhost;Port=5432;Database=supportatos;Username=postgres;Password=old-only\"}";
        await File.WriteAllTextAsync(settings, original);
        var ticket = Path.Combine(ProtectedLocalFiles.Root, "sessions", Guid.NewGuid().ToString("N") + ".dpapi");
        try
        {
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(typeof(SetupSafety).Assembly.Location);
            start.ArgumentList.Add("connection");
            start.ArgumentList.Add(root);
            // No User Secrets file is created. APPDATA isolates both API providers and store lookup.
            start.Environment["APPDATA"] = root;
            start.Environment["ConnectionStrings__DefaultConnection"] = "Host=localhost;Port=5432;Database=supportatos;Username=postgres;Password=old-only";
            start.Environment["SUPPORTATOS_SETUP_SESSION_FILE"] = ticket;
            using var process = Process.Start(start)!;
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await stdout + await stderr;
            Assert.Equal(1, process.ExitCode);
            Assert.Contains("terminal interativo", output);
            Assert.DoesNotContain("old-only", output);
            Assert.False(File.Exists(ticket));
            Assert.Equal(original, await File.ReadAllTextAsync(settings));
            Assert.Equal([settings], Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }
        finally
        {
            File.Delete(settings);
            Directory.Delete(api);
            Directory.Delete(Path.Combine(root, "src/backend/src"));
            Directory.Delete(Path.Combine(root, "src/backend"));
            Directory.Delete(Path.Combine(root, "src"));
            Directory.Delete(root);
        }
    }

    private static string Container(string volume, bool running) => JsonSerializer.Serialize(new
    {
        Config = new { Image = "postgres:17", Labels = new Dictionary<string, string> { ["com.docker.compose.project"] = "backend" } },
        Mounts = new[] { new { Destination = "/var/lib/postgresql/data", Name = volume } },
        State = new { Running = running }
    });
}
