using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Harbor.Core.Security;

/// <summary>
/// Encrypts entry secrets with AES-256-GCM under a data-encryption key (DEK).
/// Envelope layout (v1): <c>[1 version][16 keyId][12 nonce][16 tag][n ciphertext]</c>.
/// The entry id is bound as associated data, so a ciphertext copied onto another row fails to decrypt.
/// See ADR-0005.
/// </summary>
public sealed class SecretCipher : IDisposable
{
    public const int KeySize = 32;
    private const byte FormatV1 = 1;
    private const int KeyIdSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + KeyIdSize + NonceSize + TagSize;

    private readonly AesGcm _aes;
    private readonly byte[] _key;

    public SecretCipher(Guid keyId, byte[] key)
    {
        if (key.Length != KeySize)
        {
            throw new ArgumentException($"Key must be {KeySize} bytes.", nameof(key));
        }

        KeyId = keyId;
        _key = (byte[])key.Clone();
        _aes = new AesGcm(_key, TagSize);
    }

    public Guid KeyId { get; }

    /// <summary>Copy of the raw key, used only to re-wrap it for a different protector (e.g. local cache).</summary>
    internal byte[] ExportKey() => (byte[])_key.Clone();

    public static byte[] GenerateKey() => RandomNumberGenerator.GetBytes(KeySize);

    public byte[] Encrypt(string plaintext, Guid entryId)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            var envelope = new byte[HeaderSize + plain.Length];
            envelope[0] = FormatV1;
            KeyId.TryWriteBytes(envelope.AsSpan(1, KeyIdSize));
            var nonce = envelope.AsSpan(1 + KeyIdSize, NonceSize);
            RandomNumberGenerator.Fill(nonce);
            var tag = envelope.AsSpan(1 + KeyIdSize + NonceSize, TagSize);
            var cipher = envelope.AsSpan(HeaderSize);
            _aes.Encrypt(nonce, plain, cipher, tag, AssociatedData(entryId));
            return envelope;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public string Decrypt(ReadOnlySpan<byte> envelope, Guid entryId)
    {
        if (envelope.Length < HeaderSize || envelope[0] != FormatV1)
        {
            throw new CryptographicException("Unsupported secret envelope format.");
        }

        var keyId = new Guid(envelope.Slice(1, KeyIdSize));
        if (keyId != KeyId)
        {
            throw new CryptographicException($"Secret was encrypted with key {keyId}, but key {KeyId} is loaded.");
        }

        var nonce = envelope.Slice(1 + KeyIdSize, NonceSize);
        var tag = envelope.Slice(1 + KeyIdSize + NonceSize, TagSize);
        var cipher = envelope[HeaderSize..];
        var plain = new byte[cipher.Length];
        try
        {
            _aes.Decrypt(nonce, cipher, tag, plain, AssociatedData(entryId));
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static byte[] AssociatedData(Guid entryId)
    {
        var ad = new byte[4 + 16];
        BinaryPrimitives.WriteInt32BigEndian(ad, 0x48524231); // "HRB1"
        entryId.TryWriteBytes(ad.AsSpan(4));
        return ad;
    }

    public void Dispose()
    {
        _aes.Dispose();
        CryptographicOperations.ZeroMemory(_key);
    }
}
