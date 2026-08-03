# Changelog

All notable changes to Reactor will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
once a `1.0.0` release is cut. While the project is pre-1.0 and labeled experimental,
the public API surface may change between releases without notice.

<!--
Conventions for contributors:

  * Use the standard Keep-a-Changelog buckets: Added / Changed / Deprecated /
    Removed / Fixed / Security. Group entries under those buckets, not under
    per-spec or per-phase headings.
  * Focus on significant or breaking changes — not every micro-feature. Per-task
    detail belongs in the originating spec, linked from each entry.
  * Cross-reference the originating spec on every line, e.g. "(spec 033 §1)",
    so readers can navigate from changelog → design rationale.
  * Within a bucket, prefer ordering by spec/section number for predictable
    reading.
  * Cutting a release: rename `## [Unreleased]` to `## [x.y.z] — YYYY-MM-DD`
    and add a fresh empty `## [Unreleased]` block (with all six bucket
    sub-headings) above it.
-->

## [Unreleased]

### Added

- **`TabView.FillContentArea` — opt-in full-height tab body (issue #914).**
  WinUI's `DefaultTabViewStyle` sets `VerticalAlignment="Top"` on the `TabView`
  control itself, so the `*` content row in its template never receives leftover
  space and tab content collapses to its natural height instead of filling the
  tab body. Reactor keeps the WinUI default; set
  `TabView([...]) with { FillContentArea = true }` or call `.FillContentArea()`
  to stretch the control so its content area fills the available space. An
  explicit `.VAlign(...)` on the `TabView` element still wins. Apps working
  around this with `UseWindowSize()` + `.MinHeight(...)` (including the Windows
  App SDK `reactor-tabview` template) can drop that workaround.

- **`NavigationView` pane-open change notification (issue #916).**
  `NavigationViewElement.OnPaneOpenChanged` plus the `.PaneOpenChanged(handler)`
  and paired `.IsPaneOpen(value, handler)` fluents report every `IsPaneOpen`
  change on the realized control, so pane state can be driven from component
  state. `SplitViewElement` gains the matching `.IsPaneOpen(value, handler)`
  overload. `ControlDescriptor.Immediate` now accepts a `null` `loadedHook` for
  DP observations that have no template part to walk.

- **Declarative title-bar height (issue #917).** `WindowSpec.TitleBarHeight` and
  `TitleBar(...).Tall()` / `.HeightOption(WindowTitleBarHeight)` ask for the tall
  (48 DIP) caption used whenever the title bar hosts navigation chrome. Sets both
  the system caption (`AppWindow.TitleBar.PreferredHeightOption`) and the WinUI
  title-bar control's own height — the control does not follow the caption, so
  raising one alone leaves them disagreeing. Applied after Reactor's own
  content-extension flip, so there is no ordering hazard and the native setter
  cannot throw `ERROR_INVALID_STATE`. An explicit `.Height(...)` wins over the
  implied 48, and an explicit `WindowSpec.TitleBarHeight` wins over the element's
  declaration.

- **`NavigationView` declarative surface completed (issue #915).** The element
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
  (spec 047 §6, issue #949).** The engine already dispatched unmount to hand-coded
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

### Changed

- **`WithNavigation` gained an optional `settingsRoute` argument (binary-breaking,
  issue #915).** Call sites can now pass a fourth argument —
  `.WithNavigation(nav, routeToTag, tagToRoute, settingsRoute)` — to route the
  built-in settings item; passing nothing keeps the previous behaviour.
  Source-compatible: existing three-argument call sites compile unchanged. But
  optional arguments are baked in at the call site, so binaries compiled against
  `v0.1.0-preview.9`–`.12` still resolve the previous three-argument form and
  would need a recompile rather than a drop-in DLL swap. No back-compat overload
  was added: the project is pre-1.0 and explicitly reserves the right to change
  the public surface between releases, and a permanent duplicate overload is a
  worse public shape than one optional argument.

### Deprecated

### Removed

### Fixed

- **A pooled control no longer hands its previous renter's padding, background,
  border or enabled state to the next one (issue #985).**
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
  can see them. This is the pooling half of #952, whose fix corrected the *shape*
  of the reset (`ClearValue` instead of assigning a default) but not its absence.
  `TextBlock.Padding` — reset since #950, but from a case arm past the point the
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
  recycle (issue #1051).** Rule selection was `poolReset ? POOL_001 : MOD_002`,
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

- **`DataGrid<T>`'s <kbd>Shift</kbd>+<kbd>Tab</kbd> now moves focus backward
  (issue #987).** The grid's routed `KeyDown` handler captured only the raw
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
  (issue #949).** The mount-time write was issued and then silently dropped: WinUI
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

- **Unsetting a common modifier no longer permanently overrides the control's
  style (issue #952).** `Reconciler.ApplyModifiers` reset a dropped modifier by
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
  the provided value changes (issue #811).** The reconciler's skip fast-paths
  (positional, keyed prefix/suffix, the `UseMemoCellsByIndex` hint range, and the
  element-level shallow skip) short-circuited before descending into a
  structurally-unchanged child, so a `UseContext(...)` consumer behind such a skip
  kept its stale value — and its captured click handlers kept dispatching the old
  context. Every skip site now declines the skip when a consumed context changed in
  the subtree, and the subtree walk covers `SplitView` (Pane/Content) and `Viewbox`
  (Child) hosts in addition to the panel/border/content hosts.

- **`.Padding(...)` is no longer silently dropped on text (issue #950).** The
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
  them (issue #950).** `.CornerRadius(...)` now applies to `Grid`,
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
  pane (issue #916).** `IsPaneOpen` could be written but had no change
  notification, so a light dismiss or an adaptive display-mode change on resize
  left the app's state stale — the next toggle wrote a value the control already
  held and the pane appeared to need two clicks. Wire `.IsPaneOpen(value,
  handler)` (or `.PaneOpenChanged(handler)`) to keep the two in sync.

- **Window updates no longer drop a `TitleBar(...)` element's content-extension
  inference (issue #917).** `ReactorWindow.Update` wrote
  `ExtendsContentIntoTitleBar = false` whenever the spec left it unset, silently
  undoing the inference behind a still-mounted title-bar control; an unset spec
  value now preserves it.

- **`NavigationView` no longer needs `.Set()` for its chrome (issue #915).**
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

### Security

## [0.1.0-preview.12] — 2026-07-14

### Added

- **`UseExternalStore<TSnapshot>` hook — first-class subscribe/getSnapshot
  interop (issue #761).** Standardizes the external-store subscription bridge
  (subscribe to change notifications, read the latest snapshot during render)
  that previously required hand-rolled `UseEffect` + `UseReducer` boilerplate.
  Re-renders only when a notification yields a snapshot the comparer treats as
  different; accepts an optional `IEqualityComparer<TSnapshot>`. `subscribe`
  must be a stable delegate and `getSnapshot` must return a cached value, per
  the same guidance React gives for `useSyncExternalStore`.
- **TitleBar drag regions — `.AutoRefreshDragRegions()` and `.IsDragRegion()`
  (spec 059).** Windows App SDK bumped 2.0.1 → 2.1.3; custom `TitleBar` content
  now auto-excludes interactive controls from the window drag region by default.
  Override per element with `.IsDragRegion(false)` (force clickable) /
  `.IsDragRegion(true)` (force draggable), and set `.AutoRefreshDragRegions()` to
  re-derive regions when content changes across renders.
- **`DockFloatingWindowClosedEventArgs.Reason` — close-reason discriminator
  for floating-window closes (spec 045 §5.3.5, issue #417).** A new
  `required DockFloatingCloseReason Reason { get; init; }` on
  `DockFloatingWindowClosedEventArgs`, with enum values `ContentClosed`
  (content is gone — safe to release per-document resources tied to
  `Content`), `MigratedToHost`, and `MigratedToFloat` (the pane is alive in
  its new dock/float position — the close is synthetic and resources must
  **not** be released). Previously every floating `Window.Closed` — including
  the synthetic close Reactor fires right after a cross-window dock-back —
  surfaced as one indistinguishable event, so consumers disposed live state
  (SwapChainPanel, Win2D, file handles) out from under a still-mounted,
  redocked page. The reason is stashed on the window holder
  (`DockFloatingTracker.SetPendingClose`) immediately before the synthetic
  `Close()` and read once by the `Closed` handler; a window with no stash
  reports `ContentClosed`. The migrated reason is scoped to the **specific**
  consumed pane (`DockDragSession.LastConsumedPane`), so a multi-pane float
  that loses one tab to a dock-back still reports `ContentClosed` for a later
  genuine close of its surviving tabs. `Reason` is intentionally `required`:
  there is exactly one internal raise site, so it can never silently default a
  migration to a wrong reason.

- **Command debouncing: `Command.DebounceMs` (issue #136).**
  Commands can declare a leading-edge debounce window via `DebounceMs` (default
  `0` = off) to absorb double-clicks without reaching for `Task.Delay`. When
  routed through `UseCommand`, the first fire is accepted and any subsequent fire
  within the window is dropped; `IsDebouncing` drives `IsEnabled = false` so the
  bound control visibly disables and then re-enables when the window elapses. For
  async commands the disabled window is the longer of the lambda's lifetime
  (`IsExecuting`) and `DebounceMs`. Debounce state lives in the `UseCommand` hook
  store, so a raw `new Command { DebounceMs = … }` bound directly (not through
  `UseCommand`) is inert. `UseCommand` now consumes a stable hook shape regardless
  of the command's sync/async/debounce shape.

- **Uniform `Command` binding across the six command buttons (issues #153, #637).**
  The typed `Command` property on `ButtonElement`, `HyperlinkButtonElement`,
  `RepeatButtonElement`, `ToggleButtonElement`, `SplitButtonElement`, and
  `ToggleSplitButtonElement` is now `public { get; init; }` on every element and
  binds uniformly: a bare `new ButtonElement(cmd.Label) { Command = cmd }` (or
  `with { Command = cmd }`) invokes `Execute`/`ExecuteAsync` on click/toggle and
  applies the command's `IsEnabled` + Description / Accelerator / AccessKey
  identically to the `Button(Command)` factory and the `.Command(cmd)` modifier —
  closing the #153 footgun where a record-init command carried metadata but never
  dispatched. Dispatch flows from the typed property through a click trampoline
  instead of a pre-baked `OnClick` closure, so each command-bound construct
  allocates less (Button ~176 → ~88 B per construct) while the #153 reconcile
  fast-path still skips re-applying an unchanged command. The `.Command(cmd)`
  modifier makes the command fully take over (clearing any conflicting click /
  toggle callback), and `ButtonElement`'s `IsDisabledFocusable` coercion is
  preserved exactly. A `DebounceMs` command now re-disables across the debounce
  window on every command button, not just via the factory path.

- **`ReactorApp.RegisterAllBuiltIns()` — opt-in bulk registration of the built-in
  control catalog (spec 048 §3.4, issue #486).** Built-in handler registration is
  now lazy — each factory registers its own control on first use — so the trimmer
  can drop controls an app never reaches. That broke the documented direct-record
  construction idiom (`new TextBlockElement(…) { … }`, the "Hot loops" pattern in
  `docs/guide/advanced.md`), which bypasses factories and so never triggers
  registration. Call `RegisterAllBuiltIns()` once at startup to register the whole
  catalog and keep that idiom working; apps that build UI exclusively through
  factories don't need it, and a trimmed/NativeAOT build that never calls it still
  drops unused controls. Relatedly, the reconciler now throws an actionable
  `InvalidOperationException` (instead of silently mounting nothing) when it meets
  an element record whose handler was never registered.

- **`Callbacks<T>` — keep delegate props out of memo comparison (issue #151).**
  An opt-in, always-equal wrapper record (`Equals` returns `true`, `GetHashCode`
  returns `0`) for the delegate (callback) portion of a component's props. Because
  `Component<TProps>.ShouldUpdate` memoizes on `!Equals(oldProps, newProps)` and
  `Action`/`Func` fields compare by reference, a parent passing freshly-allocated
  callbacks each render forced the child to re-render even when no data changed.
  Declaring `Callbacks<MyCallbacks> Cb` on the props record excludes the callbacks
  slot from equality, so only data fields drive re-renders — replacing the old
  hand-written `Equals`/`GetHashCode` workaround. The reconciler still refreshes the
  child's live `Props` on a memo-skip, so handlers reading `Props.Cb.Value.OnX` at
  dispatch time always invoke the current delegate, never a stale one.

- New optional package `Microsoft.UI.Reactor.Devtools` for the `--devtools` runtime surface (spec 051 Phase 2).

- **Hot reload: tree-wide hook-order recovery (spec 049 §5, Phase 1).**
  Editing a **non-root** component under .NET Hot Reload to add, remove,
  or reorder a hook now recovers in a single re-render instead of replacing
  that component's subtree with the render-error fallback. The reconciler,
  while inside a hot-reload pass, catches the resulting `HookOrderException`,
  resets just that child's hook state, and re-renders it once — so the edit
  applies and sibling/descendant state is preserved. Steady-state
  (non-hot-reload) rendering is byte-for-byte unchanged: the new recovery arm
  is gated on a `HotReloadService.WithinUpdatePass` filter that is only true
  during a hot-reload-triggered render. Also fixes a latent effect-cleanup
  leak where a staged `PendingCleanup` (from an effect whose deps changed in
  the same render that later threw) was not drained on context teardown.

- **Hot reload: live state migration across record/class shape changes
  (spec 049 §6, Phase 2).** Editing a record or class that a hook stores
  (`UseState` / `UseReducer` / `UseRef` / `UseMemo` / `UsePersisted`) now
  migrates the live value onto the new shape instead of resetting it. At the
  start of each hot-reload pass — before any `Render()` runs — every live
  `RenderContext` value-swaps the hook cells whose stored type the runtime
  reported as updated, copying fields by name onto a freshly-constructed
  instance (`ReactorHotReloadCopier`); fields that can't be mapped are dropped
  with a log line rather than throwing. Cycle-guarded and block-listed against
  native handles (`IntPtr` / `Compositor` / `Visual` / `UIElement`). Devtools
  hook snapshots carry a `Migrated` flag so the inspector can show which cells
  were value-swapped.

- **Hot reload: subtree migration on component-identity change (spec 049 §7,
  Phase 3).** Renaming a component type or otherwise changing its identity
  under hot reload now migrates the existing subtree onto the new component
  instance — preserving its hook state, its live `RenderContext`, and the
  underlying WinUI controls — instead of unmounting and remounting from
  scratch. Triggered from the reconciler's `!CanUpdate` boundary
  (`TryHotReloadMigrateComponent`): it constructs the new instance, copies
  fields with the same `ReactorHotReloadCopier`, transfers the render context,
  swaps the component on the node, and re-renders once into the preserved
  wrapper control. Adds a `HotReloadService.ResetAllContexts()` escape hatch
  for a forced "lose everything, remount fresh" reload when targeted migration
  misbehaves.

- **Hot reload: NativeAOT no-op gating (spec 049 §8).** All reflection-bearing
  migration branches (Phase 2 value swap, Phase 3 subtree migration, the host
  `MigrateHotReloadState` entry points) route through
  `HotReloadService.IsHotReloadLive` (= `MetadataUpdater.IsSupported &&`
  in-pass), so under NativeAOT the entire migration subsystem is statically
  dead and trims away with zero retail overhead.

- **Docking content types & reserved document area (spec 046).**
  Additive amendment to spec 045's `DockNode` algebra so apps can
  express the IDE-class document-area / tool-window-strip distinction:
  `DockGroupRole` ({ `General`, `DocumentArea`, `ToolWindowStrip` }) on
  `DockTabGroup`; `[Flags] DockSides` mask on `ToolWindow.AllowedSides`.
  `Dock(content, Center)` now prefers `DocumentArea` for documents and
  `ToolWindowStrip` for tool windows (falls back to any accepting group
  with a `DockOperationLog` diagnostic). An empty `DocumentArea`
  survives as a visible reserved well when it's the only one in the
  tree; empty split arms next to a non-empty sibling cull so split-drag
  residue collapses cleanly. Drag-drop overlay dims targets that
  reject the payload's category or violate `AllowedSides`; `PinToSide`
  validates against the mask. New public `DockLayoutOps` façade
  (`InsertPaneAtTarget` / `RemovePane` / `MovePaneToTarget` /
  `FindContainer`) for programmatic open/close that respects the new
  routing + cull rules. New `DockHostModel.Dock(content, DockTabGroup,
  target)` overload for explicit group placement. JSON round-trip for
  `role` and `allowedSides`, omitted at defaults; old layouts
  deserialize unchanged. Defaults (`Role = General` / `AllowedSides =
  All`) keep spec-045 behavior for layouts that don't opt in. Bug-fix
  swept three Scene-J regressions surfaced during manual review:
  splitter cursor tracking in 3+ child splits (pair extent now uses
  measured leading + trailing size, not the whole panel), open-doc-
  after-split losing the new doc (host invalidates the drag-modified
  shape override when `manager.Layout`'s leaf-key set changes between
  renders), and close-non-last-in-split leaving an empty arm
  (refined prune rule above).

- **Docking (spec 045).** First-class window-docking surface under
  `Microsoft.UI.Reactor.Docking`. Phase 1 shipped via a vendored WinUI.Dock
  renderer in the `Microsoft.UI.Reactor.Docking.Xaml` package; Phase 2 replaces
  it with a Reactor-native renderer using the same public surface. Covers:
  `Document` / `ToolWindow` sealed records, `DockSplit` / `DockTabGroup` /
  `DockableContent` node algebra, 15 cancellable lifecycle events on
  `DockManager`, layout-strategy hooks (`IDockLayoutStrategy`), tab tear-out
  and 9-target drop overlay, keyboard chords (Ctrl+PageUp/Down,
  Ctrl+F4/W close, Ctrl+Shift+M move, Ctrl+Tab navigator, Alt+F7 hidden-pane
  picker), per-tab pin, AOT-clean v2 JSON layout persistence with migration
  ladder, multi-display floating-window clamp, UIA live-region announcements,
  RTL + high-contrast theming, full localization routing, perf budgets,
  and `docking.list` / `docking.snapshot` / `docking.dock` MCP tools.

- **Keyed-list reconciliation & animation (spec 042).** Templated
  `ListView` / `GridView` / `FlipView` / `LazyVStack` / `LazyHStack` now
  surface incremental WinUI deltas for keyed updates — only affected
  containers animate. New `IReactorKeyed` identity convention lets
  2-arg overloads omit the key selector. Ambient `Animations.Animate(kind, () =>
  setItems(...))` propagates animation intent through inserts / moves /
  removes on both templated and hand-built keyed children (`FlexColumn` etc.).
  New `REACTOR_DSL_001` codefix and `ReactorDiagnostics` devtools dialog
  catch missing `.WithKey` and duplicate-key bailouts. Closes
  microsoft-ui-reactor#198.

- **Property & event API scrub (spec 039).** Every callback property in the
  inventory now has a matching fluent extension (`OnClick` → `.Click(handler)`,
  ~60 callbacks). Named-style helpers (`.AccentButton()`, `.SubtleButton()`,
  `.TextLink()`, InfoBar `.Informational()` / `.Success()` / `.Warning()` /
  `.Error()`). Type-ramp factories `Title` / `Subtitle` / `Body` /
  `BodyStrong` / `BodyLarge`. `Card(child)` theme-aware factory. New events:
  `CalendarView.OnSelectedDatesChanged`; `Frame.OnNavigated` /
  `OnNavigating` / `OnNavigationFailed`; `ScrollView.OnViewChanged`;
  `WebView2.OnWebMessageReceived`; `MediaPlayerElement.OnMediaOpened` /
  `OnMediaEnded` / `OnMediaFailed`; `ContentDialog.OnOpened`;
  `Image.OnImageOpened` / `OnImageFailed`; `ComboBox.OnDropDownOpened` /
  `OnDropDownClosed`; universal multi-select `OnSelectionChanged` on
  list/grid surfaces.

- **Common content-alignment and border fluents (issue #774).** New
  `.HorizontalContentAlignment(...)`, `.VerticalContentAlignment(...)`,
  `.BorderBrush(...)`, and `.BorderThickness(...)` modifiers (with
  `Thickness`-aware overloads) expose common layout/styling that previously
  required a `.Set(...)` fallback, without adding control-specific wrappers.

- **`mur check` — fast feedback with skill pointers (spec 038).** `mur
  check` is the build (same exit code as `dotnet build`) plus two
  enrichments: skill pointers for known `REACTOR_*` IDs and did-you-mean
  `→ try:` suggestions for unknown identifiers. Three suggester tiers:
  Tier-1 analyzer-ID hints, Tier-2 Roslyn semantic suggester (CS1061 /
  CS0103 / CS0117 / CS1503 / CS7036), Tier-3 precision rules anchored on
  Roslyn `ISymbol` binding (`GridSizeFactoryParensRule`,
  `GridSizePxRenameRule`, `TextBlockStyleHintRule`,
  `ThemeBackgroundSuffixRule`, `ThemeRawResourceKeyRule`,
  `ButtonOnClickFactoryMoveRule`).
  Workflow modes: default iteration mode
  suppresses cosmetic noise; `mur check --final` is an optional pre-merge
  sweep; `--strict`, `--quiet`, and `mur check -- <msbuild-args>`
  passthrough also supported. `--trace <path>` writes JSONL diagnostic
  rows; `MUR_TELEMETRY=1` opt-in logs per-suggestion telemetry locally.
  Validated end-to-end across multi-arm EC1/EC2/EC3 evals.

- **Build-time guardrail analyzer suite (spec 060).** The analyzer package now
  ships dozens of `REACTOR_*` diagnostics — the suite spans roughly 60 rules —
  that catch Reactor-specific footguns at compile time across hooks, theming,
  accessibility, keys/DSL, collections, controlled inputs, threading,
  performance, docking, navigation, animation, and lifecycle. Examples:
  `REACTOR_EVENT_001` (event wired via `.Set(+=)` re-subscribes every render —
  #763), `REACTOR_POOL_001`, `REACTOR_ITEMS_001`, `REACTOR_CTRL_001`,
  `REACTOR_THREAD_001` / `_002`, `REACTOR_STATE_001`, and the `REACTOR_DYM_*`
  did-you-mean family. Several ship codefixes; all surface through `mur check`.

- **Multi-window, tray, and shell integration (spec 036).** First-class
  `ReactorWindow` and `ReactorTrayIcon` as peers, with
  `ReactorApp.OpenWindow` / `OpenTrayIcon` / `Windows` / `TrayIcons` /
  `FindWindow` / `WindowOpened` / `WindowClosed` /
  `TrayIconOpened` / `TrayIconClosed` / `Exit` / `ShutdownPolicy`. Per-window
  DPI awareness via WM_DPICHANGED / WM_GETMINMAXINFO. Window lifecycle
  events (`Activated`, `SizeChanged`, `StateChanged`, `Closing`, `Closed`)
  with cancellable `UseClosingGuard`. New hooks: `UseDpi`, `UseWindowSize`,
  `UseBreakpoint`, `UseWindow`, `UseWindowState`, `UseIsActive`,
  `UseOpenWindow`, `UseTrayIcon`. Per-window `WindowPersistedScope`.
  Pluggable `IWindowPersistenceStore` (packaged + JSON fallback). Owned
  windows (`WindowSpec.Owner`), `TaskbarProgress`, `TaskbarOverlay`,
  thumbnail toolbars, `JumpList`, `LaunchActivation` parsing for File /
  Protocol / Toast activations. Devtools `windows.list` /
  `windows.activate` / `windows.close` / `windows.open` MCP tools.

- **Element allocation reduction (spec 034).** Bucketed `ElementModifiers`
  (~−11% bytes/tick on the 4,900-cell stress grid), direct-record-initializer
  idiom for inner cell loops (~−60% bytes/cell), and `UseMemoCells` /
  `UseMemoCellsByKey` / `UseMemoCellsByIndex` cell-level memoization with
  `REACTOR_HOOKS_007` analyzer + codefix. ReactorOptimized at 10% mutation
  reaches 17.1 Effective Refresh/s — within noise of DirectX (17.2) and
  WPF (17.9) on the stocks-grid bench.

- **Typed-arity `UseEffect` / `UseMemo` / `UseCallback` overloads (issue #688).**
  Re-introduces `d1` / `d2` / `d3` overloads for 1–3 value-typed dependencies,
  avoiding the `params object[]` allocation and per-dep boxing on the
  unchanged-deps render path while staying behaviorally identical to the `params`
  form.
- **Public echo-suppression extension point (issue #206).** Authors of custom
  value controls can route a controlled write through the stable `WriteSuppressed`
  primitive to suppress the WinUI change echo, instead of reaching into reconciler
  internals — the same mechanism Reactor's built-in value controls use.
- **Opt-in cross-container row memoization for virtualized lists (issue #327).**
  Wrapping a realized row in a keyed `Memo(key, factory)` lets the reconciler
  reuse the row's rendered subtree across container recycles when the key is
  unchanged, cutting redundant per-row reconciliation on fast scroll (see the
  `/perf` row-memoization leg, #764, for the measured win).

- **XAML/WinUI interop response (spec 033).** New `GridSize` value type
  with `Auto` / `Star(weight)` / `Px(pixels)` smart constructors and
  invariant-culture `Parse`. New `IPersistedStateScope` interface with
  `PersistedScope.Window` / `PersistedScope.Application` and LRU-backed
  scopes with memory-pressure trimming. `RenderEachTime(...)` and
  `Memo(...)` factories replace the soft-deprecated `Func(...)`.
  `ElementRef<T>` typed-ref wrapper + `UseElementRef<T>()` hook.
  `.Backdrop(BackdropKind)` modifier for declarative Mica / Acrylic.
  `Expr(Func<Element?>)` factory for inline block-expression bodies.

### Changed (breaking)

- **`AsyncValue<T>.Match` success arm renamed `data` → `loaded`; omitted
  `reloading` now falls through to `loading()`.** The success delegate parameter
  was renamed (source-breaking for named-argument callers; no back-compat
  overload is possible because overloads can't differ by parameter name alone).
  When the value is `Reloading` and no `reloading:` handler is supplied, `Match`
  now renders the `loading()` arm instead of reusing the success arm
  (`(reloading ?? data)(r.Previous)`). Pass `reloading:` explicitly to keep the
  last-known value visible during a refresh (stale-while-revalidate).
  (issue #548, spec 020 §5.1)

- **`ReactorApp.Run` devtools parameters removed (spec 051 §13).** The
  `devtools:` and `preview:` overload parameters are gone. Enable devtools
  capability in the app project with `<RuntimeHostConfigurationOption
  Include="Reactor.DevtoolsSupport" Value="true" Trim="true" />`, then launch
  with `--devtools` to activate a session.

- **`.Margin(double, double)` and `.Padding(double, double)` parameter
  order swapped** from `(horizontal, vertical)` to `(vertical, horizontal)`
  to match CSS shorthand convention. Use the named-arg form
  (`.Margin(horizontal: 16, vertical: 8)`) for layout-stable call sites.
  (spec 038 §3)

- **`ScrollView()` factory now mounts the modern
  `Microsoft.UI.Xaml.Controls.ScrollView`** (anchor ratios,
  `ContentOrientation`, the `Scrolling*` enum surface). The legacy
  `Microsoft.UI.Xaml.Controls.ScrollViewer` mapping moved to a new
  `ScrollViewer()` factory. Element records follow the same rename.
  (Issue #348)

- **`TextField(...)` removed.** The deprecated forwarding alias was
  retired after the `TextFieldElement` → `TextBoxElement` rename. Use
  `TextBox(...)`.

- **`MaskedTextFieldElement` renamed to `MaskedTextBoxElement`.** The
  Reactor-original masked text input record was renamed to align with
  WinUI's `TextBox` naming and Reactor's `TextBox()` factory (follow-on
  to the `TextField` → `TextBox` rename). The fluent `.Changed(...)`
  modifier now extends `MaskedTextBoxElement`. (issue #389)

### Deprecated

- **`IDockBehavior` and `DockManager.Behavior`** (spec 045 Phase 1) marked
  `[Obsolete]` with migration pointers to the per-event Action props
  that landed in Phase 2 (`OnContentDocked` / `OnContentFloating` /
  `OnContentFloated`). Slated for removal one release after Phase 2 ships.
  (spec 045 §2.12)

### Removed

- **Obsolete APIs scheduled for removal have been removed (breaking, minor
  release).** Each `[Obsolete]` member previously annotated "will be removed
  in the next minor release" is now gone; migrate to the replacement:
  - `Factories.Func(Func<RenderContext, Element>)` → `RenderEachTime(ctx => …)`
    (behavior-preserving), or `Memo(ctx => …)` where memoization is wanted.
    (spec 033 §4)
  - `Factories.Grid(string[], string[], …)` → the typed
    `Grid(GridSize[], GridSize[], …)` overload with
    `GridSize.Auto` / `GridSize.Star(weight)` / `GridSize.Px(pixels)`.
    (spec 033 §1)
  - `Controls.MaskedTextFieldDsl.MaskedTextField(...)` →
    `MaskedTextBoxDsl.MaskedTextBox(...)`. (issue #389)
  - `Factories.RichText(string)` / `Factories.RichText(RichTextParagraph[])`
    → `RichTextBlock(...)`. (spec 039 §1.3)
  - `Factories.ProgressBar(double)` → `Progress(double)`;
    `Factories.ProgressBar()` → `ProgressIndeterminate()`. (spec 039 §5)

  The dead `GridStringTrackCodeFix` code fix (which rewrote the string-track
  `Grid` overload to the typed form on `CS0618`) was removed alongside the
  overload.

- **`ReactorHost.MainDispatcherQueue`** (internal static, first-host-wins
  capture). Cross-thread setState marshalling and AutoSuggest's
  `RaiseStateChanged` now route through `ReactorApp.UIDispatcher`.
  (spec 036 §4.3)

- **`Microsoft.UI.Reactor.Markdown.MarkdownHtml` and its nested `HtmlFlags`
  enum — removed from the shipped `Microsoft.UI.Reactor` assembly (issue
  #433).** This md4c-based Markdown→HTML string renderer existed only for
  CommonMark/spec/fuzz validation; it was never used by the native
  `Markdown()` element (which renders directly to a WinUI inline tree via
  `MarkdownBuilder` and is unaffected). It now lives in the test-support
  library `tests/Reactor.Markdown.TestRenderer/`, off the framework's public
  surface. No replacement ships in the package — the renderer was incidental
  test/spec API, not a supported way to convert Markdown to HTML at runtime.

### Fixed

- **`DataGrid<T>` now reacts to `SelectionMode` prop changes after first mount
  (issue #872).** The grid captured its selection mode once at construction, so a
  later `selectionMode:` change on the same instance was silently ignored (the
  factory keys the component only on type + source, so it did not remount either).
  It now reconciles the mode onto the live headless state each render — narrowing
  (`Multiple` → `Single`/`None`) trims the current selection — removing the need for
  a mode-dependent `.WithKey(...)` remount workaround.

- **RichTextBlock inline-UI scroll drift hardened with a prevention-at-source extent
  pin (issue #717).** The #487 scroll-anchor (below) restores the offset reactively
  *after* the ancestor scroll host clamps it; this adds a complementary guard that aims
  to stop the clamp from happening in the first place. The underlying cause is that the
  live app applies the document mutation inside the reconcile and then returns to the
  dispatcher, letting the compositor commit a frame carrying WinUI's transient
  collapsed extent (`RemoveEmbeddedElements` → `desiredSize=0`) *before* the inline UI
  re-attaches — the very frame the scroll host clamps against. The collapse is
  scheduled on a *separate* dispatcher callback (not a layout-dirty flag), so a
  synchronous `UpdateLayout` inside the reconcile measures the still-intact tree and
  cannot coalesce it. `UpdateRichTextBlocks` now, after mutating an inline-UI-bearing
  block, pins the block's `MinHeight` to its full pre-collapse `ActualHeight` (raising
  the floor only, never lowering an author value) and releases the pin a couple of
  rendered frames later once the inline UI has re-attached. With the floor pinned the
  transient `desiredSize=0` cannot shrink the block's measured height, so
  `ScrollableHeight` never drops, the scroll host never clamps, and there is no lost
  offset to restore. This lands as **defense-in-depth behind the #487/#649 anchor**:
  in the current build WinUI coalesces the transient collapse so the live drift is not
  reproducible, but the failure mode is real if it ever commits (e.g. different
  hardware/content or a future WinUI change), so the pin guards it at the source. No new
  API surface; the pin only engages for blocks that actually host inline UI and actually
  mutated.
- **RichTextBlock inline-UI mutations no longer scroll the ancestor scroll host to
  the top (issue #487).** Mutating any `Run.Text` inside a paragraph that hosts an
  `InlineUIContainer` (charts/sliders/buttons embedded via `InlineUI(...)`) made the
  enclosing `ScrollViewer`/`ScrollView` silently scroll up by the combined height of
  the embedded inline elements. WinUI's text engine re-measures the whole paragraph
  from scratch (`ParagraphNode::Measure` → `RemoveEmbeddedElements()` + `desiredSize=0`
  for one layout pass), so the block transiently shrinks, the scroll host clamps
  `VerticalOffset` down to the smaller `ScrollableHeight`, and never restores it once
  the inline UI re-attaches. The fix is invisible to authors — `ScrollViewer(RichTextBlock(...))`
  "Just Works": `UpdateRichTextBlocks` now arms a scroll anchor on the nearest ancestor
  scroll host before mutating an inline-UI-bearing block and restores the user's real
  offset once layout settles, while never fighting a genuine user scroll. No new API
  surface, no per-app boilerplate.
- **The accessibility scanner now sees a pie chart's `.SetColors(...)` palette
  (issue #645, spec 026).** `PieChartElement<T>.SetColors(...)` sets the colors a
  pie actually renders, but the scanner previously only saw the separate
  `.Palette(...)` palette — so a `.SetColors(<low-contrast>).ChartBackground(...)`
  pie looked contrast-checked yet A11Y_CHART_011 never ran on its rendered colors.
  The rendered `.SetColors` palette is now the single source of truth the scanner
  validates (`.Palette(...)` is consulted only as a fallback when `.SetColors` is
  unset), so A11Y_CHART_009/010/011 run on it exactly as they do for `.Palette(...)`.
  **Behavior change:** existing `.SetColors(...)` users with low-contrast or
  colorblind-unsafe palettes may now start seeing A11Y_CHART_009/010/011 findings
  they did not before. Pie palette-fix suggestions also now name `.SetColors(...)`
  (the modifier a pie exposes) instead of `.SeriesColors(...)`.
- **Multi-window teardown no longer faults with an `ACCESS_VIOLATION`
  (issue #647).** Closing a docking tear-off floating preview window could
  terminate the process with `0xC0000005` deep in the WinUI backdrop interop —
  an unmanaged fault no `try`/`catch` can trap — corrupting the lifecycle of
  windows opened later in the same process. Root cause: a transient auxiliary
  window (e.g. a docking tear-off) could be elected the fallback `PrimaryWindow`,
  so closing it fired `ShutdownPolicy.OnPrimaryWindowClosed` →
  `Application.Exit()`, tearing down every still-open window mid-process; a
  surviving host then wrote `Window.SystemBackdrop` on one of those torn-down
  surfaces. Three reinforcing fixes (spec 036, spec 045): docking floating
  windows opt out of primary election via `ExcludeFromShutdownPolicy`, and the
  single election helper that runs on both initial registration and
  unregister re-election never promotes an excluded window — so an auxiliary
  window can neither become *nor remain* primary, and `PrimaryWindow` goes
  `null` rather than to an excluded window when no eligible window remains;
  `BackdropApplier` records torn-down surfaces in a process-wide closed-window
  registry and skips every `SystemBackdrop` write (both `Apply` and `Reset`) on
  them; and `ReactorWindow.Close()` is now idempotent — a redundant or
  owner-cascade close performs the native close exactly once instead of
  re-entering native teardown.
- **The full self-test suite no longer faults with an `ACCESS_VIOLATION` at
  final process exit (issue #680; test infrastructure only).** Distinct from
  the mid-run multi-window fault fixed in #647/#673, the *entire* self-test
  suite (~600 fixtures in one process) crashed with `0xC0000005` during final
  process teardown — green TAP output, then a non-zero process exit. It
  reproduced only for the whole suite, never for any subset. Root cause is a
  **Microsoft.UI.Xaml / Microsoft.UI.Windowing framework use-after-free** walked
  over state the harness deliberately accumulates and never disposes: one shared
  `Window` carrying hundreds of `Closed` handlers (one per never-disposed
  `ReactorHost`) plus the windowing fixtures' real `ReactorWindow`s with custom
  title bars. Every orderly process-exit path trips it — `Environment.Exit`
  runs the loader's TLS destructors into the XAML core's already-freed tear-off
  map (`TearoffMemoryInfoPrivate::Discard` → `0xC0000005`), and
  `Application.Exit` double-releases the caption-buttons UI Automation provider
  inside `CTitleBar::Uninitialize` (`0xC0000005` → fast-fail `0xC0000409`). Both
  faults live *inside* framework teardown, so the issue's suspected
  fix — making Reactor's own `SetTitleBar`/`WindowMessageMonitor`/backdrop
  unregister idempotent — cannot stop them. Because no real Reactor app
  accumulates this state (apps dispose hosts and exit via the orderly
  `ReactorApp.SafeExit` / `Application.Exit` path, already hardened by #647), the
  fix is scoped to the harness: after flushing the TAP stream, the self-test
  runner ends via `TerminateProcess(GetCurrentProcess(), exitCode)`, an
  immediate teardown-free kill that runs neither the loader's TLS destructors nor
  WinUI's window-close cascade, preserving the exact `0`/`1` exit code. A new CI
  regression guard (`SelfTestBatch.HostProcessExitsCleanly_NoTeardownCrash`)
  fails if the Host ever again exits with anything other than `0` or `1`. **No
  product code changed.**
- **TitleBar in a non-content-extended window no longer corrupts the heap on
  close (issue #537).** A window whose `WindowSpec` set
  `ExtendsContentIntoTitleBar = false` while its content still rendered a
  `TitleBar(...)` element could terminate the process with
  `STATUS_HEAP_CORRUPTION` when the window closed: the WinUI
  `Microsoft.UI.Xaml.Controls.TitleBar` control only releases its caption-button
  / AppWindow interop cleanly in content-extended mode, but Reactor allows that
  combination (skipping `SetTitleBar`). Reactor now flips the window back into
  content-extended mode just before the native close, while the AppWindow is
  still alive, so the control tears down via its safe path. The flip is
  idempotent and runs on every teardown path — `Close()`, the owner-close
  cascade (including nested owned descendants), the chrome/Alt+F4 close, a direct
  `Dispose()`, and `ReactorApp.Exit()` / shutdown-policy exit — so still-open
  windows are covered no matter how the process winds down. The
  `ExtendsContentIntoTitleBar` value observed while the window is alive is
  unchanged; the flip happens only at close.
- **Virtualized rows now reset per-item component state on recycle when keyed
  (issue #326).** `LazyVStack` / `LazyHStack` / `ItemsRepeater<T>` / `ItemsView<T>`
  now propagate the `keySelector` projection onto each realized row's top-level
  `Element.Key`. Post-#324 the ItemsRepeater recycle path reuses a realized
  inner `Component<T>` across logical items as you scroll, which carried that
  component's `UseState` / `UseEffect` state from one item to another (e.g. an
  editor row left "dirty" for item 5 stayed dirty when its container was reused
  for item 12). With the per-item key in place, reusing a container for a
  *different* logical item fails `CanUpdate` and the row remounts with fresh
  hook cells; same-item re-renders keep the key and diff in place, preserving
  state. An explicit `.WithKey(...)` in the row builder still wins. This is a
  user-visible behavior change for code that (intentionally or not) relied on
  cross-item state carry-over — use a stable constant key, or hoist the state
  above the row, to opt back into durable carry-over. The shared
  `RefreshRealizedItems` refresh path now also handles a same-slot key change
  (the documented `.WithKey($"{id}:{rev}")` revision-bump pattern) by adopting
  the freshly-mounted subtree into the still-parented row wrapper, so the old
  control is no longer orphaned and the per-control tracking stays consistent.
  `ListView<T>` / `GridView<T>` already remount per realize and are unaffected.

- **ItemsView multi-select checkmark no longer flickers during window resize
  (issue #383).** In a `SelectionMode=Multiple` `ItemsView<T>`, the per-item
  selection checkmark visibly faded out/in on every realized row while the
  window was drag-resized. WinUI's `ItemsView` flips each realized
  `ItemContainer`'s internal multi-select mode on every clear/prepare recycle
  round-trip, re-running the `MultiSelectStates.Multiple` opacity storyboard with
  `useTransitions: true`; and because the inner `ItemsRepeater` recycles its
  realized set on every ancestor arrange pass during a resize, the storyboard
  re-fired dozens of times per gesture. The recycle is intrinsic WinUI
  viewport-manager behavior (not eliminable without regressing ItemsView sizing
  — see the issue #383 investigation), so Reactor now collapses each realized
  container's `Multiple`-state opacity storyboard keyframes to zero duration: the
  animated `GoToState` still runs but snaps the checkmark to full opacity in the
  same UI tick instead of fading it. Selection behavior, the recycle, and the
  final checkmark visibility are unchanged — only the spurious fade is gone.

- **`UseAnnounce().Announce(...)` now marshals to the UI thread automatically.**
  Previously, calling `Announce` off the UI thread (e.g. from a `Task.Run`
  continuation) threw `RPC_E_WRONG_THREAD` (0x8001010E) — the underlying WinUI
  XAML calls (`FrameworkElementAutomationPeer.FromElement`,
  `RaiseNotificationEvent`, `TextBlock.Text`) are UI-thread affine — so the
  announcement silently failed. The handle now captures its `DispatcherQueue`
  when the live-region `TextBlock` is wired and re-marshals off-thread calls via
  `TryEnqueue`; the UI-thread fast path stays a single `HasThreadAccess` check.
  (issue #130)

- **`UseNavigation` mutators are now thread-safe by default (issue #234).**
  `NavigationHandle<TRoute>`'s `Navigate`, `GoBack`, `GoForward`, `Replace`,
  `Reset`, `PopTo`, and `SetState` previously mutated the back/forward stacks
  with no thread guard, so calling them off the UI thread (from a `Task.Run`,
  a timer, or after `await … ConfigureAwait(false)`) could corrupt the stacks
  or silently drop the navigation. Each mutator now auto-marshals the whole
  operation onto the handle's captured UI dispatcher — completing the
  thread-safety-by-default story that #212 started for `UseState` / `UseReducer`
  — via the shared `UIThreadMarshal` gate. The UI-thread fast path stays a
  single thread-id compare with zero allocation. When no dispatcher is available
  (headless / unit-test contexts) or it has shut down, the off-thread call throws
  a loud `InvalidOperationException` instead of corrupting state. On the UI thread
  behavior is unchanged. The `RenderContext` setter marshal and the navigation
  gate now share one `UIThreadMarshal.EnqueueOrThrow` implementation.

- **`Popup` now matches the WinUI light-dismiss default (issue #873).**
  `PopupElement.IsLightDismissEnabled` defaulted to `true`, inverting WinUI's
  `false` default — a plain `Popup(child, isOpen)` dismissed on an outside click
  unless the author passed `.IsLightDismissEnabled(false)`. The record default now
  matches WinUI; opt in explicitly with `.IsLightDismissEnabled(true)`.
- **DataGrid keeps the inline editor open when Tab moves to the next cell
  (#851).** Tabbing while editing committed the current cell and advanced, but the
  reopened editor was immediately torn down by the grid's deferred `LostFocus`
  commit, so the grid appeared to drop out of edit mode. Editing-Tab now commits
  and reopens the editor on the next cell (Excel-like).
- **`ListView` / `GridView` updates can clear `Header` or `ItemContainerStyle`
  back to `null` (issue #845).** Both handlers gated the assignment on the new
  value being non-null, so a present→null transition left the stale header/style
  in place. The update now applies the `null`.
- **`ListView` / `GridView` `OnItemClick` no longer double-subscribes (issue
  #779).** The native `ItemClick` event was wired on both mount and update but
  never unsubscribed, so toggling `OnItemClick` present→null→present fired the
  handler twice per click. It is now wired exactly once.
- **`FontIconSource` no longer crashes on an unset font size (issue #854).**
  Icon-source resolution faulted on a `NaN` size; it is now coerced safely. The
  bug surfaced while adding a title-bar app-mark icon to the `dotnet new
  reactorapp` template, which the template now ships.

### Security
