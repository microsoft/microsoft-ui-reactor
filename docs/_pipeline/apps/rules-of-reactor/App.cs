using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Doc app for `rules-of-reactor.md`. Each snippet is a "before / after"
// pair — the bad shape and the corrected shape — so the page can show
// the analyzer's catch and the fix side by side. Analyzers are
// suppressed in the csproj because these examples deliberately violate
// the rules (the comment in each snippet names the analyzer code).
ReactorApp.Run<RulesApp>("Rules of Reactor", width: 360, height: 220
#if DEBUG
    , preview: true
#endif
);

class RulesApp : Component
{
    public override Element Render() => Component<RuleGood>();
}

// <snippet:hook-order-bad>
// REACTOR_HOOKS_001 — hooks must run unconditionally on every render.
// Wrapping a hook in `if` shifts the hook indices when the branch flips,
// and the next render reads slot N expecting `UseEffect` but finds
// `UseState`. The HookOrderException it raises is loud, but the bug
// can ship if the conditional is rarely true.
class HookOrderBad : Component
{
    public bool ShouldCount;

    public override Element Render()
    {
        if (ShouldCount)
        {
            var (count, _) = UseState(0);             // REACTOR_HOOKS_001
            return TextBlock($"Count: {count}");
        }
        return TextBlock("No counter.");
    }
}
// </snippet:hook-order-bad>

// <snippet:hook-order-good>
class HookOrderGood : Component
{
    public bool ShouldCount;

    public override Element Render()
    {
        // Hook always runs; the conditional moves into the render output.
        var (count, _) = UseState(0);
        return ShouldCount
            ? TextBlock($"Count: {count}")
            : TextBlock("No counter.");
    }
}
// </snippet:hook-order-good>

// <snippet:purity-bad>
// Render must be pure. Side effects (file I/O, mutation of static state,
// timers) belong inside UseEffect, which runs after the render commits.
// A logger call inside Render mounts will fire on every re-render,
// including ones triggered by the debugger — and it makes snapshot tests
// flaky because the rendered output now depends on a side effect.
static class TelemetryBad
{
    public static int CardRenders;
}

class CardBad : Component
{
    public override Element Render()
    {
        TelemetryBad.CardRenders++;                    // side effect in Render
        return TextBlock("Card");
    }
}
// </snippet:purity-bad>

// <snippet:purity-good>
static class Telemetry
{
    public static int CardRenders;
}

class CardGood : Component
{
    public override Element Render()
    {
        UseEffect(() =>
        {
            Telemetry.CardRenders++;
            return () => { };
        });
        return TextBlock("Card");
    }
}
// </snippet:purity-good>

// <snippet:keys-bad>
// A list reorder without keys forces the reconciler to walk both lists in
// order and reuse slot 0 for whatever new item lands first. Local state
// (focus, scroll position, in-flight edits) gets attached to the wrong
// row. WithKey on each child binds state to identity rather than slot.
class TodoListBad : Component
{
    public TodoItem[] Items = System.Array.Empty<TodoItem>();

    public override Element Render() => VStack(4,
        Items.Select(i =>
            // No .WithKey — reorder is destructive.
            TextField(i.Title, _ => { }, header: i.Id.ToString())
        ).ToArray()
    );
}
public record TodoItem(int Id, string Title);
// </snippet:keys-bad>

// <snippet:keys-good>
class TodoListGood : Component
{
    public TodoItem[] Items = System.Array.Empty<TodoItem>();

    public override Element Render() => VStack(4,
        Items.Select(i =>
            TextField(i.Title, _ => { }, header: i.Id.ToString())
                .WithKey(i.Id.ToString())                  // stable identity
        ).ToArray()
    );
}
// </snippet:keys-good>

class RuleGood : Component
{
    public override Element Render() => VStack(8,
        TextBlock("Rules-of-reactor doc app.").Padding(20),
        Button("OK", () => { })
    );
}

// Suppress unused warnings on the "Bad" examples; they exist as snippet
// targets, not as code that the host actually mounts.
#pragma warning disable CS0414
class _Unused
{
    private HookOrderBad? a;
    private CardBad? b;
    private TodoListBad? c;
}
