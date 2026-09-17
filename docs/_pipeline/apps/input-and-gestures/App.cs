using System.Numerics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Advanced.Factories;

ReactorApp.Run<PointerModifiersExample>("Input and Gestures", width: 640, height: 520
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
                .Background(hover ? Theme.AccentTertiary : Theme.ControlFillSecondary)
                .CornerRadius(8)
                .IsTabStop(true)
                .OnPointerEntered((_, _) => setHover(true))
                .OnPointerExited((_, _) => setHover(false))
                .OnTapped((_, _) => setTapCount(tapCount + 1))
                .OnKeyDown((_, e) =>
                {
                    if (e.Key is VirtualKey.Enter or VirtualKey.Space)
                        setTapCount(tapCount + 1);
                })
                .OnDoubleTap(() => setTapCount(0)),

            TextBlock($"Tapped {tapCount} time(s) — double-tap to reset")
        ).Padding(24);
    }
}
// </snippet:pointer-modifiers>

// <snippet:keyboard-events>
class KeyboardEventsExample : Component
{
    public override Element Render()
    {
        var (value, setValue) = UseState("");
        var (log, setLog) = UseState("press Enter to submit");

        return VStack(12,
            TextBox(value, setValue, placeholderText: "type here").Width(280)
                .AutomationName("Text to submit")
                // Tunnels first — the right spot to intercept before bubbling.
                .OnPreviewKeyDown((_, _) => setLog("preview"))
                // Bubbles — fires after the preview pair.
                .OnKeyDown((_, e) =>
                {
                    if (e.Key == VirtualKey.Enter)
                        setLog($"submitted: {value}");
                })
                .OnCharacterReceived((_, e) => setLog($"typed '{e.Character}'")),

            TextBlock(log)
        ).Padding(24);
    }
}
// </snippet:keyboard-events>

// <snippet:focus-events>
class FocusEventsExample : Component
{
    public override Element Render()
    {
        var (value, setValue) = UseState("");
        var (hint, setHint) = UseState("");

        return VStack(12,
            TextBox(value, setValue, placeholderText: "email").Width(280)
                .AutomationName("Email")
                .OnGotFocus((_, _) => setHint("We never share your address."))
                .OnLostFocus((_, _) => setHint("")),

            TextBlock(hint).Foreground(Theme.SecondaryText)
        ).Padding(24);
    }
}
// </snippet:focus-events>

// <snippet:focus-modifiers>
class FocusModifiersExample : Component
{
    public override Element Render() => VStack(12,
        // Declarative focus order + an access key (Alt+S) on the primary action.
        Button("Submit", () => { })
            .TabIndex(3)
            .AccessKey("S")
            .IsTabStop(),   // default-true overload

        // Advanced focus knobs are first-class too.
        VStack(8,
            Button("One", () => { }),
            Button("Two", () => { }))
            .TabNavigation(KeyboardNavigationMode.Once)
            .XYFocusKeyboardNavigation(XYFocusKeyboardNavigationMode.Enabled)
    ).Padding(24);
}
// </snippet:focus-modifiers>

// <snippet:command-access-key>
class CommandAccessKeyExample : Component
{
    public override Element Render()
    {
        var save = new Command { Label = "Save", Execute = () => { }, AccessKey = "S" };

        // A later .AccessKey(...) wins over the one carried by the Command.
        return Button(save).AccessKey("F").Padding(24);
    }
}
// </snippet:command-access-key>

// <snippet:typed-ref>
class SearchBoxExample : Component
{
    public override Element Render()
    {
        var (query, setQuery) = UseState("");

        // ElementRef<T>.Current is already typed — no `as TextBox` at the call site.
        var inputRef = this.UseElementRef<TextBox>();
        UseEffect(() => inputRef.Current?.SelectAll(), Array.Empty<object>());

        return VStack(12,
            TextBlock("Text is pre-selected on mount via UseElementRef<TextBox>()."),
            TextBox(query, setQuery).Width(280)
                .AutomationName("Search query")
                .Ref(inputRef)
        ).Padding(24);
    }
}
// </snippet:typed-ref>

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
                    .HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
                    .Foreground(Theme.AccentText))
                    .Width(120).Height(120)
                    .Background(Theme.Accent)
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
            ).Height(260).Background(Theme.CardBackground).CornerRadius(8).Padding(16),

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
                .Height(80).Background(Theme.SystemCautionBackground).CornerRadius(6).Padding(12)
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
            TextBox(name, setName, placeholderText: "name").Width(280)
                .AutomationName("Name")
                .Ref(inputRef)
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
        var initialTodo = UseMemo(() => new KanbanCard[]
        {
            new("k1", "Write docs"),
            new("k2", "Ship feature"),
        }, Array.Empty<object>());
        var (todo, setTodo) = UseState<IReadOnlyList<KanbanCard>>(initialTodo);
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
                    Border(TextBlock(captured.Title).Foreground(Theme.AccentText))
                        .Background(Theme.Accent).CornerRadius(6).Padding(10)
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
                .Width(240).Background(Theme.CardBackground).CornerRadius(6).Padding(10),
            Border(Column("Done", done, setDone))
                .Width(240).Background(Theme.SystemSuccessBackground).CornerRadius(6).Padding(10)
        ).Padding(24);
    }
}
// </snippet:kanban-dnd>

sealed record Card(string Title);

static class InlineInputAndGestureSnippets
{
    private const string imageUrl = "ms-appx:///Assets/StoreLogo.png";

    static void PanBasics()
    {
        // <snippet:pan-basic>
        Rectangle()
            .OnPan(
                onChanged: g => Translate(g.Translation),
                onEnded: g => SnapToGrid(g.Translation),
                minimumDistance: 8.0,
                axis: PanAxis.Both,
                withInertia: true);
        // </snippet:pan-basic>
    }

    static void PinchAndRotate()
    {
        // <snippet:pinch-and-rotate>
        Image(imageUrl)
            .AutomationName("Photo preview")
            .OnPinch(
                onChanged: g => Scale(g.Scale),
                withInertia: true)
            .OnRotate(
                onChanged: g => Rotate(g.Angle));
        // </snippet:pinch-and-rotate>
    }

    static void LongPressMouse()
    {
        var listItem = Border(TextBlock("Open menu"));

        // <snippet:long-press-mouse>
        listItem.OnLongPress(() => ShowContextMenu(), enableMouseEmulation: true);
        // </snippet:long-press-mouse>
    }

    static void MigrationAfter()
    {
        // <snippet:set-event-migration-after>
        // After
        Rectangle()
            .OnPointerEntered((_, _) => Hover())
            .OnPointerExited((_, _) => Unhover());
        // </snippet:set-event-migration-after>
    }

    static void DragStandardFormats()
    {
        // <snippet:drag-standard-formats>
        Border(TextBlock("Drag me to Notepad"))
            .OnDragStart<BorderElement>(() => new DragData().WithText("hello world"));

        Rectangle()
            .OnDrop<RectangleElement>(args =>
            {
                if (args.Data.TryGetText(out var text))
                    Log(text);
                args.AcceptedOperation = DragOperations.Copy;
            });
        // </snippet:drag-standard-formats>
    }

    static void DragLazyProvider()
    {
        // <snippet:drag-lazy-provider>
        Border(TextBlock("Rich content"))
            .OnDragStart<BorderElement>(() => new DragData()
                .WithText("plain fallback")
                .WithHtml(ct => RenderExpensiveHtmlAsync(ct)));
        // </snippet:drag-lazy-provider>
    }

    static void DropIndicatorOverrides()
    {
        var children = new[] { TextBlock("Inbox") };
        var inbox = new List<Card>();

        // <snippet:drop-indicator-overrides>
        VStack(children.ToArray())
            .OnDragOver(args =>
            {
                args.UIOverride.Caption = "Move to Inbox";
                args.UIOverride.IsGlyphVisible = false;
                args.AcceptedOperation = DragOperations.Move;
            })
            .OnDrop<StackElement, Card>(card => inbox.Add(card));
        // </snippet:drop-indicator-overrides>
    }

    static void DropSafeFiles()
    {
        // <snippet:drop-safe-files>
        Border(TextBlock("Drop files here"))
            .OnDrop<BorderElement>(args =>
            {
                if (args.Data.TryGetSafeLocalFiles(out var files))
                    Import(files);
                args.AcceptedOperation = DragOperations.Copy;
            });
        // </snippet:drop-safe-files>
    }

    static void MoveOnConfirmation(Card card, List<Card> column)
    {
        // <snippet:move-on-confirmation>
        Border(TextBlock(card.Title))
            .OnDragStart<BorderElement, Card>(
                getPayload: () => card,
                allowedOperations: DragOperations.Move | DragOperations.Copy,
                onEnd: ctx =>
                {
                    if (ctx.WasCancelled) return;
                    if (ctx.CompletedOperation == DragOperations.Move)
                        column.Remove(card);  // confirmed move — safe to remove
                    // else: Copy succeeded, source keeps the item
                });
        // </snippet:move-on-confirmation>
    }

    static void Translate(Windows.Foundation.Point translation) { }
    static void SnapToGrid(Windows.Foundation.Point translation) { }
    static void Scale(double scale) { }
    static void Rotate(double angle) { }
    static void ShowContextMenu() { }
    static void Hover() { }
    static void Unhover() { }
    static void Log(string text) { }
    static Task<string> RenderExpensiveHtmlAsync(CancellationToken cancellationToken) =>
        Task.FromResult("<p>rich content</p>");
    static void Import(IReadOnlyList<IStorageItem> files) { }
}
