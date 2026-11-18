using System.Globalization;

namespace Harbor.Core.Transfer;

/// <summary>
/// Where a transfer of several files stands as a whole (SPEC FR-70): files and bytes of all of them, so the bar is a share
/// of the full size, and the file being copied now.
/// </summary>
/// <param name="CurrentFile">The file being copied (or retried) now, relative to the batch folder; <c>null</c> when none.</param>
/// <param name="CurrentBytesDone">Bytes of <paramref name="CurrentFile"/> copied so far.</param>
/// <param name="CurrentBytes">Size of <paramref name="CurrentFile"/>; 0 when not known.</param>
/// <param name="Error">The first failed file's error, or the retrying file's.</param>
public sealed record TransferBatchProgress(
    TransferState State,
    int FilesTotal,
    int FilesDone,
    int FilesSkipped,
    int FilesFailed,
    long BytesDone,
    long BytesTotal,
    string? CurrentFile,
    long CurrentBytesDone,
    long CurrentBytes,
    string? Error)
{
    /// <summary>0..1 of the bytes of all files; skipped files count for neither part.</summary>
    public double Progress => BytesTotal > 0 ? Math.Clamp((double)BytesDone / BytesTotal, 0, 1)
        : FilesTotal > 0 && FilesDone == FilesTotal ? 1 : 0;

    /// <summary>
    /// Sums the files of one batch. Its state is the most urgent of theirs: copying, retrying, waiting, paused, failed,
    /// then done.
    /// </summary>
    public static TransferBatchProgress Of(IReadOnlyCollection<TransferJob> jobs, string? folder = null)
    {
        long done = 0, total = 0;
        int finished = 0, skipped = 0, failed = 0;
        TransferJob? current = null;
        foreach (var job in jobs)
        {
            if (job.Skipped)
            {
                skipped++;
                finished++;
                continue;
            }

            total += job.ExpectedLength;
            done += Math.Min(job.BytesDone, job.ExpectedLength);
            if (job.State == TransferState.Completed) finished++;
            if (job.State == TransferState.Failed) failed++;
            if (job.State is TransferState.Running or TransferState.Retrying && current?.State != TransferState.Running) current = job;
        }

        var states = jobs.Select(j => j.State).ToHashSet();
        var state = states.Contains(TransferState.Running) ? TransferState.Running
            : states.Contains(TransferState.Retrying) ? TransferState.Retrying
            : states.Contains(TransferState.Queued) ? TransferState.Queued
            : states.Contains(TransferState.Paused) ? TransferState.Paused
            : states.Contains(TransferState.Failed) ? TransferState.Failed
            : TransferState.Completed;
        var error = current?.State == TransferState.Retrying ? current.Error
            : jobs.FirstOrDefault(j => j.State == TransferState.Failed)?.Error;
        return new(state, jobs.Count, finished, skipped, failed, done, total,
            current is null ? null : Relative(current.DestinationPath, folder ?? current.BatchDestination),
            current?.BytesDone ?? 0, current?.ExpectedLength ?? 0, error);
    }

    /// <summary><c>sub\file.txt</c> for <c>\\host\share\folder\sub\file.txt</c> in <c>\\host\share\folder</c>; the name otherwise.</summary>
    public static string Relative(string path, string? folder)
    {
        if (!string.IsNullOrEmpty(folder))
        {
            var root = folder.TrimEnd('\\', '/');
            if (path.Length > root.Length + 1 && path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && path[root.Length] is '\\' or '/')
            {
                return path[(root.Length + 1)..];
            }
        }

        var cut = path.LastIndexOfAny(['\\', '/']);
        return cut >= 0 ? path[(cut + 1)..] : path;
    }
}

/// <summary>
/// Average speed of a transfer while it copies (SPEC FR-70): bytes copied over the time spent copying, from samples taken
/// as progress is shown. Time spent paused, waiting or retrying does not count, and a step back (a chunk copied again
/// after a dropped connection) is not taken off.
/// </summary>
public sealed class TransferRate
{
    /// <summary>Copying time before a speed is shown, so the first chunk does not give a wild figure.</summary>
    public static readonly TimeSpan MinimumTime = TimeSpan.FromSeconds(2);

    private long? _lastBytes;
    private DateTimeOffset _lastTime;
    private long _bytes;
    private TimeSpan _time;

    /// <summary>Records that <paramref name="bytesDone"/> bytes are done at <paramref name="now"/>.</summary>
    /// <param name="copying">The transfer is copying now; time to the next sample counts only while this is true.</param>
    public void Sample(long bytesDone, bool copying, DateTimeOffset now)
    {
        if (_lastBytes is { } last && now > _lastTime)
        {
            _time += now - _lastTime;
            if (bytesDone > last) _bytes += bytesDone - last;
        }

        _lastBytes = copying ? bytesDone : null;
        _lastTime = now;
    }

    /// <summary>Bytes per second on average; <c>null</c> until it has copied for <see cref="MinimumTime"/>.</summary>
    public double? BytesPerSecond => _time >= MinimumTime ? _bytes / _time.TotalSeconds : null;

    /// <summary>Time to copy <paramref name="bytesLeft"/> more at the average speed; <c>null</c> when it is not known.</summary>
    public TimeSpan? TimeLeft(long bytesLeft) =>
        BytesPerSecond is > 0.0 and var speed && bytesLeft >= 0 ? TimeSpan.FromSeconds(Math.Min(bytesLeft / speed, TimeSpan.MaxValue.TotalSeconds / 2)) : null;
}

/// <summary>Sizes, speeds and times as the transfer list shows them. SPEC FR-60, FR-70</summary>
public static class TransferText
{
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes} B"),
        < 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0:0.#} KB"),
        < 1024L * 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (1024.0 * 1024):0.#} MB"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{bytes / (1024.0 * 1024 * 1024):0.##} GB"),
    };

    /// <summary><c>12.5 MB/s</c>.</summary>
    public static string Speed(double bytesPerSecond) => Size((long)Math.Round(bytesPerSecond)) + "/s";

    /// <summary><c>less than a minute left</c>, <c>about 5 min left</c>, <c>about 2 h 10 min left</c>.</summary>
    public static string TimeLeft(TimeSpan time) => time.TotalSeconds switch
    {
        < 60 => "less than a minute left",
        < 3600 => string.Create(CultureInfo.CurrentCulture, $"about {Math.Ceiling(time.TotalMinutes):0} min left"),
        _ => string.Create(CultureInfo.CurrentCulture, $"about {(int)time.TotalHours} h {time.Minutes} min left"),
    };

    /// <summary><c>1.2 GB of 4.5 GB (27%)</c>, then speed and time left when they are known.</summary>
    public static string Progress(long done, long total, TransferRate? rate, bool copying)
    {
        var parts = new List<string>
        {
            total > 0
                ? string.Create(CultureInfo.CurrentCulture, $"{Size(done)} of {Size(total)} ({Math.Floor(100.0 * Math.Min(done, total) / total):0}%)")
                : Size(done),
        };
        if (rate?.BytesPerSecond is { } speed) parts.Add($"{Speed(speed)} average");
        if (copying && total > 0 && rate?.TimeLeft(total - done) is { } left) parts.Add(TimeLeft(left));
        return string.Join(" · ", parts);
    }
}
