using Harbor.Core.Import;
using Harbor.Core.Model;

namespace Harbor.Core.Backup;

/// <summary>An entry to add when a backup is restored, and whether to pin it. SPEC FR-68</summary>
public sealed record RestoreItem(ImportedEntry Import, bool Pinned);

/// <summary>
/// What restoring a backup would do (SPEC FR-68): the entries to add, the existing entries to pin because the backup
/// pins their twin, the smart groups to add, and how much is already here.
/// </summary>
public sealed record RestorePlan(
    IReadOnlyList<RestoreItem> ToAdd,
    int AlreadyPresent,
    IReadOnlyList<Guid> PinExisting,
    IReadOnlyList<SmartGroup> SmartGroupsToAdd,
    int SmartGroupsPresent)
{
    public bool IsEmpty => ToAdd.Count == 0 && PinExisting.Count == 0 && SmartGroupsToAdd.Count == 0;
}

/// <summary>
/// Turns the catalog into a <see cref="BackupContent"/> and plans restoring one, so a user can move their list to
/// another PC or keep a copy (SPEC FR-68, ADR-0046). OS-neutral; the app writes the encrypted file with
/// <see cref="BackupFile"/>.
/// </summary>
public static class HarborBackup
{
    /// <summary>
    /// The backup content of <paramref name="entries"/>. <paramref name="revealPassword"/> decrypts the password of an
    /// entry's default account (<c>null</c> account id) or of one of its additional accounts; the plaintext only lives in
    /// the returned content, which the caller encrypts right away (NFR-12).
    /// </summary>
    public static BackupContent Create(IEnumerable<ConnectionEntry> entries, Func<ConnectionEntry, Guid?, string?> revealPassword,
        IEnumerable<Guid> pinned, IEnumerable<SmartGroup> smartGroups, string? appVersion, DateTimeOffset nowUtc)
    {
        var pins = pinned.ToHashSet();
        return new BackupContent
        {
            AppVersion = appVersion,
            CreatedUtc = nowUtc,
            Entries = entries.Where(e => !e.IsDeleted).Select(e => new BackupEntry
            {
                Id = e.Id,
                Name = e.Name,
                Folder = e.Folder,
                Kind = e.Kind,
                Host = e.Host,
                Port = e.Port,
                Domain = e.Domain,
                UserName = e.UserName,
                Password = e.HasPassword ? revealPassword(e, null) : null,
                RdpSettings = e.RdpSettings,
                Notes = e.Notes,
                LocalOnly = e.IsLocalOnly,
                Pinned = pins.Contains(e.Id),
                Accounts = e.Accounts.Select(a => new BackupAccount
                {
                    Domain = a.Domain,
                    UserName = a.UserName,
                    Password = a.HasPassword ? revealPassword(e, a.Id) : null,
                }).ToList(),
            }).ToList(),
            SmartGroups = [.. smartGroups],
        };
    }

    /// <summary>
    /// Plans restoring <paramref name="content"/> into a catalog that holds <paramref name="existing"/> entries and
    /// <paramref name="existingSmartGroups"/>. Entries already in the catalog — same kind, name, folder, host, port and
    /// user, as for imports (SPEC FR-54) — are left out, so restoring twice adds nothing; a pin in the backup then pins
    /// the existing entry. New entries get new ids. Entries kept on this PC only go there again when
    /// <paramref name="localOnlyAvailable"/>, otherwise to the catalog. Smart groups with the name and folder of an existing
    /// one are left out; the others get new ids.
    /// </summary>
    public static RestorePlan PlanRestore(BackupContent content, IReadOnlyCollection<ConnectionEntry> existing,
        IEnumerable<SmartGroup> existingSmartGroups, bool localOnlyAvailable)
    {
        var known = new Dictionary<string, ConnectionEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in existing.Where(e => !e.IsDeleted))
        {
            known.TryAdd(RdmCsvImport.Key(e), e);
        }

        var toAdd = new List<RestoreItem>();
        var pinExisting = new List<Guid>();
        var present = 0;
        foreach (var item in content.Entries)
        {
            var import = ToImport(item, localOnlyAvailable);
            if (known.TryGetValue(RdmCsvImport.Key(import.Entry), out var twin))
            {
                present++;
                if (item.Pinned && !pinExisting.Contains(twin.Id)) pinExisting.Add(twin.Id);
                continue;
            }

            toAdd.Add(new RestoreItem(import, item.Pinned));
        }

        var groupKeys = existingSmartGroups.Select(GroupKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = new List<SmartGroup>();
        var groupsPresent = 0;
        foreach (var g in content.SmartGroups)
        {
            if (!groupKeys.Add(GroupKey(g)))
            {
                groupsPresent++;
                continue;
            }

            groups.Add(g with { Id = Guid.NewGuid(), Folder = FolderTree.Normalize(g.Folder) });
        }

        return new RestorePlan(toAdd, present, pinExisting, groups, groupsPresent);
    }

    private static ImportedEntry ToImport(BackupEntry item, bool localOnlyAvailable)
    {
        Dictionary<Guid, string>? accountPasswords = null;
        var accounts = new List<ConnectionAccount>();
        foreach (var a in item.Accounts.Where(a => !string.IsNullOrWhiteSpace(a.UserName)))
        {
            var account = ConnectionAccount.CreateNew(a.UserName, string.IsNullOrWhiteSpace(a.Domain) ? null : a.Domain);
            accounts.Add(account);
            if (!string.IsNullOrEmpty(a.Password)) (accountPasswords ??= [])[account.Id] = a.Password;
        }

        var name = string.IsNullOrWhiteSpace(item.Name) ? item.Host ?? "Restored entry" : item.Name;
        var entry = ConnectionEntry.CreateNew(name, Enum.IsDefined(item.Kind) ? item.Kind : EntryKind.Other) with
        {
            Folder = FolderTree.Normalize(item.Folder),
            Host = item.Host,
            Port = item.Port is > 0 and <= 65535 ? item.Port : ConnectionEntry.DefaultRdpPort,
            Domain = item.Domain,
            UserName = item.UserName,
            RdpSettings = item.RdpSettings,
            Notes = item.Notes,
            Accounts = accounts,
            IsLocalOnly = item.LocalOnly && localOnlyAvailable,
        };
        return new ImportedEntry(entry, string.IsNullOrEmpty(item.Password) ? null : item.Password, accountPasswords);
    }

    private static string GroupKey(SmartGroup g) => $"{FolderTree.Normalize(g.Folder)}\u001f{g.Name.Trim()}";
}
