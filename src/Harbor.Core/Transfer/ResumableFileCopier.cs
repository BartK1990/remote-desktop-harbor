using System.Security.Cryptography;

namespace Harbor.Core.Transfer;

/// <summary>
/// Copies one file in chunks into <c>&lt;destination&gt;.harbor-part</c>, flushing and recording every chunk in the job (and,
/// through <c>checkpoint</c>, in the journal) before the next one, so an attempt cut off by a dropped connection, a closed
/// app or a crash continues from the last recorded chunk. Before resuming, the partial file's length and its last chunk
/// are checked against the journal; a changed source starts over. When all bytes are in, the partial file gets its final
/// name. SPEC FR-60, FR-61, ADR-0038
/// </summary>
public sealed class ResumableFileCopier(ITransferFileSystem? fileSystem = null)
{
    private readonly ITransferFileSystem _fs = fileSystem ?? LocalTransferFileSystem.Instance;

    /// <summary>
    /// One attempt: copies from <see cref="TransferJob.BytesDone"/> to the end, calling <paramref name="checkpoint"/> after
    /// each chunk. Connection problems surface as <see cref="IOException"/>; the job then still describes the bytes safely
    /// written, so the next attempt resumes there.
    /// </summary>
    /// <exception cref="TransferFailedException">A problem retrying cannot fix (missing source, destination exists).</exception>
    public async Task CopyAsync(TransferJob job, Action<TransferJob> checkpoint, CancellationToken cancellationToken = default)
    {
        var source = _fs.GetFile(job.SourcePath) ?? throw new TransferFailedException($"{job.SourcePath} no longer exists.");
        if (job.Length != source.Length || job.SourceLastWriteUtcTicks != source.LastWriteUtc.Ticks)
        {
            // New job, or the source changed since the last attempt: the copied bytes may be stale, so start over.
            job.Length = source.Length;
            job.SourceLastWriteUtcTicks = source.LastWriteUtc.Ticks;
            job.BytesDone = 0;
            job.ChunkHashes.Clear();
        }

        if (!job.Overwrite && _fs.GetFile(job.DestinationPath) is not null)
        {
            throw new TransferFailedException($"{job.DestinationPath} already exists.");
        }

        if (Path.GetDirectoryName(job.DestinationPath) is { Length: > 0 } folder)
        {
            _fs.CreateDirectory(folder);
        }

        var buffer = new byte[job.ChunkSize];
        await using (var output = _fs.OpenWrite(job.PartialPath))
        {
            await RewindToVerifiedAsync(job, output, buffer, cancellationToken).ConfigureAwait(false);
            output.SetLength(job.BytesDone);
            checkpoint(job);

            await using var input = _fs.OpenRead(job.SourcePath);
            input.Seek(job.BytesDone, SeekOrigin.Begin);
            output.Seek(job.BytesDone, SeekOrigin.Begin);
            while (job.BytesDone < job.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var wanted = (int)Math.Min(job.ChunkSize, job.Length - job.BytesDone);
                await input.ReadExactlyAsync(buffer.AsMemory(0, wanted), cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(buffer.AsMemory(0, wanted), cancellationToken).ConfigureAwait(false);
                await FlushAsync(output, cancellationToken).ConfigureAwait(false);

                // Recorded only once the chunk is flushed: the journal never claims bytes the destination may not have.
                job.ChunkHashes.Add(Hash(buffer.AsSpan(0, wanted)));
                job.BytesDone += wanted;
                job.UpdatedUtc = DateTimeOffset.UtcNow;
                checkpoint(job);
            }
        }

        if (_fs.GetFile(job.SourcePath) is not { } after || after.Length != job.Length || after.LastWriteUtc.Ticks != job.SourceLastWriteUtcTicks)
        {
            throw new IOException($"{job.SourcePath} changed while it was copied; it is copied again.");
        }

        _fs.Move(job.PartialPath, job.DestinationPath, job.Overwrite);
        job.State = TransferState.Completed;
        job.Error = null;
        job.UpdatedUtc = DateTimeOffset.UtcNow;
        checkpoint(job);
    }

    /// <summary>
    /// Steps <see cref="TransferJob.BytesDone"/> back to what the partial file really holds: no further than its length,
    /// and past any last chunk whose bytes do not match the recorded hash (e.g. a write lost when the connection dropped).
    /// </summary>
    private static async Task RewindToVerifiedAsync(TransferJob job, Stream partial, byte[] buffer, CancellationToken ct)
    {
        var length = partial.Length;
        while (job.ChunkHashes.Count > 0)
        {
            var last = job.ChunkHashes.Count - 1;
            var start = (long)last * job.ChunkSize;
            var size = (int)(job.BytesDone - start);
            if (size <= 0 || size > job.ChunkSize)
            {
                break; // inconsistent journal: start over below
            }

            if (job.BytesDone <= length)
            {
                partial.Seek(start, SeekOrigin.Begin);
                await partial.ReadExactlyAsync(buffer.AsMemory(0, size), ct).ConfigureAwait(false);
                if (Hash(buffer.AsSpan(0, size)) == job.ChunkHashes[last])
                {
                    return;
                }
            }

            job.ChunkHashes.RemoveAt(last);
            job.BytesDone = start;
        }

        job.ChunkHashes.Clear();
        job.BytesDone = 0;
    }

    private static Task FlushAsync(Stream output, CancellationToken ct)
    {
        if (output is FileStream file)
        {
            file.Flush(flushToDisk: true);
            return Task.CompletedTask;
        }

        return output.FlushAsync(ct);
    }

    private static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));
}

/// <summary>A transfer problem that retrying does not fix.</summary>
public class TransferFailedException(string message, Exception? inner = null) : Exception(message, inner);
