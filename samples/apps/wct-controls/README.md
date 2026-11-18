# WCT × Reactor — Control Gallery

A gallery app (modelled on the Windows Community Toolkit sample app) that wraps 25 real **Windows Community Toolkit** controls using `Reactor.Wrappers.Generator` and small public adapters. A `NavigationView` lists the controls; each gets its own self-contained page (its own Reactor `Component`), so its demo state is isolated and it mounts/unmounts as you navigate.

The sample deliberately qualifies each wrapper factory (`SettingsCardElement.SettingsCard`,
for example). These remain Toolkit controls even when Reactor offers a similarly named
built-in factory.

## Run

```powershell
dotnet run --project samples/apps/wct-controls -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

Select a page directly with `-- --page range` (or any ID in
`SamplePages.Routes`). Unknown routes fail rather than opening a different page.
Each page root has the automation ID `Page.<ControlName>`.

(Needs a desktop Windows session — it opens a real WinUI 3 window. `CameraPreview` degrades gracefully when no camera is available, reporting through `PreviewFailed`.)

## What it demonstrates

The point of the sample is the **wrapper generator**. Each `[GenerateReactorWrapper(typeof(...))]` partial record in [`WctControls.cs`](WctControls.cs) names a concrete WCT control type (e.g. `typeof(CommunityToolkit.WinUI.Controls.SettingsCard)`) and is filled in by the source generator with one init-property per surfaced control property, child/items slots, `On{Event}` callbacks, a `ControlDescriptor`, Pattern-A registration, and a parameterized factory method named after the control. The interesting controls each highlight a different wrapper annotation:

| Control | Annotation shown | Why |
| --- | --- | --- |
| `SettingsCard` | `[WrapElementSlot]` on `Description`, `HeaderIcon`; `[WrapManual]` for rich/text header precedence | reconcile rich header/description/icon Elements alongside the main content child |
| `SettingsExpander` | `[WrapElementSlot]` on `Description`, `Content`, `HeaderIcon`; manual rich/text header; `[WrapControlled]` for `IsExpanded` | reconcile header children alongside items and keep expansion state in the parent via `Expanded` / `Collapsed` |
| `Segmented` | `[WrapControlled("SelectedIndex", ChangedEvent = "SelectionChanged")]` | two-way bind a value whose change event doesn't follow the `{Prop}Changed` convention |
| `CameraPreview` | `[WrapLifecycle]` + `[WrapEvent("PreviewFailed", Arg = "Error")]` | declare the imperative start/stop lifecycle once; project typed event args into a callback |
| `TokenizingTextBox`, `RichSuggestBox` | `Exclude = new[]{ "Items" }` | opt out of the auto items slot for an internally-managed collection |
| `RadialGauge`, `ColorPicker` | (none) | the `{Prop}Changed` convention auto-pairs `Value` / `Color` as two-way controlled props |
| `RangeSelector` | (none — see `WctControls.cs`) | two range thumbs share one `ValueChanged`, so they can't both be `[WrapControlled]` |
| layout panels (`UniformGrid`, `DockPanel`, `WrapPanel`, `StaggeredPanel`) | (none) | children flow through the generated panel slot; the DockPanel example uses the Toolkit's default left docking |
| `ConstrainedBox`, `LayoutTransformControl` | (none) | a single reconciled content child; both controls affect layout |

See the comments in [`WctControls.cs`](WctControls.cs) for the full rationale on every wrapped control.

Object-valued properties are raw native values unless mapped with `[WrapElementSlot]`.
Passing a Reactor `Element` to an unmapped `object` property does **not** mount it.
The Settings wrappers provide string-header overloads that retain a native string
`Header` (stored as `HeaderText` on the element). That preserves the Toolkit's own
accessible naming, including its non-clickable group versus clickable button
behavior, rather than blanket-naming every root and child. An Element `Header`
takes precedence over `HeaderText`. Rich-header callers must provide appropriate
`.AutomationName(...)` values and label interactive content; custom names are
never replaced by the helpers.

The shared native `Header` property has one lifecycle owner in `Customize`, using
the public mount/reconcile/unmount methods. It is intentionally not also a
generated slot: a generated header slot and a separate string writer would race
when switching from a rich header back to text.

## Layout

- `WctControls.cs` — the wrapped-control declarations (the generator's input).
- `Program.cs` — launch options and application entry point.
- `SamplePages.cs` — the gallery shell and pages, shared route inventory
  (`Id`, `NativeTypeName`) and `SamplePages.CreatePage(route)` selector. Validation
  hosts can link this file together with `WctControls.cs` instead of maintaining
  test-only wrapper or page copies.
