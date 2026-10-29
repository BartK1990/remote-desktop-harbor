using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Harbor.Core.Security;

namespace Harbor.Windows.Security;

/// <summary>
/// Classic DPAPI. <see cref="UserScheme"/> binds the key to the Windows user profile;
/// <see cref="UserAndMachineScheme"/> additionally wraps it with the machine key, so a copied database
/// and roaming profile still cannot be opened on another computer. No admin rights needed. See ADR-0005.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiKeyProtector : IKeyProtector
{
    public const string UserScheme = "dpapi-user";
    public const string UserAndMachineScheme = "dpapi-user+machine";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RemoteDesktopHarbor/vault-key/v1");

    private readonly bool _bindToMachine;

    private DpapiKeyProtector(bool bindToMachine) => _bindToMachine = bindToMachine;

    public static DpapiKeyProtector User { get; } = new(false);
    public static DpapiKeyProtector UserAndMachine { get; } = new(true);

    public string Scheme => _bindToMachine ? UserAndMachineScheme : UserScheme;

    public byte[] Protect(ReadOnlySpan<byte> key)
    {
        var inner = ProtectedData.Protect(key.ToArray(), Entropy, DataProtectionScope.CurrentUser);
        return _bindToMachine ? ProtectedData.Protect(inner, Entropy, DataProtectionScope.LocalMachine) : inner;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedKey)
    {
        try
        {
            var inner = _bindToMachine
                ? ProtectedData.Unprotect(protectedKey.ToArray(), Entropy, DataProtectionScope.LocalMachine)
                : protectedKey.ToArray();
            return ProtectedData.Unprotect(inner, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new VaultKeyException(
                "The vault key cannot be decrypted by this Windows user on this machine. " +
                "Local data is bound to the user and computer that created it.", ex);
        }
    }

    /// <summary>Protects a small string (e.g. the remote connection string in settings) for the current user.</summary>
    public static string ProtectString(string value) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));

    public static string UnprotectString(string protectedValue) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser));
}
