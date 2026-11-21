using SupportAtos.Application.Tests.Fakes;
using SupportAtos.Application.UseCases;
using SupportAtos.Application.Exceptions;
using SupportAtos.Domain.Entities;
using SupportAtos.Domain.Enums;

namespace SupportAtos.Application.Tests;

public class ListarFuncionariosTests
{
    [Theory]
    [InlineData(PapelUsuario.Agente)]
    [InlineData(PapelUsuario.Supervisor)]
    [InlineData(PapelUsuario.Cliente)]
    public async Task RecusaOutrosPapeis(PapelUsuario papel)
    {
        var user = new FakeCurrentUser { Usuario = FakeCurrentUser.ContextoPadrao with { Papel = papel } };
        await Assert.ThrowsAsync<ForbiddenException>(() => new ListarFuncionariosUseCase(new FakeUsuarioRepository(), user).Handle());
    }

    [Fact]
    public async Task ListaInternosComESemGrupoEEstadoRealDaCredencial()
    {
        var repo = new FakeUsuarioRepository();
        var papeis = new[] { PapelUsuario.Admin, PapelUsuario.Supervisor, PapelUsuario.Agente };
        for (var i = 0; i < 6; i++)
        {
            var u = Usuario.Criar("Mesmo nome", $"p{i}@test.invalid", i % 2 == 0 ? null : 7, papeis[i % 3]);
            await repo.AddAsync(u);
            if (i == 1) u.SetForTest("Credencial", UsuarioCredencial.Criar(u.Id, "synthetic-hash"));
            if (i == 2) {
                var c = UsuarioCredencial.Criar(u.Id, "synthetic-hash"); c.Desativar(); u.SetForTest("Credencial", c);
            }
            if (i == 3) {
                var c = UsuarioCredencial.CriarPendente(u.Id, new string('a', 64), DateTime.UtcNow.AddHours(1));
                c.SetForTest("TokenAtivacaoExpiraEm", DateTime.UtcNow.AddHours(-1)); u.SetForTest("Credencial", c);
            }
        }
        await repo.AddAsync(Usuario.Criar("Cliente", "cliente@test.invalid", 7, PapelUsuario.Cliente));
        var result = await new ListarFuncionariosUseCase(repo, new FakeCurrentUser { Usuario = FakeCurrentUser.ContextoPadrao with { Papel = PapelUsuario.Admin } }).Handle();
        Assert.Equal(6, result.Count);
        Assert.Equal(Enumerable.Range(1,6), result.Select(x => x.Id));
        Assert.Equal(new[] { "Pendente", "Ativa", "Inativa", "Pendente", "Pendente", "Pendente" }, result.Select(x => x.Situacao));
    }
}
