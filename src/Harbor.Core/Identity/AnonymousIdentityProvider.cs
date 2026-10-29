namespace Harbor.Core.Identity;

/// <summary>
/// No sign-in: the user is known only by the name entered in Settings. Not verified, so presence and audit show it
/// marked as such; signing in with Windows or Entra ID is recommended. SPEC FR-44, ADR-0015.
/// </summary>
public sealed class AnonymousIdentityProvider(string name) : IIdentityProvider
{
    public const string Marker = " (not signed in)";

    public IdentitySource Source => IdentitySource.Anonymous;

    public Task<UserIdentity> SignInAsync(bool allowInteractive, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Enter your name in Settings → Sign-in to use the app without signing in.");
        }

        var trimmed = name.Trim();
        return Task.FromResult(new UserIdentity(trimmed, trimmed + Marker, null, Source));
    }
}
