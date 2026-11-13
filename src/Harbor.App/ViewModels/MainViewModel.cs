using System.Globalization;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Harbor.App.Infrastructure;
using Harbor.Core;
using Harbor.Core.Import;
using Harbor.Core.Model;
using Harbor.Core.Services;
using Harbor.Core.Storage;
using Zoom = Harbor.Core.Model.TreeZoom;
using PaneWidth = Harbor.Core.Model.ListPaneWidth;

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
    private double _savedTreeZoom;
    private double _savedListPaneWidth;

    // Pinned entries of this user (SPEC FR-50), kept in the settings.
    private readonly HashSet<Guid> _pinned;

    // Smart groups (SPEC FR-62, ADR-0040): rows reused across rebuilds like folders; collapsed ones remembered by id.
    private Dictionary<Guid, SmartGroupViewModel> _smartGroups = [];
    private readonly HashSet<Guid> _collapsedSmartGroups;
    private readonly HashSet<Guid> _collapsedSmartGroupsWhileSearching = [];

    public MainViewModel(HarborSettings settings, IDialogService dialogs)
    {
        Settings = settings;
        _dialogs = dialogs;
        _collapsed = new HashSet<string>(settings.CollapsedFolders, StringComparer.CurrentCultureIgnoreCase);
        _pinned = [.. settings.PinnedEntryIds];
        _collapsedSmartGroups = [.. settings.CollapsedSmartGroups];
        _treeZoom = _savedTreeZoom = Zoom.Normalize(settings.TreeZoom);
        _listPaneWidth = _savedListPaneWidth = PaneWidth.Normalize(settings.ListPaneWidth);
        _syncTimer.Tick += async (_, _) => await SyncAsync();
        _presenceTimer.Tick += async (_, _) => await RefreshPresenceAsync();
    }

    public HarborSettings Settings { get; }
    public int ActiveLaunchCount => _leases.Count;

    /// <summary>True when some open session is published to the shared database, so other users see it. SPEC FR-34</summary>
    public bool PublishesPresence => _leases.Any(l => l.IsShared);
    public ObservableCollection<EntryViewModel> Entries { get; } = [];

    /// <summary>Top level of the entry tree: folders, then entries without a folder. Only entries matching <see cref="SearchText"/>.</summary>
    public TreeChildren Tree { get; } = [];

    [ObservableProperty] private string _searchText = "";

    /// <summary>All entries, or only those in use, pinned, recently connected (SPEC FR-50) or kept on this PC (FR-29).</summary>
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
        TreeFilter.ThisPc => e.IsLocalOnly, // SPEC FR-29
        _ => true,
    };

    // ------------------------------------------------------------------ tree zoom (SPEC FR-55, ADR-0034)

    /// <summary>Scale of the entry tree rows (1 = 100 %); remembered in the settings.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TreeZoomText), nameof(TreeZoomToolTip))]
    [NotifyCanExecuteChangedFor(nameof(ZoomTreeInCommand), nameof(ZoomTreeOutCommand), nameof(ResetTreeZoomCommand))]
    private double _treeZoom;

    public string TreeZoomText => Zoom.Label(TreeZoom);

    public string TreeZoomToolTip => Zoom.IsDefault(TreeZoom)
        ? "Entry list size: 100% (default)"
        : $"Entry list size: {TreeZoomText}. Click to reset to 100% (Ctrl+0)";

    partial void OnTreeZoomChanged(double value) => Settings.TreeZoom = value;

    private bool CanZoomTreeIn() => TreeZoom < Zoom.Max;
    private bool CanZoomTreeOut() => TreeZoom > Zoom.Min;
    private bool CanResetTreeZoom() => !Zoom.IsDefault(TreeZoom);

    [RelayCommand(CanExecute = nameof(CanZoomTreeIn))]
    private void ZoomTreeIn() => TreeZoom = Zoom.In(TreeZoom);

    [RelayCommand(CanExecute = nameof(CanZoomTreeOut))]
    private void ZoomTreeOut() => TreeZoom = Zoom.Out(TreeZoom);

    [RelayCommand(CanExecute = nameof(CanResetTreeZoom))]
    private void ResetTreeZoom() => TreeZoom = Zoom.Default;

    // ------------------------------------------------------------------ list pane width (SPEC FR-56)

    private double _listPaneWidth;

    /// <summary>Share of the entry list pane in the main window; set by the window when the splitter is moved.</summary>
    public double ListPaneWidth
    {
        get => _listPaneWidth;
        set => _listPaneWidth = PaneWidth.Normalize(value);
    }

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

    /// <summary>
    /// Copy or edit shortcuts in the palette (SPEC FR-48): select the highlighted result, close the palette and run the
    /// action on it.
    /// </summary>
    public async Task RunQuickActionAsync(QuickAction action)
    {
        if (!RevealQuickResult()) return;
        switch (action)
        {
            case QuickAction.CopyPassword when CopyPasswordCommand.CanExecute(null):
                CopyPasswordCommand.Execute(null);
                break;
            case QuickAction.CopyUserName when CopyUserNameCommand.CanExecute(null):
                CopyUserNameCommand.Execute(null);
                break;
            case QuickAction.CopyHost when CopyHostCommand.CanExecute(null):
                CopyHostCommand.Execute(null);
                break;
            case QuickAction.Edit when EditCommand.CanExecute(null):
                await EditCommand.ExecuteAsync(null);
                break;
        }
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
    [NotifyPropertyChangedFor(nameof(SelectedAccount), nameof(ConnectText), nameof(TakeoverWarning), nameof(SelectedSmartGroup))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(DeleteCommand), nameof(EditSmartGroupCommand), nameof(DeleteSmartGroupCommand))]
    private TreeNodeViewModel? _selectedNode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(EditCommand), nameof(DeleteCommand), nameof(CopyPasswordCommand), nameof(CopyUserNameCommand),
        nameof(CopyHostCommand), nameof(ConnectAsCommand), nameof(ConnectOtherWayCommand),
        nameof(TogglePinCommand), nameof(MoveToLocalCommand), nameof(MoveToSharedCommand), nameof(TransferFilesCommand))]
    [NotifyPropertyChangedFor(nameof(PinText))]
    private EntryViewModel? _selected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(EditCommand), nameof(DeleteCommand), nameof(ImportRdpCommand), nameof(ImportRdmCommand),
        nameof(ConnectCommand), nameof(ConnectAsCommand), nameof(ConnectOtherWayCommand), nameof(MoveToLocalCommand), nameof(MoveToSharedCommand),
        nameof(TransferFilesCommand), nameof(NewSmartGroupCommand), nameof(EditSmartGroupCommand), nameof(DeleteSmartGroupCommand),
        nameof(ExportBackupCommand), nameof(RestoreBackupCommand), nameof(DeleteShownCommand))]
    private bool _isReady;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(EditCommand), nameof(DeleteCommand), nameof(ImportRdpCommand), nameof(ImportRdmCommand),
        nameof(MoveToLocalCommand), nameof(MoveToSharedCommand), nameof(RestoreBackupCommand))]
    private bool _canWrite;

    /// <summary>Shared-database mode with this PC's vault: entries can be kept on this PC only (SPEC FR-29, ADR-0038).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(EditCommand), nameof(DeleteCommand), nameof(ImportRdpCommand),
        nameof(MoveToLocalCommand), nameof(MoveToSharedCommand))]
    private bool _supportsLocalEntries;

    // The "This PC" filter is hidden without local-only entries; never leave the tree filtered by it then. SPEC FR-29
    partial void OnSupportsLocalEntriesChanged(bool value)
    {
        if (!value && TreeFilter == TreeFilter.ThisPc) TreeFilter = TreeFilter.All;
    }

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
        _collapsedSmartGroupsWhileSearching.Clear();
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
        EntryLinkViewModel l => l.Entry.DefaultAccount,
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
            EntryLinkViewModel l => l.Entry, // SPEC FR-62: a smart group row stands for its entry
            _ => null,
        };
    }

    partial void OnSelectedChanged(EntryViewModel? value)
    {
        // An account row of the entry counts as the entry being selected.
        if (value is not null && !ReferenceEquals(SelectedNode, value) && (SelectedNode as AccountViewModel)?.Entry != value
            && (SelectedNode as EntryLinkViewModel)?.Entry != value)
        {
            RevealFolder(value.Model.Folder);
            SelectedNode = value;
        }
        else if (value is null && SelectedNode is EntryViewModel or AccountViewModel or EntryLinkViewModel)
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
        SupportsLocalEntries = ws.SupportsLocalEntries;
        IsOffline = ws.IsRemote && !ws.IsOnline;
        StoreText = !ws.IsRemote
            ? "Local vault"
            : ws.IsOnline
                ? $"Shared database · online{(ws.LastSyncUtc is { } t ? $" · synced {t.ToLocalTime():T}" : "")}"
                : ws.SupportsLocalEntries
                    ? "Shared database · OFFLINE (read-only cached copy; entries on this PC can still be changed)"
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

    // Smart groups list entries that also have their own row, so they are not counted (SPEC FR-62).
    private int CountMatches() => Tree.Sum(n => n switch { FolderViewModel f => f.Count, EntryViewModel => 1, _ => 0 });

    // ------------------------------------------------------------------ tree (SPEC FR-02)

    /// <summary>Rebuilds the tree from <see cref="Entries"/> and <see cref="SearchText"/>, reusing existing rows.</summary>
    private void RebuildTree()
    {
        var visible = Entries.Where(e => e.Matches(SearchText) && PassesFilter(e)).ToList();
        _shown = visible;
        DeleteShownCommand.NotifyCanExecuteChanged();
        var root = FolderTree.Build(visible, e => e.Model.Folder, e => e.Name);
        var previous = _folders;
        _folders = new Dictionary<string, FolderViewModel>(StringComparer.CurrentCultureIgnoreCase);

        // The selected row and the folders above it: collections holding them must not be reset (see TreeChildren).
        var selectionPath = new HashSet<TreeNodeViewModel>();
        if (SelectedNode is { } selected)
        {
            selectionPath.Add(selected);
            if (selected is AccountViewModel account) selectionPath.Add(account.Entry);
            if (selected is EntryLinkViewModel link) selectionPath.Add(link.Group);
            var folder = selected switch
            {
                FolderViewModel f => f.Path,
                AccountViewModel a => a.Entry.Model.Folder,
                SmartGroupViewModel g => g.Group.Folder,
                EntryLinkViewModel l => l.Group.Group.Folder,
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
            var smartGroups = BuildSmartGroups(visible, root);
            Tree.ReplaceWith(BuildRows(root, previous, selectionPath, smartGroups), Tree.Any(selectionPath.Contains));

            // The TreeView may have moved its selection while rows were replaced; the view model's selection stands.
            foreach (var folder in _folders.Values) folder.IsSelected = ReferenceEquals(folder, SelectedNode);
            foreach (var group in _smartGroups.Values) group.IsSelected = ReferenceEquals(group, SelectedNode);
            if (SelectedNode is not null) SelectedNode.IsSelected = true;
        }
        finally
        {
            _rebuildingTree = false;
        }

        // SPEC FR-62: an entry that left its smart group (edited, filtered out, group deleted or hidden) stays selected
        // in its own folder; a deleted or hidden smart group gives the selection up.
        if (SelectedNode is EntryLinkViewModel stale && (!_smartGroups.ContainsKey(stale.Group.Id) || !stale.Group.Children.Contains(stale)))
        {
            SelectedNode = Entries.Contains(stale.Entry) ? stale.Entry : null;
            if (SelectedNode is EntryViewModel entry) RevealFolder(entry.Model.Folder);
        }
        else if (SelectedNode is SmartGroupViewModel gone && !_smartGroups.ContainsKey(gone.Id))
        {
            SelectedNode = null;
        }
    }

    /// <summary>
    /// Smart group rows by the folder they are shown in ("" = top level), each filled with the visible entries its rules
    /// match. A group whose folder has no visible entries is shown at the top level while nothing is filtered, and hidden
    /// otherwise; while searching, groups without matches are hidden like folders. SPEC FR-62, ADR-0040
    /// </summary>
    private Dictionary<string, List<SmartGroupViewModel>> BuildSmartGroups(List<EntryViewModel> visible, FolderTreeNode<EntryViewModel> root)
    {
        var folders = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        void Collect(FolderTreeNode<EntryViewModel> node)
        {
            foreach (var child in node.Folders)
            {
                folders.Add(child.Path);
                Collect(child);
            }
        }

        Collect(root);
        var filtered = IsSearching || TreeFilter != TreeFilter.All;
        var collapsed = IsSearching ? _collapsedSmartGroupsWhileSearching : _collapsedSmartGroups;
        var previous = _smartGroups;
        _smartGroups = [];
        var byFolder = new Dictionary<string, List<SmartGroupViewModel>>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var group in Settings.SmartGroups.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var members = SmartGroups.Filter(group, visible, e => e.Model)
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (IsSearching && members.Count == 0) continue;

            var folder = FolderTree.Normalize(group.Folder) ?? "";
            if (folder.Length > 0 && !folders.Contains(folder))
            {
                if (filtered) continue;
                folder = "";
            }

            if (!previous.TryGetValue(group.Id, out var row)) row = new SmartGroupViewModel(group.Id, OnSmartGroupExpandedChanged);
            row.Group = group;
            row.Count = members.Count;
            row.IsExpanded = !collapsed.Contains(group.Id);
            var links = row.LinksFor(members);
            row.Children.ReplaceWith(links, links.Contains(SelectedNode!));
            _smartGroups[group.Id] = row;
            if (!byFolder.TryGetValue(folder, out var list)) byFolder[folder] = list = [];
            list.Add(row);
        }

        return byFolder;
    }

    private void OnSmartGroupExpandedChanged(SmartGroupViewModel group)
    {
        if (_rebuildingTree) return;
        var collapsed = IsSearching ? _collapsedSmartGroupsWhileSearching : _collapsedSmartGroups;
        if (group.IsExpanded) collapsed.Remove(group.Id);
        else collapsed.Add(group.Id);
    }

    /// <summary>Called by the view when the user selects a row. Ignored while rows are replaced by <see cref="RebuildTree"/>.</summary>
    public void SelectFromTree(TreeNodeViewModel? node)
    {
        if (!_rebuildingTree) SelectedNode = node;
    }

    private List<TreeNodeViewModel> BuildRows(FolderTreeNode<EntryViewModel> node, Dictionary<string, FolderViewModel> previous,
        HashSet<TreeNodeViewModel> selectionPath, Dictionary<string, List<SmartGroupViewModel>> smartGroups)
    {
        var rows = new List<TreeNodeViewModel>(node.Folders.Count + node.Items.Count);
        var collapsed = IsSearching ? _collapsedWhileSearching : _collapsed;
        // SPEC FR-62: smart groups come first in their folder, like RDCMan lists them with the groups.
        if (smartGroups.TryGetValue(node.Path, out var groups)) rows.AddRange(groups);
        foreach (var child in node.Folders)
        {
            if (!previous.TryGetValue(child.Path, out var folder))
            {
                folder = new FolderViewModel(child.Path, OnFolderExpandedChanged);
            }

            folder.Name = child.Name;
            folder.Count = child.TotalCount;
            folder.IsExpanded = !collapsed.Contains(child.Path);
            folder.Children.ReplaceWith(BuildRows(child, previous, selectionPath, smartGroups), folder.Children.Any(selectionPath.Contains));
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
        foreach (var group in _smartGroups.Values) group.IsExpanded = true;
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var folder in _folders.Values) folder.IsExpanded = false;
        foreach (var group in _smartGroups.Values) group.IsExpanded = false;
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    /// <summary>The folder a new entry goes to: the selected folder, or the folder of the selected entry.</summary>
    private string? SelectedFolderPath => SelectedNode switch
    {
        FolderViewModel f => f.Path,
        EntryViewModel e => e.Model.Folder,
        AccountViewModel a => a.Entry.Model.Folder,
        EntryLinkViewModel l => l.Entry.Model.Folder,
        SmartGroupViewModel g => g.Group.Folder,
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
        if (_host is null) return;
        // While offline nobody else's presence can be known; show only this app's own sessions rather than stale data.
        // In local mode and for entries kept on this PC those are the only ones (SPEC FR-37).
        var sessions = await _host.Presence.ListActiveAsync(includeShared: !IsOffline);
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

    /// <summary>New entries: always in local mode; in shared mode while online, or on this PC while offline (SPEC FR-29).</summary>
    private bool CanEdit => IsReady && (CanWrite || SupportsLocalEntries);

    /// <summary>Bulk import writes to the shared database in shared mode.</summary>
    private bool CanImport => IsReady && CanWrite;

    /// <summary>Entries kept on this PC can be changed while the shared database is offline (SPEC FR-29).</summary>
    private bool CanEditSelection => IsReady && Selected is { } vm && (vm.IsLocalOnly ? SupportsLocalEntries : CanWrite);

    /// <summary>F2 and Del act on the selected smart group row too (SPEC FR-62); smart groups are personal, so also offline.</summary>
    private bool CanEditSelectionOrGroup => SelectedNode is SmartGroupViewModel ? IsReady : CanEditSelection;
    private bool CanConnectSelection => HasSelection && Selected!.CanLaunch;

    [RelayCommand(CanExecute = nameof(CanConnectSelection))]
    private Task ConnectAsync() => ConnectSelectedAsync(Settings.ConnectInHarborWindow);

    /// <summary>
    /// Connect the other way than the Settings choose: in Harbor's window when Connect opens mstsc, and in mstsc when
    /// Connect opens Harbor's window (Shift+Enter, menus). SPEC FR-67
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConnectSelection))]
    private Task ConnectOtherWayAsync() => ConnectSelectedAsync(!Settings.ConnectInHarborWindow);

    /// <summary>"Connect in Harbor window" or "Connect in Remote Desktop Connection": the other way (SPEC FR-67).</summary>
    public string ConnectOtherWayText => Settings.ConnectInHarborWindow ? "Connect in Remote Desktop Connection" : "Connect in Harbor window";

    private async Task ConnectSelectedAsync(bool inHarborWindow)
    {
        if (Selected is { } vm && SelectedAccount is { } accountVm)
            await ConnectEntryAsync(vm, accountVm.AccountId, null, inHarborWindow ? LaunchInHarborWindowAsync : null);
    }

    /// <summary>
    /// Opens Harbor's resizable Remote Desktop window (SPEC FR-67, ADR-0045). Entries it cannot connect like mstsc
    /// (a Remote Desktop Gateway), or a PC without the Remote Desktop control, open in mstsc instead.
    /// </summary>
    private async Task<Core.Rdp.IRdpSession> LaunchInHarborWindowAsync(ConnectionEntry entry, string? password)
    {
        if (_host!.RdpWindows.IsAvailable && Core.Rdp.RdpWindowSettings.For(entry) is { } settings)
        {
            return _host.RdpWindows.Open(settings, password);
        }

        Log.Info($"Harbor window not available for {entry.Id}; opening mstsc");
        return await _host.Launcher.LaunchAsync(entry, password);
    }

    /// <summary>
    /// Connects <paramref name="vm"/> as <paramref name="accountId"/>, with the takeover and production questions, presence
    /// and recent list; <paramref name="overrides"/> are extra .rdp lines for this launch (drives shared for a file
    /// transfer, SPEC FR-63). <paramref name="launch"/> replaces mstsc (Harbor's own transfer window, SPEC FR-65); it gets
    /// the entry with the account applied and the decrypted password. Returns false when nothing was launched.
    /// </summary>
    private async Task<bool> ConnectEntryAsync(EntryViewModel vm, Guid? accountId, IReadOnlyList<string>? overrides,
        Func<ConnectionEntry, string?, Task<Core.Rdp.IRdpSession>>? launch = null)
    {
        if (_host is null) return false;
        var entry = vm.Model;
        var account = entry.FindAccount(accountId);
        var accountName = entry.QualifiedUserNameFor(account);

        // SPEC FR-33: hosts that allow several sessions (e.g. Windows Server) let other accounts connect side by side;
        // only someone using the same account (or an unknown one) would be disconnected.
        var conflicts = vm.ActiveSessions.TakenOverBy(accountName);
        // SPEC FR-51: one question at most – the takeover warning mentions production too.
        var production = vm.Environment == EnvironmentKind.Production;
        var prodNote = production ? $"{Environment.NewLine}{Environment.NewLine}{entry.Name} is a production (PROD) entry." : "";
        if (conflicts.Count > 0 && !_dialogs.ConfirmRisky("Connection in use",
                $"{EntryViewModel.Describe(conflicts)}{prodNote}{Environment.NewLine}{Environment.NewLine}Connect anyway? This may disconnect the other user.",
                "Connect anyway"))
        {
            return false;
        }

        if (conflicts.Count == 0 && production && Settings.ConfirmProductionConnect && !_dialogs.ConfirmRisky("Production entry",
                $"{entry.Name} is in {vm.GroupName} (PROD).{Environment.NewLine}{Environment.NewLine}Connect{(accountName is null ? "" : $" as {accountName}")}?",
                "Connect"))
        {
            return false;
        }

        try
        {
            var password = _host.Workspace.RevealPassword(entry, account?.Id);
            var session = launch is null
                ? await _host.Launcher.LaunchAsync(entry.ForAccount(account), password, overrides)
                : await launch(entry.ForAccount(account), password);
            StatusText = accountName is null ? $"Launched {entry.Name}" : $"Launched {entry.Name} as {accountName}";
            RememberRecent(entry.Id);
            Log.Info($"Launched {entry.Id}");

            // SPEC FR-29: an entry kept on this PC is private, so no presence is published for it; it is still
            // shown as in use on this PC (SPEC FR-37).
            var lease = await _host.Presence.BeginAsync(entry.Id, accountName, _host.User, publish: !entry.IsLocalOnly);
            _leases.Add(lease);
            await RefreshPresenceAsync();
            _ = TrackSessionAsync(session, lease);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Launch failed", ex);
            _dialogs.ShowError("Could not start Remote Desktop", ex.Message);
            return false;
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

    /// <summary>
    /// Imports a Remote Desktop Manager CSV export after one confirmation that says what will be added and skipped.
    /// Passwords from the file are only held in memory until each entry is saved encrypted. SPEC FR-54, ADR-0032
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportRdmAsync()
    {
        if (_host is null || _dialogs.PickRdmCsvFile() is not { } path) return;

        ImportResult parsed;
        try
        {
            parsed = RdmCsvImport.Parse(await System.IO.File.ReadAllTextAsync(path));
        }
        catch (Exception ex) when (ex is FormatException or System.IO.IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError("Cannot import", ex.Message);
            return;
        }

        var toImport = RdmCsvImport.WithoutExisting(parsed.Entries, Entries.Select(e => e.Model));
        var message = ImportSummary(parsed, toImport);
        if (toImport.Count == 0)
        {
            _dialogs.ShowError("Nothing to import", message);
            return;
        }

        if (!_dialogs.ConfirmRisky("Import from Remote Desktop Manager", message, "Import")) return; // SPEC FR-53: bulk write, Cancel is the default

        var imported = 0;
        try
        {
            foreach (var item in toImport)
            {
                await _host.Workspace.SaveAsync(item.Entry,
                    item.Password is { Length: > 0 } pw ? SecretUpdate.Set(pw) : SecretUpdate.Keep,
                    item.AccountPasswords?.ToDictionary(p => p.Key, p => SecretUpdate.Set(p.Value)));
                imported++;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"RDM import stopped after {imported} of {toImport.Count} entries", ex);
            _dialogs.ShowError("Import stopped", $"{imported} of {toImport.Count} entries were imported before an error: {ex.Message}");
        }
        finally
        {
            await ReloadAsync();
        }

        Log.Info($"Imported {imported} entries from Remote Desktop Manager");
        StatusText = $"Imported {imported} {(imported == 1 ? "entry" : "entries")} from Remote Desktop Manager";
    }

    private string ImportSummary(ImportResult parsed, IReadOnlyList<ImportedEntry> toImport)
    {
        var sb = new System.Text.StringBuilder();
        int Count(EntryKind kind) => toImport.Count(i => i.Entry.Kind == kind);
        sb.Append(CultureInfo.CurrentCulture,
            $"{toImport.Count} entries will be added: {Count(EntryKind.RdpConnection)} RDP connections, {Count(EntryKind.CredentialOnly)} credentials only and {Count(EntryKind.Other)} other (VNC, web, VPN… — kept for their credentials, not opened with Remote Desktop).");
        if (_host?.Workspace.IsRemote == true) sb.Append(" They will be shared with everyone using the shared database.");

        var existing = parsed.Entries.Count - toImport.Count;
        if (existing > 0) sb.Append(CultureInfo.CurrentCulture, $"\n\n{existing} entries are already in the catalog and are left out.");
        if (parsed.SkippedFolders > 0)
        {
            sb.Append(CultureInfo.CurrentCulture, $"\n\n{parsed.SkippedFolders} folder rows are skipped: folders are rebuilt from each entry's group.");
        }

        sb.Append("\n\nThe CSV file holds passwords in plain text: delete it once the import is done.");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ backup (SPEC FR-68, ADR-0046)

    /// <summary>
    /// Writes every entry — with accounts and passwords — and the user's pins and smart groups to a backup file encrypted
    /// with a passphrase the user chooses, so the list can be moved to another PC. Works offline too (from the local copy).
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsReady))]
    private async Task ExportBackupAsync()
    {
        if (_host is null) return;
        var ws = _host.Workspace;
        var entries = await ws.ListAsync();
        if (entries.Count == 0)
        {
            _dialogs.ShowError("Nothing to export", "The catalog has no entries yet.");
            return;
        }

        var name = $"Harbor backup {DateTime.Now:yyyy-MM-dd}{Core.Backup.BackupFile.Extension}";
        if (_dialogs.PickBackupFileToSave(name) is not { } path) return;
        if (_dialogs.AskBackupPassphrase(isNew: true) is not { } passphrase) return;

        try
        {
            var pinned = _pinned.ToList();
            var groups = Settings.SmartGroups.ToList();
            // PBKDF2 takes a moment; the plaintext passwords only live in memory until they are encrypted (NFR-12).
            var bytes = await Task.Run(() => Core.Backup.BackupFile.Encrypt(
                Core.Backup.HarborBackup.Create(entries, (e, a) => ws.RevealPassword(e, a), pinned, groups, AppInfo.Version, DateTimeOffset.UtcNow),
                passphrase));
            await System.IO.File.WriteAllBytesAsync(path, bytes);
        }
        catch (Exception ex)
        {
            Log.Error("Backup export failed", ex);
            _dialogs.ShowError("Backup not saved", ex.Message);
            return;
        }

        Log.Info($"Exported a backup of {entries.Count} entries");
        StatusText = $"Exported {entries.Count} {(entries.Count == 1 ? "entry" : "entries")} to {System.IO.Path.GetFileName(path)}";
    }

    /// <summary>
    /// Restores a backup file: adds the entries that are not in the catalog yet (passwords encrypted with this PC's key on
    /// save), pins what the backup pins and adds its smart groups, after one confirmation that says what will change.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task RestoreBackupAsync()
    {
        if (_host is null || _dialogs.PickBackupFileToOpen() is not { } path) return;

        byte[] file;
        try
        {
            file = await System.IO.File.ReadAllBytesAsync(path);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError("Cannot restore", ex.Message);
            return;
        }

        Core.Backup.BackupContent content;
        while (true)
        {
            if (_dialogs.AskBackupPassphrase(isNew: false) is not { } passphrase) return;
            try
            {
                content = await Task.Run(() => Core.Backup.BackupFile.Decrypt(file, passphrase));
                break;
            }
            catch (Core.Backup.BackupPassphraseException ex)
            {
                _dialogs.ShowError("Wrong passphrase", ex.Message);
            }
            catch (FormatException ex)
            {
                _dialogs.ShowError("Cannot restore", ex.Message);
                return;
            }
        }

        var plan = Core.Backup.HarborBackup.PlanRestore(content, Entries.Select(e => e.Model).ToList(), Settings.SmartGroups, SupportsLocalEntries);
        var message = RestoreSummary(content, plan);
        if (plan.IsEmpty)
        {
            _dialogs.ShowError("Nothing to restore", message);
            return;
        }

        if (!_dialogs.ConfirmRisky("Restore backup", message, "Restore")) return; // SPEC FR-53: bulk write, Cancel is the default

        var restored = 0;
        try
        {
            foreach (var item in plan.ToAdd)
            {
                var import = item.Import;
                var saved = await _host.Workspace.SaveAsync(import.Entry,
                    import.Password is { Length: > 0 } pw ? SecretUpdate.Set(pw) : SecretUpdate.Keep,
                    import.AccountPasswords?.ToDictionary(p => p.Key, p => SecretUpdate.Set(p.Value)));
                if (item.Pinned) _pinned.Add(saved.Id);
                restored++;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Backup restore stopped after {restored} of {plan.ToAdd.Count} entries", ex);
            _dialogs.ShowError("Restore stopped", $"{restored} of {plan.ToAdd.Count} entries were restored before an error: {ex.Message}");
        }
        finally
        {
            // Pins and smart groups are personal settings (SPEC FR-50, FR-62); kept for what was restored so far.
            _pinned.UnionWith(plan.PinExisting);
            Settings.PinnedEntryIds = [.. _pinned];
            if (plan.SmartGroupsToAdd.Count > 0) Settings.SmartGroups = [.. Settings.SmartGroups, .. plan.SmartGroupsToAdd];
            SaveSettingsQuietly("Restored pins and smart groups");
            await ReloadAsync();
        }

        Log.Info($"Restored {restored} entries from a backup");
        StatusText = $"Restored {restored} {(restored == 1 ? "entry" : "entries")} from {System.IO.Path.GetFileName(path)}";
    }

    private string RestoreSummary(Core.Backup.BackupContent content, Core.Backup.RestorePlan plan)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(CultureInfo.CurrentCulture,
            $"The backup from {content.CreatedUtc.ToLocalTime():g} holds {content.Entries.Count} entries and {content.SmartGroups.Count} smart groups.");
        sb.Append(CultureInfo.CurrentCulture, $"\n\n{plan.ToAdd.Count} entries will be added");
        var local = plan.ToAdd.Count(i => i.Import.Entry.IsLocalOnly);
        if (_host?.Workspace.IsRemote == true)
        {
            sb.Append(local > 0
                ? $" ({local} kept on this PC only, the others shared with everyone using the shared database)."
                : ", shared with everyone using the shared database.");
        }
        else
        {
            sb.Append('.');
        }

        if (plan.AlreadyPresent > 0) sb.Append(CultureInfo.CurrentCulture, $"\n\n{plan.AlreadyPresent} entries are already in the catalog and are left out.");
        if (plan.PinExisting.Count > 0) sb.Append(CultureInfo.CurrentCulture, $" {plan.PinExisting.Count} of them will be pinned.");
        if (plan.SmartGroupsToAdd.Count > 0) sb.Append(CultureInfo.CurrentCulture, $"\n\n{plan.SmartGroupsToAdd.Count} smart groups will be added.");
        if (plan.SmartGroupsPresent > 0) sb.Append(CultureInfo.CurrentCulture, $"\n\n{plan.SmartGroupsPresent} smart groups with the same name already exist and are left out.");
        return sb.ToString();
    }

    private async Task EditNewAsync(ConnectionEntry template, bool importFirst)
    {
        var editor = new EntryEditorViewModel(template with { Folder = SelectedFolderPath }, isNew: true, _dialogs, Folders());
        if (SupportsLocalEntries) editor.OfferLocalStorage(sharedAvailable: CanWrite);
        if (importFirst)
        {
            editor.ImportRdpFileCommand.Execute(null);
        }

        await RunEditorAsync(editor);
    }

    [RelayCommand(CanExecute = nameof(CanEditSelectionOrGroup))]
    private Task EditAsync()
    {
        if (SelectedNode is SmartGroupViewModel)
        {
            EditSmartGroup();
            return Task.CompletedTask;
        }

        if (Selected is not { } vm) return Task.CompletedTask;
        var editor = new EntryEditorViewModel(vm.Model, isNew: false, _dialogs, Folders());
        if (SupportsLocalEntries) editor.OfferLocalStorage(sharedAvailable: CanWrite);
        return RunEditorAsync(editor);
    }

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

    [RelayCommand(CanExecute = nameof(CanEditSelectionOrGroup))]
    private async Task DeleteAsync()
    {
        if (SelectedNode is SmartGroupViewModel)
        {
            DeleteSmartGroup();
            return;
        }

        if (_host is null || Selected is not { } vm) return;
        var shared = _host.Workspace.IsRemote && !vm.IsLocalOnly;
        if (!_dialogs.ConfirmRisky("Delete entry", $"Delete '{vm.Name}'?{(shared ? " It will be removed for everyone using the shared database." : "")}", "Delete"))
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

    // ------------------------------------------------------------------ delete shown entries (SPEC FR-69, ADR-0048)

    /// <summary>The entries the tree shows now (search and filter applied), each once, also when a smart group lists it.</summary>
    private List<EntryViewModel> _shown = [];

    /// <summary>Only while the search or a filter narrows the list, so one click never empties the whole catalog. SPEC FR-69</summary>
    private bool CanDeleteShown => IsReady && (IsSearching || TreeFilter != TreeFilter.All) && _shown.Count > 0;

    [RelayCommand(CanExecute = nameof(CanDeleteShown))]
    private async Task DeleteShownAsync()
    {
        if (_host is null) return;
        var entries = _shown.Select(e => e.Model).ToList();
        var writable = entries.Where(_host.Workspace.CanWriteEntry).ToList();
        var skipped = entries.Count - writable.Count;
        if (writable.Count == 0)
        {
            _dialogs.ShowError("Not deleted", "None of the shown entries can be changed while the shared database is offline.");
            return;
        }

        var shared = _host.Workspace.IsRemote ? writable.Count(e => !e.IsLocalOnly) : 0;
        var message = $"Delete {Plural(writable.Count, "shown entry", "shown entries")}?";
        if (shared > 0)
        {
            message += $" {Plural(shared, "entry", "entries")} will be removed for everyone using the shared database.";
        }

        if (_host.Workspace.IsRemote && writable.Count > shared)
        {
            message += $" {Plural(writable.Count - shared, "entry", "entries")} kept on this PC will be removed from this PC.";
        }

        if (skipped > 0)
        {
            message += $" {Plural(skipped, "shared entry", "shared entries")} cannot be changed while offline and will be kept.";
        }

        if (!_dialogs.ConfirmRisky("Delete shown entries", message, $"Delete {writable.Count}")) return;

        StatusText = $"Deleting {writable.Count} entries…";
        var result = await _host.Workspace.DeleteManyAsync(writable);
        Log.Info($"Deleted {result.Deleted.Count} shown entries, {result.Failed.Count} not deleted");
        if (Selected is { } selected && result.Deleted.Any(e => e.Id == selected.Model.Id)) Selected = null;
        await ReloadAsync();
        if (result.Failed.Count > 0)
        {
            var first = result.Failed[0];
            _dialogs.ShowError("Some entries were not deleted",
                $"Deleted {result.Deleted.Count} of {writable.Count}. {Plural(result.Failed.Count, "entry was", "entries were")} not deleted, " +
                $"e.g. '{first.Entry.Name}': {first.Error.Message} Refresh (F5) and try again.");
        }
        else
        {
            StatusText = $"Deleted {Plural(result.Deleted.Count, "entry", "entries")}.";
        }
    }

    private static string Plural(int count, string one, string many) =>
        string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{count} {(count == 1 ? one : many)}");

    // ------------------------------------------------------------------ this PC only / shared (SPEC FR-29, ADR-0038)

    private bool CanMoveToLocal => IsReady && SupportsLocalEntries && CanWrite && Selected is { IsLocalOnly: false };
    private bool CanMoveToShared => IsReady && SupportsLocalEntries && CanWrite && Selected is { IsLocalOnly: true };

    [RelayCommand(CanExecute = nameof(CanMoveToLocal))]
    private Task MoveToLocalAsync() => MoveAsync(toLocal: true);

    [RelayCommand(CanExecute = nameof(CanMoveToShared))]
    private Task MoveToSharedAsync() => MoveAsync(toLocal: false);

    private async Task MoveAsync(bool toLocal)
    {
        if (_host is null || Selected is not { } vm) return;
        var confirmed = toLocal
            ? _dialogs.ConfirmRisky("Move to this PC only",
                $"Move '{vm.Name}' to this PC only?{Environment.NewLine}{Environment.NewLine}It is removed from the shared database for everyone " +
                "and kept, with its passwords, only in this PC's local vault, encrypted for your Windows account.", "Move to this PC")
            : _dialogs.ConfirmRisky("Move to shared database",
                $"Move '{vm.Name}' to the shared database?{Environment.NewLine}{Environment.NewLine}Everyone using the shared database will see it " +
                "and can use its passwords. It is removed from this PC's local vault.", "Share");
        if (!confirmed) return;

        try
        {
            var moved = await _host.Workspace.MoveAsync(vm.Model, toLocal);
            RemapPersonalIds(vm.Model.Id, moved.Id);
            await ReloadAsync();
            Selected = Entries.FirstOrDefault(e => e.Model.Id == moved.Id);
            StatusText = toLocal ? $"{moved.Name} is now kept on this PC only" : $"{moved.Name} is now in the shared database";
            Log.Info($"Moved {vm.Model.Id} to {(toLocal ? "this PC" : "the shared database")} as {moved.Id}");
        }
        catch (Exception ex) when (ex is ConcurrencyConflictException or WorkspaceOfflineException or InvalidOperationException)
        {
            _dialogs.ShowError("Not moved", ex.Message);
            if (ex is ConcurrencyConflictException) await SyncAsync();
        }
    }

    /// <summary>A moved entry gets a new id (ADR-0038): keep it pinned and in the recent list.</summary>
    private void RemapPersonalIds(Guid from, Guid to)
    {
        if (_pinned.Remove(from))
        {
            _pinned.Add(to);
            Settings.PinnedEntryIds = [.. _pinned];
        }

        Settings.RecentEntryIds = Settings.RecentEntryIds.Select(id => id == from ? to : id).ToList();
        SaveSettingsQuietly("Pinned and recent entries");
    }

    // ------------------------------------------------------------------ smart groups (SPEC FR-62, ADR-0040)

    /// <summary>The smart group of the selected row: the group itself, or the group an entry is listed in.</summary>
    public SmartGroupViewModel? SelectedSmartGroup => SelectedNode switch
    {
        SmartGroupViewModel g => g,
        EntryLinkViewModel l => l.Group,
        _ => null,
    };

    private bool HasSmartGroupSelection => IsReady && SelectedSmartGroup is not null;

    /// <summary>New smart group, shown in the selected folder (or at the top level), like RDCMan's "Add smart group".</summary>
    [RelayCommand(CanExecute = nameof(IsReady))]
    private void NewSmartGroup()
    {
        var folder = SelectedNode is FolderViewModel f ? f.Path : null;
        RunSmartGroupEditor(new SmartGroup { Name = "New smart group", Folder = folder }, isNew: true);
    }

    [RelayCommand(CanExecute = nameof(HasSmartGroupSelection))]
    private void EditSmartGroup()
    {
        if (SelectedSmartGroup is { } row) RunSmartGroupEditor(row.Group, isNew: false);
    }

    [RelayCommand(CanExecute = nameof(HasSmartGroupSelection))]
    private void DeleteSmartGroup()
    {
        if (SelectedSmartGroup is not { } row) return;
        if (!_dialogs.ConfirmRisky("Delete smart group",
                $"Delete the smart group '{row.Name}'?{Environment.NewLine}{Environment.NewLine}The entries it lists are not deleted.", "Delete"))
        {
            return;
        }

        Settings.SmartGroups = Settings.SmartGroups.Where(g => g.Id != row.Id).ToList();
        _collapsedSmartGroups.Remove(row.Id);
        SaveSmartGroups();
        StatusText = $"Deleted smart group {row.Name}";
    }

    private void RunSmartGroupEditor(SmartGroup group, bool isNew)
    {
        var editor = new SmartGroupEditorViewModel(group, isNew, Folders(), Entries.Select(e => e.Model).ToList());
        if (!_dialogs.EditSmartGroup(editor)) return;

        var saved = editor.BuildGroup();
        Settings.SmartGroups = isNew
            ? [.. Settings.SmartGroups, saved]
            : Settings.SmartGroups.Select(g => g.Id == saved.Id ? saved : g).ToList();
        _collapsedSmartGroups.Remove(saved.Id);
        SaveSmartGroups();
        if (_smartGroups.TryGetValue(saved.Id, out var row))
        {
            RevealFolder(saved.Folder);
            SelectedNode = row;
        }

        StatusText = $"Saved smart group {saved.Name}";
    }

    private void SaveSmartGroups()
    {
        Settings.CollapsedSmartGroups = [.. _collapsedSmartGroups];
        SaveSettingsQuietly("Smart groups");
        RebuildTree();
    }

    // ------------------------------------------------------------------ file transfer (SPEC FR-60, FR-61, ADR-0039)

    private TransfersViewModel? _transfers;

    private bool CanTransferFiles => HasSelection && !string.IsNullOrWhiteSpace(Selected!.Model.Host);

    /// <summary>Opens the File transfer window for the selected entry and account.</summary>
    [RelayCommand(CanExecute = nameof(CanTransferFiles))]
    private void TransferFiles()
    {
        if (_host is null || Selected is not { } vm) return;
        _transfers ??= new TransfersViewModel(_host.Transfers, _host.RdpTransfers, _host.RdpTransferSessions, _dialogs, _host.ConnectSharesAsync,
            ConnectForTransferAsync, Settings, () => SaveSettingsQuietly("Transfer settings"));
        _transfers.Target(vm.Model, SelectedAccount?.AccountId);
        _dialogs.ShowTransfers(_transfers);
    }

    /// <summary>
    /// SPEC FR-63, FR-65: connects the transfer's entry and account with the drives it needs shared into the session, in mstsc
    /// or with <paramref name="launch"/> (Harbor's transfer window). A session
    /// already open with the same account is reconnected with them.
    /// </summary>
    private Task<bool> ConnectForTransferAsync(Guid entryId, Guid? accountId, IReadOnlyList<string> overrides,
        Func<ConnectionEntry, string?, Task<Core.Rdp.IRdpSession>>? launch) =>
        Entries.FirstOrDefault(e => e.Model.Id == entryId) is { } vm ? ConnectEntryAsync(vm, accountId, overrides, launch) : Task.FromResult(false);

    /// <summary>Transfers still copying or retrying; closing the app pauses them (they resume later). SPEC FR-61</summary>
    public int ActiveTransferCount => _host?.Transfers.Jobs.Count(j => j.State is Core.Transfer.TransferState.Running
        or Core.Transfer.TransferState.Retrying or Core.Transfer.TransferState.Queued) ?? 0;

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
        OnPropertyChanged(nameof(ConnectOtherWayText)); // SPEC FR-67: Connect may now open the other way
        if (restart && _dialogs.Confirm("Restart required", "Settings were saved. Restart Remote Desktop Harbor now to apply them?"))
        {
            System.Diagnostics.Process.Start(Environment.ProcessPath!);
            Application.Current.Shutdown();
        }
    }

    /// <summary>Remembers which folders and smart groups (SPEC FR-62) are collapsed, the tree zoom (SPEC FR-55) and the list pane width (FR-56) for the next start.</summary>
    private void SaveTreeState()
    {
        var collapsed = _collapsed.Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        // Only groups that still exist, so deleted ones do not pile up in the settings (SPEC FR-62).
        var collapsedGroups = Settings.SmartGroups.Select(g => g.Id).Where(_collapsedSmartGroups.Contains).ToList();
        if (collapsed.SequenceEqual(Settings.CollapsedFolders, StringComparer.CurrentCultureIgnoreCase) && TreeZoom == _savedTreeZoom
            && ListPaneWidth == _savedListPaneWidth && collapsedGroups.SequenceEqual(Settings.CollapsedSmartGroups)) return;
        Settings.CollapsedFolders = collapsed;
        Settings.CollapsedSmartGroups = collapsedGroups;
        Settings.TreeZoom = _savedTreeZoom = TreeZoom;
        Settings.ListPaneWidth = _savedListPaneWidth = ListPaneWidth;
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
        if (_host is not null) await _host.DisposeAsync();
    }
}
