using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<EffectsApp>("Effects and Lifecycle", width: 600, height: 550
#if DEBUG
    , preview: true
#endif
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
            _ = Task.Run(async () =>
            {
                while (await timer.WaitForNextTickAsync(cts.Token))
                    updateSeconds(s => s + 1);
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
