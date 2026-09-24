using System.Security.Cryptography;
using System.Text;
using VinylRipper.Security;

namespace VinylRipper.Tests;

public class TokenProtectorTests
{
    private static TokenProtector Create(string material = "clave-de-prueba") =>
        new(new StaticKeyMaterialProvider(Encoding.UTF8.GetBytes(material)));

    [Fact]
    public void Protect_then_Unprotect_roundtrips()
    {
        var protector = Create();
        var token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789";

        var blob = protector.Protect(token);

        Assert.NotEqual(token, blob);
        Assert.Equal(token, protector.Unprotect(blob));
    }

    [Fact]
    public void Protect_produces_different_blobs_for_same_input()
    {
        var protector = Create();
        Assert.NotEqual(protector.Protect("igual"), protector.Protect("igual"));
    }

    [Fact]
    public void Unprotect_with_other_key_material_fails()
    {
        var blob = Create("uno").Protect("secreto");
        Assert.ThrowsAny<CryptographicException>(() => Create("dos").Unprotect(blob));
    }

    [Fact]
    public void Unprotect_detects_tampering()
    {
        var protector = Create();
        var bytes = Convert.FromBase64String(protector.Protect("secreto"));
        bytes[^1] ^= 0xFF; // alteramos el último byte del cifrado

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(Convert.ToBase64String(bytes)));
    }

    [Theory]
    [InlineData("no-es-base64!!")]
    [InlineData("AAAA")]
    public void TryUnprotect_returns_null_for_garbage(string garbage)
    {
        Assert.Null(Create().TryUnprotect(garbage));
    }

    [Fact]
    public void TryUnprotect_returns_null_for_empty()
    {
        Assert.Null(Create().TryUnprotect(null));
        Assert.Null(Create().TryUnprotect(""));
    }

    [Fact]
    public void Protect_handles_unicode_and_empty_string()
    {
        var protector = Create();
        Assert.Equal("ñandú 🎵", protector.Unprotect(protector.Protect("ñandú 🎵")));
        Assert.Equal("", protector.Unprotect(protector.Protect("")));
    }
}
