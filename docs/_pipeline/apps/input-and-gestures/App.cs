using System.Numerics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<PointerModifiersExample>("Input and Gestures", width: 640, height: 520
#if DEBUG
    , preview: true
#endif
);

// <snippet:pointer-modifiers>
class PointerModifiersExample : Component
{
    public override Element Render()
    {
        var (hover, setHover) = UseState(false);
        var (tapCount, setTapCount) = UseState(0);

        return VStack(12,
            Border(TextBlock(hover ? "hovered" : "hover me")
                .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
                .Width(240).Height(120)
                .Background(hover ? "#BFE3FF" : "#E5F1FB")
                .CornerRadius(8)
                .OnPointerEntered((_, _) => setHover(true))
                .OnPointerExited((_, _) => setHover(false))
                .OnTapped((_, _) => setTapCount(tapCount + 1))
                .OnDoubleTap(() => setTapCount(0)),

            TextBlock($"Tapped {tapCount} time(s) — double-tap to reset")
        ).Padding(24);
    }
}
// </snippet:pointer-modifiers>

// <snippet:pan-gesture>
class PanGestureExample : Component
{
    public override Element Render()
    {
        var (offset, setOffset) = UseState(Vector2.Zero);

        return Border(
            Border(TextBlock("drag me")
                .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
                .Width(120).Height(120)
                .Background("#3A7BD5")
                .Foreground("#ffffff")
                .CornerRadius(8)
                .Translation(offset.X, offset.Y, 0)
                .OnPan(
                    onChanged: g => setOffset(offset + new Vector2((float)g.Delta.X, (float)g.Delta.Y)),
                    withInertia: true)
                .OnDoubleTap(() => setOffset(Vector2.Zero))
        ).Height(260).Background("#f3f3f3").CornerRadius(8).Padding(16);
    }
}
// </snippet:pan-gesture>

// <snippet:long-press>
class LongPressExample : Component
{
    public override Element Render()
    {
        var (log, setLog) = UseState("hold the card for 500 ms");

        return VStack(12,
            Border(TextBlock("Hold me")
                .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
                .Height(80).Background("#FFF4CE").CornerRadius(6).Padding(12)
                .OnLongPress(
                    g => setLog($"long-press after {g.Duration.TotalMilliseconds:F0}ms"),
                    enableMouseEmulation: true),

            TextBlock(log)
        ).Padding(24);
    }
}
// </snippet:long-press>

// <snippet:use-element-focus>
class UseElementFocusExample : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("");
        var (inputRef, requestFocus) = this.UseElementFocus();
        UseEffect(() => requestFocus(), Array.Empty<object>());

        return VStack(12,
            TextBlock("The field below auto-focuses on mount via UseElementFocus()."),
            TextField(name, setName, placeholder: "name").Width(280).Ref(inputRef)
        ).Padding(24);
    }
}
// </snippet:use-element-focus>

// <snippet:kanban-dnd>
sealed record KanbanCard(string Id, string Title);

class KanbanDndExample : Component
{
    public override Element Render()
    {
        var (todo, setTodo) = UseState<IReadOnlyList<KanbanCard>>(new KanbanCard[]
        {
            new("k1", "Write docs"),
            new("k2", "Ship feature"),
        });
        var (done, setDone) = UseState<IReadOnlyList<KanbanCard>>(Array.Empty<KanbanCard>());

        Element Column(string label,
            IReadOnlyList<KanbanCard> cards,
            Action<IReadOnlyList<KanbanCard>> setThis)
        {
            var children = new List<Element> { TextBlock(label).SemiBold() };
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
                                if (!ctx.WasCancelled && ctx.CompletedOperation == DragOperations.Move)
                                    setThis(cards.Where(c => c.Id != captured.Id).ToList());
                            }));
            }
            return VStack(6, children.ToArray())
                .OnDrop<StackElement, KanbanCard>(
                    onDrop: c =>
                    {
                        if (!cards.Any(x => x.Id == c.Id))
                            setThis(cards.Append(c).ToList());
                    },
                    acceptedOps: DragOperations.Move);
        }

        return HStack(12,
            Border(Column("Todo", todo, setTodo))
                .Width(240).Background("#F7F7F7").CornerRadius(6).Padding(10),
            Border(Column("Done", done, setDone))
                .Width(240).Background("#F1FFF4").CornerRadius(6).Padding(10)
        ).Padding(24);
    }
}
// </snippet:kanban-dnd>
