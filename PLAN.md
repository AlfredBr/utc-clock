# UtcClockWidget — Implementation Plan

Hand this document to a coding LLM/agent with file and shell access. It contains
everything needed to build the app without further design input. Decisions already
made are marked **DECIDED**; anything left open is marked **OPEN** with a recommended
default the agent should just take if no one objects.

## 1. What we're building

A tiny, chrome-less, always-on-top desktop widget for Windows 11 that shows the
current time in **UTC**, in a rounded, semi-transparent dark rectangle with light
sans-serif text. The user drags it anywhere on screen; its position persists across
restarts. It has no taskbar button and no system tray icon — the only way to interact
with it is right-clicking it (Reset Position / Exit) or dragging it. A `--reset`
command-line switch restores it to a known default position if it gets dragged off
an unplugged monitor or otherwise lost.

**DECIDED requirements (from user):**
- Time format: `HH:mm`, 24-hour, **no seconds** (updates once a minute is fine, but
  a 1-second timer is simplest to implement correctly — see §6).
- Visual style: semi-transparent dark background, light text, rounded corners.
- Exit/interaction model: right-click context menu on the widget itself
  (**no system tray icon** — keep this minimal).
- No taskbar icon, ever.
- Drag anywhere on screen; remembers last position across restarts.
- `--reset` switch restores a default position.

**OPEN, with defaults the agent should just use:**
- Default/reset position: top-right corner of the primary monitor, 24px margin
  from the top and right edges.
- Widget size: 140×56 px, corner radius 16, font size 28, `SemiBold`, centered text.
- Font: do **not** bundle a custom font — WinUI 3's default app font is
  *Segoe UI Variable*, which is already sans-serif. Just don't override it.
- No autostart-on-login, no settings UI, no multi-instance guard, no light/dark
  theme switch. These are explicitly out of scope (YAGNI) — do not add them.

## 2. Tech stack

- **WinUI 3** (Windows App SDK), **C#**, **.NET 8** (or whatever `dotnet new winui`
  defaults to — don't fight the template's default TFM).
- Packaged single-project MSIX app (the default produced by the template below).
  Don't switch to unpackaged — not worth the extra plumbing for this app.
- Target OS: Windows 11 (DWM rounded-corner API used in §4 is Windows 11-only; on
  Windows 10 the corners will just render square — acceptable degradation, not a
  blocker, do not add conditional version-detection code for this).

## 3. Scaffold the project

```powershell
dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
cd C:\Users\alfredbr\GitHub
dotnet new winui -n UtcClockWidget
cd UtcClockWidget
dotnet build
```

Confirm the unmodified template builds and runs (`dotnet run` or F5) before changing
anything — sanity-check the toolchain first.

Resulting structure to build out:

```
UtcClockWidget/
  App.xaml / App.xaml.cs          – entry point, arg parsing for --reset
  MainWindow.xaml / .xaml.cs      – the widget window itself
  Services/PositionStore.cs       – load/save {X,Y} JSON under %LOCALAPPDATA%
  Native/NativeMethods.cs         – P/Invoke declarations (Win32/DWM)
```

## 4. Strip the window chrome

In `MainWindow.xaml.cs`, after the window is constructed, get its `AppWindow` and
configure the presenter:

```csharp
var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

appWindow.Resize(new Windows.Graphics.SizeInt32(140, 56));

if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
{
    presenter.IsResizable = false;
    presenter.IsMaximizable = false;
    presenter.IsMinimizable = false;
    presenter.IsAlwaysOnTop = true;
    presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
}
```

**Hide from taskbar** — there is no managed API for this; it requires a direct
Win32 call setting `WS_EX_TOOLWINDOW` and clearing `WS_EX_APPWINDOW` on the
extended window style, done *before* the window is first shown (toggle
hide/show after if it was already shown once):

```csharp
// Native/NativeMethods.cs
internal static class NativeMethods
{
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_APPWINDOW = 0x00040000;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const int DWMWCP_ROUND = 2;

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
```

```csharp
int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
exStyle &= ~NativeMethods.WS_EX_APPWINDOW;
NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);

int corner = NativeMethods.DWMWCP_ROUND;
NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
```

If `WS_EX_TOOLWINDOW` doesn't take effect because the window was already shown,
hide and re-show it (`appWindow.Hide(); appWindow.Show();`) immediately after
setting the style.

## 5. Visual content (XAML)

`MainWindow.xaml` root content — a single `Border` for the rounded rect, no extra
chrome, no gradients, no drop shadows (don't add any `Shadow`/`ThemeShadow` —
deliberately flat):

```xml
<Border CornerRadius="16"
        Background="#CC1E1E1E"
        x:Name="RootBorder">
    <TextBlock x:Name="ClockText"
               HorizontalAlignment="Center"
               VerticalAlignment="Center"
               FontSize="28"
               FontWeight="SemiBold"
               Foreground="#FFF5F5F5"
               Text="--:--"/>
</Border>
```

`#CC1E1E1E` = near-black at ~80% opacity — gives the "semi-transparent dark" look
without needing Acrylic/Mica backdrops or Win32 layered-window tricks. This is the
simplest approach that satisfies the requirement; **do not** reach for
`DesktopAcrylicBackdrop`/blur effects unless this flat semi-transparent fill turns
out to look wrong when actually run — it adds complexity for no requirement gain.

Window background outside the Border should be `Transparent` (set on the root
`Grid`/`Window.SystemBackdrop = null`) so only the rounded rect itself is visible,
not a square window with a rounded shape drawn inside it.

## 6. Clock ticking

In `MainWindow.xaml.cs`:

```csharp
var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
timer.Tick += (_, _) => ClockText.Text = DateTime.UtcNow.ToString("HH:mm");
timer.Start();
ClockText.Text = DateTime.UtcNow.ToString("HH:mm"); // set immediately, don't wait for first tick
```

A 1-second interval is simplest and cheap enough; don't optimize this to fire only
on minute boundaries.

## 7. Drag to move

No title bar means no free OS-provided drag. Implement manually with pointer
events on the root `Border` — simplest reliable approach, no WndProc subclassing
needed:

```csharp
PointInt32 dragStartWindowPos;
Windows.Foundation.Point dragStartPointerPos;
bool dragging;

RootBorder.PointerPressed += (s, e) =>
{
    dragging = true;
    dragStartWindowPos = appWindow.Position;
    dragStartPointerPos = e.GetCurrentPoint(null).Position; // screen-relative via RawPosition below
    RootBorder.CapturePointer(e.Pointer);
};

RootBorder.PointerMoved += (s, e) =>
{
    if (!dragging) return;
    var current = e.GetCurrentPoint(null).Position;
    int dx = (int)(current.X - dragStartPointerPos.X);
    int dy = (int)(current.Y - dragStartPointerPos.Y);
    appWindow.Move(new PointInt32(dragStartWindowPos.X + dx, dragStartWindowPos.Y + dy));
};

RootBorder.PointerReleased += (s, e) =>
{
    if (!dragging) return;
    dragging = false;
    RootBorder.ReleasePointerCapture(e.Pointer);
    PositionStore.Save(appWindow.Position.X, appWindow.Position.Y);
};
```

Note: `PointerPoint` positions from `e.GetCurrentPoint(null)` are window-client-relative,
not screen-relative, so the delta math above (current minus drag-start, both
window-relative) is what makes this correct — don't try to convert to screen
coordinates, the relative delta is all that's needed.

## 8. Position persistence

`Services/PositionStore.cs`:

```csharp
internal static class PositionStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UtcClockWidget", "position.json");

    public static (int X, int Y) DefaultPosition()
    {
        var area = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
        const int margin = 24;
        const int width = 140;
        return (area.X + area.Width - width - margin, area.Y + margin);
    }

    public static (int X, int Y)? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var json = File.ReadAllText(FilePath);
            var pos = System.Text.Json.JsonSerializer.Deserialize<PositionDto>(json);
            return pos is null ? null : (pos.X, pos.Y);
        }
        catch { return null; } // corrupt/missing file -> caller falls back to default
    }

    public static void Save(int x, int y)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var json = System.Text.Json.JsonSerializer.Serialize(new PositionDto { X = x, Y = y });
        File.WriteAllText(FilePath, json);
    }

    private class PositionDto { public int X { get; set; } public int Y { get; set; } }
}
```

On window startup: if `--reset` was passed (see §9) or `Load()` returns `null`,
use `DefaultPosition()` and immediately `Save()` it. Otherwise clamp the loaded
position to the current virtual screen bounds (in case a monitor was unplugged)
before applying it with `appWindow.Move(...)` — clamping logic: for each axis,
`Math.Clamp(value, virtualScreenMin, virtualScreenMax - widgetSize)`. Use
`Microsoft.UI.Windowing.DisplayArea.GetFromPoint` or iterate `DisplayArea.FindAll()`
to compute the virtual screen bounding box.

## 9. `--reset` switch

In `App.xaml.cs`, `OnLaunched`:

```csharp
var args = Environment.GetCommandLineArgs();
bool resetRequested = args.Contains("--reset");
```

Pass `resetRequested` through to `MainWindow`'s constructor (or set a static flag
read during its startup logic) so it skips `PositionStore.Load()` and goes
straight to `DefaultPosition()`, then saves that as the new persisted position.

## 10. Right-click menu

On the root `Border`, wire a `MenuFlyout` with two items:

```csharp
var menu = new MenuFlyout();
var resetItem = new MenuFlyoutItem { Text = "Reset Position" };
resetItem.Click += (_, _) =>
{
    var (x, y) = PositionStore.DefaultPosition();
    appWindow.Move(new PointInt32(x, y));
    PositionStore.Save(x, y);
};
var exitItem = new MenuFlyoutItem { Text = "Exit" };
exitItem.Click += (_, _) => Application.Current.Exit();
menu.Items.Add(resetItem);
menu.Items.Add(exitItem);

RootBorder.RightTapped += (s, e) => menu.ShowAt(RootBorder, e.GetPosition(RootBorder));
```

## 11. Verification checklist (manual — this is a GUI app, no automated UI tests)

Run through all of these after implementation, in order:

1. `dotnet run` launches the widget at the default top-right position with no
   title bar, no border, and no taskbar button (check the taskbar directly).
2. Text shows the correct current UTC time in `HH:mm`, sans-serif, light text on
   a visibly translucent dark rounded rectangle (no gradient, no shadow).
3. Drag the widget to an arbitrary new screen location; it follows the cursor
   smoothly.
4. Close the app (right-click → Exit) and relaunch (`dotnet run`) — it reappears
   exactly where it was left.
5. Relaunch with `dotnet run -- --reset` (or run the built `.exe --reset`) — it
   snaps back to the default top-right position, and a subsequent normal
   relaunch (no flag) stays there (confirms reset also persisted).
6. Right-click shows exactly two items, "Reset Position" and "Exit"; both work.
7. The widget stays on top of other windows when they're clicked/focused.

## 12. Explicitly out of scope — do not implement

System tray icon, autostart-on-login, settings/preferences UI, single-instance
enforcement, light/dark theme toggle, localization, seconds display, multi-monitor
default-position picker beyond the clamping in §8. If any of these seem tempting
while implementing, don't — they weren't asked for.
