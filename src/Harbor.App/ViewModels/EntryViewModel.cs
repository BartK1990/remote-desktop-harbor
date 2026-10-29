using CommunityToolkit.Mvvm.ComponentModel;
using Harbor.Core.Model;

namespace Harbor.App.ViewModels;

public sealed partial class EntryViewModel : TreeNodeViewModel
{
    public const string NoFolder = "(No folder)";

    public EntryViewModel(ConnectionEntry model)
    {
        _model = model;
        DefaultAccount = new AccountViewModel(this, null);
        UpdateAccounts();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(GroupName), nameof(Subtitle), nameof(KindLabel), nameof(HasPassword), nameof(CanLaunch),
        nameof(HasAccounts))]
    private ConnectionEntry _model;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInUse), nameof(PresenceText), nameof(PresenceDetails))]
    private IReadOnlyList<ActiveSession> _activeSessions = [];

    /// <summary>The entry's own user name and password; selected when the entry row itself is selected.</summary>
    public AccountViewModel DefaultAccount { get; }

    /// <summary>
    /// One row per account (default first) when the entry has additional accounts; empty otherwise, so a single-account
    /// entry stays one row. SPEC FR-09
    /// </summary>
    public TreeChildren Children { get; } = [];

    public bool HasAccounts => Model.Accounts.Count > 0;

    public string Name => Model.Name;
    public string GroupName => string.IsNullOrWhiteSpace(Model.Folder) ? NoFolder : Model.Folder;
    public bool HasPassword => Model.HasPassword;
    public bool CanLaunch => Model.CanLaunch;
    public string KindLabel => Model.Kind == EntryKind.RdpConnection ? "RDP connection" : "Credentials only";

    public string Subtitle
    {
        get
        {
            var host = Model.Host is null ? null : Model.Port == ConnectionEntry.DefaultRdpPort ? Model.Host : $"{Model.Host}:{Model.Port}";
            var user = HasAccounts ? $"{Model.Accounts.Count + 1} accounts" : Model.QualifiedUserName;
            return (user, host) switch
            {
                (null, null) => KindLabel,
                (null, { } h) => h,
                ({ } u, null) => u,
                ({ } u, { } h) => $"{u} @ {h}",
            };
        }
    }

    public bool IsInUse => ActiveSessions.Count > 0;

    public string PresenceText => ActiveSessions.Count switch
    {
        0 => "",
        1 => $"In use by {ActiveSessions[0].DisplayName ?? ActiveSessions[0].UserName}",
        var n => $"In use by {n} users",
    };

    public string PresenceDetails => Describe(ActiveSessions);

    internal static string Describe(IEnumerable<ActiveSession> sessions) => string.Join(Environment.NewLine, sessions.Select(s =>
        $"{s.Describe()}{(s.AccountName is { } account ? $" as {account}" : "")} on {s.MachineName}, since {s.StartedUtc.ToLocalTime():t}"));

    /// <summary>The row for <paramref name="accountId"/> (<c>null</c> = default account), or <c>null</c> if it is gone.</summary>
    public AccountViewModel? FindAccount(Guid? accountId) =>
        accountId is null ? DefaultAccount : Children.OfType<AccountViewModel>().FirstOrDefault(a => a.AccountId == accountId);

    public bool Matches(string filter) =>
        string.IsNullOrWhiteSpace(filter)
        || Contains(Model.Name, filter) || Contains(Model.Host, filter) || Contains(Model.UserName, filter)
        || Contains(Model.Folder, filter) || Contains(Model.Notes, filter)
        || Model.Accounts.Any(a => Contains(a.UserName, filter));

    private static bool Contains(string? value, string filter) =>
        value is not null && value.Contains(filter, StringComparison.CurrentCultureIgnoreCase);

    partial void OnModelChanged(ConnectionEntry value) => UpdateAccounts();

    partial void OnActiveSessionsChanged(IReadOnlyList<ActiveSession> value) => UpdateAccountSessions();

    /// <summary>Syncs the account rows with the model, reusing rows by account id so selection and badges survive.</summary>
    private void UpdateAccounts()
    {
        var existing = Children.OfType<AccountViewModel>().Where(a => !a.IsDefault).ToDictionary(a => a.AccountId!.Value);
        var rows = new List<TreeNodeViewModel>();
        if (HasAccounts)
        {
            rows.Add(DefaultAccount);
            foreach (var account in Model.Accounts)
            {
                if (existing.TryGetValue(account.Id, out var row)) row.Account = account;
                else row = new AccountViewModel(this, account);
                rows.Add(row);
            }
        }

        Children.ReplaceWith(rows, Children.Any(c => c.IsSelected));
        DefaultAccount.Refresh();
        foreach (var row in rows.OfType<AccountViewModel>().Where(r => !r.IsDefault)) row.Refresh(); // names use the entry's domain
        UpdateAccountSessions();
    }

    private void UpdateAccountSessions()
    {
        foreach (var row in Children.OfType<AccountViewModel>())
        {
            // Records from older clients name no account; they only show on the entry row.
            row.ActiveSessions = ActiveSessions.Where(s => s.AccountName is not null && s.UsesAccount(row.QualifiedUserName)).ToList();
        }
    }
}

/// <summary>
/// An account row under an entry with several accounts (SPEC FR-09). <see cref="Account"/> is <c>null</c> for the
/// entry's default account.
/// </summary>
public sealed partial class AccountViewModel(EntryViewModel entry, ConnectionAccount? account) : TreeNodeViewModel
{
    public EntryViewModel Entry { get; } = entry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(QualifiedUserName), nameof(HasPassword))]
    private ConnectionAccount? _account = account;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInUse), nameof(PresenceText), nameof(PresenceDetails))]
    private IReadOnlyList<ActiveSession> _activeSessions = [];

    public bool IsDefault => Account is null;
    public Guid? AccountId => Account?.Id;
    public string? QualifiedUserName => Entry.Model.QualifiedUserNameFor(Account);
    public string Name => QualifiedUserName ?? "(no user name)";
    public bool HasPassword => Account?.HasPassword ?? Entry.Model.HasPassword;

    public bool IsInUse => ActiveSessions.Count > 0;
    public string PresenceText => IsInUse ? $"In use by {ActiveSessions[0].DisplayName ?? ActiveSessions[0].UserName}" : "";
    public string PresenceDetails => EntryViewModel.Describe(ActiveSessions);

    /// <summary>Raises change notifications after the entry's model changed (domain and default user come from it).</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
