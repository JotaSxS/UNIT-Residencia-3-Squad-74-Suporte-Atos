using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SupportAtos.Application;
using SupportAtos.Application.Abstractions;
using SupportAtos.Application.DTOs;
using SupportAtos.Application.Exceptions;
using SupportAtos.Application.Ports;
using SupportAtos.Application.Services;
using SupportAtos.Domain.Entities;
using SupportAtos.Domain.Enums;
using SupportAtos.Infrastructure;
using SupportAtos.Infrastructure.Persistence;
using SupportAtos.Infrastructure.Security;

namespace SupportAtos.Integration.Tests;

public class BootstrapAdminTests
{
    private sealed class SemUsuario : ICurrentUser
    {
        public bool IsAutenticado => false;
        public PortalUserContext UsuarioAtual => throw new InvalidOperationException("Sem identidade HTTP.");
    }

    private sealed class HashComFalha : IHashSenha
    {
        public string Hash(string senha) => throw new InvalidOperationException("Falha simulada de Argon2.");
        public bool Verifica(string senha, string hash) => false;
    }

    private static async Task<ServiceProvider> BancoExclusivo(bool falharHash = false)
    {
        var name = "supportatos_test_bootstrap_" + Guid.NewGuid().ToString("N");
        var settings = new NpgsqlConnectionStringBuilder(TestWebAppFactory.ConexaoBanco) { Database = "postgres" };
        await using (var connection = new NpgsqlConnection(settings.ConnectionString))
        {
            await connection.OpenAsync();
            // Nome gerado exclusivamente pelo teste; não aceita identificador externo.
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await create.ExecuteNonQueryAsync();
        }
        settings.Database = name;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = settings.ConnectionString }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddSingleton<ICurrentUser, SemUsuario>();
        services.AddScoped<IHashSenha, Argon2idHashSenha>();
        if (falharHash) services.AddScoped<IHashSenha, HashComFalha>();
        services.AddScoped<IRefreshTokenGerador, RefreshTokenGerador>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>().Database.MigrateAsync();
        return provider;
    }

    [Fact]
    public async Task BootstrapConcorrente_UmAdmin_ConsomeMarcadorMesmoAposExcluirConta()
    {
        await using var provider = await BancoExclusivo();
        async Task<bool> Attempt(string email)
        {
            using var scope = provider.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<FuncionarioAutenticacaoService>()
                    .BootstrapAsync(new("Primeiro Admin", email, "Frase bootstrap para testes 2026"));
                return true;
            }
            catch (ConflictException) { return false; }
        }
        var attempts = await Task.WhenAll(Attempt("first@bootstrap.test"), Attempt("second@bootstrap.test"));
        Assert.Single(attempts, success => success);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>();
            var admin = await db.Usuarios.Include(u => u.Credencial).SingleAsync();
            Assert.Equal(PapelUsuario.Admin, admin.Papel);
            Assert.Null(admin.GrupoEmpresaId);
            Assert.True(admin.Credencial!.Ativo);
            Assert.True(new Argon2idHashSenha().Verifica("Frase bootstrap para testes 2026", admin.Credencial.SenhaHash));
            Assert.Equal(1, await db.Set<AutenticacaoBootstrap>().CountAsync());
            db.Remove(admin);
            await db.SaveChangesAsync();
        }
        Assert.False(await Attempt("third@bootstrap.test"));
    }

    [Fact]
    public async Task BootstrapInvalidoNaoConsomeMarcadorENaoDeixaUsuario()
    {
        await using var provider = await BancoExclusivo();
        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<FuncionarioAutenticacaoService>();
            await Assert.ThrowsAsync<ValidationException>(() => service.BootstrapAsync(new("Admin", "admin@bootstrap.test", "curta")));
            // Falha de validação do domínio também não pode consumir o marcador.
            await Assert.ThrowsAnyAsync<Exception>(() => service.BootstrapAsync(new("Admin", "email-invalido", "Frase bootstrap para testes 2026")));
        }
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>();
            Assert.False(await db.Usuarios.AnyAsync());
            Assert.False(await db.Set<AutenticacaoBootstrap>().AnyAsync());
            await scope.ServiceProvider.GetRequiredService<FuncionarioAutenticacaoService>()
                .BootstrapAsync(new("Admin", "valid@bootstrap.test", "Frase bootstrap para testes 2026"));
        }
    }

    [Fact]
    public async Task AdminExistenteImpedeBootstrapSemAlterarContaAnterior()
    {
        await using var provider = await BancoExclusivo();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>();
            var admin = Usuario.Criar("Admin existente", "exists@bootstrap.test", null, PapelUsuario.Admin);
            admin.AtribuirCodigoExterno("portal-preservado");
            db.Add(admin);
            await db.SaveChangesAsync();
        }
        using (var scope = provider.CreateScope())
            await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider.GetRequiredService<FuncionarioAutenticacaoService>()
                .BootstrapAsync(new("Novo Admin", "new@bootstrap.test", "Frase bootstrap para testes 2026")));
        using var check = provider.CreateScope();
        var dbCheck = check.ServiceProvider.GetRequiredService<SupportAtosDbContext>();
        var existing = await dbCheck.Usuarios.Include(u => u.Credencial).SingleAsync();
        Assert.Equal("portal-preservado", existing.CodigoExterno);
        Assert.Null(existing.Credencial);
        Assert.False(await dbCheck.Set<AutenticacaoBootstrap>().AnyAsync());
    }

    [Fact]
    public async Task ServicoDeCadastroRecusaChamadorSemAdmin()
    {
        await using var provider = await BancoExclusivo();
        using var scope = provider.CreateScope();
        await Assert.ThrowsAsync<ForbiddenException>(() => scope.ServiceProvider.GetRequiredService<FuncionarioAutenticacaoService>()
            .CadastrarAsync(new("Novo", "new@bootstrap.test", null, PapelUsuario.Admin)));
        Assert.False(await scope.ServiceProvider.GetRequiredService<SupportAtosDbContext>().Usuarios.AnyAsync());
    }

    [Fact]
    public async Task FalhaAposPersistirAdminFazRollbackENaoConsomeBootstrap()
    {
        await using var provider = await BancoExclusivo(falharHash: true);
        using (var scope = provider.CreateScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<FuncionarioAutenticacaoService>()
                .BootstrapAsync(new("Admin", "rollback@bootstrap.test", "Frase bootstrap para testes 2026")));
        using var check = provider.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<SupportAtosDbContext>();
        Assert.False(await db.Usuarios.AnyAsync());
        Assert.False(await db.UsuarioCredenciais.AnyAsync());
        Assert.False(await db.Set<AutenticacaoBootstrap>().AnyAsync());
    }
}
