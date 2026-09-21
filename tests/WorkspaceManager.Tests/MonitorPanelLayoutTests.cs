using WorkspaceManager.Core;
using Xunit;

namespace WorkspaceManager.Tests;

public class MonitorPanelLayoutTests
{
    [Theory]
    [InlineData(220, 1)]
    [InlineData(550, 2)]
    [InlineData(850, 3)]
    [InlineData(1226, 4)]
    [InlineData(2452, 9)]
    public void DensityRespondsToAvailableMonitorWidth(double width, int expected)
        => Assert.Equal(expected, MonitorPanelLayout.ColumnCount(width));

    [Fact]
    public void ColumnsRespectCardMinimumAndGapBudget()
    {
        for (int width = 550; width < 4000; width++)
        {
            int columns = MonitorPanelLayout.ColumnCount(width);
            double cardWidth = (width - MonitorPanelLayout.PanelInsets - (columns - 1) * MonitorPanelLayout.CardGap) / columns;
            Assert.True(cardWidth >= MonitorPanelLayout.MinimumCardWidth);
        }
    }
    [Fact]
    public void BlankSpaceAtBottomBelongsToItsMonitor()
    {
        Assert.Equal(0, MonitorPanelLayout.HitTest(100, 899, 2400, 900, 2));
        Assert.Equal(1, MonitorPanelLayout.HitTest(2300, 899, 2400, 900, 2));
        Assert.Equal(1, MonitorPanelLayout.HitTest(1300, 800, 3000, 1000, 3));
    }
    [Fact]
    public void MonitorSeamHasOneUnambiguousTarget()
    {
        Assert.Equal(0, MonitorPanelLayout.HitTest(1199.9, 500, 2400, 900, 2));
        Assert.Equal(1, MonitorPanelLayout.HitTest(1200, 500, 2400, 900, 2));
    }
    [Fact]
    public void HeaderFooterAndOutsideEdgesAreNotMonitorTargets()
    {
        foreach (var (x, y) in new[] { (-1d, 100d), (100d, -1d), (2400d, 100d), (100d, 900d), (double.NaN, 100d) })
            Assert.Equal(-1, MonitorPanelLayout.HitTest(x, y, 2400, 900, 2));
        Assert.Equal(-1, MonitorPanelLayout.HitTest(1, 1, 2400, 900, 0));
    }
}
