using SupportAtos.Domain.Exceptions;

namespace SupportAtos.Domain.Entities;

public partial class UsuarioCredencial
{
    public int Id { get; private set; }

    public int UsuarioId { get; private set; }

    public Usuario? Usuario { get; private set; }

    public string SenhaHash { get; private set; } = string.Empty;

    public DateTime DataCriacao { get; private set; } = DateTime.UtcNow;

    public DateTime? DataUltimaAlteracao { get; private set; }

    public bool Ativo { get; private set; } = true;

    public int TentativasFalhas { get; private set; }

    public DateTime? BloqueadoAte { get; private set; }

    public string? TokenAtivacaoHash { get; private set; }
    public DateTime? TokenAtivacaoExpiraEm { get; private set; }
    public DateTime? AtivacaoConsumidaEm { get; private set; }

    protected UsuarioCredencial()
    {
    }

    private UsuarioCredencial(int usuarioId, string senhaHash)
    {
        if (usuarioId <= 0)
        {
            throw new Exceptions.DomainException("O usuário é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(senhaHash))
        {
            throw new Exceptions.DomainException("O hash de senha é obrigatório.");
        }

        UsuarioId = usuarioId;
        SenhaHash = senhaHash;
    }

    public static UsuarioCredencial Criar(int usuarioId, string senhaHash)
        => new(usuarioId, senhaHash);

    public static UsuarioCredencial CriarPendente(int usuarioId, string tokenHash, DateTime expiraEm)
    {
        if (usuarioId <= 0 || tokenHash.Length != 64 || expiraEm <= DateTime.UtcNow)
            throw new DomainException("Convite de ativação inválido.");

        return new UsuarioCredencial
        {
            UsuarioId = usuarioId,
            Ativo = false,
            SenhaHash = string.Empty,
            TokenAtivacaoHash = tokenHash,
            TokenAtivacaoExpiraEm = expiraEm
        };
    }

    public void AtivarComPrimeiraSenha(string senhaHash)
    {
        if (Ativo || AtivacaoConsumidaEm.HasValue || TokenAtivacaoHash is null
            || !TokenAtivacaoExpiraEm.HasValue || TokenAtivacaoExpiraEm.Value <= DateTime.UtcNow)
            throw new DomainException("Convite inválido ou expirado.");

        AlterarSenha(senhaHash);
        AtivacaoConsumidaEm = DateTime.UtcNow;
        Ativo = true;
    }

    public void AlterarSenha(string novoSenhaHash)
    {
        if (string.IsNullOrWhiteSpace(novoSenhaHash))
        {
            throw new Exceptions.DomainException("O novo hash de senha é obrigatório.");
        }

        SenhaHash = novoSenhaHash;
        DataUltimaAlteracao = DateTime.UtcNow;
        TentativasFalhas = 0;
        BloqueadoAte = null;
    }

    public void RegistrarFalha()
    {
        TentativasFalhas++;
    }

    public void LimparFalhas()
    {
        TentativasFalhas = 0;
        BloqueadoAte = null;
    }

    public void Bloquear(DateTime? bloqueadoAte)
    {
        BloqueadoAte = bloqueadoAte;
    }

    public void Desativar()
    {
        Ativo = false;
    }

    public void Reativar()
    {
        if (string.IsNullOrWhiteSpace(SenhaHash))
            throw new DomainException("Defina a primeira senha pelo convite antes de reativar a credencial.");
        Ativo = true;
    }

    public bool EstaBloqueado()
        => BloqueadoAte.HasValue && BloqueadoAte.Value > DateTime.UtcNow;
}
