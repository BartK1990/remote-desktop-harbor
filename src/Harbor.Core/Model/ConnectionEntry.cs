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

    public bool HasPassword => PasswordCipher is { Length: > 0 };
    public bool CanLaunch => !string.IsNullOrWhiteSpace(Host);

    /// <summary>User name as Windows expects it: <c>DOMAIN\user</c> when a domain is set.</summary>
    public string? QualifiedUserName =>
        string.IsNullOrWhiteSpace(UserName) ? null
        : string.IsNullOrWhiteSpace(Domain) || UserName.Contains('\\') || UserName.Contains('@') ? UserName
        : $"{Domain}\\{UserName}";

    public static ConnectionEntry CreateNew(string name, EntryKind kind = EntryKind.RdpConnection) =>
        new() { Id = Guid.NewGuid(), Name = name, Kind = kind };
}
