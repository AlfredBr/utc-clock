# Minute Transition Animation: Design Spec

Date: 2026-09-03
Status: approved by the owner on 2026-09-03 with two decisions: follow the Windows animation switch by default, keep the 40 ms carry lag.
Brainstorm: [../brainstorming/2026-09-03-minute-transition-animation.md](../brainstorming/2026-09-03-minute-transition-animation.md) (options, judge scores, verification corrections, live board).

## Goal

When the displayed UTC minute changes, every digit that changes rolls to its new value instead of switching instantly. Nothing else about the widget changes: same size, colors, fonts, position behavior, menu, startup registration, single-file Native AOT executable.

## Requirements

1. Only the digits that differ between the old and new `HH:mm` string move. The colon, unchanged digits and the `UTC` label never move, dim or blink.
2. At rest, the rendered widget is pixel-identical to the current build for every time string. (Measured outcome: identical except for a 1/255 blue-channel rounding on ClearType fringe pixels caused by the memory bitmap; see Acceptance.)
3. The transition is a damped odometer roll (spec below), drawn with pure GDI. No new NuGet packages, no COM, no GDI+, no Direct2D. Native AOT publish must stay warning-free.
4. The minute change starts within about 25 ms of the wall-clock minute boundary, not up to a second late as today.
5. Motion follows the Windows "Animation effects" switch by default. A persisted right-click toggle overrides it in either direction. With motion off, the minute change is a single repaint.
6. Any change that is not exactly one minute forward snaps without motion: first paint, resume from sleep, clock corrections, a roll interrupted by a new boundary. Remote Desktop sessions snap.
7. The taskbar re-assert keeps running on every one-second tick, including during a roll.
8. Pure logic (layout, easing, color lerp, changed-cell mask, one-minute guard, frame schedule, timer arithmetic, setting resolution) lives in `Services/` with no Win32 dependency and is covered by the existing test runner.
9. Owner rules: no gradients, no shadows, flat colors only, sans-serif only. Dimming is a temporal change of one flat text color per glyph per frame.

## Motion spec

| Parameter | Value |
|---|---|
| Direction | Always upward, in place: the outgoing glyph exits through the top of the aperture, the incoming glyph enters from below and seats on the baseline. Never reverses, including 9 -> 0 and 23:59 -> 00:00. |
| Duration | 280 ms per digit. |
| Easing | `e(t) = 1 - (1 - t)^3 (1 + 3t)`, zero velocity at both ends, peak speed at t = 1/3. Progress `t` comes from a monotonic clock, never a frame count. |
| Travel | 28 px = aperture height. Glyph offsets are whole pixels: `dy = round(28 * e(t))`, away-from-zero. Old glyph at `-dy`, new glyph at `+(28 - dy)`. |
| Aperture | Rows `[baseline - 26, baseline + 2)` of the widget, i.e. `[12, 40)` for the measured layout; digit ink is rows 14..37. Moving glyphs are clipped to it; resting glyphs are clipped to their full cell, which is a visual no-op. |
| Dimming | Text color = `Lerp(#FAFAFA -> #18181B, EdgeDim * (d / 28)^DimCurve)`, where `d` is the glyph's displacement from rest, `EdgeDim = 0.7`, `DimCurve = 1.0`. Lerp truncates per channel. |
| Draw order | Where two glyphs could overlap, the dimmer one is drawn first. |
| Carry lag | 40 ms per digit, right to left: a changing cell starts `40 ms x (number of changing cells to its right)` after the boundary. Totals 280 / 320 / 360 / 400 ms for 1 / 2 / 3 / 4 digits. |
| Hour, midnight | Treated as a carry: 12:59 -> 13:00 rolls three digits; 09:59 -> 10:00 and 23:59 -> 00:00 roll four. |
| Reduced motion | `MotionEnabled = persisted ?? (SPI_GETCLIENTAREAANIMATION != 0 && SPI_GETUIEFFECTS != 0)`. Both flags are re-read at every minute boundary, not cached. Off means off. |

## Layout (measured, derived at runtime)

With the time font selected into the back buffer: `GetTextExtentPoint32W("0")` gives the digit advance, `(":")` must equal it, `("00:00")` must equal five advances, `GetTextMetricsW` gives `tmHeight` and `tmAscent`. Then, matching `DrawText(DT_CENTER | DT_VCENTER | DT_SINGLELINE)` in rect (4,0)-(132,48):

- `firstCellX = 4 + (128 - textWidth) / 2` (integer division), `cellX[i] = firstCellX + i * advance`
- `cellTop = (48 - tmHeight) / 2`, `baseline = cellTop + tmAscent`
- aperture `[baseline - 24 - 2, baseline + 2)`, travel `28`

On this machine: advance 21, `tmHeight` 45, `tmAscent` 37, cells at x = 15, 36, 57, 78, 99, `cellTop` 1, baseline 38. If the monospace guard fails, the widget keeps today's whole-string `DrawText` and snaps.

## Architecture

- **Back buffer.** One 194 x 48 screen-compatible bitmap in a memory DC, created from the window DC right after `CreateWindowEx` and before the window is shown, with the first frame already rendered. `WM_PAINT` is a `BitBlt` of `rcPaint`. Teardown deletes the DC before the bitmap.
- **Per-cell drawing.** Every character is drawn with `ExtTextOutW(ETO_CLIPPED)` at `(cellX[i], cellTop + offset)` with the DC's default `TA_TOP` alignment, so the label's `DrawText` stays valid.
- **Tick timer (id 1).** Re-armed on every fire to `clamp(1000 - UtcNow.Millisecond + 5, 10, 1000)` ms. Detects the change by comparing `HH:mm` strings. Runs the taskbar re-assert every tick.
- **Frame pacing.** While a roll is live, the message loop in `Run` paces frames from idle time: it drains every pending message with `PeekMessage` (input included, so a drag never freezes), then blocks in `DwmFlush()` for one compose, computes elapsed time from `Stopwatch`, renders and presents with `InvalidateRect(timeRect) + UpdateWindow`. If `DwmFlush` fails, or returns three times in a row in under 0.5 ms (it is not waiting for a compose), the remaining frames fall back to `SetTimer(id 2, USER_TIMER_MINIMUM)`. Never `SetTimer(16)`. The one-second tick also acts as a watchdog and seats a roll whose schedule has elapsed. (Changed during execution from a self-posted `WM_APP` loop, which the review showed would starve mouse input for the length of a roll.)
- **Setting.** `position.json` gains a nullable `AnimateMinuteChange`. Saving the position preserves it; toggling the flag writes it with the live window position. `--reset` leaves it untouched. Writes are best-effort: an unwritable settings folder no longer terminates the widget. The right-click item shows the resolved state with a check mark and, while no explicit choice is stored, the label `Animate minute change (following Windows: on|off)`.

## Acceptance

- `dotnet run --project UtcClockWidget.Tests` passes with the new tests, including the pinned integer offset schedule `0, 0, 2, 4, 6, 9, 11, 14, 17, 19, 22, 23, 25, 26, 27, 28` at 15.625 ms ticks and the color oracle `0x008A8989` at k = 0.5.
- `dotnet build -c Debug` and `dotnet publish -c Release` succeed with no new IL or AOT warnings.
- A screenshot of the resting widget differs from the previous build by zero pixels. **Measured outcome (2026-09-03):** 128 to 153 ClearType fringe pixels out of 9,312 differ by exactly 1/255 in the blue channel. Whole-string and per-cell drawing into the back buffer produce identical output, so the shift comes from rendering into a memory bitmap instead of the window surface; a DIB section behaves the same. Invisible, and accepted by the owner's request to implement with a back buffer.
- Watching one carry (xx:x9 -> xx:(x+1)0) and one hour change with the toggle on shows the roll; with the toggle off the digit snaps on the second boundary.
- One-week acceptance test: if minute changes pull the eye while working, step down in this order without a redesign: `EdgeDim 0.8`, then `StaggerMs 0`, then `DurationMs 220`, then the slide-fade constants (travel 10). Record the outcome in `RELEASE_NOTES.md`.

## Out of scope

DPI scaling, per-pixel alpha or rounded corners, battery or session gating, a command-line motion switch, any change to the label or colon.
