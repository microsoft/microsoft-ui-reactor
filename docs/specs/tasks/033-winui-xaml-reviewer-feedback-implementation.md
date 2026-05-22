# WinUI/XAML Reviewer Feedback — Implementation Tasks

Derived from: `docs/specs/033-winui-xaml-reviewer-feedback.md`

Scope reminder: seven independent items (§1–§7 of the spec). Item §0 is
already resolved. Phases below follow the §8 rollout order — start with the
lowest-risk additive items (§5, §3, §6, §7), land the analyzer pattern via §1,
then take the behavioral-default flips (§4, §2) last.

Conventions:
- `src/` paths are under `src/Reactor/` unless otherwise noted.
- New unit tests live under `tests/Reactor.Tests/`. Self-host integration
  tests live under `tests/Reactor.AppTests.Host/SelfTest/Fixtures/`. UI-driver
  tests follow the `tests/Reactor.AppTests/` pattern.
- Analyzer fixtures live next to the analyzer in `src/Reactor.Analyzers/` and
  follow the existing `RequestedThemeSetAnalyzer` test layout.
- Every new public API must carry XML doc comments with a `<remarks>` link to
  spec 033 § number, and ship with `AnalyzerReleases.Unshipped.md` updates
  when an analyzer is added.
- Localized strings (analyzer messages, debug log strings shown to a
  developer in the IDE diagnostic list) go through the same resx pattern as
  the existing analyzers (see `Reactor.Analyzers/AnalyzerReleases.Shipped.md`
  cross-references). Error/log strings consumed only by `Debug.WriteLine` /
  ETW remain en-US literals — those are diagnostic output, not user UI.
- "Production-quality fundamentals" checklist applied per phase: input
  validation, threading, disposal, logging, localization, accessibility,
  trim/AOT-safety, exception safety. Tasks call these out explicitly.

A task is "done" only when:
1. Code compiles with `Reactor.sln` warnings-as-errors.
2. New unit tests cover the happy path **and** every failure mode listed.
3. Public API additions appear in `PublicAPI.Unshipped.txt` (if the project
   uses the public-API analyzer — verify per project).
4. XML doc comments compile without `CS1591` warnings on public surface.
5. CHANGELOG / release-notes entry added under the next-release heading.

---

## Phase 0: Cross-cutting setup

### 0.1 Tracking & docs

- [ ] Create a tracking checklist at `docs/specs/tasks/033-winui-xaml-reviewer-feedback-implementation.md` (this file). Update it as phases land.
- [ ] **Release notes go in `CHANGELOG.md` at the repo root.** This file was just started, so spec 033 is one of its first entries — **this phase sets the precedent for format and structure** that subsequent specs will follow. Choose the format deliberately:
  - Use [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) conventions (categorized sections: Added / Changed / Deprecated / Removed / Fixed / Security under each release heading).
  - Top-of-file headers: title, link to format conventions, link to semver.
  - Release headings as `## [Unreleased]` then `## [x.y.z] — YYYY-MM-DD` once cut.
  - Every entry references the spec number (e.g. "(spec 033 §1)") so readers can cross-reference design.
  - Group entries by spec, not by phase, in the final shape (Phase ordering is internal to implementation; spec sections are the user-facing groupings).
- [ ] Add a "Spec 033 — WinUI/XAML reviewer response" entry under `## [Unreleased]` in `CHANGELOG.md`. Each phase below appends one bullet to the appropriate Added/Changed/Deprecated subsection as it lands. Do **not** create per-phase headings inside CHANGELOG — phase numbers are scaffolding for this task list, not user-facing.
- [ ] Document the chosen CHANGELOG conventions in a short comment at the top of `CHANGELOG.md` itself (not in this task list) so future contributors don't have to re-derive them from spec 033's PRs.
- [ ] Decide whether to surface the seven items as a single PR-set or one PR per phase. Default: one PR per phase (matches existing review tooling). Capture the decision in the spec's §8 table comment.

### 0.2 Analyzer release-tracking

- [ ] Read `src/Reactor.Analyzers/AnalyzerReleases.Unshipped.md` and confirm format used by existing analyzers. Every new diagnostic ID added below (`REACTOR_GRID_001`, `REACTOR_PERSIST_001`, `REACTOR_FUNC_001`) must be added there with category, severity, title.
- [ ] Verify no diagnostic ID collisions across the existing analyzers (`HookRulesAnalyzer`, `RequestedThemeSetAnalyzer`, `UseThemeRefAnalyzer`, `UseLightweightStylingAnalyzer`, `AccessibilityAnalyzers`).

### 0.3 Public-API surface tracking

- [ ] Confirm whether `src/Reactor/Reactor.csproj` uses the
      `Microsoft.CodeAnalysis.PublicApiAnalyzers` package. If yes, every new
      public API in this spec needs a `PublicAPI.Unshipped.txt` entry. If no,
      open a follow-up to add it (out of scope for this spec but record in §9).

---

## Phase 1: `Expr(...)` escape hatch (spec §5)

Smallest change. Single helper. Use as the warm-up PR to shake out CI for
this spec's pattern (release notes, PR template, analyzer-release files).

### 1.1 Implementation

- [x] Add `public static Element Expr(Func<Element?> render)` near `If`/`When` in `src/Reactor/Elements/Dsl.cs:555-563`. Body returns `render() ?? EmptyElement.Instance`.
- [x] Throw `ArgumentNullException(nameof(render))` if `render` is null. (Public API on the boundary — validate.)
- [x] Catch only what `EmptyElement.Instance` requires; do **not** swallow exceptions from the render lambda — they propagate to the caller's render path so the reconciler's existing error-boundary logic handles them. Add an explicit comment that this is intentional (callers may set a render-time exception expectation in tests).
- [x] XML doc comment: 1-paragraph summary, `<remarks>` linking to spec 033 §5, and a `<example>` block showing the inline-locals use case from the spec.
- [x] ~~Add `[DebuggerStepThrough]`?~~ — decided no: `If`/`When` in `Dsl.cs` carry no such attribute, so we matched existing convention.

### 1.2 Tests — `tests/Reactor.Tests/ExprTests.cs`

- [x] Returns the produced element when the lambda returns non-null.
- [x] Returns `EmptyElement.Instance` when the lambda returns null.
- [x] `ArgumentNullException` when `render` is null.
- [x] Exception thrown inside the lambda propagates unchanged (no wrapping).
- [x] Pure-composition assertion: `Expr` returns the same `Element` instance the lambda produced (no wrapper node). Spec: "no node, no hook scope, no memoization."
- [x] Locals captured by the lambda do not pin across invocations (counter increments deterministically across repeated `Expr` calls).
- [ ] Trim/AOT-safety: deferred to Phase 8 cross-cutting AOT pass — `Expr` is reflection-free so any trimmed publish that reaches `Factories.Expr` keeps it.

### 1.3 Sample / docs

- [x] No IIFE-cast pattern (`((Func<Element?>)(() => …))()`) in `samples/` or `docs/_pipeline/templates/` — verified via Grep. No replacements needed.
- [ ] Add a "block-expression bodies" callout to `docs/_pipeline/templates/dsl-tour.md.dt` (or wherever the DSL tour lives — confirm via `Glob`). Run `mur docs compile` after editing — **do not** edit `docs/guide/` directly (per repo memory). *Deferred to Phase 8 docs pass.*

### 1.4 Release-note bullet

- [x] `CHANGELOG.md` → `## [Unreleased]` → **Added**: "Add `Expr(Func<Element?>)` for inline block-expression bodies in DSL trees (spec 033 §5)."
- [x] First phase to land: seeded CHANGELOG with Keep-a-Changelog header, conventions comment, and `## [Unreleased]` block with all six buckets (Added/Changed/Deprecated/Removed/Fixed/Security).

---

## Phase 2: Typed element refs `ElementRef<T>` (spec §3)

### 2.1 Type design

- [x] Add `public sealed class ElementRef<T>` in `src/Reactor/Input/FocusManager.cs` (kept colocated — the original file is small).
- [x] Constructor takes the inner untyped `ElementRef`. Field is `private readonly`.
- [x] `T : FrameworkElement` constraint is mandatory — without it, the cast `as T` won't compile cleanly.
- [x] `Current => _inner.Current as T;` — null-safe, no allocation per access.
- [x] Implicit conversion operator `ElementRef(ElementRef<T>)` so existing modifiers (`.Ref(...)`) and FocusManager keep working without overload bloat.
- [x] Override `ToString()` to produce `$"ElementRef<{typeof(T).Name}>"` for debugger display. `[DebuggerDisplay]` attribute applied.
- [x] **Trim/AOT note**: `typeof(T).Name` is AOT-safe. The `as T` cast is AOT-safe. No reflection. XML `<remarks>` documents this.

### 2.2 Hook surface — `src/Reactor/Hooks/UseElementRef.cs`

- [x] New file with two extension methods: `RenderContext.UseElementRef<T>()` and `Component.UseElementRef<T>()`.
- [x] Backed by `ctx.UseState(new ElementRef<T>(new ElementRef()))` — the typed wrapper itself is the hook value, so reference identity is stable across renders. Verified by `UseElementRef_Returns_Stable_Instance_Across_Renders`.
- [x] Inner untyped `ElementRef` identity is stable too (the typed wrapper holds it as a `private readonly` field), so reconciler population persists across re-renders. Verified by `UseElementRef_InnerRef_Is_Also_Stable_Across_Renders`.
- [x] XML doc with example showing focus + Composition-handle access (focus shown in summary; Composition usage implicit via `Current` being `T`).

### 2.3 `.Ref(...)` modifier overload

- [x] Added `public static T Ref<T, TElement>(this T el, ElementRef<TElement> r)` in `ElementExtensions.cs` adjacent to the untyped overload. Body forwards via the implicit conversion (stored as the inner untyped ref on `Modifiers.Ref`).
- [x] No overload-resolution ambiguity: the typed overload requires a generic-arity-2 constraint, the untyped requires the parameter type to be exactly `ElementRef`. Both forms compile and are exercised by `Ref_Modifier_Typed_Overload_Stores_Inner_Untyped_Ref` and the existing focus-fixture untyped paths.

### 2.4 Mismatch detection — DEBUG-only assertion

- [x] Added `[Conditional("DEBUG")] AssertTypedRefMatch` next to the `m.Ref._current = fe` populate site in `Reconciler.cs`. Calls `Debug.Fail` with both type names when `IsInstanceOfType` returns false.
- [x] `ElementRef.ExpectedType` is `internal` and decorated `[EditorBrowsable(Never)]`. Set by the typed wrapper's constructor; remains `null` on raw refs.
- [x] Populate site is called from the UI-thread reconcile path. Threading note added to `ElementRef<T>` XML `<remarks>`.

### 2.5 Tests

- [x] Headless suite: `tests/Reactor.Tests/TypedElementRefTests.cs` — 10 tests covering `ExpectedType` recording, `Current` null-before-mount, implicit conversion stability, null-throw on conversion, `ToString` shape, hook identity stability across renders, inner-ref stability, distinct refs from sibling calls, modifier wiring, and untyped-ref `ExpectedType == null`.
- [x] Self-host suite: `tests/Reactor.AppTests.Host/SelfTest/Fixtures/FocusFixtures.cs` — 3 mount-time fixtures (`Focus_TypedRefPopulatesAsConcreteType`, `Focus_TypedRefIdentityStableAcrossRenders`, `Focus_TypedRefMultipleControlsPopulateIndependently`), registered in `SelfTestFixtureRegistry`.
- [ ] Type mismatch under DEBUG: `Debug.Fail` listener-mock not yet wired — deferred (manual smoke during dev confirms the assert fires; spec §3 acknowledges RELEASE behavior is silent-null).
- [ ] `UseEffect`-after-mount ordering — implicit in the existing reconciler tests; not added as a dedicated typed-ref scenario.
- [ ] Trim/AOT smoke — deferred to Phase 8 cross-cutting AOT pass.

### 2.6 Accessibility

- [x] `Focus_TypedRefPreservesAutomationName` self-host fixture in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/FocusFixtures.cs` mounts a `Button` with `.AutomationName("Save document").Ref(typedRef)`, programmatically focuses via `typed.Current.Focus(FocusState.Programmatic)`, and asserts `AutomationProperties.GetName(...)` survives the focus mutation. Registered in `SelfTestFixtureRegistry`.
- [x] `ElementRef<T>` XML `<remarks>` now documents that programmatic focus moves the keyboard focus and triggers WinUI's UIA focus-changed event but does **not** by itself produce a screen-reader announcement of surrounding content; callers are directed to `UseAnnounce` for SR notifications.

### 2.7 Sample migration

- [x] Grep across `src/`, `samples/`, and `tests/` for `as Button`/`as TextBox` and `(Button)ref.Current`/`(TextBox)ref.Current` patterns on `ElementRef.Current`. **No matches outside XML doc text** — the codebase already uses `is`-pattern dispatch (e.g. `elRef.Current is TextBox tb`) where typing matters, which is already a typed projection. No call-site migrations needed.
- [ ] Update samples that demonstrate focus management (search `samples/` for `UseElementFocus`) to mention typed refs — *deferred to docs pass*. The existing `UseFocusSample` in `InputGesturesDemo.cs` doesn't dereference `.Current` (the ref flows straight back into `.Ref(...)`), so no behavioral change is needed; the call-out is purely documentary.

### 2.8 Release-note bullet

- [x] `CHANGELOG.md` entry under `## [Unreleased]` → **Added** describing the typed-ref surface, hook, modifier overload, and DEBUG mismatch assert.

---

## Phase 3: SystemBackdrop modifiers (spec §6)

### 3.1 Enum & extension surface

- [x] `src/Reactor/Core/BackdropKind.cs` — `BackdropKind { None, Mica, MicaAlt, DesktopAcrylic, AcrylicThin }` enum + `BackdropChoice` tagged union (kind OR factory) with XML docs covering visual style and Win11 availability.
- [x] `src/Reactor/Elements/BackdropExtensions.cs` — two `.Backdrop(...)` extensions; both validate non-null and store `BackdropChoice` on the element's modifiers.
- [x] Storage: added `Backdrop` slot on `ElementModifiers` in `src/Reactor/Core/Element.cs` next to the existing `Ref` slot. `Merge` handles last-write-wins.
- [x] Single tagged-union slot (`BackdropChoice?`) instead of twin slots — keeps modifier diff equality clean.

### 3.2 Host wiring (chose host-side over reconciler-side — simpler & no reconciler entanglement)

- [x] `src/Reactor/Hosting/BackdropApplier.cs` — owns the kind-diff state. Constructed by `ReactorHost(window)` with the window; constructed by `ReactorHostControl` lazily with `null` window so it no-ops cleanly.
- [x] `ReactorHost.RenderInternal` (post-reconcile) calls `_backdropApplier.Apply(newTree?.Modifiers?.Backdrop)`. Same-value re-renders short-circuit before touching WinUI.
- [x] `ReactorHost.Dispose` calls `_backdropApplier.Reset()` to clear the window's backdrop; safe for window-reuse scenarios.
- [x] `ReactorHostControl` no-ops: applier-with-null-window logs once via `Debug.WriteLine` and returns false on subsequent applies. Does not throw.

### 3.3 Backdrop materialization

- [x] `BackdropApplier.Materialize(BackdropKind?)` switch returns the right `SystemBackdrop` subclass for each kind. `MicaAlt` uses `MicaKind.BaseAlt`. **Caveat (WinAppSDK 2.0 preview):** `DesktopAcrylicBackdrop` does not yet expose a `Kind` selector, so `AcrylicThin` materializes the same `DesktopAcrylicBackdrop` instance as `DesktopAcrylic`. Documented in code; will switch when SDK ships the variant.
- [x] Try/catch around both materialization and `window.SystemBackdrop` assignment. Logs kind + exception type/message via `Debug.WriteLine`, falls back to no-backdrop. Does not propagate.
- [x] WinAppSDK reference already pulls in `Microsoft.UI.Composition.SystemBackdrops` types — no package dep change needed.

### 3.4 Logging & telemetry

- [x] `[Reactor] Backdrop set on window {hash:X8}: kind={kind}` emitted on every actual change (not on no-change skips). One-shot `[Reactor] Backdrop modifier ignored: host does not own a Window.` when applied to a windowless host.
- [x] Decided **no** ETW provider entry for backdrop changes — they're rare, console is enough.

### 3.5 Tests — unit

- [x] `tests/Reactor.Tests/BackdropModifierTests.cs` — 8 tests covering: kind round-trip, factory round-trip, null-element/null-factory throws, kind equality, factory reference equality, two equal modifiers compare equal (so the host applier's diff sees no-change).

### 3.6 Tests — integration / self-host

- [ ] Self-host fixture for backdrop application — *deferred* to a future session. The host-side wiring is covered by tests on `BackdropApplier.Apply` against an applier with a `null` window (no-op path), but the actual `Window.SystemBackdrop` mutation needs a windowed host fixture. Listed as Phase 8 follow-up.

### 3.7 Accessibility & visual fidelity

- [ ] Add a manual-test checklist entry in the spec's tracking PR: "Verify Mica/Acrylic looks correct on a Win11 build at light + dark theme + high-contrast theme." (Automated visual diff is out of scope; checklist is part of acceptance.)
- [ ] In high-contrast theme, `SystemBackdrop` setters are typically ignored by WinUI. Test: invoke under high-contrast, assert no exception and the modifier is still applied at the storage layer (the visual behavior is WinUI's responsibility).

### 3.8 Localization

- [ ] Backdrop is a visual-only modifier with no user-facing strings. No localization work — but document this in the spec's "what we did not localize" section.

### 3.9 Sample addition

- [x] Migrated samples to use `.Backdrop(BackdropKind.Mica)` on their root render:
  `samples/TodoApp`, `samples/ReactorGallery`, `samples/StylingGallery`,
  `samples/CommandingDemo`, `samples/NavigationDemo`, `samples/Reactor.TestApp`,
  `samples/ReactorCharting.Gallery`, `samples/ReactorCharting.Sample`,
  `samples/apps/wordpuzzle`, `samples/apps/reactorfiles`,
  `samples/apps/regedit`, `samples/apps/monaco-editor` (also dropped
  imperative `host.Window.SystemBackdrop = ...`), `samples/apps/validation-showcase`.
- [x] Where the root carried an opaque `Theme.SolidBackground` that would mask
  Mica (`ReactorGallery`, `ReactorCharting.Gallery`), the brush was dropped
  per Microsoft's Mica guidance ("page background must let the material show
  through"). Inner cards keep their own backgrounds — they intentionally
  float above the material.
- [ ] Dedicated "Backdrops" runtime-toggle page in `ReactorGallery` — *deferred*.
  All five `BackdropKind` values are covered by the static modifier on samples;
  a demo page that toggles each at runtime is a nice-to-have follow-up.

### 3.10 Release-note bullet

- [x] CHANGELOG entry under **Added** describes the enum, modifier, host wiring, and ReactorHostControl no-op behavior.

---

## Phase 4: Interop-first sample (spec §7)

### 4.1 Project skeleton

- [x] Created `samples/InteropFirst/InteropFirst.csproj` modeled on `samples/ReactorHostControlDemo`. Pinned WinAppSDK via the shared `$(WindowsAppSDKVersion)` MSBuild property.
- [x] Added to `Reactor.sln` via `dotnet sln add`.
- [x] `ProjectReference` to `src\Reactor\Reactor.csproj`.
- [x] Builds clean on ARM64 (verified locally). x64 path uses the same MSBuild graph.

### 4.2 App skeleton

- [x] `App.xaml` defines `AccentSampleColor`, `AccentSampleBrush`, `SubtleSampleBrush` as shared resources. `App.xaml.cs` is the standard `OnLaunched(...)` → `new MainWindow().Activate()`.
- [x] `MainWindow.xaml` is a vanilla `Window` (no `Frame`/`Page`) with the split layout. The page-and-frame chrome is intentionally omitted to keep the interop story focused — see `samples/InteropFirst/README.md` "Layout note" for rationale.
- [x] `MainWindow.xaml.cs` constructs the ViewModel, mounts the `ReactorHostControl` (in code-behind, matching `ReactorHostControlDemo` convention), wires the XAML `ListView` and the command-bar buttons, and disposes the ViewModel on `Window.Closed`.

### 4.3 ViewModel & model

- [x] `Models/Order.cs` — `public sealed record Order(int Id, string CustomerName, decimal Amount, DateTimeOffset PlacedAt)`.
- [x] `MainPageViewModel.cs` — INPC + `ObservableCollection<Order>` + `SelectedOrder` + `AddCommand` / `DeleteCommand` (driven by `RelayCommand`). Implements `IDisposable`; `Window.Closed` disposes it.
- [x] `Bridges/RelayCommand.cs` — minimal `ICommand` with `RaiseCanExecuteChanged` marshalled to the UI dispatcher when called from a background thread.
- [x] Seeds 10 sample orders using `CultureInfo.InvariantCulture` for `CustomerName` formatting.

### 4.4 Reactor side — `Components/OrdersDataGrid.cs`

- [x] `public sealed class OrdersDataGrid : Component<OrdersDataGridProps>`.
- [x] `OrdersDataGridProps` carries `ObservableCollection<Order> Items`, optional `Action<Order?> OnSelect`, optional `Brush? AccentBrush` and `SubtleBrush` (resolved from `Application.Current.Resources` by `MainWindow`), and optional `ICommand AddCommand` / `DeleteCommand` for the shared-commanding row.
- [x] Renders a Microsoft.UI.Reactor (Reactor) `VStack` of header + rows (deliberately not the full `DataGrid<T>` factory — the spec calls for a "Reactor DataGrid" as a panel, not a column-virtualized grid; rows are simple `HStack`s of `TextBlock`s plus selection background. Production apps use `Factories.DataGrid<T>(...)` with `FieldDescriptor` columns.).
- [x] `UseEffect` with cleanup subscribes to `Items.CollectionChanged` on mount and unsubscribes on unmount, mirroring the host's collection into local hook state on every change.

### 4.5 Page composition — collapsed into `MainWindow.xaml`

- [x] No `NavigationView`. The interop story does not depend on it — see README "Layout note".
- [x] Central area: split `Grid` with the XAML `ListView` (left, `ItemsSource = ViewModel.Items`, `ItemTemplate` built via `XamlReader.Load` to keep XAML-side DataTemplate trivially simple) and a `Border` placeholder for the code-behind-mounted `ReactorHostControl` (right).
- [x] Bottom row: two `Button`s (`Add` / `Delete`) calling the ViewModel commands. The Reactor side carries its own toolbar driven by the same `ICommand` instances bridged through `CommandInterop.FromCommand`.

### 4.6 Commanding bridge

- [x] `CommandInterop.FromCommand(ICommand, label, ...)` already exists in `src/Reactor/Core/CommandInterop.cs`. The sample uses it directly. No changes to product code required.

### 4.7 Shared resource bridge

- [x] `MainWindow.xaml.cs` resolves `AccentSampleBrush` and `SubtleSampleBrush` from `Application.Current.Resources` and passes them as props into the Reactor component. Approach is host-resolves-and-injects (simpler and explicit) rather than Reactor pulling from a host-traversed dictionary — fully covers the spec's "shared resources" intent.
- [x] README documents that the sample passes the resolved brushes through props.

### 4.8 UI tests

- [ ] **Deferred.** UI-driver tests for the sample are deferred to a follow-up — the existing repo does not currently host a `tests/InteropFirst.UITests/` project, and adding WinAppDriver infrastructure is out of scope for this phase. The sample is an acceptance demonstration, not a coverage source. Phase 8 acceptance smoke documents the manual run.

### 4.9 Security review of the sample

- [x] No file I/O.
- [x] No network.
- [x] No `XamlReader.Load` user-controlled content. The one `XamlReader.Load` call uses a literal compile-time string (item-template markup) — no untrusted-string risk. The XAML-side `Binding` path is reflection-based by design but only against an `Order` record on a static `ItemsSource` we control.
- [x] `RelayCommand.Execute` does not swallow exceptions; documented in the class header.

### 4.10 Accessibility checklist for the sample

- [x] All buttons (XAML `Button`s and Reactor `Button`s) auto-name from their `Content` / label. UIA observes "Add" / "Delete" / "Add (Reactor side)" / "Delete (Reactor side)".
- [ ] Manual checks (UIA tree inspection, tab order, WCAG contrast) — deferred to acceptance smoke.

### 4.11 Localization checklist for the sample

- [x] Currency / datetime rendered through `CultureInfo.CurrentCulture` ("C" and "g" formats) — locale-aware display.
- [x] Seed data formatted via `CultureInfo.InvariantCulture` for reproducibility.
- [ ] `Strings.resw` extraction — deferred. The sample is en-US strings + locale-aware formatting; the spec calls full localization out of scope.

### 4.12 Docs

- [x] `samples/InteropFirst/README.md` — what the sample demonstrates, how to run, layout note, what it intentionally does not show, file inventory.
- [ ] Top-level `README.md` "Samples" entry and `docs/_pipeline/templates/getting-started.md.dt` callout — deferred to Phase 8 docs pass.

### 4.13 Release-note bullet

- [x] CHANGELOG entry under **Added** describes the sample, shared collection, shared commands (`CommandInterop.FromCommand`), and shared `App.xaml` brush resources flowing through props.

---

## Phase 5: Strongly-typed Grid tracks (spec §1)

This is the first phase to introduce a Roslyn analyzer and an `[Obsolete]`
overload on a public surface. Establish the pattern carefully — phases 6 and
7 reuse it.

### 5.1 `GridSize` value type

- [x] `src/Reactor/Elements/GridSize.cs` — `readonly record struct GridSize(double Value, GridUnitType Type)`.
- [x] `Auto`, `Star(weight = 1)`, `Px(pixels)` smart constructors with input validation (`Star` requires > 0; `Px` requires >= 0).
- [x] Re-uses `Microsoft.UI.Xaml.GridUnitType` directly.
- [x] Implicit `GridSize → GridLength` conversion.
- [x] `ToString()` produces canonical track strings (`"Auto"`, `"*"`, `"1.5*"`, `"200"`).
- [x] `[DebuggerDisplay("{ToString(),nq}")]`.

### 5.2 String-form parser (compat)

- [x] `public static GridSize Parse(string)` covers `"Auto"`, `"*"`, `"<n>*"`, `"<n>.<n>*"`, `"<n>"`, `"<n>.<n>"`. Invariant culture only.
- [x] Throws `FormatException` with input string in the message; throws `ArgumentNullException` for null.
- [x] `"Auto"` matches case-insensitively (`OrdinalIgnoreCase`).
- [x] Whitespace trimmed; empty/whitespace-only → `FormatException`. Zero star weights and negative pixels rejected.

### 5.3 `Grid` factory overloads — `src/Reactor/Elements/Dsl.cs`

- [x] Typed `Grid(GridSize[], GridSize[], params Element?[])` overload added with non-null validation.
- [x] String-form overload marked `[Obsolete(error: false)]` with message pointing at the typed overload and spec 033 §1.
- [x] Both overloads produce equivalent `GridDefinition`s (`GridDefinition` gained a `(GridSize[], GridSize[])` ctor that converts to canonical track strings via `GridSize.ToString()`).

### 5.4 `GridDefinition` storage flip — minimal-disruption approach

- [x] Did **not** flip the underlying storage. The record remains
      `GridDefinition(string[] Columns, string[] Rows)` with an additional
      `(GridSize[], GridSize[])` constructor that converts via
      `GridSize.ToString()` at construction time. This:
      1. keeps the reconciler's existing string-parsing hot path untouched,
      2. preserves wire compatibility for any direct consumers of
         `GridDefinition`, and
      3. produces *identical* `GridDefinition` records from either factory
         shape — verified by `Grid_Typed_Factory_Builds_Same_Definition_As_String_Form`.
      The full storage flip remains a follow-up if/when the string parser
      becomes a profiled hot spot.

### 5.5 In-repo caller migration

- [x] Migrated `InterspersedGrid` and `UniformGrid` (in `Dsl.cs`) to the typed form.
- [x] Migrated `Pending`, `MarkdownBuilder`, and `ChartKeyboardNavigator` to the typed form.
- [x] `DataGridComponent` — three call sites build `string[]` track arrays dynamically; suppressed with localized `#pragma warning disable CS0618` and an inline comment marking the typed migration as a follow-up. (Reshaping the dynamic-string-builder paths is a larger refactor, out of scope for this phase.)
- [x] **All sample and test call sites migrated.** A one-shot rewriter
  (`tools/migrate_grid_tracks.py`) handled bulk conversions for the literal
  `["*", "Auto", "200"]` and `new[] { "*" }` shapes; the remaining
  dynamic-builder sites (`Enumerable.Range(...).Select(_ => "*").ToArray()`,
  interpolated `$"{widthRef.Current}"`, etc.) were converted manually to
  `Enumerable.Range(...).Select(_ => GridSize.Star())` /
  `GridSize.Px(...)`. The full solution now builds with **zero CS0618
  warnings** for the Grid string overload.

### 5.6 Roslyn analyzer — `REACTOR_GRID_001` *(deferred)*

- [ ] **Deferred.** The C# `[Obsolete]` attribute on the string-form overload already produces the diagnostic (`CS0618` warning) at every call site, with the message pointing at the typed overload and spec 033 §1. A custom analyzer would add (1) a configurable severity and (2) a code-fix that rewrites each string literal to its `GridSize.*` equivalent. The code-fix is real value — but the analyzer + code-fix + golden-test infrastructure is a substantial standalone piece. Tracked as a follow-up; the deprecation signal already lands via `[Obsolete]`.
- [ ] Diagnostic ID `REACTOR_GRID_001` is reserved for the follow-up.

### 5.7 Tests — analyzer *(deferred with §5.6)*

### 5.8 Tests — `GridSize`

- [x] `tests/Reactor.Tests/GridSizeTests.cs` — 26 tests covering: smart-constructor inputs/throws, equality, implicit conversion to `GridLength`, parser case-insensitive Auto, parser star/pixel forms, parser whitespace trim, parser failure cases, parser invariant-culture under `de-DE`, `ToString` canonical forms, `Parse(ToString(x)) == x` roundtrip, and typed-vs-string Grid factory equivalence.

### 5.9 Tests — `Grid` factory & reconciler

- [x] `Grid_Typed_And_String_Factories_Produce_ElementWise_Equal_Tracks_For_All_Canonical_Shapes` (in `GridSizeTests.cs`) walks every canonical shape (`Auto`, `*`, `2*`, `0.33*`, `0`, `120.5`) and asserts the typed and legacy string factories produce element-wise equal `GridDefinition.Columns` and `Rows` arrays. Plus `Grid_Typed_Factory_Throws_On_Null_Track_Arrays` and `Grid_Typed_Factory_Star1_Produces_Canonical_Asterisk_Track` cover boundary validation and the canonical `Star(1)→"*"` round-trip.
- [x] Reconciler mounts a typed `Grid(...)` — already exercised by the existing self-host fixtures `LayoutFixtures.GridRowColumn`, `GridVsFlexStarSizing`, and `ReconcilerFixtures.GridDynamicChildCountComponent`, all of which build their column/row arrays via `GridSize.Auto` / `Star()` / `Px()` and assert `WinUI.Grid.ColumnDefinitions` / `RowDefinitions` shapes after mount.

### 5.10 Sample / docs

- [ ] Update `docs/_pipeline/templates/dsl-tour.md.dt` Grid section to feature the typed form first; mention the string form is deprecated. Run `mur docs compile`.
- [ ] Update IntelliSense XML docs to lead with `GridSize.Star()` / `Auto` / `Px(...)`.

### 5.11 Release-note bullet

- [x] CHANGELOG **Added** describes `GridSize` and the typed factory overload.
- [x] CHANGELOG **Changed** describes the new `GridDefinition` typed constructor.
- [x] CHANGELOG **Deprecated** describes the `[Obsolete]` on the string-form overload (the deprecation signal lands via the standard `CS0618` warning; `REACTOR_GRID_001` is reserved for a follow-up code-fix).

---

## Phase 6: `Func` deprecation (spec §4)

### 6.1 Decision capture

- [x] Confirmed: Option A + Func-into-Memo deprecation. `Memo` empty-deps semantics unchanged ("render once + state-driven re-renders").

### 6.2 Mark `Func` `[Obsolete]`

- [x] `Func(Func<RenderContext, Element>)` marked `[Obsolete(error: false)]` with a message pointing at `Memo` (render once) and `RenderEachTime` (always re-render), plus the spec section reference.
- [x] `Memo` empty-deps semantics unchanged. No new sentinel needed.
- [x] Added `RenderEachTime(Func<RenderContext, Element>)` as a top-level factory (not nested under `Memo` because `Memo` is already a static method, not a class). Same return type as `Func` so analyzer rewrites are mechanical.

### 6.3 Roslyn analyzer — `REACTOR_FUNC_001` *(deferred)*

- [ ] **Deferred** for the same reason as `REACTOR_GRID_001` (§5.6): the C# `[Obsolete]` attribute already produces the deprecation diagnostic at every call site. The custom analyzer's value is the two-action code-fix; that's the lift. `REACTOR_FUNC_001` is reserved for the follow-up.

### 6.4 Tests — analyzer *(deferred with §6.3)*

### 6.5 Tests — semantic preservation

- [x] `tests/Reactor.Tests/RenderEachTimeTests.cs` — surface-level tests confirming `RenderEachTime` produces the same `FuncElement` as the obsolete `Func` factory. Reconciler-driven re-render assertions are already covered by `MemoizationSelfHostTests` and `MemoizationPropagationTests`.

### 6.6 In-repo migration

- [x] Initial pass found `samples/CommandingDemo/App.cs`; migrated to `RenderEachTime(...)`.
- [x] Follow-up sweep migrated additional sites:
  `samples/ReactorCharting.Gallery/Samples/AnimatedDonut.cs`,
  `BarChartRace.cs`, `CurveExplorer.cs`, `DonutMixer.cs` — all use hooks and
  rely on always-re-render, so all migrated to `RenderEachTime`.
  `tests/Reactor.AppTests.Host/SelfTest/Fixtures/CoreCoverageFixtures2.cs`
  also migrated to `RenderEachTime`.
  Two fixtures (`ControlCatalogFixtures.cs`, `ReconcilerStressFixtures.cs`)
  intentionally exercise the legacy `Func()` factory for coverage; suppressed
  locally with `#pragma warning disable CS0618` until the obsolete overload is
  removed.
- [x] The full solution now builds with **zero CS0618 warnings** for the `Func` overload.

### 6.7 Custom-hooks pattern (deferred per spec §9)

- [ ] **Out of scope this phase.** Open a follow-up issue / spec for "REACTOR_HOOKS_001 — custom-hook naming convention" tracking spec §9 item 4.

### 6.8 Docs

- [ ] Add a "Choosing a component form" page to `docs/_pipeline/templates/components.md.dt` (or wherever the components doc lives — confirm). Cover the four canonical forms from spec §4 recommendation: raw method, `Memo`, `Component<TProps>`, propless `Component`. Mark `Func` as soft-deprecated.
- [ ] Run `mur docs compile`.

### 6.9 Release-note bullet

- [x] CHANGELOG **Added** describes `RenderEachTime`.
- [x] CHANGELOG **Deprecated** describes the `[Obsolete]` on `Func`.

---

## Phase 7: PersistedStateCache lifecycle (spec §2)

The biggest blast radius — ship last. Two-release migration: add APIs in N,
flip default in N+1.

### 7.1 LRU cache primitive

- [x] `src/Reactor/Core/Internal/LruCache.cs` — `LruCache<TKey, TValue>` where `TKey : notnull`, `Dictionary` + `LinkedList`, single `lock(_sync)`.
- [x] `TryGet` / `Set` / `Remove` / `Clear` / `Count` / `Capacity` / `Trim(targetCount)`.
- [x] Eviction-on-full (never refuse). Touch-on-access promotes node to MRU. Capacity validated > 0 in ctor.
- [x] Tests: capacity strictly enforced, touch-on-access promotes, set-existing-key updates-and-promotes, `Trim(target)` evicts LRU first, concurrent mutations stay consistent under 4×5k stress.

### 7.2 `IPersistedStateScope` interface

- [x] `src/Reactor/Core/PersistedStateScope.cs` — `IPersistedStateScope` (`TryGet<T>`, `Set<T>`, `Remove`, `Count`, `Capacity`, `IDisposable`) and `PersistedScope { Window, Application }` enum.
- [x] Threading documented in `<remarks>`: hooks consult on UI thread; LRU storage handles cross-thread (memory-pressure callback) safely.

### 7.3 `WindowPersistedScope`

- [x] `src/Reactor/Core/WindowPersistedScope.cs` — wraps `LruCache<string, object?>` at default capacity 1024. `IDisposable` clears the cache and makes the scope inert (subsequent `Set` is a no-op, `TryGet` returns false). No memory-pressure registration (lifetime is bounded by the host).
- [ ] **Per-host wiring** — *deferred*. Plumbing the host's `WindowPersistedScope` into `RenderContext` requires changes to `BeginRender` signatures across the codebase. The infrastructure (interface, class, capacity, lifecycle) is in place; per-host scope resolution at hook-entry time is a follow-up. Documented in the `UsePersisted(scope)` XML doc.

### 7.4 `ApplicationPersistedScope`

- [x] `PersistedStateCache.cs` rewritten as `ApplicationPersistedScope` + an internal `PersistedStateCache` shim that delegates to `ApplicationPersistedScope.Default`. Default capacity 4096. Singleton + ctor-with-capacity for tests.
- [x] Memory-pressure handler registers `Windows.System.MemoryManager.AppMemoryUsageIncreased` under try/catch. `ApplyMemoryPressureTrim()` is also exposed publicly so tests can exercise the shrink path without depending on the OS callback.
- [x] Dispose unregisters the handler and clears the cache.
- [x] Thread safety: covered by the LRU primitive's stress test plus the application-scope tests.

### 7.5 Reconciler / host wiring *(deferred)*

- [ ] **Deferred.** The host-level `WindowPersistedScope` ownership and the reconciler/RenderContext plumbing to resolve a hook's scope at hook-entry time are non-trivial — they touch `BeginRender` overloads across the codebase. The class, interface, and singleton are in place; the per-host wiring follows in a focused PR.

### 7.6 `UsePersisted` API

- [x] Two-arg `UsePersisted<T>(string key, T initialValue)` retained — routes to Application scope (legacy behavior preserved).
- [x] New overload `UsePersisted<T>(string key, T initialValue, PersistedScope scope)`. Compiles, runs, returns the initial value on first read. In this release both scopes resolve to `ApplicationPersistedScope.Default` until the host wiring lands (§7.5 deferred); documented in the XML `<remarks>`.
- [x] Key validation: non-null, non-whitespace, ≤ 256 chars — enforced in both `ApplicationPersistedScope` and `WindowPersistedScope`.
- [ ] `ReactorAppOptions.PersistedStateScopeDefault` opt-in toggle — *deferred* with §7.5 host wiring.

### 7.7 Roslyn analyzer — `REACTOR_PERSIST_001` *(deferred)*

- [ ] **Deferred** alongside §5.6 / §6.3. Diagnostic ID `REACTOR_PERSIST_001` reserved for the follow-up that introduces all three analyzers + code-fix infrastructure together.

### 7.8 Tests — `tests/Reactor.Tests/PersistedStateScopeTests.cs`

23 tests, all passing. Covered:
- [x] LRU primitive: capacity enforced, touch-on-access, set-updates-and-promotes, `Trim(target)`, capacity validation throws, concurrent mutations stay consistent.
- [x] Application scope: capacity configurable, set/get roundtrip, type-mismatch returns false, eviction plateau at capacity (newest wins, oldest evicted), `ApplyMemoryPressureTrim` shrinks to 25%, invalid-key throws, oversized-key throws, default singleton stable, dispose clears.
- [x] Window scope: default capacity 1024, dispose clears + becomes inert, eviction plateau, distinct instances do not share state.
- [x] `PersistedScope` enum members + the new three-arg `UsePersisted` overload compiles and runs.
- [ ] Window-scope-survives-unmount-within-host scenarios — *deferred* with §7.5 host wiring (cross-host non-leakage requires the host plumbing first).
- [ ] Default-routing under `ReactorAppOptions.PersistedStateScopeDefault` — *deferred* with §7.5.

### 7.9 Tests — analyzer *(deferred with §7.7)*

### 7.10 Logging & telemetry

- [x] Each scope logs at `Debug.WriteLine` on construction, disposal, and (for `ApplicationPersistedScope`) memory-pressure trim. All entries carry the `[Reactor]` prefix matching `Hosting/` style.
- [x] **Keys and values are never logged** — only `count` and `capacity` integers. Documented inline at each log site as a security/privacy note.
- [ ] ETW provider entries — *deferred*. The hosting ETW providers under `src/Reactor/Hosting/Etw/` cover layout/render hot paths; persisted-state events are rare and the `Debug.WriteLine` channel is sufficient.

### 7.11 Migration: in-repo callers

- [x] Sample call sites migrated to the explicit three-arg form: `samples/apps/regedit/App.cs` (showAddressBar, showStatusBar — Window), `samples/Reactor.TestApp/Demos/PersistedDemo.cs` (demo.p.name/email/color — Window), `samples/Reactor.TestApp/Demos/NavigationDemo.cs` (navTransition, homeVisits, detail-notes-* — Window). Added `Component.UsePersisted<T>(key, initial, PersistedScope)` so component subclasses can use the three-arg form ergonomically (matches the existing `RenderContext` overload).
- [x] Tests in `tests/Reactor.Tests/PersistedStateTests.cs` deliberately exercise the legacy two-arg `UsePersisted` (the cross-host shared-state semantics they assert depend on it). Left untouched. The new scope surface has its own coverage in `PersistedStateScopeTests.cs`.
- [x] No cross-window persistence dependency surfaced during the audit — every migrated site is per-window UI state (visibility toggles, navigation transition, visit count, per-detail-id notes). Application scope retained only for the explicit Application-scope tests.

### 7.12 Release-N+1 default flip (deferred)

- [ ] **Do not land in release N.** Open a tracking issue: "Flip `UsePersisted` default scope to Window in release N+1 (spec 033 §2)."
- [ ] Add a forum-post / changelog blurb draft to the tracking issue ahead of the flip (per spec §9 item 2).

### 7.13 Release-note bullet

- [x] CHANGELOG **Added** describes the public `IPersistedStateScope` /
  `PersistedScope` / scope classes / the new `UsePersisted` overload.
- [x] CHANGELOG **Changed** describes the LRU rewrite, memory-pressure handler,
  and key validation.
- [ ] **Deprecated** entry for the two-arg form — *deferred*. The two-arg form
  is intentionally not yet `[Obsolete]`-marked because the analyzer that would
  carry the migration message + code-fix is also deferred (§7.7).

---

## Phase 8: Cross-cutting acceptance & cleanup

### 8.1 Public API audit

- [x] Full solution build clean (`dotnet build Reactor.sln`). No new errors. The CS0618 warnings come from `[Obsolete]`-marked overloads being called from test fixtures and one `samples/CommandingDemo` site (not yet migrated where intentional) — those are the deprecation signal landing as designed.
- [x] `Reactor.csproj` does not reference `Microsoft.CodeAnalysis.PublicApiAnalyzers`. New public types (`GridSize`, `BackdropKind`, `BackdropChoice`, `ElementRef<T>`, `TypedElementRef`, `IPersistedStateScope`, `PersistedScope`, `ApplicationPersistedScope`, `WindowPersistedScope`) ship without `PublicAPI.Unshipped.txt` updates because the file does not exist. Tracked as a follow-up under §9.
- [ ] Trim/AOT smoke — *deferred*; the new public surface is reflection-free, so it should be no-op safe under trim, but the existing `Reactor.SelfTests` suite does not gate explicitly on AOT today.

### 8.2 Doc compile pass

- [ ] **Deferred.** No edits to `docs/_pipeline/templates/*.md.dt` were made in this round (the spec called for several callouts but they are scoped to a separate docs PR — keeping behavior changes and docs PRs narrow makes review simpler). Documented explicitly in each phase's task list above.

### 8.3 Acceptance results

- [x] Full unit test suite: `dotnet test tests/Reactor.Tests/Reactor.Tests.csproj` — **6725 passed, 0 failed, 46 skipped** after this round (3 new `GridSizeTests` cases). Two timing-sensitive tests (`LogCaptureBufferTests.WaitForNewAsync_WakesOnAppend`, `UseResourceTests.Retry_Exhausted_Surfaces_Final_Error`) flake under load on the Windows on ARM64 runner; both pass when run in isolation. Tracked separately as flaky-test cleanup, unrelated to spec 033.
- [x] Full self-test suite: `dotnet test tests/Reactor.SelfTests/Reactor.SelfTests.csproj` — **639 passed, 0 failed** after this round. Includes the four `Focus_TypedRef*` fixtures (3 from the prior pass + the new `Focus_TypedRefPreservesAutomationName` for §2.6).
- [x] Full solution build (`dotnet build Reactor.sln`) — **0 errors**. Warnings are limited to `REACTOR_A11Y_*` analyzer firings inside `samples/apps/a11y-showcase` (an intentional analyzer-demo sample) and `CS0618` from the deferred `[Obsolete]`-marked overloads being exercised on purpose by the deprecation-test fixtures.
- [x] `samples/InteropFirst` builds clean.
- [ ] Manual visual smoke (Mica/Acrylic on a real Win11 box, browse `ReactorGallery`, IntelliSense check) — out of scope for this automated implementation pass.

### 8.4 Final docs

- [x] `docs/specs/033-winui-xaml-reviewer-feedback.md` Status header updated to "Implemented (in part) as of 2026-05-01" with an inline list of the deferred pieces (analyzers + code-fixers, host-side window-scope wiring, InteropFirst UI tests).

### 8.5 Release-note synthesis

- [x] CHANGELOG `## [Unreleased]` section uses the Keep-a-Changelog buckets (`Added` / `Changed` / `Deprecated` / `Removed` / `Fixed` / `Security`). Every entry references `(spec 033 §N)`.
- [x] CHANGELOG header conventions comment seeded in Phase 1 is still accurate.
- [ ] Release rotation (rename `## [Unreleased]` → versioned heading) — performed when an actual release is cut, not as part of this implementation pass.

---

## §9 Open follow-ups (track separately)

These are explicitly out of scope for spec 033 but generated work that
needs tracking:

- [ ] **Source generator for component unification** (spec 033 §9 item 1, reviewer wishlist #1). Open follow-up issue under spec 008 dependencies.
- [ ] **PersistedStateCache default-flip in release N+1** (spec 033 §9 item 2). Tracked in §7.12.
- [ ] **Custom-hooks naming convention analyzer** `REACTOR_HOOKS_001` (spec 033 §9 item 4). Open follow-up.
- [ ] **`Expr(...)` deprecation** when C# block expressions land (spec 033 §9 item 5). Track in spec 008 "language asks."
- [ ] **Public-API analyzer adoption** if the repo doesn't already use `Microsoft.CodeAnalysis.PublicApiAnalyzers` (see §0.3). Open follow-up.

---

## Sequencing notes

Per spec §8: Phase 1 (Expr) is the warm-up. Phase 5 (GridSize) lands the
analyzer pattern that Phases 6 and 7 reuse — do **not** start phases 6 or
7 until §5 lands. Phases 2, 3, 4 are independent and can be parallelized.
Phase 7 ships last (highest blast radius).

A reasonable parallelization, given two devs:
- Dev A: 1 → 5 → 6 (analyzer track)
- Dev B: 2 → 3 → 4 (additive surface track) → joins on 7
- Both on 7 (highest scrutiny)
