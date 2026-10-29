using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Harbor.Core.Security;

/// <summary>
/// Wraps a key with a key derived from a shared passphrase (PBKDF2-SHA256 + AES-256-GCM).
/// Fallback for shared databases when DPAPI-NG group protection is not available. See ADR-0006.
/// Blob: <c>[1 version][4 iterations][16 salt][12 nonce][16 tag][n ciphertext]</c>.
/// </summary>
public sealed class PassphraseKeyProtector(Func<string?> passphraseProvider, int iterations = PassphraseKeyProtector.DefaultIterations)
    : IKeyProtector
{
    public const string SchemeName = "passphrase";
    public const int DefaultIterations = 600_000;
    private const byte FormatV1 = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + 4 + SaltSize + NonceSize + TagSize;

    public string Scheme => SchemeName;

    public byte[] Protect(ReadOnlySpan<byte> key)
    {
        var blob = new byte[HeaderSize + key.Length];
        blob[0] = FormatV1;
        BinaryPrimitives.WriteInt32BigEndian(blob.AsSpan(1, 4), iterations);
        var salt = blob.AsSpan(5, SaltSize);
        RandomNumberGenerator.Fill(salt);
        var nonce = blob.AsSpan(5 + SaltSize, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        var kek = DeriveKek(salt, iterations);
        try
        {
            using var aes = new AesGcm(kek, TagSize);
            aes.Encrypt(nonce, key, blob.AsSpan(HeaderSize), blob.AsSpan(5 + SaltSize + NonceSize, TagSize));
            return blob;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedKey)
    {
        if (protectedKey.Length < HeaderSize || protectedKey[0] != FormatV1)
        {
            throw new VaultKeyException("Unsupported passphrase-protected key format.");
        }

        var iter = BinaryPrimitives.ReadInt32BigEndian(protectedKey.Slice(1, 4));
        var salt = protectedKey.Slice(5, SaltSize);
        var nonce = protectedKey.Slice(5 + SaltSize, NonceSize);
        var tag = protectedKey.Slice(5 + SaltSize + NonceSize, TagSize);
        var cipher = protectedKey[HeaderSize..];
        var kek = DeriveKek(salt, iter);
        try
        {
            using var aes = new AesGcm(kek, TagSize);
            var key = new byte[cipher.Length];
            aes.Decrypt(nonce, cipher, tag, key);
            return key;
        }
        catch (AuthenticationTagMismatchException ex)
        {
            throw new VaultKeyException("The passphrase is incorrect.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    private byte[] DeriveKek(ReadOnlySpan<byte> salt, int iter)
    {
        var passphrase = passphraseProvider();
        if (string.IsNullOrEmpty(passphrase))
        {
            throw new VaultKeyException("A passphrase is required to unlock the shared vault.");
        }

        return Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, iter, HashAlgorithmName.SHA256, 32);
    }
}
