namespace Harbor.Core.Transfer;

/// <summary>
/// How long a dropped transfer is retried: waits grow from <see cref="FirstDelay"/> to <see cref="MaxDelay"/>; after
/// <see cref="GiveUpAfter"/> without a new chunk the transfer is paused (and can be resumed later). SPEC FR-61
/// </summary>
public sealed record TransferRetryPolicy(TimeSpan FirstDelay, TimeSpan MaxDelay, TimeSpan GiveUpAfter)
{
    public static TransferRetryPolicy Default { get; } = new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10));
}

/// <summary>
/// Runs transfers one at a time, in the order they were added, keeping each one's state in the <see cref="TransferJournal"/>.
/// A connection problem (<see cref="IOException"/>, <see cref="TimeoutException"/>) is retried automatically from the last
/// recorded chunk; transfers left unfinished when the app closed come back paused and resume where they stopped.
/// <c>prepare</c> runs before every attempt, e.g. to (re)connect a share with the entry's credentials.
/// SPEC FR-60, FR-61, ADR-0038
/// </summary>
public sealed class TransferQueue : IAsyncDisposable
{
    private readonly TransferJournal _journal;
    private readonly ResumableFileCopier _copier;
    private readonly ITransferFileSystem _fs;
    private readonly Func<TransferJob, CancellationToken, Task>? _prepare;
    private readonly TransferRetryPolicy _retry;
    private readonly Action<Exception>? _onError;
    private readonly Lock _gate = new();
    private readonly List<TransferJob> _jobs = [];
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _runner;
    private Current? _current;

    private sealed class Current(Guid id, CancellationTokenSource cancel)
    {
        public Guid Id { get; } = id;
        public CancellationTokenSource Cancel { get; } = cancel;
        public bool Discard { get; set; }
    }

    public TransferQueue(TransferJournal journal, ITransferFileSystem? fileSystem = null,
        Func<TransferJob, CancellationToken, Task>? prepare = null, TransferRetryPolicy? retry = null, Action<Exception>? onError = null)
    {
        _journal = journal;
        _fs = fileSystem ?? LocalTransferFileSystem.Instance;
        _copier = new ResumableFileCopier(_fs);
        _prepare = prepare;
        _retry = retry ?? TransferRetryPolicy.Default;
        _onError = onError;
    }

    /// <summary>Raised (on a background thread) with a copy of a job whenever it changes or is added.</summary>
    public event EventHandler<TransferJob>? JobChanged;

    /// <summary>Raised when a job is cancelled or removed.</summary>
    public event EventHandler<Guid>? JobRemoved;

    /// <summary>Copies of all jobs, in queue order.</summary>
    public IReadOnlyList<TransferJob> Jobs
    {
        get
        {
            lock (_gate) return _jobs.Select(j => j.Clone()).ToList();
        }
    }

    /// <summary>
    /// Loads the journal and starts the queue. Jobs that were running or waiting when the app closed come back paused,
    /// ready to resume where they stopped.
    /// </summary>
    public void Start()
    {
        foreach (var job in _journal.LoadAll((file, ex) => _onError?.Invoke(new IOException($"Transfer journal {file} is unreadable.", ex))))
        {
            if (job.State is TransferState.Running or TransferState.Retrying or TransferState.Queued)
            {
                job.State = TransferState.Paused;
                job.Error = "Stopped when the app closed. Resume to continue where it stopped.";
                Persist(job);
            }

            lock (_gate) _jobs.Add(job);
        }

        _runner ??= Task.Run(() => RunAsync(_shutdown.Token));
    }

    public void Enqueue(TransferJob job)
    {
        job.State = TransferState.Queued;
        lock (_gate) _jobs.Add(job.Clone());
        Persist(job);
        JobChanged?.Invoke(this, job.Clone());
        _signal.Release();
    }

    /// <summary>Resumes a paused or failed job from where it stopped.</summary>
    public void Resume(Guid id) => Update(id, j => j.State is TransferState.Paused or TransferState.Failed, j =>
    {
        j.State = TransferState.Queued;
        j.Error = null;
    }, signal: true);

    public void Pause(Guid id)
    {
        lock (_gate)
        {
            if (_current?.Id == id)
            {
                _current.Cancel.Cancel();
                return;
            }
        }

        Update(id, j => j.State == TransferState.Queued, j => j.State = TransferState.Paused, signal: false);
    }

    /// <summary>Stops a job, deletes its partial file and forgets it.</summary>
    public void Cancel(Guid id)
    {
        TransferJob? job;
        lock (_gate)
        {
            if (_current?.Id == id)
            {
                _current.Discard = true;
                _current.Cancel.Cancel();
                return;
            }

            job = _jobs.FirstOrDefault(j => j.Id == id);
            if (job is null) return;
            _jobs.Remove(job);
        }

        Forget(job, deletePartial: job.State != TransferState.Completed);
    }

    /// <summary>Removes a completed or failed job from the list (the copied file stays).</summary>
    public void Remove(Guid id)
    {
        TransferJob? job;
        lock (_gate)
        {
            job = _jobs.FirstOrDefault(j => j.Id == id && j.IsFinished);
            if (job is null) return;
            _jobs.Remove(job);
        }

        Forget(job, deletePartial: false);
    }

    private void Update(Guid id, Func<TransferJob, bool> allowed, Action<TransferJob> change, bool signal)
    {
        TransferJob copy;
        lock (_gate)
        {
            if (_jobs.FirstOrDefault(j => j.Id == id) is not { } job || !allowed(job)) return;
            change(job);
            job.UpdatedUtc = DateTimeOffset.UtcNow;
            copy = job.Clone();
        }

        Persist(copy);
        JobChanged?.Invoke(this, copy);
        if (signal) _signal.Release();
    }

    private async Task RunAsync(CancellationToken shutdown)
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync(shutdown).ConfigureAwait(false);
                while (TakeNext(shutdown) is { } next)
                {
                    await RunJobAsync(next.Work, next.Current).ConfigureAwait(false);
                    lock (_gate) _current = null;
                    next.Current.Cancel.Dispose();
                }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
    }

    private (TransferJob Work, Current Current)? TakeNext(CancellationToken shutdown)
    {
        TransferJob work;
        Current current;
        lock (_gate)
        {
            if (shutdown.IsCancellationRequested || _jobs.FirstOrDefault(j => j.State == TransferState.Queued) is not { } job) return null;
            job.State = TransferState.Running;
            work = job.Clone();
            current = _current = new Current(job.Id, CancellationTokenSource.CreateLinkedTokenSource(shutdown));
        }

        Store(work);
        return (work, current);
    }

    private async Task RunJobAsync(TransferJob work, Current current)
    {
        var token = current.Cancel.Token;
        var delay = _retry.FirstDelay;
        var lastProgress = DateTimeOffset.UtcNow;
        var bytesAtLastProgress = work.BytesDone;
        while (true)
        {
            try
            {
                if (_prepare is not null) await _prepare(work, token).ConfigureAwait(false);
                await _copier.CopyAsync(work, Store, token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Stopped(work, current);
                return;
            }
            catch (Exception ex) when (IsRetryable(ex))
            {
                if (work.BytesDone > bytesAtLastProgress)
                {
                    bytesAtLastProgress = work.BytesDone;
                    lastProgress = DateTimeOffset.UtcNow;
                    delay = _retry.FirstDelay;
                }

                if (DateTimeOffset.UtcNow - lastProgress >= _retry.GiveUpAfter)
                {
                    work.State = TransferState.Paused;
                    work.Error = $"{ex.Message} Paused after retrying; resume to continue where it stopped.";
                    Store(work);
                    return;
                }

                work.State = TransferState.Retrying;
                work.Error = ex.Message;
                Store(work);
                try
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Stopped(work, current);
                    return;
                }

                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _retry.MaxDelay.Ticks));
                work.State = TransferState.Running;
                Store(work);
            }
            catch (Exception ex)
            {
                work.State = TransferState.Failed;
                work.Error = ex.Message;
                Store(work);
                return;
            }
        }
    }

    /// <summary>A connection problem worth retrying, as opposed to a missing source, an existing target or access denied.</summary>
    internal static bool IsRetryable(Exception ex) =>
        ex is not TransferFailedException and not FileNotFoundException and (IOException or TimeoutException);

    private void Stopped(TransferJob work, Current current)
    {
        if (current.Discard)
        {
            lock (_gate) _jobs.RemoveAll(j => j.Id == work.Id);
            Forget(work, deletePartial: true);
            return;
        }

        work.State = TransferState.Paused;
        work.Error = _shutdown.IsCancellationRequested ? "Stopped when the app closed. Resume to continue where it stopped." : null;
        Store(work);
    }

    /// <summary>Replaces the stored job with <paramref name="work"/>, journals it and tells listeners.</summary>
    private void Store(TransferJob work)
    {
        var copy = work.Clone();
        lock (_gate)
        {
            var i = _jobs.FindIndex(j => j.Id == work.Id);
            if (i < 0) return; // removed meanwhile
            _jobs[i] = copy;
        }

        Persist(copy);
        JobChanged?.Invoke(this, copy.Clone());
    }

    private void Persist(TransferJob job)
    {
        try
        {
            _journal.Save(job);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _onError?.Invoke(ex);
        }
    }

    private void Forget(TransferJob job, bool deletePartial)
    {
        try
        {
            if (deletePartial) _fs.Delete(job.PartialPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _onError?.Invoke(ex);
        }

        try
        {
            _journal.Delete(job.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _onError?.Invoke(ex);
        }

        JobRemoved?.Invoke(this, job.Id);
    }

    /// <summary>Stops the running job (it stays resumable in the journal) and the queue.</summary>
    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        if (_runner is not null)
        {
            await _runner.ConfigureAwait(false);
        }

        _shutdown.Dispose();
        _signal.Dispose();
    }
}
