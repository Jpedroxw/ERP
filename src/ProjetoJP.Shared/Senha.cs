using System.Security.Cryptography;

namespace ProjetoJP.Shared;

/// <summary>
/// Hash de senha PBKDF2-SHA256. Usado pela API e pelo PDV (login offline),
/// por isso fica no projeto compartilhado.
/// Formato armazenado: pbkdf2$iteracoes$salt(base64)$hash(base64)
/// </summary>
public static class Senha
{
    private const int Iteracoes = 100_000;
    private const int TamanhoSalt = 16;
    private const int TamanhoHash = 32;

    public static string Gerar(string senha)
    {
        var salt = RandomNumberGenerator.GetBytes(TamanhoSalt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(senha, salt, Iteracoes, HashAlgorithmName.SHA256, TamanhoHash);
        return $"pbkdf2${Iteracoes}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verificar(string senha, string? armazenado)
    {
        if (string.IsNullOrEmpty(senha) || string.IsNullOrEmpty(armazenado))
            return false;

        var partes = armazenado.Split('$');
        if (partes.Length != 4 || partes[0] != "pbkdf2" || !int.TryParse(partes[1], out var iteracoes))
            return false;

        try
        {
            var salt = Convert.FromBase64String(partes[2]);
            var esperado = Convert.FromBase64String(partes[3]);
            var hash = Rfc2898DeriveBytes.Pbkdf2(senha, salt, iteracoes, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(hash, esperado);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
