# UTC Clock Widget

A small standalone native Windows desktop widget that shows the current UTC time.

The widget is intentionally minimal: it stays on top, hides from the taskbar,
can be dragged around the desktop, remembers its last position, and exposes only
a right-click menu for the minute animation, reset and exit.

## Features

- Displays UTC time in `HH:mm` format
- Animates the digits that change at each minute boundary with a damped odometer roll (follows the Windows "Animation effects" setting; right-click to override)
- Always-on-top, chrome-less Win32 widget
- Stays above the taskbar when parked in the taskbar strip, including an auto-hide taskbar
- Single-file native Release executable via .NET Native AOT
- Drag-to-position with persistence under `%LOCALAPPDATA%\UtcClockWidget`
- Right-click menu with `Animate minute change`, `Reset Position` and `Exit`
- `--reset` launch option to restore the default position
- Release builds register the app as a per-user Windows startup app

## Requirements

- Windows 10 build 17763 or newer; Windows 11 recommended
- .NET 10 SDK to build from source

## Usage

Download `utc-clock.exe` from a release and run it. On first Release launch, the
app registers itself under the current user's Windows Startup Apps.

Right-click the widget for `Animate minute change`, `Reset Position` and `Exit`.
The animate item follows the Windows "Animation effects" setting until you tick
or untick it; your choice is remembered next to the saved position.

## Commands

```powershell
dotnet build
dotnet run --project utc-clock.csproj
dotnet run --project utc-clock.csproj -- --reset
dotnet publish utc-clock.csproj -c Release -o .\publish
dotnet run --project UtcClockWidget.Tests\UtcClockWidget.Tests.csproj
```

The Release publish output is a standalone native executable:

```text
publish\utc-clock.exe
```

## Native Build Notes

For details on the native conversion, runtime requirements, size comparison, and tradeoffs, see [Native Standalone Build Notes](docs/native-standalone.md).

## Project Structure

- `Program.cs` - application entry point
- `WidgetWindow.cs` - native Win32 widget window, painting, dragging, and menu
- `Services/` - launch option parsing, startup registration, saved-position logic, digit cell layout and the minute-roll timing math
- `Native/` - Win32 interop declarations
- `UtcClockWidget.Tests/` - lightweight tests for reusable logic