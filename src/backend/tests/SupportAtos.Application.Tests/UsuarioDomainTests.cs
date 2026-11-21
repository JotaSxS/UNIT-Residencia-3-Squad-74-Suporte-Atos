using SupportAtos.Domain.Entities;
using SupportAtos.Domain.Enums;
using SupportAtos.Domain.Exceptions;

namespace SupportAtos.Application.Tests;

public class UsuarioDomainTests
{
    [Fact]
    public void Criar_ClienteSemGrupo_DeveLancarDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            Usuario.Criar("Cliente", "cliente@demo.com", null, PapelUsuario.Cliente));

        Assert.Contains("grupo de empresa", ex.Message);
    }

    [Fact]
    public void Criar_ClienteComGrupoInvalido_DeveLancarDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            Usuario.Criar("Cliente", "cliente@demo.com", 0, PapelUsuario.Cliente));

        Assert.Contains("grupo de empresa", ex.Message);
    }

    [Theory]
    [InlineData(PapelUsuario.Admin)]
    [InlineData(PapelUsuario.Supervisor)]
    [InlineData(PapelUsuario.Agente)]
    public void Criar_FuncionarioInternoSemGrupo_DeveSucesso(PapelUsuario papel)
    {
        var usuario = Usuario.Criar("Funcionario", "func@demo.com", null, papel);

        Assert.Null(usuario.GrupoEmpresaId);
        Assert.Equal(papel, usuario.Papel);
    }

    [Theory]
    [InlineData(PapelUsuario.Admin)]
    [InlineData(PapelUsuario.Supervisor)]
    [InlineData(PapelUsuario.Agente)]
    public void Criar_FuncionarioInternoComGrupo_DeveSucesso(PapelUsuario papel)
    {
        var usuario = Usuario.Criar("Funcionario", "func@demo.com", 1, papel);

        Assert.Equal(1, usuario.GrupoEmpresaId);
        Assert.Equal(papel, usuario.Papel);
    }

    [Fact]
    public void AlterarPapel_ClienteSemGrupo_DeveLancarDomainException()
    {
        var usuario = Usuario.Criar("Agente", "agente@demo.com", null, PapelUsuario.Agente);

        var ex = Assert.Throws<DomainException>(() => usuario.AlterarPapel(PapelUsuario.Cliente));

        Assert.Contains("grupo de empresa", ex.Message);
        Assert.Equal(PapelUsuario.Agente, usuario.Papel); // papel não alterado
    }

    [Fact]
    public void AlterarPapel_ClienteComGrupo_DeveSucesso()
    {
        var usuario = Usuario.Criar("Agente", "agente@demo.com", 1, PapelUsuario.Agente);

        usuario.AlterarPapel(PapelUsuario.Cliente);

        Assert.Equal(PapelUsuario.Cliente, usuario.Papel);
        Assert.Equal(1, usuario.GrupoEmpresaId);
    }

    [Fact]
    public void AlterarPapel_EntreInternos_DeveSucesso()
    {
        var usuario = Usuario.Criar("Agente", "agente@demo.com", null, PapelUsuario.Agente);

        usuario.AlterarPapel(PapelUsuario.Admin);

        Assert.Equal(PapelUsuario.Admin, usuario.Papel);
        Assert.Null(usuario.GrupoEmpresaId);
    }

    [Fact]
    public void AlterarGrupoEmpresa_ClienteSemGrupo_DeveLancarDomainException()
    {
        var usuario = Usuario.Criar("Cliente", "cliente@demo.com", 1, PapelUsuario.Cliente);

        var ex = Assert.Throws<DomainException>(() => usuario.AlterarGrupoEmpresa(null));

        Assert.Contains("grupo de empresa", ex.Message);
        Assert.Equal(1, usuario.GrupoEmpresaId);
    }

    [Fact]
    public void AlterarGrupoEmpresa_FuncionarioInternoRemoveGrupo_DeveSucesso()
    {
        var usuario = Usuario.Criar("Agente", "agente@demo.com", 1, PapelUsuario.Agente);

        usuario.AlterarGrupoEmpresa(null);

        Assert.Null(usuario.GrupoEmpresaId);
    }
}
