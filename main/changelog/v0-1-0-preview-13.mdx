---
title: "0.1.0-preview.13"
type: changelog
date: 2026-08-04
changelog:
  version: "0.1.0-preview.13"
description: "10 added, 4 changed and 25 fixed."
---

## Added

- **`TabView.FillContentArea` — opt-in full-height tab body (issue [#914](https://github.com/microsoft/microsoft-ui-reactor/issues/914)).**
  WinUI's `DefaultTabViewStyle` sets `VerticalAlignment="Top"` on the `TabView`
  control itself, so the `*` content row in its template never receives leftover
  space and tab content collapses to its natural height instead of filling the
  tab body. Reactor keeps the WinUI default; set
  `TabView([...]) with { FillContentArea = true }` or call `.FillContentArea()`
  to stretch the control so its content area fills the available space. An
  explicit `.VAlign(...)` on the `TabView` element still wins. Apps working
  around this with `UseWindowSize()` + `.MinHeight(...)` (including the Windows
  App SDK `reactor-tabview` template) can drop that workaround.

- **`NavigationView` pane-open change notification (issue [#916](https://github.com/microsoft/microsoft-ui-reactor/issues/916)).**
  `NavigationViewElement.OnPaneOpenChanged` plus the `.PaneOpenChanged(handler)`
  and paired `.IsPaneOpen(value, handler)` fluents report every `IsPaneOpen`
  change on the realized control, so pane state can be driven from component
  state. `SplitViewElement` gains the matching `.IsPaneOpen(value, handler)`
  overload. `ControlDescriptor.Immediate` now accepts a `null` `loadedHook` for
  DP observations that have no template part to walk.

- **Declarative title-bar height (issue [#917](https://github.com/microsoft/microsoft-ui-reactor/issues/917)).** `WindowSpec.TitleBarHeight` and
  `TitleBar(...).Tall()` / `.HeightOption(WindowTitleBarHeight)` ask for the tall
  (48 DIP) caption used whenever the title bar hosts navigation chrome. Sets both
  the system caption (`AppWindow.TitleBar.PreferredHeightOption`) and the WinUI
  title-bar control's own height — the control does not follow the caption, so
  raising one alone leaves them disagreeing. Applied after Reactor's own
  content-extension flip, so there is no ordering hazard and the native setter
  cannot throw `ERROR_INVALID_STATE`. An explicit `.Height(...)` wins over the
  implied 48, and an explicit `WindowSpec.TitleBarHeight` wins over the element's
  declaration.

- **`NavigationView` declarative surface completed (issue [#915](https://github.com/microsoft/microsoft-ui-reactor/issues/915)).** The element
  now covers the control without escape hatches: `IsBackButtonVisible`,
  `IsPaneToggleButtonVisible`, `IsPaneVisible`, `AlwaysShowHeader`,
  `IsTitleBarAutoPaddingEnabled`, `SelectionFollowsFocus`, `OverflowLabelMode`,
  `ShoulderNavigationEnabled` and `CompactPaneLength`; the `PaneHeader` and
  `ContentOverlay` slots; a `FooterMenuItems` list reconciled like `MenuItems`;
  and the `OnSettingsSelected`, `OnItemInvoked`, `OnDisplayModeChanged`,
  `OnItemExpanding` and `OnItemCollapsed` callbacks with matching
  `.SettingsSelected()` / `.ItemInvoked()` / `.DisplayModeChanged()` /
  `.ItemExpanding()` / `.ItemCollapsed()` fluents. A new
  `NavigationViewElement.SettingsTag` sentinel lets `SelectedTag` select the
  built-in settings item, and `.WithNavigation()` now routes it.

- **ToolTip positioning is declarative (spec 002 §1.6).** `.ToolTipPlacement(mode)`
  and `.ToolTipPlacementTarget(elementRef)` expose the two `ToolTipService`
  attached properties that previously had no modifier, so positioning a tooltip
  no longer needs a `.Set(fe => ToolTipService.SetPlacement(fe, …))` escape hatch.
  Paired overloads `.ToolTip(text, placement)` and
  `.WithToolTip(element, placement)` cover the common case in one call. The
  placement target rides the same deferred reference-edge machinery as the
  XYFocus refs, so the target does not have to mount first. This completes the
  `ToolTipService` surface — §1.6 now reads 7/7 exposed. (WinUI has no tooltip
  show/hide delay knobs; `InitialShowDelay`/`BetweenShowDelay` are WPF-only.)

- **`REACTOR_POOL_001` now covers attached-property writes (spec 060 §3.1).**
  The rule previously matched only assignment-shaped `.Set` writes
  (`.Set(fe => fe.Margin = ...)`), so an attached-property write — a static call
  such as `.Set(fe => AutomationProperties.SetName(fe, "Save"))` — was
  structurally invisible to it. `ElementPool.CleanElement` clears 29 further
  attached properties across `AutomationProperties`, `FlexPanel`,
  `ToolTipService` and `TitleBar.IsDragRegion`, all of which are silently
  discarded on pool reuse, so the rule's subject matter grows from 12 properties
  to 41. **Consumer builds that were clean may start reporting
  `REACTOR_POOL_001` on upgrade**; each report is a write that really is lost when
  the control is recycled. Matching is gated on the write's target being the
  lambda parameter itself and on the setter's owner resolving to the real WinUI
  (or Reactor) type, so a write to some other object — or to a lookalike type of
  the same name — stays silent. Most map 1:1 and ship a codefix
  (`AutomationProperties.SetName` → `.AutomationName(...)`,
  `ToolTipService.SetToolTip` → `.ToolTip(...)`,
  `TitleBar.SetIsDragRegion` → `.IsDragRegion(...)`); the rest are
  diagnostic-only because the modifier's shape differs —
  `.PositionInSet(position, size)` takes two values, `.Required()` takes none,
  and every `FlexPanel.*` property funnels into one `.Flex(grow: ...)`. Attached
  owners the pool does not clear (`Canvas`, `ScrollViewer`, `Grid`) are
  unaffected.

- **`ControlDescriptor.OnUnmount` / `.WithUnmount(...)` — descriptor teardown hook
  (spec 047 §6, issue [#949](https://github.com/microsoft/microsoft-ui-reactor/issues/949)).** The engine already dispatched unmount to hand-coded
  `IElementHandler` implementations, but `DescriptorHandler` never forwarded it, so
  descriptor authors had no teardown seam at all. A descriptor can now declare
  `.WithUnmount((in UnmountContext ctx, TControl c) => ...)` to invalidate
  control-scoped state the pool reset contract cannot see — a pending deferred
  write, a one-shot lifecycle subscription, a disposable. It is not for event
  trampolines, which anchor to the control's lifetime by design. Declaring the hook
  makes the handler force the control's `ReactorState` into existence at mount,
  because the engine's unmount dispatch is tag-gated; without that the hook would
  fire for callback-bearing elements of a type and silently not for callback-free
  ones.

- **`IsHitTestVisible(bool)` common element modifier (issue [#809](https://github.com/microsoft/microsoft-ui-reactor/issues/809)).** Fills the
  last gap among the sibling boolean visual-state modifiers (`IsEnabled`,
  `IsTabStop`, `IsVisible`, `Opacity`). Opting an element out of pointer
  hit-testing — an overlay that should not swallow input, a decorative adorner —
  no longer needs the imperative `.Set(c => c.IsHitTestVisible = false)` escape
  hatch, so the write participates in the property diff and is unwound on
  removal.

- **`ReactorBinding.ShouldSuppressEcho(UIElement)` — public read-side echo
  primitive (spec 062, issue [#163](https://github.com/microsoft/microsoft-ui-reactor/issues/163)).** The read-side counterpart of the existing
  `WriteSuppressed` write primitive. External wrapper authors — and the
  `[WrapControlled]` source generator, which now emits this instead of the
  internal `ChangeEchoSuppressor.ShouldSuppress` — can ask whether a change
  notification is Reactor's own echo and skip it, closing the one place the
  generator emitted an internal Reactor symbol into externally generated code.

- **`REACTOR_ITEMS_002` — `ItemsView` viewBuilder must return an `ItemContainer`
  root (spec 060).** A `viewBuilder` that returns a bare `Border(...)` (or any
  non-`ItemContainer` root) trips a framework guard at runtime and renders an
  error banner instead of the list. The analyzer now catches that shape at build
  time and points at `ItemContainer(...)`.

## Changed

- **`WithNavigation` gained an optional `settingsRoute` argument (binary-breaking,
  issue [#915](https://github.com/microsoft/microsoft-ui-reactor/issues/915)).** Call sites can now pass a fourth argument —
  `.WithNavigation(nav, routeToTag, tagToRoute, settingsRoute)` — to route the
  built-in settings item; passing nothing keeps the previous behaviour.
  Source-compatible: existing three-argument call sites compile unchanged. But
  optional arguments are baked in at the call site, so binaries compiled against
  `v0.1.0-preview.9`–`.12` still resolve the previous three-argument form and
  would need a recompile rather than a drop-in DLL swap. No back-compat overload
  was added: the project is pre-1.0 and explicitly reserves the right to change
  the public surface between releases, and a permanent duplicate overload is a
  worse public shape than one optional argument.

- **Charting, docking, markdown and the data grid moved into
  `Microsoft.UI.Reactor.Advanced` (breaking; spec 062 §7, issue [#163](https://github.com/microsoft/microsoft-ui-reactor/issues/163)).** Four
  heavy subsystems — charting (and its D3 layer), docking, markdown, and
  `DataGrid<T>` — no longer ship in the core `Microsoft.UI.Reactor` package. The
  dependency direction is one-way (`Reactor.Advanced → Reactor`, enforced by a
  new architecture test), and core `Reactor.dll` drops **~0.9 MB (≈28%)** — ~3.2
  MB to ~2.3 MB in Release — so apps that don't use these subsystems ship a
  smaller core. **Apps that do use them must reference
  `Microsoft.UI.Reactor.Advanced`**, which is versioned in lock-step with the
  framework and already referenced by the scaffolded template. Namespaces are
  preserved (`Microsoft.UI.Reactor.Charting[.D3]`, `.Docking`, `.Controls`,
  `.Markdown`) and the relocated event fluents stayed in their element's own
  namespace, so type references and `using`s are unchanged. The factory entry
  points `DataGrid`, `Column`, `AutoColumns` and `Markdown` now live in
  `Microsoft.UI.Reactor.Advanced.Factories`.

- **The OS now picks the default window size.** `WindowSpec.Width` / `Height` and
  the `ReactorApp.Run(width, height)` parameters defaulted to a hardcoded
  1024x768 inherited from the pre-spec-036 `Run` signature — never a design
  decision (spec 036 only ever discussed the units, never the value). Every app
  that did not care about window size was still overriding the size Windows
  would have picked. Those defaults are now unset, so the OS chooses; passing an
  explicit width/height behaves exactly as before.

- **Icon fonts now fall back through `Segoe Fluent Icons, Segoe MDL2 Assets`.**
  The legacy Windows 10-era `"Segoe MDL2 Assets"` font is no longer used on its
  own, and the `FontIcon` / `FontIconSource` fallbacks — which previously
  hardcoded a bare `"Segoe Fluent Icons"` with no Windows 10 tail — now share one
  `IconResolver.SymbolFontFallback` stack with the rest of the framework. Font
  allocations on that path also route through `WinRTCache` instead of crossing
  the managed/WinRT boundary on every render.

## Fixed

- **`ContentDialog` content now re-renders while the dialog is open
  (issues [#1069](https://github.com/microsoft/microsoft-ui-reactor/issues/1069), [#948](https://github.com/microsoft/microsoft-ui-reactor/issues/948)).** The dialog is side-mounted — `dialog.Content` is a
  Reactor subtree owned by the dialog object, not a visual child of the
  collapsed placeholder that stands in for it in the tree — and unlike `Flyout`
  and `Popup` there was no back-reference from the placeholder to the live
  dialog. Update could therefore never reach it, so state changed from inside an
  open dialog (or from the owner while it was open) was rendered into nothing:
  the dialog kept whatever it was mounted with until it was dismissed. The
  placeholder now tracks its live dialog, and update reconciles the content in
  place — so transient state inside the dialog such as focus, scroll and caret
  survives the owner's re-renders — and syncs `Title`, the button labels,
  `DefaultButton` and the enabled flags. Three consequences of the same gap are
  fixed with it: a declared `IsOpen = false` now closes the dialog instead of
  being inert (the `ContentDialog` half of [#948](https://github.com/microsoft/microsoft-ui-reactor/issues/948), and it raises `OnClosed` with
  `ContentDialogResult.None` exactly as `Popup` raises `Closed`); closing now
  unmounts the content subtree, so its `UseEffect` cleanups run instead of
  leaking on every open/close cycle; and unmounting a component while its dialog
  is open now tears the dialog down instead of leaving it on screen over a
  torn-down tree. Passing `null` for `SecondaryButtonText`/`CloseButtonText` on
  a re-render also removes that button now — previously the write was skipped,
  so an optional button could never be taken away once shown.

- **A pooled control no longer hands its previous renter's padding, background,
  border or enabled state to the next one (issue [#985](https://github.com/microsoft/microsoft-ui-reactor/issues/985)).**
  `Reconciler.ApplyModifiers` writes `Padding`, `CornerRadius`,
  `BorderThickness`, `BorderBrush`, `Background` and `IsEnabled` onto `Control`,
  `Border`, `Panel` and `StackPanel` receivers, but `ElementPool.CleanElement`
  only ever reset the `Border` arm. On mount there is no previous element, so no
  unset arm runs — a recycled `Button`, `ScrollViewer`, `Grid` or `VStack`
  therefore started life carrying the *local* values the last renter had set, and
  a local value outranks every `Style` setter in WinUI's dependency-property
  precedence order. `Panel.Background` was the widest hole, since `VStack` /
  `HStack` / `Grid` are all poolable. `CleanElement` now clears all six, in the
  `FrameworkElement`-common region so the pool ⇄ analyzer consistency invariants
  can see them. This is the pooling half of [#952](https://github.com/microsoft/microsoft-ui-reactor/issues/952), whose fix corrected the *shape*
  of the reset (`ClearValue` instead of assigning a default) but not its absence.
  `TextBlock.Padding` — reset since [#950](https://github.com/microsoft/microsoft-ui-reactor/issues/950), but from a case arm past the point the
  scanners stop reading — moved into the same region, so all four of the
  receivers `ApplyModifiers` writes `Padding` to are now covered by one chain and
  verified by the same invariants.

  User-visible fallout: those six properties are now marked `poolReset` in the
  analyzer's modifier table, so `.Set(c => c.Padding = …)` and friends report
  **`REACTOR_POOL_001` (Warning)** where they previously reported
  `REACTOR_MOD_002` (Info) — but only on a receiver `ElementPool` actually
  recycles (see the receiver-aware selection entry below); a `CheckBox` or
  `RelativePanel` keeps reporting `REACTOR_MOD_002`. The suggested fix is
  unchanged — use the fluent modifier (`.Padding(…)`) — and the provided code fix
  still applies it automatically. Projects building with `TreatWarningsAsErrors`
  may need to convert those call sites (or suppress the id) when upgrading.

- **`REACTOR_POOL_001` is no longer reported for receivers the pool does not
  recycle (issue [#1051](https://github.com/microsoft/microsoft-ui-reactor/issues/1051)).** Rule selection was `poolReset ? POOL_001 : MOD_002`,
  decided per *property* and blind to the receiver, while the control gates it
  reports through name inheritance roots — `Control` admits every WinUI control,
  `Panel` admits every panel. `ElementPool` matches on the *exact* runtime type
  (`PoolableTypes.Contains(element.GetType())`), so the two sets never agreed:
  `.Set(cb => cb.IsEnabled = false)` on a `CheckBox`, or
  `.Set(rp => rp.Background = brush)` on a `RelativePanel`, claimed *"is reset on
  pool return"* for a control that is never pooled. `PoolResetSetAnalyzer` now
  mirrors `ElementPool.PoolableTypes` exactly and falls back to
  `REACTOR_MOD_002` (Info) outside it — the hazard those receivers really do
  have, with the same advice and the same code fix. A parity test fails the build
  if the mirror and `PoolableTypes` ever diverge in either direction. The change
  is strictly de-escalating, and it also covers the twelve properties
  (`Margin`, `Width`, `Height`, `Min`/`Max` sizes, alignments, `Opacity`,
  `AccessKey`, `IsTabStop`) that had the same over-breadth before this release,
  and the attached-property half of the rule
  (`.Set(cb => AutomationProperties.SetName(cb, "Save"))`), which reported
  `REACTOR_POOL_001` unconditionally. Both halves now resolve poolability once
  from the `.Set` lambda parameter, so a body mixing an instance write with an
  attached one cannot report two different ids for the same receiver.
  Custom subclasses (`class MyButton : Button`) report `REACTOR_MOD_002` too,
  matching the pool, which does not recycle them either.

- **Accessibility environment hooks now report the real system value on the
  first render (issue [#983](https://github.com/microsoft/microsoft-ui-reactor/issues/983)).** `UseReducedMotion()`, `UseHighContrast()` and
  `UseHighContrastScheme()` seeded their state inside `UseEffect`, which does
  not run until after the first render commits. Every caller therefore rendered
  its first frame against the default — `false` / `null` — and a component that
  never re-renders kept that value for its whole lifetime. All three failed in
  the one direction an accessibility hook must not: the first paint reports
  "no accommodation needed", so it animates for exactly the users who asked it
  not to. Each hook now reads its value during render (guarded so the WinRT
  settings objects are still constructed once per component) and keeps the
  effect for change notifications, plus a re-read after subscribing so a
  preference flipped in the window between the two is not missed. Matches the
  seed-then-subscribe shape the other environment hooks in `RenderContext`
  already use.

- **Reduced motion now updates while the app is running (issue [#983](https://github.com/microsoft/microsoft-ui-reactor/issues/983)).**
  `UseReducedMotion()` and the charting host both listened on
  `UISettings.ColorValuesChanged`, which does not fire when Windows'
  "Animation effects" toggle changes — measured with a live subscription to both
  events, it fired zero times in either direction while
  `AnimationsEnabledChanged` fired on both. Because `UISettings.AnimationsEnabled`
  still *reads back* the new value, the wrong subscription was invisible to any
  static reading: the value was correct on first render and then frozen for the
  life of the process, so a user turning animations off had to restart the app to
  be taken at their word. Both listeners now subscribe to
  `AnimationsEnabledChanged`, gated behind an `ApiInformation` probe because that
  event needs Windows 10 2004 (19041) and Reactor's minimum is 17763; on older
  builds the existing `ColorValuesChanged` re-read remains as the fallback.

- **`DataGrid<T>`'s <kbd>Shift</kbd>+<kbd>Tab</kbd> now moves focus backward
  (issue [#987](https://github.com/microsoft/microsoft-ui-reactor/issues/987)).** The grid's routed `KeyDown` handler captured only the raw
  `VirtualKey` and then deferred dispatch through
  `DispatcherQueue.TryEnqueue`, so the modifier state was gone by the time the
  key was handled — `Shift+Tab` was indistinguishable from `Tab` and moved
  focus *forward* in all three modes (navigation, `EditMode.Cell`, and
  `EditMode.Row`). The handler now snapshots the modifiers synchronously,
  before the deferral, into an immutable `KeyChord` that is threaded through
  the dispatch, and each of the three Tab sites gained a backward arm. Cell
  edits commit exactly what `Tab` commits and move to the previous cell,
  reopening an editor there when that cell is editable; row edits walk the
  editable ring backward and still commit nothing.

- **`TeachingTip` declared `IsOpen: true` on its first render now actually opens
  (issue [#949](https://github.com/microsoft/microsoft-ui-reactor/issues/949)).** The mount-time write was issued and then silently dropped: WinUI
  only holds a pending open on an *unparented* `TeachingTip` while nothing else is
  written to it, and Reactor — like XAML — fully configures a control before handing
  it to its parent, so the later prop entries, setters, content slots and common
  modifiers all discarded it. Ordering the entry last was not enough, because most of
  those writes happen outside the descriptor. `IsOpen` is now a bespoke descriptor
  entry that defers a mount-time `true` to the control's `Loaded` event, the first
  moment the tip is parented into a live tree. Post-mount edges are unchanged and
  stay edge-triggered, so a re-render carrying the same declared `true` still does
  not re-assert against a natively dismissed tip. One consequence worth knowing: a
  `.Set(t => t.IsOpen = false)` can no longer override a declared mount-time `true`,
  because the deferred write lands after the setter pass — declare the value instead.
  The gallery's "TeachingTip (Title Only)" card, which declared a tip with no
  `IsOpen` and no trigger and so could never appear, now uses the state-driven shape
  its sibling card already used.

- **DataGrid cell and row editors now take real keyboard focus (issue [#976](https://github.com/microsoft/microsoft-ui-reactor/issues/976),
  spec 017 §6.7–§6.8).** Every focus API on `DataGridState<T>` moved a purely
  *logical* cell cursor and raised `StateChanged`; nothing ever called XAML
  `Focus(...)`. So opening an editor — Enter, F2, click-to-edit, or a row's Edit
  button — left the caret wherever it already was, and the user had to click or
  Tab into the editor before typing. In `EditMode.Row` it was worse: Tab moved
  the logical cursor while native focus kept walking on into the row's
  Save/Cancel buttons and then out of the grid entirely, and leaving the grid
  fires the deferred blur-commit — so Tab silently committed the row instead of
  cycling its editors. `BeginEdit`, `BeginRowEdit`, and the row-edit Tab
  traversal now arm a one-shot focus request keyed by `(RowKey, columnName)`,
  which the renderer honours by moving real focus into that editor once it is
  loaded. **Behaviour change:** in `EditMode.Row`, Tab now wraps from the last
  editor back to the first rather than reaching Save/Cancel; those stay
  reachable by pointer, and <kbd>Enter</kbd> / <kbd>Esc</kbd> are their keyboard
  equivalents.

- **Reconciler no longer swallows an element's `OnUpdateAction` (issue [#976](https://github.com/microsoft/microsoft-ui-reactor/issues/976)).**
  `Element.ModifiersEqual` — the shallow-skip gate — did not compare
  `OnUpdateAction`, and `ShallowEquals` does not compare callbacks, so an
  element whose only change was *gaining* an update hook compared equal to its
  predecessor, took the skip path, and the hook never ran. Both-null still
  compares equal and a cached delegate still compares equal by reference, so the
  skip rate is unchanged for everything else.

- **Unsetting a common modifier no longer permanently overrides the control's
  style (issue [#952](https://github.com/microsoft/microsoft-ui-reactor/issues/952)).** `Reconciler.ApplyModifiers` reset a dropped modifier by
  *assigning* the dependency property's default value (`fe.Margin =
  new Thickness(0)`, `fe.HorizontalAlignment = Stretch`, `fe.Width = NaN`, …).
  A local value outranks every `Style` setter in WinUI's dependency-property
  precedence order, so the write did not restore the styled value — it
  permanently replaced it, and the control could never get its style-provided
  value back after a single set → unset cycle. Every reset arm now calls
  `ClearValue(...)`, matching the `Unset` → `ClearValue(dp)` rule already
  documented for descriptor-backed props (and enforced there by
  `REACTOR0050`). `ElementPool.CleanElement` had the same defect on pool
  return, handing a recycled control to its next renter with local values it
  could never shed; it now clears too. Separately, `Margin` and `Padding` were
  never reset *at all* — the resolved value was seeded `m.X ?? oldM?.X`, which
  is non-null whenever the previous render supplied one, so the unset arm was
  unreachable; the previous physical value is now used only as the base for
  the BiDi inline-start/end overlay.

- **Context consumers inside a reference-stable child subtree now re-render when
  the provided value changes (issue [#811](https://github.com/microsoft/microsoft-ui-reactor/issues/811)).** The reconciler's skip fast-paths
  (positional, keyed prefix/suffix, the `UseMemoCellsByIndex` hint range, and the
  element-level shallow skip) short-circuited before descending into a
  structurally-unchanged child, so a `UseContext(...)` consumer behind such a skip
  kept its stale value — and its captured click handlers kept dispatching the old
  context. Every skip site now declines the skip when a consumed context changed in
  the subtree, and the subtree walk covers `SplitView` (Pane/Content) and `Viewbox`
  (Child) hosts in addition to the panel/border/content hosts.

- **`.Padding(...)` is no longer silently dropped on text (issue [#950](https://github.com/microsoft/microsoft-ui-reactor/issues/950)).** The
  modifier compiles on every `Element`, but the reconciler only wrote the
  resolved value to `Control`, `Border` and `StackPanel`. `TextBlock` derives
  from `FrameworkElement`, not `Control`, so `.Padding(...)`,
  `.PaddingInlineStart(...)` and `.PaddingInlineEnd(...)` on `Text(...)` /
  `TextBlock(...)` and the typography factories were discarded with no warning
  and no exception, even though `TextBlock.PaddingProperty` exists. They now
  apply. **This shifts layout in code that unknowingly relied on the no-op** —
  if a text element was authored with padding that never took effect, it will
  now take effect. The `REACTOR_MOD_003` diagnostic no longer fires for
  `.Padding(...)` on a text element, and its message now reads "Control, Border,
  StackPanel, or TextBlock". Two related repairs ship with it: dropping
  `.Padding(...)` from a re-render now actually clears the property (the reset
  branch was unreachable dead code), and the reset uses `ClearValue` rather than
  writing a local `Thickness(0)`, so a themed padding comes back instead of
  being pinned to zero.

- **Panel border-box modifiers now reach the concrete WinUI panels that declare
  them (issue [#950](https://github.com/microsoft/microsoft-ui-reactor/issues/950)).** `.CornerRadius(...)` now applies to `Grid`,
  `VStack`/`HStack`, and `RelativePanel`; `.Padding(...)` now also applies to
  `Grid` and `RelativePanel`. Existing code that asked for those modifiers will
  begin rendering rounded corners or inner spacing instead of remaining square
  or flush. The gate deliberately names those concrete types rather than
  `Panel`, whose other subclasses do not declare the properties. Removing
  `.CornerRadius(...)` now uses `ClearValue`, so style/theme values return
  instead of being shadowed by a local zero.

- **`Flyout(...)` no longer terminates the process when opened at its default
  placement.** Reactor's flyout elements default `Placement` to
  `FlyoutPlacementMode.Auto`, and that value was written straight onto the WinUI
  `FlyoutBase.Placement` dependency property. WinUI's show-time validator
  (`FlyoutBase::ValidateAndSetParameters`) only accepts placements `0..12`, so
  `Auto` (13) failed with `E_INVALIDARG` and the resulting stowed
  `ArgumentException` killed the app the first time the flyout was shown —
  reproducible from the gallery's Dialogs and Flyouts → Flyout page. Reactor now
  leaves the dependency property alone when the element says `Auto`, so it keeps
  its own documented default of `Top` and WinUI repositions from there. Applies
  to `Flyout(...)`, `ContentFlyout(...)` / `.WithFlyout(...)` /
  `.WithContextFlyout(...)` and `MenuItems(...)`. `CommandBarFlyout(...)` was
  affected by the same defect and is guarded by the companion fix that made it
  open from its target at all — before that it never reached the validator,
  because nothing ever showed it. Explicit placements are unaffected
  everywhere; a guarded flyout whose placement changes from an explicit value
  back to `Auto` returns to that default, rather than leaving a stale local value
  that would outrank a `Style` setter.

- **Pooled controls no longer inherit a stale tooltip (spec 002 §1.6).**
  `ElementPool.CleanElement` never cleared the `ToolTipService` attached
  properties. The in-place update path clears them on a set → unset transition,
  but a full unmount does not, so a recycled control carried the previous
  element's tooltip — and now its placement and placement target — into the next
  unrelated renter.

- **`NavigationView` pane state no longer desyncs when the control moves its own
  pane (issue [#916](https://github.com/microsoft/microsoft-ui-reactor/issues/916)).** `IsPaneOpen` could be written but had no change
  notification, so a light dismiss or an adaptive display-mode change on resize
  left the app's state stale — the next toggle wrote a value the control already
  held and the pane appeared to need two clicks. Wire `.IsPaneOpen(value,
  handler)` (or `.PaneOpenChanged(handler)`) to keep the two in sync.

- **Window updates no longer drop a `TitleBar(...)` element's content-extension
  inference (issue [#917](https://github.com/microsoft/microsoft-ui-reactor/issues/917)).** `ReactorWindow.Update` wrote
  `ExtendsContentIntoTitleBar = false` whenever the spec left it unset, silently
  undoing the inference behind a still-mounted title-bar control; an unset spec
  value now preserves it.

- **`NavigationView` no longer needs `.Set()` for its chrome (issue [#915](https://github.com/microsoft/microsoft-ui-reactor/issues/915)).**
  `IsBackButtonVisible` and `IsPaneToggleButtonVisible` had no declarative
  mapping, so hiding the back button or the hamburger — the usual setup when a
  `TitleBar` already owns that chrome — forced an imperative
  `.Set(nv => ...)` escape hatch that the reconciler could not diff.

- **`REACTOR_POOL_001` / `REACTOR_MOD_002` no longer offer a codefix that drops
  a repeated write (spec 060 §3.1).** Both the reported and the fixable property
  bags handed to `PoolResetSetCodeFix` are keyed by property name, but the fix
  authorized each write independently — so when a `.Set` body wrote the same
  property twice and only one of them qualified, the verdict earned by the first
  was applied to the second as well. `.Set(fe => { fe.AccessKey = "F";
  fe.AccessKey = null; })` was rewritten to `.AccessKey("F").AccessKey(null)`,
  and `ApplyModifiers` only writes a modifier value that is non-null (or clears
  when the *previous* render carried one), so the explicit clear silently stopped
  happening. A property is now fixable only when *every* occurrence of it in the
  body qualified — exact rather than conservative, since the fix is already
  all-or-nothing over the whole body. Predates the attached-property work above,
  where the same shape would have emitted a call that does not compile.

- **A `Frame` navigating to a code-only `Page` no longer kills the process.**
  Navigating to a page with no XAML backing terminated the
  process instantly with an access violation (`0xC0000005`). Because it is a
  native WinUI AV rather than a managed exception,
  `Application.UnhandledException` never fired — the process simply vanished with
  no error, no dialog and no log. `Frame.Navigate` resolves a custom navigation
  target through the XAML application object, which is a path a code-only page
  cannot satisfy; Reactor now resolves those targets itself instead of handing
  them to the framework.

- **`CommandBarFlyout(...)` now opens from its target.** The flyout was installed
  as attached-flyout *metadata* only (`FlyoutBase.SetAttachedFlyout`), which
  associates it with the element but never shows it — so a
  `CommandBarFlyout(Button("Show Commands"), primaryCommands: …)` rendered a
  button that did nothing at all. It is now wired to actually open from its
  target, which also brings it under the default-placement guard described above.

- **Keyed `ListView` / `GridView` rows no longer announce Reactor's internal row
  identity (issue [#951](https://github.com/microsoft/microsoft-ui-reactor/issues/951)).** Items of a keyed or data-driven list announced the
  internal row identity — `Row[0]=<key>`, degenerating to a raw GUID when the key
  selector produced one — instead of their content, so screen-reader users heard
  bookkeeping rather than the item. The keyed path no longer projects that
  identity onto the container's automation name.

- **Expanding a `DataGrid<T>` row no longer throws `InvalidCastException` (issue
  [#919](https://github.com/microsoft/microsoft-ui-reactor/issues/919)).** The row's root element record type flipped on expand — collapsed,
  `RenderRow` returned a bare `GridElement`; expanded, it wrapped the row in
  `FlexColumn(row, detail)`, a `FlexElement` — so the reconciler tried to reuse
  the realized `Grid` as a `FlexPanel` and threw
  `Unable to cast 'Microsoft.UI.Xaml.Controls.Grid' to
  'Microsoft.UI.Reactor.Layout.FlexPanel'`. The row root is now a stable type
  across both states.

- **Docking's accessibility focus hand-off actually moves focus.**
  The hand-off ran but left real keyboard focus where it was, so the announced
  target and the focused element disagreed after a dock operation.

- **Unpackaged launches no longer raise a first-chance WinRT exception.** Every
  unpackaged app start probed for package identity in a way that threw
  `InvalidOperationException` (`HRESULT 0x80073D54 — The process has no package
  identity`) before anything useful happened — harmless but noisy under a
  debugger and a false lead when diagnosing real startup failures. Identity is
  now detected without throwing.
