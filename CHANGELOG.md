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
  * Cross-reference the originating spec on every line, e.g. "(spec 033 §1)",
    so readers can navigate from changelog → design rationale.
  * Within a bucket, prefer ordering by spec/section number for predictable
    reading.
  * Cutting a release: rename `## [Unreleased]` to `## [x.y.z] — YYYY-MM-DD`
    and add a fresh empty `## [Unreleased]` block (with all six bucket
    sub-headings) above it.

Spec 033 (WinUI/XAML reviewer feedback response) is the first set of entries
to land under these conventions; subsequent specs follow this shape.
-->

## [Unreleased]

### Changed

- **`TextField` → `TextBox` rename.** The record type
  `TextFieldElement` is renamed to `TextBoxElement` for parity with
  WinUI's `Microsoft.UI.Xaml.Controls.TextBox`. The `TextField(...)`
  factory remains as a non-erroring `[Obsolete]` forwarding alias for
  one release; call sites should migrate to `TextBox(...)`. The
  alias will be removed in a future release.

### Deprecated

- **`Factories.TextField(...)`** — use `Factories.TextBox(...)` instead.
  Emits `CS0618` warning; will be removed in a future release.

- **Spec 045 Phase 2 — §2.29 ready for human review gate.**
  Every Phase-2 implementation item in
  `docs/specs/tasks/045-docking-windows-implementation.md` is now
  `[x]` complete or `[~]` partial with an enumerated upstream-spec
  blocker (spec 027 / 031 / 036 / docking analyzer pack). The
  ten human-review items (§2.29 items 9–18) are gated on the
  visual sit-down review itself. Showcase + TestApp both build
  clean; 324 docking unit tests pass + 409 devtools tests pass.
- **Spec 045 Phase 2 — §2.22 high-contrast chrome brushes.**
  `DockSplitterControl` and `DockDropTargetOverlayControl` swap
  their hard-coded ARGB literals for `ThemedBrush(key, fallback)`
  lookups against `Application.Current.Resources`. HC themes now
  retheme the docking chrome via the same dictionary path WinUI
  defaults rely on. ARGB fallbacks remain for the headless-harness
  no-Application case. System brushes: splitter handle →
  `SystemControlForegroundBaseMediumLowBrush`; splitter hover +
  drop-target Stroke + preview Border + indicator fill →
  `SystemControlHighlightAccentBrush`; drop-target outer Fill →
  `SystemControlBackgroundChromeMediumLowBrush`; preview body +
  Center fill → `SystemControlBackgroundAccentBrush` (the preview
  Border carries an explicit `Opacity=0.30` for the transparent
  overlay).
- **Spec 045 Phase 2 — §2.19 XAML wrapper unhooked from the
  solution.** The Phase-1 `Reactor.Docking.Xaml` wrapper assembly
  was removed from `Reactor.slnx` ahead of the §2.29 human-review
  gate: ProjectReferences dropped from `DockShowcase.csproj`,
  `Reactor.AppTests.Host.csproj`, `Reactor.Tests.csproj`;
  `InternalsVisibleTo("Microsoft.UI.Reactor.Docking.Xaml")` dropped
  from `Reactor.csproj`; showcase entry point dropped the
  `REACTOR_DOCK_XAML=1` A/B flip — the native renderer (§5.1) is
  the only path. Phase-1-specific `DockingSmokeFixtures` +
  `BehaviorBridgeMappingTests` retire alongside;
  `NativeDockingSmokeFixtures` cover the same surface. Source
  under `src/Reactor.Docking.Xaml/` stays on disk per the §5.6
  reference contract; a follow-up commit removes the directory.
- **Spec 045 Phase 2 — §2.30 shape-only `layoutOverride`.** The
  docking host's internal `layoutOverride` previously stored the
  full user-mutated `DockNode` tree (with each leaf's `Content` /
  `Title` / `CanClose` snapshotted at drag-end). That broke the
  controlled-input contract: app re-renders couldn't push fresh
  pane bodies because the override always won. Apps had to walk
  the override and refresh leaf bodies manually. Now the host
  stores SHAPE-only (leaf records stripped to just `Key`) and
  resolves pane content per render via
  `DockLayoutMutator.ResolveContents(shape, manager.Layout)`,
  matching shape leaves to the app's full records by Key. Apps
  declare the full tree idiomatically in `Render()`; state flows
  naturally even after a user drag. New helpers
  `DockLayoutMutator.StripContent` + `ResolveContents`. 6 new
  unit tests. M19/M20 drag matrix selftests still pass.
  `samples/Reactor.TestApp/Demos/DockingDemo.cs` drops its
  `RefreshContents` walker / `OnLiveLayoutChanged` plumbing — the
  demo is now plain Reactor.

### Added

- **Spec 045 Phase 2 — §2.26 `docking.list` / `docking.snapshot` /
  `docking.dock` MCP tools.** New
  `DevtoolsDockingTools.Register(server)` wires three tools onto
  the live `DevtoolsMcpServer`. `docking.list` enumerates hosts
  via `DockHostRegistry.Snapshot()` (pane count + active key +
  side counts per host). `docking.snapshot` returns the
  `DockSnapshot` for a single host through the existing
  `DockSnapshotBuilder.FromRecord` path. `docking.dock` accepts
  `{ hostId, paneKey, action, target?, side? }` and routes through
  `DockHostModelBridge.Get(manager)` to call the matching
  `DockHostModel` mutator (dock / float / hide / show / close /
  activate / pinToSide). All tools execute on the UI dispatcher
  via `server.OnDispatcher<T>(...)`. Wired into
  `ReactorApp.cs:1012` next to the existing devtools tools.
  11 new unit tests in `DevtoolsDockingToolsTests`.
- **Spec 045 Phase 2 — §2.24 / §2.9 per-pane WindowPersistedScope.**
  New `DockHooks.UseDockPanePersisted<T>(string key, T initial)`
  extension on `RenderContext`. Auto-prefixes the supplied key with
  `pane:<paneKey>:` and forwards to
  `RenderContext.UsePersisted<T>(key, initial, PersistedScope.Window)`
  so two panes sharing the same unprefixed key get independent
  WindowPersistedScope slots. Throws when called outside any
  pane subtree (same contract as `UsePane`). XML docstring carries
  the cross-user-secret caveat (apps must clear sensitive per-pane
  data on logout / scope change). 3 new `DockHooksTests` cases.
- **Spec 045 Phase 2 — §2.2 per-tab pin button on ToolWindow tabs.**
  `TabViewItemData` gains optional `IsPinnable` / `IsPinned` /
  `PinAutomationName` / `PinAutomationId` / `OnPinRequested` fields.
  The `TabViewElement` reconciler builds the tab Header as a
  StackPanel (title TextBlock + Segoe Fluent Icons pin Button)
  when `IsPinnable=true`; otherwise the cheap string-header path
  is preserved verbatim. `DockTabGroupRenderer.Render` accepts a
  new `onPinRequested: Action<ToolWindow>?` callback and
  auto-enables the affordance on ToolWindow tabs whose
  `CanAutoHide=true`. `DockHostNativeComponent.PinToSideViaTabButton`
  routes clicks through `DockHostModel.PinToSide(tw, DockSide.Left)`
  so the §2.16 drain + live-region announcement contract fires
  identically to a programmatic pin. AT name + tooltip use the
  localized `Docking.Menu.PinToSide` string; AutomationId is
  `pin:<paneKey>`. 3 new `DockTabGroupRendererTests` cases.
- **Spec 045 Phase 2 — §2.26 `docking.snapshot` building blocks.**
  New `DockHostRegistry` (process-wide `WeakReference<DockManager>`
  registry with stable `dh:{n}` ids) wired through
  `DockingNativeInterop`'s mount/update/unmount handlers alongside
  the existing bridges. `DockSnapshot` shape + `DockSnapshotBuilder`
  pure transform surface a content-ref-free layout tree
  (`DockSnapshotSplit` / `DockSnapshotTabGroup` / `DockSnapshotLeaf`
  + `DockSnapshotPane` for identity + role + permissions). 13 new
  unit tests in `DockSnapshotBuilderTests`. The JSON-RPC tool
  registration on the internal `DevtoolsMcpServer` rides on the
  shape being stable.
- **Spec 045 Phase 2 — §2.10 Alt+F7 hidden-pane picker.** Re-uses
  the `DockNavigatorPopup` primitive to pick a side-stripped tool
  window for re-show. Enumerates `effLeftSide` / `effTopSide` /
  `effRightSide` / `effBottomSide` (where hidden ToolWindows end
  up after `Hide` / `CanHide=true` close via the §2.16 drain).
  Commit calls `model.Show(pane)` which routes through
  `DockLayoutMutator.ShowFromHistory` (§2.15) — and now consults
  `IDockLayoutStrategy.BeforeInsertToolWindow` first so apps can
  override the remembered-container route. No-ops on an empty
  side-strip set.
- **Spec 045 Phase 2 — §2.23 RTL selftest fixture.**
  `NativeDocking_Rtl_FlowDirectionAndSplitterSign` mounts a
  two-pane docking host, applies `FlowDirection.RightToLeft` on
  the realized host Border, and asserts every realized
  `DockSplitterControl` + `TabView` inherits RTL + the pointer-
  drag remains RTL-correct (WinUI coord-space transform).
  Validates the "WinUI FlowDirection inheritance handles it"
  claim across §2.23's sidebar / tab-order / splitter / glyph
  bullets.
- **Spec 045 Phase 2 — §2.27 composition fixtures.** Three new
  host-mounted selftests:
  `Composition_ContentMutationFlowsToActivePane` (in-pane state
  flows into pane body), `Composition_SiblingMutation_PreservesActivePaneIdentity`
  (sibling state change preserves pane wrapper Border identity),
  and `Composition_Rehydration_ContentMatchesByKey` (save / load
  round-trip + app-supplied content lands in restored slots by
  Key, AutomationId survives).
- **Spec 045 Phase 2 — §2.22 keyboard-only docking cycle fixture.**
  `NativeDocking_A11y_KeyboardCycle_NavigatorCommitsActive` drives
  the navigator commit + cancel paths via test hooks and asserts
  the host-side wiring contract (chord → popup → active pane →
  `OnActiveContentChanged`) end to end.
- **Spec 045 Phase 2 — §2.10 Ctrl+Tab pane navigator overlay.**
  VS-style navigator: Ctrl+Tab opens a Popup over the host Border
  listing all open panes (depth-first leaf enumeration via the new
  `DockHostKeyboard.EnumerateLeaves`); subsequent Ctrl+Tab /
  Ctrl+Shift+Tab presses cycle the selection ±1; Ctrl release
  commits and switches `activePaneKey` + fires
  `OnActiveContentChanged`; Esc cancels. The new
  `DockNavigatorPopup` primitive lives outside the Reactor
  reconciler (per-host `Popup` instance keyed via a
  `ConditionalWeakTable<FrameworkElement, DockNavigatorPopup>`) so
  opening it doesn't perturb the render tree (M19 / M20 control-
  identity assertions stay green). The chord bridge gains an
  optional `OpenNavigator(int delta)` slot; `DockingNativeInterop`
  attaches the two new accelerators alongside the existing chord
  set. Unit coverage in `DockNavigatorTests` (9 cases).
- **Spec 045 Phase 2 — §2.10 UIA live-region announcements.** Layout-
  state transitions (close, tear-out / float, dock-confirm, pin-to-
  side, hide, show) now raise polite UIA notifications against the
  dock-host element via WinUI's `RaiseNotificationEvent` API. The
  new `DockHostLiveAnnouncer` is a `ConditionalWeakTable<DockManager,
  FrameworkElement>` bridge paralleling `DockChordBridge` /
  `DockHostModelBridge`; `DockingNativeInterop` registers the host
  Border at mount and clears it on unmount. Announcement templates
  live under `Docking.LiveRegion.*` (`LiveDocked`, `LiveFloated`,
  `LivePinned`, `LiveClosed`, `LiveHidden`, `LiveShown`),
  parameterized by `{paneTitle}` via `DockingStrings.LiveAnnouncement`.
  Notification API is used in place of a visible
  `TextBlock`+`LiveSetting=Polite` region so the visual tree is
  unchanged (M19 / M20 control-identity assertions stay green).
- **Spec 045 Phase 2 — §2.22 splitter keyboard resizable + RTL.**
  `DockSplitterControl` is now tab-focusable (`IsTabStop=true`,
  `UseSystemFocusVisuals=true`) with arrow-key resize through the
  same direct-mutation path the pointer drag uses; default
  `KeyboardStep` = 16 DIP. Under `FlowDirection.RightToLeft` the
  Columns-direction Left/Right mapping inverts so a Right press
  grows the visually-right pane.
- **Spec 045 Phase 2 — §2.23 RTL drop-target glyph mirror.**
  `DockDropTargetOverlayControl` now emits a directional side-
  stripe overlay on each Split / Dock target (filled rectangle
  pinned to the matching edge via
  `HorizontalAlignment`/`VerticalAlignment`). FlowDirection
  inheritance auto-mirrors the Left-anchored indicators to the
  right edge under RTL, so DockLeft / SplitLeft glyphs visually
  flip to match the also-mirrored button positions.
- **Spec 045 Phase 2 — §2.22 focus invariants on close.**
  `DockHostLiveAnnouncer.FocusHostFallback(manager)` programmatically
  hands focus to the host element when a close removes the last
  pane (no group with documents remains). Sibling-pane focus carry
  on partial close is inherited from TabView's selection-change
  focus shift on the next render. Tear-out path keeps WinUI's
  default focus shift to the new floating window. Selftest
  `NativeDocking_A11y_FocusFallback_OnLastPaneClose` drives a
  model-mutator close through the §2.16 drain, verifies the bridge
  registration survives the re-render cycle, and gates the
  `FocusHostFallback` call site against the no-group-left layout
  state.
- **Spec 045 Phase 2 — §2.6 floating-window lifecycle events.**
  `DockFloatingWindow.Open` now fires
  `DockManager.OnFloatingWindowCreated` immediately after window
  registration (carrying the dragged source pane) and
  `OnFloatingWindowClosed` from the underlying `ReactorWindow.Closed`
  event (carrying the best-effort pane reference; may be stale
  after a cross-window dock-back already migrated it). Subscriber
  exceptions are swallowed so a buggy observer cannot break
  tear-out or cleanup paths.
- **Spec 045 Phase 2 — §2.20 perf budget tests.** Three new
  unit tests in `DockPerfBudgetTests` enforce the §2.20 spec
  budgets: layout JSON load (200 panes, median < 200ms CI ceiling
  / 50ms spec budget), drop-target hit-test hot path (no
  measurable allocation across 100k `ComputePreviewBounds`
  iterations — 1B/iter cap), and 50-pane mutator shape change
  (RemovePane + InsertPaneAtTarget, median ≤ 25ms CI ceiling /
  1ms spec budget). Pattern follows spec 034 (allocation counter)
  + spec 031 (median-of-N sampling). Remaining §2.20 items
  (frame-aligned hover, tear-out frame budget, cold-start, DPI
  re-layout) ride on the spec 031 frame-aligned sampler harness.
- **Spec 045 Phase 2 — §2.22 accessibility baseline.** Every docked
  pane's wrapper Border now carries a stable
  `AutomationProperties.AutomationId = "pane:<key>"` derived from
  `DockableContent.Key`, plus `AutomationProperties.Name` from the
  pane's app-owned Title — so screen readers and selftests address
  panes deterministically across re-renders. The DockHost root
  Border exposes `AutomationLandmarkType.Custom` + a
  `LocalizedLandmarkType` + `Name` sourced from `DockingStrings`
  (key `Docking.DockHost.Landmark`; default "Docking area"). New
  unit suite `DockA11yTests` (5 cases) + selftest fixture
  `NativeDocking_A11y_HostLandmarkAndPaneAutomationIds` walking
  the realized control tree.
- **Spec 045 Phase 2 — §2.21 localization routing.** Every
  docking user-facing string now flows through a static
  `DockingStrings.Get(key)` router with an optional
  `Func<string, string?>? Resolver` apps wire at startup to
  forward keys to their `IntlAccessor`. The drop-target overlay
  (`DockDropTargetOverlayControl.GetLocalizedName` + landmark
  `AutomationProperties.Name`), the side-pin tooltip
  (`DockSideStripRenderer`), and the floating-window default
  title (`DockFloatingWindow.Open`) now consult the router. Keys
  are constants on `DockingStringKeys`, mirrored 1:1 against
  `src/Reactor.Docking.Xaml/Resources/Reactor.Docking.resw` —
  drop-target tooltips, navigator headings, per-pane context-menu
  items (Close/Hide/Float/PinToSide/AutoHide/MoveToNextGroup),
  side-pin tooltip prefix, floating-window default title,
  layout-restore error, host landmark. `DockingStrings.SidePinTooltip(title)`
  performs the placeholder substitution after lookup so
  translators can rearrange the surrounding text. Unit coverage
  in `DockingStringsTests` (9 scenarios).
- **Spec 045 Phase 2 — §2.25 reliability close-out.** Three new
  host-mounted fixtures under `NativeDockingReliabilityFixture`:
  `CrashMidDrag_LeavesPersistedLayoutClean` (mid-drag layout save
  contains no drag-session state — drag state is in-memory only and
  doesn't leak into persisted JSON; restart drops the in-flight drag,
  reloaded layout is shape-identical to the pre-drag tree),
  `FloatingWindowClosesOnHostUnmount` (floating windows opened from
  a `DockManager` are tracked per-host; the host's
  `DockingNativeInterop` unmount handler closes them so they don't
  outlive the host), and `EventSubscriptionLeakBaseline` (100-pane
  open/close cycle stays within a 32 MB allocation delta — smoke
  test against catastrophic retention; precise budget tracked via
  §2.20 perf benchmarks). New `DockFloatingClamp` + `DockDisplay` +
  `DockFloatingBounds` types (in `src/Reactor/Docking/Native/`)
  implement the §2.6 / §2.25 multi-display restore clamp: saved
  floating bounds with < 200 × 100 DIP overlap against any display
  recenter on the primary; sizes clamp to the primary work area
  minus a 32 DIP margin. Pure math covered by 9 new unit tests in
  `DockFloatingClampTests`. `DockFloatingWindow.Open` takes optional
  `savedBounds` + `displays` + `manager` parameters; tear-out and
  Float-mutation call sites pass the live manager so the per-host
  tracker (ConditionalWeakTable-backed) sees each open.
- **Spec 045 Phase 2 — Reliability + security selftests (§2.24,
  §2.25).** Four new host-mounted fixtures under
  `NativeDockingReliabilityFixture`:
  `CorruptLayoutFallback_HostMounted` (corrupt JSON → fallback +
  event), `OffThreadMutation_ThrowsAndDoesNotQueue` (off-dispatcher
  mutators throw and don't dirty the queue),
  `UseEffectCleanup_RunsOnPaneClose` (programmatic close drains
  through the §2.16 mutator queue and the body unmounts from the
  visual tree), and `DragSessionPayload_ObjectRefsOnly`
  (`DockDragSession` holds object refs, refuses a second concurrent
  drag, clears on End). Cross-ref §2.24 / §2.25 close-out items.
  The cleanup test surfaced a Reactor-side gap where
  `ComponentElement` instances embedded inside
  `DockableContent.Content` don't run their `UseEffect` cleanups
  when the leaf disappears — tracked as a known limitation in the
  fixture's docstring.
- **Spec 045 Phase 2 — Closed-to-empty layout fix.** A subtle bug
  in the drag/close/drain paths caused `setLayoutOverride(null)`
  to revert to the controlled-input prop, resurrecting a closed
  pane. The internal state is now a `LayoutOverride(DockNode?)`
  wrapper so the renderer can distinguish "no override" from
  "override is intentionally empty". All five
  `setLayoutOverride` call sites (drain, tear-out, tab close,
  drag confirm, chord close) updated. Caught by the
  `Reliability_Effect_BodyGoneFromTree` assertion in the new
  reliability fixture.
- **Spec 045 Phase 2 — Corrupt-JSON ETW emission (§2.7).**
  `DockLayoutSerializer.Load` now classifies every fallback path into a
  PII-safe category (`empty` / `oversize` / `json-parse` /
  `unsupported-schema` / `null-document` / `schema-missing` /
  `validation`) and emits the new
  `ReactorEventSource.DockingLayoutLoadFallback` event (id 16, Warning,
  `Errors` keyword) carrying only the category string — the full
  `DockLayoutLoadResult.FailureReason` continues to surface to
  in-process callers under app ACL. Closes the last open checklist
  item on §2.7. (spec 045 §2.7, spec 044)
- **Spec 045 Phase 2 — Model-mutation drain (§2.16).**
  `DockHostNativeComponent.Render` now drains `DockHostModel.Pending`
  on each render pass: every queued `Dock` / `Float` / `Hide` / `Show`
  / `Close` / `Activate` / `PinToSide` op translates to a layout
  override (or side-strip override for the side-affecting ops) and
  fires the matching lifecycle event (`OnContentDocked`,
  `OnDocumentClosing`+`Closed`, `OnToolWindowHiding`+`Hidden`,
  `OnContentFloating`+`Floated`, `OnActiveContentChanged`). The model
  exposes a new internal `OnMutationQueued` callback that the host
  wires to `bumpTick` so any mutator wakes the reconciler into a
  re-render even when called from outside an event handler. A new
  `DockHostModelBridge` (mirrors `DockChordBridge`) lets tests and
  devtools grab the live model instance from a `DockManager`
  reference. Lights up `IDockLayoutStrategy.AfterInsert*` adjustments
  actually landing on the rendered tree (§2.13), `DockHostModel.Show`
  using the §2.15 PreviousContainer history, and programmatic
  Dock/Float/Hide/PinToSide mutations affecting the live layout.
  Verified by new selftest
  `NativeDocking_ModelDrain_DockCloseActivatePinAffectsLiveTree` (9
  assertions) and unit tests for the queue-wake contract. (spec 045
  §2.16)
- **Spec 045 Phase 2 — Composition-driven docs selftest (§2.18).** New
  `NativeDocking_CompositionDrivenDocumentsRespectKeyedReconciliation`
  fixture mounts a layout where the documents list is held in app
  state, then runs add / remove cycles to verify the
  `documents.Select(d => new DockableContent(Key: d.Id, ...))`
  pattern. Keyed reconciliation preserves the TabView control
  instance across the structural changes — the fixture asserts
  `ReferenceEquals` on the TabView between initial mount and the
  post-add render. Codifies the spec §5.3.7 contract that Reactor's
  functional composition replaces `DocumentsSource` /
  `LayoutItemTemplate` / `ContentResolver`. (spec 045 §2.18)
- **Spec 045 Phase 2 — IDockBehavior obsolete forwarder (§2.12).**
  `IDockBehavior` (the P1 interface) and `DockManager.Behavior` (the
  property that consumes it) are now marked `[Obsolete]` with
  migration pointers to the per-event Action props that landed in
  Phase 2 (`OnContentDocked` / `OnContentFloating` /
  `OnContentFloated`). Slated for removal one release after Phase
  2 ships. The P1 wrapper assemblies (`Reactor.Docking.Xaml`)
  suppress the obsolete warning at file scope while they continue
  to bridge the interface for source compat. (spec 045 §2.12)
- **Spec 045 Phase 2 — PreviousContainer routing (§2.15).** The close /
  tear-out paths in `DockHostNativeComponent` (tab close button,
  `Ctrl+F4` / `Ctrl+W` chord, drag-out tear-out) now record the
  pane's immediate `DockTabGroup` container into
  `PreviousContainerTracker` via the new
  `DockLayoutMutator.FindContainer` walk before removing the pane.
  A new `DockLayoutMutator.ShowFromHistory(root, pane, fallback)`
  pure-function helper folds a remembered pane back into its
  original `DockTabGroup` when that group still lives in the
  tree (matching VS's "show panel where you left it"); falls back
  to `InsertPaneAtTarget(root, pane, fallback)` when the
  remembered group has been torn down. Caller-side wiring
  (`DockManager.Show` programmatic API, drag-back snap hint)
  attaches when the §2.16 model-mutation drain materializes the
  `ShowOp` path. 7 new unit tests cover the `FindContainer` /
  `ShowFromHistory` matrix. (spec 045 §2.15)
- **Spec 045 Phase 2 — Document/ToolWindow default tab styling (§2.8).**
  `DockTabGroupRenderer.Render` now auto-resolves tab styling based on
  the group's content type: a group whose documents are all
  `ToolWindow` (and where the user hasn't customized
  `TabPosition` / `CompactTabs` beyond the record's defaults) flips
  to bottom-position + compact tabs (matches Office / VS tool-pane
  convention). All-`Document` and mixed groups stay at the top
  position + full-width default — a tool window dragged into an
  editor strip doesn't collapse the whole strip to compact. The
  `TabPosition.Bottom` visual still renders as top-strip per the
  §2.2 limitation (no WinUI TabView bottom mode), but the resolved
  value flows through so future bottom-strip support picks it up.
  5 new unit tests lock down the resolution matrix
  (all-tool / all-doc / mixed / explicit-defaults-on-tool /
  explicit-compact-on-tool). (spec 045 §2.8)
- **Spec 045 Phase 2 — Docking permission gating (§2.14).** The native
  drag pipeline now honors `DockableContent.CanMove` / `CanFloat` /
  `CanClose`: `HandleTabDragStarting` refuses to begin a session for
  a pinned pane (logs a `Note` op); `HandleTabDragCompleted` refuses
  tear-out when `CanFloat=false` (session ends, layout untouched);
  the drop-target `OnConfirm` re-checks `CanMove` on the source pane
  so the keyboard-driven `Ctrl+Shift+M` flow can't drop a pinned
  pane that turned read-only between mode-enter and confirm;
  `EnterKeyboardDropMode` skips opening the overlay entirely for a
  pinned active pane. Tab close button now routes through
  `CloseTabViaButton` which re-checks `CanClose` and goes through
  the cancellable `OnDocumentClosing` → `OnDocumentClosed` →
  `OnLiveLayoutChanged` event chain (matching the `Ctrl+F4` chord
  path from §2.10). UI cues for disabled permissions (cursor /
  disabled-tab style) ride on the §2.8 tab styling pass.
  (spec 045 §2.14)
- **Spec 045 Phase 2 — Layout strategy dispatch (§2.13).** The
  manager-side dispatch into `IDockLayoutStrategy` is now wired in
  `DockHostModel.Dock`: when the host component mirrors
  `DockManager.LayoutStrategy` onto the model each render,
  programmatic `Dock(content, target)` calls `BeforeInsertDocument`
  or `BeforeInsertToolWindow` (subtype-routed) first; a `true`
  return short-circuits the default insertion (strategy claims
  placement via the model surface); a `false` return queues the
  default `PendingMutation.DockOp` and then fires the matching
  `AfterInsert*` hook so apps can layer dimensions / pinning /
  activation on top. Bare `DockableContent` (P1 source-compat
  shape) bypasses the typed hooks since the strategy contract is
  defined against the §2.8 `Document` / `ToolWindow` subclasses.
  Six new unit tests under `LayoutStrategyTests` lock down the
  no-strategy passthrough, document/tool-window short-circuit,
  pass-through-then-`AfterInsert`, subtype routing, and
  bare-pane bypass. (spec 045 §2.13)
- **Spec 045 Phase 2 — Docking keyboard chords (§2.10, initial set).**
  Three chord families land on the Reactor-native dock host:
  `Ctrl+PageUp` / `Ctrl+PageDown` cycle the active tab group with
  wrap (VS parity); `Ctrl+F4` / `Ctrl+W` close the active document
  via the cancellable `OnDocumentClosing` → `OnDocumentClosed` path;
  `Ctrl+Shift+M` flips the §2.3 drop-target overlay into a
  keyboard-initiated drag mode (arrow keys + Enter to confirm,
  Esc / repeat-chord to dismiss) with the active pane as the
  implicit source. Wiring goes through a new `DockChordBridge`
  (ConditionalWeakTable keyed by `DockManager` instance) so the
  mount-time `KeyboardAccelerator` set on the dock host `Border`
  picks up live chord delegates from the component each render —
  no extra layout layer (a `CommandHost` Grid wrapper perturbed
  M19's outer-FlexPanel ActualWidth and was abandoned). Selected-
  index per `DockTabGroup` is now host-tracked via a path-keyed
  `selectedIndexStore` (mirrors the §2.1 ratio store) so chord
  cycling sticks across re-renders without breaking the
  controlled-input shape — apps that pin `ActiveDocument` still
  win for `UseIsActivePane` / context propagation. Deferred to a
  follow-up pass: `Ctrl+Tab` pane navigator overlay, `Alt+F7`
  hidden-pane picker, UIA `LiveSetting=Polite` announcements, and
  spec-027-driven configurable binding. 16 new unit tests cover
  the pure helpers (`DockHostKeyboard.{FindGroupContainingKey,
  FindFirstGroup, CycleIndex, BuildChords}`) plus the
  `DockChordBridge` round-trip. (spec 045 §2.10)
- **Spec 045 Phase 2 — Docking drag pipeline (§2.4).** Tab tear-out
  + drop-target dock now works end-to-end on the Reactor-native
  renderer. `DockDragSession` is the object-ref payload (replaces
  upstream WinUI.Dock's static GUID→object dict that spec §8.9
  flagged as a security/reliability anti-pattern). Tab drag wires
  through new `TabViewElement.OnTabDragStarting` /
  `OnTabDragCompleted` events: dragstart begins a session + flips
  the §2.3 overlay; confirm rebuilds the layout via
  `DockLayoutMutator` and fires `OnContentDocked`; drop-outside
  opens a floating window (`OnContentFloated`); Esc cancels via
  `OverlayDismissed`. The host component shadows `Manager.Layout`
  with a `layoutOverride` state so drag results stick until the
  app explicitly syncs through `OnContentDocked`. 23 new unit
  tests (6 for `DockDragSession`, 17 for `DockLayoutMutator`) plus
  `NativeDocking_DragSessionConfirmMutatesLayout` smoke fixture
  cover the state machine + layout-mutation algebra. Keyboard-
  initiated move (`Ctrl+Shift+M`) and the standalone `.OnPan`
  recognizer remain on the §2.10 / follow-up list. (spec 045 §2.4)
- **Spec 045 Phase 2 — Docking drop-target overlay (§2.3).**
  `DockDropTargetOverlayControl` lands as the Reactor-native replacement
  for WinUI.Dock's `DockTargetButton` + `Preview.xaml.cs`. Renders 9
  drop targets (5 split + 4 edge per `DockTarget`) at minimum 44×44 DIP
  (WCAG 2.5.5 / spec §8.7), with a hover preview rectangle keyed off
  `ComputePreviewBounds(target, hostW, hostH)`. Targets are focusable
  and arrow-key navigable through `NextFocus` (cluster cross + edge
  ring); `Enter` confirms, `Esc` dismisses. The overlay reads
  `UISettings.AnimationsEnabled` at construction for reduced-motion
  gating. Mounting is gated by the new `DockManager.ShowDropTargets`
  prop with `OnDropTargetHovered` / `OnDropTargetConfirmed` /
  `OnDropTargetsDismissed` callbacks — the §2.4 drag pipeline flips
  the flag mid-gesture; apps can also drive it directly for keyboard-
  initiated move (§2.10 `Ctrl+Shift+M`) or testing. AT names are
  inline English pending the §2.21 `Docking.*` resource pass.
  20 unit tests cover preview bounds, focus graph, and AT names;
  the `NativeDocking_DropTargetOverlayShowsAndDismisses` smoke fixture
  exercises the full mount → confirm → unmount cycle. (spec 045 §2.3,
  §8.7)
- **Spec 045 Phase 2 — Docking (foundation).** Foundation layer of the
  Reactor-native rewrite. The Phase-1 public API moves from
  `src/Reactor.Docking.Xaml/` into `src/Reactor/Docking/` (same
  `Microsoft.UI.Reactor.Docking` namespace, so app source references
  are unchanged); Phase-2 additive surface extensions land on top:
  - `Document` and `ToolWindow` sealed records (Phase-2 §5.3.1) with
    distinct default permissions (`Document.CanClose=true`,
    `ToolWindow.CanClose=false` because X-button hides per AvalonDock
    semantic; `ToolWindow.CanPin=true`, `CanAutoHide=true`,
    `CanDockAsDocument=true`).
  - `Document<TState>` generic record carrying typed per-pane state;
    `TState` versioning is the app's responsibility (§5.3.2 / §8.11).
  - `CanFloat` (default true) and `CanMove` (default true) added to
    the `DockableContent` base (§5.3.8).
  - `IDockLayoutStrategy` insertion-policy hook with default-method
    bodies — apps short-circuit insertion via `BeforeInsertDocument` /
    `BeforeInsertToolWindow`, or post-process via `AfterInsert*`
    (§5.3.6).
  - 15 cancellable lifecycle event-arg classes
    (`DockLayoutChanging`/`Changed`, `DockDocumentClosing`/`Closed`,
    `DockToolWindowHiding`/`Hidden`/`Closing`/`Closed`,
    `DockContentFloating`/`Floated`/`Docking`/`Docked`,
    `DockActiveContentChanged`,
    `DockFloatingWindowCreated`/`Closed`); every `*ing` carries
    `Cancel`. `DockManager` now exposes 15 `Action<TArgs>?` props for
    each (§5.3.5).
  - `IDockLayoutMigration` interface + `DockLayoutMigrationRegistry`
    ladder with built-in v1→v2 step (synthesizes keys from titles per
    §5.4.4). Forward-tolerant for schemas newer than the loader target.
  - `DockHostModel` internal source-of-truth class with mutation queue
    (`Dock`/`Float`/`Hide`/`Show`/`Close`/`Activate`/`PinToSide`).
    Mutations are UI-dispatcher-affined; off-thread access throws
    `InvalidOperationException` per spec §8.10. Enumerations
    `AllContent()` / `Descendants()` over the dock tree.
  - `DockContexts` slots + `DockHooks` extension methods on
    `RenderContext` for property hooks: `UseDockHost`,
    `UseActivePaneKey`, `UseIsActivePane`, `UsePane`, `UseDockState`,
    `UseDockLayout`. Each slot is a separate `Context<T>` so consumers
    only re-render on their specific slice change (selector-style scope
    per spec §5.3.11).
  - `DockPaneInfo` readonly struct + `DockPaneState` enum
    (Docked/Floating/AutoHidden/AutoHiddenExpanded/Hidden).
  - `DockSide` enum, `FloatingDockWindow` record, `DockLayoutSnapshot`
    record for the wide-net `UseDockLayout` hook.
  - `PreviousContainerTracker` — `ConditionalWeakTable`-backed
    bookkeeping for the "show panel where you left it" mechanic
    (§5.3.9). Bookkeeping decays with the pane reference.
- **Spec 045 Phase 2 — Layout JSON v2 persistence.**
  `DockLayoutSerializer.Save`/`Load` round-trips Reactor-native v2 JSON
  via a source-generated `JsonSerializerContext` (AOT-clean: no
  reflection paths from JSON; no external schema URLs; no type-name
  instantiation). Security limits per §8.9: 1 MB max input size, depth
  32; corruption / oversize / unknown-node-kind / missing-`$schema`
  inputs return a `DockLayoutLoadResult.Fallback` (never throw on the
  load path per §8.10). Invariant culture for numerics — verified by
  a save-`de-DE` / load-`en-US` selftest (§8.8). Role-default-aware
  permission emission keeps the file small. 200-pane load latency
  regression guard wired in xUnit; the §8.1 50ms perf budget is
  enforced by the perf bench harness. (spec 045 §5.4, §8.9–8.11)
- **Spec 045 Phase 1 — Docking (vendor + wrap).** First-class docking
  surface arrives in Reactor via the new `Microsoft.UI.Reactor.Docking`
  namespace, shipped from a separate `Microsoft.UI.Reactor.Docking.Xaml`
  NuGet package so apps that don't need docking don't pay for the
  vendored XAML dependency. Public API committed at P1 exit:
  `DockManager : Element`, the `DockNode` algebra (`DockSplit`,
  `DockTabGroup`, `DockableContent`), `TabPosition` /
  `DockTarget` enums, and the `IDockAdapter` / `IDockBehavior`
  observation hooks (spec 045 §4.3). Apps register the element type
  with the reconciler at host construction via
  `DockingXamlInterop.Register(host.Reconciler)` (same pattern as
  `XamlInterop.Register`); thereafter, any `DockManager` element in the
  Reactor tree reconciles to a vendored WinUI.Dock control. Pane
  identity is via `DockableContent.Key` (spec 042 keyed reconciliation
  — explicit, no Title-as-key fallback). Showcase sample lands at
  `samples/apps/dock-showcase/` with six scenes mirroring the §4.7
  review script. Smoke fixture
  `Docking_TwoPaneMountUpdateUnmount` exercises mount → update →
  unmount in the AppTests harness; 27 unit tests cover the public API
  + upstream enum mapping. Phase 2 swaps the vendored implementation
  for a Reactor-native renderer with the same public surface. (spec 045
  §4)
- **WinUI.Dock vendored under `third_party/WinUI.Dock/`.** Snapshot of
  `qian-o/WinUI.Dock` @ `2f5247f1` (MIT) with four light edits
  documented in `VENDORED.md`: Uno code paths stripped, formatting
  normalized, `[InternalsVisibleTo]` added for the wrapper + tests, and
  the cross-window DnD bug sidestepped by restricting drag-out to a
  single manager in Phase 1 per spec §4.6. The runtime reference is
  removed at Phase 2 exit (§5.6); the source stays in the tree for
  license compliance and A/B regression checks against the native
  rewrite. (spec 045 §4.1, §4.2)
- **`Microsoft.UI.Reactor.Docking.Xaml` is granted internal access to
  `Reactor.dll`.** The wrapper is a first-party Microsoft assembly that
  ships alongside the framework and calls `Reconciler.SetElementTag` /
  `DetachReactorState` — same level of trust as the in-assembly
  `Microsoft.UI.Reactor.Hosting.XamlInterop`. (spec 045 §4.4)
- **Spec 042 Phase 1 — keyed-list reconciliation & ListView animation
  groundwork.** New internal `Microsoft.UI.Reactor.Core.Internal.ReactorRow`
  /  `ReactorListState` carry reference-typed identity rows inside an
  internally-owned `ObservableCollection<ReactorRow>` per mounted templated
  items control. The new `KeyedListDiff.Apply` helper produces the
  React-style structural delta from the user's immutable list — lockstep
  prefix + suffix walks, single-op fast paths (append / prepend /
  remove-front / remove-back / insert-in-middle / remove-from-middle), and
  a bulk-replace bailout (>25% churn with ≥8 absolute ops) that returns
  to the legacy `ItemsSource` swap for correctness. (spec 042 §4)
- **Spec 042 Phase 2 — `IReactorKeyed` identity-on-data convention.**
  2-argument `where T : IReactorKeyed` factory overloads land for
  `ListView<T>` / `GridView<T>` / `FlipView<T>` / `LazyVStack<T>` /
  `LazyHStack<T>` so the `keySelector` parameter can be omitted when the
  data type owns its identity (it defaults to `t => t.Key`). A new
  `WithKey<T, TKey>(this T el, TKey item) where TKey : IReactorKeyed`
  extension is the ergonomic peer for hand-built keyed children — both
  shapes route through the same Phase 1 incremental diff. Explicit
  `keySelector` and `WithKey(string)` are unchanged for interop /
  third-party POCOs. The `samples/TodoApp/` `TodoItem` model adopts
  the convention as a worked example. (spec 042 §5)
- **Spec 042 Phase 4 + 5 — samples, gallery, and agent-kit references.**
  New `samples/apps/animated-list-demo/` mini-app drives the templated
  `ListView<Row>` and a hand-built `FlexColumn(items.Select(...).WithKey(item))`
  side-by-side over the same edits so the OC-delta and `ChildReconciler`
  paths animate together. `ReactorGallery`'s `ListViewPage` gains an
  "Animated edit (spec 042)" `SampleCard` with the same toolbar. `TodoApp`
  routes add / delete / clear-completed through an `Animations.Animate`
  wrapper that honours `UseReducedMotion()`. New `Component.UseReducedMotion()`
  delegation exposes the existing context hook so user components can
  bypass `Animate` under WCAG 2.3.3. New skill references —
  `plugins/reactor/skills/reactor-dsl/references/keyed-lists.md` for the
  three keyed-list call sites and three `.WithKey` overloads, and
  `plugins/reactor/skills/reactor-recipes/references/animated-list.md` for
  the paste-ready `Animate` recipe + five common-mistakes sections.
  (spec 042 Phase 4 + 5)
- **Spec 042 perf gate — paired Reactor vs WinUI vanilla baseline.**
  New `StressPerf.VirtualList.WinUI` is a hand-authored WinUI 3 twin
  to `StressPerf.VirtualList.Reactor` — `ItemsRepeater` +
  `ObservableCollection<ListItem>` with a recycling `IElementFactory`,
  same row visual tree, same scroll tween, same edit policy
  (deterministic seed). `tests/stress_perf/run_keyed_list_vs_winui.ps1`
  drives a paired N-rep matrix that interleaves the two apps within
  each rep to neutralize DRR / thermal drift, computes per-cell
  medians, and writes a markdown verdict alongside per-rep frames CSVs
  for forensic re-analysis. First baseline at
  `tests/stress_perf/baselines/keyed-list-vs-winui-2026-05-17-104102/`
  pins Reactor inside 0.3 % P50 of WinUI at production-realistic list
  sizes; the 10k-item P50 spread is unrelated to the diff path (gap
  doesn't move with edit pressure; Reactor's P95 / P99 tail is
  tighter than WinUI's). (spec 042 §10 perf gate)
- **Spec 042 Phase 6.3 — 10k virtualized scroll + edit stress scenario.**
  `StressPerf.VirtualList.Reactor` gained `--with-edits` /
  `--edits-per-second N` flags that interleave deterministic insert / remove
  ops with the scroll tween (50/50 mix, seeded RNG, default 4 ops/sec).
  Catches future regressions in the `ItemsRepeater` key-indexed factory
  path (`ElementFactory<T>._mountedElements`, rekeyed in Phase 1) that the
  steady-state scroll bench wouldn't see. `ListItemSource.GenerateOne(id)`
  added so synthesized items don't collide with the seed range.
  `tests/stress_perf/README.md` documents the new scenario, the
  command line, and the analysis rule ("if the gap to the edit-free
  baseline scales with `count`, the rekey path has regressed").
  (spec 042 Phase 6.3)
- **Spec 042 Phase 6.2 — `ReactorDiagnostics` + devtools dialog.**
  New public `Microsoft.UI.Reactor.Core.Diagnostics.ReactorDiagnostics`
  collector captures keyed-list bailouts (duplicate / null key) with
  per-control dedup via `ConditionalWeakTable` so a torn-down control
  doesn't leak. `RecentKeyedListWarnings` returns a bounded snapshot
  (64 entries × 8 sample keys each, newest first). `KeyedListDiff.Apply`
  gained a `controlInstance` parameter and now routes both bailout paths
  through a shared reporter — `ILogger.LogWarning` fires only on the
  first occurrence per (control, kind, sample-set) triple, while
  subsequent repeats bump an in-place `Count`. New `DevtoolsMenu` item
  "Keyed-list diagnostics (N)" pops a `ContentDialog` listing each
  captured entry; behind `ReactorApp.DevtoolsEnabled` so retail apps
  pay zero cost. Tests: 7 in `ReactorDiagnosticsTests`, all 43
  existing `KeyedListDiffTests` still pass. (spec 042 Phase 6.2)
- **Spec 042 Phase 6.1 — `REACTOR_DSL_001` codefix.** The existing
  missing-`.WithKey` analyzer now ships with a code fix that offers three
  insertion shapes ranked by discovery: `.WithKey(item)` when the lambda
  parameter implements `IReactorKeyed`, `.WithKey(item.Key)` when the type
  has a public `Key` property, and `.WithKey(item.Id)` when the type has a
  public `Id` property. The codefix opts out of `FixAllProvider` because
  each lambda needs an independent semantic lookup of the parameter type.
  Covered by 6 new tests under `tests/Reactor.Tests/AnalyzerTests/MissingWithKeyAnalyzerTests.cs`.
  (spec 042 Phase 6.1; resolves the Q2 follow-up from spec §9)
- **Spec 042 Phase 3 — ambient `Animations.Animate(...)` transaction.**
  Wrapping a state mutation in `Animations.Animate(AnimationKind.Spring,
  () => setItems(...))` propagates animation intent through an
  `AsyncLocal` ambient so the resulting structural diff — insert / move /
  remove on `ListView<T>` / `GridView<T>` / `LazyVStack<T>` /
  `LazyHStack<T>` and on hand-built keyed children inside `FlexColumn`
  etc. — picks up the kind without per-element modifiers. Setters snapshot
  the ambient synchronously at dispatch time so the eventual render observes
  the same intent even if the rerender hops a dispatcher; `ReactorHost` /
  `ReactorHostControl` re-push the snapshot around the reconcile pass.
  `KeyedListDiff.Apply` tags inserted `ReactorRow`s with the kind so the
  `ContainerContentChanging` realize path can attach a per-container
  fade-up Composition animation; survivor moves drive an implicit
  `Offset` animation on the realized container (deferred one dispatcher
  turn so WinUI has reconciled positions before lookup).
  `ChildReconciler` consumes the same ambient — insert sites apply the
  same default enter, move sites attach an implicit `Offset` animation,
  and `RemoveChildWithExitTransition` fabricates a fade-out exit when no
  per-element `.Transition(...)` is set. Per-element animation modifiers
  continue to win when declared; the ambient is purely a default for the
  transactional case. The two channels (transactional ambient on
  `AsyncLocal`, per-element curve scope on `ThreadStatic`) remain
  independent — a leaf `TextBlock`'s `Foreground` change inside
  `Animate(.Spring)` does *not* animate the foreground. New types:
  `AnimationKind` (public enum), `Animations.Animate(...)` (public),
  `AmbientAnimation` / `AnimationAmbient` / `AnimationKindMap` (internal
  glue). (spec 042 §6; matches Phase 3.2 / 3.3 / 3.4 / 3.5 of the task
  list, including the §9 Q3 / Q4 resolutions.)

### Fixed

- **ListView / GridView / LazyVStack / LazyHStack now surface incremental
  WinUI deltas for keyed updates.** Previously, any `ItemCount` change
  rebuilt `ItemsSource` from `Enumerable.Range(...)`, which caused WinUI
  to tear down every realized container and replay the entrance theme
  transition for every visible row — the symptom captured in
  microsoft-ui-reactor#198. Phase 1 routes structural changes through a
  per-control `ObservableCollection<ReactorRow>` delta channel so only
  the affected containers animate. Hand-built `FlexColumn(items.Select(...
  .WithKey(item.Id)))` already worked correctly and is now pinned by
  regression tests. (spec 042 §1, §4; closes microsoft-ui-reactor#198)
- **`ItemsRepeater` `ElementFactory<T>._mountedElements` is now keyed by
  the stable `ReactorRow.Key` instead of by realized index.** Insert-at-0
  used to shift every realized entry's effective index by one, so
  `RefreshRealizedItems` looked up the wrong element after every prepend.
  Keying by string makes the mapping reorder-stable. (spec 042 §4.4)

### Added

- **Spec 039 — Property & event API scrub.**
  - **New fluent extensions.** Every callback property in the inventory has a
    matching fluent on its element record — ~60 callbacks across §1–§9 of the
    spec. Fluents drop the leading `On` (so `OnClick` → `.Click(handler)`)
    because C# binds delegate-property invocation in preference to extension
    methods. Property names are unchanged; existing
    `new ButtonElement(…) { OnClick = … }` syntax still compiles. Passing
    `null` clears any previously-set handler. (spec 039 §0.1, §14 #1)
  - **Named-style helpers.** `.AccentButton()`, `.SubtleButton()`,
    `.TextLink()` (overloaded across `ButtonElement`, `DropDownButtonElement`,
    `SplitButtonElement`, `ToggleSplitButtonElement`, and
    `HyperlinkButtonElement` where applicable); InfoBar severity helpers
    `.Informational()` / `.Success()` / `.Warning()` / `.Error()`;
    `Card(child)` factory with theme-aware background and stroke; type-ramp
    factories `Title` / `Subtitle` / `Body` / `BodyStrong` / `BodyLarge`
    mapping to the WinUI 3 `*TextBlockStyle` resources. (spec 039 §2, §17)
  - **New events exposed.** `CalendarView.OnSelectedDatesChanged`;
    `Frame.OnNavigated` / `OnNavigating` / `OnNavigationFailed`;
    `ScrollView.OnViewChanged`; `Popup.OnOpened`;
    `WebView2.OnWebMessageReceived` / `OnCoreWebView2Initialized`;
    `MediaPlayerElement.OnMediaOpened` / `OnMediaEnded` / `OnMediaFailed`;
    `ContentDialog.OnOpened`; `Image.OnImageOpened` / `OnImageFailed`;
    `ComboBox.OnDropDownOpened` / `OnDropDownClosed`;
    `DataGrid.OnSelectionChanged`; universal multi-select
    `OnSelectionChanged` on `ListView` / `GridView` / `ListBox` (with
    `IReadOnlyList<int>` snapshot) and the typed peers `ItemsView<T>` /
    `TemplatedListView<T>` / `TemplatedGridView<T>` (with `IReadOnlyList<T>`
    snapshot). TreeView multi-select is intentionally deferred. (spec 039 §3,
    §5.8, §14 #3)
  - **New init properties.** Common-property gaps closed across the text,
    input, date/time, progress/layout/navigation, collection/dialog, and
    media/shape families (Phase 4 / Phase 5 of the implementation task list).
    See spec 039 §14 #4 for the inventory.

### Changed (breaking)

- **`ScrollView()` factory now mounts the modern
  `Microsoft.UI.Xaml.Controls.ScrollView`; the legacy
  `Microsoft.UI.Xaml.Controls.ScrollViewer` mapping moved to a new
  `ScrollViewer()` factory.** Reactor's `ScrollView()` previously named the
  ergonomic Reactor wrapper but mounted the classic `ScrollViewer`, leaving
  the new control's capabilities (`ContentOrientation`, anchor ratios, the
  `Scrolling*` enum surface) unreachable from the DSL. Migration: rename
  existing `ScrollView(...)` call sites to `ScrollViewer(...)` when you want
  to keep the classic control (and the existing `OnViewChanged` /
  `IsIntermediate` event shape, the parallax animation infrastructure, and
  `ScrollViewer.SetXxx` attached-property patterns). Reach for the new
  `ScrollView(...)` factory when you want the modern control. The element
  records follow the same rename: `ScrollViewElement` → `ScrollViewerElement`
  for the legacy element; `ScrollViewElement` is now the new control's
  record. (Issue #348)

- **`.Margin(double, double)` and `.Padding(double, double)` parameter order
  swapped to match CSS shorthand convention.** Was `(horizontal, vertical)`;
  now `(vertical, horizontal)`. This aligns with CSS — `padding: 16px 14px;`
  means top/bottom = 16, left/right = 14, vertical first. Any existing
  positional 2-arg call site in the repo has been migrated to the named-arg
  form (`.Margin(horizontal: 16, vertical: 8)`) which preserves layout
  regardless of parameter order; recommend the same for external callers.
  Pre-1.0 breaking change is intentional — the original ordering was a
  layout-rotation footgun for agents and humans with CSS muscle memory.
  (spec 038 §3 — feedback from 525-run corpus / WPF-vs-CSS mental model)

### Added

- `mur check --trace <path>` — append one JSONL row per parsed diagnostic
  to `<path>` (in addition to stdout) for offline mining. Schema:
  `{ts, code, severity, file, line, col, msg, receiver_type?, member?, mode}`.
  Source code text is never written; absolute paths outside the project
  root are redacted to `<external>`. (spec 038 §0.3)
- Tier-2 Roslyn semantic suggester for `mur check`. Covers CS1061, CS0103,
  CS0117, CS1503, CS7036 against `Microsoft.UI.Reactor.*` symbols; emits
  `→ try: <text>  // [<evidence>]` on the diagnostic line above the per-code
  confidence threshold (default 0.75). Tier-1 analyzer-ID hints still win
  ties. (spec 038 §5, §1.1–§1.6)
- Per-code emit thresholds for the Tier-2 SymbolSuggester
  (`src/Reactor.Cli/Check/Suggesters/Thresholds.cs`) calibrated against the
  spec-037 50-run corpus. CS1061 raised to 0.80 (the structural-rewrite
  fixes in the corpus would otherwise risk false positives); CS0103 / CS0117
  / CS1503 / CS7036 held at 0.75 default. Tuning harness lives in
  `tests/Reactor.Tests/CheckCommandTests/Tuning/`; first run snapshot at
  `docs/specs/tasks/038-tuning-reports/2026-05-10-50run.md`. (spec 038 §1.8,
  Data Checkpoint B)
- EC1 5×N eval (2026-05-10): `reactor-kanban-mur-check` beats baseline on
  cost mean (−24%), cost median (−33%), and wall-time variance (CV 24% vs
  81%); paired analysis wins 4 of 5 rounds. `reactor-calc-mur-check`
  regresses (+21% cost) because the suggester's per-invocation overhead
  (~5–8s) does not amortize on ~150 LoC projects with no API exploration
  surface to skip. Finding captured as a new spec 038 §11 risk + §14 open
  question on a project-size / diagnostic-count gate; merge to `main`
  pending product decision on path. No code change in this entry — eval
  result + spec doc updates only.
- `MUR_TELEMETRY=1` opt-in: appends `(code, suggester, confidence,
  evidence_short)` per emitted suggestion to
  `~/.mur/telemetry/<yyyy-mm-dd>.jsonl`. Local-first, scoped to the active
  project; no source code, file paths, or machine identifiers logged.
  (spec 038 §10, §1.7)
- `mur check --suggest-threshold <N>` — gate Tier-2 suggestions by
  per-invocation unique CS-prefixed diagnostic count. Default 3, set 0 to
  always emit. Resolution of the EC1 calc-vs-kanban split: small builds
  (1–2 errors) skip the ~5–8 s Tier-2 setup the agent doesn't need;
  larger structural failures still get suggestions. Counts the same dedup
  key `EmitDiagnostics` uses. (spec 038 §11 risk row, §14 #8)
- Data Checkpoint C (spec 038 / spec 037): 525-pair mining corpus mirrored
  into `docs/specs/tasks/038-tuning-reports/2026-05-11-525run-source/`
  (1,027 fixes / 1,233 ranker rows / 104 clusters from `gpt-5.5`). Analysis
  in `2026-05-11-525run.md`. Cross-agent reproducibility bar still open —
  a second-agent drop is required before Phase-3 rule PRs. Top Phase-3
  targets surfaced: CS0117/Theme `*Background → SolidBackground`,
  CS1061/`*Element` WinUI-name → Reactor-shortcut family, CS1955/GridSize
  missing-parens-on-factory. Tier-2 per-code thresholds held at current
  values; gate threshold (3) empirically defensible at 28.7% emit rate.
  No code change in this entry — calibration + docs only. (spec 038 §1.8,
  Data Checkpoint C)
- `mur check` Phase 2 — MSBuild passthrough + deterministic pre-emit ranker.
  `mur check [<path>] [mur-flags] [-- <msbuild-args>]` — anything after a
  bare `--` is forwarded verbatim to `dotnet build`. `mur` injects `--nologo`,
  `-v:m`, and `-p:Platform={host arch}` only if the same flag is not named
  in the passthrough section (detection by flag name, not value). When
  `--trace` is on, the trace records the effective `dotnet build` argv as
  a `kind: "command"` header row so replays are bit-faithful. New mode
  flags: `--strict` (promote warnings to errors), `--final` (emit every
  diagnostic — pre-merge sweep), `--quiet` (errors only). `--emit-threshold
  <float>` overrides the per-mode ranker default (0.6 iteration / 0.0 final).
  Pre-emit ranker (`src/Reactor.Cli/Check/Ranker/PolicyTable.cs`) suppresses
  noise mid-iteration (CS1591, CS0168, IDE0xxx, NU1701/NU1605,
  MSB3245/MSB3270/MSB3277, CS8600–CS8625 nullable warnings) while always
  emitting errors. (spec 038 §8, Phase 2.1–2.3)
- `tools/Reactor.MurCheckGuardrail` — offline guardrail that audits a pair
  of `--trace` files (one iteration, one `--final`) against PolicyTable's
  universal-error floor invariant. Fails CI if a future policy-table edit
  would let a real build error get suppressed mid-iteration. The "universal
  floor" rule (Error severity always scores 1.0 regardless of code family)
  makes the invariant hold by construction today; the guardrail is the
  regression test that catches accidental violations. (spec 038 §8 Phase 2.4)
- `plugins/reactor/skills/reactor-build-and-check/SKILL.md` updated for
  the iteration / `--final` workflow. EC2 measured 0/10 production value
  on the strong "explicit done gate" framing across 6 variant runs, so
  the framing was softened post-batch: `--final` is now documented as an
  optional pre-merge sweep (for human review / CI ship-readiness gates),
  explicitly NOT a task-completion requirement. SKILL anchor wording:
  "When `mur check` exits 0, you are done." Same wording in the legacy
  root `SKILL.md`. (spec 038 §8 Phase 2.5)
- Phase-2.x — gate-input regression fix in `CheckCommand.ShouldEmitSuggestions`.
  The initial Phase-2 implementation counted the post-ranker `emittable`
  list when deciding whether to run the Tier-2 suggester. EC2 (3-round
  preview) measured Tier-2 firing collapse from EC1's 80% to 0% on
  kanban-mur because nullable warnings (CS8602/etc) were filtered out
  of the emittable list before the gate-count, closing the gate on
  builds EC1 had left open. Fixed by counting the full parsed
  `diagnostics` list — the gate measures build complexity, not stdout
  visibility. Regression test
  `RankerTests.Suggest_gate_counts_full_parsed_list_not_post_ranker_emittable`
  locks the behavior; fails the build if the bug is reintroduced.
  (spec 038 §14 #8)
- Phase-2.x — EC2 5×N PASS by median (2026-05-11). `reactor-calc-mur-check`
  beats base on every metric (cost −5.1%, tokens −5.8%, turns −5.1%,
  wall −7.9%; variance 1.9× tighter). `reactor-kanban-mur-check` at cost
  median parity ($3.30 = $3.30); mean dragged to +5.7% by R2 outlier
  (n=5, R2-excluded mean is −3.3%). First-build OK 5/5 on both variant
  arms. `--final` invocation 0/10 across both projects (SKILL framing
  doing its job). Tier-2 firing 0/10 — gate correctly inhibits on
  small-batch iteration patterns; closing the kanban token gap is
  Phase-3's scope (rules > fuzzy match). Criterion-2 guardrail audit
  deferred to a harness retrofit (post-run `mur check --final` against
  the final workspace state to generate the iter+final trace pair the
  guardrail tool audits). Phase 2 cleared to merge to `main`.
  (spec 038 §1.8 EC2 acceptance, §8, §11)
- Phase 3.1 / 3.1a — Tier-3 rule infrastructure scaffolded. New surface
  under `src/Reactor.Cli/Check/Rules/`: `IRulePattern` contract (`Name`,
  `Provenance`, `DiagnosticCodes`, `DeclaredTargets`, `TryMatch`),
  `RuleContext` + `RuleSuggestion` records, `RuleRegistry` (reflection
  discovery of `IRulePattern` implementations in `Reactor.Cli`, `Default`
  singleton, dedup on Name collisions, `BestMatch` with disable list and
  self-disable-on-unresolved-target reporting, `Statuses` for `--list-rules`),
  and `RuleSymbolResolver` (per-`CSharpCompilation` cached symbol lookup
  via `ConditionalWeakTable` — spec §3.1a's contract that rules never
  string-match `MemberAccess.Name.ValueText`). New CLI flags
  `--disable-rule <Name>` (repeatable, warns on unknown names) and
  `--list-rules` (short-circuits `dotnet build`, prints the
  name/provenance/status table, exits 0). `SuggesterOrchestrator` runs
  rules alongside Tier-2; spec §6 "rule wins over Tier-2 fuzzy match"
  preserved; rules can match diagnostic codes outside Tier-2's
  `SupportedCodes` so CS1955 / Theme-lookup rules are unblocked.
  `tests/Reactor.Tests/CheckCommandTests/Rules/RuleTargetResolutionTests.cs`
  is the §3.1a CI gate — instantiates every registered rule against a
  live Reactor `Compilation` (full assembly references, the inverse of
  `TestCompilation.Create`) and asserts every declared target resolves.
  Passes vacuously today; becomes load-bearing the moment the first
  rule lands. 35 new unit tests covering contract shape, registry
  discovery and edge cases (duplicates, throwing rules, self-disable),
  resolver cache identity, orchestrator rule-vs-Tier-2 precedence, and
  ArgsParser round-trip. Phase-3 rule PRs themselves remain blocked on
  the second-agent corpus drop (cross-agent reproducibility bar #2 of
  the Validation Gate). (spec 038 §3.1 + §3.1a)
- Spec 038 Phase-3 vocab table at `docs/specs/tasks/038-vocab-table.csv`
  (§3.0 prerequisite for any Class-B rule PR). 20 rows covering WPF /
  Silverlight / WinUI 2 / WinUI 3 → Reactor vocabulary translations,
  seeded from the 525-run report's Phase-3 priority targets plus desk
  research against `skills/reactor.api.txt`. (spec 038 §3.0)
- `GridSizePxRenameRule` (Class-A induced): CS0117 on
  `Microsoft.UI.Reactor.GridSize` where the missing member is `Pixel`,
  `Pixels`, or `Fixed` — the WPF / WinUI / legacy-XAML names — suggests
  `GridSize.Px(...)` with the same numeric argument. Cross-agent
  reproducibility STRONG: 5 events in gpt-5.5 + 4 events in sonnet-4.6 =
  9 events combined, 100% rewrite target is `Px(...)` on every row. 5
  unit tests (3 positive covering all three legacy names, 2 negative).
  Bar #5 (independent reviewer signoff) pending.
  (spec 038 §3.2, §6 Class A, Validation Gate bar #2)
- `TextBlockStyleHintRule` (Class-A induced): CS1061 or CS0117 on
  `Microsoft.UI.Reactor.Core.TextBlockElement` where the missing-member
  name is `Style` — suggests Reactor's fluent text helpers (.FontSize,
  .Bold, .SemiBold, .Italic, .Foreground) directly on the element.
  Reactor doesn't expose a Style member; the WPF/WinUI mental model
  reaches for `TextBlock.Style = SomeTitleStyle` style-resource
  attachment. Cross-agent reproducibility STRONG-after-collapse: 2
  events gpt-5.5 (fluent `.Style(...)` shape) + 3 events sonnet-4.6
  (record `with { Style = ... }` shape) = 5 events combined; the rule
  covers BOTH syntactic shapes in one rewrite. 5 unit tests.
  (spec 038 §3.2, §6 Class A)
- `ThemeBackgroundSuffixRule` (previously Class-B, **promoted to Class-A**
  by the cross-agent audit). Rule shape unchanged; the file-header
  comment now records the audit's bar #2 evidence (16+11=27 events
  across both corpora on the (CS0117, Theme, other) key) alongside the
  original vocab-table-citation justification. The Class-A / Class-B
  distinction is about evidence type, not rule shape; this rule was
  authored from the vocab table first and the corpus later confirmed it.
  (spec 038 §6 Class A re-classification)
- **Critical fix — `CompilationLoader` now resolves `ProjectReference`
  outputs.** Without this, every Tier-3 rule self-disabled on real `mur
  check` invocations against Reactor apps: the loader only parsed
  `project.assets.json`'s `targets` section (NuGet packages), so Reactor
  itself (a project reference for every sample app) was invisible. New
  code walks `libraries.<id>` entries with `type=project`, reads the
  referenced csproj's `<AssemblyName>` (or falls back to the basename),
  and locates the most-recently-built matching `.dll` under that
  project's `bin/` subtree. Regression locked by
  `CompilationLoaderTests.Resolves_ProjectReference_built_dll_from_project_assets_json`.
  Unit tests passed before this fix because they use synthetic in-memory
  compilations; end-to-end smoke against `samples/apps/wordpuzzle` was
  what surfaced the silent failure mode. (spec 038 §5 + §6 + §3.1a)
- **Suggest-gate carve-out for Tier-3 rules.** The gate
  (`--suggest-threshold`, default 3 unique CS-prefixed diagnostics) was
  wrapping the entire suggester block — when closed, neither Tier-2 nor
  rules ran. The gate exists to suppress Tier-2 fuzzy match's noise on
  small builds; rules are precision-anchored (Roslyn ISymbol binding)
  and shouldn't be subject to that calibration. `SuggesterOrchestrator`
  now takes a `tier2Enabled` bool; `CheckCommand.Run` always builds the
  orchestrator (when the compilation loads) and passes the gate result
  in. Tier-3 rules always run when their diagnostic code surfaces;
  Tier-2 stays gated. Two new tests lock this down. This is the EC2
  watch-item ("Phase-3 rules are the right lever — not Phase-2.x gate
  tuning") finally addressed in code. (spec 038 §11 + §14 #8, EC2
  watch-item)
- First Class-A induced rule: `GridSizeFactoryParensRule` (CS1955 on
  `Microsoft.UI.Reactor.GridSize.Auto()` → suggest `GridSize.Auto` —
  i.e. drop the parens, since `Auto` is a static property and only
  `Star(double)` / `Px(double)` are methods). Cross-agent reproducibility
  STRONG: 146 events combined across the gpt-5.5 525-run (110 events) and
  claude-sonnet-4.6 525-run (36 events) corpora, top-frequency cluster in
  both at 10.7% / 9.8% of fixes respectively. Both corpora are unanimously
  about `Auto` — every captured row's `diag.member` field is exactly
  "Auto". **First cross-tier rule**: CS1955 is outside Tier-2's
  `SupportedCodes`, so the orchestrator's `RulesCoverCode` path is now
  load-bearing for at least one diagnostic code. 5 unit tests (3 positive
  fixtures from distinct cross-corpus `run_id`s, 2 negative — lookalike
  `Acme.GridSize` in a user namespace plus a synthetic non-CS1955 diag
  gate test). Validation Gate cleared on bars #1–#4 + #6; bar #5
  (independent reviewer signoff) pending. Cross-agent audit recorded at
  `docs/specs/tasks/038-tuning-reports/2026-05-11-cross-agent-audit.md`.
  (spec 038 §3.2, §6 Class A, Validation Gate bar #2)
- §3.1a per-rule performance bound test: `RulePerformanceTests.BestMatch_median_under_per_rule_budget`
  (`[Trait("Category","Perf")]`) asserts `RuleRegistry.Default.BestMatch`
  median across 1000 iters on the canonical CS1061-on-`ButtonElement.OnClick`
  fixture stays under `0.5 × rule_count × 4` ms (4× CI slack matches
  `CompilationLoaderTests` convention). Was deferred until the first rule
  landed; now load-bearing for the rule set. (spec 038 §3.1a)
- Cross-agent reproducibility audit at
  `docs/specs/tasks/038-tuning-reports/2026-05-11-cross-agent-audit.md`
  comparing gpt-5.5 and claude-sonnet-4.6 525-run corpora on receiver-typed
  clusters. Verdicts: three Class-A targets STRONG (CS1955/GridSize/other
  146 events; CS0117/Theme/other 27; CS0117/GridSize/renamed_member 7);
  two more strong after rule-design collapse (TextBlockElement member rename
  + TemplatedListViewElement<T> generalized over `<T>`); one striking
  gpt-5.5-only signal (CS1955/GridElement, 29 events, zero in sonnet —
  deferred to a third corpus drop). Closes Data Checkpoint C's
  cross-agent-reproducibility gap. (spec 038 §3.0, Validation Gate bar #2)
- First three Class-B vocabulary-translation rules: `ThemeBackgroundSuffixRule`
  (CS0117 on `Theme` with member ending in `Background` → `Theme.SolidBackground`,
  cluster C0019, 16 events); `AlignmentShortcutRule` (CS1061 on Reactor
  `*Element` receivers for `HorizontalAlignment` / `VerticalAlignment` →
  `.HAlign(...)` / `.VAlign(...)`, cluster C0017 + adjacent ≈ 22 events); and
  `ButtonOnClickFactoryMoveRule` (CS1061 on `ButtonElement.OnClick` →
  `Button(..., onClick: ...)` factory named-arg, explicitly naming `.OnTapped`
  as the wrong sibling to keep agents from reaching for the gesture event).
  Both bind target types via `RuleSymbolResolver` (no string matching);
  the rule-target resolution CI gate is now load-bearing. 10 new unit tests
  (positive fixtures cite their source `run_id`s from the 525-run corpus;
  hand-authored extensions are tagged `[Trait("Origin", "VocabHandAuthored")]`).
  PRs remain blocked on Validation Gate bar #5 (independent reviewer
  signoff) — the artifacts are "ready for review", not "ready to merge".
  (spec 038 §3.2, §6 Class B)
- `.Margin(...)` and `.Padding(...)` per-side overloads now default unspecified
  sides to `0.0`. Enables agent-intuitive call shapes like `.Margin(top: 12)`,
  `.Padding(left: 8, right: 8)` that previously failed to compile (CS7036:
  no matching overload). 525-run corpus shows **198 build failures** from
  agents writing this exact shape against the prior all-required signature —
  far and away the highest-frequency failure-driver in the drop. Eliminating
  it is a single-line code edit per overload but a large agent-productivity
  unlock. `Reactor.Tests` adds CSS-ordering + per-side + positional-overload
  regression tests. (spec 038 §3 follow-up — surfaced during Phase-3 rule
  authoring)
- Cheatsheet in `plugins/reactor/skills/reactor-getting-started/SKILL.md` now
  shows the named-arg `Button("Save", onClick: handler)` form alongside the
  positional one, with an explicit anti-pattern comment naming `.OnClick(...)`
  and `.OnTapped(...)` as the wrong fixes for click intent. The cheatsheet's
  `.OnTapped((s, e) => ...)` example is now anchored to non-Button surfaces
  (Border / Image / ScrollView) with a back-reference to the Controls section
  — the prior parenthetical Button carve-out was easy to miss mid-build.
  (spec 038 §3 — agent-facing skill updates)
- Spec 038 EC3-final watch-item: `rule_fired` trace event. When a Tier-3
  rule attaches a suggestion to a diagnostic, `mur check --trace` now writes
  one structured row per fire:
  `{kind: "rule_fired", rule, code, confidence, evidence, file, line, mode}`.
  Per-rule firing-rate audits collapse from multi-step content scans against
  `events.jsonl` agent tool outputs to a 1-line `jq` over the trace file.
  Tier-2 suggestions deliberately do not emit this row — Tier-2 firing rates
  are visible via the opt-in `MUR_TELEMETRY=1` channel. (spec 038 §0.3,
  EC3-final watch-item)
- Spec 038 §3.1a residual: trace-channel structured warning hook for
  self-disabled rules. `TraceWriter.WriteRuleSelfDisabled(rule, target)`
  emits `{kind: "rule_self_disabled", rule, unresolved_target, mode}`.
  `SuggesterOrchestrator` threads an optional `onRuleSelfDisabled`
  callback through to `RuleRegistry.BestMatch`; `CheckCommand.Run` wires
  it to the active trace writer when `--trace <path>` is set, dedup'd
  per-invocation per-rule. Stdout stays clean — agents don't read trace
  files, but maintainers see "rule X disabled because target Y didn't
  resolve" the moment a Reactor minor release breaks something.
  (spec 038 §3.1a)
- EC1 re-run with the diagnostic-count gate (2026-05-11): both arms PASS.
  `reactor-calc-mur-check` cost −4% mean (was +21% in the prior batch);
  `reactor-kanban-mur-check` cost −33% mean / −39% median (was −24% mean
  — preserved and grew). First-build OK 5/5 both variant arms. Phase 1
  acceptance bar met; Phase 1 cleared to merge to `main`. Watch-item
  carried into Phase 2: kanban CV widened (24% prior → 54%) because one
  of five runs hit 0 firings and took the long-tail base path — gate
  behavior is path-dependent on the agent's exploration order. Below
  the resolution threshold for a Phase-1 blocker; Phase 2 telemetry
  should track per-run firing counts. (spec 038 §1.8 EC1 acceptance,
  §11 risk row, §14 #8)
- `WindowSpec`, `ReactorWindow`, `WindowKey`, `WindowStartPosition`,
  `PresenterKind`, `WindowState`, `WindowIcon`, `WindowDipSizeChangedEventArgs`,
  `WindowClosingEventArgs`, `ReactorAppContext` — first-class Window primitive
  promoted out of internal hosting wiring. `ReactorApp.Run(Action<ReactorAppContext>)`
  is the new multi-window startup surface; the existing `Run<TRoot>` overload is
  preserved as a thin wrapper. (spec 036 §3, §4)
- `ReactorApp.OpenWindow`, `Windows`, `PrimaryWindow`, `FindWindow`,
  `WindowOpened` / `WindowClosed`, `Exit`, `ShutdownPolicy`, `UIDispatcher` —
  process-wide window topology. (spec 036 §4.3, §6)
- Per-window DPI awareness — `ReactorWindow.Dpi`, `DipScale`, `DpiChanged`;
  WindowMessageMonitor (`SetWindowSubclass`) for WM_DPICHANGED and
  WM_GETMINMAXINFO; DIP→physical conversion in initial size, `SetSize`,
  `SetPosition`. Min/max constraints flow through WM_GETMINMAXINFO so
  dragging across a DPI boundary respects spec'd minimums. (spec 036 §5)
- `RenderContext.UseDpi()`, parameterless `UseWindowSize()`,
  `UseBreakpoint(double)`. (spec 036 §5.2)
- `ReactorWindow.Activated`, `Deactivated`, `SizeChanged`, `StateChanged`,
  `Closing`, `Closed` events with UI-thread synchronous dispatch.
  `Closing` runs `UseClosingGuard` predicates first then subscribers; any
  false cancels. (spec 036 §6.3, §7)
- `RenderContext.UseWindow()`, `UseWindowState()`, `UseIsActive()`,
  `UseClosingGuard(Func<bool>)`. Tray-flyout fallback semantics match
  spec §7.1 (null/Normal/true/no-op). (spec 036 §7)
- `RenderContext.UseOpenWindow(WindowKey, WindowSpec, Func<Component>)`
  + `Component.UseOpenWindow` mirror — open or reuse a secondary window
  keyed by `WindowKey`. Identity-stable across re-renders; spec changes
  flow through `ReactorWindow.Update`; parent unmount does not
  auto-close the child. (spec 036 §4.3 / §15.6)
- `ReactorWindow.PersistedScope` — per-window
  `Core.WindowPersistedScope`, disposed when the window closes.
  `RenderContext.UsePersisted(_, _, PersistedScope.Window)` now resolves
  to this per-window store, so two windows of the same component class
  hold independent persisted state. (spec 036 §3.4 / §4.4 — closes spec
  033 §7.5.)
- `ShutdownPolicy.OnPrimaryWindowClosed` exits when the primary window
  closes (not just when the snapshot empties); `OnLastSurfaceClosed`
  considers tray icons (Phase 8 fills the registry). The default
  zero-window startup-callback path now exits under
  `OnLastSurfaceClosed` too when no tray icons were opened. (spec 036
  §6.2)
- `IWindowPersistenceStore`, `PackagedSettingsStore`, `JsonFileStore`,
  and `ReactorApp.WindowPersistenceStore` — pluggable per-window
  placement persistence. Default auto-detect picks the WinRT settings
  store for packaged apps and a hand-rolled, AOT-safe JSON file store
  (1 MB cap, atomic write-then-rename, base64-per-id) for unpackaged
  apps. `WindowSpec.PersistenceId` opts in; placement saves on close
  and restores on first show via `WindowPlacementCodec` with a monitor-
  layout fingerprint borrowed from `WinUIEx.WindowManager`. (spec 036
  §8)
- `WindowSpec.Backdrop` is now seeded as a window-level default through
  `BackdropApplier.SetWindowDefault`, so the first frame paints the
  declared material even when the root component tree carries no
  `BackdropChoice` modifier. Tree-level modifiers still win on
  subsequent renders. (spec 036 §3.3)
- Owned-window relationship via `WindowSpec.Owner` — applies the Win32
  `GWLP_HWNDPARENT` slot at construction time and force-hides the owned
  window from the taskbar / Alt-Tab. Owner-close cascades to owned
  children with `WindowCloseReason.OwnerClosed`; if any owned guard
  cancels, the owner-close cancels too. (spec 036 §9)
- `ReactorWindow.Progress` (`TaskbarProgress`, with `TaskbarProgressState`
  enum: None / Indeterminate / Normal / Paused / Error) and
  `ReactorWindow.Overlay` (`TaskbarOverlay` with `Icon` /
  `AccessibleDescription`). Both lazy-initialize the
  `ITaskbarList3` COM wrapper through `TaskbarComSingleton` so apps that
  never touch the shell surface pay no startup cost. (spec 036 §11.1 / §11.2)
- `ReactorWindow.SetThumbnailToolbar(IReadOnlyList<ThumbnailToolbarButton>)`
  / `ClearThumbnailToolbar()` — up to seven buttons; first call uses
  `ThumbBarAddButtons`, later calls use `ThumbBarUpdateButtons`.
  Validation rejects > 7, duplicate Ids, empty Ids, null OnClick. Click
  dispatch hooks WM_COMMAND in `WindowMessageMonitor`. HICONs are
  released on `ReactorWindow.Dispose`. (spec 036 §11.5)
- `JumpList`, `JumpListItem`, `JumpListItemKind` — process-scoped jump
  list. Packaged path uses `Windows.UI.StartScreen.JumpList`; unpackaged
  falls back to a hand-rolled `ICustomDestinationList` wrapper
  (`JumpListComInterop`) gated by runtime `Package.Current` detection
  through the new `PackageRuntime` helper. `AppUserModelId`,
  `ShowRecent`, `ShowFrequent` are settable. `JumpListItem.ForUri(...)`
  factory is the recommended way to build entries — pairs with
  `LaunchActivation.TryResolve<TRoute>(map)` for the navigation handoff.
  (spec 036 §11.3 / §11.6)
- `LaunchActivation` parsing — `OnLaunched` now reads
  `Microsoft.Windows.AppLifecycle.AppInstance.GetActivatedEventArgs`
  for File / Protocol / Toast activations and falls back to the WinUI
  `LaunchActivatedEventArgs.Arguments` + `Environment.GetCommandLineArgs`
  for jump-list / tray re-launches. `LaunchActivation.TryResolve<TRoute>`
  bridges the launch argument string into the existing
  `DeepLinkMap<TRoute>` so jump-list / tray entries become a one-liner
  navigation handoff. (spec 036 §11.6, implementation-time addition)
- `ReactorTrayIcon` + `TrayIconSpec` — system-tray icon as a peer of
  `ReactorWindow`. `ReactorApp.OpenTrayIcon`, `TrayIcons` snapshot,
  `FindTrayIcon`, `TrayIconOpened` / `TrayIconClosed` events; mirrored
  on `ReactorAppContext`. Hidden message-only window
  (`TrayHiddenWindow`) routes `Shell_NotifyIcon` callbacks back to the
  UI thread under NOTIFYICON_VERSION_4 semantics. `Click`,
  `DoubleClick`, `RightClick` events fire on the UI thread.
  `Update(spec)` diffs icon / tooltip / visibility; `Close` /
  `Dispose` removes the icon and unregisters from `ReactorApp.TrayIcons`.
  `OnLastSurfaceClosed` now reads the real `TrayIconCount` and
  re-evaluates on tray close so a tray-only app exits cleanly when the
  final icon goes away. (spec 036 §11.4)
- `RenderContext.UseTrayIcon(TrayIconSpec)` + `Component.UseTrayIcon`
  mirror — opens (or reuses by key) a tray icon scoped to the calling
  component. The trailing `UseEffect` cleanup closes the icon on
  unmount; spec changes flow through `Update` via a record-keyed
  `UseEffect`. (spec 036 §11.4)
- Seven live-shell selftest fixtures under
  `tests/Reactor.AppTests.Host/SelfTest/Fixtures/WindowModelFixtures.cs`:
  `WindowModel_LifecycleEvents`, `_ClosingEventCancels`,
  `_TaskbarProgressLiveCom`, `_ThumbnailToolbarLiveCom`,
  `_PersistedScopeIsolated`, `_TrayIconRoundTrip`,
  `_UseOpenWindowReusesByKey`. They exercise the public surface against
  real HWND / `ITaskbarList3` / `Shell_NotifyIcon` COM, opening
  secondary `ReactorWindow`s through `ReactorApp.OpenWindow` and
  cleaning up under `ShutdownPolicy.Explicit` so they don't kill the
  host harness. 33/33 assertions pass alongside the full 2314-assert
  selftest matrix. (spec 036 §0.5 / §0.6 / §11)
- Devtools `windows.list / windows.activate / windows.close /
  windows.open` MCP tools (spec 036 §10). `windows.list` returns id,
  key, title, DIP size, DPI, state, isMain — driven by a new
  `WindowRegistry.Attach(ReactorWindow, ...)` overload that retains the
  back-reference. `windows.open` is gated by the same component
  allowlist as `switchComponent` so loopback callers can't spawn
  arbitrary types; `windows.close` honors `UseClosingGuard` and surfaces
  `cancelled: true` instead of hanging. The devtools `WindowRegistry` is
  now driven from `ReactorApp.WindowOpened / WindowClosed` events so
  secondary windows opened via `OpenWindow` are tracked too. CLI and
  `skills/devtools.md` plumbed.
- `Microsoft.UI.Reactor.Hooks.UseMemoCells` /
  `UseMemoCellsByKey` / `UseMemoCellsByIndex` — cell-level memoization
  hooks (extension methods on `RenderContext`, plus matching `Component`
  shims) for high-frequency list/grid bodies. Cells whose item value
  (and declared deps) haven't changed since the previous render are
  reused by reference; the reconciler short-circuits on
  `ReferenceEquals` and skips diffing entirely. (spec 034 §C)
- `REACTOR_HOOKS_007` analyzer + codefix — warns when a `UseMemoCells`
  builder lambda closes over a value that isn't declared in the
  `params deps` list, which would silently render stale. The codefix
  appends the missing capture to the deps slot. Indirect captures
  through helper methods are a documented blind spot. (spec 034 §C)
- "Memoizing list cells" section in `docs/guide/advanced.md` covering
  the three overloads, when each is the right hammer, the gen2
  trade-off, and the analyzer-as-safety-net story. (spec 034 §C)
- `tests/stress_perf/StressPerf.ReactorOptimized` — sibling bench
  variant that demonstrates the spec-034 §B direct-record-initializer
  idiom for inner-loop cell construction. The naive `StressPerf.Reactor`
  variant stays unchanged and remains the framework-level baseline; the
  new optimized sibling is the reference implementation of the perf-tips
  skill. Wired into `run_stocks_grid_baseline.ps1`,
  `run_bench_aot_publish.sh`, `run_benchmark.sh`, and
  `run_sweep_arm64.ps1`. (spec 034 §B)
- "Hot loops" section in `docs/guide/advanced.md` documenting when to
  reach for direct record initializers, the trade-offs vs the fluent
  chain, and a side-by-side worked example. Source template at
  `docs/_pipeline/templates/advanced.md.dt`. (spec 034 §B)
- `Expr(Func<Element?>)` factory in `Microsoft.UI.Reactor.Factories` for inline
  block-expression bodies inside a DSL tree, removing the
  `((Func<Element?>)(() => …))()` cast ceremony. Pure composition — no hooks,
  no memoization, no reconciler boundary. (spec 033 §5)
- `IPersistedStateScope` interface, `PersistedScope` enum (`Window` /
  `Application`), `ApplicationPersistedScope` (process-wide singleton at
  `ApplicationPersistedScope.Default`, capacity 4096), and
  `WindowPersistedScope` (per-host instance, capacity 1024). All backed by an
  internal `LruCache<TKey,TValue>`. New `RenderContext.UsePersisted<T>(key,
  initial, PersistedScope)` overload makes the scope explicit. (spec 033 §2)
- `Microsoft.UI.Reactor.Factories.RenderEachTime(Func<RenderContext, Element>)` —
  explicit factory for "inline component with own hooks that re-renders every
  parent render". Replaces the soft-deprecated `Func(...)` for the rare cases
  that genuinely want always-re-render semantics. (spec 033 §4)
- `Microsoft.UI.Reactor.GridSize` value type with `Auto` / `Star(weight)` /
  `Px(pixels)` smart constructors, implicit conversion to
  `Microsoft.UI.Xaml.GridLength`, and a strict invariant-culture string
  parser (`Parse`). New typed `Grid(GridSize[], GridSize[], …)` factory
  overload. (spec 033 §1)
- `samples/InteropFirst` — XAML-window-hosts-Reactor demonstration with
  shared `ObservableCollection<Order>`, shared `ICommand`s bridged through
  `CommandInterop.FromCommand`, and shared `App.xaml` brush resources flowing
  through props into a Reactor `Component<TProps>`. (spec 033 §7)
- `BackdropKind` enum and `.Backdrop(BackdropKind)` / `.Backdrop(Func<SystemBackdrop?>)`
  modifier on the root tree for declarative Mica / Acrylic on Reactor-hosted
  windows. `ReactorHost` applies the modifier at the end of each reconcile
  pass and resets the window's backdrop on dispose; `ReactorHostControl` that
  does not own its window no-ops with a one-shot debug log. (spec 033 §6)
- `ElementRef<T>` typed-ref wrapper (`Microsoft.UI.Reactor.Input`),
  `UseElementRef<T>()` hook (`Microsoft.UI.Reactor.Hooks`), and a strongly-typed
  `.Ref<T,TElement>(...)` modifier overload. The typed surface removes the
  `(Button)ref.Current` cast at consumers and adds a DEBUG-only assertion when
  a typed ref is bound to an element of the wrong concrete type. AOT-safe and
  reflection-free at the public surface. (spec 033 §3)
- `Component.UsePersisted<T>(key, initial, PersistedScope)` three-arg overload
  so component subclasses can declare the persisted-state scope (Window vs
  Application) explicitly at the call site, matching the
  `RenderContext.UsePersisted` overload added earlier. (spec 033 §2)

### Changed

- **Spec 034 — Element allocation reduction.** Three independent
  allocation cuts in one PR: bucketed `ElementModifiers` (transparent
  storage shim, ~−11% bytes/tick on the 4,900-cell stress grid),
  direct-record-initializer idiom for inner cell loops (~−60% bytes
  per cell), and `UseMemoCells` cell-level memoization. Verified at
  PR-close on ARM64 Release with full ETW Present-tracking across
  10/20/50/100% mutation, all eight stress_perf variants:
  **ReactorOptimized at 10% mutation reaches 17.1 Effective Refresh/s
  — within noise of DirectX (17.2) and Wpf (17.9), and +66% over
  naive Reactor (10.3).** Reconcile-time win on the same A/B: −76% at
  10% (32.5 ms → 7.9 ms), −61% at 20%, −31% at 50%, −12% at 100% —
  memo's win tracks the partial-reuse opportunity exactly as
  predicted. DirectX runs away at saturation (50%+) — no allocating
  framework can keep up there. Component A in isolation (naive
  Reactor pre-shim vs post-shim, same source, no app-code changes)
  shows renders/sec within run-to-run noise at 20/50/100% — its win
  is allocation-side, not renders-side, on this hardware. See
  `docs/specs/034-element-allocation-reduction.md` § "Verified
  close-out — 2026-05-03" for the full eight-variant matrix and
  reads. (spec 034)
- `ElementModifiers` now stores layout and visual fields in
  `LayoutModifiers` / `VisualModifiers` sub-records. Existing call sites are
  unaffected — public properties (`Padding`, `Margin`, `Foreground`,
  `Background`, …) shim through to the appropriate bucket on read and write.
  Perf-critical inner loops may construct buckets directly via the new
  `Layout = …` / `Visual = …` initializer slots to avoid a fat
  `ElementModifiers` clone per fluent step. (spec 034 §A)
- `PersistedStateCache` rewritten over an LRU cache with eviction-on-full
  semantics. The previous "refuse new keys when 4096 entries are present"
  policy is replaced — later, hotter keys are no longer starved by the
  first 4096 keys ever recorded. Application-scope registers an
  `Windows.System.MemoryManager.AppMemoryUsageIncreased` handler and trims
  to 25% of capacity when the OS reports `OverLimit` / `High`. Best-effort:
  hosting models that do not expose the event log a notice and carry on.
  Key validation now requires non-empty keys ≤ 256 chars. (spec 033 §2)
- `GridDefinition` gains a strongly-typed constructor accepting `GridSize[]`
  for columns and rows. The legacy string-array constructor is preserved for
  backward compatibility. (spec 033 §1)
- `ApplicationPersistedScope` and `WindowPersistedScope` now emit one-line
  `Debug.WriteLine` diagnostics on construction, disposal, and (for the
  application scope) memory-pressure trim. Logs only counts and capacity —
  never keys or values, since keys may be derived from user-controlled
  identifiers in apps. (spec 033 §7.10)
- `samples/Reactor.TestApp/Demos/PersistedDemo`, `NavigationDemo`, and
  `samples/apps/regedit` migrated to the explicit
  `UsePersisted(key, initial, PersistedScope.Window)` overload to document
  per-window intent at the call site. (spec 033 §2)

### Deprecated

- `Microsoft.UI.Reactor.Factories.Grid(string[], string[], params Element?[])`
  is marked `[Obsolete]`. Use the strongly-typed
  `Grid(GridSize[], GridSize[], params Element?[])` overload with
  `GridSize.Auto` / `GridSize.Star(weight)` / `GridSize.Px(pixels)` instead.
  Slated for removal in the next minor release. (spec 033 §1)
- `Microsoft.UI.Reactor.Factories.Func(Func<RenderContext, Element>)` is
  marked `[Obsolete]`. Replace with `Memo(ctx => …)` (render once + state
  changes) or `RenderEachTime(ctx => …)` (always re-render). Slated for
  removal in the next minor release. (spec 033 §4)

### Breaking changes (deferred)

Naming-alignment renames that introduce an `[Obsolete]` forwarding alias today
and remove the old name in the next minor release.

- `Microsoft.UI.Reactor.Factories.RichText(string)` and
  `RichText(RichTextParagraph[])` renamed to `RichTextBlock(...)` for parity
  with WinUI's `Microsoft.UI.Xaml.Controls.RichTextBlock` (record was already
  `RichTextBlockElement`). The old `RichText` factory is preserved as a
  thin `[Obsolete]` forwarding alias for one release; slated for removal in
  the next minor release. (spec 039 §1.3 / §14 #8)
- No `Microsoft.UI.Reactor.Factories.ScrollViewer` alias. (Originally
  considered as a discoverability hint for callers reaching for the
  WPF/WinUI-legacy name, but the alias would shadow
  `Microsoft.UI.Xaml.Controls.ScrollViewer`'s attached-property type for
  callers using `using static Microsoft.UI.Reactor.Factories;` alongside
  `using Microsoft.UI.Xaml.Controls;` — forcing them to fully-qualify
  `ScrollViewer.SetVerticalScrollMode(...)` etc. Discoverability win
  didn't justify the imposed disambiguation cost on existing consumers.
  Use `ScrollView` directly.) (spec 039 §6 / §16)
- `Microsoft.UI.Reactor.Factories.ProgressBar(double)` and `ProgressBar()`
  added as `[Obsolete]` aliases for the existing `Progress(double)` /
  `ProgressIndeterminate()` factories. Reactor's `Progress` reconciles to
  WinUI's `ProgressBar`; the alias lets agents reaching for the WinUI name
  discover it. (spec 039 §5 / §16)

### Removed

- `ReactorHost.MainDispatcherQueue` (internal static, first-host-wins
  capture). Cross-thread setState marshalling and AutoSuggest's
  `RaiseStateChanged` now route through `ReactorApp.UIDispatcher`.
  `ReactorHost` ctor seeds `UIDispatcher` for embedded
  `ReactorHostControl` scenarios that bypass `ReactorApp.Run`.
  (spec 036 §4.3)

### Fixed

### Security
