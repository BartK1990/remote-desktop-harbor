namespace Harbor.Core.Model;

/// <summary>
/// How an entry is stored. See SPEC FR-03.
/// </summary>
public enum EntryKind
{
    /// <summary>A full RDP profile: host plus .rdp settings, optionally with credentials.</summary>
    RdpConnection = 0,

    /// <summary>Only a credential (user + password, optional host). Launching uses default RDP settings.</summary>
    CredentialOnly = 1,
}

/// <summary>
/// A stored remote desktop connection or credential. The password is never held in plain text
/// here: <see cref="PasswordCipher"/> is an encrypted envelope (see <c>SecretCipher</c>).
/// </summary>
public sealed record ConnectionEntry
{
    public const int DefaultRdpPort = 3389;

    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Folder { get; init; }
    public EntryKind Kind { get; init; } = EntryKind.RdpConnection;
    public string? Host { get; init; }
    public int Port { get; init; } = DefaultRdpPort;
    public string? Domain { get; init; }
    public string? UserName { get; init; }

    /// <summary>Extra .rdp file lines (<c>key:type:value</c>). Only meaningful for <see cref="EntryKind.RdpConnection"/>.</summary>
    public string? RdpSettings { get; init; }

    public string? Notes { get; init; }
    public byte[]? PasswordCipher { get; init; }

    /// <summary>Optimistic concurrency token, incremented by the store on every write. 0 = not yet stored.</summary>
    public long Version { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public string? UpdatedBy { get; init; }
    public bool IsDeleted { get; init; }

    /// <summary>
    /// Accounts besides the default one (<see cref="UserName"/>, <see cref="Domain"/>, <see cref="PasswordCipher"/>),
    /// in display order. See SPEC FR-09, ADR-0019.
    /// </summary>
    public IReadOnlyList<ConnectionAccount> Accounts { get; init; } = [];

    public bool HasPassword => PasswordCipher is { Length: > 0 };
    public bool CanLaunch => !string.IsNullOrWhiteSpace(Host);

    /// <summary>
    /// Host as shown in the tree and details: <c>host</c>, or <c>host:port</c> when the port is not the RDP default;
    /// <c>null</c> without a host. SPEC FR-02
    /// </summary>
    public string? Address => string.IsNullOrWhiteSpace(Host) ? null : Port == DefaultRdpPort ? Host : $"{Host}:{Port}";

    /// <summary>User name as Windows expects it: <c>DOMAIN\user</c> when a domain is set.</summary>
    public string? QualifiedUserName => Qualify(Domain, UserName);

    /// <summary>
    /// The user name <paramref name="account"/> connects as; an account without a domain uses the entry's.
    /// <c>null</c> selects the default account.
    /// </summary>
    public string? QualifiedUserNameFor(ConnectionAccount? account) =>
        account is null ? QualifiedUserName : Qualify(string.IsNullOrWhiteSpace(account.Domain) ? Domain : account.Domain, account.UserName);

    /// <summary>The additional account with <paramref name="accountId"/>, or <c>null</c> (the default account).</summary>
    public ConnectionAccount? FindAccount(Guid? accountId) =>
        accountId is { } id ? Accounts.FirstOrDefault(a => a.Id == id) : null;

    /// <summary>
    /// This entry as it connects with <paramref name="account"/>: that account's user and domain, no other accounts and
    /// no password cipher (the cipher is bound to the account, see <c>HarborWorkspace.RevealPassword</c>).
    /// <c>null</c> returns the entry itself.
    /// </summary>
    public ConnectionEntry ForAccount(ConnectionAccount? account) => account is null
        ? this
        : this with
        {
            Domain = string.IsNullOrWhiteSpace(account.Domain) ? Domain : account.Domain,
            UserName = account.UserName,
            PasswordCipher = null,
            Accounts = [],
        };

    private static string? Qualify(string? domain, string? userName) =>
        string.IsNullOrWhiteSpace(userName) ? null
        : string.IsNullOrWhiteSpace(domain) || userName.Contains('\\') || userName.Contains('@') ? userName
        : $"{domain}\\{userName}";

    public static ConnectionEntry CreateNew(string name, EntryKind kind = EntryKind.RdpConnection) =>
        new() { Id = Guid.NewGuid(), Name = name, Kind = kind };
}
