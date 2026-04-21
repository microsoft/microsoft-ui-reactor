# UI Framework Comparative Analysis — Overview

A critical comparison of modern declarative UI frameworks against Microsoft's
Windows UI options. This document synthesizes the per-framework analyses into
a unified scorecard and identifies where each Microsoft framework stands
relative to the industry.

**Date:** April 2026

**Frameworks analyzed:**

| Category | Framework | Platform | First Stable |
|---|---|---|---|
| Competitor | [SwiftUI](swiftui.md) | Apple (iOS, macOS, watchOS, tvOS, visionOS) | 2019 |
| Competitor | [Jetpack Compose](compose.md) | Android (+ iOS, Desktop, Web via CMP) | 2021 |
| Competitor | [React](react.md) | Web (+ mobile via React Native) | 2013 |
| Competitor | [Flutter](flutter.md) | Android, iOS, Web, Windows, macOS, Linux | 2018 |
| Competitor | [Avalonia](avalonia.md) | Windows, macOS, Linux, iOS, Android, Web, Embedded | 2023 |
| Microsoft | WinForms | Windows | 2002 |
| Microsoft | WPF | Windows | 2006 |
| Microsoft | WinUI 3 | Windows | 2021 |
| Microsoft | [Blazor](blazor.md) | Web (+ Desktop/Mobile via Hybrid WebView) | 2020 |
| Microsoft | Reactor | Windows (on WinUI 3) | Pre-release |

---

## Methodology

Each framework is evaluated across 19 categories on a letter-grade scale
(A through F). Grades represent capability relative to what a production
application needs, not relative to each other — an "A" means the framework
handles this area comprehensively with minimal gaps; a "D" means critical
functionality is missing.

The Microsoft frameworks (WinForms, WPF, WinUI 3, Reactor) are rated using the
same scale, then compared against the competitor median to identify where
they lead, match, or lag. Reactor grades come from the existing
[critical-review.md](../critical-review.md).

**Important context:** The competitors are cross-platform or single-vendor-
platform frameworks with billions of dollars of investment and millions of
developers. WinForms and WPF are 20+ year old frameworks in maintenance mode.
WinUI 3 is actively developed but Windows-only. Reactor is a pre-release
experimental framework. The comparison is intentionally unfair — the goal is
to understand the gap, not to declare winners.

---

## Master Scorecard

### Competitor Frameworks

| Category | SwiftUI | Compose | React | Flutter | Avalonia |
|---|---|---|---|---|---|
| **Declarative Syntax** | A | A | A- | B | B- |
| **Component Architecture** | A- | A- | A- | B+ | B |
| **State & Reactivity** | A | A | B+ | B- | B+ |
| **Rendering & Performance** | B+ | B+ | B+ | A- | B+ |
| **Layout** | A- | A- | B+ | B+ | A- |
| **Styling & Theming** | A | A | B | B | A- |
| **Navigation** | B+ | A | B+ | B- | B- |
| **Animation** | A | A- | B | B+ | B+ |
| **Accessibility** | A | A- | B | B- | B+ |
| **Input & Gestures** | A- | B+ | B | A- | B+ |
| **Developer Experience** | B+ | A- | A | A | B- |
| **Platform Reach** | B | B+ | A | A- | A- |
| **Testing** | C+ | A | A- | B+ | B |
| **Error Handling** | D | D | B+ | C+ | C+ |
| **Data Loading & Async** | A- | A- | A | B+ | B |
| **Lists & Virtualization** | B+ | A- | C+ | A- | B+ |
| **Internationalization** | A | B+ | B | A- | B- |
| **Interop & Adoption** | A- | A | A | B | B+ |
| **Forms & Data Entry** | B | B- | B+ | B+ | B |

**Competitor median by category** (5 frameworks, median = 3rd sorted value):

| Category | Median | Best-in-class |
|---|---|---|
| Declarative Syntax | A- | SwiftUI / Compose |
| Component Architecture | A- | SwiftUI / Compose / React |
| State & Reactivity | B+ | SwiftUI / Compose |
| Rendering & Performance | B+ | Flutter (Impeller) |
| Layout | A- | SwiftUI / Compose / Avalonia |
| Styling & Theming | A- | SwiftUI / Compose |
| Navigation | B+ | Compose (Nav 3) |
| Animation | B+ | SwiftUI |
| Accessibility | B+ | SwiftUI |
| Input & Gestures | B+ | SwiftUI / Flutter |
| Developer Experience | A- | React / Flutter |
| Platform Reach | A- | React |
| Testing | B+ | Compose |
| Error Handling | C+ | React (only one with boundaries) |
| Data Loading & Async | A- | React (Suspense + TanStack Query) |
| Lists & Virtualization | B+ | Compose / Flutter (built-in virtualization) |
| Internationalization | B+ | SwiftUI (implicit localization) |
| Interop & Adoption | A- | Compose / React (most flexible) |
| Forms & Data Entry | B | Flutter (built-in validation) / React (Hook Form + Zod) |

---

### Microsoft Frameworks

| Category | WinForms | WPF | WinUI 3 | Blazor | Reactor |
|---|---|---|---|---|---|
| **Declarative Syntax** | F | C+ | C+ | B+ | B |
| **Component Architecture** | D | B- | B- | B | B+ |
| **State & Reactivity** | D | B | B | C+ | B+ |
| **Rendering & Performance** | C+ | B | B+ | B | B- |
| **Layout** | D+ | A- | A- | B+ | B+ |
| **Styling & Theming** | D | A- | A | B | B- |
| **Navigation** | F | C | C+ | B | B+ |
| **Animation** | F | B+ | A | C | B- |
| **Accessibility** | D+ | A- | A | B | B |
| **Input & Gestures** | C | B+ | B+ | B+ | B |
| **Developer Experience** | B | B- | B- | B | B |
| **Platform Reach** | D | D | D | B+ | D |
| **Testing** | D+ | B | B- | A- | B- |
| **Error Handling** | C | C | C | B+ | B |
| **Data Loading & Async** | D+ | B | B | B+ | B+ |
| **Lists & Virtualization** | C+ | A- | A- | B+ | B+ |
| **Internationalization** | C+ | B | B+ | B+ | B+ |
| **Interop & Adoption** | B+ | A- | B+ | A- | A- |
| **Forms & Data Entry** | B | A | B | A- | B |

---

## Category-by-Category Analysis

### 1. Declarative Syntax

**What this measures:** How natural and ergonomic is it to describe UI in code?
Quality of the DSL, verbosity, readability, composability.

**Competitor standard:** SwiftUI's result builders and Compose's Kotlin trailing
lambdas set the bar. Clean, block-based syntax where children are visually
separated from configuration. React's JSX is close behind. Flutter's nested
constructors are the weakest of the four but still declarative.

**Microsoft assessment:**
- **WinForms (F):** No declarative UI. Everything is imperative
  `textBox1.Text = value` in code-behind or designer-generated code
- **WPF (C+):** XAML is declarative but verbose. Angle brackets, explicit
  closing tags, and namespace declarations add noise. Decades old but
  functional
- **WinUI 3 (C+):** Same XAML model as WPF/UWP with minor improvements.
  `x:Bind` is compile-time checked (unlike WPF's runtime binding), which
  is a genuine advantage, but the syntax is equally verbose
- **Blazor (B+):** Razor mixes HTML-like markup with C# via `@` directives.
  Full C# expressiveness inside `@{}` blocks, type-safe parameter passing,
  mature Roslyn-based tooling. Separate language requiring a build step and
  more verbose than function-as-component models, but more readable than XAML
- **Reactor (B):** C# method calls with `params` arrays. Closer to Flutter's
  constructor style than SwiftUI/Compose's block syntax. Modifier chains
  (`.Bold().FontSize(24)`) are ergonomic. The main weakness is bracket-
  counting in deeply nested UIs — C# lacks trailing closures or result
  builders. Still, it's the most readable declarative option on Windows

**Gap:** WPF/WinUI3's XAML is ~15 years behind modern declarative syntax.
Blazor's Razor closes most of the gap (B+); Reactor closes roughly half the
gap with its method-call syntax (B).

---

### 2. Component Architecture

**What this measures:** How are components defined, composed, and reused?
Component identity, lifecycle, slots/children.

**Competitor standard:** Function-as-component (React hooks, Compose) or
value-type-as-component (SwiftUI structs). Clean composition via nesting.
Flutter's StatefulWidget two-class pattern is the most verbose.

**Microsoft assessment:**
- **WinForms (D):** Controls are classes with event handlers. Reuse via
  UserControl. Tight coupling to visual state
- **WPF (B-):** MVVM enables clean separation. Controls are classes with
  DependencyProperty. DataTemplate enables data-driven composition. Verbose
  but architecturally sound
- **WinUI 3 (B-):** Same model as WPF with `x:Bind` improvements. Community
  Toolkit MVVM source generators reduce boilerplate significantly
- **Blazor (B):** Class-based `ComponentBase` with `[Parameter]`/
  `[CascadingParameter]`. `EventCallback<T>` is a clean child→parent event
  primitive that auto-re-renders the subscriber. `RenderFragment` is a
  first-class "piece of UI" value. No hooks equivalent — class-based
  composition is a generation behind function-as-component models
- **Reactor (B+):** React-style function components with hooks. Context system,
  memoization. The mental model transfers from React cleanly. No slots
  pattern (unlike Compose's named slot APIs), but children via params work

**Gap:** WPF/WinUI3 are a generation behind (class-based vs function-based).
Reactor is competitive with React/Compose's component model.

---

### 3. State & Reactivity

**What this measures:** Built-in state primitives, automatic UI updates on
state changes, observation granularity.

**Competitor standard:** SwiftUI's `@Observable` and Compose's Snapshot system
provide automatic, fine-grained state tracking. React defers to external
libraries. Flutter provides only `setState()`.

**Microsoft assessment:**
- **WinForms (D):** Manual property setting. Limited data binding
- **WPF (B):** DependencyProperty + INotifyPropertyChanged + rich binding
  engine. Architecturally sound but verbose. ReactiveUI brings Rx integration
- **WinUI 3 (B):** `x:Bind` (compiled) is faster and safer than WPF's
  reflection-based binding. CommunityToolkit.Mvvm source generators
  (`[ObservableProperty]`) dramatically reduce boilerplate
- **Blazor (C+):** `ComponentBase.StateHasChanged()` is a manual render trigger.
  Framework auto-calls it after lifecycle methods, parameter changes, and
  `EventCallback` invocations; everything else (timers, observables, async
  callbacks) requires `InvokeAsync(StateHasChanged)`. No built-in state
  store — ecosystem uses Fluxor, Blazor-State, observable libraries. **No
  automatic reactivity tracking** — Blazor's biggest ergonomic weakness
- **Reactor (B+):** React-style hooks (UseState, UseReducer, UseEffect, UseMemo,
  UseRef, UseContext, UsePersisted). Hook state is now generic (no boxing for
  value types via `ValueHookState<T>`). Effect cleanup is post-render, not
  synchronous — matching React's behavior. Default-on memoization: propless
  components skip re-renders unless self-triggered or context-changed;
  `Component<TProps>` with record props gets structural equality comparison
  for free. Context system (`Context<T>` + `.Provide()` + `UseContext`)
  provides tree-scoped ambient state — `LocaleProvider` was migrated from a
  thread-static hack to Context, validating the primitive with a real use
  case. `UsePersisted<T>(key, initial)` survives unmount/remount (in-process
  static cache — unbounded, no eviction, string keys with no collision
  protection). `UseObservable` family (tree, property, collection) bridges
  MVVM. Async resources ship as a separate subsystem (see Data Loading &
  Async). **Implementation concerns:** `ShouldUpdateWithProps` uses reflection
  on every memo check with no `MethodInfo` caching — 50 components in a tree
  = 50 reflection calls per parent re-render. Context scope stores values as
  `object?` so value-typed context values (`Context<int>`) box on every
  provide. `UseCallback` is literally `UseMemo(() => callback, deps)` — the
  lambda captures callback, so reference stability is only as fresh as the
  deps (correct behavior, undocumented nuance). No `startTransition`
  equivalent for priority-scheduled updates. Dependency arrays still box
  into `object[]` and compare with `Equals`, and there's no
  `exhaustive-deps` analyzer (deferred as control-flow-analysis work)

**Gap:** No Microsoft framework has automatic fine-grained state tracking.
WPF/WinUI3's INotifyPropertyChanged is functionally equivalent to Flutter's
approach (manual notification). Blazor's `StateHasChanged` is more manual
still — the framework auto-triggers only for lifecycle, parameters, and
`EventCallback`, leaving observable/timer/async scenarios as developer
responsibility. Reactor's hooks match React's model. None match SwiftUI/
Compose's automatic observation.

---

### 4. Rendering & Performance

**What this measures:** How rendering works, reconciliation efficiency,
real-world performance, optimization tools.

**Competitor standard:** Flutter's Impeller achieves 120fps with no shader
jank. Compose's smart recomposition with pausable composition. React's
concurrent rendering with priority scheduling. SwiftUI's AttributeGraph.

**Microsoft assessment:**
- **WinForms (C+):** GDI+ is CPU-bound but fast for simple UIs. <200ms
  startup, 15-30MB memory. Struggles with modern graphics
- **WPF (B):** DirectX retained-mode rendering. GPU-accelerated transforms
  and animations. Higher memory (40-80MB), slower startup (1-3s cold).
  VirtualizingStackPanel for lists
- **WinUI 3 (B+):** Composition layer provides independent animation thread
  at 60fps even when UI thread is blocked. Better than WPF for modern
  scenarios. `x:Phase` for incremental list loading
- **Blazor (B):** Compile-time sequence numbers injected by the Razor compiler
  enable linear-time render-tree diffing (architecturally clever vs React's
  general tree diff). Four render modes (Static SSR, Interactive Server,
  Interactive WebAssembly, Interactive Auto). Server mode is latency-sensitive
  (every interaction is a SignalR round-trip); WebAssembly has a significant
  initial download cost even post-trim. No concurrent rendering
- **Reactor (B-):** Single-threaded reconciler. No concurrent rendering. Known
  GC pressure from modifier chain allocations. No profiling tools. However,
  reconciler correctness is solid and perf experiments are underway

**Gap:** WinUI 3's Composition layer is genuinely competitive for animation
rendering. For general UI performance, the lack of concurrent/pausable
rendering and fine-grained recomposition puts all Microsoft options behind
Compose and React.

---

### 5. Layout

**What this measures:** Built-in layout primitives, flexibility, responsive
design support.

**Competitor standard:** SwiftUI and Compose have elegant declarative layout
with custom layout protocols. React delegates to CSS (most powerful layout
system). Flutter's constraint model is powerful but has a steep learning curve.

**Microsoft assessment:**
- **WinForms (D+):** Absolute positioning + Anchor/Dock. FlowLayoutPanel,
  TableLayoutPanel. No responsive breakpoint system
- **WPF (A-):** Rich panel system (Grid, StackPanel, DockPanel, WrapPanel,
  Canvas). Two-pass Measure/Arrange. Custom panels via MeasureOverride/
  ArrangeOverride. This is WPF's strongest area — architecturally on par with
  modern frameworks
- **WinUI 3 (A-):** Inherits WPF's panel model plus RelativePanel (constraint-
  based) and AdaptiveTrigger for responsive layouts. Strong
- **Blazor (B+):** Delegates entirely to CSS — Flexbox, Grid, container queries.
  Full CSS ecosystem compatibility including in Hybrid (WebView hosts HTML).
  No framework-level layout primitive, but CSS is the most capable layout
  system available
- **Reactor (B+):** FlexPanel (full Flexbox implementation) is ambitious and
  useful — provides layout capabilities WinUI itself doesn't have. Grid is
  stringly-typed (`["*", "Auto", "200"]`). No custom layout protocol. A
  silent correctness gap was just closed: FlexPanel's measure pass was not
  CSS-equivalent until commit `397f274` (April 2026), paired with a 526-line
  fixture suite that pins behavior against a real WebView-rendered layout.
  The fix is the right shape; the fact that there was no parity harness from
  day one is a process gap — any app built against the earlier FlexPanel may
  have subtly wrong layouts

**Gap:** WPF and WinUI 3 have **no gap** in layout — they're competitive with
or ahead of most declarative frameworks. Reactor's FlexPanel is a genuine
addition. The only missing piece is WPF/WinUI3's lack of a single-line
responsive layout primitive (SwiftUI's ViewThatFits, Compose's
adaptive Scenes). Responsive hooks (`UseWindowSize`, `UseBreakpoint`) in
Reactor force a full component re-render on resize — SwiftUI's
`@Environment(\.horizontalSizeClass)` only invalidates views that read it.

---

### 6. Styling & Theming

**What this measures:** Design system integration, custom theming, dark mode
support, style composition.

**Competitor standard:** SwiftUI and Compose have comprehensive built-in design
systems (Apple Design, Material 3) with automatic dark mode. Avalonia's CSS-
like selector system with ControlTheme separation is innovative in the XAML
world. React has no built-in system (ecosystem-dependent). Flutter is
Material-centric.

**Microsoft assessment:**
- **WinForms (D):** Owner-draw or third-party control suites. No declarative
  styling
- **WPF (A-):** The most powerful styling system of any framework: Styles,
  ControlTemplate, DataTemplate, Triggers, ResourceDictionary. Can completely
  redefine any control's visual tree. The power is unmatched but verbosity
  is extreme (50+ lines for a custom button)
- **WinUI 3 (A):** Fluent Design with Mica/Acrylic materials. Lightweight
  styling (override resources without re-templating). ThemeResource for
  automatic light/dark/high-contrast. This is a genuine strength
- **Blazor (B):** CSS isolation (`MyComponent.razor.css` auto-scoped with
  `b-xxx` attribute) is the distinctive feature — zero-runtime scoped CSS per
  component. Limitation: third-party components don't participate (open issue
  dotnet/aspnetcore#63091). No built-in design system — ecosystem relies on
  Fluent UI Blazor, MudBlazor, Telerik, Syncfusion, DevExpress
- **Reactor (B-):** ~40 semantic theme tokens with `Theme.Ref(key)` for
  custom resources. `ResourceBuilder` lightweight styling is Reactor's first
  genuinely unique styling feature — no other C# declarative framework
  surfaces WinUI's per-control resource key overrides. Style caching with
  deterministic sorted keys in a `ConcurrentDictionary` eliminates the
  XamlReader.Load-per-element perf concern that was the previous review's
  biggest theming critique. `.RequestedTheme()` modifier and `UseColorScheme`
  hook shipped. Three Roslyn analyzers (still carrying the old `DUCT001-003`
  IDs — the `REACTOR_*` rename is partial; `DUCT_LOC001` and `DUCT_A11Y_*`
  were migrated, the theming IDs were not). **Two headline features don't
  compose correctly with each other:** `UseColorScheme` reads
  `Application.Current.RequestedTheme` (app-level), not the element's
  effective theme — so a component inside `.RequestedTheme(ElementTheme.Dark)`
  sees `ColorScheme.Light` in a light-mode app. `{ThemeResource}` bindings in
  dynamically-loaded styles also resolve against the app theme, not
  per-element `RequestedTheme`. The "dark sidebar in a light app" scenario —
  exactly what both features were designed for — is broken. **Custom branded
  theme resources still don't exist** after multiple iterations; `Theme.Ref`
  only references existing WinUI resources. Only 3 properties support
  ThemeRef bindings (Background, Foreground, BorderBrush). Lightweight
  styling resource keys are stringly-typed with no validation or IntelliSense

**Gap:** WPF's styling power exceeds every competitor. WinUI 3's Fluent Design
with lightweight styling is competitive with SwiftUI/Compose. Reactor's theming
has improved (from C+ to B-) — style caching fixed the major perf concern,
lightweight styling is a genuine differentiator, analyzers provide static
guidance — but the grade stays at B- because the composition bug between
`UseColorScheme` and `.RequestedTheme()` defeats the scenario they were built
for, custom theme resources remain missing, and the pieces don't compose.
Resource key overrides cannot match WinUI 3's ControlTemplate power for deep
visual customization.

---

### 7. Navigation

**What this measures:** Built-in navigation, back stack management, type safety,
deep linking, adaptive layouts.

**Competitor standard:** Compose Navigation 3 is best-in-class (developer-owned
typed back stack, Scenes API for adaptive). SwiftUI NavigationStack is good
but type-erased. React has no built-in router. Flutter's Navigator 2.0 is
notoriously complex.

**Microsoft assessment:**
- **WinForms (F):** No navigation framework. MDI or manual form swapping
- **WPF (C):** NavigationWindow/Frame/Page exists but rarely used. Most apps
  use MVVM navigation via ContentControl + DataTemplate
- **WinUI 3 (C+):** NavigationView + Frame.Navigate(typeof(Page)). Functional
  but imperative and not type-safe for parameters
- **Blazor (B):** `@page "/products/{id:int}"` directive with route constraints
  (bool, int, long, guid, datetime, etc.), `NavigationManager` for programmatic
  nav, `LocationChanging` with cancellation for unsaved-changes guards. URL-
  native deep linking. **No type-safe navigation** — `NavigateTo(string)` is
  not compile-checked against route templates
- **Reactor (B+):** Type-safe routes via C# records, developer-owned back stack,
  GPU-powered composition-layer transitions, lifecycle guards on both source
  AND destination sides (`NavigatingToContext.Cancel()` lets destinations
  reject navigation for authorization), LRU caching, JSON state serialization,
  deep linking with `DeepLinkMap<TRoute>` supporting typed parameters
  `{id:int}`, optional segments `{name?}`, wildcards `/**`, and query string
  extraction. `NavigationDiagnostics` emits observability events. A test
  infrastructure bug was just discovered and fixed: 44 of 46 E2E tests were
  invisible behind a broken `ClassName=InteractiveTests` filter (should have
  been `ClassName!=SelfTestBatch`); 43 Appium tests now run across 6 classes.
  The fix is real, but the fact that 44 tests went unexecuted for the entire
  dev cycle is a CI process gap. Architecturally on par with Compose Nav 3.
  **Remaining gaps:** `ConnectedTransition` is a documented API that falls
  back to a slide animation with only a debug-log message — a shipped name
  that doesn't do what it says. No adaptive multi-pane layout (Compose Nav 3
  has `Scene`/`SceneStrategy`). Deep link patterns are stringly-typed at the
  URI→route boundary — `"/detail/{id:int}"` is a string, `RouteArgs.Get<T>("id")`
  is a string-keyed lookup, so the type safety stops at the edge. Destination
  guards are synchronous — React Router's async loaders have no equivalent.
  `UseSystemBackButton` is opt-in rather than default. **The showcase apps
  (Outlook clone, file manager) still use `UseState<string>` for navigation**
  and haven't adopted the system that was built to solve their problem

**Gap:** Reactor's navigation is architecturally competitive with Compose Nav 3
and ahead of SwiftUI's type-erased NavigationPath. Deep linking covers the
common patterns but loses type safety at the URI boundary. WPF and WinUI 3's
navigation is a full generation behind. The trajectory is right; the last
15% (adaptive layouts, working connected transitions, async guards, showcase
adoption) separates "competitive" from "best-in-class."

---

### 8. Animation

**What this measures:** Built-in animation primitives, transitions, physics,
enter/exit, composability.

**Competitor standard:** SwiftUI's `withAnimation` is the most ergonomic.
Compose has comprehensive APIs. Flutter has a rich layered system. React
has no built-in animation.

**Microsoft assessment:**
- **WinForms (F):** Timer-based manual animation
- **WPF (B+):** Storyboard/Timeline animation with GPU acceleration, easing
  functions, PropertyPath targeting. Blend provides visual timeline editor.
  Architecturally strong but API is verbose
- **WinUI 3 (A):** Composition API provides independent animation thread,
  implicit animations, connected animations, expression animations, spring
  animations. **Best animation system of any Microsoft framework and
  competitive with the best competitors.** This is WinUI 3's crown jewel
- **Blazor (C):** No framework-level animation. CSS transitions/animations are
  the floor. Small fragmented ecosystem (Blazor.Animate, blazor-transition-
  group, Toolbelt.ViewTransition). Exit animations are awkward because Blazor
  removes DOM nodes synchronously on state change
- **Reactor (B-):** The previous review graded this C+ with four integration
  bugs. All four are now fixed (commit `d38c6ef`), plus three additional
  runtime issues (async scope persistence across the DispatcherQueue render
  boundary, Opacity routing for `.Animate()`, pool crash with
  compositor-tainted elements). The animation system now delivers what its
  API promises. Curves (spring, bezier, linear, 7 easing presets) feed an
  eight-tier system: implicit property transitions, theme transitions, the
  `Curve` DSL, `.Animate()` modifier, layout animations, enter/exit
  transitions with `+` (parallel) and `|` (asymmetric) composition operators,
  interaction states (pointer/pressed/focused all wired), keyframes with
  per-step easing and looping, staggered children integrated with enter
  transitions, scroll-linked expression animations, and `AnimationScope.WithAnimation`
  / `WithAnimationAsync` for ambient curve propagation. 47 regression tests
  guard the bug fixes. **The ceiling is structural, not fixable:** the system
  can only animate what the WinUI compositor exposes on the `Visual` —
  Opacity, Scale, Rotation, Translation, CenterPoint, plus 3 brush swaps
  in InteractionStates. Width, Height, CornerRadius, Margin, Padding,
  FontSize, and arbitrary colors cannot animate. SwiftUI animates any state
  change that produces a different view body; Compose's `animateAsState`
  works for any type with a `TwoWayConverter`; Flutter's `Tween` works for
  anything with `lerp`. Reactor has an increasingly sophisticated control
  model over the same narrow set of properties. There is also no per-frame
  hook (`UseAnimation`/`UseSpring` equivalents) — compositor runs the
  animation, component code can't observe intermediate values. Connected
  animations still use string-key coordination (typos fail silently).
  Keyframes can only target the same compositor properties. No cross-element
  orchestration beyond stagger

**Gap:** WinUI 3's Composition API is world-class. WPF is solid. Reactor's
animation moved from C+ to B- because all four integration bugs are fixed
and the API is now faithful to what it advertises. The remaining gap is the
compositor-property ceiling — a WinUI platform constraint, not a Reactor
design failure, but real. A developer who wants a button's CornerRadius to
animate on hover, or a sidebar's Width to animate on expand, still has no
declarative path and must fall back to `.Set()` and WinUI's `DoubleAnimation`.
Per-frame hooks, typed connected-animation keys, and cross-element orchestration
are design omissions rather than broken promises.

---

### 9. Accessibility

**What this measures:** Built-in accessibility, screen reader integration,
semantic tree, custom component accessibility.

**Competitor standard:** SwiftUI has the best automatic accessibility (standard
views work with VoiceOver without any code). Compose's semantics tree is
strong. Avalonia is notably the first .NET framework with native Linux
accessibility (AT-SPI2). React relies on web standards (ARIA). Flutter has
gaps on web.

**Microsoft assessment:**
- **WinForms (D+):** MSAA (older API). Limited automation support
- **WPF (A-):** Full UIA support. AutomationPeer for every control. Custom
  peers for custom controls. Screen readers work well. This is a strength
- **WinUI 3 (A):** Full UIA support, same as UWP. All standard controls
  accessible. High contrast mode via theme resources. Narrator integration
  is strong. Competitive with SwiftUI
- **Blazor (B):** Inherits web accessibility — semantic HTML + ARIA attributes
  + keyboard events. Framework contributes nothing on top (same as React).
  Quality depends entirely on component library choice: Fluent UI Blazor,
  Telerik (WCAG 2.2), Syncfusion (WCAG 2.2 / Section 508 / ADA) all provide
  comprehensive a11y
- **Reactor (B):** 16 first-class accessibility modifiers across two storage
  tiers (tier 1 inline on `ElementModifiers`, tier 2 lazy sub-record) —
  common case pays zero cost. 12-13 E2E Appium tests through the real UIA
  pipeline with explicit WCAG 2.1 criterion mapping (1.1.1, 1.3.1, 2.1.1,
  3.3.2, 4.1.2, 4.1.3). `SemanticPanel` + `.Semantics()` modifier wraps
  composite components in a real WinUI `Panel` subclass with a custom
  `SemanticPanelAutomationPeer` that exposes role, value, and range through
  `IValueProvider` and `IRangeValueProvider` — this closes the previously
  "architecturally blocked" composite-automation gap. `LabeledBy` is now
  wired with deferred resolution (targets not yet in the tree at mount time
  resolve on `Loaded`). `ElementPool` clears 14 UIA properties on return.
  `Heading()` and `SubHeading()` factories auto-set `HeadingLevel.Level1`/
  `Level2`. `UseFocusTrap` hook handles modal focus containment via the
  `LosingFocus` event. `AccessibilityScanner` runs post-reconciliation WCAG
  diagnostics with rich context (parent names, nearest heading, WCAG
  criterion, code-snippet fix suggestions) designed for AI-agent consumption.
  Three Roslyn analyzers (`REACTOR_A11Y_001/002/003`) cover icon-button,
  image, and interactive-element missing-AutomationName at edit time.
  **Remaining gaps:** `SemanticPanel` only implements 2 of ~20 UIA patterns
  (Value, RangeValue) — no `IToggleProvider`, `IExpandCollapseProvider`,
  `ISelectionProvider`. Semantic role is a bare string with no enum
  (`"slidre"` silently maps to `Custom`). `UseFocusTrap` traps but doesn't
  cycle — tabbing past the last focusable element stays put rather than
  wrapping, which is the WAI-ARIA Dialog Pattern expectation. `UseFocusTrap`
  requires `.Set(el => trap.SetContainer(el))` to wire the container, a
  reach-through-the-declarative-model pattern. `UseAnnounce`,
  `UseReducedMotion`, and `UseScreenReaderActive` are still unbuilt.
  `AccessibilityScanner` is DEBUG-only and runtime-only (requires a
  rendered UI). Analyzer coverage is narrow — `REACTOR_A11Y_001` matches
  `Button` by identifier name, missing `AppBarButton`, `ToggleButton`,
  `SplitButton`, `RepeatButton`; `REACTOR_A11Y_003` misses `RadioButton`,
  `TextBox`, `PasswordBox`, `AutoSuggestBox`. Programmatic focus control
  (`FocusRequester` / `@FocusState` equivalent), XYFocus directional
  navigation, and focus restoration on back-navigation are all still
  missing. SemanticPanel adds a fourth invisible wrapper to the visual
  tree (after component Border, NavigationHost Grid, CommandHost Grid).
  **The showcase apps still don't use any accessibility modifiers** —
  a11y-showcase is an isolated demo; Outlook clone, file manager,
  registry editor, and word puzzle game still have no headings, landmarks,
  or live regions

**Gap:** WPF and WinUI 3 have **no gap** in accessibility — UIA is the most
comprehensive accessibility API on any platform. Reactor moved from B- to B
in the last cycle on the strength of SemanticPanel (solves the hardest
architectural problem), a three-layer diagnostic approach (compile-time +
runtime + E2E) that no other C# UI framework has, and auto-HeadingLevel as
pit-of-success design. The layered accessibility *system* is real; the
layers are thin. SwiftUI's `.accessibilityRepresentation {}` and Compose's
`Modifier.semantics {}` are open — SemanticPanel is closed to two patterns.
The trajectory is right; the last imperative hooks (announce, reduced motion)
and the focus-cycling correctness bug are concrete, specific gaps.

---

### 10. Input & Gestures

**What this measures:** Touch, pointer, keyboard, focus management, gesture
recognition and composition.

**Competitor standard:** Flutter's gesture arena is the most principled.
SwiftUI's gesture composition is elegant. Compose's pointer input is
capable. React has no built-in gesture system.

**Microsoft assessment:**
- **WinForms (C):** Standard Win32 input events. Mouse, keyboard. No gesture
  system
- **WPF (B+):** Rich input system: mouse, keyboard, touch, stylus, multi-touch.
  Command binding for keyboard shortcuts. Manipulation events for pinch/rotate.
  UIElement.Focus() for focus management
- **WinUI 3 (B+):** Same as WPF with improved touch/pen support. Composition
  interaction for smooth gesture-driven animations
- **Blazor (B+):** Typed event argument types (`MouseEventArgs`,
  `KeyboardEventArgs`, `ChangeEventArgs`) — safer than React's `SyntheticEvent`.
  Declarative `@onclick:stopPropagation="true"` / `:preventDefault="true"`.
  Typed form inputs (`<InputText>`, `<InputNumber>`, `<InputDate>`) with
  two-way binding and validation integration. No gesture system
- **Reactor (B):** Spec 027 (commit `76d0f51`, 73 files, +7.2k LoC) closed the
  longest-standing "Reactor is a thin WinUI wrapper" critique in one PR.
  What shipped: the full pointer modifier surface (`.OnPointerPressed`,
  `Moved`, `Released`, `Entered`, `Exited`, `Canceled`, `CaptureLost`,
  `WheelChanged`); tap family (`.OnTapped`, `.OnDoubleTapped`, `.OnRightTapped`,
  `.OnHolding`) with `IsTapEnabled`-family flags auto-toggled by the
  reconciler; keyboard (`.OnKeyDown`, `.OnKeyUp`, `.OnPreviewKeyDown`,
  `.OnPreviewKeyUp`, `.OnCharacterReceived`); focus (`.OnGotFocus`,
  `.OnLostFocus`, plus a `UseElementFocus()` hook with typed `ElementRef`
  and `.Ref(target)` modifier, and a `FocusManager` helper — the first
  imperative focus primitive the framework has had); typed pan/pinch/rotate
  gestures (`.OnPan`, `.OnPinch`, `.OnRotate`) with `PanGesture`/`PinchGesture`/
  `RotateGesture` records carrying phase, translation/delta/velocity, scale/anchor,
  angle, and an `IsInertial` flag; drag-and-drop with eager/sync-provider/
  async-provider format overloads (text, URI, HTML, RTF, files, bitmap, custom)
  wired through `DataPackage.SetDataProvider` so expensive formats only
  render if a drop target asks. The event dispatch model also changed: a
  stable trampoline is attached once per event per element lifetime, replacing
  the previous "detach + attach on every render" COM churn on hot input
  paths. ETW events (`EventTrampolineAttached`, `EventTrampolineDispatch`,
  keyword 0x40) let the one-time-attach invariant be verified from a trace.
  Commanding remains a real differentiator: define-once `Command` records
  bundle execute + canExecute + label + icon + accelerator + description;
  16 standard commands (Cut/Copy/Paste/Undo/Redo/etc.); focus-scoped
  accelerators via `CommandHost`; ICommand interop. **Remaining gaps:**
  gesture composition (SwiftUI's `.simultaneously`/`.sequenced`/`.exclusively`)
  doesn't exist. Long-press is a raw event, not a typed
  `LongPressGesture(minimumDuration:)`, so "long-press to begin drag" must
  be hand-rolled across `.OnHolding` and `.OnPan`. Inertia deceleration isn't
  tunable — `withInertia` is on/off. Pan inherits WinUI's manipulation
  constraints (screen-axis-aligned translation only). Pointer capture
  (`CapturePointer`/`ReleasePointerCapture`) still requires `.Set()`. Wheel
  handling has no typed delta record and no mouse-vs-trackpad momentum
  classification. StandardCommand labels are English-only (surprising given
  Reactor has a full ICU localization system). Command routing to the focused
  view is still missing — Cut/Copy/Paste in multi-panel apps continues to
  require manual wiring. Accelerators rebuild on every render (O(commands)
  COM calls per CommandHost per render). **Zero field evidence for the
  trampoline perf claim** — all 6,390 unit tests pass, but the suite is
  WinUI-free and doesn't exercise the COM interop path. No before/after
  render-cycle benchmark exists; the architectural argument is right, the
  measurement isn't there. **Zero field evidence for drag-and-drop** — 370
  lines of new `DragData` shipped with no showcase consumer. Typed format
  ids use `typeof(T).FullName`, unversioned, so renames break round-trips

**Gap:** WPF and WinUI 3 are competitive. Reactor moved from C to B in a
single PR — pointer/gesture/focus/drag-drop are no longer passthrough, and
commanding remains a unique industry-first (no competitor ships define-once
commands with metadata bundling). The remaining gaps are composition
primitives (simultaneous/sequenced gesture chains, typed long-press), command
routing to focus, typed pointer-capture and wheel-momentum APIs, and the
uncomfortable fact that Reactor's own showcase apps don't use any of these
new modifiers. The Outlook clone has no `.OnRightTapped` for message
context menus, no `.OnDrop<EmailMessage>` for folder drop, no
`UseElementFocus` for "focus the reading pane after selection." The feature
exists; the integration evidence doesn't.

---

### 11. Developer Experience

**What this measures:** Hot reload, debugging tools, IDE integration,
documentation, learning curve.

**Competitor standard:** Flutter's hot reload is the gold standard. React's
DevTools and documentation are the gold standard. Compose's Layout Inspector
and Preview are excellent.

**Microsoft assessment:**
- **WinForms (B):** Visual Studio designer, drag-and-drop, Properties window.
  Fast compilation. Simple mental model. Very mature. But no hot reload, no
  live visual tree
- **WPF (B-):** XAML designer, XAML Hot Reload (UI only), Live Visual Tree.
  But steep learning curve (XAML verbosity, DependencyProperty, MVVM
  boilerplate). Blend for animations
- **WinUI 3 (B-):** XAML Hot Reload, Live Visual Tree. But packaging complexity,
  deployment issues, smaller community for troubleshooting. Documentation
  has gaps for advanced scenarios
- **Blazor (B):** Mature Roslyn-based tooling in Visual Studio and JetBrains
  Rider. Hot Reload works across Server, WebAssembly, and Hybrid modes (Razor
  edits, C# method bodies, CSS); reliability issues on VS Code. Unified C#
  end-to-end. **No component DevTools** — no render tree inspector, no
  parameter inspector, no render-count profiler. WebAssembly debugging has
  debug-proxy handshake fragility
- **Reactor (B):** Hot reload works via .NET's `MetadataUpdateHandler` —
  UI updates on code change while hook state survives (`UseState` values
  persist because `RenderContext` stays in memory). Works with both VS and
  `dotnet watch`. Limited by .NET hot reload's inherent restrictions: adding
  fields, changing type hierarchies, and lambda shape changes still require
  a restart. Spec 024+025 shipped an MCP (Model Context Protocol) devtools
  server — `mur devtools ./App.csproj` spawns a supervisor that exposes
  ~12 tools over HTTP JSON-RPC and stdio: `reactor.tree`, `reactor.click`,
  `reactor.type`, `reactor.state`, `reactor.screenshot`, `reactor.logs`,
  `reactor.windows`, `reactor.waitFor`, `reactor.fire`, `reactor.reload`.
  Stable window-scoped node ids (`r:main/Counter.btn-inc`) survive re-renders.
  UIA-based automation means any test an agent can write is also a test that
  Narrator can see. **No C# declarative framework has an MCP server** — this
  is genuinely unique, but it's a bet: agents-first debugging. The
  traditional-developer equivalent (React DevTools panel, Compose Layout
  Inspector) still doesn't exist; "preview" was renamed to "devtools" and
  replaced rather than augmented. ETW tracing via `Microsoft-UI-Reactor`
  EventSource (commit `310a299`) emits reconcile/render/state/MCP/lifecycle/
  event-dispatch events to classic ETW and EventPipe. Consumers use PerfView
  or dotnet-trace. Four hook-rule Roslyn analyzers (`REACTOR_HOOKS_001/004/
  005/006`) catch conditional hooks, unstable deps, hooks-outside-render,
  and UseResource-on-mutation at edit time — the `eslint-plugin-react-hooks`
  equivalent that was previously absent. The most valuable ESLint rule
  (`exhaustive-deps`) is still deferred as control-flow-analysis work.
  ~5,600 lines of new coverage tests pushed selftest coverage past 85%.
  **Remaining gaps:** state is read-only in MCP (no `reactor.setState`);
  tree diffing is deferred (agents must cache client-side); source mapping
  from tree nodes to authored C# lines is v1.1; no cache inspection
  surface (`QueryCache` is invisible to devtools — contrast with TanStack
  Query Devtools); ETW has no per-component render timing or per-hook
  execution breakdown, only aggregate boundaries; PerfView's learning curve
  is real; the DEBUG-gated devtools surface has a risk of Release-build leak
  if environment flags misconfigure. The VS Code extension's interactive
  preview has regressed relative to the old `--preview` flag — screenshots
  are now a sub-capability of devtools rather than a first-class command

**Gap:** All Microsoft options lag in developer experience. Reactor moved
from C+ to B on the strength of MCP devtools (unique industry-wide), ETW
tracing, and the hook-rule analyzers — but "unique" is not the same as
"better for humans." There is still no equivalent to React DevTools'
component tree inspector, Compose Layout Inspector's per-composable
recomposition counts, or SwiftUI Xcode Previews' inline interactive
rendering. Reactor's bet is that AI agents are a primary UI-debugging
audience; if that bet doesn't play out, the traditional-developer story
is still behind. Per-component render timing is the largest concrete
profiling gap.

---

### 12. Platform Reach

**What this measures:** Number of platforms, cross-platform capability,
ecosystem size.

**Competitor standard:** Flutter targets 6 platforms. Avalonia targets 7
(including embedded Linux). React targets web + mobile + desktop via React
Native. Compose Multiplatform now covers Android, iOS, Desktop, Web.
SwiftUI covers all Apple platforms.

**Microsoft assessment:**
- **WinForms, WPF, WinUI 3, Reactor (D):** Windows only. Full stop. WPF and
  WinForms have no cross-platform story. WinUI 3 is Windows-only by design.
  Reactor inherits WinUI 3's limitation
- **Blazor (B+):** **The only Microsoft framework with genuine cross-platform
  reach.** Same Razor components run on Web (Server/WASM), Windows (WPF,
  WinForms, MAUI), Mac/iOS/Android (MAUI) via `BlazorWebView`. Caveat: Hybrid
  renders HTML in a WebView, not platform-native controls — you don't get
  native UIA, native look, or platform controls. Platform API access requires
  MAUI APIs or JS interop bridges

**Avalonia note:** Avalonia is the .NET ecosystem's answer to this gap,
covering 7 platforms (Windows, macOS, Linux, iOS, Android, WebAssembly,
embedded Linux) from a single C# codebase. It is the only .NET UI framework
with production-grade Linux desktop support. However, Avalonia is a separate
framework from the Microsoft stack — it self-renders via Skia rather than
using platform controls, and its mobile/web platforms are less mature than
its desktop story.

**Gap:** This is the largest gap between Microsoft's *native* frameworks and
competitors. WinForms, WPF, WinUI 3, and Reactor target one platform.
**Blazor Hybrid is the only first-party Microsoft framework that targets
multiple platforms** — at the cost of rendering in a WebView rather than
native controls. Avalonia provides a cross-platform native-ish path for .NET
developers but requires leaving the WinUI 3/WPF ecosystem (unless using XPF
for WPF binary compat). The architectural trade-off is clear: Blazor Hybrid
prioritizes code reuse over native fidelity; WinUI 3/Reactor prioritize
native fidelity over code reuse.

---

### 13. Testing

**What this measures:** Unit, component, integration, and visual testing
capabilities.

**Competitor standard:** Compose's semantics-based testing and React Testing
Library's behavior-based testing are best-in-class. Flutter's widget testing
is fast and comprehensive. SwiftUI has the weakest testing story.

**Microsoft assessment:**
- **WinForms (D+):** Tight UI-logic coupling makes testing difficult. UI
  testing via WinAppDriver/FlaUI
- **WPF (B):** MVVM enables excellent ViewModel unit testing. UI testing via
  WinAppDriver/FlaUI. Binding errors are silent at runtime (a testing gap)
- **WinUI 3 (B-):** MVVM + `x:Bind` compile-time checking catches binding
  errors. UI testing via WinAppDriver. Test infrastructure still evolving
- **Blazor (A-):** **bUnit is the best testing story in the Microsoft
  ecosystem** — renderer-level component tests, semantic HTML assertions
  (whitespace/attribute-order insensitive), officially endorsed by Microsoft
  Learn, xUnit/NUnit/MSTest/TUnit compatible, milliseconds per test. Mocked
  `IJSRuntime` and `NavigationManager` built in
- **Reactor (B-):** Pure C# function components are unit-testable. ErrorBoundary
  exists. Navigation has 146+ unit tests (including 29 stress tests covering
  concurrency, serialization, and deep linking). DataGrid has 1,600+ state
  unit tests. Accessibility has ~34 tests (scanner + analyzer). E2E Appium
  tests exist for DataGrid, WinForms interop (13 tests), and accessibility
  interactions. No component-level testing framework

**Gap:** WPF's MVVM testability is on par with competitors. **Blazor's bUnit
closes the component-testing gap entirely** — it's the renderer-level equivalent
of ComposeTestRule / React Testing Library and is the best testing story in the
Microsoft ecosystem. Reactor's ErrorBoundary is a genuine advantage over
SwiftUI, Compose, and Flutter.

---

### 14. Error Handling

**What this measures:** Error boundaries, crash recovery, graceful degradation.

**Competitor standard:** React is the only framework with error boundaries.
Flutter's ErrorWidget is partial. SwiftUI and Compose crash the app on
view errors.

**Microsoft assessment:**
- **WinForms (C):** Application.ThreadException catches UI thread exceptions.
  No granular recovery
- **WPF (C):** Dispatcher.UnhandledException. Silent binding errors (a mixed
  blessing). No error boundary concept
- **WinUI 3 (C):** Application.UnhandledException. `x:Bind` compile-time
  checking prevents binding errors. No error boundary
- **Blazor (B+):** `<ErrorBoundary>` component with `Recover()` method. Puts
  Blazor in a small group with React and Reactor as the only component
  frameworks with first-class error boundaries. Circuit-level unhandled
  errors in Server mode trigger a default UI overlay
- **Reactor (B):** ErrorBoundary component exists — this is a **genuine
  differentiator.** Neither SwiftUI nor Compose has this. Only React provides
  equivalent functionality. Reactor is ahead of every competitor except React

**Gap:** Both Blazor and Reactor are competitive or ahead. Error boundaries
are one of only two categories (alongside commanding) where Microsoft
frameworks lead the industry. Blazor and Reactor share this with React —
a small club.

---

### 15. Data Loading & Async

**What this measures:** How the framework handles async data fetching,
loading/error/success states, and lifecycle-scoped async operations.

**Competitor standard:** React's Suspense + Error Boundaries provide the most
elegant declarative loading pattern. SwiftUI's `.task` is the cleanest
lifecycle-scoped async. Compose's coroutines are the most powerful async
runtime. Flutter's `FutureBuilder` is built-in but considered low-level.

**Microsoft assessment:**
- **WinForms (D+):** `BackgroundWorker` (legacy) or `async`/`await` in event
  handlers. Manual UI thread marshaling via `Control.Invoke`. Manual
  loading/error state management via control property toggling
- **WPF (B):** `async`/`await` works naturally in MVVM commands.
  `SynchronizationContext` auto-marshals to UI thread. `IsBusy` property
  pattern. CommunityToolkit.Mvvm's `IAsyncRelayCommand` provides `IsRunning`.
  ReactiveUI provides reactive command lifecycle
- **WinUI 3 (B):** Same async/await + MVVM pattern as WPF with
  `DispatcherQueue` instead of `Dispatcher`. `ISupportIncrementalLoading`
  for automatic pagination in lists. No built-in async loading framework
- **Blazor (B+):** Streaming rendering (`@attribute [StreamRendering(true)]`)
  is the SSR equivalent of Suspense — initial markup sent immediately, async
  content streams in. `[PersistentState]` (.NET 10) closes the prerender →
  interactive double-fetch gap. Auto-`StateHasChanged` after `OnInitializedAsync`
  removes boilerplate. No Suspense equivalent in Interactive mode — manual
  loading-state booleans. No built-in caching/retry (no TanStack Query peer)
- **Reactor (B+):** Spec 020 (PR #29, `3814def..9f2f5de`, ~40 sub-commits)
  shipped a full async data subsystem in 72 hours. `UseResource<T>` for
  single-fetch; `UseInfiniteResource<T>` for cursor pagination;
  `UseMutation<TIn,TOut>` for writes with pattern-based cache invalidation.
  The return type is an `AsyncValue<T>` sealed record ADT —
  `Loading` / `Data` / `Error` / `Reloading` — which expresses
  stale-while-revalidate as a type-level concept (Reloading carries the
  old value while the new fetch runs). `Pending` / `PendingScope` provide
  Suspense-style fallback regions. Hook-owned `CancellationToken` means
  unmount cancels in-flight requests automatically. `QueryCache.Default`
  has per-key `SemaphoreSlim` locks preventing duplicate concurrent
  fetches, ref-counted subscriptions, TTL + pattern invalidation
  (`Invalidate("employees.*")`), focus revalidation, and an `EntryChanged`
  event for mutation-driven invalidation. `DataGrid` row-commit was
  migrated to `UseMutation` and hook-based paging is on by default —
  a real production-sized control consuming the new API, not just a demo.
  Four `REACTOR_HOOKS_*` analyzers back it up. **Remaining concerns:**
  Cache is in-process-only — lost on app exit. No automatic retry;
  transient network errors throw straight into `AsyncValue.Error<T>`.
  `StaleTime` defaults to zero (refetch on every mount). `InvalidateKeys`
  is `string[]` — no partial-match arrays like TanStack Query's
  `queryKey`. Cache keys derive from `CallerHookId + DepsHash` by default,
  which differs from TanStack Query where cache sharing is by named
  key by default — two sibling components calling the same fetcher get
  different cache entries unless they specify `CacheKey`. **No devtools
  for the cache** — `reactor.state` exposes hook shape but not
  `QueryCache` state. Diagnosing "why is this query stale" requires
  reading source. **No stress tests** — 15 self-host fixtures cover
  correctness but nothing profiles eviction (O(n) per tick) or per-key
  lock contention under thousands of live queries. Zero field evidence

**Gap:** Declarative frameworks have lifecycle-scoped async (`.task`,
`LaunchedEffect`, Suspense + TanStack Query). Reactor now competes on the
primitives — ADT-based state, hook-owned cancellation, shared cache, typed
analyzers — but TanStack Query has persistence adapters, devtools, retry,
prefetching, and a multi-year production track record that Reactor's
brand-new subsystem lacks. The gap to React's ecosystem is smaller than in
any other category (this is the closest competitive position), but it
hasn't been battle-tested.

---

### 16. Lists & Virtualization

**What this measures:** Large-list performance, built-in virtualization,
recycling, grouping, and pagination.

**Competitor standard:** Compose and Flutter have excellent built-in
virtualization. SwiftUI's `List` recycles via UITableView underneath.
React has **no built-in virtualization** — a genuine gap.

**Microsoft assessment:**
- **WinForms (C+):** `ListView.VirtualMode` and `DataGridView.VirtualMode`
  provide data virtualization (query on demand). No UI container recycling.
  Handles 100k+ items efficiently in virtual mode. Grouping not supported
  in virtual mode
- **WPF (A-):** `VirtualizingStackPanel` with recycling mode. `ICollectionView`
  for sorting, filtering, grouping. Virtualized grouping (since .NET 4.5).
  Deferred scrolling. `DataGrid` column+row virtualization. This is one of
  WPF's strongest areas
- **WinUI 3 (A-):** `ItemsRepeater` (flexible virtualizing layout) + `ListView`/
  `GridView` with container recycling. `x:Phase` for incremental loading.
  `ContainerContentChanging` for efficient recycling. `ISupportIncrementalLoading`
  for automatic pagination
- **Blazor (B+):** `<Virtualize>` built-in for vertical lists (sync `Items` or
  async `ItemsProvider`). **QuickGrid** is Microsoft's official simple data grid
  with `IQueryable` integration (sorting, paging, column templates). Commercial
  grids (Telerik, Syncfusion, DevExpress) cover high-end scenarios. No built-in
  grouping, no horizontal/grid-of-items virtualization
- **Reactor (B+):** Typed `ListView<T>`/`GridView<T>` with `viewBuilder` pattern
  and `ContainerContentChanging` recycling. `LazyVStack<T>`/`LazyHStack<T>`
  via `ItemsRepeater`. `ElementPool` for interactive control recycling (capped
  at 32 per type). **VirtualListComponent** provides count-based virtualization
  with fixed-height O(1) and variable-height modes, imperative scroll control
  (`ScrollToIndex`, `RestoreScrollOffset`), and visible-range change callbacks.
  **DataGrid** is a full-featured data grid with: `DataPageCache` (LRU block
  cache with configurable block size, max blocks, and prefetch), `IDataSource`
  abstraction declaring server-side capabilities (sort, filter, search, count,
  CRUD), inline cell and row editing with per-edit `ValidationContext`, async
  commit with optimistic updates and rollback on failure, multi-selection with
  shift-click range selection, full keyboard navigation (arrow keys, Tab,
  Home/End, Enter/F2 to edit, Escape to cancel), column pinning (Left/Right),
  column resize, observable data source auto-refresh, and scroll-jank
  prevention via deferred rendering during active scrolling. 1,600+ DataGrid
  unit tests and E2E Appium tests. Still no grouping, no built-in pagination
  UI, no column drag reorder

**Gap:** WPF and WinUI 3 have **no gap** — their virtualization is on par with
or ahead of competitors. WPF's `ICollectionView` for sorting/filtering/grouping
has no equivalent in any declarative framework. Reactor's DataGrid now provides
server-side sort/filter with paged caching and inline editing — a substantial
LOB capability — but still lacks grouping (WPF's ICollectionView) and built-in
pagination UI. React's lack of built-in virtualization means WPF/WinUI 3/Reactor
are all ahead of React here.

---

### 17. Internationalization & Localization

**What this measures:** Resource management, plural/gender rules, RTL support,
runtime locale switching, date/number formatting.

**Competitor standard:** SwiftUI's implicit localization (string literals
auto-resolve) is the most ergonomic. Flutter's ICU MessageFormat in ARB
files is comprehensive. Compose uses Android's mature resource system. React
has no built-in i18n.

**Microsoft assessment:**
- **WinForms (C+):** `.resx` files with satellite assemblies. `ComponentResourceManager`
  auto-loads per locale. No plural/gender support. RTL via `RightToLeft`
  property. Dynamic switching requires form recreation
- **WPF (B):** `.resx` + `x:Static` binding. Satellite assemblies. No built-in
  plural/gender. RTL via `FlowDirection`. Dynamic switching requires UI reload
  or INPC wrapper on resources
- **WinUI 3 (B+):** `.resw` + MRT (Modern Resource Technology). `x:Uid` for
  XAML resource binding. `ResourceLoader` for code access. No plural/gender
  built-in. `ApplicationLanguages.PrimaryLanguageOverride` for per-app language.
  RTL via `FlowDirection`
- **Blazor (B+):** Inherits the mature .NET i18n stack — `IStringLocalizer<T>`,
  `.resx`, `CultureInfo`, `NumberFormatInfo`. ICU support via WASM globalization
  bundle (size trade-off). Runtime culture switching works cleanly in Server
  mode. **No built-in CLDR plural/gender rules** — same gap as WPF/WinUI 3.
  RTL is a CSS concern
- **Reactor (B+):** ICU MessageFormat on top of WinUI's `.resw` system.
  `Context<IntlAccessor?>` provides context-based locale propagation.
  CLDR plural/gender/select support — **addresses WinUI 3's biggest i18n gap**.
  Runtime locale switching via context rerender

**Gap:** The biggest gap across all Microsoft frameworks is **no built-in
plural/gender rules** in the resource system (WinForms, WPF, WinUI 3 all lack
this). Reactor closes this gap with ICU MessageFormat. SwiftUI and Flutter have
the most complete i18n. Dynamic locale switching is smoother in declarative
frameworks than in XAML frameworks.

---

### 18. Interop & Incremental Adoption

**What this measures:** Can you adopt the framework incrementally? Bidirectional
embedding, state bridging, migration path, performance overhead.

**Competitor standard:** Compose's `ComposeView` drops into XML with <1ms
overhead. React mounts into any DOM element. SwiftUI's `UIHostingController`
is mature. Flutter's add-to-app works but platform views are costly.

**Microsoft assessment:**
- **WinForms (B+):** P/Invoke for Win32 is near-zero overhead. COM/ActiveX
  hosting. `ElementHost` embeds WPF. The most seamless native interop since
  WinForms is essentially a Win32 wrapper
- **WPF (A-):** `ElementHost`/`WindowsFormsHost` for bidirectional WinForms
  interop. `HwndHost` for Win32. Rich COM interop. Can embed in and host from
  WinForms. Mature migration path from WinForms
- **WinUI 3 (B+):** XAML Islands for WPF/WinForms hosting. WebView2 for web
  content. `AppWindow` for Win32 access. Migration from UWP is non-trivial
  (sandbox removal, API changes)
- **Blazor (A-):** `BlazorWebView` hosts Razor components inside WPF, WinForms,
  and MAUI. Same Razor components work in Blazor Web App and Blazor Hybrid —
  genuinely shared UI code across web, desktop, and mobile. `IJSRuntime` +
  `[JSInvokable]` for typed JS interop. Razor Class Libraries are the
  reusable packaging unit. Caveat: Hybrid renders HTML in a WebView (not
  native controls); platform API access requires MAUI APIs or JS interop
  bridges. "Not actually native" is the defining trade-off
- **Reactor (A-):** **Best interop story in the Microsoft ecosystem.**
  `ReactorHostControl` drops into any WinUI XAML layout — no `ReactorApp` required.
  `XamlHostElement`/`XamlPageElement` embed existing XAML in Reactor trees.
  `UseObservable`/`UseObservableTree`/`UseObservableProperty`/`UseCollection`
  bridge unmodified MVVM ViewModels. `.Set()` provides direct WinUI control
  access. Same-project coexistence with no rewrite required.
  **NEW: WinForms interop** via `Reactor.Interop.WinForms` library:
  `XamlIslandControl` hosts Reactor/WinUI content inside WinForms layouts with
  WinForms designer support (ComponentType property with dropdown), proper
  Tab/Shift+Tab focus bridging across the WinForms↔WinUI boundary, per-monitor
  DPI awareness, and `XamlIslandBootstrap` for WinForms-primary apps (WinForms
  owns the message loop). 13 E2E tests covering rendering, keyboard navigation,
  and accessibility across the island boundary. Reactor can now be incrementally
  adopted from both WinUI 3 and WinForms — the two largest Windows desktop
  frameworks

**Gap:** Reactor's interop is a **genuine strength** — the best incremental
adoption story across all frameworks analyzed. Three adoption paths: WinUI→Reactor
(`ReactorHostControl`), WinForms→Reactor (`XamlIslandControl`), and XAML-in-Reactor
(`XamlHostElement`). The ability to bridge existing MVVM ViewModels with a
single hook call (`UseObservable(viewModel)`) and to embed Reactor alongside
XAML in the same window, plus WinForms designer support, means Reactor can be
adopted from the vast majority of existing Windows desktop apps.

---

### 19. Forms & Data Entry

**What this measures:** Two-way data binding, built-in validation, error display,
input formatting/masking, focus management for form-heavy LOB applications.

**Competitor standard:** WPF has the richest validation system (INotifyDataErrorInfo,
ValidationRule, ErrorTemplate, BindingGroup). Flutter has the best built-in
validation of declarative frameworks (Form.validate()). React Hook Form + Zod
is the most productive ecosystem solution. SwiftUI and Compose have no
built-in validation.

**Microsoft assessment:**
- **WinForms (B):** `BindingSource` for two-way binding. `Validating`/`Validated`
  events. `ErrorProvider` component (icon + tooltip display). `MaskedTextBox`
  for input masking. Simple but effective for LOB forms
- **WPF (A):** **The most powerful form/validation system of any framework.**
  Two-way binding with `INotifyPropertyChanged`. `INotifyDataErrorInfo` for
  async validation with multiple errors per field. `ValidationRule` for binding-
  level validation. `ErrorTemplate` for custom error visuals. `BindingGroup`
  for transactional edits. `IValueConverter`/`IMultiValueConverter` for
  transforms. This is WPF's strongest category relative to competitors
- **WinUI 3 (B):** `x:Bind` compiled two-way binding. `NumberBox` with built-in
  min/max/increment validation. No built-in validation framework (unlike WPF).
  CommunityToolkit.Mvvm's `ObservableValidator` fills the gap. `XYFocus` for
  directional navigation (gamepad/Xbox)
- **Blazor (A-):** **`<EditForm>` + `<DataAnnotationsValidator>` + typed
  `<Input*>` components is the most comprehensive form/validation story of
  any modern declarative framework.** `EditContext` tracks per-field touched/
  modified/valid state; expression-tree field identification (`@(() =>
  m.Prop)`) is compile-checked against the model; typed inputs (`InputText`,
  `InputNumber<T>`, `InputDate<T>`, `InputCheckbox`, `InputSelect<T>`,
  `InputRadioGroup<T>`, `InputFile`) wrap native inputs with two-way binding,
  validation state visualization, and accessibility markup. Works in both
  Interactive and Static SSR with antiforgery integration. Only WPF's full
  depth (BindingGroup, ErrorTemplate) is ahead
- **Reactor (B):** Full validation system with `ValidationContext` (thread-safe,
  multi-error, multi-severity, touched/dirty state tracking, internal vs.
  external messages). 10+ built-in validators (Required, MinLength, MaxLength,
  Range, Match, Email, Url, Must, MustAsync, MustBeTrue, EqualTo) plus async
  validators with cancellation. `FormField` component renders label with
  required indicator, wrapped content, and description/error area with
  automatic validation — the reconciler calls `ValidateAttached()` on mount
  and update, so `.Validate("email", email, Validate.Required())` on an
  element is all that's needed. Cross-field `ValidationRule` auto-evaluates
  when placed in the tree. `ValidationVisualizer` supports Inline, Summary,
  InfoBar, and Custom display modes with severity filtering and ShowWhen
  gating. ErrorBubbling is fully wired. Error styling via
  `.WithErrorStyling()` applies border changes from theme resources.
  `UseValidationContext` hook propagates context via Context. 330+ tests
  cover the full pipeline. Still behind WPF: no transactional editing
  (BindingGroup), no custom error templates (one fixed FormField layout),
  no schema-driven or model-level validation, no conditional validation
  without manual predicates. Submit flow is partly manual (`MarkAllTouched()`
  is app responsibility)

**Gap:** WPF's form/validation system is **ahead of every competitor** — no
declarative framework has equivalent depth (transactional edits, multiple
errors per field, binding-level validation rules, custom error templates).
This is WPF's most underappreciated strength. **Blazor's `<EditForm>` is a
close second (A-)** — the best form story of any modern declarative framework
and the feature most worth learning from. Reactor has closed significant
ground (from C+ to B) with automatic validation, FormField rendering, and a
comprehensive validator library — comparable to Compose's approach and
approaching Flutter's `Form.validate()` integration. The remaining gap to
Blazor/WPF is in data-annotation-driven validation, template customization,
and binding-level validation integration.

---

### 20. Charting & Chart Accessibility (Reactor-specific)

**What this measures:** Built-in charting, chart accessibility for screen
readers, keyboard navigation of charts, color-blindness accommodation. No
competitor scorecard slot because only Reactor and Apple Charts ship this
as a framework concern.

**Competitor standard:** Apple Charts (iOS 16+, 50+ chart types, audio
graphs/sonification via VoiceOver) is the benchmark. Compose has Vico and
community libraries. React has D3 and countless charting libs (not
framework-integrated). WinUI ships no chart library — enterprise WinUI
apps use LiveCharts, OxyPlot, or Telerik/DevExpress/Syncfusion.

**Reactor:** Spec 026 + 9 implementation phases shipped a full charting
sub-framework in 72 hours: 43 D3-ported samples, plus an 8-layer
accessibility infrastructure:

1. **Automation peer infrastructure.** `ChartAutomationPeer` implements
   `IGridProvider` (series × points) and `ITableProvider`; `ChartPointProvider`
   exposes per-point `IValueProvider`; `IScrollProvider` for large datasets
2. **Per-point labels + auto-summarization** via `ChartSummarizer` —
   "Line chart, 5 series, 120 points. Revenue trending upward by 12%"
3. **Alternate-view convention.** `.AlternateView(Element)` attaches a
   developer-supplied DataGrid; T-key toggle, focus save/restore, live
   announcement handled by the framework
4. **Keyboard navigation.** Arrow keys walk points/series, Home/End jump
   to edges, Ctrl+arrow steps by series/axis, +/- zoom, Shift+arrow brush,
   L focuses legend. Highcharts/Power BI model translated to WinUI
5. **Focus context** preserves focused point across view transitions
6. **Live announcements** via debounced (400ms trailing) `ChartLiveAnnouncer`
7. **Forced colors, reduced motion, double encoding.** `ChartPalette.ForcedColors`
   swaps to `[CanvasText, Highlight, LinkText, GrayText]` under high
   contrast. `ChartPalette.Harden` runs deterministic LCH-space lightness
   adjustment to meet WCAG AA contrast — no other C# declarative framework
   ships this. `ColorblindSimulate` uses Brettel matrices. Series are
   double-encoded (color + shape + dash) by default; `.ColorOnly()` warns
   via the scanner. `ReactorHost` honors `WindowsThemeSettings.HighContrast`
   and `UISettings.AnimationsEnabled`
8. **Scanner rules.** 12 chart-specific rules (`A11Y_CHART_001..012`):
   missing title, missing axis labels, missing point labels, color-only
   encoding, insufficient contrast, tiny hit targets, etc. Fix suggestions
   include CLI commands (`reactor charts harden`) for AI-agent consumption

The 43-sample gallery got retrofitted to use all of this in one pass
(commit `229e41b`) — a real counter-example to the "features exist in
isolation" pattern that dominates the rest of the review.

**Remaining gaps:** No sonification / audio graphs — Apple Charts and
Highcharts both ship this. Spec explicitly lists it as "A+ ceiling deferred."
ForceGraph a11y is explicitly decorative-only — screen reader users get the
node/edge list but no interactive exploration of dense graphs. Hit-target
expansion (24×24px minimum for WCAG 2.5.8) requires `.Interactive()` opt-in;
static analytics charts don't get it. Forced-colors palette clips to 4
series — beyond that, colors collide under high contrast. `ChartPointProvider`
peer allocation is unbounded — 10,000-point scatterplot creates 10,000 peer
instances on each UIA walk, unprofiled. "Alternate view" depends entirely
on app-supplied DataGrid content — no canonical fully-integrated sample
that syncs sort/filter state between chart and table. Live-region debounce
is hard-coded at 400ms. Chart scanner rules don't merge with general a11y
scanner rules, so `A11Y_001` + `A11Y_CHART_001` both fire on a titleless
chart. E2E Appium coverage for charts is thin (~153 lines). No third-party
dashboard has adopted this — zero field evidence, no 10k-point stress
testing, no screen-reader-user survey.

**Gap:** Reactor's chart accessibility is arguably the most comprehensive
*system* in any C# declarative framework and ahead of everything except
Apple Charts on most dimensions. Only sonification and ForceGraph
interaction remain as clear competitive gaps. Grade: **B+** as a new
category; would be **A-** with sonification and always-on hit-target
expansion.

---

### 21. Devtools & Tracing Infrastructure (Reactor-specific)

**What this measures:** Framework-integrated debugging, tree inspection,
automation, tracing.

**Competitor standard:** React DevTools (component tree, props, hook
inspection, Profiler flame graphs). Compose Layout Inspector (per-composable
recomposition counts). SwiftUI Xcode Previews (interactive preview). Apple
Instruments (sophisticated profiling). None ship an MCP server.

**Reactor:** An unusual position. Spec 024+025 shipped a Model Context
Protocol devtools server — `reactor.tree`, `reactor.click`, `reactor.type`,
`reactor.state`, `reactor.screenshot`, `reactor.logs`, `reactor.reload`,
`reactor.waitFor`, `reactor.fire`, `reactor.switchComponent` — over HTTP
JSON-RPC and stdio. `mur devtools` is a supervisor; `mur devtools call`
provides CLI parity for humans. Stable window-scoped node ids
(`r:<window>/<local>`) survive re-renders. UIA-based automation means
tests authored against the devtools surface also validate accessibility.
Single-instance lockfile with pid probe + HTTP liveness check. ETW
tracing via `Microsoft-UI-Reactor` EventSource emits 6 keyword categories
to classic ETW and EventPipe; consumers use PerfView or dotnet-trace.

**What's missing:** `reactor.state` is read-only — no `reactor.setState`.
Tree diffing deferred to a later phase (agents must cache client-side).
Source mapping (tree node → C# file/line) is v1.1. No cache inspection
for `QueryCache`. No performance profiling via MCP. ETW has no
per-component render timing or per-hook execution breakdown — only
aggregate boundaries. DEBUG-gated with risk of Release-build leak if
env vars misconfigure. The previous `--preview` flag was replaced
(renamed to `--devtools`), so non-agent developers have a regression
in mental model — screenshots are now a devtools sub-capability rather
than a first-class command. Traditional-developer tooling (live component
tree in an IDE panel, per-component Profiler) still doesn't exist.

**Gap:** No other C# UI framework — and for that matter, no other
declarative UI framework on any platform — ships an MCP server. The
architectural choices (UIA as the bus, MCP as the protocol, stable
node ids, CLI parity, single-instance lockfile) are correct. The bet
is that AI agents are a primary UI-debugging audience. If that bet
pays off, Reactor is uniquely positioned; if developers still prefer
React DevTools-style GUI devtools, Reactor shipped the supplement
before the primary. Grade: **B** as a new category.

---

## Gap Analysis: Microsoft vs Competitor Median

| Category | Competitor Median | Best MS | MS Grade | Gap |
|---|---|---|---|---|
| Declarative Syntax | A- | Blazor (B+) | B+ | **Half grade behind** |
| Component Architecture | A- | Reactor (B+) | B+ | **Half grade behind** |
| State & Reactivity | B+ | Reactor (B+) | B+ | **Matched** |
| Rendering & Performance | B+ | WinUI 3 (B+) | B+ | **Matched** |
| Layout | A- | WPF/WinUI 3 (A-) | A- | **Matched** |
| Styling & Theming | A- | WinUI 3 (A) | A | **Matched or ahead** |
| Navigation | B+ | Reactor (B+) | B+ | **Matched** |
| Animation | B+ | WinUI 3 (A) | A | **Ahead** |
| Accessibility | B+ | WinUI 3 (A) | A | **Ahead** |
| Input & Gestures | B+ | WPF/WinUI 3/Blazor/Reactor (B+/B) | B+ | **Matched** |
| Developer Experience | A- | WinForms/Blazor/Reactor (B) | B | **1 grade behind** |
| Platform Reach | A- | Blazor (B+) | B+ | **Half grade behind** |
| Testing | B+ | Blazor (A-) | A- | **Ahead** |
| Error Handling | C+ | Blazor (B+) | B+ | **Ahead** |
| Data Loading & Async | A- | Blazor/Reactor (B+) | B+ | **Half grade behind** |
| Lists & Virtualization | B+ | WPF/WinUI 3 (A-) | A- | **Ahead** |
| Internationalization | B+ | Blazor/Reactor/WinUI 3 (B+) | B+ | **Matched** |
| Interop & Adoption | A- | Blazor/Reactor (A-) | A- | **Matched** |
| Forms & Data Entry | B | WPF (A) | A | **Ahead** |
| Charting + Chart A11y | C (Compose/median) | Reactor (B+) | B+ | **Ahead** |
| Devtools & Tracing (MCP) | — (unique) | Reactor (B) | B | **Unique industry position** |

### Where Microsoft leads or matches:
1. **Forms & Data Entry** (WPF) — The richest validation system of any
   framework. INotifyDataErrorInfo, ValidationRule, ErrorTemplate, BindingGroup.
   No competitor matches this depth. **Blazor's `<EditForm>` (A-) is a close
   second** and the best form story of any modern declarative framework
2. **Testing** (Blazor) — bUnit is the renderer-level equivalent of
   ComposeTestRule / React Testing Library. **Ahead of the competitor median**
   (A- vs B+) and the best testing story in the Microsoft ecosystem
3. **Lists & Virtualization** (WPF/WinUI 3) — VirtualizingStackPanel,
   ItemsRepeater, ICollectionView. Ahead of all competitors at the median.
   Blazor's built-in `<Virtualize>` + QuickGrid covers the web side
4. **Animation** (WinUI 3) — Composition API is world-class. Ahead of the
   competitor median (B+)
5. **Accessibility** (WPF/WinUI 3) — UIA is the most comprehensive a11y API.
   Ahead of the competitor median (B+)
6. **Styling & Theming** (WPF/WinUI 3) — WPF's ControlTemplate is unmatched;
   WinUI 3's Fluent Design is competitive. Matched or ahead of median (A-)
7. **Layout** (WPF/WinUI 3) — Panel system matches the best competitors
8. **Error Handling** (Blazor/Reactor) — `<ErrorBoundary>` in both Blazor
   (B+) and Reactor (B) is ahead of all but React
9. **Interop & Adoption** (Blazor/Reactor) — Both A-. Blazor's BlazorWebView
   hosts in WPF/WinForms/MAUI (web components on desktop, not native);
   Reactor's `UseObservable` + `ReactorHostControl` + `XamlIslandControl`
   drops native declarative UI into existing WinUI/WinForms
10. **Rendering** (WinUI 3) — Composition layer's independent animation thread
    is competitive
11. **Navigation** (Reactor) — Architecturally competitive with Compose Nav 3
12. **Commanding** (Reactor) — No competitor has this. Unique differentiator

### Where Microsoft lags significantly:
1. **Platform Reach (native frameworks)** — WinForms, WPF, WinUI 3, and
   Reactor are Windows-only vs 2-7 platforms for competitors. Unbridgeable
   without Avalonia/Uno. Blazor Hybrid is the only first-party way out, at
   the cost of rendering HTML in a WebView rather than native controls
2. **Developer Experience** — No equivalent to React DevTools, Flutter hot
   reload, or Compose Layout Inspector in *any* Microsoft framework including
   Blazor and Reactor
3. **Declarative Syntax** — XAML is a generation behind; Blazor's Razor and
   Reactor's method-call syntax both narrow the gap but C# lacks the language
   features of Swift/Kotlin. Avalonia modernizes XAML (compiled bindings,
   CSS-like styling) but the syntax remains fundamentally XAML
4. **State & Reactivity** — No Microsoft framework has automatic fine-grained
   tracking. Blazor's `StateHasChanged` is notably the most manual of the set
5. **Data Loading & Async** — No lifecycle-scoped async primitives (vs
   `.task`, `LaunchedEffect`, Suspense). MVVM async patterns work but are
   imperative. Blazor's streaming rendering is SSR-only

---

## Framework Profiles

### WinForms — "The Workhorse"

**Best for:** Simple line-of-business apps, rapid prototyping, tools with
minimal UI complexity.

**Profile:** Fastest startup (<200ms), lowest memory (15-30MB), simplest
mental model. Zero declarative capability. Zero modern UI features. Still
maintained in .NET 9 but architecturally frozen since 2002. Dark mode
support was added as a preview in .NET 9 — the first major visual update
in decades.

**Competitive position:** WinForms is not competing with modern declarative
frameworks. It occupies a different niche entirely: maximum simplicity for
maximum developer velocity on trivial UIs. In that niche, it has no peer.

### WPF — "The Enterprise Standard"

**Best for:** Complex desktop applications requiring rich data visualization,
custom control styling, and MVVM architecture.

**Profile:** The most powerful styling/theming system of any framework. The
most powerful form validation system of any framework. Rich layout. Strong
accessibility. Excellent virtualized lists. Good animation. Mature ecosystem
with 120,000+ Stack Overflow questions and extensive third-party control
suites (Telerik, DevExpress, Syncfusion). In "sustaining engineering"
mode — bug fixes but no new features.

**Competitive position:** WPF is architecturally contemporary with 2006-era
thinking: XAML markup, class-based controls, INotifyPropertyChanged. It
lacks the declarative paradigm shift (function components, reactive state,
virtual DOM) that defines modern frameworks. However, its raw capability
in layout, styling, accessibility, forms, and virtualized lists remains
competitive or superior to modern alternatives. The gap is in ergonomics
and developer experience, not in what's possible.

### WinUI 3 — "The Modern Platform"

**Best for:** New Windows apps requiring Fluent Design, modern animation,
and the latest platform integration.

**Profile:** WinUI 3's Composition API is its crown jewel — independent
animation thread, implicit animations, connected animations, spring
physics. Fluent Design with Mica/Acrylic materials. Full UIA accessibility.
Windows App SDK updates are active. But: Windows-only, packaging complexity,
smaller community than WPF, documentation gaps.

**Competitive position:** WinUI 3 is the strongest Microsoft option for new
development. It matches or exceeds competitors in animation, accessibility,
and styling. The gaps are in the declarative programming model (still XAML
+ code-behind / MVVM), developer experience (tooling is weaker than
competitors), and platform reach. The Windows App SDK is active but
adoption has been slower than hoped.

### Blazor — "The Web Framework With a Desktop Side Door"

**Best for:** Web apps written in C# end-to-end; LOB apps where form/
validation depth matters; teams that need to ship the same UI across web,
desktop, and mobile without learning JavaScript.

**Profile:** Microsoft's only modern declarative component framework. Razor
components mix HTML-like markup with C# via `@` directives, compiling to
C# classes derived from `ComponentBase`. Four render modes (Static SSR,
Interactive Server, Interactive WebAssembly, Interactive Auto) let different
parts of an app pick different interactivity models. **Forms are Blazor's
crown jewel** — `<EditForm>` + `<DataAnnotationsValidator>` + typed `<Input*>`
components make it the best form/validation story of any modern declarative
framework. **`<ErrorBoundary>`** puts Blazor in a small club with React and
Reactor. **bUnit** is the best testing story in the Microsoft ecosystem.
**`<Virtualize>` and QuickGrid** are built-in. Compile-time sequence numbers
enable linear-time render-tree diffing.

**Blazor Hybrid** (`BlazorWebView` in WPF/WinForms/MAUI) makes Blazor the
only first-party Microsoft framework with genuine cross-platform reach —
at the cost of rendering HTML in a WebView rather than platform-native
controls. You don't get native UIA trees, native platform controls, or
native look-and-feel. Platform API access requires MAUI APIs or JS interop
bridges. `[PersistentState]` (.NET 10) closes the prerender → interactive
double-fetch gap; streaming rendering is an SSR Suspense equivalent.

**Biggest weaknesses:** `StateHasChanged()` is a manual render trigger —
no automatic reactivity tracking. No component DevTools. WebAssembly
initial download is hefty (hundreds of KB even post-trim). Server mode
is latency-sensitive. No type-safe route navigation. Class-based
components are a generation behind function-as-component models. No
built-in animation system beyond CSS transitions.

**Competitive position:** Blazor competes on the **same playing field as
React, SwiftUI, and Compose** as a declarative component framework — the
only Microsoft framework that does. It is **ahead of the competitor median
in testing (A- vs B+) and error handling**, tied in interop, and close
behind on forms (WPF still wins). Its main competitor positioning is vs
React — with the value prop being "C# end-to-end and Microsoft support" at
the cost of a smaller ecosystem. Vs Reactor, it sits across an architectural
divide: Blazor Hybrid reuses web components on desktop (non-native);
Reactor renders native WinUI 3 controls from a React-style component
model (native, Windows-only).

### Reactor — "The Declarative Experiment"

**Best for:** Teams wanting React-style declarative UI on WinUI 3 with
type safety and no XAML.

**Profile:** The only Microsoft-ecosystem option with a modern declarative
component model: function components, hooks, reconciler, context, navigation,
commanding. 94% of WinUI controls wrapped. Solid component foundation
(context system, default-on memoization via record prop equality, generic
hook state without boxing, persisted state, post-render effect cleanup).
Navigation is architecturally competitive with comprehensive deep linking
(typed params, wildcards, query strings) and runtime diagnostics.
Commanding is a genuine industry-first (define-once commands bundling
execute + canExecute + label + icon + accelerator + description, 16 standard
commands, async lifecycle, focus-scoped accelerators). ErrorBoundary exists
(rare). Interop with existing WinUI/MVVM code is excellent (`ReactorHostControl`,
`UseObservable`, `XamlHostElement`) and extends to WinForms via
`XamlIslandControl`. ICU localization closes WinUI 3's plural/gender gap.
Form validation (FormField + 10+ validators + ValidationVisualizer).
`DataGrid` with paged LRU caching, server-side sort/filter, inline editing,
async commit with optimistic updates. ~40 theme tokens with `ResourceBuilder`
lightweight styling (a genuinely unique feature — no other C# declarative
framework wraps WinUI's per-control resource key overrides), style caching
that eliminates the XamlReader.Load-per-element perf concern, and Roslyn
analyzers. Accessibility has crossed a threshold: `SemanticPanel` solves the
hardest architectural problem (custom automation peers for composites),
`AccessibilityScanner` runtime WCAG diagnostics + 3 Roslyn analyzers
(`REACTOR_A11Y_001–003`) = a three-layer diagnostic system (compile-time +
runtime + E2E) no other C# UI framework has. **Async data is now a full
framework subsystem** — `UseResource`, `UseInfiniteResource`, `UseMutation`,
`Pending`/`PendingScope`, `AsyncValue<T>` ADT, `QueryCache` with per-key
locks, ref-counted subscriptions, TTL + pattern invalidation, focus
revalidation; `DataGrid` migrated to `UseMutation` as a real integration
test. Four `REACTOR_HOOKS_*` analyzers cover the most common hook mistakes.
**Spec 027 made input declarative**: pointer/tap/keyboard/focus modifiers,
typed pan/pinch/rotate gestures with phase+delta+velocity+inertia metadata,
drag-and-drop with eager/sync/async provider overloads; stable trampoline
dispatch replaces per-render COM detach/attach. **Charting + chart a11y**
is an 8-layer sub-framework (43 samples, 12 scanner rules, WCAG-hardening
palette, forced-colors remap, keyboard navigation, debounced live
announcements) — arguably the most comprehensive chart accessibility on
any platform except Apple Charts' sonification. **Animation** is now
operational (all 4 previously-identified integration bugs fixed), though
compositor-property-bound (Opacity, Scale, Rotation, Translation, CenterPoint
+ 3 brush swaps). **MCP devtools** is unique industry-wide (`reactor.tree`,
`reactor.click`, `reactor.state`, etc. over HTTP + stdio). **ETW tracing**
closes part of the profiling gap. ~5,600 lines of new coverage tests pushed
selftest past 85%. **WinForms interop** via `XamlIslandControl` opens
brownfield adoption.

**But — and it's a growing list:** `UseColorScheme` reads app-level theme,
not element effective theme, so the headline RequestedTheme + UseColorScheme
scenario doesn't compose. Custom branded theme resources still don't exist
(multiple review cycles). Only 3 properties support ThemeRef bindings.
`SemanticPanel` covers 2 of ~20 UIA patterns. `UseFocusTrap` doesn't cycle —
the WAI-ARIA Dialog Pattern expectation isn't met. `UseAnnounce`,
`UseReducedMotion`, and `UseScreenReaderActive` are still unbuilt. Animation
ceiling is structural: Width, CornerRadius, Margin, FontSize, arbitrary
colors can't animate. `StandardCommand` labels are English-only (ironic —
Reactor has a full ICU localization system the commands don't use). No
command routing to the focused view. Accelerators rebuild on every render.
Delegate equality on `Command` records defeats the memoization system.
The reconciler is a giant type-based switch — no open/closed extensibility
for built-in types. Tag-based event dispatch is a fragile workaround.
Every component adds an invisible `Border` wrapper; NavigationHost adds
a `Grid`; CommandHost adds a `Grid`; SemanticPanel is a fourth wrapper —
a well-structured Reactor component can accumulate 4+ framework wrappers
between a component and its content. `ConnectedTransition` is a shipped
API that silently falls back to a slide animation. Async `QueryCache` has
no persistence, no automatic retry, no devtools surface, no stress tests,
zero field evidence. Drag-and-drop has 370 lines of new code with zero
showcase consumer. Typed drag format ids are unversioned — rename a model,
round-trip breaks. Trampoline dispatch is unvalidated at the thing it
promises (no before/after render benchmark; 6,390 unit tests that don't
exercise COM interop don't measure COM interop). The `REACTOR_*` analyzer
rename is partial (`DUCT001-003` still carry the old product name).
E2E tests were just discovered to be 95% invisible behind a broken filter
(now fixed, but the blind spot for the entire dev period is telling).
Three to four invisible wrapper element types, and a running theme of
string-typed APIs at boundaries (deep link patterns, semantic roles,
lightweight styling keys, connected-animation keys, drag-format ids).
`.Set()` surface is materially thinner than a quarter ago but still carries
composition-layer effects, materials, windowing, pointer-capture, custom
geometry, and ink. And the single most damning critique that's been
constant for three review cycles: **the showcase apps don't use the
framework's own features.** Outlook clone still uses `UseState<string>`
for navigation. File manager still needs `SynchronizationContext` capture
for off-thread state updates. None of the four flagship apps use context,
memoization, persisted state, commanding, navigation, SemanticPanel,
async resources, UseFocusTrap, or any of the spec 027 input modifiers.
The charting gallery is the one bright exception — all 43 samples were
retrofitted in one pass, which proves the work is possible; it just isn't
being prioritized for the general-purpose flagship apps.

**Competitive position:** Reactor now has *five* features where it's ahead
of the entire industry: commanding (no competitor has define-once commands
with metadata bundling), lightweight styling (no competitor wraps WinUI's
per-control resource overrides), the AccessibilityScanner + chart a11y
system (no C# UI framework ships framework-integrated runtime WCAG
diagnostics, and no declarative framework ships an 8-layer chart
accessibility system), MCP devtools (no framework ships an MCP server —
whether this bet pays off depends on adoption patterns that don't exist
yet), and ErrorBoundary (shared with React). These are real differentiators,
not catch-up. But "features exist in isolated demos" and "features compose
correctly in real apps under production load" are different claims.
Reactor's velocity is impressive; the integration debt is accumulating
at a rate that's also impressive in a less comforting way. Each review
cycle, new capability arrives faster than the previous cycle's features
get integrated into the flagship apps. Navigation and commanding moved
Reactor from "component library with hooks" to "framework with application
architecture." Accessibility moved it from "annotations on primitives" to
"a layered system." Async resources closed the largest remaining capability
gap. Spec 027 closed the largest remaining "this is just a wrapper" gap.
But the Outlook clone still uses `UseState<string>` for navigation.
Production-readiness is where that gap closes or the framework stalls.

---

## Key Takeaways

### 1. The platform reach gap is structural — Blazor and Avalonia are the two escape hatches

Every competitor targets multiple platforms. Microsoft's *native* frameworks
(WinForms, WPF, WinUI 3, Reactor) all target one. Two first-party-or-adjacent
escape hatches exist, each with a distinct trade-off:

- **Blazor Hybrid** ships the same Razor components across web, WPF,
  WinForms, MAUI (Windows/Mac/iOS/Android). The cost is rendering HTML in
  a WebView rather than platform-native controls — no native UIA, no native
  controls, no native look. Microsoft-owned and first-party
- **Avalonia** covers 7 platforms with 30K+ GitHub stars and production use
  at JetBrains, Autodesk, and NASA. Only .NET framework with Linux desktop
  support and native Linux accessibility (AT-SPI2). Self-renders via Skia
  (also non-native look), but closer to WinUI 3 in architecture than Blazor.
  Third-party

Teams committed to Windows-only still benefit from WPF/WinUI 3/Reactor's
deeper platform integration. Teams that need cross-platform .NET have a
real choice between Blazor (web-style components, WebView) and Avalonia
(XAML-style components, Skia). Neither gives native platform controls.

### 2. WinUI 3 is stronger than its reputation

WinUI 3 is often criticized for slow adoption and packaging complexity. But
in raw capability — animation, accessibility, styling, rendering — it matches
or exceeds most competitors. The Composition API in particular is arguably
the most powerful animation system across all frameworks analyzed. The gap
is in developer experience and the declarative programming model, not in
the underlying platform.

### 3. Blazor is Reactor's closest cousin — but the bets are opposite

Blazor and Reactor are both C#-first declarative component frameworks in the
Microsoft ecosystem. They make opposite bets on the most important
architectural choice:

| | Blazor Hybrid | Reactor |
|---|---|---|
| **Renderer** | HTML in a WebView | Native WinUI 3 controls |
| **Controls** | HTML elements | Native UIElement tree |
| **Accessibility** | Manual ARIA | UIA (automatic from WinUI) |
| **Look & feel** | Browser-chrome HTML | Native Windows + Fluent Design |
| **Animation** | CSS transitions | Composition API |
| **Component model** | Class-based + `StateHasChanged` | Function-as-component + hooks |
| **Platform reach** | Web + Desktop + Mobile | Windows only |
| **Forms** | `<EditForm>` (A-) | FormField + validators (B) |
| **Testing** | bUnit (A-) | Unit tests per component (B-) |

**Blazor's form story, testing infrastructure, and error boundary are three
features Reactor should learn from directly.** Blazor's `StateHasChanged`
model, string-literal routing, and class-based components are three things
Reactor already does better. **Reactor's native-rendering, native-UIA,
native-look positioning is its structural advantage over Blazor Hybrid on
Windows desktop** — positioning that's worth stating explicitly in marketing.

### 4. Reactor addresses the right gaps — but feature velocity is outpacing integration

Reactor's declarative model, navigation, and commanding are not random feature
additions — they directly address the areas where WPF/WinUI 3 are weakest
relative to competitors (declarative syntax, component model, navigation
type safety). The commanding system is genuinely novel. Recent work has
deepened coverage across the board: theming (style caching + lightweight
styling + analyzers), forms (automatic validation + FormField), accessibility
(SemanticPanel + AccessibilityScanner + 3 analyzers + UseFocusTrap), animation
(all 4 integration bugs fixed, API now faithful), DataGrid (paged caching +
inline editing + server-side sort/filter), navigation (destination guards,
wildcards, diagnostics), interop (WinForms via XamlIslandControl), **async
data (full subsystem — UseResource/UseInfiniteResource/UseMutation with
AsyncValue ADT and QueryCache)**, **charting + chart a11y (8-layer system
with WCAG-hardening palette + scanner rules)**, **MCP devtools (unique
industry-wide)**, **ETW tracing**, and **spec 027 declarative input
(pointer/gesture/focus/drag-drop modifiers + trampoline dispatch)**. The key
remaining gaps are structural (animation compositor-property ceiling, no
custom theme resources, SemanticPanel covering 2 of ~20 UIA patterns),
compositional (UseColorScheme/RequestedTheme don't compose correctly,
StandardCommand labels aren't localized despite Reactor having a full ICU
system, no command routing to focused view), and — most uncomfortably —
integrative. The showcase apps (Outlook clone, file manager, registry
editor, word puzzle game) don't use navigation, commanding, context,
memoization, persisted state, SemanticPanel, UseFocusTrap, async resources,
or any of the spec 027 input modifiers. Every new feature ships with an
isolated demo; the flagship apps freeze in time. The charting gallery is
the one bright exception (43 samples retrofitted in one pass, proving the
team *can* do integration work). Production-readiness doesn't live in
"features exist" — it lives in "features compose in real apps under real
load." That gap is widening, not closing.

### 5. Error handling is an industry-wide gap — but Microsoft has two answers

React's ErrorBoundary is the only production-quality error containment
mechanism among the big competitor set. SwiftUI, Compose, and Flutter all
crash the app on view-level errors. **Both Blazor (`<ErrorBoundary>`) and
Reactor (`ErrorBoundary`) ship error boundaries** — putting two Microsoft
frameworks in a small club with React. This is a genuine Microsoft-ecosystem
differentiator that's under-marketed.

### 6. State management is converging on automatic observation

SwiftUI's `@Observable`, Compose's Snapshot system, and signals-based
frameworks (SolidJS, Angular Signals, Vue's reactivity) all converge on
the same idea: **the framework should automatically track which state each
view reads and only re-render when that specific state changes.** React
is moving in this direction with the React Compiler. Neither WPF, WinUI 3,
Avalonia, nor Reactor have this — all require manual INotifyPropertyChanged or
explicit dependency arrays. Avalonia's `IObservable<T>` stream bindings and
ReactiveUI integration are the most ergonomic reactive patterns in the .NET
ecosystem, but they still require explicit subscription rather than automatic
tracking. This is the most impactful architectural gap to close across the
entire .NET UI landscape.

### 7. WPF's form/validation system is an underappreciated asset — and Blazor's is the best modern version

WPF's two-way binding engine with `INotifyDataErrorInfo`, `ValidationRule`,
`ErrorTemplate`, `BindingGroup`, and `IValueConverter`/`IMultiValueConverter`
is the most comprehensive form validation system of any framework in this
analysis. **Blazor's `<EditForm>` + `<DataAnnotationsValidator>` + typed
`<Input*>` components (A-) is the best form story of any modern declarative
framework** — it inherits .NET's `DataAnnotations` attributes, adds
expression-tree field identification, and integrates with ASP.NET Core
antiforgery. Reactor has closed significant ground (from C+ to B) with
automatic validation, FormField rendering, 10+ validators, and
ValidationVisualizer. But WPF's transactional editing (BindingGroup), custom
error templates (ErrorTemplate), and binding-level validation rules remain
unmatched, and Blazor's data-annotation-driven validation is still ahead
of Reactor's explicit-validator approach. For LOB applications — which are
the core use case for Windows desktop — Reactor should study both WPF's
depth and Blazor's declarative pattern.

### 8. Interop is Reactor's strongest selling point for adoption

Reactor's `ReactorHostControl` (drop into existing WinUI XAML), `UseObservable`
(bridge unmodified MVVM ViewModels with one line), `XamlHostElement`
(embed existing XAML pages in Reactor trees), and now `XamlIslandControl`
(host Reactor in WinForms with designer support and focus bridging) provide the
smoothest incremental adoption story across all frameworks analyzed. This
matters because the realistic path for Reactor adoption is not greenfield apps
— it's existing WinUI 3 and WinForms apps that want to go declarative
without a rewrite. With WinForms interop, Reactor now covers the two largest
Windows desktop frameworks as adoption entry points.

### 9. Avalonia validates the self-rendering cross-platform approach for .NET

Avalonia's trajectory mirrors Flutter's: self-render everything via Skia for
pixel-perfect cross-platform consistency, accept the trade-off of non-native
look-and-feel. With 30K+ GitHub stars, production use at JetBrains and
Autodesk, and partnership with Google's Flutter team on Impeller, Avalonia
has proven this model works for .NET. Its CSS-like styling system is genuinely
innovative in the XAML world. Its weaknesses — no built-in hot reload, paid
tooling tiers, smaller ecosystem than WPF/React/Flutter, RTL issues, class-
based component model — are real but not disqualifying. For the Microsoft
ecosystem, Avalonia's most important role is as proof that cross-platform
.NET desktop/mobile is viable, and as a migration path for WPF apps that
need macOS/Linux (via XPF). It does not compete with Reactor's declarative
model — Avalonia is still MVVM/class-based — but it competes directly with
WPF for new desktop development where cross-platform is a requirement.

### 10. No framework is complete

Every framework has embarrassing gaps:
- **SwiftUI:** No error boundaries, no official testing, type-checker issues,
  no form validation
- **Compose:** Stability system complexity, no error boundaries, no form
  validation
- **React:** No built-in layout/styling/animation/routing/gestures/lists/i18n
- **Flutter:** Verbose syntax, Navigator 2.0, web accessibility gaps, state
  fragmentation
- **Avalonia:** No built-in hot reload, no ICollectionView, RTL issues,
  no error boundaries, paid tooling creates friction, class-based component
  model is a generation behind function-as-component
- **WPF:** No hot reload for code, MVVM boilerplate, in maintenance mode,
  no lifecycle-scoped async
- **WinUI 3:** Packaging complexity, small community, Windows-only, no
  plural/gender i18n
- **Blazor:** Manual `StateHasChanged` (no auto-reactivity), no component
  DevTools, hefty WebAssembly payload, Server-mode latency sensitivity,
  CSS isolation doesn't cover third-party components, no type-safe routing,
  Hybrid renders in a WebView (not native), no built-in animation system
- **Reactor:** Animation capped at 5 compositor properties + 3 brush swaps
  (Width/CornerRadius/Margin/FontSize/colors can't animate).
  `UseColorScheme` reads app theme, not element effective theme — so
  RequestedTheme + UseColorScheme don't compose in the exact "dark sidebar
  in light app" scenario they were built for. Custom branded theme
  resources still don't exist. SemanticPanel covers 2 of ~20 UIA patterns.
  `UseFocusTrap` doesn't cycle (WAI-ARIA Dialog Pattern expects wrapping).
  `UseAnnounce`/`UseReducedMotion`/`UseScreenReaderActive` are still
  unbuilt. `StandardCommand` labels are hard-coded English. No command
  routing to focused view. Accelerators rebuild on every render. Delegate
  equality on Command records defeats memoization. 4+ invisible wrapper
  element types accumulate in the visual tree. `.Set()` still needed for
  composition layer, materials, windowing, pointer capture, custom
  geometry, ink. `ConnectedTransition` is shipped but silently falls back
  to slide. Trampoline dispatch has no render-cycle benchmark proving the
  perf claim it makes. Async `QueryCache` has no persistence, no auto-retry,
  no devtools surface, no stress tests. Drag-and-drop has zero consumer
  apps and unversioned typed format ids that break on model renames.
  Stringly-typed APIs at every platform boundary (deep link patterns,
  semantic roles, resource keys, connected-animation keys, drag formats).
  Analyzer IDs half-renamed (DUCT001-003 still carry the old product name).
  And the constant-across-three-review-cycles critique: **showcase apps
  don't use the framework's own features** — Outlook clone still uses
  `UseState<string>` for navigation; nothing uses SemanticPanel, commanding,
  context, UseFocusTrap, async resources, or spec 027 input modifiers
  except isolated demos

The "perfect framework" doesn't exist. The question is which gaps matter
most for your specific application.

---

## Sources

### Competitor frameworks (detailed analyses)
- [SwiftUI Analysis](swiftui.md) — Apple's declarative framework
- [Jetpack Compose Analysis](compose.md) — Google's/JetBrains' declarative toolkit
- [React Analysis](react.md) — Meta's component library
- [Flutter Analysis](flutter.md) — Google's cross-platform toolkit
- [Avalonia Analysis](avalonia.md) — Cross-platform .NET XAML framework

### Microsoft frameworks
- [Blazor Analysis](blazor.md) — Microsoft's component-based web/hybrid framework
- [Reactor Critical Review](../critical-review.md) — Detailed Reactor analysis
- [WinUI 3 Gap Analysis](../spec/duct-winui3-gap-analysis.md) — Reactor vs WinUI 3 coverage
- [Microsoft Learn: WinForms Overview](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/overview)
- [Microsoft Learn: WPF Architecture](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-architecture)
- [Microsoft Learn: Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/)
- [Microsoft Learn: WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
- [Microsoft Learn: Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/)
- [Microsoft Learn: Blazor Hybrid](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/)
- [Microsoft Learn: Composition API](https://learn.microsoft.com/en-us/windows/uwp/composition/)
- [Microsoft Learn: UI Automation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/ui-automation-of-a-wpf-custom-control)

### Avalonia
- [Avalonia 12 Release](https://avaloniaui.net/blog/avalonia-12)
- [Avalonia vs MAUI](https://avaloniaui.net/maui-compare)
- [Avalonia Impeller Partnership](https://avaloniaui.net/blog/avalonia-partners-with-google-s-flutter-t-eam-to-bring-impeller-rendering-to-net)
- [Avalonia Supported Platforms](https://docs.avaloniaui.net/docs/supported-platforms)
- [Avalonia GitHub](https://github.com/AvaloniaUI/Avalonia)

### Industry context
- [SolidJS vs React 2026](https://www.boundev.com/blog/solidjs-vs-react-2026-performance-guide)
- [JS Framework Benchmark](https://www.frontendtools.tech/blog/best-frontend-frameworks-2025-comparison)
- [WinUI vs WPF in 2026](https://www.ctco.blog/posts/winui-vs-wpf-2026-practical-comparison)
- [State of Swift 2026](https://devnewsletter.com/p/state-of-swift-2026/)
- [Compose Navigation 3 is Stable](https://android-developers.googleblog.com/2025/11/jetpack-navigation-3-is-stable.html)
- [React Compiler v1.0](https://react.dev/blog/2025/10/07/react-compiler-1)
- [Flutter's 2026 Roadmap](https://webartdesign.com.au/blog/flutters-2026-roadmap-just-dropped-and-its-all-about-finishing-the-job/)
- [Compose Multiplatform 1.8: iOS Stable](https://blog.jetbrains.com/kotlin/2025/05/compose-multiplatform-1-8-0-released-compose-multiplatform-for-ios-is-stable-and-production-ready/)
