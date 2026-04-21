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
                    kids.Add(
                        Button(captured.Title, null)
                            .Padding(10)
                            .OnDragStart<ButtonElement, CardPayload>(
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
                return VStack(6, kids.ToArray())
                    .OnDrop<StackElement, CardPayload>(
                        onDrop: card =>
                        {
                            if (!cards.Any(x => x.Id == card.Id))
                                setThis(cards.Append(card).ToList());
                        },
                        acceptedOps: DragOperations.Move);
            }

            return HStack(12,
                Border(RenderCards(todo, setTodo, "Todo"))
                    .Width(260).Height(220)
                    .Background("#F7F7F7").CornerRadius(6).Padding(8)
                    .AutomationId("Col_Todo"),
                Border(RenderCards(done, setDone, "Done"))
                    .Width(260).Height(220)
                    .Background("#F1FFF4").CornerRadius(6).Padding(8)
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

            return VStack(12,
                Button("drag source", null)
                    .Padding(12)
                    .OnDragStart<ButtonElement>(() => DragData.Text("dragged-text"),
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
