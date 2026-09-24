using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Fundamentals;

class UseMemoPage : Component
{
    static readonly string[] Fruits =
    [
        "Apricot", "Banana", "Blackberry", "Cherry", "Clementine",
        "Damson", "Elderberry", "Fig", "Grape", "Guava",
    ];

    public override Element Render()
    {
        var (filter, setFilter) = UseState("");
        var (clicks, bumpClicks) = UseReducer(0);
        var (width, setWidth) = UseState(3.0);
        var (height, setHeight) = UseState(4.0);

        // Recomputed only when `filter` changes — not on every unrelated re-render.
        var matches = UseMemo(
            () => Fruits.Where(f => f.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray(),
            filter);

        // Empty dependencies, so the same delegate instance is handed out every render.
        var resetClicks = UseCallback(() => bumpClicks(_ => 0), Array.Empty<object>());

        // A scalar key, NOT the tuple (width, height) — REACTOR_HOOKS_004 rejects a tuple
        // expression in the deps slot, even though a ValueTuple is value-equal at runtime.
        var area = UseMemo(() => width * height, $"{width}|{height}");

        return ScrollView(VStack(16,
            PageHeader("UseMemo and UseCallback",
                "UseMemo caches a computed value, UseCallback caches a delegate. Both recompute only when a dependency changes."),

            SampleCard("Memoized computation",
                VStack(8,
                    TextBox(filter, setFilter, placeholderText: "Filter the list")
                        .AutomationName("Filter")
                        .Width(280),
                    TextBlock(matches.Length == 0 ? "No matches." : string.Join(", ", matches))
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (filter, setFilter) = UseState("""");

var matches = UseMemo(
    () => Fruits.Where(f => f.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray(),
    filter);

TextBox(filter, setFilter, placeholderText: ""Filter the list"")
"),

            SampleCard("UseCallback — a stable delegate",
                VStack(8,
                    HStack(8,
                        Button("Click me", () => bumpClicks(c => c + 1)),
                        Button("Reset", resetClicks)),
                    TextBlock($"Clicks: {clicks}").Foreground(Theme.SecondaryText),
                    Caption("resetClicks is the same delegate instance on every render, so a child that compares handlers by reference is not re-rendered by it.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (clicks, bumpClicks) = UseReducer(0);

var resetClicks = UseCallback(() => bumpClicks(_ => 0), Array.Empty<object>());

Button(""Reset"", resetClicks)
"),

            SampleCard("Dependencies must survive a render",
                VStack(8,
                    HStack(8,
                        Button("Wider", () => setWidth(width + 1)),
                        Button("Taller", () => setHeight(height + 1))),
                    TextBlock($"{width} x {height} = {area}").Foreground(Theme.SecondaryText),
                    Caption("A tuple expression is rejected by REACTOR_HOOKS_004 even though a ValueTuple is value-equal at runtime. A freshly allocated object, array, or lambda genuinely does compare unequal every render. Either way: use a scalar key, or pass the values as separate dependencies.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (width, setWidth) = UseState(3.0);
var (height, setHeight) = UseState(4.0);

// A scalar key is stable across renders and reads clearly in the deps slot.
var area = UseMemo(() => width * height, $""{width}|{height}"");

// Passing the values as separate dependencies works the same way.
var areaFromPair = UseMemo(() => width * height, width, height);
")
        ).Margin(36, 24, 36, 36));
    }
}
