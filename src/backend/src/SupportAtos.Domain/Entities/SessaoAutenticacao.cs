using SupportAtos.Domain.Exceptions;

namespace SupportAtos.Domain.Entities;

public partial class SessaoAutenticacao
{
    public int Id { get; private set; }

    public int UsuarioId { get; private set; }

    public Usuario? Usuario { get; private set; }

    public string RefreshTokenHash { get; private set; } = string.Empty;

    public DateTime RefreshTokenExpiracao { get; private set; }

    public DateTime CriadoEm { get; private set; } = DateTime.UtcNow;

    public DateTime? RevogadoEm { get; private set; }

    public string? SubstituidoPorRefreshTokenHash { get; private set; }

    public string? EnderecoIp { get; private set; }

    public string? UserAgent { get; private set; }

    protected SessaoAutenticacao()
    {
    }

    private SessaoAutenticacao(
        int usuarioId,
        string refreshTokenHash,
        DateTime refreshTokenExpiracao,
        string? enderecoIp = null,
        string? userAgent = null)
    {
        if (usuarioId <= 0)
        {
            throw new Exceptions.DomainException("O usuário é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(refreshTokenHash))
        {
            throw new Exceptions.DomainException("O hash do refresh token é obrigatório.");
        }

        if (refreshTokenExpiracao <= DateTime.UtcNow)
        {
            throw new Exceptions.DomainException("A expiração do refresh token deve ser futura.");
        }

        UsuarioId = usuarioId;
        RefreshTokenHash = refreshTokenHash;
        RefreshTokenExpiracao = refreshTokenExpiracao;
        EnderecoIp = enderecoIp;
        UserAgent = userAgent;
    }

    public static SessaoAutenticacao Criar(
        int usuarioId,
        string refreshTokenHash,
        DateTime refreshTokenExpiracao,
        string? enderecoIp = null,
        string? userAgent = null)
        => new(usuarioId, refreshTokenHash, refreshTokenExpiracao, enderecoIp, userAgent);

    public void Revogar()
    {
        RevogadoEm = DateTime.UtcNow;
    }

    public void MarcarComoSubstituido(string novoRefreshTokenHash)
    {
        if (string.IsNullOrWhiteSpace(novoRefreshTokenHash))
        {
            throw new Exceptions.DomainException("O hash do novo refresh token é obrigatório.");
        }

        SubstituidoPorRefreshTokenHash = novoRefreshTokenHash;
    }

    public bool EstaExpirado()
        => RefreshTokenExpiracao <= DateTime.UtcNow;

    public bool EstaRevogado()
        => RevogadoEm.HasValue;
}
