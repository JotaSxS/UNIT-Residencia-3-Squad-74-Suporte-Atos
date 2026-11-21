using System.Threading.Tasks;
using SupportAtos.Application.DTOs;

namespace SupportAtos.Application.Ports;

/// <summary>
/// Porta para autenticação própria de funcionários internos (e-mail + senha).
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Autentica um funcionário interno com e-mail e senha.
    /// Lança <see cref="SupportAtos.Application.Exceptions.ValidationException"/>
    /// com mensagem genérica em caso de falha (não revela se o e-mail existe).
    /// </summary>
    Task<LoginResult> LoginAsync(
        LoginInput input,
        string enderecoIp,
        string? userAgent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renova o acesso usando um refresh token. Implementa rotação: o refresh token
    /// usado é revogado e um novo é emitido.
    /// Lança <see cref="SupportAtos.Application.Exceptions.ValidationException"/>
    /// se o refresh token for inválido, expirado, revogado ou se a conta não estiver ativa.
    /// </summary>
    Task<RefreshTokenResult> RefreshAsync(
        string refreshToken,
        string enderecoIp,
        string? userAgent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoga a sessão de autenticação associada ao refresh token.
    /// </summary>
    Task LogoutAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);
}
