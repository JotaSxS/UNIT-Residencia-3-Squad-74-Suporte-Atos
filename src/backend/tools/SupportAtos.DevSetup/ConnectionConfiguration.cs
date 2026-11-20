using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace SupportAtos.DevSetup;

public static class ConnectionConfiguration
{
    public const string Key = "ConnectionStrings:DefaultConnection";
    public const string EnvironmentKey = "ConnectionStrings__DefaultConnection";

    public static bool IsConnectionEnvironmentKey(string name)
    {
        foreach (var prefix in new[] { "ASPNETCORE_", "DOTNET_" })
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { name = name[prefix.Length..]; break; }
        return name.Replace("__", ":", StringComparison.Ordinal).Equals(Key, StringComparison.OrdinalIgnoreCase)
        || new[] { "CUSTOMCONNSTR_", "SQLCONNSTR_", "SQLAZURECONNSTR_", "MYSQLCONNSTR_" }
            .Any(prefix => name.Equals(prefix + "DefaultConnection", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsDocker(string? value)
    {
        try { SetupSafety.LocalConnection(value ?? ""); return true; }
        catch (SetupFailure) { return false; }
    }

    // Never returns a password, arbitrary host, or user-controlled connection text.
    public static string Classify(string? value)
        => string.IsNullOrWhiteSpace(value) ? "ausente"
            : value.Contains("__SUA_SENHA__", StringComparison.Ordinal) ? "placeholder; configurar"
            : IsDocker(value) ? "Docker oficial" : "legada/divergente; substituicao exige consentimento";

    public static Dictionary<string, string?> EnvironmentValues(EnvironmentVariableTarget target)
        => Environment.GetEnvironmentVariables(target).Cast<System.Collections.DictionaryEntry>()
            .Where(entry => IsConnectionEnvironmentKey((string)entry.Key))
            .ToDictionary(entry => (string)entry.Key, entry => entry.Value?.ToString(), StringComparer.OrdinalIgnoreCase);

    public static string? StoredConnection(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(raw));
        return new ConfigurationBuilder().AddJsonStream(stream).Build()[Key];
    }

    public static string UpdateSecrets(string? raw, string connection)
    {
        SetupSafety.LocalConnection(connection);
        var json = string.IsNullOrWhiteSpace(raw) ? new JsonObject() : JsonNode.Parse(raw, documentOptions:
            new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
            ?? throw new SetupFailure("User Secrets invalido. Corrija o formato sem publicar seu conteudo.");
        var flat = json.Select(p => p.Key).FirstOrDefault(k => k.Equals(Key, StringComparison.OrdinalIgnoreCase));
        var section = json.Select(p => p.Key).FirstOrDefault(k => k.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase));
        if (flat is not null) json[flat] = connection;
        else if (section is not null && json[section] is JsonObject connections)
        {
            var key = connections.Select(p => p.Key).FirstOrDefault(k => k.Equals("DefaultConnection", StringComparison.OrdinalIgnoreCase)) ?? "DefaultConnection";
            connections[key] = connection;
        }
        else json[Key] = connection;
        // Reject duplicate/ambiguous paths before writing any machine configuration.
        if (StoredConnection(json.ToJsonString()) != connection) throw new SetupFailure("User Secrets ambiguo; nenhuma substituicao realizada.");
        return json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static bool ExistingContainer(JsonElement container)
    {
        if (container.GetProperty("Config").GetProperty("Image").GetString() != "postgres:17"
            || !container.GetProperty("Mounts").EnumerateArray().Any(m =>
                m.GetProperty("Destination").GetString() == "/var/lib/postgresql/data"
                && m.TryGetProperty("Name", out var name) && name.GetString() == "backend_supportatos-db-data")
            || !container.GetProperty("Config").GetProperty("Labels").TryGetProperty("com.docker.compose.project", out var project)
            || project.GetString() != "backend")
            throw new SetupFailure("Container existente nao corresponde ao Compose/volume oficial. Nenhum container foi modificado.");
        return container.GetProperty("State").GetProperty("Running").GetBoolean();
    }
}
