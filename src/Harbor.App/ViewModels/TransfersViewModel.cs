using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Harbor.Core.Model;
using Harbor.Core.Transfer;

namespace Harbor.App.ViewModels;

/// <summary>
/// The File transfer window (SPEC FR-60, FR-61, ADR-0039): upload files or a folder to the selected entry's host, download
/// files from it, and follow every transfer, which survives dropped connections and restarts. Copies run in the
/// <see cref="TransferQueue"/>; this view model only adds jobs and mirrors their state.
/// </summary>
public sealed partial class TransfersViewModel : ObservableObject
{
    private readonly TransferQueue _queue;
    private readonly IDialogService _dialogs;
    private readonly Func<TransferJob, CancellationToken, Task> _connect;
    private readonly Action<string> _rememberFolder;

    public TransfersViewModel(TransferQueue queue, IDialogService dialogs, Func<TransferJob, CancellationToken, Task> connect,
        string remoteFolder, Action<string> rememberFolder)
    {
        _queue = queue;
        _dialogs = dialogs;
        _connect = connect;
        _remoteFolderSetting = remoteFolder;
        _rememberFolder = rememberFolder;
        foreach (var job in queue.Jobs) Jobs.Add(new TransferItemViewModel(job, queue));
        queue.JobChanged += (_, job) => OnUi(() => Apply(job));
        queue.JobRemoved += (_, id) => OnUi(() =>
        {
            if (Jobs.FirstOrDefault(j => j.Id == id) is { } item) Jobs.Remove(item);
            OnPropertyChanged(nameof(Summary));
        });
    }

    private string _remoteFolderSetting;

    public ObservableCollection<TransferItemViewModel> Jobs { get; } = [];

    /// <summary>The entry whose host and account the next transfer uses.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetText))]
    [NotifyCanExecuteChangedFor(nameof(UploadFilesCommand), nameof(UploadFolderCommand), nameof(DownloadCommand))]
    private ConnectionEntry? _entry;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadFilesCommand), nameof(UploadFolderCommand), nameof(DownloadCommand))]
    private Guid? _accountId;

    /// <summary>Folder on the host, as a UNC path (<c>\\host\C$\Users\Public\Downloads</c>).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadFilesCommand), nameof(UploadFolderCommand), nameof(DownloadCommand))]
    private string _remoteFolder = "";

    [ObservableProperty] private bool _isBusy;

    public string TargetText => Entry is null ? "No entry selected"
        : $"{Entry.Name} · {Entry.Host} · as {Entry.QualifiedUserNameFor(Entry.FindAccount(AccountId)) ?? "your Windows account"}";

    public string Summary
    {
        get
        {
            var active = Jobs.Count(j => !j.IsFinished && j.State != TransferState.Paused);
            var paused = Jobs.Count(j => j.State == TransferState.Paused);
            return Jobs.Count == 0 ? "No transfers yet."
                : string.Create(CultureInfo.CurrentCulture, $"{active} active, {paused} paused, {Jobs.Count(j => j.State == TransferState.Completed)} done");
        }
    }

    /// <summary>Points the window at <paramref name="entry"/> (with <paramref name="accountId"/>), as selected in the main window.</summary>
    public void Target(ConnectionEntry entry, Guid? accountId)
    {
        Entry = entry;
        AccountId = accountId;
        RemoteFolder = UncPath.ForHost(entry.Host ?? "", _remoteFolderSetting);
    }

    private bool CanTransfer => Entry is { Host.Length: > 0 } && UncPath.ShareRoot(RemoteFolder) is not null && !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        UploadFilesCommand.NotifyCanExecuteChanged();
        UploadFolderCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanTransfer))]
    private void UploadFiles()
    {
        if (_dialogs.PickFilesToUpload() is not { Length: > 0 } files) return;
        Add(files.Select(f => (f, UncPath.Combine(RemoteFolder, Path.GetFileName(f)))).ToList(), upload: true);
    }

    [RelayCommand(CanExecute = nameof(CanTransfer))]
    private void UploadFolder()
    {
        if (_dialogs.PickFolder("Folder to upload", null) is not { } folder) return;
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        List<(string, string)> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => (f, UncPath.Combine(RemoteFolder, Path.Combine(name, Path.GetRelativePath(folder, f))))).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError("Cannot upload the folder", ex.Message);
            return;
        }

        Add(files, upload: true);
    }

    /// <summary>Connects the share with the entry's account, lets the user pick files there and a local folder for them.</summary>
    [RelayCommand(CanExecute = nameof(CanTransfer))]
    private async Task DownloadAsync()
    {
        IsBusy = true;
        try
        {
            await _connect(NewJob(RemoteFolder, RemoteFolder, upload: false), CancellationToken.None);
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

    private void Add(IReadOnlyList<(string Source, string Destination)> files, bool upload)
    {
        if (files.Count == 0) return;
        _rememberFolder(_remoteFolderSetting = RemoteFolder);

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
        foreach (var job in Jobs.Where(j => j.State == TransferState.Paused).ToList()) _queue.Resume(job.Id);
    }

    [RelayCommand]
    private void ClearFinished()
    {
        foreach (var job in Jobs.Where(j => j.State == TransferState.Completed).ToList()) _queue.Remove(job.Id);
    }

    private void Apply(TransferJob job)
    {
        if (Jobs.FirstOrDefault(j => j.Id == job.Id) is { } item) item.Update(job);
        else Jobs.Add(new TransferItemViewModel(job, _queue));
        OnPropertyChanged(nameof(Summary));
    }

    private static void OnUi(Action action)
    {
        if (Application.Current?.Dispatcher is { } dispatcher) dispatcher.BeginInvoke(action);
    }
}

/// <summary>One row of the transfer list. SPEC FR-60</summary>
public sealed partial class TransferItemViewModel : ObservableObject
{
    private readonly TransferQueue _queue;

    public TransferItemViewModel(TransferJob job, TransferQueue queue)
    {
        _queue = queue;
        _job = job;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(State), nameof(Percent), nameof(StatusText), nameof(IsFinished), nameof(CanPause), nameof(CanResume),
        nameof(Direction), nameof(Destination), nameof(RemoveText))]
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
