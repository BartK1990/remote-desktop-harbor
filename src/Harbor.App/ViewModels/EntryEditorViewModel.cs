using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Harbor.Core.Model;
using Harbor.Core.Rdp;
using Harbor.Core.Services;

namespace Harbor.App.ViewModels;

public sealed partial class EntryEditorViewModel : ObservableValidator
{
    private readonly ConnectionEntry _original;
    private readonly IDialogService _dialogs;

    public EntryEditorViewModel(ConnectionEntry original, bool isNew, IDialogService dialogs, IEnumerable<string> folders)
    {
        _original = original;
        _dialogs = dialogs;
        IsNew = isNew;
        Folders = folders.ToList();
        _name = original.Name;
        _folder = original.Folder;
        _isCredentialOnly = original.Kind == EntryKind.CredentialOnly;
        _host = original.Host;
        _port = original.Port;
        _domain = original.Domain;
        _userName = original.UserName;
        RdpSettings = new RdpSettingsEditorViewModel(original.RdpSettings);
        _notes = original.Notes;
        _changePassword = isNew || !original.HasPassword;
        Accounts = new ObservableCollection<AccountEditorViewModel>(original.Accounts.Select(a => new AccountEditorViewModel(a)));
    }

    public bool IsNew { get; }
    public bool HadPassword => _original.HasPassword;
    public IReadOnlyList<string> Folders { get; }
    public string Title => IsNew ? "New entry" : $"Edit – {_original.Name}";

    [ObservableProperty] private string _name;
    [ObservableProperty] private string? _folder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRdpConnection))]
    private bool _isCredentialOnly;

    [ObservableProperty] private string? _host;
    [ObservableProperty] private int _port;
    [ObservableProperty] private string? _domain;
    [ObservableProperty] private string? _userName;
    [ObservableProperty] private string? _notes;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStoredPassword), nameof(PasswordRemovalPending))]
    private bool _changePassword;

    [ObservableProperty] private string? _error;

    /// <summary>Where the last validation error is (SPEC FR-46): the tab header and the field to focus.</summary>
    public (string Tab, string Field)? ErrorLocation { get; private set; }

    /// <summary>Set from the PasswordBox in code-behind (PasswordBox cannot bind); cleared by <see cref="RemovePasswordCommand"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordRemovalPending))]
    private string _password = "";

    /// <summary>The stored password is kept: the editor shows "Stored, encrypted" with Replace and Remove. SPEC FR-46</summary>
    public bool ShowStoredPassword => HadPassword && !ChangePassword;

    /// <summary>Saving now removes the stored password.</summary>
    public bool PasswordRemovalPending => HadPassword && ChangePassword && Password.Length == 0;

    [RelayCommand]
    private void ReplacePassword() => ChangePassword = true;

    [RelayCommand]
    private void RemovePassword()
    {
        ChangePassword = true;
        Password = "";
    }

    [RelayCommand]
    private void KeepPassword()
    {
        Password = "";
        ChangePassword = !HadPassword;
    }

    /// <summary>The RDP settings form (SPEC FR-13); only used for RDP connections.</summary>
    public RdpSettingsEditorViewModel RdpSettings { get; }

    /// <summary>Accounts besides the default one above (SPEC FR-09).</summary>
    public ObservableCollection<AccountEditorViewModel> Accounts { get; }

    [RelayCommand]
    private void AddAccount() => Accounts.Add(new AccountEditorViewModel(ConnectionAccount.CreateNew("")));

    [RelayCommand]
    private void RemoveAccount(AccountEditorViewModel? account)
    {
        if (account is not null) Accounts.Remove(account);
    }

    public bool IsRdpConnection
    {
        get => !IsCredentialOnly;
        set => IsCredentialOnly = !value;
    }

    [RelayCommand]
    private void ImportRdpFile()
    {
        var path = _dialogs.PickRdpFile();
        if (path is null) return;
        var imported = RdpFile.Import(System.IO.File.ReadAllText(path));
        Host = imported.Host;
        Port = imported.Port;
        Domain = imported.Domain ?? Domain;
        UserName = imported.UserName ?? UserName;
        RdpSettings.Load(imported.RdpSettings);
        IsCredentialOnly = false;
        if (string.IsNullOrWhiteSpace(Name) || Name == "New connection")
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(path);
        }
    }

    public bool Validate()
    {
        NormalizeFields();
        (Error, ErrorLocation) = (null, null);
        if (string.IsNullOrWhiteSpace(Name)) Fail("Name is required.", "General", "NameBox");
        else if (IsRdpConnection && string.IsNullOrWhiteSpace(Host)) Fail("Host is required for an RDP connection.", "General", "HostBox");
        else if (Port is < 1 or > 65535) Fail("Port must be between 1 and 65535.", "General", "PortBox");
        else if (Accounts.Any(a => string.IsNullOrWhiteSpace(a.UserName))) Fail("Each additional account needs a user name.", "Accounts", "");
        else if (DuplicateAccount() is { } duplicate) Fail($"The account {duplicate} is listed more than once.", "Accounts", "");
        else if (IsRdpConnection && RdpSettings.Validate() is { } rdpError) Fail(rdpError, "Display", "");
        return Error is null;
    }

    private void Fail(string message, string tab, string field)
    {
        ErrorLocation = (tab, field);
        Error = message;
    }

    /// <summary>
    /// Splits what was pasted into one box (SPEC FR-46): <c>host:port</c> in Host (when Port still has the default, or
    /// the same port) and <c>DOMAIN\user</c> in User name (when Domain is empty), for the entry and every account.
    /// </summary>
    private void NormalizeFields()
    {
        if (Host?.Trim() is { Length: > 0 } host && EntryInput.SplitHostPort(host) is (var h, { } p and >= 1 and <= 65535)
            && (Port == ConnectionEntry.DefaultRdpPort || Port == p))
        {
            (Host, Port) = (h, p);
        }

        if (string.IsNullOrWhiteSpace(Domain) && UserName?.Trim() is { Length: > 0 } user && EntryInput.SplitDomainUser(user) is ({ } d, var u))
        {
            (Domain, UserName) = (d, u);
        }

        foreach (var account in Accounts)
        {
            if (string.IsNullOrWhiteSpace(account.Domain) && EntryInput.SplitDomainUser(account.UserName.Trim()) is ({ } ad, var au))
            {
                (account.Domain, account.UserName) = (ad, au);
            }
        }
    }

    private string? DuplicateAccount()
    {
        var entry = BuildEntry();
        return entry.Accounts.Select(entry.QualifiedUserNameFor).Prepend(entry.QualifiedUserName).OfType<string>()
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1)?.Key;
    }

    public ConnectionEntry BuildEntry() => _original with
    {
        Name = Name.Trim(),
        Folder = string.IsNullOrWhiteSpace(Folder) ? null : Folder.Trim(),
        Kind = IsCredentialOnly ? EntryKind.CredentialOnly : EntryKind.RdpConnection,
        Host = string.IsNullOrWhiteSpace(Host) ? null : Host.Trim(),
        Port = Port,
        Domain = string.IsNullOrWhiteSpace(Domain) ? null : Domain.Trim(),
        UserName = string.IsNullOrWhiteSpace(UserName) ? null : UserName.Trim(),
        RdpSettings = IsCredentialOnly ? null : RdpSettings.Build() is { Length: > 0 } rdp ? rdp : null,
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes,
        Accounts = Accounts.Select(a => a.Build()).ToList(),
    };

    public SecretUpdate BuildSecretUpdate() => ToSecretUpdate(ChangePassword, Password);

    public IReadOnlyDictionary<Guid, SecretUpdate> BuildAccountSecretUpdates() =>
        Accounts.ToDictionary(a => a.Id, a => ToSecretUpdate(a.ChangePassword, a.Password));

    internal static SecretUpdate ToSecretUpdate(bool change, string password) =>
        !change ? SecretUpdate.Keep
        : password.Length == 0 ? SecretUpdate.Clear
        : SecretUpdate.Set(password);
}

/// <summary>An additional account in the entry editor (SPEC FR-09).</summary>
public sealed partial class AccountEditorViewModel : ObservableObject
{
    private readonly ConnectionAccount _original;

    public AccountEditorViewModel(ConnectionAccount original)
    {
        _original = original;
        _domain = original.Domain;
        _userName = original.UserName;
        _changePassword = !original.HasPassword;
    }

    public Guid Id => _original.Id;
    public bool HadPassword => _original.HasPassword;

    [ObservableProperty] private string? _domain;
    [ObservableProperty] private string _userName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStoredPassword))]
    private bool _changePassword;

    /// <summary>Set from the PasswordBox in code-behind (PasswordBox cannot bind).</summary>
    public string Password { get; set; } = "";

    /// <summary>The stored password is kept: the row shows "Stored" with Replace. SPEC FR-46</summary>
    public bool ShowStoredPassword => HadPassword && !ChangePassword;

    [RelayCommand]
    private void ReplacePassword() => ChangePassword = true;

    [RelayCommand]
    private void KeepPassword()
    {
        Password = "";
        ChangePassword = false;
    }

    public ConnectionAccount Build() => _original with
    {
        Domain = string.IsNullOrWhiteSpace(Domain) ? null : Domain.Trim(),
        UserName = UserName.Trim(),
    };
}
