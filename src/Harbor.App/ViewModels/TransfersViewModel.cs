using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Threading;
using Harbor.Core;
using Harbor.Core.Model;
using Harbor.Core.Rdp;
using Harbor.Core.Transfer;
using Harbor.Core.Transfer.Rdp;

namespace Harbor.App.ViewModels;

/// <summary>
/// The File transfer window (SPEC FR-60, FR-61, FR-63, ADR-0039, ADR-0041): upload files or a folder to the selected entry's
/// host, download files from it, and follow every transfer, which survives dropped connections and restarts. Copies go
/// either through the Remote Desktop connection (only port 3389 needed: a helper started in the session copies through the
/// PC's shared drive) or over a network share (port 445, run here by the <see cref="TransferQueue"/>). Remote Desktop
/// transfers connect in Harbor's own transfer window, which starts the helper in the session by itself (SPEC FR-65,
/// ADR-0042), or in mstsc with the start command to paste.
/// </summary>
public sealed partial class TransfersViewModel : ObservableObject
{
    private static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(2);

    private readonly TransferQueue _queue;
    private readonly RdpTransferKit _kit;
    private readonly IRdpTransferSessionHost _sessions;
    private readonly IDialogService _dialogs;
    private readonly Func<TransferJob, CancellationToken, Task> _connectShares;
    private readonly ConnectRdp _connectRdp;
    private readonly HarborSettings _settings;
    private readonly Action _saveSettings;
    private readonly DispatcherTimer _statusTimer = new() { Interval = StatusInterval };

    /// <summary>
    /// Connects entry, account with extra .rdp lines for mstsc, or with <c>launch</c> instead of mstsc (Harbor's transfer
    /// window); false when the user cancelled or the launch failed.
    /// </summary>
    public delegate Task<bool> ConnectRdp(Guid entryId, Guid? accountId, IReadOnlyList<string> overrides,
        Func<ConnectionEntry, string?, Task<IRdpSession>>? launch);

    public TransfersViewModel(TransferQueue queue, RdpTransferKit kit, IRdpTransferSessionHost sessions, IDialogService dialogs,
        Func<TransferJob, CancellationToken, Task> connectShares, ConnectRdp connectRdp, HarborSettings settings, Action saveSettings)
    {
        _queue = queue;
        _kit = kit;
        _sessions = sessions;
        _dialogs = dialogs;
        _connectShares = connectShares;
        _connectRdp = connectRdp;
        _settings = settings;
        _saveSettings = saveSettings;
        _useRemoteDesktop = settings.TransferMethod != TransferMethod.NetworkShare;
        _serverFolder = settings.TransferServerFolder;
        _existingFiles = settings.TransferExistingFiles;
        _autoStart = settings.TransferAutoStart && sessions.IsAvailable;

        foreach (var plan in kit.List()) Jobs.Add(NewRdpRow(plan));
        foreach (var job in queue.Jobs) Jobs.Add(new TransferItemViewModel(job, queue));
        queue.JobChanged += (_, job) => OnUi(() => Apply(job));
        queue.JobRemoved += (_, id) => OnUi(() => Forget(id.ToString()));
        _statusTimer.Tick += (_, _) => RefreshRdpRows();
        _statusTimer.Start();
        RefreshRdpRows();
    }

    public ObservableCollection<ITransferRow> Jobs { get; } = [];

    /// <summary>The entry whose host and account the next transfer uses.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetText))]
    [NotifyCanExecuteChangedFor(nameof(UploadFilesCommand), nameof(UploadFolderCommand), nameof(DownloadCommand))]
    private ConnectionEntry? _entry;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadFilesCommand), nameof(UploadFolderCommand), nameof(DownloadCommand))]
    private Guid? _accountId;

    /// <summary>Copy through the Remote Desktop connection (default) instead of a network share. SPEC FR-63</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseNetworkShare), nameof(MethodHint))]
    [NotifyCanExecuteChangedFor(nameof(UploadFilesCommand), nameof(UploadFolderCommand), nameof(DownloadCommand))]
    private bool _useRemoteDesktop;

    public bool UseNetworkShare
    {
        get => !UseRemoteDesktop;
        set { if (value) UseRemoteDesktop = false; }
    }

    partial void OnUseRemoteDesktopChanged(bool value)
    {
        _settings.TransferMethod = value ? TransferMethod.RemoteDesktop : TransferMethod.NetworkShare;
        _saveSettings();
    }

    public string MethodHint => !UseRemoteDesktop
        ? "Copies over Windows file sharing (port 445) with the entry's account, run from this PC."
        : AutoStart
            ? "Needs only the Remote Desktop port. Harbor connects in its own window with the drives of this PC shared into the session and starts the copy there for you."
            : "Needs only the Remote Desktop port. Harbor connects with the needed drive of this PC shared into the session; you start the copy there with Win+R and Ctrl+V.";

    /// <summary>
    /// Remote Desktop mode: connect in Harbor's own transfer window, which starts the copy in the session (SPEC FR-65);
    /// off: mstsc and the start command to paste (FR-63).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MethodHint))]
    private bool _autoStart;

    /// <summary>False when the Remote Desktop control is missing on this PC: only mstsc can be used.</summary>
    public bool CanAutoStart => _sessions.IsAvailable;

    partial void OnAutoStartChanged(bool value)
    {
        if (!_sessions.IsAvailable) return;
        _settings.TransferAutoStart = value;
        _saveSettings();
    }

    /// <summary>Network share mode: folder on the host as a UNC path (<c>\\host\C$\Users\Public\Downloads</c>).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadFilesCommand), nameof(UploadFolderCommand), nameof(DownloadCommand))]
    private string _remoteFolder = "";

    /// <summary>Remote Desktop mode: folder on the server as the server sees it (<c>%USERPROFILE%\Downloads</c>).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadFilesCommand), nameof(UploadFolderCommand), nameof(DownloadCommand))]
    private string _serverFolder;

    /// <summary>What to do with files that already exist at the destination, either way of copying. SPEC FR-64</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkipExisting), nameof(ReplaceExisting), nameof(FailExisting))]
    private ExistingFiles _existingFiles;

    partial void OnExistingFilesChanged(ExistingFiles value)
    {
        _settings.TransferExistingFiles = value;
        _saveSettings();
    }

    /// <summary>Copy only the files that are not there yet (same name and extension, same sub-folder). SPEC FR-64</summary>
    public bool SkipExisting
    {
        get => ExistingFiles == ExistingFiles.Skip;
        set { if (value) ExistingFiles = ExistingFiles.Skip; }
    }

    public bool ReplaceExisting
    {
        get => ExistingFiles == ExistingFiles.Replace;
        set { if (value) ExistingFiles = ExistingFiles.Replace; }
    }

    public bool FailExisting
    {
        get => ExistingFiles == ExistingFiles.Fail;
        set { if (value) ExistingFiles = ExistingFiles.Fail; }
    }

    /// <summary>What to do in the session to start the last Remote Desktop transfer; <c>null</c> until one was prepared.</summary>
    [ObservableProperty] private string? _startHint;

    [ObservableProperty] private string? _startCommand;

    [ObservableProperty] private bool _isBusy;

    public string TargetText => Entry is null ? "No entry selected"
        : $"{Entry.Name} · {Entry.Host} · as {Entry.QualifiedUserNameFor(Entry.FindAccount(AccountId)) ?? "your Windows account"}";

    public string Summary
    {
        get
        {
            if (Jobs.Count == 0) return "No transfers yet.";
            return string.Create(CultureInfo.CurrentCulture,
                $"{Jobs.Count(j => j.IsActive)} active, {Jobs.Count(j => j.IsPaused)} paused, {Jobs.Count(j => j.IsDone)} done");
        }
    }

    /// <summary>Points the window at <paramref name="entry"/> (with <paramref name="accountId"/>), as selected in the main window.</summary>
    public void Target(ConnectionEntry entry, Guid? accountId)
    {
        Entry = entry;
        AccountId = accountId;
        RemoteFolder = UncPath.ForHost(entry.Host ?? "", _settings.TransferRemoteFolder);
    }

    private bool CanTransfer => !IsBusy && Entry is { Host.Length: > 0 } entry && (UseRemoteDesktop
        ? entry.CanLaunch && !string.IsNullOrWhiteSpace(ServerFolder)
        : UncPath.ShareRoot(RemoteFolder) is not null);

    partial void OnIsBusyChanged(bool value)
    {
        UploadFilesCommand.NotifyCanExecuteChanged();
        UploadFolderCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanTransfer))]
    private async Task UploadFilesAsync()
    {
        if (_dialogs.PickFilesToUpload() is not { Length: > 0 } files) return;
        if (UseRemoteDesktop)
        {
            await StartRdpAsync(() => _kit.CreateUpload(Entry!.Name, Entry.Id, files.Select(f => (f, Path.GetFileName(f))).ToList(),
                ServerFolder.Trim(), ExistingFiles, AccountId));
            return;
        }

        Add(files.Select(f => (f, UncPath.Combine(RemoteFolder, Path.GetFileName(f)))).ToList(), upload: true);
    }

    [RelayCommand(CanExecute = nameof(CanTransfer))]
    private async Task UploadFolderAsync()
    {
        if (_dialogs.PickFolder("Folder to upload", null) is not { } folder) return;
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        List<(string Source, string Relative)> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => (f, Path.Combine(name, Path.GetRelativePath(folder, f)))).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError("Cannot upload the folder", ex.Message);
            return;
        }

        if (files.Count == 0) return;
        if (UseRemoteDesktop)
        {
            await StartRdpAsync(() => _kit.CreateUpload(Entry!.Name, Entry.Id, files, ServerFolder.Trim(), ExistingFiles, AccountId));
            return;
        }

        Add(files.Select(f => (f.Source, UncPath.Combine(RemoteFolder, f.Relative))).ToList(), upload: true);
    }

    /// <summary>
    /// Remote Desktop: picks a local folder; the files are picked in the session. Network share: connects the share with
    /// the entry's account, lets the user pick files there and a local folder for them.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTransfer))]
    private async Task DownloadAsync()
    {
        if (UseRemoteDesktop)
        {
            if (_dialogs.PickFolder("Save the files to", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) is not { } local) return;
            await StartRdpAsync(() => _kit.CreateDownload(Entry!.Name, Entry.Id, ServerFolder.Trim(), local, ExistingFiles, AccountId));
            return;
        }

        IsBusy = true;
        try
        {
            await _connectShares(NewJob(RemoteFolder, RemoteFolder, upload: false), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("Cannot open the remote folder", ex.Message);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (_dialogs.PickFilesToDownload(RemoteFolder) is not { Length: > 0 } files) return;
        if (_dialogs.PickFolder("Save the files to", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) is not { } target) return;
        Add(files.Select(f => (f, Path.Combine(target, Path.GetFileName(f)))).ToList(), upload: false);
    }

    /// <summary>
    /// Writes the plan, then connects with the plan's drives shared into the session: in Harbor's transfer window, which
    /// starts the helper there (SPEC FR-65, ADR-0042), or in mstsc with the start command on the clipboard for the user to
    /// run (Win+R, Ctrl+V, Enter). SPEC FR-63, ADR-0041
    /// </summary>
    private async Task StartRdpAsync(Func<RdpTransferPrepared> prepare)
    {
        var entry = Entry!;
        RdpTransferPrepared prepared;
        try
        {
            prepared = prepare();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError("Cannot prepare the transfer", ex.Message);
            return;
        }

        _settings.TransferServerFolder = ServerFolder.Trim();
        _saveSettings();
        var row = NewRdpRow(prepared.Plan);
        Jobs.Insert(0, row);
        OnPropertyChanged(nameof(Summary));
        StartCommand = prepared.StartCommand;

        IsBusy = true;
        try
        {
            StartHint = await ConnectAsync(row, entry, prepared.Drives);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Connects <paramref name="row"/>'s transfer; returns what the window says about starting it.</summary>
    private async Task<string> ConnectAsync(RdpTransferRowViewModel row, ConnectionEntry entry, IReadOnlyList<char> drives)
    {
        var plan = row.Plan;
        var account = entry.FindAccount(plan.AccountId);
        if (AutoStart && RdpTransferSessionSettings.For(entry.ForAccount(account)) is not null)
        {
            var request = new RdpTransferSessionRequest(entry.Name, _kit.AutoStartCommand(plan.Id),
                () => _kit.ReadStatus(plan.Id) is not { State: "done" or "cancelled" });
            Task<IRdpSession> Launch(ConnectionEntry e, string? password)
            {
                var session = _sessions.Open(RdpTransferSessionSettings.For(e)!, password, request);
                row.Attach(session);
                return Task.FromResult<IRdpSession>(session);
            }

            return await _connectRdp(entry.Id, plan.AccountId, [], Launch)
                ? "Harbor's transfer window connects with the drives of this PC shared and starts the copy in the session. " +
                  "Keep it in front until the copy starts; it closes when the copy is done. The copy continues after a dropped connection."
                : "Remote Desktop was not opened. Connect with the drive shared and run this command in the session (Win+R):";
        }

        // SPEC FR-63: mstsc with the needed drives; the user starts the helper.
        CopyText(_kit.StartCommand(plan.Id));
        var line = RdpTransferKit.DriveRedirection(entry.Kind == EntryKind.RdpConnection ? entry.RdpSettings : null, drives);
        var names = string.Join(", ", drives.Select(d => $"{d}:"));
        var hint = AutoStart
            ? "This entry connects through a Remote Desktop Gateway, which Harbor's transfer window does not use, so it opens in Remote Desktop Connection. "
            : "";
        return await _connectRdp(entry.Id, plan.AccountId, [line], null)
            ? $"{hint}Remote Desktop opens with drive {names} of this PC shared. In the session press Win+R, paste with Ctrl+V and press Enter. " +
              "If Windows asks whether to run the file, choose Run. The copy continues after a dropped connection; run the same command again to resume it."
            : "Remote Desktop was not opened. Connect with the drive shared and run this command in the session (Win+R):";
    }

    /// <summary>Reconnects an unfinished transfer in the transfer window (its row's Connect). SPEC FR-65</summary>
    private async Task ReconnectAsync(RdpTransferRowViewModel row)
    {
        if (row.Plan.ConnectionId is not { } id || IsBusy) return;
        if (Entry?.Id != id)
        {
            _dialogs.ShowError("Cannot connect", $"Select {row.Plan.Label} in the main window and open File transfer again to connect this transfer.");
            return;
        }

        var drives = (row.Plan.IsUpload ? row.Plan.Items.Select(i => i.Source) : [row.Plan.ClientFolder])
            .Append(_kit.StartCommand(row.Plan.Id)).Select(ClientDrive).OfType<char>().Distinct().Order().ToList();
        IsBusy = true;
        try
        {
            StartHint = await ConnectAsync(row, Entry, drives);
            StartCommand = _kit.StartCommand(row.Plan.Id);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary><c>C</c> for <c>\\tsclient\C\…</c>.</summary>
    private static char? ClientDrive(string path) =>
        path.StartsWith(@"\\tsclient\", StringComparison.OrdinalIgnoreCase) && path.Length > 11 && char.IsAsciiLetter(path[11])
            ? char.ToUpperInvariant(path[11]) : null;

    private RdpTransferRowViewModel NewRdpRow(RdpTransferPlan plan) =>
        new(plan, _kit, Forget, _sessions.IsAvailable ? ReconnectAsync : null);

    [RelayCommand]
    private void CopyStartCommand()
    {
        if (!string.IsNullOrEmpty(StartCommand)) CopyText(StartCommand);
    }

    internal static void CopyText(string text)
    {
        try
        {
            Clipboard.SetDataObject(text, copy: true);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
        }
    }

    private void Add(IReadOnlyList<(string Source, string Destination)> files, bool upload)
    {
        if (files.Count == 0) return;
        _settings.TransferRemoteFolder = RemoteFolder;
        _saveSettings();

        // SPEC FR-60, FR-64: existing files are skipped, fail, or are replaced; a local one only after asking. A remote
        // one is checked when copying.
        var overwrite = ExistingFiles == ExistingFiles.Replace;
        var skip = ExistingFiles == ExistingFiles.Skip;
        if (overwrite && !upload && files.Count(f => File.Exists(f.Destination)) is var existing and > 0)
        {
            overwrite = _dialogs.ConfirmRisky("Files already exist",
                $"{existing} of the {files.Count} files already exist in the target folder. Replace them?", "Replace");
            skip = !overwrite;
        }

        foreach (var (source, destination) in files)
        {
            var job = NewJob(source, destination, upload);
            job.Overwrite = overwrite;
            job.SkipExisting = skip;
            _queue.Enqueue(job);
        }
    }

    private TransferJob NewJob(string source, string destination, bool upload) => new()
    {
        ConnectionId = Entry?.Id,
        AccountId = AccountId,
        Label = Entry?.Name ?? "",
        IsUpload = upload,
        SourcePath = source,
        DestinationPath = destination,
    };

    [RelayCommand]
    private void ResumeAll()
    {
        foreach (var job in Jobs.OfType<TransferItemViewModel>().Where(j => j.State == TransferState.Paused).ToList()) _queue.Resume(job.Id);
    }

    [RelayCommand]
    private void ClearFinished()
    {
        foreach (var job in Jobs.OfType<TransferItemViewModel>().Where(j => j.State == TransferState.Completed).ToList()) _queue.Remove(job.Id);
        foreach (var row in Jobs.OfType<RdpTransferRowViewModel>().Where(r => r.IsDone).ToList()) row.RemoveCommand.Execute(null);
    }

    private void RefreshRdpRows()
    {
        foreach (var row in Jobs.OfType<RdpTransferRowViewModel>().ToList()) row.Refresh();
        OnPropertyChanged(nameof(Summary));
    }

    private void Apply(TransferJob job)
    {
        if (Jobs.OfType<TransferItemViewModel>().FirstOrDefault(j => j.Id == job.Id) is { } item) item.Update(job);
        else Jobs.Add(new TransferItemViewModel(job, _queue));
        OnPropertyChanged(nameof(Summary));
    }

    private void Forget(string key)
    {
        if (Jobs.FirstOrDefault(j => j.Key == key) is { } row) Jobs.Remove(row);
        OnPropertyChanged(nameof(Summary));
    }

    private static void OnUi(Action action)
    {
        if (Application.Current?.Dispatcher is { } dispatcher) dispatcher.BeginInvoke(action);
    }
}

/// <summary>A row of the transfer list, whichever way it copies. SPEC FR-60, FR-63</summary>
public interface ITransferRow
{
    string Key { get; }
    bool IsActive { get; }
    bool IsPaused { get; }
    bool IsDone { get; }
}

/// <summary>
/// A transfer through the Remote Desktop connection (SPEC FR-63, ADR-0041): one row per plan, showing what the helper in
/// the session reports in <c>status.json</c>. Pausing is closing the helper's window; resuming is running the start
/// command again, or Connect, which opens Harbor's transfer window and starts it there (SPEC FR-65, ADR-0042). That
/// window is closed when the copy is done.
/// </summary>
public sealed partial class RdpTransferRowViewModel : ObservableObject, ITransferRow
{
    private readonly RdpTransferKit _kit;
    private readonly Action<string> _removed;
    private readonly Func<RdpTransferRowViewModel, Task>? _reconnect;
    private IRdpTransferSession? _session;
    private bool _cancelling;

    /// <param name="reconnect">Opens the transfer window for this transfer again; <c>null</c> when it is not available.</param>
    public RdpTransferRowViewModel(RdpTransferPlan plan, RdpTransferKit kit, Action<string> removed,
        Func<RdpTransferRowViewModel, Task>? reconnect = null)
    {
        Plan = plan;
        _kit = kit;
        _removed = removed;
        _reconnect = reconnect;
    }

    public RdpTransferPlan Plan { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName), nameof(Percent), nameof(StatusText), nameof(IsActive), nameof(IsDone), nameof(HasStartCommand),
        nameof(RemoveText), nameof(CanConnect))]
    private RdpTransferStatus? _status;

    /// <summary>The transfer window's state while it is open (SPEC FR-65); <c>null</c> otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(CanConnect), nameof(ConnectText))]
    private RdpTransferSessionState? _sessionState;

    public string Key => Plan.Id;
    public string FileName => Status is { FilesTotal: > 0 } s ? (s.FilesTotal == 1 && s.CurrentFile is { } f ? f : $"{s.FilesTotal} files")
        : Plan.IsUpload ? (Plan.Items.Count == 1 ? Path.GetFileName(Plan.Items[0].Destination) : $"{Plan.Items.Count} files")
        : "Files picked in the session";
    public string Direction => Plan.IsUpload ? $"↑ to {Plan.Label} over Remote Desktop" : $"↓ from {Plan.Label} over Remote Desktop";
    public string Destination => Plan.IsUpload ? $"{Plan.ServerFolder} (on the server)" : Plan.LocalFolder;
    public double Percent => Status is { BytesTotal: > 0 } s ? 100.0 * s.BytesDone / s.BytesTotal : Status?.State == "done" ? 100 : 0;
    public bool CanPause => false;
    public bool CanResume => false;
    public bool HasStartCommand => Status is not { IsFinished: true };
    public bool IsActive => !IsDone && Status?.State != "failed" && Status?.State != "cancelled";
    public bool IsPaused => false;
    public bool IsDone => Status?.State == "done";
    public string RemoveText => Status is null or { IsFinished: true } ? "Remove" : "Cancel";

    /// <summary>Show the open transfer window, or connect one for an unfinished transfer. SPEC FR-65</summary>
    public bool CanConnect => _reconnect is not null && !_cancelling && (SessionOpen || Status?.State is not ("done" or "cancelled"));
    public string ConnectText => SessionOpen ? "Show window" : "Connect";
    private bool SessionOpen => SessionState is { } state && state != RdpTransferSessionState.Closed;

    public string StatusText
    {
        get
        {
            if (_cancelling && Status is not { IsFinished: true }) return "Cancelling…";
            var size = Status is { BytesTotal: > 0 } t
                ? $" · {TransferItemViewModel.FormatSize(t.BytesDone)} of {TransferItemViewModel.FormatSize(t.BytesTotal)}" : "";
            if (Status is null or { State: "failed" } && SessionState is { } session && session != RdpTransferSessionState.Closed)
            {
                // SPEC FR-65: the helper has not reported yet; say what the transfer window is doing.
                return session switch
                {
                    RdpTransferSessionState.Connecting => "Connecting in Harbor's transfer window…",
                    RdpTransferSessionState.Starting => "Signed in; starting the copy in the session…",
                    RdpTransferSessionState.NeedsClick => "Click Start copy in Harbor's transfer window",
                    RdpTransferSessionState.Disconnected => "Disconnected · click Reconnect in Harbor's transfer window",
                    _ => "Starting… if nothing happens, press Win+R, Ctrl+V and Enter in Harbor's transfer window",
                };
            }

            return Status?.State switch
            {
                null => "Waiting to be started in the Remote Desktop session (Win+R, Ctrl+V, Enter)",
                "running" => $"Copying file {Math.Min(Status.FilesDone + 1, Status.FilesTotal)} of {Status.FilesTotal}{size}",
                "waiting" => $"{Status.Error ?? "Connection lost."} The copy continues where it stopped{size}",
                "done" => Status.FilesSkipped > 0
                    ? $"Done · {Status.FilesTotal - Status.FilesSkipped} file(s) copied, {Status.FilesSkipped} skipped (already there){size}"
                    : $"Done · {Status.FilesTotal} file(s){size}",
                "cancelled" => "Cancelled",
                _ => $"Failed · {Status.Error} Connect or run the start command again to retry",
            };
        }
    }

    /// <summary>Follows the transfer window opened for this transfer. SPEC FR-65</summary>
    public void Attach(IRdpTransferSession session)
    {
        if (_session is not null) _session.StateChanged -= OnSessionChanged;
        _session = session;
        session.StateChanged += OnSessionChanged;
        SessionState = session.State;
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (sender is not IRdpTransferSession session) return;
        var state = session.State;
        if (Application.Current?.Dispatcher is { } dispatcher) dispatcher.BeginInvoke(() => Apply(session, state));
    }

    private void Apply(IRdpTransferSession session, RdpTransferSessionState state)
    {
        if (!ReferenceEquals(session, _session)) return;
        SessionState = state;
        if (state == RdpTransferSessionState.Closed)
        {
            session.StateChanged -= OnSessionChanged;
            _session = null;
        }
    }

    public void Refresh()
    {
        var status = _kit.ReadStatus(Plan.Id);
        if (status is not null) Status = status;
        else OnPropertyChanged(nameof(StatusText));
        // SPEC FR-65: the transfer window (with every drive of this PC shared) is closed as soon as the copy is done.
        if (Status is { State: "done" or "cancelled" } && _session is { } session)
        {
            _session = null;
            session.Close();
        }

        if (_cancelling && Status is { IsFinished: true }) Forget();
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (_session is { } session && SessionOpen)
        {
            session.Show();
            return;
        }

        if (_reconnect is not null) await _reconnect(this);
    }

    [RelayCommand]
    private void CopyStartCommand() => TransfersViewModel.CopyText(_kit.StartCommand(Plan.Id));

    /// <summary>Removes a finished or never started transfer; asks a running helper to stop and delete its partial files.</summary>
    [RelayCommand]
    private void Remove()
    {
        if (Status is null or { IsFinished: true })
        {
            _session?.Close();
            Forget();
            return;
        }

        _kit.Cancel(Plan.Id);
        _cancelling = true;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanConnect));
    }

    private void Forget()
    {
        try
        {
            _kit.Delete(Plan.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        _removed(Plan.Id);
    }
}

/// <summary>One row of the transfer list. SPEC FR-60</summary>
public sealed partial class TransferItemViewModel : ObservableObject, ITransferRow
{
    private readonly TransferQueue _queue;

    public TransferItemViewModel(TransferJob job, TransferQueue queue)
    {
        _queue = queue;
        _job = job;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(State), nameof(Percent), nameof(StatusText), nameof(IsFinished), nameof(CanPause), nameof(CanResume),
        nameof(Direction), nameof(Destination), nameof(RemoveText), nameof(IsActive), nameof(IsPaused), nameof(IsDone))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand), nameof(ResumeCommand), nameof(RemoveCommand))]
    private TransferJob _job;

    public Guid Id => Job.Id;
    public string FileName => Job.FileName;
    public string Direction => Job.IsUpload ? $"↑ to {Job.Label}" : $"↓ from {Job.Label}";
    public string Destination => Job.DestinationPath;
    public TransferState State => Job.State;
    public bool IsFinished => Job.IsFinished;
    public double Percent => Job.Progress * 100;
    public bool CanPause => Job.State is TransferState.Queued or TransferState.Running or TransferState.Retrying;
    public bool CanResume => Job.State is TransferState.Paused or TransferState.Failed;
    public string RemoveText => Job.State == TransferState.Completed ? "Remove" : "Cancel";
    public bool HasStartCommand => false;
    public bool CanConnect => false;
    public string ConnectText => "Connect";
    public string Key => Id.ToString();
    public bool IsActive => !IsFinished && State != TransferState.Paused;
    public bool IsPaused => State == TransferState.Paused;
    public bool IsDone => State == TransferState.Completed;

    public string StatusText
    {
        get
        {
            var size = Job.Length >= 0 ? $"{FormatSize(Job.BytesDone)} of {FormatSize(Job.Length)}" : "";
            return Job.State switch
            {
                TransferState.Queued => "Waiting",
                TransferState.Running => $"Copying · {size}",
                TransferState.Retrying => $"Connection lost, retrying from {FormatSize(Job.BytesDone)} · {Job.Error}",
                TransferState.Paused => $"Paused at {FormatSize(Job.BytesDone)}{(Job.Error is { } e ? $" · {e}" : "")}",
                TransferState.Completed when Job.Skipped => "Skipped · already exists",
                TransferState.Completed => $"Done · {FormatSize(Job.Length)}",
                _ => $"Failed · {Job.Error}",
            };
        }
    }

    public void Update(TransferJob job) => Job = job;

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause() => _queue.Pause(Id);

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume() => _queue.Resume(Id);

    /// <summary>Removes a completed row, or stops any other transfer and deletes its partial file.</summary>
    [RelayCommand]
    private void Remove()
    {
        if (State == TransferState.Completed) _queue.Remove(Id);
        else _queue.Cancel(Id);
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes} B"),
        < 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0:0.#} KB"),
        < 1024L * 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (1024.0 * 1024):0.#} MB"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{bytes / (1024.0 * 1024 * 1024):0.##} GB"),
    };
}
