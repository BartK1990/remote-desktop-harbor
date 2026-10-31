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

    // Pinned entries of this user (SPEC FR-50), kept in the settings.
    private readonly HashSet<Guid> _pinned;

    public MainViewModel(HarborSettings settings, IDialogService dialogs)
    {
        Settings = settings;
        _dialogs = dialogs;
        _collapsed = new HashSet<string>(settings.CollapsedFolders, StringComparer.CurrentCultureIgnoreCase);
        _pinned = [.. settings.PinnedEntryIds];
        _syncTimer.Tick += async (_, _) => await SyncAsync();
        _presenceTimer.Tick += async (_, _) => await RefreshPresenceAsync();
    }

    public HarborSettings Settings { get; }
    public int ActiveLaunchCount => _leases.Count;
    public ObservableCollection<EntryViewModel> Entries { get; } = [];

    /// <summary>Top level of the entry tree: folders, then entries without a folder. Only entries matching <see cref="SearchText"/>.</summary>
    public TreeChildren Tree { get; } = [];

    [ObservableProperty] private string _searchText = "";

    /// <summary>All entries, or only those in use, pinned or recently connected (SPEC FR-50).</summary>
    [ObservableProperty] private TreeFilter _treeFilter;

    partial void OnTreeFilterChanged(TreeFilter value)
    {
        RebuildTree();
        UpdateCountText();
    }

    private bool PassesFilter(EntryViewModel e) => TreeFilter switch
    {
        TreeFilter.InUse => e.IsInUse,
        TreeFilter.Pinned => e.IsPinned,
        TreeFilter.Recent => Settings.RecentEntryIds.Contains(e.Model.Id),
        _ => true,
    };

    public string PinText => Selected?.IsPinned == true ? "Unpin" : "Pin";

    // ------------------------------------------------------------------ quick connect (SPEC FR-48)

    [ObservableProperty] private bool _isQuickConnectOpen;
    [ObservableProperty] private string _quickQuery = "";
    [ObservableProperty] private QuickConnectItem? _selectedQuickResult;

    public ObservableCollection<QuickConnectItem> QuickResults { get; } = [];

    partial void OnQuickQueryChanged(string value) => UpdateQuickResults();

    [RelayCommand]
    private void OpenQuickConnect()
    {
        QuickQuery = "";
        UpdateQuickResults();
        IsQuickConnectOpen = true;
    }

    [RelayCommand]
    private void CloseQuickConnect() => IsQuickConnectOpen = false;

    private void UpdateQuickResults()
    {
        var recent = Settings.RecentEntryIds;
        var candidates = new List<QuickSearchCandidate<QuickConnectItem>>();
        foreach (var e in Entries)
        {
            var rank = recent.IndexOf(e.Model.Id) is var i and >= 0 ? i : (int?)null;
            candidates.Add(new(new QuickConnectItem(e, null), e.Name, e.Address, e.Model.QualifiedUserName, e.Model.Folder, e.IsPinned, rank));
            foreach (var account in e.Children.OfType<AccountViewModel>().Where(a => !a.IsDefault))
            {
                candidates.Add(new(new QuickConnectItem(e, account), e.Name, e.Address, account.QualifiedUserName, e.Model.Folder, e.IsPinned, rank));
            }
        }

        QuickResults.Clear();
        foreach (var item in QuickSearch.Rank(candidates, QuickQuery)) QuickResults.Add(item);
        SelectedQuickResult = QuickResults.FirstOrDefault();
    }

    /// <summary>Enter in the palette: select the result in the tree and connect with its account.</summary>
    [RelayCommand]
    private async Task QuickConnectAcceptAsync()
    {
        if (!RevealQuickResult()) return;
        if (ConnectCommand.CanExecute(null)) await ConnectCommand.ExecuteAsync(null);
    }

    /// <summary>Shift+Enter in the palette: only select the result in the tree.</summary>
    [RelayCommand]
    private void QuickConnectReveal() => RevealQuickResult();

    private bool RevealQuickResult()
    {
        IsQuickConnectOpen = false;
        if (SelectedQuickResult is not { } item) return false;
        if (TreeFilter != TreeFilter.All && !PassesFilter(item.Entry)) TreeFilter = TreeFilter.All;
        if (!item.Entry.Matches(SearchText)) SearchText = "";
        RevealFolder(item.Entry.Model.Folder);
        if (item.Account is not null) item.Entry.IsExpanded = true;
        SelectedNode = (TreeNodeViewModel?)item.Account ?? item.Entry;
        return true;
    }

    // ------------------------------------------------------------------ pinned and recent (SPEC FR-50)

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void TogglePin()
    {
        if (Selected is not { } vm) return;
        vm.IsPinned = !vm.IsPinned;
        if (vm.IsPinned) _pinned.Add(vm.Model.Id);
        else _pinned.Remove(vm.Model.Id);
        Settings.PinnedEntryIds = [.. _pinned];
        SaveSettingsQuietly("Pinned entries");
        OnPropertyChanged(nameof(PinText));
        StatusText = vm.IsPinned ? $"Pinned {vm.Name}" : $"Unpinned {vm.Name}";
        if (TreeFilter == TreeFilter.Pinned) RebuildTree();
    }

    private void RememberRecent(Guid id)
    {
        Settings.RecentEntryIds = RecentList.Push(Settings.RecentEntryIds, id);
        SaveSettingsQuietly("Recent entries");
        if (TreeFilter == TreeFilter.Recent) RebuildTree();
    }

    private void SaveSettingsQuietly(string what)
    {
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Log.Error($"{what} could not be saved", ex);
        }
    }

    /// <summary>The selected tree row (folder, entry or account); kept in sync with <see cref="Selected"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedAccount), nameof(ConnectText), nameof(TakeoverWarning))]
    private TreeNodeViewModel? _selectedNode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(EditCommand), nameof(DeleteCommand), nameof(CopyPasswordCommand), nameof(CopyUserNameCommand),
        nameof(CopyHostCommand), nameof(ConnectAsCommand),
        nameof(TogglePinCommand))]
    [NotifyPropertyChangedFor(nameof(PinText))]
    private EntryViewModel? _selected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(EditCommand), nameof(DeleteCommand), nameof(ImportRdpCommand), nameof(ConnectCommand), nameof(ConnectAsCommand))]
    private bool _isReady;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(EditCommand), nameof(DeleteCommand), nameof(ImportRdpCommand))]
    private bool _canWrite;

    [ObservableProperty] private string _userText = "Signing in…";
    [ObservableProperty] private string _storeText = "";
    [ObservableProperty] private bool _isOffline;
    [ObservableProperty] private bool _isRemote;
    [ObservableProperty] private string _statusText = "Starting…";

    /// <summary>"Connect as svc-admin": the account Connect uses, so nobody connects with the wrong one. SPEC FR-47</summary>
    public string ConnectText => SelectedAccount?.QualifiedUserName is { } user ? $"Connect as {user[(user.LastIndexOf('\\') + 1)..]}" : "Connect";

    /// <summary>
    /// Shown above the active-sessions table: whether connecting with the selected account would take a session over.
    /// SPEC FR-33, FR-47
    /// </summary>
    public string? TakeoverWarning
    {
        get
        {
            if (Selected is not { IsInUse: true } vm || SelectedAccount is not { } account) return null;
            var name = account.QualifiedUserName;
            var takenOver = vm.ActiveSessions.TakenOverBy(name);
            if (takenOver.Count == 0) return $"Connecting as {name} runs side by side with these sessions.";
            var who = takenOver.Count == 1 ? "this session" : $"{takenOver.Count} of these sessions";
            return name is null ? $"Connecting without a user name takes over {who}." : $"Connecting as {name} takes over {who}.";
        }
    }

    /// <summary>"2 in use" for the status bar; empty when nothing is in use. SPEC FR-47</summary>
    public string InUseText => Entries.Count(e => e.IsInUse) is var n and > 0 ? $"{n} in use" : "";

    private bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    partial void OnSearchTextChanged(string value)
    {
        _collapsedWhileSearching.Clear();
        RebuildTree();
        UpdateCountText();
    }

    /// <summary>
    /// The account that Connect and Copy use: the selected account row, or the default account of the selected entry.
    /// SPEC FR-09
    /// </summary>
    public AccountViewModel? SelectedAccount => SelectedNode switch
    {
        AccountViewModel a => a,
        EntryViewModel e => e.DefaultAccount,
        _ => null,
    };

    partial void OnSelectedNodeChanged(TreeNodeViewModel? oldValue, TreeNodeViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        Selected = newValue switch
        {
            EntryViewModel e => e,
            AccountViewModel a => a.Entry,
            _ => null,
        };
    }

    partial void OnSelectedChanged(EntryViewModel? value)
    {
        // An account row of the entry counts as the entry being selected.
        if (value is not null && !ReferenceEquals(SelectedNode, value) && (SelectedNode as AccountViewModel)?.Entry != value)
        {
            RevealFolder(value.Model.Folder);
            SelectedNode = value;
        }
        else if (value is null && SelectedNode is EntryViewModel or AccountViewModel)
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
        IsRemote = ws.IsRemote;
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
            vm.IsPinned = _pinned.Contains(entry.Id);
            Entries.Add(vm);
        }

        RebuildTree();
        Selected = Entries.FirstOrDefault(e => e.Model.Id == selectedId) ?? Selected;
        if (SelectedNode is AccountViewModel { IsDefault: false } account && account.Entry.FindAccount(account.AccountId) is null)
        {
            SelectedNode = account.Entry; // the account was removed
        }

        OnPropertyChanged(nameof(SelectedAccount));
        OnPropertyChanged(nameof(ConnectText));
        OnPropertyChanged(nameof(TakeoverWarning));
        OnPropertyChanged(nameof(InUseText));
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
        var root = FolderTree.Build(Entries.Where(e => e.Matches(SearchText) && PassesFilter(e)), e => e.Model.Folder, e => e.Name);
        var previous = _folders;
        _folders = new Dictionary<string, FolderViewModel>(StringComparer.CurrentCultureIgnoreCase);

        // The selected row and the folders above it: collections holding them must not be reset (see TreeChildren).
        var selectionPath = new HashSet<TreeNodeViewModel>();
        if (SelectedNode is { } selected)
        {
            selectionPath.Add(selected);
            if (selected is AccountViewModel account) selectionPath.Add(account.Entry);
            var folder = selected switch
            {
                FolderViewModel f => f.Path,
                AccountViewModel a => a.Entry.Model.Folder,
                _ => ((EntryViewModel)selected).Model.Folder,
            };
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
        AccountViewModel a => a.Entry.Model.Folder,
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

        OnPropertyChanged(nameof(TakeoverWarning));
        OnPropertyChanged(nameof(InUseText));
        if (TreeFilter == TreeFilter.InUse) RebuildTree();
    }

    // ------------------------------------------------------------------ commands

    private bool HasSelection => IsReady && Selected is not null;
    private bool CanEdit => IsReady && CanWrite;
    private bool CanEditSelection => CanEdit && Selected is not null;
    private bool CanConnectSelection => HasSelection && Selected!.CanLaunch;

    [RelayCommand(CanExecute = nameof(CanConnectSelection))]
    private async Task ConnectAsync()
    {
        if (_host is null || Selected is not { } vm || SelectedAccount is not { } accountVm) return;
        var entry = vm.Model;
        var account = entry.FindAccount(accountVm.AccountId);
        var accountName = entry.QualifiedUserNameFor(account);

        // SPEC FR-33: hosts that allow several sessions (e.g. Windows Server) let other accounts connect side by side;
        // only someone using the same account (or an unknown one) would be disconnected.
        var conflicts = vm.ActiveSessions.TakenOverBy(accountName);
        if (conflicts.Count > 0 && !_dialogs.Confirm("Connection in use",
                $"{EntryViewModel.Describe(conflicts)}{Environment.NewLine}{Environment.NewLine}Connect anyway? This may disconnect the other user."))
        {
            return;
        }

        try
        {
            var password = _host.Workspace.RevealPassword(entry, account?.Id);
            var session = await _host.Launcher.LaunchAsync(entry.ForAccount(account), password);
            StatusText = accountName is null ? $"Launched {entry.Name}" : $"Launched {entry.Name} as {accountName}";
            RememberRecent(entry.Id);
            Log.Info($"Launched {entry.Id}");

            var lease = await _host.Presence.BeginAsync(entry.Id, accountName, _host.User);
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

    /// <summary>Connects with <paramref name="account"/> of the selected entry (the Connect button's account menu). SPEC FR-47</summary>
    [RelayCommand(CanExecute = nameof(CanConnectSelection))]
    private async Task ConnectAsAsync(AccountViewModel? account)
    {
        if (account is null || account.Entry != Selected) return;
        account.Entry.IsExpanded = true; // show the account row that is now selected
        SelectedNode = account;
        await ConnectAsync();
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
                var saved = await _host.Workspace.SaveAsync(editor.BuildEntry(), editor.BuildSecretUpdate(), editor.BuildAccountSecretUpdates());
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
        if (_host is null || Selected is not { } vm || SelectedAccount is not { } account) return;
        var password = _host.Workspace.RevealPassword(vm.Model, account.AccountId);
        if (password is null)
        {
            StatusText = account.IsDefault ? "This entry has no stored password." : $"{account.Name} has no stored password.";
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
        if (SelectedAccount?.QualifiedUserName is { } user)
        {
            Clipboard.SetDataObject(user, copy: false);
            StatusText = "User name copied.";
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopyHost()
    {
        if (Selected?.Model.Address is { } address)
        {
            Clipboard.SetDataObject(address, copy: false);
            StatusText = "Host copied.";
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
        if (!_dialogs.EditSettings(vm))
        {
            vm.RevertTheme(); // SPEC FR-08: Cancel undoes the theme preview
            return;
        }

        var restart = vm.Apply();
        SettingsStore.Save(Settings);
        if (restart && _dialogs.Confirm("Restart required", "Settings were saved. Restart Remote Desktop Harbor now to apply them?"))
        {
            System.Diagnostics.Process.Start(Environment.ProcessPath!);
            Application.Current.Shutdown();
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
