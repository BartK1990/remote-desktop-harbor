using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using System.Security.Principal;
using Harbor.Core.Identity;
using Harbor.Windows.Interop;

namespace Harbor.Windows.Identity;

/// <summary>
/// Uses the Windows logon as the app identity (implicit SSO, no prompt). Email and display name come from
/// Active Directory when reachable, otherwise from the UPN. Works for domain, Entra-joined and local accounts.
/// See ADR-0009.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsIdentityProvider : IIdentityProvider
{
    private static readonly TimeSpan DirectoryTimeout = TimeSpan.FromSeconds(3);

    public IdentitySource Source => IdentitySource.Windows;

    public async Task<UserIdentity> SignInAsync(bool allowInteractive, CancellationToken cancellationToken = default)
    {
        using var windowsIdentity = WindowsIdentity.GetCurrent();
        var userName = windowsIdentity.Name;
        var sid = windowsIdentity.User?.Value;
        var upn = NativeMethods.GetUserNameEx(NativeMethods.NameUserPrincipal);
        var displayName = NativeMethods.GetUserNameEx(NativeMethods.NameDisplay);

        string? email = null;
        if (sid is not null)
        {
            // AD lookups can hang when the DC is unreachable (e.g. off VPN); never block startup on them.
            var lookup = Task.Run(() => LookupDirectory(sid), cancellationToken);
            if (await Task.WhenAny(lookup, Task.Delay(DirectoryTimeout, cancellationToken)).ConfigureAwait(false) == lookup)
            {
                (email, var dirName) = await lookup.ConfigureAwait(false);
                displayName = dirName ?? displayName;
            }
        }

        email ??= upn is not null && upn.Contains('@') ? upn : null;
        return new UserIdentity(userName, string.IsNullOrWhiteSpace(displayName) ? userName : displayName, email, Source);
    }

    private static (string? Email, string? DisplayName) LookupDirectory(string sid)
    {
        try
        {
            using var context = new PrincipalContext(ContextType.Domain);
            using var user = UserPrincipal.FindByIdentity(context, IdentityType.Sid, sid);
            return (user?.EmailAddress, user?.DisplayName);
        }
        catch (Exception)
        {
            return (null, null); // not domain-joined or DC unreachable
        }
    }
}
