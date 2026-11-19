using SupportAtos.Domain.Entities;
using SupportAtos.Domain.Exceptions;

namespace SupportAtos.Application.Tests;

public class AtivacaoCredencialTests
{
    [Fact]
    public void ConviteCriaCredencialInativaSemSenha()
    {
        var credencial = UsuarioCredencial.CriarPendente(1, new string('a', 64), DateTime.UtcNow.AddHours(1));
        Assert.False(credencial.Ativo);
        Assert.Empty(credencial.SenhaHash);
        Assert.Null(credencial.AtivacaoConsumidaEm);
        Assert.Throws<DomainException>(() => credencial.Reativar());
    }

    [Fact]
    public void AtivacaoDefineSenhaEConsomeConviteUmaUnicaVez()
    {
        var credencial = UsuarioCredencial.CriarPendente(1, new string('a', 64), DateTime.UtcNow.AddHours(1));
        credencial.AtivarComPrimeiraSenha("hash-real-gerado-pelo-servico");
        Assert.True(credencial.Ativo);
        Assert.NotNull(credencial.AtivacaoConsumidaEm);
        Assert.Equal("hash-real-gerado-pelo-servico", credencial.SenhaHash);
        Assert.Throws<DomainException>(() => credencial.AtivarComPrimeiraSenha("outra-senha"));
    }

    [Fact]
    public void ConviteExpiradoNaoAtiva()
    {
        var credencial = UsuarioCredencial.CriarPendente(1, new string('a', 64), DateTime.UtcNow.AddHours(1));
        typeof(UsuarioCredencial).GetProperty(nameof(UsuarioCredencial.TokenAtivacaoExpiraEm))!
            .SetValue(credencial, DateTime.UtcNow.AddSeconds(-1));
        Assert.Throws<DomainException>(() => credencial.AtivarComPrimeiraSenha("hash"));
        Assert.False(credencial.Ativo);
        Assert.Empty(credencial.SenhaHash);
    }

    [Fact]
    public void ConviteExpiradoNaoPodeSerCriado()
        => Assert.Throws<DomainException>(() => UsuarioCredencial.CriarPendente(1, new string('a', 64), DateTime.UtcNow.AddSeconds(-1)));
}
