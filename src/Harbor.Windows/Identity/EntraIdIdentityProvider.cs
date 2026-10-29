using System.Runtime.Versioning;
using Harbor.Core.Identity;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;

namespace Harbor.Windows.Identity;

/// <summary>
/// Microsoft Entra ID sign-in through the Windows Web Account Manager (WAM) broker. On Entra-joined or
/// hybrid-joined devices the silent call signs in with the Windows account (SSO, no prompt).
/// Requires an app registration with redirect URI <c>ms-appx-web://microsoft.aad.brokerplugin/{clientId}</c>.
/// See ADR-0009.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EntraIdIdentityProvider(string clientId, string? tenantId, Func<nint> parentWindow) : IIdentityProvider
{
    private static readonly string[] Scopes = ["User.Read"];
    private IPublicClientApplication? _app;

    public IdentitySource Source => IdentitySource.EntraId;

    public async Task<UserIdentity> SignInAsync(bool allowInteractive, CancellationToken cancellationToken = default)
    {
        _app ??= PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, string.IsNullOrWhiteSpace(tenantId) ? "organizations" : tenantId)
            .WithDefaultRedirectUri()
            .WithParentActivityOrWindow(parentWindow)
            .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { Title = "Remote Desktop Harbor" })
            .Build();

        AuthenticationResult result;
        try
        {
            var account = (await _app.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault()
                ?? PublicClientApplication.OperatingSystemAccount;
            result = await _app.AcquireTokenSilent(Scopes, account).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (MsalUiRequiredException) when (allowInteractive)
        {
            result = await _app.AcquireTokenInteractive(Scopes).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }

        var upn = result.Account.Username;
        var name = result.ClaimsPrincipal?.FindFirst("name")?.Value;
        var email = result.ClaimsPrincipal?.FindFirst("email")?.Value ?? (upn.Contains('@') ? upn : null);
        return new UserIdentity(upn, string.IsNullOrWhiteSpace(name) ? upn : name, email, Source);
    }
}
