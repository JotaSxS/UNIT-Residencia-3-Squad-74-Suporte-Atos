using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Npgsql;

namespace SupportAtos.DevSetup;

public sealed class SetupFailure(string message) : Exception(message);

public static class SetupSafety
{
    public static bool HasLocalValue(IConfigurationRoot configuration, string key)
    {
        var provider = configuration.Providers.Reverse()
            .FirstOrDefault(p => p.TryGet(key, out var value) && !string.IsNullOrWhiteSpace(value));
        if (provider is null) return false;
        return provider is not JsonConfigurationProvider json
            || Path.GetFileName(json.Source.Path) is not ("appsettings.json" or "appsettings.Development.json");
    }

    public static NpgsqlConnectionStringBuilder LocalConnection(string value)
    {
        try { return SupportAtos.Api.DevelopmentDatabaseConfiguration.ParseDockerConnection(value); }
        catch (InvalidOperationException ex) { throw new SetupFailure(ex.Message); }
    }

    public static void MatchingKeys(string privatePem, string publicPem)
    {
        try
        {
            using var privateKey = RSA.Create();
            using var publicKey = RSA.Create();
            privateKey.ImportFromPem(privatePem);
            publicKey.ImportFromPem(publicPem);
            if (privateKey.KeySize < 2048 || !privateKey.ExportSubjectPublicKeyInfo().SequenceEqual(publicKey.ExportSubjectPublicKeyInfo()))
                throw new CryptographicException();
            privateKey.ExportPkcs8PrivateKey();
        }
        catch { throw new SetupFailure("Par JWT ausente, invalido ou divergente. Restaure o par correto; o setup nao rotaciona chaves existentes."); }
    }
}
