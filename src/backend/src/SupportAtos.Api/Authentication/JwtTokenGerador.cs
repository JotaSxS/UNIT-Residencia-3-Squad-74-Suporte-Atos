using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SupportAtos.Application.DTOs;
using SupportAtos.Application.Options;
using SupportAtos.Application.Ports;
using SupportAtos.Domain.Enums;

namespace SupportAtos.Api.Authentication;

/// <summary>
/// Gerador de JWT RS256 para login próprio de funcionários internos.
/// </summary>
public class JwtTokenGerador : ITokenGerador
{
    private readonly AuthSettings _authSettings;

    public JwtTokenGerador(IOptions<AuthSettings> authSettings)
    {
        _authSettings = authSettings.Value;
    }

    public string GerarToken(
        int usuarioId,
        string email,
        string nome,
        PapelUsuario papel,
        int? grupoEmpresaId)
    {
        if (string.IsNullOrWhiteSpace(_authSettings.PrivateKeyPem))
        {
            throw new InvalidOperationException(
                "Auth:PrivateKeyPem não configurada. Defina via user-secrets ou variável de ambiente.");
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(_authSettings.PrivateKeyPem);
        var chave = new RsaSecurityKey(rsa);

        var credenciais = new SigningCredentials(chave, SecurityAlgorithms.RsaSha256)
        {
            // A RSA pertence somente a esta emissão. Um provider em cache poderia
            // reutilizá-la depois do Dispose, inclusive em outro scope do gerador.
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, usuarioId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, usuarioId.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, nome),
            new Claim(ClaimTypes.Role, papel.ToString()),
            new Claim("tipo", "interno"),
        };

        if (grupoEmpresaId.HasValue)
        {
            claims.Add(new Claim("grupo_empresa_id", grupoEmpresaId.Value.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: _authSettings.Issuer,
            audience: _authSettings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_authSettings.ExpiracaoMinutos),
            signingCredentials: credenciais);

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(token);
    }
}
