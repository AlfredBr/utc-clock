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

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"expected {expected}, got {actual}");
    }
}

static void AssertTrue(bool value)
{
    if (!value)
    {
        throw new InvalidOperationException("expected true");
    }
}

static void AssertFalse(bool value)
{
    if (value)
    {
        throw new InvalidOperationException("expected false");
    }
}
