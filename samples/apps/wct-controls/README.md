# WCT × Reactor — Control Gallery

A gallery app (modelled on the Windows Community Toolkit sample app) that turns ~25 real **Windows Community Toolkit** controls into first-class Reactor elements using `Reactor.Wrappers.Generator` — **no hand-written wrapper or handler code**. A `NavigationView` lists the controls; each gets its own self-contained page (its own Reactor `Component`), so its demo state is isolated and it mounts/unmounts as you navigate.

## Run

```powershell
dotnet run --project samples/apps/wct-controls -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

(Needs a desktop Windows session — it opens a real WinUI 3 window. `CameraPreview` degrades gracefully when no camera is available, reporting through `PreviewFailed`.)

## What it demonstrates

The point of the sample is the **wrapper generator**. Each `[GenerateReactorWrapper(typeof(...))]` partial record in [`WctControls.cs`](WctControls.cs) names a concrete WCT control type (e.g. `typeof(CommunityToolkit.WinUI.Controls.SettingsCard)`) and is filled in by the source generator with one init-property per surfaced control property, child/items slots, `On{Event}` callbacks, a `ControlDescriptor`, Pattern-A registration, and a parameterized factory method named after the control. The interesting controls each highlight a different wrapper annotation:

| Control | Annotation shown | Why |
| --- | --- | --- |
| `SettingsCard` | `Exclude = new[]{ "CommandParameter" }` | drop the meaningless inherited `ButtonBase.CommandParameter` prop (the content slot is already `Content`) |
| `Segmented` | `[WrapControlled("SelectedIndex", ChangedEvent = "SelectionChanged")]` | two-way bind a value whose change event doesn't follow the `{Prop}Changed` convention |
| `CameraPreview` | `[WrapLifecycle]` + `[WrapEvent("PreviewFailed", Arg = "Error")]` | declare the imperative start/stop lifecycle once; project typed event args into a callback |
| `TokenizingTextBox`, `RichSuggestBox` | `Exclude = new[]{ "Items" }` | opt out of the auto items slot for an internally-managed collection |
| `RadialGauge`, `ColorPicker` | (none) | the `{Prop}Changed` convention auto-pairs `Value` / `Color` as two-way controlled props |
| `RangeSelector` | (none — see `WctControls.cs`) | two range thumbs share one `ValueChanged`, so they can't both be `[WrapControlled]` |
| layout panels (`UniformGrid`, `DockPanel`, `WrapPanel`, `StaggeredPanel`, `ConstrainedBox`) | (none) | children flow through the generated panel / items slot |

See the comments in [`WctControls.cs`](WctControls.cs) for the full rationale on every wrapped control.

## Layout

- `WctControls.cs` — the wrapped-control declarations (the generator's input).
- `Program.cs` — the `NavigationView` gallery shell plus one demo page per control.
- `SettingsReferencePage.cs` — the "Settings reference" page: fixed `SettingsCard` / `SettingsExpander` content for the spec 065 reference screenshots, with a light/dark switch that sets `RequestedTheme` on the app root. See [`docs/specs/065/capture-runbook.md`](../../../docs/specs/065/capture-runbook.md).
