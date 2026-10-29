using System.Reflection;

namespace Harbor.App.Infrastructure;

/// <summary>Release version of the running app, set by the build (<c>-p:Version</c>). See SPEC FR-07.</summary>
public static class AppInfo
{
    public const string ProductName = "Remote Desktop Harbor";

    private static readonly string Informational =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>Release version without build metadata, e.g. <c>0.1.0</c>.</summary>
    public static string Version { get; } = Informational.Split('+')[0];

    /// <summary>Short commit hash the build was made from, if the SDK recorded it.</summary>
    public static string? Commit { get; } =
        Informational.Split('+') is [_, var sha, ..] && sha.Length > 0 ? sha[..Math.Min(7, sha.Length)] : null;

    public static string Title => $"{ProductName} {Version}";

    public static string Description => Commit is null ? $"Version {Version}" : $"Version {Version} (build {Commit})";
}
