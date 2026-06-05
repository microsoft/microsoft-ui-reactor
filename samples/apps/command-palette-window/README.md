# Command Palette Window

PowerToys Run-style windowing sample for spec 054 Phase 4.

Highlights:

- `WindowStyle.None`
- `IsMovableByBackground = true`
- `Level = WindowLevel.AlwaysOnTop`
- hidden from taskbar and switcher
- `StartPosition = WindowStartPosition.CenterOnCurrent`
- `CornerStyle = WindowCornerStyle.Rounded`

AOT smoke:

```powershell
dotnet publish samples/apps/command-palette-window -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:PublishAotInternal=true -c Release
```
