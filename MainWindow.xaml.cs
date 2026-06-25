using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using utc_clock.Native;
using utc_clock.Services;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace utc_clock;

/// <summary>
/// The application window.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly Microsoft.UI.Windowing.AppWindow _appWindow;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private PointInt32 _dragStartWindowPosition;
    private NativeMethods.POINT _dragStartPointerPosition;
    private bool _dragging;

    public MainWindow(bool resetRequested)
    {
        InitializeComponent();

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        ConfigureWindow(hwnd);
        ConfigureClock();
        ConfigureDragging();
        ConfigureContextMenu();
        ApplyStartupPosition(resetRequested);
    }

    private void ConfigureWindow(IntPtr hwnd)
    {
        _appWindow.Resize(new SizeInt32(PositionMath.WidgetWidth, PositionMath.WidgetHeight));
        ExtendsContentIntoTitleBar = true;

        if (_appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        }

        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
        exStyle &= ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);

        int corner = NativeMethods.DWMWCP_DONOTROUND;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref corner,
            sizeof(int));

        int borderColor = NativeMethods.SurfaceBorderColorRef;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_BORDER_COLOR,
            ref borderColor,
            sizeof(int));

        // WinUI leaves a light non-client frame just outside the XAML content
        // (visible as a white border on dark backgrounds), and it is not
        // removed by the DWM border color or corner settings. Clip the window
        // to a slightly inset rectangle so that frame is never drawn, leaving a
        // clean square dark card.
        const int inset = 3;
        IntPtr clipRegion = NativeMethods.CreateRectRgn(
            inset,
            inset,
            PositionMath.WidgetWidth - inset,
            PositionMath.WidgetHeight - inset);
        if (clipRegion != IntPtr.Zero)
        {
            NativeMethods.SetWindowRgn(hwnd, clipRegion, bRedraw: true);
        }
    }

    private void ConfigureClock()
    {
        _timer.Tick += (_, _) => UpdateClock();
        _timer.Start();
        UpdateClock();
    }

    private void ConfigureDragging()
    {
        RootBorder.PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(RootBorder);
            if (!point.Properties.IsLeftButtonPressed || !NativeMethods.GetCursorPos(out _dragStartPointerPosition))
            {
                return;
            }

            _dragging = true;
            _dragStartWindowPosition = _appWindow.Position;
            RootBorder.CapturePointer(e.Pointer);
        };

        RootBorder.PointerMoved += (_, _) =>
        {
            if (!_dragging || !NativeMethods.GetCursorPos(out NativeMethods.POINT current))
            {
                return;
            }

            int dx = current.X - _dragStartPointerPosition.X;
            int dy = current.Y - _dragStartPointerPosition.Y;

            _appWindow.Move(new PointInt32(
                _dragStartWindowPosition.X + dx,
                _dragStartWindowPosition.Y + dy));
        };

        RootBorder.PointerReleased += (_, e) => EndDrag(e.Pointer);
        RootBorder.PointerCanceled += (_, e) => EndDrag(e.Pointer);
    }

    private void ConfigureContextMenu()
    {
        var menu = new MenuFlyout();

        var resetItem = new MenuFlyoutItem { Text = "Reset Position" };
        resetItem.Click += (_, _) =>
        {
            var (x, y) = PositionStore.DefaultPosition();
            _appWindow.Move(new PointInt32(x, y));
            PositionStore.Save(x, y);
        };

        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        exitItem.Click += (_, _) => Application.Current.Exit();

        menu.Items.Add(resetItem);
        menu.Items.Add(exitItem);

        RootBorder.RightTapped += (_, e) => menu.ShowAt(RootBorder, e.GetPosition(RootBorder));
    }

    private void ApplyStartupPosition(bool resetRequested)
    {
        (int X, int Y) position = resetRequested
            ? PositionStore.DefaultPosition()
            : PositionStore.Load() is { } savedPosition
                ? PositionStore.ClampToVirtualScreen(savedPosition.X, savedPosition.Y)
                : PositionStore.DefaultPosition();

        _appWindow.Move(new PointInt32(position.X, position.Y));
        PositionStore.Save(position.X, position.Y);
    }

    private void UpdateClock()
    {
        TimeText.Text = DateTime.UtcNow.ToString("HH:mm");
    }

    private void EndDrag(Microsoft.UI.Xaml.Input.Pointer pointer)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        RootBorder.ReleasePointerCapture(pointer);
        PositionStore.Save(_appWindow.Position.X, _appWindow.Position.Y);
    }
}
