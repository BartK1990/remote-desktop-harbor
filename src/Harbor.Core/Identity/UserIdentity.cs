namespace Harbor.Core.Identity;

public enum IdentitySource
{
    Windows = 0,
    EntraId = 1,

    /// <summary>No sign-in: a name the user typed, not verified. SPEC FR-44</summary>
    Anonymous = 2,
}

/// <summary>The signed-in user of the app. See SPEC FR-40..FR-43.</summary>
public sealed record UserIdentity(string UserName, string DisplayName, string? Email, IdentitySource Source)
{
    public override string ToString() =>
        Source == IdentitySource.Anonymous ? DisplayName
        : string.IsNullOrWhiteSpace(Email) ? $"{DisplayName} ({UserName})" : $"{DisplayName} <{Email}>";
}

public interface IIdentityProvider
{
    IdentitySource Source { get; }

    /// <summary>Resolves the user, silently (SSO) when possible, interactively when <paramref name="allowInteractive"/>.</summary>
    Task<UserIdentity> SignInAsync(bool allowInteractive, CancellationToken cancellationToken = default);
}
