# Reactor Navigation — Design Spec

Declarative, type-safe navigation for Reactor: a developer-owned navigation stack
with Composition-layer transitions, built entirely in C# with no XAML dependency.

---

## Status

**Implemented** — Phases 1–9 complete (2026-04-08). Core navigation system,
lifecycle hooks, transitions, caching, serialization, deep linking,
NavigationView integration, and test fixtures are all in place.
Remaining: Phase 10 documentation and E2E Appium test execution.

---

## Problem Statement

The [critical review](../critical-review.md) §7 and the
[gap analysis](002-winui3-gap-analysis.md) identify navigation as the single
largest fundamental gap blocking production readiness:

> "Navigation is the second most critical gap... Frame/Page navigation is
> *architecturally blocked*."

Today, Reactor developers manually switch components via `UseState`:

```csharp
var (currentPage, setCurrentPage) = UseState("home");
return NavigationView(menuItems,
    currentPage switch {
        "home" => Component<HomePage>(),
        "settings" => Component<SettingsPage>(),
        _ => Text("404")
    }
);
```

This loses back stack, transitions, lifecycle hooks, deep linking, parameter
passing, nested navigation, and state serialization. It is the framework's most
critical missing feature.

### Why WinUI Frame is not the answer

WinUI's `Frame.Navigate()` requires:

1. **XAML type metadata** — `MetadataAPI::GetClassInfoByTypeName()` resolves
   types through `IXamlMetadataProvider`. Code-only types crash with a null
   access violation in `ActivationAPI::ActivateInstance()` because
   `GetXamlTypeNoRef()` returns null.

2. **IPage interface** — `PageStackEntry::PrepareContent()` hard-casts content
   to `IPage` via `ctl::query_interface<IPage>()` (Frame_Partial.cpp:642).
   Non-Page content fails.

3. **Parameterless constructors** — `ActivationAPI::ActivateInstance()` calls
   the constructor with zero arguments. Reactor components take props.

4. **No extension points** — Frame has no virtual methods for intercepting
   Navigate(), no content provider abstraction, and
   `OnReferenceTrackerWalk` is marked `final`.

These are hard constraints in C++ code, not configuration choices. Working
around them requires either XAML adapter files (breaking Reactor's pure-C# model)
or forking WinUI (unsustainable maintenance burden).

---

## Goals

1. **Type-safe navigation** — routes are C# types, not strings. Incorrect routes
   fail at compile time.
2. **Developer-owned navigation state** — the back stack is a data structure the
   developer controls, not an opaque framework object.
3. **Zero XAML dependency** — no `.xaml` files, no `IXamlMetadataProvider`, no
   `IPage` interface.
4. **Composition-layer transitions** — GPU-accelerated slide, fade, drill, and
   connected animations running on the compositor thread.
5. **Nested navigation** — independent stacks for tabs, drawers, and split views.
6. **Navigation lifecycle** — `OnNavigatedTo`, `OnNavigatingFrom` (with
   cancellation), `OnNavigatedFrom` hooks.
7. **Deep linking** — construct any stack state from a URI or activation argument.
8. **State serialization** — persist and restore the full navigation stack across
   app suspension and termination.
9. **Page caching** — configurable LRU cache for component instances across
   navigation.
10. **NavigationView integration** — works naturally with the existing
    `NavigationViewElement` for back button, selection tracking, and pane chrome.

### Non-goals

- Replacing WinUI's Frame for XAML-based apps — existing Frame navigation
  continues to work as-is.
- URL-based routing (web-style) — desktop apps don't have a browser address bar.
  Deep linking maps activation URIs to routes, but routes are not URLs.
- Server-side rendering or data loaders — desktop apps don't have the
  request/response cycle that makes React Router's loader pattern valuable.
- Automatic navigation from XAML NavigationView.MenuItems — Reactor controls
  navigation imperatively in response to selection events.

---

## Research: How the Competition Does It

Six frameworks were evaluated. The full analysis is in the appendix; here are
the patterns that inform this design:

### Pattern 1: Developer-owned navigation state

**SwiftUI** `NavigationPath` and **Compose Navigation 3** `mutableStateListOf`
both give the developer a bindable/observable data structure that IS the
navigation state. Navigation = mutating a list. This is the modern consensus for
declarative UI frameworks.

```swift
// SwiftUI — path IS the navigation state
@State private var path = NavigationPath()
path.append(Route.detail(id: 42))   // navigate
path.removeLast()                    // go back
```

```kotlin
// Compose Nav3 — back stack IS a mutable list
val backStack = remember { mutableStateListOf<Any>(Home) }
backStack.add(Detail(id = 42))       // navigate
backStack.removeLastOrNull()          // go back
```

**Takeaway:** Reactor's navigation state should be a typed list managed via a hook.

### Pattern 2: Type-safe routes via enums or data classes

SwiftUI uses enums with associated values. Compose Nav3 uses `@Serializable`
data classes. Both provide compile-time safety. MAUI's string-based routing is
universally criticized.

**Takeaway:** Routes should be C# records (or any type implementing a marker
interface). Parameters are fields on the route type.

### Pattern 3: Declarative destination mapping

SwiftUI's `navigationDestination(for:)` and Compose Nav3's `entryProvider` both
use a pattern where route types are mapped to views declaratively. The framework
calls the mapping function when a route is pushed.

**Takeaway:** `NavigationHost` takes a route-to-element mapping function.

### Pattern 4: Serializable navigation state

SwiftUI's `NavigationPath` is `Codable`. React Navigation's state is JSON.
This enables deep linking and state restoration.

**Takeaway:** Routes should be serializable (records with `System.Text.Json`
support). The navigation handle should expose `GetState()` / `SetState()`.

### Pattern 5: Independent stacks for nested navigation

Flutter's `StatefulShellRoute` (separate Navigator per branch) and React
Navigation's "screens stay mounted" both solve tab state preservation through
independent navigation stacks.

**Takeaway:** Each `UseNavigation()` call creates an independent stack. Tabs
each get their own stack, naturally.

### Pattern 6: Navigation guards

React Router's `useBlocker()`, React Navigation's `beforeRemove`, and Flutter's
`PopScope` all provide mechanisms to intercept and cancel navigation (e.g.,
unsaved changes prompts).

**Takeaway:** `OnNavigatingFrom` should support cancellation via a
`NavigatingFromContext` with a `Cancel()` method.

---

## Design

### Architecture overview

```
┌─────────────────────────────────────────────────────────┐
│  Developer code                                         │
│                                                         │
│  var nav = UseNavigation<Route>(new HomeRoute());       │
│  NavigationHost(nav, route => route switch { ... })     │
│                                                         │
├─────────────────────────────────────────────────────────┤
│  Navigation Layer (new code)                            │
│                                                         │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────┐ │
│  │ UseNavigation │  │ Navigation   │  │ Navigation    │ │
│  │ hook          │  │ HostElement  │  │ Transitions   │ │
│  │               │  │              │  │               │ │
│  │ • typed stack │  │ • mounts     │  │ • Composition │ │
│  │ • back/fwd    │  │   current    │  │   animations  │ │
│  │ • guards      │  │   route      │  │ • slide/fade/ │ │
│  │ • lifecycle   │  │ • caches     │  │   drill/zoom  │ │
│  │ • serializes  │  │   pages      │  │ • custom      │ │
│  └──────┬───────┘  └──────┬───────┘  └───────┬───────┘ │
│         │                 │                   │         │
│         │    NavigationContext (Context)   │         │
│         └─────────────────┼───────────────────┘         │
│                           │                             │
├───────────────────────────┼─────────────────────────────┤
│  Existing Reactor layers     │                             │
│                           ▼                             │
│  ┌────────────────────────────────────────────────────┐ │
│  │ Reconciler — mounts NavigationHostElement as a     │ │
│  │ ContentPresenter, swaps content on navigation      │ │
│  └────────────────────────────────────────────────────┘ │
│  ┌────────────────────────────────────────────────────┐ │
│  │ Composition layer — Visual, animations, batches    │ │
│  └────────────────────────────────────────────────────┘ │
│  ┌────────────────────────────────────────────────────┐ │
│  │ WinUI controls — ContentPresenter, Grid, etc.      │ │
│  └────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

No WinUI Frame is used. Navigation state lives in a hook. The
`NavigationHostElement` renders as a WinUI `ContentPresenter` (or `Grid`) and
the reconciler swaps its child when the route changes. Composition-layer
animations run the transition between old and new content.

---

### 1. Route types

Routes are ordinary C# types. Any type works, but records are recommended for
structural equality and serialization:

```csharp
// Simple: records as routes
record HomeRoute;
record DetailRoute(int Id);
record SettingsRoute;
record ProfileRoute(string UserId, string? Tab = null);

// Advanced: route hierarchy for organization
abstract record AppRoute;
record Home : AppRoute;
record Detail(int Id) : AppRoute;
record Settings : AppRoute;
record Profile(string UserId) : AppRoute;
```

**Why records, not enums:** C# enums can't carry associated data. Records
provide structural equality, immutability, `with` expressions, and JSON
serialization out of the box. A `switch` expression on a record hierarchy gives
the same exhaustiveness checking as a sealed type hierarchy.

**No marker interface required.** The generic constraint on `UseNavigation<T>`
is just `where T : notnull`. Any reference or value type works. However, types
used with state serialization must be JSON-serializable.

---

### 2. UseNavigation hook

`UseNavigation<TRoute>` is the primary API. It creates and manages a navigation
stack, and distributes a `NavigationHandle<TRoute>` via `Context` so
descendant components can access it.

#### Creating a navigation stack (root)

```csharp
class AppShell : Component
{
    public override Element Render()
    {
        var nav = UseNavigation<AppRoute>(initial: new Home());
        // nav is a NavigationHandle<AppRoute>
        // Also published to Context so children can UseNavigation<AppRoute>()
        ...
    }
}
```

#### Consuming from a child component

```csharp
class DetailPage : Component<DetailProps>
{
    public override Element Render()
    {
        // Retrieves the nearest ancestor's NavigationHandle<AppRoute>
        var nav = UseNavigation<AppRoute>();
        return VStack(
            Text($"Item {Props.Id}"),
            Button("Related", () => nav.Navigate(new Detail(Props.Id + 1)))
        );
    }
}
```

#### Implementation

```csharp
// In RenderContext:
public NavigationHandle<TRoute> UseNavigation<TRoute>(TRoute? initial = default)
    where TRoute : notnull
{
    // Hook slot stores the NavigationStack<TRoute>
    // If initial is provided → create new stack (root mode)
    // If initial is null → consume from context (child mode)
    ...
}
```

Root mode:
1. Allocates a `NavigationStack<TRoute>` in a `UseRef` hook slot (persists
   across renders).
2. Wraps it in a `NavigationHandle<TRoute>` (readonly view + methods).
3. Publishes via `Context<NavigationHandle<TRoute>>` to the subtree.
4. Calls `_requestRerender` when the stack changes.

Child mode:
1. Calls `UseContext(NavigationContext<TRoute>)` to retrieve the ancestor's
   handle.
2. Returns the same `NavigationHandle<TRoute>`.

---

### 3. NavigationHandle API

```csharp
/// <summary>
/// Readonly handle for reading and manipulating a navigation stack.
/// Provided via Context to all descendants of the component that
/// called UseNavigation with an initial route.
/// </summary>
public sealed class NavigationHandle<TRoute> where TRoute : notnull
{
    // ── State queries ───────────────────────────────────────────────

    /// <summary>Current (topmost) route on the stack.</summary>
    public TRoute CurrentRoute { get; }

    /// <summary>True if there is at least one entry in the back stack.</summary>
    public bool CanGoBack { get; }

    /// <summary>True if there is at least one entry in the forward stack.</summary>
    public bool CanGoForward { get; }

    /// <summary>Readonly view of the back stack (most recent last).</summary>
    public IReadOnlyList<TRoute> BackStack { get; }

    /// <summary>Readonly view of the forward stack.</summary>
    public IReadOnlyList<TRoute> ForwardStack { get; }

    /// <summary>Number of entries including current (back stack + 1).</summary>
    public int Depth { get; }

    // ── Navigation actions ──────────────────────────────────────────

    /// <summary>
    /// Push a new route onto the stack. Clears the forward stack.
    /// </summary>
    public void Navigate(TRoute route, NavigateOptions? options = null);

    /// <summary>
    /// Pop the current route and return to the previous one.
    /// Returns false if CanGoBack is false or a guard cancelled.
    /// </summary>
    public bool GoBack();

    /// <summary>
    /// Navigate forward (after a GoBack). Returns false if CanGoForward
    /// is false or a guard cancelled.
    /// </summary>
    public bool GoForward();

    /// <summary>
    /// Replace the current route without pushing to the back stack.
    /// Useful for redirects (e.g., login → home after auth).
    /// </summary>
    public void Replace(TRoute route);

    /// <summary>
    /// Clear the entire stack and set a new root route.
    /// </summary>
    public void Reset(TRoute route);

    /// <summary>
    /// Pop to the first entry matching the predicate.
    /// Returns false if no match found.
    /// </summary>
    public bool PopTo(Func<TRoute, bool> predicate);

    // ── Serialization ───────────────────────────────────────────────

    /// <summary>
    /// Serialize the full navigation state (back stack + current + forward
    /// stack) to a JSON string. Routes must be JSON-serializable.
    /// </summary>
    public string GetState();

    /// <summary>
    /// Restore navigation state from a JSON string previously returned
    /// by GetState(). Replaces the entire stack.
    /// </summary>
    public void SetState(string json);

    // ── Events (for advanced scenarios) ─────────────────────────────

    /// <summary>
    /// Fires after every navigation (push, pop, replace, reset).
    /// </summary>
    public event Action<NavigationEventArgs<TRoute>>? Navigated;
}
```

#### NavigateOptions

```csharp
public record NavigateOptions
{
    /// <summary>
    /// Override the default transition for this navigation.
    /// </summary>
    public NavigationTransition? Transition { get; init; }

    /// <summary>
    /// If false, navigate without adding to the back stack.
    /// Default: true.
    /// </summary>
    public bool PushToBackStack { get; init; } = true;
}
```

#### NavigationEventArgs

```csharp
public record NavigationEventArgs<TRoute>(
    TRoute Route,
    TRoute? PreviousRoute,
    NavigationMode Mode   // Push, Pop, Replace, Reset, Forward
);

public enum NavigationMode { Push, Pop, Replace, Reset, Forward }
```

---

### 4. NavigationHost element

`NavigationHost` is a new element that renders the current route's component.
It is the bridge between the navigation stack (data) and the UI (elements).

#### DSL

```csharp
// In Dsl.cs:
public static NavigationHostElement NavigationHost<TRoute>(
    NavigationHandle<TRoute> nav,
    Func<TRoute, Element> routeMap)
    where TRoute : notnull
    => new NavigationHostElement(nav, route => routeMap((TRoute)route));
```

#### Usage

```csharp
var nav = UseNavigation<AppRoute>(initial: new Home());

return NavigationView(
    new[] {
        NavItem("Home", icon: "Home", tag: "home"),
        NavItem("Settings", icon: "Setting", tag: "settings"),
    },
    content: NavigationHost(nav, route => route switch
    {
        Home => Component<HomePage>(),
        Detail r => Component<DetailPage>(new DetailProps(r.Id)),
        Settings => Component<SettingsPage>(),
        Profile p => Component<ProfilePage>(new ProfileProps(p.UserId)),
        _ => Text("Unknown route")
    })
) with {
    SelectedTag = nav.CurrentRoute switch {
        Home => "home",
        Settings => "settings",
        _ => null
    },
    IsBackEnabled = nav.CanGoBack,
    OnBackRequested = () => nav.GoBack(),
    OnSelectionChanged = tag => nav.Navigate(tag switch {
        "home" => new Home(),
        "settings" => new Settings(),
        _ => new Home()
    })
};
```

#### Element definition

```csharp
// In Element.cs:
public record NavigationHostElement(
    object NavigationHandle,           // NavigationHandle<TRoute> (type-erased for element tree)
    Func<object, Element> RouteMap     // TRoute → Element mapping
) : Element
{
    /// <summary>Transition to apply when navigation occurs.</summary>
    public NavigationTransition Transition { get; init; } = NavigationTransition.Default;

    /// <summary>Cache mode for page component instances.</summary>
    public NavigationCacheMode CacheMode { get; init; } = NavigationCacheMode.Disabled;

    /// <summary>Maximum number of pages in the LRU cache (when CacheMode is Enabled).</summary>
    public int CacheSize { get; init; } = 10;
}
```

#### Reconciler behavior

`MountNavigationHost`:
1. Create a `Grid` as the host container (supports overlapping children for
   transitions).
2. Call `routeMap(nav.CurrentRoute)` to get the initial element.
3. Mount the element as a child via the standard reconciler path.
4. Subscribe to `nav.Navigated` to handle future navigations.
5. Store the subscription and current child element in the control's `Tag`.

`UpdateNavigationHost`:
- Reconcile the NavigationHostElement properties (transition, cache mode).
- If the route has changed since last reconcile, trigger the content swap
  (same path as the Navigated handler).

On navigation:
1. Call `OnNavigatingFrom` guards on the current page (may cancel).
2. If not cancelled, resolve the new element via `routeMap(newRoute)`.
3. Mount the new element off-screen (in the Grid, but with `Opacity = 0`).
4. Run the exit animation on old content + enter animation on new content
   concurrently via `CompositionScopedBatch`.
5. When the batch completes: unmount the old element, set new element to full
   opacity.
6. Fire `OnNavigatedTo` on the new page, `OnNavigatedFrom` on the old page.
7. If caching is enabled, store the old element's mounted control in the cache
   instead of unmounting.

---

### 5. Navigation lifecycle hooks

Components that need to respond to navigation events use lifecycle hooks:

```csharp
class DetailPage : Component<DetailProps>
{
    public override Element Render()
    {
        UseNavigationLifecycle(
            onNavigatedTo: ctx =>
            {
                // Called after this page becomes active.
                // ctx.Route is the current route.
                // ctx.Mode is Push, Pop, Forward, etc.
                // ctx.Parameter is the route object.
                LoadData(Props.Id);
            },
            onNavigatingFrom: ctx =>
            {
                // Called before navigating away. Call ctx.Cancel() to block.
                if (hasUnsavedChanges)
                {
                    ctx.Cancel();
                    ShowSaveDialog();
                }
            },
            onNavigatedFrom: ctx =>
            {
                // Called after this page is no longer active.
                // Cleanup, analytics, etc.
            }
        );

        var (data, setData) = UseState<DetailData?>(null);
        // ...
    }
}
```

#### Implementation

```csharp
// In RenderContext / Component:
public void UseNavigationLifecycle(
    Action<NavigatedToContext>? onNavigatedTo = null,
    Action<NavigatingFromContext>? onNavigatingFrom = null,
    Action<NavigatedFromContext>? onNavigatedFrom = null)
{
    // Stores callbacks in a hook slot.
    // NavigationHost reads these from mounted components when
    // navigation occurs, similar to how FlushEffects works.
}
```

`NavigatingFromContext`:
```csharp
public sealed class NavigatingFromContext
{
    public object Route { get; }            // Current route being left
    public object TargetRoute { get; }      // Route being navigated to
    public NavigationMode Mode { get; }
    public bool IsCancelled { get; private set; }
    public void Cancel() => IsCancelled = true;
}
```

`NavigatedToContext`:
```csharp
public sealed class NavigatedToContext
{
    public object Route { get; }            // Route that was navigated to
    public object? PreviousRoute { get; }   // Route that was left
    public NavigationMode Mode { get; }
}
```

`NavigatedFromContext`:
```csharp
public sealed class NavigatedFromContext
{
    public object Route { get; }            // Route that was left
    public object TargetRoute { get; }      // Route that is now active
    public NavigationMode Mode { get; }
}
```

#### Lifecycle sequence

Navigate(newRoute):
```
1. onNavigatingFrom(current page)     ← can cancel
2. [if cancelled, abort]
3. Push current to back stack
4. Resolve new element via routeMap
5. Mount new element
6. Run transition animation
7. onNavigatedTo(new page)
8. onNavigatedFrom(old page)
9. Unmount or cache old element
```

GoBack():
```
1. onNavigatingFrom(current page)     ← can cancel
2. [if cancelled, abort]
3. Push current to forward stack
4. Pop back stack → previous route
5. Resolve/restore previous element
6. Run reverse transition animation
7. onNavigatedTo(previous page)
8. onNavigatedFrom(current page)
9. Unmount or cache current element
```

---

### 6. Navigation transitions

Transitions are powered by the WinUI Composition layer — the same GPU-
accelerated animation system used by Reactor's existing `LayoutAnimation` and
`ImplicitTransitions`. No WinUI Frame is involved.

#### Built-in transitions

```csharp
public abstract record NavigationTransition
{
    /// <summary>Platform default: slide from right on push, slide from left on pop.</summary>
    public static readonly NavigationTransition Default = new SlideTransition();

    /// <summary>No animation.</summary>
    public static readonly NavigationTransition None = new SuppressTransition();

    /// <summary>Slide in from a direction.</summary>
    public static NavigationTransition Slide(
        SlideDirection direction = SlideDirection.FromRight,
        TimeSpan? duration = null,
        CompositionEasingFunction? easing = null)
        => new SlideTransition(direction, duration, easing);

    /// <summary>Crossfade between old and new content.</summary>
    public static NavigationTransition Fade(TimeSpan? duration = null)
        => new FadeTransition(duration);

    /// <summary>Drill in (scale up from center) for hierarchical navigation.</summary>
    public static NavigationTransition DrillIn(TimeSpan? duration = null)
        => new DrillInTransition(duration);

    /// <summary>Connected animation: shared element transitions between pages.</summary>
    public static NavigationTransition Connected(string animationKey)
        => new ConnectedTransition(animationKey);

    /// <summary>Spring-based slide with configurable physics.</summary>
    public static NavigationTransition Spring(
        float dampingRatio = 0.7f,
        float period = 0.15f,
        SlideDirection direction = SlideDirection.FromRight)
        => new SpringSlideTransition(dampingRatio, period, direction);
}

public enum SlideDirection { FromRight, FromLeft, FromBottom, FromTop }
```

#### Per-navigation override

```csharp
// Use drill-in for this specific navigation
nav.Navigate(new Detail(42), new NavigateOptions
{
    Transition = NavigationTransition.DrillIn()
});
```

#### Per-host default

```csharp
NavigationHost(nav, routeMap) with
{
    Transition = NavigationTransition.Slide(SlideDirection.FromRight,
                                            TimeSpan.FromMilliseconds(250))
}
```

#### Automatic reverse transitions

When `GoBack()` is called, the transition plays in reverse automatically:
- `SlideFromRight` reverses to slide-out-to-right for old + slide-in-from-left
  for restored.
- `DrillIn` reverses to drill-out (scale down to center).
- `Fade` plays the same in both directions.
- `Connected` plays the connected animation in reverse.

#### Implementation approach

```
NavigationHost (Grid with two children: outgoing + incoming)
  │
  ├─ Get Composition Visual for outgoing content
  ├─ Get Composition Visual for incoming content (mounted at Opacity 0)
  │
  ├─ Create CompositionScopedBatch
  │   ├─ outgoing.StartAnimation("Offset", slideOutAnimation)
  │   ├─ outgoing.StartAnimation("Opacity", fadeOutAnimation)
  │   ├─ incoming.StartAnimation("Offset", slideInAnimation)
  │   └─ incoming.StartAnimation("Opacity", fadeInAnimation)
  │
  └─ batch.Completed += () =>
       ├─ Remove outgoing from Grid (or move to cache)
       └─ Set incoming Opacity = 1, Offset = (0,0)
```

This uses `ElementCompositionPreview.GetElementVisual()` to access the
Composition Visual for each mounted element, then runs animations directly on
the Visual's properties. All animation runs on the compositor thread — zero
managed-code involvement during the transition.

---

### 7. Page caching

NavigationHost optionally caches mounted component trees so that navigating
back restores the exact visual state (scroll position, form input, etc.)
without re-mounting.

#### Cache modes

```csharp
public enum NavigationCacheMode
{
    /// <summary>No caching. Components are unmounted on navigate-away and
    /// re-mounted on navigate-back. Default.</summary>
    Disabled,

    /// <summary>LRU cache bounded by CacheSize. Components are kept alive
    /// in memory but removed from the visual tree.</summary>
    Enabled,

    /// <summary>Components are never evicted from cache. Use sparingly for
    /// critical pages (e.g., home, dashboard).</summary>
    Required
}
```

#### Per-page cache override (future)

In Phase 3, individual routes can override the host's cache mode:

```csharp
NavigationHost(nav, route => route switch
{
    Home => Component<HomePage>().CacheMode(NavigationCacheMode.Required),
    Detail r => Component<DetailPage>(new(r.Id)),  // inherits host default
    _ => Text("404")
})
```

#### Cache implementation

The cache is a simple LRU dictionary keyed by route (using structural equality):

```
Dictionary<object, CachedPage> _cache;

struct CachedPage
{
    UIElement MountedControl;      // The WinUI control tree (detached from visual tree)
    Element LastElement;           // The Reactor element tree (for reconciliation)
    ComponentNode ComponentNode;   // The component tree node (preserves hook state)
    DateTime LastAccessed;         // For LRU eviction
}
```

On navigate-away (if cached):
1. Detach the control from the Grid (remove from `Children`).
2. Store in `_cache` keyed by the departing route.
3. Do NOT run cleanup effects or dispose hooks.

On navigate-to (cache hit):
1. Retrieve from `_cache`.
2. Re-attach the control to the Grid.
3. Run `onNavigatedTo` lifecycle hook.
4. The component's hook state is intact — no re-render needed unless the route
   changed (e.g., `Detail(42)` vs `Detail(43)`).

On cache eviction (LRU):
1. Run cleanup effects (pending `UseEffect` cleanups).
2. Unmount the control tree via the reconciler's standard unmount path.
3. Remove from `_cache`.

---

### 8. Nested navigation

Each `UseNavigation<T>(initial)` call with an initial route creates an
independent navigation stack. This naturally supports nested navigation
patterns:

#### Tabs with independent stacks

```csharp
class AppShell : Component
{
    public override Element Render()
    {
        var (activeTab, setActiveTab) = UseState(0);

        return TabView(
            Tab("Mail", MailTab()),
            Tab("Calendar", CalendarTab()),
            Tab("Contacts", ContactsTab())
        ) with { SelectedIndex = activeTab, OnSelectionChanged = setActiveTab };
    }
}

class MailTab : Component
{
    public override Element Render()
    {
        // This creates its OWN navigation stack, independent of other tabs
        var nav = UseNavigation<MailRoute>(initial: new Inbox());

        return NavigationHost(nav, route => route switch
        {
            Inbox => Component<InboxPage>(),
            MailDetail r => Component<MailDetailPage>(new(r.Id)),
            Compose => Component<ComposePage>(),
            _ => Text("404")
        });
    }
}
```

Each tab has its own back stack. Switching tabs preserves each tab's navigation
state (because the component is kept alive by TabView's existing behavior).

#### Nested navigation within a page

```csharp
class SettingsPage : Component
{
    public override Element Render()
    {
        // Nested stack within a page that's already in a parent stack
        var nav = UseNavigation<SettingsRoute>(initial: new SettingsHome());

        return VStack(
            Text("Settings").Heading(),
            NavigationHost(nav, route => route switch
            {
                SettingsHome => Component<SettingsHomePage>(),
                Account => Component<AccountPage>(),
                Privacy => Component<PrivacyPage>(),
                _ => Text("404")
            })
        );
    }
}
```

#### Split view (list-detail)

```csharp
class MasterDetail : Component
{
    public override Element Render()
    {
        var nav = UseNavigation<ItemRoute>(initial: new ItemList());
        var (width, _) = UseWindowSize(App.Window);
        var isWide = width >= 720;

        if (isWide)
        {
            // Side-by-side: list always visible, detail in right pane
            return HStack(
                Component<ItemListPage>().Width(320),
                NavigationHost(nav, route => route switch
                {
                    ItemDetail r => Component<ItemDetailPage>(new(r.Id)),
                    _ => Text("Select an item")
                })
            );
        }
        else
        {
            // Stacked: single pane with back navigation
            return NavigationHost(nav, route => route switch
            {
                ItemList => Component<ItemListPage>(),
                ItemDetail r => Component<ItemDetailPage>(new(r.Id)),
                _ => Text("404")
            });
        }
    }
}
```

---

### 9. Deep linking

Deep linking maps activation URIs (protocol handlers, toast notifications, app
launch arguments) to navigation routes.

#### Route URL mapping

```csharp
// Register URL patterns → route constructors
var deepLinks = new DeepLinkMap<AppRoute>()
    .Map("/", () => new Home())
    .Map("/detail/{id:int}", args => new Detail(args.Get<int>("id")))
    .Map("/profile/{userId}", args => new Profile(args.Get<string>("userId")))
    .Map("/settings", () => new Settings());
```

#### Handling activation

```csharp
class AppShell : Component
{
    public override Element Render()
    {
        var nav = UseNavigation<AppRoute>(initial: new Home());

        // Handle deep links on activation
        UseEffect(() =>
        {
            var args = App.LaunchArgs;
            if (args?.Uri is Uri uri)
            {
                var (routes, matched) = deepLinks.Resolve(uri);
                if (matched)
                {
                    // Restore full stack from URI
                    // e.g., /detail/42 → [Home, Detail(42)]
                    nav.SetState(routes);
                }
            }
        });

        // ... NavigationView + NavigationHost
    }
}
```

#### Synthetic back stack

When deep-linking to `/detail/42`, the framework can optionally construct a
synthetic back stack so the user can press Back to reach Home:

```csharp
deepLinks.Map("/detail/{id:int}", args => new Detail(args.Get<int>("id")))
    .WithBackStack(() => new AppRoute[] { new Home() });
```

This is consistent with Android's approach (synthetic back stack for
notifications) and React Navigation's `initialRouteName` + deep link state
reconstruction.

---

### 10. State serialization and restoration

The full navigation state can be serialized for app suspension (PLM) and
restored on relaunch.

#### API

```csharp
// Save (caller picks the storage format)
NavigationState<AppRoute> state = nav.GetState();
string json = JsonSerializer.Serialize(state, AppJsonContext.Default.NavigationStateAppRoute);
// json: {"BackStack":[{"$type":"home"},{"$type":"detail","Id":42}],
//        "Current":{"$type":"settings"},
//        "ForwardStack":[]}
// (Default STJ casing — apply JsonNamingPolicy.CamelCase on your context if
//  you prefer camelCase output.)

ApplicationData.Current.LocalSettings.Values["nav_state"] = json;

// Restore
if (ApplicationData.Current.LocalSettings.Values.TryGetValue("nav_state", out var saved))
{
    var restored = JsonSerializer.Deserialize((string)saved, AppJsonContext.Default.NavigationStateAppRoute);
    if (restored is not null) nav.SetState(restored);
}
```

#### Serialization format

Uses `System.Text.Json` with polymorphic serialization (`$type` discriminator)
for route hierarchies:

```csharp
var options = new JsonSerializerOptions
{
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    // Route types registered via [JsonDerivedType] on the base
};

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Home), "Home")]
[JsonDerivedType(typeof(Detail), "Detail")]
[JsonDerivedType(typeof(Settings), "Settings")]
abstract record AppRoute;
```

The `NavigationStack` serializes (with default STJ casing) as:
```json
{
    "BackStack": [ ... ],
    "Current": { ... },
    "ForwardStack": [ ... ]
}
```

---

### 11. SystemNavigationManager integration

The framework integrates with Windows' system-level back button and title bar:

```csharp
class AppShell : Component
{
    public override Element Render()
    {
        var nav = UseNavigation<AppRoute>(initial: new Home());

        // Wire system back button (title bar, tablet mode, gamepad B button)
        UseEffect(() =>
        {
            var snm = SystemNavigationManager.GetForCurrentView();
            snm.BackRequested += (s, e) =>
            {
                if (nav.CanGoBack)
                {
                    nav.GoBack();
                    e.Handled = true;
                }
            };
            return () => { /* unsubscribe */ };
        });

        // Or use the built-in helper hook:
        UseSystemBackButton(nav);

        return NavigationView(...) with {
            IsBackEnabled = nav.CanGoBack,
            OnBackRequested = () => nav.GoBack()
        };
    }
}
```

`UseSystemBackButton(nav)` is a convenience hook that:
1. Subscribes to `SystemNavigationManager.BackRequested`.
2. Subscribes to `CoreWindow.KeyDown` for `VirtualKey.GoBack` / `Alt+Left`.
3. Calls `nav.GoBack()` and marks the event handled.
4. Updates `AppViewBackButtonVisibility` based on `nav.CanGoBack`.

---

### 12. Full example: production app shell

```csharp
// Routes
abstract record AppRoute;
record Home : AppRoute;
record Detail(int Id) : AppRoute;
record Settings : AppRoute;
record Profile(string UserId) : AppRoute;

class AppShell : Component
{
    public override Element Render()
    {
        var nav = UseNavigation<AppRoute>(initial: new Home());
        UseSystemBackButton(nav);

        return VStack(
            TitleBar("My App") with {
                IsBackButtonVisible = nav.CanGoBack,
                IsBackButtonEnabled = nav.CanGoBack,
                OnBackRequested = () => nav.GoBack()
            },
            NavigationView(
                new[] {
                    NavItem("Home",     icon: "Home",    tag: "home"),
                    NavItem("Profile",  icon: "Contact", tag: "profile"),
                    NavItem("Settings", icon: "Setting", tag: "settings"),
                },
                content: NavigationHost(nav, route => route switch
                {
                    Home      => Component<HomePage>(),
                    Detail  r => Component<DetailPage>(new DetailProps(r.Id)),
                    Settings  => Component<SettingsPage>(),
                    Profile p => Component<ProfilePage>(new ProfileProps(p.UserId)),
                    _         => Text("Page not found")
                }) with {
                    Transition = NavigationTransition.Default,
                    CacheMode = NavigationCacheMode.Enabled,
                    CacheSize = 5
                }
            ) with {
                SelectedTag = nav.CurrentRoute switch {
                    Home     => "home",
                    Profile  => "profile",
                    Settings => "settings",
                    _        => null
                },
                IsBackEnabled = nav.CanGoBack,
                OnBackRequested = () => nav.GoBack(),
                OnSelectionChanged = tag => nav.Navigate(tag switch {
                    "home"     => new Home() as AppRoute,
                    "profile"  => new Profile("me"),
                    "settings" => new Settings(),
                    _          => new Home()
                })
            }
        );
    }
}

class DetailPage : Component<DetailProps>
{
    public override Element Render()
    {
        var nav = UseNavigation<AppRoute>();
        var (data, setData) = UseState<ItemData?>(null);

        UseNavigationLifecycle(
            onNavigatedTo: ctx => LoadData(Props.Id),
            onNavigatingFrom: ctx =>
            {
                if (hasUnsavedChanges) ctx.Cancel();
            }
        );

        UseEffect(() =>
        {
            // Async data loading
            _ = Task.Run(async () =>
            {
                var item = await Api.GetItem(Props.Id);
                setData(item);
            });
        }, Props.Id);

        return ScrollViewer(VStack(16,
            Text(data?.Title ?? "Loading...").Heading(),
            Text(data?.Description ?? ""),
            Button("Related item", () => nav.Navigate(new Detail(Props.Id + 1),
                new NavigateOptions { Transition = NavigationTransition.DrillIn() })),
            Button("Go home", () => nav.PopTo(r => r is Home))
        ));
    }
}
```

---

## Implementation Plan

### Phase 1: Core navigation (unblocks all scenarios)

**Deliverables:**
- `NavigationStack<TRoute>` internal state management class
- `NavigationHandle<TRoute>` public API
- `UseNavigation<TRoute>()` hook in `RenderContext` and `Component`
- `NavigationHostElement` record in `Element.cs`
- `NavigationHost()` DSL factory in `Dsl.cs`
- `MountNavigationHost()` / `UpdateNavigationHost()` in `Reconciler.Mount.cs` /
  `Reconciler.Update.cs`
- `UseNavigationLifecycle()` hook
- Content switching via `ContentPresenter` (no transitions yet — instant swap)
- `NavigationContext<TRoute>` — `Context` for sharing the handle

**New files:**
- `Reactor/Core/Navigation/NavigationStack.cs`
- `Reactor/Core/Navigation/NavigationHandle.cs`
- `Reactor/Core/Navigation/NavigationHostElement.cs` (or inline in Element.cs)
- `Reactor/Core/Navigation/NavigationTransition.cs`
- `Reactor/Core/Navigation/NavigationLifecycle.cs`

**Modified files:**
- `Reactor/Core/RenderContext.cs` — add `UseNavigation<T>()` and
  `UseNavigationLifecycle()` hooks
- `Reactor/Core/Component.cs` — add convenience methods delegating to
  `RenderContext`
- `Reactor/Core/Element.cs` — add `NavigationHostElement` record
- `Reactor/Elements/Dsl.cs` — add `NavigationHost()` factory
- `Reactor/Core/Reconciler.Mount.cs` — add `MountNavigationHost()`
- `Reactor/Core/Reconciler.Update.cs` — add `UpdateNavigationHost()`

**Tests:**
- Unit tests for `NavigationStack` (push, pop, replace, reset, popTo, guards)
- Integration tests for `NavigationHost` rendering
- Lifecycle hook ordering tests
- Nested navigation tests

**Estimated scope:** ~800-1200 lines of production code, ~600 lines of tests.

### Phase 2: Composition-layer transitions

**Deliverables:**
- `NavigationTransition` record hierarchy (Slide, Fade, DrillIn, Connected,
  Spring, Suppress)
- Transition engine using `CompositionScopedBatch` + `Visual.StartAnimation()`
- Automatic reverse transitions on GoBack
- Transition configuration on NavigationHost and per-Navigate
- `UseSystemBackButton()` convenience hook

**New files:**
- `Reactor/Core/Navigation/TransitionEngine.cs`

**Modified files:**
- `Reactor/Core/Reconciler.Mount.cs` — transition logic in NavigationHost handler

**Estimated scope:** ~500-700 lines of production code.

### Phase 3: Page caching, deep linking, serialization

**Deliverables:**
- `NavigationCacheMode` enum and LRU cache in NavigationHost
- `GetState()` / `SetState()` on NavigationHandle
- `DeepLinkMap<TRoute>` URI-to-route mapping
- `UseSystemBackButton()` hook
- Per-route cache mode overrides
- Connected animation integration (shared element transitions across pages)

**New files:**
- `Reactor/Core/Navigation/NavigationCache.cs`
- `Reactor/Core/Navigation/DeepLinkMap.cs`

**Estimated scope:** ~600-900 lines of production code.

### Phase 4: Polish and integration

**Deliverables:**
- Sample app demonstrating all navigation features
- NavigationView helper that auto-syncs selected tag with current route
- Predictive back gesture support (Windows 11)
- Performance profiling and optimization
- Documentation and migration guide from manual state-switching

---

## Future: WinUI3 Runtime Integration

When we can make changes to the WinUI3 runtime, the goal is to make Reactor
components first-class citizens in Frame's navigation system while preserving
full backward compatibility for existing XAML apps.

### Change 1: INavigationContentProvider interface

**File:** New IDL + implementation in `dxaml/xcp/dxaml/lib/`

Add a new interface that allows Frame to delegate content creation and lifecycle
to an external provider instead of using MetadataAPI + ActivationAPI + IPage:

```midl
[contract(Microsoft.UI.Xaml.WinUIContract, 6)]
interface INavigationContentProvider
{
    /// <summary>
    /// Create content for the given type descriptor and parameter.
    /// Returns a UIElement to set as Frame.Content.
    /// </summary>
    Windows.UI.Xaml.UIElement CreateContent(
        String descriptor,
        Object parameter);

    /// <summary>
    /// Called before navigating away from content. Return false to cancel.
    /// </summary>
    Boolean OnNavigatingFrom(
        Windows.UI.Xaml.UIElement content,
        NavigationMode mode);

    /// <summary>
    /// Called after content is displayed.
    /// </summary>
    void OnNavigatedTo(
        Windows.UI.Xaml.UIElement content,
        Object parameter,
        NavigationMode mode);

    /// <summary>
    /// Called after navigating away from content.
    /// </summary>
    void OnNavigatedFrom(
        Windows.UI.Xaml.UIElement content,
        NavigationMode mode);

    /// <summary>
    /// Serialize the given content's state for GetNavigationState().
    /// </summary>
    String SerializeContent(Windows.UI.Xaml.UIElement content);

    /// <summary>
    /// Deserialize content state from SetNavigationState().
    /// </summary>
    Windows.UI.Xaml.UIElement DeserializeContent(
        String descriptor,
        String serializedState);
}
```

**Frame changes:**

Add a new `ContentProvider` property to Frame:

```midl
[contract(Microsoft.UI.Xaml.WinUIContract, 6)]
{
    INavigationContentProvider ContentProvider;
}
```

In `Frame_Partial.cpp`, modify the navigation path:

```
NavigateImpl() / NavigateWithTransitionInfoImpl():
  │
  ├─ [existing] MetadataAPI::GetClassInfoByTypeName(sourcePageType)
  │   └─ If ContentProvider is set AND type is not in XAML metadata:
  │      └─ Store descriptor in PageStackEntry (existing behavior)
  │      └─ Skip ActivationAPI — delegate to ContentProvider
  │
  └─ StartNavigation() → PerformNavigation() → ChangeContent():
      │
      ├─ [existing path if ContentProvider is null]
      │   NavigationCache::GetContent() → ActivateInstance → IPage cast
      │
      └─ [new path if ContentProvider is set]
          ContentProvider->CreateContent(descriptor, parameter)
          → returns UIElement (no IPage cast)
          → Set as Frame.Content via put_Content()
          → ContentProvider->OnNavigatedTo(content, parameter, mode)
```

**Backward compatibility:**
- If `ContentProvider` is `null` (default), Frame behaves exactly as today.
  Zero impact on existing apps.
- If `ContentProvider` is set, Frame uses it for ALL navigations (the provider
  can delegate back to default behavior for specific types if needed).
- `PageStackEntry` already stores a string descriptor — no change needed.
- `NavigationCache` is bypassed when ContentProvider is set (the provider
  manages its own caching).
- `GetNavigationState()` / `SetNavigationState()` delegate serialization to the
  provider.

**Risk assessment:**
- The change is additive — new property, new interface, new code path gated
  behind a null check.
- Existing tests continue to pass because the default path is unchanged.
- New code path needs new tests covering: navigation, back/forward, caching
  bypass, serialization, transition info forwarding.

### Change 2: Relaxed IPage requirement in ChangeContent

**File:** `Frame_Partial.cpp` line ~642

Currently:
```cpp
spNewIPage = ctl::query_interface_cast<IPage>(pNewIInspectable);
IFCPTR(spNewIPage);  // HARD FAIL if not IPage
```

Change to:
```cpp
spNewIPage = ctl::query_interface_cast<IPage>(pNewIInspectable);
if (spNewIPage)
{
    // Existing IPage lifecycle calls
    spNewIPage.Cast<Page>()->InvokeOnNavigatedTo(pNavigationEventArgs);
}
else if (m_spContentProvider)
{
    // Delegate lifecycle to content provider
    m_spContentProvider->OnNavigatedTo(pNewIInspectable, parameter, mode);
}
else
{
    // No IPage, no provider — fail as before for backward compat
    IFCPTR(spNewIPage);
}
```

This three-way branch preserves exact existing behavior when no provider is set,
while allowing non-IPage content when a provider is present.

### Change 3: TypeName registration for non-XAML types

**File:** `MetadataAPI.cpp`

For the ContentProvider path, Frame still receives a `TypeName` in
`Navigate()`. Rather than requiring XAML metadata registration, allow the
ContentProvider path to use the TypeName's `Name` field as an opaque descriptor
string (which is what PageStackEntry already stores).

Modify `NavigateImpl`:
```cpp
if (m_spContentProvider)
{
    // Skip MetadataAPI resolution — use the TypeName.Name as descriptor directly
    strDescriptor = sourcePageType.Name;
}
else
{
    // Existing path: resolve through MetadataAPI
    IFC(MetadataAPI::GetClassInfoByTypeName(sourcePageType, &pType));
    IFC(pType->GetFullName().Promote(&strDescriptor));
}
```

This means Reactor can pass `TypeName { Name = "MyApp.Routes.Detail", Kind = ... }`
and Frame stores `"MyApp.Routes.Detail"` as the descriptor without requiring
XAML metadata registration.

### Change 4: ContentProvider-aware NavigationCache (optional)

**File:** `NavigationCache.cpp`

Add an overload or mode where NavigationCache delegates to ContentProvider for
content creation instead of `ActivationAPI::ActivateInstance()`:

```cpp
LoadContent(descriptor, ppInstance):
  if (m_spContentProvider)
    return m_spContentProvider->CreateContent(descriptor, nullptr, ppInstance);
  else
    // existing ActivateInstance path
```

This allows Reactor pages to participate in Frame's built-in LRU caching.

### Summary of WinUI3 changes

| Change | Files Modified | Risk | Backward Compatible |
|--------|---------------|------|-------------------|
| INavigationContentProvider interface | New IDL + new .cpp | Low | Yes — additive |
| Frame.ContentProvider property | Frame.idl, Frame_Partial.h/cpp | Low | Yes — null = old behavior |
| Relaxed IPage check in ChangeContent | Frame_Partial.cpp | Medium | Yes — gated on provider |
| TypeName pass-through for provider | Frame_Partial.cpp | Low | Yes — only with provider |
| Provider-aware NavigationCache | NavigationCache.cpp | Low | Yes — gated on provider |

**Total estimated WinUI3 diff:** ~200-300 lines of C++ across 4 files, plus
~100 lines of IDL, plus tests. All changes are gated behind
`m_spContentProvider != nullptr` — the existing navigation path is untouched.

---

## Migration Guide

### From manual state switching (current Reactor pattern)

Before:
```csharp
var (currentPage, setCurrentPage) = UseState("home");
return NavigationView(menuItems,
    currentPage switch {
        "home" => Component<HomePage>(),
        "settings" => Component<SettingsPage>(),
        _ => Text("404")
    }
) with {
    OnSelectionChanged = tag => { if (tag != null) setCurrentPage(tag); }
};
```

After:
```csharp
var nav = UseNavigation<AppRoute>(initial: new Home());
return NavigationView(menuItems,
    NavigationHost(nav, route => route switch {
        Home => Component<HomePage>(),
        Settings => Component<SettingsPage>(),
        _ => Text("404")
    })
) with {
    IsBackEnabled = nav.CanGoBack,
    OnBackRequested = () => nav.GoBack(),
    OnSelectionChanged = tag => nav.Navigate(tag switch {
        "home" => new Home() as AppRoute,
        "settings" => new Settings(),
        _ => new Home()
    })
};
```

Key differences:
1. Replace `UseState<string>` with `UseNavigation<TRoute>`.
2. Replace inline `switch` with `NavigationHost(nav, routeMap)`.
3. Wire `IsBackEnabled` + `OnBackRequested`.
4. Routes are typed records instead of strings.
5. You get back stack, transitions, lifecycle, and deep linking for free.

### From PageHelper + XAML Frame navigation

Before:
```csharp
// MyPage.xaml (required for Frame.Navigate)
// MyPage.xaml.cs:
protected override void OnNavigatedTo(NavigationEventArgs e)
{
    _host = PageHelper.Mount<MyComponent>(this, e);
}
```

After:
```csharp
// No XAML files needed
var nav = UseNavigation<AppRoute>(initial: new Home());
return NavigationHost(nav, route => route switch {
    Home => Component<MyComponent>(),
    ...
});
```

`PageHelper` remains available for hybrid apps that mix XAML pages and Reactor
components in a WinUI Frame. The navigation system does not deprecate it.

---

## Appendix A: Framework Navigation Comparison

### SwiftUI

- **Model:** Data-driven `NavigationPath` binding.
- **Routes:** `Hashable` types (typically enums with associated values).
- **Destination mapping:** `navigationDestination(for:)` modifier.
- **State ownership:** Developer-owned `@State` / `@Observable` path.
- **Back:** Automatic back button; `path.removeLast()` programmatic.
- **Serialization:** `NavigationPath` is `Codable` → `@SceneStorage`.
- **Transitions:** Platform default slide; iOS 18+ custom `NavigationTransition`.
- **Nested nav:** Separate `NavigationStack` per `TabView` tab.

### Jetpack Compose Navigation 3

- **Model:** Developer-owned `mutableStateListOf<Any>` back stack.
- **Routes:** `@Serializable` data classes / data objects.
- **Destination mapping:** `entryProvider { entry<Route> { ... } }`.
- **State ownership:** Developer-owned snapshot-state list.
- **Back:** `onBack` lambda on `NavDisplay`; developer implements.
- **Serialization:** Manual (developer serializes the list).
- **Transitions:** Configurable enter/exit/pop transitions on `NavDisplay`.
- **Nested nav:** `Scene` + `SceneStrategy` APIs for multi-pane.

### React Router v7

- **Model:** URL-based route tree with loaders and actions.
- **Routes:** Path strings with typed params (generated types).
- **Destination mapping:** Route object array with `element` + `loader`.
- **State ownership:** Browser history API (framework-owned).
- **Back:** Browser back button; `navigate(-1)` programmatic; `useBlocker()`.
- **Serialization:** URL IS the state.
- **Transitions:** CSS View Transitions API integration.
- **Nested nav:** First-class `<Outlet />` for nested routes.

### React Navigation (React Native)

- **Model:** Navigator hierarchy (Stack, Tab, Drawer composable).
- **Routes:** String screen names with typed `ParamList`.
- **Destination mapping:** `<Stack.Screen name component>` JSX.
- **State ownership:** Framework-owned JSON state tree.
- **Back:** `navigation.goBack()`; `beforeRemove` event guard.
- **Serialization:** Full JSON state; `onStateChange` callback.
- **Transitions:** Platform-native (iOS slide, Android fade) or JS-driven custom.
- **Nested nav:** First-class composable navigators.

### Flutter GoRouter

- **Model:** URL-based route tree with builder functions.
- **Routes:** Path strings; optional `go_router_builder` code-gen for types.
- **Destination mapping:** `GoRoute(path, builder)` objects.
- **State ownership:** Framework-owned (GoRouter internal state).
- **Back:** `context.pop()`; `PopScope` widget for guards.
- **Serialization:** URL for path/query params; `extra` is ephemeral.
- **Transitions:** `CustomTransitionPage` per route.
- **Nested nav:** `StatefulShellRoute.indexedStack` for tab state preservation.

### .NET MAUI Shell

- **Model:** URI-based routing with XAML shell structure.
- **Routes:** String URIs registered via `Routing.RegisterRoute()`.
- **Destination mapping:** `ShellContent ContentTemplate` in XAML.
- **State ownership:** Framework-owned (Shell internal state).
- **Back:** `GoToAsync("..")`; automatic system back button.
- **Serialization:** URI only (no full state serialization).
- **Transitions:** Platform defaults; limited customization.
- **Nested nav:** XAML hierarchy (TabBar > Tab > ShellContent).

### What Reactor takes from each

| Framework | Pattern adopted | Adaptation |
|-----------|----------------|------------|
| SwiftUI | Developer-owned path as bindable data | `UseNavigation` returns a handle wrapping a typed list |
| Compose Nav3 | Back stack as plain mutable list | `NavigationStack<T>` is a list managed by the hook |
| SwiftUI | Type-safe routes via value types | C# records with structural equality |
| Compose Nav3 | `entryProvider` destination mapping | `NavigationHost(nav, routeMap)` |
| SwiftUI | `Codable` state serialization | `System.Text.Json` polymorphic serialization |
| React Navigation | `beforeRemove` guard pattern | `UseNavigationLifecycle(onNavigatingFrom: ctx.Cancel())` |
| Flutter | `StatefulShellRoute` tab preservation | Independent `UseNavigation` per tab, natural composition |
| React Router | Loader-driven transitions | Composition-layer `CompositionScopedBatch` transitions |

---

## Appendix B: Rejected Alternatives

### Alternative 1: WinUI Frame with IPage Bridge Page

Create a single XAML-backed `ReactorBridgePage : Page` and use WinUI's Frame
normally.

**Rejected because:**
- Requires at least one XAML file, breaking the pure-C# model.
- Creates a new `ReactorHost` per navigation (performance overhead).
- Component state is lost on navigation unless `NavigationCacheMode` is set.
- Parameters are `object`-typed (no compile-time safety).
- Nested navigation (Frame-in-Frame) is problematic in WinUI.
- Two state management systems (Reactor hooks + Frame back stack) must stay in sync.

### Alternative 2: Fork WinUI Frame

Modify WinUI3 source to add `INavigationContentProvider`, remove IPage
requirement, and expose virtual extension points.

**Rejected as primary approach because:**
- Requires C++/WinRT modifications with deep understanding of WinUI internals.
- Must be maintained across WinUI updates.
- Long timeline for code review and upstream acceptance.
- Cannot ship Reactor navigation until WinUI changes land.

**Preserved as future work:** Once Reactor's native navigation proves the design,
the WinUI changes described in "Future: WinUI3 Runtime Integration" can be
proposed upstream. This gives Reactor navigation today AND deeper integration later.

### Alternative 3: Type-safe router layer over WinUI Frame

Build the Reactor router API (type-safe routes, hooks) but internally delegate to
a WinUI Frame + bridge page for rendering and transitions.

**Rejected because:**
- Dual state management (Reactor stack + Frame stack) is a bug surface.
- Still requires one XAML file.
- Creates new ReactorHost per page.
- Nested navigation is constrained by Frame.
- Gains are marginal vs. the pure Reactor approach — WinUI's slide transition is
  achievable with Composition APIs directly.

---

## Appendix C: Decision Log

| Decision | Options considered | Chosen | Rationale |
|----------|-------------------|--------|-----------|
| Navigation state ownership | Framework-owned (Frame), Developer-owned (hook) | Developer-owned | Industry trend (SwiftUI, Compose Nav3); fits Reactor's hook model; testable |
| Route type system | Strings, enums, records, interfaces | Records | Carry parameters, structural equality, JSON serializable, `switch` exhaustiveness |
| Hosting control | WinUI Frame, ContentPresenter, Grid | Grid | Supports overlapping children for transitions; no IPage requirement |
| Transition system | WinUI Frame transitions, CSS-like, Composition APIs | Composition APIs | Already used by Reactor (LayoutAnimation); GPU-accelerated; full control |
| Cache strategy | WinUI NavigationCache, custom LRU | Custom LRU | No IPage dependency; cache keyed by route (structural equality); controllable |
| Context distribution | Props threading, Context, global static | Context | Consistent with Reactor's existing context system; no prop drilling |
| Deep linking | URL router, URI mapper, manual | URI mapper with synthetic back stack | Desktop apps use activation URIs, not browser URLs; synthetic back stack follows Android pattern |
