using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using utc_clock.Native;
using utc_clock.Services;

namespace utc_clock;

internal sealed unsafe class WidgetWindow
{
    private const string WindowClassName = "UtcClockWidgetNativeWindow";
    private const uint TimerId = 1;
    private const uint ResetMenuId = 100;
    private const uint ExitMenuId = 101;
    private static readonly IntPtr HwndTopMost = new(-1);
    private static WidgetWindow? current;

    private readonly IntPtr backgroundBrush;
    private readonly IntPtr nullPen;
    private readonly IntPtr timeFont;
    private readonly IntPtr labelFont;
    private IntPtr hwnd;
    private bool dragging;
    private NativeMethods.POINT dragStartPointer;
    private NativeMethods.RECT dragStartWindow;

    public WidgetWindow(bool resetRequested)
    {
        current = this;
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

        RegisterWindowClass();
        CreateWindow(resetRequested);
    }

    public int Run()
    {
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        NativeMethods.UpdateWindow(hwnd);

        while (NativeMethods.GetMessage(out NativeMethods.MSG message, IntPtr.Zero, 0, 0))
        {
            NativeMethods.TranslateMessage(in message);
            NativeMethods.DispatchMessage(in message);
        }

        return 0;
    }

    private static NativeMethods.POINT CursorPosition()
    {
        return NativeMethods.GetCursorPos(out NativeMethods.POINT point)
            ? point
            : new NativeMethods.POINT();
    }

    private void RegisterWindowClass()
    {
        IntPtr instance = NativeMethods.GetModuleHandle(null);
        var windowClass = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = &WndProc,
            hInstance = instance,
            hCursor = NativeMethods.LoadCursor(IntPtr.Zero, new IntPtr(NativeMethods.IDC_ARROW)),
            hbrBackground = IntPtr.Zero,
            lpszClassName = WindowClassName,
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

        NativeMethods.SetLayeredWindowAttributes(hwnd, 0, NativeMethods.WidgetAlpha, NativeMethods.LWA_ALPHA);
        NativeMethods.SetWindowPos(
            hwnd,
            HwndTopMost,
            position.X,
            position.Y,
            PositionMath.WidgetWidth,
            PositionMath.WidgetHeight,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        NativeMethods.SetTimer(hwnd, new UIntPtr(TimerId), 1000, IntPtr.Zero);
        PositionStore.Save(position.X, position.Y);
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
                NativeMethods.InvalidateRect(windowHandle, IntPtr.Zero, false);
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

    private void Paint(IntPtr windowHandle)
    {
        IntPtr hdc = NativeMethods.BeginPaint(windowHandle, out NativeMethods.PAINTSTRUCT paint);
        IntPtr oldBrush = NativeMethods.SelectObject(hdc, backgroundBrush);
        IntPtr oldPen = NativeMethods.SelectObject(hdc, nullPen);

        NativeMethods.Rectangle(hdc, 0, 0, PositionMath.WidgetWidth + 1, PositionMath.WidgetHeight + 1);
        NativeMethods.SelectObject(hdc, oldPen);
        NativeMethods.SelectObject(hdc, oldBrush);

        NativeMethods.SetBkMode(hdc, NativeMethods.TRANSPARENT);
        DrawText(hdc, DateTime.UtcNow.ToString("HH:mm"), timeFont, NativeMethods.TimeTextColorRef, new NativeMethods.RECT
        {
            Left = 4,
            Top = 0,
            Right = 132,
            Bottom = PositionMath.WidgetHeight,
        });
        DrawText(hdc, "UTC", labelFont, NativeMethods.LabelTextColorRef, new NativeMethods.RECT
        {
            Left = 132,
            Top = 1,
            Right = PositionMath.WidgetWidth - 4,
            Bottom = PositionMath.WidgetHeight,
        });

        NativeMethods.EndPaint(windowHandle, in paint);
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
        IntPtr menu = NativeMethods.CreatePopupMenu();
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

        if (command == ResetMenuId)
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

    private void DestroyResources(IntPtr windowHandle)
    {
        NativeMethods.KillTimer(windowHandle, new UIntPtr(TimerId));
        NativeMethods.DeleteObject(backgroundBrush);
        NativeMethods.DeleteObject(nullPen);
        NativeMethods.DeleteObject(timeFont);
        NativeMethods.DeleteObject(labelFont);
    }
}
