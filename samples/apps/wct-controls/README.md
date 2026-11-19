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
| `CameraPreview` | `[WrapLifecycle]` + `[WrapEvent("PreviewFailed", Arg = "Error")]` | start after `Loaded`, serialize stop/capture cleanup with startup, and project failures into a callback |
| `TokenizingTextBox`, `RichSuggestBox` | `Exclude = new[]{ "Items" }` | opt out of the auto items slot for an internally-managed collection |
| `RadialGauge`, `ColorPicker` | (none) | the `{Prop}Changed` convention auto-pairs `Value` / `Color` as two-way controlled props |
| `RangeSelector` | `[WrapControlled]` on `RangeStart` and `RangeEnd`, both using `ValueChanged` | observe pointer-driven endpoint changes and reassert supplied parent state without reconciliation echoes; keyboard limitation below |
| `SwitchPresenter` | (none) | populate the native `SwitchCases` collection and drive `Value`; leave the generated content slot unset so Toolkit owns selection |
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
Omitted `isExpanded` and `isClickEnabled` remain `Optional<bool>.Unset`, not
explicit `false`: the Toolkit owns omitted expansion state, and styles can
provide an omitted click-enabled value.

The shared native `Header` property has one lifecycle owner in `Customize`, using
the public mount/reconcile/unmount methods. It is intentionally not also a
generated slot: a generated header slot and a separate string writer would race
when switching from a rich header back to text.
An unrelated update leaves a native/style/imperative header alone when both
header inputs remain unset. Only a previous Reactor `Header` supplies a child to
reconcile; removing a previously supplied rich or text header releases its value.
Ownership is stored on the native control independently of its current `Header`.
If native code replaces or clears that property, a later rich-header update
reconciles the original child without losing its state. Removal and teardown
unmount only that owned child, once, and clear the native property only if it
still holds the owned value. Native replacements, including those installed by
`.OnUnmount(...)`, are left alone.

## Pointer-controlled range scenario

Launch with `-- --page range`. Drag each thumb with the pointer. With
**Accept range changes** enabled, the corresponding parent endpoint and callback
count change. Disable it and repeat: the callback still records the request, but
the thumb returns to the parent's value. The other endpoint's callback count must
not increase.

**Keyboard control is not supported by this mapping.** In the pinned Toolkit
`8.3.260402-preview2`, the [thumb key handlers](https://github.com/CommunityToolkit/Windows/blob/39c45efed2601b22170b9f6258c9cbe7e51031d1/components/RangeSelector/src/RangeSelector.Input.Key.cs)
write `RangeStart`/`RangeEnd` without raising `ValueChanged`. Arrow keys can
therefore move the native thumbs without updating the parent or callback counts.
A later parent update reasserts the supplied values, but rejection is not immediate.
Do not use keyboard edits to verify this pointer-driven accept/reject scenario.

**Reset to 20–80** writes parent state without incrementing callback counts.
**Rerender unchanged range** changes only Width by 1 px, making an unchanged-parent
reconciliation observable. If native values have drifted, this restores the parent
range; one-way endpoint properties would not. Native property assignments alone
are not evidence of user-input events.

Selectors are `Toolkit.RangeSelector`, `Range.AcceptChanges`, `Range.Reset`,
`Range.Rerender`, `Range.Parent`, `Range.Callbacks`, `Range.LastChange` and
`Range.ParentUpdates`. Omitted endpoints remain uncontrolled (`Optional.Unset`);
the example supplies both endpoints deliberately.

## ContentSizer target

The ContentSizer page keeps the Toolkit control and explicitly targets the colored
sibling above the bar. The sibling's `.Ref(...)` is populated before the sizer's
`.OnMount(...)` assigns `TargetControl`; reading the ref during `Render` would be too
early. `.OnUnmount(...)` clears the target, and navigating back creates a fresh
mapping. The horizontal bar changes the sibling's height, not the containing
stack's explicit height (which remains automatic). The stack still measures to fit
its resized content. No extra size limits or orientation switches are imposed by
this sample.

## SwitchPresenter

Open `-- --page swp`. Both presenters receive the selected `Value` and real native
`CaseCollection` entries through `SwitchCases`. Loading, Ready and Error match a
`Case`; Unknown selects the first presenter's default case and clears the second
presenter's content. Select Ready again to see both presenters update in place.

The collections and their native `TextBlock` content are memoized for the page's
lifetime. Toolkit alone chooses `CurrentCase` and assigns its `Content`; the page
does not compute a selected Reactor child. Leave the generated `content` parameter
unset. Reactor preserves native content when neither render declares a child;
supplying a Reactor child would instead let its child strategy replace Toolkit's
selection. No descriptor customization or presenter reimplementation is needed.

## TokenizingTextBox

The TokenizingTextBox page filters its existing action list by case-insensitive
substring as text changes (`cal` offers only `Calendar`). An unmatched query has
no suggestions but can still become a free-text token with a comma. Clearing the
query restores the full list. Filtering changes `SuggestedItemsSource`, never the
Toolkit-owned token `Items` collection.

## RichSuggestBox

Type `@al` to filter the sample names to **Alex Wilber**, then press Down and Enter
to insert an `@Alex Wilber` token. Matching is case-insensitive; a query such as
`@zzzz` has no suggestions. A bare `@` offers all eight names.

The generated `OnSuggestionRequested` callback updates a page-owned observable
suggestion source synchronously, before the Toolkit decides whether to open its
popup. It uses `ObservableCollection<object>` with string entries, reusing the
WinRT collection projection that Reactor roots for NativeAOT.
`OnSuggestionChosen` supplies the chosen name as `DisplayText` (the Toolkit
adds the prefix) and updates the visible last-choice/count caption. The Toolkit
still owns the rich document and token collection; the excluded `Items` slot is
not written. Navigating away and back starts a fresh page.

## Camera lifecycle

The sample owns one Toolkit `CameraHelper` per preview. It starts only after
`Loaded` has applied the template, so the task returned by `StartAsync(helper)`
covers initial capture setup rather than leaving it to Toolkit's `async void`
`OnApplyTemplate`. Unmount before loading never opens a camera. Unmount during
startup waits for that task, calls `Stop()` to release playback, and awaits
`CameraHelper.CleanUpAsync()` to release the frame reader and capture resources.
A final `Stop()` also clears playback restored by an in-flight Toolkit camera
switch. Before that final stop, cleanup waits for a low-priority dispatcher item,
so the switch's already-queued UI continuation runs first rather than creating
playback or raising `PreviewFailed` after the listener is removed. Rapid navigation back waits for the previous preview's cleanup. Each
preview joins that queue when it mounts, not when it unmounts, so a replacement
mounted before its predecessor unmounts still waits for the earlier release.
The camera page also requests the same idempotent cleanup from `UseEffect`, since
host disposal runs effect cleanup but does not guarantee native `OnUnmount`
callbacks. Completion of asynchronous cleanup still requires a running dispatcher.
The page lifetime guards late callbacks before requesting cleanup. Its preview
key survives ordinary renders but changes when hot reload resets hook state, so
recovery mounts a fresh preview instead of keeping a stopped native control.
Teardown disables the abandoned native preview before cleanup, preventing its
camera-switch button from restarting capture if a disposed host leaves it visible.

Toolkit failure results and lifecycle exceptions are written to stderr. While
mounted, exceptions also reach the current `OnPreviewFailed` callback; callbacks
are not invoked after unmount. A failed start remains owned until unmount, so
native camera-switch retries remain covered by the same cleanup. This adapter
uses public Toolkit APIs, not a replacement capture
implementation, and does not change device permissions.
The diagnostic failure listener remains attached until pending startup and cleanup
complete, including when a denied/no-device result arrives after page disposal.
Cleanup completion alone is not proof of release. If capture cleanup, final
playback shutdown, or listener removal fails, the sample retains that lifetime
and blocks new previews with a reported error until the sample is restarted.
Ordinary start/preview failures do not block re-entry after successful cleanup.

Live validation requires a provisioned, permission-approved camera. Open the
`camera` page, verify actual frames, navigate away during startup and after
preview begins, verify release, then return and verify a fresh preview. Also
exercise the camera-switch button while navigating away on multi-camera devices.
No-device/denied results and headless lifecycle tests do **not** prove live
preview, device release, or restart. The sample uses the Toolkit's default
template; arbitrary re-templating while capture is active is not coordinated by
its public API. Toolkit operations that never complete cannot be cancelled by
this adapter.

## Layout

- `WctControls.cs` — the wrapped-control declarations (the generator's input).
- `CameraPreviewLifetime.cs` — the camera's asynchronous lifecycle ordering,
  source-linked into the headless unit tests.
- `Program.cs` — launch options and application entry point.
- `SamplePages.cs` — the gallery shell and pages, shared route inventory
  (`Id`, `NativeTypeName`) and `SamplePages.CreatePage(route)` selector. Validation
  hosts can link this file together with `WctControls.cs` and
  `CameraPreviewLifetime.cs` instead of maintaining
  test-only wrapper or page copies.
