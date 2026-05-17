// ═════════════════════════════════════════════════════════════════════════════
//  Animated List Demo — spec 042 (keyed list reconciliation + Animate(...))
//
//  Demonstrates:
//    • Incremental container updates (Phase 1) — insert at 0 / insert at end /
//      remove from middle / shuffle reuse the same realized containers instead
//      of redrawing the visible viewport.
//    • IReactorKeyed identity-on-data (Phase 2) — `Row` carries its own key,
//      so the templated ListView<T> call site drops the `keySelector:` arg.
//    • Animations.Animate(...) ambient (Phase 3) — wrap a state mutation,
//      every resulting structural change (insert / move / remove) animates
//      with the chosen kind without per-element transition modifiers.
//    • Reduced-motion compliance — the wrapper drops to a bare commit when
//      the OS opts the user out (WCAG 2.3.3).
//
//  Try it: flip the "Animate edits" switch off and click "+1 at top". The
//  insert is instant. Flip it on, click again — the list slides instead.
// ═════════════════════════════════════════════════════════════════════════════

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

namespace AnimatedListDemo;

// Identity-on-data: implementing IReactorKeyed lets the templated ListView<T>
// drop the explicit `keySelector:` argument and lets hand-built children call
// `.WithKey(item)` instead of `.WithKey(item.Id)`.
record Row(string Id, string Label, string Color) : IReactorKeyed
{
    string IReactorKeyed.Key => Id;
}

sealed class App : Component
{
    static readonly string[] Palette =
    {
        "#0078D4", "#D83B01", "#107C10", "#5C2D91",
        "#E81123", "#00B7C3", "#FF8C00", "#8E562E",
    };

    static Row MakeRow(int n) =>
        new(Guid.NewGuid().ToString("N"),
            $"Row {n:000}",
            Palette[n % Palette.Length]);

    static IReadOnlyList<Row> Seed(int count) =>
        Enumerable.Range(1, count).Select(MakeRow).ToList();

    public override Element Render()
    {
        var (items, setItems) = UseState(Seed(8));
        var (animate, setAnimate) = UseState(true);
        var (kind, setKind) = UseState((int)AnimationKind.Spring);
        var (counter, setCounter) = UseState(9);

        var reduceMotion = UseReducedMotion();

        // The single chokepoint every mutation flows through. Keeps the
        // "with or without Animate" contract honest: identical state
        // transitions; only the ambient differs.
        void Mutate(Func<IReadOnlyList<Row>, IReadOnlyList<Row>> change)
        {
            void commit() => setItems(change(items));
            if (!animate || reduceMotion)
            {
                commit();
                return;
            }
            Animations.Animate((AnimationKind)kind, commit);
        }

        var addEnd     = () => { Mutate(list => [.. list, MakeRow(counter)]); setCounter(counter + 1); };
        var addTop     = () => { Mutate(list => [MakeRow(counter), .. list]); setCounter(counter + 1); };
        var removeMid  = () => Mutate(list => list.Count == 0
                                                ? list
                                                : list.Where((_, i) => i != list.Count / 2).ToList());
        var removeLast = () => Mutate(list => list.Count == 0
                                                ? list
                                                : list.Take(list.Count - 1).ToList());
        var shuffle    = () =>
        {
            var rng = new Random();
            Mutate(list => list.OrderBy(_ => rng.Next()).ToList());
        };
        var reverse    = () => Mutate(list => list.Reverse().ToList());
        var bulkReset  = () => { Mutate(_ => Seed(8)); setCounter(9); };

        return FlexColumn(
            TitleBar("Animated List Demo"),

            ScrollView(
                VStack(16,
                    Heading("Keyed reconciliation in action"),
                    Caption(
                        "Insert / remove / shuffle reuse realized containers. Toggle Animate to " +
                        "watch the same edits go from instant to fluid. Spec 042.")
                        .Foreground(SecondaryText),

                    // ── Toolbar ────────────────────────────────────────────
                    Card(VStack(12,
                        (FlexRow(
                            ToggleSwitch(animate, setAnimate,
                                onContent: "On", offContent: "Off",
                                header: "Animate edits"),
                            ComboBox(
                                Enum.GetNames(typeof(AnimationKind)),
                                kind,
                                setKind)
                                .Header("Kind"),
                            When(reduceMotion, () =>
                                Border(TextBlock("Reduced motion ON — bypassing Animate")
                                          .Foreground(SecondaryText))
                                    .Background(Theme.SubtleFill)
                                    .Padding(horizontal: 10, vertical: 6)
                                    .CornerRadius(4)
                                    .VAlign(VerticalAlignment.Center))
                          ) with { ColumnGap = 16, AlignItems = FlexAlign.End }),

                        (FlexRow(
                            Button("+1 at top",    addTop).MinWidth(110),
                            Button("+1 at end",    addEnd).MinWidth(110),
                            Button("− middle",     removeMid).MinWidth(110),
                            Button("− last",       removeLast).MinWidth(110),
                            Button("Shuffle",      shuffle).MinWidth(110),
                            Button("Reverse",      reverse).MinWidth(110),
                            Button("Bulk reset",   bulkReset).MinWidth(110)
                         ) with { ColumnGap = 8, Wrap = FlexWrap.Wrap, RowGap = 8 })
                    ).Padding(16))
                    .Background(CardBackground)
                    .WithBorder(CardStroke, 1)
                    .CornerRadius(8),

                    // ── Side-by-side: templated vs. hand-built ─────────────
                    (FlexRow(
                        DemoColumn(
                            "ListView<T> (Phase 1 + 2)",
                            "Templated list. `IReactorKeyed` defaults the key " +
                            "selector; the diff emits Add/Move/Remove on " +
                            "ItemsSource so WinUI shows incremental theme " +
                            "transitions.",
                            ListView<Row>(items, RowView)
                                .Height(380)),

                        DemoColumn(
                            "Hand-built children (.WithKey)",
                            "FlexColumn(items.Select(...).WithKey(item)). " +
                            "Same Animate ambient drives mount / move / unmount " +
                            "on every child via ChildReconciler.",
                            ScrollView(
                                FlexColumn(items.Select(item => RowView(item, 0)).ToArray<Element?>())
                                    .Padding(4))
                                .Height(380))
                     ) with { ColumnGap = 16, AlignItems = FlexAlign.Stretch }),

                    Caption($"Count: {items.Count} — IDs survive across renders, so focus " +
                            "and selection stick to logical rows.")
                        .Foreground(TertiaryText)
                ).Padding(24)
            ).Flex(grow: 1)
        ).Backdrop(BackdropKind.Mica);
    }

    static Element RowView(Row item, int _) =>
        (FlexRow(
            Border(Empty())
                .Background(item.Color)
                .Size(10, 28)
                .CornerRadius(2)
                .Flex(shrink: 0),

            TextBlock(item.Label)
                .FontSize(15)
                .Flex(grow: 1, alignSelf: FlexAlign.Center),

            Caption(item.Id[..6])
                .Foreground(TertiaryText)
                .Flex(shrink: 0, alignSelf: FlexAlign.Center)
         ) with { ColumnGap = 12, AlignItems = FlexAlign.Center })
            .Padding(horizontal: 12, vertical: 8)
            .WithKey(item);

    static Element DemoColumn(string title, string body, Element content) =>
        VStack(8,
            SubHeading(title),
            Caption(body).Foreground(SecondaryText),
            Border(content)
                .Background(CardBackground)
                .WithBorder(CardStroke, 1)
                .CornerRadius(8)
        ).Flex(grow: 1);
}
