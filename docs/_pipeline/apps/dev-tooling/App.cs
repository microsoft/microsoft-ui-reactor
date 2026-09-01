using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<DevToolingApp>("Dev Tooling Demo", width: 600, height: 450
);

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
// ReactorApp.Run<DevToolingApp>("Dev Tooling Demo",
//     width: 600, height: 450
// );
//
// Hot reload works when the app is launched under dotnet watch. Devtools
// screenshot capture is enabled by the app project's Reactor.DevtoolsSupport
// switch and activated by launching with --devtools.
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
// }, width: 400, height: 300);
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

// <snippet:devtools-gate>
static class DevGate
{
    // UseDevtools() is a RenderContext extension, so it is reached from a
    // function component's ctx (or any helper you hand the ctx to) — there is
    // no Component-level forwarder. It returns true only when BOTH the
    // Reactor.DevtoolsSupport build switch and `--devtools app` are present.
    //
    // The helper is named Use* because it consumes a hook slot: REACTOR_HOOKS_005
    // only permits hook calls from Render() or a Use*-named method, so a helper
    // called `Shell` here would warn in any project that copies this.
    public static Element UseShell(RenderContext ctx)
    {
        var dev = ctx.UseDevtools();

        return VStack(8,
            MainContent(),
            dev ? DebugOverlay() : Empty()
        );
    }

    private static Element MainContent() => TextBlock("App content");

    // Only *constructed* when dev is true. In retail the ternary costs one
    // bool read plus one branch — no element tree, no children reconciled.
    private static Element DebugOverlay() =>
        TextBlock("debug overlay").Opacity(0.6);
}
// </snippet:devtools-gate>

// <snippet:devtools-menu>
static class DevMenu
{
    public static Element UseTitleBar(RenderContext ctx)
    {
        // Subscribe during render — not inside the menu builder. The builder
        // lambda runs when the flyout opens, which is not a render pass, so a
        // hook call in there would break hook ordering.
        //
        // Named Use* for the same reason as DevGate.UseShell: it consumes a
        // hook slot, which REACTOR_HOOKS_005 requires be done from Render() or
        // a Use*-named helper.
        var debugUI = ctx.UseObservable(AppFlags.DebugUI).Value;

        return HStack(8,
            TextBlock("My App"),
            // DevtoolsMenu renders itself as a titlebar item only when
            // UseDevtools() is true, so the same cost model carries over.
            DevtoolsMenu(() => new MenuFlyoutItemBase[]
            {
                ToggleMenuItem("Debug UI", debugUI,
                    v => AppFlags.DebugUI.Value = v),
                MenuSeparator(),
                MenuItem("Clear cache", () => CacheService.Clear()),
                MenuItem("Slow mode off", () => AppFlags.SlowMode.Value = false),
            })
        );
    }
}

// Stand-in for whatever your app actually caches.
static class CacheService
{
    public static void Clear() { }
}
// </snippet:devtools-menu>
