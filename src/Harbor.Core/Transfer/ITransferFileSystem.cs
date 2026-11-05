namespace Harbor.Core.Transfer;

/// <summary>Length and last write time of a file, or <c>null</c> from <see cref="ITransferFileSystem.GetFile"/> when missing.</summary>
public sealed record TransferFileInfo(long Length, DateTime LastWriteUtc);

/// <summary>
/// The file operations a transfer needs, so the copy logic can be tested with dropped connections. The default
/// <see cref="LocalTransferFileSystem"/> uses System.IO, which on Windows also reaches UNC paths (<c>\\host\share</c>).
/// </summary>
public interface ITransferFileSystem
{
    TransferFileInfo? GetFile(string path);
    Stream OpenRead(string path);

    /// <summary>Opens or creates <paramref name="path"/> for writing without truncating it.</summary>
    Stream OpenWrite(string path);

    void CreateDirectory(string path);
    void Move(string source, string destination, bool overwrite);
    void Delete(string path);
}

public sealed class LocalTransferFileSystem : ITransferFileSystem
{
    public static LocalTransferFileSystem Instance { get; } = new();

    public TransferFileInfo? GetFile(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new TransferFileInfo(info.Length, info.LastWriteTimeUtc) : null;
    }

    public Stream OpenRead(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan);

    public Stream OpenWrite(string path) =>
        new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void Move(string source, string destination, bool overwrite) => File.Move(source, destination, overwrite);

    public void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
