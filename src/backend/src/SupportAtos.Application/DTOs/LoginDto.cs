using System.Text.Json.Serialization;
using SupportAtos.Domain.Enums;

namespace SupportAtos.Application.DTOs;

/// <summary>Entrada do endpoint de login próprio.</summary>
public record LoginInput(string Email, string Senha);

/// <summary>Resultado do login próprio com JWT e refresh token (apenas cookie).</summary>
public record LoginResult(
    string Token,
    DateTime ExpiraEm,
    int UsuarioId,
    string Nome,
    string Email,
    PapelUsuario Papel,
    int? GrupoEmpresaId,
    [property: JsonIgnore]
    string? RefreshToken = null);

/// <summary>Resultado da renovação de token com novo JWT e refresh token rotacionado.</summary>
public record RefreshTokenResult(
    string Token,
    DateTime ExpiraEm,
    int UsuarioId,
    string Nome,
    string Email,
    PapelUsuario Papel,
    int? GrupoEmpresaId,
    [property: JsonIgnore]
    string? RefreshToken = null);

/// <summary>Resultado do logout.</summary>
public record LogoutResult(
    string Mensagem);
