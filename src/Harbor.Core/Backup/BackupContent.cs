using Harbor.Core.Model;

namespace Harbor.Core.Backup;

/// <summary>
/// What a backup file holds once it is decrypted (SPEC FR-68, ADR-0046): every entry with its accounts and plaintext
/// passwords, and the user's personal pins and smart groups. Only ever held in memory; on disk it is always encrypted
/// by <see cref="BackupFile"/>.
/// </summary>
public sealed record BackupContent
{
    /// <summary>Version of this content layout; a newer one is refused by <see cref="BackupFile.Decrypt"/>.</summary>
    public int Version { get; init; } = BackupFile.CurrentVersion;

    /// <summary>The app version that wrote the backup, e.g. <c>0.12.0</c>; informational.</summary>
    public string? AppVersion { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }
    public IReadOnlyList<BackupEntry> Entries { get; init; } = [];

    /// <summary>The user's smart groups (SPEC FR-62).</summary>
    public IReadOnlyList<SmartGroup> SmartGroups { get; init; } = [];
}

/// <summary>One entry of a backup, with plaintext passwords. Never log it (NFR-12): <see cref="ToString"/> omits them.</summary>
public sealed record BackupEntry
{
    /// <summary>The entry's id on the PC that wrote the backup; restored entries get new ids.</summary>
    public Guid Id { get; init; }

    public string Name { get; init; } = "";
    public string? Folder { get; init; }
    public EntryKind Kind { get; init; } = EntryKind.RdpConnection;
    public string? Host { get; init; }
    public int Port { get; init; } = ConnectionEntry.DefaultRdpPort;
    public string? Domain { get; init; }
    public string? UserName { get; init; }
    public string? Password { get; init; }
    public string? RdpSettings { get; init; }
    public string? Notes { get; init; }

    /// <summary>Kept on this PC only in shared-database mode (SPEC FR-29).</summary>
    public bool LocalOnly { get; init; }

    /// <summary>Pinned by the user who wrote the backup (SPEC FR-50).</summary>
    public bool Pinned { get; init; }

    public IReadOnlyList<BackupAccount> Accounts { get; init; } = [];

    public override string ToString() => $"{Name} ({Kind})";
}

/// <summary>An additional account (SPEC FR-09) of a <see cref="BackupEntry"/>.</summary>
public sealed record BackupAccount
{
    public string? Domain { get; init; }
    public string UserName { get; init; } = "";
    public string? Password { get; init; }

    public override string ToString() => UserName;
}
