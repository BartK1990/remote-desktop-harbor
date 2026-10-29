using System.Collections.ObjectModel;
using System.Windows;
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

    // Tree state (SPEC FR-02). Folders are reused across rebuilds so expansion and scroll position survive syncs.
    // Collapsed folders are remembered separately while searching, where every folder with matches starts expanded.
    private Dictionary<string, FolderViewModel> _folders = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly HashSet<string> _collapsed;
    private readonly HashSet<string> _collapsedWhileSearching = new(StringComparer.CurrentCultureIgnoreCase);
    private bool _rebuildingTree;

    public MainViewModel(HarborSettings settings, IDialogService dialogs)
    {
        Settings = settings;
        _dialogs = dialogs;
        _collapsed = new HashSet<string>(settings.CollapsedFolders, StringComparer.CurrentCultureIgnoreCase);
        _theme = settings.Theme;
        _syncTimer.Tick += async (_, _) => await SyncAsync();
        _presenceTimer.Tick += async (_, _) => await RefreshPresenceAsync();
    }

    public HarborSettings Settings { get; }
    public int ActiveLaunchCount => _leases.Count;
    public ObservableCollection<EntryViewModel> Entries { get; } = [];

    /// <summary>Top level of the entry tree: folders, then entries without a folder. Only entries matching <see cref="SearchText"/>.</summary>
    public TreeChildren Tree { get; } = [];

    [ObservableProperty] private string _searchText = "";

    /// <summary>Choices for the theme selector in the toolbar (SPEC FR-08).</summary>
    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    /// <summary>Colour theme; applied immediately and remembered across restarts (SPEC FR-08).</summary>
    [ObservableProperty] private AppTheme _theme;

    /// <summary>The selected tree row (folder or entry); kept in sync with <see cref="Selected"/>.</summary>
    [ObservableProperty] private TreeNodeViewModel? _selectedNode;

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

    private bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    partial void OnSearchTextChanged(string value)
    {
        _collapsedWhileSearching.Clear();
        RebuildTree();
        UpdateCountText();
    }

    partial void OnSelectedNodeChanged(TreeNodeViewModel? oldValue, TreeNodeViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        Selected = newValue as EntryViewModel;
    }

    partial void OnSelectedChanged(EntryViewModel? value)
    {
        if (value is not null && !ReferenceEquals(SelectedNode, value))
        {
            RevealFolder(value.Model.Folder);
            SelectedNode = value;
        }
        else if (value is null && SelectedNode is EntryViewModel)
        {
            SelectedNode = null;
        }
    }

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

        RebuildTree();
        Selected = Entries.FirstOrDefault(e => e.Model.Id == selectedId) ?? Selected;
        UpdateCountText();
    }

    private void UpdateCountText()
    {
        if (IsOffline) return; // keep the offline reason visible
        StatusText = IsSearching ? $"{CountMatches()} of {Entries.Count} entries" : $"{Entries.Count} entries";
    }

    private int CountMatches() => Tree.Sum(n => n is FolderViewModel f ? f.Count : 1);

    // ------------------------------------------------------------------ tree (SPEC FR-02)

    /// <summary>Rebuilds the tree from <see cref="Entries"/> and <see cref="SearchText"/>, reusing existing rows.</summary>
    private void RebuildTree()
    {
        var root = FolderTree.Build(Entries.Where(e => e.Matches(SearchText)), e => e.Model.Folder, e => e.Name);
        var previous = _folders;
        _folders = new Dictionary<string, FolderViewModel>(StringComparer.CurrentCultureIgnoreCase);

        // The selected row and the folders above it: collections holding them must not be reset (see TreeChildren).
        var selectionPath = new HashSet<TreeNodeViewModel>();
        if (SelectedNode is { } selected)
        {
            selectionPath.Add(selected);
            var folder = selected is FolderViewModel f ? f.Path : ((EntryViewModel)selected).Model.Folder;
            foreach (var path in FolderTree.AncestorPaths(folder))
            {
                if (previous.TryGetValue(path, out var ancestor)) selectionPath.Add(ancestor);
            }
        }

        _rebuildingTree = true;
        try
        {
            Tree.ReplaceWith(BuildRows(root, previous, selectionPath), Tree.Any(selectionPath.Contains));

            // The TreeView may have moved its selection while rows were replaced; the view model's selection stands.
            foreach (var folder in _folders.Values) folder.IsSelected = ReferenceEquals(folder, SelectedNode);
            if (SelectedNode is not null) SelectedNode.IsSelected = true;
        }
        finally
        {
            _rebuildingTree = false;
        }
    }

    /// <summary>Called by the view when the user selects a row. Ignored while rows are replaced by <see cref="RebuildTree"/>.</summary>
    public void SelectFromTree(TreeNodeViewModel? node)
    {
        if (!_rebuildingTree) SelectedNode = node;
    }

    private List<TreeNodeViewModel> BuildRows(
        FolderTreeNode<EntryViewModel> node, Dictionary<string, FolderViewModel> previous, HashSet<TreeNodeViewModel> selectionPath)
    {
        var rows = new List<TreeNodeViewModel>(node.Folders.Count + node.Items.Count);
        var collapsed = IsSearching ? _collapsedWhileSearching : _collapsed;
        foreach (var child in node.Folders)
        {
            if (!previous.TryGetValue(child.Path, out var folder))
            {
                folder = new FolderViewModel(child.Path, OnFolderExpandedChanged);
            }

            folder.Name = child.Name;
            folder.Count = child.TotalCount;
            folder.IsExpanded = !collapsed.Contains(child.Path);
            folder.Children.ReplaceWith(BuildRows(child, previous, selectionPath), folder.Children.Any(selectionPath.Contains));
            _folders[child.Path] = folder;
            rows.Add(folder);
        }

        rows.AddRange(node.Items);
        return rows;
    }

    private void OnFolderExpandedChanged(FolderViewModel folder)
    {
        if (_rebuildingTree) return;
        var collapsed = IsSearching ? _collapsedWhileSearching : _collapsed;
        if (folder.IsExpanded) collapsed.Remove(folder.Path);
        else collapsed.Add(folder.Path);
    }

    /// <summary>Expands <paramref name="folder"/> and its ancestors so an entry in it is visible.</summary>
    private void RevealFolder(string? folder)
    {
        foreach (var path in FolderTree.AncestorPaths(folder))
        {
            if (_folders.TryGetValue(path, out var vm)) vm.IsExpanded = true;
            else (IsSearching ? _collapsedWhileSearching : _collapsed).Remove(path);
        }
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var folder in _folders.Values) folder.IsExpanded = true;
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var folder in _folders.Values) folder.IsExpanded = false;
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    /// <summary>The folder a new entry goes to: the selected folder, or the folder of the selected entry.</summary>
    private string? SelectedFolderPath => SelectedNode switch
    {
        FolderViewModel f => f.Path,
        EntryViewModel e => e.Model.Folder,
        _ => null,
    };

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
        var editor = new EntryEditorViewModel(template with { Folder = SelectedFolderPath }, isNew: true, _dialogs, Folders());
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
                RevealFolder(saved.Folder); // also when an already selected entry moved to another folder
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

    partial void OnThemeChanged(AppTheme value)
    {
        ThemeService.Apply(value);
        if (Settings.Theme == value) return;
        Settings.Theme = value;
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Log.Error("Theme could not be saved", ex);
        }
    }

    /// <summary>Remembers which folders are collapsed for the next start.</summary>
    private void SaveTreeState()
    {
        var collapsed = _collapsed.Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        if (collapsed.SequenceEqual(Settings.CollapsedFolders, StringComparer.CurrentCultureIgnoreCase)) return;
        Settings.CollapsedFolders = collapsed;
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Log.Error("Tree state could not be saved", ex);
        }
    }

    private List<string> Folders() =>
        Entries.Select(e => e.Model.Folder).OfType<string>().Distinct(StringComparer.CurrentCultureIgnoreCase).Order().ToList();

    public async ValueTask DisposeAsync()
    {
        SaveTreeState();
        _syncTimer.Stop();
        _presenceTimer.Stop();
        // End presence records now; otherwise others would see us as connected until the TTL runs out.
        await Task.WhenAll(_leases.ToList().Select(l => l.DisposeAsync().AsTask()));
        _host?.Dispose();
    }
}
