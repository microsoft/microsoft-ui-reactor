---
name: reactor-navigation
description: >
  Reactor's navigation system — UseNavigation hook, NavigationHost renderer,
  NavigationView sidebar, TabView tabs, route params, stack operations,
  page lifecycle, deep linking, page transitions, caching, and state
  serialization. Load this when building multi-page apps, wiring sidebar
  or tab navigation, or implementing deep links.
---

# Navigation in Reactor

Reactor uses a **stack-based navigation model** with type-safe routes. You
define routes as an enum, create a navigation handle with `UseNavigation`,
and render the current page with `NavigationHost`. The system manages
back/forward stacks, transitions, caching, and deep linking.

## Quick reference

| API | Purpose |
|-----|---------|
| `UseNavigation(Route.Home)` | Create a `NavigationHandle<Route>` (call once at root) |
| `UseNavigation<Route>()` | Retrieve the nearest ancestor's handle (child components) |
| `NavigationHost(nav, route => ...)` | Render the current page |
| `NavigationView([items], content)` | Sidebar navigation with icons |
| `TabView(Tab(...), Tab(...))` | Tab-based parallel navigation |
| `UseNavigationLifecycle(...)` | Page appear/disappear callbacks |
| `UseSystemBackButton(nav, window)` | Wire system back button to nav stack |
| `DeepLinkMap<Route>` | Map URI patterns to routes |

## 1. Defining routes

Use an enum. Each value is a distinct page:

```csharp
enum Route { Home, Settings, Profile, Details }
```

For routes that carry data, use a discriminated-union pattern with records:

```csharp
abstract record Route
{
    public record Home : Route;
    public record Settings : Route;
    public record Details(int ItemId) : Route;
}
```

## 2. Basic navigation

```csharp
class App : Component
{
    public override Element Render()
    {
        var nav = UseNavigation(Route.Home);

        return VStack(12,
            HStack(8,
                Button("Home", () => nav.Navigate(Route.Home)),
                Button("Settings", () => nav.Navigate(Route.Settings)),
                Button("Back", () => nav.GoBack()).Disabled(!nav.CanGoBack)
            ),
            NavigationHost(nav, route => route switch
            {
                Route.Home     => Component<HomePage>(),
                Route.Settings => Component<SettingsPage>(),
                _ => TextBlock("Not found")
            })
        );
    }
}
```

**Key points:**
- `UseNavigation(Route.Home)` — call once at the root with the initial route.
- `nav.Navigate(route)` — pushes onto the back stack.
- `nav.GoBack()` / `nav.GoForward()` — stack navigation.
- `NavigationHost(nav, route => ...)` — renders the matched element.

## 3. Stack operations

| Method | Effect |
|--------|--------|
| `Navigate(route)` | Push route onto back stack |
| `GoBack()` | Pop current, return to previous |
| `GoForward()` | Move forward (after going back) |
| `Replace(route)` | Swap current route without touching the stack |
| `Reset(route)` | Clear all stacks, start fresh |
| `PopTo(predicate)` | Pop until a matching route is found |

Properties: `CurrentRoute`, `CanGoBack`, `CanGoForward`, `BackStack`,
`ForwardStack`, `Depth`.

Use `Reset` for sign-out flows to prevent navigating back to authenticated
pages.

## 4. NavigationView (sidebar)

```csharp
var nav = UseNavigation(Route.Home);

return NavigationView(
    [
        NavItem("Home", icon: "Home", tag: "Home"),
        NavItem("Settings", icon: "Setting", tag: "Settings"),
        NavItem("Profile", icon: "Contact", tag: "Profile")
    ],
    content: NavigationHost(nav, route => route switch
    {
        Route.Home     => VStack(Heading("Home"), TextBlock("Welcome.")).Padding(24),
        Route.Settings => VStack(Heading("Settings"), TextBlock("Configure.")).Padding(24),
        Route.Profile  => VStack(Heading("Profile"), TextBlock("Your info.")).Padding(24),
        _ => TextBlock("Not found").Padding(24)
    })
);
```

`NavItem(label, icon?, tag?)` creates sidebar items. Icons are Segoe Fluent
Icons names. The `NavigationView` handles selection state.

## 5. TabView (parallel tabs)

Unlike stack navigation, tabs keep all content alive simultaneously:

```csharp
return TabView(
    Tab("Documents", VStack(12,
        TextBlock("Your documents."),
        Button("New", () => { })).Padding(24)),
    Tab("Recent", TextBlock("Recently opened.").Padding(24)),
    Tab("Shared", TextBlock("Shared files.").Padding(24))
);
```

Use tabs when users switch freely between parallel workspaces.

## 6. Page lifecycle

`UseNavigationLifecycle` fires callbacks when a page appears or disappears:

```csharp
UseNavigationLifecycle(
    onNavigatedTo: ctx => LoadData(ctx.PreviousRoute),
    onNavigatingFrom: ctx =>
    {
        if (hasUnsavedChanges) ctx.Cancel(); // prevent navigation
    },
    onNavigatedFrom: ctx => SaveDraft()
);
```

| Callback | When |
|----------|------|
| `onNavigatedTo` | After this page becomes active — fetch data, start timers |
| `onNavigatingFrom` | Before leaving — call `ctx.Cancel()` to block navigation |
| `onNavigatedFrom` | After leaving — save drafts, stop timers |

## 7. Page transitions

Set `Transition` on `NavigationHost`:

```csharp
NavigationHost(nav, route => ...) with
{
    Transition = NavigationTransition.DrillIn()
}
```

| Transition | Effect |
|-----------|--------|
| `NavigationTransition.Slide()` | Slide + fade (default) |
| `NavigationTransition.Fade()` | Crossfade |
| `NavigationTransition.DrillIn()` | Scale + fade — use for list→detail |
| `NavigationTransition.Spring()` | Spring-physics slide |
| `NavigationTransition.None` | Instant swap |

GoBack automatically reverses direction. Transitions run on the compositor
thread.

## 8. Deep linking

`DeepLinkMap` maps URI patterns to route constructors:

```csharp
var map = UseMemo(() => new DeepLinkMap<Route>()
    .Map("/", _ => Route.Home)
    .Map("/settings", _ => Route.Settings)
    .Map("/users/{id:int}/posts/{postId:int}", args =>
    {
        var userId = args.Get<int>("id");
        var postId = args.Get<int>("postId");
        return Route.Details;
    }, backStackFactory: () => new[] { Route.Home }));

// Resolve from activation URI:
map.Resolve(uri);
```

**Pattern segments:**

| Segment | Matches |
|---------|---------|
| `/literal` | Exact match |
| `/{param}` | String capture |
| `/{param:int}` | Typed capture (int, long, bool, Guid) |
| `/{param?}` | Optional parameter |
| `/{**}` | Wildcard — remaining path |
| `?key=value` | Query string parameters |

`backStackFactory` builds a synthetic back stack so GoBack works after
deep-linked entry.

## 9. Page caching

```csharp
NavigationHost(nav, route => ...) with
{
    CacheMode = NavigationCacheMode.Enabled,
    CacheSize = 5
}
```

| Cache mode | Behavior |
|-----------|----------|
| `Disabled` | Always unmount/remount (default) |
| `Enabled` | LRU cache up to `CacheSize` entries |
| `Required` | Always cached, never evicted |

Caching preserves scroll position, text input, and component state.

## 10. State serialization

```csharp
var json = nav.GetState();          // serialize full nav state
nav.SetState(json);                 // restore stacks + navigate
```

Use for persisting navigation across app restarts.

## 11. System back button

```csharp
UseSystemBackButton(nav, window);
```

Wires the Windows title-bar back button and hardware back button to your
navigation stack automatically.

## Critical gotchas

1. **Call `UseNavigation(initial)` once at the root.** Children retrieve the
   same handle via `UseNavigation<Route>()` (no initial value) through
   context.
2. **Use enums for routes.** Compile-time safety — can't navigate to a
   non-existent route.
3. **Use `Reset` for sign-out.** Clears the stack so users can't go back.
4. **DrillIn for list→detail.** Signals hierarchy; pairs with connected
   animation.
5. **`ctx.Cancel()` in `onNavigatingFrom`** — the only way to guard
   against unsaved-changes navigation.
6. **Don't forget `backStackFactory` in deep links.** Without it, GoBack
   does nothing after a deep-linked entry.
