using SupportAtos.Application.DTOs;
using SupportAtos.Application.Abstractions;
using SupportAtos.Application.Ports;
using SupportAtos.Application.Exceptions;
using SupportAtos.Domain.Enums;

namespace SupportAtos.Application.UseCases;

public class ListarFuncionariosUseCase(IUsuarioRepository usuarios, ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<FuncionarioDto>> Handle(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAutenticado || currentUser.UsuarioAtual.Papel != PapelUsuario.Admin)
            throw new ForbiddenException("Somente Admin pode listar funcionarios internos.");
        var funcionarios = await usuarios.ListarFuncionariosAsync(cancellationToken);
        return funcionarios.Select(u => new FuncionarioDto(u.Id, u.Nome, u.Email, u.Papel,
            u.Credencial is null || string.IsNullOrEmpty(u.Credencial.SenhaHash) ? "Pendente"
                : u.Credencial.Ativo ? "Ativa" : "Inativa")).ToList();
    }
}
