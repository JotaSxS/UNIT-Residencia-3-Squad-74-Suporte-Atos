using SupportAtos.Application.Ports;
using System.Security.Cryptography;
using System.Text;

namespace SupportAtos.Infrastructure.Security;

/// <summary>
/// Implementação de hash de senha usando Argon2id (Konscious.Security.Cryptography).
/// </summary>
public class Argon2idHashSenha : IHashSenha
{
    private const int MemoriaKb = 65536;
    private const int Iteracoes = 3;
    private const int GrauParalelismo = 4;
    private const int TamanhoHashBytes = 32;
    private const int TamanhoSaltBytes = 16;

    private const char Separador = '.';

    public string Hash(string senha)
    {
        if (string.IsNullOrWhiteSpace(senha))
            throw new ArgumentException("A senha não pode ser vazia.", nameof(senha));

        var salt = RandomNumberGenerator.GetBytes(TamanhoSaltBytes);

        var hasher = new Konscious.Security.Cryptography.Argon2id(Encoding.UTF8.GetBytes(senha))
        {
            Salt = salt,
            DegreeOfParallelism = GrauParalelismo,
            Iterations = Iteracoes,
            MemorySize = MemoriaKb
        };

        var hashBytes = hasher.GetBytes(TamanhoHashBytes);

        return $"{Convert.ToBase64String(salt)}{Separador}{Convert.ToBase64String(hashBytes)}";
    }

    public bool Verifica(string senha, string hash)
    {
        if (string.IsNullOrWhiteSpace(senha))
            return false;

        if (string.IsNullOrWhiteSpace(hash))
            return false;

        try
        {
            var partes = hash.Split(Separador);
            if (partes.Length != 2)
                return false;

            var salt = Convert.FromBase64String(partes[0]);
            var hashEsperado = Convert.FromBase64String(partes[1]);

            var hasher = new Konscious.Security.Cryptography.Argon2id(Encoding.UTF8.GetBytes(senha))
            {
                Salt = salt,
                DegreeOfParallelism = GrauParalelismo,
                Iterations = Iteracoes,
                MemorySize = MemoriaKb
            };

            var hashCalculado = hasher.GetBytes(TamanhoHashBytes);

            return CryptographicOperations.FixedTimeEquals(hashCalculado, hashEsperado);
        }
        catch
        {
            return false;
        }
    }
}
