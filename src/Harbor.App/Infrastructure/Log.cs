using System.IO;

namespace Harbor.App.Infrastructure;

/// <summary>Minimal append-only file log. Never logs secrets.</summary>
public static class Log
{
    private static readonly Lock Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.LogFile)!);
                var info = new FileInfo(AppPaths.LogFile);
                if (info.Exists && info.Length > 2 * 1024 * 1024)
                {
                    File.Move(AppPaths.LogFile, AppPaths.LogFile + ".1", overwrite: true);
                }

                File.AppendAllText(AppPaths.LogFile, $"{DateTimeOffset.Now:O} {level} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
        }
    }
}
