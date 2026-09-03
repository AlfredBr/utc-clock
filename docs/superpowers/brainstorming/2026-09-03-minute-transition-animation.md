# Brainstorm: animating the minute change

Date: 2026-09-03
Status: design proposed, awaiting owner approval. No code written.
Companion: [2026-09-03-minute-transition-motion-studies.html](2026-09-03-minute-transition-motion-studies.html) is a live side-by-side board of the options (open it in a browser), with the real GDI frames rendered during verification.

## The ask

> I want to animate the time change. Rather than just instantly switching the numbers, i want an animated transition from one minute to the next (for all numbers that will change). What options do i have? What might you suggest?

## What constrains the design

Facts about the current widget that every option had to respect:

- Native Win32 window painted with plain GDI. `WS_POPUP`, `WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_LAYERED`, uniform alpha 236 via `SetLayeredWindowAttributes`. Size 194 x 48 px, fixed.
- `Paint` fills the client with #18181B, then one `DrawText("HH:mm")` in Cascadia Mono, height -34, semibold, ClearType, #FAFAFA, in rect (4,0)-(132,48) with `DT_CENTER | DT_VCENTER | DT_SINGLELINE`, then `DrawText("UTC")` in Segoe UI Variable Display -20 in rect (132,1)-(190,48). No back buffer.
- One `SetTimer` at 1000 ms with arbitrary phase. It invalidates the whole window and re-asserts `HWND_TOPMOST` over an overlapping taskbar.
- .NET 10, Native AOT, no NuGet packages, flat P/Invoke only. Single 2.7 MB executable.
- Owner's design rules: no gradients, no shadows, flat colors, sans-serif only. The widget lives in an auto-hide taskbar strip and is glanced at, not watched.
- Measured on this machine (96 DPI, 60 Hz): Cascadia Mono at -34 semibold realizes as `tmHeight` 45, `tmAscent` 37, 21 px advance for every digit and the colon. `DrawText` places the five cells at x = 15, 36, 57, 78, 99 with cell top y = 1 and baseline y = 38. Every digit inks rows 14..37 (24 rows); the colon inks rows 20..37. Adjacent cells never touch.
- Windows currently reports client-area animations disabled on this machine: `SPI_GETCLIENTAREAANIMATION` reads **0** live (re-verified twice), `SPI_GETUIEFFECTS` reads 1, not a remote session. The persisted registry preference (`MinAnimate` = 1, `TaskbarAnimations` = 1, the client-area bit set in `UserPreferencesMask`) says animations are **on**, so a runtime setter in this session turned the flag off without persisting it. Do not attribute the 0 to the owner's Accessibility toggle, and do not cache the flag.

## How the options were explored

An orchestrated pass with fifteen agents: four rendering-technology probes (pure GDI, GDI+ flat API, Direct2D/DirectWrite under AOT, platform behavior and accessibility), four independent motion proposals from different design angles (mechanical purist, OS-native motion, peripheral minimalist, expressive), three judges scoring every proposal, one synthesis, and three adversarial verifiers trying to refute the synthesis. Several agents built throwaway GDI harnesses in the session scratchpad to measure rather than guess; the frames in the companion board come from those.

## Rendering technology

| Option | Verdict |
|---|---|
| **Pure GDI + memory-DC back buffer** | Chosen. Everything the roll needs is integer-pixel text output (`ExtTextOutW` with `ETO_CLIPPED`), a solid ground, one `BitBlt` per frame, and a monotonic clock. About ten new flat P/Invokes. Per-cell drawing reproduced today's whole-string `DrawText` with 0 differing pixels for sixteen test strings. |
| GDI+ flat API | Works under AOT (spiked: 25 exports, zero IL warnings), but lays out the same `LOGFONT` 2 px higher with 20 px instead of 21 px advances and maps weight 600 differently. The resting digits would have to move to GDI+ too, changing the resting render. About 28 P/Invokes plus `GdiplusStartup` and a background thread. Not needed. |
| Direct2D + DirectWrite via `[GeneratedComInterface]` | Feasible but disproportionate: roughly +0.8 MB on the exe, about +40 MB private bytes and a resident D3D11 device, about 300 lines of hand-written vtables, device-lost handling, and ClearType drops to grayscale whenever alpha is involved. Nothing the roll needs is exclusive to it. |
| `UpdateLayeredWindow` per-pixel alpha | Fails once `SetLayeredWindowAttributes` has been called; GDI text writes zero alpha and ClearType needs an opaque ground. Nothing to gain for a solid rectangle. |

## Motion options considered

Four proposals were scored on fit, legibility, perceived quality at 34 px, implementability in GDI, and calm (least distracting). Judge totals out of 50:

| Proposal | Idea | Judge 1 | Judge 2 | Judge 3 |
|---|---|---|---|---|
| Odometer | Full-height roll, no dimming, kick-and-detent easing, 40 ms carry lag | 33 | 32 | 34 |
| Carry Roll | 10 px slide with fade, 250 ms, ease-out cubic, 30 ms lag; the only one that noticed animations are off on this machine | 37 | 42 | 40 |
| Breath | No travel: dim to surface, 40 ms dark rest, brighten; 400 ms | 35 | 35 | 31 |
| Carry Ripple | Roll through the 28 px ink band with distance-based dimming and a 60 ms right-to-left carry ripple | 39 | 38 | 36 |

Two judges picked Carry Roll, one picked Carry Ripple, and all three wrote dissents saying the same thing: Carry Roll is the hardest to make look wrong in GDI but is functionally "a new digit fades up from 6 px below", while the owner asked to animate the change of the numbers. The synthesis took Carry Ripple's geometry, Odometer's zero-start-velocity curve, Carry Roll's fade discipline and measured layout numbers, and Carry Roll's discovery that the Windows animation switch is off here.

Techniques ruled out by measurement or by the design rules:

- **Split flap.** `StretchBlt` squashing of a 12-row half-glyph either drops rows (which rows survive changes each frame and shimmers) or, in `HALFTONE` mode, averages the counter of an 8 into a flat bar. A believable flap also relies on shading, which the rules forbid.
- **Scale or pop.** A new font size per frame re-hints the outlines, so stem weights flicker rather than grow; a 5% pop is about 1 px at this size.
- **Wipe or reveal.** Directionless. Says something changed, not that time advanced.
- **Horizontal slide.** Wrong metaphor for a counter.
- **Breath's 2 px nudge.** Two isolated 1 px hops read as jitter in integer-pixel GDI.

## Recommendation: the damped carry roll

Only the digits that change roll upward through an invisible 28 px aperture that exactly brackets the digit ink. The old digit leaves through the top while the new one rises in from below and seats on the baseline. Both dim toward the surface color as they approach the aperture edge. The roll starts from rest and stops into a detent. On a carry, each digit to the left starts 40 ms after its neighbor. The colon, unchanged digits and the UTC label never move. At rest, every frame is pixel-identical to today's rendering.

Why this one: the owner asked to animate the change of the numbers, and the roll is the only transition whose intermediate frames still describe that change truthfully (a half-9 above a half-0 reads as "9 becoming 0") and whose direction carries meaning (a clock only counts up). Its two weaknesses at 34 px, a full-contrast horizontal cut and a first frame that kicks, are fixed by the dimming and the zero-velocity curve. The same renderer degrades to every alternative by changing constants.

### Motion spec

| Parameter | Value |
|---|---|
| Direction | Always upward, in place. Never reverses, including 9 -> 0 and 23:59 -> 00:00. |
| Duration | 280 ms per digit. Visible motion is about 200 ms (frames 2-15 at 15.6 ms ticks), with a ~31 ms still lead-in and a ~46 ms seated tail. |
| Easing | `e(t) = 1 - (1 - t)^3 (1 + 3t)`. `e'(t) = 12 t (1 - t)^2`, zero at both ends, peak 16/9 at t = 1/3 (about 2.8 px per frame). `e(0.5) = 0.6875`. Integer offsets on the 15.625 ms schedule: 0, 0, 2, 4, 6, 9, 11, 14, 17, 19, 22, 23, 25, 26, 27, 28. |
| Travel | 28 px = aperture height. Aperture is rows [12, 40); ink is rows 14..37. Old glyph at `-dy`, new glyph at `28 - dy`, whole pixels only. Ink (24 rows) is shorter than travel, so a constant 4-row gap separates the two half-glyphs and they never fuse. |
| Dimming | `SetTextColor(Lerp(#FAFAFA, #18181B, EdgeDim * d / 28))` per glyph per frame, with `d` its displacement from rest and `EdgeDim = 0.7` (range 0.6-0.8). A `DimCurve` exponent (default 1.0; 0.5 front-loads the dim) is a second tunable. Temporal color change only; no spatial gradient, no alpha. Contrast at the dimmest point is about 2.6:1, mid-roll about 7.7:1. |
| Carry lag | 40 ms per digit, right to left. Totals 280 / 320 / 360 / 400 ms for 1 / 2 / 3 / 4 changing digits. Visible as a cascade when watched directly, not peripherally. Tunable: 0 or 40. |
| Colon | Static anchor. Never moves, dims or blinks. |
| Multi-digit | Changing cells for a +1 minute step are always a contiguous run from the right, so "lag = 40 ms x cells to the right" is exactly carry order. |
| Hour, midnight | Treated as a carry (mod 1440), never a reset. 12:59 -> 13:00 rolls three digits; 09:59 -> 10:00, 19:59 -> 20:00 and 23:59 -> 00:00 roll four. |
| Non +1 changes | Snap with no motion: first paint, resume from sleep, NTP or manual clock step, a roll interrupted by a new boundary. |
| Reduced motion | `MotionEnabled = persisted ?? (SPI_GETCLIENTAREAANIMATION && SPI_GETUIEFFECTS)`, with the live flags re-read at every minute boundary (one cheap call) rather than cached; `WM_SETTINGCHANGE` handled unconditionally as a bonus, since a runtime setter may broadcast nothing. Right-click item "Animate minute change", check mark reflecting the resolved state, label suffix "(following Windows: on/off)" while no explicit choice is stored. Off means off: one repaint, but now on the second boundary. `SM_REMOTESESSION` also snaps. `--reset` leaves the setting untouched. |

### Alternatives, reachable by constants

- **Strict odometer** (`EdgeDim = 0`): purest instrument reading; full-contrast hard cuts on every moving frame and every timer hitch fully exposed. Never the default.
- **Short slide-fade** (travel 10 px in band [10, 42), 250 ms ease-out cubic, out by ~90 ms, in from e = 0.25, 30 ms lag): calmest of the moving options, best mid-transition legibility, reads as a fade with a nudge, last 100 ms is 1 px hops.
- **Dip** (travel 0; 120 ms out, 40 ms dark, 160 ms in on smoothstep): quietest, directionless, and at 09:59 / 19:59 / 23:59 the clock briefly shows only the colon and label.

## Architecture sketch

- **Back buffer.** One 194 x 48 bitmap from `CreateCompatibleBitmap(windowDC, ...)` (never from the memory DC, or it is 1-bpp) in a `CreateCompatibleDC`. Create it and render the initial snap frame right after `CreateWindowEx` and before `SetWindowPos(SWP_SHOWWINDOW)` / `UpdateWindow`, because `WM_PAINT` becomes a pure `BitBlt` of `rcPaint` and must have a frame to blit. Tear down in `WM_DESTROY` with `DeleteDC` first, then `DeleteObject` on the bitmap (deleting a selected bitmap fails and leaks).
- **Per-digit drawing.** Measure once with the time font selected: `GetTextExtentPoint32W("0")`, `(":")`, `("00:00")`, `GetTextMetricsW`. Derive `x0 = 4 + (128 - textWidth) / 2` (integer floor), `cellX[i] = x0 + i * advance`, `cellTop = (48 - tmHeight) / 2`, `baseline = cellTop + tmAscent`, aperture `[baseline - 26, baseline + 2)`. Draw every glyph with `ExtTextOutW(ETO_CLIPPED)`: resting glyphs clipped to the full cell (a visual no-op), moving glyphs clipped to the aperture. Keep the DC at `TA_TOP` so the label's `DrawText` stays valid. If the monospace guard fails (advance of ':' != advance of '0', or textWidth != 5 x advance), fall back to today's whole-string path and snap. Per-cell and whole-string layout agree because GDI's `ExtTextOut` and `DrawText` apply no kern pairs or GSUB features to simple-script text; both place glyph i at `x0 + i x advance`. The 0-pixel diff across sixteen strings is the evidence.
- **Frame pacing.** `SetTimer(10)` measured 15.64 ms against a 16.67 ms compose interval, a 64/60 beat that drops about one frame per roll. Pace with `DwmFlush` instead: render frame 0, post a private `WM_APP` message, handler calls `DwmFlush()`, computes t from `Stopwatch.GetElapsedTime`, renders, presents with `InvalidateRect(timeRect) + UpdateWindow`, re-posts until complete. Fall back to `SetTimer(hwnd, 2, 10)` only if `DwmFlush` fails. Never `SetTimer(16)`: measured bimodal 15.6 / 31.2 ms.
- **Minute alignment.** The tick timer re-arms itself each fire to `clamp(1000 - UtcNow.Millisecond + 5, 10, 1000)` ms so it lands 5-25 ms after each wall-clock second. Change detection is by string comparison. `KeepAboveTaskbar` keeps running on every tick (it only calls `SetWindowPos` when the widget is already covered).
- **Pure logic.** `Services/DigitLayout.cs` (cells from metrics, monospace guard, ink constants) and `Services/MinuteTransition.cs` (easing, color lerp, changed-cell mask, one-minute guard mod 1440, stagger delays, frame schedule, ms-to-next-second, motion-enabled resolution). No Win32 dependency, so the existing test runner covers them. Pin the color-lerp rounding rule (recommended: integer truncation per channel, `c = fg + (bg - fg) * k`) and derive the k = 0.5 test oracle from it: 0x008A8989 under truncation, 0x008B8989 under away-from-zero rounding.
- **Setting.** `PositionDto` gains `bool? AnimateMinuteChange`; `Save(x, y)` must preserve it (today it rewrites the file with X and Y only).
- **New P/Invokes.** `GetDC`, `ReleaseDC`, `CreateCompatibleDC`, `CreateCompatibleBitmap`, `DeleteDC`, `BitBlt`, `ExtTextOutW`, `GetTextExtentPoint32W` (+ `SIZE`), `GetTextMetricsW` (+ `TEXTMETRICW`), `SystemParametersInfoW` overload with `out int`, `DwmFlush`. Constants: `SRCCOPY`, `ETO_CLIPPED`, `SPI_GETCLIENTAREAANIMATION` 0x1042, `SPI_GETUIEFFECTS` 0x103E, `WM_SETTINGCHANGE`, `SM_REMOTESESSION` 0x1000, `USER_TIMER_MINIMUM`, `MF_CHECKED`, `WM_APP`.
- **Tests.** Layout from metrics (21/105/45/37 -> 15/36/57/78/99, top 1, baseline 38, aperture [12,40), travel 28); changed cells for the carry, hour and midnight cases with index 2 never set; one-minute guard including 23:59 -> 00:00 true and backward false; easing endpoints, monotonicity, e(0.5); the exact integer offset schedule; color lerp byte order (k = 0.5 -> 0x008A8989); dimmer-first draw order; 4-row gap invariant; ms-to-next-second clamp; motion-enabled truth table; Save preserves the persisted flag. Manual: resting screenshot diff = 0, AOT publish with no new warnings, watch one carry and one hour change.

Rough size: 250-350 lines across `WidgetWindow.cs`, `Native/NativeMethods.cs`, two new Services files, `PositionStore.cs`, tests, release notes.

## What the adversarial pass changed

Upheld when reproduced: the measured geometry, the zero-pixel resting diff for sixteen strings, whole-pixel translation invariance of GDI glyphs, the 4-row gap invariant, ClearType fringes shrinking as text dims (keep `CLEARTYPE_QUALITY` for the life of the window), the kick-and-detent arithmetic, the timer measurements, the PerMonitorV2 manifest meaning nothing is bitmap-scaled.

Refuted or corrected:

- "The clipped edge never reads as a hard cut" is false as worded. Dim tracks displacement, so a glyph missing 2 rows is still at ~90% ink; the cut is softest in the middle third and near full contrast at both ends. It reads as an odometer in motion, but the promise is gone and `DimCurve` is exposed as a tunable.
- `SetTimer` pacing would show a small snag most minutes (64/60 beat). Replaced by `DwmFlush` pacing.
- Skipping `KeepAboveTaskbar` during a roll protects nothing visible and lengthens the time an auto-hide taskbar covers the widget. Removed.
- Visible motion is ~200 ms, not ~240 ms.
- A 40 ms lag is a cue for a direct viewer, not a "wave" peripheral vision notices; 30 vs 40 ms is not a perceptual boundary. Kept as a tunable, not a differentiator.
- The easing is not "approximately cubic-bezier(0.4, 0, 0.2, 1)"; describe it by its own properties.
- "Off means off" under reduced motion is right because Windows removes its own fades when the switch is off, not because of photosensitivity.
- Read `SPI_GETUIEFFECTS` as a master switch alongside `SPI_GETCLIENTAREAANIMATION`.
- The menu item must show why it is unchecked ("following Windows: off") or the feature is a discoverability failure on the owner's own machine.
- The animations-off reading is live but not persisted (registry preference says on), so it is not attributed to the owner, the flag is re-read at every minute boundary instead of cached, and the notes say "Windows currently reports client-area animations disabled".
- The color-lerp unit-test oracle (`0x008A8989` at k = 0.5) is only valid once the rounding rule is pinned; truncation and banker's rounding give 0x8A, away-from-zero gives 0x8B in the blue channel.
- Resource ordering: the back buffer must exist and hold a rendered frame before the first `WM_PAINT`, and the memory DC must be deleted before its bitmap.
- The kerning rationale was wrong (right conclusion): GDI applies no kern pairs to simple-script text at all, so no font-specific assumption belongs in the docs.
- Geometry notes must use one coordinate frame: colon ink is window x 64..70 (cell-relative columns 7..13), rows 20..37; digit ink is cell-relative columns 1..20, rows 14..37.
- The owner's system-wide animations-off setting is evidence about tolerance for ambient motion: ship quiet defaults (EdgeDim 0.7-0.8, never strict odometer by default) and make a one-week check an explicit acceptance test. If minute changes pull the eye while working, step down in this order without a redesign: EdgeDim 0.8, then lag 0, then duration 220 ms, then the slide-fade constants (travel 10).

## Open decisions for the owner

1. **Default behavior.** Follow the Windows "Animation effects" switch (so on this machine, as it currently reports, it starts off, with the right-click item to enable it), or animate by default regardless of that setting?
2. **Carry lag.** Keep the 40 ms right-to-left lag (an hour change takes 360-400 ms) or roll all changing digits together (every change exactly 280 ms)?

## Next step

On approval, write the design spec under `docs/superpowers/specs/` and then an implementation plan. No code has been written for this feature.
