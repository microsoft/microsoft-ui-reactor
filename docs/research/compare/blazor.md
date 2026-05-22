# Blazor — Framework Analysis

**Purpose:** Critical technical analysis for comparison against the other Microsoft UI frameworks (WinForms, WPF, WinUI 3, Microsoft.UI.Reactor) and the competitor lineup (SwiftUI, Compose, React, Flutter, Avalonia).

**Version analyzed:** Blazor in .NET 9 (Nov 2024) and .NET 10 (Nov 2025), part of ASP.NET Core.

---

## Overview

Blazor is Microsoft's component-based UI framework for building interactive web UIs with C# instead of JavaScript, first released in 2020 as part of ASP.NET Core 3.1. It is Microsoft's *web* answer to React/Vue/Angular — but it also has a desktop-hosting side door (**Blazor Hybrid** via `BlazorWebView` in WPF, WinForms, and MAUI) that makes it relevant alongside the desktop stack. In this analysis Blazor is categorized as a **Microsoft framework** — a peer of WinForms/WPF/WinUI 3/Reactor — because its primary delivery is Microsoft-owned and its Hybrid mode directly overlaps Reactor's problem domain.

Blazor is the only declarative, component-based UI framework Microsoft ships today. Its architecture is conceptually close to React (tree-diffing reconciler, parameter-driven data flow, a virtual-DOM-style render tree) but its reactivity model is notably manual (`StateHasChanged()`), its routing uses Razor's `@page` directive strings, and its form/validation story is genuinely excellent.

The .NET 8 "Blazor United" effort (shipped Nov 2023, refined through .NET 10) consolidated what used to be separate Server and WebAssembly project templates into a single **Blazor Web App** that can mix four render modes per component: Static SSR, Interactive Server, Interactive WebAssembly, and Interactive Auto. The .NET 10 release (Nov 2025) shrank the `blazor.web.js` bundle by ~76% (183 KB → 43 KB) and introduced `[PersistentState]` for prerender → interactive handoff.

---

## 1. Declarative Model & Syntax

**Razor:** Blazor components are `.razor` files — a templating language that mixes HTML-like markup with C# expressions. The Razor compiler emits a C# class derived from `ComponentBase` with a `BuildRenderTree(RenderTreeBuilder builder)` method. `@code { }` blocks hold component logic. `@if`, `@foreach`, `@bind` are C#-side directives that compile to standard control flow.

**Strengths:**
- Razor co-locates markup and logic in a single `.razor` file, comparable to JSX's co-location story
- Full C# expressiveness inside `@{ }` — type-safe, LSP-aware, refactorable
- Control flow uses native C# (`@if`, `@foreach`, `@switch`) rather than special directives
- Mature tooling in Visual Studio and JetBrains Rider (syntax highlighting, IntelliSense, refactoring, Roslyn analyzers)
- TypeScript equivalent is unnecessary — you already have C# end-to-end

**Weaknesses:**
- Razor is a separate language requiring a build step; error messages can reference generated code rather than the `.razor` source
- No compile-time view validation on parameter types vs. passed values (unlike SwiftUI's type-checked body or Compose's compiler plugin enforcing `@Composable`)
- Three syntax concepts to learn: HTML, Razor directives, and C# — vs SwiftUI/Compose which are pure Swift/Kotlin
- Markup-and-code mixing is less composable than function-as-component models (can't easily pass a "piece of view" around as a first-class value beyond `RenderFragment`)

**Sources:** [ASP.NET Core Razor components (Microsoft Learn)](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/)

---

## 2. Component Architecture

**ComponentBase:** Every Razor component derives from `ComponentBase` (unless you implement `IComponent` directly). Parameters are public properties annotated with `[Parameter]`. Parent→child data flow uses parameters; child→parent uses `EventCallback<T>`.

**Key patterns:**
- **`[Parameter]`** — input property, bound from parent markup
- **`[CascadingParameter]`** — receives value from ancestor `<CascadingValue>` component (Blazor's Context equivalent)
- **`EventCallback<T>`** — typed event delegate that **auto-triggers a re-render of the subscriber** (see §3)
- **`RenderFragment` / `RenderFragment<T>`** — first-class "piece of UI" values, equivalent to React children / render props
- **`[SupplyParameterFromQuery]`** / **`[SupplyParameterFromForm]`** — bind to URL or form data
- **`IComponentRenderMode`** — attribute-driven per-component render mode (Server/WebAssembly/Auto)

**Lifecycle hooks:** `OnInitialized`/`OnInitializedAsync`, `OnParametersSet`/`OnParametersSetAsync`, `OnAfterRender`/`OnAfterRenderAsync`, `ShouldRender`, `Dispose`. Roughly equivalent to React's class-component lifecycle before hooks existed.

**Strengths:**
- Type-safe parameter passing — the Razor compiler enforces parameter types at component call sites
- `RenderFragment` is first-class — a `@RenderFragment` can be stored, passed, and invoked like any C# value
- `EventCallback<T>` is an elegant primitive (more on this below)
- Source generators + `partial class` split let you keep `.razor` markup and `.razor.cs` code-behind cleanly separated
- `<CascadingValue IsFixed="true">` is an explicit perf opt-in — ancestors that never change don't subscribe descendants to re-render

**Weaknesses:**
- **Class-based components are a generation behind function-as-component models** (React hooks, SwiftUI structs, Compose `@Composable` functions). There is no hook equivalent in core Blazor
- Cross-cutting logic reuse requires base classes or mixins via interfaces — no equivalent to React custom hooks or Compose composables
- `[CascadingParameter]` is coarser than React Context — it has no selector story, consumers re-render on any change unless `IsFixed="true"`
- Lifecycle method proliferation (5 overrides, each with sync/async variants) is verbose compared to hooks
- `StateHasChanged()` leaks into component code as explicit re-render triggers (see §3)

**Sources:** `src/Components/Components/src/ComponentBase.cs`, `src/Components/Components/src/CascadingValue.cs`

---

## 3. State Management & Reactivity

**The `StateHasChanged` model:** Blazor has **no automatic reactivity tracking**. The render pipeline runs when `ComponentBase.StateHasChanged()` is called, which the framework does automatically in three cases (verified in `ComponentBase.cs`):

1. After a lifecycle method completes (`OnInitialized`, `OnParametersSet`, `OnAfterRender`)
2. After a parameter change from a parent
3. After an `EventCallback` fires — `ComponentBase.HandleEventAsync` calls `StateHasChanged()` immediately after invoking the delegate (ComponentBase.cs:355-367: "This just saves the developer the trouble of putting `StateHasChanged()`")

Anything else — a timer tick, a SignalR push, a property change on an injected service, a background `Task` completion, a JS interop callback, an `INotifyPropertyChanged` event — **requires explicit `StateHasChanged()` calls**, typically via `InvokeAsync(StateHasChanged)` to marshal onto the renderer's sync context.

**Built-in primitives:** None beyond fields on the component class. Blazor does not ship `useState`, signals, or an observable store.

**Ecosystem libraries (filling the gap):**
- **Fluxor** — Redux-style unidirectional state (the de facto enterprise choice)
- **Blazor-State** — MediatR-based state management
- **Skclusive.Mobx.Observable** — MobX port for auto-tracking
- **phork-blazor-reactivity** — lightweight reactive state library

**Strengths:**
- Explicit render model is easy to reason about — no "why did this re-render?" mystery
- `EventCallback<T>` eliminates `StateHasChanged()` boilerplate for the common case (child event → parent re-render)
- No dependency-array bugs (the #1 React bug category doesn't exist)

**Weaknesses:**
- **This is Blazor's biggest ergonomic weakness.** SwiftUI `@Observable`, React hooks, Compose Snapshot, Vue reactivity, and Solid signals all converge on automatic dependency tracking. Blazor does not
- `InvokeAsync(StateHasChanged)` sync-context marshaling is a concept developers must learn and apply correctly
- `[CascadingParameter]` has no selector — all consumers re-render on any change (same problem React Context has, with no ecosystem fix)
- External state stores (services, `INotifyPropertyChanged`, observables) require manual wiring to `StateHasChanged()`

**Sources:** `src/Components/Components/src/ComponentBase.cs` (HandleEventAsync, StateHasChanged)

---

## 4. Rendering & Performance

**Render tree with sequence numbers:** Blazor uses a virtual-DOM-like **render tree** but with a key architectural optimization: the Razor compiler injects **compile-time sequence numbers** into every `OpenElement`/`AddContent`/`AddAttribute` call. At diff time, the renderer uses these sequence numbers to do a **linear-time diff** rather than React's general tree-diffing algorithm. This is why Microsoft's docs explicitly discourage writing `BuildRenderTree` by hand — incorrect sequence numbers produce pathological diffs.

**RenderBatch + Dispatcher:** The renderer collects changes into a `RenderBatch` and dispatches them to the appropriate output (HTML diffs over SignalR, direct DOM patches in WebAssembly, WebView2 in Hybrid). Batching consolidates multiple state changes into a single update.

**Render mode impact on performance:**
- **Static SSR** — zero client JS, fastest initial load, no interactivity
- **Interactive Server** — sub-ms UI updates after connection, but each interaction is a SignalR round-trip (latency-sensitive)
- **Interactive WebAssembly** — zero network per interaction, but a hefty initial download (runtime + framework + app, often hundreds of KB even post-trim)
- **Interactive Auto** — starts as Server, upgrades to WebAssembly in the background

**WASM specifics:** AOT (`<RunAOTCompilation>true</RunAOTCompilation>`) improves runtime perf but *increases* payload. `WasmStripILAfterAOT` strips IL from AOT-compiled methods. IL trimming is on by default for publish builds.

**Strengths:**
- Compile-time sequence numbers are a genuine architectural win — O(n) diffs instead of React's O(n³→n) heuristic
- `ShouldRender` override is the explicit perf escape hatch (equivalent to `React.memo`)
- `@key` directive stabilizes identity across list reorders (same role as React's `key`)
- `<CascadingValue IsFixed="true">` opts out of re-subscription for values that never change
- Server mode's DOM-diff-over-wire is extremely efficient *when latency is low*
- Render mode per component lets you put interactivity only where needed

**Weaknesses:**
- **WebAssembly initial download remains the headline complaint.** GitHub issue `dotnet/aspnetcore#41909` ("Blazor wasm size and load time is the worst and biggest problem ever") captures the community sentiment. Even trimmed/AOT'd, the floor is hundreds of KB before app code
- Server mode is **latency-sensitive** — every click is a round-trip; users far from the server see tens-to-hundreds of ms perceived lag
- Server mode is **stateful on the server** — circuits consume memory per connected user, eviction on disconnect loses UI state (`[PersistentState]` in .NET 10 helps but is opt-in)
- No concurrent rendering / priority-based scheduling (unlike React 18's Fiber + transitions)
- No compiler-based auto-memoization (unlike React Compiler 1.0)

**Sources:** [Blazor RenderTree Explained (InfoQ)](https://www.infoq.com/articles/blazor-rendertree-explained/), [ASP.NET Core Blazor rendering performance (Microsoft Learn)](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/rendering), [dotnet/aspnetcore#41909](https://github.com/dotnet/aspnetcore/issues/41909)

---

## 5. Layout System

Blazor renders to HTML in all render modes (including Hybrid, which hosts HTML in a WebView). Layout is entirely **CSS-delegated** — Flexbox, Grid, utility frameworks (Tailwind), whatever. Blazor has no framework-level layout primitive.

**Strengths:**
- Full CSS ecosystem — Flexbox, Grid, container queries, anything the browser supports
- Tailwind integrates cleanly
- Responsive design is a CSS concern (media queries, container queries), not a framework concern

**Weaknesses:**
- **No framework-level layout abstraction** — same criticism as React
- No equivalent to WinUI 3's `Grid` or Reactor's `FlexPanel` (because it's just CSS)
- No built-in responsive breakpoint system

**Rating: B+** — Delegating to CSS is fine; the ecosystem is powerful; Blazor contributes nothing on top.

---

## 6. Styling & Theming

**CSS Isolation (scoped CSS):** The one distinctive Blazor styling feature. A `MyComponent.razor` file can have a sibling `MyComponent.razor.css`; the Razor build rewrites selectors with a generated `b-xxxxxxx` attribute that gets added to every element the component renders. This provides per-component CSS scoping without runtime cost.

**Limitation:** CSS isolation's `b-xxx` attribute is only added to elements the component directly renders. **Third-party component libraries don't participate** — they're precompiled with their own (or no) scoping, so `.my-class` selectors in your isolated CSS won't reach MudBlazor/Radzen/Fluent UI Blazor internals. Proposal `dotnet/aspnetcore#63091` tracks a fix, unshipped at time of writing.

**Design system:** Blazor has no built-in look-and-feel beyond raw HTML. The ecosystem fills this:
- **Microsoft Fluent UI Blazor** (`Microsoft.FluentUI.AspNetCore.Components`) — official Microsoft library, wraps FAST web components
- **MudBlazor** — Material-inspired, community favorite, MIT licensed
- **Radzen.Blazor** — free + paid IDE tooling
- **Telerik**, **Syncfusion**, **DevExpress** — commercial, comprehensive

**Theming:** Handled by whichever component library you choose; no framework-level theme abstraction.

**Strengths:**
- CSS isolation without runtime cost is genuinely nice
- Plain CSS + CSS custom properties give native theming
- Multiple strong component libraries exist

**Weaknesses:**
- **No built-in design system** — every project must pick and configure
- **CSS isolation doesn't extend to third-party components** — open since 2020s-era, unfixed
- No compile-time validation of class name references

**Rating: B** — Ecosystem solutions are strong; framework-level story is thin.

---

## 7. Navigation

**`@page` directive:** Components annotate themselves with `@page "/products/{id:int}"` to register routes. Multiple `@page` directives per component are allowed. The `Router` component (in `Components.Routing`) walks the route table and renders the matching component.

**Route constraints:** `bool`, `datetime`, `decimal`, `double`, `float`, `guid`, `int`, `long`, `nonfile` (newer). Regex constraints supported in recent versions.

**`NavigationManager`:** Injected service for programmatic navigation, URI parsing, and `LocationChanged`/`LocationChanging` events.

**Type safety:** Route templates are **string literals with no compile-time type safety**. `NavigateTo("/products/abc")` does not verify against the `{id:int}` constraint at build time. Community libraries (Blazor.StrongRouter and friends) attempt to solve this, but nothing is canonical.

**Strengths:**
- Route constraints cover the common primitive types
- Multi-route per component works cleanly
- `NavigationManager`'s `LocationChanging` event (with cancellation) is good for unsaved-changes guards
- URL-based deep linking is native (it's the web)

**Weaknesses:**
- **No type-safe route navigation** — React's TanStack Router, Compose Nav 3, and Reactor's typed-record routes are all ahead here
- No nested-route composition equivalent to React Router v6+ layouts (though `_Layout.razor` provides some of this)
- Lazy loading of routes is possible but configuration-heavy

**Rating: B** — Functional and mature; weak on type safety compared to modern alternatives.

**Sources:** `src/Components/Components/src/Routing/Router.cs`, [ASP.NET Core Blazor routing (Microsoft Learn)](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/routing)

---

## 8. Animation

Blazor has **no first-class animation system**. Options:
- **CSS transitions and animations** — zero-JS, limited to what CSS can express
- **Blazor.Animate**, **blazor-transition-group**, **Toolbelt.Blazor.ViewTransition** — small community libraries, fragmented
- **JS interop** to larger JS animation libraries (GSAP, Motion) — possible but awkward
- **View Transitions API** (browser-native) — accessible via JS interop

**Strengths:**
- CSS transitions are free and perform well for simple cases
- View Transitions API (shipping in browsers) gives cross-page transitions

**Weaknesses:**
- **No framework-level animation support** — exit animations in particular are awkward because Blazor removes DOM nodes synchronously when state changes
- Ecosystem libraries are small and fragmented — nothing approaching Motion for React, SwiftUI's `.animation()`, or Compose's animation APIs
- `@key` + CSS transitions is the "standard" pattern but requires careful DOM manipulation to work

**Rating: C** — CSS is a floor, not a ceiling; Blazor adds nothing above it.

---

## 9. Accessibility

Blazor's accessibility story is **"inherit whatever HTML you render."** Microsoft Learn's Blazor accessibility guidance is essentially "use semantic HTML + ARIA attributes correctly." The framework provides:
- No accessibility primitives beyond what HTML offers
- No framework-level automation tree (unlike WinUI 3's UIA inheritance)
- No accessibility linting or analyzers shipped with the framework

What you get "for free" depends entirely on the component library:
- **Fluent UI Blazor** — FAST components manage ARIA automatically
- **Telerik** — targets WCAG 2.2 and WAI-ARIA 1.2
- **Syncfusion** — claims WCAG 2.2 / Section 508 / ADA compliance

**Strengths:**
- Web accessibility standards (semantic HTML, ARIA, keyboard nav) are the most mature accessibility model on any platform
- Screen readers, high-contrast modes, zoom, and keyboard nav are browser-platform features
- Quality component libraries provide comprehensive a11y out of the box

**Weaknesses:**
- **Framework contributes nothing on top of HTML** — same limitation as React
- Custom components (comboboxes, date pickers, dialogs) must be built accessibly from scratch
- No static analysis for a11y violations at the framework level (unlike `eslint-plugin-jsx-a11y` for React)
- Accessibility is entirely a library/developer concern

**Rating: B** — Web a11y is solid; Blazor adds nothing.

---

## 10. Input & Gestures

**Input events:** Blazor wraps DOM events with typed `.NET` event handlers (`@onclick`, `@onchange`, `@onkeydown`, etc.). Arguments are typed (`MouseEventArgs`, `KeyboardEventArgs`, `ChangeEventArgs`). Event delegation is to the renderer root. Stop-propagation and prevent-default are declarative: `@onclick:stopPropagation="true"`.

**Form inputs:** First-class `<InputText>`, `<InputNumber>`, `<InputDate>`, `<InputCheckbox>`, `<InputSelect>`, `<InputRadioGroup>`, `<InputTextArea>`, `<InputFile>` wrap native inputs with two-way binding, validation integration, and accessibility markup.

**Gestures:** No built-in gesture system. Touch events (`@ontouchstart`, etc.) are available; drag-drop via HTML5 drag events; anything richer requires JS interop.

**Strengths:**
- Typed event argument types are safer than React's `SyntheticEvent`
- Declarative `stopPropagation`/`preventDefault` is ergonomic
- The `<Input*>` components are genuinely polished — validation, binding, and accessibility integrated
- Form actions via `EditForm` + `[SupplyParameterFromForm]` (§19) make server-rendered forms first-class

**Weaknesses:**
- **No gesture system** — same gap as React
- Multi-touch / pinch / pan require JS interop or third-party libs
- No equivalent to Flutter's gesture arena or SwiftUI's gesture composition

**Rating: B+** — Strong inputs, weak gestures.

---

## 11. Developer Experience

**Tooling:**
- **Visual Studio 2022/2025** — first-class Razor support, IntelliSense, refactoring
- **JetBrains Rider** — also strong; implemented its own WASM debugger in 2023
- **VS Code** — via C# Dev Kit + Blazor extension; less polished than VS
- **Hot Reload** — works across Server, WebAssembly, Hybrid in .NET 9+. Razor edits, C# method bodies, CSS generally reload. Structural changes (adding a `[Parameter]`, removing a component) often require a restart. The `microsoft/vscode-dotnettools` repo has a long-running Hot Reload reliability tracker
- **Browser DevTools** — standard web DevTools work (F12 in the browser)
- **No DevTools-equivalent for Blazor components** — no tree inspector, no parameter inspector, no render-count profiler. This is a notable gap vs React DevTools, Flutter Widget Inspector, Compose Layout Inspector

**Ecosystem libraries:**
- **Fluent UI Blazor** (Microsoft, MIT)
- **MudBlazor** (Material-inspired, MIT, community favorite)
- **Radzen.Blazor** (free + paid IDE)
- **Telerik**, **Syncfusion**, **DevExpress** (commercial)

**Debugging:**
- **Server mode** — standard .NET debugging, works cleanly
- **WebAssembly** — debug proxy bridges browser → .NET; fragile, breakpoints before proxy attaches (e.g., early `Program.cs`) don't always hit
- **Hybrid** — in-process .NET debugging + WebView inspector; better than pure WASM

**Strengths:**
- Mature Roslyn-based tooling (years of investment)
- Hot Reload works well for common edits
- Unified C# end-to-end reduces context switching
- Rider's WASM debugger is good

**Weaknesses:**
- **No component DevTools** — can't inspect the render tree, parameter values, or render counts the way React/Compose developers can
- WASM debugging complexity (debug proxy handshake, timing issues)
- Hot Reload reliability issues on VS Code
- Project setup for Blazor Web App requires understanding four render modes before you can choose confidently

**Rating: B** — Tooling is mature; component DevTools are the big gap.

---

## 12. Platform Reach & Ecosystem

**Platforms:**
- **Web** (primary) — browser via WebAssembly or Server (SignalR)
- **Desktop** — Blazor Hybrid in WPF, WinForms (Windows), MAUI (Windows, Mac); hosts Razor components in a `BlazorWebView` backed by WebView2/WKWebView
- **Mobile** — Blazor Hybrid in .NET MAUI (iOS, Android)
- **Static site generation** — Blazor Web App can produce prerendered-only pages

**Ecosystem scale:** Small compared to React (~34k Blazor customers per Telerik's 2025 numbers; React's ecosystem is orders of magnitude larger). Growing, but Stack Overflow coverage, NuGet package selection, and tutorial availability are all meaningfully behind React/Vue.

**Strengths:**
- **Unified codebase across web + desktop + mobile** via Hybrid — arguably the broadest platform reach of any Microsoft framework
- C# all the way (no JavaScript learning curve for .NET teams)
- Microsoft-backed, first-party

**Weaknesses:**
- **Hybrid rendering is not native** — BlazorWebView hosts HTML in a WebView, so desktop/mobile apps don't get platform-native controls, native accessibility trees, or platform-native look-and-feel
- Smaller ecosystem than React/Vue
- WebAssembly initial-load cost on web
- Platform API access in Hybrid requires JS interop bridges for anything not exposed by MAUI

**Rating: B+** — Platform reach via Hybrid is broad; "not native" is the headline caveat for desktop/mobile.

---

## 13. Testing

**bUnit:** The canonical Blazor component-testing library. Microsoft Learn officially endorses it in the Blazor testing docs. Features:
- Render components in isolation, pass parameters, inject services, fire events
- Assert on rendered markup via semantic HTML comparer (whitespace/attribute-order insensitive)
- Mock `IJSRuntime` with `bUnit.JSInterop`
- xUnit, NUnit, MSTest, TUnit compatible
- Milliseconds per test (vs seconds for Playwright/Selenium)

**E2E:** Playwright, Selenium, or Cypress hit a real browser.

**Strengths:**
- **bUnit is the best testing story of any Microsoft UI framework** — fast, semantic, renderer-level, officially supported
- Semantic HTML comparer produces non-brittle assertions
- Mock infrastructure for JS interop, NavigationManager, auth state is built in

**Weaknesses:**
- No JavaScript execution in bUnit — must mock `IJSRuntime`
- Testing streaming rendering and Server Component async behavior is complex
- No visual/golden testing built in (third-party tools required)
- Hybrid-mode testing (WebView hosting) is an E2E concern, not bUnit's scope

**Rating: A-** — bUnit's renderer-level approach is the right architecture; mature and well-documented.

**Sources:** [bUnit](https://bunit.dev/), [Test Razor components (Microsoft Learn)](https://learn.microsoft.com/en-us/aspnet/core/blazor/test)

---

## 14. Error Handling & Resilience

**Error Boundaries:** Blazor has `<ErrorBoundary>` — a component that catches unhandled exceptions in its descendants and renders `ErrorContent` instead. `Recover()` resets the boundary after the error is resolved. This puts Blazor in a small group with React and Reactor as the only component frameworks with first-class error boundaries.

**Unhandled errors:**
- **Server mode** — circuit errors are logged and shown in a default `<div id="blazor-error-ui">` overlay; circuit disconnects on unhandled errors in some modes
- **WebAssembly** — errors log to browser console; default UI overlay same as Server
- **Hybrid** — .NET exception handling via the host app's global handlers

**Strengths:**
- **`<ErrorBoundary>` is a genuine advantage** — SwiftUI, Compose, Flutter, Avalonia, and WinUI 3 all lack this
- `Recover()` lets boundaries reset after transient failures
- Standard .NET exception handling applies (try/catch, AggregateException, etc.) — no callback-hell patterns

**Weaknesses:**
- Doesn't catch errors in event handlers unless the event handler itself throws synchronously within a render
- `async void` or detached async tasks can still escape boundaries
- Recovery UX patterns (retry, rollback, offline) are app responsibility, not framework-provided

**Rating: B+** — Error boundaries put Blazor ahead of every competitor except React (tied) and Reactor (tied).

---

## 15. Data Loading & Async

**Server-side rendering with streaming:** .NET 8+ introduced **streaming rendering** — a component can mark a `<section>` as `@attribute [StreamRendering(true)]`, and the server sends initial markup immediately and streams in updates as async data resolves. Equivalent to React Server Components + Suspense for the SSR case.

**Interactive modes:** Async data is typically loaded in `OnInitializedAsync` / `OnParametersSetAsync`. The component shows a loading state while the `Task` is pending, then re-renders when it completes (framework calls `StateHasChanged()` automatically).

**`[PersistentState]` (.NET 10):** Data fetched during prerender can be serialized into the page and rehydrated during interactive boot — eliminates double-fetching when transitioning from SSR to Interactive modes.

**No Suspense equivalent in interactive mode.** Server streaming rendering is the closest thing, and only works in the SSR phase.

**Strengths:**
- `OnInitializedAsync` is simple and works reliably
- Streaming rendering is a legitimate answer to declarative loading boundaries in SSR
- `[PersistentState]` closes the prerender → interactive double-fetch gap
- `async`/`await` is native — no callback patterns
- Auto-`StateHasChanged` after lifecycle async completion removes boilerplate

**Weaknesses:**
- **No Suspense equivalent in Interactive Server/WebAssembly** — manual loading-state boolean patterns are the norm
- No declarative "this whole subtree is waiting" boundary like React's `<Suspense>`
- No built-in caching/retry/deduplication (no TanStack Query equivalent)
- Cancellation on component disposal requires manual `CancellationTokenSource` plumbing

**Rating: B+** — Streaming rendering is strong; interactive async is manual; no ecosystem caching layer equivalent to TanStack Query.

---

## 16. Lists & Virtualization

**`<Virtualize>` component:** Blazor ships a built-in `<Virtualize TItem="TItem" ItemsProvider="...">` component that virtualizes large lists by rendering only the visible viewport items. Supports both in-memory collections (`Items`) and async item providers (`ItemsProvider`) for server-side pagination.

**`QuickGrid`:** Microsoft ships an official `QuickGrid` component (in `Microsoft.AspNetCore.Components.QuickGrid`) — a simple high-performance data grid with sorting, paging, column templates, and Entity Framework integration via `IQueryable`. Not as full-featured as commercial grids (Telerik, Syncfusion) but solid.

**Third-party:** MudBlazor, Radzen, Telerik, Syncfusion, DevExpress all ship data grids with features beyond QuickGrid (grouping, editing, filtering, export).

**Strengths:**
- **Built-in virtualization** — Blazor has a built-in answer where React notably does not
- `<Virtualize>` handles both fixed-size and dynamic-size items
- `ItemsProvider` async pattern enables seamless server-side pagination
- `QuickGrid` is official and `IQueryable`-native

**Weaknesses:**
- No built-in grouping (unlike WPF's `ICollectionView`)
- `<Virtualize>` has no horizontal/grid-of-items variant (vertical list only)
- QuickGrid is deliberately simple — serious LOB scenarios still use commercial grids
- No dynamic item height measurement as smooth as react-virtuoso

**Rating: B+** — Built-in virtualization + QuickGrid is a solid foundation; commercial grids fill the high-end.

---

## 17. Internationalization & Localization

**Architecture:** Uses ASP.NET Core's standard `IStringLocalizer<T>` and `.resx` files. `RequestLocalizationMiddleware` sets `CultureInfo.CurrentCulture` / `CurrentUICulture` per request. Locale-based formatting (`DateTime`, `decimal`, `NumberFormatInfo`) is native .NET.

**Blazor-specific:**
- `BlazorWebAssemblyLoadAllGlobalizationData` — option to load all ICU data vs. locale-specific subset (size trade-off)
- `Microsoft.Extensions.Localization` integrates with Razor components (`@inject IStringLocalizer<MyComponent> L`)

**Strengths:**
- Inherits the mature .NET i18n stack (`CultureInfo`, `IStringLocalizer`, resource managers)
- ICU support via WASM globalization bundle
- Runtime culture switching works cleanly in Server mode

**Weaknesses:**
- **No built-in CLDR plural/gender rules** — same gap as WPF/WinUI 3. Requires third-party (e.g., `PluralizationServices` or a custom ICU MessageFormat layer)
- ICU WASM bundle is large (~8 MB uncompressed) if you load all locales
- RTL is a CSS concern (`dir="rtl"` or `direction: rtl`) with no framework abstraction
- Compile-time validation of resource keys is not native (third-party analyzers exist)

**Rating: B+** — Mature .NET i18n inheritance; no framework-level plural/gender; same rating as WinUI 3.

---

## 18. Interop & Incremental Adoption

**Razor Class Libraries (RCLs):** Reusable component packages — the standard sharing mechanism. An RCL built on .NET 8 works across Server, WebAssembly, and Hybrid.

**JS Interop:**
- **`IJSRuntime.InvokeAsync<T>`** — call arbitrary JS from .NET
- **`[JSInvokable]`** — expose .NET methods callable from JS
- **`IJSObjectReference` + ES modules** — isolated per-component JS modules (recommended pattern)

**Hosting Blazor in existing apps:**
- **ASP.NET Core** — `app.MapRazorComponents<App>()` adds Blazor to any ASP.NET Core app
- **WPF/WinForms** — `BlazorWebView` is a control you drop into a `Window`/`Form`; configures DI, root component, host HTML
- **MAUI** — same BlazorWebView pattern, cross-platform
- **Hybrid embedding direction** — Blazor-in-WPF works; WPF-in-Blazor does not (Blazor renders HTML, not XAML)

**Strengths:**
- **BlazorWebView is the closest thing to Reactor in the Microsoft ecosystem** — declarative components hosted inside a native desktop shell
- JS interop is typed and well-documented
- RCLs work across all three render modes
- Same Razor components work in Blazor Web App and Blazor Hybrid — genuinely shared UI code across web and desktop

**Weaknesses:**
- **BlazorWebView is a WebView-hosted renderer** — you don't get native platform controls, native UIA/accessibility tree, or native platform look. It's HTML-in-a-WebView
- In Blazor Server mode, every JS interop call is a network round-trip
- Render mode mismatches between Web App and Hybrid cause feature drift (e.g., MAUI Hybrid rejects per-component `@rendermode`)
- Bidirectional embedding (native → Blazor → native) requires multiple WebView instances or careful state coordination
- Platform API access (file pickers, drag-drop, OS dialogs) requires MAUI APIs or custom JS interop bridges — the #1 real-world Hybrid complaint

**Rating: A-** — BlazorWebView's ability to reuse web components on desktop is genuinely valuable; the "not actually native" caveat is the defining trade-off.

**Sources:** [ASP.NET Core Blazor Hybrid (Microsoft Learn)](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/), [Host a Blazor web app in a .NET MAUI app using BlazorWebView (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/blazorwebview)

---

## 19. Forms & Data Entry

**`<EditForm>`:** Blazor's form component (verified in `src/Components/Web/src/Forms/EditForm.cs`). Given a `Model` parameter, it constructs an `EditContext` that tracks touched/modified/valid state per field, cascades it to descendants via `<CascadingValue IsFixed="true">`, and provides `OnSubmit`, `OnValidSubmit`, and `OnInvalidSubmit` event callbacks. Mutually exclusive: using `OnSubmit` makes the developer responsible for manually calling `EditContext.Validate()`; using `OnValidSubmit`/`OnInvalidSubmit` triggers implicit validation on submit.

**`<DataAnnotationsValidator>`:** A component you drop inside `<EditForm>` that wires standard `System.ComponentModel.DataAnnotations` attributes (`[Required]`, `[StringLength]`, `[Range]`, `[EmailAddress]`, custom `[ValidationAttribute]`) into `EditContext`'s validation pipeline.

**Typed inputs:** `<InputText>`, `<InputNumber<T>>`, `<InputDate<T>>`, `<InputCheckbox>`, `<InputSelect<T>>`, `<InputRadioGroup<T>>`, `<InputTextArea>`, `<InputFile>`. All inherit from `InputBase<TValue>` and provide two-way binding (`@bind-Value`), validation state visualization (`valid`/`invalid` CSS classes), and accessibility markup.

**`<ValidationMessage For="@(() => model.Property)">`:** Renders per-field error messages. Uses an expression tree to statically identify the field.

**`<ValidationSummary>`:** Renders all current errors.

**Server-side form handling (`<EditForm>` + SSR):** Blazor Web App's static SSR mode supports full server-rendered form posting via `[SupplyParameterFromForm]`, antiforgery tokens (`<AntiforgeryToken>`), and POST-based form submission — without JavaScript. `data-enhance` on the form enables streaming SSR enhancement without a full page reload.

**Strengths:**
- **This is Blazor's crown jewel.** The most comprehensive form/validation system of any modern declarative framework
- Data-annotation-driven validation is declarative and reuses validation attributes already common in .NET models
- Expression-tree field identification (`@(() => model.Property)`) is compile-checked against the model
- Typed inputs cover all the common cases with sensible defaults
- Works in both interactive and static SSR modes — forms can be progressive-enhancement-compatible
- Integrates with ASP.NET Core antiforgery

**Weaknesses:**
- `DataAnnotationsValidator` validates only the top-level object by default — nested/collection validation requires `ObjectGraphDataAnnotationsValidator` from an extension package
- No built-in async validation beyond whatever custom `ValidationAttribute` subclasses provide
- Input masking is not built in (no `<InputText Mask="...">`)
- No built-in multi-step / wizard form framework
- WPF's `BindingGroup` (transactional edits) and `ErrorTemplate` (custom error visuals at binding level) remain unmatched

**Rating: A-** — Best declarative-framework form system by a significant margin. Second only to WPF's full depth (transactional edits, custom error templates).

**Sources:** `src/Components/Web/src/Forms/EditForm.cs`, `src/Components/Forms/src/DataAnnotationsValidator.cs`, [ASP.NET Core Blazor forms validation (Microsoft Learn)](https://learn.microsoft.com/en-us/aspnet/core/blazor/forms/validation)

---

## Summary Ratings

| Category | Grade | Notes |
|---|---|---|
| Declarative Syntax | B+ | Razor is mature and type-safe; separate language, verbose vs function components |
| Component Architecture | B | Class-based + `EventCallback`/`RenderFragment` are clean; no hooks |
| State & Reactivity | C+ | `StateHasChanged` is manual; no automatic tracking; no built-in store |
| Rendering & Performance | B | Compile-time sequence numbers are clever; WASM download + Server latency are real |
| Layout | B+ | CSS delegation; same as React |
| Styling & Theming | B | CSS isolation is unique; doesn't cover third-party components; no built-in design system |
| Navigation | B | `@page` routes are functional; no compile-time type safety |
| Animation | C | No framework support; CSS transitions only |
| Accessibility | B | Inherits web a11y; framework contributes nothing |
| Input & Gestures | B+ | Strong typed inputs; no gesture system |
| Developer Experience | B | Mature tooling; no component DevTools |
| Platform Reach | B+ | Web + Desktop + Mobile via Hybrid (non-native) |
| Testing | A- | bUnit is excellent; renderer-level; officially supported |
| Error Handling | B+ | `<ErrorBoundary>` puts Blazor in a small group with React and Reactor |
| Data Loading & Async | B+ | Streaming rendering for SSR; manual for interactive |
| Lists & Virtualization | B+ | `<Virtualize>` built-in; QuickGrid official; no grouping |
| Internationalization | B+ | Inherits .NET i18n stack; no built-in plural/gender |
| Interop & Adoption | A- | BlazorWebView hosts in WPF/WinForms/MAUI; not native |
| Forms & Data Entry | A- | Crown jewel; best declarative form system besides WPF's |

---

## Takeaways for Reactor

**Blazor is Reactor's closest philosophical cousin inside Microsoft.** Both are C#-first declarative component frameworks. But they make opposite bets:

| | Blazor Hybrid | Reactor |
|---|---|---|
| **Renderer** | HTML in a WebView | Native WinUI 3 controls |
| **Controls** | HTML elements | Native UIElement tree |
| **Accessibility** | Manual ARIA | UIA (automatic from WinUI) |
| **Look & feel** | Browser-chrome HTML | Native Windows + Fluent Design |
| **Animation** | CSS transitions | Composition API |
| **Platform reach** | Web + Desktop + Mobile | Windows only |

### What Reactor should steal

1. **`<EditForm>` + `<DataAnnotationsValidator>` + typed inputs.** Highest-leverage feature. Reactor's validation system (B grade) could close more ground toward WPF (A) by adopting a Blazor-style wrapper around data-annotation-driven validation with typed `InputText`/`InputNumber` components.
2. **`EventCallback<T>` semantics.** An event type that auto-re-renders the subscriber on the correct sync context. Small primitive, large ergonomic win.
3. **Compile-time sequence numbers for O(n) diffing.** If Reactor ever adds a compiled DSL or source generator over its method-call syntax, bake in the sequence-number invariant.
4. **bUnit-style renderer-level testing.** Reactor's testing gap (no component-level framework) is what bUnit solves for Blazor. Same architecture should work — a mock visual tree, direct assertion on the element graph.
5. **Built-in `<Virtualize>` + `QuickGrid`.** Reactor already has a DataGrid; a lighter-weight `QuickGrid` equivalent for simple sortable/pageable scenarios would broaden adoption.

### What Reactor should keep avoiding

1. **Manual `StateHasChanged()`.** Hooks with auto-tracking (`UseState`, `UseObservable`) should stay the default. Don't let anyone "simplify" by exposing a manual render trigger.
2. **String-literal routes with no compile-time type safety.** Reactor's typed-record routes are a lead worth protecting.
3. **CSS-isolation-style scoping that breaks for third-party components.** Whatever theming abstraction grows, test it against library-author scenarios day 1.
4. **Per-host feature incompatibilities** (MAUI Hybrid rejects `@rendermode`). Component code should be invariant under hosting choice.

### Where Reactor structurally wins

- **Native controls.** BlazorWebView's "not actually native" is the defining caveat. Reactor renders real WinUI 3 controls with real UIA, real Composition, real Fluent Design
- **No WebAssembly payload.** Reactor inherits WinUI 3's native startup
- **In-process only.** No circuit latency, no debug proxy, no JS interop marshaling
- **Compositor animations.** Reactor's 5 compositor properties are limiting, but they're genuinely GPU-accelerated, independent-thread animations. Blazor has CSS transitions

### Where Blazor structurally wins

- **Platform reach.** One codebase → Web + Windows + Mac + iOS + Android. No Microsoft framework matches this (Avalonia is the closest non-MS option)
- **Forms.** `<EditForm>` is the single best form/validation story in any declarative framework today
- **Error boundaries.** Shared with React and Reactor — a small club
- **Testing.** bUnit's renderer-level approach is the testing gold standard Reactor should aspire to
- **Ecosystem of component libraries.** MudBlazor, Fluent UI Blazor, Telerik, Syncfusion, DevExpress all ship production-ready component sets — Reactor's ecosystem is effectively just Reactor itself

---

## Sources

### Microsoft Learn
- [Blazor Overview](https://learn.microsoft.com/en-us/aspnet/core/blazor/)
- [Blazor render modes](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes)
- [Blazor Hybrid](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/)
- [BlazorWebView in .NET MAUI](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/blazorwebview)
- [Blazor forms validation](https://learn.microsoft.com/en-us/aspnet/core/blazor/forms/validation)
- [Blazor routing](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/routing)
- [Blazor CSS isolation](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/css-isolation)
- [Blazor rendering performance](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/rendering)
- [Blazor app download size](https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/app-download-size)
- [WebAssembly build tools and AOT](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot)
- [Test Razor components](https://learn.microsoft.com/en-us/aspnet/core/blazor/test)
- [Cascading values and parameters](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/cascading-values-and-parameters)
- [PersistentState (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/blazor/state-management/prerendered-state-persistence)

### ASP.NET Core source (`~/code/aspnetcore`)
- `src/Components/Components/src/ComponentBase.cs` — `StateHasChanged`, `HandleEventAsync`
- `src/Components/Components/src/EventCallback.cs` — event-callback semantics
- `src/Components/Components/src/Routing/Router.cs` — route matching
- `src/Components/Web/src/Forms/EditForm.cs` — form + EditContext wiring
- `src/Components/Forms/src/DataAnnotationsValidator.cs` — validation integration
- `src/Components/WebView/WebView/src/WebViewManager.cs` — Hybrid host runtime

### Industry context
- [.NET 10 Has Arrived — What's Changed for Blazor (Telerik, Nov 2025)](https://www.telerik.com/blogs/net-10-has-arrived-heres-whats-changed-blazor)
- [Blazor in .NET 10: What's New and Why It Finally Feels Complete (dev.to)](https://dev.to/mashrulhaque/blazor-in-net-10-the-features-that-actually-matter-nc1)
- [ASP.NET Core in .NET 10 (InfoQ, Dec 2025)](https://www.infoq.com/news/2025/12/asp-net-core-10-release/)
- [Blazor RenderTree Explained (InfoQ)](https://www.infoq.com/articles/blazor-rendertree-explained/)
- [Is Blazor Production-Ready in 2025? (Code With Kazik)](https://codewithkazik.com/is-blazor-production-ready-in-2025-lets-find-out)
- [dotnet/aspnetcore#41909 — WASM size is the worst problem](https://github.com/dotnet/aspnetcore/issues/41909)
- [dotnet/aspnetcore#63091 — CSS isolation for third-party components](https://github.com/dotnet/aspnetcore/issues/63091)
- [dotnet/maui#32515 — WPF BlazorWebView broken in .NET 10](https://github.com/dotnet/maui/issues/32515)
- [bUnit — a testing library for Blazor components](https://bunit.dev/)
- [Fluent UI Blazor](https://github.com/microsoft/fluentui-blazor)
- [MudBlazor](https://mudblazor.com/)
