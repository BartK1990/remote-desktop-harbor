using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harbor.Core.Backup;

/// <summary>
/// Reads and writes Harbor backup files (<c>.harborbackup</c>, SPEC FR-68, ADR-0046). The file is a small JSON envelope
/// whose payload — the <see cref="BackupContent"/> as JSON — is encrypted with AES-256-GCM under a key derived from the
/// user's backup passphrase (PBKDF2-SHA256). Nothing but the format name, version and key derivation parameters is
/// readable without the passphrase, so the file can be copied to a USB stick or a cloud drive. The vault keys of the
/// PC are not involved: they are bound to the Windows user and machine (NFR-10), the passphrase is not.
/// </summary>
public static class BackupFile
{
    public const string FormatName = "remote-desktop-harbor-backup";
    public const string Extension = ".harborbackup";
    public const int CurrentVersion = 1;
    public const int DefaultIterations = 600_000;

    /// <summary>Shortest passphrase the app accepts for a new backup.</summary>
    public const int MinPassphraseLength = 12;

    private const int MaxIterations = 10_000_000;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>Encrypts <paramref name="content"/> with <paramref name="passphrase"/> into the bytes of a backup file.</summary>
    public static byte[] Encrypt(BackupContent content, string passphrase, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = JsonSerializer.SerializeToUtf8Bytes(content, BackupJsonContext.Default.BackupContent);
        var data = new byte[plain.Length];
        var tag = new byte[TagSize];
        var key = DeriveKey(passphrase, salt, iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plain, data, tag, AssociatedData(CurrentVersion, iterations, salt));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain); // the payload holds plaintext passwords (NFR-12)
        }

        var envelope = new BackupEnvelope
        {
            Format = FormatName,
            Version = CurrentVersion,
            Kdf = "PBKDF2-SHA256",
            Iterations = iterations,
            Salt = Convert.ToBase64String(salt),
            Cipher = "AES-256-GCM",
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Data = Convert.ToBase64String(data),
        };
        return JsonSerializer.SerializeToUtf8Bytes(envelope, BackupJsonContext.Default.BackupEnvelope);
    }

    /// <summary>Decrypts the bytes of a backup file.</summary>
    /// <exception cref="FormatException">The file is not a Harbor backup, is damaged, or was written by a newer version.</exception>
    /// <exception cref="BackupPassphraseException">The passphrase is wrong (or the file was changed).</exception>
    public static BackupContent Decrypt(ReadOnlySpan<byte> file, string passphrase)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        var envelope = ReadEnvelope(file);
        byte[] salt, nonce, tag, data;
        try
        {
            salt = Convert.FromBase64String(envelope.Salt ?? "");
            nonce = Convert.FromBase64String(envelope.Nonce ?? "");
            tag = Convert.FromBase64String(envelope.Tag ?? "");
            data = Convert.FromBase64String(envelope.Data ?? "");
        }
        catch (FormatException ex)
        {
            throw new FormatException("The backup file is damaged.", ex);
        }

        if (salt.Length != SaltSize || nonce.Length != NonceSize || tag.Length != TagSize)
        {
            throw new FormatException("The backup file is damaged.");
        }

        var plain = new byte[data.Length];
        var key = DeriveKey(passphrase, salt, envelope.Iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, data, tag, plain, AssociatedData(envelope.Version, envelope.Iterations, salt));
            var content = JsonSerializer.Deserialize(plain, BackupJsonContext.Default.BackupContent)
                ?? throw new FormatException("The backup file is empty.");
            if (content.Version > CurrentVersion)
            {
                throw new FormatException("This backup was written by a newer version of Remote Desktop Harbor. Update the app to restore it.");
            }

            return content;
        }
        catch (AuthenticationTagMismatchException ex)
        {
            throw new BackupPassphraseException("The passphrase is incorrect, or the backup file was changed.", ex);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The backup file is damaged.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static BackupEnvelope ReadEnvelope(ReadOnlySpan<byte> file)
    {
        BackupEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(file, BackupJsonContext.Default.BackupEnvelope);
        }
        catch (JsonException ex)
        {
            throw new FormatException("This is not a Remote Desktop Harbor backup file.", ex);
        }

        if (envelope is null || envelope.Format != FormatName)
        {
            throw new FormatException("This is not a Remote Desktop Harbor backup file.");
        }

        if (envelope.Version > CurrentVersion)
        {
            throw new FormatException("This backup was written by a newer version of Remote Desktop Harbor. Update the app to restore it.");
        }

        if (envelope.Version < 1 || envelope.Kdf != "PBKDF2-SHA256" || envelope.Cipher != "AES-256-GCM" ||
            envelope.Iterations is < 1 or > MaxIterations)
        {
            throw new FormatException("The backup file is damaged.");
        }

        return envelope;
    }

    // The header is authenticated with the payload, so its key derivation parameters cannot be swapped unnoticed.
    private static byte[] AssociatedData(int version, int iterations, byte[] salt) =>
        Encoding.UTF8.GetBytes($"{FormatName}|{version}|{iterations}|{Convert.ToBase64String(salt)}");

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, 32);
}

/// <summary>The backup passphrase does not decrypt the file.</summary>
public sealed class BackupPassphraseException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The readable part of a backup file. SPEC FR-68</summary>
internal sealed record BackupEnvelope
{
    public string? Format { get; init; }
    public int Version { get; init; }
    public string? Kdf { get; init; }
    public int Iterations { get; init; }
    public string? Salt { get; init; }
    public string? Cipher { get; init; }
    public string? Nonce { get; init; }
    public string? Tag { get; init; }
    public string? Data { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true)]
[JsonSerializable(typeof(BackupEnvelope))]
[JsonSerializable(typeof(BackupContent))]
internal sealed partial class BackupJsonContext : JsonSerializerContext;
