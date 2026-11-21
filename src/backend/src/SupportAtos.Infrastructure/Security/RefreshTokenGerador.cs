using System.Security.Cryptography;
using SupportAtos.Application.Ports;

namespace SupportAtos.Infrastructure.Security;

/// <summary>
/// Gerador de refresh tokens criptograficamente aleatórios.
/// </summary>
public class RefreshTokenGerador : IRefreshTokenGerador
{
    private const int TamanhoTokenBytes = 32;

    /// <summary>Gera um refresh token criptograficamente aleatório (texto puro, não hash).</summary>
    public string GerarToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TamanhoTokenBytes);
        return Convert.ToBase64String(bytes);
    }
}
