using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// <snippet:startup>
var services = new ServiceCollection();
services.AddSingleton<IClock, SystemClock>();
services.AddSingleton<ITodoStore, InMemoryTodoStore>();
services.AddScoped<IEditSession, EditSession>();

// validateScopes makes the root provider refuse to hand out scoped services.
var provider = services.BuildServiceProvider(validateScopes: true);

ReactorApp.Run("Services", ctx =>
    VStack(Component<ServicesShell>()).Provide(AppServices.Provider, provider));
// </snippet:startup>

class ServicesShell : Component
{
    public override Element Render() =>
        VStack(24,
            Component<Greeting>(),
            Component<GreetingPreview>(),
            Component<TodoList>(),
            Component<TodoListPreview>(),
            Component<ScopedDemo>()
        ).Padding(24);
}

// <snippet:plain-service>
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
// </snippet:plain-service>

// <snippet:plain-service-fake>
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
// </snippet:plain-service-fake>

// <snippet:todo-store>
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
// </snippet:todo-store>

// <snippet:service-context>
static class AppServices
{
    public static readonly Context<IServiceProvider?> Provider = new(null);
}
// </snippet:service-context>

// <snippet:use-service>
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
// </snippet:use-service>

// <snippet:consume-service>
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
// </snippet:consume-service>

// <snippet:props-instead>
// CountBadge only needs a number. It takes a prop, not a service.
class CountBadge : Component<int>
{
    public override Element Render() =>
        Caption(Props == 1 ? "1 task" : $"{Props} tasks");
}
// </snippet:props-instead>

// <snippet:scoped-service>
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
// </snippet:scoped-service>

// <snippet:scoped-toggle>
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
// </snippet:scoped-toggle>

// <snippet:fake-services>
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
// </snippet:fake-services>

// <snippet:build-in-render-dont>
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
// </snippet:build-in-render-dont>

// <snippet:leaf-resolves-dont>
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
// </snippet:leaf-resolves-dont>
