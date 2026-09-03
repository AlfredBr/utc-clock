using System.Text.Json;
using utc_clock.Services;

var tests = new (string Name, Action Test)[]
{
    ("default position uses primary work area top-right margin", DefaultPositionUsesTopRightMargin),
    ("clamp keeps a saved position inside virtual screen bounds", ClampKeepsPositionInsideBounds),
    ("clamp handles screens smaller than the widget", ClampHandlesTinyBounds),
    ("union handles displays with negative coordinates", UnionHandlesNegativeCoordinates),
    ("overlap detects a widget parked in the taskbar strip", OverlapDetectsWidgetInTaskbarStrip),
    ("overlap ignores bounds that only touch or are disjoint", OverlapIgnoresTouchingOrDisjointBounds),
    ("launch options detect reset switch case-insensitively", LaunchOptionsDetectResetSwitch),
    ("startup registration quotes executable path", StartupRegistrationQuotesExecutablePath),
    ("digit layout derives the measured Cascadia Mono cells", DigitLayoutMatchesMeasuredCells),
    ("digit layout centers a narrower font the way DrawText does", DigitLayoutCentersNarrowerFont),
    ("digit layout rejects fonts that are not monospaced", DigitLayoutRejectsNonMonospace),
    ("easing starts and ends at rest and never overshoots", EasingIsKickAndDetent),
    ("offset schedule at 15.625 ms ticks matches the verified frames", OffsetScheduleMatchesVerifiedFrames),
    ("color lerp uses COLORREF byte order and truncates", ColorLerpTruncatesPerChannel),
    ("changed cells never include the colon", ChangedCellsSkipColon),
    ("one minute step wraps at midnight and rejects everything else", OneMinuteStepGuard),
    ("stagger delays run right to left in 40 ms steps", StaggerDelaysRunRightToLeft),
    ("frame at draws the dimmer glyph first and seats on completion", FrameAtDimsTowardTheEdge),
    ("ms to next second is clamped to the timer range", MsToNextSecondIsClamped),
    ("motion enabled prefers the persisted choice", MotionEnabledPrefersPersisted),
    ("minute roll completes after the last staggered digit", MinuteRollCompletesAfterLastDigit),
    ("position dto keeps the animate flag when the position changes", PositionDtoPreservesAnimateFlag),
    ("position dto round-trips the animate flag through json", PositionDtoRoundTripsThroughJson),
};

foreach ((string name, Action test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
        return 1;
    }
}

return 0;

static void DefaultPositionUsesTopRightMargin()
{
    var position = PositionMath.DefaultPosition(new ScreenBounds(0, 0, 1920, 1040));

    AssertEqual((1702, 24), position);
}

static void ClampKeepsPositionInsideBounds()
{
    var bounds = new ScreenBounds(-1280, 0, 3200, 1080);

    AssertEqual((-1280, 0), PositionMath.ClampToBounds(-4000, -200, bounds));
    AssertEqual((1726, 1080 - PositionMath.WidgetHeight), PositionMath.ClampToBounds(4000, 4000, bounds));
    AssertEqual((50, 75), PositionMath.ClampToBounds(50, 75, bounds));
}

static void ClampHandlesTinyBounds()
{
    var bounds = new ScreenBounds(10, 20, 80, 40);

    AssertEqual((10, 20), PositionMath.ClampToBounds(900, 900, bounds));
}

static void UnionHandlesNegativeCoordinates()
{
    var union = PositionMath.Union(
    [
        new ScreenBounds(-1280, 0, 1280, 1024),
        new ScreenBounds(0, -120, 1920, 1080),
    ]);

    AssertEqual(new ScreenBounds(-1280, -120, 3200, 1144), union);
}

static void OverlapDetectsWidgetInTaskbarStrip()
{
    // A 48px bottom taskbar on a 5120x1440 screen, with the widget parked in its strip.
    var widget = new ScreenBounds(3474, 1392, PositionMath.WidgetWidth, PositionMath.WidgetHeight);
    var shownTaskbar = new ScreenBounds(0, 1392, 5120, 48);
    var autoHiddenTaskbar = new ScreenBounds(0, 1438, 5120, 48);

    AssertTrue(PositionMath.Overlaps(widget, shownTaskbar));
    AssertTrue(PositionMath.Overlaps(widget, autoHiddenTaskbar));
    AssertTrue(PositionMath.Overlaps(shownTaskbar, widget));
}

static void OverlapIgnoresTouchingOrDisjointBounds()
{
    var widgetTopRight = new ScreenBounds(1702, 24, PositionMath.WidgetWidth, PositionMath.WidgetHeight);
    var bottomTaskbar = new ScreenBounds(0, 1392, 5120, 48);

    AssertFalse(PositionMath.Overlaps(widgetTopRight, bottomTaskbar));
    AssertFalse(PositionMath.Overlaps(new ScreenBounds(0, 0, 10, 10), new ScreenBounds(10, 0, 10, 10)));
    AssertFalse(PositionMath.Overlaps(new ScreenBounds(0, 0, 10, 10), new ScreenBounds(0, 10, 10, 10)));
}

static void LaunchOptionsDetectResetSwitch()
{
    AssertTrue(LaunchOptions.ResetRequested(["utc-clock.exe", "--RESET"]));
    AssertFalse(LaunchOptions.ResetRequested(["utc-clock.exe", "--not-reset"]));
}

static void StartupRegistrationQuotesExecutablePath()
{
    string command = StartupRegistration.BuildStartupCommand(@"C:\Program Files\Utc Clock\utc-clock.exe");

    AssertEqual("\"C:\\Program Files\\Utc Clock\\utc-clock.exe\"", command);
}

static void DigitLayoutMatchesMeasuredCells()
{
    DigitLayout? layout = DigitLayout.FromMetrics(21, 21, 105, 45, 37, 4, 128, 48);

    AssertTrue(layout.HasValue);
    DigitLayout cells = layout.GetValueOrDefault();
    AssertEqual(15, cells.FirstCellX);
    AssertEqual(1, cells.CellTop);
    AssertEqual(38, cells.Baseline);
    AssertEqual(12, cells.ApertureTop);
    AssertEqual(40, cells.ApertureBottom);
    AssertEqual(28, cells.Travel);
    AssertEqual(57, cells.CellX(DigitLayout.ColonIndex));
    AssertEqual(99, cells.CellX(4));
    AssertEqual(4, cells.Travel - DigitLayout.DigitInkHeight);
}

static void DigitLayoutCentersNarrowerFont()
{
    DigitLayout? layout = DigitLayout.FromMetrics(20, 20, 100, 40, 33, 4, 128, 48);

    AssertTrue(layout.HasValue);
    DigitLayout cells = layout.GetValueOrDefault();
    AssertEqual(18, cells.FirstCellX);
    AssertEqual(4, cells.CellTop);
    AssertEqual(37, cells.Baseline);
}

static void DigitLayoutRejectsNonMonospace()
{
    AssertFalse(DigitLayout.FromMetrics(21, 20, 105, 45, 37, 4, 128, 48).HasValue);
    AssertFalse(DigitLayout.FromMetrics(21, 21, 104, 45, 37, 4, 128, 48).HasValue);
    AssertFalse(DigitLayout.FromMetrics(0, 0, 0, 45, 37, 4, 128, 48).HasValue);
}

static void EasingIsKickAndDetent()
{
    AssertEqual(0.0, MinuteTransition.Ease(0));
    AssertEqual(1.0, MinuteTransition.Ease(1));
    AssertTrue(Math.Abs(MinuteTransition.Ease(0.5) - 0.6875) < 1e-9);
    AssertTrue(MinuteTransition.Ease(0.01) < 0.001);
    double previous = 0;
    for (int i = 1; i <= 1000; i++)
    {
        double value = MinuteTransition.Ease(i / 1000.0);
        AssertTrue(value >= previous, $"not monotonic at t={i / 1000.0}: {value} < {previous}");
        AssertTrue(value <= 1.0, $"overshoot at t={i / 1000.0}: {value}");
        previous = value;
    }
}

static void OffsetScheduleMatchesVerifiedFrames()
{
    int[] expected = [0, 0, 2, 4, 6, 9, 11, 14, 17, 19, 22, 23, 25, 26, 27, 28];
    for (int frame = 0; frame < expected.Length; frame++)
    {
        AssertEqual(expected[frame], MinuteTransition.Offset(frame * 15.625, 28));
    }

    AssertEqual(28, MinuteTransition.Offset(5000, 28));
    AssertEqual(0, MinuteTransition.Offset(-40, 28));
}

static void ColorLerpTruncatesPerChannel()
{
    const int fg = 0x00FAFAFA;
    const int bg = 0x001B1818;

    AssertEqual(fg, MinuteTransition.LerpColorRef(fg, bg, 0));
    AssertEqual(bg, MinuteTransition.LerpColorRef(fg, bg, 1));
    AssertEqual(0x008A8989, MinuteTransition.LerpColorRef(fg, bg, 0.5));
    AssertEqual(bg, MinuteTransition.LerpColorRef(fg, bg, 2));
}

static void ChangedCellsSkipColon()
{
    AssertEqual("00011", Mask(MinuteTransition.ChangedCells("12:39", "12:40")));
    AssertEqual("01011", Mask(MinuteTransition.ChangedCells("12:59", "13:00")));
    AssertEqual("11011", Mask(MinuteTransition.ChangedCells("09:59", "10:00")));
    AssertEqual("11011", Mask(MinuteTransition.ChangedCells("23:59", "00:00")));
    AssertEqual("00000", Mask(MinuteTransition.ChangedCells("12:34", "12:34")));
}

static string Mask(bool[] cells)
{
    return string.Concat(cells.Select(changed => changed ? '1' : '0'));
}

static void OneMinuteStepGuard()
{
    AssertTrue(MinuteTransition.IsOneMinuteStep("12:00", "12:01"));
    AssertTrue(MinuteTransition.IsOneMinuteStep("23:59", "00:00"));
    AssertFalse(MinuteTransition.IsOneMinuteStep("12:00", "12:02"));
    AssertFalse(MinuteTransition.IsOneMinuteStep("12:01", "12:00"));
    AssertFalse(MinuteTransition.IsOneMinuteStep(null, "12:00"));
    AssertFalse(MinuteTransition.IsOneMinuteStep("12:00", "12:00"));
    AssertFalse(MinuteTransition.IsOneMinuteStep("1200", "12:01"));
    AssertTrue(MinuteTransition.IsOneMinuteStep("12:59", "13:00"));
    AssertFalse(MinuteTransition.IsOneMinuteStep("12:60", "13:01"));
    AssertFalse(MinuteTransition.IsOneMinuteStep("23:59", "24:00"));
    AssertFalse(MinuteTransition.IsOneMinuteStep("1a:00", "1a:01"));
}

static void StaggerDelaysRunRightToLeft()
{
    int[] carry = MinuteTransition.StaggerDelays([false, false, false, true, true]);
    AssertEqual(0, carry[4]);
    AssertEqual(40, carry[3]);

    int[] midnight = MinuteTransition.StaggerDelays([true, true, false, true, true]);
    AssertEqual(0, midnight[4]);
    AssertEqual(40, midnight[3]);
    AssertEqual(80, midnight[1]);
    AssertEqual(120, midnight[0]);

    AssertEqual(280, MinuteTransition.TotalDurationMs(1));
    AssertEqual(320, MinuteTransition.TotalDurationMs(2));
    AssertEqual(360, MinuteTransition.TotalDurationMs(3));
    AssertEqual(400, MinuteTransition.TotalDurationMs(4));
    AssertEqual(0, MinuteTransition.TotalDurationMs(0));
}

static void FrameAtDimsTowardTheEdge()
{
    const int fg = 0x00FAFAFA;
    const int bg = 0x001B1818;

    // Color oracles are literals worked out by hand for #FAFAFA -> #18181B with EdgeDim 0.7, so a
    // change to the dimming constant or the lerp shows up here instead of being re-derived.
    CellFrame start = MinuteTransition.FrameAt(0, 0, 28, fg, bg);
    AssertTrue(start.Moving);
    AssertEqual(0, start.Dy);
    AssertEqual(fg, start.OldColor);
    AssertEqual(0x005D5B5B, start.NewColor);   // k = 0.7: the incoming glyph enters at the edge dim
    AssertFalse(start.DrawOldFirst);

    CellFrame early = MinuteTransition.FrameAt(5 * 15.625, 0, 28, fg, bg);
    AssertEqual(9, early.Dy);
    AssertEqual(0x00C7C7C7, early.OldColor);   // k = 0.225
    AssertEqual(0x00908E8E, early.NewColor);   // k = 0.475
    AssertFalse(early.DrawOldFirst);

    CellFrame tie = MinuteTransition.FrameAt(7 * 15.625, 0, 28, fg, bg);
    AssertEqual(14, tie.Dy);
    AssertEqual(0x00ABAAAA, tie.OldColor);     // k = 0.35 for both glyphs
    AssertEqual(tie.OldColor, tie.NewColor);
    AssertTrue(tie.DrawOldFirst);

    CellFrame late = MinuteTransition.FrameAt(10 * 15.625, 0, 28, fg, bg);
    AssertEqual(22, late.Dy);
    AssertEqual(0x007F7D7D, late.OldColor);    // k = 0.55
    AssertEqual(0x00D8D8D8, late.NewColor);    // k = 0.15
    AssertTrue(late.DrawOldFirst);

    CellFrame edge = MinuteTransition.FrameAt(15 * 15.625, 0, 28, fg, bg);
    AssertTrue(edge.Moving);                   // 234 ms: seated offset but the cell is still inside its 280 ms
    AssertEqual(28, edge.Dy);
    AssertEqual(0x005D5B5B, edge.OldColor);
    AssertEqual(fg, edge.NewColor);
    AssertTrue(edge.DrawOldFirst);

    CellFrame waiting = MinuteTransition.FrameAt(30, 40, 28, fg, bg);
    AssertTrue(waiting.Moving);
    AssertEqual(0, waiting.Dy);

    CellFrame done = MinuteTransition.FrameAt(280, 0, 28, fg, bg);
    AssertFalse(done.Moving);
}

static void MsToNextSecondIsClamped()
{
    AssertEqual(10, MinuteTransition.MsToNextSecond(995));
    AssertEqual(10, MinuteTransition.MsToNextSecond(999));
    AssertEqual(1000, MinuteTransition.MsToNextSecond(0));
    AssertEqual(505, MinuteTransition.MsToNextSecond(500));
}

static void MotionEnabledPrefersPersisted()
{
    AssertFalse(MinuteTransition.MotionEnabled(null, false));
    AssertTrue(MinuteTransition.MotionEnabled(null, true));
    AssertTrue(MinuteTransition.MotionEnabled(true, false));
    AssertFalse(MinuteTransition.MotionEnabled(false, true));
}

static void MinuteRollCompletesAfterLastDigit()
{
    var roll = new MinuteRoll("09:59", "10:00");

    AssertEqual(400, roll.TotalMs);
    AssertFalse(roll.IsComplete(399));
    AssertTrue(roll.IsComplete(400));
    AssertFalse(roll.FrameAt(DigitLayout.ColonIndex, 100, 28, 0x00FAFAFA, 0x001B1818).Moving);
    AssertTrue(roll.FrameAt(0, 100, 28, 0x00FAFAFA, 0x001B1818).Moving);   // hour tens waits 120 ms, still in flight
    AssertEqual(0, roll.FrameAt(0, 100, 28, 0x00FAFAFA, 0x001B1818).Dy);
    AssertTrue(roll.FrameAt(4, 100, 28, 0x00FAFAFA, 0x001B1818).Dy > 0);

    bool rejected = false;
    try
    {
        _ = new MinuteRoll("9:59", "10:00");
    }
    catch (ArgumentException)
    {
        rejected = true;
    }

    AssertTrue(rejected, "a non HH:mm string must be rejected");
}

static void PositionDtoPreservesAnimateFlag()
{
    PositionDto fresh = PositionDto.WithPosition(null, 1, 2);
    AssertEqual(1, fresh.X);
    AssertEqual(2, fresh.Y);
    AssertEqual((bool?)null, fresh.AnimateMinuteChange);

    var existing = new PositionDto { X = 10, Y = 20, AnimateMinuteChange = false };
    PositionDto moved = PositionDto.WithPosition(existing, 5, 6);
    AssertEqual(5, moved.X);
    AssertEqual(6, moved.Y);
    AssertEqual((bool?)false, moved.AnimateMinuteChange);
}

static void PositionDtoRoundTripsThroughJson()
{
    var dto = new PositionDto { X = 3, Y = 4, AnimateMinuteChange = true };
    string json = JsonSerializer.Serialize(dto, PositionJsonContext.Default.PositionDto);
    PositionDto? back = JsonSerializer.Deserialize(json, PositionJsonContext.Default.PositionDto);

    AssertTrue(back is not null);
    AssertEqual(3, back!.X);
    AssertEqual(4, back.Y);
    AssertEqual((bool?)true, back.AnimateMinuteChange);

    PositionDto? legacy = JsonSerializer.Deserialize("{\"X\":7,\"Y\":8}", PositionJsonContext.Default.PositionDto);
    AssertTrue(legacy is not null);
    AssertEqual((bool?)null, legacy!.AnimateMinuteChange);
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"expected {expected}, got {actual}");
    }
}

static void AssertTrue(bool value, string? message = null)
{
    if (!value)
    {
        throw new InvalidOperationException(message ?? "expected true");
    }
}

static void AssertFalse(bool value, string? message = null)
{
    if (value)
    {
        throw new InvalidOperationException(message ?? "expected false");
    }
}
