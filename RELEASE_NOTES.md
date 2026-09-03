# Release Notes

## 2026-09-03

### Added

- **Animated minute change.** Digits that change at a minute boundary now roll upward through a clipped 28 px aperture, dimming toward the surface as they leave and brightening as they seat, 280 ms per digit with a 40 ms right-to-left carry lag (so 12:59 -> 13:00 takes 360 ms and 23:59 -> 00:00 takes 400 ms). Only the changing digits move; the colon, unchanged digits and the `UTC` label stay put.
- **Motion follows Windows by default.** The roll honours Settings > Accessibility > Visual effects > Animation effects (both `SPI_GETCLIENTAREAANIMATION` and `SPI_GETUIEFFECTS`, read live at each minute boundary). A new right-click item, `Animate minute change`, overrides it in either direction and is remembered in `position.json`; while no choice is stored the label shows which source is in charge, for example `Animate minute change (following Windows: off)`. Windows currently reports client-area animations disabled on the development machine, so the first roll there needs that item ticked once.
- **Minute changes land on the second.** The one-second timer re-arms itself to the wall-clock second, so a change shows within about 25 ms of the boundary instead of up to a second late.

### Changed

- Painting goes through a memory-DC back buffer; `WM_PAINT` is a single `BitBlt`. Each character is drawn with `ExtTextOutW` into a fixed cell derived from the measured font metrics at startup (21 px cells at x = 15, 36, 57, 78, 99 for Cascadia Mono -34). If the time font is not monospaced, the widget keeps the whole-string path and snaps.
- While a roll is live the message loop paces frames from idle time: it drains every pending message (mouse input included, so a drag never freezes) and then blocks in `DwmFlush` for exactly one compose, giving one frame per refresh on any display. If composition refuses, or `DwmFlush` returns three times without waiting, the remaining frames fall back to a 10 ms `SetTimer`. A one-second watchdog seats a roll whose frame source stopped. Any change that is not exactly one minute forward (launch, resume, clock correction, a roll interrupted by a new boundary) snaps without motion, as do Remote Desktop sessions.
- `PositionStore.Save` now preserves the persisted animate flag; the DTO and JSON context moved to `Services/PositionDto.cs` so the test project can compile them; `--reset` leaves the flag untouched. Toggling the menu item writes the flag together with the live window position, so a missing settings file never records (0, 0).

### Fixed

- **A settings file that cannot be written no longer terminates the widget.** Previously an unwritable `%LOCALAPPDATA%\UtcClockWidget` (read-only or redirected profile) raised an unhandled exception at startup or at the end of a drag. `PositionStore` now writes best-effort, like the Run-key registration: the widget keeps running and the position and animate choice are simply not remembered across launches.
- Resting render: measured against the previous build at the same screen position, 128 to 153 ClearType fringe pixels out of 9,312 differ by exactly 1/255 in the blue channel. The cause is drawing into the memory bitmap rather than onto the window surface (whole-string and per-cell drawing into the buffer are identical); it is not visible and is accepted.

### Verified

- 23 unit tests pass, including the pinned integer offset schedule `0, 0, 2, 4, 6, 9, 11, 14, 17, 19, 22, 23, 25, 26, 27, 28` at 15.625 ms ticks and the colour oracle `0x008A8989` at k = 0.5.
- Live capture of 21:06 -> 21:07 on the development machine: first visible motion 58 ms after the boundary, seated 274 ms after it, 14 distinct frames.

### Design

- **Minute-change animation, brainstormed.** Explored how the widget could animate the digits that change at a minute boundary instead of switching them instantly. Rendering technologies (pure GDI, GDI+, Direct2D/DirectWrite under Native AOT) and four motion proposals were probed, judged and adversarially verified, with frames rendered through the app's actual GDI pipeline on this machine to check the result. Recommendation: a damped odometer roll (only the changing digits roll upward 28 px through their ink band, 280 ms, zero-velocity start and stop, distance-based dimming toward the surface, 40 ms right-to-left carry lag), drawn with pure GDI into a back buffer, resting render pixel-identical to today. Awaiting owner approval; no code written. Discussion and a live comparison board are saved under `docs/superpowers/brainstorming/`.
- **Finding:** Windows currently reports client-area animations disabled on this machine while the persisted preference says on, so any motion feature needs an in-app override and must re-read the flag rather than cache it.

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