# Reactor Navigation System — Implementation Tasks

Reference: [docs/specs/011-navigation-design.md](../011-navigation-design.md)

### Test classification

Tests are classified by the infrastructure they require:

| Level | Project | What it needs | Speed | When to use |
|---|---|---|---|---|
| **Unit** | `Reactor.Tests` (xUnit) | `NavigationStack`, `NavigationHandle`, `DeepLinkMap`, record equality, pure logic. No reconciler, no WinUI control tree. | Fast | Stack operations, guard logic, serialization, route matching |
| **Self-host** | `Reactor.Tests` (xUnit) | Reconciler + WinUI controls instantiated in-process. No visible window, no event loop. | Medium | `NavigationHost` mount/update/unmount, content switching, lifecycle hooks, context propagation, cache eviction |
| **E2E Appium** | `Reactor.AppTests` (MSTest + Appium) | Full app launched, WinAppDriver out-of-process automation. | **Slow** | Navigation transitions visible to user, back button UIA, NavigationView selection sync, system back button integration |

---

## Phase 1: NavigationStack & NavigationHandle (Pure Logic)

Scope: New files in `Reactor/Core/Navigation/`. No reconciler changes. Pure C# data structures and logic.

### 1.1 NavigationStack\<TRoute\> internal class
- [x] Create `Reactor/Core/Navigation/NavigationStack.cs`
- [x] Private fields: `List<TRoute> _backStack`, `TRoute _current`, `List<TRoute> _forwardStack`
- [x] Constructor: `NavigationStack(TRoute initial)` — sets `_current`, empty back/forward
- [x] `TRoute Current` property
- [x] `bool CanGoBack` → `_backStack.Count > 0`
- [x] `bool CanGoForward` → `_forwardStack.Count > 0`
- [x] `IReadOnlyList<TRoute> BackStack` readonly view
- [x] `IReadOnlyList<TRoute> ForwardStack` readonly view
- [x] `int Depth` → `_backStack.Count + 1`

### 1.2 NavigationStack mutation operations
- [x] `Push(TRoute route)` — push `_current` to back stack, set new current, clear forward stack
- [x] `bool Pop()` — if `CanGoBack`: push current to forward, pop back to current, return true; else false
- [x] `bool Forward()` — if `CanGoForward`: push current to back, pop forward to current, return true; else false
- [x] `Replace(TRoute route)` — replace `_current` without touching back/forward stacks
- [x] `Reset(TRoute route)` — clear back + forward, set current
- [x] `bool PopTo(Func<TRoute, bool> predicate)` — pop entries until predicate matches, collecting into forward stack; return false if no match
- [x] `Action? OnChanged` callback — invoked after every mutation (for triggering re-renders)

### 1.3 Navigation guards on NavigationStack
- [x] `Func<NavigatingFromContext, bool>? Guard` property — called before any mutation that changes current
- [x] `NavigatingFromContext` class: `Route`, `TargetRoute`, `Mode`, `IsCancelled`, `Cancel()` method
- [x] Guard integration in `Push`, `Pop`, `Forward`, `Replace`, `Reset`, `PopTo` — if guard cancels, mutation is no-op and returns false
- [x] `NavigationMode` enum: `Push`, `Pop`, `Replace`, `Reset`, `Forward`

### 1.4 NavigationHandle\<TRoute\> public API
- [x] Create `Reactor/Core/Navigation/NavigationHandle.cs`
- [x] Wraps `NavigationStack<TRoute>` with public readonly + action API
- [x] Properties: `CurrentRoute`, `CanGoBack`, `CanGoForward`, `BackStack`, `ForwardStack`, `Depth`
- [x] Methods: `Navigate(TRoute, NavigateOptions?)`, `GoBack()`, `GoForward()`, `Replace(TRoute)`, `Reset(TRoute)`, `PopTo(Func<TRoute, bool>)`
- [x] `NavigateOptions` record: `Transition` (nullable), `PushToBackStack` (default true)
- [x] `event Action<NavigationEventArgs<TRoute>>? Navigated` — fires after every successful navigation
- [x] `NavigationEventArgs<TRoute>` record: `Route`, `PreviousRoute`, `Mode`
- [x] When `PushToBackStack` is false, call `Replace` instead of `Push` internally

### 1.5 NavigationTransition type hierarchy (stubs)
- [x] Create `Reactor/Core/Navigation/NavigationTransition.cs`
- [x] Abstract record `NavigationTransition` with static factory methods (stubs — animation logic deferred to Phase 3)
- [x] `static readonly NavigationTransition Default` → `new SlideTransition()`
- [x] `static readonly NavigationTransition None` → `new SuppressTransition()`
- [x] `static Slide(SlideDirection, TimeSpan?, CompositionEasingFunction?)`
- [x] `static Fade(TimeSpan?)`
- [x] `static DrillIn(TimeSpan?)`
- [x] `static Connected(string animationKey)`
- [x] `static Spring(float dampingRatio, float period, SlideDirection)`
- [x] `SlideDirection` enum: `FromRight`, `FromLeft`, `FromBottom`, `FromTop`
- [x] `NavigationCacheMode` enum: `Disabled`, `Enabled`, `Required`
- [x] Concrete record types: `SlideTransition`, `FadeTransition`, `DrillInTransition`, `ConnectedTransition`, `SpringSlideTransition`, `SuppressTransition`

### 1.6 Phase 1 tests — NavigationStack (unit)

All Phase 1 tests are **unit tests** (`Reactor.Tests`). No reconciler, no WinUI controls, no UI thread.

**Stack operations:**
- [x] Test: Push adds to back stack and sets new current
- [x] Test: Push clears forward stack
- [x] Test: Pop returns to previous route and pushes current to forward stack
- [x] Test: Pop returns false when back stack is empty
- [x] Test: Forward navigates to forward stack entry and pushes current to back stack
- [x] Test: Forward returns false when forward stack is empty
- [x] Test: Replace changes current without modifying back or forward stacks
- [x] Test: Reset clears all stacks and sets new root
- [x] Test: PopTo pops until predicate matches, returns true
- [x] Test: PopTo returns false when no match in back stack
- [x] Test: Depth returns backStack.Count + 1

**Guard tests:**
- [x] Test: Guard that cancels prevents Push and returns false
- [x] Test: Guard that cancels prevents Pop and returns false
- [x] Test: Guard receives correct `NavigatingFromContext` (route, target, mode)
- [x] Test: Guard not invoked when guard is null
- [x] Test: Guard cancellation leaves stack state unchanged

**NavigationHandle tests:**
- [x] Test: Navigate fires Navigated event with correct args
- [x] Test: GoBack fires Navigated event with Mode = Pop
- [x] Test: GoForward fires Navigated event with Mode = Forward
- [x] Test: Replace fires Navigated event with Mode = Replace
- [x] Test: Reset fires Navigated event with Mode = Reset
- [x] Test: Navigate with `PushToBackStack = false` calls Replace internally
- [x] Test: NavigationHandle exposes correct readonly state after operations

---

## Phase 2: UseNavigation Hook & NavigationHost Element

Scope: Wire navigation into Microsoft.UI.Reactor's hook system, element tree, and reconciler. Depends on Phase 1.

### 2.1 NavigationContext for Context
- [x] Create `NavigationContext<TRoute>` as a `Context<NavigationHandle<TRoute>>` instance
- [x] Ensure type-erased storage works with Reactor's existing context system
- [x] Consider static cache of `Context` instances per TRoute type (avoid allocating per render)

### 2.2 UseNavigation\<TRoute\> hook — root mode
- [x] Add `UseNavigation<TRoute>(TRoute initial)` to `RenderContext.cs`
- [x] Root mode (initial provided): allocate `NavigationStack<TRoute>` via `UseRef`, wrap in `NavigationHandle<TRoute>`
- [x] Publish handle via `Context<NavigationHandle<TRoute>>` using `.Provide()` on the subtree
- [x] Wire `NavigationStack.OnChanged` to `_requestRerender` so navigation mutations trigger re-render
- [x] Add convenience method on `Component`: `UseNavigation<TRoute>(TRoute initial)` delegating to `RenderContext`

### 2.3 UseNavigation\<TRoute\> hook — child mode
- [x] Child mode (no initial): call `UseContext(NavigationContext<TRoute>)` to retrieve ancestor's handle
- [x] Throw descriptive error if no ancestor provides `NavigationContext<TRoute>`
- [x] Add convenience method on `Component`: `UseNavigation<TRoute>()` (parameterless overload)

### 2.4 NavigationHostElement record
- [x] Add `NavigationHostElement` record to `Reactor/Core/Element.cs` (or new file in Navigation/)
- [x] Fields: `object NavigationHandle` (type-erased), `Func<object, Element> RouteMap`
- [x] Properties: `NavigationTransition Transition` (default: `NavigationTransition.Default`), `NavigationCacheMode CacheMode` (default: `Disabled`), `int CacheSize` (default: 10)

### 2.5 NavigationHost DSL factory
- [x] Add `NavigationHost<TRoute>(NavigationHandle<TRoute> nav, Func<TRoute, Element> routeMap)` to `Dsl.cs`
- [x] Type-erase `routeMap` to `Func<object, Element>` for element tree storage
- [x] Return `NavigationHostElement` with `with { }` initializer support for Transition, CacheMode, CacheSize

### 2.6 Reconciler: MountNavigationHost
- [x] Add `MountNavigationHost(NavigationHostElement element, ...)` to `Reconciler.Mount.cs`
- [x] Create a `Grid` as host container (supports overlapping children for future transitions)
- [x] Call `routeMap(nav.CurrentRoute)` to get initial element
- [x] Mount the child element via standard reconciler path (recursive mount)
- [x] Subscribe to `nav.Navigated` event to handle future navigations
- [x] Store subscription handle and current child reference in control's `Tag` or reconciler metadata
- [x] On navigation event: resolve new element via `routeMap`, unmount old child, mount new child (instant swap for Phase 2 — no transitions yet)

### 2.7 Reconciler: UpdateNavigationHost
- [x] Add `UpdateNavigationHost(NavigationHostElement oldElement, NavigationHostElement newElement, ...)` to `Reconciler.Update.cs`
- [x] If route unchanged: reconcile the existing child element (standard update path)
- [x] If route changed: unmount old child, mount new child via `routeMap`
- [x] Update NavigationHostElement properties (transition, cache mode) if changed
- [x] Handle NavigationHost unmount: unsubscribe from Navigated event, unmount child

### 2.8 Phase 2 tests — UseNavigation hook (unit + self-host)

**UseNavigation hook (unit):**
- [x] Test: Root mode creates NavigationStack with initial route
- [x] Test: Root mode returns stable handle across re-renders
- [x] Test: Navigation mutation triggers re-render
- [x] Test: Child mode retrieves ancestor's handle via context
- [x] Test: Child mode throws when no ancestor provides context

**NavigationHost rendering (self-host):**
- [x] Test: NavigationHost mounts initial route's component
- [x] Test: Navigate swaps content to new route's component
- [x] Test: GoBack restores previous route's component
- [x] Test: Replace swaps content without adding to back stack
- [x] Test: Reset clears all and shows new root component
- [x] Test: Unmounting NavigationHost cleans up subscription

**Nested navigation (self-host):**
- [x] Test: Two independent UseNavigation stacks in sibling components don't interfere
- [x] Test: Child component can navigate parent's stack via UseNavigation\<T\>()
- [x] Test: Nested NavigationHost inside outer NavigationHost works independently

---

## Phase 3: Navigation Lifecycle Hooks

Scope: `UseNavigationLifecycle` hook for components to respond to navigation events. Depends on Phase 2.

### 3.1 Lifecycle context types
- [x] Create `Reactor/Core/Navigation/NavigationLifecycle.cs`
- [x] `NavigatedToContext` class: `Route`, `PreviousRoute`, `Mode`
- [x] `NavigatingFromContext` class: `Route`, `TargetRoute`, `Mode`, `IsCancelled`, `Cancel()` (already existed in NavigationStack.cs from Phase 1)
- [x] `NavigatedFromContext` class: `Route`, `TargetRoute`, `Mode`

### 3.2 UseNavigationLifecycle hook
- [x] Add `UseNavigationLifecycle(onNavigatedTo?, onNavigatingFrom?, onNavigatedFrom?)` to `RenderContext.cs`
- [x] Create `NavigationLifecycleHookState : HookState` to store callbacks
- [x] Store callbacks in hook slot, update on re-render (always use latest callback)
- [x] Add convenience method on `Component`

### 3.3 NavigationHost lifecycle integration
- [x] Before content swap: collect `onNavigatingFrom` callbacks from current page's component tree
- [x] Invoke `onNavigatingFrom` — if any callback cancels, abort navigation
- [x] After content swap and mount: invoke `onNavigatedTo` on new page's component tree
- [x] After content swap: invoke `onNavigatedFrom` on old page's component tree
- [x] Ensure lifecycle hooks fire in correct order per spec: navigatingFrom → mount new → navigatedTo → navigatedFrom

### 3.4 Phase 3 tests — Lifecycle hooks (unit + self-host)

**Lifecycle ordering (self-host):**
- [x] Test: `onNavigatedTo` fires after page becomes active
- [x] Test: `onNavigatingFrom` fires before navigating away
- [x] Test: `onNavigatingFrom` cancellation prevents navigation
- [x] Test: `onNavigatedFrom` fires after page is no longer active
- [x] Test: Full lifecycle sequence on Navigate: navigatingFrom → navigatedTo → navigatedFrom
- [x] Test: Full lifecycle sequence on GoBack: navigatingFrom → navigatedTo → navigatedFrom
- [x] Test: Multiple components with lifecycle hooks — all are invoked
- [x] Test: Lifecycle hooks receive correct context (route, previousRoute, mode)

---

## Phase 4: Composition-Layer Transitions

Scope: GPU-accelerated transition animations between pages. Depends on Phase 2 (NavigationHost rendering).

### 4.1 TransitionEngine
- [x] Create `Reactor/Core/Navigation/TransitionEngine.cs`
- [x] `RunTransition(Visual outgoing, Visual incoming, NavigationTransition transition, NavigationMode mode)` method
- [x] Create `CompositionScopedBatch` for coordinating exit + enter animations
- [x] `batch.Completed` callback to finalize: remove outgoing, set incoming to full opacity
- [x] Handle `SuppressTransition` — instant swap, no animation

### 4.2 Slide transition
- [x] `SlideTransition` implementation: animate `Offset` and `Opacity` on both visuals
- [x] Slide in from `SlideDirection` for incoming, slide out opposite for outgoing
- [x] Default duration: 250ms with `CubicBezier(0.1, 0.9, 0.2, 1)` easing
- [x] Automatic reverse: `GoBack` reverses slide direction

### 4.3 Fade transition
- [x] `FadeTransition` implementation: crossfade `Opacity` on both visuals
- [x] Default duration: 200ms
- [x] Same animation in both directions (no reverse needed)

### 4.4 DrillIn transition
- [x] `DrillInTransition` implementation: scale + fade from center
- [x] Incoming: scale 0.85 → 1.0 with fade in; outgoing: fade out
- [x] Reverse (GoBack): outgoing scales 1.0 → 0.85 with fade out
- [x] Default duration: 300ms

### 4.5 Spring transition
- [x] `SpringSlideTransition` implementation: spring-based offset animation
- [x] Use `Compositor.CreateSpringScalarAnimation()` for natural physics feel
- [x] Configurable `dampingRatio` and `period` from transition record

### 4.6 Connected animation (stub for Phase 6)
- [x] `ConnectedTransition` — stub implementation that falls back to `SlideTransition`
- [x] Log warning that connected animations are not yet implemented
- [x] Full implementation deferred to Phase 6

### 4.7 Wire transitions into NavigationHost
- [x] Update `MountNavigationHost` content swap to use `TransitionEngine`
- [x] Mount new content at `Opacity = 0` in Grid alongside old content
- [x] Get `Visual` for both via `ElementCompositionPreview.GetElementVisual()`
- [x] Call `TransitionEngine.RunTransition()`
- [x] On batch complete: remove old content from Grid (or cache), set new content to full opacity
- [x] Respect per-navigation `NavigateOptions.Transition` override
- [x] Respect per-host `NavigationHostElement.Transition` default

### 4.8 Phase 4 tests — Transitions (self-host + E2E)

**Transition engine (self-host):**
- [x] Test: `SuppressTransition` swaps content instantly (no animation started)
- [x] Test: `SlideTransition` creates animations on both visuals
- [x] Test: `FadeTransition` creates opacity animations on both visuals
- [x] Test: Transition batch completes and old content is removed from Grid
- [x] Test: Per-navigation transition override takes precedence over host default

**Visual transition verification (E2E Appium):**
- [ ] Test: Navigate forward shows slide-in animation (visual verification via screenshot comparison or timing)
- [ ] Test: GoBack shows reverse slide animation
- [ ] Test: Transition.None shows instant swap with no delay

---

## Phase 5: Page Caching

Scope: LRU cache for component instances across navigation. Depends on Phase 2 (NavigationHost).

### 5.1 NavigationCache internal class
- [x] Create `Reactor/Core/Navigation/NavigationCache.cs`
- [x] `CachedPage` struct: `UIElement MountedControl`, `Element LastElement`, `DateTime LastAccessed`, `CacheMode`
- [x] `Dictionary<object, CachedPage> _cache` keyed by route (structural equality)
- [x] `int MaxSize` from `NavigationHostElement.CacheSize`
- [x] `TryGet(object route, out CachedPage page)` — returns true if cached, updates `LastAccessed`
- [x] `Add(object route, CachedPage page)` — add to cache, evict LRU if over `MaxSize`
- [x] `Evict()` — remove least recently accessed entry, run cleanup effects and unmount
- [x] `Clear()` — evict all entries (for NavigationHost unmount)

### 5.2 Cache integration in NavigationHost
- [x] On navigate-away with `CacheMode.Enabled` or `Required`: detach control from Grid, store in cache instead of unmounting
- [x] On navigate-to with cache hit: re-attach control to Grid from cache, skip mount
- [x] On navigate-to with cache miss: mount new element (standard path)
- [x] `Required` cache mode: never evict (override LRU)
- [x] On cache eviction: run cleanup effects, unmount control tree via reconciler
- [x] `CacheMode.Disabled`: always unmount on navigate-away (current behavior)

### 5.3 Phase 5 tests — Caching (self-host)

- [x] Test: `CacheMode.Disabled` — component unmounts on navigate-away, remounts on navigate-back
- [x] Test: `CacheMode.Enabled` — component preserved in cache, restored on navigate-back
- [x] Test: Cache hit preserves hook state (UseState value survives round-trip)
- [x] Test: Cache miss after eviction re-mounts fresh component
- [x] Test: LRU eviction removes least recently accessed entry when cache is full
- [x] Test: `CacheMode.Required` pages are never evicted
- [x] Test: Cache cleanup on NavigationHost unmount disposes all cached entries
- [x] Test: Lifecycle hook `onNavigatedTo` fires on cache restore

---

## Phase 6: State Serialization & Deep Linking

Scope: Persist and restore navigation state; map URIs to routes. Depends on Phase 1.

### 6.1 State serialization on NavigationHandle
- [x] Implement `GetState()` on `NavigationHandle<TRoute>` — serialize back stack + current + forward stack to JSON
- [x] Use `System.Text.Json` with polymorphic serialization (`[JsonPolymorphic]`, `[JsonDerivedType]`)
- [x] Serialization format: `{ "backStack": [...], "current": {...}, "forwardStack": [...] }`
- [x] Implement `SetState(string json)` — deserialize and replace entire stack
- [x] `SetState` fires `Navigated` event with `Mode = Reset`

### 6.2 DeepLinkMap\<TRoute\>
- [x] Create `Reactor/Core/Navigation/DeepLinkMap.cs`
- [x] `Map(string pattern, Func<RouteArgs, TRoute> factory)` — register URI pattern to route constructor
- [x] URI pattern syntax: `/segment/{param:type}` with type constraints (`int`, `string`)
- [x] `RouteArgs` class with `Get<T>(string name)` for typed parameter access
- [x] `Resolve(Uri uri)` → `(TRoute[] routes, bool matched)` — returns matched routes
- [x] `WithBackStack(Func<TRoute[]> backStackFactory)` — synthetic back stack for deep-linked routes

### 6.3 UseSystemBackButton convenience hook
- [x] Implement `UseSystemBackButton<TRoute>(NavigationHandle<TRoute> nav)` in `RenderContext.cs` / `Component.cs`
- [x] Subscribe to window root element KeyDown for `VirtualKey.GoBack` / Alt+Left via `UseEffect`
- [x] Call `nav.GoBack()` and mark event handled
- [x] Cleanup: unsubscribe on unmount

### 6.4 Phase 6 tests — Serialization & deep linking (unit)

**Serialization (unit):**
- [x] Test: `GetState` produces correct JSON for simple route
- [x] Test: `GetState` round-trips through `SetState` with matching stack state
- [x] Test: `SetState` with polymorphic route hierarchy deserializes correctly
- [x] Test: `SetState` fires Navigated event with Mode = Reset
- [x] Test: `GetState` includes back and forward stacks

**Deep linking (unit):**
- [x] Test: `DeepLinkMap.Resolve` matches simple pattern `/settings`
- [x] Test: `DeepLinkMap.Resolve` extracts int parameter from `/detail/{id:int}`
- [x] Test: `DeepLinkMap.Resolve` extracts string parameter from `/profile/{userId}`
- [x] Test: `DeepLinkMap.Resolve` returns `matched = false` for unknown URI
- [x] Test: `WithBackStack` produces synthetic back stack on resolve

---

## Phase 7: NavigationView Integration & Polish

Scope: Seamless integration with existing `NavigationViewElement`. Depends on Phases 2-4.

### 7.1 NavigationView auto-sync helper
- [x] Create helper or extension that auto-syncs `NavigationView.SelectedTag` with `nav.CurrentRoute`
- [x] Accept a `Func<TRoute, string?>` mapping from route to tag
- [x] Auto-wire `OnSelectionChanged` to navigate, `OnBackRequested` to `nav.GoBack()`
- [x] Auto-set `IsBackEnabled = nav.CanGoBack`

### 7.2 TitleBar back button integration
- [x] Ensure `TitleBar.IsBackButtonVisible` / `IsBackButtonEnabled` works with `nav.CanGoBack`
- [x] Wire `OnBackRequested` to `nav.GoBack()` in sample code

### 7.3 Phase 7 tests — Integration (self-host + E2E)

**NavigationView sync (self-host):**
- [x] Test: NavigationView.SelectedTag updates when route changes
- [x] Test: NavigationView.OnSelectionChanged triggers navigation
- [x] Test: NavigationView.IsBackEnabled reflects nav.CanGoBack
- [x] Test: NavigationView.OnBackRequested calls nav.GoBack()

**Full app flow (E2E Appium):**
- [ ] Test: App launches with NavigationView, selecting items navigates content
- [ ] Test: Back button in NavigationView returns to previous page
- [ ] Test: Deep navigation stack — navigate 3+ levels, back button works through entire stack
- [ ] Test: Tab-based navigation — each tab maintains independent back stack

---

## Phase 8: Navigation Sample App

Scope: Standalone sample demonstrating all navigation features. Also update existing samples.

### 8.1 New navigation sample: `samples/NavigationDemo`
- [x] Create `samples/NavigationDemo/` project (WinUI packaged app, no XAML beyond App.xaml)
- [x] Route types: `abstract record AppRoute; record Home : AppRoute; record Detail(int Id) : AppRoute; record Settings : AppRoute; record Profile(string Name) : AppRoute;`
- [x] `AppShell` component with `NavigationView` + `NavigationHost`
- [x] `HomePage` — list of items, clicking navigates to `Detail(id)`
- [x] `DetailPage` — shows item detail, "Related" button navigates to next item, demonstrates `DrillIn` transition
- [x] `SettingsPage` — demonstrates nested navigation within a page (sub-settings stack)
- [x] `ProfilePage` — demonstrates navigation guard (`onNavigatingFrom` with unsaved changes prompt)
- [x] Demonstrate `UseSystemBackButton` for title bar / keyboard back
- [x] Demonstrate per-navigation transition overrides (drill-in for detail, slide for tabs)
- [x] Demonstrate page caching (`CacheMode.Enabled`) — scroll position preserved on back

### 8.2 Navigation demo features checklist
- [x] Basic forward/back navigation with NavigationView
- [x] Route parameters (Detail page with ID)
- [x] Navigation guards (unsaved changes prompt)
- [x] Lifecycle hooks (load data on navigatedTo)
- [x] Nested navigation (settings sub-pages)
- [x] Page transitions (slide, fade, drill-in)
- [x] Page caching (scroll position preserved)
- [x] Deep linking from launch arguments
- [x] State serialization (restore on relaunch)
- [x] System back button (Alt+Left, title bar)

### 8.3 Update Reactor.TestApp to use navigation
- [x] Add "Navigation" tab to the existing DemoApp tab bar (`samples/Reactor.TestApp/App.cs`)
- [x] Create `NavigationDemo` component demonstrating basic UseNavigation + NavigationHost
- [x] Show a simple 3-page flow: Home → Detail → Settings with back navigation
- [x] Replaces manual `UseState<string>` page switching with proper navigation

### 8.4 Update Outlook sample to use navigation
- [x] Refactor `samples/apps/outlook/App.cs` to use `UseNavigation<OutlookRoute>` instead of `UseState(initialView)`
- [x] Define route types: `record MailRoute; record CalendarRoute;`
- [x] Replace `activeView switch { ... }` with `NavigationHost(nav, route => route switch { ... })`
- [x] Wire `NavigationView.OnSelectionChanged` to `nav.Navigate()`
- [x] Add slide transition between Mail and Calendar views

### 8.5 Update Reactor.TestApp to use NavigationView (optional)
- [x] Consider replacing the manual tab bar in `DemoApp.Render()` with `NavigationView` + `NavigationHost`
- **Decision: Skipped.** The selfhost test fixtures in `tests/Reactor.AppTests.Host` rely on exact button label matching and disabled state to identify tabs. Converting to NavigationView would break those checks for "Counter", "Todo List", etc. The existing tab bar pattern is preserved; the new "Navigation" tab demonstrates the navigation system within the existing shell.

---

## Phase 9: Appium / E2E Tests

Scope: End-to-end tests using the full app + WinAppDriver for user-visible navigation behavior.

### 9.1 Update NavigationFixtures for Appium
- [x] Update `tests/Reactor.AppTests.Host/Fixtures/NavigationFixtures.cs` to use `UseNavigation` + `NavigationHost` instead of manual `UseState` tab switching
- [x] Add fixture for multi-level navigation (Home → List → Detail → Related)
- [x] Add fixture for navigation with guards (cancel navigation)
- [x] Add fixture for NavigationView + NavigationHost integration
- [x] Add SelfTest variants in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/NavigationFixtures.cs`

### 9.2 E2E Appium test cases
- [ ] Test: NavigationView item click navigates to correct page (verify UIA content changes)
- [ ] Test: Back button click returns to previous page
- [ ] Test: Navigation guard blocks navigation (unsaved changes dialog)
- [ ] Test: Multi-level deep navigation — forward through 3 pages, back through all 3
- [ ] Test: NavigationView selection state stays in sync with current route
- [ ] Test: Tab-based navigation preserves per-tab state

### 9.3 Self-host test fixtures
- [x] Self-host fixture: NavigationHost renders initial route
- [x] Self-host fixture: Navigate changes rendered content
- [x] Self-host fixture: GoBack restores previous content
- [x] Self-host fixture: Lifecycle hooks fire in correct order
- [x] Self-host fixture: Nested navigation works independently

---

## Phase 10: Documentation & AI Guidance

Scope: Update documentation and AI skills guidance.

### 10.1 Create skills.md navigation guidance
- [x] Create `docs/skills.md` (or add to existing file if one is created before this phase)
- [x] Section: "Navigation System" — overview of when and how to use `UseNavigation` + `NavigationHost`
- [x] Guidance: route type design patterns (records, hierarchy, when to use abstract base)
- [x] Guidance: choosing transition types (slide for lateral, drill-in for hierarchical, fade for modals)
- [x] Guidance: when to enable page caching (scroll-heavy pages, form pages with state)
- [x] Guidance: navigation guard patterns (unsaved changes, auth redirect)
- [x] Guidance: NavigationView integration pattern (the 4 wiring points: SelectedTag, IsBackEnabled, OnBackRequested, OnSelectionChanged)
- [x] Guidance: nested navigation patterns (tabs, settings sub-pages, list-detail)
- [x] Anti-patterns: don't use `UseState<string>` for page switching (use navigation instead)
- [x] Anti-patterns: don't create route types with mutable fields
- [x] Anti-patterns: don't call `nav.Navigate()` during render (use effects or event handlers)

### 10.2 Update existing documentation
- [x] Update `docs/specs/011-navigation-design.md` status from "Not started" to reflect progress
- [x] Add migration guide: how to convert `UseState` page-switching to `UseNavigation`
- [x] Document the `NavigationHost` DSL signature and all `with { }` options

### 10.3 Regression testing
- [x] Run full `Reactor.Tests` suite — all existing tests pass (1 pre-existing failure in PersistedStateTests unrelated to navigation)
- [ ] Run full `Reactor.AppTests` E2E suite — no regressions (requires WinAppDriver)
- [x] Run all sample apps — verify they build and compile correctly
- [ ] Performance check: navigation sample with 10+ pages, rapid forward/back doesn't leak memory

---

## Dependency graph

```
Phase 1 (NavigationStack + Handle)
  ├── Phase 2 (UseNavigation hook + NavigationHost)
  │     ├── Phase 3 (Lifecycle hooks)
  │     ├── Phase 4 (Transitions)
  │     │     └── Phase 7 (NavigationView integration)
  │     ├── Phase 5 (Page caching)
  │     └── Phase 8 (Samples)
  │           └── Phase 9 (E2E tests)
  └── Phase 6 (Serialization + Deep linking)
        └── Phase 8 (Samples — deep link demo)

Phase 10 (Documentation) — after all other phases
```

## Summary

| Phase | Description | Test level | Depends on |
|-------|-------------|------------|------------|
| 1 | NavigationStack & Handle | Unit | — |
| 2 | UseNavigation hook & NavigationHost | Unit + Self-host | 1 |
| 3 | Navigation lifecycle hooks | Self-host | 2 |
| 4 | Composition-layer transitions | Self-host + E2E | 2 |
| 5 | Page caching | Self-host | 2 |
| 6 | Serialization & deep linking | Unit | 1 |
| 7 | NavigationView integration | Self-host + E2E | 2, 4 |
| 8 | Sample apps | — | 2-6 |
| 9 | E2E Appium tests | E2E | 2, 8 |
| 10 | Documentation & AI guidance | — | All |
