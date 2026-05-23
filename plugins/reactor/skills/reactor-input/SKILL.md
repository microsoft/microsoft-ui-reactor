---
name: reactor-input
description: "Reactor input — gestures (`OnPan`, `OnPinch`, `OnRotate`), pointer events, drag-and-drop (`OnDragStarting`), focus management (`UseElementFocus`, `UseFocus`, `UseFocusTrap`). Use when implementing direct manipulation or custom focus behavior."
---

# Input and Gestures in Reactor

Reactor exposes input via **trampoline-dispatched `.On*` modifiers**. Events
auto-enable their underlying WinUI flags (`ManipulationMode`, `AllowDrop`,
etc.) when you attach a handler, so you never need to set those manually.

## Quick reference

| API | Purpose |
|-----|---------|
| `.OnPointerEntered/Exited/Pressed/Released/Moved` | Pointer events |
| `.OnTapped()` / `.OnDoubleTapped()` / `.OnRightTapped()` | Tap events |
| `.OnKeyDown()` / `.OnKeyUp()` | Keyboard events |
| `.OnPan(...)` | Pan gesture (drag with inertia) |
| `.OnPinch(...)` | Pinch-to-zoom gesture |
| `.OnRotate(...)` | Rotation gesture |
| `.OnLongPress(...)` | Press-and-hold |
| `.OnGotFocus()` / `.OnLostFocus()` | Focus change events |
| `UseElementFocus()` | Untyped ref + dispatcher-scheduled focus |
| `UseElementRef<T>()` | Typed element ref — `.Current` is `T` (no cast) |
| `.AccessKey("S")` | Alt+key keyboard shortcut |
| `.OnDragStart(...)` / `.OnDrop(...)` | Drag-and-drop |

## 1. Pointer events

```csharp
var (isHovered, setIsHovered) = UseState(false);

return Border(child)
    .OnPointerEntered((s, e) => setIsHovered(true))
    .OnPointerExited((s, e) => setIsHovered(false))
    .Background(isHovered ? Theme.SubtleFill : Colors.Transparent);
```

Events: `OnPointerEntered`, `OnPointerExited`, `OnPointerPressed`,
`OnPointerReleased`, `OnPointerMoved`, `OnPointerCanceled`.

The `(s, e)` signature gives you the sender and `PointerRoutedEventArgs`.

## 2. Tap events

```csharp
Border(child)
    .OnTapped((s, e) => HandleClick())
    .OnDoubleTapped((s, e) => HandleDoubleClick())
    .OnRightTapped((s, e) => ShowContextMenu())
```

## 3. Keyboard events

```csharp
TextBox(text, setText)
    .OnKeyDown((s, e) =>
    {
        if (e.Key == VirtualKey.Enter)
        {
            Submit();
            e.Handled = true;
        }
    })
```

## 4. Continuous gestures (Pan, Pinch, Rotate)

Gestures follow a **phase lifecycle**: `Began → Changed (repeat) → Ended | Cancelled`.

### Pan gesture

```csharp
var (offset, setOffset) = UseState(new Point(0, 0));

return Border(child)
    .OnPan(
        minimumDistance: 10,  // pixels before gesture starts
        axis: PanAxis.Both,  // or Horizontal, Vertical
        withInertia: true,   // momentum after release
        onBegan: (e) => { /* pan began */ },
        onChanged: (e) =>
        {
            setOffset(new Point(
                offset.X + e.Delta.Translation.X,
                offset.Y + e.Delta.Translation.Y));
        },
        onEnded: (e) => { /* pan ended */ })
    .Translation((float)offset.X, (float)offset.Y, 0);
```

### 60Hz pan pattern (performance-critical)

For smooth dragging, write `Translation` directly via ref during the
gesture and only `setState` at the end:

```csharp
var itemRef = UseRef<UIElement>();
var (finalPos, setFinalPos) = UseState(new Point(0, 0));

return Border(child)
    .Ref(itemRef)
    .OnPan(
        onChanged: (e) =>
        {
            // Direct property write — no re-render per frame
            if (itemRef.Current is { } el)
            {
                var t = el.Translation;
                el.Translation = new System.Numerics.Vector3(
                    t.X + (float)e.Delta.Translation.X,
                    t.Y + (float)e.Delta.Translation.Y,
                    t.Z);
            }
        },
        onEnded: (e) =>
        {
            // Sync state once at end
            if (itemRef.Current is { } el)
                setFinalPos(new Point(el.Translation.X, el.Translation.Y));
        });
```

### Pinch and Rotate

```csharp
var (scale, setScale) = UseState(1.0f);

return Border(child)
    .OnPinch(
        onChanged: (e) => setScale(scale * (float)e.Delta.Scale),
        onEnded: (e) => { })
    .Scale(scale);

// Rotation:
var (angle, setAngle) = UseState(0f);

return Border(child)
    .OnRotate(
        onChanged: (e) => setAngle(angle + (float)e.Delta.Rotation),
        onEnded: (e) => { })
    .Rotation(angle);
```

## 5. Long press

Touch/pen has built-in long press; mouse requires emulation:

```csharp
Border(child)
    .OnLongPress(
        onTriggered: (e) => ShowEditMode(),
        enableMouseEmulation: true)  // also trigger on mouse press-and-hold
```

## 6. Focus management

### UseElementFocus

```csharp
var focusRef = UseElementFocus();

return VStack(12,
    TextBox(text, setText).Ref(focusRef.Ref),
    Button("Focus the field", () => focusRef.RequestFocus())
);
```

`UseElementFocus` returns a handle with `.Ref` (attach to the element)
and `.RequestFocus()` (imperatively focus it).

### UseElementRef\<T\> — typed refs

When you actually need to call methods on the underlying control (`SelectAll()`
on a `TextBox`, `Focus(FocusState.Programmatic)`, etc.), use the typed variant.
`.Current` is already typed as `T` — no cast at the call site:

```csharp
var inputRef = UseElementRef<TextBox>();

UseEffect(() => inputRef.Current?.SelectAll(), Array.Empty<object>());

return TextBox(query, setQuery).Ref(inputRef);
```

The constraint is `T : FrameworkElement`. In `DEBUG` builds Reactor asserts
the actual mounted element is a `T`; in release the mismatch is silent and
`.Current` returns `null`. Spec 033 §3.

### Focus events

```csharp
TextBox(text, setText)
    .OnGotFocus((s, e) => ShowSuggestions())
    .OnLostFocus((s, e) => HideSuggestions())
```

### Access keys (keyboard shortcuts)

```csharp
Button("Save", onSave).AccessKey("S")  // Alt+S
```

### Tab order

```csharp
TextBox(name, setName).IsTabStop(true).TabIndex(1)
TextBox(email, setEmail).IsTabStop(true).TabIndex(2)
Button("Submit", onSubmit).IsTabStop(true).TabIndex(3)
```

## 7. Drag and drop

Reactor's drag-drop system supports **typed in-process payloads** (via a
transfer registry) and standard formats for cross-process drops.

### Basic drag source + drop target

```csharp
// Drag source — provide a DragData factory
Border(TextBlock(item.Name))
    .OnDragStart(
        getData: () => DragData.Typed(item),
        allowedOperations: DragOperations.Copy | DragOperations.Move)

// Drop target — typed overload auto-extracts payload
Border(TextBlock("Drop here"))
    .OnDrop<BorderElement, Item>(
        onDrop: (droppedItem) => HandleDrop(droppedItem))
```

### Typed in-process payloads

For type-safe object transfer within the same app, use the typed
`OnDragStart<T, TPayload>` and `OnDrop<T, TPayload>` overloads:

```csharp
// Drag source — typed payload via getPayload factory
Border(TextBlock(item.Name))
    .OnDragStart<BorderElement, Item>(
        getPayload: () => item,
        allowedOperations: DragOperations.Move,
        onEnd: (ctx) =>
        {
            if (ctx.CompletedOperation == DragOperations.Move && !ctx.WasCancelled)
                RemoveItem(item);  // only remove after confirmed drop
        })

// Drop target — typed extraction
Border(TextBlock("Drop here"))
    .OnDrop<BorderElement, Item>(
        onDrop: (droppedItem) => MoveItem(droppedItem),
        acceptedOps: DragOperations.Move)
```

### Untyped drop target (raw DragTargetArgs)

For cross-process drops or custom handling:

```csharp
Border(TextBlock("Drop here"))
    .OnDragOver((args) =>
    {
        args.UIOverride.Caption = "Copy here";
        args.UIOverride.IsCaptionVisible = true;
    })
    .OnDrop((args) =>
    {
        if (args.Data.TryGetTypedPayload<Item>(out var item))
            HandleDrop(item);
    })
```

### DragUI customization

Via `args.UIOverride` on drag-over handlers:

```csharp
.OnDragOver((args) =>
{
    args.UIOverride.IsCaptionVisible = true;
    args.UIOverride.IsContentVisible = true;
    args.UIOverride.IsGlyphVisible = false;
    args.UIOverride.Caption = "Move to folder";
})
```

## Critical gotchas

1. **CANNOT combine `OnPan` and `OnTapped` on the same element.** They
   conflict via `ManipulationMode`. If you need both, put `OnTapped` on a
   child and `OnPan` on the parent.
2. **60Hz pan pattern** — for smooth drag UIs, write `Translation` directly
   via ref during `onChanged`, and only `setState` in `onEnded`.
3. **Gesture phase lifecycle** — always handle both `onEnded` and
   `onCancelled` to avoid stuck state.
4. **`AllowDrop` auto-enables** — just attaching `.OnDrop()` sets the flag.
   Don't set it manually.
5. **Typed payloads are in-process only** — `TryGetTypedPayload<T>` returns
   false for cross-app drops. Always check the return value.
6. **Use `e.Handled = true`** on keyboard events to prevent bubbling when
   you've handled the key.
7. **Mouse long-press needs `enableMouseEmulation: true`** — touch/pen have
   native hold detection, but mouse does not.
