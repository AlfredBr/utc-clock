using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using utc_clock.Native;
using utc_clock.Services;

namespace utc_clock;

internal sealed unsafe class WidgetWindow
{
    private const string WindowClassName = "UtcClockWidgetNativeWindow";
    private const uint TickTimerId = 1;
    private const uint FrameTimerId = 2;
    private const uint ResetMenuId = 100;
    private const uint ExitMenuId = 101;
    private const uint AnimateMenuId = 102;

    /// <summary>A DwmFlush that returns faster than this did not wait for a compose; three in a row means composition is not pacing us.</summary>
    private const double MinDwmFlushBlockMs = 0.5;

    private static readonly IntPtr HwndTopMost = new(-1);
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
    private static readonly NativeMethods.RECT WholeWidget = new()
    {
        Left = 0,
        Top = 0,
        Right = PositionMath.WidgetWidth,
        Bottom = PositionMath.WidgetHeight,
    };
    private static WidgetWindow? current;

    private readonly IntPtr backgroundBrush;
    private readonly IntPtr nullPen;
    private readonly IntPtr timeFont;
    private readonly IntPtr labelFont;
    private readonly IntPtr iconLarge;
    private readonly IntPtr iconSmall;
    private IntPtr hwnd;
    private bool dragging;
    private NativeMethods.POINT dragStartPointer;
    private NativeMethods.RECT dragStartWindow;
    private IntPtr backBufferDc;
    private IntPtr backBufferBitmap;
    private DigitLayout? layout;
    private string? displayed;
    private MinuteRoll? roll;
    private long rollStart;
    private int nonBlockingFlushes;
    private bool frameTimerFallback;
    private bool? animatePreference;

    public WidgetWindow(bool resetRequested)
    {
        current = this;
        animatePreference = PositionStore.LoadAnimateSetting();
        backgroundBrush = NativeMethods.CreateSolidBrush(NativeMethods.SurfaceColorRef);
        nullPen = NativeMethods.CreatePen(NativeMethods.PS_NULL, 0, 0);
        timeFont = NativeMethods.CreateFont(
            -34,
            0,
            0,
            0,
            NativeMethods.FW_SEMIBOLD,
            0,
            0,
            0,
            NativeMethods.DEFAULT_CHARSET,
            NativeMethods.OUT_DEFAULT_PRECIS,
            NativeMethods.CLIP_DEFAULT_PRECIS,
            NativeMethods.CLEARTYPE_QUALITY,
            NativeMethods.DEFAULT_PITCH | NativeMethods.FF_DONTCARE,
            "Cascadia Mono");
        labelFont = NativeMethods.CreateFont(
            -20,
            0,
            0,
            0,
            NativeMethods.FW_SEMIBOLD,
            0,
            0,
            0,
            NativeMethods.DEFAULT_CHARSET,
            NativeMethods.OUT_DEFAULT_PRECIS,
            NativeMethods.CLIP_DEFAULT_PRECIS,
            NativeMethods.CLEARTYPE_QUALITY,
            NativeMethods.DEFAULT_PITCH | NativeMethods.FF_DONTCARE,
            "Segoe UI Variable Display");

        IntPtr instance = NativeMethods.GetModuleHandle(null);
        IntPtr iconName = new(NativeMethods.AppIconResourceId);
        iconLarge = NativeMethods.LoadImage(
            instance,
            iconName,
            NativeMethods.IMAGE_ICON,
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CXICON),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CYICON),
            NativeMethods.LR_DEFAULTCOLOR);
        iconSmall = NativeMethods.LoadImage(
            instance,
            iconName,
            NativeMethods.IMAGE_ICON,
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSMICON),
            NativeMethods.LR_DEFAULTCOLOR);

        RegisterWindowClass();
        CreateWindow(resetRequested);
    }

    public int Run()
    {
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        NativeMethods.UpdateWindow(hwnd);

        while (true)
        {
            if (roll is not null && !frameTimerFallback)
            {
                // While a roll is live, pace frames from idle time: drain everything pending (input
                // included, so a drag never freezes), then block in DwmFlush for exactly one compose.
                while (NativeMethods.PeekMessage(out NativeMethods.MSG pending, IntPtr.Zero, 0, 0, NativeMethods.PM_REMOVE))
                {
                    if (pending.message == NativeMethods.WM_QUIT)
                    {
                        return 0;
                    }

                    NativeMethods.TranslateMessage(in pending);
                    NativeMethods.DispatchMessage(in pending);
                }

                if (roll is not null && !frameTimerFallback)
                {
                    OnFrame(hwnd);
                }

                continue;
            }

            if (!NativeMethods.GetMessage(out NativeMethods.MSG message, IntPtr.Zero, 0, 0))
            {
                return 0;
            }

            NativeMethods.TranslateMessage(in message);
            NativeMethods.DispatchMessage(in message);
        }
    }

    private static NativeMethods.POINT CursorPosition()
    {
        return NativeMethods.GetCursorPos(out NativeMethods.POINT point)
            ? point
            : new NativeMethods.POINT();
    }

    private static string CurrentText()
    {
        return DateTime.UtcNow.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private void RegisterWindowClass()
    {
        IntPtr instance = NativeMethods.GetModuleHandle(null);
        var windowClass = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = &WndProc,
            hInstance = instance,
            hIcon = iconLarge,
            hCursor = NativeMethods.LoadCursor(IntPtr.Zero, new IntPtr(NativeMethods.IDC_ARROW)),
            hbrBackground = IntPtr.Zero,
            lpszClassName = WindowClassName,
            hIconSm = iconSmall,
        };

        ushort atom = NativeMethods.RegisterClassEx(ref windowClass);
        if (atom == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register window class.");
        }
    }

    private void CreateWindow(bool resetRequested)
    {
        (int X, int Y) position = resetRequested
            ? PositionStore.DefaultPosition()
            : PositionStore.Load() is { } savedPosition
                ? PositionStore.ClampToVirtualScreen(savedPosition.X, savedPosition.Y)
                : PositionStore.DefaultPosition();

        IntPtr instance = NativeMethods.GetModuleHandle(null);
        hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_LAYERED,
            WindowClassName,
            "UTC",
            NativeMethods.WS_POPUP,
            position.X,
            position.Y,
            PositionMath.WidgetWidth,
            PositionMath.WidgetHeight,
            IntPtr.Zero,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create widget window.");
        }

        // The buffer must hold a rendered frame before the first WM_PAINT, which is now a plain blit.
        CreateBackBuffer();
        MeasureLayout();
        Snap(CurrentText());

        NativeMethods.SetLayeredWindowAttributes(hwnd, 0, NativeMethods.WidgetAlpha, NativeMethods.LWA_ALPHA);
        NativeMethods.SetWindowPos(
            hwnd,
            HwndTopMost,
            position.X,
            position.Y,
            PositionMath.WidgetWidth,
            PositionMath.WidgetHeight,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        ArmTickTimer(hwnd);
        NativeMethods.SendMessage(hwnd, NativeMethods.WM_SETICON, new UIntPtr(NativeMethods.ICON_BIG), iconLarge);
        NativeMethods.SendMessage(hwnd, NativeMethods.WM_SETICON, new UIntPtr(NativeMethods.ICON_SMALL), iconSmall);
        PositionStore.Save(position.X, position.Y);
    }

    /// <summary>Arms the one-second tick to fire just after the next wall-clock second. SetTimer with the same id resets it.</summary>
    private static void ArmTickTimer(IntPtr windowHandle)
    {
        NativeMethods.SetTimer(windowHandle, new UIntPtr(TickTimerId), (uint)MinuteTransition.MsToNextSecond(DateTime.UtcNow.Millisecond), IntPtr.Zero);
    }

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
                if (dc != IntPtr.Zero)
                {
                    NativeMethods.DeleteDC(dc);
                }

                if (bitmap != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(bitmap);
                }

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

    /// <summary>
    /// Measures the time font once so each character can be drawn in its own cell on exactly the pixels
    /// DrawText would use. A font that is not monospaced leaves <see cref="layout"/> null and keeps the
    /// whole-string path.
    /// </summary>
    private void MeasureLayout()
    {
        layout = null;
        if (backBufferDc == IntPtr.Zero)
        {
            return;
        }

        IntPtr oldFont = NativeMethods.SelectObject(backBufferDc, timeFont);
        bool measured = NativeMethods.GetTextExtentPoint32(backBufferDc, "0", 1, out NativeMethods.SIZE digit);
        measured &= NativeMethods.GetTextExtentPoint32(backBufferDc, ":", 1, out NativeMethods.SIZE colon);
        measured &= NativeMethods.GetTextExtentPoint32(backBufferDc, "00:00", 5, out NativeMethods.SIZE text);
        measured &= NativeMethods.GetTextMetrics(backBufferDc, out NativeMethods.TEXTMETRICW metrics);
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

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static IntPtr WndProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam)
    {
        return current?.HandleMessage(hwnd, message, wParam, lParam)
            ?? NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private IntPtr HandleMessage(IntPtr windowHandle, uint message, UIntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case NativeMethods.WM_PAINT:
                Paint(windowHandle);
                return IntPtr.Zero;
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
            case NativeMethods.WM_LBUTTONDOWN:
                BeginDrag(windowHandle);
                return IntPtr.Zero;
            case NativeMethods.WM_MOUSEMOVE:
                if (dragging && ((int)wParam & NativeMethods.MK_LBUTTON) == NativeMethods.MK_LBUTTON)
                {
                    ContinueDrag(windowHandle);
                    return IntPtr.Zero;
                }
                break;
            case NativeMethods.WM_LBUTTONUP:
                EndDrag(windowHandle);
                return IntPtr.Zero;
            case NativeMethods.WM_RBUTTONUP:
                ShowContextMenu(windowHandle);
                return IntPtr.Zero;
            case NativeMethods.WM_DESTROY:
                DestroyResources(windowHandle);
                NativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(windowHandle, message, wParam, lParam);
    }

    private void OnTick(IntPtr windowHandle)
    {
        ArmTickTimer(windowHandle);

        // Watchdog: a roll whose frame source stopped would otherwise sit mid-glyph until the next
        // boundary. Seat it once its own schedule has elapsed.
        if (roll is not null && roll.IsComplete(Stopwatch.GetElapsedTime(rollStart).TotalMilliseconds))
        {
            FinishRoll(windowHandle);
        }

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
                StartRoll(text);
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

    private void StartRoll(string text)
    {
        roll = new MinuteRoll(displayed!, text);
        rollStart = Stopwatch.GetTimestamp();
        nonBlockingFlushes = 0;
        frameTimerFallback = false;
        RenderRoll(0);
        Present();
        // The message loop in Run sees the live roll and paces the following frames.
    }

    /// <summary>Advances the roll by one frame. Called from idle time in <see cref="Run"/>, or from the frame timer once DwmFlush has been given up on.</summary>
    private void OnFrame(IntPtr windowHandle)
    {
        if (roll is null)
        {
            return;
        }

        if (!frameTimerFallback)
        {
            // Block until the next compose so each frame lands in its own refresh. If composition
            // refuses, or DwmFlush keeps returning without waiting, pace with the USER timer instead
            // rather than spinning.
            long before = Stopwatch.GetTimestamp();
            int result = NativeMethods.DwmFlush();
            bool blocked = Stopwatch.GetElapsedTime(before).TotalMilliseconds >= MinDwmFlushBlockMs;
            nonBlockingFlushes = blocked ? 0 : nonBlockingFlushes + 1;
            if (result < 0 || nonBlockingFlushes >= 3)
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
            ComposeBackground(hdc, WholeWidget);
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

        ComposeBackground(backBufferDc, WholeWidget);
        if (layout is { } cells)
        {
            IntPtr oldFont = NativeMethods.SelectObject(backBufferDc, timeFont);
            for (int cell = 0; cell < DigitLayout.CellCount; cell++)
            {
                DrawSeatedCell(cells, cell, text[cell]);
            }

            NativeMethods.SelectObject(backBufferDc, oldFont);
        }
        else
        {
            DrawWholeString(backBufferDc, text);
        }

        DrawLabel(backBufferDc);
    }

    /// <summary>Composes one frame of the roll into the back buffer. Only the time region changes, so only it is repainted.</summary>
    private void RenderRoll(double elapsedMs)
    {
        if (roll is null || layout is not { } cells || backBufferDc == IntPtr.Zero)
        {
            return;
        }

        ComposeBackground(backBufferDc, TimeRect);
        IntPtr oldFont = NativeMethods.SelectObject(backBufferDc, timeFont);
        for (int cell = 0; cell < DigitLayout.CellCount; cell++)
        {
            CellFrame frame = roll.FrameAt(cell, elapsedMs, cells.Travel, NativeMethods.TimeTextColorRef, NativeMethods.SurfaceColorRef);
            if (!frame.Moving)
            {
                DrawSeatedCell(cells, cell, roll.To[cell]);
                continue;
            }

            NativeMethods.RECT aperture = ApertureClip(cells, cell);
            int oldY = cells.CellTop - frame.Dy;
            int newY = cells.CellTop + cells.Travel - frame.Dy;
            if (frame.DrawOldFirst)
            {
                DrawCell(backBufferDc, cells, cell, roll.From[cell], oldY, frame.OldColor, aperture);
                DrawCell(backBufferDc, cells, cell, roll.To[cell], newY, frame.NewColor, aperture);
            }
            else
            {
                DrawCell(backBufferDc, cells, cell, roll.To[cell], newY, frame.NewColor, aperture);
                DrawCell(backBufferDc, cells, cell, roll.From[cell], oldY, frame.OldColor, aperture);
            }
        }

        NativeMethods.SelectObject(backBufferDc, oldFont);
    }

    private void ComposeBackground(IntPtr dc, NativeMethods.RECT area)
    {
        IntPtr oldBrush = NativeMethods.SelectObject(dc, backgroundBrush);
        IntPtr oldPen = NativeMethods.SelectObject(dc, nullPen);
        NativeMethods.Rectangle(dc, area.Left, area.Top, area.Right + 1, area.Bottom + 1);
        NativeMethods.SelectObject(dc, oldPen);
        NativeMethods.SelectObject(dc, oldBrush);
    }

    /// <summary>A glyph at rest in full color, clipped to its whole cell (a visual no-op that keeps one code path).</summary>
    private void DrawSeatedCell(DigitLayout cells, int cell, char glyph)
    {
        DrawCell(backBufferDc, cells, cell, glyph, cells.CellTop, NativeMethods.TimeTextColorRef, FullCellClip(cells, cell));
    }

    private static NativeMethods.RECT FullCellClip(DigitLayout cells, int cell)
    {
        return new NativeMethods.RECT
        {
            Left = cells.CellX(cell),
            Top = 0,
            Right = cells.CellX(cell) + cells.CellWidth,
            Bottom = PositionMath.WidgetHeight,
        };
    }

    private static NativeMethods.RECT ApertureClip(DigitLayout cells, int cell)
    {
        return new NativeMethods.RECT
        {
            Left = cells.CellX(cell),
            Top = cells.ApertureTop,
            Right = cells.CellX(cell) + cells.CellWidth,
            Bottom = cells.ApertureBottom,
        };
    }

    /// <summary>Draws one glyph with its top at <paramref name="y"/>, clipped to <paramref name="clip"/>. The DC keeps TA_TOP alignment.</summary>
    private static void DrawCell(IntPtr dc, DigitLayout cells, int cell, char glyph, int y, int color, NativeMethods.RECT clip)
    {
        NativeMethods.SetTextColor(dc, color);
        char c = glyph;
        NativeMethods.ExtTextOut(dc, cells.CellX(cell), y, NativeMethods.ETO_CLIPPED, &clip, &c, 1, IntPtr.Zero);
    }

    private void DrawWholeString(IntPtr dc, string text)
    {
        DrawText(dc, text, timeFont, NativeMethods.TimeTextColorRef, TimeRect);
    }

    private void DrawLabel(IntPtr dc)
    {
        DrawText(dc, "UTC", labelFont, NativeMethods.LabelTextColorRef, LabelRect);
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

    /// <summary>Pushes the back buffer's time region to the screen synchronously (WM_PAINT alone would coalesce frames).</summary>
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

    private void BeginDrag(IntPtr windowHandle)
    {
        if (!NativeMethods.GetCursorPos(out dragStartPointer) || !NativeMethods.GetWindowRect(windowHandle, out dragStartWindow))
        {
            return;
        }

        dragging = true;
        NativeMethods.SetCapture(windowHandle);
    }

    private void ContinueDrag(IntPtr windowHandle)
    {
        NativeMethods.POINT currentPointer = CursorPosition();
        int dx = currentPointer.X - dragStartPointer.X;
        int dy = currentPointer.Y - dragStartPointer.Y;
        NativeMethods.SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            dragStartWindow.Left + dx,
            dragStartWindow.Top + dy,
            0,
            0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private void EndDrag(IntPtr windowHandle)
    {
        if (!dragging)
        {
            return;
        }

        dragging = false;
        NativeMethods.ReleaseCapture();
        if (NativeMethods.GetWindowRect(windowHandle, out NativeMethods.RECT rect))
        {
            PositionStore.Save(rect.Left, rect.Top);
        }
    }

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
            // Persist with the live window position so a missing settings file never records (0, 0).
            (int X, int Y) position = NativeMethods.GetWindowRect(windowHandle, out NativeMethods.RECT rect)
                ? (rect.Left, rect.Top)
                : PositionStore.DefaultPosition();
            PositionStore.SaveAnimateSetting(animatePreference, position.X, position.Y);
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

    private static void ResetPosition(IntPtr windowHandle)
    {
        (int X, int Y) position = PositionStore.DefaultPosition();
        NativeMethods.SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            position.X,
            position.Y,
            0,
            0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        PositionStore.Save(position.X, position.Y);
    }

    /// <summary>
    /// The taskbar is topmost too, and Explorer raises it above every other topmost window each
    /// time it is shown. When the widget is parked in a taskbar strip, put the widget back on top.
    /// Nothing happens for a widget that does not overlap a taskbar, so other always-on-top
    /// windows are never fought with.
    /// </summary>
    private static void KeepAboveTaskbar(IntPtr windowHandle)
    {
        if (!NativeMethods.GetWindowRect(windowHandle, out NativeMethods.RECT widgetRect))
        {
            return;
        }

        ScreenBounds widget = ToBounds(widgetRect);
        foreach (IntPtr taskbar in TaskbarWindows())
        {
            if (NativeMethods.GetWindowRect(taskbar, out NativeMethods.RECT taskbarRect)
                && PositionMath.Overlaps(widget, ToBounds(taskbarRect))
                && IsAbove(taskbar, windowHandle))
            {
                NativeMethods.SetWindowPos(
                    windowHandle,
                    HwndTopMost,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                return;
            }
        }
    }

    private static List<IntPtr> TaskbarWindows()
    {
        var taskbars = new List<IntPtr>(2);
        IntPtr primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero)
        {
            taskbars.Add(primary);
        }

        IntPtr secondary = IntPtr.Zero;
        while ((secondary = NativeMethods.FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            taskbars.Add(secondary);
        }

        return taskbars;
    }

    private static bool IsAbove(IntPtr candidate, IntPtr windowHandle)
    {
        // Walk from the widget toward the top of the z-order; only windows above it are visited.
        IntPtr above = NativeMethods.GetWindow(windowHandle, NativeMethods.GW_HWNDPREV);
        for (int guard = 0; above != IntPtr.Zero && guard < 1024; guard++)
        {
            if (above == candidate)
            {
                return true;
            }

            above = NativeMethods.GetWindow(above, NativeMethods.GW_HWNDPREV);
        }

        return false;
    }

    private static ScreenBounds ToBounds(NativeMethods.RECT rect)
    {
        return new ScreenBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private void DestroyResources(IntPtr windowHandle)
    {
        roll = null;
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

        NativeMethods.DeleteObject(backgroundBrush);
        NativeMethods.DeleteObject(nullPen);
        NativeMethods.DeleteObject(timeFont);
        NativeMethods.DeleteObject(labelFont);
        if (iconLarge != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(iconLarge);
        }

        if (iconSmall != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(iconSmall);
        }
    }
}
