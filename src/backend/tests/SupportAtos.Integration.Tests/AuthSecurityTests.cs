using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;
using SupportAtos.Api.Middleware;
using SupportAtos.Application.DTOs;
using SupportAtos.Application.Ports;
using SupportAtos.Application.Services;
using SupportAtos.Domain.Entities;
using SupportAtos.Domain.Enums;
using SupportAtos.Infrastructure.Persistence;
using SupportAtos.Infrastructure.Security;

namespace SupportAtos.Integration.Tests;

public class CsrfSecurityTests
{
    [Theory]
    [InlineData(null, null, 403)]
    [InlineData("", null, 403)]
    [InlineData(" ", null, 403)]
    [InlineData("null", null, 403)]
    [InlineData("https://evil.test", null, 403)]
    [InlineData("https://frontend.test.evil.test", null, 403)]
    [InlineData("https://frontend.test/path", null, 403)]
    [InlineData("https://user@frontend.test", null, 403)]
    [InlineData("https://frontend.test, https://evil.test", null, 403)]
    [InlineData("https://frontend.test:444", null, 403)]
    [InlineData("http://frontend.test", null, 403)]
    [InlineData("https://frontend.test", null, 204)]
    [InlineData(null, "https://frontend.test/page?q=1", 204)]
    [InlineData(null, "https://evil.test/page", 403)]
    [InlineData("null", "https://frontend.test/page", 403)]
    public async Task OrigemPrecisaSerConfiavel(string? origin, string? referer, int status)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/auth/refresh";
        context.Request.Method = "PATCH";
        if (origin is not null) context.Request.Headers.Origin = origin;
        if (referer is not null) context.Request.Headers.Referer = referer;
        var middleware = new CsrfProtectionMiddleware(c => { c.Response.StatusCode = 204; return Task.CompletedTask; },
            ["https://frontend.test"], ["/api/auth/refresh"]);
        await middleware.InvokeAsync(context);
        Assert.Equal(status, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://frontend.test/")]
    [InlineData("https://frontend.test/path")]
    [InlineData("null")]
    public void ConfiguracaoInvalidaFalha(string origin)
        => Assert.Throws<InvalidOperationException>(() => CsrfProtectionMiddleware.ValidarOrigem(origin));
}

public class AuthSecurityTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;
    public AuthSecurityTests(TestWebAppFactory factory) => _factory = factory;
    private HttpClient Client(TestWebAppFactory? factory = null) => (factory ?? _factory).CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    private static HttpRequestMessage Request(string path, string? cookie = null, string? origin = "https://frontend.test")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path);
        if (origin is not null) req.Headers.Add("Origin", origin);
        if (cookie is not null) req.Headers.Add("Cookie", cookie);
        return req;
    }
    private static string Cookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    private static string Token(string cookie) => Uri.UnescapeDataString(cookie[(cookie.IndexOf('=') + 1)..]);
    private async Task<(string Cookie, LoginResult Login, int Id)> Login(HttpClient client)
    {
        var email = Guid.NewGuid().ToString("N") + "@auth.test";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>();
            var user = Usuario.Criar("Auth Test", email, null, PapelUsuario.Admin);
            db.Usuarios.Add(user);
            await db.SaveChangesAsync();
            db.UsuarioCredenciais.Add(UsuarioCredencial.Criar(user.Id, new Argon2idHashSenha().Hash("test-password")));
            await db.SaveChangesAsync();
        }
        using var req = Request("/api/auth/login");
        req.Content = JsonContent.Create(new LoginInput(email, "test-password"));
        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var login = (await response.Content.ReadFromJsonAsync<LoginResult>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }))!;
        Assert.DoesNotContain(Token(Cookie(response)), body);
        var header = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("httponly", header);
        Assert.Contains("secure", header);
        Assert.Contains("samesite=lax", header);
        return (Cookie(response), login, login.UsuarioId);
    }

    [Fact]
    public async Task ConcorrenciaEntreHosts_UmVencedor_EDuplicataNaoRevoga()
    {
        using var c1 = Client();
        using var secondHost = new TestWebAppFactory();
        using var c2 = Client(secondHost);
        var login = await Login(c1);
        var responses = await Task.WhenAll(c1.SendAsync(Request("/api/auth/refresh", login.Cookie)),
            c2.SendAsync(Request("/api/auth/refresh", login.Cookie)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        var winner = responses.Single(r => r.IsSuccessStatusCode);
        using (var countScope = _factory.Services.CreateScope())
            Assert.Equal(2, await countScope.ServiceProvider.GetRequiredService<SupportAtosDbContext>().SessoesAutenticacao
                .CountAsync(s => s.UsuarioId == login.Id));
        var next = await c1.SendAsync(Request("/api/auth/refresh", Cookie(winner)));
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>();
        var original = await db.SessoesAutenticacao.SingleAsync(s => s.RefreshTokenHash == Hash(Token(login.Cookie)));
        Assert.NotNull(original.RevogadoEm);
        Assert.Equal(Hash(Token(Cookie(winner))), original.SubstituidoPorRefreshTokenHash);
    }

    [Fact]
    public async Task ReusoTardio_RevogaCadeia_MasPreservaLoginIndependente()
    {
        using var client = Client();
        var login = await Login(client);
        // Create an independent session for the same user, via the real repository.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>();
            db.SessoesAutenticacao.Add(SessaoAutenticacao.Criar(login.Id, Hash("independent-" + login.Id), DateTime.UtcNow.AddDays(1)));
            await db.SaveChangesAsync();
        }
        var first = await client.SendAsync(Request("/api/auth/refresh", login.Cookie));
        var second = await client.SendAsync(Request("/api/auth/refresh", Cookie(first)));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>();
            await db.SessoesAutenticacao.Where(s => s.RefreshTokenHash == Hash(Token(login.Cookie)))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevogadoEm, DateTime.UtcNow.AddSeconds(-10)));
        }
        var reuse = await client.SendAsync(Request("/api/auth/refresh", login.Cookie));
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Request("/api/auth/refresh", Cookie(second)))).StatusCode);
        using var check = _factory.Services.CreateScope();
        var session = await check.ServiceProvider.GetRequiredService<SupportAtosDbContext>().SessoesAutenticacao
            .SingleAsync(s => s.RefreshTokenHash == Hash("independent-" + login.Id));
        Assert.Null(session.RevogadoEm);
    }

    [Fact]
    public async Task LogoutAntigoRevogaDescendente_AccessTokenContinuaValido()
    {
        using var client = Client();
        var login = await Login(client);
        var refresh = await client.SendAsync(Request("/api/auth/refresh", login.Cookie));
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var logout = await client.SendAsync(Request("/api/auth/logout", login.Cookie));
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Contains("refresh_token=;", logout.Headers.GetValues("Set-Cookie").Single());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Request("/api/auth/refresh", Cookie(refresh)))).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Login.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/usuarios/me")).StatusCode);
    }

    [Fact]
    public async Task PapelEGrupoAtuaisDoBancoSubstituemJwtAntigo()
    {
        using var client = Client();
        var login = await Login(client);
        int groupId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>();
            var group = GrupoEmpresa.Criar("Auth group " + Guid.NewGuid());
            db.GrupoEmpresas.Add(group);
            await db.SaveChangesAsync();
            groupId = group.Id;
            await db.Usuarios.Where(u => u.Id == login.Id).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Papel, PapelUsuario.Agente).SetProperty(u => u.GrupoEmpresaId, groupId));
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Login.Token);
        var me = await client.GetAsync("/api/usuarios/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var json = System.Text.Json.JsonDocument.Parse(await me.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Agente", json.GetProperty("papel").GetString());
        Assert.Equal(groupId, json.GetProperty("grupoEmpresaId").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/usuarios", new { })).StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/refresh")]
    [InlineData("/api/auth/logout")]
    public async Task PipelineRecusaSemOrigemEPermitePreflight(string path)
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(path, origin: null))).StatusCode);
        using var preflight = new HttpRequestMessage(HttpMethod.Options, path);
        preflight.Headers.Add("Origin", "https://frontend.test");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        var response = await client.SendAsync(preflight);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("https://frontend.test", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
    }

    [Fact]
    public async Task LogoutConcorrenteComRefresh_NaoDeixaSucessorAtivo()
    {
        using var client = Client();
        var login = await Login(client);
        var responses = await Task.WhenAll(client.SendAsync(Request("/api/auth/refresh", login.Cookie)),
            client.SendAsync(Request("/api/auth/logout", login.Cookie)));
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.Contains(responses[0].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest });
        using var scope = _factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>().SessoesAutenticacao
            .AnyAsync(s => s.UsuarioId == login.Id && s.RevogadoEm == null));
    }

    [Fact]
    public async Task CredencialDesativadaInvalidaJwtERefresh()
    {
        using var client = Client();
        var login = await Login(client);
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>().UsuarioCredenciais
                .Where(c => c.UsuarioId == login.Id).ExecuteUpdateAsync(c => c.SetProperty(x => x.Ativo, false));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Login.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/usuarios/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Request("/api/auth/refresh", login.Cookie))).StatusCode);
    }

    private sealed class FalhaToken : ITokenGerador
    {
        public string GerarToken(int id, string email, string nome, PapelUsuario papel, int? grupo)
            => throw new InvalidOperationException("Falha simulada de assinatura.");
    }
    private sealed class FalhaFactory : TestWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => { s.RemoveAll<ITokenGerador>(); s.AddSingleton<ITokenGerador, FalhaToken>(); });
        }
    }
    [Fact]
    public async Task FalhaAposSalvarRotacaoFazRollbackDoConsumoEDoSucessor()
    {
        using var client = Client();
        var login = await Login(client);
        using var failingHost = new FalhaFactory();
        using var failingClient = Client(failingHost);
        Assert.Equal(HttpStatusCode.InternalServerError,
            (await failingClient.SendAsync(Request("/api/auth/refresh", login.Cookie))).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var sessions = await scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>().SessoesAutenticacao
                .Where(s => s.UsuarioId == login.Id).ToListAsync();
            var session = Assert.Single(sessions);
            Assert.Null(session.RevogadoEm);
            Assert.Null(session.SubstituidoPorRefreshTokenHash);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Request("/api/auth/refresh", login.Cookie))).StatusCode);
    }

    private sealed class CookieFactory(string sameSite, bool secure) : TestWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Auth:RefreshTokenCookieSameSite", sameSite);
            builder.UseSetting("Auth:RefreshTokenCookieSecure", secure.ToString());
        }
    }
    [Theory]
    [InlineData("None", false)]
    [InlineData("Lax", false)]
    [InlineData("invalid", true)]
    public void ConfiguracaoCookieInseguraFalhaAoIniciar(string sameSite, bool secure)
    {
        using var host = new CookieFactory(sameSite, secure);
        var ex = Assert.ThrowsAny<Exception>(() => Client(host));
        Assert.Contains("Cookie", ex.ToString());
    }

    private sealed class HashObservado : IHashSenha
    {
        public string? Recebido;
        public string Hash(string senha) => new Argon2idHashSenha().Hash(senha);
        public bool Verifica(string senha, string hash) { Recebido = hash; return new Argon2idHashSenha().Verifica(senha, hash); }
    }
    private sealed class HashFactory(HashObservado observado) : TestWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => { s.RemoveAll<IHashSenha>(); s.AddSingleton<IHashSenha>(observado); });
        }
    }
    [Fact]
    public async Task LoginInexistentePassaHashNativoParaArgonReal()
    {
        var observado = new HashObservado();
        using var factory = new HashFactory(observado);
        using var client = Client(factory);
        using var req = Request("/api/auth/login");
        req.Content = JsonContent.Create(new LoginInput(Guid.NewGuid() + "@missing.test", "unknown-password"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(req)).StatusCode);
        Assert.Equal(AuthService.HashVerificacaoInexistente, observado.Recebido);
        var parts = observado.Recebido!.Split('.');
        Assert.Equal(16, Convert.FromBase64String(parts[0]).Length);
        Assert.Equal(32, Convert.FromBase64String(parts[1]).Length);
        var real = new Argon2idHashSenha();
        Assert.True(real.Verifica("known-password", real.Hash("known-password")));
    }
}
