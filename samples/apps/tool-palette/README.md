# Tool Palette

Photoshop-style tool palette sample for spec 054 Phase 4.

This sample demonstrates the spec §2 G5 ≤7-fields-shape promise for common window recipes. The owned tool palette uses exactly seven `WindowSpec` fields:

1. `Title`
2. `Width`
3. `Height`
4. `Style`
5. `Level`
6. `CornerStyle`
7. `Owner`

Inline `WindowSpec` from `Program.cs`:

```csharp
var main = ReactorApp.OpenWindow(new WindowSpec
{
    Title = "Tool Palette Main",
    Width = 720,
    Height = 480,
    StartPosition = WindowStartPosition.CenterOnPrimary,
}, () => new MainWindow());

ReactorApp.OpenWindow(new WindowSpec
{
    Title = "Tools",
    Width = 220,
    Height = 320,
    Style = WindowStyle.ToolWindow,
    Level = WindowLevel.Floating,
    CornerStyle = WindowCornerStyle.RoundedSmall,
    Owner = main,
}, () => new ToolsWindow());
```

The app opens a normal main window and an owned floating tool window. `Owner = main` makes the tool palette owned by the main canvas window, while `WindowStyle.ToolWindow` gives it tool-window chrome and `WindowLevel.Floating` keeps it above the owner for quick access.

*Screenshot: not bundled — run the sample locally to see it. Reactor samples do not currently ship screenshots.*

## How to run

```powershell
dotnet run --project samples/apps/tool-palette -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

AOT publish is optional for this sample and intentionally skipped; the command-palette-window sample covers the Phase 10 AOT publish gate.
