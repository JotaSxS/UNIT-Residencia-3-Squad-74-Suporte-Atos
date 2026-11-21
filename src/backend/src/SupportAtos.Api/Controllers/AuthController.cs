using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using SupportAtos.Api.Authentication;
using SupportAtos.Application.DTOs;
using SupportAtos.Application.Options;
using SupportAtos.Application.Ports;
using SupportAtos.Application.Services;

namespace SupportAtos.Api.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly AuthSettings _authSettings;

    public AuthController(
        IAuthService authService,
        IOptions<AuthSettings> authSettings)
    {
        _authService = authService;
        _authSettings = authSettings.Value;
    }

    /// <summary>Define a primeira senha usando convite de ativação de uso único.</summary>
    [HttpPost("ativar")]
    public async Task<IActionResult> Ativar(
        [FromBody] AtivarFuncionarioInput input,
        [FromServices] FuncionarioAutenticacaoService funcionarios,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        await funcionarios.AtivarAsync(input, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Autentica um funcionário interno (Admin, Supervisor ou Agente) com e-mail e senha.
    /// O refresh token é enviado exclusivamente por cookie HttpOnly.
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginInput input,
        CancellationToken cancellationToken = default)
    {
        var enderecoIp = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
        var userAgent = Request.Headers.UserAgent.ToString();

        var resultado = await _authService.LoginAsync(
            input,
            enderecoIp,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            cancellationToken);

        // Define o refresh token exclusivamente como cookie HttpOnly.
        // Não é retornado no corpo da resposta JSON.
        DefinirCookieRefreshToken(resultado.RefreshToken!);

        var resultadoSemRefresh = new LoginResult(
            resultado.Token,
            resultado.ExpiraEm,
            resultado.UsuarioId,
            resultado.Nome,
            resultado.Email,
            resultado.Papel,
            resultado.GrupoEmpresaId);

        return Ok(resultadoSemRefresh);
    }

    /// <summary>
    /// Renova o acesso usando o refresh token armazenado no cookie HttpOnly.
    /// Implementa rotação de token: o refresh token usado é revogado e um novo é emitido.
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(RefreshTokenResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        CancellationToken cancellationToken = default)
    {
        var refreshToken = ObterRefreshTokenDoCookie();

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(new { Mensagem = "Token de renovação não fornecido." });
        }

        var enderecoIp = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
        var userAgent = Request.Headers.UserAgent.ToString();

        var resultado = await _authService.RefreshAsync(
            refreshToken,
            enderecoIp,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            cancellationToken);

        // Rotaciona o cookie: define o novo refresh token.
        DefinirCookieRefreshToken(resultado.RefreshToken!);

        var resultadoSemRefresh = new RefreshTokenResult(
            resultado.Token,
            resultado.ExpiraEm,
            resultado.UsuarioId,
            resultado.Nome,
            resultado.Email,
            resultado.Papel,
            resultado.GrupoEmpresaId);

        return Ok(resultadoSemRefresh);
    }

    /// <summary>
    /// Encerra a sessão revogando o refresh token e removendo o cookie.
    /// </summary>
    [HttpPost("logout")]
    [ProducesResponseType(typeof(LogoutResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(
        CancellationToken cancellationToken = default)
    {
        var refreshToken = ObterRefreshTokenDoCookie();

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await _authService.LogoutAsync(refreshToken, cancellationToken);
        }

        RemoveCookieRefreshToken();

        return Ok(new LogoutResult("Sessão encerrada com sucesso."));
    }

    private string? ObterRefreshTokenDoCookie()
    {
        return Request.Cookies[_authSettings.RefreshTokenCookieName];
    }

    private void DefinirCookieRefreshToken(string refreshToken)
    {
        var opcoes = new CookieOptions
        {
            HttpOnly = true,
            Secure = _authSettings.RefreshTokenCookieSecure,
            SameSite = ParseSameSite(_authSettings.RefreshTokenCookieSameSite),
            Path = _authSettings.RefreshTokenCookiePath,
            Expires = DateTimeOffset.UtcNow.AddDays(_authSettings.RefreshTokenExpiracaoDias),
        };

        Response.Cookies.Append(_authSettings.RefreshTokenCookieName, refreshToken, opcoes);
    }

    private void RemoveCookieRefreshToken()
    {
        var opcoes = new CookieOptions
        {
            HttpOnly = true,
            Secure = _authSettings.RefreshTokenCookieSecure,
            SameSite = ParseSameSite(_authSettings.RefreshTokenCookieSameSite),
            Path = _authSettings.RefreshTokenCookiePath,
            Expires = DateTimeOffset.UtcNow.AddDays(-1),
        };

        Response.Cookies.Append(_authSettings.RefreshTokenCookieName, string.Empty, opcoes);
    }

    private static Microsoft.AspNetCore.Http.SameSiteMode ParseSameSite(string sameSite)
    {
        return sameSite switch
        {
            "Lax" => Microsoft.AspNetCore.Http.SameSiteMode.Lax,
            "None" => Microsoft.AspNetCore.Http.SameSiteMode.None,
            _ => Microsoft.AspNetCore.Http.SameSiteMode.Strict,
        };
    }
}
