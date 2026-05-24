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
        // For 60 Hz smooth panning, write directly to the mounted element's
        // Translation inside onChanged. Going through setState would queue
        // Low-priority re-renders that get starved by the manipulation event
        // stream itself, producing a laggy drag. The committedRef holds the
        // position at the last gesture end so successive drags accumulate.
        // Reset lives on a sibling Button because WinUI suppresses the tap
        // recognizer when ManipulationMode ≠ System — .OnDoubleTap on the same
        // element as .OnPan wouldn't fire.
        var cardRef = UseRef<FrameworkElement?>(null);
        var committedRef = UseRef(Vector2.Zero);
        var (offset, setOffset) = UseState(Vector2.Zero);

        void Reset()
        {
            committedRef.Current = Vector2.Zero;
            setOffset(Vector2.Zero);
            if (cardRef.Current is { } fe)
                fe.Translation = System.Numerics.Vector3.Zero;
        }

        return VStack(8,
            Border(
                Border(TextBlock("drag me")
                    .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
                    .Width(120).Height(120)
                    .Background("#3A7BD5")
                    .Foreground("#ffffff")
                    .CornerRadius(8)
                    .Translation(offset.X, offset.Y, 0)
                    .OnMount(fe => cardRef.Current = fe)
                    .OnPan(
                        onChanged: g =>
                        {
                            var next = committedRef.Current +
                                new Vector2((float)g.Translation.X, (float)g.Translation.Y);
                            if (cardRef.Current is { } fe)
                                fe.Translation = new System.Numerics.Vector3(next.X, next.Y, 0);
                        },
                        onEnded: g =>
                        {
                            committedRef.Current += new Vector2((float)g.Translation.X, (float)g.Translation.Y);
                            setOffset(committedRef.Current);
                        },
                        withInertia: true)
            ).Height(260).Background("#f3f3f3").CornerRadius(8).Padding(16),

            Button("Reset position", Reset)
        );
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
            TextBox(name, setName, placeholderText: "name").Width(280).Ref(inputRef)
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
