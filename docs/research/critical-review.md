# Reactor Framework — Critical Review

A skeptic's component-by-component analysis of Reactor against React, SwiftUI, and
Jetpack Compose. This document catalogs architectural weaknesses, API design
problems, missing capabilities, and places where Reactor is a hack on top of WinUI
rather than a principled declarative UI framework.

---

## Executive Summary

Reactor is an ambitious attempt to bring React-style declarative UI to WinUI 3.
It has impressive breadth of control coverage (94% of WinUI controls wrapped),
a now-solid component model foundation, and a faithful hooks system. The last
four days alone landed five substantial systems: async resources (a React
Query-equivalent), a full charting accessibility layer (8 layers across 18
commits), an MCP devtools surface for AI-agent-driven automation, ETW
tracing, and — shipped in the last 24 hours — spec 027's declarative input
system (73 files, +7.2k LoC) that closes the long-standing pointer /
gesture / drag-drop / focus gaps and replaces per-render COM event churn
with a trampoline-dispatch model. The framework is no longer short on
features — it is now short on the integration and polish to make those
features compose reliably in real apps. Significant problems remain, and
several of the new shipments have implementation concerns that temper the
enthusiasm:

1. **The component model is now a real framework foundation** — context system,
   memoization, generic hook state, persisted state, and post-render effect
   cleanup have all landed, closing the most fundamental gaps
2. **Navigation has matured beyond initial implementation** — type-safe routes,
   developer-owned back stack, composition-layer transitions, lifecycle guards
   (now including destination-side guards), LRU caching, serialization, and
   enhanced deep linking (query strings, optional params, wildcards). Navigation
   diagnostics provide observability. E2E test discovery was broken (44/46 tests
   invisible behind a filter bug) and is now fixed — 43 Appium tests pass across
   6 classes. But connected transitions are still a stub, and there's no adaptive
   multi-pane layout story
3. **Commanding is a genuine differentiator** — define-once commands with
   metadata bundling, 16 standard commands, async lifecycle, focus-scoped
   keyboard accelerators, and ICommand interop. No competing framework (React,
   SwiftUI, Compose) provides this out of the box. But accelerators rebuild on
   every render, standard command labels aren't localized, and there's no command
   routing to the focused view
4. **The DSL is constrained by C# language limitations** — verbose, repetitive,
   and leaky compared to JSX, SwiftUI's result builders, or Compose's Kotlin DSL
5. **Theming and styling have made a major leap** — style caching eliminates the
   XamlReader.Load-per-element perf concern, `.RequestedTheme()` modifier closes
   a gap, `UseColorScheme` hook exists (but reads app-level theme, not per-element
   effective theme), lightweight styling is Reactor's first genuinely unique styling
   feature, and three Roslyn analyzers provide static guidance. But custom branded
   theme resources still don't exist, and the `UseColorScheme` bug undermines
   the RequestedTheme story
6. **Accessibility has crossed a meaningful threshold** — SemanticPanel +
   `.Semantics()` modifier solves the custom automation peer problem for
   composite components (the single hardest architectural limitation), a runtime
   WCAG scanner (AccessibilityScanner) and 3 Roslyn analyzers (REACTOR_A11Y_001/
   002/003) provide both runtime and compile-time diagnostics, UseFocusTrap adds
   the first imperative accessibility hook, LabeledBy is now wired with deferred
   resolution, ElementPool clears 14 UIA properties on return, and a dedicated
   a11y-showcase sample demonstrates the full surface. But SemanticPanel adds
   another invisible wrapper, the scanner is DEBUG-only and runtime-only,
   UseFocusTrap doesn't cycle focus (it traps but doesn't wrap), and most
   imperative hooks (UseAnnounce, UseReducedMotion, UseScreenReaderActive)
   remain unbuilt
7. **Animation is now a fully operational compositor animation system** — 8
   features (curves, transitions, interaction states, keyframes, stagger,
   scroll-linked, WithAnimation scope) with solid abstractions; 4 prior
   integration bugs now fixed (exit transitions, Focused state, WithAnimation
   routing, stagger integration), plus 3 runtime fixes (async scope
   persistence, Opacity routing for .Animate(), pool crash prevention).
   Still compositor-property-bound, no arbitrary DP animation
8. **WinForms interop has landed as a new capability** — bidirectional hosting
   via `XamlIslandControl` (WinForms hosts Reactor/WinUI) and `WinFormsHostElement`
   (Reactor hosts WinForms), with E2E tests for Tab navigation, rendering, and
   accessibility across boundaries. One direction is blocked (Reactor-primary
   hosting WinForms) due to WinUI's compositor-only rendering
9. **The `.Set()` escape hatch is load-bearing, but meaningfully narrower** — spec 027
   pulled pointer / tap / keyboard / focus events, pan/pinch/rotate gestures, and
   drag-and-drop out of `.Set()` and into first-class modifiers. What remains:
   composition-layer effects, materials, windowing, ink input, shape geometry,
   and pointer-capture ergonomics
10. **Async data is now a framework feature, not a developer chore** — `UseResource`,
    `UseInfiniteResource`, `UseMutation`, and a shared `QueryCache` cover the
    React Query / SWR territory in-framework, with a `Pending` element for
    suspense-like fallbacks, focus revalidation, and a DataGrid integration
    (hook-based paging on by default). Four hook-rule analyzers
    (REACTOR_HOOKS_001/004/005/006) back it up. But the API is fresh, has
    zero field evidence of correctness at scale, and the cache is
    in-process-only
11. **Charting is now a genuine sub-framework** — 43 D3-ported chart samples,
    plus a comprehensive 8-layer accessibility infrastructure
    (ChartAutomationPeer, ChartPalette with WCAG-hardening, ChartSummarizer,
    ChartKeyboardNavigator, ChartLiveAnnouncer, alternate-view convention,
    forced-colors + reduced-motion, 12 chart-specific scanner rules). No other
    C# declarative framework ships a chart accessibility system of this
    depth. But sonification is absent, the ForceGraph a11y story is
    explicitly "decorative only," and hit-target expansion requires opt-in
12. **Devtools is now an MCP surface for AI agents, not just a preview** —
    `reactor.tree`/`click`/`type`/`state`/`windows`/`screenshot`/etc. over
    both HTTP and stdio transports, with stable window-scoped node ids, a
    `mur devtools` supervisor, rolling observability logs, and CLI parity.
    This is uniquely ahead of the entire competition (nobody else ships an
    MCP devtools surface). But it's Windows-only, DEBUG-gated, read-only for
    state, and the previous VS Code preview has been replaced rather than
    augmented
13. **ETW tracing exists** — `Microsoft-UI-Reactor` EventSource emits
    reconcile/render/state/MCP/lifecycle keywords to classic ETW and
    EventPipe. This closes part of the "no performance profiling" critique
    from Section 13, but it's coarse-grained (no per-component render
    timing) and requires external tooling (PerfView, dotnet-trace) to
    consume
14. **Declarative input shipped (spec 027)** — pointer enter/exit/wheel/
    capture-lost, tap / double-tap / right-tap / holding, key-up /
    preview-key / character-received, focus modifiers + `UseElementFocus`
    hook, typed pan/pinch/rotate gestures, and drag-and-drop with
    eager + sync-provider + async-provider format overloads wired to
    `DataPackage.SetDataProvider`. Event dispatch now uses a stable
    trampoline per event per element lifetime, eliminating the previous
    per-render COM detach/attach cost on hot input paths. But gesture
    composition (`.simultaneously`/`.sequenced`) is absent, long-press is
    an event rather than a typed gesture, the trampoline refactor has
    zero benchmark evidence (just "all 6,390 unit tests pass"), and
    drag-drop's 370-line `DragData` has no showcase consumer
15. **Selftest coverage crossed 85%** — commits `e85f4d8` (1,264 new
    unit tests, 4,222 LoC) and `1870868` (30 new selftest fixtures,
    1,426 LoC) bring coverage materially higher. Real reliability
    investment, but whether the new tests catch actual regressions or
    just inflate the percentage is a later question
16. **Two XAML-based samples were removed** (`FlexPanelGallery`,
    `regedit-winui`) to stop teaching anti-patterns, trimming confusion
    for newcomers without adding new proof points

**Verdict:** Reactor has crossed a third important threshold. The first was the
component model foundation (context, memoization, hooks). The second was the
application architecture layer (navigation, commanding). The third is the
accessibility semantic layer: SemanticPanel solves the hardest architectural
problem — composite components describing their own semantics to screen readers
— and the diagnostic tooling (runtime scanner, compile-time analyzers) moves
accessibility from "annotations on primitives" toward "a system with
guardrails." Navigation and commanding continue to mature with incremental
improvements (destination guards, deep link enhancements, diagnostics).

The distance from "impressive demo" to "ships to real users" has narrowed
further. Async data loading was the single largest remaining feature gap
(the old "React has had Suspense since 2018" critique) and has now been
addressed with a fully typed ADT-based system, not a bolt-on. Charting
accessibility transforms Reactor's charting surface from "pretty graphics
that screen readers can't see" into arguably the most accessible chart
library on any platform. Devtools MCP is an unconventional bet — that the
future of UI debugging is AI-agent automation rather than human devtools —
and whether it pays off depends on adoption patterns nobody can predict
yet. The critical path now runs through: (1) stress-testing async resources
and QueryCache under realistic load — this is a brand-new subsystem with
no production miles; (2) the showcase apps still not using the framework's
own features (Outlook clone still uses `UseState<string>` for navigation,
and now also doesn't use `.OnPan`, `.OnDrop`, or `UseElementFocus`); (3)
benchmarking the spec 027 trampoline claim — ETW events prove the
"one-time attach" invariant held after the refactor, but no one has
measured a render-cycle before/after delta on a real app; (4) fixing the
UseColorScheme/RequestedTheme composition bug; (5) custom branded theme
resources (still missing after multiple iterations); (6) remaining
imperative accessibility hooks (UseAnnounce, UseReducedMotion); (7) chart
sonification and hit-target-always-on; (8) devtools state mutation and
source mapping; (9) gesture composition and a typed long-press primitive;
(10) composition-layer / materials / windowing — the remaining fat `.Set()`
surface. The framework is no longer blocked on any single P0 gap, but the
sum of its P1/P2 gaps has grown slightly larger as new features arrive
faster than integration testing. "Features exist" now outpaces "features
compose correctly in real apps" by a wider margin than a quarter ago.

---

## Table of Contents

1. [The DSL: C# as a UI Language](#1-the-dsl-c-as-a-ui-language)
2. [Component Model](#2-component-model)
3. [State Management](#3-state-management)
3a. [Async Resources (new)](#3a-async-resources)
4. [The Reconciler](#4-the-reconciler)
5. [Layout System](#5-layout-system)
6. [Styling and Theming](#6-styling-and-theming)
7. [Navigation](#7-navigation)
8. [Commanding](#8-commanding)
9. [Lists and Collections](#9-lists-and-collections)
10. [Animation](#10-animation)
11. [Accessibility](#11-accessibility)
11a. [Charting and Chart Accessibility (new)](#11a-charting-and-chart-accessibility)
12. [Input Handling and Events](#12-input-handling-and-events)
13. [Developer Experience](#13-developer-experience)
13a. [Devtools MCP and ETW Tracing (new)](#13a-devtools-mcp-and-etw-tracing)
14. [The .Set() Problem](#14-the-set-problem)
15. [Component-by-Component Scorecard](#15-component-by-component-scorecard)
16. [Conclusion](#16-conclusion)

---

## 1. The DSL: C# as a UI Language

### The fundamental problem

React has JSX. SwiftUI has result builders. Compose has Kotlin's trailing lambdas
and compiler plugin. Reactor has... C# method calls with `params` arrays.

This matters enormously. The quality of the UI DSL determines whether writing UI
code feels natural or feels like fighting the language.

### What Reactor looks like

```csharp
VStack(
    Text("Hello").Bold().FontSize(24),
    Button("Click me", () => setCount(count + 1))
        .Background("#0078D4")
        .Padding(12, 8),
    count > 5 ? Text("Wow!") : null
)
```

### What the competition looks like

**React (JSX):**
```jsx
<VStack>
  <Text bold fontSize={24}>Hello</Text>
  <Button onClick={() => setCount(c => c + 1)}
          style={{ background: '#0078D4', padding: '12px 8px' }}>
    Click me
  </Button>
  {count > 5 && <Text>Wow!</Text>}
</VStack>
```

**SwiftUI:**
```swift
VStack {
    Text("Hello").bold().font(.title)
    Button("Click me") { count += 1 }
        .background(.blue)
        .padding(.horizontal, 12)
    if count > 5 { Text("Wow!") }
}
```

**Compose:**
```kotlin
Column {
    Text("Hello", fontWeight = FontWeight.Bold, fontSize = 24.sp)
    Button(onClick = { count++ }) {
        Text("Click me")
    }
    if (count > 5) { Text("Wow!") }
}
```

### Specific DSL weaknesses

**1. No block syntax for children.** SwiftUI and Compose use trailing closures
/ lambdas that visually separate children from the container. In Reactor, children
are positional arguments mixed into the constructor call. For deeply nested UIs,
this becomes bracket-counting hell:

```csharp
VStack(
    HStack(
        VStack(
            Text("Label"),
            Text("Value")
        ).Width(200),
        VStack(
            Text("Label 2"),
            Text("Value 2")
        ).Width(200)
    ),
    HStack(
        Button("OK", onOk),
        Button("Cancel", onCancel)
    )
)  // Which closing paren belongs to which?
```

**2. Modifier chains allocate on every render.** Every `.Margin(10)` call does
`el with { Modifiers = ... }` creating a new record copy. A chain of 5 modifiers
creates 5 intermediate records. SwiftUI avoids this via opaque return types and
view builder inlining. Compose avoids it via `Modifier` composition that chains
without copying. Reactor's approach generates measurable GC pressure in hot render
paths — every element with modifiers allocates multiple short-lived objects per
render cycle.

**3. The `with` expression problem.** Reactor's element records use C# records
with `init` properties. The fluent modifier pattern uses either extension methods
that create `ElementModifiers` records and merge them, or `with` expressions on
the concrete record. This means *every modifier call allocates a new
ElementModifiers record*, even for a single property change:

```csharp
// This creates a new ElementModifiers for just one property,
// then merges it (another allocation) with the existing modifiers
public static T Margin<T>(this T el, double uniform) where T : Element =>
    Modify(el, new ElementModifiers { Margin = new Thickness(uniform) });
```

**4. String-typed APIs where enums or types should be used.** Grid columns are
`string[]`: `Grid(["*", "Auto", "200"], ...)`. There's no compile-time
validation that `"*"` or `"Auto"` are valid values. SwiftUI uses
`GridItem(.flexible())`, `GridItem(.fixed(200))` — type-safe and discoverable.
Compose uses `GridCells.Adaptive(minSize)`. Reactor borrowed CSS grid syntax but
lost type safety in the process.

**5. No children as content blocks.** In React, JSX children are a natural part
of the markup. In SwiftUI, `@ViewBuilder` closures provide block syntax. In
Compose, `content: @Composable () -> Unit` trailing lambdas do the same. Reactor
forces all children into `params Element?[]` constructor arguments — there's no
visual distinction between container properties and container children.

**6. Implicit string conversion is a code smell, not a feature.** `Element` has
`public static implicit operator Element(string text) => new TextElement(text)`.
This means `VStack("Hello", "World")` works. But it also means any string
accidentally passed where an Element is expected silently becomes text. This is
the kind of convenience that causes bugs in large codebases.

**7. Null-based conditional rendering is fragile.** Reactor uses nullable elements
for conditional rendering: `condition ? Text("yes") : null`. This works but
requires `FilterChildren` to strip nulls on every render. SwiftUI uses `if`
directly in the view builder. Compose uses `if` naturally. React uses
`{condition && <Component/>}`. Reactor's approach works but is less readable and
requires runtime filtering.

---

## 2. Component Model

### The component model story: from gaps to genuine framework foundation

The previous version of this review scored Component Model at C and Global State
at F — "no Context equivalent," "no memoization," "hooks box value types." A
significant component model diff has landed that addresses these systematically:
context system, default-on memoization, generic hook state, persisted state, and
post-render effect cleanup. The honest assessment now: **the component model
foundation is solid and competitive with the established frameworks on core
mechanics, but some secondary gaps remain.**

### What Reactor has now

- **Class components** extending `Component` with `Render()` method
- **Props via generics**: `Component<TProps>` for typed props
- **Function components**: `Func(ctx => { ... })` inline lambdas
- **Memo function components**: `Memo(ctx => { ... }, deps)` with dependency tracking
- **Error boundaries**: `ErrorBoundary(child, fallback)`
- **Context\<T\>** — tree-scoped ambient state (React Context equivalent)
- **Default-on memoization** — `ShouldUpdate()` on class components, dependency
  tracking on Memo elements
- **Generic hook state** — `ValueHookState<T>` eliminates boxing for value types
- **Persisted state** — `UsePersisted<T>(key, initial)` survives unmount/remount
- **Post-render effect cleanup** — cleanup runs in `FlushEffects`, not during render
- **No slots/named children** — no mechanism for multi-slot composition

### What shipped: Context system (Context\<T\>)

```csharp
// Define: static, typed, named, with default
public static readonly Context<ThemeConfig> ThemeContext =
    new(new ThemeConfig("light"));

// Provide: modifier on any element, scoped to subtree
VStack(children).Provide(ThemeContext, darkTheme)

// Consume: hook in any descendant component
var theme = UseContext(ThemeContext);
```

The design follows React's mental model with SwiftUI's fluent syntax. Contexts
are tree-scoped: inner providers shadow outer ones. The `ContextScope` class
maintains a stack during reconciler traversal — pushed on entering an element
with `ContextValues`, popped on leaving. `UseContext<T>` reads the nearest
ancestor's value by walking the stack backward.

The reconciler integrates context into the memo check: `HasConsumedContextChanged`
compares each `ContextHookState.LastValue` against the current scope value. A
component that consumes `ThemeContext` re-renders when the theme changes, even
if its props haven't changed. A component that doesn't consume any context skips
the check entirely — zero cost for the common case.

**LocaleProvider now uses Context.** The localization system has been migrated
from the hand-rolled `LocaleContext.Current` thread-static hack to a proper
`Context<IntlAccessor?>`. `UseIntl()` is now `UseContext(IntlContexts.Locale)`
under the hood. The legacy `LocaleContext.Current` is maintained for backward
compatibility but marked internal. This validates the context system with a
real use case — the framework's own localization depends on it.

### What shipped: Default-on memoization

```csharp
// Propless component: ShouldUpdate() defaults to false
// → only re-renders from own state changes or context changes
public class StatusBar : Component { ... }

// Props component: ShouldUpdate(old, new) defaults to Equals(old, new)
// → record props get structural comparison for free
public record DashboardProps(string Title, int Count);
public class Dashboard : Component<DashboardProps> { ... }

// Function component with memo: explicit dependency array
Memo(ctx => { ... }, count, theme)
```

Class components have `ShouldUpdate()` which defaults to `false` for propless
components (never re-render from parent) and `!Equals(oldProps, newProps)` for
`Component<TProps>` (re-render when props change structurally). This leverages
C# records — a `record` props type gets value equality for free, so the common
case requires zero developer effort. The reconciler also checks context changes
via `HasConsumedContextChanged`, so components re-render when consumed contexts
change regardless of the `ShouldUpdate` result.

The `ShouldUpdateWithProps` method in the reconciler uses reflection to call the
typed `ShouldUpdate(TProps?, TProps?)` through the untyped `Component` reference.
This is a one-time cost per component type (reflection calls are not cached), but
it runs on every parent re-render for every child component. For deep trees with
many components, this reflection overhead could add up.

### What shipped: Generic hook state (no boxing)

```csharp
// Before: HookState.Value was object — boxing for every int, bool, double
private class HookState { public object Value = default!; }

// After: ValueHookState<T> — no boxing, no allocation for value types
private class ValueHookState<T> : HookState { public T Value; }
```

`UseState<int>`, `UseReducer<bool>`, `UseMemo<double>` — none of these box
anymore. The generic `ValueHookState<T>` stores the value directly. The equality
check uses `EqualityComparer<T>.Default` instead of `object.Equals`, avoiding
both boxing for comparison and the allocation of intermediate `object` references.

This also means hook type mismatches (calling `UseState<int>` on one render and
`UseState<string>` on the next) produce clearer error messages — the exception
says exactly which generic types conflicted.

### What shipped: Persisted state

```csharp
var (scrollPos, setScrollPos) = UsePersisted("inbox-scroll", 0.0);
```

`UsePersisted<T>(key, initialValue)` works like `UseState<T>` but survives
unmount/remount. Values are stored in `PersistedStateCache` — a static
`Dictionary<string, object?>` — and saved to cache in `RunCleanups()` (on
unmount). On next mount, the cached value is used instead of `initialValue`.

### What shipped: Post-render effect cleanup

Previously, effect cleanup ran synchronously during `UseEffect()` — inside the
render phase. Now, cleanup is deferred: `UseEffect` sets `PendingCleanup` (the
old cleanup function) and `FlushEffects()` runs all pending cleanups *before*
running new effects, in a two-phase approach:

```
Phase 1: Run all PendingCleanup actions (from previous render)
Phase 2: Run all new pending effects
```

This matches React's behavior: cleanup from the previous render runs after the
new render completes, not during it. Expensive cleanup (network cancellation,
timer disposal) no longer blocks the render.

### What's actually good about this (credit where due)

The context system is well-designed. The `Context<T>` type is simple — a
static field with a default value and a debug name (via `[CallerMemberName]`).
The provide mechanism uses the existing element `with` pattern via
`ContextExtensions.Provide()`, so it composes naturally with other modifiers.
The `ContextScope` stack is lightweight — a `List<(ContextBase, object?)>`
with version tracking. The consumer hook follows the existing hook pattern
exactly. This is the right API shape — it mirrors React Context closely enough
that the mental model transfers, while using Reactor's fluent modifier convention
for providing.

The memoization default is the right call. Making propless components skip
re-renders by default (unless self-triggered or context-changed) is aggressive
but correct — it matches Compose's behavior where stable parameters cause
automatic skipping. Record-based props getting structural equality for free via
`Equals` is a clever use of C# language features that React and Compose can't
match (they need explicit `React.memo()` comparators or `@Stable` annotations).

The test coverage is thorough: 24 context unit tests, 12 context self-host tests,
19 memoization tests, 18 hook refactor tests, 17 persisted state tests, 15
integration tests exercising realistic multi-hook component patterns. The self-
host test pattern (manually driving BeginRender → Render → FlushEffects through
a ContextScope without WinUI controls) is a good strategy for testing framework
internals without platform dependencies.

### What's still concerning (skeptic's view)

**1. Context values are boxed in the scope stack.** `ContextScope` stores
`List<(ContextBase, object?)>` — the value is `object?`. A
`Context<int>` provided with value `42` boxes that int. Every `UseContext<T>`
read casts back from `object?`. The hook state itself is now unboxed
(`ValueHookState<T>`), but the context delivery mechanism reintroduces boxing
for value types. For `Context<string>` or `Context<ThemeConfig>` (the
common cases), this doesn't matter. But the inconsistency is notable — hooks
went to the effort of eliminating boxing while the context system didn't.

**2. ShouldUpdateWithProps uses reflection on every memo check.** The reconciler
calls `ShouldUpdateWithProps` which does `compType.GetMethod("ShouldUpdate", ...)`
via reflection to find the typed `ShouldUpdate(TProps?, TProps?)` method. This
runs every time a parent re-renders and the child is a `Component<TProps>`. There's
no caching of the `MethodInfo` — each check does a fresh reflection lookup. For
a tree with 50 components and a root state change, that's 50 reflection calls per
render cycle. React's `React.memo()` stores the comparator as a direct function
reference. Compose's stability check is compile-time. Reactor's is a runtime
reflection walk.

**3. PersistedStateCache is a static Dictionary with no eviction.** The cache
grows unboundedly — every `UsePersisted` key stays in memory for the process
lifetime. There's no LRU eviction, no size limit, no TTL. For a long-running
desktop app where users navigate through many views, the cache accumulates stale
state from components that will never remount. SwiftUI's `@SceneStorage`
serializes to disk with OS-managed lifecycle. Compose's `rememberSaveable` ties
to the `SaveableStateRegistry` which is scoped to the composition. Reactor's cache
is a global singleton that only clears on process exit.

**4. PersistedStateCache keys are stringly-typed with no collision protection.**
`UsePersisted("scroll-pos", 0.0)` uses a bare string key. Two unrelated
components that happen to use the same key silently share state — a bug with no
diagnostic. The cache stores `object?`, so a type mismatch (one component persists
`int`, another reads `string` with the same key) produces a runtime
`InvalidCastException` on the next mount. There's no namespacing, no type
validation, no collision warning.

**5. No lazy-loaded / code-split components.** React's `React.lazy()` for
dynamically imported code-split components has no equivalent. Async *data* is
now handled via `UseResource` / `Pending` (see Section 3a), but async *code*
loading isn't a scenario Reactor addresses — a single .exe loads everything
at startup. SwiftUI and Compose also lack this, so Reactor is not uniquely
deficient on this axis.

**6. Component props are still untyped at the element level.** `ComponentElement`
stores props as `object?`. Props are set via `IPropsReceiver.SetProps(object props)`
with a cast. The generic `Component<T, TProps>(props)` factory hides this, but the
underlying infrastructure is type-erased. A wrong props type produces a runtime
`InvalidCastException`, not a compile error.

**7. No slots/named children convention.** The design spec mentions documenting
a convention for named children using Element-typed props, with `Lazy<Element>`
as future work. This hasn't materialized. Multi-slot composition (header + body +
footer) requires ad-hoc props types. SwiftUI has `@ViewBuilder` parameters for
multiple content slots. Compose has multiple `@Composable` lambda parameters.
Reactor has no convention, pattern, or framework support.

**8. FuncElement identity is still problematic.** `Func(ctx => ...)` creates a
`FuncElement` with a lambda. The lambda is recreated every render (it captures
outer state), so the function reference changes every time. `ShallowEquals` still
returns false for FuncElements, forcing an update. The new `Memo(ctx => ..., deps)`
provides a workaround — but `Func` remains the first thing developers reach for,
and it can't memo-skip.

**9. Context change detection compares boxed values with Equals.** The
`HasConsumedContextChanged` method compares `Equals(currentValue, ctxHook.LastValue)`
where both values are `object?`. For reference types that don't override `Equals`,
this is reference equality — meaning a new `ThemeConfig` record with the same
values still triggers re-renders unless the record type properly implements
`Equals`. C# records do implement value equality by default, so this works for
the common case. But a class-based context value type silently breaks the memo
optimization — every provide creates a new object, which is always "different"
by reference, defeating the point of memoization.

### Revised component model verdict

**Previously: Component Model C, Global State F, Local State B+.**
**Now: Component Model B+, Global State B+, Local State A-.**

The improvement is substantial and addresses the most critical gaps
identified in the previous review. The context system provides a real answer
to "how do I share state across the tree" — theming, auth, feature flags, and
localization all have a clean mechanism now. Default-on memoization means
components skip unnecessary re-renders without developer opt-in. Generic hook
state eliminates boxing. Post-render effect cleanup matches React's behavior.
Persisted state handles the unmount/remount scenario.

The grade is B+/B+ rather than A because of implementation concerns: reflection
for ShouldUpdate dispatch, boxing in the context scope stack, unbounded persisted
state cache, string-keyed persistence with no collision protection. These are
solvable problems — caching MethodInfo, using a generic scope entry, adding LRU
eviction — but they're real costs in the current implementation. The competition
doesn't have these particular issues: React's memo comparator is a direct
function reference, Compose's stability is compile-time, SwiftUI's environment
is type-safe throughout.

Local State moves to A- because the boxing is gone, effect cleanup timing is
correct, and persisted state exists. The remaining gap is the `object[]`
dependency array for `UseEffect`/`UseMemo` — dependencies are still boxed into
`object[]` and compared with `Equals`, which is the same issue React has (but
React has ESLint rules to catch common mistakes, and Reactor has no tooling).

---

## 3. State Management

### What Reactor has

- `UseState<T>` — React's useState equivalent, **now generic (no boxing)**
- `UseReducer<T>` — functional updater variant
- `UseReducer<TState, TAction>` — Redux-style reducer
- `UseEffect` — side effects with dependency tracking, **cleanup now post-render**
- `UseMemo<T>` — memoized computation
- `UseCallback` — stable callback reference
- `UseRef<T>` — mutable reference
- `UseObservable<T>` — INotifyPropertyChanged bridge
- `UseObservableTree<T>` — deep INotifyPropertyChanged bridge (recursive)
- `UseObservableProperty<T>` — single-property INotifyPropertyChanged bridge
- `UseCollection<T>` — ObservableCollection bridge
- `UseContext<T>` — **NEW** — reads nearest ancestor's context value
- `UsePersisted<T>` — **NEW** — state that survives unmount/remount
- `UseResource<T>` — **NEW** — async single-fetch with cache + ADT state (see Section 3a)
- `UseInfiniteResource<T>` — **NEW** — cursor-paginated async fetch (see Section 3a)
- `UseMutation<TIn,TOut>` — **NEW** — write-side async with invalidation (see Section 3a)
- `UseWindowSize` / `UseBreakpoint` — responsive hooks

### What's improved

**1. Hook state is now generic — no boxing.** Previously `HookState.Value` was
`object`, boxing every int/bool/double. Now `ValueHookState<T>` stores the value
directly. `EqualityComparer<T>.Default` handles comparison without boxing. This
is a clean fix for a real performance problem.

**2. Effect cleanup timing is now correct.** Previously, cleanup ran synchronously
during `UseEffect()` — inside the render phase. Now, `UseEffect` stores the old
cleanup as `PendingCleanup`, and `FlushEffects()` runs all pending cleanups in
Phase 1 before running new effects in Phase 2. This matches React's behavior:
cleanup from the previous render runs after the new render completes, not during
it. The fix is clean and the two-phase approach in FlushEffects is easy to reason
about.

**3. State persistence exists.** `UsePersisted<T>(key, initialValue)` stores
values in a static `PersistedStateCache` that survives unmount/remount. Values
are saved to cache during `RunCleanups()` (on unmount) and restored on next
mount. This closes the gap where navigating away and back lost all state.

**4. Global state exists via Context.** Covered in detail in Section 2. The
`UseContext<T>` hook reads tree-scoped ambient state provided by any ancestor.
The old "no global state at all" critique is resolved.

### Remaining critiques

**1. Dependency comparison still uses `object.Equals` on `object[]`.** `UseEffect`,
`UseMemo`, and other dependency-tracking hooks compare deps with
`Equals(prev[i], next[i])` where deps are `params object[]`. This means:
- Value types are boxed into the `object[]` dependency array (allocation)
- Reference types use reference equality by default unless they override Equals
- Collections as dependencies compare by reference, not by content
- No warning or guidance about what makes a good dependency

The hook state itself is now unboxed, but the dependency arrays still box. React
has the same issue (shallow comparison) but has ESLint rules that catch common
mistakes. Reactor has no tooling support.

**2. UseCallback is just UseMemo returning the same Action.** The implementation
is literally:

```csharp
public Action UseCallback(Action callback, params object[] dependencies)
{
    return UseMemo(() => callback, dependencies);
}
```

This doesn't actually stabilize the callback reference in the way React's
useCallback does. The `() => callback` lambda captures `callback`, so if
`callback` is a new closure each render (which it always is), the memoized
value is the closure captured at first render — meaning it has stale captures
unless the deps change. This is correct behavior but the documentation doesn't
explain this nuance.

**3. No batching control or transition API.** React 18 has `startTransition`
for marking non-urgent updates. Reactor batches via DispatcherQueue, which is
all-or-nothing. There's no way to say "this state update is low priority" or
"this update should not show a loading state."

**4. PersistedStateCache limitations.** Covered in Section 2 — unbounded growth,
string keys with no collision protection, no disk persistence. For a desktop
framework where window/session restoration is expected, in-memory-only persistence
is a partial answer. SwiftUI's `@SceneStorage` and Compose's `rememberSaveable`
both tie into the platform's state restoration lifecycle. Reactor's cache is process-
scoped only.

---

## 3a. Async Resources

### The async story: a major new subsystem landed in 72 hours

The previous version of this review lumped async under "state management" and
said: "React has had Suspense since 2018." In the last three days, Reactor
shipped a full async data subsystem — `AsyncValue<T>` ADT, `UseResource`,
`UseInfiniteResource`, `UseMutation`, `Pending` / `PendingScope`, a shared
`QueryCache`, focus revalidation, cache-entry subscription invalidation, a
DataGrid hook-paging path enabled by default, four hook-rule analyzers,
and ~15 self-host + framerate fixtures covering edit lifecycle, sequential
prefixes, and churn. The commit range is `3814def..9f2f5de` (PR #29, ~40
sub-commits). **The honest assessment: this is a principled, well-designed
subsystem that closes a genuine capability gap — but it is also brand-new
code with no field evidence, and the design decisions that differentiate it
from TanStack Query introduce their own tradeoffs.**

### What shipped

```csharp
// AsyncValue: discriminated union of Loading / Data / Error / Reloading.
// Pattern-match exhaustively.
var fruit = UseResource(async ct => await FetchFruitAsync(id, ct), id);
return fruit switch
{
    Loading<Fruit>    => Spinner(),
    Data<Fruit> d     => FruitCard(d.Value),
    Reloading<Fruit> r => FruitCard(r.Stale).Opacity(0.7),
    Error<Fruit> e    => ErrorBox(e.Exception.Message, retry: fruit.Retry),
    _                 => null
};

// Cursor-based infinite pagination (not offset-based)
var page = UseInfiniteResource(
    initialCursor: null,
    fetcher: async (cursor, ct) => await api.GetPageAsync(cursor, ct));
// page.Pages, page.HasMore, page.LoadMore()

// Mutations with optimistic invalidation
var save = UseMutation<Employee, Unit>(
    async (emp, ct) => await api.PatchAsync(emp, ct),
    invalidateKeys: ["employees.list", "employees.detail"]);
Button("Save", () => save.Trigger(current));
// save.IsRunning / save.Error / save.Result

// Suspense-style fallback regions
PendingScope(
    Spinner(),                // fallback while any UseResource inside is Loading
    Children(fruitView, listView))

// Cache invalidation by pattern
QueryCache.Default.Invalidate("employees.*");
```

Backing infrastructure: `QueryCache` with per-key locks, subscription
ref-counting, background eviction on a shared timer, TTL + pattern
invalidation, `EntryChanged` event; `DataPageCache` for infinite-pagination
chunk storage; `IDataSource<T>` adapter so `DataGrid` can consume the hook
path without changing its public API; offset-cursor short-circuit for
batched sequential loads; `UseHookBasedPaging` flag (default on as of commit
4ce1eba).

Four Roslyn hook-rule analyzers ship alongside:

| Analyzer | Severity | Detects |
|---|---|---|
| REACTOR_HOOKS_001 | Warning | Hook called conditionally (inside `if`/`while`/`?:`) |
| REACTOR_HOOKS_004 | Warning | Hook deps include a freshly allocated value (array, closure, record literal) |
| REACTOR_HOOKS_005 | Warning | Hook called outside a `Render()` override or a `Use*` method |
| REACTOR_HOOKS_006 | Info | `UseResource` fetcher name suggests a write (`Save*`, `Post*`, `Delete*`) — use `UseMutation` |

This is the hook-rule coverage the DX section previously criticized Reactor
for not having. The four rules correspond roughly to the top findings
`eslint-plugin-react-hooks` surfaces in a React codebase.

### What's actually good about this (credit where due)

**The ADT is the right shape.** `AsyncValue<T>` as a sealed record hierarchy
with `Loading` / `Data` / `Error` / `Reloading` is more expressive than
React's `{ isLoading, data, error }` tuple. The four-state design makes
stale-while-revalidate a type-level concept — a UI can render the stale
payload with reduced opacity while `Reloading<T>` carries the old value.
Riverpod's `AsyncValue` proves this pattern works; Reactor's adoption is
principled, not trend-chasing.

**Hook-owned cancellation is correct.** Every fetcher takes a
`CancellationToken` that the hook itself owns. Unmount cancels the token.
A component navigating away mid-fetch doesn't leak the in-flight request.
React's `useEffect`-based fetch patterns require developers to thread the
token themselves, which they frequently don't. Reactor forces the right
pattern.

**`QueryCache` is serious infrastructure.** Per-key `SemaphoreSlim` locks
prevent duplicate concurrent fetches for the same key. Subscription ref-counts
track how many live components observe each key. Background eviction runs on
a shared timer (not per-entry `Timer` allocations). TTL and pattern-based
invalidation (`Invalidate("employees.*")`) cover the common use cases. The
`EntryChanged` event lets mutations invalidate downstream readers without a
global re-render. This is the level of engineering a long-lived desktop app
cache needs.

**DataGrid integration is a real proof point.** Commit `49e1fe4` routes
`DataGrid` row-commit through `UseMutation`. Commit `f43684a` adds a hook-
paging path. Commit `4ce1eba` flips the default to on. This isn't a demo —
a production-sized control was migrated to the hook path and the tests pass.
If async resources were toy infrastructure, this migration would have
surfaced cracks; it didn't.

**`REACTOR_HOOKS_006` is a nice touch.** The analyzer reads the name of the
fetcher delegate passed to `UseResource` and warns if it starts with `Save`,
`Post`, `Put`, `Delete`, or contains "mutation" — nudging the developer
toward `UseMutation` instead. This is pit-of-success design: it catches the
most common conceptual mistake at edit time.

**Self-host fixtures cover real scenarios.** `PendingChurn` framerate
fixture, `DataGridEditMutation` fixture, placeholder-count parity for
sequential-prefix loads, edit-lifecycle parity under hook-based paging. This
is more thorough coverage at ship time than navigation or commanding got.

### What's still concerning (skeptic's view)

**1. `QueryCache` is in-process-only.** Cache is lost on app exit. For
desktop apps with session restoration expectations (dashboards that users
leave open for days), expensive computed data re-fetches from zero every
cold start. SwiftUI's `@SceneStorage` and Compose's `rememberSaveable` don't
solve this either, but TanStack Query has a documented persistence plugin.
Reactor ships the subsystem without a persistence story. The spec mentions
"serialize on evict" as future work.

**2. Cache keys are string-derived but not user-visible by default.** The
key is computed from `CallerHookId + DepsHash`. Two components calling the
same fetcher with identical deps get *different* cache entries unless they
override with an explicit `CacheKey`. This differs from TanStack Query,
where cache sharing is by explicit named key by default. Developers porting
TanStack Query mental models will be surprised when "the same query" fetches
twice because it runs in two sibling components. The right answer —
explicit `CacheKey` — requires reading the spec carefully.

**3. No automatic retry.** Network transient failures are common in
production. `UseResource` throws the error into `AsyncValue.Error<T>` with
zero retries by default. Spec 020 D10 defends this as intentional ("don't
hide bugs"), which is a defensible philosophical position but conflicts
with production reality. Every serious consumer will add a retry wrapper
locally, which means the framework is punting a design decision to
application code.

**4. `StaleTime` defaults to zero (always refetch on mount).** Conservative,
but costly. A `UsePersisted`-equivalent for `AsyncValue` (cache survives
unmount/remount with a configurable `StaleTime`) isn't the default — a
component that unmounts and remounts refetches unless the cache entry is
still live (ref-counted to zero, kept via TTL). The docs recommend thinking
about TTL per key, but most developers won't.

**5. `PendingScope` boxes context values.** The scope is delivered via
`Context<PendingScope?>` — and `ContextScope` stores values as `object?`.
Every `PendingScope` push is a boxing allocation. Section 2 flagged this
same issue for `Context<T>` generally; the async subsystem inherits it.

**6. `UseMutation.InvalidateKeys` is `string[]`, not structured.** Flat
strings. You cannot say "invalidate anything that depends on employee 42"
without writing `InvalidatePattern("employees.*42*")` yourself. Compared to
TanStack Query's `queryKey` arrays (which can be partial-matched), this is
coarser. It's consistent with the rest of the key design (flat strings), but
the consistency locks in a limitation.

**7. Performance at scale is untested.** The cache's eviction sweep is O(n)
per tick; per-key locks could bottleneck under concurrent churn; the
ref-count / subscription machinery hasn't been profiled with thousands of
live queries. PR #29 ships unit + selfhost tests but no stress/soak tests
against a realistic dashboard app. The previous review criticized Reactor
for the same pattern in other subsystems; async resources repeat it.

**8. `Pending` fallback has only one level of granularity.** A single
`PendingScope` covers all nested `UseResource` calls — there's no
per-query suspension boundary. React Suspense lets you nest `<Suspense>`
boundaries for different parts of a layout, so the header resolves
independently of the list body. Reactor's scope-based approach is coarser
and less composable.

**9. Focus revalidation exists but is new and unproven.** Commit `66b3cbb`
adds focus revalidation + `EntryChanged` subscriptions. This closes a gap
with TanStack Query's `refetchOnWindowFocus`. But "focus" on Windows desktop
is a different event model than web browsers — multi-window apps, background
windows, minimize/restore. The implementation is fresh and hasn't been
field-tested against the range of window-lifecycle scenarios WinUI exposes.

**10. The DataGrid hook-path flag is on by default but still feature-flagged.**
`UseHookBasedPaging` is a flag in headtrax (an internal product, per commit
`4ce1eba`). Flags that ship "on by default" usually persist as flags longer
than intended because nobody wants to commit to removing the escape hatch.
The two code paths (legacy DataPageCache vs. new hook-based) both have to be
maintained until the flag is removed, which accrues tech debt.

**11. No devtools surface for the cache.** TanStack Query Devtools shows
live cache state, query timings, mutation history, and lets you invalidate
keys manually — a major productivity boost when debugging async UI. Reactor
has no equivalent. The `reactor.state` MCP tool (Section 13a) exposes hook
shape but not cache state. Diagnosing "why is this query returning stale
data?" requires reading the source.

**12. The spec is 10 pages long and the concepts stack quickly.**
`AsyncValue<T>`, `Pending` element vs. `PendingScope`, `UseResource` vs.
`UseInfiniteResource` vs. `UseMutation`, cache-key derivation, TTL vs.
`StaleTime`, invalidation patterns, focus revalidation, optimistic updates,
and the four hook analyzers. The concept budget is real. A React developer
who's used TanStack Query will find most of this familiar, but someone
coming in fresh has a steep climb. The `async-resources-cookbook` doc helps,
but the API surface is large for a framework that aims at approachability.

### Revised async resources verdict

**Previously graded under State Management: no async story. Now: B+.**

Async resources is a substantial new capability that closes a genuine gap.
The ADT design, the `QueryCache` infrastructure, the DataGrid integration,
and the four hook analyzers are principled and well-executed. The grade is
B+, not A, because: the subsystem is brand-new with no field evidence;
cache is in-process-only (no persistence); no automatic retry; performance
at scale is untested; no devtools; and the concept count is high relative
to the competition's simpler `isLoading`/`data` tuples.

The competition comparison is closer than the rest of Reactor's feature
areas. React has TanStack Query / SWR — battle-tested, with persistence
adapters, devtools, optimistic updates, retry policy, prefetching, and a
multi-year stability track record. SwiftUI has `async let` / `.task` but no
cache. Compose has nothing standardized (developers use Coroutines + StateFlow
ad-hoc). Reactor's position is above Compose (nothing shipped), near SwiftUI
(comparable primitives, no cache), and behind React's ecosystem (mature
tooling). For a feature this new, B+ reflects promise more than proven
value.

---

## 4. The Reconciler

### Architecture

The reconciler follows React's model: diff old and new element trees, apply
minimal patches. It's split across `Reconciler.cs` (orchestration + component
lifecycle + memo checks + context scope), `Reconciler.Mount.cs` (~40 mount
handlers), `Reconciler.Update.cs` (~30 update handlers), and `ChildReconciler.cs`
(keyed/unkeyed lists).

The reconciler now owns the `ContextScope` and drives context push/pop during
tree traversal — elements with `ContextValues` push on mount/update entry and
pop on exit. Components receive the scope via `BeginRender(requestRerender,
contextScope)` so their `UseContext` hooks read from the correct scope position.
The component update path includes a memo check that combines props comparison
(`ShouldUpdate`) with context change detection (`HasConsumedContextChanged`).

### Critiques

**1. Massive switch/dispatch for every element type.** The Mount and Update
methods are giant type-based dispatches. Adding a new element type requires
modifying both `Mount()` and `Update()` — a violation of the open/closed
principle. The `RegisterType<>` extensibility API exists but the built-in types
don't use it; they're hardcoded switch arms. This means the reconciler is a
monolithic class with ~70+ individual handler methods across its partial files.

React's reconciler is element-type-agnostic — it calls `createElement` and the
component decides how to render. SwiftUI's diffing is automatic via the View
protocol. Compose's compiler plugin handles recomposition transparently. Reactor's
reconciler is essentially a hand-maintained mapping table from element types to
WinUI control operations.

**2. The Tag-based event dispatch is a fragile workaround.** WinUI controls
have a `Tag` property (type `object`) intended for user data. Reactor repurposes it
to store the current element, so event handlers can read fresh state:

```csharp
// At mount time: wire event handler once
button.Click += (sender, _) => {
    var el = GetElementTag<ButtonElement>(sender);
    el?.OnClick?.Invoke();
};

// At update time: update the tag to point to the new element
SetElementTag(button, newElement);
```

This is clever but fragile:
- If anything else sets `Tag` (e.g., WinUI internals, user code via `.Set()`),
  event handlers silently break with no error
- It prevents users from using `Tag` for their own purposes
- It couples event dispatch to a WinUI implementation detail that could change
- There's a race condition: if an event fires between `Tag` being cleared and
  updated during reconciliation, the handler gets `null`

**3. Element Pool only handles non-interactive controls.** The element pool
recycles unmounted controls: TextBlock, StackPanel, Grid, Border, ScrollViewer,
Canvas, Image. But interactive controls (Button, TextBox, etc.) are NOT pooled
because "resetting their event state safely is more complex." In a real app,
interactive controls are the majority of the UI. This means the pool optimizes
the cheap case (layout containers) while leaving the expensive case (controls
with event subscriptions and visual state) unoptimized.

**4. ShallowEquals is conservative to the point of being useless.** The
`Element.ShallowEquals` optimization returns `false` for any element that has
Setters, event handlers, or is of an unknown type. Since virtually every
interactive element has at least one event handler, ShallowEquals will return
false for all of them, meaning the optimization only helps for static text and
images. The method explicitly says "Conservative: returns false for unknown
element types" — which means custom element types never benefit.

**5. Every component adds a hidden Border to the visual tree.** The reconciler
wraps every `Component` and `FuncElement` in a WinUI `Border` as an identity
anchor:

```csharp
var wrapper = new Border { Child = childControl };
_componentNodes[wrapper] = new ComponentNode { ... };
```

This means the WinUI visual tree is NOT 1:1 with the Reactor element tree. A
component that renders a single `Text("Hello")` actually produces
`Border > TextBlock`. In a deeply nested component tree (which is how
well-structured Reactor apps should look), you accumulate invisible Borders at
every component boundary. These are not free — each Border participates in
WinUI's measure/arrange layout cycle. React's Fiber architecture, SwiftUI's
view protocol, and Compose's slot table all avoid this overhead.

**6. The element pool uses a ForceDetach hack.** Returning a control to the pool
requires round-tripping it through a scratch `StackPanel`:

```csharp
_scratchPanel ??= new StackPanel();
_scratchPanel.Children.Add(element);    // Re-parent
_scratchPanel.Children.Remove(element);  // Detach
```

WinUI has internal parent tracking that doesn't fully release on logical removal.
Without this workaround, re-parenting a pooled control causes `COMException`.
This is the kind of hack that works until a WinUI update changes the internal
behavior, and it betrays how much the framework is fighting the platform rather
than working with it.

**7. No concurrent/interruptible rendering.** React 18's concurrent mode allows
rendering to be interrupted and resumed, preventing long renders from blocking
user interaction. Reactor's render cycle is fully synchronous and runs to
completion. For complex UIs with many components, a state change triggers a full
synchronous re-render of the entire dirty subtree, which blocks the UI thread.
There is no mechanism to yield back to the event loop mid-render, prioritize
urgent updates, or time-slice work.

---

## 5. Layout System

### What Reactor has

- `VStack` / `HStack` (StackPanel)
- `Grid` with string-based column/row definitions
- `Canvas` with absolute positioning
- `RelativePanel` with named references
- `FlexPanel` (CSS Flexbox via Yoga port) — **Reactor-exclusive**
- `WrapGrid` (VariableSizedWrapGrid)
- `ScrollView`
- `Border`

### Critiques

**1. Grid definitions are stringly-typed.** `Grid(["*", "Auto", "200"], ["*"])`
uses string arrays. A typo like `"Atuo"` silently fails at runtime. SwiftUI uses
`GridItem(.flexible())`, `GridItem(.fixed(200))`. Even WinUI's own XAML
validates `ColumnDefinition` values at parse time. Reactor's pure-string approach
is a regression in type safety.

**2. No Spacer equivalent.** SwiftUI's `Spacer()` is one of its most-used
layout tools — a flexible element that expands to fill available space. Reactor has
no equivalent. You'd need to use `.HAlign(HorizontalAlignment.Stretch)` or Flex
layout with `grow`, neither of which is as intuitive as `Spacer()`.

**3. FlexPanel is impressive but duplicates WinUI's layout system.** Reactor
ships a full CSS Flexbox implementation via a Yoga port. This is a significant
engineering effort that creates a parallel layout system alongside WinUI's
native layout. It means:
- Two layout systems with different mental models
- Flex children can't participate in Grid layout and vice versa
- Performance overhead of running Yoga's layout algorithm on top of WinUI's
- Debugging layout issues requires understanding which system is in play

SwiftUI and Compose don't have this problem because they own the entire layout
pipeline. Reactor bolted Flexbox onto a platform that has its own (different) layout
model.

A notable recent addition: the skills/design docs now **prefer FlexPanel over
StackPanel** for layout work (commit `791f380`), which means the framework's
own guidance is steering developers toward the parallel layout system rather
than WinUI-native. Whether that's the right call depends on whether FlexPanel
is performant enough to be the default — a claim the docs make but that no
profiling in the repository actually supports.

**3a. FlexPanel measure had a CSS-semantics bug that was just fixed.** Commit
`397f274` (April 19) rewrote `FlexPanel.cs` to "align measure with CSS block-
level semantics" — 60 lines added, 45 removed, no commit-message detail. The
companion test file (`FlexPanelCssBehaviorFixtures.cs`, 526 lines) is a
behavioral parity suite against `WebViewCssMeasurement` (CSS measured via an
embedded WebView2). That's a strong testing pattern — pinning behavior
against a real browser's layout engine — but it also tells you something
uncomfortable: until this landed, FlexPanel's measure was *not* CSS-
equivalent, and there was no mechanism to detect when it drifted. The fact
that CSS-compliance now has a 526-line fixture file is the right fix; the
fact that it wasn't there from day one is a process gap. How many existing
Reactor apps have subtly wrong FlexPanel layouts they didn't notice?

**4. No safe area or inset handling.** WinUI desktop apps have title bars,
task bars, and potentially custom chrome. Reactor has no `SafeArea` concept. SwiftUI
has `.ignoresSafeArea()` and `.safeAreaInset()`. Compose has `WindowInsets`. Reactor
developers must manually account for title bar height and other insets.

**5. Responsive layout is hook-based, which forces full re-renders.**
`UseWindowSize()` and `UseBreakpoint()` trigger a full component re-render
when the window resizes. SwiftUI's `@Environment(\.horizontalSizeClass)` only
invalidates views that read it. Compose's `BoxWithConstraints` only recomposes
the contained scope. Reactor's approach means resizing a window re-renders the
entire component tree that uses any responsive hook.

---

## 6. Styling and Theming

### The theming story: from functional-with-caveats to a real styling system

The previous version of this review graded theming at C+ — the ThemeRef system
closed the P0 blocker but had performance concerns (XamlReader.Load per element
per render), a narrow API surface (3 properties, no custom resources, no hooks),
no guardrails against hard-coded colors, and no lightweight styling. A focused
styling diff has landed that addresses several of these criticisms directly:
style caching, `.RequestedTheme()` modifier, `UseColorScheme` hook, lightweight
styling via `ResourceBuilder`, and three Roslyn analyzers. The honest assessment
now: **theming and styling have crossed from "functional for the common case"
to a genuinely capable system with one unique differentiator (lightweight
styling), but with a UseColorScheme implementation bug, still no custom theme
resources, and the ThemeRef/lightweight styling interaction story unresolved.**

### What shipped previously (ThemeRef foundation)

**ThemeRef tokens and modifier overloads.** ~40 semantic theme tokens
(`Theme.PrimaryText`, `Theme.Accent`, `Theme.CardBackground`, etc.) and
`Theme.Ref("AnyResourceKey")` for custom WinUI resources. Three-tier model:
unstyled elements theme automatically (Tier 3), theme tokens resolve reactively
(Tier 2), local concrete values override (Tier 1).

**Reconciler integration via XAML Style injection.** `ApplyThemeBindings`
constructs `{ThemeResource}` styles and assigns them to elements. WinUI handles
theme resolution natively.

### What shipped in the styling diff

**1. Style caching (Proposal 5 — P0 performance fix).** This directly addresses
the biggest criticism from the previous review: XamlReader.Load per element per
render. The implementation:

```csharp
private static readonly ConcurrentDictionary<string, Style> _styleCache = new();

private static string BuildCacheKey(string targetType, IReadOnlyDictionary<string, ThemeRef> bindings)
{
    var sortedKeys = bindings.Keys.ToArray();
    Array.Sort(sortedKeys, StringComparer.Ordinal);
    var sb = new StringBuilder(targetType);
    foreach (var key in sortedKeys)
        sb.Append('|').Append(key).Append('=').Append(bindings[key].ResourceKey);
    return sb.ToString();
}
```

Cache hit path: immediate Style assignment, zero XAML parsing. Cache miss:
build XAML → parse → store. `ApplyStyleToElement()` does a clever null-then-set
to force WinUI re-evaluation of `{ThemeResource}` setters when the same cached
Style reference is reapplied (without this, assigning the same reference is a
no-op). `ClearStyleCache()` runs on theme change as conservative memory cleanup.

This is well-engineered. The key generation is deterministic (sorted by
`StringComparer.Ordinal`), thread-safe (`ConcurrentDictionary`), and handles
dictionary enumeration order correctly. For a grid of 200 elements all using
`Theme.Accent`, `XamlReader.Load` fires once, not 200 times. The previous
review's primary performance critique is resolved.

**2. `.RequestedTheme()` modifier (Proposal 8A — P0 API gap).** Previously
required `.Set(b => b.RequestedTheme = ElementTheme.Dark)`. Now:

```csharp
VStack(children).RequestedTheme(ElementTheme.Dark)
```

The reconciler applies `RequestedTheme` **before** `ApplyThemeBindings` — the
ordering comment in the code is explicit about why this matters. The modifier
is clean: one property on `ElementModifiers`, one extension method, one line
in `ApplyModifiers` with a change guard. This is a textbook example of turning
a `.Set()` workaround into a first-class modifier — small scope, correct
integration, no surprises.

**3. `UseColorScheme` hook (Proposal 6 — P0 reactive hook).** Previously there
was no way to read the current theme in render logic. Now:

```csharp
var scheme = ctx.UseColorScheme();   // Light, Dark, or HighContrast
var isDark = ctx.UseIsDarkTheme();   // convenience wrapper
```

`ColorScheme` is a clean three-value enum. `ColorSchemeContext` handles the
`ElementTheme` → `ColorScheme` mapping with High Contrast detection via
`AccessibilitySettings.HighContrast`. The hook re-evaluates on every render, so
theme changes (which trigger a full re-render via ReactorHost) are picked up
naturally.

**4. Lightweight Styling via ResourceBuilder (Proposal 2 — P0 unique
differentiator).** This is the most significant feature in the diff. WinUI's
"lightweight styling" lets you override specific theme resource keys per-control
(e.g., `ButtonBackground`, `ButtonBackgroundPointerOver`) without replacing the
control template. The VisualStateManager continues to work, so hover/pressed/
disabled states respect the overrides automatically. Reactor now surfaces this:

```csharp
Button("Submit").Resources(r => r
    .Set("ButtonBackground", "#0078D4")
    .Set("ButtonBackgroundPointerOver", "#106EBE")
    .Set("ButtonBackgroundPressed", "#005A9E")
    .Set("ButtonForeground", "#FFFFFF"))

// Theme-reactive resource overrides
Button("Go").Resources(r => r
    .Set("ButtonBackground", Theme.Accent)
    .Set("ButtonBackgroundPointerOver", Theme.Ref("AccentButtonBackgroundPointerOver")))
```

`ResourceBuilder` separates literal values from `ThemeRef`-based entries.
`ResourceOverrides` is an immutable snapshot record. The reconciler's
`ApplyResourceOverrides()` tracks which keys Reactor has set via a
`ConditionalWeakTable<FrameworkElement, HashSet<string>>` — ensuring cleanup
only removes Reactor-managed keys, never interfering with XAML-set resources.
On update, old keys not present in the new overrides are removed. `ThemeRef`-
based resources are re-resolved on theme change via the re-render pipeline.

**No other C# declarative framework surfaces this.** React's CSS custom
properties are conceptually similar but operate at a different abstraction level.
SwiftUI's `.environment(\.font)` and Compose's `LocalContentColor` provide
ambient overrides but not per-control resource key targeting. Reactor's
`ResourceBuilder` gives developers access to the exact same resource override
mechanism that WinUI's XAML lightweight styling uses — but through a fluent
builder instead of raw ResourceDictionary manipulation.

**5. Three Roslyn Analyzers.** A separate `Reactor.Analyzers` project provides
static analysis:

| Analyzer | Severity | Detects | Suggests |
|---|---|---|---|
| REACTOR_THEME_001 | Warning | `.Background("#FFFFFF")` hard-coded colors | Use `Theme.*` tokens |
| REACTOR_THEME_002 | Info | `.Set(b => b.Background = brush)` on known controls | Use `.Resources()` lightweight styling |
| REACTOR_THEME_003 | Info | `.Set(fe => fe.RequestedTheme = ...)` pattern | Use `.RequestedTheme()` modifier |

Each has matching unit tests via `CSharpAnalyzerVerifier`. REACTOR_THEME_001 includes a
`UseThemeRefCodeFix` that maps known colors to tokens (`#FFFFFF` → `Theme.PrimaryBackground`,
`#0078D4` → `Theme.Accent`). REACTOR_THEME_003 has `RequestedThemeSetCodeFix` for
the `.Set()` → `.RequestedTheme()` transformation.

**Naming consistency (resolved).** Spec 018 renamed the framework from
"Duct" to "Microsoft.UI.Reactor". The initial rename commit migrated
accessibility and localization diagnostics (`REACTOR_A11Y_*`, `REACTOR_LOC001`)
but left the three theming analyzers on their pre-rename IDs. The pre-launch
cleanup completed the rename — all three are now `REACTOR_THEME_001`/
`REACTOR_THEME_002`/`REACTOR_THEME_003`. A consumer's `.editorconfig` only
needs the `REACTOR_*` prefix to suppress all Reactor diagnostics.

### What's actually good about this (credit where due)

**Style caching is the right fix for the right problem.** The previous review
called out XamlReader.Load-per-element as the heaviest possible implementation.
The cache eliminates repeated parses for identical binding sets — which covers
the vast majority of real-world usage (lists of identically-themed controls).
The implementation is clean: deterministic keys, thread-safe concurrent
dictionary, correct null-then-set for Style reapplication. The cache is
conservative — cleared on theme change even though `{ThemeResource}` setters
self-resolve. This is defense in depth, not a correctness requirement.

**Lightweight styling is Reactor's first genuinely unique styling feature.** Every
other styling feature (ThemeRef, RequestedTheme, UseColorScheme) is Reactor
catching up to what the competition provides natively. `ResourceBuilder` is
different — it surfaces a WinUI capability that no other declarative framework
wraps. A developer who writes `.Resources(r => r.Set("ButtonBackgroundPointerOver",
"#106EBE"))` gets hover/pressed/disabled states that respect the override, with
zero template replacement. This is the correct abstraction level: give
developers the resource key names (which are documented in WinUI docs), handle
the dictionary plumbing, track cleanup.

**The ConditionalWeakTable for managed-key tracking is smart.** The alternative
approaches (Tag, attached property, side dictionary keyed by identity) all have
GC or lifecycle problems. `ConditionalWeakTable` ties the tracking data's
lifetime to the FrameworkElement's GC lifetime, avoiding both leaks and
dangling references. When the element is collected, the tracking set goes with
it.

**The analyzers close the "pit of success" gap.** The previous review criticized
hard-coded colors as a trap with no guardrails. REACTOR_THEME_001 now warns at edit time
when a string literal is passed to `.Background()`, `.Foreground()`, or
`.WithBorder()`. It's not perfect (see concerns below), but it's the difference
between "no guidance" and "IDE squiggles on the suspicious line."

**RequestedTheme ordering is explicitly documented.** The comment at
Reconciler.cs:1615 says exactly why `RequestedTheme` must be set before
`ApplyThemeBindings`. This is the kind of ordering invariant that causes subtle
bugs when someone later refactors `ApplyModifiers`. The comment makes the
constraint visible.

### What's still concerning (skeptic's view)

**1. UseColorScheme reads the app-level theme, not the element's effective
theme.** This is a bug, not a design limitation. The implementation:

```csharp
public ColorScheme UseColorScheme()
{
    var theme = Microsoft.UI.Xaml.Application.Current?.RequestedTheme;
    // ...maps to ColorScheme...
}
```

The doc comment says "Automatically reflects [...] per-element RequestedTheme
overrides." The code reads `Application.Current.RequestedTheme` — the global
app theme. A component inside a `.RequestedTheme(ElementTheme.Dark)` subtree
will see `ColorScheme.Light` when the system is in Light mode. The spec
(Proposal 6, Option A) says to read `FrameworkElement.ActualTheme` from the
component's mounted control. The implementation doesn't do this.

This is worse than missing the feature entirely, because the API exists, the
doc comment claims it works, and the `StylingGallery` sample demonstrates it.
A developer who writes:

```csharp
var scheme = ctx.UseColorScheme();
VStack(
    scheme == ColorScheme.Dark ? Text("🌙") : Text("☀")
).RequestedTheme(ElementTheme.Dark)
```

expects the moon icon. They get the sun. The hook re-evaluates on system theme
change (via ReactorHost re-render), so it "works" at the app level — but the
per-element awareness that makes it useful alongside `.RequestedTheme()` is
broken. The test suite doesn't catch this because `ColorSchemeTests` test the
enum mapping, not the `RequestedTheme` interaction.

SwiftUI's `@Environment(\.colorScheme)` correctly reads the effective color
scheme at the view's position in the hierarchy, including any
`.preferredColorScheme(.dark)` overrides from ancestors. Compose's
`isSystemInDarkTheme()` also reads from the local composition context. Reactor's
hook reads the global.

**2. ThemeRef `{ThemeResource}` in dynamically-loaded Styles doesn't respect
per-element RequestedTheme.** The code itself documents this (Reconciler.cs:1970):
"Note: {ThemeResource} in dynamically-loaded Styles resolves against the app
theme, not per-element RequestedTheme overrides." This is a WinUI platform
behavior — `XamlReader.Load()` parses XAML in the app's default theme context,
not the element's local theme context. The `ApplyStyleToElement` null-then-set
trick forces WinUI to reprocess the Style assignment, which helps with system
theme changes, but doesn't bridge the gap for per-element `RequestedTheme`.

This means: `.RequestedTheme(ElementTheme.Dark)` correctly sets the native
`FrameworkElement.RequestedTheme` property, so WinUI's built-in control theming
works (buttons, text, etc. render in dark theme). But ThemeRef bindings on that
element (`.Background(Theme.CardBackground)`) resolve against the *app* theme,
not the element's requested theme. A dark sidebar with
`.Background(Theme.CardBackground)` in a Light-mode app may get the Light
variant of `CardBackground`, not the Dark one.

The workaround documented in the code ("rely on native WinUI control theming
instead of ThemeRef bindings") is valid — WinUI controls apply their own theme
resources based on `RequestedTheme`. But it means `.RequestedTheme()` +
`Theme.*` tokens don't compose as a developer would expect. The two features
work independently but not together.

**3. Lightweight styling resource keys are stringly-typed with no validation.**
`ResourceBuilder.Set("ButtonBackgrnd", "#0078D4")` — typo in the key —
silently sets a resource that no control reads. There's no compile-time
validation of resource key names, no IntelliSense for known keys, no runtime
warning when a key doesn't match any control template. WinUI's own XAML
suffers the same problem (lightweight styling keys are strings everywhere),
but Reactor had the opportunity to provide type-safe constants:

```csharp
// What could exist (from the deferred items list):
Button("Submit").Resources(r => r
    .Set(ButtonResources.Background, "#0078D4")
    .Set(ButtonResources.BackgroundPointerOver, "#106EBE"))
```

The design spec lists "ButtonResources.Background constants" as future work.
Without them, `ResourceBuilder` inherits all the discoverability problems of
WinUI's stringly-typed XAML lightweight styling — you need to look up the
correct resource key names in the WinUI documentation. A framework that wraps
the platform should improve on this.

**4. Lightweight styling ThemeRef resources resolve at application level, not
element level.** `ApplyResourceOverrides` calls `ThemeRef.Resolve(resourceKey,
fe)`, which reads the effective theme for the element via `GetEffectiveThemeName`.
This is better than `UseColorScheme` (it does check the element). But the
resolved brush is a *snapshot* — a concrete `Brush` set into `fe.Resources`.
It's not a live `{ThemeResource}` binding. If the system theme changes, the
re-render pipeline calls `ApplyResourceOverrides` again and re-resolves. But
between renders, the resource is a frozen brush, not a theme-reactive binding.
For ThemeRef entries in `ResourceBuilder`, this means:

- System theme changes: re-resolved on next render (correct, with one-frame
  delay)
- Per-element RequestedTheme: resolved correctly at mount time (good — it uses
  `GetEffectiveThemeName(fe)`)
- VisualStateManager transitions: the resource is a frozen brush, not a
  `{ThemeResource}`. VSM state changes that reference the resource key will
  pick up the Reactor-set brush, but it won't adapt to subsequent theme changes
  *between renders*

For the common case (set once, re-resolve on system theme change), this works.
For the edge case (rapid theme toggling or theme-dependent VSM transitions),
the snapshot model has a one-render-cycle delay.

**5. No custom theme resource definitions (unchanged).** `Theme.Ref("key")`
references existing WinUI resources, but there's no way to define new theme
resources from Reactor. No branded colors that adapt to light/dark, no app-specific
semantic tokens. This was the most frequently cited remaining gap in the previous
review and it's still unaddressed. React's Material UI `createTheme()`, SwiftUI's
asset catalog named colors, and Compose's `lightColorScheme()`/`darkColorScheme()`
all provide this. Reactor still only references the platform's built-in palette.

The lightweight styling `ResourceBuilder` makes this more pressing, not less.
A developer who discovers they can override `ButtonBackground` with a brand
color will immediately want a brand color that adapts to light/dark — which
requires custom theme resources. The feature that should unblock this (custom
theme definitions) is the feature that's still missing.

**6. Only three properties support ThemeRef bindings (unchanged).** Background,
Foreground, and BorderBrush via `GetDependencyPropertyName()`. No Fill/Stroke
on shapes, no PlaceholderForeground, no CaretBrush. The lightweight styling
`ResourceBuilder` partially compensates — you can override resource keys for
any property — but ThemeRef bindings on the element itself remain limited to
three.

**7. Style assignment still clobbers existing styles.** `ApplyThemeBindings`
does `fe.Style = cachedStyle` (via `ApplyStyleToElement`). The caching
eliminates the performance cost of building duplicate styles, but the
architectural concern remains: every theme-bound element gets a dynamically
assigned style that replaces whatever style WinUI would have naturally applied.
This interacts poorly with `.ApplyStyle("AccentButtonStyle")` and implicit
styles. The cache means this is now a *correctness* concern (style precedence)
rather than a *performance* concern.

**8. REACTOR_THEME_002 analyzer coverage is shallow.** The `UseLightweightStylingAnalyzer`
only knows about 3 properties (Background, Foreground, BorderBrush) and 6
control types (Button, ToggleButton, RepeatButton, SplitButton, AppBarButton,
HyperlinkButton). Missing: TextBox, CheckBox, ComboBox, Slider, ToggleSwitch.
Missing properties: PlaceholderForeground, BorderThickness, CornerRadius.
Missing resource keys: anything beyond the Button family. The analyzer detects
a fraction of the cases where lightweight styling would be beneficial. It's
better than nothing — but it won't nudge a developer who's using `.Set()` on a
TextBox to set its placeholder foreground, which is one of the most common
lightweight styling use cases.

**9. REACTOR_THEME_001 color-to-token mapping is incomplete.** The `UseThemeRefAnalyzer`
maps 5 specific colors (#FFFFFF, white, #000000, black, #0078D4) to theme
tokens. Every other hard-coded color gets a generic "use ThemeRef" message with
no specific token suggestion. In practice, developers use dozens of colors —
grays (#E5E5E5, #808080), blues (#005A9E, #106EBE), reds (#D13438), greens
(#107C10). None of these map. The analyzer is useful for the simplest cases but
doesn't provide actionable guidance for the majority of real-world hard-coded
colors.

### Revised theming verdict

**Previously: C+. Now: B-.**

The improvement is meaningful and addresses the right problems. Style caching
eliminates the XamlReader.Load performance concern that was the #1 critique.
Lightweight styling provides a genuinely unique capability. RequestedTheme
modifier removes a `.Set()` workaround. The analyzers provide static guidance
where none existed.

But the grade is B-, not B, because of the `UseColorScheme` bug (reads app
theme, not element effective theme), the `ThemeRef` + `RequestedTheme`
interaction gap (dynamically-loaded styles don't respect per-element theme),
missing custom theme resource definitions, stringly-typed resource keys with
no validation, and shallow analyzer coverage. The two most impactful new
features — RequestedTheme and UseColorScheme — don't compose correctly with
each other, which is exactly the scenario they were designed for (dark sidebar
in a light app with reactive rendering).

The competition has moved too. SwiftUI's styling story (semantic colors +
`@Environment(\.colorScheme)` + asset catalog + `.tint()` + `.preferredColorScheme()`)
is deeply integrated and consistent. Compose's Material 3 (`lightColorScheme` /
`darkColorScheme` / `dynamicColorScheme`) provides full custom theming with
one API surface. Reactor's styling is now *functional* across more scenarios,
but the pieces don't always compose cleanly with each other.

### Other styling issues

**No style composition or reuse (partially addressed).** The design spec notes
that `Func<T, T>` extension methods serve as style bundles in practice. This is
valid C# — `static T BrandButton<T>(this T el) where T : Element => el
.Background(Theme.Accent).Foreground(Theme.PrimaryBackground)` — and is
documented in the styling spec. No new framework-level concept was added, which
is the right call: plain C# extension methods compose naturally with modifiers.

**Lightweight styling partially replaces the need for style composition.**
`.Resources()` lets a developer customize hover/pressed/disabled appearance
without needing to compose multiple modifiers. For the "brand button" pattern,
lightweight styling is strictly better than modifier chaining because it
preserves VisualStateManager integration.

**ApplyStyle is still string-based runtime lookup (unchanged).**
`.ApplyStyle("AccentButtonStyle")` does a dictionary lookup in
`Application.Current.Resources` at runtime. A typo silently fails.

---

## 7. Navigation

### The navigation story: from "architecturally blocked" to competitive and maturing

The previous version of this review scored Navigation at F — "Roll your own
string-based switch statement." A comprehensive navigation system shipped
previously, and a follow-up diff has added: destination-side guards
(`NavigatingToContext.Cancel()`), deep link enhancements (query strings, optional
params, wildcards), `NavigationDiagnostics` for observability, thread-safe cache,
configurable transition distance, and exposed `INavigationHandle`. Additionally,
a critical test infrastructure discovery was made: 44 of 46 E2E tests were
invisible behind a broken test filter (`ClassName=InteractiveTests` instead of
`ClassName!=SelfTestBatch`). This has been fixed — 43 Appium tests now pass across
6 test classes. The honest assessment now: **navigation is architecturally strong,
competitively mature, and now has real E2E validation across the full pipeline.**

### What Reactor has now

```csharp
// Define routes as C# records — type-safe, serializable, pattern-matchable
record HomeRoute;
record DetailRoute(int Id);
record SettingsRoute;
record ProfileRoute(string UserId, string? Tab = null);

// Root: create navigation stack with initial route
var nav = UseNavigation<AppRoute>(initial: new HomeRoute());

// Child: retrieve ancestor's handle via Context
var nav = UseNavigation<AppRoute>();

// Navigate with full type safety
nav.Navigate(new DetailRoute(42));
nav.GoBack();
nav.GoForward();
nav.Replace(new SettingsRoute());
nav.Reset(new HomeRoute());       // clear stack
nav.PopTo(r => r is HomeRoute);   // pop until match

// Render current route
NavigationHost(nav, route => route switch {
    HomeRoute => Component<HomePage>(),
    DetailRoute d => Component<DetailPage, int>(d.Id),
    SettingsRoute => Component<SettingsPage>(),
    _ => Text("Unknown route")
}) with {
    Transition = NavigationTransition.Slide(),
    CacheMode = NavigationCacheMode.Enabled,
    CacheSize = 10
}

// Lifecycle hooks with navigation guards (source AND destination)
UseNavigationLifecycle(
    onNavigatedTo: ctx => LoadData(ctx.Route),
    onNavigatingFrom: ctx => { if (hasUnsavedChanges) ctx.Cancel(); },
    onNavigatingTo: ctx => { if (!IsAuthorized(ctx.Route)) ctx.Cancel(); },  // NEW
    onNavigatedFrom: ctx => Cleanup()
);

// State serialization
var json = nav.GetState();   // full stack to JSON
nav.SetState(json);          // restore from JSON

// Deep linking (enhanced: query strings, optional params, wildcards)
var deepLinks = new DeepLinkMap<AppRoute>()
    .Map("/detail/{id:int}", args => new DetailRoute(args.Get<int>("id")))
    .Map("/profile/{userId}/{tab?}", args =>                                 // NEW
        new ProfileRoute(args.Get<string>("userId"), args.GetOrDefault<string>("tab")))
    .Map("/docs/**", args => new DocsRoute(args.GetWildcard()))              // NEW
    .Map("/settings", _ => new SettingsRoute());
// Query string access: args.Query<int>("page"), args.QueryString("search")  // NEW

// NavigationView integration
NavigationView(menuItems,
    NavigationHost(nav, routeMap)
).WithNavigation(nav, routeToTag, tagToRoute)
```

### What the competition provides now (2026 update)

**React Router v7** (stable, Nov 2024+): Three operating modes (declarative,
data, framework). Type safety via code generation (`npx react-router typegen`).
View Transitions API integration. Data loaders/actions for SSR. **But**: the
back stack is opaque — developers interact via `useNavigate()`, not by owning
the stack. Type safety is bolt-on (code-generated `.d.ts`), not intrinsic.

**TanStack Router** (v1.0 via TanStack Start, March 2026): 100% type-safe
routing with full inference — no code generation. Search params as typed state.
This is now the type-safety gold standard in the React ecosystem.

**SwiftUI NavigationStack** (iOS 16+, refreshed WWDC 2025): `NavigationPath`
is developer-owned and `Codable` (serializable). `navigationDestination(for:)`
maps types to views. WWDC 2025 added "Liquid Glass" visual refresh and improved
deep-linking. **But**: `NavigationPath` is type-erased — you can append and
count, but can't inspect or modify entries by type without workarounds.

**Compose Navigation 3** (stable Nov 2025 — **the biggest competitive shift
since the last review**): Ground-up rewrite that mirrors Reactor's philosophy
almost exactly. Developer-owned back stack (`SnapshotStateList<T>`). Type-safe
routes via `@Serializable` data classes. `NavDisplay` observes the list and
renders content. `Scene`/`SceneStrategy` for adaptive multi-pane layouts
(list-detail on wide screens). **This is now the direct competitor to Reactor's
navigation model.**

| Capability | Reactor | React Router v7 | SwiftUI | Compose Nav3 |
|---|---|---|---|---|
| Type-safe routes | Native (C# records) | Codegen | Native (Swift types) | Native (@Serializable) |
| Developer-owned back stack | Yes (IReadOnlyList) | No (opaque) | Partial (type-erased) | Yes (SnapshotStateList) |
| GPU-accelerated transitions | Yes (composition layer) | View Transitions API | System-managed | Basic |
| Lifecycle guards (cancel nav) | Yes (source + destination) | useBlocker() | beforeRemove | Developer-managed |
| Page caching (LRU) | Yes | N/A | N/A | Developer-managed |
| State serialization | Yes (JSON) | N/A | Codable | Developer-managed |
| Deep linking | Yes (pattern matching) | Built-in (URL) | Built-in (URI) | Developer-managed |
| Adaptive multi-pane | No | N/A (web) | Limited | Yes (SceneStrategy) |
| Nested navigation | Yes (independent stacks) | Yes | Yes | Yes |
| NavigationView chrome | Yes (auto-sync) | N/A | Built-in | N/A |

### What's actually good about this (credit where due)

**The architectural decision to bypass WinUI Frame is correct and
well-justified.** The design spec documents exactly why Frame doesn't work:
XAML type metadata requirements, IPage interface hard-casts, parameterless
constructor constraints, no extension points. These are hard constraints in
WinUI's C++ code. Rather than fighting the platform, Reactor built its own
navigation on a `ContentPresenter`/`Grid` host with the reconciler managing
content swap. This is the same architectural choice Compose Nav3 made —
own the stack, own the rendering, let the framework manage it.

**The developer-owned back stack is the right call.** Both Compose Nav3 and
SwiftUI converged on "navigation state is a list the developer controls." Reactor
arrived at this independently. `NavigationStack<TRoute>` with `Navigate`,
`GoBack`, `GoForward`, `Replace`, `Reset`, `PopTo` is a clean, complete API
surface. The stack is an `IReadOnlyList<TRoute>` — inspectable, serializable,
testable. SwiftUI's `NavigationPath` is type-erased (you can't pattern-match on
entries), which is arguably worse.

**C# records as routes is clever.** Records give you structural equality,
immutability, `with` expressions, pattern matching in `switch`, and JSON
serialization for free. A `DetailRoute(int Id)` is more type-safe and more
ergonomic than Compose Nav3's `@Serializable data class Detail(val id: Int)`
(roughly equivalent) and strictly better than React Router's string-template
params (`"/detail/:id"`).

**Composition-layer transitions are a genuine differentiator.** The transition
engine uses `ElementCompositionPreview.GetElementVisual()` and runs slide/fade/
drill-in/spring animations on the compositor thread — zero managed-code
callbacks during animation, zero UI thread blocking. The automatic direction
reversal on GoBack (slide-from-right becomes slide-to-right) is a nice touch.
Per-navigation transition overrides (`nav.Navigate(route, new NavigateOptions {
Transition = NavigationTransition.DrillIn() })`) provide fine-grained control.
No competitor has GPU-accelerated transitions as a first-class navigation
feature.

**The lifecycle hook system is well-designed.** `UseNavigationLifecycle` with
`onNavigatedTo`, `onNavigatingFrom` (with cancellation), and `onNavigatedFrom`
follows the established navigation lifecycle pattern. The ordering is correct:
guard fires before stack mutation, mount and `onNavigatedTo` happen on the new
page, `onNavigatedFrom` fires after the old page is swapped out. The
cancellation mechanism (via `NavigatingFromContext.Cancel()`) is clean and
the guard runs synchronously before the stack mutates — no race conditions.

**The NavigationView integration is thoughtful.** `.WithNavigation(nav,
routeToTag, tagToRoute)` auto-syncs selected item, wires selection change to
navigate, and manages back button visibility/state. This is the kind of
framework integration that eliminates 30+ lines of boilerplate in every app.

**117 unit tests is substantial coverage.** The test suite covers stack
operations, host rendering, lifecycle ordering, caching, serialization/deep
linking, and NavigationView sync. The self-host test pattern (driving navigation
without WinUI controls) enables fast, reliable CI.

### What's still concerning (skeptic's view)

**1. ConnectedTransition is a stub.** The `NavigationTransition.Connected()`
factory exists and is documented, but `TransitionEngine.RunTransition` logs a
warning and falls back to `SlideTransition`. This is a spec-driven API that
doesn't work. A developer who writes `Transition = NavigationTransition.Connected()`
gets a slide instead of a connected animation, with the only indication being a
debug log message. Shipping a named API that doesn't do what the name says is
worse than not shipping it — it's a trap. SwiftUI's `matchedGeometryEffect` and
Compose's `SharedTransitionLayout` both work. Reactor's doesn't.

**2. E2E tests were silently broken — now fixed, but the blind spot is telling.**
The previous review flagged E2E Appium tests as "fixtures only — not executed."
The truth was worse: 44 of 46 E2E tests existed and ran, but the test filter
(`ClassName=InteractiveTests`) made only 2 visible to `dotnet test`. The fix
(changing to `ClassName!=SelfTestBatch`) now surfaces 43 Appium tests across 6
test classes including `AccessibilityInteractionTests` (10 tests) and
`WinFormsInteropTests` (14 tests). This is a significant improvement — E2E
validation is real and running. But the fact that 44 tests were invisible for
the entire development period means prior E2E "we shipped tests" claims were
untested claims. How many other test configurations are silently misconfigured?

**3. No adaptive multi-pane layout.** Compose Nav3's `Scene`/`SceneStrategy`
abstraction handles the list-detail pattern: on a wide screen, show list and
detail side-by-side; on a narrow screen, push detail onto the stack. This is
increasingly important even for desktop apps (think adaptive panels in Outlook,
Teams, VS Code). Reactor's navigation is strictly single-pane — one route renders
one page. Building a master-detail layout requires manual composition outside the
navigation system. For a desktop-first framework, this is an odd omission.

**4. Deep link patterns are stringly-typed at the boundary.** `DeepLinkMap.Map(
"/detail/{id:int}", args => ...)` uses string patterns with string parameter
names. A typo in `"{id:int}"` silently fails to match. The `RouteArgs.Get<T>(
"id")` call is a string-keyed dictionary lookup with a runtime cast. The irony
is thick — Reactor chose C# records for routes specifically for type safety, then
introduced string-based pattern matching at the deep link boundary. The type
safety is only skin deep: the deep link system connects untyped URI strings to
typed routes through a stringly-typed middle layer.

**5. The Grid wrapper adds another invisible element.** `MountNavigationHost`
creates a Grid as the container for navigation content. This is the same
problem as the Border wrapper for components (Section 4, critique #5) — the
WinUI visual tree accumulates framework-internal containers that participate in
layout but serve no visual purpose. A NavigationHost inside a component inside
a NavigationView produces: NavigationView > ... > Border (component) >
Grid (nav host) > content. Every extra layout container costs measure/arrange
time.

**6. Cache restore may skip transitions.** When a cached page is restored, the
reconciler skips remounting (the cached `UIElement` is reattached directly).
It's unclear whether transitions run during cache restore. If they don't, the
user sees an instant snap when navigating back to a cached page but a smooth
slide when navigating to an uncached one — an inconsistency that would feel
broken.

**7. Destination-side guards now exist but data-dependent navigation is still
missing.** `NavigatingToContext.Cancel()` now allows the destination page to
reject navigation — closing the "outgoing guard only" gap flagged in the
previous review. This handles authorization checks and prerequisite validation.
But React Router's loaders (fetch data before navigation completes, show loading
state during fetch) still have no equivalent. The guard is synchronous — there's
no way to say "defer this navigation until the data loads, then decide." For
data-heavy apps where every page transition depends on an API call, the guard
is necessary but not sufficient.

**8. UseSystemBackButton is a separate opt-in hook.** The developer must
explicitly call `UseSystemBackButton(nav, window)` to wire Alt+Left and the
system back button. For a desktop framework where back-navigation is a core
interaction, this should be default behavior that you opt *out* of, not opt
*in* to. Every NavigationHost should respond to Alt+Left unless told not to.

**9. The sample app is the only integration test.** The NavigationDemo sample
app is comprehensive (routes, guards, transitions, caching, deep linking,
nested navigation). But it's a demo, not a test. There's no automated
verification that it works. The existing showcase apps (Outlook clone, file
manager) haven't been updated to use the new navigation system — meaning the
framework's most complex apps still use the hand-rolled `UseState` switch
pattern.

### Revised navigation verdict

**Previously: F → B+. Now: B+ (solid, trending toward A-).**

The B+ holds, with meaningful incremental progress. Destination-side guards
close a real gap (destination can now reject navigation). Deep link enhancements
(query strings, optional params, wildcards) make the deep linking system more
practical. `NavigationDiagnostics` provides the observability hooks that
production apps need. Most importantly, the E2E test discovery fix means 43
Appium tests are actually validating the full pipeline — the previous review's
"E2E tests are unexecuted" critique was correct at the `dotnet test` level
but the underlying problem was worse than assumed (broken filter, not missing
tests).

The grade stays B+ rather than moving to A- because: ConnectedTransition is
still a stub, no adaptive multi-pane layout exists, deep linking still has a
stringly-typed middle layer, the showcase apps still haven't adopted navigation,
and the destination guard is synchronous (no async data loading). The trajectory
is clear — each iteration closes real gaps — but the last 15% (adaptive layouts,
connected transitions, showcase adoption, async guards) separates "competitive"
from "best-in-class."

---

## 8. Commanding

### The commanding story: a novel framework feature with real gaps

The previous version of this review scored Commands at F — "No ICommand
equivalent." A comprehensive commanding system has now shipped: immutable
`Command` records that bundle execute + canExecute + label + icon +
description + keyboard accelerator, 16 standard commands, a `UseCommand` hook
for async lifecycle, focus-scoped keyboard accelerators via `CommandHost`,
command-aware DSL overloads for Button/AppBarButton/MenuItem, and ICommand
interop. The honest assessment: **commanding is a genuine framework
differentiator — no competing declarative framework provides this — but it
has performance concerns and missing capabilities that limit its claim to
being a complete solution.**

### Why this matters (the competitive context)

The commanding design spec's research is correct and worth repeating:

- **React** has no command abstraction. Third-party command palette libraries
  (`cmdk`, `kbar`) are UI components, not command registries.
- **SwiftUI** has `CommandMenu`/`CommandGroup` for macOS menu bars and (as of
  iPadOS 26) iPad menu bars. But there's no bundling — each menu item repeats
  its label, icon, shortcut, and action. No "define once, use everywhere."
- **Compose** has nothing. No commanding abstraction whatsoever.
- **Every serious app builds a custom command registry.** VS Code, Files App,
  Windows Terminal all rolled their own. This is a real, unsolved gap.

Reactor filling this gap is the single most novel feature in the framework. It's
the one area where a skeptic has to concede: Reactor provides something the
competition genuinely doesn't.

### What Reactor has now

```csharp
// Define once — immutable record with all metadata
var saveCmd = new Command {
    Label = "Save",
    ExecuteAsync = async () => await SaveDocumentAsync(),
    Icon = new SymbolIconData("Save"),
    Accelerator = new KeyboardAcceleratorData(VirtualKey.S, VirtualKeyModifiers.Control),
    CanExecute = isDirty,
    Description = "Save the current document"
};

// Or use standard commands (16 built-in)
var cutCmd = StandardCommand.Cut(() => CutSelection(), canExecute: hasSelection);
var copyCmd = StandardCommand.Copy(() => CopySelection(), canExecute: hasSelection);
var pasteCmd = StandardCommand.Paste(() => PasteFromClipboard());

// UseCommand hook for async lifecycle (auto-debounce, IsExecuting tracking)
var save = UseCommand(saveCmd);
// save.IsExecuting is true during async operation
// save.IsEnabled is false while executing — buttons auto-disable

// Use in N surfaces from one definition
CommandBar(
    AppBarButton(cutCmd),    // auto-maps Label, Icon, Accelerator, IsEnabled
    AppBarButton(copyCmd),
    AppBarButton(pasteCmd)
)
MenuBar(
    MenuBarItem("Edit",
        MenuItem(cutCmd),    // same command, same metadata, different surface
        MenuItem(copyCmd),
        MenuItem(pasteCmd)
    )
)

// Per-site overrides via record with-expressions
MenuItem(deleteCmd with { Label = "Remove from list" })

// Focus-scoped keyboard accelerators
CommandHost([saveCmd, undoCmd, redoCmd],
    VStack(editorContent)    // Ctrl+S/Z/Y only work inside this region
)

// Parameterized commands
var deleteItem = new Command<Item> {
    Label = "Delete",
    Execute = item => RemoveItem(item),
    Icon = new SymbolIconData("Delete"),
    CanExecute = canDelete
};
MenuItem(deleteItem, selectedItem)  // binds parameter at use site

// ICommand interop for migration
var legacyCmd = CommandInterop.FromCommand(viewModel.SaveCommand, "Save");
```

### What's actually good about this (credit where due)

**The "CanExecute is a bool" design is brilliant in context.** Traditional
`ICommand` uses `CanExecuteChanged` events to notify the UI when enablement
changes. This is an event-based mechanism fighting a reactive framework. Reactor's
approach: commands are created during `Render()`, which runs on every state
change. `CanExecute` is just a bool that naturally reflects current state
because `isDirty` or `hasSelection` are already reactive. No events needed.
The framework's re-render cycle IS the notification mechanism. This is the
insight that makes the whole design work — it eliminates the impedance mismatch
between ICommand's event model and Reactor's declarative model.

**The "define once, use everywhere" pattern works.** One `Command` drives
`AppBarButton`, `MenuItem`, `Button`, keyboard accelerator, and tooltip from a
single definition. The DSL overloads (`Button(cmd)`, `AppBarButton(cmd)`,
`MenuItem(cmd)`) auto-map all metadata fields to the appropriate WinUI
properties. Per-site overrides via `cmd with { Label = "..." }` let you
customize without duplicating. This is exactly the capability VS Code, Files,
and Windows Terminal all built custom registries to achieve.

**StandardCommand is a nice convenience.** `StandardCommand.Cut(action)` gives
you the correct icon (SymbolIcon("Cut")), the correct keyboard accelerator
(Ctrl+X), and a label — ready to use. All 16 standard commands (Cut, Copy,
Paste, Undo, Redo, Delete, SelectAll, Save, Open, Close, Share, Play, Pause,
Stop, Forward, Backward) are implemented. This eliminates the "look up the
right icon and accelerator" dance that wastes time in every WinUI app.

**UseCommand for async is well-designed.** The hook wraps async commands with
`IsExecuting` tracking and re-entrance prevention. During `ExecuteAsync`, the
command auto-disables (buttons go gray). When it completes, it re-enables.
The implementation is clean: `UseState(false)` for the executing flag,
`UseMemo` for the wrapped execute with the guard. Sync-only commands skip hook
state entirely (no slot waste), which is a thoughtful optimization.

**Focus-scoped accelerators are the right model for desktop.** `CommandHost`
creates a region where keyboard accelerators are active only when focus is
within the host. This solves the "Ctrl+S means different things in different
panels" problem that desktop apps face. The implementation checks
`IsDescendantOf(focused, host)` before invoking — simple and correct.

### What's still concerning (skeptic's view)

**1. Accelerators are rebuilt on every render.** `UpdateCommandHost` clears and
recreates all `KeyboardAccelerator` objects on the WinUI `UIElement` every
render cycle. This is O(n) COM interop calls per render per CommandHost, where
n is the number of commands with accelerators. The reason is that accelerator
event handlers capture command closures that reference current state — so they
need fresh closures each render. This is functionally correct but
architecturally expensive. React avoids this with event delegation. Compose
avoids it by not having keyboard accelerators. Reactor's approach is the most
direct and the most wasteful.

For a CommandHost with 20 commands (not unusual for a document editor's main
region), that's 20 accelerator creates + 20 accelerator destroys per render.
Each involves COM interop to the WinUI layer. If the component re-renders
frequently (e.g., on every keystroke in a text field), this compounds quickly.

**2. StandardCommand labels are English-only.** `StandardCommand.Cut` has
`Label = "Cut"`. Not `Label = Intl("Cut")`. Not `Label = loc.GetString("Cut")`.
Just a hard-coded English string. This is surprising given that Reactor has a full
ICU-based localization system with `Context<IntlAccessor?>` integration.
The framework's own standard commands don't use the framework's own
localization system. A developer localizing their app will discover that
toolbar buttons say "Cut"/"Copy"/"Paste" in English regardless of locale, and
the fix is to create their own command definitions instead of using
`StandardCommand`. This defeats the purpose of standard commands.

For comparison, WinUI's `StandardUICommand` has localized labels for all
supported Windows languages. Reactor's `StandardCommand` is a regression from the
platform it's built on.

**3. No command routing to the focused view.** The design spec lists "command
routing to focused view" as a non-goal ("future work — needs focus management
first"). But this is the core use case for Cut/Copy/Paste in a multi-document
or multi-panel app. When the user presses Ctrl+C, which panel's selection gets
copied? `CommandHost` scopes accelerators to a region, but it doesn't solve
the routing problem — if two editors are both inside the same CommandHost, the
command fires on whatever closure was captured at render time, not on the
focused editor.

SwiftUI solves this with `FocusedValue`/`FocusedObject` — the focused view
publishes its cut/copy/paste handlers, and the menu bar reads them. WPF solved
this with `RoutedUICommand` and command routing through the visual tree. Reactor
has neither. For a desktop framework, this is a conspicuous gap.

**4. No command palette UI.** The commanding design spec lists this as future
work. The registry data model (commands with labels, icons, accelerators) is
the perfect foundation for a VS Code-style command palette. But the framework
doesn't provide the palette itself. Given that Reactor aims to be an opinionated
framework (not just a component library), shipping the registry without the
palette is like shipping a search index without a search box.

**5. CommandHost creates another invisible Grid wrapper.** `MountCommandHost`
creates a Grid to host the accelerators. This is the third invisible wrapper
(component Border, NavigationHost Grid, CommandHost Grid) that accumulates in
the visual tree. A component with a navigation host and a command host produces
Border > Grid > Grid > content — three extra layout containers.

**6. IsDescendantOf visual tree walk on every key press.** When any keyboard
accelerator fires inside a CommandHost, the handler walks the WinUI visual tree
upward from the focused element to check if it's a descendant of the host. For
deep visual trees (which Reactor creates via its wrapper elements), this is O(d)
per key press where d is tree depth. Most key presses in a document editor fire
a KeyDown → accelerator check chain. This isn't catastrophic, but it's another
tax on a hot path.

**7. No batch command registration.** Each `CommandHost` independently manages
its accelerators. If multiple `CommandHost` elements exist (e.g., one for
document commands, one for panel commands), accelerators are registered on
separate WinUI elements. There's no global registry, no deduplication, no
priority system. If two CommandHosts register Ctrl+S, both fire (or one
shadows the other depending on focus). The design spec mentions a global
command registry as future work, but the current system is purely local.

**8. Command equality may defeat memoization.** `Command` is a record,
so its `Equals` compares all fields structurally — including `Execute` (an
`Action`) and `ExecuteAsync` (a `Func<Task>`). Delegate equality in C# compares
target + method — two lambdas that capture different closure state are never
equal, even if they do the same thing. This means a `Command` created in
`Render()` is *always* unequal to the one from the previous render (because the
lambda captures fresh state). Components that receive commands as props will
always fail the memo check, defeating the default-on memoization from Section 2.
The `UseCommand` hook doesn't address this — it wraps the command but returns a
new record each time.

### Revised commanding verdict

**Previously: F (no ICommand equivalent). Now: B+.**

The improvement is a leap, and the feature is genuinely novel. No competing
declarative framework provides define-once commands with metadata bundling,
standard commands, async lifecycle, and focus-scoped accelerators. Reactor is
ahead of the entire competition here — not catching up, actually leading. The
API design is clean, the integration with the DSL is natural, and the
`UseCommand` hook for async is well-considered.

The grade is B+ rather than A because of implementation concerns: accelerator
rebuild on every render, un-localized standard commands (ironic given the
localization system exists), no command routing to the focused view, delegate
equality defeating memoization, and the missing command palette UI. The
foundation is strong — a command palette, localization, and routing can all be
built on top of what exists. But "the foundation enables it" and "it's shipped"
are different claims, and this review scores what's shipped.

---

## 9. Lists and Collections

### What Reactor has

- `ListView(items)` / `GridView(items)` — WinUI controls
- `LazyVStack<T>` / `LazyHStack<T>` — virtualized via ItemsRepeater
- `TemplatedListView<T>` / `TemplatedGridView<T>` — typed templates
- `ForEach` — non-virtualized iteration
- `TreeView` with drag support
- `FlipView`, `SemanticZoom`

### Critiques

**1. ForEach is not virtualized.** `ForEach(items, item => ...)` produces a
`GroupElement` with ALL items rendered. For 1000 items, this creates 1000
elements in memory, diffs all 1000, and mounts all 1000 WinUI controls. React
has the same problem (it needs react-window), but SwiftUI's `ForEach` inside
`List` is virtualized, and Compose's `items()` inside `LazyColumn` is virtualized.
Reactor requires explicitly choosing `LazyVStack<T>` for virtualization — a common
footgun for developers who reach for the simpler `ForEach` API first.

**2. LazyVStack requires a key selector.** `LazyVStack<T>(items, keySelector,
viewBuilder)` forces you to provide a key extraction function. React, SwiftUI,
and Compose all have default behaviors for keyless lists (positional matching).
Reactor's LazyVStack won't compile without a key selector. While keys are a best
practice, making them mandatory adds friction for prototyping.

**3. No sections or grouping in lists.** SwiftUI has `Section(header:)` inside
`List`. Compose has `stickyHeader {}` in `LazyColumn`. Reactor has nothing — to
create a sectioned list, you'd need to manually interleave header elements with
content items and handle all the layout yourself.

**4. No pull-to-refresh.** `RefreshContainer` exists but it's a separate
element, not integrated with ListView. SwiftUI has `.refreshable { }` on List.
Compose has `PullToRefreshBox`. Reactor requires manually wrapping a list in a
RefreshContainer and wiring the callback.

**5. No drag-and-drop reordering for lists.** TreeView has drag support, but
ListView and GridView have no built-in reordering. SwiftUI has `.onMove` on
`ForEach`. This must be manually implemented in Reactor.

**6. No empty state handling.** All frameworks require manual `if list.isEmpty`
checks, so this isn't a competitive disadvantage — but a production framework
could provide a convenience API like `ListView(items, template, emptyState)`.

---

## 10. Animation

### The animation story: from partially-wired to fully operational (within its ceiling)

The last version of this review graded animation at C+ — good API design across
8 new compositor-layer features, but four integration bugs where the API
promised behavior the reconciler didn't deliver: exit transitions were dead code,
Focused interaction state was silently ignored, WithAnimation only animated
Opacity, and stagger didn't integrate with enter transitions. The review
explicitly said: "Fix the four integration bugs and the compositor-property
ceiling is the only remaining structural concern — that would be a clear B-."

A focused bug-fix diff (commit d38c6ef, 3 sub-commits) has done exactly that —
plus fixed three additional runtime issues discovered during implementation.
The honest assessment now: **all four integration bugs are fixed, three runtime
issues are resolved, and the compositor-property ceiling is the only remaining
structural concern. The animation system now delivers what its API promises.**

### What Reactor has now

**Tier 1: Implicit transitions (5 properties, unchanged).**

- `.OpacityTransition(duration?)` — ScalarTransition on UIElement.Opacity
- `.RotationTransition(duration?)` — ScalarTransition on UIElement.Rotation
- `.ScaleTransition(transition?)` — Vector3Transition on UIElement.Scale
- `.TranslationTransition(transition?)` — Vector3Transition on UIElement.Translation
- `.BackgroundTransition(duration?)` — BrushTransition on Grid/StackPanel only

These are thin wrappers over WinUI's built-in implicit transition properties.
Unchanged from before. They run on the composition thread with zero managed-code
involvement.

**Tier 2: Theme transitions (structural enter/exit).**

- `.WithTransitions(params Transition[])` — ChildrenTransitions on panels
- `.ItemContainerTransitions(params Transition[])` — ItemContainerTransitions
  on ListView/GridView

Also unchanged. Uses WinUI's theme transitions directly.

**Tier 3: Curve system (NEW — easing/spring DSL).**

```csharp
Curve.Spring(dampingRatio: 0.8f, period: 0.05f)
Curve.Ease(300, Easing.EaseInOut)
Curve.Ease(200, Easing.CubicBezier(0.2f, 0.9f, 0.3f, 1.0f))
Curve.Linear(150)
```

`Curve` is an abstract record with three sealed subtypes: `SpringCurve`,
`EaseCurve`, `LinearCurve`. `Easing` is a readonly record struct with 7 presets
(Linear, EaseIn, EaseOut, EaseInOut, Accelerate, Decelerate, Standard) plus
`CubicBezier()` for custom curves. Immutable, zero-allocation, shareable.
`AnimationHelper` maps these to WinUI compositor animations:
`CreateSpringScalarAnimation()` / `CreateSpringVector3Animation()` for springs,
`CreateScalarKeyFrameAnimation()` with `CreateCubicBezierEasingFunction()` for
eased, plain keyframe animation for linear.

This directly addresses the "no easing function DSL" critique from the previous
review. The design is clean — the curve types carry enough data for the
compositor bridge to create the right animation, and the bridge (`AnimationHelper`)
handles scalar vs. Vector3 variants uniformly across all curve types.

**Tier 4: `.Animate()` modifier (NEW — declaration-site implicit animations).**

```csharp
Border(child).Animate(Curve.Spring(), AnimateProperty.Opacity | AnimateProperty.Scale)
Border(child).Animate(Curve.Ease(200, Easing.Decelerate))  // default: All 5 properties
```

`AnimateProperty` is a flags enum covering Opacity, Offset, Scale, Rotation,
CenterPoint. The reconciler sets up `ImplicitAnimationCollection` entries on the
element's composition Visual for the specified properties. Any subsequent
compositor-level change to those properties animates automatically. This extends
beyond the 5 UIElement-level implicit transitions by targeting the composition
Visual directly, though it still only reaches compositor properties.

**Tier 5: Layout animations (previously documented — composition-layer
position/size).**

```csharp
Border(child).LayoutAnimation()
Border(child).SpringLayoutAnimation(dampingRatio: 0.8f, period: 0.1f)
```

Unchanged from last review. Sets up implicit animation on the composition
Visual's Offset and optionally Size. Well-engineered, spring physics, correct
reconciler integration.

**Tier 6: Enter/exit transitions (NEW — individual element mount/unmount
animations).**

```csharp
// Fade in on mount, fade out on unmount
Text("Hello").Transition(Transition.Fade)

// Slide in from bottom on mount, scale out on unmount
Panel.Transition(Transition.Slide(Edge.Bottom) | Transition.Scale(0.85f))

// Parallel: fade + slide together
Panel.Transition(Transition.Fade + Transition.Slide(Edge.Left))

// Asymmetric: different enter and exit
Panel.Transition(Transition.Fade | Transition.Slide(Edge.Bottom))
```

`Transition` is an abstract record with sealed subtypes: `FadeTransition`,
`SlideTransition(Edge)`, `ScaleTransition(float From)`, `CombinedTransition`,
`AsymmetricTransition`, `DirectionalTransition`. Two operators: `+` for
parallel composition, `|` for asymmetric enter/exit. `ElementTransition`
wraps the transition with an optional `Curve` override.

The reconciler integration is now complete. On mount:
`ApplyEnterTransition()` runs compositor animations (opacity/offset/scale) on
the element's Visual, respecting `AnimationScope.Current` if present (priority:
explicit curve > ambient scope > 300ms Decelerate default). On unmount:
`ApplyExitTransition()` creates a `CompositionScopedBatch`, runs exit
animations, and defers removal until `batch.Completed`. All three removal paths
in `ChildReconciler` (positional excess, keyed-only removals, keyed middle
unmatched) route through `RemoveChildWithExitTransition()`. Type-mismatch
replacements (e.g., `visible ? Border(...) : Text(...)`) route through
`ReplaceChildWithExitTransition()`, which inserts the new control, re-inserts
the old for its exit animation, then removes it on completion.

This fully addresses the "no enter/exit for individual elements" critique.
Both enter and exit transitions work. The composition-operator DSL
(`+` for parallel, `|` for asymmetric enter/exit) now has full runtime effect.

**Tier 7: Interaction states (NEW — zero-reconcile visual state machine).**

```csharp
Border(child).InteractionStates(s => s
    .PointerOver(scale: 1.05f, opacity: 0.9f)
    .Pressed(scale: 0.95f, background: hoverBrush)
    .Focused(borderBrush: focusBrush))
```

`InteractionStatesConfig` defines visual values for PointerOver, Pressed, and
Focused states. `InteractionStateValues` carries: 5 compositor properties
(Opacity, Scale, ScaleV, Translation, Rotation) + 3 brush properties
(Background, Foreground, BorderBrush). The reconciler registers pointer event
handlers that apply compositor animations for transform properties (zero-cost
during interaction) and direct brush swaps (~1μs) for brush properties. No
reconciler re-render. No state change. No virtual tree diff.

All three interaction states — PointerOver, Pressed, and Focused — are wired.
The reconciler's `ApplyInteractionStates()` registers PointerEntered/
PointerExited, PointerPressed/PointerReleased, and GotFocus/LostFocus handlers.
Focus styling values are properly applied and reverted through the same
transition system as pointer states.

This fully addresses the "VSM replacement is expensive" critique. Hover, press,
and focus effects using InteractionStates run entirely on the composition
thread (for transforms) or via pre-cached brush swap (for brushes), with zero
managed-code re-rendering.

The scope is intentionally narrow — `InteractionStateValues` is a closed record,
not an extensible property bag. No Width, Margin, FontSize, CornerRadius. The
record IS the boundary, and the boundary is the compositor + 3 brush properties.
This is honest design: it covers what can be done cheaply and stops there.

**Tier 8: Staggered children (NEW).**

```csharp
StackPanel(children).Stagger(TimeSpan.FromMilliseconds(50))
StackPanel(children).Stagger(TimeSpan.FromMilliseconds(50), Curve.Spring())
```

`StaggerConfig` applies progressive delay to child animations — layout
animations, property animations, and enter transitions — via
`CompositionAnimation.DelayTime`. Parent elements with `StaggerConfig` push a
`StaggerScope` before mounting children; each child's `ApplyEnterTransition`
consumes an incrementing index for stagger delay computation. The scope supports
nesting — inner stagger panels reset the index independently.

This fully integrates stagger with enter transitions. A staggered list with
`.Transition(Transition.Fade)` on each item produces a cascade fade-in effect
with each item delayed by the stagger interval.

**Tier 9: Keyframe animations (NEW — trigger-based multi-property keyframes).**

```csharp
Border(child).Keyframes("pulse", triggerValue, k => k
    .Duration(600).Loop()
    .At(0f, scale: Vector3.One)
    .At(0.5f, scale: new Vector3(1.1f, 1.1f, 1f), easing: Easing.EaseOut)
    .At(1f, scale: Vector3.One, easing: Easing.EaseIn))
```

`KeyframeBuilder` constructs a `KeyframeAnimationDef` with typed `KeyframeDef`
entries. Each keyframe can set Opacity, Scale, Translation, Rotation with
per-keyframe easing. Animations are trigger-based: when the `Trigger` value
changes (by reference), the reconciler starts new compositor animations. Looping
is supported. `ApplyKeyframeAnimations()` in the reconciler creates
`ScalarKeyFrameAnimation` / `Vector3KeyFrameAnimation` instances, inserts
keyframes with their easing functions, and calls `visual.StartAnimation()`.

This directly addresses the "no keyframe or sequenced animation DSL" critique.
The design is adequate — the builder pattern is familiar and the trigger
mechanism is sensible. The limitation: keyframes can only target the same
compositor properties everything else targets. No color keyframes, no layout
property keyframes.

**Tier 10: Scroll-linked animations (NEW).**

```csharp
ScrollViewer(content).ScrollAnimation(sv, b => b
    .Parallax(0.5f)
    .FadeOut(100, 400)
    .ScaleRange(0, 300, 1f, 0.8f)
    .Expression("Rotation", "scroll.Translation.Y * 0.001"))
```

`ScrollAnimationBuilder` produces `ScrollExpression[]` entries (property name +
expression string). The reconciler creates `ExpressionAnimation` instances on
the composition Visual, referencing the ScrollViewer's manipulation property
set. Preset helpers cover parallax, fade ranges, and scale ranges. Custom
expressions via `.Expression()` for anything else.

**Tier 11: AnimationScope.WithAnimation (NEW — ambient animation context).**

```csharp
// All property changes in this block animate with the given curve
AnimationScope.WithAnimation(Curve.Ease(300, Easing.Decelerate), () =>
{
    SetExpanded(!expanded);
});

// Async: returns Task that completes when all animations finish
await AnimationScope.WithAnimationAsync(Curve.Spring(), () =>
{
    SetStep(nextStep);
});
```

`AnimationScope` uses `[ThreadStatic]` fields to hold a `Curve?` and a
`bool HasScope`. `WithAnimation()` sets the ambient curve, runs the action,
and restores the previous curve in a `finally` block (nestable). The reconciler
routes all five compositor properties (Opacity, Scale, Rotation, Translation,
CenterPoint) through `AnimationHelper.SetOrAnimate()` / `SetOrAnimateVector3()`,
which check `AnimationScope.Current` for an ambient curve. The ambient curve
also propagates to enter transitions (overrides the default 300ms Decelerate).
Additionally, `SetOrAnimate` checks for the element's `AnimationConfig` (from
`.Animate()`) as a fallback when no ambient scope is present, creating explicit
compositor animations using the config's curve.

A critical runtime fix ensures the scope persists across the async render
boundary: state setters inside `WithAnimation` trigger an async re-render via
`DispatcherQueue`, and by the time `Reconcile()` runs the scope would normally
be gone. `ReactorHost` now captures the ambient curve in `RequestRender()` and
restores it around the `Reconcile()` call via `AnimationScope.PushScope/PopScope`.

The async variant creates a `CompositionScopedBatch`, runs the action inside
the batch, calls `batch.End()`, and returns a `Task` that completes on
`batch.Completed`. This lets callers await animation completion.

This fully addresses the "no declarative value-driven animation API" critique.
It's close to SwiftUI's `withAnimation { }` pattern — wrap a state change, and
the resulting property updates animate. All compositor properties are covered.
Width changes, color changes, margin changes still don't animate (those are
layout/XAML properties, not compositor properties). But within the compositor
property set, `WithAnimation` works completely.

**Tier 12: Connected animations (previously documented).**

```csharp
Border(avatar).ConnectedAnimation("hero-image")
```

Unchanged from last review. Two-phase prepare-on-unmount / start-after-mount
pattern with deferred flush. Still string-keyed.

### What's actually good about this (credit where due)

The design quality across these 8 new features is consistently high. A few
things stand out:

**The Curve type system is well-modeled.** Three sealed record subtypes, each
carrying exactly the data the compositor bridge needs. Immutable, shareable,
zero-allocation. The presets cover standard motion needs. Custom bezier is
available. This is a clean API that will age well.

**The Transition composition operators are genuinely expressive.** `Fade +
Slide(Bottom)` for parallel, `Fade | Scale(0.85f)` for asymmetric enter/exit.
The record hierarchy (FadeTransition, SlideTransition, CombinedTransition,
AsymmetricTransition, DirectionalTransition) handles these combinations
correctly without combinatorial explosion. The reconciler decomposes them via
pattern matching in `GetEnterTransition()` / `GetExitTransition()`.

**InteractionStates solves a real performance problem correctly.** The previous
review flagged VSM replacement as expensive — hover effects triggering full
reconciliation. InteractionStates eliminates this for the common case. The
closed record design is honest: it covers what can be done at compositor speed
and doesn't pretend to be a general-purpose state machine for arbitrary
properties. The builder API is ergonomic.

**The reconciler lifecycle integration is now thorough and complete.** Every
feature has mount/update/unmount wiring:
- Enter transitions applied at mount, exit transitions defer removal via
  CompositionScopedBatch callback — all removal paths covered
- InteractionStates handlers registered at mount, cleared at unmount
  (PointerOver, Pressed, and Focused all wired)
- Scroll animations created at mount, cleared at unmount
- Keyframe animations triggered on mount and on trigger-value change
- Stagger delays applied to both implicit animations and enter transitions
  via StaggerScope
- Layout animations and `.Animate()` configs applied via implicit animation
  collections

On update, the reconciler correctly diffs: if a config changed, reapply; if
removed, clear. The structural wiring is complete — the API delivers what it
promises.

**AnimationScope.WithAnimation is the right abstraction, now fully wired.**
Ambient curve propagation via ThreadStatic fields, nestable with proper restore
in `finally`, async variant with CompositionScopedBatch completion tracking.
All five compositor properties route through `SetOrAnimate`/`SetOrAnimateVector3`.
The scope persists across the async render boundary via ReactorHost capture/restore.
The pattern is architecturally sound (same model as SwiftUI's `withAnimation`)
and the implementation is complete.

### Previously-reported integration bugs: all four fixed

The previous version of this review identified four integration bugs where
the API promised behavior the reconciler didn't deliver. All four have been
fixed in commit d38c6ef (3 sub-commits, 767 lines added), along with three
additional runtime issues discovered during implementation:

**Bug 1 (exit transitions): FIXED.** All three removal paths in
`ChildReconciler` now route through `RemoveChildWithExitTransition()`.
Type-mismatch replacements route through `ReplaceChildWithExitTransition()`.

**Bug 2 (Focused state): FIXED.** `ApplyInteractionStates()` now registers
GotFocus/LostFocus handlers alongside pointer handlers.

**Bug 3 (WithAnimation scope): FIXED.** All five compositor properties now
route through `SetOrAnimate()`/`SetOrAnimateVector3()`. Additionally, the
scope persistence across the async render boundary was fixed (ReactorHost captures
the ambient curve before DispatcherQueue dispatch and restores it around
`Reconcile()`).

**Bug 4 (stagger + enter transitions): FIXED.** `StaggerScope` is pushed
before mounting children; each child's `ApplyEnterTransition` consumes an
incrementing index for stagger delay computation, with proper nesting support.

**Additional runtime fixes:**
- **Opacity routing for `.Animate()`:** UIElement.Opacity is a XAML DP, not a
  compositor facade — setting it directly doesn't trigger implicit animations
  from `.Animate()`. `SetOrAnimate` now creates explicit compositor animations
  when an `AnimationConfig` is present.
- **Pool crash with compositor-tainted elements:** Elements that had
  `GetElementVisual()` called permanently lose XAML implicit transition API
  access. `ElementPool` now tracks "compositor-tainted" elements via
  `ConditionalWeakTable` and excludes them from pooling.
- **Exit transitions in type-mismatch replacement:** `visible ? Border : Text`
  scenarios now defer removal via `ReplaceChildWithExitTransition`.

**47 regression tests** in `AnimationBugTests.cs` (all pure logic, no WinUI
host) validate these fixes.

### What's still concerning (skeptic's view)

**1. Still compositor-property-bound.** This is the fundamental ceiling that
no amount of good API design can remove. Reactor's animation system — all of it,
every tier — can only animate what the WinUI compositor exposes on the Visual:
Opacity, Offset (Translation), Scale, Rotation, CenterPoint, and Size
(cosmetic only). You cannot animate:

- Width, Height, MinWidth, MaxWidth (layout properties)
- CornerRadius, Margin, Padding (layout/shape properties)
- FontSize, FontWeight (text properties)
- Arbitrary brush colors or gradients (beyond the 3 InteractionStates swaps)
- Clip geometry, border thickness, any custom dependency property

SwiftUI animates *any* state change that produces a different view body.
Compose's `animateAsState` works for any type with a `TwoWayConverter`.
Flutter's `AnimatedBuilder` + `Tween` works for any property with `lerp`.
Reactor can only animate the ~5 compositor properties plus 3 brush swaps. This
isn't a Reactor design failure — it's a WinUI platform constraint. But the result
is real: a developer who wants a button's corner radius to animate on hover,
or a sidebar's width to animate on expand, has no declarative path. They need
`.Set()` and WinUI's `DoubleAnimation` / `Storyboard` API.

The `.Animate()` modifier, InteractionStates, keyframes, and
AnimationScope.WithAnimation all work within the same compositor property set.
They provide different *control models* (implicit, event-driven, trigger-based,
ambient) for the same narrow set of properties. This is good engineering — but
it's an increasingly sophisticated system for animating the same 5 things.

**2. No per-frame UseAnimation hook.** `AnimationScope.WithAnimation` wraps
state changes so the reconciler routes property updates through compositor
animations. This is the right model for reactive animation. But there's no hook
that yields interpolated values each frame for custom rendering:

```csharp
// Still doesn't exist:
var progress = UseAnimation(0.0, 1.0, duration: 300ms, easing: EaseOut);
// progress smoothly animates from 0 to 1, re-rendering at each frame

var spring = UseSpring(targetX, stiffness: 200, damping: 20);
// spring.Value drives layout or drawing on each frame
```

React Spring's `useSpring`, Framer Motion's `useAnimation`, and Compose's
`Animatable` all provide this. A game progress bar, a custom drawing that
interpolates between states, a physics simulation — these need per-frame values
driven from component code. Reactor has no bridge between the hooks system and the
animation system. The compositor runs the animation; component code can't
observe the intermediate values.

In fairness, per-frame hooks would fight the compositor-thread model (they'd
require managed-code callbacks at display refresh rate), and WithAnimation
covers the common case of "animate this state transition." But the gap exists.

**3. Connected animations still use string-key coordination.** Source and
destination must use the same string key. A typo silently produces no animation
(the `try/catch` swallows the failure). SwiftUI's `matchedGeometryEffect`
uses typed Namespace objects; Compose's `SharedTransitionScope` uses typed keys.
Reactor uses bare strings. This was called out in the previous review and is
unchanged.

**4. Scroll-linked expressions are stringly-typed.** The
`ScrollAnimationBuilder` presets (Parallax, FadeOut, FadeIn, ScaleRange) are
type-safe and cover common cases. But the escape hatch —
`.Expression("Rotation", "scroll.Translation.Y * 0.001")` — is two bare
strings with no validation. A typo in the property name or expression syntax
fails at runtime. This is inherent to WinUI's `ExpressionAnimation` API, but
the framework could validate property names against a known set.

**5. Keyframes can only target compositor properties.** The `KeyframeBuilder`
accepts Opacity, Scale, Translation, Rotation — the same compositor properties
as everything else. You can't keyframe a color transition, a width animation,
or a corner radius change. SwiftUI's `KeyframeAnimator` (iOS 17+) works with
any `Animatable` value. The keyframe system is useful within its bounds but
those bounds are the same narrow set.

**6. Layout animation limitations are inherent and unchanged.**
- Hit-testing uses the final layout position, not the animated visual
- Size animation is cosmetic — content doesn't re-layout during animation
- Elements need stable keys for reconciler matching across reorders

These are inherent to composition-layer visual animations. Layout animations
work well for list reorders and grid reflows, less well for "sidebar expands
from collapsed width."

**7. InteractionStates is closed to 8 properties.** The 5 compositor
transform properties + 3 brush swaps cover hover/press/focus for common UI
patterns. But a button that changes CornerRadius on hover, or a card that
changes BorderThickness on focus, needs `.Set()`. The closed record design is
honest, but the boundary is tight. WinUI's VSM can animate any dependency
property via Storyboard; InteractionStates can animate 8.

**8. No sequencing or orchestration.** Keyframes provide multi-step animation
on a single element. But there's no built-in way to orchestrate animations
across multiple elements in sequence — "first fade in the header, then slide
in the list items, then scale up the FAB." Stagger handles the simple case
(uniform delay per child), but general sequencing requires manual coordination
via `WithAnimationAsync` + `await` chains. Framer Motion's `staggerChildren`
and `delayChildren` variants, and Compose's `AnimatedContent` with
`SizeTransform`, provide richer orchestration primitives.

### Revised animation verdict

**Previously: C+ (good design, 4 integration bugs). Now: B-.**

The previous review explicitly said: "Fix the four integration bugs and the
compositor-property ceiling is the only remaining structural concern — that
would be a clear B-." The bugs are fixed. The grade moves to B-.

The animation system is now a fully operational multi-tier compositor animation
system:

- A clean curve/easing DSL with springs, bezier, and presets
- Enter AND exit transitions with composition operators — both work
- Zero-reconcile interaction states for hover/press/focus — all three wired
- Keyframe animations with per-frame easing and looping
- Staggered children integrated with enter transitions
- Scroll-linked expression animations
- Ambient animation context (WithAnimation) for ALL compositor properties
- Compositor-tainted element tracking prevents pool crashes

The design quality is real. Immutable record types, composition operators,
proper lifecycle wiring in mount/update/unmount, ThreadStatic ambient
propagation with correct nesting and async-boundary persistence. The API now
delivers what it promises — `.Focused(borderBrush: focusBrush)` works,
`.Transition(Transition.Fade | Transition.Scale(0.85f))` runs the exit, and
`WithAnimation` animates all five compositor properties.

The grade is B-, not B, because the compositor-property ceiling is real and
structural. The system can only animate Opacity, Scale, Rotation, Translation,
CenterPoint, and 3 brush swaps. Width, Height, CornerRadius, Margin, FontSize,
arbitrary brush colors — none of these animate. SwiftUI and Compose animate
any value. Reactor has an increasingly sophisticated system for animating the same
narrow set of properties. This isn't a Reactor design failure — it's a WinUI
platform constraint — but it's the ceiling that keeps the grade below B.

Additionally, per-frame animation hooks (`UseAnimation`, `UseSpring`) still
don't exist, connected animations still use string keys, and there's no
cross-element sequencing/orchestration beyond stagger. These are real gaps, but
they're design omissions rather than broken promises — the system does what it
says it does.

---

## 11. Accessibility

### The accessibility story: from credible foundation to a real system (with limits)

The previous version of this review graded accessibility at C+ — 16 modifiers
and 12 UIA tests, but "the harder problems — hooks, diagnostics, custom
automation peers, and focus management — remain unbuilt." A focused accessibility
diff has landed that attacks three of those four gaps directly: SemanticPanel +
`.Semantics()` modifier for custom automation peers, AccessibilityScanner for
runtime WCAG diagnostics, 3 Roslyn analyzers (REACTOR_A11Y_001/002/003) for
compile-time checks, and UseFocusTrap for modal focus management. Additionally,
LabeledBy is now wired (with deferred resolution), ElementPool clears 14 UIA
properties on return to prevent stale state, Heading/SubHeading DSL functions
auto-set HeadingLevel, and a dedicated a11y-showcase sample demonstrates all
features. The honest assessment now: **accessibility has crossed from "annotations
on primitives" to a genuine accessibility system with semantic, diagnostic, and
behavioral layers — but the SemanticPanel approach has real architectural costs,
the scanner is runtime-only, UseFocusTrap doesn't cycle, and several imperative
hooks remain unbuilt.**

### What shipped

**16 first-class accessibility modifiers across two storage tiers:**

| Property | Modifier | Tier |
|---|---|---|
| AutomationProperties.Name | `.AutomationName()` | Tier 1 (inline) |
| AutomationProperties.AutomationId | `.AutomationId()` | Tier 1 (inline) |
| AutomationProperties.HeadingLevel | `.HeadingLevel()` | Tier 1 (inline) |
| Control.IsTabStop | `.IsTabStop()` | Tier 1 (inline) |
| Control.TabIndex | `.TabIndex()` | Tier 1 (inline) |
| UIElement.AccessKey | `.AccessKey()` | Tier 1 (inline) |
| AutomationProperties.HelpText | `.HelpText()` | Tier 2 (lazy) |
| AutomationProperties.FullDescription | `.FullDescription()` | Tier 2 (lazy) |
| AutomationProperties.LandmarkType | `.Landmark()` | Tier 2 (lazy) |
| AutomationProperties.AccessibilityView | `.AccessibilityView()` | Tier 2 (lazy) |
| Shorthand: hide from AT | `.AccessibilityHidden()` | Tier 2 (lazy) |
| AutomationProperties.IsRequiredForForm | `.Required()` | Tier 2 (lazy) |
| AutomationProperties.LiveSetting | `.LiveRegion()` | Tier 2 (lazy) |
| AutomationProperties.PositionInSet/SizeOfSet | `.PositionInSet()` | Tier 2 (lazy) |
| AutomationProperties.Level | `.HierarchyLevel()` | Tier 2 (lazy) |
| AutomationProperties.ItemStatus | `.ItemStatus()` | Tier 2 (lazy) |
| AutomationProperties.LabeledBy | `.LabeledBy()` | Tier 2 (lazy) — **defined but not reconciler-applied** |
| UIElement.TabFocusNavigation | `.TabNavigation()` | Tier 2 (lazy) |
| Custom AutomationPeer (composite) | `.Semantics()` | **NEW** — SemanticPanel wrapper |

**Lazy sub-record architecture.** Tier 1 properties (HeadingLevel, IsTabStop,
TabIndex, AccessKey) are stored inline on `ElementModifiers` — zero allocation
overhead for elements that don't use them. Tier 2/3 properties live in a
separate `AccessibilityModifiers` record that is only allocated when an advanced
modifier is first applied. A `ModifyA11y()` helper merges sub-records
automatically, and the developer sees a completely flat API surface — all
modifiers look identical at the call site:

```csharp
Button("Search", doSearch)
    .AutomationName("Search documents")
    .AccessKey("S")               // Tier 1 — inline
    .HelpText("Search all files") // Tier 2 — lazy sub-record
    .LiveRegion()                 // Tier 2 — same flat API
```

**12 end-to-end UIA tests via Appium/WinAppDriver.** These are real
out-of-process tests that read properties through the Windows UI Automation
client API — the same pipeline used by Narrator, NVDA, and automated testing
tools. Each test maps to a specific WCAG 2.1 success criterion:

| Test | WCAG | Validates |
|---|---|---|
| `A11y_1_1_1_IconButtonHasAccessibleName` | 1.1.1 | Name on icon-only buttons |
| `A11y_1_1_1_DecorativeImageHiddenFromUIA` | 1.1.1 | AccessibilityView.Raw hides decorative elements |
| `A11y_1_3_1_HeadingLevelsExposed` | 1.3.1 | HeadingLevel (Level1, Level2) |
| `A11y_1_3_1_LandmarksExposed` | 1.3.1 | Navigation & Main landmarks |
| `A11y_1_3_1_FormFieldRequired` | 1.3.1 | IsRequiredForForm |
| `A11y_1_3_1_HierarchyLevels` | 1.3.1 | Level property for tree structures |
| `A11y_2_1_1_AccessKeysExposed` | 2.1.1 | Access key shortcuts (Alt+F, Alt+E) |
| `A11y_3_3_2_FormFieldHasNameAndHelpText` | 3.3.2 | Name + HelpText on form fields |
| `A11y_3_3_2_FullDescriptionExposed` | 3.3.2 | FullDescription for complex elements |
| `A11y_4_1_2_ItemStatusExposed` | 4.1.2 | ItemStatus announcements |
| `A11y_4_1_2_PositionInSetExposed` | 4.1.2 | PositionInSet / SizeOfSet |
| `A11y_4_1_3_LiveRegionPolite` | 4.1.3 | Live region (Polite mode) |
| `A11y_4_1_3_LiveRegionAssertive` | 4.1.3 | Live region (Assertive mode) |

**Reconciler integration with change detection.** `ApplyAccessibilityModifiers()`
in the reconciler compares each property against the previous value before
calling the WinUI `AutomationProperties.Set*()` methods. This avoids redundant
COM interop calls on re-render, following the same pattern used for other
modifiers.

### What shipped in the accessibility improvements diff

**1. SemanticPanel + `.Semantics()` modifier — custom automation peers for
composites.** This is the single most impactful accessibility feature. Reactor
components are C# records that can't override `OnCreateAutomationPeer()` on
WinUI Controls. `SemanticPanel` is a lightweight WinUI `Panel` that does override
it, providing a custom `SemanticPanelAutomationPeer` that exposes:
- Semantic role (mapped to `AutomationControlType`)
- Semantic value (exposed via `IValueProvider`)
- Range value, min, max (exposed via `IRangeValueProvider`)

```csharp
// A star rating built from Image elements can now tell screen readers
// "I am a slider with value 3 of 5 stars"
StarRating(value: 3, max: 5)
    .Semantics(role: "slider", value: "3 of 5 stars",
               rangeValue: 3, rangeMin: 0, rangeMax: 5)
```

The `.Semantics()` extension method wraps the target element in a `SemanticElement`
record, which the reconciler mounts as a `SemanticPanel` containing the child.
The panel uses single-child passthrough layout (measure/arrange delegates to the
child), so the visual appearance is unchanged.

**2. AccessibilityScanner — runtime WCAG 2.1 diagnostic scanner.** A post-
reconciliation scanner that walks the Reactor virtual element tree and produces
structured diagnostics. Each diagnostic includes:
- Diagnostic ID (e.g., "A11Y_001"), severity, message
- WCAG 2.1 criterion reference
- Element type, AutomationId, component type
- Fix suggestion (which modifier to add, suggested value, code snippet)
- Rich context (parent names, nearest heading, sibling texts, child content)

The context harvesting is designed for AI-agent consumption: an agent can
generate semantically correct fixes from the JSON export alone, without
re-reading source code. The scanner is intended for DEBUG builds only, accessed
via `ReactorHostControl.EnableAccessibilityDiagnostics`.

**3. Three Roslyn accessibility analyzers (REACTOR_A11Y_001/002/003).**

| Analyzer | Severity | Detects | Suggests |
|---|---|---|---|
| REACTOR_A11Y_001 | Warning | `Button(icon, action)` where icon is not a string literal, missing `.AutomationName()` | Add `.AutomationName()` |
| REACTOR_A11Y_002 | Warning | `Image(source)` without `.AutomationName()` or `.AccessibilityHidden()` | Add alt text or mark decorative |
| REACTOR_A11Y_003 | Warning | Interactive elements (CheckBox, ToggleSwitch, Slider, ComboBox) without `.AutomationName()` | Add `.AutomationName()` |

Each has matching unit tests via `CSharpAnalyzerVerifier`. The analyzers walk
the fluent modifier chain upward from the call site to check for the presence of
`.AutomationName()` or `.AccessibilityHidden()`.

**4. UseFocusTrap hook — modal focus management.**

```csharp
var trap = UseFocusTrap(isActive: isDialogOpen);
Dialog(content).Set(el => trap.SetContainer(el))
```

`FocusTrapHandle` hooks into the `LosingFocus` event on the container element.
When active, it cancels any focus change that would move focus outside the
container by checking `IsDescendantOf(newFocus, container)` via a visual tree
walk. The hook activates/deactivates reactively based on the `isActive` flag.

**5. LabeledBy deferred resolution.** Previously, `LabeledBy` was defined in
`AccessibilityModifiers` but `ApplyAccessibilityModifiers()` had no code to
apply it. Now the reconciler resolves `LabeledBy` by finding the target element
via `FindByAutomationId()`. During mount, the target may not be in the visual
tree yet (XamlRoot is null), so resolution defers to the `Loaded` event:

```csharp
// If target isn't found at mount time, defer to Loaded
fe.Loaded += OnLoaded; // resolves FindByAutomationId in Loaded handler
```

**6. ElementPool a11y cleanup.** The pool now clears all 14 UIA properties
(`Name`, `AutomationId`, `HelpText`, `FullDescription`, `LandmarkType`,
`AccessibilityView`, `IsRequiredForForm`, `LiveSetting`, `PositionInSet`,
`SizeOfSet`, `Level`, `ItemStatus`, `LabeledBy`, `HeadingLevel`) plus
`AccessKey` on pool return. This prevents stale accessibility state from
leaking across element reuse.

**7. Heading/SubHeading DSL auto-set HeadingLevel.** The `Heading()` factory
now sets `AutomationHeadingLevel.Level1` and `SubHeading()` sets `Level2`
automatically. Previously, heading structure required explicit
`.HeadingLevel()` modifiers.

**8. a11y-showcase sample app.** A dedicated accessibility sample demonstrating
all features: SemanticPanel usage, scanner output, form patterns, heading
structure, landmarks, live regions.

**9. New tests (1,450+ lines).** `AccessibilityScannerTests` (324 lines),
`AccessibilityAnalyzerTests` (249 lines), `A11yShowcaseScannerTest` (185 lines),
`AccessibilityInteractionTests` (298 lines, E2E via Appium), plus selfhost
fixtures and navigation stress tests.

### What's actually good about this (credit where due)

The tiered storage design is smart engineering. Most elements in a typical UI
need zero accessibility annotations (WinUI's built-in automation peers handle
the basics). The few that need annotations usually need only Tier 1 (a heading
level, a tab stop). The rare elements that need advanced annotations (landmarks,
live regions, position-in-set) get a lazy sub-record. This means the common
case (no a11y modifiers) pays zero cost, the typical case (one or two modifiers)
pays minimal cost, and only the advanced case allocates the sub-record. This is
better than a flat struct with 16 nullable fields on every element.

The E2E test approach is genuinely rigorous. Testing through the real UIA
pipeline (out-of-process via WinAppDriver) validates what assistive technology
actually sees, not what the framework thinks it set. This is a higher bar than
React's `eslint-plugin-jsx-a11y` (which checks markup, not runtime behavior) or
SwiftUI's accessibility inspector (which is a developer tool, not a CI test).
If these tests pass, Narrator will actually read the right values. That matters.

The WCAG criterion mapping in the tests is good practice. Each test says
*which* accessibility requirement it validates. This makes it possible to answer
"do we cover WCAG 1.3.1?" by grepping the test file rather than reading
implementation code.

**SemanticPanel solves the right problem with the right WinUI mechanism.** The
previous review called custom automation peers "architecturally blocked" because
Reactor components can't override `OnCreateAutomationPeer()` on Controls. The
`SemanticPanel` solution is elegant: a real WinUI `Panel` subclass that *can*
override `OnCreateAutomationPeer()`, wrapping the composite component's content.
The panel delegates layout to its single child (passthrough measure/arrange), so
it's visually transparent. The custom peer implements `IRangeValueProvider` and
`IValueProvider`, which are the two most common patterns for composite widgets
(sliders, ratings, progress indicators, gauges). SwiftUI's
`.accessibilityRepresentation {}` and Compose's `Modifier.semantics { role =
Role.Slider }` solve the same problem — SemanticPanel is Reactor's answer, and
it works within WinUI's automation peer model rather than fighting it.

**The three-layer diagnostic approach (compile-time + runtime + E2E) is now
comprehensive.** Roslyn analyzers catch missing `AutomationName` at edit time.
The `AccessibilityScanner` catches issues at runtime after reconciliation. The
Appium UIA tests validate that properties actually reach assistive technology.
This is a defense-in-depth strategy that covers the full development lifecycle.
No other C# UI framework has all three layers — WinUI has Accessibility Insights
(external tool), but nothing at compile time or framework-integrated at runtime.

**Auto-setting HeadingLevel on Heading/SubHeading is a pit-of-success design.**
Every `Heading("Title")` now automatically exposes as Level1 to screen readers.
This means developers who use the semantic DSL functions get correct heading
structure for free. The alternative — requiring `.HeadingLevel(Level1)` on every
heading — was an opt-in model that most developers would skip.

**UseFocusTrap addresses a real production need.** Modal dialogs that don't
trap focus are a WCAG 2.4.3 violation and a screen reader usability disaster.
The `LosingFocus` event approach is the correct WinUI mechanism — it fires
before focus actually moves, and `Cancel` prevents the move. This is better
than the alternatives (keyboard event interception, which misses programmatic
focus changes).

### What's still concerning (skeptic's view)

**1. SemanticPanel adds yet another invisible wrapper element.** This is the
fourth invisible wrapper in the stack (component Border, NavigationHost Grid,
CommandHost Grid, and now SemanticPanel). A composite component with semantic
annotations inside a navigation host produces: NavigationView > ... > Border
(component) > Grid (nav host) > SemanticPanel > content. Each extra layout
container costs measure/arrange time. SemanticPanel uses passthrough layout
(delegates to single child), which minimizes the cost, but it's still a real
WinUI element in the visual tree.

The alternative — having the reconciler set automation properties directly on
the child's WinUI control without a wrapper — would avoid this overhead. But
WinUI's automation peer model is per-control (only the control that overrides
`OnCreateAutomationPeer()` gets the custom peer), so the wrapper is
architecturally necessary. This is an honest trade-off, not a design mistake.

**2. SemanticPanel only supports Value and RangeValue patterns.** WinUI's
automation system has ~20 control patterns (`ISelectionProvider`,
`IToggleProvider`, `IExpandCollapseProvider`, `IScrollProvider`,
`IInvokeProvider`, etc.). SemanticPanel implements `IValueProvider` and
`IRangeValueProvider`. A custom tree control that needs to expose
`IExpandCollapseProvider`, or a custom multi-select widget that needs
`ISelectionProvider`, can't use SemanticPanel. The two patterns cover the
most common composite widgets (sliders, ratings, gauges), but the long tail
of automation patterns is unaddressed.

SwiftUI's `.accessibilityRepresentation {}` lets you provide an arbitrary
native view as the accessibility representation. Compose's `Modifier.semantics
{}` supports arbitrary role, state, and action descriptions. Both are open
systems. SemanticPanel is closed to two patterns — it's a targeted solution,
not a general one.

**3. The semantic role is a string, not an enum.** `.Semantics(role: "slider")`
uses a bare string for the role. A typo like `"slidre"` silently maps to
`AutomationControlType.Custom` (the fallback in `MapRoleToControlType`). There
are ~40 `AutomationControlType` values; SemanticPanel's string-to-enum mapping
covers a subset. An enum-based API (e.g., `SemanticRole.Slider`) would provide
compile-time safety and IntelliSense discoverability. The string approach is
more flexible but loses the type safety that Reactor prizes elsewhere.

**4. AccessibilityScanner is runtime-only and DEBUG-only.** The scanner walks
the element tree after reconciliation and produces diagnostics. But it requires
actually rendering the UI — you can't scan a component's accessibility from a
unit test without a WinUI host. The Roslyn analyzers (REACTOR_A11Y_001/002/003)
cover the compile-time case, but they only check for missing `AutomationName`
on three specific patterns (icon buttons, images, interactive elements). The
scanner catches more issues (heading structure, landmark coverage, form
labeling) but only at runtime. For comparison, React's `eslint-plugin-jsx-a11y`
catches a broader set of issues at edit time without running the app.

**5. UseFocusTrap traps but doesn't cycle.** The `LosingFocus` handler cancels
focus changes that would leave the container, which prevents Tab from escaping
a modal dialog. But when the user tabs past the last focusable element in the
trap, focus simply stays on that element — it doesn't wrap to the first
focusable element. The WAI-ARIA Dialog Pattern specifies that Tab should cycle
within the dialog. React Focus Lock, the de facto React modal focus library,
cycles Tab and Shift+Tab. Reactor's trap prevents escape but doesn't implement
the full cycling behavior that accessibility guidelines expect.

Additionally, `UseFocusTrap` requires `.Set(el => trap.SetContainer(el))` to
wire the container — the escape hatch is needed to connect the hook to the
DOM. A first-class integration (e.g., a modifier or a `FocusTrapHost` element)
would be more consistent with the framework's declarative model.

**6. Remaining imperative accessibility hooks are still unbuilt.** UseFocusTrap
is the first imperative accessibility hook. But the accessibility design doc
specifies several more:

- `UseAnnounce()` — triggering a live-region announcement from code (e.g., "3
  items deleted") without needing a visible element
- `UseReducedMotion()` — respecting user's motion preferences
- `UseScreenReaderActive()` — adapting UI when a screen reader is running

`UseHighContrast()` is partially covered by `UseColorScheme()` (returns
`ColorScheme.HighContrast`), though that hook still reads app-level theme, not
element effective theme (see Section 6 critique). A developer who needs to
announce a toast message to screen readers today has no option except `.Set()`
on a hidden live-region element.

**7. REACTOR_A11Y analyzer coverage is narrow.** REACTOR_A11Y_001 only matches
`Button` by identifier name, not by type resolution — if someone writes
`var b = Button(icon, action)` and then chains modifiers on `b` in a
separate statement, the analyzer misses it. REACTOR_A11Y_001 doesn't check
`AppBarButton`, `ToggleButton`, `SplitButton`, or `RepeatButton` — all of
which can be icon-only. REACTOR_A11Y_003 checks `CheckBox`, `ToggleSwitch`,
`Slider`, `ComboBox` but misses `RadioButton`, `TextBox`, `PasswordBox`,
`AutoSuggestBox`. The analyzers are a start, but the false negative rate
will be high for real codebases that use the full control surface.

**8. Focus management beyond trapping is still missing.** UseFocusTrap
addresses one focus scenario (modal dialogs). But there's still no:

- Programmatic focus control (`FocusRequester` / `@FocusState` equivalent)
- `XYFocusUp/Down/Left/Right` for directional D-pad/gamepad navigation
- Focus restoration on back-navigation

SwiftUI's `@FocusState` + `.focused()` and Compose's `FocusRequester` +
`Modifier.focusable()` both provide programmatic focus management. Reactor covers
Tab order and trapping but not the programmatic side.

**9. E2E interaction tests have improved but still have gaps.** The new
`AccessibilityInteractionTests` (10 methods via Appium) test keyboard
navigation, live regions, headings, and semantic panels through the UIA
pipeline. This addresses the previous critique that "the test suite validates
modifiers but not interaction patterns." However, the tests still don't cover:

- Focus restoration after dialog dismiss
- High contrast rendering (are all elements visible in HC mode?)
- Reduced motion behavior
- SemanticPanel range value interaction (can AT change the value?)

**10. The original showcase apps still don't demonstrate accessibility.** The
Outlook clone, file manager, registry editor, and word puzzle game still don't
use any accessibility modifiers. The new a11y-showcase sample exists, but it's
an isolated demo — not a proof that accessibility composes naturally in a real
app. When the framework's most complex apps don't use heading structure,
landmarks, or live regions, it signals that accessibility is still an add-on,
not a default. The Heading/SubHeading auto-HeadingLevel fix helps — apps using
those DSL functions get heading structure for free — but that's a narrow
improvement, not a systemic adoption of accessibility in the showcase apps.

### Revised accessibility verdict

**Previously: D- → C+. Now: B-.**

The improvement from C+ to B- is earned by addressing the three hardest
critiques from the previous review:

1. **Custom automation peers were "architecturally blocked."** SemanticPanel
   provides a working solution — composite components can now describe their
   role, value, and range to screen readers. The solution adds a wrapper
   element and only covers two automation patterns (Value, RangeValue), but
   it's a real answer to the hardest problem.

2. **"No accessibility diagnostics or linting."** Three Roslyn analyzers
   provide compile-time checks. AccessibilityScanner provides runtime WCAG
   diagnostics with structured JSON output. Between them, the development
   lifecycle has coverage from edit-time to runtime.

3. **"No accessibility hooks."** UseFocusTrap is the first imperative
   accessibility hook. It handles the most critical scenario (modal dialogs)
   but needs cycling behavior and better integration with the declarative model.

The grade is B-, not B, because: SemanticPanel is closed to two patterns (the
competition's semantic systems are open), the semantic role is stringly-typed,
UseFocusTrap doesn't cycle (WAI-ARIA Dialog Pattern expects wrapping), the
remaining imperative hooks (UseAnnounce, UseReducedMotion, UseScreenReaderActive)
are still unbuilt, the analyzers have narrow coverage, and the showcase apps
still don't use accessibility features.

The competition gap has narrowed meaningfully. SwiftUI's
`.accessibilityRepresentation {}` is still more flexible than SemanticPanel
(arbitrary view as accessibility proxy vs. two fixed patterns). Compose's
`Modifier.semantics {}` supports arbitrary roles, states, and actions. React
has ARIA on DOM elements plus a mature lint ecosystem. But Reactor now has
something at each layer: annotations (16 modifiers), semantics (SemanticPanel),
behavior (UseFocusTrap), compile-time diagnostics (3 analyzers), and runtime
diagnostics (AccessibilityScanner). The coverage is uneven — some layers are
thin — but the shape of a complete accessibility system is visible for the first
time.

---

## 11a. Charting and Chart Accessibility

### Why this warrants its own section

The critical review has not previously covered charting because the charting
surface was a separate experimental project — a D3-port demo gallery with no
framework integration. In the last 72 hours, that changed dramatically.
Spec 026 (840 lines) landed, followed by 9 implementation phases (commits
`d4893eb..225655c`, PR #54, ~3,000 lines of source plus 1,500+ lines of
fixtures and tests). The result is a full charting sub-framework with 43
chart samples, an 8-layer accessibility infrastructure, theme-aware brushes
for dark mode, a native `TitleBar` shell in the sample app, forced-colors
remapping, reduced-motion honoring, and 12 chart-specific scanner rules.

**This is a genuine sub-framework now, not a demo.** WinUI has no built-in
chart library — enterprise WinUI apps typically use LiveCharts, OxyPlot,
or the Telerik/DevExpress commercial controls. Reactor shipping an
accessible D3-equivalent is a real competitive position. SwiftUI ships
Apple's Charts framework (iOS 16+, 50+ chart types, audio graphs),
Compose has Vico and community libraries, Jetpack has no canonical
choice. Reactor's charting + accessibility is arguably ahead of everything
except Apple Charts on accessibility depth — and Apple has the advantage
of OS-integrated VoiceOver sonification.

### What shipped — the 8-layer architecture

**Layer 1: Automation peer infrastructure.** `ChartAutomationPeer` implements
`IGridProvider` (series × points grid) and `ITableProvider` (column
headers for axis labels). `ChartPointProvider` exposes per-point peers with
`IValueProvider`, and commit `9d0b4f9` adds `IScrollProvider` for large
datasets. `IChartAccessibilityData` is the accessor contract charts
implement to expose their data model to the peer.

**Layer 2: Per-point labels and auto-summarization.** `ChartSummarizer`
generates screen-reader summary text: "Line chart, 5 series, 120 points.
Revenue trending upward by 12% over 6 months. Peak: $42K in October."
`.Description(accessor)` lets apps override per-point labels; defaults
cover the common cases.

**Layer 3: Alternate-view convention.** `.AlternateView(Element)` attaches
a developer-supplied alternate (usually a DataGrid). `ChartAlternateViewWrapper`
handles the T-key toggle, focus save/restore, and live-region announcement
("Switched to table view"). The framework owns the keybinding and focus
lifecycle; the app owns the table content.

**Layer 4: Keyboard navigation.** `ChartKeyboardNavigator` implements:
arrow keys walk points/series, Home/End jump to edges, Ctrl+arrow steps
by series/axis, +/- zoom, Shift+arrow brush, L focuses legend, T toggles
alternate view. This is the Highcharts/Power BI keyboard model translated
to WinUI.

**Layer 5: Focus context.** `ChartFocusContext` captures and restores
focus across view transitions (chart → alternate view → back to chart,
preserving the previously-focused point).

**Layer 6: Live announcements.** `ChartLiveAnnouncer` is a debounced
(400ms trailing) live-region announcer for dynamic changes — brush
selection, zoom level, filter changes. Polite region for normal updates,
assertive only for errors. Scanner rule A11Y_CHART_007 warns against
`.AnnounceEveryFrame()` abuse.

**Layer 7: Forced colors, reduced motion, double encoding.**
`ChartPalette.ForcedColors` swaps to `[CanvasText, Highlight, LinkText,
GrayText]` when Windows high-contrast mode is active. `ChartPalette.Harden`
runs LCH-space lightness adjustment on a brand palette to meet WCAG AA
contrast against a given background — deterministic, not heuristic.
`ChartPalette.ColorblindSimulate` uses a simplified Brettel matrix for
deuteranopia/protanopia/tritanopia. `ReactorHost` honors
`WindowsThemeSettings.HighContrast` and `UISettings.AnimationsEnabled`,
wiring reduced-motion via a render context flag. All series are double-
encoded (color + shape + dash) by default — `.ColorOnly()` is an opt-out
that triggers a warning.

**Layer 8: Scanner rules.** 12 chart-specific rules
(A11Y_CHART_001..012) detect: missing title, missing axis labels, missing
per-point labels, color-only encoding, insufficient contrast, palette
hardening suggestions, animate-every-frame abuse, sparse data handling,
tiny hit targets, and more. The scanner's rich context (WCAG criterion,
suggested fix with `reactor charts harden` code snippet) targets AI-
agent consumption.

### What's actually good about this (credit where due)

**The `Harden` utility is rare and valuable.** Most frameworks punt on
"make my brand palette accessible." `ChartPalette.Harden(brandColors,
background)` returns a new palette with LCH lightness adjusted to meet
WCAG AA contrast. Deterministic. Per-color. Embedded in scanner fix
suggestions. No other C# declarative framework has this — Highcharts has
a similar feature via CSS custom properties, but it requires manual CSS;
Reactor's is a one-line C# API.

**Double-encoding is on by default.** Color + shape + dash are applied
automatically for every series. This is the most common accessibility
failure in charts (color-only bar charts, color-only line charts) and
Reactor prevents it at the framework level. The `.ColorOnly()` opt-out
exists for when it's justified but warns via the scanner.

**The 43-sample gallery got made accessible in one pass.** Commit
`229e41b` updated every chart sample (AnimatedDonut, BarChart, BoxPlot,
CandlestickChart, ChordDiagram, ForceDirectedGraph, Sankey, Sunburst,
Treemap, 34 others) to use `.Title()`, `.Description()`, and the a11y
infrastructure. No new chart types were added; existing ones were
retrofitted. This is exactly the "features compose in real apps" work
the previous review criticized the framework for *not* doing. It's a
real counter-example — one domain where the framework's own showcase
actually uses its own new features comprehensively.

**The reduced-motion integration is framework-level.** `ReactorHost` now
reads `UISettings.AnimationsEnabled` and exposes a render-context flag.
Charts (and any other animation consumer) can check this flag and skip
entry/exit animations. This is better than the `UseReducedMotion` hook
that Section 11 criticized as "still unbuilt" — charts have it, generic
components don't yet.

**Scanner rules with `reactor charts harden` code snippets.** A11Y_CHART_
fix suggestions are structured for agent consumption: the fix includes the
exact CLI command to run and the exact C# modifier to add. This is the
payoff of the scanner-diagnostic investment — an AI agent reading the
scanner output can generate a PR without re-reading source.

### What's still concerning (skeptic's view)

**1. No sonification.** Apple Charts supports audio graphs — each point
plays a tone proportional to its Y-value, letting blind users detect
patterns they can't see. Highcharts has a sonification plugin. Reactor's
spec explicitly lists sonification as an "A+ ceiling feature deferred" — a
feature gap versus the named competitors. Not shipping this is defensible
(it's a lot of engineering), but it's the single biggest remaining
accessibility differentiator.

**2. ForceGraph a11y is explicitly decorative-only.** Spec 026 §11 says
"force graph physics is decorative; accessibility ships as structure, not
motion." A screen reader user on a ForceDirectedGraph gets the node list
and edge list but no way to explore the graph interactively. For dense
graphs (>200 nodes), this is a non-trivial UX problem. The spec asks
whether a dialog-based "graph explorer" would be better (open question 1).
No answer yet.

**3. Hit-target expansion requires `.Interactive()`.** Points smaller than
24×24px get a transparent hit-shape overlay to meet WCAG 2.5.8, but only
when `.Interactive()` is set. Static charts — which are the majority of
analytics dashboards — don't get this. Accessibility of static charts
should not depend on developer action; this should be always-on, with
opt-out for decorative cases.

**4. Forced-colors palette clips to 4 series.** Under Windows high-
contrast, series cycle over 4 system colors (`CanvasText`, `Highlight`,
`LinkText`, `GrayText`). Beyond 4 series, colors collide and the only
remaining distinction is shape/dash. Spec 026 notes this clipping but
doesn't address the graceful fallback — what's the UX when you have 12
series in HC mode? No guidance.

**5. Per-point `ChartPointProvider` instances allocate under inspection.**
Every time an assistive tech walks the UIA tree, `ChartPointProvider` peers
are created for every data point. A 10,000-point scatterplot creates 10,000
peer instances. They're lightweight, but under stress (repeated UIA walks
from Narrator + automation tests) the allocation rate could matter. No
benchmarks in the spec or tests.

**6. Sparse data / single-point charts unspecified.** Open question 4 asks
whether sparklines <80×40px should skip full UIA grid exposure. Still
unresolved. A stock ticker with 1,000 mini-sparklines all advertising a
full grid peer is pathological but unconstrained by the current design.

**7. "Alternate view" convention depends on app-supplied content.** The
framework owns the T-key binding and the focus transition, but the app
supplies the `.AlternateView(Element)` — usually a DataGrid. A naive app
will supply a static table that doesn't mirror filter/sort state of the
chart. There's no canonical sample of a fully-integrated alternate view
(dynamic, sorted, filtered to match the chart). Open question 5 from the
spec asks for this; it didn't ship in phase 1-9.

**8. Live-region debounce is hard-coded to 400ms.** No way to customize.
For truly time-critical scenarios (live-updating alert dashboard), 400ms
trailing feels slow. For UI churn (rapid keyboard zoom), 400ms is
reasonable. A single hard-coded value can't serve both.

**9. Chart accessibility is not integrated with `AccessibilityScanner`'s
general rules.** The chart-specific rules (A11Y_CHART_001..012) live in the
same scanner but don't cross-reference the general a11y rules. A chart
without `.Title()` fails A11Y_CHART_001 *and* A11Y_001 ("element has no
name"). Both warnings fire. This is noise — either the rules should
merge when they overlap, or one should defer to the other.

**10. Testing is thorough on the selfhost path but sparse on E2E.** The
`ChartAccessibilityFixtures` file (343 lines after phase 4) is a selfhost
fixture battery covering peer creation, summarizer output, palette
hardening, scanner rules. But the Appium E2E coverage for charts is thin —
`ChartAccessibilityTests.cs` is 153 lines, roughly 8–10 tests. Narrator /
NVDA actual-behavior validation (the thing that really matters for a11y)
requires manual testing in the `charting/App.cs` doc-app.

### Charting verdict

**New section, first grade: B+.**

The charting infrastructure and its accessibility layer are the most
comprehensive I've seen in any C# declarative framework. The architectural
choices (layered accessibility, scanner integration, LCH-space palette
hardening, forced-colors honoring, framework-level reduced-motion) are
principled and well-executed. The 43-sample gallery getting retrofitted
in one pass is exactly the "features compose in real apps" behavior the
framework needs to show more of.

The grade is B+, not A, because: no sonification (a real competitive gap
versus Apple Charts and Highcharts), ForceGraph a11y is decorative-only,
hit-target expansion is opt-in when it should be always-on, per-point
peer allocation is unbounded, the "alternate view" convention lacks a
canonical fully-integrated sample, live-region debounce is hard-coded,
chart scanner rules don't merge with general rules, and E2E test
coverage is thin. The layered design is right; the corners haven't been
swept yet.

This is also a new sub-framework that hasn't been stress-tested in real
apps. `ReactorCharting.Gallery` is comprehensive but synthetic. No
third-party dashboard app has adopted it. No performance characterization
under 10k+ points. No survey of screen reader users validating the
summarizer output. The foundation is strong; the field evidence is zero.

---

## 12. Input Handling and Events

### What Reactor has — after spec 027

This section was largely rewritten by commit `76d0f51` (spec 027 —
"declarative pointer, gesture, focus, and drag-drop modifiers," 73 files,
+7,249 / −2,137). Four previous critiques are closed, and a fifth (event
handler re-attachment churn) is addressed by a new trampoline dispatch
model. What now exists:

- **Semantic events on controls:** `OnClick`, `OnChanged`, `OnSelectionChanged`
  — unchanged, well-covered for all wrapped controls.
- **Pointer modifiers (full surface):** `.OnPointerPressed`, `.OnPointerMoved`,
  `.OnPointerReleased`, `.OnPointerEntered`, `.OnPointerExited`,
  `.OnPointerCanceled`, `.OnPointerCaptureLost`, `.OnPointerWheelChanged`.
  Eleven previously-absent handlers shipped as first-class modifiers.
- **Tap-family modifiers:** `.OnTapped`, `.OnDoubleTapped`, `.OnRightTapped`,
  `.OnHolding` — with the corresponding `Is{Tap,DoubleTap,RightTap,Holding}Enabled`
  flags auto-toggled by the reconciler when a handler is attached/detached.
- **Keyboard modifiers:** `.OnKeyDown`, `.OnKeyUp`, `.OnPreviewKeyDown`,
  `.OnPreviewKeyUp`, `.OnCharacterReceived`.
- **Focus modifiers + hook:** `.OnGotFocus`, `.OnLostFocus`, plus a
  `UseElementFocus()` hook and a `FocusManager` helper for programmatic
  focus — the first imperative focus primitive the framework has had.
- **Gesture recognizers (Tier 3):** `.OnPan(minimumDistance, axis, withInertia)`,
  `.OnPinch(withInertia)`, `.OnRotate(withInertia)` with typed
  `PanGesture`/`PinchGesture`/`RotateGesture` records carrying phase,
  translation/delta/velocity (pan), scale/anchor (pinch), angle (rotate),
  and an `IsInertial` flag. Backed by a single
  `ManipulationStarted/Delta/Completed` subscription per element,
  `ManipulationMode` auto-computed from attached configs.
- **Drag-and-drop:** `.OnDragStart<TPayload>` + `.OnDrop<TPayload>` for typed
  round-trip (same-process), plus untyped overloads for cross-process, plus
  `.OnDragEnter` / `.OnDragOver` / `.OnDragLeave`. `DragData` supports typed
  payloads, standard formats (text / URI / HTML / RTF / files / bitmap), and
  custom format ids; each format has eager / sync-provider / async-provider
  overloads wired to `DataPackage.SetDataProvider` so rendering HTML or
  rasterizing a bitmap only happens if a drop target actually asks for it.
  `CanDrag` / `AllowDrop` auto-set. `DragOperationNegotiation` picks an
  operation when source and target disagree.
- **Event dispatch model (Tier 2 trampolines):** `EventHandlerState` now
  attaches a stable trampoline delegate to each WinUI event once per
  element lifetime; updating the user handler is a single field write, not
  a detach/attach round-trip. ETW (keyword `EventDispatch`, 0x40) emits
  `EventTrampolineAttached` (first attach) and `EventTrampolineDispatch`
  (per fire) so the one-time-attach invariant is observable.
- **Keyboard accelerators:** unchanged — `Accelerator(key, modifiers)` data
  records (Section 8).
- **Escape hatch:** `.Set()` is still available for truly exotic events
  (`UIElement.ProcessKeyboardAccelerators`, `CharacterReceived` on
  non-FrameworkElements, ink input, etc.).

### Critiques

**1. Gesture composition is missing.** SwiftUI's gesture API provides
`.simultaneously(with:)`, `.sequenced(before:)`, and `.exclusively(before:)`
so you can declare "pinch and pan at the same time" or "long-press then
drag." Reactor has three discrete gesture primitives, but they don't
compose declaratively — if you want simultaneous pan+pinch on the same
element you can just attach both (the single ManipulationStarted/Delta/
Completed subscription handles it), but conditional sequencing or priority
ordering has no API. Compose's `pointerInput { detectTransformGestures { }
}` handles pan+pinch+rotate in one callback with a shared event loop;
Reactor's three separate callbacks get separate delta events and have to
reconcile across them if a component wants unified transform state.

**2. LongPressGesture is an event, not a typed gesture.** SwiftUI has
`LongPressGesture(minimumDuration:)` that fires with phase and passes the
recognized duration. Reactor has `.OnHolding` which surfaces the raw
`HoldingRoutedEventArgs` — no phase-typed record, no min-duration
configuration on the modifier (the caller filters inside the handler by
reading `HoldingState`), and no unification with `.OnPan` for the common
"long-press to begin drag" interaction. Android and iOS both treat this as
a single composable gesture.

**3. Gesture recognizers rely on WinUI manipulations and inherit its
constraints.** `.OnPan/.OnPinch/.OnRotate` wrap
`UIElement.ManipulationStarted/Delta/Completed`. This means: pan only
reports translation in screen-axis-aligned space (no rotated-container
coordinate math), rail behavior is WinUI-native (can't be disabled on
inertia), `ManipulationCompleted` fires once for all gestures that
completed together, and inertia deceleration rate isn't exposed — the
`withInertia` bool toggles inertia on/off but you can't tune the decay.
Compose and SwiftUI both expose per-axis deceleration parameters.

**4. Drag-and-drop has 370 lines of new `DragData` with no field
evidence.** The API is thoughtfully designed (eager + sync-provider +
async-provider per format, typed round-trip via
`reactor/typed/<typeof(T).FullName>` format ids, per-drag GUID in
`DataPackage.Properties` for same-process transfer). But:

- The typed-format-id is `typeof(T).FullName` — if a class is renamed or
  moved to a different namespace between the drag source and drop target
  (during a hot-reload session, or across two differently-versioned
  assemblies in a multi-process app), the payload won't round-trip. There's
  no typed-format-id versioning story.
- The format dictionary uses `StringComparer.Ordinal` over string keys.
  Reactor's theme throughout this review — "stringly-typed APIs at
  boundaries" — applies here too.
- No DnD-specific devtools: you can't inspect what formats a drag is
  advertising, trace which provider was invoked, or see negotiation
  outcomes. For a subsystem this large, the observability gap is
  conspicuous.
- Zero field evidence. The feature is in spec 027 phase 6-8 plus the E2E
  fixtures landed the same day; no showcase app consumes it. This is the
  "feature velocity outpaces integration velocity" pattern from the
  conclusion, landing again.

**5. Trampoline dispatch is unverified at scale.** Phase 2's claim is
that attaching a stable trampoline once per event per element lifetime
eliminates per-render COM churn on the hot `.OnPointer*`/`.OnKey*`
path. This is architecturally correct and the ETW instrumentation lets
you confirm the invariant after the fact. But:

- It introduces a new category of bugs: the trampoline is attached
  permanently, so if the `EventHandlerState` isn't cleaned up on unmount
  or element-pool return, handlers leak. The PR adds
  `TrampolineFixtures` covering "latest-handler-wins across 100
  re-renders" but not "trampoline detached on unmount" explicitly.
- There's no benchmark yet showing the hot-path delta. "N × events ×
  subscribe/unsubscribe COM calls per update" was the previous
  observed cost; the new model claims a single field write, but
  nobody has measured a real app render cycle before/after. The
  ETW events exist; running a trace isn't the same as running a
  benchmark.
- All 6,390 unit tests still pass, and the Phase 2 commit message
  calls that out, but the unit suite is largely WinUI-free and
  doesn't exercise the COM interop path the claim depends on.

**6. Auto-enable / auto-fill is convenient but silently mutates control
state.** When you attach `.OnTapped`, the reconciler sets
`IsTapEnabled = true`; when you attach any pointer-family handler on a
`Shape`, it auto-fills a null `Shape.Fill` with a transparent brush so
hit-testing works. Both are right defaults — but they mean a developer
inspecting the live visual tree will see properties they didn't set,
without any breadcrumb pointing at the modifier that set them. The
reverse path (detach the last handler → clear the auto-flag) is
implemented, but conflicts with user-set values (what if the user set
`IsTapEnabled = true` via `.Set()`?) aren't handled explicitly.

**7. Commanding still doesn't cover all input surfaces.** Unchanged from
the previous review. `Command` bundles execute + canExecute + metadata,
but only `Button`, `AppBarButton`, and `MenuItem` integrate. `SplitButton`,
`SwipeItem`, `ContentDialog` actions still take bare `Action` callbacks.
And command routing to the focused view is still missing, so Cut/Copy/
Paste in multi-panel apps continues to require manual wiring.

**8. Focus hook ships but stops short of `@FocusState`.** `UseElementFocus`
returns `(ElementRef, Action RequestFocus)`, and the new `.Ref(target)`
modifier binds the ref fluently — that's the right ergonomic shape and
closes the "wire it via `.Set()`" concern cleanly. What it doesn't give
you is a *scoped* focus state: SwiftUI's `@FocusState var field: Field?`
models "which of these N fields is focused" as a single binding;
Compose's `FocusRequester` composes into `Modifier.focusRequester()`.
Reactor's pattern is one `ElementRef` per element you might want to
focus, each with its own `RequestFocus` closure — fine for "focus the
first input on mount," awkward for "the last-edited row of a datagrid."
Focus restoration after navigation or dialog dismissal (Section 11
critique #8) is still unaddressed.

**9. No pointer-capture ergonomics.** `.OnPointerCaptureLost` fires on
loss, but there's no declarative `.CapturePointer(when:)` modifier.
Implementing a drag-to-draw surface still requires calling
`CapturePointer` / `ReleasePointerCapture` manually inside the handler —
a `.Set()` pattern. Compose's `awaitPointerEventScope` and SwiftUI's
`DragGesture` hide capture semantics entirely.

**10. Wheel and touchpad gestures are still second-class.**
`.OnPointerWheelChanged` exists, but there's no typed wheel delta
record, no momentum-vs-discrete classification (trackpad glide vs. mouse
scroll), and no horizontal-wheel shortcut. Scroll is handled by
`ScrollView` via virtualization; anything custom (zoomable canvases,
custom scrollable surfaces) has to parse `PointerPointProperties` by
hand.

---

## 13. Developer Experience

### What's good

- **Full IntelliSense and refactoring** — the C# DSL gets IDE support for free
- **Type safety** — mismatched types are caught at compile time
- **No XAML parsing errors** — a common WinUI pain point eliminated
- **No DataContext confusion** — data flows explicitly through props and state

### What's bad

**1. Hot reload exists but with .NET's inherent limitations.** Reactor hooks into
.NET's `MetadataUpdateHandler` via `HotReloadService.cs` — when code changes,
`ReactorApp.ActiveHost?.RequestRender()` fires and the UI updates while preserving
hook state (UseState values survive because the RenderContext stays in memory).
This works with both Visual Studio's hot reload and `dotnet watch`.

This is genuinely good — and better than "no hot reload." However, .NET hot
reload has well-known limitations: adding new fields, changing type hierarchies,
adding new classes, and lambda changes often require a full restart. These are
exactly the kinds of changes you make during UI development (adding a new
component, restructuring a layout). React's Fast Refresh and Compose's Live Edit
are purpose-built for UI changes and handle a wider range of edits.

**2. The preview system has been renamed to "devtools" and replaced, not just
augmented.** The previous `--preview [ComponentName]` CLI flag is gone.
Commit `27e89d9` renamed the surface to `--devtools`, and the preview
feature (live screenshot stream) now sits *inside* the devtools MCP server
(see Section 13a for the full devtools discussion). The VS Code extension
was updated with legacy fallback (`8828ab5`) so existing users shouldn't be
broken, but the naming change signals the team's priority: MCP automation
for AI agents is now the headline, live-screenshot-preview is a secondary
capability.

For non-agent users, this is a regression in mental model. The old `--preview`
flag was discoverable by name; `--devtools --screenshot` requires knowing
that screenshots are a devtools sub-capability. The VS Code extension's live
preview panel still works, but the top-level story is now "AI agents can
drive your app" rather than "look at your component without running the app."

The fundamental limitation remains: it's a live screenshot, not an
interactive preview in the IDE. SwiftUI Xcode Previews are interactive;
Compose Preview renders composables inline. Reactor's preview is still
"look at the app as it renders."

**3. Error messages from the reconciler are mostly runtime-only, but hook
rule violations are now caught at edit time.** Spec 020 shipped four hook
analyzers alongside async resources:

- `REACTOR_HOOKS_001` — hook called conditionally
- `REACTOR_HOOKS_004` — deps include freshly allocated values (array, closure)
- `REACTOR_HOOKS_005` — hook called outside Render or a custom-hook method
- `REACTOR_HOOKS_006` — UseResource fetcher looks like a mutation

This closes a specific gap the previous review flagged: "React has
`eslint-plugin-react-hooks`; Reactor has no static analysis for hook rule
violations." Now it does — for four rules. React's ESLint plugin has ~10
rules (exhaustive-deps, rules-of-hooks, and variants). Reactor has ~4.
Closer than before but not parity; the two missing-deps rules
(REACTOR_HOOKS_002/003) are listed as "require control-flow / data-flow
analysis" in `HookRulesAnalyzer.cs:35` — i.e., harder, deferred. The most
valuable ESLint rule (`react-hooks/exhaustive-deps`) is still missing.

**4. Debugging the reconciler now has MCP devtools (see Section 13a).** The
Claude-driven `reactor.tree`, `reactor.state`, and `reactor.logs` tools
replace manual `Debug.WriteLine`. The component tree is inspectable as JSON;
hook shape is readable; logs are rolling with per-call observability. For
developers who work with AI coding agents, this is a significant DX
improvement. For developers who don't, it's still a CLI-only flow —
there's no React DevTools-style UI showing the live component tree in
a browser panel.

**5. Performance profiling now exists via ETW, with limits.** Commit
`310a299` added `Microsoft-UI-Reactor` ETW provider with 6 keywords and 5
tasks: Reconcile start/stop, component render boundaries, state change,
MCP call, effects-flush, child-reconcile. This closes part of the "no
performance profiling" gap. Consumers use standard tooling — PerfView,
dotnet-trace, WPA — to view traces.

The grade is "exists but coarse":

- **What you can see:** Reconciliation duration, how many components were
  rendered, effects flush timing, MCP dispatch latency, the build tag
  emitted into traces for correlation.
- **What you still can't see:** Per-component render timing (only aggregate
  start/stop boundaries), per-hook execution timing (no breakdown inside
  `FlushEffects`), memory allocation per render cycle, render-tree diff
  shape, element-pool hit rate. React DevTools Profiler shows per-component
  render times and flame graphs. Compose Layout Inspector shows
  recomposition counts per composable. Reactor's ETW is *infrastructure*
  for profiling; the viewer and per-component drill-down still have to be
  built on top.

Additionally, ETW is Windows-only (not an issue given Reactor is WinUI-only),
classic-ETW tooling is intimidating for newcomers (PerfView has a learning
curve), and there's no built-in summary view for common questions ("which
component re-renders most frequently?"). Commit `a0ea276` added
`ReactorEventSourceCoverageTests.cs` (116 lines) which validates emit-guard
behavior but doesn't benchmark the overhead when tracing is on.

**6. Unit test coverage jumped meaningfully.** Commit `a0ea276` added 16
new test files totaling 4,222 lines of coverage tests — accessibility
scanner, attached extensions, curves, voronoi, lockfile registry, element
extensions, element records, intl accessor, navigation diagnostics,
property grid arrays, ReactorEventSource, theme tokens, Yoga layout, and
more. This is a real reliability investment. The previous review noted
that "E2E tests were silently broken" as a process gap; the coverage-test
expansion is the corrective action. Whether it catches all the important
regressions remains to be seen, but the *investment* is unambiguous.

---

## 13a. Devtools MCP and ETW Tracing

### Why this warrants its own section

In a three-day span, Reactor shipped ~60 commits under spec 024 (AI Agent
Devtools) and spec 025 (Devtools CLI Parity) that built out an MCP
(Model Context Protocol) server for AI-agent-driven UI automation. This
is unusual enough that it deserves its own section — no other C# UI
framework has anything comparable. Whether it's the right bet is a
separate question from whether it's well-built.

### What shipped

```bash
# Supervisor spawns the app with devtools enabled and exits cleanly
mur devtools ./App.csproj

# CLI parity — agents (or humans) can call MCP tools via `mur devtools call`
mur devtools call reactor.tree --window main
mur devtools call reactor.click --selector "Button#submit"
mur devtools call reactor.state --node "r:main/Counter"
mur devtools call reactor.screenshot --output screen.png

# Or use named verbs
mur devtools tree
mur devtools click "Button#submit"
mur devtools logs --tail 50

# MCP fragment for Claude Desktop / other MCP clients
mur devtools --print-config
```

Tools exposed over MCP (HTTP JSON-RPC on loopback + stdio as of phase 4.1):

| Tool | Purpose |
|---|---|
| `reactor.windows` | List open windows with ids and titles |
| `reactor.tree` | Full element tree with stable ids, view=full adds layout/context/visual |
| `reactor.state` | Hook shape (not values) for a node |
| `reactor.logs` | Rolling log with per-call observability |
| `reactor.screenshot` | PNG screenshot of a window |
| `reactor.click` / `reactor.invoke` | UIA invoke on an element |
| `reactor.type` / `reactor.focus` | Keyboard simulation |
| `reactor.toggle` / `reactor.select` / `reactor.scroll` | UIA pattern invocations |
| `reactor.fire` | Direct event injection for non-UIA controls |
| `reactor.waitFor` | Wait until a selector resolves |
| `reactor.switchComponent` | Swap the active preview component (invalidates stale tree ids — commit `274ec54`) |
| `reactor.reload` | Hot-reload the app (exit-code-42 restart pattern) |

Backing infrastructure: `DevtoolsMcpServer`, `McpDispatcher` (extracted
for testability, `d992189`), `NodeIdBuilder` + `NodeRegistry` with stable
window-scoped ids, `SelectorParser`, `WindowRegistry`, single-instance
enforcement via `LockfileRegistry` (pid probe + HTTP liveness check),
rolling log with per-call observability (`fc2f93f`), `--print-config` that
emits an MCP config fragment ready for paste into Claude Desktop
(`539b22e`), stdio transport alongside HTTP (`7cf59c7`), security review
document (`477bd17`).

### What's actually good about this (credit where due)

**The architectural choice to use UIA is correct.** Rather than reinventing
hit-testing or pixel automation, Reactor leans on Windows UI Automation —
the platform-native accessibility bus. Every WinUI control already has a
UIA peer; Reactor just routes `reactor.click` through `IInvokeProvider`.
This means custom controls don't need special support, and tests authored
against the devtools surface also validate accessibility (a test that can
find a control by name is a test that Narrator can find it too). Compared
to Playwright/Selenium-style pixel automation, this is a much better bus.

**Node ids are stable, human-readable, and window-scoped.** `r:main/Counter.btn-inc`
survives re-renders, theme changes, and re-layouts because it encodes
component tree position, not visual tree position. Window-scoped ids
(`r:<window>/<local>`) handle multi-window correctly. The selector
resolution order (explicit id → automation id → name → type+index → source
location) gives agents multiple escape routes when the primary path fails.

**The MCP surface is deliberately minimal in v1.** Read tree, read state,
invoke/click/type, screenshot, reload. No cache inspection, no hook
mutation, no tree diffing. The spec (`#10 open question`) explicitly says
these are deferred until real client-side patterns emerge. This is the
right discipline — ship what's needed, avoid speculative tools.

**Stdio transport landed alongside HTTP (phase 4.1, commit `7cf59c7`).**
This closes the obvious agent-integration gap: Claude Desktop and other
MCP clients that launch a stdio subprocess no longer need HTTP port
binding. This contradicts the spec's earlier defense of HTTP-only; the
team reversed course within the phase. The fact that it shipped quickly
is a positive signal, but the docs and earlier commits may still reflect
the HTTP-only design.

**Single-instance enforcement via lockfile is correct.** A lockfile with
pid-probe + HTTP liveness check prevents two devtools processes from
fighting over the node-id space. Stale lockfiles are detected and
cleared. This kind of infrastructure work is easy to skip; Reactor did
it properly.

**CLI parity (spec 025) means humans can use it too.** `mur devtools list`,
`mur devtools tree`, `mur devtools screenshot` work as standalone
commands — agents aren't the only consumer. The generic `mur devtools call
<tool> --args` escape hatch handles anything the named verbs don't. Commit
`4768224` addressed PR feedback on atomic write, lifetimes, and stdout
streaming — the review pass was thorough.

**Stress harnesses exist.** Commit `8562438` adds in-process and E2E stress
harnesses for MCP lifecycle — this is infrastructure the team cares about
stressing under concurrent load before shipping. Test coverage for the
devtools subsystem is genuinely robust (23+ integration tests via
`McpDispatcher` self-host fixtures, plus E2E stress).

### What's still concerning (skeptic's view)

**1. The premise is a bet.** Devtools MCP assumes AI agents are a
primary UI-debugging audience. If that bet is right, Reactor is uniquely
positioned. If it's wrong — if developers still prefer traditional GUI
devtools (React DevTools, Xcode Instruments) — then this infrastructure
serves a small user population. The competitive landscape today is React
DevTools and Layout Inspector; AI-agent automation is a supplement to
those, not a replacement. Reactor shipped the supplement before the
primary.

**2. State is read-only.** `reactor.state` returns hook shape but not
values (commit `bc132a3` — "shape, not values"). There's no `reactor.setState`
to mutate component state directly. The spec defends this as "agents
should use event handlers" — but in deterministic testing or
debugging, direct state mutation would be faster. An agent trying to
reproduce "the app in state X" has to compose a chain of event
invocations; TanStack Query Devtools lets you edit cache entries live,
which is one-click. The "shape not values" choice is privacy-conscious
but limits introspection power.

**3. Tree diffing is deferred.** The spec punts `reactor.snapshot(name)` /
`reactor.diff(A, B)` to phase 3, hypothesizing that agents will cache
trees locally and diff client-side. That's defensible when agents have
unlimited context, but agents running in constrained environments (CI,
token-budgeted LLM sessions) may not have the context window for paired
trees. Server-side diffing would be materially cheaper. No evidence yet
whether the hypothesis holds.

**4. Window lifecycle is polled, not pushed.** An agent calling
`reactor.windows` sees a snapshot. New windows aren't pushed; the
agent has to re-poll. For a short-lived modal that appears and closes
within 100ms, the agent might miss it entirely. No subscription/notification
model.

**5. No source mapping in v1.** The tree response includes a `reactor`
block intended to carry source file + line (from spec 010 Source
Mapping). That spec hasn't landed. Without it, agents can't correlate a
tree node back to the C# component that created it. This is the most
important agent capability — "show me the code that made this button"
— and it's a v1.1 at best.

**6. No cache inspection.** Async resources (Section 3a) ship with a
`QueryCache` but the devtools surface doesn't expose it. An agent
debugging "why is this query stale?" has no tool. TanStack Query
Devtools is the gold standard here; Reactor has nothing equivalent.

**7. No performance profiling via MCP.** ETW traces exist (see Section
13) but there's no `reactor.profile` tool to start/stop/export traces
from an MCP session. An agent suspecting a performance regression has to
spawn PerfView out-of-band. The profiling and the MCP surface are two
separate tools, not an integrated workflow.

**8. Selector ambiguity returns errors, not first-match.** Agents
hitting an ambiguous selector (e.g., `"Button"` with 3 buttons) have to
disambiguate and re-call. This is correct for determinism but chatty —
more turns = more LLM tokens = more latency. A `--first` or
`--require-unique` flag would let agents opt into the tradeoff.

**9. DEBUG-gated with risk of Release leak.** The devtools surface is
intended for DEBUG builds only, but it's controlled by a compile-time
`#if DEBUG` and a runtime flag. If a Release build accidentally enables
devtools (misconfigured env var, test flag left on), any local process
can drive the app. The security review (`477bd17`) addresses loopback-
only binding but doesn't prevent the accidental enablement path. A
startup banner warning when devtools is on in a Release-like build would
reduce the risk.

**10. The UI preview surface was replaced, not augmented.** The previous
`--preview` flag was purpose-built for human developers iterating on a
single component. `--devtools` is purpose-built for agents. The team
chose to *rename* preview → devtools (commit `27e89d9`) rather than
keeping both. The VS Code extension's legacy fallback keeps things
working, but the message is clear: agents are the primary audience.
Non-agent users are a supported secondary.

**11. ETW tracing lives separately from the MCP surface.** The
`Microsoft-UI-Reactor` EventSource and the devtools MCP server are
parallel telemetry surfaces — one streams ETW events, the other responds
to JSON-RPC calls. There's no integration (can't start an ETW trace via
MCP; can't correlate devtools activity to trace events by session id).
Two telemetry stories, no bridge.

### Devtools + tracing verdict

**New section, first grade: B.**

Devtools MCP is genuinely unique — no C# UI framework (and for that
matter, no declarative UI framework on any platform) ships an MCP server
for AI-agent-driven automation. The architectural choices (UIA as the
bus, stable window-scoped node ids, MCP as the protocol, stdio + HTTP
transports, CLI parity, single-instance enforcement) are correct. The
implementation (DevtoolsMcpServer, McpDispatcher, NodeRegistry,
SelectorParser, stress harnesses) is serious infrastructure.

ETW tracing exists but is infrastructure-grade rather than developer-
facing. Consumers need PerfView or dotnet-trace. Per-component render
timing and per-hook execution breakdowns are not emitted. There's no
built-in viewer.

The grade is B because: state is read-only, tree diffing is deferred,
source mapping is v1.1, cache inspection and performance profiling
aren't in the MCP surface, the audience is narrow (AI agents first,
humans second), and the VS Code extension's interactive-preview story
has regressed relative to the old `--preview` flag. The bet on AI
agents as a primary audience is unconventional; whether it pays off
depends on adoption patterns that don't exist yet.

---

## 14. The .Set() Problem

### The escape hatch that carries the framework

`.Set()` is Reactor's escape hatch for accessing any WinUI property that doesn't
have a first-class modifier. It takes a lambda that receives the underlying WinUI
control:

```csharp
Button("Click", onClick)
    .Set(b => b.FlowDirection = FlowDirection.RightToLeft)
    .Set(b => {
        b.PointerEntered += (_, _) => setHovered(true);
        b.PointerExited += (_, _) => setHovered(false);
    })
```

### Why this is a problem

**1. It breaks the declarative model.** The entire point of Reactor is declarative
UI. `.Set()` is imperative mutation. When you use `.Set()`, you're bypassing the
reconciler — the framework doesn't know what you changed and can't diff it. If
a control is recycled from the pool, your `.Set()` mutations from the previous
owner are still there (the pool only resets properties the reconciler knows
about).

**2. Event handlers wired in .Set() leak.** If you do
`.Set(b => b.PointerEntered += handler)`, that handler is wired on every update
(Set runs during mount AND update). You'll accumulate duplicate event handlers
unless you manually track and remove them. The framework provides no lifecycle
hook for this — no "cleanup on unmount" for Set-based side effects.

**3. A meaningful fraction of WinUI is still .Set()-only.** Spec 027 pulled a
significant chunk out of the escape-hatch list — pointer/tap/keyboard/focus
events, manipulation gestures, and drag-and-drop all shipped as first-class
modifiers in commit `76d0f51`. What's left:
- `ProcessKeyboardAccelerators`, ink input, and a few other specialty event
  surfaces
- Custom storyboard animations (outside the compositor-property set
  covered by `.Animate()` / interaction states)
- Composition layer access beyond `.Shadow()` / `.Animate()`
- Materials and effects (`AcrylicBrush` customization, `Compositor.CreateSpriteVisual`)
- Pointer capture (`CapturePointer` / `ReleasePointerCapture`)
- Most windowing APIs (`AppWindow`, `OverlappedPresenter`, backdrop configuration)
- Shape-specific drawing APIs (`Path` data, `Geometry`, stroke dash arrays)

The abstraction is meaningfully thicker than a quarter ago — the input
critique that dominated this section for three review cycles is mostly
resolved. But composition, materials, and windowing remain flat-out
absent, and those are the surfaces customers notice in "polished desktop
app" territory.

**4. .Set() runs on every render.** The documentation for `OnMount` says it runs
once at mount time, but `.Set()` Setters are `Action<TControl>[]` arrays stored
on the element. These arrays are compared by length in `ShallowEquals`, not by
content — meaning any element with Setters always updates, and the Set callbacks
run on every render. This is both wasteful and surprising.

**5. There's no .Get() — no way to read control state.** `.Set()` lets you
mutate the control, but there's no way to read from it back into the component's
render logic. `.OnMount(control => ...)` captures a reference, but by the time
you use it in a render, you might get stale data. There's no reactive bridge
from control properties back to component state.

---

## 15. Component-by-Component Scorecard

How Reactor's individual feature areas compare to the mature frameworks (React,
SwiftUI, Compose).

| Feature Area | Reactor | React | SwiftUI | Compose | Notes |
|---|---|---|---|---|---|
| **Component Model** | B+ | A | A | A | Context, memoization, generic hooks; reflection in memo check, no slots |
| **Local State** | A- | A | A | A | Generic hooks, post-render cleanup, persisted state; dep arrays still box |
| **Global State** | B+ | A | A | A | Context + UseContext + .Provide(); boxing in scope stack, no selector |
| **Async Data** | B+ | A (TanStack) | B+ | C | **NEW section 3a.** UseResource/UseInfiniteResource/UseMutation/Pending, AsyncValue ADT, QueryCache with TTL+pattern invalidation, focus revalidation, DataGrid hook-paging. In-process cache only, no retry default, zero field evidence |
| **Reconciler** | B- | A | A- | A | Works but monolithic, no concurrent mode; Grid-children-on-type-flip fix (e86ff69) closes a real bug; spec 027 Phase 2 trampoline dispatch removes per-render COM detach/attach on pointer/key/focus events — architecturally right, unmeasured in the field |
| **Layout** | B+ | B+ | A | A | Flex is good; Grid is stringly-typed; FlexPanel CSS semantics just got corrected (commit 397f274) — previous behavior was drift-prone |
| **Theming** | B- | B+ | A | A | Style caching fixes XamlReader.Load perf; RequestedTheme modifier; UseColorScheme hook (reads app theme, not element effective); 3 ThemeRef props; no custom resources |
| **Navigation** | B+ | A | A | A | Type-safe routes, dev-owned stack, GPU transitions, source+destination guards, caching, serialization, enhanced deep linking, diagnostics; ConnectedTransition still a stub, no adaptive multi-pane |
| **Commanding** | B+ | N/A | C+ | N/A | Define-once commands, 16 standard, async lifecycle, focus-scoped accelerators; accelerator rebuild per render, labels not localized, no command routing, no palette UI |
| **Lists/Collections** | B | B+ | A | A | Virtualization exists, no sections |
| **Animation** | B- | B | A | A | Curve DSL, enter+exit, interaction states, keyframes, stagger, WithAnimation; compositor-property-bound ceiling; no per-frame hooks |
| **Accessibility** | B | B | A | A | 16 modifiers + SemanticPanel, UseFocusTrap, 3 Roslyn a11y analyzers, runtime WCAG scanner; SemanticPanel limited to 2 patterns, focus trap doesn't cycle, remaining hooks unbuilt. **Grade rose from B- to B** mostly because of charting a11y (below) — the general surface is unchanged |
| **Charting + Chart A11y** | B+ | N/A | A (Apple Charts) | C (Vico/ad-hoc) | **NEW section 11a.** 43 samples + 8-layer a11y (ChartAutomationPeer, ChartPalette.Harden/ColorblindSimulate, keyboard nav, live announcer, alternate view, forced-colors, 12 scanner rules). No sonification, ForceGraph a11y decorative-only, hit-target expansion opt-in |
| **Input/Events** | B | B | A | A | **Rose from C to B.** Spec 027 shipped: full pointer modifier surface (entered/exited/canceled/wheel), tap/double/right/holding, key-up/preview/character, focus modifiers + `UseElementFocus`, typed `.OnPan`/`.OnPinch`/`.OnRotate` gestures, drag-and-drop with eager+lazy format providers. Trampoline dispatch eliminates per-render COM churn. Still missing: gesture composition (.simultaneously/.sequenced), typed long-press, pointer capture ergonomics, tuned inertia, wheel momentum classification |
| **Styling** | B- | B+ | A | A | Lightweight styling is a genuine differentiator; ResourceBuilder fluent API; 3 Roslyn analyzers; stringly-typed resource keys; analyzer coverage shallow |
| **Developer Experience** | B | A | B+ | B+ | **Rose from C+ to B.** Hot reload works; MCP devtools + ETW + 4 hook analyzers + ~5,600 lines of new coverage tests (4,222 unit + 1,426 selftest wave 1+2) pushed selftest coverage past 85%. Still no GUI devtools, preview is screenshot-only, no per-component render timing, no cache inspector |
| **Devtools + Tracing** | B | B+ (DevTools) | A (Instruments) | B+ (LI) | **NEW section 13a.** MCP server (HTTP+stdio), 12+ MCP tools, stable node ids, supervisor, CLI parity, ETW provider with 6 keywords. State is read-only, no diff in v1, no source map in v1, no cache inspection, ETW has no per-component timing |
| **Control Coverage** | A | N/A | A | A | 94% of WinUI wrapped |
| **Error Handling** | B | B+ | D | D | ErrorBoundary exists (rare feature) |
| **Localization** | B+ | B | B | B | ICU-based, full system, now using Context |
| **Responsive Layout** | B | B+ | A | A | Hooks work but force full re-render |

---

## 16. Conclusion

### The sample apps are telling — and the story has shifted again

Reactor now has eight sample apps: the original four (Outlook clone, file manager,
registry editor, word puzzle game) plus NavigationDemo, CommandingDemo,
StylingGallery, and the new a11y-showcase. Additionally, the WinFormsInterop
sample demonstrates bidirectional hosting. The new samples demonstrate their
respective features comprehensively: NavigationDemo covers routes, guards
(including the new destination-side guards), transitions, caching, deep linking,
and nested navigation. CommandingDemo covers standard commands, async lifecycle,
parameterized commands, focus-scoped accelerators, per-site overrides, and
context-based command sharing. StylingGallery demonstrates theme tokens,
RequestedTheme, UseColorScheme, lightweight styling, and style caching.
a11y-showcase demonstrates SemanticPanel, AccessibilityScanner, form patterns,
heading structure, landmarks, and live regions.

But the original showcase apps remain frozen in time:
- **Outlook clone** still uses string-based view switching (`currentPage switch
  { ... }`) — it hasn't adopted the navigation system that was built to solve
  exactly this problem
- **ReactorFiles** still requires manual `SynchronizationContext` capture for
  off-thread state updates — no async state management
- **Samples that use hard-coded colors** are still broken in dark mode
- **None of the original samples** use the context system, memoization,
  persisted state, commanding, navigation, SemanticPanel, or the accessibility
  modifiers

The positive signal: the Heading/SubHeading DSL change auto-sets HeadingLevel,
so any showcase app that uses `Heading()` now gets correct a11y heading structure
for free — a systemic improvement that doesn't require per-app adoption.

But the fundamental pattern remains: every new feature ships with its own
isolated demo app, while the showcase apps — the ones that prove the framework
works for *real* UIs — don't adopt the new features. The Outlook clone is the
most telling case: it's the framework's most complex app, it has the navigation
problem *and* the accessibility problem *and* the commanding problem, and all
three systems were explicitly designed to solve those problems. It's still using
`UseState<string>` for navigation.

The gap between "feature works in isolation" and "feature works in a real app"
is where production confidence lives. Reactor keeps building features and demo
apps without going back to prove they compose in the existing showcase apps.
This is a red flag for a framework that wants to be production-ready.

### What Reactor gets right

1. **The component model foundation is now solid.** Context, memoization, generic
   hooks, persisted state, post-render effect cleanup — these are the core
   mechanics that every declarative UI framework needs, and they're now
   implemented with correct semantics.

2. **Navigation is architecturally competitive and maturing.** Type-safe routes
   via C# records, developer-owned back stack, composition-layer GPU transitions,
   source + destination lifecycle guards with cancellation, LRU caching, state
   serialization, enhanced deep linking (query strings, optional params,
   wildcards), and NavigationDiagnostics for observability. The design
   independently converged with Compose Nav3's philosophy and is arguably
   stronger on type safety and transitions.

3. **Commanding is a genuine differentiator.** No competing declarative framework
   provides define-once commands with metadata bundling, standard commands, async
   lifecycle, and focus-scoped accelerators. Reactor is ahead of the field here.

4. **Control coverage is impressive.** 94% of WinUI controls wrapped with
   clean factory APIs. This is a huge amount of tedious work done well.

5. **The hooks system is faithful to React and now correctly implemented.**
   UseState, UseReducer, UseEffect, UseMemo, UseRef, UseContext, UseNavigation,
   UseCommand, UsePersisted — the hook surface area has grown meaningfully and
   the React mental model transfers cleanly.

6. **ErrorBoundary exists.** Neither SwiftUI nor Compose has this. Reactor's error
   boundary is a genuine differentiator for resilient UIs.

7. **FlexPanel is ambitious and useful.** A full Flexbox implementation on WinUI
   provides layout capabilities that WinUI itself doesn't have.

8. **Type safety over XAML.** No more binding errors, DataContext confusion, or
   resource-not-found runtime failures. The C# compiler catches real mistakes.
   Navigation and commanding extend this further — routes are types, commands
   are records, everything is compiler-checked.

9. **Lightweight styling surfaces a WinUI capability no competitor wraps.**
   `ResourceBuilder` provides ergonomic access to WinUI's per-control resource
   key overrides with proper cleanup, managed-key tracking, and ThemeRef
   integration. No other C# declarative framework does this.

10. **Accessibility now has a layered system, not just annotations.** SemanticPanel
    solves the hardest problem (custom automation peers for composites). Three
    Roslyn analyzers and AccessibilityScanner provide compile-time and runtime
    diagnostics. UseFocusTrap handles modal focus. Auto-HeadingLevel on
    Heading/SubHeading is a pit-of-success design. No other C# UI framework
    has this combination of semantic, diagnostic, and behavioral accessibility
    layers.

11. **Observable interop.** UseObservable, UseObservableTree, UseObservableProperty,
    and UseCollection bridge cleanly to MVVM. Essential for incremental adoption
    in existing WinUI codebases.

12. **WinForms interop opens a brownfield migration path.** `XamlIslandControl`
    lets WinForms apps host Reactor/WinUI content in specific panels — the
    incremental adoption story that enterprise apps need. E2E tests validate
    Tab navigation, rendering, and accessibility across the WinForms/WinUI
    boundary.

13. **The localization system validates the framework's own abstractions.** The
    migration of LocaleProvider to `Context<IntlAccessor?>` proves the context
    system works for real cross-cutting concerns. Navigation uses Context for
    sharing handles. Commanding can use Context for sharing commands. When
    multiple framework features build on the same primitive, that's good
    architecture.

14. **Async data is now a real framework feature.** `UseResource`,
    `UseInfiniteResource`, `UseMutation`, and `Pending` close what was the
    single largest remaining capability gap. The `AsyncValue<T>` ADT is more
    expressive than React's tuple-based approach. The `QueryCache` is serious
    infrastructure with per-key locks, ref-counted subscriptions, and TTL +
    pattern invalidation. `DataGrid` was migrated to the hook path as a real
    integration test. And four `REACTOR_HOOKS_*` analyzers now catch common
    mistakes at edit time. This is the most complete async data story in any
    C# declarative framework.

15. **Charting accessibility is arguably the best on any platform.** 8 layers,
    12 scanner rules, WCAG-hardening palette transform, LCH-space adjustment,
    colorblind simulation, keyboard navigation matching Highcharts/Power BI,
    forced-colors remap, reduced-motion integration, debounced live regions,
    and the 43-sample gallery fully retrofitted. Only Apple Charts exceeds
    this on sonification; Reactor matches or exceeds on every other a11y
    dimension. This is a genuine competitive differentiator for enterprise
    WinUI apps that need Section 508 compliance.

16. **MCP devtools is unconventional but correctly built.** `reactor.tree`,
    `reactor.click`, `reactor.type`, `reactor.state`, `reactor.screenshot`,
    `reactor.reload` over HTTP + stdio. Stable window-scoped node ids.
    UIA-based automation. Single-instance lockfile. CLI parity. No
    competitor has this. Whether AI-agent-driven automation is the future
    of UI debugging is debatable; the bet itself is defensible.

17. **Hook rules are now compile-time checked.** `REACTOR_HOOKS_001/004/
    005/006` catch conditional hooks, unstable deps, hooks outside Render,
    and non-idempotent fetchers. The previous review flagged the absence of
    `eslint-plugin-react-hooks` equivalent; that gap is now partially closed
    (4 rules vs. React's ~10). The hardest rule (`exhaustive-deps`) is still
    deferred as control-flow-analysis work.

18. **ETW tracing exists.** `Microsoft-UI-Reactor` EventSource emits to
    classic ETW and EventPipe. PerfView / dotnet-trace can consume. This is
    the first real performance-profiling infrastructure the framework has
    had, closing part of Section 13's "no performance profiling" critique.

19. **Declarative input is a real system now.** Spec 027 closed the
    single longest-standing "Reactor is a thin WinUI wrapper" critique:
    pointer / tap / keyboard / focus modifiers are no longer `.Set()`
    territory, pan/pinch/rotate ship as typed gesture records with
    phase/delta/velocity/inertia metadata, drag-and-drop supports
    typed + standard + custom formats with eager / sync / async provider
    overloads, and the reconciler's event-dispatch model changed from
    "detach + attach on every render" to a stable-trampoline model that
    only rewrites a field. No other C# declarative framework ships this
    breadth of input on top of a Retained UI platform.

### What prevents Reactor from being production-ready

1. **The showcase apps don't use the framework's own features.** This is the
   most damning critique I can level. Navigation, commanding, context, memoization,
   persisted state — none of these are used in the Outlook clone, file manager,
   registry editor, or word puzzle game. Each feature has its own isolated demo
   app, but nobody has proven they compose in a complex real-world UI. Until the
   Outlook clone navigates with `UseNavigation`, uses `Context` for session
   state, and surfaces Cut/Copy/Paste through `StandardCommand`, the framework's
   production-readiness is theoretical.

2. **Theming has improved but the pieces don't compose.** Style caching fixes
   performance. Lightweight styling is a genuine differentiator. But
   `UseColorScheme` reads the app-level theme instead of the element's effective
   theme, and `{ThemeResource}` in dynamically-loaded Styles doesn't respect
   per-element `RequestedTheme`. The two headline features — RequestedTheme
   modifier and UseColorScheme hook — were designed for the "dark sidebar"
   scenario and don't work together correctly in that exact scenario. Custom
   branded theme resources still don't exist.

3. **Accessibility has real breadth but uneven depth.** SemanticPanel solves the
   hardest architectural problem (custom automation peers for composites), 3
   Roslyn analyzers and AccessibilityScanner provide diagnostics at compile-time
   and runtime, and UseFocusTrap is the first imperative hook. But SemanticPanel
   only covers 2 of ~20 automation patterns, UseFocusTrap doesn't cycle focus
   (WAI-ARIA Dialog Pattern expects wrapping), the remaining imperative hooks
   (UseAnnounce, UseReducedMotion) are unbuilt, and the semantic role is
   stringly-typed. The shape of a complete system is visible, but the layers are
   thin.

4. **Animation is now fully operational within its ceiling.** The four
   integration bugs from the previous review (dead exit transitions, unwired
   Focused state, WithAnimation only routing Opacity, stagger not integrating
   with enter transitions) are all fixed. Three additional runtime issues
   (async scope loss, Opacity routing for .Animate(), pool crash with
   compositor-tainted elements) are also resolved. The API now delivers what
   it promises. The remaining limitation is structural: the compositor-property
   ceiling (Opacity, Scale, Rotation, Translation, CenterPoint + 3 brush swaps)
   means Width, CornerRadius, Margin, FontSize, and arbitrary colors can't
   animate. This is a WinUI platform constraint, not a Reactor design failure.

5. **.Set() still carries too much weight, but less of it.** Navigation,
   commanding, and styling reclaimed application-architecture surface; spec 027
   just reclaimed most of the input surface (pointer enter/exit/wheel,
   right-tap, double-tap, holding, key-up/preview/character, focus, pan/pinch/
   rotate gestures, and drag-and-drop). What still requires `.Set()`: composition-
   layer effects, materials (`AcrylicBrush` internals, sprite visuals), most
   windowing APIs (`AppWindow`, presenters, backdrop config), custom
   `Path`/`Geometry` drawing, ink input, and pointer-capture ergonomics. The
   abstraction is materially thicker than a quarter ago, but "you don't need
   to know WinUI" is still overclaimed for anything that touches composition
   or the window chrome.

6. **Performance concerns accumulate (but one is fixed).** Style caching
   eliminates the XamlReader.Load-per-themed-element concern. But reflection-
   based ShouldUpdateWithProps (Section 2), boxing in context scope (Section 2),
   unbounded persisted state cache (Section 2), accelerator rebuild per render
   (Section 8), and invisible Border/Grid wrappers adding layout cost (Sections
   4, 7, 8) remain. The style caching fix shows the team responds to performance
   critiques, but no systematic profiling pass has occurred. No profiling tools
   exist to measure the remaining costs (Section 13).

7. **E2E test infrastructure has improved but revealed a process gap.** The
   discovery that 44/46 E2E tests were invisible behind a broken filter is both
   a fix and a warning. The fix: 43 Appium tests now run across 6 test classes,
   covering interactive controls, accessibility, accessibility interactions,
   events, data grid, and WinForms interop. The warning: these tests existed
   for the entire development period without anyone noticing they weren't
   running. How confident can we be in the rest of the CI pipeline? Commanding
   still has no integration tests.
8. **WinForms interop is promising but has a fundamental hosting asymmetry.**
   WinForms hosting Reactor/WinUI works fully. Reactor hosting WinForms is blocked
   in the Reactor-primary scenario because WinUI windows use compositor-only
   rendering (`WS_EX_NOREDIRECTIONBITMAP`) — there's no GDI surface for Win32
   child HWNDs. This is a WinUI platform limitation, not a Reactor design issue,
   but it limits the brownfield migration story to "WinForms-primary apps
   adopting Reactor panels," not "Reactor apps embedding legacy WinForms controls."

9. **Async resources shipped without field evidence or performance
   characterization.** The subsystem is principled and well-tested at the unit
   level, but there are no stress tests for large caches (thousands of keys),
   concurrent fetches, or long-running app scenarios where the cache accumulates
   indefinitely. `QueryCache` eviction is O(n) per tick; per-key locks could
   bottleneck under churn; no benchmarks exist. `UseHookBasedPaging` is enabled
   by default on `DataGrid` but still feature-flagged, meaning two code paths
   (legacy + new) both have to be maintained. And there's no devtools surface
   for the cache — TanStack Query Devtools is a major productivity feature
   that Reactor has no equivalent of.

10. **The velocity is creating an integration deficit.** In four days the
    framework shipped: async resources (full subsystem, ~40 commits), charting
    accessibility (8 layers, ~18 commits, 3,000+ lines of src), devtools MCP
    (24 tools, HTTP + stdio, ~25 commits), ETW tracing, FlexPanel CSS
    compliance fix, spec 027 declarative input (73 files, ~7,200 LoC in a
    single PR), ~5,600 lines of new coverage tests, and a partial
    analyzer rename. Each feature is individually well-engineered. But the
    showcase apps (Outlook clone, file manager, registry editor, word
    puzzle game) — the only proof that features compose in a real UI — still
    don't use any of this. The charting gallery was retrofitted (a real
    proof point); the apps that were supposed to prove navigation,
    commanding, context, memoization, *and now input* compose together
    still don't use any of them. The pipeline of "ship feature + isolated
    demo + move on to next feature" accelerates each iteration while the
    integration debt quietly compounds.

11. **The spec 027 trampoline refactor is unvalidated where it matters
    most.** Phase 2 rewrote event dispatch from "detach + attach on every
    render" to a stable-trampoline-per-event model. The architectural
    argument is right, and the ETW telemetry lets you verify the
    one-time-attach invariant after the fact. What's missing is a
    render-cycle benchmark — the original critique was "O(n) COM interop
    calls per render per element with event handlers." The fix claims to
    eliminate those. But "all 6,390 unit tests pass" doesn't measure
    the thing being claimed: unit tests don't exercise the COM interop
    path, and no real app has been profiled before/after. The
    `TrampolineFixtures` added to selftest cover "latest-handler-wins
    across 100 re-renders" — useful for correctness, not for the
    performance claim. Performance refactors that ship without a
    before/after number are always a risk: they can regress in
    unexpected dimensions (memory, first-render latency, teardown cost)
    while nominally delivering on the headline metric.

12. **Drag-and-drop ships with 370 lines of new `DragData` and zero
    consumers.** `DragData` is genuinely thoughtful — eager / sync /
    async provider overloads per format, `DataPackage.SetDataProvider`
    for cross-process laziness, typed round-trip via
    `reactor/typed/<typeof(T).FullName>` keys, origin-process-id tagging
    so same-process transfers can stash the live object pointer. But no
    sample app uses drag-and-drop today, the typed-format-id is unversioned
    (rename `Models.EmailMessage` and the round-trip breaks), and there's
    no devtools surface for watching DnD negotiation. This is the
    integration-deficit critique in miniature: a carefully-designed
    subsystem landing with no production exercise.

13. **Naming and conceptual churn.** Spec 018 renamed Duct → Reactor;
    commit 55dc53f renamed half the analyzer ids (A11Y + Loc) while
    leaving the theming ids (REACTOR_THEME_001-003) alone; `Factories.Text` →
    `TextBlock`; `--preview` → `--devtools`; Monaco moved out of core into
    a sample. These are all individually defensible, but cumulatively
    they're a lot of churn for a framework that's not yet at v1. Customers
    following along will have to update editorconfig suppressions,
    docs, sample code, and muscle memory.

### The fundamental question (revisited)

Is Reactor a *framework* or a *wrapper*?

The previous version of this review posed this question and concluded "moving
toward framework." That's still true, and the movement has accelerated. The
component model (context, hooks, memoization), navigation (type-safe routing,
developer-owned stack, transitions), and commanding (define-once commands,
standard commands, async lifecycle) are genuine framework-level abstractions.
A developer can now build a multi-page app with shared state, keyboard
shortcuts, and navigation transitions thinking entirely in Reactor's model.

But the framework-vs-wrapper question has refined. Reactor is no longer a wrapper
for the parts it covers. The problem is coverage: too many common scenarios
still require `.Set()`, and the features that exist haven't been stress-tested
in the framework's own showcase apps. The framework's ambition exceeds its
integration testing.

The competitive landscape has also shifted. When this review was first written,
Compose Navigation used strings and an opaque controller. Now Compose Navigation
3 (stable Nov 2025) uses developer-owned typed stacks — the same model Reactor
chose. SwiftUI's NavigationStack is mature. React Router v7 has view transitions
and TanStack Router has full type inference. The window where "declarative
navigation for WinUI" was a novelty has closed; now it needs to be competitive
in quality, not just existence.

Reactor's strongest position is commanding — it's genuinely ahead of the entire
industry. Its navigation is competitive and maturing (destination guards, deep
link enhancements, diagnostics). Its component model is solid. Animation has
crossed a threshold — the four integration bugs are fixed, all advertised
features now work, and the compositor-property ceiling is the only remaining
structural concern. Styling has made a significant jump: style caching fixes the
major perf concern, lightweight styling is a genuine differentiator, and Roslyn
analyzers provide static guidance — but the UseColorScheme/RequestedTheme
composition bug and missing custom theme resources keep it from being fully
competitive. Accessibility has made the largest single-iteration improvement:
SemanticPanel solves the hardest architectural problem, the three-layer
diagnostic approach (compile-time + runtime + E2E) is now more comprehensive
than any other C# UI framework, and UseFocusTrap is the first behavioral hook.
But the layers are still thin — SemanticPanel covers 2 of ~20 automation
patterns, and the remaining imperative hooks are unbuilt. Input took its own
threshold-crossing step in the last 24 hours (spec 027) — pointer / tap /
keyboard / focus / gesture / drag-drop are no longer passthrough, event
dispatch is now trampoline-based, and the hot-path COM churn the previous
review called out has been architecturally addressed (though not yet
benchmarked). WinForms interop opens a migration path that didn't exist
before. The `.Set()` surface area is meaningfully smaller but still too
large in the composition / materials / windowing region.

### To become production-ready, Reactor needs to:

1. **Adopt its own features in the showcase apps.** This has been the
   dominant critique for three review cycles now. The Outlook clone should
   use `UseNavigation`, `Context`, `StandardCommand`, `SemanticPanel`,
   `UsePersisted`, `UseResource`, *and now* the spec 027 input modifiers
   (`.OnRightTapped` for message context menus, `.OnDrop<EmailMessage>` for
   folder drag-drop, `UseElementFocus` for "focus the reading pane after
   message selection"). The charting gallery getting fully retrofitted with
   accessibility in one pass (commit 229e41b) proves the team can do this
   work — they just haven't prioritized it for the flagship apps. The gap
   between "ReactorCharting.Gallery uses all the new chart a11y features"
   and "the Outlook clone still uses `UseState<string>` for navigation" is
   where production confidence lives or dies.
2. **Stress-test async resources before claiming production-ready.** Large
   caches, concurrent fetches, long-running sessions, memory growth under
   churn, per-key lock contention, subscription ref-count correctness under
   rapid mount/unmount. The subsystem is brand-new; unit tests prove
   correctness, but no soak tests prove it scales. A TanStack-Query-Devtools-
   equivalent for `QueryCache` would also materially improve debugging.
3. **Audit and harden the CI pipeline.** The E2E test filter bug (44 invisible
   tests) was a process failure. What other test configurations are silently
   broken? Commits `e85f4d8` + `1870868` added ~5,600 lines of coverage
   tests, which is real investment, but a CI health check that validates
   test counts and alerts on regression in test discovery would prevent
   recurrence.
4. **Do a performance pass using the new ETW infrastructure — starting
   with the trampoline refactor.** ETW tracing exists — use it. The spec
   027 Phase 2 claim is that event dispatch no longer detaches/reattaches
   per render; that's verifiable with a trace but nobody has run one.
   Also measure: reflection-based ShouldUpdate, XamlReader.Load theming,
   accelerator rebuild, wrapper elements (now 5 types: Border, Grid×2,
   SemanticPanel, potentially ChartAlternateViewWrapper), AccessibilityScanner
   overhead, ChartPointProvider allocation rate on large charts, QueryCache
   eviction cost. Per-component render timing is missing from ETW; add it.
5. **Localize StandardCommand labels.** Still unfixed. The framework's own
   commanding system should use the framework's own localization system.
6. **Fix UseColorScheme to read element effective theme, not app theme.**
   Still unfixed. The fix is to read the mounted FrameworkElement's
   ActualTheme, not Application.Current.RequestedTheme.
7. **Finish the theming story.** Custom theme resource definitions, more than
   3 ThemeRef binding properties, type-safe resource key constants for
   lightweight styling. Still unshipped.
8. **Add command routing to the focused view.** Still missing. Cut/Copy/Paste
   in multi-panel apps continues to require manual wiring.
9. **Extend SemanticPanel to more automation patterns.** `IToggleProvider`,
   `IExpandCollapseProvider`, `ISelectionProvider` — the most common after
   Value/RangeValue.
10. **Fix UseFocusTrap cycling.** Tab should wrap to first/last; currently
    just traps.
11. **Add chart sonification or the "audio graph" alternative.** Apple
    Charts and Highcharts ship this. It's the single biggest remaining
    chart-a11y competitive gap.
12. **Finish devtools v1.1.** Source mapping (spec 010), tree diffing,
    state mutation (`reactor.setState`), cache inspection. Without source
    mapping, agents can't map tree nodes back to authored components.
13. ~~**Complete the `REACTOR_*` analyzer rename.**~~ Done in pre-launch
    cleanup — theming analyzers are now `REACTOR_THEME_001/002/003`.
14. **Finish exhaustive-deps analyzer.** `REACTOR_HOOKS_002/003` are deferred
    pending control-flow analysis. This is the single most valuable ESLint
    rule React developers rely on.
15. **Add gesture composition.** Spec 027 shipped three discrete gesture
    primitives but no `.simultaneously` / `.sequenced` / `.exclusively`
    combinators and no typed `LongPressGesture(minimumDuration:)`. A common
    "long-press to begin drag" interaction still has to be hand-rolled
    across `.OnHolding` and `.OnPan`.
16. **Version typed drag formats.** `DragData`'s
    `reactor/typed/<typeof(T).FullName>` format id breaks when models are
    renamed or moved. Add either an explicit version arg or surface-level
    opt-in versioning before the API is used in shipping apps.

The trajectory is right, and the velocity is notable — probably *too*
notable. In the last 96 hours: async resources (full subsystem, closing
the biggest remaining capability gap), charting accessibility (8-layer
system, uniquely comprehensive), devtools MCP (unconventional but
correctly built), ETW tracing (infrastructure for profiling), FlexPanel
CSS compliance fix (closes a silent behavioral drift), spec 027
declarative input (pointer/gesture/focus/drag-drop modifier surface plus
a trampoline-dispatch refactor — 73 files in one PR), and ~5,600 lines of
coverage tests pushing selftest past 85%.

Navigation and commanding moved Reactor from "component library with hooks"
to "framework with application architecture." The accessibility diff moved
the story from "annotations on primitives" to "a system with semantic,
diagnostic, and behavioral layers." WinForms interop moved the adoption
story from "greenfield only" to "brownfield migration possible." Async
resources now moves the data story from "do it yourself" to "first-class
subsystem." Charting accessibility moves chart a11y from "WinUI has no
charts anyway" to "Reactor's charts are more accessible than most native
iOS/Android chart libraries." Devtools MCP moves the testing story from
"write Appium tests" to "AI agents can drive your app." Spec 027 moves
the input story from "everything non-trivial is `.Set()`-passthrough" to
"pointer, gesture, focus, and drag-drop are first-class declarative
modifiers." These are real capability expansions, not just polish.

But the velocity is outpacing the integration. Three review cycles in a
row have flagged the same critique: the showcase apps don't use the
framework's own features. The velocity of new-feature shipping has
accelerated; the velocity of "use the new feature in a real app"
hasn't. That ratio is the risk: a framework that ships features faster
than it integrates them accumulates un-field-tested surface area, which
is exactly how production bugs hide until someone else hits them.

But three themes keep recurring across every feature area:

1. **Wrapper element accumulation.** Component Borders, NavigationHost Grids,
   CommandHost Grids, and now SemanticPanels all add invisible layout containers
   to the WinUI visual tree. Each is individually justified, but together they
   compound — a well-structured Reactor app can have 3-4 framework wrappers between
   a component and its content. This is a systemic tax that needs measurement.

2. **String-typed APIs at boundaries.** Routes are type-safe records, but deep
   link patterns are strings. SemanticPanel roles are strings. Resource keys in
   lightweight styling are strings. Connected animation keys are strings. The
   pattern: Reactor has excellent type safety at the core and stringly-typed
   boundaries at the edges. Each individual instance is defensible (WinUI's
   APIs are strings, deep links are URIs, etc.), but the cumulative effect is
   that developers hit string-typing wherever they touch the framework's
   integration points with the platform or the outside world.

3. **The showcase apps remain the credibility gap.** Every new feature ships
   with its own isolated demo, proving the feature works in a clean context.
   The charting gallery is the one bright exception — 43 samples were fully
   retrofitted to use the new chart a11y features, and they *work*. But the
   Outlook clone, file manager, registry editor, and word puzzle game — the
   framework's most complex and most public *general-purpose* apps — still
   don't use navigation, commanding, context, memoization, persisted state,
   async resources, SemanticPanel, UseFocusTrap, *or* any of the new spec
   027 input modifiers. Until they do, "these features compose in real
   apps" is a claim supported by demos, not proof.

4. **New theme: feature velocity now outpaces integration velocity by a
   widening margin.** In 96 hours: one new subsystem (async resources), one
   new sub-framework (charting + chart a11y), one new paradigm (MCP
   devtools), one new profiling story (ETW), and one major input rewrite
   (spec 027 — pointer / gesture / focus / drag-drop modifiers +
   trampoline dispatch). None of these are small. And they land before
   the previous cycle's features (SemanticPanel, UseFocusTrap,
   NavigationDemo, StandardCommand) have been integrated into the flagship
   apps. The ratio of feature-land-time to feature-integration-time is
   getting worse, not better. Sustainable framework development requires
   the second to keep up with the first.

Reactor now has *five* features where it's genuinely ahead of the
competition:
1. **Commanding** — no competitor has define-once commands with metadata
   bundling, standard commands, async lifecycle, and focus-scoped accelerators.
2. **Lightweight styling** — no competitor surfaces WinUI's per-control
   resource key overrides.
3. **AccessibilityScanner + chart a11y** — no other C# UI framework ships
   framework-integrated runtime WCAG diagnostics with structured AI-agent-
   friendly output, and no declarative framework on any platform ships an
   8-layer chart accessibility system.
4. **Devtools MCP** — no competitor ships an MCP server for AI-agent-driven
   UI automation. Whether that bet pays off depends on adoption patterns
   that don't exist yet.
5. **ErrorBoundary** — neither SwiftUI nor Compose has this; React does.

These are real differentiators, not catch-up. But differentiators only
matter if customers can reach them. The gap between "features exist" and
"features compose correctly in real apps, under production load, in a
maintained showcase" is where the remaining work lives. That gap has
narrowed on charting (one gallery fully retrofitted), but widened on
everything else (async resources, devtools, ETW, spec 027 input
modifiers, accessibility improvements to the general surface all sit
behind isolated demos or selftest fixtures). Net direction: the
framework is more capable than it was a week ago *and* further from
"production-ready" because the new capability hasn't been integration-
tested.

The honest final grade for this review: **Reactor is a remarkable
framework being developed at a pace that is both impressive and
uncomfortable.** Impressive because no declarative C# UI framework has
ever moved this fast and covered this much ground. Uncomfortable because
every review cycle, the integration debt gets larger and the critique
"showcase apps don't use these features" gets more true, not less. At
some point the team will need to spend a sprint doing nothing but
retrofitting the Outlook clone, or the pattern will become irreversible.
