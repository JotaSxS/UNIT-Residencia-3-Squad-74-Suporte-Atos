namespace SupportAtos.Application.Options;

/// <summary>
/// Configurações de autenticação interna (login próprio com e-mail e senha)
/// para funcionários da equipe (Admin, Supervisor, Agente).
/// Nenhum segredo deve vir do appsettings: configure via user-secrets (dev)
/// ou variáveis de ambiente (produção).
/// </summary>
public class AuthSettings
{
    public const string SectionName = "Auth";

    /// <summary>Emissor do JWT interno (deve coincidir com Sso:Issuer).</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Público do JWT interno (deve coincidir com Sso:Audience).</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Chave privada PEM (RSA) para assinar JWTs internos.</summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>Tempo de expiração do JWT interno em minutos.</summary>
    public int ExpiracaoMinutos { get; set; } = 15;

    /// <summary>Número máximo de tentativas falhas antes de bloquear.</summary>
    public int MaxTentativasFalhas { get; set; } = 5;

    /// <summary>Tempo de bloqueio em minutos após exceder tentativas falhas.</summary>
    public int BloqueioMinutos { get; set; } = 30;

    /// <summary>Tempo de expiração do refresh token em dias.</summary>
    public int RefreshTokenExpiracaoDias { get; set; } = 7;

    /// <summary>Nome do cookie do refresh token.</summary>
    public string RefreshTokenCookieName { get; set; } = "refresh_token";

    /// <summary>Caminho do cookie do refresh token.</summary>
    public string RefreshTokenCookiePath { get; set; } = "/";

    /// <summary>Se o cookie deve ser marcado como Secure (apenas HTTPS).</summary>
    public bool RefreshTokenCookieSecure { get; set; } = true;

    /// <summary>Modo SameSite do cookie do refresh token.</summary>
    /// <remarks>
    /// SameSite compares sites, not origins. Lax/Strict work for same-site deployments;
    /// cross-site frontend/API deployments require None with Secure and explicit CORS origins.
    /// SameSite never replaces CSRF validation.
    /// </remarks>
    public string RefreshTokenCookieSameSite { get; set; } = "Lax";
}
