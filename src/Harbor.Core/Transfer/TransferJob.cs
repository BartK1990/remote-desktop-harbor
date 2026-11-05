using System.Text.Json.Serialization;

namespace Harbor.Core.Transfer;

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
/// up from the entry for every attempt. SPEC FR-60, FR-61, ADR-0038
/// </summary>
public sealed class TransferJob
{
    /// <summary>Default chunk: bytes between two checkpoints in the journal.</summary>
    public const int DefaultChunkSize = 4 * 1024 * 1024;

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

    public int ChunkSize { get; set; } = DefaultChunkSize;

    /// <summary>Source length and last write time when copying started; a change restarts the copy from 0.</summary>
    public long Length { get; set; } = -1;
    public long SourceLastWriteUtcTicks { get; set; }

    /// <summary>Bytes written to the partial file and flushed: always a whole number of chunks, or <see cref="Length"/>.</summary>
    public long BytesDone { get; set; }

    /// <summary>SHA-256 (hex) of each copied chunk, to check the partial file before resuming.</summary>
    public List<string> ChunkHashes { get; set; } = [];

    public TransferState State { get; set; } = TransferState.Queued;
    public string? Error { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string PartialPath => DestinationPath + PartialSuffix;
    [JsonIgnore]
    public string FileName => Path.GetFileName(SourcePath);
    [JsonIgnore]
    public bool IsFinished => State is TransferState.Completed or TransferState.Failed;

    [JsonIgnore]
    /// <summary>0..1; 0 while the length is not known yet.</summary>
    public double Progress => Length > 0 ? (double)BytesDone / Length : State == TransferState.Completed ? 1 : 0;

    public TransferJob Clone()
    {
        var copy = (TransferJob)MemberwiseClone();
        copy.ChunkHashes = [.. ChunkHashes];
        return copy;
    }
}
