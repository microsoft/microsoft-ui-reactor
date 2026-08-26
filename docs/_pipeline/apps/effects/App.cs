using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<EffectsApp>("Effects and Lifecycle", width: 600, height: 550
);

// <snippet:mount-effect>
class MountEffectExample : Component
{
    public override Element Render()
    {
        var (loadedAt, setLoadedAt) = UseState("");

        UseEffect(() =>
        {
            setLoadedAt(DateTime.Now.ToString("HH:mm:ss"));
        }, Array.Empty<object>());

        return VStack(8,
            TextBlock("Component mounted at:"),
            TextBlock(loadedAt).FontSize(20).Bold()
        ).Padding(24);
    }
}
// </snippet:mount-effect>

// <snippet:dependency-effect>
class DependencyEffectExample : Component
{
    public override Element Render()
    {
        var (query, setQuery) = UseState("");
        var (results, setResults) = UseState("Type to search...");

        UseEffect(() =>
        {
            if (string.IsNullOrWhiteSpace(query))
                setResults("Type to search...");
            else
                setResults($"Found 3 results for \"{query}\"");
        }, query);

        return VStack(12,
            TextBox(query, setQuery, placeholderText: "Search...").Width(300),
            TextBlock(results).Foreground(Theme.SecondaryText)
        ).Padding(24);
    }
}
// </snippet:dependency-effect>

// <snippet:timer-cleanup>
class TimerCleanupExample : Component
{
    public override Element Render()
    {
        var (seconds, updateSeconds) = UseReducer(0);
        var (isRunning, setIsRunning) = UseState(false);

        UseEffect(() =>
        {
            if (!isRunning) return () => { };
            var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            var cts = new CancellationTokenSource();
            var token = cts.Token;   // capture once — cleanup disposes cts
            _ = Task.Run(async () =>
            {
                try
                {
                    while (await timer.WaitForNextTickAsync(token))
                        updateSeconds(s => s + 1);
                }
                catch (OperationCanceledException) { /* expected on cleanup */ }
            });
            return () => { cts.Cancel(); timer.Dispose(); };
        }, isRunning);

        return VStack(12,
            TextBlock($"Elapsed: {seconds}s").FontSize(24).Bold(),
            HStack(8,
                Button(isRunning ? "Stop" : "Start", () => setIsRunning(!isRunning)),
                Button("Reset", () => updateSeconds(_ => 0))
            )
        ).Padding(24);
    }
}
// </snippet:timer-cleanup>

// <snippet:async-loading>
class AsyncLoadingExample : Component
{
    public override Element Render()
    {
        var (items, setItems) = UseState<string[]?>(null);

        UseEffect(() =>
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500); // simulate network call
                setItems(new[] { "Alice", "Bob", "Charlie" });
            });
        }, Array.Empty<object>());

        if (items is null)
            return TextBlock("Loading...").Padding(24);

        return VStack(8,
            Heading("Loaded Users"),
            VStack(4, items.Select(name => TextBlock(name)).ToArray())
        ).Padding(24);
    }
}
// </snippet:async-loading>

// <snippet:infinite-loop-warning>
class InfiniteLoopWarning : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        // BAD: this creates an infinite loop!
        // UseEffect(() => { setCount(count + 1); }, count);

        // GOOD: guard with a condition
        UseEffect(() =>
        {
            if (count < 5) setCount(count + 1);
        }, count);

        return TextBlock($"Count stopped at: {count}").Padding(24);
    }
}
// </snippet:infinite-loop-warning>

// <snippet:fetch-cancellation>
class FetchCancellationExample : Component
{
    static Task<string[]> FetchAsync(string q, CancellationToken ct) =>
        Task.FromResult(new[] { $"{q} result" });

    public override Element Render()
    {
        var (query, setQuery) = UseState("reactor");
        var (items, setItems) = UseState(Array.Empty<string>());

        UseEffect(() =>
        {
            var cts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try
                {
                    var data = await FetchAsync(query, cts.Token);
                    setItems(data);
                }
                catch (OperationCanceledException) { /* expected */ }
            });
            return () => { cts.Cancel(); };
        }, query);

        return VStack(8,
            TextBox(query, setQuery).Width(250),
            ForEach(items, i => TextBlock(i))
        ).Padding(24);
    }
}
// </snippet:fetch-cancellation>

// <snippet:subscription-cleanup>
class EventSource
{
    public event EventHandler? Changed;
    public void Fire() => Changed?.Invoke(this, EventArgs.Empty);
}

class SubscriptionCleanupExample : Component
{
    static readonly EventSource Source = new();

    public override Element Render()
    {
        var (tick, updateTick) = UseReducer(0);

        UseEffect(() =>
        {
            void Handler(object? s, EventArgs e) => updateTick(t => t + 1);
            Source.Changed += Handler;
            return () => Source.Changed -= Handler;
        }, Source); // re-attach if Source identity changes

        return VStack(8,
            TextBlock($"Ticks: {tick}"),
            Button("Fire", Source.Fire)
        ).Padding(24);
    }
}
// </snippet:subscription-cleanup>

// <snippet:deps-literal-dont>
class DepsLiteralDontExample : Component
{
    static Task FetchAsync(object options) => Task.CompletedTask;

    public override Element Render()
    {
        var (url, setUrl) = UseState("https://example.test");

        // Don't — `options` is a fresh anonymous object on every render, so the
        // effect re-runs every commit and the fetch fires in a loop.
        var options = new { Url = url, Limit = 10 };
        UseEffect(() => FetchAsync(options), options);

        return TextBox(url, setUrl).Width(250).Padding(24);
    }
}
// </snippet:deps-literal-dont>

// <snippet:deps-literal-do>
class DepsLiteralDoExample : Component
{
    static Task FetchAsync(string url, int limit) => Task.CompletedTask;

    public override Element Render()
    {
        var (url, setUrl) = UseState("https://example.test");

        // Do — pass the primitives, which compare by value.
        UseEffect(() => FetchAsync(url, 10), url);

        return TextBox(url, setUrl).Width(250).Padding(24);
    }
}
// </snippet:deps-literal-do>

// <snippet:missing-cleanup-dont>
class MissingCleanupDontExample : Component
{
    public override Element Render()
    {
        var (tick, updateTick) = UseReducer(0);

        // Don't — no cleanup, so the timer fires forever after unmount.
        UseEffect(() =>
        {
            var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            _ = Task.Run(async () =>
            {
                while (await timer.WaitForNextTickAsync())
                    updateTick(t => t + 1);
            });
        }, Array.Empty<object>());

        return TextBlock($"Ticks: {tick}").Padding(24);
    }
}
// </snippet:missing-cleanup-dont>

// <snippet:missing-cleanup-do>
class MissingCleanupDoExample : Component
{
    public override Element Render()
    {
        var (tick, updateTick) = UseReducer(0);

        UseEffect(() =>
        {
            var cts = new CancellationTokenSource();
            var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            var token = cts.Token;   // capture once — cleanup disposes cts
            _ = Task.Run(async () =>
            {
                try
                {
                    while (await timer.WaitForNextTickAsync(token))
                        updateTick(t => t + 1);
                }
                catch (OperationCanceledException) { /* expected on unmount */ }
            });
            // Cancel the source and dispose the timer; do NOT dispose the
            // source. The fire-and-forget worker shares ownership of it, and
            // CancellationTokenSource.Dispose is not safe alongside concurrent
            // member access — a call still registering against the token would
            // see ObjectDisposedException. A CTS with no timer holds no
            // unmanaged resource, so dropping the reference is enough.
            return () => { cts.Cancel(); timer.Dispose(); };
        }, Array.Empty<object>());

        return TextBlock($"Ticks: {tick}").Padding(24);
    }
}
// </snippet:missing-cleanup-do>

// <snippet:effect-vs-memo-dont>
class EffectVsMemoDontExample : Component
{
    public override Element Render()
    {
        var (first, setFirst) = UseState("Ada");
        var (last, setLast) = UseState("Lovelace");

        // Don't — this pays for two extra renders just to derive a string.
        var (full, setFull) = UseState("");
        UseEffect(() => setFull($"{first} {last}"), first, last);

        return VStack(8,
            TextBox(first, setFirst).Width(150),
            TextBox(last, setLast).Width(150),
            TextBlock(full)
        ).Padding(24);
    }
}
// </snippet:effect-vs-memo-dont>

// <snippet:effect-vs-memo-do>
class EffectVsMemoDoExample : Component
{
    static string Compute(string input) => input.ToUpperInvariant();

    public override Element Render()
    {
        var (first, setFirst) = UseState("Ada");
        var (last, setLast) = UseState("Lovelace");

        var full = $"{first} {last}";                        // inline
        var stats = UseMemo(() => Compute(full), full);      // memoized when expensive

        return VStack(8,
            TextBox(first, setFirst).Width(150),
            TextBox(last, setLast).Width(150),
            TextBlock(full),
            TextBlock(stats)
        ).Padding(24);
    }
}
// </snippet:effect-vs-memo-do>
// Main app
class EffectsApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Effects and Lifecycle"),
                Component<MountEffectExample>(),
                Component<DependencyEffectExample>(),
                Component<TimerCleanupExample>(),
                Component<AsyncLoadingExample>()
            ).Padding(24)
        );
    }
}
