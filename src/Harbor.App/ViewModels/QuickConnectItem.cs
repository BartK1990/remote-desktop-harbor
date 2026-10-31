namespace Harbor.App.ViewModels;

/// <summary>Which entries the tree shows besides the search filter. SPEC FR-50</summary>
public enum TreeFilter
{
    All,
    InUse,
    Pinned,
    Recent,
}

/// <summary>What a shortcut in the quick connect palette does with the highlighted result. SPEC FR-48</summary>
public enum QuickAction
{
    CopyPassword,
    CopyUserName,
    CopyHost,
    Edit,
}

/// <summary>
/// One result of the quick connect palette (SPEC FR-48): an entry, or one of its additional accounts.
/// </summary>
public sealed class QuickConnectItem(EntryViewModel entry, AccountViewModel? account)
{
    public EntryViewModel Entry { get; } = entry;

    /// <summary>The account to connect as; <c>null</c> = the entry's default account.</summary>
    public AccountViewModel? Account { get; } = account;

    public string Name => Entry.Name;
    public string? User => Account?.QualifiedUserName ?? Entry.Model.QualifiedUserName;
    public string? Address => Entry.Address;
    public string Folder => Entry.GroupName;
    public Harbor.Core.Model.EnvironmentKind? Environment => Entry.Environment;
    public string? EnvironmentLabel => Entry.EnvironmentLabel;
    public bool IsAccount => Account is not null;
    public bool IsPinned => Entry.IsPinned;
    public bool IsInUse => Account?.IsInUse ?? Entry.IsInUse;
    public string PresenceName => Account?.PresenceName ?? Entry.PresenceName;
}
