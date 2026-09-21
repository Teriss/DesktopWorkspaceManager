namespace WorkspaceManager.Core;

public static class PlacementCalculator
{
    // Physical coordinates at the boundary; logical size inside the calculation.
    // Relative position is measured along the available travel, so aligned edges stay aligned.
    public static PixelRect Move(PixelRect original, MonitorInfo source, MonitorInfo target)
    {
        if (!original.IsValid || !source.WorkArea.IsValid || !target.WorkArea.IsValid)
            throw new ArgumentException("窗口或显示器尺寸无效。");
        double ratio = (double)Math.Max(96, target.Dpi) / Math.Max(96, source.Dpi);
        int width = Math.Clamp((int)Math.Round(original.Width * ratio), 1, target.WorkArea.Width);
        int height = Math.Clamp((int)Math.Round(original.Height * ratio), 1, target.WorkArea.Height);
        double x = Fraction(original.X - source.WorkArea.X, source.WorkArea.Width - original.Width);
        double y = Fraction(original.Y - source.WorkArea.Y, source.WorkArea.Height - original.Height);
        return new(target.WorkArea.X + (int)Math.Round(x * (target.WorkArea.Width - width)),
            target.WorkArea.Y + (int)Math.Round(y * (target.WorkArea.Height - height)), width, height);
    }
    private static double Fraction(int offset, int travel) => travel <= 0 ? 0 : Math.Clamp((double)offset / travel, 0, 1);
}
