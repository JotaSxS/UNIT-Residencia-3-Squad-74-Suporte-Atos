using System.ComponentModel.DataAnnotations;
using SupportAtos.Domain.Enums;

namespace SupportAtos.Application.DTOs;

public record AtivarFuncionarioInput(
    [Required, StringLength(128, MinimumLength = 40)] string Token,
    [Required, StringLength(128, MinimumLength = 15)] string Senha);

// Preserva os campos existentes de UsuarioDto na raiz da resposta de cadastro.
public record ConviteFuncionarioResult(int Id, string Nome, string Email, int? GrupoEmpresaId,
    string? GrupoEmpresaNome, PapelUsuario Papel, string TokenAtivacao, DateTime ExpiraEm);

public record BootstrapAdminInput(string Nome, string Email, string Senha);
