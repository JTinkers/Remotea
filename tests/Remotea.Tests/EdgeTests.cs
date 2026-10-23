namespace Remotea.Tests;

public class EdgeTests
{
    private static readonly ScreenRect Screen = new(0, 0, 1920, 1080);

    // Two monitors, the primary on the right, so the virtual screen starts at a negative x.
    private static readonly ScreenRect MultiMonitor = new(-2560, 0, 4480, 1440);

    [Theory]
    [InlineData("Right", 1919, 500, true)]
    [InlineData("Right", 1918, 500, false)]
    [InlineData("Left", 0, 500, true)]
    [InlineData("Left", 1, 500, false)]
    [InlineData("Top", 900, 0, true)]
    [InlineData("Bottom", 900, 1079, true)]
    [InlineData("Bottom", 900, 1078, false)]
    public void DetectsEdge(string edgeName, int x, int y, bool expected)
    {
        Assert.Equal(expected, Edge.IsAt(x, y, Screen, Enum.Parse<ScreenEdge>(edgeName)));
    }

    [Fact]
    public void DetectsEdgeOnNegativeOriginVirtualScreen()
    {
        Assert.True(Edge.IsAt(-2560, 100, MultiMonitor, ScreenEdge.Left));
        Assert.True(Edge.IsAt(1919, 100, MultiMonitor, ScreenEdge.Right));
        Assert.False(Edge.IsAt(0, 100, MultiMonitor, ScreenEdge.Left));
    }

    [Theory]
    [InlineData("Right", 1919, 0, 0f)]
    [InlineData("Right", 1919, 1079, 1f)]
    [InlineData("Bottom", 0, 1079, 0f)]
    [InlineData("Top", 1919, 0, 1f)]
    public void ComputesRatioAlongEdge(string edgeName, int x, int y, float expected)
    {
        Assert.Equal(expected, Edge.RatioAlong(x, y, Screen, Enum.Parse<ScreenEdge>(edgeName)), 3);
    }

    [Fact]
    public void ControllerRightEdgeMapsToReceiverLeftEdgeAtSameHeight()
    {
        var receiver = new ScreenRect(0, 0, 2560, 1440);
        var ratio = Edge.RatioAlong(1919, 540, Screen, ScreenEdge.Right);

        var (x, y) = Edge.PointInside(receiver, Edge.Opposite(ScreenEdge.Right), ratio);

        Assert.Equal(Edge.ArrivalInset, x);
        Assert.Equal(720, y);
        Assert.False(Edge.IsAt(x, y, receiver, ScreenEdge.Left));
    }

    [Fact]
    public void ReturnMapsBackInsideControllerEdge()
    {
        var (x, y) = Edge.PointInside(MultiMonitor, ScreenEdge.Left, 0.5f);

        Assert.Equal(-2560 + Edge.ArrivalInset, x);
        Assert.Equal(720, y);
    }

    [Theory]
    [InlineData("Left", -1, 0, true)]
    [InlineData("Left", 1, 0, false)]
    [InlineData("Bottom", 0, 3, true)]
    [InlineData("Top", 5, 0, false)]
    public void DetectsPushingOut(string edgeName, int dx, int dy, bool expected)
    {
        Assert.Equal(expected, Edge.IsPushingOut(dx, dy, Enum.Parse<ScreenEdge>(edgeName)));
    }

    [Fact]
    public void PointInsideClampsInvalidRatio()
    {
        Assert.Equal((1919 - Edge.ArrivalInset, 540), Edge.PointInside(Screen, ScreenEdge.Right, float.NaN));
        Assert.Equal((1919 - Edge.ArrivalInset, 1079), Edge.PointInside(Screen, ScreenEdge.Right, 7f));
    }
}
