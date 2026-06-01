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

- **`mur check` — fast feedback with skill pointers (spec 038).** `mur
  check` is the build (same exit code as `dotnet build`) plus two
  enrichments: skill pointers for known `REACTOR_*` IDs and did-you-mean
  `→ try:` suggestions for unknown identifiers. Three suggester tiers:
  Tier-1 analyzer-ID hints, Tier-2 Roslyn semantic suggester (CS1061 /
  CS0103 / CS0117 / CS1503 / CS7036), Tier-3 precision rules anchored on
  Roslyn `ISymbol` binding (`GridSizeFactoryParensRule`,
  `GridSizePxRenameRule`, `TextBlockStyleHintRule`,
  `ThemeBackgroundSuffixRule`, `AlignmentShortcutRule`,
  `ButtonOnClickFactoryMoveRule`). Workflow modes: default iteration mode
  suppresses cosmetic noise; `mur check --final` is an optional pre-merge
  sweep; `--strict`, `--quiet`, and `mur check -- <msbuild-args>`
  passthrough also supported. `--trace <path>` writes JSONL diagnostic
  rows; `MUR_TELEMETRY=1` opt-in logs per-suggestion telemetry locally.
  Validated end-to-end across multi-arm EC1/EC2/EC3 evals.

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

- **`Microsoft.UI.Reactor.Controls.MaskedTextFieldDsl.MaskedTextField(...)`**
  renamed to `MaskedTextBoxDsl.MaskedTextBox(...)`. Old name preserved as
  an `[Obsolete]` forwarding alias for one release; slated for removal in
  the next minor release. (issue #389)

- **`Microsoft.UI.Reactor.Factories.Grid(string[], string[], …)`** —
  use the strongly-typed `Grid(GridSize[], GridSize[], …)` overload
  with `GridSize.Auto` / `GridSize.Star(weight)` / `GridSize.Px(pixels)`.
  Slated for removal in the next minor release. (spec 033 §1)

- **`Microsoft.UI.Reactor.Factories.Func(Func<RenderContext, Element>)`** —
  replace with `Memo(ctx => …)` (render once + state changes) or
  `RenderEachTime(ctx => …)` (always re-render). Slated for removal in
  the next minor release. (spec 033 §4)

- **`Microsoft.UI.Reactor.Factories.RichText(...)`** renamed to
  `RichTextBlock(...)` for parity with WinUI's `RichTextBlock` (record
  was already `RichTextBlockElement`). Old name preserved as an
  `[Obsolete]` alias for one release. (spec 039 §1.3)

- **`IDockBehavior` and `DockManager.Behavior`** (spec 045 Phase 1) marked
  `[Obsolete]` with migration pointers to the per-event Action props
  that landed in Phase 2 (`OnContentDocked` / `OnContentFloating` /
  `OnContentFloated`). Slated for removal one release after Phase 2 ships.
  (spec 045 §2.12)

### Added (discoverability aliases)

- **`Microsoft.UI.Reactor.Factories.ProgressBar(double)` / `ProgressBar()`**
  added as `[Obsolete]` aliases for `Progress(double)` /
  `ProgressIndeterminate()`. Reactor's `Progress` reconciles to WinUI's
  `ProgressBar`; the alias helps agents reaching for the WinUI name
  discover it. (spec 039 §5)

### Removed

- **`ReactorHost.MainDispatcherQueue`** (internal static, first-host-wins
  capture). Cross-thread setState marshalling and AutoSuggest's
  `RaiseStateChanged` now route through `ReactorApp.UIDispatcher`.
  (spec 036 §4.3)

### Fixed

### Security
