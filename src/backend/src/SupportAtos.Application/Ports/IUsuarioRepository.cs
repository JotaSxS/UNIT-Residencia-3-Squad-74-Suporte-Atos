using SupportAtos.Domain.Entities;

namespace SupportAtos.Application.Ports;

public interface IUsuarioRepository
{
    Task<Usuario?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Usuario?> GetComGrupoEmpresaPorIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Usuario>> ListarPorGrupoEmpresaAsync(int grupoEmpresaId, CancellationToken cancellationToken = default);

    Task<Usuario?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<Usuario?> GetByCodigoExternoAsync(string codigoExterno, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Usuario>> ListarFuncionariosAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Usuario>> ListarAgentesAsync(CancellationToken cancellationToken = default);

    Task<bool> EmailExisteAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Incrementa de forma atômica o contador de tentativas falhas no banco de dados,
    /// evitando perda de atualizações em requisições simultâneas.
    /// </summary>
    Task IncrementarTentativasFalhasAsync(int usuarioId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Incrementa tentativas falhas e, se atingir o limite, bloqueia a conta — tudo
    /// em uma única operação atômica no banco, sem dependir do change tracker do EF Core.
    /// </summary>
    Task IncrementarTentativasEFalharSeLimiteAsync(
        int usuarioId,
        int maxTentativas,
        int bloqueioMinutos,
        CancellationToken cancellationToken = default);

    Task AddAsync(Usuario usuario, CancellationToken cancellationToken = default);
}