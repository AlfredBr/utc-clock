# Release Notes

## 2026-09-02

### Fixed

- **Widget hidden behind the taskbar.** The widget and the taskbar are both topmost windows, and Explorer raises the taskbar above every other topmost window each time it is shown. A widget parked in the taskbar strip was covered whenever the taskbar appeared, which read as "not always on top". The widget now checks on its one-second timer whether a taskbar that overlaps it sits above it in the z-order and, only in that case, reasserts `HWND_TOPMOST`. A widget that does not overlap a taskbar never triggers this, so other always-on-top windows are left alone. Secondary-monitor taskbars are included.
- **Window title stored as "U".** `DefWindowProc`, `GetMessage`, and `DispatchMessage` were imported without a `CharSet`, so they bound to the ANSI exports while the window itself is Unicode. The title was truncated at the first NUL of the UTF-16 string. All three now bind to the `W` exports.
- **Stale unit test.** The clamp test still hard-coded a pre-48px widget height and failed. It now derives the expectation from `PositionMath.WidgetHeight`.

### Added

- `PositionMath.Overlaps` with tests covering a widget in the shown and auto-hidden taskbar strip, plus touching and disjoint bounds.

### Investigation notes

- The always-on-top flag itself was never lost. Live inspection showed `WS_EX_TOPMOST` set at creation and still set after minutes of desktop activity.
- The clock running at logon was the previous WinUI 3 build. The per-user Run value pointed at the old WinUI publish folder under `bin\Release`, so the native build on this branch was not the one being observed. Running the native Release build once rewrites the Run value to its own path.
- Virtual desktops need no handling. The shell's `IVirtualDesktopManager` reports no desktop association for unowned tool windows (`GetWindowDesktopId` returns `TYPE_E_ELEMENTNOTFOUND`) and never cloaks them on a desktop switch, so the widget is already visible on every desktop. A tracker that moved the window between desktops was prototyped and removed as dead code.