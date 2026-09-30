using System.Security.Cryptography;
using System.Text;

namespace APP_CELULAR_API.Services;

public sealed class CatalogoCriptografia
{
    private readonly IConfiguration _configuration;

    public CatalogoCriptografia(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private byte[] ObterChave()
    {
        string? base64 = _configuration["MasterAdmin:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(base64))
            throw new InvalidOperationException("A chave de criptografia do catálogo não está configurada.");
        byte[] chave;
        try { chave = Convert.FromBase64String(base64); }
        catch (FormatException ex) { throw new InvalidOperationException("A chave do catálogo deve estar em Base64.", ex); }
        if (chave.Length != 32)
            throw new InvalidOperationException("A chave de criptografia do catálogo deve conter 32 bytes.");
        return chave;
    }

    public byte[] Criptografar(string texto)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] plain = Encoding.UTF8.GetBytes(texto);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[16];
        using var aes = new AesGcm(ObterChave(), tag.Length);
        aes.Encrypt(nonce, plain, cipher, tag);
        return [.. nonce, .. tag, .. cipher];
    }

    public string Descriptografar(byte[] payload)
    {
        if (payload.Length < 28) throw new CryptographicException("Registro criptografado inválido.");
        var nonce = payload.AsSpan(0, 12);
        var tag = payload.AsSpan(12, 16);
        var cipher = payload.AsSpan(28);
        byte[] plain = new byte[cipher.Length];
        using var aes = new AesGcm(ObterChave(), 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
