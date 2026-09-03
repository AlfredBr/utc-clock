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
    private static readonly int[] DigitIndexes = [0, 1, 3, 4];

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

    /// <summary>Which of the five cells differ between two validated "HH:mm" strings. The colon cell is never marked.</summary>
    public static bool[] ChangedCells(string from, string to)
    {
        var changed = new bool[DigitLayout.CellCount];
        for (int i = 0; i < DigitLayout.CellCount; i++)
        {
            changed[i] = i != DigitLayout.ColonIndex && from[i] != to[i];
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

        foreach (int index in DigitIndexes)
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
        // Ease clamps, so a cell still waiting on its stagger delay sits at 0 and a finished one at travel.
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
        if (from.Length != DigitLayout.CellCount || to.Length != DigitLayout.CellCount)
        {
            throw new ArgumentException("Both times must be HH:mm strings.");
        }

        From = from;
        To = to;
        changed = MinuteTransition.ChangedCells(from, to);
        delays = MinuteTransition.StaggerDelays(changed);
        TotalMs = MinuteTransition.TotalDurationMs(changed.Count(cell => cell));
    }

    public string From { get; }

    public string To { get; }

    public int TotalMs { get; }

    public CellFrame FrameAt(int cell, double elapsedMs, int travel, int foreground, int background)
    {
        return changed[cell]
            ? MinuteTransition.FrameAt(elapsedMs, delays[cell], travel, foreground, background)
            : CellFrame.Seated;
    }

    public bool IsComplete(double elapsedMs) => elapsedMs >= TotalMs;
}
