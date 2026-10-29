using Harbor.Core.Identity;

namespace Harbor.Core.Security;

/// <summary>
/// Shared vault key protected only by access to the shared database plus a verified app sign-in (Windows or Entra ID):
/// no passphrase and no AD group. The key is stored in the database as is, so whoever can read the database (or a
/// backup of it) can read the stored passwords; the sign-in check runs in the app. See ADR-0016, SPEC FR-28.
/// Blob: <c>[1 version][n key]</c>.
/// </summary>
public sealed class SignInKeyProtector(UserIdentity user) : IKeyProtector
{
    public const string SchemeName = "sign-in";
    private const byte FormatV1 = 1;

    public string Scheme => SchemeName;

    public byte[] Protect(ReadOnlySpan<byte> key)
    {
        EnsureVerified();
        var blob = new byte[1 + key.Length];
        blob[0] = FormatV1;
        key.CopyTo(blob.AsSpan(1));
        return blob;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedKey)
    {
        EnsureVerified();
        if (protectedKey.Length < 2 || protectedKey[0] != FormatV1)
        {
            throw new VaultKeyException("Unsupported sign-in-protected key format.");
        }

        return protectedKey[1..].ToArray();
    }

    private void EnsureVerified()
    {
        if (user.Source == IdentitySource.Anonymous)
        {
            throw new VaultKeyException(
                "This shared vault opens for users signed in with Windows or Microsoft Entra ID. Change Settings → Sign-in.");
        }
    }
}
