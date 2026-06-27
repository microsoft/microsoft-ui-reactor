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

- **RichTextBlock inline-UI scroll drift now fixed at the root, not just mitigated
  (issue #717).** The #487 scroll-anchor (below) restored the offset reactively
  *after* the ancestor scroll host clamped it. The underlying cause is that the live
  app applies the document mutation inside the reconcile and then returns to the
  dispatcher, letting the compositor commit a frame carrying WinUI's transient
  collapsed extent (`RemoveEmbeddedElements` → `desiredSize=0`) *before* the inline UI
  re-attaches — the very frame the scroll host clamps against. The collapse is
  scheduled on a *separate* dispatcher callback (not a layout-dirty flag), so a
  synchronous `UpdateLayout` inside the reconcile measures the still-intact tree and
  cannot coalesce it. `UpdateRichTextBlocks` now prevents the collapse at its source:
  after mutating an inline-UI-bearing block it pins the block's `MinHeight` to its
  full pre-collapse `ActualHeight` (raising the floor only, never lowering an author
  value) and releases the pin a couple of rendered frames later once the inline UI has
  re-attached. With the floor pinned the transient `desiredSize=0` can no longer shrink
  the block's measured height, so `ScrollableHeight` never drops, the scroll host never
  clamps, and there is no lost offset to restore. With this in place the #487 anchor
  becomes a belt-and-suspenders safety net. No new API surface; the pin only engages for
  blocks that actually host inline UI and actually mutated.
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

### Security
