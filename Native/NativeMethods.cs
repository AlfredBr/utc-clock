using System.Runtime.InteropServices;

namespace utc_clock.Native;

internal static unsafe class NativeMethods
{
    internal const int WidgetAlpha = 236;
    internal const int SurfaceColorRef = 0x001B1818;
    internal const int TimeTextColorRef = 0x00FAFAFA;
    internal const int LabelTextColorRef = 0x00ECE8E8;

    internal const int SPI_GETWORKAREA = 0x0030;
    internal const int SM_XVIRTUALSCREEN = 76;
    internal const int SM_YVIRTUALSCREEN = 77;
    internal const int SM_CXVIRTUALSCREEN = 78;
    internal const int SM_CYVIRTUALSCREEN = 79;

    internal const int WS_POPUP = unchecked((int)0x80000000);
    internal const int WS_EX_TOPMOST = 0x00000008;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_LAYERED = 0x00080000;

    internal const int SW_SHOWNOACTIVATE = 4;
    internal const int SWP_NOSIZE = 0x0001;
    internal const int SWP_NOZORDER = 0x0004;
    internal const int SWP_NOACTIVATE = 0x0010;
    internal const int SWP_SHOWWINDOW = 0x0040;
    internal const int SWP_NOMOVE = 0x0002;
    internal const uint GW_HWNDPREV = 3;

    internal const int WM_DESTROY = 0x0002;
    internal const int WM_PAINT = 0x000F;
    internal const int WM_SETICON = 0x0080;
    internal const int WM_TIMER = 0x0113;
    internal const int WM_LBUTTONDOWN = 0x0201;
    internal const int WM_LBUTTONUP = 0x0202;
    internal const int WM_MOUSEMOVE = 0x0200;
    internal const int WM_RBUTTONUP = 0x0205;
    internal const uint WM_QUIT = 0x0012;
    internal const uint PM_REMOVE = 0x0001;

    internal const int SPI_GETCLIENTAREAANIMATION = 0x1042;
    internal const int SPI_GETUIEFFECTS = 0x103E;
    internal const int SM_REMOTESESSION = 0x1000;
    internal const uint USER_TIMER_MINIMUM = 10;
    internal const int MF_CHECKED = 0x00000008;
    internal const uint SRCCOPY = 0x00CC0020;
    internal const uint ETO_CLIPPED = 0x0004;

    internal const int MK_LBUTTON = 0x0001;
    internal const int IDC_ARROW = 32512;
    internal const int LWA_ALPHA = 0x00000002;

    internal const int ICON_SMALL = 0;
    internal const int ICON_BIG = 1;
    internal const uint IMAGE_ICON = 1;
    internal const uint LR_DEFAULTCOLOR = 0;
    internal const int SM_CXICON = 11;
    internal const int SM_CYICON = 12;
    internal const int SM_CXSMICON = 49;
    internal const int SM_CYSMICON = 50;

    // Resource id the .NET SDK assigns to the icon group it embeds for <ApplicationIcon>.
    internal const int AppIconResourceId = 32512;

    internal const int MF_STRING = 0x00000000;
    internal const int TPM_RIGHTBUTTON = 0x0002;
    internal const int TPM_RETURNCMD = 0x0100;

    internal const int FW_SEMIBOLD = 600;
    internal const int DEFAULT_CHARSET = 1;
    internal const int OUT_DEFAULT_PRECIS = 0;
    internal const int CLIP_DEFAULT_PRECIS = 0;
    internal const int CLEARTYPE_QUALITY = 5;
    internal const int DEFAULT_PITCH = 0;
    internal const int FF_DONTCARE = 0;
    internal const int TRANSPARENT = 1;
    internal const int PS_NULL = 5;
    internal const int DT_CENTER = 0x00000001;
    internal const int DT_VCENTER = 0x00000004;
    internal const int DT_SINGLELINE = 0x00000020;
    internal const int DT_NOPREFIX = 0x00000800;

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateWindowEx(
        int dwExStyle,
        [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    internal static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(IntPtr hwnd, int crKey, byte bAlpha, int dwFlags);

    [DllImport("user32.dll")]
    internal static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr LoadImage(IntPtr hInst, IntPtr name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static extern IntPtr SendMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", EntryPoint = "GetMessageW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(in MSG lpMsg);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW", CharSet = CharSet.Unicode)]
    internal static extern IntPtr DispatchMessage(in MSG lpMsg);

    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool KillTimer(IntPtr hWnd, UIntPtr uIDEvent);

    [DllImport("user32.dll")]
    internal static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EndPaint(IntPtr hWnd, in PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InvalidateRect(IntPtr hWnd, RECT* lpRect, bool bErase);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

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

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateSolidBrush(int colorRef);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreatePen(int fnPenStyle, int nWidth, int crColor);

    [DllImport("gdi32.dll", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateFont(
        int cHeight,
        int cWidth,
        int cEscapement,
        int cOrientation,
        int cWeight,
        int bItalic,
        int bUnderline,
        int bStrikeOut,
        int iCharSet,
        int iOutPrecision,
        int iClipPrecision,
        int iQuality,
        int iPitchAndFamily,
        [MarshalAs(UnmanagedType.LPWStr)] string pszFaceName);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RoundRect(IntPtr hdc, int left, int top, int right, int bottom, int width, int height);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Rectangle(IntPtr hdc, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    internal static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    internal static extern int SetTextColor(IntPtr hdc, int colorRef);

    [DllImport("user32.dll", EntryPoint = "DrawTextW", CharSet = CharSet.Unicode)]
    internal static extern int DrawText(IntPtr hdc, [MarshalAs(UnmanagedType.LPWStr)] string lpchText, int cchText, ref RECT lprc, uint format);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetCapture(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    internal static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, [MarshalAs(UnmanagedType.LPWStr)] string lpNewItem);

    [DllImport("user32.dll")]
    internal static extern uint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(int uiAction, int uiParam, out RECT pvParam, int fWinIni);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(int uiAction, int uiParam, out int pvParam, int fWinIni);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindow(
        [MarshalAs(UnmanagedType.LPWStr)] string? lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string? lpWindowName);

    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowEx(
        IntPtr hWndParent,
        IntPtr hWndChildAfter,
        [MarshalAs(UnmanagedType.LPWStr)] string? lpszClass,
        [MarshalAs(UnmanagedType.LPWStr)] string? lpszWindow);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged[Stdcall]<IntPtr, uint, UIntPtr, IntPtr, IntPtr> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        private readonly byte rgbReserved0;
        private readonly byte rgbReserved1;
        private readonly byte rgbReserved2;
        private readonly byte rgbReserved3;
        private readonly byte rgbReserved4;
        private readonly byte rgbReserved5;
        private readonly byte rgbReserved6;
        private readonly byte rgbReserved7;
        private readonly byte rgbReserved8;
        private readonly byte rgbReserved9;
        private readonly byte rgbReserved10;
        private readonly byte rgbReserved11;
        private readonly byte rgbReserved12;
        private readonly byte rgbReserved13;
        private readonly byte rgbReserved14;
        private readonly byte rgbReserved15;
        private readonly byte rgbReserved16;
        private readonly byte rgbReserved17;
        private readonly byte rgbReserved18;
        private readonly byte rgbReserved19;
        private readonly byte rgbReserved20;
        private readonly byte rgbReserved21;
        private readonly byte rgbReserved22;
        private readonly byte rgbReserved23;
        private readonly byte rgbReserved24;
        private readonly byte rgbReserved25;
        private readonly byte rgbReserved26;
        private readonly byte rgbReserved27;
        private readonly byte rgbReserved28;
        private readonly byte rgbReserved29;
        private readonly byte rgbReserved30;
        private readonly byte rgbReserved31;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

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
}
