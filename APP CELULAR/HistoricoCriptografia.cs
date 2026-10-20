using System.Security.Cryptography;
using System.Text;

namespace APP_CELULAR.Services;

public sealed record ConteudoMensagemPrivado(string Texto, byte[]? Foto, string? FotoNome, string? FotoTipo);

public static class HistoricoCriptografia
{
    private const int Iteracoes = 250_000;
    private const int TamanhoTag = 16;
    private const int TamanhoNonce = 12;
    private const int TamanhoFotoMaxima = 8 * 1024 * 1024;

    public static byte[] CriarSalt() => RandomNumberGenerator.GetBytes(16);

    public static byte[] DerivarChave(string senha, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(senha, salt, Iteracoes, HashAlgorithmName.SHA256, 32);

    public static byte[] Criptografar(ConteudoMensagemPrivado conteudo, byte[] chave)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(conteudo.Texto ?? "");
            writer.Write(conteudo.FotoNome ?? "");
            writer.Write(conteudo.FotoTipo ?? "");
            var foto = conteudo.Foto ?? Array.Empty<byte>();
            writer.Write(foto.Length);
            writer.Write(foto);
        }

        var nonce = RandomNumberGenerator.GetBytes(TamanhoNonce);
        var tag = new byte[TamanhoTag];
        var claro = stream.ToArray();
        var cifrado = new byte[claro.Length];
        using (var aes = new AesGcm(chave, TamanhoTag))
            aes.Encrypt(nonce, claro, cifrado, tag);

        var resultado = new byte[nonce.Length + tag.Length + cifrado.Length];
        Buffer.BlockCopy(nonce, 0, resultado, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, resultado, nonce.Length, tag.Length);
        Buffer.BlockCopy(cifrado, 0, resultado, nonce.Length + tag.Length, cifrado.Length);
        CryptographicOperations.ZeroMemory(claro);
        return resultado;
    }

    public static ConteudoMensagemPrivado Descriptografar(byte[] payloadCifrado, byte[] chave)
    {
        if (payloadCifrado.Length < TamanhoNonce + TamanhoTag)
            throw new CryptographicException("O conteúdo protegido está incompleto.");

        var nonce = payloadCifrado.AsSpan(0, TamanhoNonce);
        var tag = payloadCifrado.AsSpan(TamanhoNonce, TamanhoTag);
        var cifra = payloadCifrado.AsSpan(TamanhoNonce + TamanhoTag);
        var claro = new byte[cifra.Length];
        using (var aes = new AesGcm(chave, TamanhoTag))
            aes.Decrypt(nonce, cifra, tag, claro);

        try
        {
            using var stream = new MemoryStream(claro, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            var texto = reader.ReadString();
            var fotoNome = reader.ReadString();
            var fotoTipo = reader.ReadString();
            var tamanhoFoto = reader.ReadInt32();
            if (tamanhoFoto is < 0 or > TamanhoFotoMaxima)
                throw new CryptographicException("O conteúdo protegido contém uma imagem inválida.");
            var foto = tamanhoFoto == 0 ? null : reader.ReadBytes(tamanhoFoto);
            if (foto is { Length: > 0 } && foto.Length != tamanhoFoto)
                throw new CryptographicException("O conteúdo protegido está incompleto.");
            return new ConteudoMensagemPrivado(texto, foto,
                string.IsNullOrEmpty(fotoNome) ? null : fotoNome,
                string.IsNullOrEmpty(fotoTipo) ? null : fotoTipo);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(claro);
        }
    }

    public static string ChaveMensagem(long empresaId, long usuarioId, long contatoId, Guid? clienteMensagemId, long idServidor)
    {
        var id = clienteMensagemId?.ToString("N") ?? $"servidor-{idServidor}";
        return $"{empresaId}:{usuarioId}:{contatoId}:{id}";
    }
}
