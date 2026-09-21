using WorkspaceManager.Core;
using Xunit;

namespace WorkspaceManager.Tests;

public class PreviewGeometryTests
{
    [Fact]
    public void FitPreservesAspectAndCentersImage()
    {
        var result = PreviewGeometry.Calculate(new(new(100, 200, 400, 300), new(0, 0, 1000, 1000)), 1600, 900)!;
        Assert.Equal(new PixelRect(100, 237, 400, 225), result.HostBounds);
        Assert.Equal(new PixelRect(0, 0, 1600, 900), result.SourceBounds);
    }
    [Fact]
    public void ScrollPastTopCropsSourceInsteadOfSquashingOrPaintingOverHeader()
    {
        var result = PreviewGeometry.Calculate(new(new(10, 50, 400, 200), new(0, 100, 800, 600)), 1600, 800)!;
        Assert.Equal(new PixelRect(10, 100, 400, 150), result.HostBounds);
        Assert.Equal(new PixelRect(0, 200, 1600, 600), result.SourceBounds);
    }
    [Fact]
    public void BottomAndSideCroppingHandlesNegativeScreenCoordinates()
    {
        var result = PreviewGeometry.Calculate(new(new(-400, 100, 400, 200), new(-300, 50, 200, 200)), 800, 400)!;
        Assert.Equal(new PixelRect(-300, 100, 200, 150), result.HostBounds);
        Assert.Equal(new PixelRect(200, 0, 400, 300), result.SourceBounds);
    }
    [Fact]
    public void LetterboxOnlyIntersectionDoesNotShowHost()
    {
        Assert.Null(PreviewGeometry.Calculate(new(new(0, 0, 400, 400), new(0, 0, 400, 90)), 1600, 800));
    }
    [Fact]
    public void EveryScrollStepKeepsHostInsideViewportAndSourceInsideWindow()
    {
        var clip = new PixelRect(-1700, 140, 630, 580);
        for (int y = -900; y < 1200; y++)
        {
            var result = PreviewGeometry.Calculate(new(new(-1687, y, 603, 255), clip), 1919, 1079);
            if (result is null) continue;
            Assert.InRange(result.HostBounds.X, clip.X, clip.Right - 1);
            Assert.InRange(result.HostBounds.Y, clip.Y, clip.Bottom - 1);
            Assert.True(result.HostBounds.Right <= clip.Right && result.HostBounds.Bottom <= clip.Bottom);
            Assert.True(result.SourceBounds.IsValid && result.SourceBounds.X >= 0 && result.SourceBounds.Y >= 0);
            Assert.True(result.SourceBounds.Right <= 1919 && result.SourceBounds.Bottom <= 1079);
        }
    }
    [Fact]
    public void InvalidOrCompletelyOffscreenPreviewHasNoHost()
    {
        Assert.Null(PreviewGeometry.Calculate(new(new(0, 0, 0, 200), new(0, 0, 500, 500)), 800, 600));
        Assert.Null(PreviewGeometry.Calculate(new(new(0, -300, 400, 200), new(0, 0, 500, 500)), 800, 600));
        Assert.Null(PreviewGeometry.Calculate(new(new(0, 0, 400, 200), new(0, 0, 500, 500)), 0, 0));
    }
}
