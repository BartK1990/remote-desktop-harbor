using System.Security.Cryptography;
using Harbor.Core.Security;

namespace Harbor.Core.Tests;

// SPEC NFR-10, NFR-11
public class SecretCipherTests
{
    [Fact]
    public void Roundtrip_returns_original_secret()
    {
        using var cipher = new SecretCipher(Guid.NewGuid(), SecretCipher.GenerateKey());
        var id = Guid.NewGuid();
        var envelope = cipher.Encrypt("P@ssw0rd-żółć", id);
        Assert.Equal("P@ssw0rd-żółć", cipher.Decrypt(envelope, id));
    }

    [Fact]
    public void Same_plaintext_encrypts_differently_each_time()
    {
        using var cipher = new SecretCipher(Guid.NewGuid(), SecretCipher.GenerateKey());
        var id = Guid.NewGuid();
        Assert.NotEqual(cipher.Encrypt("x", id), cipher.Encrypt("x", id));
    }

    [Fact]
    public void Ciphertext_moved_to_another_entry_fails()
    {
        using var cipher = new SecretCipher(Guid.NewGuid(), SecretCipher.GenerateKey());
        var envelope = cipher.Encrypt("secret", Guid.NewGuid());
        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt(envelope, Guid.NewGuid()));
    }

    [Fact]
    public void Tampered_ciphertext_fails()
    {
        using var cipher = new SecretCipher(Guid.NewGuid(), SecretCipher.GenerateKey());
        var id = Guid.NewGuid();
        var envelope = cipher.Encrypt("secret", id);
        envelope[^1] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt(envelope, id));
    }

    [Fact]
    public void Wrong_key_is_detected_by_key_id()
    {
        using var a = new SecretCipher(Guid.NewGuid(), SecretCipher.GenerateKey());
        using var b = new SecretCipher(Guid.NewGuid(), SecretCipher.GenerateKey());
        var id = Guid.NewGuid();
        var ex = Assert.ThrowsAny<CryptographicException>(() => b.Decrypt(a.Encrypt("s", id), id));
        Assert.Contains("key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Passphrase_protector_roundtrip_and_wrong_passphrase()
    {
        var key = SecretCipher.GenerateKey();
        var good = new PassphraseKeyProtector(() => "correct horse", iterations: 1000);
        var blob = good.Protect(key);
        Assert.Equal(key, good.Unprotect(blob));

        var bad = new PassphraseKeyProtector(() => "wrong", iterations: 1000);
        Assert.Throws<VaultKeyException>(() => bad.Unprotect(blob));
    }
}
