using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SupportAtos.Api.Authentication;
using SupportAtos.Application.Options;
using SupportAtos.Domain.Enums;

namespace SupportAtos.Integration.Tests;

public class JwtTokenGeradorTests
{
    [Fact]
    public void EmissoesConsecutivasEMultiplosEscopos_PreservamAssinaturaRs256()
    {
        using var privateKey = RSA.Create(2048);
        using var publicKey = RSA.Create();
        publicKey.ImportFromPem(privateKey.ExportSubjectPublicKeyInfoPem());
        var settings = Options.Create(new AuthSettings
        {
            PrivateKeyPem = privateKey.ExportRSAPrivateKeyPem(),
            Issuer = "jwt-regression", Audience = "jwt-regression-api", ExpiracaoMinutos = 5
        });
        var validation = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true, IssuerSigningKey = new RsaSecurityKey(publicKey),
            ValidateIssuer = true, ValidIssuer = settings.Value.Issuer,
            ValidateAudience = true, ValidAudience = settings.Value.Audience,
            ValidateLifetime = true, RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ClockSkew = TimeSpan.Zero
        };
        // Mesma chave PEM entre instâncias, como em diferentes scopes de requisição.
        for (var scope = 0; scope < 2; scope++)
        {
            var generator = new JwtTokenGerador(settings);
            for (var emission = 0; emission < 10; emission++)
            {
                var encoded = generator.GerarToken(42, "qa@example.invalid", "QA", PapelUsuario.Agente, 7);
                var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
                var principal = handler.ValidateToken(encoded, validation, out var validated);
                Assert.Equal(SecurityAlgorithms.RsaSha256, Assert.IsType<JwtSecurityToken>(validated).Header.Alg);
                Assert.Equal("42", principal.FindFirst("sub")?.Value);
                Assert.Equal("interno", principal.FindFirst("tipo")?.Value);
                Assert.Equal("7", principal.FindFirst("grupo_empresa_id")?.Value);
            }
        }
    }
}
