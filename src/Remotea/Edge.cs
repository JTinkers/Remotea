namespace Remotea;

internal enum ScreenEdge : byte
{
    Left = 0,
    Right = 1,
    Top = 2,
    Bottom = 3,
}

/// <summary>Screen rectangle in pixels; <see cref="Right"/> and <see cref="Bottom"/> are exclusive.</summary>
internal readonly record struct ScreenRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public (int X, int Y) Center => (Left + Width / 2, Top + Height / 2);

    public (int X, int Y) Clamp(int x, int y) => (Math.Clamp(x, Left, Right - 1), Math.Clamp(y, Top, Bottom - 1));
}

internal static class Edge
{
    /// <summary>Pixels the cursor is placed inside an edge when it arrives, so it doesn't immediately re-trigger.</summary>
    public const int ArrivalInset = 2;

    public static ScreenEdge Opposite(ScreenEdge edge) => edge switch
    {
        ScreenEdge.Left => ScreenEdge.Right,
        ScreenEdge.Right => ScreenEdge.Left,
        ScreenEdge.Top => ScreenEdge.Bottom,
        _ => ScreenEdge.Top,
    };

    public static bool IsAt(int x, int y, ScreenRect screen, ScreenEdge edge) => edge switch
    {
        ScreenEdge.Left => x <= screen.Left,
        ScreenEdge.Right => x >= screen.Right - 1,
        ScreenEdge.Top => y <= screen.Top,
        _ => y >= screen.Bottom - 1,
    };

    /// <summary>True when a movement of (dx, dy) points out of the screen through <paramref name="edge"/>.</summary>
    public static bool IsPushingOut(int dx, int dy, ScreenEdge edge) => edge switch
    {
        ScreenEdge.Left => dx < 0,
        ScreenEdge.Right => dx > 0,
        ScreenEdge.Top => dy < 0,
        _ => dy > 0,
    };

    /// <summary>Position along the edge as 0..1 (top→bottom for vertical edges, left→right for horizontal ones).</summary>
    public static float RatioAlong(int x, int y, ScreenRect screen, ScreenEdge edge)
    {
        var vertical = edge is ScreenEdge.Left or ScreenEdge.Right;
        var offset = vertical ? y - screen.Top : x - screen.Left;
        var span = (vertical ? screen.Height : screen.Width) - 1;
        return span <= 0 ? 0f : Math.Clamp(offset / (float)span, 0f, 1f);
    }

    /// <summary>Point just inside <paramref name="edge"/> at <paramref name="ratio"/> along it.</summary>
    public static (int X, int Y) PointInside(ScreenRect screen, ScreenEdge edge, float ratio, int inset = ArrivalInset)
    {
        ratio = Math.Clamp(float.IsFinite(ratio) ? ratio : 0.5f, 0f, 1f);
        var alongX = screen.Left + (int)MathF.Round(ratio * (screen.Width - 1));
        var alongY = screen.Top + (int)MathF.Round(ratio * (screen.Height - 1));
        var (x, y) = edge switch
        {
            ScreenEdge.Left => (screen.Left + inset, alongY),
            ScreenEdge.Right => (screen.Right - 1 - inset, alongY),
            ScreenEdge.Top => (alongX, screen.Top + inset),
            _ => (alongX, screen.Bottom - 1 - inset),
        };
        return screen.Clamp(x, y);
    }
}
