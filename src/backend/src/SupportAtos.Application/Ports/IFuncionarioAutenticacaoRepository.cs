using SupportAtos.Domain.Entities;

namespace SupportAtos.Application.Ports;

public interface IFuncionarioAutenticacaoRepository
{
    Task<T> ExecutarCadastroAsync<T>(Func<Task<T>> operacao, CancellationToken ct = default);
    Task<T> ExecutarBootstrapAsync<T>(Func<Task<T>> operacao, CancellationToken ct = default);
    Task<T> ExecutarAtivacaoAsync<T>(string hash, Func<Task<T>> operacao, CancellationToken ct = default);
    Task<UsuarioCredencial?> BuscarConviteAsync(string hash, CancellationToken ct = default);
    Task AdicionarCredencialAsync(UsuarioCredencial credencial, CancellationToken ct = default);
}
