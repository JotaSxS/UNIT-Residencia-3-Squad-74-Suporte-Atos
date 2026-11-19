using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SupportAtos.Application.Abstractions;
using SupportAtos.Application.DTOs;
using SupportAtos.Application.Exceptions;
using SupportAtos.Application.Options;
using SupportAtos.Application.Ports;
using SupportAtos.Domain.Entities;
using SupportAtos.Domain.Enums;

namespace SupportAtos.Application.Services;

/// <summary>
/// Serviço de autenticação própria de funcionários internos (e-mail + senha).
/// </summary>
public class AuthService : IAuthService
{
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly ISessaoAutenticacaoRepository _sessaoRepository;
    private readonly IHashSenha _hashSenha;
    private readonly IRefreshTokenGerador _refreshTokenGerador;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenGerador _tokenGerador;
    private readonly AuthSettings _authSettings;

    // Formato nativo Base64(salt).Base64(hash); valor sintético sem conta associada.
    public const string HashVerificacaoInexistente = "AAECAwQFBgcICQoLDA0ODw==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private static readonly TimeSpan ToleranciaReuso = TimeSpan.FromSeconds(5);

    public AuthService(
        IUsuarioRepository usuarioRepository,
        ISessaoAutenticacaoRepository sessaoRepository,
        IHashSenha hashSenha,
        IRefreshTokenGerador refreshTokenGerador,
        IUnitOfWork unitOfWork,
        ITokenGerador tokenGerador,
        IOptions<AuthSettings> authSettings)
    {
        _usuarioRepository = usuarioRepository;
        _sessaoRepository = sessaoRepository;
        _hashSenha = hashSenha;
        _refreshTokenGerador = refreshTokenGerador;
        _unitOfWork = unitOfWork;
        _tokenGerador = tokenGerador;
        _authSettings = authSettings.Value;
    }

    public async Task<LoginResult> LoginAsync(
        LoginInput input,
        string enderecoIp,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var email = input.Email.Trim().ToLowerInvariant();

        // Busca o usuário sem revelar se o e-mail existe.
        var usuario = await _usuarioRepository.GetByEmailAsync(email, cancellationToken);

        var credencial = usuario?.Credencial;

        // Se a conta já está bloqueada, rejeita imediatamente sem verificar a senha.
        if (credencial is not null && credencial.EstaBloqueado())
        {
            throw new ValidationException("E-mail ou senha inválidos.");
        }

        // O formato nativo força Argon2 também para credenciais inexistentes.
        // Aproxima o custo computacional, sem prometer tempo de resposta constante.
        if (usuario is null || credencial is null)
        {
            _hashSenha.Verifica(input.Senha, HashVerificacaoInexistente);
            throw new ValidationException("E-mail ou senha inválidos.");
        }

        // Verifica a senha do usuário existente.
        var senhaValida = credencial.Ativo
            && !credencial.EstaBloqueado()
            && _hashSenha.Verifica(input.Senha, credencial.SenhaHash);

        // Se não há credencial ou senha inválida, registra falha de forma atômica
        // no banco (evita perda de incrementos em requisições simultâneas)
        // e lança exceção genérica.
        if (!senhaValida)
        {
            await _usuarioRepository.IncrementarTentativasEFalharSeLimiteAsync(
                usuario.Id,
                _authSettings.MaxTentativasFalhas,
                _authSettings.BloqueioMinutos,
                cancellationToken);

            throw new ValidationException("E-mail ou senha inválidos.");
        }

        // Verifica se é funcionário interno (Admin, Supervisor ou Agente).
        if (usuario.Papel is not (PapelUsuario.Admin or PapelUsuario.Supervisor or PapelUsuario.Agente))
        {
            throw new ValidationException("E-mail ou senha inválidos.");
        }

        // Limpa falhas após login bem-sucedido.
        credencial.LimparFalhas();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Gera refresh token e persiste apenas seu hash no banco.
        var refreshToken = _refreshTokenGerador.GerarToken();
        var refreshTokenHash = FazerHash(refreshToken);
        var refreshTokenExpiracao = DateTime.UtcNow.AddDays(_authSettings.RefreshTokenExpiracaoDias);

        var sessao = SessaoAutenticacao.Criar(
            usuario.Id,
            refreshTokenHash,
            refreshTokenExpiracao,
            enderecoIp,
            userAgent);

        await _sessaoRepository.AddAsync(sessao, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var jwtToken = _tokenGerador.GerarToken(
            usuario.Id,
            usuario.Email,
            usuario.Nome,
            usuario.Papel,
            usuario.GrupoEmpresaId);

        return new LoginResult(
            jwtToken,
            DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(_authSettings.ExpiracaoMinutos), DateTimeKind.Utc),
            usuario.Id,
            usuario.Nome,
            usuario.Email,
            usuario.Papel,
            usuario.GrupoEmpresaId,
            refreshToken);
    }

    public async Task<RefreshTokenResult> RefreshAsync(
        string refreshToken,
        string enderecoIp,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var refreshTokenHash = FazerHash(refreshToken);

        var resultado = await _sessaoRepository.ExecutarSerializadoAsync<RefreshTokenResult?>(
            refreshTokenHash, async () =>
        {
            var sessao = await _sessaoRepository.GetByRefreshTokenHashAsync(refreshTokenHash, cancellationToken);
            if (sessao is null) return null;
            if (sessao.EstaRevogado())
            {
                // Rejeita duplicatas próximas sem revogar a requisição vencedora.
                // Reuso tardio revoga apenas descendentes, preservando outros logins.
                if (sessao.SubstituidoPorRefreshTokenHash is not null
                    && DateTime.UtcNow - sessao.RevogadoEm!.Value > ToleranciaReuso)
                {
                    await RevogarCadeiaAsync(sessao, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
                return null; // Confirma a revogação antes de lançar a exceção fora da transação.
            }

            if (sessao.EstaExpirado())
            {
                throw new ValidationException("Token de renovação expirado.");
            }

            // Valida o usuário vinculado à sessão.
            var usuario = await _usuarioRepository.GetByIdAsync(sessao.UsuarioId, cancellationToken);
            if (usuario is null)
            {
                throw new ValidationException("Token de renovação inválido.");
            }

            if (usuario.Credencial is null || !usuario.Credencial.Ativo)
            {
                throw new ValidationException("Token de renovação inválido: credencial de acesso não encontrada ou inativa.");
            }

            if (usuario.Credencial.EstaBloqueado())
            {
                throw new ValidationException("Token de renovação inválido: conta bloqueada.");
            }

            if (usuario.Papel is not (PapelUsuario.Admin or PapelUsuario.Supervisor or PapelUsuario.Agente))
            {
                throw new ValidationException("Token de renovação inválido: papel não autorizado.");
            }

            // Rotação: revoga a sessão atual e cria uma nova com novo refresh token.
            var novoRefreshToken = _refreshTokenGerador.GerarToken();
            var novoRefreshTokenHash = FazerHash(novoRefreshToken);
            var novoRefreshTokenExpiracao = DateTime.UtcNow.AddDays(_authSettings.RefreshTokenExpiracaoDias);

            // Marca a sessão atual como substituída e a revoga.
            sessao.MarcarComoSubstituido(novoRefreshTokenHash);
            sessao.Revogar();

            // Cria a nova sessão.
            var novaSessao = SessaoAutenticacao.Criar(
                usuario.Id,
                novoRefreshTokenHash,
                novoRefreshTokenExpiracao,
                enderecoIp,
                userAgent);

            await _sessaoRepository.AddAsync(novaSessao, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Emite novo JWT com dados atuais do banco (papel, grupo, etc.).
            var jwtToken = _tokenGerador.GerarToken(
                usuario.Id,
                usuario.Email,
                usuario.Nome,
                usuario.Papel,
                usuario.GrupoEmpresaId);

            return new RefreshTokenResult(
                jwtToken,
                DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(_authSettings.ExpiracaoMinutos), DateTimeKind.Utc),
                usuario.Id,
                usuario.Nome,
                usuario.Email,
                usuario.Papel,
                usuario.GrupoEmpresaId,
                novoRefreshToken);
        }, cancellationToken);
        return resultado ?? throw new ValidationException("Token de renova\u00e7\u00e3o inv\u00e1lido.");
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = FazerHash(refreshToken);
        await _sessaoRepository.ExecutarSerializadoAsync(hash, async () =>
        {
            var sessao = await _sessaoRepository.GetByRefreshTokenHashAsync(hash, cancellationToken);
            if (sessao is not null)
            {
                await RevogarCadeiaAsync(sessao, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            return true;
        }, cancellationToken);
    }

    private async Task RevogarCadeiaAsync(SessaoAutenticacao sessao, CancellationToken cancellationToken)
    {
        var visitados = new HashSet<string>();
        SessaoAutenticacao? atual = sessao;
        while (atual is not null && visitados.Add(atual.RefreshTokenHash))
        {
            if (!atual.EstaRevogado()) atual.Revogar();
            atual = atual.SubstituidoPorRefreshTokenHash is { } proximo
                ? await _sessaoRepository.GetByRefreshTokenHashAsync(proximo, cancellationToken)
                : null;
            if (atual is not null && atual.UsuarioId != sessao.UsuarioId) break;
        }
    }

    private static string FazerHash(string valor)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(valor));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
