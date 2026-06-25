namespace utc_clock.Services;

internal readonly record struct ScreenBounds(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

internal static class PositionMath
{
    public const int WidgetWidth = 194;
    public const int WidgetHeight = 68;
    public const int DefaultMargin = 24;

    public static (int X, int Y) DefaultPosition(ScreenBounds workArea)
    {
        return (
            workArea.Right - WidgetWidth - DefaultMargin,
            workArea.Y + DefaultMargin);
    }

    public static (int X, int Y) ClampToBounds(int x, int y, ScreenBounds bounds)
    {
        int maxX = Math.Max(bounds.X, bounds.Right - WidgetWidth);
        int maxY = Math.Max(bounds.Y, bounds.Bottom - WidgetHeight);

        return (
            Math.Clamp(x, bounds.X, maxX),
            Math.Clamp(y, bounds.Y, maxY));
    }

    public static ScreenBounds Union(IEnumerable<ScreenBounds> bounds)
    {
        using var enumerator = bounds.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            throw new ArgumentException("At least one bounds value is required.", nameof(bounds));
        }

        int left = enumerator.Current.X;
        int top = enumerator.Current.Y;
        int right = enumerator.Current.Right;
        int bottom = enumerator.Current.Bottom;

        while (enumerator.MoveNext())
        {
            ScreenBounds current = enumerator.Current;
            left = Math.Min(left, current.X);
            top = Math.Min(top, current.Y);
            right = Math.Max(right, current.Right);
            bottom = Math.Max(bottom, current.Bottom);
        }

        return new ScreenBounds(left, top, right - left, bottom - top);
    }
}
