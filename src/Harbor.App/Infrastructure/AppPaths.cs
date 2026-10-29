using System.IO;

namespace Harbor.App.Infrastructure;

/// <summary>
/// Where the app keeps its data. Default: %LOCALAPPDATA%\RemoteDesktopHarbor (per user, no admin, also
/// virtualised correctly under MSIX). Portable mode: a <c>portable.txt</c> file next to the exe puts data in
/// <c>.\data</c>. Data stays bound to the Windows user and machine by encryption either way. See ADR-0003.
/// </summary>
public static class AppPaths
{
    public static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));

    public static string DataDirectory { get; } = IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteDesktopHarbor");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string LocalDatabase => Path.Combine(DataDirectory, "harbor.db");
    public static string TempDirectory => Path.Combine(DataDirectory, "tmp");
    public static string LogFile => Path.Combine(DataDirectory, "logs", "harbor.log");

    /// <summary>Offline cache for a remote database; one file per remote so switching databases never mixes data.</summary>
    public static string CacheDatabase(string provider, string connectionString)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{provider}|{connectionString}"));
        return Path.Combine(DataDirectory, $"cache-{Convert.ToHexStringLower(hash.AsSpan(0, 8))}.db");
    }
}
