using System.Numerics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

// Gallery demo for spec 027 (Input & Gestures). Exercises the full declarative
// surface so the demo app doubles as a manual-smoke for each tier of the plan:
// pan with inertia (Tier 3), long-press context menu (Tier 3b), typed kanban DnD
// (Tier 6a), text-format drag for cross-process drops (Tier 6b), and
// UseElementFocus auto-focus on mount (Tier 5).

class InputGesturesDemo : Component
{
    public override Element Render() => ScrollView(
        VStack(24,
            Heading("Input & Gestures"),
            Component<GesturePanSample>(),
            Component<LongPressSample>(),
            Component<KanbanDragDropSample>(),
            Component<TextDragSample>(),
            Component<UseFocusSample>()
        )
    );
}

sealed class GesturePanSample : Component
{
    public override Element Render()
    {
        var (offset, setOffset) = UseState(Vector2.Zero);

        return InputGesturesSampleCard.Build(
            "Pan with inertia",
            "Drag the blue square. Release fast to see inertia. Double-tap to reset.",
            Border(
                Border(TextBlock("drag me").Foreground("#ffffff")
                    .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
                    .Width(120).Height(120)
                    .Background("#3A7BD5")
                    .CornerRadius(8)
                    .Translation(offset.X, offset.Y, 0)
                    .OnPan(
                        onChanged: g => setOffset(offset + new Vector2((float)g.Delta.X, (float)g.Delta.Y)),
                        withInertia: true)
                    .OnDoubleTap(() => setOffset(Vector2.Zero))
            ).Height(220).Background("#f3f3f3").CornerRadius(8).Padding(8)
        );
    }
}

sealed class LongPressSample : Component
{
    public override Element Render()
    {
        var (log, setLog) = UseState("touch / pen only — enable mouse emulation for desktop");

        return InputGesturesSampleCard.Build(
            "Long press",
            "Touch-and-hold the card for 500ms. Mouse is off by default; this sample opts in so a desktop mouse also triggers.",
            VStack(8,
                Border(TextBlock("Hold me")
                    .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
                    .Height(80).Background("#FFF4CE").CornerRadius(6).Padding(12)
                    .OnLongPress(
                        g => setLog($"Long press @ ({g.Position.X:F0}, {g.Position.Y:F0}) after {g.Duration.TotalMilliseconds:F0}ms"),
                        enableMouseEmulation: true),
                TextBlock(log).Foreground(TertiaryText)
            )
        );
    }
}

// A tiny kanban: two columns with cards. Drag to reorder between columns using
// a typed payload. The source removes its card only after DropCompleted confirms
// the Move (move-on-confirmation pattern — see docs/guide/input-and-gestures.md).
sealed record KanbanCard(string Id, string Title);

sealed class KanbanDragDropSample : Component
{
    public override Element Render()
    {
        var (todo, setTodo) = UseState<IReadOnlyList<KanbanCard>>(new KanbanCard[]
        {
            new("k1", "Write doc template"),
            new("k2", "Land E2E tests"),
            new("k3", "Compile samples"),
        });
        var (done, setDone) = UseState<IReadOnlyList<KanbanCard>>(new KanbanCard[]
        {
            new("d1", "Tier 1 modifiers"),
        });

        Element RenderColumn(string title,
            IReadOnlyList<KanbanCard> cards,
            Action<IReadOnlyList<KanbanCard>> setThis)
        {
            var children = new List<Element>
            {
                TextBlock(title).SemiBold(),
            };

            foreach (var card in cards)
            {
                var captured = card;
                children.Add(
                    Border(TextBlock(captured.Title).Foreground("#ffffff"))
                        .Background("#4B7BEC").CornerRadius(6).Padding(10)
                        .OnDragStart<BorderElement, KanbanCard>(
                            getPayload: () => captured,
                            allowedOperations: DragOperations.Move,
                            onEnd: ctx =>
                            {
                                // Only remove after a confirmed Move (not on cancel or Copy).
                                if (!ctx.WasCancelled && ctx.CompletedOperation == DragOperations.Move)
                                    setThis(cards.Where(c => c.Id != captured.Id).ToList());
                            })
                );
            }

            return VStack(6, children.ToArray())
                .OnDrop<StackElement, KanbanCard>(
                    onDrop: card =>
                    {
                        if (!cards.Any(c => c.Id == card.Id))
                            setThis(cards.Append(card).ToList());
                    },
                    acceptedOps: DragOperations.Move);
        }

        return InputGesturesSampleCard.Build(
            "Kanban (typed drag & drop)",
            "Drag a card between columns. The source removes its copy only once DropCompleted confirms the Move.",
            Grid(
                new[] { "*", "*" },
                new[] { "Auto" },
                Border(RenderColumn("Todo", todo, setTodo))
                    .Background("#F7F7F7").CornerRadius(6).Padding(10).Margin(4)
                    .Grid(column: 0),
                Border(RenderColumn("Done", done, setDone))
                    .Background("#F1FFF4").CornerRadius(6).Padding(10).Margin(4)
                    .Grid(column: 1)
            )
        );
    }
}

sealed class TextDragSample : Component
{
    public override Element Render()
    {
        var (dropped, setDropped) = UseState<string?>(null);

        return InputGesturesSampleCard.Build(
            "Drag text (to Notepad or into the drop zone)",
            "The source writes plain text to the DataPackage so a cross-process target (Notepad, Word) can accept it. The in-app drop zone reads the text back via TryGetText.",
            HStack(12,
                Border(TextBlock("drag me → notepad").Foreground("#ffffff"))
                    .Background("#2C7A7B").CornerRadius(6).Padding(12)
                    .OnDragStart<BorderElement>(() => new DragData().WithText("hello from Reactor")),

                Border(TextBlock(dropped ?? "drop text here"))
                    .Background("#E6FFFA").CornerRadius(6).Padding(12).Width(220)
                    .OnDrop<BorderElement>(args =>
                    {
                        if (args.Data.TryGetText(out var text))
                        {
                            setDropped(text);
                            args.AcceptedOperation = DragOperations.Copy;
                        }
                    })
            )
        );
    }
}

sealed class UseFocusSample : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("");
        var (inputRef, requestFocus) = this.UseElementFocus();
        UseEffect(() => requestFocus(), Array.Empty<object>());

        return InputGesturesSampleCard.Build(
            "UseElementFocus: auto-focus on mount",
            "Switching to this tab should place the caret in the first input via ctx.UseElementFocus().",
            VStack(8,
                TextField(name, setName, placeholder: "name").Width(280).Ref(inputRef),
                HStack(6, TextBlock("focused via"), TextBlock("UseElementFocus()").FontFamily("Consolas"))
            )
        );
    }
}

static class InputGesturesSampleCard
{
    public static Element Build(string title, string subtitle, Element content) =>
        Border(VStack(8,
            SubHeading(title),
            TextBlock(subtitle).Foreground(TertiaryText).TextWrapping(),
            content
        )).Padding(16).CornerRadius(8).WithBorder("#dddddd");
}
