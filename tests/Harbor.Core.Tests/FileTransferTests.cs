using Harbor.Core.Transfer;

namespace Harbor.Core.Tests;

// SPEC FR-60, FR-61, ADR-0039: resumable file transfer that survives dropped connections.
public sealed class FileTransferTests : IDisposable
{
    private const int Chunk = 1024;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"harbor-transfer-{Guid.NewGuid():N}");

    public FileTransferTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Source(int length, int seed = 1)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        var path = Path.Combine(_dir, $"source-{seed}.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private TransferJob Job(string source, string? name = null) => new()
    {
        SourcePath = source,
        DestinationPath = Path.Combine(_dir, "remote", name ?? "copy.bin"),
        ChunkSize = Chunk,
    };

    [Fact]
    public async Task Copies_a_file_in_chunks_and_gives_it_its_name_at_the_end()
    {
        var job = Job(Source(5 * Chunk + 17));
        var checkpoints = new List<long>();

        await new ResumableFileCopier().CopyAsync(job, j => checkpoints.Add(j.BytesDone));

        Assert.Equal(TransferState.Completed, job.State);
        Assert.Equal(File.ReadAllBytes(job.SourcePath), File.ReadAllBytes(job.DestinationPath));
        Assert.False(File.Exists(job.PartialPath));
        Assert.Equal(6, job.ChunkHashes.Count);
        Assert.Contains(3L * Chunk, checkpoints);
    }

    [Fact]
    public async Task A_dropped_connection_resumes_from_the_last_recorded_chunk()
    {
        var job = Job(Source(10 * Chunk));
        var flaky = new FlakyFileSystem { FailAfterBytes = 4 * Chunk + 100 };

        await Assert.ThrowsAsync<IOException>(() => new ResumableFileCopier(flaky).CopyAsync(job, _ => { }));
        Assert.Equal(4L * Chunk, job.BytesDone); // the half-written fifth chunk is not counted

        flaky.FailAfterBytes = null;
        await new ResumableFileCopier(flaky).CopyAsync(job, _ => { });

        Assert.Equal(File.ReadAllBytes(job.SourcePath), File.ReadAllBytes(job.DestinationPath));
        Assert.Equal(10L * Chunk, 4L * Chunk + flaky.BytesWrittenAfterRecovery); // nothing copied twice
    }

    [Fact]
    public async Task A_corrupted_last_chunk_is_copied_again()
    {
        var job = Job(Source(6 * Chunk));
        await Assert.ThrowsAsync<IOException>(() => new ResumableFileCopier(new FlakyFileSystem { FailAfterBytes = 3 * Chunk }).CopyAsync(job, _ => { }));
        Assert.Equal(3L * Chunk, job.BytesDone);

        // The last recorded chunk did not really reach the disk.
        using (var partial = File.OpenWrite(job.PartialPath))
        {
            partial.Seek(2 * Chunk + 10, SeekOrigin.Begin);
            partial.Write([0xFF, 0xFE, 0xFD]);
        }

        await new ResumableFileCopier().CopyAsync(job, _ => { });
        Assert.Equal(File.ReadAllBytes(job.SourcePath), File.ReadAllBytes(job.DestinationPath));
    }

    [Fact]
    public async Task A_changed_source_starts_over()
    {
        var source = Source(4 * Chunk);
        var job = Job(source);
        await Assert.ThrowsAsync<IOException>(() => new ResumableFileCopier(new FlakyFileSystem { FailAfterBytes = 2 * Chunk }).CopyAsync(job, _ => { }));

        var changed = new byte[3 * Chunk];
        new Random(7).NextBytes(changed);
        File.WriteAllBytes(source, changed);
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(1));

        await new ResumableFileCopier().CopyAsync(job, _ => { });
        Assert.Equal(changed, File.ReadAllBytes(job.DestinationPath));
    }

    [Fact]
    public async Task An_existing_destination_is_not_replaced_unless_asked()
    {
        var job = Job(Source(Chunk));
        Directory.CreateDirectory(Path.GetDirectoryName(job.DestinationPath)!);
        File.WriteAllText(job.DestinationPath, "keep me");

        await Assert.ThrowsAsync<TransferFailedException>(() => new ResumableFileCopier().CopyAsync(job, _ => { }));
        Assert.Equal("keep me", File.ReadAllText(job.DestinationPath));

        job.Overwrite = true;
        await new ResumableFileCopier().CopyAsync(job, _ => { });
        Assert.Equal(File.ReadAllBytes(job.SourcePath), File.ReadAllBytes(job.DestinationPath));
    }

    [Fact]
    public async Task The_queue_retries_dropped_connections_until_the_file_is_copied()
    {
        var flaky = new FlakyFileSystem { FailEveryBytes = 3 * Chunk + 1 };
        var journal = new TransferJournal(Path.Combine(_dir, "journal"));
        var prepared = 0;
        await using var queue = new TransferQueue(journal, flaky, (_, _) => { Interlocked.Increment(ref prepared); return Task.CompletedTask; },
            new TransferRetryPolicy(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(5), TimeSpan.FromMinutes(1)));
        var states = new List<TransferState>();
        queue.JobChanged += (_, j) => { lock (states) states.Add(j.State); };
        queue.Start();

        var job = Job(Source(10 * Chunk));
        queue.Enqueue(job);
        var done = await WaitAsync(queue, job.Id, TransferState.Completed);

        Assert.Equal(File.ReadAllBytes(job.SourcePath), File.ReadAllBytes(job.DestinationPath));
        Assert.Equal(10L * Chunk, done.BytesDone);
        lock (states) Assert.Contains(TransferState.Retrying, states);
        Assert.True(prepared > 1); // the share is reconnected before every attempt
        Assert.Equal(TransferState.Completed, Assert.Single(journal.LoadAll()).State);
    }

    [Fact]
    public async Task An_unfinished_transfer_continues_after_the_app_restarts()
    {
        var journal = new TransferJournal(Path.Combine(_dir, "journal"));
        var job = Job(Source(8 * Chunk));
        var slow = new FlakyFileSystem { DelayPerWrite = TimeSpan.FromMilliseconds(30) };

        await using (var first = new TransferQueue(journal, slow))
        {
            first.Start();
            first.Enqueue(job);
            await WaitUntilAsync(() => first.Jobs.Single().BytesDone >= 3L * Chunk);
        } // the app closes mid-copy

        var stopped = Assert.Single(journal.LoadAll());
        Assert.InRange(stopped.BytesDone, 3L * Chunk, 7L * Chunk);

        await using var second = new TransferQueue(journal);
        second.Start();
        var paused = Assert.Single(second.Jobs);
        Assert.Equal(TransferState.Paused, paused.State);
        Assert.NotNull(paused.Error);

        second.Resume(paused.Id);
        await WaitAsync(second, paused.Id, TransferState.Completed);
        Assert.Equal(File.ReadAllBytes(job.SourcePath), File.ReadAllBytes(job.DestinationPath));
    }

    [Fact]
    public async Task Retrying_too_long_pauses_the_transfer_and_other_errors_fail_it()
    {
        var journal = new TransferJournal(Path.Combine(_dir, "journal"));
        var down = new FlakyFileSystem { FailEveryBytes = 0 };
        await using var queue = new TransferQueue(journal, down, retry: new TransferRetryPolicy(TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(50)));
        queue.Start();

        var unreachable = Job(Source(2 * Chunk), "a.bin");
        queue.Enqueue(unreachable);
        Assert.Contains("Paused after retrying", (await WaitAsync(queue, unreachable.Id, TransferState.Paused)).Error);

        var missing = Job(Path.Combine(_dir, "nope.bin"), "b.bin");
        queue.Enqueue(missing);
        Assert.Contains("no longer exists", (await WaitAsync(queue, missing.Id, TransferState.Failed)).Error);
    }

    [Fact]
    public async Task Cancel_deletes_the_partial_file_and_forgets_the_transfer()
    {
        var journal = new TransferJournal(Path.Combine(_dir, "journal"));
        await using var queue = new TransferQueue(journal, new FlakyFileSystem { DelayPerWrite = TimeSpan.FromMilliseconds(20) });
        queue.Start();
        var job = Job(Source(50 * Chunk));
        queue.Enqueue(job);
        await WaitUntilAsync(() => queue.Jobs.Single().BytesDone > 0);

        queue.Cancel(job.Id);
        await WaitUntilAsync(() => queue.Jobs.Count == 0);
        await WaitUntilAsync(() => !File.Exists(job.PartialPath));
        Assert.Empty(journal.LoadAll());
        Assert.False(File.Exists(job.DestinationPath));
    }

    [Theory]
    [InlineData(@"\\srv01\C$\Users\Public", @"\\srv01\C$")]
    [InlineData("//srv01/share/a/b.txt", @"\\srv01\share")]
    [InlineData(@"\\srv01", null)]
    [InlineData(@"C:\temp", null)]
    [InlineData(@"\\?\C:\temp", null)]
    public void Share_root_of_a_unc_path(string path, string? root) => Assert.Equal(root, UncPath.ShareRoot(path));

    [Fact]
    public void Upload_folder_for_a_host()
    {
        Assert.Equal(@"\\srv01\C$\Users\Public\Downloads", UncPath.ForHost("srv01", @"C$\Users\Public\Downloads\"));
        Assert.Equal(@"\\files\drop", UncPath.ForHost("srv01", @"\\files\drop\"));
        Assert.Equal(@"\\srv01\C$\x\sub\a.txt", UncPath.Combine(@"\\srv01\C$\x\", "sub/a.txt"));
    }

    private static async Task<TransferJob> WaitAsync(TransferQueue queue, Guid id, TransferState state)
    {
        await WaitUntilAsync(() => queue.Jobs.Single(j => j.Id == id).State == state);
        return queue.Jobs.Single(j => j.Id == id);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met in time.");
            await Task.Delay(5);
        }
    }

    /// <summary>A network share that drops: writes fail after a number of bytes, and can be slowed down.</summary>
    private sealed class FlakyFileSystem : ITransferFileSystem
    {
        private long _written;

        /// <summary>Fail once this many bytes have been written in total; <c>null</c> = never.</summary>
        public long? FailAfterBytes { get; set; }

        /// <summary>Fail every time this many bytes were written since the last failure (0 = always fail).</summary>
        public long? FailEveryBytes { get; set; }

        public TimeSpan DelayPerWrite { get; set; }
        public long BytesWrittenAfterRecovery { get; private set; }

        public TransferFileInfo? GetFile(string path) => LocalTransferFileSystem.Instance.GetFile(path);
        public Stream OpenRead(string path) => LocalTransferFileSystem.Instance.OpenRead(path);
        public Stream OpenWrite(string path) => new FlakyStream(this, LocalTransferFileSystem.Instance.OpenWrite(path));
        public void CreateDirectory(string path) => LocalTransferFileSystem.Instance.CreateDirectory(path);
        public void Move(string source, string destination, bool overwrite) => LocalTransferFileSystem.Instance.Move(source, destination, overwrite);
        public void Delete(string path) => LocalTransferFileSystem.Instance.Delete(path);

        private void BeforeWrite(int count)
        {
            if (DelayPerWrite > TimeSpan.Zero) Thread.Sleep(DelayPerWrite);
            if (FailEveryBytes is { } every && _written + count > every)
            {
                _written = 0;
                throw new IOException("The specified network name is no longer available.");
            }

            if (FailAfterBytes is { } limit && _written + count > limit)
            {
                // Like a real drop: part of the buffer may reach the disk before the error.
                throw new IOException("The network path was not found.");
            }

            _written += count;
            if (FailAfterBytes is null) BytesWrittenAfterRecovery += count;
        }

        private sealed class FlakyStream(FlakyFileSystem owner, Stream inner) : Stream
        {
            public override bool CanRead => inner.CanRead;
            public override bool CanSeek => inner.CanSeek;
            public override bool CanWrite => inner.CanWrite;
            public override long Length => inner.Length;
            public override long Position { get => inner.Position; set => inner.Position = value; }
            public override void Flush() => inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
            public override void SetLength(long value) => inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                owner.BeforeWrite(count);
                inner.Write(buffer, offset, count);
            }

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                owner.BeforeWrite(buffer.Length);
                return inner.WriteAsync(buffer, cancellationToken);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
