using System.Globalization;
using Npgsql;

namespace SupportAtos.Api;

/// <summary>Development database policy, shared by host, EF factory and setup.</summary>
public static class DevelopmentDatabaseConfiguration
{
    public const int DefaultPort = 5433;

    /// <summary>
    /// Porta HOST local, definida por SUPPORTATOS_DB_PORT (scripts carregam
    /// .supportatos.local.env). Sem a variavel, usa o padrao documentado 5433.
    /// </summary>
    public static int ConfiguredPort
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("SUPPORTATOS_DB_PORT");
            if (string.IsNullOrWhiteSpace(raw)) return DefaultPort;
            if (!int.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
                || port < 1 || port > 65535)
                throw new InvalidOperationException(
                    "SUPPORTATOS_DB_PORT invalida. Use um numero entre 1 e 65535 em .supportatos.local.env.");
            return port;
        }
    }

    public static NpgsqlConnectionStringBuilder ParseDockerConnection(string? value)
        => ParseDockerConnection(value, ConfiguredPort);

    public static NpgsqlConnectionStringBuilder ParseDockerConnection(string? value, int expectedPort)
    {
        if (expectedPort < 1 || expectedPort > 65535)
            throw new InvalidOperationException("Porta de desenvolvimento invalida. Use um numero entre 1 e 65535.");
        NpgsqlConnectionStringBuilder connection;
        try { connection = new(value ?? ""); }
        catch { throw new InvalidOperationException("Conexao de desenvolvimento invalida. Execute setup.ps1; nenhum destino foi acessado."); }
        if (connection.Host is not ("localhost" or "127.0.0.1") || connection.Port != expectedPort
            || connection.Database != "supportatos" || connection.Username != "admin"
            || string.IsNullOrWhiteSpace(connection.Password) || connection.Password == "__SUA_SENHA__"
            || !string.IsNullOrEmpty(connection.Options) || !string.IsNullOrEmpty(connection.SearchPath))
            throw new InvalidOperationException(
                $"Development exige PostgreSQL Docker em 127.0.0.1:{expectedPort}/supportatos (admin). " +
                "Execute setup.ps1 para substituir configuracoes antigas com consentimento e backup; nenhum destino foi acessado.");
        return connection;
    }
}
