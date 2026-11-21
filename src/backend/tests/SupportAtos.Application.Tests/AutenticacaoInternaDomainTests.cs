using SupportAtos.Domain.Entities;
using SupportAtos.Domain.Exceptions;

namespace SupportAtos.Application.Tests;

public class UsuarioCredencialDomainTests
{
    [Fact]
    public void Criar_Valido_DeveSucesso()
    {
        var credencial = UsuarioCredencial.Criar(1, "hash_abc123");

        Assert.Equal(1, credencial.UsuarioId);
        Assert.Equal("hash_abc123", credencial.SenhaHash);
        Assert.True(credencial.Ativo);
        Assert.Equal(0, credencial.TentativasFalhas);
        Assert.Null(credencial.BloqueadoAte);
    }

    [Fact]
    public void Criar_UsuarioInvalido_DeveLancarDomainException()
    {
        var ex = Assert.Throws<DomainException>(() => UsuarioCredencial.Criar(0, "hash"));

        Assert.Contains("usuário", ex.Message);
    }

    [Fact]
    public void Criar_SenhaHashVazia_DeveLancarDomainException()
    {
        var ex = Assert.Throws<DomainException>(() => UsuarioCredencial.Criar(1, ""));

        Assert.Contains("hash de senha", ex.Message);
    }

    [Fact]
    public void AlterarSenha_Valida_DeveAtualizarELimparFalhas()
    {
        var credencial = UsuarioCredencial.Criar(1, "hash_velho");
        credencial.RegistrarFalha();
        credencial.RegistrarFalha();
        credencial.Bloquear(DateTime.UtcNow.AddHours(1));

        credencial.AlterarSenha("hash_novo");

        Assert.Equal("hash_novo", credencial.SenhaHash);
        Assert.Equal(0, credencial.TentativasFalhas);
        Assert.Null(credencial.BloqueadoAte);
        Assert.NotNull(credencial.DataUltimaAlteracao);
    }

    [Fact]
    public void AlterarSenha_Vazia_DeveLancarDomainException()
    {
        var credencial = UsuarioCredencial.Criar(1, "hash_velho");

        var ex = Assert.Throws<DomainException>(() => credencial.AlterarSenha(""));

        Assert.Contains("hash de senha", ex.Message);
    }

    [Fact]
    public void RegistrarFalha_DeveIncrementarContador()
    {
        var credencial = UsuarioCredencial.Criar(1, "hash");

        credencial.RegistrarFalha();
        credencial.RegistrarFalha();

        Assert.Equal(2, credencial.TentativasFalhas);
    }

    [Fact]
    public void LimparFalhas_DeveZerarContadorEBloqueio()
    {
        var credencial = UsuarioCredencial.Criar(1, "hash");
        credencial.RegistrarFalha();
        credencial.Bloquear(DateTime.UtcNow.AddHours(1));

        credencial.LimparFalhas();

        Assert.Equal(0, credencial.TentativasFalhas);
        Assert.Null(credencial.BloqueadoAte);
    }

    [Fact]
    public void Bloquear_DeveDefinirBloqueadoAte()
    {
        var credencial = UsuarioCredencial.Criar(1, "hash");
        var ate = DateTime.UtcNow.AddHours(1);

        credencial.Bloquear(ate);

        Assert.Equal(ate, credencial.BloqueadoAte);
        Assert.True(credencial.EstaBloqueado());
    }

    [Fact]
    public void EstaBloqueado_SemBloqueio_DeveRetornarFalso()
    {
        var credencial = UsuarioCredencial.Criar(1, "hash");

        Assert.False(credencial.EstaBloqueado());
    }

    [Fact]
    public void Desativar_Reativar_DeveAlterarAtivo()
    {
        var credencial = UsuarioCredencial.Criar(1, "hash");

        credencial.Desativar();
        Assert.False(credencial.Ativo);

        credencial.Reativar();
        Assert.True(credencial.Ativo);
    }
}

public class SessaoAutenticacaoDomainTests
{
    [Fact]
    public void Criar_Valido_DeveSucesso()
    {
        var expiracao = DateTime.UtcNow.AddHours(1);
        var sessao = SessaoAutenticacao.Criar(1, "refresh_hash", expiracao, "127.0.0.1", "Mozilla/5.0");

        Assert.Equal(1, sessao.UsuarioId);
        Assert.Equal("refresh_hash", sessao.RefreshTokenHash);
        Assert.Equal(expiracao, sessao.RefreshTokenExpiracao);
        Assert.Equal("127.0.0.1", sessao.EnderecoIp);
        Assert.Equal("Mozilla/5.0", sessao.UserAgent);
        Assert.Null(sessao.RevogadoEm);
        Assert.Null(sessao.SubstituidoPorRefreshTokenHash);
    }

    [Fact]
    public void Criar_UsuarioInvalido_DeveLancarDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            SessaoAutenticacao.Criar(0, "hash", DateTime.UtcNow.AddHours(1)));

        Assert.Contains("usuário", ex.Message);
    }

    [Fact]
    public void Criar_RefreshTokenVazio_DeveLancarDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            SessaoAutenticacao.Criar(1, "", DateTime.UtcNow.AddHours(1)));

        Assert.Contains("refresh token", ex.Message);
    }

    [Fact]
    public void Criar_ExpiracaoNoPassado_DeveLancarDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            SessaoAutenticacao.Criar(1, "hash", DateTime.UtcNow.AddMinutes(-1)));

        Assert.Contains("futura", ex.Message);
    }

    [Fact]
    public void Revogar_DeveDefinirRevogadoEm()
    {
        var sessao = SessaoAutenticacao.Criar(1, "hash", DateTime.UtcNow.AddHours(1));

        sessao.Revogar();

        Assert.NotNull(sessao.RevogadoEm);
        Assert.True(sessao.EstaRevogado());
    }

    [Fact]
    public void MarcarComoSubstituido_Valido_DeveDefinirHash()
    {
        var sessao = SessaoAutenticacao.Criar(1, "hash_velho", DateTime.UtcNow.AddHours(1));

        sessao.MarcarComoSubstituido("hash_novo");

        Assert.Equal("hash_novo", sessao.SubstituidoPorRefreshTokenHash);
    }

    [Fact]
    public void MarcarComoSubstituido_HashVazio_DeveLancarDomainException()
    {
        var sessao = SessaoAutenticacao.Criar(1, "hash", DateTime.UtcNow.AddHours(1));

        var ex = Assert.Throws<DomainException>(() => sessao.MarcarComoSubstituido(""));

        Assert.Contains("refresh token", ex.Message);
    }

    [Fact]
    public void EstaExpirado_ExpiracaoFutura_DeveRetornarFalso()
    {
        var sessao = SessaoAutenticacao.Criar(1, "hash", DateTime.UtcNow.AddHours(1));

        Assert.False(sessao.EstaExpirado());
    }

    [Fact]
    public void EstaRevogado_SemRevogacao_DeveRetornarFalso()
    {
        var sessao = SessaoAutenticacao.Criar(1, "hash", DateTime.UtcNow.AddHours(1));

        Assert.False(sessao.EstaRevogado());
    }
}
