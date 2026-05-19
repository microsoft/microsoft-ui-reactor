using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<DragReorderApp>("Drag-Reorder Recipe", width: 360, height: 440
#if DEBUG
    , preview: true
#endif
);

class DragReorderApp : Component
{
    public override Element Render() => Component<TaskList>();
}

// <snippet:data>
// Identity lives on the record. Moves preserve `Id`, so the reconciler keeps
// the same row element and its focus state across a reorder.
record TaskItem(int Id, string Title);

static class Seed
{
    public static readonly TaskItem[] Initial = new[]
    {
        new TaskItem(1, "Write the recipe lead"),
        new TaskItem(2, "Wire the drag source"),
        new TaskItem(3, "Wire the drop target"),
        new TaskItem(4, "Add the keyboard alternative"),
        new TaskItem(5, "Land the snippets"),
        new TaskItem(6, "Run tier-lint"),
    };
}
// </snippet:data>

class TaskList : Component
{
    public override Element Render()
    {
        // <snippet:state>
        // The list itself is a UseState<List<TaskItem>>. `draggingId` tracks
        // the row currently being dragged so we can dim it; `hoverId` tracks
        // the drop target so we can draw the insertion hint. Both reset on
        // drop-completed.
        var (items, setItems) = UseState<List<TaskItem>>(Seed.Initial.ToList());
        var (draggingId, setDraggingId) = UseState<int?>(null);
        var (hoverId, setHoverId) = UseState<int?>(null);
        var (focusedId, setFocusedId) = UseState(Seed.Initial[0].Id);
        // </snippet:state>

        // <snippet:move>
        // Splice a single item from `fromIndex` to `toIndex`. The reconciler
        // keys rows by `Id`, so this is a pure data move — no row remounts,
        // no lost focus, no animation seam.
        void Move(int fromIndex, int toIndex)
        {
            if (fromIndex == toIndex) return;
            var copy = new List<TaskItem>(items);
            if (fromIndex < 0 || fromIndex >= copy.Count) return;
            toIndex = System.Math.Clamp(toIndex, 0, copy.Count - 1);
            var item = copy[fromIndex];
            copy.RemoveAt(fromIndex);
            copy.Insert(toIndex, item);
            setItems(copy);
        }

        void MoveById(int sourceId, int targetId)
        {
            var from = items.FindIndex(i => i.Id == sourceId);
            var to = items.FindIndex(i => i.Id == targetId);
            if (from >= 0 && to >= 0) Move(from, to);
        }
        // </snippet:move>

        // <snippet:keyboard>
        // Alt+Up / Alt+Down moves the focused row. This is the load-bearing
        // accessibility story — drag-and-drop alone fails screen-reader and
        // motor-impaired users; a keyboard alternative makes the recipe
        // WCAG-conformant.
        void HandleKey(int rowId, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            var alt = (Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(VirtualKey.Menu)
                & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            if (!alt) return;

            var idx = items.FindIndex(i => i.Id == rowId);
            if (idx < 0) return;

            if (e.Key == VirtualKey.Up && idx > 0)
            {
                Move(idx, idx - 1);
                setFocusedId(rowId);
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Down && idx < items.Count - 1)
            {
                Move(idx, idx + 1);
                setFocusedId(rowId);
                e.Handled = true;
            }
        }
        // </snippet:keyboard>

        // <snippet:render>
        Element Row(TaskItem item)
        {
            var isDragging = draggingId == item.Id;
            var isHover = hoverId == item.Id && draggingId is not null && draggingId != item.Id;
            var isFocused = focusedId == item.Id;

            return HStack(8,
                    TextBlock("☰").Opacity(0.4).Width(20),     // grab handle glyph
                    TextBlock(item.Title)
                )
                .Padding(10)
                .Background(isFocused ? "#EEF4FB" : "#FFFFFF")
                .WithBorder(isHover ? "#0078D4" : "#E1E1E1", isHover ? 2 : 1)
                .Opacity(isDragging ? 0.4 : 1.0)
                .IsTabStop(true)
                .OnGotFocus((_, _) => setFocusedId(item.Id))
                .OnKeyDown((_, e) => HandleKey(item.Id, e))
                .OnDragStart<StackElement, int>(
                    getPayload: () => { setDraggingId(item.Id); return item.Id; },
                    allowedOperations: DragOperations.Move,
                    onEnd: _ => { setDraggingId(null); setHoverId(null); })
                .OnDragEnter(args =>
                {
                    if (args.Data.TryGetTypedPayload<int>(out var srcId) && srcId != item.Id)
                        setHoverId(item.Id);
                })
                .OnDrop<StackElement, int>(srcId =>
                {
                    MoveById(srcId, item.Id);
                    setDraggingId(null);
                    setHoverId(null);
                }, acceptedOps: DragOperations.Move);
        }

        return VStack(8,
            Heading("Reorder tasks"),
            TextBlock("Drag a row, or focus one and press Alt+Up / Alt+Down.")
                .Opacity(0.7),
            VStack(4,
                items.Select(Row).ToArray()
            )
        ).Padding(16).Width(320);
        // </snippet:render>
    }
}
