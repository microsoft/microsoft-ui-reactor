
# Element Pool

The Microsoft.UI.Reactor (Reactor) element pool recycles unmounted WinUI controls so a scroll-heavy
list or fast-mounting subtree doesn't allocate a fresh `TextBlock` /
`Button` / `Border` on every render. Each WinUI type gets its own
fixed-size stack (cap 32) inside `ElementPool`; the
[reconciler](reconciliation.md) calls `TryRent(typeof(T))` on mount
and `Return(element)` on unmount. This page documents the rental
contract, the cleanup pass that resets a returned element, and the
exclusions that keep pooling safe — composition-tainted visuals,
non-poolable types, and parent-state corner cases.

## Renting on mount

Every `MountXxx` handler in `Reconciler.Mount.cs` follows the same
shape: try the pool first, allocate fresh on miss.

```csharp
public FrameworkElement? TryRent(Type type)
{
    if (!Enabled) return null;
    if (!PoolableTypes.Contains(type)) return null;
    if (!_pools.TryGetValue(type, out var stack) || stack.Count == 0) return null;
    var item = stack.Pop();
    return item;
}
```

![Element-pool state machine: a control starts unpooled, transitions to Rented on Mount, to Returning on Unmount, and back to Pooled after ForceDetach + CleanElement — unless the per-type cap is exceeded or the control became compositor-tainted, in which case it falls out of pooling.](images/element-pool/rental-states.svg)

`TryRent` is three checks and a pop: pool enabled, type in the
poolable set, stack non-empty. Anything that fails returns `null` and
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
| `Button`, `TextBox`, `ToggleSwitch` | Interactive — pooled via Tag-based event trampoline | See "Why interactive controls are safe" below. |

Types not in this set are not pooled — every mount of a `ListView`,
`NumberBox`, `DatePicker`, etc. allocates fresh. That's deliberate:
the cleanup pass has to know how to reset every property a Reactor
modifier or `.Set` callback might have written, and adding a type to
`PoolableTypes` without a matching `CleanElement` case would leak
property state into the next reader's mount.

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

    // Don't pool elements that had GetElementVisual() called — they permanently
    // lose XAML implicit transition API access (OpacityTransition, etc.).
    if (IsCompositorTainted(element)) return;

    if (!_pools.TryGetValue(type, out var stack))
    {
        stack = new Stack<FrameworkElement>();
        _pools[type] = stack;
    }

    if (stack.Count >= MaxPerType) return;
```

The path runs in five steps after the type check: composition-taint
check (a visual that has had `GetElementVisual()` called permanently
loses access to XAML implicit transitions like `OpacityTransition`,
so it must not be pooled), per-type stack lookup, per-type cap check
(stop accepting at 32 instances), then — past the snippet boundary —
the detach-from-parent dance and `CleanElement(...)` reset before the
control finally pushes onto the stack. The scratch-panel round-trip
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
    /// Set via fluent extension methods: Text("hi").Margin(10).Width(200)
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
The fix is the Tag-based event trampoline: the WinUI subscription
attached at first mount stays attached, but it reads the *current*
element from the control's `Tag` at invocation time, so a recycled
control dispatching from its old subscription naturally routes to
the new element's `OnClick` after `Reconciler.SetElementTag` runs:

```csharp
private bool MarshalIfOffUIThread(string hookName, Action work)
{
    // Hot path — same thread that ran BeginRender. ~1ns TLS read + cmp + branch.
    if (Environment.CurrentManagedThreadId == _uiThreadId) return false;
```

`CleanElement` calls `ClearCurrentEventHandlers(fe)` on pool return,
which nulls the per-mount user-handler delegates without touching the
WinUI subscription. On the next mount, the trampoline finds null
handlers in the new Tag's element record, dispatches to nothing, and
costs essentially zero — until the user actually wires `.Click(...)`,
at which point the trampoline routes correctly. TASK-060 in
`Reconciler.cs` calls this out specifically: clearing the delegate
list on return is what stops a pooled control from firing the
previous component's captured closure into the next mount.

## Tips

**The pool is opt-in by type.** Adding a custom control to the pool
means adding it to `ElementPool.PoolableTypes` *and* extending
`CleanElement` with a switch arm that resets every property the
control might have carried. Skipping the second step leaks state.

**`GetElementVisual()` is one-way.** Once a control's composition
visual has been requested (typically for an `.Animate()` modifier),
the control can no longer use XAML implicit transitions and is
permanently un-poolable. `ElementPool.MarkCompositorTainted` records
this; the next `Return` for that instance silently drops it. Reach for
`.WithOpacityTransition()` and friends before `.Animate()` when both
work.

**The cap is 32 per type.** A burst of 100 list items mounts 32 from
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
