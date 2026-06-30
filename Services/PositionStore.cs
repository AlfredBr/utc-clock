using System.Text.Json;
using System.Text.Json.Serialization;
using utc_clock.Native;

namespace utc_clock.Services;

internal static class PositionStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UtcClockWidget",
        "position.json");

    public static (int X, int Y) DefaultPosition()
    {
        if (!NativeMethods.SystemParametersInfo(
            NativeMethods.SPI_GETWORKAREA,
            0,
            out NativeMethods.RECT workArea,
            0))
        {
            return PositionMath.DefaultPosition(VirtualScreenBounds());
        }

        return PositionMath.DefaultPosition(new ScreenBounds(
            workArea.Left,
            workArea.Top,
            workArea.Right - workArea.Left,
            workArea.Bottom - workArea.Top));
    }

    public static (int X, int Y)? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            string json = File.ReadAllText(FilePath);
            PositionDto? position = JsonSerializer.Deserialize(json, PositionJsonContext.Default.PositionDto);
            return position is null ? null : (position.X, position.Y);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(int x, int y)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string json = JsonSerializer.Serialize(new PositionDto { X = x, Y = y }, PositionJsonContext.Default.PositionDto);
        File.WriteAllText(FilePath, json);
    }

    public static (int X, int Y) ClampToVirtualScreen(int x, int y)
    {
        ScreenBounds bounds = VirtualScreenBounds();
        return PositionMath.ClampToBounds(x, y, bounds);
    }

    private static ScreenBounds VirtualScreenBounds()
    {
        return new ScreenBounds(
            NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));
    }

    internal sealed class PositionDto
    {
        public int X { get; set; }

        public int Y { get; set; }
    }
}

[JsonSerializable(typeof(PositionStore.PositionDto))]
internal sealed partial class PositionJsonContext : JsonSerializerContext
{
}