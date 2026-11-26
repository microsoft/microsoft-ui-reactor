using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Hooks;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<DevToolingApp>("Dev Tooling Demo");

// <snippet:preview-app>
class DevToolingApp : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        var (message, setMessage) = UseState("Edit this code and save!");

        return VStack(16,
            Heading("Preview Mode Demo"),
            TextBlock(message).FontSize(16),
            HStack(8,
                Button("Click me", () => setCount(count + 1)),
                TextBlock($"Clicked {count} times").SemiBold()
            ),
            TextBox(message, setMessage, placeholderText: "Type something",
                header: "Message")
                .Width(300)
        ).Padding(24);
    }
}
// </snippet:preview-app>

// <snippet:entry-point>
// Program entry point — this is the entire App.cs file:
// ReactorApp.Run<DevToolingApp>("Dev Tooling Demo");
//
// Hot reload works when the app is launched under dotnet watch. Inspect the
// running app with the WinUI DevTools: winapp run . --devtools on
// </snippet:entry-point>

// <snippet:function-entry>
// Alternative: inline function component, no class needed
// ReactorApp.Run("Quick Test", ctx =>
// {
//     var (n, setN) = ctx.UseState(0);
//     return VStack(12,
//         TextBlock($"Count: {n}").FontSize(20),
//         Button("+1", () => setN(n + 1))
//     ).Padding(24);
// });
// </snippet:function-entry>

// <snippet:iteration-demo>
class IterationDemo : Component
{
    public override Element Render()
    {
        var (items, updateItems) = UseReducer(new List<string>());
        var (input, setInput) = UseState("");

        return VStack(12,
            Heading("Iteration Cycle Demo"),
            TextBlock("Add items, then edit this code and save to see hot reload."),
            HStack(8,
                TextBox(input, setInput, placeholderText: "New item",
                    header: "New item")
                    .Width(200),
                Button("Add", () =>
                {
                    if (!string.IsNullOrWhiteSpace(input))
                    {
                        updateItems(list =>
                        {
                            var next = new List<string>(list) { input };
                            return next;
                        });
                        setInput("");
                    }
                })
            ),
            ForEach(items, (item, i) => TextBlock($"  - {item}").WithKey($"{i}-{item}"))
        ).Padding(24);
    }
}
// </snippet:iteration-demo>

// <snippet:app-flags>
// Observable<T> is the lightweight INPC cell that backs dev-only flags.
// Declare them as static readonly so every component shares one instance.
public static class AppFlags
{
    public static readonly Observable<bool> DebugUI = new(false);
    public static readonly Observable<bool> SlowMode = new(false);
    public static readonly Observable<bool> ForceDark = new(false);
}
// </snippet:app-flags>

// <snippet:debug-menu>
static class DevMenu
{
    // Named Use* because it consumes a hook slot: REACTOR_HOOKS_005 only
    // permits hook calls from Render() or a Use*-named method.
    public static Element UseTitleBar(RenderContext ctx)
    {
        // Subscribe during render — not inside a menu item callback, which
        // runs when the item is clicked rather than during a render pass.
        var debugUI = ctx.UseObservable(AppFlags.DebugUI).Value;

        return HStack(8,
            TextBlock("My App"),
#if DEBUG
            // An ordinary menu, compiled into Debug builds only.
            DropDownButton("Dev", MenuItems(new MenuFlyoutItemBase[]
            {
                ToggleMenuItem("Debug UI", debugUI,
                    v => AppFlags.DebugUI.Value = v),
                MenuSeparator(),
                MenuItem("Clear cache", () => CacheService.Clear()),
                MenuItem("Slow mode off", () => AppFlags.SlowMode.Value = false),
            })),
#endif
            // Only *constructed* when the flag is on: in retail the ternary
            // costs one bool read — no element tree, no children reconciled.
            debugUI ? TextBlock("debug overlay").Opacity(0.6) : null
        );
    }
}

// Stand-in for whatever your app actually caches.
static class CacheService
{
    public static void Clear() { }
}
// </snippet:debug-menu>

// <snippet:debug-overlays>
static class DebugOverlays
{
    // The two items the removed dev menu built in, as ordinary menu items.
    // Add them to your own Debug-only menu.
    public static MenuFlyoutItemBase[] Items() => new MenuFlyoutItemBase[]
    {
        // After each render, mounted controls flash red and changed ones yellow.
        ToggleMenuItem("Highlight reconcile changes",
            ReactorFeatureFlags.HighlightReconcileChanges,
            v => ReactorFeatureFlags.HighlightReconcileChanges = v),
        // The latest duplicate or null keys in keyed lists, newest first.
        MenuItem("Log keyed-list warnings", () =>
        {
            foreach (var w in ReactorDiagnostics.RecentKeyedListWarnings)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"{w.Kind} in {w.ControlContext}: {string.Join(", ", w.SampleKeys)} ({w.Count}x)");
            }
        }),
    };
}
// </snippet:debug-overlays>