using System.Reflection;
using System.Windows;
using CPALauncher.Views;
using Xunit;

namespace CPALauncher.Wpf.Tests;

public sealed class WindowPlacementTests
{
    [Theory]
    [InlineData(0, 0, 1920, 1040, 1800, 950)]
    [InlineData(-1920, 0, 1920, 1040, -1900, 950)]
    [InlineData(1920, -900, 1440, 860, 3250, -100)]
    [InlineData(0, 0, 768, 432, 20, 20)]
    [InlineData(0, 40, 1280, 680, -1000, -1000)]
    public void FitAroundCenter_StaysInsideSmallOrOffsetMonitor(double x, double y, double width, double height, double left, double top)
    {
        var workArea = new Rect(x, y, width, height);
        var type = typeof(MainWindow).Assembly.GetType("CPALauncher.Views.WindowPlacement")!;
        var method = type.GetMethod("FitAroundCenter", BindingFlags.Static | BindingFlags.NonPublic)!;
        var fitted = (Rect)method.Invoke(null, [new Rect(left, top, 900, 560), new Size(1100, 720), workArea])!;
        Assert.True(workArea.Contains(fitted));
        Assert.Equal(Math.Min(1100, width), fitted.Width);
        Assert.Equal(Math.Min(720, height), fitted.Height);
    }
}
