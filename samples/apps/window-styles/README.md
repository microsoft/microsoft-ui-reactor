# Window Styles Playground

Interactive demo of every windowing property added in
[spec 054](../../../docs/specs/054-windowing-evolution.md). Open the app
and tweak controls in a Windows 11 settings-page UI — every change is
applied live to the window hosting the demo.

## What's exposed

| Section | Property | Control |
| --- | --- | --- |
| Identity | `WindowSpec.Title` | TextBox |
| Identity | `TaskbarItem.Description` (thumbnail tooltip) | TextBox |
| Style & chrome | `WindowSpec.Style` (Default / None / ToolWindow) | ComboBox |
| Style & chrome | `WindowSpec.CornerStyle` (Default / Square / Rounded / RoundedSmall) | ComboBox |
| Style & chrome | `WindowSpec.ExtendsContentIntoTitleBar` | ToggleSwitch |
| Behavior | `WindowSpec.ResizeMode` (CanResize / CanMinimize / NoResize) | ComboBox |
| Behavior | `WindowSpec.Level` (Normal / Floating / AlwaysOnTop) | ComboBox |
| Behavior | `WindowSpec.IsMovableByBackground` | ToggleSwitch |
| Behavior | `WindowSpec.ShowInTaskbar` | ToggleSwitch |
| Behavior | `WindowSpec.ShowInSwitcher` | ToggleSwitch |
| Sizing | `WindowSpec.SizeToContent` | ComboBox |
| Sizing | `WindowSpec.AspectRatio` (Unlocked / 1:1 / 4:3 / 16:9 / 21:9 / 3:4) | ComboBox |
| Sizing | `ReactorWindow.CenterOnScreen()` | Button |
| Sizing | `ReactorWindow.BeginDragMove()` | Button |
| Appearance | `BackdropKind` (None / Mica / MicaAlt / DesktopAcrylic / AcrylicThin / Transparent) | ComboBox |
| Appearance | `ReactorWindow.SetOpacity(double)` | Slider |

Plus a live status footer with `Size`, `Position`, `DPI`, and `State`
read back via `UseWindowSize()`, `UseWindowPosition()`, and the window
object itself.

`IgnorePointerInput` (click-through) is intentionally **omitted** — a
playground UI you can't click is the worst kind of demo. The runtime
API (`ReactorWindow.SetIgnorePointerInput(true)` while `Opacity < 1.0`)
is still available.

## Run

```powershell
dotnet run --project samples/apps/window-styles -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

## Design notes

- **Settings-card layout** — each section is a rounded `Border` (8 DIP
  corners) with a translucent fill that picks up the Mica backdrop
  below. Matches the Windows 11 Settings app pattern: title +
  description on the left, control on the right, hairline separators
  between rows.
- **Declarative + imperative.** Most properties round-trip through
  `ReactorWindow.Update(WindowSpec)` so the spec diff applies only what
  changed. `Opacity` uses the targeted `SetOpacity` setter; `Backdrop`
  uses the declarative `.Backdrop(kind)` root modifier (the
  WinUI-native path).
- **`AspectRatio` ⊕ `SizeToContent`.** The spec forbids combining
  them; the playground disables the aspect-ratio picker (label text
  changes) whenever Size-to-content is anything other than `Manual`.
- **`WindowStyle.None` warns by spec** when `IsMovableByBackground`
  is false (you'd have a draggable-from-nowhere window). The warning
  is logged; the toggle works fine independently.
