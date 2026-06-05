# Command Palette Window

PowerToys Run-style windowing sample for spec 054 Phase 4.

This sample demonstrates the spec §2 G5 ≤7-fields-shape promise for common window recipes. The command palette uses exactly seven `WindowSpec` fields:

1. `Style`
2. `IsMovableByBackground`
3. `Level`
4. `ShowInTaskbar`
5. `ShowInSwitcher`
6. `StartPosition`
7. `CornerStyle`

Inline `WindowSpec` from `Program.cs`:

```csharp
ReactorApp.OpenWindow(new WindowSpec
{
    Style = WindowStyle.None,
    IsMovableByBackground = true,
    Level = WindowLevel.AlwaysOnTop,
    ShowInTaskbar = false,
    ShowInSwitcher = false,
    StartPosition = WindowStartPosition.CenterOnCurrent,
    CornerStyle = WindowCornerStyle.Rounded,
}, () => new PaletteApp(commands));
```

Highlights:

- borderless `WindowStyle.None`
- movable by dragging the background
- always-on-top command surface
- hidden from taskbar and Alt+Tab switcher
- centered on the current display
- rounded corners

*Screenshot: not bundled — run the sample locally to see it. Reactor samples do not currently ship screenshots.*

## How to run

```powershell
dotnet run --project samples/apps/command-palette-window -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

## AOT publish

```powershell
dotnet publish samples/apps/command-palette-window -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:PublishAotInternal=true -c Release
```
