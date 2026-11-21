using Microsoft.Extensions.Hosting;
using System.Security.Cryptography;
using System.Text;
using SupportAtos.Application.Abstractions;
using SupportAtos.Application.DTOs;
using SupportAtos.Application.Exceptions;
using SupportAtos.Application.Ports;
using SupportAtos.Application.UseCases;
using SupportAtos.Domain.Entities;
using SupportAtos.Domain.Enums;

namespace SupportAtos.Application.Services;

public class FuncionarioAutenticacaoService(
    CadastrarUsuarioUseCase cadastrarUsuario,
    IFuncionarioAutenticacaoRepository repository,
    IUsuarioRepository usuarios,
    IUnitOfWork unitOfWork,
    IHashSenha hashSenha,
    IRefreshTokenGerador tokens,
    ICurrentUser currentUser,
    IHostEnvironment environment)
{
    public async Task<ConviteFuncionarioResult> CadastrarAsync(CadastrarUsuarioInput input, CancellationToken ct = default)
    {
        if (!currentUser.IsAutenticado || currentUser.UsuarioAtual.Papel != PapelUsuario.Admin)
            throw new ForbiddenException("Somente Admin pode cadastrar funcionários internos.");
        ValidarPapelInterno(input.Papel);

        return await repository.ExecutarCadastroAsync(async () =>
        {
            var usuario = await cadastrarUsuario.Handle(input, ct);
            var token = tokens.GerarToken(); // 32 bytes aleatórios; mesmo gerador seguro já utilizado no refresh.
            var expiraEm = DateTime.UtcNow.AddHours(24);
            await repository.AdicionarCredencialAsync(
                UsuarioCredencial.CriarPendente(usuario.Id, HashToken(token), expiraEm), ct);
            await unitOfWork.SaveChangesAsync(ct);
            return new ConviteFuncionarioResult(usuario.Id, usuario.Nome, usuario.Email, usuario.GrupoEmpresaId,
                usuario.GrupoEmpresaNome, usuario.Papel, token, expiraEm);
        }, ct);
    }

    public async Task AtivarAsync(AtivarFuncionarioInput input, CancellationToken ct = default)
    {
        ValidarSenha(input.Senha);
        if (string.IsNullOrWhiteSpace(input.Token) || input.Token.Length is < 40 or > 128)
            throw new ValidationException("Convite inválido ou expirado.");

        var hash = HashToken(input.Token);
        await repository.ExecutarAtivacaoAsync(hash, async () =>
        {
            var credencial = await repository.BuscarConviteAsync(hash, ct);
            var usuario = credencial is null ? null : await usuarios.GetByIdAsync(credencial.UsuarioId, ct);
            if (usuario is null || credencial is null || usuario.CodigoExterno is not null
                || usuario.Papel is not (PapelUsuario.Admin or PapelUsuario.Supervisor or PapelUsuario.Agente)
                || credencial.Ativo || credencial.AtivacaoConsumidaEm.HasValue
                || !credencial.TokenAtivacaoExpiraEm.HasValue || credencial.TokenAtivacaoExpiraEm.Value <= DateTime.UtcNow)
                throw new ValidationException("Convite inválido ou expirado.");

            credencial.AtivarComPrimeiraSenha(hashSenha.Hash(input.Senha));
            await unitOfWork.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    // Somente o comando local explícito chama este método; não existe rota HTTP de bootstrap.
    public async Task<UsuarioDto> BootstrapAsync(BootstrapAdminInput input, CancellationToken ct = default)
    {
        ValidarSenha(input.Senha, environment.IsDevelopment() ? 6 : 15);
        return await repository.ExecutarBootstrapAsync(async () =>
        {
            var usuario = await cadastrarUsuario.Handle(new(input.Nome, input.Email, null, PapelUsuario.Admin), ct);
            // O operador define sua primeira senha durante o procedimento, antes de qualquer login.
            await repository.AdicionarCredencialAsync(UsuarioCredencial.Criar(usuario.Id, hashSenha.Hash(input.Senha)), ct);
            await unitOfWork.SaveChangesAsync(ct);
            return usuario;
        }, ct);
    }

    private static void ValidarPapelInterno(PapelUsuario papel)
    {
        if (papel is not (PapelUsuario.Admin or PapelUsuario.Supervisor or PapelUsuario.Agente))
            throw new ValidationException("Credenciais internas são exclusivas de Admin, Supervisor e Agente.");
    }

    private static void ValidarSenha(string senha, int minimo = 15)
    {
        if (string.IsNullOrWhiteSpace(senha) || senha.Length < minimo || senha.Length > 128)
            throw new ValidationException($"A senha deve conter entre {minimo} e 128 caracteres.");
    }

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
