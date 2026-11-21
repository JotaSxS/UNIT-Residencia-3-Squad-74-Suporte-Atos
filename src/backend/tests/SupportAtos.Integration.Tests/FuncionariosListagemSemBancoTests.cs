using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SupportAtos.Application.Ports;
using SupportAtos.Domain.Entities;
using SupportAtos.Domain.Enums;

namespace SupportAtos.Integration.Tests;

// JWT real e pipeline HTTP real; apenas repositorio substituido. Nenhuma conexao ao banco.
public class FuncionariosListagemSemBancoTests
{
    public class Repo : DispatchProxy
    {
        public PapelUsuario Papel;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var u = Usuario.Criar("Interno", "interno@test.invalid", Papel == PapelUsuario.Cliente ? 7 : null, Papel);
            typeof(Usuario).GetProperty("Id")!.SetValue(u, 1);
            typeof(Usuario).GetProperty("Credencial")!.SetValue(u, UsuarioCredencial.Criar(1, "synthetic-hash"));
            if (method!.Name == "GetByIdAsync") return Task.FromResult<Usuario?>(u);
            if (method.Name == "ListarFuncionariosAsync") return Task.FromResult<IReadOnlyList<Usuario>>([u]);
            throw new InvalidOperationException("Consulta inesperada: " + method.Name);
        }
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(PapelUsuario.Cliente, HttpStatusCode.Unauthorized)]
    [InlineData(PapelUsuario.Agente, HttpStatusCode.Forbidden)]
    [InlineData(PapelUsuario.Supervisor, HttpStatusCode.Forbidden)]
    [InlineData(PapelUsuario.Admin, HttpStatusCode.OK)]
    public async Task EndpointExigeAdmin(PapelUsuario? papel, HttpStatusCode esperado)
    {
        var repo = DispatchProxy.Create<IUsuarioRepository, Repo>();
        ((Repo)repo).Papel = papel ?? PapelUsuario.Admin;
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=127.0.0.1;Port=1;Database=never_used;Username=synthetic;Password=synthetic");
            builder.UseSetting("Sso:Issuer", TestTokenFactory.Issuer);
            builder.UseSetting("Sso:Audience", TestTokenFactory.Audience);
            builder.UseSetting("Sso:PublicKeyPem", TestTokenFactory.ChavePublicaPem);
            builder.UseSetting("Auth:Issuer", TestTokenFactory.Issuer);
            builder.UseSetting("Auth:Audience", TestTokenFactory.Audience);
            builder.UseSetting("Auth:PrivateKeyPem", TestTokenFactory.ChavePrivadaPem);
            builder.ConfigureServices(s => { s.RemoveAll<IUsuarioRepository>(); s.AddSingleton(repo); });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (papel.HasValue) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.Services.GetRequiredService<ITokenGerador>().GerarToken(1, "interno@test.invalid", "Interno", papel.Value, null));
        var response = await client.GetAsync("/api/usuarios/funcionarios");
        Assert.Equal(esperado, response.StatusCode);
        if (esperado == HttpStatusCode.OK) {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Ativa", body); Assert.Contains("Interno", body);
            Assert.DoesNotContain("synthetic-hash", body);
            Assert.DoesNotContain("tokenAtivacao", body);
        }
    }
}
