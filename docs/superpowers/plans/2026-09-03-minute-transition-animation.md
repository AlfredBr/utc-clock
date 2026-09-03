# Minute Transition Animation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the digits that change at a minute boundary roll upward through a clipped aperture with damped edges, drawn with pure GDI, while the resting widget stays pixel-identical to today.

**Architecture:** Pure geometry and timing logic goes into two new `Services/` files with no Win32 dependency so the hand-rolled test runner covers them. `WidgetWindow` gains a memory-DC back buffer, per-cell `ExtTextOut` drawing, a tick timer aligned to the wall-clock second, and a `DwmFlush`-paced frame loop driven by posted `WM_APP` messages. The persisted position file gains a nullable animate flag exposed through the right-click menu.

**Tech Stack:** C# / .NET 10, Native AOT, flat P/Invoke into user32, gdi32 and dwmapi. Tests are the existing `(Name, Action)` table in `UtcClockWidget.Tests/Program.cs`.

**Spec:** `docs/superpowers/specs/2026-09-03-minute-transition-animation-design.md`

## Global Constraints

- No new NuGet packages, no COM interop, no GDI+, no Direct2D. Flat `DllImport` only.
- `dotnet publish utc-clock.csproj -c Release -o .\publish` must produce no new IL or AOT warnings.
- Resting render must be pixel-identical to the current build.
- Owner rules: no gradients, no shadows, flat colors, sans-serif fonts only.
- **Commits are made by the owner, never by an agent.** Each task ends with "leave the changes staged for the owner to review" instead of a commit.
- Motion constants: `DurationMs = 280`, `StaggerMs = 40`, `EdgeDim = 0.7`, `DimCurve = 1.0`, `DigitInkHeight = 24`, `AperturePadding = 2`, travel 28.
- Default behavior follows the Windows animation switch; the carry lag is kept at 40 ms (owner decisions, 2026-09-03).

---

### Task 1: DigitLayout (pure cell geometry)

**Files:**
- Create: `Services/DigitLayout.cs`
- Modify: `UtcClockWidget.Tests/UtcClockWidget.Tests.csproj` (add `<Compile Include>`)
- Test: `UtcClockWidget.Tests/Program.cs`

**Interfaces:**
- Produces: `internal readonly record struct DigitLayout(int CellWidth, int FirstCellX, int CellTop, int Baseline, int ApertureTop, int ApertureBottom, int Travel)` with `int CellX(int index)`, constants `CellCount = 5`, `ColonIndex = 2`, `DigitInkHeight = 24`, `AperturePadding = 2`, and `static DigitLayout? FromMetrics(int digitAdvance, int colonAdvance, int textWidth, int textHeight, int ascent, int timeLeft, int timeWidth, int widgetHeight)`.

- [x] **Step 1: Add the compile include so the test project sees the new file**

In `UtcClockWidget.Tests/UtcClockWidget.Tests.csproj` add inside the existing `<ItemGroup>`:

```xml
    <Compile Include="..\Services\DigitLayout.cs" Link="Services\DigitLayout.cs" />
```

- [x] **Step 2: Write the failing tests**

Add to the `tests` array in `UtcClockWidget.Tests/Program.cs`:

```csharp
    ("digit layout derives the measured Cascadia Mono cells", DigitLayoutMatchesMeasuredCells),
    ("digit layout centers a narrower font the way DrawText does", DigitLayoutCentersNarrowerFont),
    ("digit layout rejects fonts that are not monospaced", DigitLayoutRejectsNonMonospace),
```

and the test bodies:

```csharp
static void DigitLayoutMatchesMeasuredCells()
{
    DigitLayout? layout = DigitLayout.FromMetrics(21, 21, 105, 45, 37, 4, 128, 48);

    AssertTrue(layout.HasValue);
    AssertEqual(15, layout!.Value.FirstCellX);
    AssertEqual(1, layout.Value.CellTop);
    AssertEqual(38, layout.Value.Baseline);
    AssertEqual(12, layout.Value.ApertureTop);
    AssertEqual(40, layout.Value.ApertureBottom);
    AssertEqual(28, layout.Value.Travel);
    AssertEqual(57, layout.Value.CellX(DigitLayout.ColonIndex));
    AssertEqual(99, layout.Value.CellX(4));
    AssertEqual(4, layout.Value.Travel - DigitLayout.DigitInkHeight);
}

static void DigitLayoutCentersNarrowerFont()
{
    DigitLayout? layout = DigitLayout.FromMetrics(20, 20, 100, 40, 33, 4, 128, 48);

    AssertTrue(layout.HasValue);
    AssertEqual(18, layout!.Value.FirstCellX);
    AssertEqual(4, layout.Value.CellTop);
    AssertEqual(37, layout.Value.Baseline);
}

static void DigitLayoutRejectsNonMonospace()
{
    AssertFalse(DigitLayout.FromMetrics(21, 20, 105, 45, 37, 4, 128, 48).HasValue);
    AssertFalse(DigitLayout.FromMetrics(21, 21, 104, 45, 37, 4, 128, 48).HasValue);
    AssertFalse(DigitLayout.FromMetrics(0, 0, 0, 45, 37, 4, 128, 48).HasValue);
}
```

- [x] **Step 3: Run the tests to verify they fail**

Run: `dotnet run --project UtcClockWidget.Tests\UtcClockWidget.Tests.csproj`
Expected: build error, `DigitLayout` does not exist.

- [x] **Step 4: Write the implementation**

Create `Services/DigitLayout.cs`:

```csharp
namespace utc_clock.Services;

/// <summary>
/// Cell geometry for drawing "HH:mm" one character at a time so the result is pixel-identical to
/// DrawText(DT_CENTER | DT_VCENTER | DT_SINGLELINE) in the time rect, plus the aperture a rolling digit
/// is clipped to. All values are whole pixels relative to the widget's client area.
/// </summary>
internal readonly record struct DigitLayout(
    int CellWidth,
    int FirstCellX,
    int CellTop,
    int Baseline,
    int ApertureTop,
    int ApertureBottom,
    int Travel)
{
    public const int CellCount = 5;
    public const int ColonIndex = 2;

    /// <summary>Rows of ink in every digit of Cascadia Mono at height -34 (rows 14..37 of the widget).</summary>
    public const int DigitInkHeight = 24;

    /// <summary>Rows of surface kept above and below the ink inside the aperture.</summary>
    public const int AperturePadding = 2;

    public int CellX(int index) => FirstCellX + index * CellWidth;

    /// <summary>
    /// Derives the layout from GDI measurements of the time font. Returns null when the font is not
    /// monospaced across the digits and the colon, in which case the caller keeps the whole-string path.
    /// </summary>
    public static DigitLayout? FromMetrics(
        int digitAdvance,
        int colonAdvance,
        int textWidth,
        int textHeight,
        int ascent,
        int timeLeft,
        int timeWidth,
        int widgetHeight)
    {
        if (digitAdvance <= 0
            || colonAdvance != digitAdvance
            || textWidth != CellCount * digitAdvance
            || textHeight <= 0
            || ascent <= 0
            || ascent > textHeight)
        {
            return null;
        }

        // DrawText centers with integer division, so the same floor keeps the cells on today's pixels.
        int firstCellX = timeLeft + (timeWidth - textWidth) / 2;
        int cellTop = (widgetHeight - textHeight) / 2;
        int baseline = cellTop + ascent;
        int apertureBottom = baseline + AperturePadding;
        int travel = DigitInkHeight + 2 * AperturePadding;
        int apertureTop = apertureBottom - travel;
        if (apertureTop < 0 || apertureBottom > widgetHeight)
        {
            return null;
        }

        return new DigitLayout(digitAdvance, firstCellX, cellTop, baseline, apertureTop, apertureBottom, travel);
    }
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet run --project UtcClockWidget.Tests\UtcClockWidget.Tests.csproj`
Expected: every line starts with `PASS`, including the three new names.

- [x] **Step 6: Leave the changes staged for the owner to review**

```bash
git add Services/DigitLayout.cs UtcClockWidget.Tests/UtcClockWidget.Tests.csproj UtcClockWidget.Tests/Program.cs
```

---

### Task 2: MinuteTransition and MinuteRoll (pure timing, easing, color)

**Files:**
- Create: `Services/MinuteTransition.cs`
- Modify: `UtcClockWidget.Tests/UtcClockWidget.Tests.csproj`
- Test: `UtcClockWidget.Tests/Program.cs`

**Interfaces:**
- Consumes: `DigitLayout.CellCount`, `DigitLayout.ColonIndex` from Task 1.
- Produces: `internal readonly record struct CellFrame(bool Moving, int Dy, int OldColor, int NewColor, bool DrawOldFirst)`; `internal static class MinuteTransition` with `DurationMs`, `StaggerMs`, `EdgeDim`, `DimCurve`, `double Ease(double t)`, `int LerpColorRef(int from, int to, double k)`, `bool[] ChangedCells(string from, string to)`, `bool IsOneMinuteStep(string? from, string to)`, `int[] StaggerDelays(bool[] changed)`, `int TotalDurationMs(int changedCount)`, `int Offset(double localMs, int travel)`, `CellFrame FrameAt(double elapsedMs, int delayMs, int travel, int foreground, int background)`, `int MsToNextSecond(int millisecond)`, `bool MotionEnabled(bool? persisted, bool systemAnimations)`; `internal sealed class MinuteRoll(string from, string to)` with `From`, `To`, `TotalMs`, `bool IsChanging(int cell)`, `CellFrame FrameAt(int cell, double elapsedMs, int travel, int foreground, int background)`, `bool IsComplete(double elapsedMs)`.

- [x] **Step 1: Add the compile include**

```xml
    <Compile Include="..\Services\MinuteTransition.cs" Link="Services\MinuteTransition.cs" />
```

- [x] **Step 2: Write the failing tests**

Add to the `tests` array:

```csharp
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
```

and the bodies:

```csharp
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
        AssertTrue(value >= previous);
        AssertTrue(value <= 1.0);
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

    CellFrame start = MinuteTransition.FrameAt(0, 0, 28, fg, bg);
    AssertTrue(start.Moving);
    AssertEqual(0, start.Dy);
    AssertEqual(fg, start.OldColor);
    AssertFalse(start.DrawOldFirst);

    CellFrame early = MinuteTransition.FrameAt(5 * 15.625, 0, 28, fg, bg);
    AssertEqual(9, early.Dy);
    AssertFalse(early.DrawOldFirst);

    CellFrame late = MinuteTransition.FrameAt(10 * 15.625, 0, 28, fg, bg);
    AssertEqual(22, late.Dy);
    AssertTrue(late.DrawOldFirst);
    AssertEqual(MinuteTransition.LerpColorRef(fg, bg, MinuteTransition.EdgeDim * 22 / 28.0), late.OldColor);

    CellFrame waiting = MinuteTransition.FrameAt(30, 40, 28, fg, bg);
    AssertTrue(waiting.Moving);
    AssertEqual(0, waiting.Dy);

    CellFrame done = MinuteTransition.FrameAt(280, 0, 28, fg, bg);
    AssertFalse(done.Moving);
}

static void MsToNextSecondIsClamped()
{
    AssertEqual(10, MinuteTransition.MsToNextSecond(995));
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
    AssertTrue(roll.IsChanging(0));
    AssertFalse(roll.IsChanging(DigitLayout.ColonIndex));
    AssertFalse(roll.IsComplete(399));
    AssertTrue(roll.IsComplete(400));
    AssertFalse(roll.FrameAt(DigitLayout.ColonIndex, 100, 28, 0x00FAFAFA, 0x001B1818).Moving);
    AssertEqual(0, roll.FrameAt(0, 100, 28, 0x00FAFAFA, 0x001B1818).Dy);
    AssertTrue(roll.FrameAt(4, 100, 28, 0x00FAFAFA, 0x001B1818).Dy > 0);
}
```

- [x] **Step 3: Run the tests to verify they fail**

Run: `dotnet run --project UtcClockWidget.Tests\UtcClockWidget.Tests.csproj`
Expected: build error, `MinuteTransition` does not exist.

- [x] **Step 4: Write the implementation**

Create `Services/MinuteTransition.cs`:

```csharp
namespace utc_clock.Services;

/// <summary>What to draw in one digit cell for one frame of a roll.</summary>
/// <param name="Moving">False once the cell has seated; draw the target glyph at rest in full color.</param>
/// <param name="Dy">Whole-pixel displacement of the outgoing glyph upward (the incoming glyph sits at travel - Dy below rest).</param>
/// <param name="OldColor">COLORREF for the outgoing glyph.</param>
/// <param name="NewColor">COLORREF for the incoming glyph.</param>
/// <param name="DrawOldFirst">True when the outgoing glyph is the dimmer of the two and must be drawn first.</param>
internal readonly record struct CellFrame(bool Moving, int Dy, int OldColor, int NewColor, bool DrawOldFirst)
{
    public static readonly CellFrame Seated = new(false, 0, 0, 0, false);
}

/// <summary>Pure timing, easing and color math for the minute-change roll. No Win32 here.</summary>
internal static class MinuteTransition
{
    public const int DurationMs = 280;
    public const int StaggerMs = 40;
    public const double EdgeDim = 0.7;
    public const double DimCurve = 1.0;

    private const int MinutesPerDay = 24 * 60;

    /// <summary>Kick-and-detent: e(t) = 1 - (1 - t)^3 (1 + 3t). Zero velocity at both ends, peak speed at t = 1/3.</summary>
    public static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        double u = 1 - t;
        return 1 - u * u * u * (1 + 3 * t);
    }

    /// <summary>Linear interpolation between two 0x00BBGGRR COLORREFs, truncating each channel.</summary>
    public static int LerpColorRef(int from, int to, double k)
    {
        k = Math.Clamp(k, 0, 1);
        int r = Channel(from & 0xFF, to & 0xFF, k);
        int g = Channel((from >> 8) & 0xFF, (to >> 8) & 0xFF, k);
        int b = Channel((from >> 16) & 0xFF, (to >> 16) & 0xFF, k);
        return r | (g << 8) | (b << 16);
    }

    private static int Channel(int from, int to, double k)
    {
        return (int)(from + (to - from) * k);
    }

    /// <summary>Which of the five cells differ. The colon cell is never marked.</summary>
    public static bool[] ChangedCells(string from, string to)
    {
        var changed = new bool[DigitLayout.CellCount];
        for (int i = 0; i < DigitLayout.CellCount; i++)
        {
            changed[i] = i != DigitLayout.ColonIndex
                && i < from.Length
                && i < to.Length
                && from[i] != to[i];
        }

        return changed;
    }

    /// <summary>True only when <paramref name="to"/> is exactly one minute after <paramref name="from"/>, wrapping at midnight.</summary>
    public static bool IsOneMinuteStep(string? from, string to)
    {
        return from is not null
            && TryParseMinutes(from, out int fromMinutes)
            && TryParseMinutes(to, out int toMinutes)
            && (fromMinutes + 1) % MinutesPerDay == toMinutes;
    }

    private static bool TryParseMinutes(string text, out int minutes)
    {
        minutes = 0;
        if (text.Length != 5 || text[2] != ':')
        {
            return false;
        }

        foreach (int index in (int[])[0, 1, 3, 4])
        {
            if (!char.IsAsciiDigit(text[index]))
            {
                return false;
            }
        }

        int hours = (text[0] - '0') * 10 + (text[1] - '0');
        int mins = (text[3] - '0') * 10 + (text[4] - '0');
        if (hours > 23 || mins > 59)
        {
            return false;
        }

        minutes = hours * 60 + mins;
        return true;
    }

    /// <summary>Per-cell start delay: StaggerMs for every changing cell to the right, so a carry runs right to left.</summary>
    public static int[] StaggerDelays(bool[] changed)
    {
        var delays = new int[changed.Length];
        int toTheRight = 0;
        for (int i = changed.Length - 1; i >= 0; i--)
        {
            delays[i] = StaggerMs * toTheRight;
            if (changed[i])
            {
                toTheRight++;
            }
        }

        return delays;
    }

    public static int TotalDurationMs(int changedCount)
    {
        return changedCount <= 0 ? 0 : DurationMs + StaggerMs * (changedCount - 1);
    }

    /// <summary>Whole-pixel displacement of the outgoing glyph after <paramref name="localMs"/> of its own roll.</summary>
    public static int Offset(double localMs, int travel)
    {
        if (localMs <= 0)
        {
            return 0;
        }

        if (localMs >= DurationMs)
        {
            return travel;
        }

        return (int)Math.Round(travel * Ease(localMs / DurationMs), MidpointRounding.AwayFromZero);
    }

    /// <summary>The frame for one changing cell. <paramref name="delayMs"/> is the cell's stagger delay.</summary>
    public static CellFrame FrameAt(double elapsedMs, int delayMs, int travel, int foreground, int background)
    {
        double local = elapsedMs - delayMs;
        if (local >= DurationMs)
        {
            return CellFrame.Seated;
        }

        int dy = Offset(local, travel);
        double oldDim = EdgeDim * Math.Pow((double)dy / travel, DimCurve);
        double newDim = EdgeDim * Math.Pow((double)(travel - dy) / travel, DimCurve);
        return new CellFrame(
            Moving: true,
            Dy: dy,
            OldColor: LerpColorRef(foreground, background, oldDim),
            NewColor: LerpColorRef(foreground, background, newDim),
            DrawOldFirst: oldDim >= newDim);
    }

    /// <summary>Milliseconds until just after the next wall-clock second, clamped to the USER timer range.</summary>
    public static int MsToNextSecond(int millisecond)
    {
        return Math.Clamp(1000 - millisecond + 5, 10, 1000);
    }

    /// <summary>An explicit user choice wins; otherwise follow the Windows animation switch.</summary>
    public static bool MotionEnabled(bool? persisted, bool systemAnimations)
    {
        return persisted ?? systemAnimations;
    }
}

/// <summary>One minute-change roll from one "HH:mm" string to the next.</summary>
internal sealed class MinuteRoll
{
    private readonly bool[] changed;
    private readonly int[] delays;

    public MinuteRoll(string from, string to)
    {
        From = from;
        To = to;
        changed = MinuteTransition.ChangedCells(from, to);
        delays = MinuteTransition.StaggerDelays(changed);
        TotalMs = MinuteTransition.TotalDurationMs(changed.Count(cell => cell));
    }

    public string From { get; }

    public string To { get; }

    public int TotalMs { get; }

    public bool IsChanging(int cell) => changed[cell];

    public CellFrame FrameAt(int cell, double elapsedMs, int travel, int foreground, int background)
    {
        return changed[cell]
            ? MinuteTransition.FrameAt(elapsedMs, delays[cell], travel, foreground, background)
            : CellFrame.Seated;
    }

    public bool IsComplete(double elapsedMs) => elapsedMs >= TotalMs;
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet run --project UtcClockWidget.Tests\UtcClockWidget.Tests.csproj`
Expected: all `PASS`.

- [x] **Step 6: Leave the changes staged for the owner to review**

```bash
git add Services/MinuteTransition.cs UtcClockWidget.Tests/UtcClockWidget.Tests.csproj UtcClockWidget.Tests/Program.cs
```

---

### Task 3: Persisted animate setting

**Files:**
- Create: `Services/PositionDto.cs` (DTO and JSON context move here so the test project can compile them)
- Modify: `Services/PositionStore.cs`
- Modify: `UtcClockWidget.Tests/UtcClockWidget.Tests.csproj`
- Test: `UtcClockWidget.Tests/Program.cs`

**Interfaces:**
- Produces: `internal sealed class PositionDto { int X; int Y; bool? AnimateMinuteChange; static PositionDto WithPosition(PositionDto? existing, int x, int y); static PositionDto WithAnimateMinuteChange(PositionDto? existing, bool? value); }`, `internal sealed partial class PositionJsonContext`; `PositionStore.LoadAnimateSetting() : bool?` and `PositionStore.SaveAnimateSetting(bool? value)`. `PositionStore.Save(x, y)` now preserves the flag.

- [x] **Step 1: Add the compile include**

```xml
    <Compile Include="..\Services\PositionDto.cs" Link="Services\PositionDto.cs" />
```

- [x] **Step 2: Write the failing tests**

Add to the `tests` array:

```csharp
    ("position dto keeps the animate flag when the position changes", PositionDtoPreservesAnimateFlag),
    ("position dto round-trips the animate flag through json", PositionDtoRoundTripsThroughJson),
```

and the bodies (add `using System.Text.Json;` at the top of `Program.cs`):

```csharp
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

    PositionDto toggled = PositionDto.WithAnimateMinuteChange(existing, true);
    AssertEqual(10, toggled.X);
    AssertEqual(20, toggled.Y);
    AssertEqual((bool?)true, toggled.AnimateMinuteChange);
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
    AssertEqual((bool?)null, legacy!.AnimateMinuteChange);
}
```

- [x] **Step 3: Run the tests to verify they fail**

Run: `dotnet run --project UtcClockWidget.Tests\UtcClockWidget.Tests.csproj`
Expected: build error, `PositionDto` is not accessible.

- [x] **Step 4: Create the DTO file**

Create `Services/PositionDto.cs`:

```csharp
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

    public static PositionDto WithAnimateMinuteChange(PositionDto? existing, bool? value)
    {
        return new PositionDto { X = existing?.X ?? 0, Y = existing?.Y ?? 0, AnimateMinuteChange = value };
    }
}

[JsonSerializable(typeof(PositionDto))]
internal sealed partial class PositionJsonContext : JsonSerializerContext
{
}
```

- [x] **Step 5: Rewire PositionStore**

In `Services/PositionStore.cs`: delete the nested `PositionDto` class and the `PositionJsonContext` at the bottom, remove `using System.Text.Json.Serialization;`, and replace `Load` and `Save` with:

```csharp
    public static (int X, int Y)? Load()
    {
        PositionDto? position = LoadDto();
        return position is null ? null : (position.X, position.Y);
    }

    public static bool? LoadAnimateSetting()
    {
        return LoadDto()?.AnimateMinuteChange;
    }

    public static void Save(int x, int y)
    {
        WriteDto(PositionDto.WithPosition(LoadDto(), x, y));
    }

    public static void SaveAnimateSetting(bool? value)
    {
        WriteDto(PositionDto.WithAnimateMinuteChange(LoadDto(), value));
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
```

`WithAnimateMinuteChange(null, value)` falls back to X = 0, Y = 0 only when no file exists yet; the widget always saves its position at startup before the menu can be used, so in practice the existing DTO is present.

- [x] **Step 6: Run the tests and build the app**

Run: `dotnet run --project UtcClockWidget.Tests\UtcClockWidget.Tests.csproj` then `dotnet build utc-clock.csproj -c Debug`
Expected: all `PASS`; build succeeds with 0 warnings.

- [x] **Step 7: Leave the changes staged for the owner to review**

```bash
git add Services/PositionDto.cs Services/PositionStore.cs UtcClockWidget.Tests/UtcClockWidget.Tests.csproj UtcClockWidget.Tests/Program.cs
```

---

### Task 4: Native declarations

**Files:**
- Modify: `Native/NativeMethods.cs`

**Interfaces:**
- Produces: constants `WM_SETTINGCHANGE`, `WM_APP`, `SPI_GETCLIENTAREAANIMATION`, `SPI_GETUIEFFECTS`, `SM_REMOTESESSION`, `USER_TIMER_MINIMUM`, `MF_CHECKED`, `SRCCOPY`, `ETO_CLIPPED`; imports `GetDC`, `ReleaseDC`, `CreateCompatibleDC`, `CreateCompatibleBitmap`, `DeleteDC`, `BitBlt`, `ExtTextOut` (pointer signature), `GetTextExtentPoint32`, `GetTextMetrics`, `SystemParametersInfo(int, int, out int, int)`, `DwmFlush`, `PostMessage`, `InvalidateRect(IntPtr, RECT*, bool)`; structs `SIZE`, `TEXTMETRICW`.

- [x] **Step 1: Add the constants**

After `internal const int WM_RBUTTONUP = 0x0205;` add:

```csharp
    internal const int WM_SETTINGCHANGE = 0x001A;
    internal const uint WM_APP = 0x8000;

    internal const int SPI_GETCLIENTAREAANIMATION = 0x1042;
    internal const int SPI_GETUIEFFECTS = 0x103E;
    internal const int SM_REMOTESESSION = 0x1000;
    internal const uint USER_TIMER_MINIMUM = 10;
    internal const int MF_CHECKED = 0x00000008;
    internal const uint SRCCOPY = 0x00CC0020;
    internal const uint ETO_CLIPPED = 0x0004;
```

- [x] **Step 2: Add the imports**

After the existing `InvalidateRect` import add:

```csharp
    [DllImport("user32.dll", EntryPoint = "InvalidateRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InvalidateRect(IntPtr hWnd, RECT* lpRect, bool bErase);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(int uiAction, int uiParam, out int pvParam, int fWinIni);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BitBlt(IntPtr hdcDest, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);

    [DllImport("gdi32.dll", EntryPoint = "ExtTextOutW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ExtTextOut(IntPtr hdc, int x, int y, uint options, RECT* lprect, char* lpString, uint c, IntPtr lpDx);

    [DllImport("gdi32.dll", EntryPoint = "GetTextExtentPoint32W", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTextExtentPoint32(IntPtr hdc, [MarshalAs(UnmanagedType.LPWStr)] string lpString, int c, out SIZE psizl);

    [DllImport("gdi32.dll", EntryPoint = "GetTextMetricsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTextMetrics(IntPtr hdc, out TEXTMETRICW lptm);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmFlush();
```

- [x] **Step 3: Add the structs**

After the `POINT` struct add:

```csharp
    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct TEXTMETRICW
    {
        public int tmHeight;
        public int tmAscent;
        public int tmDescent;
        public int tmInternalLeading;
        public int tmExternalLeading;
        public int tmAveCharWidth;
        public int tmMaxCharWidth;
        public int tmWeight;
        public int tmOverhang;
        public int tmDigitizedAspectX;
        public int tmDigitizedAspectY;
        public ushort tmFirstChar;
        public ushort tmLastChar;
        public ushort tmDefaultChar;
        public ushort tmBreakChar;
        public byte tmItalic;
        public byte tmUnderlined;
        public byte tmStruckOut;
        public byte tmPitchAndFamily;
        public byte tmCharSet;
    }
```

- [x] **Step 4: Build**

Run: `dotnet build utc-clock.csproj -c Debug`
Expected: succeeds, 0 warnings (unused imports are fine; they are used in Tasks 5 and 6).

- [x] **Step 5: Leave the changes staged for the owner to review**

```bash
git add Native/NativeMethods.cs
```

---

### Task 5: Back buffer, per-cell resting render, second-aligned tick

**Files:**
- Modify: `WidgetWindow.cs`

**Interfaces:**
- Consumes: `DigitLayout.FromMetrics`, `MinuteTransition.MsToNextSecond`, the Task 4 imports.
- Produces: fields `backBufferDc`, `backBufferBitmap`, `layout`, `displayed`; methods `CreateBackBuffer()`, `MeasureLayout()`, `RenderSeated(string text)`, `ComposeBackground(IntPtr dc)`, `DrawCell(IntPtr dc, DigitLayout l, int cell, char glyph, int y, int color, NativeMethods.RECT clip)`, `DrawLabel(IntPtr dc)`, `Present()`, `Snap(string text)`, `CurrentText()`, `OnTick()`. Task 6 adds the roll on top of these.

- [x] **Step 1: Add fields and constants**

Replace `private const uint TimerId = 1;` with:

```csharp
    private const uint TickTimerId = 1;
    private const uint FrameTimerId = 2;
    private const uint AnimateMenuId = 102;
    private const uint FrameMessage = NativeMethods.WM_APP + 1;
    private static readonly NativeMethods.RECT TimeRect = new()
    {
        Left = 4,
        Top = 0,
        Right = 132,
        Bottom = PositionMath.WidgetHeight,
    };
    private static readonly NativeMethods.RECT LabelRect = new()
    {
        Left = 132,
        Top = 1,
        Right = PositionMath.WidgetWidth - 4,
        Bottom = PositionMath.WidgetHeight,
    };
```

After `private NativeMethods.RECT dragStartWindow;` add:

```csharp
    private IntPtr backBufferDc;
    private IntPtr backBufferBitmap;
    private DigitLayout? layout;
    private string? displayed;
```

Add `using System.Globalization;` at the top.

- [x] **Step 2: Create the buffer and first frame before the window is shown**

In `CreateWindow`, immediately after the `if (hwnd == IntPtr.Zero) { throw ... }` block and before `SetLayeredWindowAttributes`, insert:

```csharp
        CreateBackBuffer();
        MeasureLayout();
        Snap(CurrentText());
```

Replace `NativeMethods.SetTimer(hwnd, new UIntPtr(TimerId), 1000, IntPtr.Zero);` with:

```csharp
        NativeMethods.SetTimer(hwnd, new UIntPtr(TickTimerId), (uint)MinuteTransition.MsToNextSecond(DateTime.UtcNow.Millisecond), IntPtr.Zero);
```

Add these methods after `CreateWindow`:

```csharp
    private void CreateBackBuffer()
    {
        IntPtr windowDc = NativeMethods.GetDC(hwnd);
        if (windowDc == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // The bitmap must be compatible with the window DC, not the fresh memory DC, or it is 1-bpp.
            IntPtr dc = NativeMethods.CreateCompatibleDC(windowDc);
            IntPtr bitmap = NativeMethods.CreateCompatibleBitmap(windowDc, PositionMath.WidgetWidth, PositionMath.WidgetHeight);
            if (dc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                if (dc != IntPtr.Zero) NativeMethods.DeleteDC(dc);
                if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
                return;
            }

            NativeMethods.SelectObject(dc, bitmap);
            NativeMethods.SetBkMode(dc, NativeMethods.TRANSPARENT);
            backBufferDc = dc;
            backBufferBitmap = bitmap;
        }
        finally
        {
            NativeMethods.ReleaseDC(hwnd, windowDc);
        }
    }

    private void MeasureLayout()
    {
        layout = null;
        if (backBufferDc == IntPtr.Zero)
        {
            return;
        }

        IntPtr oldFont = NativeMethods.SelectObject(backBufferDc, timeFont);
        bool measured = NativeMethods.GetTextExtentPoint32(backBufferDc, "0", 1, out NativeMethods.SIZE digit)
            && NativeMethods.GetTextExtentPoint32(backBufferDc, ":", 1, out NativeMethods.SIZE colon)
            && NativeMethods.GetTextExtentPoint32(backBufferDc, "00:00", 5, out NativeMethods.SIZE text)
            && NativeMethods.GetTextMetrics(backBufferDc, out NativeMethods.TEXTMETRICW metrics);
        NativeMethods.SelectObject(backBufferDc, oldFont);
        if (!measured)
        {
            return;
        }

        layout = DigitLayout.FromMetrics(
            digit.cx,
            colon.cx,
            text.cx,
            metrics.tmHeight,
            metrics.tmAscent,
            TimeRect.Left,
            TimeRect.Right - TimeRect.Left,
            PositionMath.WidgetHeight);
    }

    private static string CurrentText()
    {
        return DateTime.UtcNow.ToString("HH:mm", CultureInfo.InvariantCulture);
    }
```

Note: C# does not allow `out` variables declared inside a `&&` chain to be used after the chain unless definitely assigned; the compiler accepts the pattern above because `measured` guards the use. If the compiler complains, declare the four `out` variables before the expression and use `out digit` etc.

- [x] **Step 3: Replace Paint with a blit and add the compose helpers**

Replace the whole `Paint` method and the static `DrawText` helper with:

```csharp
    private void Paint(IntPtr windowHandle)
    {
        IntPtr hdc = NativeMethods.BeginPaint(windowHandle, out NativeMethods.PAINTSTRUCT paint);
        if (backBufferDc != IntPtr.Zero)
        {
            NativeMethods.RECT r = paint.rcPaint;
            NativeMethods.BitBlt(hdc, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, backBufferDc, r.Left, r.Top, NativeMethods.SRCCOPY);
        }
        else
        {
            // No back buffer: draw straight to the window exactly as before this feature.
            ComposeBackground(hdc);
            NativeMethods.SetBkMode(hdc, NativeMethods.TRANSPARENT);
            DrawWholeString(hdc, displayed ?? CurrentText());
            DrawLabel(hdc);
        }

        NativeMethods.EndPaint(windowHandle, in paint);
    }

    /// <summary>Composes the resting widget for <paramref name="text"/> into the back buffer.</summary>
    private void RenderSeated(string text)
    {
        if (backBufferDc == IntPtr.Zero)
        {
            return;
        }

        ComposeBackground(backBufferDc);
        if (layout is { } l)
        {
            IntPtr oldFont = NativeMethods.SelectObject(backBufferDc, timeFont);
            for (int cell = 0; cell < DigitLayout.CellCount; cell++)
            {
                DrawCell(backBufferDc, l, cell, text[cell], l.CellTop, NativeMethods.TimeTextColorRef, FullCellClip(l, cell));
            }

            NativeMethods.SelectObject(backBufferDc, oldFont);
        }
        else
        {
            DrawWholeString(backBufferDc, text);
        }

        DrawLabel(backBufferDc);
    }

    private void ComposeBackground(IntPtr dc)
    {
        IntPtr oldBrush = NativeMethods.SelectObject(dc, backgroundBrush);
        IntPtr oldPen = NativeMethods.SelectObject(dc, nullPen);
        NativeMethods.Rectangle(dc, 0, 0, PositionMath.WidgetWidth + 1, PositionMath.WidgetHeight + 1);
        NativeMethods.SelectObject(dc, oldPen);
        NativeMethods.SelectObject(dc, oldBrush);
    }

    private static NativeMethods.RECT FullCellClip(DigitLayout l, int cell)
    {
        return new NativeMethods.RECT
        {
            Left = l.CellX(cell),
            Top = 0,
            Right = l.CellX(cell) + l.CellWidth,
            Bottom = PositionMath.WidgetHeight,
        };
    }

    private static NativeMethods.RECT ApertureClip(DigitLayout l, int cell)
    {
        return new NativeMethods.RECT
        {
            Left = l.CellX(cell),
            Top = l.ApertureTop,
            Right = l.CellX(cell) + l.CellWidth,
            Bottom = l.ApertureBottom,
        };
    }

    /// <summary>Draws one glyph with its top at <paramref name="y"/>, clipped to <paramref name="clip"/>. The DC keeps TA_TOP alignment.</summary>
    private static void DrawCell(IntPtr dc, DigitLayout l, int cell, char glyph, int y, int color, NativeMethods.RECT clip)
    {
        NativeMethods.SetTextColor(dc, color);
        char c = glyph;
        NativeMethods.ExtTextOut(dc, l.CellX(cell), y, NativeMethods.ETO_CLIPPED, &clip, &c, 1, IntPtr.Zero);
    }

    private void DrawWholeString(IntPtr dc, string text)
    {
        NativeMethods.RECT bounds = TimeRect;
        DrawText(dc, text, timeFont, NativeMethods.TimeTextColorRef, bounds);
    }

    private void DrawLabel(IntPtr dc)
    {
        NativeMethods.RECT bounds = LabelRect;
        DrawText(dc, "UTC", labelFont, NativeMethods.LabelTextColorRef, bounds);
    }

    private static void DrawText(IntPtr hdc, string text, IntPtr font, int colorRef, NativeMethods.RECT bounds)
    {
        IntPtr oldFont = NativeMethods.SelectObject(hdc, font);
        NativeMethods.SetTextColor(hdc, colorRef);
        NativeMethods.DrawText(
            hdc,
            text,
            text.Length,
            ref bounds,
            NativeMethods.DT_CENTER | NativeMethods.DT_VCENTER | NativeMethods.DT_SINGLELINE | NativeMethods.DT_NOPREFIX);
        NativeMethods.SelectObject(hdc, oldFont);
    }

    /// <summary>Pushes the back buffer's time region to the screen synchronously (WM_PAINT would otherwise coalesce frames).</summary>
    private void Present()
    {
        NativeMethods.RECT region = TimeRect;
        NativeMethods.InvalidateRect(hwnd, &region, false);
        NativeMethods.UpdateWindow(hwnd);
    }

    /// <summary>Shows <paramref name="text"/> with no motion.</summary>
    private void Snap(string text)
    {
        displayed = text;
        RenderSeated(text);
        Present();
    }
```

Keep `SetBkMode(TRANSPARENT)` on the back buffer set once in `CreateBackBuffer` (the direct-draw fallback sets it per paint as before).

- [x] **Step 4: Replace the timer handler**

Replace the `WM_TIMER` case with:

```csharp
            case NativeMethods.WM_TIMER:
                if ((uint)wParam == TickTimerId)
                {
                    OnTick(windowHandle);
                }

                return IntPtr.Zero;
```

and add:

```csharp
    private void OnTick(IntPtr windowHandle)
    {
        // Re-arm so the next tick lands just after the wall-clock second; SetTimer with the same id resets it.
        NativeMethods.SetTimer(windowHandle, new UIntPtr(TickTimerId), (uint)MinuteTransition.MsToNextSecond(DateTime.UtcNow.Millisecond), IntPtr.Zero);

        string text = CurrentText();
        if (!string.Equals(text, displayed, StringComparison.Ordinal))
        {
            Snap(text);
        }

        KeepAboveTaskbar(windowHandle);
    }
```

- [x] **Step 5: Tear down in the right order**

In `DestroyResources`, replace `NativeMethods.KillTimer(windowHandle, new UIntPtr(TimerId));` with:

```csharp
        NativeMethods.KillTimer(windowHandle, new UIntPtr(TickTimerId));
        NativeMethods.KillTimer(windowHandle, new UIntPtr(FrameTimerId));
        if (backBufferDc != IntPtr.Zero)
        {
            // Delete the DC first so the bitmap is no longer selected; DeleteObject on a selected bitmap fails and leaks.
            NativeMethods.DeleteDC(backBufferDc);
            backBufferDc = IntPtr.Zero;
        }

        if (backBufferBitmap != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(backBufferBitmap);
            backBufferBitmap = IntPtr.Zero;
        }
```

- [x] **Step 6: Build and verify the resting render is unchanged**

Run: `dotnet build utc-clock.csproj -c Debug`
Expected: 0 warnings.

Then capture the widget before and after. Before: run the previous build (`git stash` is not allowed to touch the owner's tree; instead use the last published `publish\utc-clock.exe` from the current main) and after: `dotnet run --project utc-clock.csproj`. Capture each with this PowerShell snippet, which finds the widget window by class and grabs its rectangle from the screen:

```powershell
Add-Type -AssemblyName System.Drawing
$sig = '[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string t); [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); public struct RECT { public int L, T, R, B; }'
$u = Add-Type -MemberDefinition $sig -Name U32 -Namespace Probe -PassThru
$h = $u::FindWindowW("UtcClockWidgetNativeWindow", $null); $r = New-Object Probe.RECT; [void]$u::GetWindowRect($h, [ref]$r)
$bmp = New-Object System.Drawing.Bitmap 194, 48; $g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size); $bmp.Save("$env:TEMP\widget-after.png"); $g.Dispose(); $bmp.Dispose()
```

Compare the two PNGs pixel by pixel (same snippet with `widget-before.png`, then a loop over `GetPixel`). Expected: 0 differing pixels while both show the same minute. Because the layered alpha blends the desktop through, take both captures at the same screen position over the same background.

- [x] **Step 7: Leave the changes staged for the owner to review**

```bash
git add WidgetWindow.cs
```

---

### Task 6: The roll, its pacing, the reduced-motion gates and the menu toggle

**Files:**
- Modify: `WidgetWindow.cs`

**Interfaces:**
- Consumes: `MinuteRoll`, `MinuteTransition.MotionEnabled`, `MinuteTransition.IsOneMinuteStep`, `PositionStore.LoadAnimateSetting`, `PositionStore.SaveAnimateSetting`, Task 5 helpers.
- Produces: fields `roll`, `rollStart`, `lastFrameTimestamp`, `shortFrames`, `frameTimerFallback`, `animatePreference`; methods `CanRoll(string text)`, `ReadSystemAnimations()`, `StartRoll(string text)`, `OnFrame()`, `RenderRoll(double elapsedMs)`, `FinishRoll()`, `CancelRoll()`.

- [x] **Step 1: Add fields**

After `private string? displayed;` add:

```csharp
    private MinuteRoll? roll;
    private long rollStart;
    private long lastFrameTimestamp;
    private int shortFrames;
    private bool frameTimerFallback;
    private bool? animatePreference;
```

In the constructor, before `RegisterWindowClass();`, add:

```csharp
        animatePreference = PositionStore.LoadAnimateSetting();
```

- [x] **Step 2: Route the new messages**

In `HandleMessage`, replace the `WM_TIMER` case with:

```csharp
            case NativeMethods.WM_TIMER:
                if ((uint)wParam == TickTimerId)
                {
                    OnTick(windowHandle);
                }
                else if ((uint)wParam == FrameTimerId)
                {
                    OnFrame(windowHandle);
                }

                return IntPtr.Zero;
            case FrameMessage:
                OnFrame(windowHandle);
                return IntPtr.Zero;
            case NativeMethods.WM_SETTINGCHANGE:
                // Nothing to cache: the flags are re-read at every boundary. Handled so a future cache has a hook.
                break;
```

(`FrameMessage` is a `const uint`, so it is a valid case label alongside the `int` constants after the `switch (message)` on a `uint`; the existing constants are `int` and convert implicitly. If the compiler rejects mixing, change `case FrameMessage:` to `case (uint)FrameMessage:` or declare the constant as `int`.)

- [x] **Step 3: Decide snap versus roll in the tick**

Replace the body of `OnTick` with:

```csharp
    private void OnTick(IntPtr windowHandle)
    {
        NativeMethods.SetTimer(windowHandle, new UIntPtr(TickTimerId), (uint)MinuteTransition.MsToNextSecond(DateTime.UtcNow.Millisecond), IntPtr.Zero);

        string text = CurrentText();
        string? shown = roll?.To ?? displayed;
        if (!string.Equals(text, shown, StringComparison.Ordinal))
        {
            if (roll is not null)
            {
                // A new boundary arrived mid-roll: the clock stalled or jumped, so do not animate.
                CancelRoll(windowHandle);
                Snap(text);
            }
            else if (CanRoll(text))
            {
                StartRoll(windowHandle, text);
            }
            else
            {
                Snap(text);
            }
        }

        KeepAboveTaskbar(windowHandle);
    }

    private bool CanRoll(string text)
    {
        return layout.HasValue
            && MinuteTransition.MotionEnabled(animatePreference, ReadSystemAnimations())
            && NativeMethods.GetSystemMetrics(NativeMethods.SM_REMOTESESSION) == 0
            && MinuteTransition.IsOneMinuteStep(displayed, text);
    }

    /// <summary>Live read of the Windows animation switches. Not cached: a runtime setter may broadcast no change.</summary>
    private static bool ReadSystemAnimations()
    {
        return NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETCLIENTAREAANIMATION, 0, out int clientArea, 0)
            && clientArea != 0
            && NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETUIEFFECTS, 0, out int uiEffects, 0)
            && uiEffects != 0;
    }
```

- [x] **Step 4: Add the roll lifecycle and frame loop**

Add after `CanRoll`:

```csharp
    private void StartRoll(IntPtr windowHandle, string text)
    {
        roll = new MinuteRoll(displayed!, text);
        rollStart = Stopwatch.GetTimestamp();
        lastFrameTimestamp = rollStart;
        shortFrames = 0;
        frameTimerFallback = false;
        RenderRoll(0);
        Present();
        NativeMethods.PostMessage(windowHandle, FrameMessage, UIntPtr.Zero, IntPtr.Zero);
    }

    private void OnFrame(IntPtr windowHandle)
    {
        if (roll is null)
        {
            return;
        }

        if (!frameTimerFallback)
        {
            // Block until the next compose so each frame lands in its own refresh. Fall back to the
            // USER timer if composition refuses or DwmFlush stops blocking.
            int result = NativeMethods.DwmFlush();
            long now = Stopwatch.GetTimestamp();
            bool tooFast = Stopwatch.GetElapsedTime(lastFrameTimestamp, now).TotalMilliseconds < 4;
            shortFrames = tooFast ? shortFrames + 1 : 0;
            lastFrameTimestamp = now;
            if (result < 0 || shortFrames >= 2)
            {
                frameTimerFallback = true;
                NativeMethods.SetTimer(windowHandle, new UIntPtr(FrameTimerId), NativeMethods.USER_TIMER_MINIMUM, IntPtr.Zero);
            }
        }

        double elapsed = Stopwatch.GetElapsedTime(rollStart).TotalMilliseconds;
        if (roll.IsComplete(elapsed))
        {
            FinishRoll(windowHandle);
            return;
        }

        RenderRoll(elapsed);
        Present();
        if (!frameTimerFallback)
        {
            NativeMethods.PostMessage(windowHandle, FrameMessage, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>Composes one frame of the roll into the back buffer.</summary>
    private void RenderRoll(double elapsedMs)
    {
        if (roll is null || layout is not { } l || backBufferDc == IntPtr.Zero)
        {
            return;
        }

        ComposeBackground(backBufferDc);
        IntPtr oldFont = NativeMethods.SelectObject(backBufferDc, timeFont);
        for (int cell = 0; cell < DigitLayout.CellCount; cell++)
        {
            CellFrame frame = roll.FrameAt(cell, elapsedMs, l.Travel, NativeMethods.TimeTextColorRef, NativeMethods.SurfaceColorRef);
            if (!frame.Moving)
            {
                DrawCell(backBufferDc, l, cell, roll.To[cell], l.CellTop, NativeMethods.TimeTextColorRef, FullCellClip(l, cell));
                continue;
            }

            NativeMethods.RECT aperture = ApertureClip(l, cell);
            int oldY = l.CellTop - frame.Dy;
            int newY = l.CellTop + l.Travel - frame.Dy;
            if (frame.DrawOldFirst)
            {
                DrawCell(backBufferDc, l, cell, roll.From[cell], oldY, frame.OldColor, aperture);
                DrawCell(backBufferDc, l, cell, roll.To[cell], newY, frame.NewColor, aperture);
            }
            else
            {
                DrawCell(backBufferDc, l, cell, roll.To[cell], newY, frame.NewColor, aperture);
                DrawCell(backBufferDc, l, cell, roll.From[cell], oldY, frame.OldColor, aperture);
            }
        }

        NativeMethods.SelectObject(backBufferDc, oldFont);
        DrawLabel(backBufferDc);
    }

    private void FinishRoll(IntPtr windowHandle)
    {
        string target = roll!.To;
        CancelRoll(windowHandle);
        Snap(target);
    }

    private void CancelRoll(IntPtr windowHandle)
    {
        roll = null;
        if (frameTimerFallback)
        {
            NativeMethods.KillTimer(windowHandle, new UIntPtr(FrameTimerId));
            frameTimerFallback = false;
        }
    }
```

Add `using System.Diagnostics;` at the top of the file.

- [x] **Step 5: Add the menu toggle**

Replace `ShowContextMenu` with:

```csharp
    private void ShowContextMenu(IntPtr windowHandle)
    {
        NativeMethods.POINT point = CursorPosition();
        bool systemAnimations = ReadSystemAnimations();
        bool animate = MinuteTransition.MotionEnabled(animatePreference, systemAnimations);
        string animateLabel = animatePreference is null
            ? $"Animate minute change (following Windows: {(systemAnimations ? "on" : "off")})"
            : "Animate minute change";

        IntPtr menu = NativeMethods.CreatePopupMenu();
        NativeMethods.AppendMenu(menu, (uint)(NativeMethods.MF_STRING | (animate ? NativeMethods.MF_CHECKED : 0)), new UIntPtr(AnimateMenuId), animateLabel);
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new UIntPtr(ResetMenuId), "Reset Position");
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new UIntPtr(ExitMenuId), "Exit");
        NativeMethods.SetForegroundWindow(windowHandle);

        uint command = NativeMethods.TrackPopupMenu(
            menu,
            NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD,
            point.X,
            point.Y,
            0,
            windowHandle,
            IntPtr.Zero);
        NativeMethods.DestroyMenu(menu);

        if (command == AnimateMenuId)
        {
            animatePreference = !animate;
            PositionStore.SaveAnimateSetting(animatePreference);
        }
        else if (command == ResetMenuId)
        {
            ResetPosition(windowHandle);
        }
        else if (command == ExitMenuId)
        {
            NativeMethods.DestroyWindow(windowHandle);
        }
    }
```

`AppendMenu`'s `uFlags` parameter is `uint`; `MF_STRING` and `MF_CHECKED` are `int` constants, hence the cast.

- [x] **Step 6: Build, run, and watch one carry and one hour change**

Run: `dotnet build utc-clock.csproj -c Debug` then `dotnet run --project utc-clock.csproj`.
Expected: 0 warnings. Right-click shows "Animate minute change (following Windows: off)" unchecked on this machine. Tick it; the label loses its suffix and gains a check. At the next xx:x9 -> xx:(x+1)0 both minute digits roll, the ones digit first. To see an hour change without waiting, temporarily test with the system clock is not acceptable; instead rely on the unit-tested schedule and watch the next natural hour change. Untick the item: the next change snaps on the second boundary.

- [x] **Step 7: Leave the changes staged for the owner to review**

```bash
git add WidgetWindow.cs
```

---

### Task 7: Documentation and release verification

**Files:**
- Modify: `RELEASE_NOTES.md`
- Modify: `README.md`
- Modify: `docs/native-standalone.md` (one line in the behavior list)

- [x] **Step 1: Release notes**

Under the existing `## 2026-09-03` heading, above `### Design`, add:

```markdown
### Added

- **Animated minute change.** Digits that change at a minute boundary now roll upward through a clipped 28 px aperture, dimming toward the surface as they leave and brightening as they seat, 280 ms per digit with a 40 ms right-to-left carry lag (so 12:59 -> 13:00 takes 360 ms and 23:59 -> 00:00 takes 400 ms). Only the changing digits move; the colon, unchanged digits and the `UTC` label stay put. At rest the widget renders pixel-for-pixel as before.
- **Motion follows Windows by default.** The roll honours Settings > Accessibility > Visual effects > Animation effects. A new right-click item, `Animate minute change`, overrides it in either direction and is remembered in `position.json`; while no choice is stored the label shows which source is in charge, for example `Animate minute change (following Windows: off)`. Windows currently reports client-area animations disabled on the development machine, so the first roll there needs that item ticked once.
- **Minute changes land on the second.** The one-second timer now re-arms itself to the wall-clock second, so a change shows within about 25 ms of the boundary instead of up to a second late.

### Changed

- Painting goes through a memory-DC back buffer; `WM_PAINT` is a single `BitBlt`. Each character is drawn with `ExtTextOutW` into a fixed 21 px cell derived from the measured font metrics at startup. If the time font is not monospaced (Cascadia Mono absent), the widget keeps the whole-string path and snaps.
- Frames are paced by `DwmFlush` from a posted-message loop, falling back to a 10 ms `SetTimer` if composition refuses. Any change that is not exactly one minute forward (launch, resume, clock correction, remote session) snaps without motion.
- `PositionStore.Save` now preserves the persisted animate flag; `--reset` leaves it untouched.
```

- [x] **Step 2: README**

In the Features list add after the `HH:mm` bullet:

```markdown
- Animates the digits that change at each minute boundary with a damped odometer roll (follows the Windows "Animation effects" setting; right-click to override)
```

In the Usage section add:

```markdown
Right-click the widget for `Animate minute change`, `Reset Position` and `Exit`.
```

Update the Project Structure bullet for `Services/` to: ``- `Services/` - launch option parsing, startup registration, saved-position logic, digit cell layout and the minute-roll timing math``.

- [x] **Step 3: Native build notes**

In `docs/native-standalone.md`, in the list under "The native version still keeps the important user behavior", add:

```markdown
- animated minute change drawn with GDI into a back buffer (see `RELEASE_NOTES.md`, 2026-09-03)
```

- [x] **Step 4: Full verification**

Run, in order:

```powershell
dotnet run --project UtcClockWidget.Tests\UtcClockWidget.Tests.csproj
dotnet build utc-clock.csproj -c Debug
dotnet publish utc-clock.csproj -c Release -o .\publish
```

Expected: all tests `PASS`; both builds succeed; the publish output shows no `IL` or `AOT` analyzer warnings and produces `publish\utc-clock.exe`. Start the published exe once to confirm it runs and the menu item appears.

- [x] **Step 5: Leave the changes staged for the owner to review**

```bash
git add RELEASE_NOTES.md README.md docs/native-standalone.md
git status
```

Report the staged file list to the owner; the owner commits.

---

## Execution record (2026-09-03)

All tasks were executed inline in one session, test-first, with the following deviations from the text above:

- **Task 5, resting-frame check.** The compare script's first run was invalid (PowerShell's case-insensitive typed parameter turned the bitmaps into strings). The corrected run found 128 to 153 ClearType fringe pixels out of 9,312 differing by exactly 1/255 in blue. Whole-string and per-cell drawing into the back buffer are identical, and a DIB section behaves the same, so the shift comes from rendering into a memory bitmap instead of the window surface. Accepted and recorded in the spec and release notes.
- **Task 6, frame pacing.** The adversarial review confirmed that a self-posted `WM_APP` loop starves mouse input and `WM_TIMER` for the length of a roll (posted messages outrank input), and that the 4 ms short-frame heuristic misfires on high-refresh displays. Replaced by an idle-time loop in `Run` (`PeekMessage` drain, then `DwmFlush`), a 0.5 ms block-time guard with three strikes, and a watchdog in `OnTick`. `PostMessage`, `WM_APP` and `WM_SETTINGCHANGE` declarations were dropped; `PeekMessage`, `PM_REMOVE` and `WM_QUIT` were added.
- **Task 6, menu toggle.** `SaveAnimateSetting` now takes the live window position so a missing settings file never records (0, 0); `PositionDto.WithAnimateMinuteChange` was removed.
- **Task 6, WM_SETTINGCHANGE.** The planned no-op case was not added: it is behaviourally identical to falling through to `DefWindowProc`, and the flags are read live at every boundary.
- **Task 7, publish.** `dotnet publish` needs `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer` on PATH for `vswhere.exe`, and the output went to a scratch folder because `publish\utc-clock.exe` was the owner's running widget. Result: 2,493,952-byte `utc-clock.exe`, zero IL/AOT warnings, smoke-run for 2.7 s with the Run-key value backed up and restored.
- **Tests.** The review added pinned color oracles (`0x005D5B5B` at k = 0.7, `0x00ABAAAA` at the dy = 14 tie, `0x007F7D7D` / `0x00D8D8D8` at dy = 22), the last moving frame, out-of-range time strings, the lower timer clamp, and `MinuteRoll` input validation. `MinuteRoll.IsChanging` (test-only API) was removed.
- **Commits.** Nothing was committed; the owner commits.
