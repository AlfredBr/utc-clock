using System.Text.Json;
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
        PositionDto? position = LoadDto();
        return position is null ? null : (position.X, position.Y);
    }

    public static bool? LoadAnimateSetting()
    {
        return LoadDto()?.AnimateMinuteChange;
    }

    /// <summary>Saves the window position and keeps every other persisted setting as it was.</summary>
    public static void Save(int x, int y)
    {
        WriteDto(PositionDto.WithPosition(LoadDto(), x, y));
    }

    /// <summary>Saves the animate choice together with the live window position, so a missing file never records (0, 0).</summary>
    public static void SaveAnimateSetting(bool? value, int x, int y)
    {
        WriteDto(new PositionDto { X = x, Y = y, AnimateMinuteChange = value });
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

    private static PositionDto? LoadDto()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize(json, PositionJsonContext.Default.PositionDto);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Best-effort, like the Run-key registration: a read-only or redirected LocalAppData must not
    /// terminate the widget, it just stops remembering the position and the animate choice.
    /// </summary>
    private static void WriteDto(PositionDto dto)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string json = JsonSerializer.Serialize(dto, PositionJsonContext.Default.PositionDto);
            File.WriteAllText(FilePath, json);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
