# Native Standalone Build Notes

This document explains what changed to turn UTC Clock Widget from a WinUI 3 / Windows App SDK app into a standalone native Windows executable.

The native work lives on branch `feature/native-standalone-exe` and was first committed as `92681c0 refactor: replace WinUI app with native standalone widget`.

## Goal

The goal was to publish a single `utc-clock.exe` that can be copied and run without carrying a publish folder full of WinUI, Windows App SDK, and .NET runtime files.

The native version still keeps the important user behavior:

- UTC time in `HH:mm` format
- always-on-top widget window that reasserts itself above the taskbar when parked in its strip
- no taskbar button
- drag to move
- right-click `Reset Position` and `Exit`
- persisted position under `%LOCALAPPDATA%\UtcClockWidget`
- `--reset` launch option
- per-user Startup Apps registration for Release launches

## Implementation Changes

### Removed WinUI and MSIX packaging

The native branch removes the WinUI and MSIX app surface:

- `App.xaml` / `App.xaml.cs`
- `MainWindow.xaml` / `MainWindow.xaml.cs`
- `MainPage.xaml` / `MainPage.xaml.cs`
- `Package.appxmanifest`
- unused MSIX logo and splash assets
- Windows App SDK package references from `utc-clock.csproj`

This removes the Windows App SDK / WinUI runtime dependency and avoids the unpackaged WinUI bootstrap path.

### Added a native Win32 shell

The native branch adds:

- `Program.cs` as the process entry point
- `WidgetWindow.cs` as the Win32 message-loop window implementation
- expanded `Native/NativeMethods.cs` for the required Win32/GDI APIs

`WidgetWindow` owns the window class, message loop, painting, timer, drag handling, context menu, and shutdown. The widget is painted directly with GDI instead of XAML.

### Enabled Native AOT publish

`utc-clock.csproj` now enables Release Native AOT publishing:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <SelfContained>true</SelfContained>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
  <DebugType>none</DebugType>
  <DebugSymbols>false</DebugSymbols>
</PropertyGroup>
```

The publish command is:

```powershell
dotnet publish utc-clock.csproj -c Release -o .\publish
```

The output is:

```text
publish\utc-clock.exe
```

### Made JSON persistence AOT-safe

`PositionStore` now uses a source-generated `System.Text.Json` context:

```csharp
[JsonSerializable(typeof(PositionStore.PositionDto))]
internal sealed partial class PositionJsonContext : JsonSerializerContext
{
}
```

That avoids reflection-based JSON serialization paths that produce Native AOT warnings and may break under trimming.

## Runtime Requirements

| Area | WinUI / Windows App SDK release | Native standalone release |
|---|---|---|
| User install shape | Zip containing many files that must stay together | One `utc-clock.exe` |
| .NET runtime on target machine | Not required for the measured release because the publish output carried .NET runtime files | Not required; Native AOT includes the needed runtime support in the native executable |
| Windows App SDK runtime | Required or bootstrapped for WinUI / Windows App SDK features | Not required |
| WinUI runtime files | Present in the release folder | Not used |
| Startup registration | Release app writes an HKCU `Run` value | Same behavior |
| Target OS | Windows 10 build 17763 or newer; Windows 11 recommended | Same target OS |
| Build machine | .NET SDK plus Windows App SDK dependencies | .NET SDK plus Native AOT toolchain support |

Native AOT publishes ahead-of-time compiled native code. Microsoft documents that Native AOT apps can run on machines without the .NET runtime installed and that `<PublishAot>true</PublishAot>` enables AOT compilation during publish.

Windows App SDK apps have a different deployment model. Microsoft documents that framework-dependent Windows App SDK apps depend on the Windows App SDK runtime being present, and unpackaged apps use the bootstrapper path to load that runtime.

Sources:

- https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/
- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deployment-architecture
- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps

## Measured File Sizes

Measured on June 30, 2026 from local publish outputs and the existing GitHub `v1.0.0` release.

| Build | Distribution shape | File count | Size |
|---|---:|---:|---:|
| WinUI `v1.0.0` release zip | `utc-clock-v1.0.0-win-x64.zip` | 1 zip | 32,633,452 bytes, about 31.12 MiB |
| WinUI `v1.0.0` extracted publish folder | extracted files | 48 files | 96,139,964 bytes, about 91.69 MiB |
| Native AOT publish output | `utc-clock.exe` | 1 file | 2,788,864 bytes, about 2.66 MiB |
| Native AOT zipped executable | `utc-clock.exe` in a zip | 1 zip | 1,148,890 bytes, about 1.10 MiB |

Practical effect:

- Native zip is about 96.5% smaller than the original WinUI release zip.
- Native extracted executable is about 97.1% smaller than the original extracted WinUI publish folder.
- Users can move or copy one file instead of preserving a whole publish directory.

## Tradeoffs

The native version is much smaller and easier to distribute, but it trades away WinUI conveniences:

- UI is hand-drawn with GDI rather than XAML.
- Window behavior is implemented with direct Win32 messages.
- Future visual changes require Win32/GDI work instead of XAML styling.
- Native AOT is stricter about reflection, dynamic code, and serialization patterns.
- Published binaries are runtime-identifier specific; build separate artifacts for x64, ARM64, or x86 as needed.

For this app, the tradeoff is favorable because the UI is intentionally tiny: a small always-on-top clock, drag behavior, and a two-item context menu.

## Verification Commands

```powershell
dotnet run --project UtcClockWidget.Tests\UtcClockWidget.Tests.csproj
dotnet build utc-clock.csproj -c Debug
dotnet publish utc-clock.csproj -c Release -o D:\tmp\utc-clock-native-final2
```

The verified Native AOT output was:

```text
D:\tmp\utc-clock-native-final2\utc-clock.exe
```

SHA256:

```text
4A0A84ADE81BC13BA5E6183BCF1DDCCBC4405D655DA2296C58BBFE832F7BA2E3
```