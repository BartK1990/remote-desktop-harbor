using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Harbor.App.Infrastructure;
using Harbor.Core;
using Harbor.Core.Model;
using Harbor.Core.Services;
using Harbor.Core.Storage;

namespace Harbor.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly TimeSpan PresenceRefreshInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ClipboardClearDelay = TimeSpan.FromSeconds(30);

    private readonly IDialogService _dialogs;
    private readonly List<PresenceLease> _leases = [];
    private readonly DispatcherTimer _syncTimer = new();
    private readonly DispatcherTimer _presenceTimer = new() { Interval = PresenceRefreshInterval };
    private AppHost? _host;

    public MainViewModel(HarborSettings settings, IDialogService dialogs)
    {
        Settings = settings;
        _dialogs = dialogs;
        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(EntryViewModel.GroupName)));
        EntriesView.Filter = o => o is EntryViewModel e && e.Matches(SearchText);
        _syncTimer.Tick += async (_, _) => await SyncAsync();
        _presenceTimer.Tick += async (_, _) => await RefreshPresenceAsync();
    }

    public HarborSettings Settings { get; }
    public int ActiveLaunchCount => _leases.Count;
    public ObservableCollection<EntryViewModel> Entries { get; } = [];
    public ICollectionView EntriesView { get; }

    [ObservableProperty] private string _searchText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(EditCommand), nameof(DeleteCommand), nameof(CopyPasswordCommand), nameof(CopyUserNameCommand))]
    private EntryViewModel? _selected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(EditCommand), nameof(DeleteCommand), nameof(ImportRdpCommand), nameof(ConnectCommand))]
    private bool _isReady;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(EditCommand), nameof(DeleteCommand), nameof(ImportRdpCommand))]
    private bool _canWrite;

    [ObservableProperty] private string _userText = "Signing in…";
    [ObservableProperty] private string _storeText = "";
    [ObservableProperty] private bool _isOffline;
    [ObservableProperty] private string _statusText = "Starting…";

    partial void OnSearchTextChanged(string value) => EntriesView.Refresh();

    // ------------------------------------------------------------------ lifecycle

    public async Task InitializeAsync(Func<nint> parentWindow, Func<bool, string?> passphrasePrompt)
    {
        try
        {
            _host = await Task.Run(() => AppHost.StartAsync(Settings, parentWindow, passphrasePrompt));
        }
        catch (Exception ex)
        {
            Log.Error("Startup failed", ex);
            StatusText = "Not connected";
            UserText = "";
            _dialogs.ShowError("Remote Desktop Harbor could not start", ex.Message + Environment.NewLine + Environment.NewLine +
                "Open Settings to change the storage or sign-in configuration.");
            return;
        }

        _host.Workspace.StateChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(UpdateState);
        UserText = _host.User.ToString();
        IsReady = true;
        UpdateState();
        await ReloadAsync();

        if (_host.Workspace.IsRemote)
        {
            _syncTimer.Interval = TimeSpan.FromSeconds(Math.Max(10, Settings.RemoteSyncIntervalSeconds));
            _syncTimer.Start();
            _presenceTimer.Start();
            await RefreshPresenceAsync();
        }
    }

    private void UpdateState()
    {
        if (_host is null) return;
        var ws = _host.Workspace;
        if (Settings.RemoteKeyConvert && ws.RemoteKeyConverted)
        {
            // One-time request done (SPEC FR-28); do not convert again on the next start.
            Settings.RemoteKeyConvert = false;
            SettingsStore.Save(Settings);
            StatusText = "Shared vault protection changed.";
        }

        CanWrite = ws.CanWrite;
        IsOffline = ws.IsRemote && !ws.IsOnline;
        StoreText = !ws.IsRemote
            ? "Local vault"
            : ws.IsOnline
                ? $"Shared database · online{(ws.LastSyncUtc is { } t ? $" · synced {t.ToLocalTime():T}" : "")}"
                : "Shared database · OFFLINE (read-only cached copy)";
        if (IsOffline && ws.LastRemoteError is { } err)
        {
            StatusText = $"Offline: {err.Message}";
        }
    }

    private async Task ReloadAsync()
    {
        if (_host is null) return;
        var selectedId = Selected?.Model.Id;
        var entries = await _host.Workspace.ListAsync();
        var byId = Entries.ToDictionary(e => e.Model.Id);

        Entries.Clear();
        foreach (var entry in entries)
        {
            var vm = byId.TryGetValue(entry.Id, out var existing) ? existing : new EntryViewModel(entry);
            vm.Model = entry;
            Entries.Add(vm);
        }

        Selected = Entries.FirstOrDefault(e => e.Model.Id == selectedId) ?? Selected;
        if (!IsOffline) StatusText = $"{Entries.Count} entries";
    }

    private async Task SyncAsync()
    {
        if (_host is null) return;
        await _host.Workspace.SyncAsync();
        UpdateState();
        await ReloadAsync();
    }

    private async Task RefreshPresenceAsync()
    {
        if (_host is null || !_host.Presence.IsEnabled) return;
        // While offline nobody's presence can be known; show none rather than stale data.
        var sessions = IsOffline ? [] : await _host.Presence.ListActiveAsync();
        var byConnection = sessions.ToLookup(s => s.ConnectionId);
        foreach (var e in Entries)
        {
            e.ActiveSessions = byConnection[e.Model.Id].ToList();
        }
    }

    // ------------------------------------------------------------------ commands

    private bool HasSelection => IsReady && Selected is not null;
    private bool CanEdit => IsReady && CanWrite;
    private bool CanEditSelection => CanEdit && Selected is not null;
    private bool CanConnectSelection => HasSelection && Selected!.CanLaunch;

    [RelayCommand(CanExecute = nameof(CanConnectSelection))]
    private async Task ConnectAsync()
    {
        if (_host is null || Selected is not { } vm) return;
        var entry = vm.Model;

        if (vm.IsInUse && !_dialogs.Confirm("Connection in use",
                $"{vm.PresenceDetails}{Environment.NewLine}{Environment.NewLine}Connect anyway? This may disconnect the other user."))
        {
            return;
        }

        try
        {
            var password = _host.Workspace.RevealPassword(entry);
            var session = await _host.Launcher.LaunchAsync(entry, password);
            StatusText = $"Launched {entry.Name}";
            Log.Info($"Launched {entry.Id}");

            var lease = await _host.Presence.BeginAsync(entry.Id, _host.User);
            _leases.Add(lease);
            await RefreshPresenceAsync();
            _ = TrackSessionAsync(session, lease);
        }
        catch (Exception ex)
        {
            Log.Error("Launch failed", ex);
            _dialogs.ShowError("Could not start Remote Desktop", ex.Message);
        }
    }

    private async Task TrackSessionAsync(Core.Rdp.IRdpSession session, PresenceLease lease)
    {
        try
        {
            await session.WaitForExitAsync();
        }
        finally
        {
            _leases.Remove(lease);
            await lease.DisposeAsync();
            await RefreshPresenceAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task AddAsync() => EditNewAsync(ConnectionEntry.CreateNew("New connection"), importFirst: false);

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task ImportRdpAsync() => EditNewAsync(ConnectionEntry.CreateNew("New connection"), importFirst: true);

    private async Task EditNewAsync(ConnectionEntry template, bool importFirst)
    {
        var editor = new EntryEditorViewModel(template with { Folder = Selected?.Model.Folder }, isNew: true, _dialogs, Folders());
        if (importFirst)
        {
            editor.ImportRdpFileCommand.Execute(null);
        }

        await RunEditorAsync(editor);
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private Task EditAsync() =>
        Selected is { } vm ? RunEditorAsync(new EntryEditorViewModel(vm.Model, isNew: false, _dialogs, Folders())) : Task.CompletedTask;

    private async Task RunEditorAsync(EntryEditorViewModel editor)
    {
        if (_host is null) return;
        while (_dialogs.EditEntry(editor))
        {
            try
            {
                var saved = await _host.Workspace.SaveAsync(editor.BuildEntry(), editor.BuildSecretUpdate());
                await ReloadAsync();
                Selected = Entries.FirstOrDefault(e => e.Model.Id == saved.Id);
                StatusText = $"Saved {saved.Name}";
                return;
            }
            catch (Exception ex) when (ex is ConcurrencyConflictException or WorkspaceOfflineException)
            {
                _dialogs.ShowError("Not saved", ex.Message);
                if (ex is ConcurrencyConflictException) await SyncAsync();
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Save failed", ex);
                editor.Error = ex.Message; // reopen the editor with the message, keeping the user's input
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private async Task DeleteAsync()
    {
        if (_host is null || Selected is not { } vm) return;
        if (!_dialogs.Confirm("Delete entry", $"Delete '{vm.Name}'?{(_host.Workspace.IsRemote ? " It will be removed for everyone using the shared database." : "")}"))
        {
            return;
        }

        try
        {
            await _host.Workspace.DeleteAsync(vm.Model);
            Selected = null;
            await ReloadAsync();
        }
        catch (Exception ex) when (ex is ConcurrencyConflictException or WorkspaceOfflineException)
        {
            _dialogs.ShowError("Not deleted", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopyPassword()
    {
        if (_host is null || Selected is not { } vm) return;
        var password = _host.Workspace.RevealPassword(vm.Model);
        if (password is null)
        {
            StatusText = "This entry has no stored password.";
            return;
        }

        Clipboard.SetDataObject(password, copy: false);
        StatusText = $"Password copied – the clipboard is cleared in {ClipboardClearDelay.TotalSeconds:0} s.";
        var timer = new DispatcherTimer { Interval = ClipboardClearDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                if (Clipboard.ContainsText() && Clipboard.GetText() == password) Clipboard.Clear();
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
            }
        };
        timer.Start();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopyUserName()
    {
        if (Selected?.Model.QualifiedUserName is { } user)
        {
            Clipboard.SetDataObject(user, copy: false);
            StatusText = "User name copied.";
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        StatusText = "Syncing…";
        await SyncAsync();
        await RefreshPresenceAsync();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = new SettingsViewModel(Settings);
        if (!_dialogs.EditSettings(vm)) return;
        vm.Apply();
        SettingsStore.Save(Settings);
        if (_dialogs.Confirm("Restart required", "Settings were saved. Restart Remote Desktop Harbor now to apply them?"))
        {
            System.Diagnostics.Process.Start(Environment.ProcessPath!);
            Application.Current.Shutdown();
        }
    }

    private List<string> Folders() =>
        Entries.Select(e => e.Model.Folder).OfType<string>().Distinct(StringComparer.CurrentCultureIgnoreCase).Order().ToList();

    public async ValueTask DisposeAsync()
    {
        _syncTimer.Stop();
        _presenceTimer.Stop();
        // End presence records now; otherwise others would see us as connected until the TTL runs out.
        await Task.WhenAll(_leases.ToList().Select(l => l.DisposeAsync().AsTask()));
        _host?.Dispose();
    }
}
