using utc_clock.Services;

var tests = new (string Name, Action Test)[]
{
    ("default position uses primary work area top-right margin", DefaultPositionUsesTopRightMargin),
    ("clamp keeps a saved position inside virtual screen bounds", ClampKeepsPositionInsideBounds),
    ("clamp handles screens smaller than the widget", ClampHandlesTinyBounds),
    ("union handles displays with negative coordinates", UnionHandlesNegativeCoordinates),
    ("launch options detect reset switch case-insensitively", LaunchOptionsDetectResetSwitch),
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
    AssertEqual((1726, 1012), PositionMath.ClampToBounds(4000, 4000, bounds));
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

static void LaunchOptionsDetectResetSwitch()
{
    AssertTrue(LaunchOptions.ResetRequested(["utc-clock.exe", "--RESET"]));
    AssertFalse(LaunchOptions.ResetRequested(["utc-clock.exe", "--not-reset"]));
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
