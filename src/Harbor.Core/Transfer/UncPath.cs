namespace Harbor.Core.Transfer;

/// <summary>Helpers for <c>\\server\share\path</c> names used by file transfers. SPEC FR-60</summary>
public static class UncPath
{
    /// <summary>True for <c>\\server\share…</c> (also with forward slashes).</summary>
    public static bool IsUnc(string? path) =>
        path is { Length: > 2 } && IsSeparator(path[0]) && IsSeparator(path[1]) && !IsSeparator(path[2]) && path[2] != '?' && path[2] != '.';

    /// <summary><c>\\server\share</c> of a UNC path, the part a network connection is made to; <c>null</c> for other paths.</summary>
    public static string? ShareRoot(string? path)
    {
        if (!IsUnc(path)) return null;
        var parts = path![2..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? null : $@"\\{parts[0]}\{parts[1]}";
    }

    /// <summary>
    /// The default folder on <paramref name="host"/> for uploads: <paramref name="folder"/> when it is already a UNC path,
    /// otherwise <c>\\host\folder</c> (e.g. <c>C$\Users\Public\Downloads</c>).
    /// </summary>
    public static string ForHost(string host, string folder)
    {
        if (IsUnc(folder)) return folder.TrimEnd('\\', '/');
        return $@"\\{host.Trim()}\{folder.Trim().TrimStart('\\', '/').TrimEnd('\\', '/')}";
    }

    /// <summary>
    /// Joins <paramref name="folder"/> and <paramref name="relative"/> with backslashes (UNC style), whatever the OS, for
    /// destinations on a share.
    /// </summary>
    public static string Combine(string folder, string relative) =>
        folder.TrimEnd('\\', '/') + "\\" + relative.Replace('/', '\\').TrimStart('\\');

    private static bool IsSeparator(char c) => c is '\\' or '/';
}
