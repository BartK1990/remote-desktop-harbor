namespace Harbor.Core.Model;

/// <summary>
/// Zoom level of the entry tree rows (SPEC FR-55, ADR-0034): 80 % to 160 % in 10 % steps, 100 % by default.
/// A stored value outside the range, or not a number, is read as the nearest valid level (or the default).
/// </summary>
public static class TreeZoom
{
    public const double Default = 1.0;
    public const double Min = 0.8;
    public const double Max = 1.6;
    public const double Step = 0.1;

    public static double Normalize(double zoom) =>
        double.IsFinite(zoom) ? Math.Round(Math.Clamp(zoom, Min, Max), 1, MidpointRounding.AwayFromZero) : Default;

    public static double In(double zoom) => Normalize(Normalize(zoom) + Step);

    public static double Out(double zoom) => Normalize(Normalize(zoom) - Step);

    public static bool IsDefault(double zoom) => Normalize(zoom) == Default;

    /// <summary>The level as a percentage, e.g. <c>120%</c>.</summary>
    public static string Label(double zoom) => $"{(int)Math.Round(Normalize(zoom) * 100)}%";
}
