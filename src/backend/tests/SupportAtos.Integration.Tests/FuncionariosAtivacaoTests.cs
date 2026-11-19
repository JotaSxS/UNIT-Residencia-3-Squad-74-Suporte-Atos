using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SupportAtos.Application.DTOs;
using SupportAtos.Application.Ports;
using SupportAtos.Domain.Entities;
using SupportAtos.Domain.Enums;
using SupportAtos.Infrastructure.Persistence;
using SupportAtos.Infrastructure.Security;

namespace SupportAtos.Integration.Tests;

public class FuncionariosAtivacaoTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    private const string Senha = "Frase de teste fase tres 2026";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };
    private HttpClient Client(TestWebAppFactory? host = null)
    {
        var client = (host ?? factory).CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        client.DefaultRequestHeaders.Add("Origin", "https://frontend.test");
        return client;
    }
    private static string Email() => Guid.NewGuid().ToString("N") + "@phase3.test";
    private async Task<string> UsuarioAutenticado(PapelUsuario papel)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>();
        int? grupoId = null;
        if (papel == PapelUsuario.Cliente)
        {
            var grupo = GrupoEmpresa.Criar("Grupo teste fase 3 " + Guid.NewGuid());
            db.Add(grupo);
            await db.SaveChangesAsync();
            grupoId = grupo.Id;
        }
        var user = Usuario.Criar("Funcionário teste", Email(), grupoId, papel);
        db.Add(user);
        await db.SaveChangesAsync();
        // Cenário negativo também verifica que JWT interno de cliente é rejeitado.
        db.Add(UsuarioCredencial.Criar(user.Id, new Argon2idHashSenha().Hash(Senha)));
        await db.SaveChangesAsync();
        return scope.ServiceProvider.GetRequiredService<ITokenGerador>()
            .GerarToken(user.Id, user.Email, user.Nome, user.Papel, user.GrupoEmpresaId);
    }
    private async Task<ConviteFuncionarioResult> Convidar(HttpClient client, PapelUsuario papel = PapelUsuario.Agente, string? email = null)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await UsuarioAutenticado(PapelUsuario.Admin));
        var response = await client.PostAsJsonAsync("/api/usuarios", new CadastrarUsuarioInput("Novo funcionário", email ?? Email(), null, papel));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var responseJson = await response.Content.ReadAsStringAsync();
        var convite = JsonSerializer.Deserialize<ConviteFuncionarioResult>(responseJson, Json)!;
        var dtoAnterior = JsonSerializer.Deserialize<UsuarioDto>(responseJson, Json)!;
        Assert.Equal(convite.Id, dtoAnterior.Id);
        return convite;
    }

    [Theory]
    [InlineData(PapelUsuario.Admin)]
    [InlineData(PapelUsuario.Supervisor)]
    [InlineData(PapelUsuario.Agente)]
    public async Task AdminCadastraInativo_AtivaComArgon_EPermiteLogin(PapelUsuario papel)
    {
        using var admin = Client();
        var convite = await Convidar(admin, papel);
        var consulta = await admin.GetAsync("/api/usuarios/" + convite.Id);
        Assert.Equal(HttpStatusCode.OK, consulta.StatusCode);
        var consultaJson = await consulta.Content.ReadAsStringAsync();
        Assert.DoesNotContain(convite.TokenAtivacao, consultaJson);
        Assert.DoesNotContain("tokenAtivacao", consultaJson);
        using (var scope = factory.Services.CreateScope())
        {
            var c = await scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>().UsuarioCredenciais.SingleAsync(c => c.UsuarioId == convite.Id);
            Assert.False(c.Ativo);
            Assert.Empty(c.SenhaHash);
            Assert.NotEqual(convite.TokenAtivacao, c.TokenAtivacaoHash);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(convite.TokenAtivacao))).ToLowerInvariant(), c.TokenAtivacaoHash);
            Assert.InRange(convite.ExpiraEm - DateTime.UtcNow, TimeSpan.FromHours(23), TimeSpan.FromHours(24));
        }
        using var employee = Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await employee.PostAsJsonAsync("/api/auth/login", new LoginInput(convite.Email, Senha))).StatusCode);
        var activation = await employee.PostAsJsonAsync("/api/auth/ativar", new AtivarFuncionarioInput(convite.TokenAtivacao, Senha));
        Assert.Equal(HttpStatusCode.NoContent, activation.StatusCode);
        Assert.True(activation.Headers.CacheControl?.NoStore);
        Assert.False(activation.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.OK, (await employee.PostAsJsonAsync("/api/auth/login", new LoginInput(convite.Email, Senha))).StatusCode);
        using var check = factory.Services.CreateScope();
        var credential = await check.ServiceProvider.GetRequiredService<SupportAtosDbContext>().UsuarioCredenciais.SingleAsync(c => c.UsuarioId == convite.Id);
        Assert.True(credential.Ativo);
        Assert.NotNull(credential.AtivacaoConsumidaEm);
        Assert.True(new Argon2idHashSenha().Verifica(Senha, credential.SenhaHash));
        Assert.DoesNotContain(Senha, credential.SenhaHash);
    }

    [Theory]
    [InlineData(PapelUsuario.Supervisor, HttpStatusCode.Forbidden)]
    [InlineData(PapelUsuario.Agente, HttpStatusCode.Forbidden)]
    [InlineData(PapelUsuario.Cliente, HttpStatusCode.Unauthorized)]
    public async Task OutrosPapeisNaoCadastram(PapelUsuario papel, HttpStatusCode status)
    {
        using var client = Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await UsuarioAutenticado(papel));
        var response = await client.PostAsJsonAsync("/api/usuarios", new CadastrarUsuarioInput("Sem permissão", Email(), null, PapelUsuario.Admin));
        Assert.Equal(status, response.StatusCode);
    }

    [Fact]
    public async Task AnonimoEApiKeyNaoCadastram()
    {
        using var client = Client();
        var input = new CadastrarUsuarioInput("Sem permissão", Email(), null, PapelUsuario.Admin);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/usuarios", input)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Api-Key", TestTokenFactory.PortalApiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/usuarios", input)).StatusCode);
    }

    [Fact]
    public async Task ClienteContinuaSemCredencialEPortalExistenteNaoEConvertido()
    {
        using var client = Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await UsuarioAutenticado(PapelUsuario.Admin));
        int grupoId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>();
            var group = GrupoEmpresa.Criar("Cliente preservado " + Guid.NewGuid());
            db.Add(group);
            await db.SaveChangesAsync();
            grupoId = group.Id;
        }
        var email = Email();
        var response = await client.PostAsJsonAsync("/api/usuarios", new CadastrarUsuarioInput("Cliente", email, grupoId, PapelUsuario.Cliente));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var usuario = (await response.Content.ReadFromJsonAsync<UsuarioDto>(Json))!;
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>().Usuarios.Where(u => u.Id == usuario.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.CodigoExterno, "portal-preservado-" + usuario.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/usuarios", new CadastrarUsuarioInput("Tentativa", email, null, PapelUsuario.Admin))).StatusCode);
        using var check = factory.Services.CreateScope();
        var dbCheck = check.ServiceProvider.GetRequiredService<SupportAtosDbContext>();
        var stored = await dbCheck.Usuarios.Include(u => u.Credencial).SingleAsync(u => u.Id == usuario.Id);
        Assert.Equal(PapelUsuario.Cliente, stored.Papel);
        Assert.Equal(grupoId, stored.GrupoEmpresaId);
        Assert.Equal("portal-preservado-" + usuario.Id, stored.CodigoExterno);
        Assert.Null(stored.Credencial);
    }

    [Theory]
    [InlineData("expirado")]
    [InlineData("cliente")]
    [InlineData("portal")]
    public async Task ConviteInvalidoNaoAtiva(string tipo)
    {
        using var admin = Client();
        var convite = await Convidar(admin);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>();
            if (tipo == "expirado")
                await db.UsuarioCredenciais.Where(c => c.UsuarioId == convite.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.TokenAtivacaoExpiraEm, DateTime.UtcNow.AddSeconds(-1)));
            else if (tipo == "portal")
                await db.Usuarios.Where(u => u.Id == convite.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.CodigoExterno, "portal-" + Guid.NewGuid()));
            else
            {
                var grupo = GrupoEmpresa.Criar("Grupo alteração " + Guid.NewGuid());
                db.Add(grupo); await db.SaveChangesAsync();
                await db.Usuarios.Where(u => u.Id == convite.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.GrupoEmpresaId, grupo.Id).SetProperty(u => u.Papel, PapelUsuario.Cliente));
            }
        }
        using var client = Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/ativar", new AtivarFuncionarioInput(convite.TokenAtivacao, Senha))).StatusCode);
        using var check = factory.Services.CreateScope();
        var stored = await check.ServiceProvider.GetRequiredService<SupportAtosDbContext>().UsuarioCredenciais.SingleAsync(c => c.UsuarioId == convite.Id);
        Assert.False(stored.Ativo);
        Assert.Empty(stored.SenhaHash);
    }

    [Fact]
    public async Task ReusoNaoAlteraSenhaNemReativaCredencialDesativada()
    {
        using var admin = Client();
        var convite = await Convidar(admin);
        using var employee = Client();
        Assert.Equal(HttpStatusCode.NoContent, (await employee.PostAsJsonAsync("/api/auth/ativar", new AtivarFuncionarioInput(convite.TokenAtivacao, Senha))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await employee.PostAsJsonAsync("/api/auth/ativar", new AtivarFuncionarioInput(convite.TokenAtivacao, Senha + "nova"))).StatusCode);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>().UsuarioCredenciais.Where(c => c.UsuarioId == convite.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Ativo, false));
        Assert.Equal(HttpStatusCode.BadRequest, (await employee.PostAsJsonAsync("/api/auth/ativar", new AtivarFuncionarioInput(convite.TokenAtivacao, Senha + "nova"))).StatusCode);
    }

    [Fact]
    public async Task AtivacoesConcorrentesEntreHosts_ApenasUmaSenhaVence()
    {
        using var admin = Client();
        var convite = await Convidar(admin);
        using var secondHost = new TestWebAppFactory();
        using var a = Client(); using var b = Client(secondHost);
        var responses = await Task.WhenAll(
            a.PostAsJsonAsync("/api/auth/ativar", new AtivarFuncionarioInput(convite.TokenAtivacao, Senha)),
            b.PostAsJsonAsync("/api/auth/ativar", new AtivarFuncionarioInput(convite.TokenAtivacao, Senha + "segunda")));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>().UsuarioCredenciais.SingleAsync(c => c.UsuarioId == convite.Id);
        Assert.True(new Argon2idHashSenha().Verifica(responses[0].IsSuccessStatusCode ? Senha : Senha + "segunda", stored.SenhaHash));
    }

    [Fact]
    public async Task CadastrosConcorrentesMesmoEmail_UmUsuarioEUmaCredencial()
    {
        using var client = Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await UsuarioAutenticado(PapelUsuario.Admin));
        var email = Email();
        var input = new CadastrarUsuarioInput("Concorrente", email, null, PapelUsuario.Supervisor);
        var responses = await Task.WhenAll(client.PostAsJsonAsync("/api/usuarios", input), client.PostAsJsonAsync("/api/usuarios", input));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>();
        var user = await db.Usuarios.SingleAsync(u => u.Email == email);
        Assert.Equal(1, await db.UsuarioCredenciais.CountAsync(c => c.UsuarioId == user.Id));
    }

    [Theory]
    [InlineData("curta")]
    [InlineData("")]
    public async Task SenhaInvalidaNaoConsomeConvite(string senha)
    {
        using var admin = Client();
        var convite = await Convidar(admin);
        using var client = Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/ativar", new AtivarFuncionarioInput(convite.TokenAtivacao, senha))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/ativar", new AtivarFuncionarioInput(convite.TokenAtivacao, Senha))).StatusCode);
    }

    [Fact]
    public async Task TokenDesconhecidoSemOrigemEBootstrapHttpNaoSaoAceitos()
    {
        using var client = Client();
        var input = new AtivarFuncionarioInput(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), Senha);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/ativar", input)).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/auth/ativar", input)).StatusCode);
        // Não há endpoint de bootstrap. A política global pode responder 401 antes do 404.
        Assert.NotEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/bootstrap", new { })).StatusCode);
    }

    private sealed class TokenComFalha : IRefreshTokenGerador
    {
        public string GerarToken() => throw new InvalidOperationException("Falha simulada do gerador.");
    }
    private sealed class CadastroComFalhaFactory : TestWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRefreshTokenGerador>();
                services.AddSingleton<IRefreshTokenGerador, TokenComFalha>();
            });
        }
    }
    [Fact]
    public async Task FalhaAoGerarConviteNaoDeixaFuncionarioOrfao()
    {
        using var host = new CadastroComFalhaFactory();
        using var client = Client(host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await UsuarioAutenticado(PapelUsuario.Admin));
        var email = Email();
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync("/api/usuarios",
            new CadastrarUsuarioInput("Rollback", email, null, PapelUsuario.Admin))).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>().Usuarios.AnyAsync(u => u.Email == email));
    }
}
