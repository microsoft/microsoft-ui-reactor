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

### Deprecated

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
