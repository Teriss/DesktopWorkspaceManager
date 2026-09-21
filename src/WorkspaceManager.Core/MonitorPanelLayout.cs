namespace WorkspaceManager.Core;

public static class MonitorPanelLayout
{
    public const double CardGap = 12;
    public const double PanelInsets = 38;
    public const double MinimumCardWidth = 250;

    public static int ColumnCount(double panelWidth)
    {
        if (!double.IsFinite(panelWidth) || panelWidth <= 0) return 1;
        return Math.Max(1, (int)Math.Floor((panelWidth - PanelInsets + CardGap) / (MinimumCardWidth + CardGap)));
    }

    // Drop zones cover the whole visible monitor column, including padding, gaps
    // and the empty area below the final card. Content height is intentionally irrelevant.
    public static int HitTest(double x, double y, double viewportWidth, double viewportHeight, int monitorCount)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) ||
            monitorCount <= 0 || viewportWidth <= 0 || viewportHeight <= 0 || x < 0 || y < 0 || x >= viewportWidth || y >= viewportHeight) return -1;
        return Math.Min(monitorCount - 1, (int)(x / viewportWidth * monitorCount));
    }
}
