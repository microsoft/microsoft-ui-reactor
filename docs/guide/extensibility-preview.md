# Extensibility preview (V1 protocol)

> **Status: provisional surface.** The V1 handler protocol ships in Phase 1
> of [spec 047](../specs/047-extensible-control-model.md) behind a feature
> flag. Every public type below is marked `[Experimental("REACTOR_V1_PREVIEW")]`
> and may change after Phase 2's descriptor-vs-handler measurement gate.
> If you take a dependency on this surface, accept the breaking-change risk
> through Phase 2 — see the [spec exit gate](../specs/047-extensible-control-model.md#14-suggested-phasing).

This page documents what's available today for authoring controls outside
`Reactor.dll` — for the split-library plan and for downstream consumers
(Pix, Monaco, Win2D-wrapped controls, etc.).

The protocol intentionally shrinks the gap between "first-party Reactor
control" and "externally-registered control": every invariant a built-in
honors (pool survival, echo suppression, attached-DP tag refresh, modifier
composition, AOT cleanliness) is reachable from the public surface.

## Enabling the V1 path

V1 dispatch is off by default. Two ways to opt in:

```csharp
// Per-Reconciler — wins over the global switch.
var reconciler = new Reconciler(logger: null, useV1Protocol: true);

// Process-wide fallback.
AppContext.SetSwitch("Reactor.UseV1Protocol", true);
```

When `UseV1Protocol` is **off**, the V1 handler registry is skipped
entirely; ported controls fall through to the legacy `MountXxx` switch.
That's what makes diff-on-same-binary safe — flip the flag, the same
controls travel either path.

The dispatch order when V1 is **on** is:

1. `V1HandlerRegistry` — built-in handlers ported in Phase 1 and any
   external handlers registered via `RegisterHandler`.
2. `_typeRegistry` — the legacy `RegisterType` API (still supported).
3. Legacy `MountXxx` switch — everything else.

## The handler interface

```csharp
public interface IElementHandler<TElement, TControl>
    where TElement : Element
    where TControl : UIElement
{
    TControl Mount(MountContext ctx, TElement element);
    void Update(UpdateContext ctx, TElement oldEl, TElement newEl, TControl control);
    void Unmount(UnmountContext ctx, TControl control) { }
    ChildrenStrategy<TElement, TControl>? Children => null;
}
```

`Update` returns `void`. Substitution-mid-update is forbidden in v1 — type
changes flow through unmount-and-remount (see [§13 Q12](../specs/047-extensible-control-model.md#13-future-design-session-questions)).

A worked example (the in-tree `ToggleSwitchHandler` shape):

```csharp
internal sealed class ToggleSwitchHandler
    : IElementHandler<ToggleSwitchElement, WinUI.ToggleSwitch>
{
    public WinUI.ToggleSwitch Mount(MountContext ctx, ToggleSwitchElement el)
    {
        var ctrl = ctx.RentControl<WinUI.ToggleSwitch>();
        var bind = ctx.BindFor(ctrl, el);

        // Tight-diff WriteSuppressed: only suppress when the write would
        // actually fire Toggled. A no-op write is no-op.
        if (ctrl.IsOn != el.IsOn)
            bind.WriteSuppressed(() => ctrl.IsOn = el.IsOn);

        ctrl.OnContent = el.OnContent;
        ctrl.OffContent = el.OffContent;

        // The closure captures `ctrl` but reads back the current element
        // from the tag each time it fires — this is what makes the
        // subscription survive re-renders without re-attaching.
        bind.OnCustomEvent<RoutedEventArgs>(
            subscribe:   (c, h) => ((WinUI.ToggleSwitch)c).Toggled += new RoutedEventHandler(h),
            unsubscribe: (c, h) => ((WinUI.ToggleSwitch)c).Toggled -= new RoutedEventHandler(h),
            handler:     (cur, _) => cur.OnIsOnChanged?.Invoke(((WinUI.ToggleSwitch)ctrl).IsOn));

        ctx.ApplySetters(el.Setters, ctrl);
        return ctrl;
    }

    public void Update(UpdateContext ctx, ToggleSwitchElement o, ToggleSwitchElement n, WinUI.ToggleSwitch ctrl)
    {
        if (o.IsOn != n.IsOn)
            ctx.BindFor(ctrl, n).WriteSuppressed(() => ctrl.IsOn = n.IsOn);
        if (o.OnContent != n.OnContent) ctrl.OnContent = n.OnContent;
        if (o.OffContent != n.OffContent) ctrl.OffContent = n.OffContent;
        ctx.ApplySetters(n.Setters, ctrl);
    }

    public ChildrenStrategy<ToggleSwitchElement, WinUI.ToggleSwitch>? Children
        => new None<ToggleSwitchElement, WinUI.ToggleSwitch>();
}
```

Register through the public API:

```csharp
reconciler.RegisterHandler(new ToggleSwitchHandler());
```

Duplicate registration throws `InvalidOperationException` — including a
duplicate against a built-in or against a handler already in
`_typeRegistry`. Open generics are rejected at registration time. There
is no `RegisterOverride` verb in v1 ([§13 Q9](../specs/047-extensible-control-model.md#13-future-design-session-questions)).

## Mount / Update / Unmount contexts

All three are `readonly ref struct` — allocation-free, stack-only. They
expose exactly the engine machinery handlers are allowed to touch:

```csharp
public readonly ref struct MountContext
{
    public Action RequestRerender { get; }
    public UIElement? MountChild(Element child);
    public void ApplySetters<T>(Action<T>[] setters, T control) where T : class;
    public ReactorBinding<TElement> BindFor<TElement>(FrameworkElement control, TElement element)
        where TElement : Element;
    public T RentControl<T>(PoolPolicy<T>? policy = null, Func<T>? factory = null)
        where T : class, new();
    public IDisposable PushContext<T>(T value);
    public IDisposable PushStaggerScope(TimeSpan delay);
    public void AddRawRoutedHandler(UIElement target, RoutedEvent re, Delegate h, bool handledEventsToo);
}
```

`UpdateContext` is the same surface minus `RentControl` (Update never
allocates). `UnmountContext` exposes `RequestRerender` and a
`ReturnControl<T>` pool-return hook.

## ReactorBinding\<T\> — events and writes

```csharp
public readonly struct ReactorBinding<TElement> where TElement : Element
{
    // True routed events (pointer/key/tap/focus/context).
    public void OnPointerPressed(Action<TElement, PointerRoutedEventArgs> handler);
    public void OnTapped(Action<TElement, TappedRoutedEventArgs> handler);
    public void OnKeyDown(Action<TElement, KeyRoutedEventArgs> handler);
    public void OnGotFocus(Action<TElement, RoutedEventArgs> handler);
    public void OnLostFocus(Action<TElement, RoutedEventArgs> handler);
    // ... full family — pointer/key/tap/focus

    // Plain CLR events (Toggled, Click, ValueChanged, TextChanged, custom).
    public void OnCustomEvent<TArgs>(
        Action<FrameworkElement, EventHandler<TArgs>> subscribe,
        Action<FrameworkElement, EventHandler<TArgs>> unsubscribe,
        Action<TElement, TArgs> handler);

    // The only correct way to write a value-bearing DP from Mount/Update.
    public void WriteSuppressed(Action mutate);
}
```

The `On*` family writes through the engine's shared trampoline — wire
once, refresh-via-tag on every render. The `OnCustomEvent` path uses
the per-control event-state box (see "Per-control event state" below)
so its subscriptions survive pool reset.

`WriteSuppressed` is the public primitive ([§13 Q19](../specs/047-extensible-control-model.md#13-future-design-session-questions)).
The body swaps in Phase 4 when the echo audit ships its tighter diff
machinery; **the signature is stable from day one**.

There is also a static convenience:

```csharp
public static class ReactorBinding
{
    public static void WriteSuppressed(UIElement target, Action mutate);
    public static void WriteSuppressed<T>(T target, Action<T> mutate) where T : UIElement;
}
```

— useful when you have a control reference but no `ReactorBinding<T>`
instance (e.g., a setter chain mutating a child of the bound control).

## Children strategies

Containers declare a `ChildrenStrategy` instead of writing children-
reconciliation by hand:

```csharp
public override ChildrenStrategy<BorderElement, WinUI.Border>? Children { get; } =
    new SingleContent<BorderElement, WinUI.Border>(
        GetChild: el => el.Child,
        SetChild: (ctrl, ui) => ctrl.Child = ui);
```

Six variants ship:

| Variant | Use case | Phase 1 status |
|---|---|---|
| `None<TE, TC>` | Leaf controls (TextBlock, Image, ToggleSwitch). | Full. |
| `SingleContent<TE, TC>(GetChild, SetChild)` | Single-slot content (Border, ContentPresenter). | Full. |
| `Panel<TE, TC>(GetChildren, GetCollection)` | Flat panels (StackPanel, Grid, Canvas). | Naive append/clear-and-readd in v1; keyed-reconcile integration lands in Phase 3. |
| `NamedSlots<TE, TC>(slots)` | Header/content/footer-style (Expander, NavigationView). | Full. |
| `ItemsHost<TE, TC>(GetItemsSource, GetContainer, Options)` | Templated items hosts (ListView, ItemsView). Plugs into spec-042 keyed reconcile. | Shape only; concrete dispatch goes through the existing ChildReconciler. |
| `Imperative<TE, TC>(Reconcile)` | Escape hatch for irregular containers. | Full. |

Attached-prop writers (`Grid.Row`, `Canvas.Left`, `DockPanel.Dock`) ship
as a separate type for container handlers to declare:

```csharp
public sealed record AttachedPropWriter<TChildElement>(
    string Name,
    Func<TChildElement, object?> Get,
    Action<UIElement, object?> Write)
    where TChildElement : Element;
```

## The pool contract

`PoolPolicy<TControl>` is the documented contract for pool participation
([§13 Q18](../specs/047-extensible-control-model.md#13-future-design-session-questions)):

```csharp
public sealed class PoolPolicy<TControl> where TControl : class
{
    public bool IsPoolable { get; init; } = true;
    public Action<TControl>? Reset { get; init; }
}
```

- `IsPoolable = false` — opt-out for controls with persistent native
  resources (some `MediaPlayerElement` configurations, custom controls
  holding unmanaged buffers, etc.).
- `Reset` — additional reset beyond the default contract.

Pool reset enumerated:

1. Clear `ControlEventState` box (the per-control event payload).
2. Clear shared `ModifierEventHandlerState` (`state.Events`) — sets all
   `Current<Event>` delegates to null; trampolines stay attached and
   become no-ops on the next fire if state.Element is null.
3. Clear `ReactorAttached.StateProperty` (element tag).
4. Clear Reactor-set `DataContext`.
5. Invoke `policy?.Reset(control)` last.

Pool key: `typeof(TControl)` only. Finer keys (e.g., `(typeof(TextBlock),
styleKey)`) are deferred until a concrete consumer needs them.

Dual-RCW: `ReturnControl` is idempotent — calling it twice on the same
native control does not double-clear and does not double-pool.

## Per-control event state

Control-intrinsic events (`Toggled`, `Click`, `ValueChanged`, `TextChanged`,
…) live in a per-control payload inside the existing
`ReactorAttached.StateProperty`. The discriminated wrapper:

```csharp
internal sealed class ControlEventStateBox
{
    public Type HandlerType;   // identity of the handler that wrote Payload
    public object Payload;     // the per-control state struct/class
}
```

A handler reads `ControlEventState` only after verifying
`HandlerType == typeof(this-handler-payload)`. The pool reset path clears
the entire box on `ReturnControl` so stale payloads never survive into a
new tenant.

This split is documented in [§9.2](../specs/047-extensible-control-model.md#92-a-revised-split).
Phase 1 ships the shape and storage; the lift of every per-event slot
out of the shared `EventHandlerState` happens during Phase 3 / Phase 4.

## UI-thread guarantee

Per [§13 Q14](../specs/047-extensible-control-model.md#13-future-design-session-questions),
Mount/Update/Unmount handlers run on the reconciler's UI dispatcher. You
can access control state freely without synchronization. Debug builds
include a `DispatcherQueue.HasThreadAccess` assertion; the Release-mode
tightening is a Phase 1 measurement deferral
([phase1-results/q14-dispatcher-affinity.md](../specs/047/phase1-results/q14-dispatcher-affinity.md)).

Off-thread Mount is intentionally **not** part of v1. Adding it later is
non-breaking.

## Reactor.Compile.Analyzer

Three diagnostics ship alongside the V1 protocol:

| Id | Rule | Status |
|---|---|---|
| `REACTOR1001` | String-form event reference resolves against the control type. | Phase 2 (waits on descriptor model). |
| `REACTOR1002` | `OnCustomEvent<TArgs>` — `TArgs` matches the EventArgs of `+=` / `-=` events inside the subscribe/unsubscribe lambdas. | Active. |
| `REACTOR1003` | `Prop.Controlled` — `readBack` return type matches the `set` lambda's value type. | Phase 2 (waits on descriptor model). |

REACTOR1002 catches the common authoring mistake of wrapping a delegate
across an `EventArgs` mismatch — for example:

```csharp
// Compiles, but bridges across types. Analyzer flags this.
bind.OnCustomEvent<WidgetEventArgs>(
    subscribe:   (c, h) => ((ToggleSwitch)c).Toggled += new RoutedEventHandler(h),
    unsubscribe: (c, h) => ((ToggleSwitch)c).Toggled -= new RoutedEventHandler(h),
    handler:     (cur, args) => ...);  // args.GetType() != WidgetEventArgs at runtime
```

Consume the analyzer through `<PackageReference Include="Reactor.Compile.Analyzer" PrivateAssets="all" />`.

## What is NOT in v1

- `RegisterOverride` — no override verb. Test fakes compose a Reconciler
  with the registry they need. Additive later if a real scenario surfaces.
- Open-generic registrations — `typeof(DataGrid<>)` throws.
- Off-thread Mount — UI-thread-only.
- `mostRecentEventCount` round-trip — rejected at Phase 0; one site
  (ColorPicker) carries an imperative shim instead.
- Source-generated handlers — explicitly deferred ([§7](../specs/047-extensible-control-model.md#7-simplification-direction-source-generated-handlers)).
- Descriptor authoring — Phase 2 measurement gate.

## Authoring a Win2D / external control: the full checklist

1. Add a project reference to `Microsoft.UI.Reactor` (no `InternalsVisibleTo`).
2. Add `<NoWarn>$(NoWarn);REACTOR_V1_PREVIEW</NoWarn>` to opt into the
   provisional surface.
3. Define the `Element` record. Use `Action<TControl>[] Setters` if you
   want the `.Set(...)` modifier chain. Don't override `HasCallbacks`
   unless you have a callback prop.
4. Author the `IElementHandler<TElement, TControl>`.
5. Use `ctx.RentControl<TControl>(policy)` to allocate — don't `new`
   directly. The pool participates only for types you opt in.
6. Use `ctx.BindFor(control, element)` to wire events. Use
   `.WriteSuppressed(...)` for every value-bearing-DP write.
7. Call `ctx.ApplySetters(el.Setters, control)` last in Mount/Update.
8. Register through `reconciler.RegisterHandler(handler)` once per
   reconciler instance.

A working test-only example lives in
[`tests/external_proof/Reactor.External.TestControl`](https://github.com/microsoft/microsoft-ui-reactor/tree/main/tests/external_proof/Reactor.External.TestControl).
It hosts `MarqueeControl` — a tiny `UserControl` with one value-bearing
prop and one custom event — entirely outside `Reactor.dll` with no
`InternalsVisibleTo` and serves as the Phase 1 exit-gate proof.
