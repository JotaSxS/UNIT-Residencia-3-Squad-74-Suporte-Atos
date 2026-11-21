using SupportAtos.Domain.Enums;

namespace SupportAtos.Application.DTOs;

/// <summary>DTO para listagem administrativa de funcionários internos.</summary>
public record FuncionarioDto(
    int Id,
    string Nome,
    string Email,
    PapelUsuario Papel,
    string Situacao // "Pendente", "Ativa" ou "Inativa" (situacao da credencial, nao do convite)
);