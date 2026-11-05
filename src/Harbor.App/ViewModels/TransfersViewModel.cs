using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Threading;
using Harbor.Core;
using Harbor.Core.Model;
using Harbor.Core.Transfer;
using Harbor.Core.Transfer.Rdp;

namespace Harbor.App.ViewModels;

/// <summary>
/// The File transfer window (SPEC FR-60, FR-61, FR-63, ADR-0039, ADR-0041): upload files or a folder to the selected entry's
/// host, download files from it, and follow every transfer, which survives dropped connections and restarts. Copies go
/// either through the Remote Desktop connection (only port 3389 needed: a helper started in the session copies through the
/// PC's shared drive) or over a network share (port 445, run here by the <see cref="TransferQueue"/>).
/// </summary>
public sealed partial class TransfersViewModel : ObservableObject
{
    private static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(2);

    private readonly TransferQueue _queue;
    private readonly RdpTransferKit _kit;
    private readonly IDialogService _dialogs;
    private readonly Func<TransferJob, CancellationToken, Task> _connectShares;
    private readonly Func<Guid, Guid?, IReadOnlyList<string>, Task<bool>> _connectRdp;
    private readonly HarborSettings _settings;
    private readonly Action _saveSettings;
    private readonly DispatcherTimer _statusTimer = new() { Interval = StatusInterval };

    /// <param name="connectRdp">Connects entry, account with extra .rdp lines; false when the user cancelled.</param>
    public TransfersViewModel(TransferQueue queue, RdpTransferKit kit, IDialogService dialogs,
        Func<TransferJob, CancellationToken, Task> connectShares, Func<Guid, Guid?, IReadOnlyList<string>, Task<bool>> connectRdp,
        HarborSettings settings, Action saveSettings)
    {
        _queue = queue;
        _kit = kit;
        _dialogs = dialogs;
        _connectShares = connectShares;
        _connectRdp = connectRdp;
        _settings = settings;
        _saveSettings = saveSettings;
        _useRemoteDesktop = settings.TransferMethod != TransferMethod.NetworkShare;
        _serverFolder = settings.TransferServerFolder;

        foreach (var plan in kit.List()) Jobs.Add(new RdpTransferRowViewModel(plan, kit, Forget));
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

    public string MethodHint => UseRemoteDesktop
        ? "Needs only the Remote Desktop port. Harbor connects with the needed drive of this PC shared into the session; you start the copy there with Win+R and Ctrl+V."
        : "Copies over Windows file sharing (port 445) with the entry's account, run from this PC.";

    /// <summary>Network share mode: folder on the host as a UNC path (<c>\\host\C$\Users\Public\Downloads</c>).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadFilesCommand), nameof(UploadFolderCommand), nameof(DownloadCommand))]
    private string _remoteFolder = "";

    /// <summary>Remote Desktop mode: folder on the server as the server sees it (<c>%USERPROFILE%\Downloads</c>).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadFilesCommand), nameof(UploadFolderCommand), nameof(DownloadCommand))]
    private string _serverFolder;

    /// <summary>Remote Desktop mode: replace files that already exist at the destination.</summary>
    [ObservableProperty] private bool _replaceExisting;

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
                ServerFolder.Trim(), ReplaceExisting));
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
            await StartRdpAsync(() => _kit.CreateUpload(Entry!.Name, Entry.Id, files, ServerFolder.Trim(), ReplaceExisting));
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
            await StartRdpAsync(() => _kit.CreateDownload(Entry!.Name, Entry.Id, ServerFolder.Trim(), local, ReplaceExisting));
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
    /// Writes the plan, puts the start command on the clipboard and connects with the plan's drives shared into the
    /// session, where the user starts the helper (Win+R, Ctrl+V, Enter). SPEC FR-63, ADR-0041
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
        Jobs.Insert(0, new RdpTransferRowViewModel(prepared.Plan, _kit, Forget));
        OnPropertyChanged(nameof(Summary));
        StartCommand = prepared.StartCommand;
        CopyStartCommand();

        var drives = string.Join(", ", prepared.Drives.Select(d => $"{d}:"));
        IsBusy = true;
        try
        {
            var line = RdpTransferKit.DriveRedirection(entry.Kind == EntryKind.RdpConnection ? entry.RdpSettings : null, prepared.Drives);
            StartHint = await _connectRdp(entry.Id, AccountId, [line])
                ? $"Remote Desktop opens with drive {drives} of this PC shared. In the session press Win+R, paste with Ctrl+V and press Enter. " +
                  "If Windows asks whether to run the file, choose Run. The copy continues after a dropped connection; run the same command again to resume it."
                : "Remote Desktop was not opened. Connect with the drive shared and run this command in the session (Win+R):";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CopyStartCommand()
    {
        if (string.IsNullOrEmpty(StartCommand)) return;
        try
        {
            Clipboard.SetDataObject(StartCommand, copy: true);
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

        // An existing destination file is only replaced after asking (SPEC FR-60); a remote one is checked when copying.
        var existing = upload ? 0 : files.Count(f => File.Exists(f.Destination));
        var overwrite = existing > 0 && _dialogs.ConfirmRisky("Files already exist",
            $"{existing} of the {files.Count} files already exist in the target folder. Replace them?", "Replace");
        foreach (var (source, destination) in files)
        {
            if (!upload && !overwrite && File.Exists(destination)) continue;
            var job = NewJob(source, destination, upload);
            job.Overwrite = overwrite;
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
/// command again.
/// </summary>
public sealed partial class RdpTransferRowViewModel : ObservableObject, ITransferRow
{
    private readonly RdpTransferPlan _plan;
    private readonly RdpTransferKit _kit;
    private readonly Action<string> _removed;
    private bool _cancelling;

    public RdpTransferRowViewModel(RdpTransferPlan plan, RdpTransferKit kit, Action<string> removed)
    {
        _plan = plan;
        _kit = kit;
        _removed = removed;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName), nameof(Percent), nameof(StatusText), nameof(IsActive), nameof(IsDone), nameof(HasStartCommand),
        nameof(RemoveText))]
    private RdpTransferStatus? _status;

    public string Key => _plan.Id;
    public string FileName => Status is { FilesTotal: > 0 } s ? (s.FilesTotal == 1 && s.CurrentFile is { } f ? f : $"{s.FilesTotal} files")
        : _plan.IsUpload ? (_plan.Items.Count == 1 ? Path.GetFileName(_plan.Items[0].Destination) : $"{_plan.Items.Count} files")
        : "Files picked in the session";
    public string Direction => _plan.IsUpload ? $"↑ to {_plan.Label} over Remote Desktop" : $"↓ from {_plan.Label} over Remote Desktop";
    public string Destination => _plan.IsUpload ? $"{_plan.ServerFolder} (on the server)" : _plan.LocalFolder;
    public double Percent => Status is { BytesTotal: > 0 } s ? 100.0 * s.BytesDone / s.BytesTotal : Status?.State == "done" ? 100 : 0;
    public bool CanPause => false;
    public bool CanResume => false;
    public bool HasStartCommand => Status is not { IsFinished: true };
    public bool IsActive => !IsDone && Status?.State != "failed" && Status?.State != "cancelled";
    public bool IsPaused => false;
    public bool IsDone => Status?.State == "done";
    public string RemoveText => Status is null or { IsFinished: true } ? "Remove" : "Cancel";

    public string StatusText
    {
        get
        {
            if (_cancelling && Status is not { IsFinished: true }) return "Cancelling…";
            var size = Status is { BytesTotal: > 0 } t
                ? $" · {TransferItemViewModel.FormatSize(t.BytesDone)} of {TransferItemViewModel.FormatSize(t.BytesTotal)}" : "";
            return Status?.State switch
            {
                null => "Waiting to be started in the Remote Desktop session (Win+R, Ctrl+V, Enter)",
                "running" => $"Copying file {Math.Min(Status.FilesDone + 1, Status.FilesTotal)} of {Status.FilesTotal}{size}",
                "waiting" => $"{Status.Error ?? "Connection lost."} The copy continues where it stopped{size}",
                "done" => $"Done · {Status.FilesTotal} file(s){size}",
                "cancelled" => "Cancelled",
                _ => $"Failed · {Status.Error} Run the start command again to retry",
            };
        }
    }

    public void Refresh()
    {
        var status = _kit.ReadStatus(_plan.Id);
        if (status is not null) Status = status;
        else OnPropertyChanged(nameof(StatusText));
        if (_cancelling && Status is { IsFinished: true }) Forget();
    }

    [RelayCommand]
    private void CopyStartCommand()
    {
        try
        {
            Clipboard.SetDataObject(_kit.StartCommand(_plan.Id), copy: true);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
        }
    }

    /// <summary>Removes a finished or never started transfer; asks a running helper to stop and delete its partial files.</summary>
    [RelayCommand]
    private void Remove()
    {
        if (Status is null or { IsFinished: true })
        {
            Forget();
            return;
        }

        _kit.Cancel(_plan.Id);
        _cancelling = true;
        OnPropertyChanged(nameof(StatusText));
    }

    private void Forget()
    {
        try
        {
            _kit.Delete(_plan.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        _removed(_plan.Id);
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
