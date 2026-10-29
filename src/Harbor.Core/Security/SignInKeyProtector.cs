namespace Harbor.Core.Security;

/// <summary>
/// Shared vault key protected only by signing in to the shared database (Windows integrated, SQL login or Entra ID):
/// no passphrase and no AD group, whatever the app sign-in (including no sign-in, FR-44). The key is stored in the
/// database as is, so whoever can read the database (or a backup of it) can read the stored passwords.
/// See ADR-0016, ADR-0018, SPEC FR-28. Blob: <c>[1 version][n key]</c>.
/// </summary>
public sealed class SignInKeyProtector : IKeyProtector
{
    public const string SchemeName = "sign-in";
    private const byte FormatV1 = 1;

    public string Scheme => SchemeName;

    public byte[] Protect(ReadOnlySpan<byte> key)
    {
        var blob = new byte[1 + key.Length];
        blob[0] = FormatV1;
        key.CopyTo(blob.AsSpan(1));
        return blob;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedKey)
    {
        if (protectedKey.Length < 2 || protectedKey[0] != FormatV1)
        {
            throw new VaultKeyException("Unsupported sign-in-protected key format.");
        }

        return protectedKey[1..].ToArray();
    }
}
