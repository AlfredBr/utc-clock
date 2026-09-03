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

    /// <summary>
    /// Rows the aperture reserves for digit ink above the baseline. Measured for Cascadia Mono at
    /// height -34 (every digit inks rows 14..37 of the widget); any substituted monospaced face is
    /// assumed to keep its digit ink inside baseline - 24 .. baseline.
    /// </summary>
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
