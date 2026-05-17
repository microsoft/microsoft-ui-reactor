using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor;

class ListViewPage : Component
{
    // Identity-on-data: implementing IReactorKeyed lets the templated
    // ListView<Fruit> drop the explicit `keySelector:` argument. Spec 042 §5.
    record Fruit(string Id, string Name) : IReactorKeyed
    {
        string IReactorKeyed.Key => Id;
    }

    static readonly string[] Catalog =
    {
        "Apricot", "Blackberry", "Cherry", "Date", "Elderberry",
        "Fig", "Grape", "Honeydew", "Kiwi", "Lemon",
        "Mango", "Nectarine", "Orange", "Papaya", "Quince",
    };

    public override Element Render()
    {
        var (mode, setMode) = UseState(0);
        var modes = new[] { "Single", "Multiple", "Extended", "None" };
        var items = new[] { "Apples", "Bananas", "Carrots", "Dates", "Eggplant", "Figs" };

        var selectionMode = mode switch
        {
            1 => ListViewSelectionMode.Multiple,
            2 => ListViewSelectionMode.Extended,
            3 => ListViewSelectionMode.None,
            _ => ListViewSelectionMode.Single
        };

        // ── Animated-edit demo state (spec 042) ─────────────────────────
        var (fruits, setFruits) = UseState<IReadOnlyList<Fruit>>(
            Catalog.Take(6).Select(n => new Fruit(Guid.NewGuid().ToString("N"), n)).ToList());
        var (animate, setAnimate) = UseState(true);
        var (cursor, setCursor) = UseState(6);
        var reduceMotion = UseReducedMotion();

        // Single chokepoint — every edit goes through here so toggling
        // "Animate" really is the only difference between the two paths.
        // Reduced-motion bypasses the wrapper entirely (WCAG 2.3.3).
        void Mutate(Func<IReadOnlyList<Fruit>, IReadOnlyList<Fruit>> change)
        {
            void commit() => setFruits(change(fruits));
            if (!animate || reduceMotion) { commit(); return; }
            Animations.Animate(AnimationKind.Spring, commit);
        }

        Fruit Next()
        {
            var name = Catalog[cursor % Catalog.Length];
            setCursor(cursor + 1);
            return new Fruit(Guid.NewGuid().ToString("N"), name);
        }

        return ScrollView(
            VStack(16,
                PageHeader("ListView", "Displays items in a vertical scrolling list."),

                SampleCard("Basic ListView",
                    ListView(
                        items.Select(i => TextBlock(i) as Element).ToArray()
                    ).SelectionMode(selectionMode).Height(250),
                    @"ListView(\n    TextBlock(""Apples""), TextBlock(""Bananas""), ...\n).SelectionMode(ListViewSelectionMode.Multiple)",
                    OptionPanel(
                        TextBlock("Selection Mode"),
                        ComboBox(modes, mode, setMode)
                    )),

                SampleCard("Data-Driven ListView",
                    ListView(
                        items.ToList().AsReadOnly(),
                        s => s,
                        (s, i) => HStack(8,
                            Border(TextBlock($"{i + 1}").Center().Foreground("#FFFFFF"))
                                .Background(Theme.Accent).Size(28, 28).CornerRadius(14),
                            TextBlock(s)
                        )
                    ).Height(250),
                    @"ListView(\n    items, s => s,\n    (s, i) => HStack(8,\n        Border(TextBlock($""{i+1}"")).Size(28,28),\n        TextBlock(s)\n    )\n)"),

                // ── Animated edit (spec 042 — keyed reconciliation + Animate) ──
                SampleCard("Animated edit (spec 042)",
                    VStack(12,
                        (FlexRow(
                            Button("+1 at top",  () => Mutate(list => [Next(), .. list])).MinWidth(110),
                            Button("+1 at end",  () => Mutate(list => [.. list, Next()])).MinWidth(110),
                            Button("− middle",   () => Mutate(list => list.Count == 0
                                                                        ? list
                                                                        : list.Where((_, i) => i != list.Count / 2).ToList())).MinWidth(110),
                            Button("Shuffle",    () => { var r = new Random(); Mutate(list => list.OrderBy(_ => r.Next()).ToList()); }).MinWidth(110),
                            Button("Reverse",    () => Mutate(list => list.Reverse().ToList())).MinWidth(110)
                         ) with { ColumnGap = 8, Wrap = FlexWrap.Wrap, RowGap = 8 }),

                        // Templated ListView<Fruit> — note the missing keySelector
                        // argument; Fruit : IReactorKeyed defaults it to t => t.Key.
                        ListView<Fruit>(fruits, (f, i) => HStack(8,
                                Border(TextBlock($"{i + 1}").Center().Foreground("#FFFFFF"))
                                    .Background(Theme.Accent).Size(28, 28).CornerRadius(14),
                                TextBlock(f.Name).VAlign(VerticalAlignment.Center)
                            ).Padding(horizontal: 4, vertical: 2))
                            .Height(280)
                    ),
                    @"// Identity on data — no `keySelector:` argument needed
record Fruit(string Id, string Name) : IReactorKeyed
{ string IReactorKeyed.Key => Id; }

// Toggle: with or without the Animate wrapper
void Mutate(...) {
    void commit() => setFruits(change(fruits));
    if (!animate || reduceMotion) { commit(); return; }
    Animations.Animate(AnimationKind.Spring, commit);
}

ListView<Fruit>(fruits, (f, i) => ...)",
                    OptionPanel(
                        TextBlock("Animate edits"),
                        ToggleSwitch(animate, setAnimate, onContent: "On", offContent: "Off"),
                        reduceMotion
                            ? Caption("Reduced motion ON — bypassing Animate").Foreground(Theme.SecondaryText)
                            : Caption("Insert / move / remove pick up Spring via the ambient.").Foreground(Theme.SecondaryText)
                    ))
            ).Margin(36, 24, 36, 36)
        );
    }
}
