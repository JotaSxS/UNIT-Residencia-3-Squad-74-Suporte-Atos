using Npgsql;

namespace SupportAtos.Api;

/// <summary>Development database policy, shared by host, EF factory and setup.</summary>
public static class DevelopmentDatabaseConfiguration
{
    public static NpgsqlConnectionStringBuilder ParseDockerConnection(string? value)
    {
        NpgsqlConnectionStringBuilder connection;
        try { connection = new(value ?? ""); }
        catch { throw new InvalidOperationException("Conexao de desenvolvimento invalida. Execute setup.ps1; nenhum destino foi acessado."); }
        if (connection.Host is not ("localhost" or "127.0.0.1") || connection.Port != 5433
            || connection.Database != "supportatos" || connection.Username != "admin"
            || string.IsNullOrWhiteSpace(connection.Password) || connection.Password == "__SUA_SENHA__"
            || !string.IsNullOrEmpty(connection.Options) || !string.IsNullOrEmpty(connection.SearchPath))
            throw new InvalidOperationException("Development exige PostgreSQL Docker em 127.0.0.1:5433/supportatos (admin). Execute setup.ps1 para substituir configuracoes antigas com consentimento e backup; nenhum destino foi acessado.");
        return connection;
    }
}
