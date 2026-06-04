using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<AsyncResourcesCookbookApp>("Async Resources Cookbook", width: 600, height: 600
);

// Stand-in "API" so each snippet can demonstrate a real fetch without depending
// on a backend. Deterministic delays keep screenshots repeatable.
static class DemoApi
{
    public sealed record User(string Name, string Role);

    public static async Task<User> GetUserAsync(int id, CancellationToken ct)
    {
        await Task.Delay(350, ct);
        return new User($"User #{id}", id % 2 == 0 ? "Editor" : "Viewer");
    }

    public sealed record Commit(string Sha, string Message);

    public static async Task<(IReadOnlyList<Commit> Items, string? NextCursor, int? Total)>
        GetCommitsPageAsync(string? cursor, CancellationToken ct)
    {
        await Task.Delay(200, ct);
        int start = cursor is null ? 0 : int.Parse(cursor);
        const int PageSize = 20;
        var items = Enumerable.Range(start, PageSize)
            .Select(i => new Commit($"sha{i:D4}", $"Commit message {i}"))
            .ToList();
        string? next = start + PageSize >= 200 ? null : (start + PageSize).ToString();
        return (items, next, 200);
    }

    public sealed record Todo(string Title, bool IsTemporary = false);
    public sealed record TodoInput(string Title);

    public static async Task<Todo> AddTodoAsync(TodoInput input, CancellationToken ct)
    {
        await Task.Delay(400, ct);
        return new Todo(input.Title, IsTemporary: false);
    }
}

// <snippet:before-useresource>
class BeforeUseResourceExample : Component
{
    public override Element Render()
    {
        var (data, setData) = UseState<DemoApi.User?>(null);
        var (error, setError) = UseState<Exception?>(null);
        var (loading, setLoading) = UseState(true);

        UseEffect(() =>
        {
            var cts = new CancellationTokenSource();
            setLoading(true);
            _ = Task.Run(async () =>
            {
                try
                {
                    var u = await DemoApi.GetUserAsync(42, cts.Token);
                    if (!cts.IsCancellationRequested)
                    {
                        setData(u);
                        setError(null);
                        setLoading(false);
                    }
                }
                catch (OperationCanceledException) { /* swallow */ }
                catch (Exception ex)
                {
                    if (!cts.IsCancellationRequested)
                    {
                        setError(ex);
                        setLoading(false);
                    }
                }
            });
            return () => cts.Cancel();
        }, 42);

        if (loading) return (Element)TextBlock("Loading…").Padding(24);
        if (error is not null) return (Element)TextBlock($"Error: {error.Message}").Padding(24);
        return VStack(4,
            Heading("Before: manual plumbing").FontSize(14),
            TextBlock(data?.Name ?? "(none)").FontSize(20).Bold(),
            TextBlock(data?.Role ?? "").Opacity(0.6)
        ).Padding(24);
    }
}
// </snippet:before-useresource>

// <snippet:after-useresource>
class AfterUseResourceExample : Component
{
    public override Element Render()
    {
        var user = UseResource(
            ct => DemoApi.GetUserAsync(42, ct),
            deps: new object[] { 42 });

        return user.Match<Element>(
            loading: () => TextBlock("Loading…").Padding(24),
            data: u => VStack(4,
                Heading("After: one hook").FontSize(14),
                TextBlock(u.Name).FontSize(20).Bold(),
                TextBlock(u.Role).Opacity(0.6)
            ).Padding(24),
            error: ex => TextBlock($"Error: {ex.Message}").Padding(24));
    }
}
// </snippet:after-useresource>

// <snippet:infinite-scroll>
class InfiniteScrollExample : Component
{
    public override Element Render()
    {
        var commits = UseInfiniteResource<DemoApi.Commit, string>(
            fetchPage: async (cursor, ct) =>
            {
                var (items, next, total) = await DemoApi.GetCommitsPageAsync(cursor, ct);
                return new Page<DemoApi.Commit, string>(items, next, total);
            },
            deps: new object[] { "repo-main" });

        // With a real virtualizer, drive fetches from ItemAt inside VirtualList's
        // renderItem. The important bit: null return = placeholder row.
        return VStack(4,
            Heading($"Commits ({commits.TotalCount ?? 0})").FontSize(14),
            VStack(2,
                Enumerable.Range(0, Math.Min(commits.Items.Count, 6))
                    .Select(i =>
                    {
                        var commit = commits.ItemAt(i);
                        return commit is null
                            ? TextBlock("…").Opacity(0.4).Padding(4)
                            : TextBlock($"{commit.Sha} — {commit.Message}").Padding(4);
                    })
                    .ToArray())
        ).Padding(24);
    }
}
// </snippet:infinite-scroll>

// <snippet:use-mutation>
class UseMutationExample : Component
{
    public override Element Render()
    {
        var (todos, setTodos) = UseState<IReadOnlyList<DemoApi.Todo>>(Array.Empty<DemoApi.Todo>());

        var mutation = UseMutation<DemoApi.TodoInput, DemoApi.Todo>(
            mutator: (input, ct) => DemoApi.AddTodoAsync(input, ct),
            options: new MutationOptions<DemoApi.TodoInput, DemoApi.Todo>(
                OnOptimistic: input =>
                    setTodos([.. todos, new DemoApi.Todo(input.Title, IsTemporary: true)]),
                OnSuccess: (todo, _) =>
                    setTodos([.. todos.Select(t => t.IsTemporary ? todo : t)]),
                OnError: (_, input) =>
                    setTodos([.. todos.Where(t => t.Title != input.Title)]),
                InvalidateKeys: ["todos/list"]));

        return VStack(8,
            Heading($"Todos ({todos.Count})").FontSize(14),
            VStack(2, todos.Select(t =>
                TextBlock(t.Title + (t.IsTemporary ? " (saving…)" : ""))
                    .Opacity(t.IsTemporary ? 0.5 : 1.0)).ToArray()),
            Button("Add Todo",
                () => _ = mutation.RunAsync(new DemoApi.TodoInput($"Item {todos.Count + 1}")))
        ).Padding(24);
    }
}
// </snippet:use-mutation>

// <snippet:pending-fallback>
class PendingFallbackExample : Component
{
    public override Element Render()
    {
        return PendingFactory.Pending(
            fallback: TextBlock("Loading dashboard…").Opacity(0.5).Padding(24),
            child: VStack(8,
                Heading("Dashboard").FontSize(14),
                Component<UserHeader>(),
                Component<RecentActivity>(),
                Component<Stats>()
            ).Padding(24));
    }

    private class UserHeader : Component
    {
        public override Element Render()
        {
            var user = UseResource(
                ct => DemoApi.GetUserAsync(1, ct),
                deps: new object[] { "user-1" });
            return user.Match<Element>(
                loading: () => TextBlock("• user loading…").Opacity(0.5),
                data: u => TextBlock($"• {u.Name} ({u.Role})"),
                error: ex => TextBlock($"• error: {ex.Message}"));
        }
    }

    private class RecentActivity : Component
    {
        public override Element Render()
        {
            var feed = UseInfiniteResource<DemoApi.Commit, string>(
                fetchPage: async (cursor, ct) =>
                {
                    var (items, next, total) = await DemoApi.GetCommitsPageAsync(cursor, ct);
                    return new Page<DemoApi.Commit, string>(items, next, total);
                },
                deps: new object[] { "feed" });
            return TextBlock($"• {feed.Items.Count} recent items");
        }
    }

    private class Stats : Component
    {
        public override Element Render()
        {
            var user = UseResource(
                ct => DemoApi.GetUserAsync(99, ct),
                deps: new object[] { "stats" });
            return user.Match(
                loading: () => TextBlock("• stats loading…").Opacity(0.5),
                data: _ => TextBlock("• stats ready"),
                error: ex => TextBlock($"• error: {ex.Message}"));
        }
    }
}
// </snippet:pending-fallback>

class AsyncResourcesCookbookApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(16,
                Heading("Async Resources Cookbook"),
                Component<AfterUseResourceExample>(),
                Component<InfiniteScrollExample>(),
                Component<UseMutationExample>(),
                Component<PendingFallbackExample>()
            ).Padding(16)
        );
    }
}
