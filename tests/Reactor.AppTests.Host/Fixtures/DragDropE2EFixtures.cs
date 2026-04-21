using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

/// <summary>
/// E2E host fixtures for spec 027 Tier 6 drag-and-drop. WinAppDriver's mouse
/// primitives drive real drags across these fixtures; see
/// <c>tests/Reactor.AppTests/Tests/DragDropTests.cs</c>.
/// </summary>
internal static class DragDropE2EFixtures
{
    // ── Typed reorder: two columns with a card that moves across ──

    private sealed record CardPayload(string Id, string Title);

    internal class TypedReorderComponent : Component
    {
        public override Element Render()
        {
            var (todo, setTodo) = UseState<IReadOnlyList<CardPayload>>(
                new[] { new CardPayload("c1", "task-1") });
            var (done, setDone) = UseState<IReadOnlyList<CardPayload>>(Array.Empty<CardPayload>());

            Element RenderCards(IReadOnlyList<CardPayload> cards,
                Action<IReadOnlyList<CardPayload>> setThis,
                string columnId)
            {
                var kids = new List<Element>
                {
                    TextBlock(columnId).AutomationId($"Col_{columnId}_Label"),
                    TextBlock($"Count:{cards.Count}").AutomationId($"Col_{columnId}_Count"),
                };
                foreach (var c in cards)
                {
                    var captured = c;
                    // TextBlock drag source — Button consumes PointerPressed, which
                    // prevents WinUI's CanDrag detection from seeing the drag gesture.
                    kids.Add(
                        TextBlock(captured.Title)
                            .Padding(10).Width(200)
                            .OnDragStart<TextBlockElement, CardPayload>(
                                getPayload: () => captured,
                                allowedOperations: DragOperations.Move,
                                onEnd: ctx =>
                                {
                                    if (!ctx.WasCancelled && ctx.CompletedOperation == DragOperations.Move)
                                        setThis(cards.Where(x => x.Id != captured.Id).ToList());
                                })
                            .AutomationId($"Card_{captured.Id}")
                    );
                }
                return VStack(6, kids.ToArray());
            }

            void AcceptDrop(IReadOnlyList<CardPayload> cards,
                Action<IReadOnlyList<CardPayload>> setThis,
                CardPayload card)
            {
                if (!cards.Any(x => x.Id == card.Id))
                    setThis(cards.Append(card).ToList());
            }

            // Drop-zone columns are Buttons so they expose an AutomationPeer that
            // WinAppDriver can locate via FindById. OnDrop is attached to the Button
            // itself so the drop target is the outermost visible element.
            return HStack(12,
                Button(RenderCards(todo, setTodo, "Todo"), null)
                    .Width(260).Height(220)
                    .Padding(8)
                    .OnDrop<ButtonElement, CardPayload>(
                        onDrop: card => AcceptDrop(todo, setTodo, card),
                        acceptedOps: DragOperations.Move)
                    .AutomationId("Col_Todo"),
                Button(RenderCards(done, setDone, "Done"), null)
                    .Width(260).Height(220)
                    .Padding(8)
                    .OnDrop<ButtonElement, CardPayload>(
                        onDrop: card => AcceptDrop(done, setDone, card),
                        acceptedOps: DragOperations.Move)
                    .AutomationId("Col_Done")
            );
        }
    }

    internal static Element TypedReorderTest(RenderContext ctx) =>
        Component<TypedReorderComponent>();

    // ── Text-format round-trip: source writes text, target reads it ──

    internal class TextFormatComponent : Component
    {
        public override Element Render()
        {
            var (dropped, setDropped) = UseState("(none)");

            // TextBlock is the drag source: it has an AutomationPeer (so WinAppDriver
            // can locate it) but doesn't consume PointerPressed the way Button does,
            // which lets WinUI's CanDrag detection see the gesture.
            return VStack(12,
                TextBlock("drag source")
                    .Padding(12)
                    .Width(180)
                    .OnDragStart<TextBlockElement>(() => DragData.Text("dragged-text"),
                        allowedOperations: DragOperations.Copy | DragOperations.Move)
                    .AutomationId("TextDragSource"),

                Button("drop zone", null)
                    .Padding(12).Width(220)
                    .OnDrop<ButtonElement>(args =>
                    {
                        if (args.Data.TryGetText(out var text))
                        {
                            setDropped(text);
                            args.AcceptedOperation = DragOperations.Copy;
                        }
                    })
                    .AutomationId("TextDropZone"),

                TextBlock($"Dropped: {dropped}").AutomationId("TextDropResult")
            );
        }
    }

    internal static Element TextFormatTest(RenderContext ctx) =>
        Component<TextFormatComponent>();
}
