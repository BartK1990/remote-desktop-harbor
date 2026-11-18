using System.Text.Json.Serialization;

namespace Harbor.Core.Transfer;

/// <summary>What a transfer does with a file that already exists at the destination. SPEC FR-64</summary>
public enum ExistingFiles
{
    /// <summary>Fail that file; the others are copied.</summary>
    Fail = 0,

    /// <summary>Replace it.</summary>
    Replace = 1,

    /// <summary>Leave it as it is and copy only the files that are not there yet (same name and extension).</summary>
    Skip = 2,
}

/// <summary>Where a transfer stands. SPEC FR-60</summary>
public enum TransferState
{
    /// <summary>Waiting for its turn in the queue.</summary>
    Queued = 0,

    /// <summary>Copying now.</summary>
    Running = 1,

    /// <summary>The connection dropped; retrying automatically from where it stopped.</summary>
    Retrying = 2,

    /// <summary>Stopped by the user, by closing the app or after retrying too long; resumes from where it stopped.</summary>
    Paused = 3,

    /// <summary>All bytes copied and verified; the file has its final name.</summary>
    Completed = 4,

    /// <summary>Stopped by an error retrying cannot fix (e.g. access denied, the file already exists).</summary>
    Failed = 5,
}

/// <summary>
/// One file copied by <see cref="ResumableFileCopier"/>, as kept in the transfer journal after every chunk so a copy can
/// continue where it stopped after a dropped connection, a closed app or a crash. Holds no secret: credentials are looked
/// up from the entry for every attempt. SPEC FR-60, FR-61, ADR-0039
/// </summary>
public sealed class TransferJob
{
    /// <summary>
    /// Default chunk: the unit that is read, hashed and recorded in the journal. 4 MiB (ADR-0051): with progress saved on
    /// a timer, bigger chunks mean fewer round trips and less work per byte. A journal keeps the chunk size it was started
    /// with.
    /// </summary>
    public const int DefaultChunkSize = DefaultChunkSizeMiB * 1024 * 1024;

    /// <summary><see cref="DefaultChunkSize"/> in MiB, as the File transfer window offers it.</summary>
    public const int DefaultChunkSizeMiB = 4;

    /// <summary>Chunk sizes the File transfer window offers, in MiB (SPEC FR-61).</summary>
    public static IReadOnlyList<int> ChunkSizesMiB { get; } = [1, 2, 4, 8, 16];

    /// <summary>The offered chunk size closest to <paramref name="mib"/> (a hand-edited setting may hold anything).</summary>
    public static int NormalizeChunkSizeMiB(int mib) => ChunkSizesMiB.MinBy(s => Math.Abs(s - mib));

    /// <summary>Suffix of the destination file while it is incomplete; renamed to the final name at the end.</summary>
    public const string PartialSuffix = ".harbor-part";

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Entry whose host and account the transfer uses (credentials for the share).</summary>
    public Guid? ConnectionId { get; set; }

    /// <summary>Additional account of the entry (SPEC FR-09); <c>null</c> for the default account.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Shown in the transfer list, e.g. the entry name.</summary>
    public string Label { get; set; } = "";

    public bool IsUpload { get; set; } = true;
    public string SourcePath { get; set; } = "";
    public string DestinationPath { get; set; } = "";

    /// <summary>Replace an existing destination file at the end instead of failing.</summary>
    public bool Overwrite { get; set; }

    /// <summary>Leave an existing destination file as it is and finish without copying. SPEC FR-64</summary>
    public bool SkipExisting { get; set; }

    /// <summary>Finished without copying because the destination already existed (<see cref="SkipExisting"/>).</summary>
    public bool Skipped { get; set; }

    public int ChunkSize { get; set; } = DefaultChunkSize;

    /// <summary>
    /// The transfer this file is part of when several files were picked or a folder was uploaded: the list shows them as
    /// one row with the progress of the whole (SPEC FR-70); <c>null</c> for a file on its own.
    /// </summary>
    public Guid? BatchId { get; set; }

    /// <summary>What the batch row is called, e.g. the uploaded folder's name or "12 files".</summary>
    public string? BatchName { get; set; }

    /// <summary>The folder the batch copies into; files are shown relative to it.</summary>
    public string? BatchDestination { get; set; }

    /// <summary>
    /// Size of the source when it was queued, or -1: counts in the batch total before the copy has read the file's real
    /// <see cref="Length"/>, so the bar is a share of the whole from the start (SPEC FR-70).
    /// </summary>
    public long SizeHint { get; set; } = -1;

    /// <summary>Source length and last write time when copying started; a change restarts the copy from 0.</summary>
    public long Length { get; set; } = -1;
    public long SourceLastWriteUtcTicks { get; set; }

    /// <summary>Bytes written to the partial file and flushed: always a whole number of chunks, or <see cref="Length"/>.</summary>
    public long BytesDone { get; set; }

    /// <summary>SHA-256 (hex) of each copied chunk, to check the partial file before resuming.</summary>
    public List<string> ChunkHashes { get; set; } = [];

    /// <summary>When copying this file first started; <c>null</c> until then. SPEC FR-70</summary>
    public DateTimeOffset? StartedUtc { get; set; }

    /// <summary>When it was done (copied or skipped); <c>null</c> until then. SPEC FR-70</summary>
    public DateTimeOffset? CompletedUtc { get; set; }

    public TransferState State { get; set; } = TransferState.Queued;
    public string? Error { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string PartialPath => DestinationPath + PartialSuffix;
    [JsonIgnore]
    public string FileName => Path.GetFileName(SourcePath);
    /// <summary>Bytes this file counts for in a total: its length once known, else <see cref="SizeHint"/>, else 0.</summary>
    [JsonIgnore]
    public long ExpectedLength => Length >= 0 ? Length : Math.Max(SizeHint, 0);

    [JsonIgnore]
    public bool IsFinished => State is TransferState.Completed or TransferState.Failed;

    [JsonIgnore]
    /// <summary>0..1; 0 while the length is not known yet.</summary>
    public double Progress => Skipped ? 1 : Length > 0 ? (double)BytesDone / Length : State == TransferState.Completed ? 1 : 0;

    public TransferJob Clone()
    {
        var copy = (TransferJob)MemberwiseClone();
        copy.ChunkHashes = [.. ChunkHashes];
        return copy;
    }
}
