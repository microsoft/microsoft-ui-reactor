# Control-Reference Properties & the Reactor Graph Model — Design

## Status

**Accepted — design + phased plan; implemented through Phase 3.1.** Resolves [issue #456](https://github.com/microsoft/microsoft-ui-reactor/issues/456)
("general model for control properties that reference another control"). The
first external consumer is already shipping against the gap this spec closes —
the ArcGIS Maps SDK .NET Toolkit's Reactor bindings
(`Toolkit.Reactor/Factories.ToolkitHandlers.cs`) wire every toolkit control
(`Compass`, `ScaleLine`, `Legend`, `OverviewMap`, `FloorFilter`, `SearchView`,
`BookmarksView`, `MeasureToolbar`, `UtilityNetworkTraceTool`) to a shared
`GeoView`/`MapView` through an `ElementRef<GeoView>` read at mount. That pattern
works by accident of mount ordering and has a latent late-binding bug (§2.3);
this spec makes it correct, reactive, and a first-class authoring surface.

Two design decisions are ratified up front (the questions that shaped this doc):

- **D1 — resolution lives entirely in the reconciler.** Element records carry a
  strongly-typed `ElementRef<T>?` property; a descriptor *reference entry* (or
  the equivalent handler-binding call) declares the `ref → target-property`
  edge; the engine owns subscription, ordering, write, recreation-survival, and
  teardown. Authors never read `ref.Current` from a handler. This removes the
  order-of-construction problem by construction (§4, §6).
- **D2 — circular references are a tested Phase-1 guarantee.** A synthetic
  torture-test control family exercises every reference topology we can name
  (chain, fan-out, fan-in, bidirectional, n-cycle, self, parent/child, diamond,
  late-mount, conditional, reorder, pool-recycle). Cycles are not "tolerated";
  they are proven (§3.3, §9).

---

## Table of Contents

- [§1 Motivation](#1-motivation)
- [§2 The gap today](#2-the-gap-today)
- [§3 The graph model](#3-the-graph-model)
- [§4 `ElementRef` as a reactive cell](#4-elementref-as-a-reactive-cell)
- [§5 Author-facing API](#5-author-facing-api)
- [§6 Engine: the reference-binding entry](#6-engine-the-reference-binding-entry)
- [§7 Reconciler changes](#7-reconciler-changes)
- [§8 Prior art, cross-framework validation & alternatives](#8-prior-art-cross-framework-validation--alternatives)
- [§9 The torture-test control family](#9-the-torture-test-control-family)
- [§10 Migration: ArcGIS toolkit + first-party proof points](#10-migration-arcgis-toolkit--first-party-proof-points)
- [§11 Phasing](#11-phasing)
- [§12 Open questions](#12-open-questions)

---

## §1 Motivation

WinUI is full of control properties that hold a *live reference to another
control* somewhere else in the tree:

| Property | Type | Reference is to |
|---|---|---|
| `TeachingTip.Target` | `FrameworkElement` | the element the tip points at |
| `XYFocusUp/Down/Left/Right` | `DependencyObject` | directional-focus neighbor |
| `AutomationProperties.LabeledBy` | `UIElement` | the labeling control |
| `AutomationProperties.DescribedBy` / `FlowsTo` / `FlowsFrom` | `IList<DependencyObject>` | related controls (list-valued) |
| `Compass.GeoView`, `ScaleLine.MapView`, `Legend.GeoView`, … (ArcGIS) | `GeoView` / `MapView` | the map the toolkit control drives |
| `CommandBar` / `Flyout` placement targets, `ScrollView.HorizontalScrollController` | various | a sibling control |

These all share one shape: **control A needs a managed reference to control B,
where B is created by a different part of the component tree, possibly later in
time, possibly never, possibly in a way that recreates B (pool recycling, keyed
reorder, conditional remount) while A keeps living.**

Reactor is a *tree* reconciler. A reference property is a *non-tree edge*. The
framework has no general mechanism for these edges. The one place it solves a
cross-reference today — `RelativePanel` — does so with a name-map confined to a
single panel's direct children (`RelativePanelDescriptor.ApplyRelativePanelAttachedProps`,
the `PerChildAttachedAfterAll` two-pass), and that approach **cannot** generalize
to references whose endpoints have no common container.

This spec defines the general model: **refs as a reactive graph overlay on the
tree, owned and resolved entirely by the reconciler.**

---

## §2 The gap today

### 2.1 `ElementRef` is an inert holder

`ElementRef` (`src/Reactor/Input/FocusManager.cs:17`) is a mutable box:

```csharp
public sealed class ElementRef
{
    internal FrameworkElement? _current;
    public FrameworkElement? Current => _current;   // read-only, no change signal
}
```

The reconciler populates it on mount/update with a **direct field write**
(`Reconciler.cs:3541`):

```csharp
if (m.Ref is not null)
{
    m.Ref._current = fe;          // ← no notification
    AssertTypedRefMatch(m.Ref, fe);
}
```

There is **no `CurrentChanged` signal**, so nothing downstream can react when the
referenced control appears, disappears, or is swapped. Consumers must either poll
`.Current` or rely on mount ordering.

### 2.2 Refs are never cleared on unmount

`UnmountRecursive` (`Reconciler.cs:1698`) tears down component state, animations,
V1/registered-type handlers, and child controls — but it never writes
`element.Modifiers.Ref._current = null`. Because `UseElementRef` returns a
**stable** `ElementRef` instance that outlives any single mount, the box keeps
pointing at a **dead control** after its target unmounts. This is a latent
dangling-ref bug (called out in #456's scope) and the reason any reactive binding
must be careful about leak-safety (§6.4).

### 2.3 The ArcGIS pattern — works by accident, breaks on late binding

The customer stores an `ElementRef<GeoView>?` on each element record and reads it
at mount (`Factories.ToolkitHandlers.cs`):

```csharp
public Compass Mount(MountContext ctx, CompassElement element)
{
    var compass = new Compass { GeoView = element.GeoView?.Current, /* … */ };
    //                                     ^^^^^^^^^^^^^^^^^^^^^^^^
    //   null unless the GeoView already mounted before this Compass
    ctx.ApplySetters(element.Setters, compass);
    return compass;
}

public void Update(UpdateContext ctx, CompassElement oldEl, CompassElement newEl, Compass control)
{
    if (oldEl.GeoView?.Current != newEl.GeoView?.Current)   // ← always false
        control.GeoView = newEl.GeoView?.Current;
    // …
}
```

Two distinct defects:

1. **Mount ordering dependence.** `element.GeoView?.Current` is non-null only
   because, in `CompassPage`, the `MapView` is declared *before* the `Compass`
   in the `Grid`, so it mounts first. Declare the compass first, put the map
   behind a conditional or an async resource, and the compass silently never
   gets its `GeoView`.
2. **Dead `Update` branch.** `UseElementRef` returns a stable instance, so
   `oldEl.GeoView` and `newEl.GeoView` are the **same `ElementRef`**. Their
   `.Current` values are therefore always equal, and the re-assignment branch is
   unreachable — even when the ref's `Current` legitimately changes (the GeoView
   remounts/recycles). The toolkit control never re-binds.

The customer cannot fix this from outside the framework: there is no change
signal to subscribe to, and no engine hook that resolves the edge for them. **The
fix has to be in Reactor**, and it has to be reachable from the spec-047/048
authoring surface they program against.

---

## §3 The graph model

### 3.1 Refs are a reactive overlay, not a second tree

Model an `ElementRef` as a **reactive cell**: a single-valued observable whose
value is "the control currently mounted for this ref, or null." The tree
reconciler is unchanged; references are *edges layered on top*:

```
        component tree (owned by reconciler)
        ─────────────────────────────────────
          Grid
          ├── MapView .Ref(mapRef)        ← writes the cell  mapRef := <GeoView>
          ├── Compass  .GeoView(mapRef)    ─┐
          ├── ScaleLine.MapView(mapRef)    ─┼─ read the cell, reactively
          └── Legend   .GeoView(mapRef)    ─┘   (fan-out: one cell, N edges)

        reference graph (overlay)
          mapRef ──┬──▶ Compass.GeoView
                   ├──▶ ScaleLine.MapView
                   └──▶ Legend.GeoView
```

Each edge is `(sourceCell, targetControl, writeProperty)`. The engine subscribes
the target control to the source cell and writes the property whenever the cell
changes. **Resolution is push, not pull** — order-independent, and it
naturally tolerates the source mounting after the targets.

### 3.2 Why push beats a resolution pass

A synchronous "resolve all references in topological order after layout" pass has
three problems this model sidesteps:

- **Ordering** — toposort requires a DAG; reference graphs are not DAGs (§3.3).
- **Late/async binding** — a target behind `UseAsyncResource` or a route that
  mounts later has no place in a one-shot pass; push handles it for free when the
  cell fills in.
- **Recreation** — when the source control is pool-recycled or keyed-reordered,
  the cell is re-pointed and every edge re-fires. A pass would have to re-run
  wholesale.

### 3.3 Cycles are normal, and push handles them

Several legitimate WinUI patterns are genuinely circular:

- `A.XYFocusRight = B; B.XYFocusLeft = A` — bidirectional directional focus.
- Mutual `LabeledBy` / `LabelFor` accessibility pairs.
- Master/detail panes that each reference the other.
- A 3-node focus ring `A → B → C → A`.

Push converges on cycles without special-casing because **writing a reference
property does not change the source cell's value.** Walk `A ↔ B`:

1. `A` mounts → cell `refA := A`. `B` is not mounted yet; `B`'s edge to `refA`
   subscribes and writes `B.XYFocusLeft = null` (or is deferred — §6.3).
2. `B` mounts → cell `refB := B`. `A`'s edge writes `A.XYFocusRight = B`. `B`'s
   edge to `refA` re-fires (cell already `A`) and writes `B.XYFocusLeft = A`.
3. Quiescent. Two property writes, no recursion, no toposort.

The only invariant push needs is **the no-echo rule**: *writing a control
reference property must not synchronously mutate any `ElementRef`'s `Current`.*
WinUI's reference DPs (`Target`, `XYFocus*`, `LabeledBy`, the ArcGIS `GeoView`)
satisfy this — assigning them does not mount/unmount anything. The engine still
installs a re-entrancy guard + depth cap that `Debug.Fail`s if a write ever
violates the rule (§6.5), so a pathological control fails loudly instead of
stack-overflowing.

### 3.4 Glitch-freedom within a commit

If `A` references `B` and both mount in the *same* reconcile pass with `A` first,
eager push writes `A.target = null` then `A.target = B` — a transient null. For
reference DPs that is harmless, but for some (`TeachingTip.Target` controls
open/close animation) the transient is observable. The model therefore **defers
cell-change dispatch to a post-commit flush** (§6.3): changes that occur during a
reconcile pass are coalesced per-cell to their final value and dispatched once,
after the whole tree is committed. Steady-state async mounts (a cell that fills
in on a later frame) dispatch immediately. This gives glitch-free batching for
the common case and reactive latency for the late case.

---

## §4 `ElementRef` as a reactive cell

Add the change signal and a leak-safe subscription surface to the existing type.
The public read stays identical; the mutation point becomes a method that fires.

```csharp
public sealed class ElementRef
{
    internal FrameworkElement? _current;
    public FrameworkElement? Current => _current;

    // Raised after Current changes (mount, remount/recycle, swap, unmount-clear).
    // Public for advanced imperative consumers; the engine is the primary client.
    public event Action<FrameworkElement?>? CurrentChanged;

    // The reconciler's only write path. Fires CurrentChanged exactly when the
    // value actually changes — never on a same-value re-render write (kills churn).
    internal void SetCurrent(FrameworkElement? value)
    {
        if (ReferenceEquals(_current, value)) return;
        _current = value;
        RaiseCurrentChanged(value);   // re-entrancy-guarded, depth-capped (§6.5)
    }
}
```

`ElementRef<T>` is unchanged except it forwards a typed `CurrentChanged` for
ergonomics; the inner untyped cell remains the identity and the subscription
target. Stable identity (`UseElementRef` memoizes one instance for the
component's lifetime) makes the cell a stable graph node.

> **Why `SetCurrent` rather than keeping the field write:** the reconciler writes
> the ref on *every* update today (`Reconciler.cs:3539-3543`) so the box stays
> fresh across pool recycling. With a change signal we must fire **only on actual
> change**, or every re-render of a referrer would re-dispatch its whole edge set.
> The `ReferenceEquals` guard in `SetCurrent` is the single most important line
> for keeping this off the hot path.

---

## §5 Author-facing API

Three layers, all sugar over one engine mechanism (§6). Per D1 the author never
touches `Current`.

### 5.1 Element records carry a typed ref property

The reference is a first-class, strongly-typed property on the element record, so
it can be set directly *or* via fluent:

```csharp
public sealed record CompassElement : Element
{
    public ElementRef<GeoView>? GeoView { get; init; }   // the reference slot
    public bool AutoHide { get; init; } = true;
    public Action<Compass>[] Setters { get; init; } = [];
}

// direct:
new CompassElement { GeoView = mapRef, AutoHide = false }

// fluent (sugar — `element with { GeoView = r }`):
Compass().GeoView(mapRef)
```

### 5.2 Per-property fluent setter

Each reference property gets a typed fluent that reads naturally at the call site
and is the surface the issue asked for:

```csharp
var mapRef = ctx.UseElementRef<GeoView>();
return Grid(
    MapView(map).Ref(mapRef),
    Compass().GeoView(mapRef),
    ScaleLine().MapView(mapRef),
    TeachingTip("Pan around here").Target(mapRef));
```

`.Target(...)`, `.GeoView(...)`, `.XYFocusRight(...)` etc. are thin `with`-record
setters. (Phase 3 can source-generate them from a `[ReferenceProp]` marker; hand
-authored is fine until then — they are one-liners.)

### 5.3 The descriptor reference entry — where resolution is declared

The edge is declared on the control descriptor. This is the line that hands the
`ref → target-property` relationship to the engine:

```csharp
public static readonly ControlDescriptor<CompassElement, Compass> Descriptor =
    new ControlDescriptor<CompassElement, Compass>()
        .OneWay(e => e.AutoHide, (c, v) => c.AutoHide = v)
        // NEW: reactive reference binding. Engine owns subscribe / write /
        // recreation-survival / unmount-clear.
        .Reference<GeoView>(
            get: e => e.GeoView,                    // ElementRef<GeoView>? on the record
            set: (compass, geoView) => compass.GeoView = geoView);  // geoView is GeoView? (typed, nullable)
```

`set` receives `TTarget?` — `null` when the cell is empty (target not yet
mounted, or unmounted). The author writes the property; the engine decides
*when*.

### 5.4 Imperative handlers — the binding bridge

ArcGIS authors `IElementHandler`s, not descriptors. To keep resolution in the
reconciler without forcing an immediate descriptor migration, the
`ReactorBinding<TElement>` surface (already returned by `ctx.BindFor(control, el)`
and used for `OnCustomEvent`) gains a `Reference` method that registers the
**same** engine edge:

```csharp
public Compass Mount(MountContext ctx, CompassElement element)
{
    var compass = new Compass { AutoHide = element.AutoHide };
    ctx.BindFor(compass, element)
       .Reference<GeoView>(
           get: e => e.GeoView,
           set: (c, geoView) => c.GeoView = geoView);   // wired once; survives recycle
    ctx.ApplySetters(element.Setters, compass);
    return compass;                                       // no more reading .Current
}

public void Update(UpdateContext ctx, CompassElement oldEl, CompassElement newEl, Compass control)
{
    // No GeoView handling at all — the reference edge is engine-owned and
    // already re-fires on cell change. Only diff the non-reference props.
    ctx.ApplySetters(newEl.Setters, control);
}
```

Both `descriptor.Reference(...)` and `binding.Reference(...)` funnel into one
`ReferencePropEntry` / `ControlReferenceEdge` mechanism (§6). The imperative
bridge is the migration on-ramp; descriptors are the destination.

---

## §6 Engine: the reference-binding entry

The reference edge is implemented as a new `PropEntry` subtype that mirrors the
proven `ControlledPropEntry` lifecycle (static trampoline + per-control payload
that survives pool rent/return), but subscribes to an **`ElementRef.CurrentChanged`
cell** instead of a WinUI control event.

### 6.1 Shape

```csharp
internal sealed class ReferencePropEntry<TElement, TControl, TTarget> : PropEntry<TElement, TControl>
    where TElement : Element
    where TControl : FrameworkElement
    where TTarget  : FrameworkElement
{
    private readonly Func<TElement, ElementRef<TTarget>?> _get;
    private readonly Action<TControl, TTarget?> _set;

    public override void Mount(TControl ctrl, TElement el)
    {
        // Bare initial write of the cell's current value (may be null if the
        // target hasn't mounted yet). Subscription happens in EnsureSubscribed.
        var cell = _get(el)?.Inner;
        _set(ctrl, cell?.Current as TTarget);
    }

    public override void EnsureSubscribed(ReactorBinding<TElement> binding, TControl ctrl, TElement el)
    {
        var cell = _get(el)?.Inner;
        if (cell is null) return;

        // KD-3 pool survival: the subscription handle lives on a per-control
        // payload preserved across pool rent/return, keyed so re-mounts of the
        // SAME control skip re-subscription. The trampoline is captured-free; it
        // reads the live element + setter off the payload at fire time.
        var payload = Reconciler.GetOrCreateControlEventPayload<ReferenceEdgePayload<TControl, TTarget>>(ctrl);
        if (payload.Cell == cell) return;            // already wired to this cell
        if (payload.Cell is not null) Detach(payload);   // author swapped which ref → rewire

        payload.Cell = cell;
        payload.Set  = _set;
        payload.Control = ctrl;
        payload.Handler = target => ApplyEdge(payload, target);
        cell.CurrentChanged += payload.Handler;       // engine-owned subscription
        Reconciler.RegisterReferenceEdgeForUnmount(ctrl, payload);  // §6.4 teardown
    }

    public override void Update(TControl ctrl, TElement oldEl, TElement newEl)
    {
        // Reference props are NOT diffed by value here — the cell drives writes.
        // The only Update concern is the author swapping the ref *instance*
        // (rare); EnsureSubscribed's `payload.Cell == cell` check + Detach handles
        // it when the descriptor re-runs EnsureSubscribed on the new element.
    }
}
```

`ApplyEdge` writes the property under the post-commit flush discipline (§6.3) and
the no-echo guard (§6.5).

### 6.2 Why reuse the controlled-entry machinery

The §6.1 entry is deliberately the same shape as `ControlledPropEntry`
(`PropEntry.cs:262`): static/captured-free trampoline, per-control payload via
`GetOrCreateControlEventPayload<T>` that survives pool rent/return (the KD-3
invariant), callback-presence gate (no ref ⇒ no subscription cost). That machinery
already solves "subscribe exactly once per control lifetime, survive recycling,
don't double-subscribe" — the hard part. The only novelty is the subscription
*target* (an `ElementRef` cell, not a WinUI event) and the teardown registration.

### 6.3 Post-commit flush (glitch-freedom)

`ElementRef.SetCurrent` does not invoke subscribers synchronously during a
reconcile pass. Instead:

- During a commit, a cell that changes adds itself to a per-reconcile **dirty
  set** with its final value.
- At end of commit (after the tree is fully mounted/updated), the reconciler
  drains the dirty set, invoking each cell's subscribers **once** with the final
  value. A referrer that mounted before its target in the same pass therefore
  sees only the final value — no transient null write (§3.4).
- A cell that changes **outside** a reconcile pass (async target mounts on a
  later frame, conditional toggles) dispatches immediately, so reactive latency is
  preserved for the late-binding case.

This is the same "coalesce-to-final, flush-once" shape the renderer already uses
for batched effects; it is not a new scheduler.

### 6.4 Teardown — closing the dangling-ref + leak gaps

Two clearing responsibilities, both engine-owned:

1. **Target unmounts → clear the cell.** `UnmountRecursive` (§7) calls
   `ref.SetCurrent(null)` for the unmounting control's `.Ref`. That fires
   `CurrentChanged(null)`, and every referrer's edge writes `set(ctrl, null)` —
   `TeachingTip.Target` clears, `Compass.GeoView` clears, no dangling reference.
   Fixes §2.2.
2. **Referrer unmounts → unsubscribe from the cell.** The cell (held by a
   long-lived `UseElementRef`) must not retain a dead referrer control. The edge
   payload registered via `RegisterReferenceEdgeForUnmount` is detached in
   `UnmountRecursive` (`cell.CurrentChanged -= payload.Handler`), so the cell's
   invocation list returns to zero subscribers when all referrers are gone. This
   is the leak-safety counterpart to (1).

### 6.5 Re-entrancy + depth guard

`ElementRef.RaiseCurrentChanged` runs under a per-cell `_dispatching` flag and a
global small depth counter. If an edge write re-enters cell dispatch (violating
the §3.3 no-echo rule), the guard breaks the recursion and `Debug.Fail`s with the
offending control/property in DEBUG; in RELEASE it drops the re-entrant dispatch
and logs once. Legitimate cycles (§3.3) never trip this because reference writes
don't mutate cells — only the dirty-set flush walks cells, and it is not
re-entrant.

---

## §7 Reconciler changes

Small, localized, three sites:

1. **Route ref population through `SetCurrent`** — `Reconciler.cs:3541`:
   ```csharp
   if (m.Ref is not null)
   {
       m.Ref.SetCurrent(fe);          // was: m.Ref._current = fe;
       AssertTypedRefMatch(m.Ref, fe);
   }
   ```
   `SetCurrent`'s identity guard makes the per-update write a no-op unless the
   control actually changed, so the hot path is cheaper than today's
   unconditional field write *and* now reactive.

2. **Clear on unmount** — in `UnmountRecursive` (`Reconciler.cs:1698`), when the
   unmounting control carries a `.Ref`, call `ref.SetCurrent(null)`; and detach
   any reference-edge payloads registered against the control (§6.4). Both run in
   the existing element-tag lookup the method already performs.

3. **Dirty-set + flush** — a per-reconcile cell dirty-set and an end-of-commit
   drain (§6.3), wired into the existing commit boundary.

Everything else (the `ReferencePropEntry`, `ReferenceEdgePayload`, the
`binding.Reference` / `descriptor.Reference` builders) is additive and sits in the
V1Protocol descriptor surface alongside the existing entries.

---

## §8 Prior art, cross-framework validation & alternatives

How do React, SwiftUI, and Jetpack Compose solve "control A holds a live
reference to control B elsewhere in the tree"? The short answer: **none of them
use an inert imperative ref for it, and none use a synchronous cross-tree
resolution pass.** Each reaches for reactivity instead — which is exactly the
move this spec makes by turning `ElementRef` into a reactive cell (§4). The
parallels below are strong independent validation of D1 (engine-owned reactive
resolution) and the teardown discipline of §6.4.

### 8.1 React — refs are inert boxes; reactivity is opt-in via state

A React `ref` is a `{ current }` box with **no change signal**, identical to
Reactor's `ElementRef` today. React 19 added a **callback-ref cleanup function**:
the callback runs with the node on mount and runs the returned cleanup on unmount
— precisely our `SetCurrent(fe)` on mount / `SetCurrent(null)` on unmount pair
(§4, §6.4). Crucially, React's **recommended way to *react* to a node** is to
route the callback ref through state:

```jsx
const [node, setNode] = useState(null);
return <div ref={setNode} />;   // node is now reactive — effects/renders re-run
```

That "ref-callback-as-`setState`" pattern *is* the reactive cell. We are promoting
it into the framework so authors don't hand-roll it per reference. And note what
React does for genuine *cross-tree* references: it does **not** use refs — it uses
**id strings** (`aria-labelledby="…"`) or **Context** (reactive). Reactor keeps
the typed-ref identity (no stringly-typed ids) while getting React-state-grade
reactivity built in.

> Validation: our design is React's own reactive-ref pattern + React 19's
> mount/unmount cleanup, unified onto one stable typed cell.

### 8.2 SwiftUI — no imperative refs at all; identity by namespace + id, reactivity by the declarative engine

SwiftUI has **no view references**. Cross-view relationships are expressed three
ways, all declarative:

- **`matchedGeometryEffect(id:in:)`** links two views in *different* subtrees by a
  shared **(id, `@Namespace`)** pair; the engine synchronizes their geometry. This
  is a namescope match — the §8.4 "namescope" alternative, done reactively.
- **`anchorPreference` / `PreferenceKey`** lets a child publish a geometry anchor
  *up* the tree, which an ancestor reads to position an overlay (e.g. a tooltip
  pointing at a view) — the §8.4 "anchor-preferences" alternative.
- **`@FocusState`** binds focus to a **value/enum**, never to a view reference
  (`.focused($field, equals: .username)`).

SwiftUI gets reactivity for free from its declarative invalidation engine, so it
can afford to avoid handles entirely. Reactor is a retained-WinUI reconciler
driving controls with imperative reference DPs (`Target`, `XYFocus`), so a handle
(`ElementRef`) is the right primitive — but we adopt SwiftUI's lesson that the
*resolution* should be reactive and identity-based, not a manual pass. Unlike
anchor preferences (child→ancestor geometry only), our cell handles **arbitrary
A→B references** in any direction.

> Validation: SwiftUI deliberately rejects inert refs; its namespace+id matching
> and preference propagation are the §8.4 alternatives, and it confirms reactivity
> (not a resolve pass) is the correct engine behavior.

### 8.3 Jetpack Compose — the closest parallel: stable handle + directional reference property

Compose's `FocusRequester` is almost exactly this spec's `ElementRef`. You
`remember {}` a stable handle, attach it to one composable via a modifier, and
then **link it as the target of a directional-reference property**:

```kotlin
val (first, second) = remember { FocusRequester.createRefs() }
Button(Modifier.focusRequester(first).focusProperties { right = second }) { … }
Button(Modifier.focusRequester(second)) { … }
```

`Modifier.focusRequester(req)` ≈ `.Ref(elementRef)`; `focusProperties { right =
other }` ≈ `.XYFocusRight(otherRef)`; `FocusRequester.createRefs()` ≈ batched
`UseElementRef`. Compose converged independently on "stable handle attached by
modifier, used as a reference property to wire one control to another" — direct
evidence the `ElementRef` + reference-prop shape is right. Two more Compose data
points:

- **`ConstraintLayout` `createRefs()` + `constrainAs(ref){ top.linkTo(other.bottom) }`**
  are *scope-bound* reference handles resolved at measure time within one
  ConstraintLayout — precisely the `RelativePanel` / scoped-namescope shape that
  **does not generalize** across containers (§2, §8.4). Compose keeps it as a
  separate, narrower tool, exactly as we keep `RelativePanel` separate.
- **`onGloballyPositioned`** reports coordinates into state — the reactive-cell
  pattern once more.

> Validation: Compose's `FocusRequester` + `focusProperties` is a near-isomorphic
> design; its scoped ConstraintLayout refs confirm scoped resolution is a distinct,
> non-generalizing mechanism.

### 8.4 Alternatives considered (and which frameworks chose them)

| Alternative | Shape | Seen in | Why not for Reactor |
|---|---|---|---|
| **Namescope / `ElementName`** | Name the target, referrer cites the name; resolve within a scope. | XAML; SwiftUI `matchedGeometryEffect` (namespace+id); Compose `ConstraintLayout`; our own `RelativePanel`. | String-keyed and scope-bound. Doesn't cross containers without a global namescope, which reintroduces ordering + collision problems. Strings defeat the typed-ref identity we already have from `UseElementRef`. |
| **ID/handle indirection** | Stable string IDs, engine keeps an `id → control` map. | DOM `aria-labelledby` (React's cross-tree answer). | Parallel string-identity system competing with `ElementRef`; same stringly-typed downsides. |
| **Deferred resolution queue** | Collect unresolved refs at mount, retry each commit. | — | Handles late binding but not recreation or unmount-clear; needs its own liveness/GC story; cycles need explicit detection. The reactive cell subsumes it with less machinery. |
| **Synchronous topological resolve pass** | Build the ref DAG each commit, toposort, write in order. | — | Reference graphs aren't DAGs (§3.3); async/late targets have no slot; full re-run on any recreation. |
| **Anchor / preference propagation** | Children publish values up; ancestor aggregates. | SwiftUI `anchorPreference`; Compose `onGloballyPositioned`. | Solves *upward geometry aggregation*, not *arbitrary A→B references across the tree*. Wrong shape for `Target` / `XYFocus`. |
| **Reactive cell on a stable typed handle** (this spec) | Handle = observable cell; engine subscribes target → writes property; push resolution. | React (ref-as-`setState`); Compose (`FocusRequester`); SwiftUI (declarative reactivity). | **Chosen.** The stable typed ref already exists; push handles ordering / late-binding / recreation / cycles uniformly; the subscription machinery is already built and pool-hardened for controlled props (§6.2). |

The reactive-cell-on-stable-ref model wins because the stable typed ref already
exists, push handles ordering/late-binding/recreation/cycles uniformly, the
subscription machinery is already pool-hardened (§6.2), and — as §8.1–8.3 show —
it is the design every comparable framework converges toward.

**Sources:** React 19 callback-ref cleanup & ref-as-state —
[react.dev refs](https://react.dev/learn/manipulating-the-dom-with-refs),
[tkdodo: Ref Callbacks, React 19](https://tkdodo.eu/blog/ref-callbacks-react-19-and-the-compiler);
SwiftUI —
[matchedGeometryEffect](https://developer.apple.com/documentation/swiftui/view/matchedgeometryeffect(id:in:properties:anchor:issource:)),
[anchorPreference](https://developer.apple.com/documentation/swiftui/view/anchorpreference(key:value:transform:));
Jetpack Compose —
[Change focus traversal order](https://developer.android.com/develop/ui/compose/touch-input/focus/change-focus-traversal-order),
[Focus in Compose](https://developer.android.com/develop/ui/compose/touch-input/focus).

---

## §9 The torture-test control family

Per D2, cycles and every other topology are proven, not assumed. Build a
synthetic control family in the self-host test assembly
(`tests/Reactor.AppTests.Host/SelfTest/Fixtures/`, alongside the existing
external-proof fixtures) so the matrix runs against real WinUI mount/unmount.

### 9.1 The synthetic control

A `RefNode : Control` with several reference DPs and a debug identity:

```csharp
public sealed partial class RefNode : Control
{
    public string NodeId { get; set; } = "";
    public FrameworkElement? Left  { get; set; }   // ← reference DPs
    public FrameworkElement? Right { get; set; }
    public FrameworkElement? Up    { get; set; }
    public FrameworkElement? Down  { get; set; }
    public FrameworkElement? Parent { get; set; }
    public FrameworkElement? Peer  { get; set; }
    public IList<FrameworkElement> Related { get; } = new List<FrameworkElement>(); // list-valued (Phase 2)
}
```

…with a `RefNodeElement` record carrying `ElementRef<RefNode>?` per slot, and a
descriptor declaring a `.Reference<RefNode>(...)` per slot (plus one
`.ReferenceList<RefNode>(...)` for `Related`, Phase 2).

### 9.2 Topology matrix

Each row asserts: after commit every reference property equals the expected
mounted control; after the relevant teardown the property is cleared; the cell's
subscriber count is exactly the live-referrer count (no leak); no re-entrancy
fail; re-render is stable (no spurious writes).

| # | Topology | What it stresses |
|---|---|---|
| 1 | Linear chain `A.Right → B.Right → C` | basic forward reference |
| 2 | Fan-out `1 source → N referrers` | the ArcGIS GeoView case; one cell, many edges |
| 3 | Fan-in `N sources → 1 referrer's list` | list-valued reference (Phase 2) |
| 4 | Bidirectional `A ↔ B` | the core cycle case; convergence in 2 writes |
| 5 | 3-cycle `A → B → C → A` | multi-node cycle; flush ordering |
| 6 | Self-reference `A → A` | degenerate cycle; no stack overflow |
| 7 | Parent/child both ways (child→parent + parent→child) | tree edge vs reference edge interplay |
| 8 | Diamond `A → {B, C} → D` | shared downstream target |
| 9 | **Late mount** — referrer declared *before* target | push correctness vs §2.3 mount-order bug |
| 10 | **Conditional** — target toggles in/out via state | unmount-clear + remount re-link |
| 11 | **Reorder** — keyed list of nodes shuffled | cell re-point under keyed reconcile |
| 12 | **Pool recycle** — long `ListView` of `RefNode`s scrolled | KD-3 payload survival; no double-subscribe |
| 13 | **Referrer unmount while source lives** | leak-safety: subscriber count returns to baseline |
| 14 | **Source swap** — author changes which `ElementRef` a referrer uses | `EnsureSubscribed` rewire path |

### 9.3 Headless-safe surface tests

The identity / change-signal / re-entrancy-guard behavior of `ElementRef`
(SetCurrent fires only on change, CurrentChanged ordering, depth cap) is unit
-tested without a XAML host in `tests/Reactor.Tests/` next to the existing
`TypedElementRefTests`.

---

## §10 Migration: ArcGIS toolkit + first-party proof points

- **First-party proof — `TeachingTip.Target`.** The issue's canonical case. Land
  the built-in `TeachingTipElement` descriptor with a `.Reference<FrameworkElement>`
  entry; gallery sample showing a tip targeting a button mounted elsewhere.
- **External proof — ArcGIS toolkit.** Migrate `Factories.ToolkitHandlers.cs`:
  every `GeoView`/`MapView` field handled today by `element.GeoView?.Current`
  reads moves to a `ctx.BindFor(...).Reference<GeoView>(...)` call (§5.4). Delete
  the dead `oldEl.X?.Current != newEl.X?.Current` Update branches. The element
  records already have the `ElementRef<GeoView>?` shape, so the public API the
  customer exposes (`Compass(geoView: mapViewRef)`) is unchanged — only the
  handler internals and correctness change. Their `CompassPage` keeps working and
  *gains* correctness if the map is ever declared after the compass.
- **`XYFocus*`** as the multi-slot, partly-cyclic proof (four reference props on
  one control, often bidirectional between neighbors).

---

## §11 Phasing

**Phase 0 — model ratification + harness.** Land the `RefNode` torture-test
control family skeleton (§9) and the headless `ElementRef` reactive-cell unit
tests. Agree the flush semantics (§6.3) against a measured re-render bench so the
dirty-set drain has a perf gate before it ships. No production wiring yet.

**Phase 1 — the mechanism (closes #456).**
- `ElementRef.SetCurrent` + `CurrentChanged` + re-entrancy/depth guard (§4, §6.5).
- Reconciler: route population through `SetCurrent`, clear on unmount, dirty-set +
  post-commit flush (§7).
- `ReferencePropEntry` + `ReferenceEdgePayload` + `descriptor.Reference(...)` +
  `binding.Reference(...)` (§6).
- Prove on `TeachingTip.Target`; migrate the ArcGIS toolkit (§10).
- **Full §9 topology matrix green, including all cycle rows (4, 5, 6).**

**Phase 2 — breadth.**
- List-valued references: `.ReferenceList<T>(...)` for `AutomationProperties`
  `LabeledBy`/`DescribedBy`/`FlowsTo`/`FlowsFrom` and the `Related` torture slot,
  with keyed diff of the target list (reuse the `CollectionDiffControlled` shape).
- `XYFocus*` family across the catalog; per-property fluents for the common WinUI
  reference props.
- Accessibility validation (a `LabeledBy` edge must survive control recreation so
  screen-reader relationships don't drop).

**Phase 3 — graph-grade polish.**
- Devtools: render the reference overlay as edges in the element-tree inspector;
  surface cycles and unresolved (perpetually-null) references as diagnostics.
  **Landed (3.1):** the `references` MCP tool + `ReferenceOverlay` engine, the
  `GET /references` preview endpoint, and the VS Code **References** overlay
  toggle, with cycle / unresolved diagnostics.
- Optional weak-subscription mode for refs intentionally held far longer than
  their referrers. **Deferred:** gated on a public `CurrentChanged`, which Q4
  resolved to keep `internal` (no imperative consumer appeared). Non-breaking to
  add later.
- Source-generate the per-property fluents + descriptor reference entries from a
  `[ReferenceProp(nameof(Control.Target))]` marker on the element record, folding
  into the spec-047 §7 source-gen track when it lands. **Deferred by design**
  until that source-gen track lands; the hand-written fluents + descriptor entries
  ship today.

---

## §12 Open questions

- **Q1 — flush granularity.** Is one end-of-commit drain enough, or do nested
  commits (a referrer whose edge write triggers a state set → re-render) need a
  bounded multi-pass settle? Lean: single drain + depth cap; revisit only if a
  real control needs settle.
  **Resolution (Phase 3):** the single end-of-commit drain plus the depth cap
  (`ReferenceDirtySet`, guard ≤ 64) is sufficient. The full §9 topology matrix —
  including the n-cycle, self-reference, and late-mount rows — and the Phase 2
  real-control torture matrix all converge in one drain; no shipping control has
  required a bounded multi-pass settle. The dirty-set's `while` loop already
  absorbs the rare case where a flush enqueues further cells within the same
  commit, so the granularity question is closed at "single drain + depth cap."
- **Q2 — `binding.Reference` vs descriptor migration timeline.** Do we keep the
  imperative `binding.Reference` bridge as a permanent public surface, or
  deprecate it once descriptors cover the catalog? It is non-breaking either way;
  default to keeping it (downstream handler authors like ArcGIS may prefer it).
  **Resolution (Phase 3):** keep `binding.Reference`/`.ReferenceList` as a
  permanent, supported public surface. Descriptors are the primary path, but the
  imperative bridge stays first-class — out-of-`Reactor.dll` handler authors
  (e.g. the ArcGIS toolkit, generated wrappers) rely on it, and it shares the
  exact engine machinery, so there is no maintenance cost to keeping it. Not
  deprecated.
- **Q3 — list-reference identity.** For `LabeledBy`-style lists, is target order
  significant to WinUI/UIA? If yes the keyed diff must preserve order; if no a set
  diff is cheaper. Resolve during Phase 2 against UIA behavior.
  **Resolution (Phase 2 / task 2.1):** list references preserve the author's
  declaration order, omit unresolved or unmounted targets, and rebuild the
  destination list on every cell/list change. This is intentionally an
  idempotent set-write rather than an in-place keyed reconcile: UIA relationship
  lists such as `DescribedBy` / `FlowsTo` should reflect author intent, and the
  lists are small enough that clear-and-repopulate keeps the engine simpler.
- **Q4 — public `CurrentChanged`.** Exposing the event invites imperative misuse
  (subscribing without unsubscribing → the exact leak §6.4 guards against in the
  engine). Ship it `public` with a documented "the engine manages this for you;
  manual subscribers own their teardown" note, or keep it `internal` and force all
  consumption through reference entries? Lean: `internal` in Phase 1, promote with
  guidance only if a concrete imperative need appears.
  **Resolution (Phase 3):** keep `CurrentChanged` `internal`. No concrete
  imperative consumer surfaced across Phases 1–3 — every reference relationship is
  expressible through descriptor reference entries, the `binding.Reference`
  bridge, or the modifier fluents, all of which let the engine own subscription
  and teardown. Leaving the event `internal` keeps the §6.4 leak guarantee total
  (no external subscriber can strand a subscription). The optional
  weak-subscription mode (§11 Phase 3) is therefore deferred too: it only earns
  its keep alongside a public `CurrentChanged`, which is not warranted yet. Both
  remain non-breaking to add later if a real imperative need appears.