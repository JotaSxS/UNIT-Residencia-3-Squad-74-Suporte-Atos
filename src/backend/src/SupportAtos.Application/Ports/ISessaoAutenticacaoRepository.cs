using SupportAtos.Domain.Entities;

namespace SupportAtos.Application.Ports;

/// <summary>
/// Porta para gerenciamento de sessões de autenticação (refresh tokens).
/// </summary>
public interface ISessaoAutenticacaoRepository
{
    /// <summary>Run rotation/revocation in a database transaction serialized per user.</summary>
    Task<T> ExecutarSerializadoAsync<T>(string refreshTokenHash, Func<Task<T>> operacao, CancellationToken cancellationToken = default);

    /// <summary>Busca uma sessão pelo hash do refresh token.</summary>
    Task<SessaoAutenticacao?> GetByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken cancellationToken = default);

    /// <summary>Adiciona uma nova sessão.</summary>
    Task AddAsync(SessaoAutenticacao sessao, CancellationToken cancellationToken = default);

    /// <summary>Busca todas as sessões de um usuário (para revogação em caso de reuso de token).</summary>
    Task<IReadOnlyList<SessaoAutenticacao>> GetByUsuarioIdAsync(int usuarioId, CancellationToken cancellationToken = default);
}
