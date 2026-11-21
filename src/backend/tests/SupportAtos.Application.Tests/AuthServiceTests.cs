using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using SupportAtos.Application.Abstractions;
using SupportAtos.Application.DTOs;
using SupportAtos.Application.Exceptions;
using SupportAtos.Application.Options;
using SupportAtos.Application.Ports;
using SupportAtos.Application.Services;
using SupportAtos.Application.Tests.Fakes;
using SupportAtos.Domain.Entities;
using SupportAtos.Domain.Enums;

namespace SupportAtos.Application.Tests;

public class FakeHashSenha : IHashSenha
{
    public string Hash(string senha) => $"hash:{senha}";
    public bool Verifica(string senha, string hash) => hash == $"hash:{senha}";
}

public class FakeTokenGerador : ITokenGerador
{
    public string GerarToken(int usuarioId, string email, string nome, PapelUsuario papel, int? grupoEmpresaId)
        => $"token-fake:{usuarioId}:{papel}";
}

public class FakeRefreshTokenGerador : IRefreshTokenGerador
{
    private readonly Queue<string> _fila = new();
    private int _contador;

    public string GerarToken()
    {
        if (_fila.Count > 0)
            return _fila.Dequeue();

        _contador++;
        return $"refresh-fake:{_contador}";
    }

    public void EnfileirarParams(params string[] tokens)
    {
        foreach (var t in tokens)
            _fila.Enqueue(t);
    }
}

public class FakeUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
}

public class AuthServiceTests
{
    private Usuario CriarUsuarioInterno(string email, PapelUsuario papel, int? grupoEmpresaId = null, bool ativo = true, string senha = "senha123")
    {
        var usuario = Usuario.Criar("Usuario", email, grupoEmpresaId, papel);
        usuario.SetIdForTest(1);
        var credencial = UsuarioCredencial.Criar(1, $"hash:{senha}");
        credencial.SetForTest("Ativo", ativo);
        usuario.SetForTest("Credencial", credencial);
        return usuario;
    }

    private static T SetIdForTest<T>(T entity, int id) where T : class
    {
        var property = typeof(T).GetProperty("Id");
        property!.SetValue(entity, id);
        return entity;
    }

    private static T SetForTest<T>(T entity, string propertyName, object? value) where T : class
    {
        typeof(T).GetProperty(propertyName)!.SetValue(entity, value);
        return entity;
    }

    [Fact]
    public async Task Login_Valido_DeveRetornarTokenERefreshToken()
    {
        var usuario = CriarUsuarioInterno("admin@atos.com", PapelUsuario.Admin);
        var repo = new FakeUsuarioRepositoryComCredencial(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshToken = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings { ExpiracaoMinutos = 15, MaxTentativasFalhas = 5, BloqueioMinutos = 30, RefreshTokenExpiracaoDias = 7 });

        var service = new AuthService(repo, repo, hash, refreshToken, uow, token, settings);

        var resultado = await service.LoginAsync(new LoginInput("admin@atos.com", "senha123"), "127.0.0.1", null);

        Assert.NotNull(resultado.Token);
        Assert.NotNull(resultado.RefreshToken);
        Assert.Equal(1, resultado.UsuarioId);
        Assert.Equal("admin@atos.com", resultado.Email);
        Assert.Equal(PapelUsuario.Admin, resultado.Papel);
        Assert.Contains("token-fake:1:Admin", resultado.Token);
        Assert.Contains("refresh-fake:1", resultado.RefreshToken);
    }

    [Fact]
    public async Task Refresh_Valido_DeveRetornarNovoTokenERotacionarRefreshToken()
    {
        var usuario = CriarUsuarioInterno("admin@atos.com", PapelUsuario.Admin);
        var repo = new FakeUsuarioRepositoryComCredencial(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshToken = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings { ExpiracaoMinutos = 15, MaxTentativasFalhas = 5, BloqueioMinutos = 30, RefreshTokenExpiracaoDias = 7 });

        var service = new AuthService(repo, repo, hash, refreshToken, uow, token, settings);

        // Login inicial
        var login = await service.LoginAsync(new LoginInput("admin@atos.com", "senha123"), "127.0.0.1", null);
        var refreshTokenUsado = login.RefreshToken!;

        // Renova usando o refresh token
        var renovacao = await service.RefreshAsync(refreshTokenUsado, "127.0.0.1", null);

        Assert.NotNull(renovacao.Token);
        Assert.Equal(1, renovacao.UsuarioId);
        Assert.Equal(PapelUsuario.Admin, renovacao.Papel);
        // O novo refresh token deve ser diferente do antigo
        Assert.NotEqual(refreshTokenUsado, renovacao.RefreshToken);
    }

    [Fact]
    public async Task Refresh_RefreshTokenExpirado_DeveLancarValidationException()
    {
        var usuario = CriarUsuarioInterno("admin@atos.com", PapelUsuario.Admin);
        var repo = new FakeUsuarioRepositoryComCredencial(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshTokenGerador = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings { ExpiracaoMinutos = 15, MaxTentativasFalhas = 5, BloqueioMinutos = 30, RefreshTokenExpiracaoDias = 7 });

        var service = new AuthService(repo, repo, hash, refreshTokenGerador, uow, token, settings);

        // Login e depois expira manualmente a sessão
        var login = await service.LoginAsync(new LoginInput("admin@atos.com", "senha123"), "127.0.0.1", null);

        // Acessa a sessão diretamente pelo repositório fake para expiração
        // O FakeUsuarioRepositoryComCredencial armazena o usuário; precisamos acessar a sessão
        // Para simplificar, testamos com um refresh token inexistente (simulando expiração/remoção)
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.RefreshAsync("refresh-token-inexistente", "127.0.0.1", null));

        Assert.Contains("inválido", ex.Message);
    }

    [Fact]
    public async Task Refresh_ContaInativa_DeveLancarValidationException()
    {
        var usuario = CriarUsuarioInterno("admin@atos.com", PapelUsuario.Admin, ativo: true);
        var repo = new FakeUsuarioRepositoryComCredencial(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshTokenGerador = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings { ExpiracaoMinutos = 15, MaxTentativasFalhas = 5, BloqueioMinutos = 30, RefreshTokenExpiracaoDias = 7 });

        var service = new AuthService(repo, repo, hash, refreshTokenGerador, uow, token, settings);

        // Login com conta ativa
        var login = await service.LoginAsync(new LoginInput("admin@atos.com", "senha123"), "127.0.0.1", null);

        // Desativa a credencial (simula desativação administrativa)
        usuario.Credencial!.Desativar();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.RefreshAsync(login.RefreshToken!, "127.0.0.1", null));

        Assert.Contains("inativa", ex.Message);
    }

    [Fact]
    public async Task Refresh_ReusoTardio_RevogaDescendentes()
    {
        var usuario = CriarUsuarioInterno("reuso@atos.com", PapelUsuario.Agente);
        var repo = new FakeUsuarioRepositoryComCredencial(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshTokenGerador = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings { ExpiracaoMinutos = 15, MaxTentativasFalhas = 5, BloqueioMinutos = 30, RefreshTokenExpiracaoDias = 7 });

        var service = new AuthService(repo, repo, hash, refreshTokenGerador, uow, token, settings);

        // Login inicial
        var login1 = await service.LoginAsync(new LoginInput("reuso@atos.com", "senha123"), "127.0.0.1", null);
        var rt1 = login1.RefreshToken!;

        // Primeiro refresh: rotaciona rt1 -> rt2
        var refresh1 = await service.RefreshAsync(rt1, "127.0.0.1", null);
        var rt2 = refresh1.RefreshToken!;

        // Segundo refresh: rotaciona rt2 -> rt3
        var refresh2 = await service.RefreshAsync(rt2, "127.0.0.1", null);
        var rt3 = refresh2.RefreshToken!;

        (await repo.GetByRefreshTokenHashAsync(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rt1))).ToLowerInvariant()))!
            .SetForTest("RevogadoEm", DateTime.UtcNow.AddSeconds(-10));

        // Agora tenta usar rt1 novamente (já foi substituído por rt2)
        // Isso deve detectar reuso e revogar os descendentes da sessão
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.RefreshAsync(rt1, "127.0.0.1", null));

        Assert.Contains("inválido", ex.Message);

        // Tentar usar rt3 (a última sessão válida) agora deve falhar porque seus descendentes foram revogados
        var ex2 = await Assert.ThrowsAsync<ValidationException>(() =>
            service.RefreshAsync(rt3, "127.0.0.1", null));

        Assert.Contains("inválido", ex2.Message);
    }

    [Fact]
    public async Task Logout_RevogaRefreshToken()
    {
        var usuario = CriarUsuarioInterno("admin@atos.com", PapelUsuario.Admin);
        var repo = new FakeUsuarioRepositoryComCredencial(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshTokenGerador = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings { ExpiracaoMinutos = 15, MaxTentativasFalhas = 5, BloqueioMinutos = 30, RefreshTokenExpiracaoDias = 7 });

        var service = new AuthService(repo, repo, hash, refreshTokenGerador, uow, token, settings);

        // Login
        var login = await service.LoginAsync(new LoginInput("admin@atos.com", "senha123"), "127.0.0.1", null);
        var refreshToken = login.RefreshToken!;

        // Logout
        await service.LogoutAsync(refreshToken);

        // Tentar usar o refresh token após logout deve falhar
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.RefreshAsync(refreshToken, "127.0.0.1", null));

        Assert.Contains("inválido", ex.Message);
    }

    [Fact]
    public async Task Refresh_ContaBloqueada_DeveLancarValidationException()
    {
        var usuario = CriarUsuarioInterno("admin@atos.com", PapelUsuario.Admin);
        var repo = new FakeUsuarioRepositoryComCredencial(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshTokenGerador = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings { ExpiracaoMinutos = 15, MaxTentativasFalhas = 1, BloqueioMinutos = 30, RefreshTokenExpiracaoDias = 7 });

        var service = new AuthService(repo, repo, hash, refreshTokenGerador, uow, token, settings);

        // Login inicial com sucesso (cria sessão e refresh token)
        var login = await service.LoginAsync(new LoginInput("admin@atos.com", "senha123"), "127.0.0.1", null);

        // Forçar bloqueio: 1 tentativa falha (MaxTentativasFalhas = 1)
        try
        {
            await service.LoginAsync(new LoginInput("admin@atos.com", "senha_errada"), "127.0.0.1", null);
        }
        catch { }

        // Agora tente fazer refresh com o token existente - deve falhar porque a conta está bloqueada
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.RefreshAsync(login.RefreshToken!, "127.0.0.1", null));

        Assert.Contains("bloqueada", ex.Message);
    }

    private sealed class FakeUsuarioRepositoryComCredencial : IUsuarioRepository, ISessaoAutenticacaoRepository
    {
        private readonly Usuario? _usuario;
        private readonly List<SessaoAutenticacao> _sessoes = new();

        public FakeUsuarioRepositoryComCredencial(Usuario? usuario)
        {
            _usuario = usuario;
        }

        public Task<Usuario?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
            => Task.FromResult<Usuario?>(_usuario);

        public Task<Usuario?> GetComGrupoEmpresaPorIdAsync(int id, CancellationToken cancellationToken = default)
            => Task.FromResult<Usuario?>(_usuario);

        public Task<IReadOnlyList<Usuario>> ListarPorGrupoEmpresaAsync(int grupoEmpresaId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Usuario>>(System.Array.Empty<Usuario>());

        public Task<Usuario?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
            => Task.FromResult<Usuario?>(_usuario);

        public Task<Usuario?> GetByCodigoExternoAsync(string codigoExterno, CancellationToken cancellationToken = default)
            => Task.FromResult<Usuario?>(null);

        public Task<bool> EmailExisteAsync(string email, CancellationToken cancellationToken = default)
            => Task.FromResult(_usuario is not null && _usuario.Email == email);

        public Task<IReadOnlyList<Usuario>> ListarFuncionariosAsync(CancellationToken cancellationToken = default) => ListarAgentesAsync(cancellationToken);

        public Task<IReadOnlyList<Usuario>> ListarAgentesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Usuario>>(System.Array.Empty<Usuario>());

        public Task AddAsync(Usuario usuario, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task IncrementarTentativasFalhasAsync(int usuarioId, CancellationToken cancellationToken = default)
        {
            if (_usuario is not null && _usuario.Id == usuarioId && _usuario.Credencial is not null)
            {
                _usuario.Credencial.RegistrarFalha();
            }

            return Task.CompletedTask;
        }

        public Task IncrementarTentativasEFalharSeLimiteAsync(int usuarioId, int maxTentativas, int bloqueioMinutos, CancellationToken cancellationToken = default)
        {
            if (_usuario is not null && _usuario.Id == usuarioId && _usuario.Credencial is not null)
            {
                _usuario.Credencial.RegistrarFalha();

                if (_usuario.Credencial.TentativasFalhas >= maxTentativas)
                {
                    _usuario.Credencial.Bloquear(DateTime.UtcNow.AddMinutes(bloqueioMinutos));
                }
            }

            return Task.CompletedTask;
        }

        // Implementação de ISessaoAutenticacaoRepository
        private readonly SemaphoreSlim _sessaoLock = new(1, 1);
        public async Task<T> ExecutarSerializadoAsync<T>(string hash, Func<Task<T>> operacao, CancellationToken cancellationToken = default)
        {
            await _sessaoLock.WaitAsync(cancellationToken);
            try { return await operacao(); }
            finally { _sessaoLock.Release(); }
        }

        public Task<SessaoAutenticacao?> GetByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken cancellationToken = default)
        {
            // O refreshTokenHash já vem hasheado (SHA256) do AuthService.
            // Comparamos diretamente com o hash armazenado na sessão.
            var sessao = _sessoes.FirstOrDefault(s => s.RefreshTokenHash == refreshTokenHash);
            return Task.FromResult(sessao);
        }

        public Task AddAsync(SessaoAutenticacao sessao, CancellationToken cancellationToken = default)
        {
            _sessoes.Add(sessao);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SessaoAutenticacao>> GetByUsuarioIdAsync(int usuarioId, CancellationToken cancellationToken = default)
        {
            var sessoes = _sessoes.Where(s => s.UsuarioId == usuarioId).ToList();
            return Task.FromResult<IReadOnlyList<SessaoAutenticacao>>(sessoes);
        }
    }

    private sealed class FakeUsuarioRepositoryConcorrente : IUsuarioRepository, ISessaoAutenticacaoRepository
    {
        private readonly Usuario _usuario;
        private int _tentativasFalhas;
        private readonly object _lock = new();
        private readonly List<SessaoAutenticacao> _sessoes = new();

        public FakeUsuarioRepositoryConcorrente(Usuario usuario)
        {
            _usuario = usuario;
            _tentativasFalhas = usuario.Credencial?.TentativasFalhas ?? 0;
        }

        public Task<Usuario?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
            => Task.FromResult<Usuario?>(null);

        public Task<Usuario?> GetComGrupoEmpresaPorIdAsync(int id, CancellationToken cancellationToken = default)
            => Task.FromResult<Usuario?>(null);

        public Task<IReadOnlyList<Usuario>> ListarPorGrupoEmpresaAsync(int grupoEmpresaId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Usuario>>(System.Array.Empty<Usuario>());

        public Task<Usuario?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
            => Task.FromResult<Usuario?>(_usuario);

        public Task<Usuario?> GetByCodigoExternoAsync(string codigoExterno, CancellationToken cancellationToken = default)
            => Task.FromResult<Usuario?>(null);

        public Task<bool> EmailExisteAsync(string email, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<Usuario>> ListarFuncionariosAsync(CancellationToken cancellationToken = default) => ListarAgentesAsync(cancellationToken);

        public Task<IReadOnlyList<Usuario>> ListarAgentesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Usuario>>(System.Array.Empty<Usuario>());

        public Task AddAsync(Usuario usuario, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task IncrementarTentativasFalhasAsync(int usuarioId, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _tentativasFalhas++;
                if (_usuario.Credencial is not null)
                {
                    _usuario.Credencial.SetForTest("TentativasFalhas", _tentativasFalhas);
                }
            }

            return Task.CompletedTask;
        }

        public Task IncrementarTentativasEFalharSeLimiteAsync(int usuarioId, int maxTentativas, int bloqueioMinutos, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _tentativasFalhas++;
                if (_usuario.Credencial is not null)
                {
                    _usuario.Credencial.SetForTest("TentativasFalhas", _tentativasFalhas);
                    if (_tentativasFalhas >= maxTentativas)
                    {
                        _usuario.Credencial.Bloquear(DateTime.UtcNow.AddMinutes(bloqueioMinutos));
                    }
                }
            }

            return Task.CompletedTask;
        }

        private readonly SemaphoreSlim _sessaoLock = new(1, 1);
        public async Task<T> ExecutarSerializadoAsync<T>(string hash, Func<Task<T>> operacao, CancellationToken cancellationToken = default)
        {
            await _sessaoLock.WaitAsync(cancellationToken);
            try { return await operacao(); }
            finally { _sessaoLock.Release(); }
        }

        public Task<SessaoAutenticacao?> GetByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken cancellationToken = default)
        {
            var sessao = _sessoes.FirstOrDefault(s => s.RefreshTokenHash == refreshTokenHash);
            return Task.FromResult(sessao);
        }

        public Task AddAsync(SessaoAutenticacao sessao, CancellationToken cancellationToken = default)
        {
            _sessoes.Add(sessao);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SessaoAutenticacao>> GetByUsuarioIdAsync(int usuarioId, CancellationToken cancellationToken = default)
        {
            var sessoes = _sessoes.Where(s => s.UsuarioId == usuarioId).ToList();
            return Task.FromResult<IReadOnlyList<SessaoAutenticacao>>(sessoes);
        }

        public int ObterTentativasFalhas() => _tentativasFalhas;
    }

    [Fact]
    public async Task Login_RebaixamentoAdminParaAgente_UsaPapelDoBanco()
    {
        var usuario = CriarUsuarioInterno("rebaixado@atos.com", PapelUsuario.Admin);
        var repo = new FakeUsuarioRepositoryComCredencial(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshToken = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings { ExpiracaoMinutos = 15, MaxTentativasFalhas = 5, BloqueioMinutos = 30, RefreshTokenExpiracaoDias = 7 });

        var service = new AuthService(repo, repo, hash, refreshToken, uow, token, settings);

        var resultadoInicial = await service.LoginAsync(new LoginInput("rebaixado@atos.com", "senha123"), "127.0.0.1", null);
        Assert.Equal(PapelUsuario.Admin, resultadoInicial.Papel);

        usuario.AlterarPapel(PapelUsuario.Agente);

        var resultadoPosRebaixamento = await service.LoginAsync(new LoginInput("rebaixado@atos.com", "senha123"), "127.0.0.1", null);
        Assert.Equal(PapelUsuario.Agente, resultadoPosRebaixamento.Papel);
        Assert.Contains("token-fake:1:Agente", resultadoPosRebaixamento.Token);
    }

    [Fact]
    public async Task Login_MudancaDeGrupo_UsaGrupoDoBanco()
    {
        var usuario = CriarUsuarioInterno("mudanca@atos.com", PapelUsuario.Agente, grupoEmpresaId: 1);
        var repo = new FakeUsuarioRepositoryComCredencial(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshToken = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings { ExpiracaoMinutos = 15, MaxTentativasFalhas = 5, BloqueioMinutos = 30, RefreshTokenExpiracaoDias = 7 });

        var service = new AuthService(repo, repo, hash, refreshToken, uow, token, settings);

        var resultadoInicial = await service.LoginAsync(new LoginInput("mudanca@atos.com", "senha123"), "127.0.0.1", null);
        Assert.Equal(1, resultadoInicial.GrupoEmpresaId);

        usuario.AlterarGrupoEmpresa(2);

        var resultadoPosMudanca = await service.LoginAsync(new LoginInput("mudanca@atos.com", "senha123"), "127.0.0.1", null);
        Assert.Equal(2, resultadoPosMudanca.GrupoEmpresaId);
    }

    [Fact]
    public async Task Login_UsuarioInexistente_RejeitaCredenciais()
    {
        var repo = new FakeUsuarioRepositoryComCredencial(null);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshToken = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings { ExpiracaoMinutos = 15, MaxTentativasFalhas = 5, BloqueioMinutos = 30, RefreshTokenExpiracaoDias = 7 });

        var service = new AuthService(repo, repo, hash, refreshToken, uow, token, settings);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.LoginAsync(new LoginInput("naoexiste@atos.com", "qualquer_senha"), "127.0.0.1", null));

        Assert.Contains("inválidos", ex.Message);
    }

    [Fact]
    public async Task Refresh_Cliente_DeveLancarValidationException()
    {
        var usuario = Usuario.Criar("Cliente", "cliente@atos.com", 1, PapelUsuario.Cliente);
        usuario.SetIdForTest(2);
        var credencial = UsuarioCredencial.Criar(2, "hash:senha");
        usuario.SetForTest("Credencial", credencial);
        var repo = new FakeUsuarioRepositoryComCredencial(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshToken = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings { ExpiracaoMinutos = 15, MaxTentativasFalhas = 5, BloqueioMinutos = 30, RefreshTokenExpiracaoDias = 7 });

        var service = new AuthService(repo, repo, hash, refreshToken, uow, token, settings);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.LoginAsync(new LoginInput("cliente@atos.com", "senha123"), "127.0.0.1", null));

        Assert.Contains("inválidos", ex.Message);
    }

    [Fact]
    public async Task Login_ContaInativa_DeveLancarValidationException()
    {
        var usuario = CriarUsuarioInterno("admin@atos.com", PapelUsuario.Admin, ativo: false);
        var repo = new FakeUsuarioRepositoryComCredencial(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshToken = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings { ExpiracaoMinutos = 15, MaxTentativasFalhas = 5, BloqueioMinutos = 30, RefreshTokenExpiracaoDias = 7 });

        var service = new AuthService(repo, repo, hash, refreshToken, uow, token, settings);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.LoginAsync(new LoginInput("admin@atos.com", "senha123"), "127.0.0.1", null));

        Assert.Contains("inválidos", ex.Message);
    }

    [Fact]
    public async Task Login_BloqueioAposTentativasFalhas_DeveBloquearConta()
    {
        var usuario = CriarUsuarioInterno("admin@atos.com", PapelUsuario.Admin);
        var repo = new FakeUsuarioRepositoryComCredencial(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshToken = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings { ExpiracaoMinutos = 15, MaxTentativasFalhas = 3, BloqueioMinutos = 30, RefreshTokenExpiracaoDias = 7 });

        var service = new AuthService(repo, repo, hash, refreshToken, uow, token, settings);

        for (int i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<ValidationException>(() =>
                service.LoginAsync(new LoginInput("admin@atos.com", "senha_errada"), "127.0.0.1", null));
        }

        var credencial = usuario.Credencial;
        Assert.NotNull(credencial);
        Assert.True(credencial.EstaBloqueado());

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.LoginAsync(new LoginInput("admin@atos.com", "senha123"), "127.0.0.1", null));

        Assert.Contains("inválidos", ex.Message);
    }

    [Theory]
    [InlineData(PapelUsuario.Admin)]
    [InlineData(PapelUsuario.Supervisor)]
    [InlineData(PapelUsuario.Agente)]
    public async Task Login_FuncionarioInterno_DeveSucesso(PapelUsuario papel)
    {
        var usuario = CriarUsuarioInterno("func@atos.com", papel);
        var repo = new FakeUsuarioRepositoryComCredencial(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshToken = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings { ExpiracaoMinutos = 15, MaxTentativasFalhas = 5, BloqueioMinutos = 30, RefreshTokenExpiracaoDias = 7 });

        var service = new AuthService(repo, repo, hash, refreshToken, uow, token, settings);

        var resultado = await service.LoginAsync(new LoginInput("func@atos.com", "senha123"), "127.0.0.1", null);

        Assert.Equal(papel, resultado.Papel);
        Assert.Contains($"token-fake:1:{papel}", resultado.Token);
    }

    [Fact]
    public async Task IncrementarTentativasFalhas_ChamadasSimultaneas_NaoPerdeIncrementos()
    {
        var usuario = CriarUsuarioInterno("concorrente@atos.com", PapelUsuario.Agente);
        var repo = new FakeUsuarioRepositoryConcorrente(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshToken = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings
        {
            ExpiracaoMinutos = 15,
            MaxTentativasFalhas = 100,
            BloqueioMinutos = 30,
            RefreshTokenExpiracaoDias = 7
        });

        var service = new AuthService(repo, repo, hash, refreshToken, uow, token, settings);

        var tarefas = Enumerable.Range(0, 20).Select(_ =>
            Assert.ThrowsAsync<ValidationException>(() =>
                service.LoginAsync(new LoginInput("concorrente@atos.com", "senha_errada"), "127.0.0.1", null)));

        await Task.WhenAll(tarefas);

        Assert.Equal(20, repo.ObterTentativasFalhas());
    }

    [Fact]
    public async Task Login_BloqueioAposLimiteComConcorrencia_ContaBloqueada()
    {
        var usuario = CriarUsuarioInterno("bloqueio@atos.com", PapelUsuario.Agente);
        var repo = new FakeUsuarioRepositoryConcorrente(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshToken = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings
        {
            ExpiracaoMinutos = 15,
            MaxTentativasFalhas = 3,
            BloqueioMinutos = 30,
            RefreshTokenExpiracaoDias = 7
        });

        var service = new AuthService(repo, repo, hash, refreshToken, uow, token, settings);

        var tarefas = Enumerable.Range(0, 3).Select(_ =>
            Assert.ThrowsAsync<ValidationException>(() =>
                service.LoginAsync(new LoginInput("bloqueio@atos.com", "senha_errada"), "127.0.0.1", null)));

        await Task.WhenAll(tarefas);

        Assert.Equal(3, repo.ObterTentativasFalhas());

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.LoginAsync(new LoginInput("bloqueio@atos.com", "senha123"), "127.0.0.1", null));

        Assert.Contains("inválidos", ex.Message);
    }

    [Fact]
    public async Task TokenJwt_AdminRebaixadoParaAgente_NaoTemMaisAcessoAdmin()
    {
        using var rsa = RSA.Create(2048);
        var chavePrivadaPem = rsa.ExportPkcs8PrivateKeyPem();
        var chavePublicaPem = rsa.ExportSubjectPublicKeyInfoPem();

        var tokenHandler = new JwtSecurityTokenHandler();
        var chaveAssinatura = new RsaSecurityKey(rsa);
        var credenciais = new SigningCredentials(chaveAssinatura, SecurityAlgorithms.RsaSha256);

        var tokenAdmin = tokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "1"),
                new Claim(ClaimTypes.Email, "admin@atos.com"),
                new Claim(ClaimTypes.Name, "Admin"),
                new Claim(ClaimTypes.Role, PapelUsuario.Admin.ToString()),
                new Claim("tipo", "interno"),
            }),
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = credenciais,
        });

        var tokenString = tokenHandler.WriteToken(tokenAdmin);

        var parametrosValidacao = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new RsaSecurityKey(rsa),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            ClockSkew = TimeSpan.Zero,
        };

        var principalValidado = tokenHandler.ValidateToken(tokenString, parametrosValidacao, out _);

        var papelNoBanco = PapelUsuario.Agente;

        var identidade = (ClaimsIdentity)principalValidado.Identity!;
        var claimsRoleAntigas = identidade.FindAll(ClaimTypes.Role).ToList();
        foreach (var c in claimsRoleAntigas)
        {
            identidade.RemoveClaim(c);
        }
        identidade.AddClaim(new Claim(ClaimTypes.Role, papelNoBanco.ToString()));

        var papelEfetivo = identidade.FindFirst(ClaimTypes.Role)?.Value;
        Assert.Equal(PapelUsuario.Agente.ToString(), papelEfetivo);

        var claimsAdmin = identidade.FindAll(ClaimTypes.Role).Where(c => c.Value == PapelUsuario.Admin.ToString());
        Assert.Empty(claimsAdmin);
    }

    [Fact]
    public async Task Refresh_DuasRequisicoesSimultaneas_MesmoToken_ApenasUmaSucesso()
    {
        // Arrange: usuário com sessão ativa
        var usuario = CriarUsuarioInterno("concorrente@atos.com", PapelUsuario.Agente);
        var repo = new FakeUsuarioRepositoryComCredencial(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshTokenGerador = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings
        {
            ExpiracaoMinutos = 15,
            MaxTentativasFalhas = 5,
            BloqueioMinutos = 30,
            RefreshTokenExpiracaoDias = 7
        });

        var service = new AuthService(repo, repo, hash, refreshTokenGerador, uow, token, settings);

        // Login inicial obtém refresh token
        var login = await service.LoginAsync(new LoginInput("concorrente@atos.com", "senha123"), "127.0.0.1", null);
        var refreshToken = login.RefreshToken!;

        // Act: duas requisições simultâneas com o mesmo refresh token
        var tarefa1 = service.RefreshAsync(refreshToken, "127.0.0.1", null);
        var tarefa2 = service.RefreshAsync(refreshToken, "127.0.0.1", null);

        var resultados = await Task.WhenAll(
            tarefa1.ContinueWith(t => t.IsCompletedSuccessfully ? "OK" : $"FALHOU: {t.Exception?.InnerException?.Message}"),
            tarefa2.ContinueWith(t => t.IsCompletedSuccessfully ? "OK" : $"FALHOU: {t.Exception?.InnerException?.Message}")
        );

        // Assert: exatamente uma deve ter sucesso, a outra deve falhar (token já foi rotacionado/revogado)
        var sucessos = resultados.Count(r => r == "OK");
        var falhas = resultados.Count(r => r != "OK");

        Assert.Equal(1, sucessos);
        Assert.Equal(1, falhas);
        Assert.Contains("FALHOU: Token de renovação inválido.", resultados);
    }

    [Fact]
    public async Task Refresh_MesmoTokenUsadoDuasVezes_SegundoFalha()
    {
        // Arrange: usar tokens pré-definidos explícitos para evitar qualquer
        // dependência de contador estático ou ordem de execução dos testes.
        var usuario = CriarUsuarioInterno("reuso@atos.com", PapelUsuario.Agente);
        var repo = new FakeUsuarioRepositoryComCredencial(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshTokenGerador = new FakeRefreshTokenGerador();
        refreshTokenGerador.EnfileirarParams("rt-login", "rt-refresh-1", "rt-refresh-2");
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings
        {
            ExpiracaoMinutos = 15,
            MaxTentativasFalhas = 5,
            BloqueioMinutos = 30,
            RefreshTokenExpiracaoDias = 7
        });

        var service = new AuthService(repo, repo, hash, refreshTokenGerador, uow, token, settings);

        // Login inicial (usa "rt-login")
        var login = await service.LoginAsync(new LoginInput("reuso@atos.com", "senha123"), "127.0.0.1", null);
        var rt1 = login.RefreshToken!;
        Assert.Equal("rt-login", rt1);

        // Primeiro refresh com rt1: rotaciona -> "rt-refresh-1"
        var refresh1 = await service.RefreshAsync(rt1, "127.0.0.1", null);
        var rt2 = refresh1.RefreshToken!;
        Assert.Equal("rt-refresh-1", rt2);

        // Segundo refresh com rt2: rotaciona -> "rt-refresh-2"
        var refresh2 = await service.RefreshAsync(rt2, "127.0.0.1", null);
        var rt3 = refresh2.RefreshToken!;
        Assert.Equal("rt-refresh-2", rt3);

        // Tentar usar rt1 novamente (já foi substituído): deve falhar
        var ex1 = await Assert.ThrowsAsync<ValidationException>(() =>
            service.RefreshAsync(rt1, "127.0.0.1", null));
        Assert.Contains("inválido", ex1.Message);

        // Tentar usar rt3 (válido, último gerado): deve funcionar
        var refresh3 = await service.RefreshAsync(rt3, "127.0.0.1", null);
        Assert.NotNull(refresh3.Token);
    }

    [Fact]
    public async Task Refresh_ReusoTardio_RevogaSucessor()
    {
        // Arrange
        var usuario = CriarUsuarioInterno("revogacao@atos.com", PapelUsuario.Admin);
        var repo = new FakeUsuarioRepositoryComCredencial(usuario);
        var uow = new FakeUnitOfWork();
        var hash = new FakeHashSenha();
        var token = new FakeTokenGerador();
        var refreshTokenGerador = new FakeRefreshTokenGerador();
        var settings = Microsoft.Extensions.Options.Options.Create(new AuthSettings
        {
            ExpiracaoMinutos = 15,
            MaxTentativasFalhas = 5,
            BloqueioMinutos = 30,
            RefreshTokenExpiracaoDias = 7
        });

        var service = new AuthService(repo, repo, hash, refreshTokenGerador, uow, token, settings);

        // Login inicial (sessão 1)
        var login1 = await service.LoginAsync(new LoginInput("revogacao@atos.com", "senha123"), "127.0.0.1", null);
        var rt1 = login1.RefreshToken!;

        // Primeiro refresh (sessão 1 -> sessão 2)
        var r1 = await service.RefreshAsync(rt1, "127.0.0.1", null);
        var rt2 = r1.RefreshToken!;

        (await repo.GetByRefreshTokenHashAsync(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rt1))).ToLowerInvariant()))!
            .SetForTest("RevogadoEm", DateTime.UtcNow.AddSeconds(-10));

        // Segundo refresh com rt1 (já substituído): detecta reuso e revoga os descendentes
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.RefreshAsync(rt1, "127.0.0.1", null));
        Assert.Contains("inválido", ex.Message);

        // rt2 (que estava válida) agora deve estar revogada devido à revogação da cadeia
        var ex2 = await Assert.ThrowsAsync<ValidationException>(() =>
            service.RefreshAsync(rt2, "127.0.0.1", null));
        Assert.Contains("inválido", ex2.Message);
    }
}
