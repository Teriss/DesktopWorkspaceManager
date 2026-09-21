using WorkspaceManager.Core;
using Xunit;

namespace WorkspaceManager.Tests;

public class PlacementTests
{
    private static MonitorInfo M(string id, PixelRect area, uint dpi = 96) => new(id, id, area, area, dpi, false);
    [Fact] public void MixedDpiPreservesLogicalSizeAndRelativeCenter()
    {
        var from = M("a", new(0, 0, 1920, 1040)); var to = M("b", new(-2560, 0, 2560, 1400), 144);
        Assert.Equal(new PixelRect(-1880, 250, 1200, 900), PlacementCalculator.Move(new(560, 220, 800, 600), from, to));
    }
    [Fact] public void OversizeWindowClampsToPortraitWorkArea()
    {
        var result = PlacementCalculator.Move(new(300, 100, 1800, 1200), M("a", new(0, 0, 2560, 1440)), M("b", new(2560, -800, 1080, 1880)));
        Assert.Equal(1080, result.Width); Assert.Equal(2560, result.X); Assert.InRange(result.Y, -800, -120);
    }
    [Fact] public void BottomRightAlignmentSurvivesTaskbarAndNegativeOrigin()
    {
        var result = PlacementCalculator.Move(new(1000, 520, 920, 520), M("a", new(0, 0, 1920, 1040)), M("b", new(-1200, -900, 1200, 860)));
        Assert.Equal(new PixelRect(-920, -560, 920, 520), result);
    }
    [Fact] public void OffscreenWindowIsRecovered()
    {
        var result = PlacementCalculator.Move(new(-3000, -1000, 500, 400), M("a", new(0, 0, 1920, 1080)), M("b", new(1920, 0, 1920, 1080)));
        Assert.Equal(new PixelRect(1920, 0, 500, 400), result);
    }
    [Fact] public void InvalidGeometryIsRejected() => Assert.Throws<ArgumentException>(() => PlacementCalculator.Move(new(0, 0, 0, 10), M("a", new(0, 0, 100, 100)), M("b", new(0, 0, 100, 100))));
}
