# UTC Clock Widget

A small WinUI 3 desktop widget for Windows that shows the current UTC time.

The widget is intentionally minimal: it stays on top, hides from the taskbar,
can be dragged around the desktop, remembers its last position, and exposes only
a right-click menu for reset and exit.

## Features

- Displays UTC time in `HH:mm` format
- Always-on-top, chrome-less Windows widget
- Drag-to-position with persistence under `%LOCALAPPDATA%\UtcClockWidget`
- Right-click menu with `Reset Position` and `Exit`
- `--reset` launch option to restore the default position

## Requirements

- Windows 10 build 17763 or newer; Windows 11 recommended
- .NET 10 SDK

## Commands

```powershell
dotnet build
dotnet run --project utc-clock.csproj
dotnet run --project utc-clock.csproj -- --reset
dotnet test UtcClockWidget.Tests\UtcClockWidget.Tests.csproj
```

## Project Structure

- `MainWindow.xaml` / `MainWindow.xaml.cs` - widget UI and window behavior
- `Services/` - launch option parsing and saved-position logic
- `Native/` - Win32 and DWM interop used for desktop-widget behavior
- `UtcClockWidget.Tests/` - lightweight tests for reusable logic
