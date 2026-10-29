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
        _rdpSettings = original.RdpSettings;
        _notes = original.Notes;
        _changePassword = isNew || !original.HasPassword;
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
    [ObservableProperty] private string? _rdpSettings;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private bool _changePassword;
    [ObservableProperty] private string? _error;

    /// <summary>Set from the PasswordBox in code-behind (PasswordBox cannot bind).</summary>
    public string Password { get; set; } = "";

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
        RdpSettings = imported.RdpSettings;
        IsCredentialOnly = false;
        if (string.IsNullOrWhiteSpace(Name) || Name == "New connection")
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(path);
        }
    }

    public bool Validate()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(Name)) Error = "Name is required.";
        else if (IsRdpConnection && string.IsNullOrWhiteSpace(Host)) Error = "Host is required for an RDP connection.";
        else if (Port is < 1 or > 65535) Error = "Port must be between 1 and 65535.";
        return Error is null;
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
        RdpSettings = IsCredentialOnly || string.IsNullOrWhiteSpace(RdpSettings) ? null : RdpSettings,
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes,
    };

    public SecretUpdate BuildSecretUpdate() =>
        !ChangePassword ? SecretUpdate.Keep
        : Password.Length == 0 ? SecretUpdate.Clear
        : SecretUpdate.Set(Password);
}
