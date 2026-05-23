using System.Diagnostics;
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
        // Pan smoothly at 60 Hz by writing directly to the mounted element's
        // Translation inside the onChanged callback. Going through setState
        // would queue a Low-priority re-render (see ReactorHost.RenderLoop),
        // which gets starved during active input — the square only catches up
        // to the cursor every few frames.
        //
        // The committedRef holds the position at the last gesture end, so
        // successive drags accumulate. We only call setOffset at gesture end
        // (+ reset on Reset button) so React-style state stays in sync — the
        // label below updates on a small number of renders per gesture, not
        // on every manipulation tick.
        //
        // Why a Reset button instead of OnDoubleTap? WinUI suppresses the
        // tap/double-tap/right-tap/holding recognizers whenever ManipulationMode
        // is anything other than `System`. Adding `.OnPan` sets ManipulationMode
        // to TranslateX|TranslateY, which disables the double-tap recognizer
        // on the same element. An explicit button is also easier to discover.
        var cardRef = UseRef<FrameworkElement?>(null);
        var committedRef = UseRef(Vector2.Zero);
        var (offset, setOffset) = UseState(Vector2.Zero);
        var (eps, setEps) = UseState(0);
        var eventCountRef = UseRef(0);
        var lastTickRef = UseRef(Environment.TickCount64);

        void Reset()
        {
            committedRef.Current = Vector2.Zero;
            setOffset(Vector2.Zero);
            if (cardRef.Current is { } fe) fe.Translation = Vector3.Zero;
        }

        return InputGesturesSampleCard.Build(
            "Pan with inertia",
            $"Drag the blue square. Release fast to see inertia. Current: ({offset.X:F0}, {offset.Y:F0}) · pan events/sec: {eps}",
            VStack(8,
                Border(
                    Border(TextBlock("drag me").Foreground("#ffffff")
                        .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center))
                        .Width(120).Height(120)
                        .Background("#3A7BD5")
                        .CornerRadius(8)
                        // Translation is driven imperatively in OnPan.onChanged via cardRef to
                        // keep pan at compositor speed. A declarative .Translation(offset) here
                        // would reset the live position whenever setEps re-renders mid-gesture.
                        // On Reset we zero fe.Translation directly (see Reset above).
                        .OnMount(fe =>
                        {
                            cardRef.Current = fe;
                            fe.Translation = new Vector3(committedRef.Current.X, committedRef.Current.Y, 0);
                        })
                        .OnPan(
                            onChanged: g =>
                            {
                                var next = committedRef.Current + new Vector2((float)g.Translation.X, (float)g.Translation.Y);

                                // Direct compositor property — no reconciler round-trip.
                                if (cardRef.Current is { } fe)
                                    fe.Translation = new Vector3(next.X, next.Y, 0);

                                // Running events/sec counter, repainted once per second.
                                eventCountRef.Current++;
                                var now = Environment.TickCount64;
                                if (now - lastTickRef.Current >= 1000)
                                {
                                    setEps(eventCountRef.Current);
                                    eventCountRef.Current = 0;
                                    lastTickRef.Current = now;
                                }
                            },
                            onEnded: g =>
                            {
                                committedRef.Current += new Vector2((float)g.Translation.X, (float)g.Translation.Y);
                                setOffset(committedRef.Current);
                            },
                            withInertia: true)
                ).Height(220).Background("#f3f3f3").CornerRadius(8).Padding(8),

                Button("Reset position", Reset)
            )
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
                new[] { GridSize.Star(), GridSize.Star() },
                new[] { GridSize.Auto },
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

        // Deliberately NOT auto-focusing on mount in this gallery: a TextBox
        // deep inside a ScrollView that takes focus pulls the viewport down via
        // WinUI's BringIntoView — and it re-triggers every time the window
        // regains foreground. Real apps that auto-focus a first input (login
        // forms, modals) typically live on their own page and aren't scrolled.
        return InputGesturesSampleCard.Build(
            "UseElementFocus: imperative focus via ref",
            "Click the button to imperatively focus the input via ctx.UseElementFocus().",
            VStack(8,
                TextBox(name, setName, placeholder: "name").Width(280).Ref(inputRef),
                HStack(8,
                    Button("Focus input", () => requestFocus()),
                    TextBlock("wired via UseElementFocus()").FontFamily("Consolas")
                )
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
