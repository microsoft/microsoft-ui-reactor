---
title: "Services and dependency injection"
description: "Most apps have code that is not UI: a data store, a settings file, an HTTP client, a clock, a logger. Components need to reach that code, and tests need to replace it with fakes."
sidebar:
  label: "Services and DI"
---

Reactor does not ship a dependency injection
container, and `ReactorApp` has no service-provider hook. You do not need one.
Reactor's context system already passes values down the element tree to any
component that asks for them. You can put a single service in a context. Or
you can build a `Microsoft.Extensions.DependencyInjection` container at
startup and put the whole `IServiceProvider` in a context, then resolve
services with a small custom hook. This page shows both approaches, how scoped
and disposable services follow the component lifecycle, how to swap in fakes,
and when plain props are the better choice.

This page builds on [Context](/microsoft-ui-reactor/main/guides/context) and [Hooks](/microsoft-ui-reactor/main/guides/hooks). All code
comes from one compiled doc app,
[`docs/_pipeline/apps/services`](https://github.com/microsoft/microsoft-ui-reactor/tree/main/docs/_pipeline/apps/services).

## Reference [#reference]

| Piece | Comes from | Purpose |
|---|---|---|
| `Context<T>` | Reactor | Carries one service, or the whole service provider, down the tree. |
| `.Provide(context, value)` | Reactor modifier | Makes a value available to an element's subtree. |
| `UseContext(context)` | Reactor hook | Reads the nearest provided value, or the context's default. |
| `UseMemo`, `UseEffect` | Reactor hooks | Keep a resolved service stable. Dispose a scope on unmount. |
| `UseExternalStore` | Reactor hook | Re-renders a component when a service reports a change. |
| `ServiceCollection`, `BuildServiceProvider` | `Microsoft.Extensions.DependencyInjection` package | Register services and build the container at startup. |
| `CreateScope()` | `Microsoft.Extensions.DependencyInjection` package | Creates a child provider for scoped services. |
| `UseService<T>()`, `UseServiceScope()` | Your app (shown on this page) | Small custom hooks built from the Reactor hooks above. |

Pick the lightest approach that works:

- **A context per service.** Good for one to three services with no
  lifetimes to manage.
- **A container in a context.** Good when you have many services, need
  scoped lifetimes, or share registrations with other .NET projects.
- **A prop.** Best when a parent already has the value and only its child
  needs it. See [when not to use DI](#when-not-to-use-di).

## Provide a plain service through context [#provide-a-plain-service-through-context]

A context can hold any value, including an interface. Give the context the
real service as its default value. Production code then needs no provider at
all, and any component can read the service with `UseContext`:

```csharp
interface IClock
{
    DateTimeOffset Now { get; }
}

sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}

static class AppContexts
{
    // The default value is the real service, so no provider is needed in production.
    public static readonly Context<IClock> Clock = new(new SystemClock());
}

class Greeting : Component
{
    public override Element Render()
    {
        var clock = UseContext(AppContexts.Clock);
        var hour = clock.Now.Hour;
        var part = hour < 12 ? "morning" : hour < 18 ? "afternoon" : "evening";
        return TextBlock($"Good {part}.");
    }
}
```

To replace the service, provide a different value for a subtree. The
`Greeting` component does not change:

```csharp
sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now { get; } = now;
}

class GreetingPreview : Component
{
    // A static instance keeps the provided value stable across renders.
    private static readonly IClock Morning =
        new FixedClock(new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero));

    public override Element Render() =>
        VStack(Component<Greeting>()).Provide(AppContexts.Clock, Morning);
}
```

The fake is a static field, so the provided value keeps the same identity on
every render. A new object on every render re-renders every consumer. See
[stabilizing the provided value](/microsoft-ui-reactor/main/guides/context#stabilizing-the-provided-value).

## Wire up a dependency injection container [#wire-up-a-dependency-injection-container]

When you have many services, register them in a container instead of
creating one context each. This page uses
`Microsoft.Extensions.DependencyInjection`, the standard .NET container. Add
the package to your app project:

```shell
dotnet add package Microsoft.Extensions.DependencyInjection
```

Create one context that carries the provider. Its default is `null`, so a
missing provider becomes a clear error instead of a silent fallback:

```csharp
static class AppServices
{
    public static readonly Context<IServiceProvider?> Provider = new(null);
}
```

Build the container once, before `ReactorApp.Run`, and provide it on the root
element:

```csharp
var services = new ServiceCollection();
services.AddSingleton<IClock, SystemClock>();
services.AddSingleton<ITodoStore, InMemoryTodoStore>();
services.AddScoped<IEditSession, EditSession>();

// validateScopes makes the root provider refuse to hand out scoped services.
var provider = services.BuildServiceProvider(validateScopes: true);

ReactorApp.Run("Services", ctx =>
    VStack(Component<ServicesShell>()).Provide(AppServices.Provider, provider));
```

Every component under the root can now reach the provider. The diagram shows
the tree the rest of this page builds:

![Startup builds the service provider and the root element provides it through context. TodoList resolves a singleton store. EditorPanel creates a scope and provides the scoped provider to EditorBody, which resolves a scoped edit session. The scope is created when EditorPanel mounts and disposed when it unmounts.](/microsoft-ui-reactor/main/images/services/provider-tree.svg)

`validateScopes: true` tells the container to throw when code resolves a
scoped service from the root provider. Turn it on so lifetime mistakes fail
right away.

## Write a UseService hook [#write-a-useservice-hook]

A custom hook hides the context lookup and the resolve call. It is a
`RenderContext` extension method whose name starts with `Use`, built only
from other hooks:

```csharp
static class ServiceHooks
{
    public static IServiceProvider UseServiceProvider(this RenderContext ctx) =>
        ctx.UseContext(AppServices.Provider)
        ?? throw new InvalidOperationException(
            "No IServiceProvider found. Wrap the tree in .Provide(AppServices.Provider, provider).");

    public static T UseService<T>(this RenderContext ctx) where T : notnull
    {
        var provider = ctx.UseServiceProvider();
        // Resolve once per provider, not on every render.
        return ctx.UseMemo(() => provider.GetRequiredService<T>(), provider);
    }
}
```

- `UseServiceProvider` reads the context and throws a clear error when no
  provider is in scope.
- `UseService<T>` wraps `GetRequiredService<T>()` in `UseMemo`, keyed on the
  provider. A transient service is resolved once per component, not once per
  render. If a nested scope supplies a different provider, the service is
  resolved again.
- Both hooks call their inner hooks unconditionally, in the same order on
  every render, so they follow the [hook rules](/microsoft-ui-reactor/main/guides/hooks#hook-rules).

Class components cannot call `RenderContext` extension methods directly.
Wrap the body in `Memo(ctx => ...)`, the same convention as other
[custom hooks](/microsoft-ui-reactor/main/guides/hooks#custom-hooks).

## Services whose data changes [#services-whose-data-changes]

Resolving a service does not make a component re-render when the service's
data changes. If a service holds data that changes, give it a subscription
and read it with `UseExternalStore`. Here is a small to-do store:

```csharp
record TodoItem(int Id, string Title);

interface ITodoStore
{
    IReadOnlyList<TodoItem> Items { get; }
    Action Subscribe(Action onChange);
    void Add(string title);
}

sealed class InMemoryTodoStore : ITodoStore
{
    private readonly List<Action> _listeners = [];
    private int _nextId = 1;

    // Replaced, never mutated, so each change produces a new snapshot.
    public IReadOnlyList<TodoItem> Items { get; private set; } = [];

    public Action Subscribe(Action onChange)
    {
        _listeners.Add(onChange);
        return () => _listeners.Remove(onChange);
    }

    public void Add(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        Items = [.. Items, new TodoItem(_nextId++, title.Trim())];
        foreach (var listener in _listeners.ToArray()) listener();
    }
}
```

The component resolves the store, then subscribes to it:

```csharp
class TodoList : Component
{
    public override Element Render() => Memo(ctx =>
    {
        var store = ctx.UseService<ITodoStore>();
        var items = ctx.UseExternalStore(store.Subscribe, () => store.Items);
        var (draft, setDraft) = ctx.UseState("");

        return VStack(8,
            HStack(8,
                TextBox(draft, setDraft, placeholderText: "New task", header: "Task"),
                Button("Add", () =>
                {
                    store.Add(draft);
                    setDraft("");
                })
            ),
            Component<CountBadge, int>(items.Count),
            VStack(4, items.Select(item => TextBlock(item.Title).WithKey(item.Id.ToString())).ToArray())
        );
    });
}
```

Two rules from [external stores](/microsoft-ui-reactor/main/guides/hooks#external-stores) apply:

- Pass a stable `subscribe` delegate. A method group such as
  `store.Subscribe` is stable.
- Return a snapshot that changes identity only when the data changes. The
  store replaces its list on every change and never mutates it.

The example store is not thread-safe. Call it from the UI thread, or add
locking. For work that runs on background threads, see
[Threading and dispatch](/microsoft-ui-reactor/main/how-it-works/threading-and-dispatch).

## Scoped and disposable services [#scoped-and-disposable-services]

`Microsoft.Extensions.DependencyInjection` has three lifetimes:

| Lifetime | Registered with | One instance per | Disposed when |
|---|---|---|---|
| Singleton | `AddSingleton` | root provider | the root provider is disposed |
| Scoped | `AddScoped` | scope | the scope is disposed |
| Transient | `AddTransient` | resolve call | the scope or root that resolved it is disposed |

Reactor does not know about the container, so it never creates or disposes a
scope for you. A component can own a scope with two hooks: `UseMemo` creates
the scope on the first render, and the cleanup of `UseEffect` disposes it.
The component then provides the scoped provider to its children:

```csharp
interface IEditSession
{
    Guid Id { get; }
}

sealed class EditSession : IEditSession, IDisposable
{
    public Guid Id { get; } = Guid.NewGuid();
    public void Dispose() => Debug.WriteLine($"Edit session {Id} disposed");
}

static class ScopeHooks
{
    public static IServiceProvider UseServiceScope(this RenderContext ctx)
    {
        var parent = ctx.UseServiceProvider();
        var scope = ctx.UseMemo(() => parent.CreateScope(), parent);
        // The cleanup runs on unmount, or before a new scope replaces this one.
        ctx.UseEffect(() => () => scope.Dispose(), scope);
        return scope.ServiceProvider;
    }
}

class EditorPanel : Component
{
    public override Element Render() => Memo(ctx =>
    {
        var scoped = ctx.UseServiceScope();
        return VStack(Component<EditorBody>()).Provide(AppServices.Provider, scoped);
    });
}

class EditorBody : Component
{
    public override Element Render() => Memo(ctx =>
    {
        var session = ctx.UseService<IEditSession>();
        return Caption($"Edit session {session.Id:N}");
    });
}
```

Every component under `EditorPanel` that resolves `IEditSession` gets the same
instance. Toggle the panel to see the lifetime. Closing it unmounts
`EditorPanel`, the effect cleanup disposes the scope, and the scope disposes
the `EditSession`. Opening it again creates a new scope and a new session ID:

```csharp
class ScopedDemo : Component
{
    public override Element Render()
    {
        var (open, setOpen) = UseState(false);
        return VStack(8,
            Button(open ? "Close editor" : "Open editor", () => setOpen(!open)),
            open ? Component<EditorPanel>() : null
        );
    }
}
```

The root provider usually lives as long as the app. Reactor does not dispose
it. Treat singletons as app-lifetime objects, and do not rely on their
`Dispose` running at exit.

## Swap in fakes for tests and previews [#swap-in-fakes-for-tests-and-previews]

Because components only see the provider through context, a test or a
preview can provide a different one. The component code stays the same:

```csharp
sealed class FakeTodoStore(params string[] titles) : ITodoStore
{
    public IReadOnlyList<TodoItem> Items { get; } =
        [.. titles.Select((title, i) => new TodoItem(i + 1, title))];

    public List<string> Added { get; } = [];

    public Action Subscribe(Action onChange) => () => { };
    public void Add(string title) => Added.Add(title);
}

static class TestServices
{
    public static IServiceProvider Create(ITodoStore store)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new FixedClock(new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero)));
        services.AddSingleton(store);
        services.AddScoped<IEditSession, EditSession>();
        return services.BuildServiceProvider(validateScopes: true);
    }
}

class TodoListPreview : Component
{
    private static readonly IServiceProvider Services =
        TestServices.Create(new FakeTodoStore("Write the docs", "Review the PR"));

    public override Element Render() =>
        VStack(Component<TodoList>()).Provide(AppServices.Provider, Services);
}
```

- **Test logic in the services.** A store, a parser or an API client is plain
  C#. Test it with ordinary unit tests. It needs no Reactor at all.
- **Keep components thin.** A component that resolves services renders
  through `Memo`, and the `Memo` body runs only under the reconciler. Calling
  `Render()` directly returns a `MemoElement` and does not run the body.
- **Render the real tree under a fake provider** in a self-test or a preview
  window, as `TodoListPreview` does. See [Testing](/microsoft-ui-reactor/main/guides/testing) for the test
  tiers, and the
  [mock provider pattern](/microsoft-ui-reactor/main/guides/context#testing-with-a-mock-provider) on the
  context page.

## When not to use DI [#when-not-to-use-di]

Not every dependency is a service. If a parent already has the value and
only its child needs it, pass it as a prop:

```csharp
// CountBadge only needs a number. It takes a prop, not a service.
class CountBadge : Component<int>
{
    public override Element Render() =>
        Caption(Props == 1 ? "1 task" : $"{Props} tasks");
}
```

`TodoList` resolves the store and passes `items.Count` to `CountBadge`.
`CountBadge` works in any tree, with or without a provider, and is easy to
reuse. Use a service when many distant components need the same object, or
when the object owns resources or state that outlives a single component.
Use [components](/microsoft-ui-reactor/main/guides/components) and props for everything else.

> **Caveat:** Reactor has no built-in container. Every service you resolve comes from a
> context you provide, so three mistakes cause most problems. First, building
> the container inside `Render()` creates new singletons on every render and
> loses their state. Build it once at startup. Second, calling
> `GetRequiredService<T>()` in render without `UseMemo` creates a new transient
> on every render. Third, a scope created in render must be disposed in a
> `UseEffect` cleanup, or its disposable services are never disposed. With `validateScopes: true`, resolving a scoped service from
> the root provider throws `InvalidOperationException`. That error means a
> component is missing a scope above it, not that the registration is wrong.

## Patterns [#patterns]

### One context per service for small apps [#one-context-per-service-for-small-apps]

Two or three services, each with a real default, need no container. Each
context reads like a typed global that tests can override. This is the
[plain service](#provide-a-plain-service-through-context) shape.

### One container at the root for larger apps [#one-container-at-the-root-for-larger-apps]

Register everything in one `ServiceCollection`, provide the provider at the
root, and resolve with `UseService<T>()`. Registrations can be shared with
other .NET projects, such as a class library or a command-line tool, that use
the same container.

### One scope per document or editor [#one-scope-per-document-or-editor]

Give each long-lived view its own scope with `UseServiceScope()`, as
`EditorPanel` does. Services such as an undo stack or an edit session then
live exactly as long as the view. A [window](/microsoft-ui-reactor/main/guides/windows) root can own a scope
the same way.

### Resolve high, pass props low [#resolve-high-pass-props-low]

Resolve services near the top of a feature, then pass plain data and
callbacks to small child components as props. Leaf components stay simple
and reusable.

## Common Mistakes [#common-mistakes]

### Building the container inside Render [#building-the-container-inside-render]

```csharp
// Avoid: building the container inside Render.
class RebuildsServicesEveryRender : Component
{
    public override Element Render()
    {
        // A new provider on every render: new singletons, lost state,
        // and every consumer below re-renders.
        var services = new ServiceCollection();
        services.AddSingleton<ITodoStore, InMemoryTodoStore>();
        var provider = services.BuildServiceProvider();
        return VStack(Component<TodoList>()).Provide(AppServices.Provider, provider);
    }
}
```

Every render builds a new container, new singletons and a new provider. The
provided value changes identity, so every consumer re-renders, and the store
loses its items. Build the provider once at startup, as in
[the startup code](#wire-up-a-dependency-injection-container).

### Resolving services in every leaf [#resolving-services-in-every-leaf]

```csharp
// Avoid: resolving services in every small leaf component.
class TodoRow : Component<TodoItem>
{
    public override Element Render() => Memo(ctx =>
    {
        // Each row now needs a provider in context just to show a title.
        var clock = ctx.UseService<IClock>();
        return TextBlock($"{Props.Title} ({clock.Now:t})");
    }, Props);
}
```

`TodoRow` resolves a service only to format one line. It can no longer render
without a provider in context, and every row adds hook slots. Resolve once in
the parent and pass the value as a prop.

### Resolving a scoped service from the root [#resolving-a-scoped-service-from-the-root]

With `validateScopes: true`, `UseService<IEditSession>()` throws when no
component above it created a scope. Without the flag, the root provider
returns one instance that it keeps for the whole app, which is rarely what a
scoped registration means. Add a component that calls `UseServiceScope()`
above the consumer.

### Forgetting to subscribe to changes [#forgetting-to-subscribe-to-changes]

A component that resolves a store and reads `store.Items` directly shows the
first snapshot and never updates. Read changing data through
`UseExternalStore`, as `TodoList` does.

## Tips [#tips]

**Say it plainly in code.** Name the context `AppServices.Provider` and the
hook `UseService<T>()`. Readers then know where a dependency comes from
without searching.

**Give plain service contexts a real default.** Components then work without
a provider in production, and tests provide only what they replace.

**Keep the provided value stable.** Provide a provider, scope or service
object that you created once. Never create it inline in `Render()`.

**Turn on container validation.** `validateScopes: true` catches
lifetime mistakes at the first resolve. `BuildServiceProvider` also has a
`ServiceProviderOptions` overload with `ValidateOnBuild`, which checks that
every registration can be constructed.

**Prefer props for data.** A service is an object with behaviour. A count, a
title or a selected ID is data. Pass data as props.

## Next Steps [#next-steps]

- **[Context](/microsoft-ui-reactor/main/guides/context)** — how `Context<T>`, `.Provide` and `UseContext` work
- **[Hooks](/microsoft-ui-reactor/main/guides/hooks)** — `UseMemo`, `UseEffect`, `UseExternalStore` and custom hooks
- **[Testing](/microsoft-ui-reactor/main/guides/testing)** — test tiers, and where a fake provider fits
- **[Persistence](/microsoft-ui-reactor/main/guides/persistence)** — save and load app state
- **[Threading and dispatch](/microsoft-ui-reactor/main/how-it-works/threading-and-dispatch)** — call services from background work safely
