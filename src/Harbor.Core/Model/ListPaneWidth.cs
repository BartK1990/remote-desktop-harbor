namespace Harbor.Core.Model;

/// <summary>
/// Width of the main window's entry list pane, as a share of the list and details panes together (SPEC FR-56).
/// A share, not pixels, so the split still fits after the window is resized or opened on another screen.
/// The default 0.4 is the original 2* : 3* split; a stored value outside 0.1 to 0.9, or not a number, is read as
/// the nearest valid share (or the default). The panes' minimum widths still apply on top of this.
/// </summary>
public static class ListPaneWidth
{
    public const double Default = 0.4;
    public const double Min = 0.1;
    public const double Max = 0.9;

    public static double Normalize(double share) =>
        double.IsFinite(share) ? Math.Round(Math.Clamp(share, Min, Max), 3, MidpointRounding.AwayFromZero) : Default;

    /// <summary>The share of the list pane, from the widths the two panes have on screen.</summary>
    public static double FromWidths(double listWidth, double detailsWidth) =>
        listWidth > 0 && detailsWidth > 0 ? Normalize(listWidth / (listWidth + detailsWidth)) : Default;
}
