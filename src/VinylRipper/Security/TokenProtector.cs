using System.Security.Cryptography;
using System.Text;

namespace VinylRipper.Security;

/// <summary>
/// Cifra/descifra secretos con AES-256-GCM. La clave se deriva con PBKDF2-SHA256 a partir
/// del material de <see cref="IKeyMaterialProvider"/> y de una sal aleatoria por mensaje.
/// <para>Formato del blob (base64): <c>[versión 1][sal 16][nonce 12][tag 16][cifrado…]</c>.</para>
/// </summary>
public sealed class TokenProtector
{
    private const byte Version = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32; // 256 bits
    private const int Iterations = 100_000;

    private readonly IKeyMaterialProvider _keyMaterial;

    public TokenProtector(IKeyMaterialProvider keyMaterial)
    {
        _keyMaterial = keyMaterial;
    }

    public string Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plainText);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(DeriveKey(salt), TagSize))
            aes.Encrypt(nonce, plain, cipher, tag);

        var blob = new byte[1 + SaltSize + NonceSize + TagSize + cipher.Length];
        var offset = 0;
        blob[offset++] = Version;
        salt.CopyTo(blob, offset); offset += SaltSize;
        nonce.CopyTo(blob, offset); offset += NonceSize;
        tag.CopyTo(blob, offset); offset += TagSize;
        cipher.CopyTo(blob, offset);

        return Convert.ToBase64String(blob);
    }

    public string Unprotect(string protectedText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedText);

        byte[] blob;
        try { blob = Convert.FromBase64String(protectedText); }
        catch (FormatException ex) { throw new CryptographicException("El token cifrado no es base64 válido.", ex); }

        if (blob.Length < 1 + SaltSize + NonceSize + TagSize || blob[0] != Version)
            throw new CryptographicException("Formato de token cifrado no reconocido.");

        var offset = 1;
        var salt = blob.AsSpan(offset, SaltSize); offset += SaltSize;
        var nonce = blob.AsSpan(offset, NonceSize); offset += NonceSize;
        var tag = blob.AsSpan(offset, TagSize); offset += TagSize;
        var cipher = blob.AsSpan(offset);
        var plain = new byte[cipher.Length];

        using (var aes = new AesGcm(DeriveKey(salt), TagSize))
            aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>Como <see cref="Unprotect"/> pero devuelve null si el blob no se puede descifrar.</summary>
    public string? TryUnprotect(string? protectedText)
    {
        if (string.IsNullOrWhiteSpace(protectedText)) return null;
        try { return Unprotect(protectedText); }
        catch (CryptographicException) { return null; }
    }

    private byte[] DeriveKey(ReadOnlySpan<byte> salt) =>
        Rfc2898DeriveBytes.Pbkdf2(_keyMaterial.GetKeyMaterial(), salt, Iterations, HashAlgorithmName.SHA256, KeySize);
}
