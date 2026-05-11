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

### Removed

- `ReactorHost.MainDispatcherQueue` (internal static, first-host-wins
  capture). Cross-thread setState marshalling and AutoSuggest's
  `RaiseStateChanged` now route through `ReactorApp.UIDispatcher`.
  `ReactorHost` ctor seeds `UIDispatcher` for embedded
  `ReactorHostControl` scenarios that bypass `ReactorApp.Run`.
  (spec 036 §4.3)

### Fixed

### Security
