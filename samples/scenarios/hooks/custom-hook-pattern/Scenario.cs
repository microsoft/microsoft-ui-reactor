// id: custom-hook-pattern
// intent: compose hooks into a reusable custom Use* extension that debounces input
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Custom hooks should keep the Use* naming convention and compose other hooks internally.
ReactorApp.Run<App>("CustomHookPattern", width: 400, height: 200);

class App : Component
{
    public override Element Render() => RenderEachTime(UseDebouncedView);

    static Element UseDebouncedView(RenderContext ctx)
    {
        var (query, setQuery) = ctx.UseState("");
        var debounced = ctx.UseDebouncedValue(query, 400);

        return VStack(12,
            TextBox(query, setQuery, "Type quickly", header: "Live query"),
            TextBlock($"Immediate: {query}"),
            TextBlock($"Debounced: {debounced}"));
    }
}

static class DebounceHooks
{
    public static T UseDebouncedValue<T>(this RenderContext ctx, T value, int delayMs)
    {
        var (debounced, setDebounced) = ctx.UseState(value);
        ctx.UseEffect(() =>
        {
            var cts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs, cts.Token);
                    setDebounced(value);
                }
                catch (TaskCanceledException)
                {
                }
            });
            return () => cts.Cancel();
        }, value!, delayMs);
        return debounced;
    }
}
