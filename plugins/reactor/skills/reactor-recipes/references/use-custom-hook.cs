// Recipe: extract stateful logic into a custom hook.
//
// Rule: hooks (UseState, UseEffect, UseMemo, …) can only be called from a
// Render() override or from another method whose name starts with `Use`. If
// you want to share stateful behavior across components — or just hide it
// behind a clean name — write a custom hook as an *extension method on
// RenderContext*. The analyzer (REACTOR_HOOKS_005) enforces the naming
// convention; a method called `DebouncedValue(...)` that calls UseState
// internally will fail to compile.
//
// Wrong shapes that the analyzer rejects:
//   * UseState in a field initializer:  `int _x = UseState(0).Item1;`
//   * UseState in a regular helper:     `int GetCount() { return UseState(0).Item1; }`
//   * UseState inside an event handler: `Button("X", () => UseState(0))`
//
// Right shape: a `Use*` extension on RenderContext that bundles the hooks.
// Render() calls it like any other hook — order-stable, deps-driven.

#:package Microsoft.UI.Reactor@0.0.0-local
#:package Microsoft.WindowsAppSDK@2.0.1
#:property OutputType=WinExe
#:property TargetFramework=net10.0-windows10.0.22621.0
#:property UseWinUI=true
#:property WindowsPackageType=None

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Custom hook demo", width: 520, height: 320);

// ── The custom hook ────────────────────────────────────────────────────
// Extension on RenderContext; name starts with `Use`. Inside we are free
// to call other hooks (UseState, UseEffect). Callers see one return value
// and never know the hook ran two underlying hooks.

static class DebounceHooks
{
    public static T UseDebouncedValue<T>(this RenderContext ctx, T value, int delayMs)
    {
        var (debounced, setDebounced) = ctx.UseState(value);

        // Deps array drives re-arming. Pass scalars (value, delayMs) — never
        // a freshly-allocated array/lambda, or REACTOR_HOOKS_004 fires.
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
                catch (TaskCanceledException) { /* superseded by next render */ }
            });
            return () => cts.Cancel();
        }, value!, delayMs);

        return debounced;
    }

    // Optional Component-receiver overload mirrors the built-in hook pattern
    // (see UseAnnounce / UseFocus in src/Reactor/Hooks). Lets callers write
    // `this.UseDebouncedValue(...)` from inside a Component subclass without
    // touching ctx directly.
    public static T UseDebouncedValue<T>(this Component component, T value, int delayMs)
        => component.Context.UseDebouncedValue(value, delayMs);
}

// ── The component that uses it ─────────────────────────────────────────

class App : Component
{
    public override Element Render()
    {
        var (query, setQuery) = UseState("");

        // One call site, two underlying hooks. Order is stable because the
        // custom hook always calls the same hooks in the same sequence.
        var debouncedQuery = this.UseDebouncedValue(query, delayMs: 300);

        return VStack(12,
            TextBox(query, setQuery, placeholderText: "Type to search…"),
            TextBlock($"Live: {query}").Opacity(0.7),
            TextBlock($"Debounced (300ms): {debouncedQuery}").Bold()
        ).Padding(24);
    }
}
