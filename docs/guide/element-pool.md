
# Element Pool

The Microsoft.UI.Reactor (Reactor) element pool recycles unmounted WinUI controls so a scroll-heavy
list or fast-mounting subtree doesn't allocate a fresh `TextBlock` /
`Button` / `Border` on every render. Each WinUI type gets its own
fixed-size stacks (cap 32 per lane) inside `ElementPool`; the
[reconciler](reconciliation.md) rents on mount and calls
`Return(element)` on unmount. This page documents the rental
contract, the two lanes that keep a control whose composition visual
was taken away from code that can't use it, the cleanup pass that
resets a returned element, and the exclusions that keep pooling safe —
transform-transition locks, non-poolable types, and parent-state
corner cases.

## Renting on mount

The standard descriptor-backed control mount path follows the same
shape: try the pool first, allocate fresh on miss. Composition-primitive
handlers in `Reconciler.Mount.cs` allocate their structural wrappers
directly.

```csharp
internal FrameworkElement? TryRent(Type type, bool visualLane)
{
    if (!Enabled) return null;
    if (!PoolableTypes.Contains(type)) return null;
    if (visualLane && _visualPools.TryGetValue(type, out var taken) && taken.Count > 0) return Rented(taken.Pop());
    if (!_pools.TryGetValue(type, out var stack) || stack.Count == 0) return null;
    return Rented(stack.Pop());
}
```

![Element-pool state machine: a control starts unpooled, transitions to Rented on Mount, to Returning on Unmount, and back to Pooled after ForceDetach + CleanElement — unless the lane's cap is exceeded or WinUI refused the reset, in which case it falls out of pooling.](images/element-pool/rental-states.svg)

`TryRent` is three checks and a pop: pool enabled, type in the
poolable set, a non-empty lane. Anything that fails returns `null` and
the caller falls through to `new TextBlock()` / `new WinUI.Button()`.
The exclusions are deliberate — the live-previewer can flip
`Pool.Enabled = false` when recycled controls with stale property
state would cause visual glitches, and the poolable type set is a
hardcoded `HashSet<Type>` because pooling a type the cleanup pass
doesn't know about would leak property state into the next user.

| Type | Why it's poolable | Notes |
|---|---|---|
| `TextBlock`, `RichTextBlock` | Static leaf; no event handlers; cheap reset | High-volume win in lists. |
| `StackPanel`, `Grid`, `Border` | Container shells; children cleared on return | Reused across most layouts. |
| `ScrollViewer`, `Canvas`, `Viewbox` | Single-`Content` controls; content nulled on return | Same shape. |
| `ProgressBar`, `ProgressRing` | Indeterminate flag + value reset to defaults | Stop-state restored on return. |
| `Image`, `InfoBadge` | `Source` / `Value` nulled | Image source pooling is the largest win. |
| `Button`, `TextBox`, `ToggleSwitch` | Interactive — pooled via the element-tag event trampoline | See "Why interactive controls are safe" below. |

Types not in this set are not pooled — every mount of a `ListView`,
`NumberBox`, `DatePicker`, etc. allocates fresh. That's deliberate:
the cleanup pass has to know how to reset every property a Reactor
modifier or `.Set` callback might have written, and adding a type to
`PoolableTypes` without a matching `CleanElement` case would leak
property state into the next reader's mount.

## Two lanes

Taking a control's composition visual (`ElementCompositionPreview.GetElementVisual`)
is permanent. Reactor takes it for `.Animate()`, `.Transition()`,
`.InteractionStates()`, `.Keyframes()`, `.LayoutAnimation()`,
`.ScrollLinked()`, the implicit transitions (`.OpacityTransition()`,
`.ScaleTransition()`, `.RotationTransition()`, `.TranslationTransition()`),
a non-identity `.Scale()`, `.Rotation()`, `.Translation()` or `.CenterPoint()`,
and the children of a `.Stagger()` panel. After that
WinUI refuses every write to the control's `Rotation`, `Scale`,
`CenterPoint`, `RotationAxis` and `TransformMatrix` facades and to its
XAML implicit transitions (`OpacityTransition` and friends), even an
identity write. So each type has two lanes:

| Lane | Holds | Who rents from it |
|---|---|---|
| Plain | Controls whose visual was never taken | Everyone, as before |
| Visual | Controls whose visual was taken, reset to a fresh control's pixels | Only a built-in element whose chain, including a keyed `Memo` over it, takes the visual at mount (`.Animate()`, an enter `.Transition()`, `.Keyframes()`, `.LayoutAnimation()`, `.ScrollLinked()`, an implicit transition other than `.BackgroundTransition()`, a non-identity transform modifier) and carries no `.Set` setters or `.OnMount` callbacks |

An eligible element takes from the visual lane first and falls back to
the plain lane. Every other element only takes from the plain lane, so
user code never meets a control it couldn't have met fresh: a
`Border().Set(b => b.Rotation = 10)` gets a plain control, never one
whose rotation facade would throw. `.InteractionStates()` and an
exit-only `.Transition()` don't make an element eligible, because a
fresh control keeps writable facades until the first hover or the
removal. Neither does an identity transform such as `.Scale(1)`, which never takes the
visual, so `Border().Scale(1).Ref(r)` can still write `r.Current.Rotation`.

`Return` picks the lane from a `VisualTaken` marker that every handoff
sets on the native control's attached state, so it holds whichever
managed wrapper reaches the control. Before a visual-lane control is
pushed, `CleanElement` stops every compositor animation on it, drops
its implicit animations, writes identity to the visual (opacity, scale,
rotation, center point, anchor point, orientation, transform matrix,
clip and translation), disables translation, zeroes the `Translation`
facade and forgets Reactor's composition bookkeeping. `Size` is left to layout, and
the renter gets its layout `Offset` back once it loads, because the reset runs after the
control left the tree. Navigation pages are never pooled, because a
navigation transition's completion can still reach them after they
leave the tree.

## Returning on unmount

`Return` is the more interesting half. A control coming back to the
pool may carry hundreds of WinUI property values, a parent reference,
event handler subscriptions, and (on interactive controls) the
trampoline state that drives Reactor's event dispatch:

```csharp
public void Return(FrameworkElement element)
{
    if (!Enabled) return;
    var type = element.GetType();
    if (!PoolableTypes.Contains(type)) return;
    // A handler may return its control through UnmountContext.ReturnControl and the engine
    // return it again. Pushing it twice would hand one control to two renters.
    if (_pooled.Contains(element)) return;

    // The lane follows the native control's marker, whichever wrapper reached it (#1643).
    var state = element.GetValue(Reconciler.ReactorAttached.StateProperty) as Reconciler.ReactorState;
    // A renter returned before it loaded must not stay on the window's layout list.
    if (state is not null) StopOpacitySync(element, state);
    if (state?.PoolExcluded == true)
    {
        if (state.ReleaseWhenExcluded)
        {
            Reconciler.ClearElementTag(element);
            Reconciler.ClearCurrentEventHandlers(element);
        }
        return;
    }
    var lanes = state?.VisualTaken == true ? _visualPools : _pools;

    if (!lanes.TryGetValue(type, out var stack))
    {
        stack = new Stack<FrameworkElement>();
        lanes[type] = stack;
    }

    if (stack.Count >= MaxPerType) return;
```

The path runs in five steps after the type check: lane choice from the
`VisualTaken` marker (a navigation page is dropped here), per-lane
stack lookup, per-lane cap check (stop accepting at 32 instances),
then — past the snippet boundary — the detach-from-parent dance and
`CleanElement(...)` reset before the control finally pushes onto its
lane. Reactor never sets the XAML implicit transitions, but a user
`.Set` can, so `CleanElement` clears them on a plain-lane control. Any
of them (`OpacityTransition`, `ScaleTransition`, `RotationTransition`,
`TranslationTransition`) locks the control's `RenderTransform` for good,
even once removed, so WinUI then refuses the `RenderTransform` reset and
the control is dropped instead. `CleanElement` also clears the
theme transitions `.WithTransitions(...)` writes (`ChildrenTransitions`,
`ChildTransitions`, `ContentTransitions`) and a panel's
`BackgroundTransition` in both lanes. The scratch-panel round-trip
in `ForceDetach` is the load-bearing piece: WinUI's internal parent
tracking can retain stale state that throws `COMException` when the
control is later re-parented, and `_scratchPanel.Children.Add` +
`Remove` forces WinUI to clear it.

The element record itself isn't pooled — only the realized WinUI
control on the other side:

```csharp
public abstract record Element
{
    /// <summary>
    /// Optional key for stable identity across re-renders (like React's key prop).
    /// When set, the reconciler uses it to match elements across list reorderings.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>
    /// Layout modifiers (margin, padding, size, alignment, etc.) applied to this element.
    /// Set via fluent extension methods: TextBlock("hi").Margin(10).Width(200)
    /// Modifiers are stored inline so the concrete element type is preserved through chaining.
    /// </summary>
    public ElementModifiers? Modifiers { get; init; }
```

Records are cheap; the heavy classes are the WinUI controls that
allocate template instances, brush caches, and visual state machines.
Pooling the records would save nothing because the reconciler creates
fresh records every render by design.

## Why interactive controls are safe to pool

A `Button.Click` handler that closes over the captured rerender
closure cannot be left dangling on a pooled control — the next user
of that control would receive clicks targeting the wrong component.
The fix is the element-tag event trampoline: the WinUI subscription
attached at first mount stays attached, but the handler is a `static`
delegate that reads the *current* element back off the control's
`ReactorAttached.StateProperty` at invocation time, so a recycled
control dispatching from its old subscription naturally routes to
the new element's `OnClick` once the reconciler has re-tagged it:

```csharp
private static readonly global::Microsoft.UI.Xaml.RoutedEventHandler __ClickTrampoline = (s, _) =>
{
    if (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag((WinUI.Button)s!) is ButtonElement live)
    {
        if (live.IsDisabledFocusable) return;
        if (live.OnClick is not null) live.OnClick();
        else if (live.Command is not null) global::Microsoft.UI.Reactor.Core.CommandBindings.Invoke(live.Command);
    }
};
```

`CleanElement` calls `Reconciler.ClearElementTag(fe)` and
`Reconciler.ClearCurrentEventHandlers(fe)` on pool return, which drop
the element pointer and null the per-mount user-handler delegates
without touching the WinUI subscription. On the next mount, the
trampoline finds no element (or an element with null callbacks),
dispatches to nothing, and costs essentially zero — until the user
actually wires `.Click(...)`, at which point the trampoline routes
correctly. The `SECURITY (TASK-060)` comment above that call in
`ElementPool.CleanElement` calls this out specifically: clearing the
delegates on return is what stops a pooled control from firing the
previous component's captured closure into the next mount.

## Tips

**The pool is opt-in by type.** Adding a custom control to the pool
means adding it to `ElementPool.PoolableTypes` *and* extending
`CleanElement` with a switch arm that resets every property the
control might have carried. Skipping the second step leaks state.

**Compositor features pool too.** A list item with `.Animate()`,
`.Transition()` or `.LayoutAnimation()` returns to the visual lane and
the next such item reuses it, so it pays the visual reset instead of a
fresh control plus a new handoff. Keep `.Set(...)` and `.OnMount(...)`
off those chains where you can: a chain with either only rents from
the plain lane.

**The cap is 32 per type and lane.** A burst of 100 list items mounts 32 from
the pool and 68 fresh; the next render returns 32 to the pool and
drops 36. That's fine for steady-state scrolling — the working set
stabilizes around the visible window — but doesn't scale to giant
flat lists. Reach for [`VirtualList`](collections.md) for those.

**Disable the pool when you need pristine state.** The live previewer
sets `Pool.Enabled = false` because hot-reloading new code into a
recycled control with old property state causes visible glitches.
Test fixtures that snapshot-compare WinUI properties on mount should
do the same.

## Next Steps

- **[Reconciliation](reconciliation.md)** — Previous: where Mount and Unmount call `TryRent` / `Return`.
- **[Collections](collections.md)** — Next: the heaviest user of pooling (virtualized list items).
- **[Source mapping](source-mapping.md)** — How pool reuse interacts with per-control source attribution.
- **[Architecture overview](architecture-overview.md)** — Where the pool sits in the render loop.
