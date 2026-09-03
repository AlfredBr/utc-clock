using System.Text.Json.Serialization;

namespace utc_clock.Services;

/// <summary>Everything persisted in position.json. New fields must be nullable so old files still load.</summary>
internal sealed class PositionDto
{
    public int X { get; set; }

    public int Y { get; set; }

    /// <summary>Null means "follow the Windows animation switch"; true or false is an explicit user choice.</summary>
    public bool? AnimateMinuteChange { get; set; }

    public static PositionDto WithPosition(PositionDto? existing, int x, int y)
    {
        return new PositionDto { X = x, Y = y, AnimateMinuteChange = existing?.AnimateMinuteChange };
    }
}

[JsonSerializable(typeof(PositionDto))]
internal sealed partial class PositionJsonContext : JsonSerializerContext
{
}
