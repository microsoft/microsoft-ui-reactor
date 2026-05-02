# Reactor Framework — Critical Review

A skeptic's component-by-component analysis of Reactor against React, SwiftUI,
and Jetpack Compose. This document is a *current-state* assessment — not a
running diary. It records where the framework actually is today (2026-05-02),
what it gets right, and what still keeps it short of production-ready.

Reviewer perspective: deep familiarity with SwiftUI and Jetpack Compose,
working knowledge of React and React Native. The benchmark throughout is the
mature declarative UI frameworks, not WinUI XAML.

---

## Executive Summary

Reactor is an ambitious React-style declarative UI framework over WinUI 3
that has matured well past "impressive demo." It now ships a coherent
component model, a navigation system, a commanding surface, an async data
subsystem, an accessible charting sub-framework, an MCP-based devtools
server, and a full declarative input layer. Spec 033 just landed seven
reviewer-driven API improvements (typed `GridSize`, declarative `.Backdrop`,
typed `ElementRef<T>`, `RenderEachTime`, `Expr`, scoped persisted state,
WinUI-primary interop sample). The unit-test count is 6,723; selftest
coverage of the reconciler reached 74.5% on its own.

**The honest assessment: Reactor is more capable than any other declarative
C# UI framework, has five features where it is genuinely ahead of the
broader competition (commanding, lightweight styling, runtime WCAG
diagnostics + chart a11y, MCP devtools, ErrorBoundary), and is still
roughly one production-app cycle away from "ship to real users." The work
that remains is integration, not invention.**

The five biggest concerns:

1. **The flagship apps still don't use most of the framework's own
   features.** ReactorFiles, regedit, wordpuzzle, and the Microsoft
   internal samples (HeadTrax, NetPulse) adopted the spec 033 typed-Grid
   and `.Backdrop()` migrations but still avoid `UseNavigation`,
   `Context`, `StandardCommand`, `SemanticPanel`, `UseFocusTrap`, and
   the spec 027 input modifiers. Each new system ships with a
   purpose-built demo (NavigationDemo, CommandingDemo, a11y-showcase,
   ReactorGallery) that proves the system works in isolation; the
   complex showcase apps still don't prove they compose. The David
   Fowler chat sample (#95) is the rare external-author signal but it
   too uses only `GridSize` from the recent additions.

2. **Three known correctness gaps are unfixed across multiple review
   cycles.** `UseColorScheme()` reads `Application.Current.RequestedTheme`
   despite an inline doc comment claiming it reads element-effective
   theme; this defeats the headline composition story for `.RequestedTheme()`.
   `UseFocusTrap` still cancels Tab-out but does not cycle to the first
   focusable element (the source code even has a `// Optionally cycle:`
   TODO at the cancel site). `StandardCommand.Cut/Copy/Paste/...` labels
   are still hard-coded English in a framework that ships a full ICU
   localization system. Each is a small fix; none has been done.

3. **Performance refactors are landing without before/after numbers.**
   The spec 027 trampoline-dispatch rewrite, the new in-place updates
   for 14 controls, the lazy event wiring and skip-path correctness fix
   — all are architecturally correct, all have ETW telemetry, none have
   a published render-cycle delta. The new layout-cost overlay (spec
   032) is the right tool to produce those numbers but no one has run
   the experiment yet.

4. **String-typed APIs accumulate at the boundaries.** Routes are typed
   records, but deep-link patterns are strings. `SemanticPanel` roles
   are strings. Lightweight-styling resource keys are strings. Connected
   animation keys are strings. Drag-and-drop typed-format-ids derive from
   `typeof(T).FullName` with no versioning. Each individual instance is
   defensible; cumulatively they leak the type-safety guarantee at every
   integration seam.

5. **Wrapper-element accumulation is now structural.** A component
   inside a navigation host inside a command host inside a semantic
   wrapper produces `Border > Grid > Grid > SemanticPanel > content` —
   four framework wrappers before user content. Each is justified
   individually; the cumulative measure/arrange tax is unmeasured.

The framework is genuinely ahead of the field on commanding, chart
accessibility, MCP devtools, lightweight styling, and ErrorBoundary. It
is competitive on navigation, async data, and the component model. It
trails on animation breadth (compositor-property ceiling), DSL
ergonomics (C# can't match JSX or Swift result builders), and the
unevenness that comes from shipping faster than you integrate. The
trajectory is right; the integration debt is the risk.

---

## Table of Contents

1. [The DSL: C# as a UI Language](#1-the-dsl-c-as-a-ui-language)
2. [Component Model and State](#2-component-model-and-state)
3. [Async Resources](#3-async-resources)
4. [The Reconciler](#4-the-reconciler)
5. [Layout System](#5-layout-system)
6. [Styling and Theming](#6-styling-and-theming)
7. [Navigation](#7-navigation)
8. [Commanding](#8-commanding)
9. [Lists and Collections](#9-lists-and-collections)
10. [Animation](#10-animation)
11. [Accessibility](#11-accessibility)
12. [Charting](#12-charting)
13. [Input and Events](#13-input-and-events)
14. [Developer Experience and Devtools](#14-developer-experience-and-devtools)
15. [The .Set() Escape Hatch](#15-the-set-escape-hatch)
16. [Scorecard](#16-scorecard)
17. [Conclusion](#17-conclusion)

---

## 1. The DSL: C# as a UI Language

React has JSX. SwiftUI has result builders. Compose has Kotlin's trailing
lambdas plus a compiler plugin. Reactor has C# method calls with `params`
arrays. The quality of the UI DSL determines whether writing UI feels
natural or feels like fighting the language, and on this axis Reactor
loses to all three competitors — with one recent mitigation.

```csharp
VStack(
    Text("Hello").Bold().FontSize(24),
    Button("Click me", () => setCount(count + 1))
        .Background("#0078D4")
        .Padding(12, 8),
    count > 5 ? Text("Wow!") : null)
```

```jsx
// JSX
<VStack>
  <Text bold fontSize={24}>Hello</Text>
  <Button onClick={() => setCount(c => c + 1)}
          style={{ background: '#0078D4' }}>Click me</Button>
  {count > 5 && <Text>Wow!</Text>}
</VStack>
```

```swift
// SwiftUI
VStack {
    Text("Hello").bold().font(.title)
    Button("Click me") { count += 1 }.background(.blue)
    if count > 5 { Text("Wow!") }
}
```

### Specific weaknesses

1. **No block syntax for children.** Children are positional `params`
   arguments mixed into the constructor call. Deep nesting becomes
   bracket-counting. SwiftUI/Compose use trailing closures that
   visually separate the container from its content; Reactor cannot.

2. **Modifier chains allocate on every render.** Every `.Margin(10)`
   produces a new `ElementModifiers` record and merges it. SwiftUI
   inlines these via opaque return types; Compose composes a single
   `Modifier` value. Reactor's per-call allocation is measurable in
   hot render paths.

3. **String-typed Grid tracks — finally typed (spec 033 §1).** The
   long-standing `Grid(["*", "Auto", "200"], ...)` API is now superseded
   by `Grid([GridSize.Star(), GridSize.Auto, GridSize.Px(200)], ...)`.
   The string-form overload is `[Obsolete]` and every in-repo call site
   was migrated. `GridSize` is a tagged value type with `Auto`, `Star(n)`,
   and `Px(n)`. The string parser is preserved (invariant culture). This
   is exactly the right fix and a model for future stringly-typed
   surfaces.

4. **`Func()` is now soft-deprecated, replaced by `Memo()` /
   `RenderEachTime()` (spec 033 §4).** The previous `Func(ctx => ...)`
   factory had no memoization escape hatch, so every call site re-rendered
   on every parent render. The framework now nudges developers toward
   `Memo(ctx => ..., deps)` (the common case) and `RenderEachTime(...)`
   (explicit always-rerender). `Func` still exists but is `[Obsolete]`.

5. **`Expr(() => ...)` adds inline block expressions (spec 033 §5).**
   A genuine ergonomic improvement: `VStack(Text("hi"), Expr(() => {
   var x = compute(); return Text(x); }))` allows local-variable scope
   inside a child slot without forward-declaration. This is closer to
   Kotlin's expression-statement unification than to Swift's view
   builders, but it's a real reduction in the verbosity penalty.

6. **Implicit string-to-Element conversion is still a footgun.** `Element`
   has `implicit operator Element(string text)` — convenient for
   `VStack("Hello", "World")`, dangerous for any string accidentally
   passed where an Element is expected.

7. **Null-based conditional rendering is fragile.** `condition ?
   Text("yes") : null` requires `FilterChildren` to strip nulls each
   render. SwiftUI/Compose handle `if` natively in their builders;
   React handles `{cond && <C/>}` in JSX. Reactor needs runtime
   filtering.

The DSL grade is fundamentally limited by C# itself. Spec 033 §1, §4,
§5 each tightened it where C# allows. The remaining ceiling is what
the language gives you.

---

## 2. Component Model and State

The component model is the part of Reactor that has matured most cleanly.
The mechanics — context, memoization, generic hooks, persisted state,
post-render effect cleanup — are now correct and competitive.

### What Reactor has

- **Class components** extending `Component<TProps>` with structural
  prop equality from C# records
- **Function components** via `Memo(ctx => ..., deps)` (memoizing) and
  `RenderEachTime(...)` (always-rerender). `Func()` is soft-deprecated
- **Error boundaries** via `ErrorBoundary(child, fallback)`
- **Tree-scoped Context\<T\>** with `.Provide(ctx, value)` modifier and
  `UseContext<T>` hook. The framework's own `LocaleProvider` migrated
  from a thread-static hack to `Context<IntlAccessor?>` — proof the
  abstraction handles real cross-cutting concerns
- **Default-on memoization.** Propless components default to "never
  re-render from parent." `Component<TProps>` defaults to
  `!Equals(oldProps, newProps)`, which works for free with record props.
  Components that consume context still re-render when consumed contexts
  change
- **Generic hook state.** `ValueHookState<T>` stores values directly
  using `EqualityComparer<T>.Default` — no boxing for `int`, `bool`,
  `double`
- **Hooks**: `UseState<T>`, `UseReducer<T>`, `UseReducer<TState,TAction>`,
  `UseEffect`, `UseMemo`, `UseCallback`, `UseRef`, `UseObservable[Tree|Property]`,
  `UseCollection`, `UseContext`, `UsePersisted`, `UseResource` /
  `UseInfiniteResource` / `UseMutation` (Section 3), `UseElementFocus`,
  `UseFocusTrap`, `UseAnnounce`, `UseReducedMotion`, `UseColorScheme`,
  `UseWindowSize` / `UseBreakpoint`, and the new `UseElementRef<T>`
  (spec 033 §3)
- **Post-render effect cleanup.** `UseEffect` defers old cleanup to
  `FlushEffects` Phase 1, runs new effects in Phase 2 — matches React
- **Scoped persisted state (spec 033 §2).** `UsePersisted<T>(key,
  initial, PersistedScope)` now takes an explicit scope:
  `PersistedScope.Application` (process-wide, LRU-bounded,
  memory-pressure-aware) or `PersistedScope.Window` (per-host,
  LRU-bounded). The previous unbounded `PersistedStateCache` is now
  rewritten with eviction-on-full. The two-arg overload is preserved
  for backward compatibility
- **Typed element refs (spec 033 §3).** `UseElementRef<TextBox>()` plus
  `.Ref<TextBox>(target)` modifier replaces the cast-prone
  `el => (TextBox)el.Current` pattern. A DEBUG-only assertion fires when
  the runtime element type doesn't match the requested type

### Concerns

1. **`UseColorScheme` still reads app-level theme, not element-effective
   theme.** The doc comment in `RenderContext.cs:680` claims components
   inside a `.RequestedTheme(Dark)` subtree see the correct variant
   "because `FrameworkElement.ActualTheme` is read at reconcile time."
   The actual code reads `Microsoft.UI.Xaml.Application.Current?.RequestedTheme`.
   This is a load-bearing bug for the headline "dark sidebar in light app"
   composition story. Fixing it is small (read `ActualTheme` from the
   mounted FE); it has not been done across multiple review cycles.

2. **`ShouldUpdateWithProps` uses uncached reflection.** The reconciler
   resolves the typed `ShouldUpdate(TProps?, TProps?)` method via
   `compType.GetMethod(...)` per memo check. React stores the comparator
   as a direct function reference; Compose checks stability at compile
   time. For a tree with 50 components and a root state change, that's
   50 reflection lookups per render. This has been flagged for several
   review cycles.

3. **Context values box value types in the scope stack.** `ContextScope`
   holds `List<(ContextBase, object?)>`. Hooks went to the effort of
   eliminating boxing via `ValueHookState<T>` but context delivery
   reintroduces it for `Context<int>` etc. Common cases (`Context<string>`,
   `Context<ThemeConfig>`) are unaffected.

4. **No slots / named children.** Multi-slot composition (header + body +
   footer) requires ad-hoc props types. SwiftUI has `@ViewBuilder`
   parameters; Compose has multiple `@Composable` lambda params; Reactor
   has no convention.

5. **`UseEffect` / `UseMemo` deps still box.** Dependencies are `params
   object[]` compared with `Equals`. React has the same shape but
   `eslint-plugin-react-hooks` catches misuses; Reactor's
   `REACTOR_HOOKS_004` catches one specific case (freshly-allocated
   values in deps), and `REACTOR_HOOKS_002/003` (the equivalent of
   `react-hooks/exhaustive-deps`) are deferred pending control-flow
   analysis.

6. **`UseCallback` is `UseMemo` returning the closure.** Functionally
   correct but the implementation captures the first-render closure
   unless deps change — different mental model from React's
   `useCallback` and not documented.

### Verdict

**Component Model: B+. State Management: A−. Global State: B+.**

The mechanics are right. The implementation costs (reflection, boxing,
unbounded reflection cache before spec 033 §2 — now fixed) are the
gap to A. The biggest single fix remaining is `UseColorScheme`'s
element-theme bug.

---

## 3. Async Resources

The previous review marked async data as the single largest remaining
capability gap. That gap is closed. `UseResource`, `UseInfiniteResource`,
`UseMutation`, `Pending`, `PendingScope`, and `QueryCache` form a coherent
React-Query-equivalent subsystem with one quality React's ecosystem can't
match: a typed ADT for async state.

```csharp
// ADT-based async state — pattern-match exhaustively
var fruit = UseResource(async ct => await FetchFruitAsync(id, ct), id);
return fruit switch {
    Loading<Fruit>     => Spinner(),
    Data<Fruit> d      => FruitCard(d.Value),
    Reloading<Fruit> r => FruitCard(r.Stale).Opacity(0.7),
    Error<Fruit> e     => ErrorBox(e.Exception.Message, retry: fruit.Retry),
    _                  => null
};

// Cursor pagination
var page = UseInfiniteResource(
    initialCursor: null,
    fetcher: async (cursor, ct) => await api.GetPageAsync(cursor, ct));

// Mutations + invalidation
var save = UseMutation<Employee, Unit>(
    async (emp, ct) => await api.PatchAsync(emp, ct),
    invalidateKeys: ["employees.list", "employees.detail"]);

// Suspense-style fallback regions
PendingScope(Spinner(), Children(fruitView, listView))

// Pattern invalidation
QueryCache.Default.Invalidate("employees.*");
```

### What's good

- **The ADT is the right shape.** `Loading | Data | Error | Reloading`
  makes stale-while-revalidate a type-level concept. Riverpod's
  `AsyncValue` proves the pattern works.
- **Hook-owned cancellation.** Every fetcher takes a `CancellationToken`
  the hook owns. Unmount cancels it. React requires developers to thread
  the token themselves, which they frequently don't.
- **Real `QueryCache` infrastructure.** Per-key `SemaphoreSlim` locks,
  ref-counted subscriptions, background eviction on a shared timer
  (no per-entry `Timer` allocations), TTL + pattern invalidation,
  `EntryChanged` event for downstream readers.
- **DataGrid migrated to the hook path.** `UseHookBasedPaging` is on by
  default — a real production-sized control validates the subsystem.
- **Four hook-rule analyzers (REACTOR_HOOKS_001/004/005/006).** Catch
  conditional hooks, freshly-allocated deps, hooks outside `Render`, and
  fetcher names that look like writes (`Save*`, `Delete*` →
  "use UseMutation").

### Concerns

1. **`QueryCache` is in-process-only.** Cache is lost on app exit. For
   desktop apps with session-restoration expectations, expensive computed
   data refetches from zero on every cold start. TanStack Query has a
   documented persistence plugin; Reactor doesn't.
2. **Cache keys derive from `CallerHookId + DepsHash`.** Two components
   calling the same fetcher with identical deps get *different* cache
   entries unless an explicit `CacheKey` is provided. TanStack Query
   shares by named key by default — a discoverability tax for developers
   porting that mental model.
3. **No automatic retry.** Every serious consumer will add a retry
   wrapper locally. Spec 020 D10 defends this as intentional; it still
   pushes a design decision to application code.
4. **`StaleTime` defaults to zero (always refetch on mount).** Conservative
   but costly.
5. **`PendingScope` has one level of granularity.** A single scope
   covers all nested resources — no per-query suspension boundary.
   React Suspense lets you nest `<Suspense>` for layout sections.
6. **No devtools surface for the cache.** TanStack Query Devtools shows
   live cache state, lets you invalidate keys manually. Reactor has
   nothing equivalent.
7. **`InvalidateKeys` is `string[]`, not structured.** No way to say
   "invalidate everything that depends on employee 42" without writing
   a glob pattern by hand.
8. **No stress evidence at scale.** Eviction sweep is O(n) per tick;
   per-key locks could bottleneck under concurrent churn. Unit tests
   prove correctness; no benchmarks prove scaling.

### Verdict

**B+.** Above Compose (nothing standardized), comparable to SwiftUI
(`async let` / `.task` but no cache), behind React's TanStack Query
ecosystem (mature persistence, devtools, retry). The ADT is a small
quality win SwiftUI/React don't match.

---

## 4. The Reconciler

The reconciler diffs old and new element trees and applies minimal patches.
It's split across `Reconciler.cs`, `Reconciler.Mount.cs`, `Reconciler.Update.cs`,
and `ChildReconciler.cs`. Recent work has tightened correctness in three
specific places: skip-path Tag refresh, lazy event wiring, and in-place
updates for previously-remount-on-change controls.

### Architecture

The reconciler owns the `ContextScope` and drives push/pop during traversal.
Components receive the scope via `BeginRender(requestRerender, contextScope)`.
The component update path checks both `ShouldUpdate(props)` and
`HasConsumedContextChanged` — components re-render when consumed contexts
change even if props are equal.

### Recent correctness fixes (last 9 days)

- **Skip-path Tag refresh (#88).** `Update.cs` previously skipped WinRT
  writes when `ShallowEquals` matched but left `control.Tag` pointing at
  the old element. The trampoline reads `Tag` at dispatch time, so a
  skipped Button fired the previous closure after a re-render that only
  changed `OnClick`. Phase A added `Element.HasCallbacks` (overridden on
  44 callback-bearing leaves), refreshes `Tag` on skip when
  `HasCallbacks` is true, and added 13 missing `ShallowEquals` leaf cases.
  This is the kind of subtle correctness bug that escapes unit tests
  unless someone deliberately writes the "skip + handler-update" fixture
  — `2b92f98` added 14 such identity-preservation fixtures.
- **Aliased RCW dup-event fix (#89).** WinRT runtime callable wrappers
  occasionally aliased across element transitions, producing duplicate
  event subscriptions. Now deduped via `EventHandlerState` lookup. The
  fix shipped with regression fixtures.
- **In-place updates for 14 previously-remount-on-change controls
  (`cd90291`).** `TabView`, `Pivot`, `SplitView`, `RadioButtons`,
  `ComboBox`, `ListBox`, `SelectorBar`, `SemanticZoom`, `RelativePanel`,
  `Popup`, `RefreshContainer`, `CommandBarFlyout`, `SwipeControl`,
  `ParallaxView` now update in place instead of remounting. Preserves
  focus and descendant state across re-renders — a real UX improvement.
- **Programmatic-write change-event echo suppression (`c2b2613`).**
  When a Reactor `Update` handler wrote a value-bearing DP
  (`cp.Color = n.Color`, `nb.Value = n.Value`, `ts.IsOn = n.IsOn`,
  …), the control raised its change event. If the user had wired
  `onChange`, the callback fired with the value Reactor just wrote —
  indistinguishable from a real edit. For PropertyGrid-like surfaces
  bound to an external selection, this caused a cross-row byte-for-byte
  swap (the previous selection's state got overwritten with the new
  selection's value). Fixed via `ChangeEchoSuppressor`
  (`ConditionalWeakTable<UIElement, counter>`) — every Update gates
  programmatic writes with `BeginSuppress`, every Mount-side handler
  consumes a token via `ShouldSuppress`. Covers the full
  value-bearing-control set (ColorPicker, NumberBox, ToggleSwitch,
  CheckBox, RadioButton, ComboBox, Slider, RatingControl, DatePicker,
  …). This was a real production-class bug and the fix is principled.
- **ToggleButton.Toggled wiring deduped via EventHandlerState (#113).**
  Latest fix in the same family.
- **Reconciler coverage selftest fixtures (#99, +1,539 lines).** Lift
  reconciler coverage from 65.7% to 74.5% via 38 new selftest fixtures
  exercising `Ensure*Subscribed` event-handler trampolines, two-phase
  handler wiring, `RegisterType` custom elements, exit transitions, and
  validation visualizers. Remaining ~25% (`Gestures.cs`, `DragDrop.cs`,
  pointer-driven interaction states) needs an OS input driver and stays
  on the Appium tier.
- **Layout-cost ETW overlay (spec 032).** New devtool surfaces the cost
  of every Component's measure/arrange via the
  `Microsoft-Windows-XAML` ETW provider, attributed by innermost-rect
  match. This is the right tool to validate the rest of the reconciler's
  performance claims; the validation pass hasn't run.

### Persistent concerns

1. **Massive switch dispatch for every element type.** `Mount()` and
   `Update()` are giant type-based dispatches. Adding a built-in element
   modifies both files. The `RegisterType<>` API exists for user types
   but the built-ins don't use it.

2. **Tag-based event dispatch is fragile.** `Tag` is repurposed to
   point at the current element so trampolines can read fresh state.
   If anything else sets `Tag` (WinUI internals, user `.Set()` code),
   handlers silently break. The skip-path Tag-refresh fix (#88) closed
   one specific race; the architectural concern remains.

3. **Element pool only handles non-interactive controls.** TextBlock,
   StackPanel, Grid, Border, ScrollViewer, Canvas, Image. Buttons,
   TextBoxes — the majority of any real UI — aren't pooled because
   "resetting their event state safely is more complex." The pool
   optimizes the cheap case while leaving the expensive case
   unoptimized.

4. **Every component adds an invisible Border wrapper.** The reconciler
   wraps every `Component` and `FuncElement` in a `Border` as an
   identity anchor. NavigationHost adds a Grid. CommandHost adds a Grid.
   `SemanticPanel` adds another wrapper. A well-structured Reactor app
   accumulates 3–4 framework wrappers between a component and its
   content — each participating in measure/arrange. The new
   layout-cost overlay is the right tool to measure this; nobody has
   measured it.

5. **No concurrent / interruptible rendering.** React 18's concurrent
   mode allows rendering to be interrupted. Reactor's render cycle is
   fully synchronous and runs to completion. State changes trigger a
   full synchronous re-render of the dirty subtree, which blocks the
   UI thread.

6. **Spec 027's trampoline-dispatch claim is unverified at scale.**
   Phase 2 rewrote event dispatch from "detach + attach on every
   render" to "attach a stable trampoline once per event per element
   lifetime." The architecture is right; the ETW telemetry can verify
   the one-time-attach invariant after the fact. What's missing is a
   render-cycle benchmark on a real app — the layout-cost overlay
   exists, the experiment doesn't.

### Verdict

**B−** trending toward B. The recent correctness fixes (skip-path Tag,
aliased RCW dup-events, in-place updates for 14 controls, change-event
echo suppression) are the right work. The structural concerns (wrapper
elements, pool coverage, no concurrent mode, type-switch monolith)
remain.

---

## 5. Layout System

```
VStack / HStack          // StackPanel
Grid([GridSize], [GridSize], ...)   // typed; string-form deprecated
Canvas, RelativePanel
FlexPanel                // Yoga port — Reactor-exclusive
WrapGrid, ScrollView, Border
```

### Concerns

1. **Grid is now typed (spec 033 §1).** The previous `Grid(["*", "Auto",
   "200"], ...)` API is `[Obsolete]`. The replacement
   `Grid([GridSize.Star(), GridSize.Auto, GridSize.Px(200)], ...)` has
   IntelliSense, type-safe arithmetic on stars, and an invariant-culture
   parser preserved on the side. Closed.

2. **No Spacer equivalent.** SwiftUI's `Spacer()` is one of its
   most-used layout tools. Reactor has none — you fall back to
   `.HAlign(Stretch)` or FlexPanel `grow`, neither as intuitive.

3. **FlexPanel duplicates WinUI's layout system.** A full Yoga port
   creates a parallel layout system alongside WinUI's native one: two
   mental models, no participation across the boundary, performance
   overhead of running Yoga atop WinUI's layout pass. The framework's
   own design docs prefer FlexPanel over StackPanel — meaning it's
   steering developers into the parallel system without a profiling
   pass to justify the default. The earlier CSS-semantics drift bug
   (`397f274`) was fixed by a 526-line `WebViewCssMeasurement` parity
   suite; that's a strong test, but it tells you the layout was
   drift-prone before.

4. **No safe-area / inset handling.** Title bars, taskbars, custom
   chrome — Reactor has no `SafeArea`. SwiftUI has
   `.ignoresSafeArea()` and `.safeAreaInset()`. Compose has
   `WindowInsets`.

5. **Responsive layout via hooks forces full re-renders.**
   `UseWindowSize()` and `UseBreakpoint()` re-render the consuming
   component on resize. SwiftUI's `@Environment(\.horizontalSizeClass)`
   only invalidates views that read it. Compose's `BoxWithConstraints`
   only recomposes the contained scope.

### Verdict

**B+.** Spec 033 §1 closed the type-safety regression. FlexPanel duality
and the missing Spacer/SafeArea remain.

---

## 6. Styling and Theming

A capable system with one genuine differentiator (lightweight styling), one
unfixed correctness bug (`UseColorScheme` reads app theme), and one missing
piece (no custom theme resource definitions).

### What Reactor has

- **~40 semantic theme tokens** (`Theme.PrimaryText`, `Theme.Accent`,
  `Theme.CardBackground`, …) plus `Theme.Ref("AnyResourceKey")` for
  arbitrary WinUI resources
- **Three-tier model.** Unstyled elements theme automatically; theme
  tokens resolve reactively; local concrete values override
- **Style caching.** `ConcurrentDictionary<string, Style>` keyed by
  target type + sorted bindings; `XamlReader.Load` runs once per
  unique binding set, not once per element. Fixed the previous P0 perf
  concern
- **`.RequestedTheme()` modifier.** Applied before `ApplyThemeBindings`
  so ordering invariants hold
- **`UseColorScheme()` / `UseIsDarkTheme()` hooks**
- **Lightweight styling via `ResourceBuilder`.** Per-control resource-
  key overrides (`ButtonBackground`, `ButtonBackgroundPointerOver`, …)
  with VSM integration, ThemeRef support, and managed-key tracking via
  `ConditionalWeakTable<FrameworkElement, HashSet<string>>`. **No
  other C# declarative framework wraps this WinUI capability.**
- **`.Backdrop(BackdropKind)` modifier (spec 033 §6).** Mica/Acrylic/
  None as a declarative element modifier instead of `.Set()`.
  `BackdropApplier` wires into `ReactorHost`; `ReactorHostControl`
  no-ops with a log when the host doesn't own its window. Every
  major sample (TodoApp, ReactorGallery, StylingGallery, regedit,
  ReactorFiles, …) now uses `.Backdrop(BackdropKind.Mica)` at the
  root
- **Three Roslyn analyzers (REACTOR_THEME_001/002/003).**
  Hard-coded colors, `.Set(b => b.Background = ...)` patterns, and
  `.Set(fe => fe.RequestedTheme = ...)` patterns get diagnostics with
  code-fixers for the simple cases. The analyzer IDs were renamed from
  `DUCT001/002/003` in the pre-launch cleanup (`99b608a`)

### Concerns

1. **`UseColorScheme` reads app-level theme, not element effective
   theme.** `RenderContext.cs:680` — the doc comment claims
   element-effective resolution; the code reads
   `Application.Current.RequestedTheme`. A component inside
   `.RequestedTheme(Dark)` returns the wrong scheme. This is the
   single biggest unfixed correctness bug in the framework and has
   persisted across multiple review cycles.

2. **`{ThemeResource}` in dynamically-loaded styles doesn't respect
   per-element `RequestedTheme`.** Documented in `Reconciler.cs:1970`
   as a WinUI platform constraint. Workaround: rely on native control
   theming (which respects `RequestedTheme`) instead of ThemeRef
   bindings. Means `.RequestedTheme()` and `Theme.*` tokens don't
   compose.

3. **No custom theme resource definitions.** `Theme.Ref("key")`
   references existing WinUI resources only. No way to define brand
   colors that adapt to light/dark from Reactor. SwiftUI's asset
   catalog, Compose's `lightColorScheme()`/`darkColorScheme()`, and
   Material UI's `createTheme()` all provide this. Lightweight styling
   makes it more pressing — once developers can override
   `ButtonBackground` with brand color, they want a brand color that
   adapts to theme.

4. **Lightweight-styling resource keys are stringly-typed.**
   `ResourceBuilder.Set("ButtonBackgrnd", ...)` silently sets a
   resource no control reads. The design spec lists
   `ButtonResources.Background` typed constants as future work.

5. **REACTOR_THEME_001/002/003 analyzer coverage is shallow.**
   `THEME_001` maps 5 specific colors to tokens (#FFFFFF, white,
   #000000, black, #0078D4); everything else gets a generic message.
   `THEME_002` knows 3 properties on 6 button-family controls — misses
   TextBox, CheckBox, ComboBox, Slider, ToggleSwitch.

6. **`ApplyStyle` is still string-based.** `.ApplyStyle("AccentButtonStyle")`
   does a runtime dictionary lookup; typos fail silently.

### Verdict

**B−.** Lightweight styling is genuinely unique. Style caching and
`.Backdrop()` close real gaps. The unfixed `UseColorScheme` element-theme
bug and missing custom theme resources keep it from B/B+.

---

## 7. Navigation

```csharp
record HomeRoute;
record DetailRoute(int Id);
record ProfileRoute(string UserId, string? Tab = null);

var nav = UseNavigation<AppRoute>(initial: new HomeRoute());
nav.Navigate(new DetailRoute(42));
nav.GoBack();
nav.Replace(new SettingsRoute());
nav.Reset(new HomeRoute());
nav.PopTo(r => r is HomeRoute);

NavigationHost(nav, route => route switch {
    HomeRoute     => Component<HomePage>(),
    DetailRoute d => Component<DetailPage, int>(d.Id),
    SettingsRoute => Component<SettingsPage>(),
    _             => Text("Unknown route")
}) with {
    Transition = NavigationTransition.Slide(),
    CacheMode  = NavigationCacheMode.Enabled,
    CacheSize  = 10
}

// Lifecycle guards on both source and destination
UseNavigationLifecycle(
    onNavigatedTo: ctx => LoadData(ctx.Route),
    onNavigatingFrom: ctx => { if (hasUnsavedChanges) ctx.Cancel(); },
    onNavigatingTo: ctx => { if (!IsAuthorized(ctx.Route)) ctx.Cancel(); });

// Deep linking
new DeepLinkMap<AppRoute>()
    .Map("/detail/{id:int}", args => new DetailRoute(args.Get<int>("id")))
    .Map("/profile/{userId}/{tab?}", args =>
        new ProfileRoute(args.Get<string>("userId"), args.GetOrDefault<string>("tab")))
    .Map("/docs/**", args => new DocsRoute(args.GetWildcard()));
```

### What's good

- **Architectural choice to bypass WinUI Frame is correct.** Frame's
  XAML metadata requirements, IPage hard-casts, and parameterless
  constructor constraints are hard. Reactor owns the stack, owns the
  rendering, runs transitions on the compositor thread.
- **Developer-owned typed back stack.** Compose Nav3 (stable Nov 2025)
  arrived at the same model independently. SwiftUI's NavigationStack is
  similar but type-erased. Reactor's `IReadOnlyList<TRoute>` is
  inspectable, serializable, testable.
- **C# records as routes.** Structural equality, `with` expressions,
  pattern matching, JSON serialization — for free.
- **Composition-layer GPU transitions.** `ElementCompositionPreview.GetElementVisual()`
  + slide/fade/drill-in/spring on the compositor thread, automatic
  direction reversal on `GoBack`. No competitor has GPU-accelerated
  transitions as a first-class navigation feature.
- **Source + destination guards with cancellation.**
- **State serialization (`GetState()` / `SetState()` JSON round-trip).**
- **NavigationView auto-sync via `.WithNavigation(nav, routeToTag,
  tagToRoute)`.** Eliminates 30+ lines of boilerplate per app.
- **`NavigationDiagnostics` observability hooks.**
- **117+ unit tests + 43 Appium E2E tests across 6 classes.** The E2E
  test discovery filter bug (44 tests invisible) is fixed.

### Concerns

1. **`ConnectedTransition` is a stub.** `NavigationTransition.Connected()`
   exists and is documented, but `TransitionEngine.RunTransition` logs a
   warning and falls back to slide. Shipping a named API that does
   something different is worse than not shipping it.
2. **No adaptive multi-pane layout.** Compose Nav3's
   `Scene`/`SceneStrategy` handles list-detail (side-by-side on wide
   screens, push on narrow). Important even for desktop (Outlook,
   Teams, VS Code). Reactor is strictly single-pane; multi-pane requires
   manual composition outside the navigation system.
3. **Deep-link patterns are stringly-typed.** `Map("/detail/{id:int}",
   ...)` with `args.Get<int>("id")` — typos fail silently. The irony is
   thick: typed routes, stringly-typed deep-link middle layer.
4. **`UseSystemBackButton` is opt-in.** For a desktop framework where
   back-navigation is core, this should be default with opt-out.
5. **Showcase apps don't use it.** Regedit, ReactorFiles, wordpuzzle,
   monaco-editor, the chat sample, and HeadTrax all manage their own
   view state. Navigation is proven in NavigationDemo and the
   ReactorGallery; the production-shaped apps don't compose it with
   anything else.
6. **Synchronous guards only.** No async loader pattern (React Router
   v7-style fetch-before-navigate). For data-heavy apps where every
   transition depends on an API call, this is necessary but not
   sufficient.

### Verdict

**B+.** Architecturally competitive with Compose Nav3 and SwiftUI
NavigationStack on type safety; ahead on transitions. The stub
`ConnectedTransition`, missing adaptive layout, and showcase non-adoption
keep it short of A−.

---

## 8. Commanding

The single most novel feature in Reactor. No competing declarative
framework provides this:

- **React** has nothing — `cmdk`/`kbar` are UI components, not
  registries.
- **SwiftUI** has `CommandMenu`/`CommandGroup` for menu bars but no
  bundling — each menu item repeats label/icon/shortcut/action.
- **Compose** has nothing.
- **VS Code, Files, Windows Terminal** all built custom registries.

```csharp
// Define once with metadata
var saveCmd = new Command {
    Label = "Save",
    ExecuteAsync = async () => await SaveDocumentAsync(),
    Icon = new SymbolIconData("Save"),
    Accelerator = new KeyboardAcceleratorData(VirtualKey.S, VirtualKeyModifiers.Control),
    CanExecute = isDirty,
    Description = "Save the current document"
};

// 16 standard commands (Cut/Copy/Paste/Undo/Redo/Delete/SelectAll/Save/Open/...)
var cutCmd = StandardCommand.Cut(() => CutSelection(), canExecute: hasSelection);

// Async lifecycle wrapping
var save = UseCommand(saveCmd);   // .IsExecuting auto-disables the button

// Use in N surfaces from one definition
CommandBar(AppBarButton(cutCmd), AppBarButton(copyCmd), AppBarButton(pasteCmd))
MenuBar(MenuBarItem("Edit", MenuItem(cutCmd), MenuItem(copyCmd), MenuItem(pasteCmd)))

// Per-site override
MenuItem(deleteCmd with { Label = "Remove from list" })

// Focus-scoped accelerators
CommandHost([saveCmd, undoCmd, redoCmd], VStack(editorContent))
```

### What's good

- **"`CanExecute` is a bool" is the right design.** Traditional
  `ICommand` uses `CanExecuteChanged` events fighting the reactive
  framework. Reactor's model: commands are created during `Render()`,
  which already runs on every relevant state change, so `CanExecute` is
  just a bool that reflects current reactive state. Eliminates the
  impedance mismatch.
- **Define-once works.** One `Command` drives `AppBarButton`,
  `MenuItem`, `Button`, accelerator, tooltip — all from one record.
- **`UseCommand` for async is well-designed.** Wraps with
  `IsExecuting` tracking and re-entrance prevention. Sync-only commands
  skip hook state — thoughtful optimization.
- **`StandardCommand` covers the 16 standard interactions** with the
  correct icons and accelerators.

### Concerns

1. **`StandardCommand` labels are still hard-coded English.**
   `StandardCommand.Cut().Label = "Cut"` — not `Intl()`. The framework
   has a full ICU localization system; its own commanding system
   doesn't use it. WinUI's own `StandardUICommand` localizes for all
   supported Windows languages. This is a regression from the platform
   it's built on, and the fix is small. Unfixed across multiple
   review cycles.

2. **Accelerators rebuild on every render.** `UpdateCommandHost` clears
   and recreates all `KeyboardAccelerator` objects per render cycle —
   O(n) COM interop calls per CommandHost where n is the number of
   commands. Architecturally expensive and unfixed.

3. **No command routing to focused view.** Cut/Copy/Paste in
   multi-document apps requires manual wiring. SwiftUI has
   `FocusedValue`/`FocusedObject`; WPF has `RoutedUICommand`. Reactor
   has neither. Listed as future work in the design spec.

4. **No command palette UI.** The data model is the perfect foundation
   for a VS Code-style palette; the palette itself doesn't ship.

5. **Command equality defeats memoization.** `Command` is a record;
   record equality compares all fields including `Execute` and
   `ExecuteAsync` delegates. Two lambdas capturing different closures
   are never equal. Components receiving commands as props always fail
   the memo check.

6. **Limited control coverage.** Button, AppBarButton, MenuItem
   integrate. SplitButton, SwipeItem, ContentDialog actions still take
   bare `Action` callbacks.

### Verdict

**B+.** Genuinely ahead of the entire industry; the implementation
gaps (localization, accelerator rebuild, routing, palette, equality)
are the gap to A.

---

## 9. Lists and Collections

```
ListView / GridView          // WinUI controls
LazyVStack<T> / LazyHStack<T>  // virtualized via ItemsRepeater
TemplatedListView<T> / TemplatedGridView<T>
ForEach                      // not virtualized
TreeView                     // with drag support
FlipView, SemanticZoom
```

### Concerns

1. **`ForEach` is not virtualized.** 1000 items = 1000 elements +
   1000 mounted controls. SwiftUI's `ForEach` inside `List` is
   virtualized; Compose's `items {}` inside `LazyColumn` is virtualized.
   Reactor requires explicitly choosing `LazyVStack<T>` — a footgun for
   developers who reach for the simpler API first.
2. **`LazyVStack` requires a key selector.** Mandatory keys are
   best practice but add prototyping friction. React/SwiftUI/Compose
   all have positional defaults.
3. **No sections or grouping.** SwiftUI's `Section(header:)`, Compose's
   `stickyHeader {}` — Reactor has nothing.
4. **No pull-to-refresh integration.** `RefreshContainer` exists as a
   separate element — not wired into ListView like SwiftUI's
   `.refreshable {}` or Compose's `PullToRefreshBox`.
5. **No drag-and-drop reordering for lists.** TreeView has it; ListView
   and GridView don't. SwiftUI's `.onMove` on `ForEach` has no
   equivalent.

A new spec (#029, "tree-grid-and-grouping") exists in design but hasn't
landed.

### Verdict

**B.** Functional but conspicuously thin compared to SwiftUI/Compose.

---

## 10. Animation

A fully operational compositor-animation system with one structural
ceiling (compositor properties only).

### What Reactor has

- **Implicit transitions:** `.OpacityTransition`, `.RotationTransition`,
  `.ScaleTransition`, `.TranslationTransition`, `.BackgroundTransition`
- **Theme transitions:** `.WithTransitions`, `.ItemContainerTransitions`
- **Curve DSL:** `Curve.Spring(damping, period)`, `Curve.Ease(ms,
  Easing.*)`, `Curve.Linear(ms)`, plus `Easing.CubicBezier(...)` for
  custom curves
- **`.Animate(curve, AnimateProperty.*)` modifier** for declaration-site
  implicit animations
- **Layout animations:** `.LayoutAnimation()`, `.SpringLayoutAnimation()`
- **Enter/exit transitions** with composition operators:
  `.Transition(Transition.Fade + Transition.Slide(Bottom))`,
  `.Transition(Transition.Fade | Transition.Scale(0.85f))` (asymmetric)
- **Interaction states:** `.InteractionStates(s => s.PointerOver(...).
  Pressed(...).Focused(...))` — zero-reconcile visual state machine
- **Stagger:** `.Stagger(TimeSpan, Curve?)`
- **Keyframes:** `.Keyframes("pulse", trigger, k => k.At(0, ...).At(0.5,
  ...))`
- **Scroll-linked:** `.ScrollAnimation(sv, b => b.Parallax(0.5).
  FadeOut(100, 400).ScaleRange(...))`
- **`AnimationScope.WithAnimation(curve, () => SetExpanded(!expanded))`**
  — SwiftUI-style ambient animation; works for all 5 compositor
  properties; persists across the async render boundary
- **Connected animations** (string-keyed)

The reconciler integration is thorough: enter transitions on mount,
exit transitions defer removal via `CompositionScopedBatch`, all three
removal paths in `ChildReconciler` route through
`RemoveChildWithExitTransition`. Type-mismatch replacements (e.g.,
`visible ? Border : Text`) route through
`ReplaceChildWithExitTransition`. Compositor-tainted elements (those
that had `GetElementVisual()` called) are excluded from pooling via
`ConditionalWeakTable` to prevent crashes.

### Concerns

1. **Compositor-property-bound, structurally.** Every tier of the
   animation system targets the same set: Opacity, Offset, Scale,
   Rotation, CenterPoint + 3 brush swaps. **You cannot animate**
   Width, Height, CornerRadius, Margin, Padding, FontSize, arbitrary
   colors, Clip geometry, BorderThickness, or any custom DP. SwiftUI
   animates anything that produces a different view body. Compose's
   `animateAsState` works for any type with a `TwoWayConverter`. This
   is a WinUI platform constraint, not a Reactor design failure, but
   it's the ceiling that keeps the grade below B.

2. **No per-frame `UseAnimation` / `UseSpring` hook.** React Spring,
   Framer Motion, Compose's `Animatable` — these provide interpolated
   values each frame for custom rendering. Reactor's animations run
   on the compositor; managed code can't observe intermediate values.

3. **Connected animations are string-keyed.** A typo silently produces
   no animation (`try/catch` swallows it). SwiftUI's
   `matchedGeometryEffect` uses typed Namespace; Compose's
   `SharedTransitionScope` uses typed keys.

4. **`InteractionStates` is closed to 8 properties.** 5 transforms +
   3 brushes. CornerRadius-on-hover, BorderThickness-on-focus need
   `.Set()`.

5. **No cross-element sequencing primitives.** Stagger handles uniform
   delay; "first fade in header, then slide in list" requires manual
   `WithAnimationAsync` + `await` chains. Framer Motion's
   `staggerChildren`/`delayChildren` and Compose's `AnimatedContent`
   provide richer orchestration.

### Verdict

**B−.** Within its compositor ceiling, the system delivers what its API
promises — composition operators, ambient animation context,
zero-reconcile interaction states. The ceiling is real and structural;
SwiftUI/Compose animate any value, Reactor animates ~8.

---

## 11. Accessibility

Reactor now has an accessibility *system* — annotations + semantics +
behavior + diagnostics — rather than just modifiers on primitives. The
diagnostic story (compile-time + runtime + E2E) is more comprehensive
than any other C# UI framework. Three load-bearing gaps remain.

### What Reactor has

- **16 accessibility modifiers** across two storage tiers (inline for
  Tier 1, lazy `AccessibilityModifiers` sub-record for Tier 2): `.AutomationName`,
  `.AutomationId`, `.HeadingLevel`, `.IsTabStop`, `.TabIndex`,
  `.AccessKey`, `.HelpText`, `.FullDescription`, `.Landmark`,
  `.AccessibilityView`, `.AccessibilityHidden`, `.Required`,
  `.LiveRegion`, `.PositionInSet`, `.HierarchyLevel`, `.ItemStatus`,
  `.LabeledBy` (deferred resolution via `Loaded` event), `.TabNavigation`
- **`SemanticPanel` + `.Semantics(role, value, rangeValue, rangeMin,
  rangeMax)`** — composite components describe their own automation
  peer. The panel is a real WinUI `Panel` subclass that overrides
  `OnCreateAutomationPeer()` and provides `IValueProvider` +
  `IRangeValueProvider`. This is the single hardest architectural
  problem in declarative-UI accessibility, and Reactor solves it
- **`AccessibilityScanner`** — runtime WCAG 2.1 diagnostic scanner with
  rich AI-agent-friendly structured output (diagnostic ID, WCAG
  criterion, severity, fix suggestion, code snippet, parent context,
  nearest heading, sibling text)
- **3 Roslyn analyzers (REACTOR_A11Y_001/002/003)** — icon buttons
  without `.AutomationName()`, images without alt text or
  `.AccessibilityHidden()`, interactive elements (CheckBox, ToggleSwitch,
  Slider, ComboBox) without `.AutomationName()`
- **Imperative hooks (now expanded):** `UseFocusTrap`,
  `UseAnnounce` (live-region announcer with zero-size anchor element),
  `UseReducedMotion` (reads `UISettings.AnimationsEnabled`),
  `UseColorScheme` (with the High Contrast value), `UseElementFocus`
- **Auto-set HeadingLevel.** `Heading()` factory automatically sets
  `Level1`, `SubHeading()` sets `Level2` — pit-of-success
- **`ElementPool` clears 14 UIA properties** on return so stale
  state doesn't leak across reuse
- **12 Appium E2E tests** mapped to specific WCAG success criteria,
  validated through the real UIA pipeline (the bus Narrator and NVDA
  use)
- **a11y-showcase sample app**

### Concerns

1. **`UseFocusTrap` traps but doesn't cycle.** `UseFocusTrap.cs:80–85`
   cancels the focus change when Tab would leave the container — but
   the inline TODO comment "Optionally cycle: if Tab was going forward
   past the last element, move focus to the first focusable child" is
   never executed. WAI-ARIA Dialog Pattern expects Tab to wrap. React
   Focus Lock cycles. Unfixed.

2. **`SemanticPanel` covers only 2 of ~20 automation patterns.**
   `IValueProvider` and `IRangeValueProvider`. A custom tree
   (`IExpandCollapseProvider`), multi-select widget
   (`ISelectionProvider`), or expandable panel — none of these can
   use `SemanticPanel`. SwiftUI's `.accessibilityRepresentation {}`
   accepts any view as the proxy. Compose's `Modifier.semantics {}`
   supports arbitrary roles, states, actions.

3. **Semantic role is stringly-typed.** `.Semantics(role: "slidre", ...)`
   silently maps to `AutomationControlType.Custom`. An enum
   (`SemanticRole.Slider`) would prevent typos and surface IntelliSense.

4. **`SemanticPanel` adds another invisible wrapper.** Now 4 framework
   wrapper types: component Border, NavigationHost Grid, CommandHost
   Grid, SemanticPanel. Cumulative measure/arrange tax.

5. **`AccessibilityScanner` is runtime-only and DEBUG-only.** Roslyn
   analyzers cover three specific compile-time patterns; the broader
   scanner rules (heading structure, landmark coverage, form labeling)
   only fire when the app runs. React's `eslint-plugin-jsx-a11y`
   catches a broader set at edit time.

6. **REACTOR_A11Y_001/003 narrow coverage.** REACTOR_A11Y_001 matches
   `Button` by identifier name — misses `var b = Button(...)` followed
   by separate-statement modifier chains. Misses `AppBarButton`,
   `ToggleButton`, `SplitButton`, `RepeatButton`. REACTOR_A11Y_003 misses
   `RadioButton`, `TextBox`, `PasswordBox`, `AutoSuggestBox`.

7. **No programmatic focus restoration.** Focus management beyond
   trapping (focus restoration on back-navigation, `XYFocus` D-pad
   navigation) is unaddressed. `UseElementFocus` provides
   `RequestFocus` but no scoped state model like SwiftUI's `@FocusState`.

8. **The flagship apps still don't use any of this.** Regedit,
   ReactorFiles, wordpuzzle, monaco-editor, the chat sample, and
   HeadTrax don't use heading structure, landmarks, live regions, or
   `SemanticPanel`. The Heading/SubHeading auto-Level helps any app
   that uses those DSL functions, but no flagship app demonstrates the
   full surface. Only a11y-showcase does — and it's purpose-built.

### Verdict

**B−.** The system shape is right; the layers are still uneven.
SemanticPanel solves the hardest problem at a real cost. The
load-bearing UseFocusTrap-doesn't-cycle bug and the still-narrow
analyzer coverage are the gap to B. Showcase non-adoption keeps it
from feeling production-grade.

---

## 12. Charting

A genuine sub-framework. WinUI has no built-in charts; serious WinUI
apps use LiveCharts/OxyPlot/Telerik. Reactor ships 43 D3-ported chart
samples plus an 8-layer accessibility infrastructure that is — apart
from sonification — arguably the most accessible chart library on any
platform.

### Architecture

- **Layer 1 — Automation peers.** `ChartAutomationPeer` implements
  `IGridProvider` (series × points), `ITableProvider` (axis labels);
  `ChartPointProvider` exposes per-point peers with `IValueProvider`;
  `IScrollProvider` for large datasets
- **Layer 2 — Per-point labels + auto-summary.** `ChartSummarizer`
  produces "Line chart, 5 series, 120 points. Revenue trending upward by
  12% over 6 months. Peak: $42K in October."
- **Layer 3 — Alternate-view convention.** `.AlternateView(Element)`
  attaches a developer-supplied alternate (typically DataGrid). Framework
  owns the T-key toggle, focus save/restore, live-region announcement
- **Layer 4 — Keyboard navigation.** Arrow walks points, Ctrl+arrow
  by series/axis, +/− zoom, Shift+arrow brush, L focuses legend, T
  toggles alternate. The Highcharts/Power BI keyboard model
- **Layer 5 — Focus context.** Captures and restores focus across
  chart ↔ alternate-view transitions
- **Layer 6 — Live announcements.** `ChartLiveAnnouncer`,
  debounced 400ms trailing
- **Layer 7 — Forced colors / reduced motion / double encoding.**
  `ChartPalette.ForcedColors` swaps to system high-contrast colors;
  `ChartPalette.Harden` does LCH-space lightness adjustment to meet
  WCAG AA against a given background; `ChartPalette.ColorblindSimulate`
  uses Brettel matrix for deuteranopia/protanopia/tritanopia.
  `ReactorHost` honors `UISettings.AnimationsEnabled`. All series
  double-encoded by default (color + shape + dash); `.ColorOnly()` is
  the opt-out and triggers a scanner warning
- **Layer 8 — 12 chart-specific scanner rules** (A11Y_CHART_001..012)
  with WCAG criterion references and `reactor charts harden` code
  snippets

### What's good

- **`Harden` is rare and valuable.** Most frameworks punt on "make my
  brand palette accessible." Reactor's `ChartPalette.Harden(brand,
  background)` returns a palette with LCH lightness adjusted to meet
  WCAG AA — deterministic, per-color.
- **Double-encoding on by default.** The most common chart-a11y failure
  (color-only) is prevented at the framework level.
- **The 43-sample gallery was retrofitted in one pass.** Every chart
  type uses `.Title()`, `.Description()`, and the a11y infrastructure.
  This is the rare case where the framework's own showcase actually
  uses its own new features comprehensively.
- **Reduced-motion is framework-level.** `ReactorHost` reads
  `UISettings.AnimationsEnabled`; charts (and any other consumer) can
  check the flag.

### Concerns

1. **No sonification.** Apple Charts supports audio graphs (each point
   plays a tone proportional to Y-value); Highcharts has a
   sonification plugin. Spec 026 lists it as "deferred A+ ceiling
   feature" — the single biggest competitive a11y gap.
2. **`ForceGraph` a11y is decorative-only.** Spec 026 §11 says force
   graph physics ships as structure, not motion. Dense graphs (>200
   nodes) become inaccessible.
3. **Hit-target expansion requires `.Interactive()`.** Static charts —
   the majority of analytics dashboards — don't get this. Should be
   always-on with opt-out for decorative.
4. **Forced-colors palette clips to 4 series.** Beyond 4, colors collide
   in HC mode; only shape/dash distinguishes them. No graceful-fallback
   guidance.
5. **Per-point peer allocation is unbounded.** A 10,000-point
   scatterplot creates 10,000 `ChartPointProvider` instances per UIA
   walk. No benchmarks.
6. **Live-region debounce is hard-coded 400ms.** No per-chart
   customization.
7. **No third-party adoption signal.** `ReactorCharting.Gallery` is
   comprehensive but synthetic. No production dashboard app has used
   it. No screen-reader-user survey.

### Verdict

**B+.** Architecturally the most comprehensive chart accessibility
system in any C# declarative framework, plausibly second only to Apple
Charts on overall a11y depth (Apple wins on sonification). The
synthetic-only gallery and missing sonification keep it from A−.

---

## 13. Input and Events

Spec 027 (commit `76d0f51`, 73 files, +7,249 / −2,137) closed the
single longest-standing "Reactor is a thin WinUI passthrough" critique.
Pointer / tap / keyboard / focus / gesture / drag-drop are now
first-class declarative modifiers backed by a new trampoline-dispatch
event model.

### What Reactor has

- **Semantic events:** `OnClick`, `OnChanged`, `OnSelectionChanged` —
  unchanged
- **Pointer modifiers (full surface):** `.OnPointerPressed`, `.OnPointerMoved`,
  `.OnPointerReleased`, `.OnPointerEntered`, `.OnPointerExited`,
  `.OnPointerCanceled`, `.OnPointerCaptureLost`, `.OnPointerWheelChanged`
- **Tap-family:** `.OnTapped`, `.OnDoubleTapped`, `.OnRightTapped`,
  `.OnHolding` — `IsTapEnabled` etc. auto-toggled
- **Keyboard:** `.OnKeyDown`, `.OnKeyUp`, `.OnPreviewKeyDown`,
  `.OnPreviewKeyUp`, `.OnCharacterReceived`
- **Focus:** `.OnGotFocus`, `.OnLostFocus`, `UseElementFocus()` hook,
  `FocusManager` helper, the new `UseElementRef<T>()` (spec 033 §3) +
  typed `.Ref<T>(target)` modifier
- **Gesture recognizers:** `.OnPan(minimumDistance, axis, withInertia)`,
  `.OnPinch(withInertia)`, `.OnRotate(withInertia)` with typed
  `PanGesture`/`PinchGesture`/`RotateGesture` records carrying phase,
  translation/delta/velocity (pan), scale/anchor (pinch), angle
  (rotate), `IsInertial`. Single
  `ManipulationStarted/Delta/Completed` subscription per element;
  `ManipulationMode` auto-computed
- **Drag-and-drop:** `.OnDragStart<TPayload>` + `.OnDrop<TPayload>` typed
  round-trip plus untyped overloads. Standard formats (text/URI/HTML/RTF/
  files/bitmap) and custom format ids — eager / sync-provider /
  async-provider overloads wired to `DataPackage.SetDataProvider` so
  expensive formats only render if a target asks. `CanDrag`/`AllowDrop`
  auto-set. `DragOperationNegotiation` resolves source/target conflicts
- **Trampoline dispatch.** `EventHandlerState` attaches a stable
  trampoline once per event per element lifetime. Updating the user
  handler is a single field write, not detach/attach. ETW emits
  `EventTrampolineAttached` (first attach) and
  `EventTrampolineDispatch` (per fire). The recent `bf9b2e7` and #88
  fixes addressed dedupe and skip-path Tag refresh in this layer

### Concerns

1. **No gesture composition.** SwiftUI has `.simultaneously(with:)`,
   `.sequenced(before:)`, `.exclusively(before:)`. Reactor has three
   discrete gesture primitives — they cooperate at the manipulation
   level but don't compose declaratively. "Long-press to begin drag"
   has to be hand-rolled.
2. **`LongPressGesture` is an event, not a typed gesture.** SwiftUI has
   `LongPressGesture(minimumDuration:)` with phase. Reactor has
   `.OnHolding` surfacing raw `HoldingRoutedEventArgs`.
3. **Inherits WinUI manipulation constraints.** Inertia decay rate
   isn't exposed, rail behavior is WinUI-native and can't be disabled,
   pan is screen-axis-aligned only.
4. **Drag-and-drop has no production consumers.** 370 lines of
   `DragData` and zero showcase apps use it. Typed-format-id is
   `typeof(T).FullName` — rename a model and round-trip breaks. No
   versioning. No devtools surface for DnD negotiation.
5. **Trampoline dispatch is unbenchmarked.** "All 6,723 unit tests
   pass" doesn't measure the COM interop path the claim depends on.
   The layout-cost overlay (spec 032) is now the right tool to produce
   a before/after on a real app — no one has run it.
6. **Auto-mutation can surprise.** Attaching `.OnTapped` sets
   `IsTapEnabled=true`; attaching pointer handlers on a `Shape`
   auto-fills a transparent `Fill`. Right defaults; conflict with
   user-set values via `.Set()` isn't handled explicitly.
7. **No declarative pointer-capture modifier.** Drag-to-draw still
   requires `CapturePointer`/`ReleasePointerCapture` calls inside the
   handler — a `.Set()`-flavored escape.
8. **Wheel/touchpad are second-class.** No typed wheel-delta record,
   no momentum-vs-discrete classification, no horizontal-wheel
   shortcut.
9. **`UseElementFocus` ≠ `@FocusState`.** Each focusable element gets
   its own `ElementRef` + `RequestFocus` closure. Fine for "focus the
   first input"; awkward for "the last-edited row of a datagrid"
   (a single binding modeling N states).

### Verdict

**B.** Pointer / tap / keyboard / focus / gesture / drag-drop are no
longer passthrough — that's the largest single shift in this section
since the framework began. The remaining gaps (composition, typed
long-press, capture ergonomics, gesture inertia tuning, benchmarking
the trampoline) are all design omissions rather than broken promises.

---

## 14. Developer Experience and Devtools

### What's good

- **IntelliSense, refactoring, and compile-time type safety.** No XAML
  parse errors, no DataContext confusion, navigation routes are types,
  commands are records. The C# compiler catches real mistakes that XAML
  doesn't.
- **.NET hot reload integrates.** `HotReloadService` hooks
  `MetadataUpdateHandler`; UI updates while preserving hook state via
  in-memory `RenderContext`.
- **MCP devtools server (specs 024/025).** `reactor.tree`, `reactor.
  click`, `reactor.type`, `reactor.state`, `reactor.windows`,
  `reactor.screenshot`, `reactor.fire`, `reactor.invoke`, `reactor.
  toggle`, `reactor.scroll`, `reactor.waitFor`,
  `reactor.switchComponent`, `reactor.reload` — over HTTP and stdio.
  CLI parity via `mur devtools`. **No competing C# UI framework ships
  this.** Stable window-scoped node ids (`r:main/Counter.btn-inc`)
  survive re-renders because they encode component-tree position.
  Selector resolution falls back through explicit id → automation id →
  name → type+index → source location
- **Pre-launch security hardening (#102).** Per-launch 256-bit bearer
  token in lockfile; constant-time `Authorization` compare on every
  `/mcp` request; CSRF/Origin/Host checks; 1 MiB body cap;
  `SemaphoreSlim(16)` dispatch gate; mutation audit logging. VS Code
  extension hardened: dotnet PATH hijack closed, strict CSP with
  nonces on the webview, textContent-based component-list rendering.
  Source generators apply `SymbolDisplay.FormatLiteral` on .resw
  emission (build-time RCE fix). The remediation pass closed the kind
  of sharp edges that show up in security review and tend to be
  ignored elsewhere
- **ETW tracing.** `Microsoft-UI-Reactor` EventSource with 6 keywords
  (Reconcile start/stop, component render boundaries, state change,
  MCP call, effects-flush, child-reconcile, EventDispatch) consumable
  via PerfView, dotnet-trace, EventPipe
- **Layout-cost overlay (spec 032 — new).** A live dev overlay that
  paints a green outline + 64×20 sparkline of measure+arrange time
  over the last 6s on every Component. Data flows from the
  `Microsoft-Windows-XAML` ETW provider, attributed by innermost-rect
  match against bounds derived from a live visual-tree walk on each
  flush. Live flag toggle — no host restart. This is the right tool to
  validate spec 027's trampoline claim and the in-place-update claim
- **Reconcile highlight overlay (#83 — new).** Paints a fading flash
  on every element the reconciler touches. Pairs with the layout-cost
  overlay via shared `OverlayHostWiring` infrastructure
- **Frame-aligned sampling design (spec 031).** Captures perf samples
  aligned to render frames so traces don't smear across frame
  boundaries. Implementation is partial
- **Four hook-rule analyzers (REACTOR_HOOKS_001/004/005/006).** Catch
  conditional hooks, freshly-allocated deps, hooks outside Render,
  and write-shaped fetcher names. The exhaustive-deps-equivalent
  (REACTOR_HOOKS_002/003) is deferred pending control-flow analysis
- **Selftest coverage at scale.** 6,723 unit tests passing.
  Reconciler's own coverage rose from 65.7% to 74.5% via #99 (38
  selftest fixtures). 12 unit tests deflaked across 4 classes (#118).
  Wave 1+2 coverage tests added ~5,600 lines. The CI pipeline now
  runs `Reactor.SelfTests` on hosted `windows-latest` (#110)

### Concerns

1. **The premise of MCP devtools is a bet.** AI-agent automation as a
   primary UI-debugging audience is plausible; if developers
   continue to prefer GUI devtools (React DevTools, Xcode Instruments,
   Layout Inspector), the infrastructure serves a small audience.
   Reactor shipped the supplement before the primary.
2. **State is read-only.** `reactor.state` returns hook *shape*, not
   values. No `reactor.setState`. The privacy posture is defensible;
   debugging and deterministic test scenarios suffer.
3. **No source mapping in v1.** Spec 010 hasn't landed. Without it,
   agents can't correlate a tree node back to the C# component that
   created it — the most important agent capability. Listed for
   v1.1.
4. **No cache inspection.** `QueryCache` (Section 3) ships without an
   MCP surface. TanStack Query Devtools is the gold standard here.
5. **No performance profiling via MCP.** ETW exists; a `reactor.
   profile` tool to start/stop/export traces from an MCP session
   doesn't.
6. **Tree diffing is deferred.** Spec hypothesizes agents diff
   client-side. Reasonable when context is unlimited; constrained
   sessions (CI, token-budgeted LLMs) may benefit from server-side
   diffing.
7. **ETW is coarse.** Aggregate Reconcile start/stop, component render
   boundaries, effects-flush. **No per-component render timing**, no
   per-hook breakdown, no element-pool hit rate. React DevTools
   Profiler shows per-component flame graphs; Compose Layout Inspector
   shows recomposition counts. Reactor's ETW is *infrastructure for
   profiling*; the per-component drill-down has to be built on top.
   The new layout-cost overlay (spec 032) closes part of this — but
   only for measure+arrange, not render
8. **Hot reload inherits .NET's limitations.** Adding fields, changing
   hierarchies, adding classes, lambda changes often require a full
   restart — exactly the kinds of changes you make during UI
   development. React Fast Refresh and Compose Live Edit are
   purpose-built for UI changes and handle a wider range of edits
9. **The preview surface has been replaced, not augmented.** The
   previous `--preview [ComponentName]` CLI flag is gone; preview
   capture lives inside the devtools MCP server. The VS Code extension
   has a legacy fallback. Non-agent users have a regression in mental
   model — `--preview` was discoverable; `--devtools --screenshot`
   requires knowing screenshots are a sub-capability
10. **No interactive in-IDE preview.** It's still a live screenshot.
    SwiftUI Xcode Previews are interactive, Compose Preview renders
    composables inline. Reactor is "look at the app as it renders."

### Verdict

**B.** Devtools MCP and ETW + the new layout-cost overlay together
are the most ambitious DX investment in any C# UI framework. The bet
on agents-as-primary-audience is unconventional. Source mapping,
state mutation, cache inspection, and per-component render timing are
the v1.1 work that turns "infrastructure" into "developer-facing."

---

## 15. The .Set() Escape Hatch

`.Set()` is Reactor's escape hatch — a lambda that receives the
underlying WinUI control. The remaining surface is meaningfully smaller
than a quarter ago, but real.

```csharp
Button("Click", onClick)
    .Set(b => b.FlowDirection = FlowDirection.RightToLeft)
```

### What's been reclaimed

Spec 027 pulled pointer / tap / keyboard / focus / gesture / drag-drop
into first-class modifiers. Spec 033 pulled `Backdrop`. Earlier work
pulled `RequestedTheme`. Lightweight styling pulled per-control
resource overrides. Navigation/commanding/styling pulled the
application-architecture surface.

### What still requires .Set()

- Composition layer effects beyond `.Shadow()` / `.Animate()`
- Materials (`AcrylicBrush` internals, `Compositor.CreateSpriteVisual`)
- Most windowing APIs (`AppWindow`, `OverlappedPresenter`, custom
  backdrop config beyond `BackdropKind`)
- Custom `Path` / `Geometry` drawing, stroke dash arrays
- Ink input
- `ProcessKeyboardAccelerators` and other specialty event surfaces
- Pointer capture (`CapturePointer` / `ReleasePointerCapture`)

### Persistent issues

1. **Breaks the declarative model.** `.Set()` is imperative mutation.
   The reconciler doesn't know what changed; pool reuse may carry
   over previous-owner state.
2. **Event handlers wired in `.Set()` leak.** `Set` runs on mount AND
   update, and there's no unmount lifecycle hook for `.Set`-wired
   side effects.
3. **`.Set()` runs on every render.** Setters are `Action<TControl>[]`
   compared by length, not content — meaning any element with Setters
   always updates and the callbacks fire each render. Wasteful and
   surprising.
4. **There's no `.Get()`.** No reactive bridge from control properties
   back to component state for component logic.

### Verdict

The abstraction is materially thicker than a quarter ago, but
"composition / materials / windowing" — the surfaces customers notice
in *polished desktop app* territory — remain `.Set()`-bound. "You
don't need to know WinUI" is still overclaimed.

---

## 16. Scorecard

How Reactor's individual feature areas compare to the established
declarative frameworks.

| Feature Area | Reactor | React | SwiftUI | Compose | Notes |
|---|---|---|---|---|---|
| **Component Model** | B+ | A | A | A | Context, default-on memoization, generic hooks; uncached reflection in memo dispatch, no slots |
| **Local State** | A− | A | A | A | Generic hooks, post-render cleanup, scoped+LRU persisted state (spec 033 §2); dep arrays still box |
| **Global State** | B+ | A | A | A | Context system; boxing in scope stack, no selector |
| **Async Data** | B+ | A (TanStack) | B+ | C | UseResource/UseInfiniteResource/UseMutation/Pending; in-process cache only, no retry default, no devtools |
| **Reconciler** | B− | A | A− | A | Skip-path Tag refresh, lazy event wiring, in-place updates for 14 controls, change-event echo suppression — recent correctness work; monolithic switch dispatch, 4 wrapper-element types, no concurrent mode, trampoline benchmark unrun |
| **Layout** | B+ | B+ | A | A | Typed `GridSize` (spec 033 §1) closes the regression; FlexPanel duality; no Spacer; no SafeArea |
| **Theming** | B− | B+ | A | A | Style caching, `.RequestedTheme`, `.Backdrop`, lightweight styling, 3 analyzers; `UseColorScheme` element-theme bug *unfixed*; no custom theme resources |
| **Navigation** | B+ | A | A | A | Type-safe routes, dev-owned stack, GPU transitions, source+destination guards; `ConnectedTransition` stub, no adaptive multi-pane, showcase non-adoption |
| **Commanding** | B+ | N/A | C+ | N/A | Define-once + 16 standard + async lifecycle + focus-scoped accelerators; English-only labels *unfixed*, accelerator rebuild per render, no routing, no palette |
| **Lists/Collections** | B | B+ | A | A | Virtualization exists; no sections, no built-in pull-to-refresh integration, no list reordering |
| **Animation** | B− | B | A | A | Curve DSL, enter+exit, interaction states, keyframes, stagger, WithAnimation; compositor-property ceiling; no per-frame hooks |
| **Accessibility** | B− | B | A | A | 16 modifiers + SemanticPanel + 3 analyzers + runtime scanner + `UseAnnounce`/`UseFocusTrap`/`UseReducedMotion`; trap doesn't cycle *unfixed*, panel covers 2 of ~20 patterns, showcase non-adoption |
| **Charting + Chart A11y** | B+ | N/A | A (Apple) | C | 43 samples + 8-layer a11y + LCH-space `Harden` + colorblind sim + 12 scanner rules; no sonification, ForceGraph decorative-only |
| **Input/Events** | B | B | A | A | Spec 027 pulled pointer / tap / key / focus / gesture / drag-drop; trampoline dispatch unbenchmarked, no gesture composition, no typed long-press |
| **Styling** | B− | B+ | A | A | Lightweight styling unique; analyzer coverage shallow; no custom theme defs |
| **Developer Experience** | B | A | B+ | B+ | Hot reload, MCP devtools, ETW, layout-cost + reconcile-highlight overlays, 4 hook analyzers, ~5,600 lines coverage tests; no GUI devtools, no per-component render timing, no in-IDE interactive preview |
| **Devtools + Tracing** | B | B+ (DevTools) | A (Instruments) | B+ (LI) | Unique MCP server + 13 tools + stable node ids + supervisor + CLI parity + bearer-token security; state read-only, no source map, no cache inspection |
| **Control Coverage** | A | N/A | A | A | 94% of WinUI wrapped |
| **Error Handling** | B | B+ | D | D | ErrorBoundary exists — neither SwiftUI nor Compose has this |
| **Localization** | B+ | B | B | B | ICU-based, full system, on Context |
| **Responsive Layout** | B | B+ | A | A | Hooks work but force full re-render |
| **Interop (WinForms / Vanilla WinUI)** | B+ | N/A | N/A | N/A | `XamlIslandControl` + `WinFormsHostElement` for WinForms-hosts-Reactor; spec 033 §7 InteropFirst sample for vanilla-WinUI hosts Reactor with shared ObservableCollection / ICommand / brushes / declarative XAML ItemTemplate |

---

## 17. Conclusion

### Where Reactor genuinely leads

1. **Commanding.** No competing declarative framework provides
   define-once commands with metadata bundling, standard commands,
   async lifecycle, and focus-scoped accelerators.
2. **Lightweight styling.** `ResourceBuilder` surfaces WinUI's
   per-control resource-key overrides as a fluent API with proper
   cleanup and managed-key tracking. Nobody else wraps this.
3. **Accessibility diagnostics + chart a11y.** 3 Roslyn analyzers +
   `AccessibilityScanner` + 12 chart rules + structured AI-friendly
   output is more comprehensive than any other C# UI framework. The
   8-layer chart accessibility, with `Harden` for WCAG-AA palette
   adjustment, exceeds all but Apple Charts.
4. **MCP devtools.** Stable window-scoped node ids, UIA-based
   automation, HTTP + stdio transports, CLI parity, lockfile
   single-instance, 256-bit per-launch bearer tokens. No competitor
   ships this. Whether the bet on AI-agent-primary audience pays off
   depends on adoption that doesn't exist yet.
5. **ErrorBoundary.** Neither SwiftUI nor Compose has this. React
   does.

### Where Reactor is competitive

- Component model (with caveats: reflection, no slots)
- Navigation (Compose Nav3 stable Nov 2025 reached the same model
  independently)
- Async data (above Compose, comparable to SwiftUI, behind React's
  TanStack ecosystem)
- Input handling (post spec 027)
- WinForms / vanilla-WinUI interop (spec 033 §7)
- Localization (ICU, full)

### Where Reactor trails

- DSL ergonomics (C# language constraint; spec 033 §5 `Expr` helps)
- Animation breadth (compositor-property ceiling; WinUI platform
  constraint)
- Hot reload (inherits .NET's limitations vs. React Fast Refresh /
  Compose Live Edit)
- Performance tooling depth (ETW + new layout-cost overlay are
  infrastructure; no per-component render timing yet)

### What still keeps Reactor from production-ready

1. **The flagship apps don't use the framework's own features.** This
   is now the dominant critique across multiple review cycles. Recent
   migrations adopted `GridSize` and `.Backdrop()` — both forced by
   spec 033's `[Obsolete]` regime. Nothing else. Regedit, ReactorFiles,
   wordpuzzle, monaco-editor, validation-showcase, the chat sample,
   HeadTrax, NetPulse — none of them use `UseNavigation`, `Context`,
   `StandardCommand`, `SemanticPanel`, `UseFocusTrap`, `UseAnnounce`,
   `UseResource` (except HeadTrax, partially), `UseElementFocus`,
   `UseElementRef<T>`, `.OnPan` / `.OnDrop`, or any of the spec 027
   modifiers. Each new system ships with an isolated demo that proves
   it works in isolation; nothing proves they compose. The charting
   gallery is the rare exception (43 samples retrofitted with chart
   a11y in one pass, commit `229e41b`) — and it works. Until the same
   discipline reaches the general-purpose flagship apps,
   "production-ready" is theoretical.

2. **Three small unfixed correctness gaps stay unfixed.**
   `UseColorScheme` reads app theme not element theme. `UseFocusTrap`
   doesn't cycle. `StandardCommand` labels are English-only. Each fix
   is small. None has been done across multiple cycles. Either no one
   prioritizes them, or no one in production has hit them — both
   answers signal the same thing about adoption.

3. **Performance refactors lack before/after evidence.** Spec 027
   trampoline dispatch, in-place updates for 14 controls, lazy event
   wiring, skip-path correctness — architecturally correct, no
   render-cycle benchmark on a real app. The new layout-cost overlay
   (spec 032) is the right instrument; nobody has run the experiment.
   "All 6,723 tests pass" doesn't measure the COM interop path the
   claims depend on.

4. **String-typed APIs at boundaries accumulate.** Routes are typed
   records but deep-link patterns are strings. `SemanticPanel` roles
   are strings. Lightweight-styling resource keys are strings.
   Connected animation keys are strings. Drag-and-drop typed-format-ids
   are `typeof(T).FullName` with no versioning. The pattern: type-safe
   core, stringly-typed integration seams.

5. **Wrapper-element accumulation is structural.** Component Borders,
   NavigationHost Grids, CommandHost Grids, SemanticPanel — four
   framework wrapper types in the worst case. Each justified
   individually; the cumulative measure/arrange tax is unmeasured.
   The new layout-cost overlay can answer "how much" — the question
   hasn't been asked.

6. **Async resources shipped without scale evidence.** Eviction is
   O(n) per tick; per-key lock contention under churn is untested;
   no devtools surface for the cache. Unit tests prove correctness;
   no benchmarks prove scaling.

### To become production-ready, Reactor needs to

1. **Adopt its own features in the flagship apps.** Regedit and
   ReactorFiles should navigate via `UseNavigation`, share state via
   `Context`, surface Cut/Copy/Paste via `StandardCommand`, use
   `SemanticPanel` for any composite that exposes a slider or rating,
   trap focus in dialogs via `UseFocusTrap`, and announce status via
   `UseAnnounce`. The chat sample (David Fowler — an external author!)
   should use `UseResource` for message loading and `UseMutation` for
   send. The chart gallery already proves the team can do this; the
   discipline just hasn't reached the general-purpose flagships.
2. **Fix `UseColorScheme` to read element-effective theme.** The fix
   is to read the mounted FE's `ActualTheme` instead of
   `Application.Current.RequestedTheme`. Small, load-bearing, unfixed.
3. **Implement focus cycling in `UseFocusTrap`.** The TODO is in the
   source code. WAI-ARIA Dialog Pattern expects it.
4. **Localize `StandardCommand` labels.** The framework's own
   localization system should drive its own commanding system.
5. **Run the layout-cost overlay on a real app and publish the
   numbers.** Validate the trampoline-dispatch claim, the
   in-place-update claim, the skip-path claim. The instrument exists
   now; the experiment doesn't.
6. **Stress-test async resources.** Large caches, concurrent fetches,
   long-running sessions, per-key lock contention.
7. **Finish theming.** Custom theme resource definitions, more than 3
   ThemeRef binding properties, type-safe resource key constants.
8. **Add command routing to the focused view.** Cut/Copy/Paste in
   multi-panel apps still requires manual wiring.
9. **Extend `SemanticPanel` to more automation patterns.**
   `IToggleProvider`, `IExpandCollapseProvider`, `ISelectionProvider`.
10. **Source mapping for devtools (spec 010).** Without it, agents
    can't map tree nodes back to source.
11. **Cache inspection for devtools.** `QueryCache` deserves a
    TanStack-Query-Devtools-equivalent.
12. **Sonification for charts.** The single biggest remaining a11y
    gap vs. Apple Charts and Highcharts.
13. **Gesture composition.** `.simultaneously` / `.sequenced` /
    `.exclusively` and a typed `LongPressGesture(minimumDuration:)`.
14. **Version typed drag formats.** `reactor/typed/<typeof(T).
    FullName>` breaks when models are renamed.
15. **Finish exhaustive-deps analyzer (REACTOR_HOOKS_002/003).** The
    most valuable ESLint rule React developers rely on.

### The fundamental question

**Is Reactor a framework or a wrapper?**

Reactor is no longer a wrapper for the parts it covers. The component
model, navigation, commanding, async resources, accessibility, charting,
devtools, and (post spec 027) input handling are genuine framework-level
abstractions. A developer can build a multi-page accessible WinUI app
with shared state, keyboard shortcuts, navigation transitions, async
data, and a chart all in Reactor's mental model.

The remaining "wrapper" surfaces are composition / materials / windowing
— exactly the polished-desktop-app territory where customers notice the
gap. Spec 033 §6 (`.Backdrop`) chipped at this; the rest stays
`.Set()`-bound.

The competitive landscape has moved. Compose Navigation 3 (stable
Nov 2025) reached Reactor's developer-owned typed-stack model
independently. SwiftUI's NavigationStack is mature and `Charts`
ships with audio graphs. React Router v7 has view transitions and
TanStack Router has full type inference. The window where
"declarative UI for WinUI 3" was a novelty has closed; what matters
now is whether Reactor is *competitive in quality*, not whether it
exists.

### The honest final assessment

**Reactor is the most capable declarative C# UI framework that has
ever existed**, has five features where it leads the broader industry,
and is genuinely competitive on most of the rest. Spec 033 closed real
ergonomic gaps (typed Grid, Backdrop, typed refs, scoped persisted
state, Expr block expressions). The reconciler correctness work over
the last 9 days (skip-path Tag refresh, in-place updates for 14
controls, change-event echo suppression, 38 reconciler coverage
fixtures) hardened a layer that used to leak subtle bugs. The pre-launch
hardening pass (#102) closed a long list of security sharp edges and
moved the project to MIT. The new layout-cost overlay (spec 032) and
reconcile-highlight overlay (#83) are the right instruments to
validate the rest.

The framework's risk isn't capability — it's integration. New
subsystems land faster than the existing flagship apps adopt them, and
three small unfixed correctness bugs persist across multiple review
cycles because nothing in the maintained showcase exercises them. At
some point the team will need to spend a sprint doing nothing but
retrofitting Regedit and ReactorFiles, *or* the showcase has to be
honestly recategorized as "demo apps for individual features"
rather than "proof the framework composes."

**Trajectory: right.** **Pace: aggressive.** **Integration debt:
growing.** **Production-readiness: one focused integration sprint
away, if the team takes it.**
