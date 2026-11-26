using Harbor.Core.Rdp;

namespace Harbor.App.Infrastructure;

/// <summary>This PC's monitors for the monitor picker and for connecting (SPEC FR-75); set by the app at start.</summary>
public static class Monitors
{
    /// <summary>This PC's monitors now, with Remote Desktop's numbers. Empty when they cannot be read (and in tests).</summary>
    public static Func<IReadOnlyList<LocalMonitor>> Current { get; set; } = () => [];
}
