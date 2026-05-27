# Fully Extensible Control Model — Design Proposal

## Status

**Phase 0 complete — Phase 1 greenlit.** The seven Phase 0 deliverables in §14 have all shipped (see [`docs/specs/tasks/047-extensible-control-model-implementation.md`](tasks/047-extensible-control-model-implementation.md) for the per-deliverable status). The audits ([`047/audits/`](047/audits/)) reshaped the design space materially: §8 collapsed from a §8 vs §8.1 debate into a small concrete plan, §9 has a clean structural cut, and §13's data-driven open questions have ratified decision criteria in [`047/decision-criteria.md`](047/decision-criteria.md). Baseline numbers are committed under [`047/baseline-results/`](047/baseline-results/); §11.6 and §12 now anchor their targets to measured M1–M13 values from LAPTOP-4MEP83VI (workstation x64 + ARM64-native captures deferred to Phase 1's first promotion PR per [`047/baseline-results/machines.md`](047/baseline-results/machines.md)). The §8.2 setter-suppression carve-out landed ahead of Phase 1 ([`047/factoring-recommendation.md`](047/factoring-recommendation.md)); spec 047 stays unified — no split executed.

This spec documents the design for removing the asymmetry between *built-in Reactor controls* and *externally-authored controls registered via `Reconciler.RegisterType`*. The conversation started concretely (could a Win2D `CanvasControl` wrapper live downstream in the pix project without Reactor changes?), and ended in a broader question: what would it take for every mechanism Reactor uses to implement its own controls to be available to third-party authors — and could that protocol be smaller, more data-driven, and lower-overhead than what we have today?

Companion proposals consider similar questions for child reconciliation ([spec 042](042-keyed-list-reconciliation-design.md)) and modifier bucketing ([spec 034](034-element-allocation-reduction.md)).

This spec captures:
- the current state of extensibility,
- a straw-man unified protocol (v1),
- the simplification and performance angles that should be explored *before* implementing v1,
- explicit open questions to revisit in a follow-up design session.

---

## Table of Contents

- [§1 Motivation](#1-motivation)
- [§2 The two-tier reality today](#2-the-two-tier-reality-today)
- [§3 What the engine actually does for a built-in control](#3-what-the-engine-actually-does-for-a-built-in-control)
- [§4 Straw-man v1 — handler protocol with rich context](#4-straw-man-v1--handler-protocol-with-rich-context)
- [§5 Why v1 alone isn't the answer](#5-why-v1-alone-isnt-the-answer)
- [§6 Simplification direction: declarative control descriptors](#6-simplification-direction-declarative-control-descriptors)
- [§7 Simplification direction: source-generated handlers](#7-simplification-direction-source-generated-handlers)
- [§8 Simplification direction: eliminate the change-echo suppressor](#8-simplification-direction-eliminate-the-change-echo-suppressor)
- [§9 Simplification direction: per-control trampoline tables](#9-simplification-direction-per-control-trampoline-tables)
- [§10 What is the right delta over WinUI?](#10-what-is-the-right-delta-over-winui)
- [§11 Per-element memory overhead — concrete bytes](#11-per-element-memory-overhead--concrete-bytes)
- [§12 Runtime perf — dispatch, code size, cache, JIT](#12-runtime-perf--dispatch-code-size-cache-jit)
- [§13 Future design-session questions](#13-future-design-session-questions)
- [§14 Suggested phasing](#14-suggested-phasing)
- [§15 Performance validation suite](#15-performance-validation-suite)

---

## §1 Motivation

Reactor today has a public extension hook, `Reconciler.RegisterType<TElement, TControl>(mount, update, unmount)` at `src/Reactor/Core/Reconciler.cs:521`. A downstream project (Pix's WinUI port, the Monaco sample at `samples/apps/monaco-editor/Monaco/MonacoEditorElement.cs`, the in-tree docking system, several utility controls) can register a custom element type with mount/update/unmount lambdas and use it indistinguishably from a built-in element *as far as the dispatch table is concerned*.

However: **the lambdas registered via `RegisterType` cannot reach most of the machinery that built-in controls use.** Specifically, the following are `internal`:

| Mechanism | Location | What it does |
|---|---|---|
| `ApplySetters<T>` | `Reconciler.cs:1436` | Runs the `Action<TControl>[]` from the element's `.Set(...)` modifier chain |
| `SetElementTag` / `GetElementTag` | `Reconciler.cs:331-352` | Writes/reads the current `Element` on the `ReactorAttached.StateProperty` attached DP — feeds the event trampoline |
| `ChangeEchoSuppressor` | `ChangeEchoSuppressor.cs` | Suppresses the change-event echo that fires when the engine programmatically writes a value-bearing DP (`ColorPicker.Color`, `ToggleSwitch.IsOn`, `NumberBox.Value`, …) |
| `EventHandlerState` + `Ensure*Subscribed` family | `Reconciler.cs:2787`, `2963-3200` | Attach exactly one stable trampoline per WinUI event per native DependencyObject; update handler delegates by swapping a `Current*` field on the state object rather than `event +=` / `-=` |
| `ApplyDefaultAutomationName` / `UpdateDefaultAutomationName` / `ApplyThemeBindings` / `ApplyResourceOverrides` | `Reconciler.cs` | Per-control accessibility (mount + update variants), theming, resource override pipelines. Phase 1 promoted all four to `public static` with `[Experimental("REACTOR_V1_PREVIEW")]`. |
| `_pool` (`ElementPool`) | `Reconciler.cs` | Control rental/return for re-mount and ListView recycling |

In other words: an external author who tries to wire `control.PointerPressed += ...` themselves silently bypasses pool-survivable subscription and re-introduces double-subscribe on re-mount (issue #114). An external author who writes a value-bearing DP without `BeginSuppress` re-introduces the cross-state-echo bug from spec 030. The asymmetry isn't just "first-party gets nicer helpers"; it's "first-party gets correctness."

### 1.1 Why this is becoming urgent: the split-library plan

The asymmetry has been tolerable so far because *most* controls are built-in and external `RegisterType` consumers are a small minority. That ratio is about to flip. Roughly half of Reactor's current control catalog is planned to move out of `Reactor.dll` into a separate `Reactor.Controls.*` package (or family of packages). After the split, the "external" registration path is no longer an escape hatch — it is the path the majority of controls travel through, including controls maintained by the Reactor team itself.

That makes every correctness gap in the external surface a product gap. A pool-survival bug that today only affects the one downstream consumer wrapping a `CanvasControl` becomes, after the split, a bug that affects half the catalog. The same is true for echo suppression, attached-DP state, child reconciliation, modifier composition, and accessibility fallback. The spec's framing should be read with that in mind: this is not "an architecture cleanup that happens to help third parties," it is "the third-party path is becoming the first-party path, so it must be first-party-quality."

Concretely, a control authored against the public surface must support the same correctness invariants as a control today authored as a private `MountXxx` arm — without `InternalsVisibleTo`, without reflection escape hatches, without bypassing the pool/trampoline/suppression discipline. Phase 0 (§14) must measure the post-split scenario directly, not just micro-level dispatch.

This spec asks: **what's the right shape for closing that asymmetry?** And, more interestingly: now that we're forced to think about the full surface, **is the current shape even right?** Could we shrink it?

---

## §2 The two-tier reality today

The reconciler dispatches in two phases (`Reconciler.Mount.cs:62-160+`, `Reconciler.Update.cs:108+`):

```csharp
// 1. Check the type-registry first — external types win.
if (_typeRegistry.TryGetValue(element.GetType(), out var reg))
    control = reg.Mount(element, requestRerender, this);
else
    control = element switch {
        TextBlockElement t => MountText(t),
        ButtonElement b   => MountButton(b, requestRerender),
        // ... 70+ arms ...
    };

// 2. Run the post-mount pipeline (modifiers, accessibility, theming).
//    This runs regardless of which branch produced the control — registered
//    types do get modifier/theme support automatically.
if (modifiers is not null && control is FrameworkElement fe)
    ApplyModifiers(fe, modifiers, requestRerender);
```

The two tiers are:
- **Tier A — built-ins:** private `MountXxx` / `UpdateXxx` instance methods on the `Reconciler` partial, with full access to every private helper. There are ~70 such methods. `Reconciler.Mount.cs` is ~1,400 lines; `Reconciler.Update.cs` is ~4,000.
- **Tier B — registered types:** three lambdas. The reconciler hands them `requestRerender` and the `Reconciler` instance, then trusts them.

The gap between the two tiers is everything in §3.

### 2.1 Registry rules — ratified Phase 0

The `_typeRegistry` contract is small and deliberate (ratified per §13 Q17, [`decision-criteria.md`](047/decision-criteria.md#q17)):

- **Exact runtime type only.** Lookup is `_typeRegistry.TryGetValue(element.GetType(), ...)`. No assignable / base-match. Subtype dispatch is a footgun under the split-library plan.
- **No override.** Duplicate registration (the element type is already registered, including by a built-in dispatcher arm) **throws at registration time**. There is no `RegisterOverride` verb in v1; test fakes compose a `Reconciler` from scratch with the registry contents they need (see §13 Q9). Adding `RegisterOverride` later is non-breaking — existing `RegisterType` callers keep working unchanged.
- **No open generics.** `RegisterType<DataGrid<>, _>` is not supported in v1; open generic element types interact badly with trim and AOT.
- **`RegisterType` stays the verb.** No rename, no split between first-party and external registration verbs (see §13 Q5). After the split-library plan (§1.1), first-party `Reactor.Controls.*` packages register through the same surface as downstream consumers — they are equal citizens at runtime, and a single verb is a true reflection of that.

These rules apply to both v1's handler-protocol surface (§4) and any future descriptor surface (§6).

---

## §3 What the engine actually does for a built-in control

Strip down `MountToggleSwitch` to its essentials and the engine touches the following machinery:

1. **Allocation/rental.** `_pool.TryRent(typeof(ToggleSwitch)) as ToggleSwitch ?? new ToggleSwitch()`.
2. **Initial property write.** `ts.IsOn = el.IsOn` — directly, no suppression needed at mount because no handler is attached yet.
3. **Setter array application.** `ApplySetters(el.Setters, ts)`.
4. **Tag binding.** Conceptually `SetElementTag(ts, el)` — the attached DP that lets event handlers re-look-up the current element on each fire.
5. **Shared-trampoline event wiring.** For ToggleSwitch's `Toggled` event, `EventHandlerState.ToggleSwitchToggledTrampoline` is attached at most once per native DO. The trampoline reads `ReactorAttached.StateProperty.Element` to get the current element and invokes `el.OnIsOnChanged`. Programmatic writes from the update path call `ChangeEchoSuppressor.BeginSuppress(ts)` first; the trampoline's first line is `if (ShouldSuppress(ts)) return;`.
6. **Modifier pipeline.** Runs automatically after the mount returns — see `Reconciler.Mount.cs:184`. Pointer events, focus refs, accessibility, theme bindings, resource overrides, automation-name fallback.
7. **Child reconciliation.** Not for ToggleSwitch, but for containers — keyed LIS via `ChildReconciler.Reconcile`.

The update path is symmetric: re-runs setters, refreshes the tag, re-applies modifiers (with diff against old modifiers), and — critically — uses `ChangeEchoSuppressor.BeginSuppress` before any programmatic write to a value-bearing DP whose change event the user might be listening to.

Three of these mechanisms (tag, trampolines, echo suppressor) all share the **same attached DP** — `ReactorAttached.StateProperty` carrying a `ReactorState` object that bundles the current element, the per-event delegate handles, an echo-suppress counter, and (for items containers) a `ReactorListState`. The reason for one shared attached DP rather than three is documented at `Reconciler.cs:269-310`: WinRT projection can produce two managed RCWs for the same native DependencyObject, and anything keyed by managed-wrapper identity (CWT, instance fields) returns different state for each wrapper. **The attached DP lives on the native object, so every wrapper sees the same state.** This is a hard-won invariant (issues #86, #114) and any extensibility design must respect it.

---

## §4 Straw-man v1 — handler protocol with rich context

The straightforward way to expose all of §3 is to formalize a handler interface and ship a context object whose methods are the *only* way to touch the invariant-sensitive machinery:

```csharp
public interface IElementHandler<TElement, TControl>
    where TElement : Element where TControl : UIElement
{
    TControl Mount(MountContext ctx, TElement element);
    void Update(UpdateContext ctx, TElement oldEl, TElement newEl, TControl control);
    void Unmount(UnmountContext ctx, TControl control) { }
    void ReconcileChildren(ChildReconcileContext ctx, TElement oldEl, TElement newEl, TControl control) { }
}
```

**`Update` returns `void`** (ratified Phase 0, see §13 Q12 and [`decision-criteria.md`](047/decision-criteria.md#q12)). Substitution-mid-update — handler returning a *different* control — is forbidden in v1. Type changes flow through the existing unmount-and-remount path; the handler mutates `control` in place or accepts that the engine remounts. Widening to `UIElement? Update(...)` later is non-breaking if a real need surfaces. Matches React Native Fabric's `updateProps(oldProps, newProps) → void` shape.

The `MountContext` / `UpdateContext` expose the engine's mechanisms as typed operations:

```csharp
public readonly ref struct MountContext
{
    public Action RequestRerender { get; }
    public UIElement? MountChild(Element child);
    public void ApplySetters<T>(Action<T>[] setters, T c) where T : class;
    public ReactorBinding<TElement> BindFor<TElement>(FrameworkElement c, TElement el)
        where TElement : Element;

    // Engine owns pool policy. The handler hands over a factory; the engine
    // either rents or invokes it. Pool invariants are not visible to authors.
    public T AllocateControl<T>(Func<T> factory) where T : class;

    // Type-safe context push. No (object, object?) bag — keeps the same
    // generic-keyed model as UseContext<T> on the consumer side.
    public IDisposable PushContext<T>(T value);
    public IDisposable PushStaggerScope(TimeSpan delay);
}

public readonly struct ReactorBinding<TElement> where TElement : Element
{
    // Wire an event ONCE via the shared trampoline. Handler receives the
    // current TElement so closures refresh automatically across re-renders.
    public void OnPointerPressed(Action<TElement, PointerRoutedEventArgs> handler);
    public void OnTapped(Action<TElement, TappedRoutedEventArgs> handler);
    public void OnKeyDown(Action<TElement, KeyRoutedEventArgs> handler);
    // ... full family ...

    // Generic wire-once / refresh-via-tag for control-specific events
    // (CanvasControl.Draw, MonacoEditor.TextChanged, ToggleSwitch.Toggled).
    public void OnCustomEvent<TArgs>(
        Action<FrameworkElement, EventHandler<TArgs>> subscribe,
        Action<TElement, TArgs> handler);

    // The only correct way to write a value-bearing DP from Update.
    public void WriteSuppressed(Action mutate);
}
```

A handler authored against this surface is structurally identical for built-in and external controls:

```csharp
public sealed class ToggleSwitchHandler : IElementHandler<ToggleSwitchElement, ToggleSwitch>
{
    public ToggleSwitch Mount(MountContext ctx, ToggleSwitchElement el)
    {
        var ctrl = ctx.AllocateControl(static () => new ToggleSwitch());
        ctrl.IsOn = el.IsOn;
        var bind = ctx.BindFor(ctrl, el);
        bind.OnCustomEvent<RoutedEventArgs>(
            subscribe: (c, h) => ((ToggleSwitch)c).Toggled += (s, e) => h(c, e),
            handler:   (cur, _) => cur.OnIsOnChanged?.Invoke(((ToggleSwitch)ctrl).IsOn));
        ctx.ApplySetters(el.Setters, ctrl);
        return ctrl;
    }

    public void Update(UpdateContext ctx, ToggleSwitchElement o, ToggleSwitchElement n, ToggleSwitch ctrl)
    {
        if (o.IsOn != n.IsOn)
            ctx.BindFor(ctrl, n).WriteSuppressed(() => ctrl.IsOn = n.IsOn);
        ctx.ApplySetters(n.Setters, ctrl);
    }
}
```

The test of completeness is straightforward: **can you author a working `Slider` element using only public API, indistinguishable from the built-in?** A Slider exercises echo-suppressed value writes (`Value` → `ValueChanged`), pool-survivable event subscription (drag interactions plus ListView recycling), modifiers, and the setter modifier chain. If all four are reachable from the public surface, the protocol is complete.

This is a real, shippable design. It would close the asymmetry. But the discussion that prompted this spec ended with a healthier skepticism: **is this the right shape, or is it just the shape we already have, with `internal` replaced by `public`?**

---

## §5 Why v1 alone isn't the answer

Three observations the v1 design doesn't answer:

### 5.1 The protocol is wide because the engine is wide

`MountContext` exposes ~8 methods; `ReactorBinding<T>` exposes ~25 (one per shared-trampoline event family). Each method codifies an invariant. That's a lot of API surface for what is conceptually "WinUI control wrapped in a record." A naive read of "what should a handler look like" is "construct a WinUI control and write some properties," but the actual minimum-correct handler requires understanding tag refresh, echo suppression, trampoline reuse, pool rental, and modifier interaction. The protocol *encodes* those concerns, but it also *demands* them — every author has to learn them.

For comparison: writing a WinUI `UserControl` directly involves none of this machinery. The reason Reactor needs it is the *re-rendering loop*. The trampoline exists because we don't want to detach/reattach on every render. The echo suppressor exists because programmatic writes look like user interactions. The pool exists because GC pressure under high-frequency list scroll. **Each piece of machinery is justified, but each is a downstream consequence of the framework's update model.**

### 5.2 Most of the protocol is mechanical

Look at a handler for any value-bearing built-in (ToggleSwitch, Slider, NumberBox, ColorPicker, RatingControl, CalendarDatePicker, …). The mount and update bodies follow the same shape:

- Allocate / rent the WinUI control.
- For each `(WinUIProp, ElementProp)` pair, write the prop. If the WinUI prop is value-bearing and has a change event the user can subscribe to, suppress the echo.
- For each `(WinUIEvent, ElementCallback)` pair, wire the event through the shared trampoline.
- Apply setters. Return.

This is *data*, not *logic*. Every value-bearing control has the same structure with different (prop, event) names plugged in. The v1 protocol asks every handler author to write the same code by hand, hoping they remember `WriteSuppressed` and `OnCustomEvent` rather than `ctrl.IsOn = el.IsOn` and `ctrl.Toggled += ...`.

### 5.3 We're choosing the runtime to be the place where invariants get checked

`ReactorBinding<T>.WriteSuppressed` is a runtime call. Forgetting it is a runtime bug. If the binding were described *declaratively* — "this element prop maps to this WinUI prop, suppressing this event" — the framework could enforce the invariant at registration time, generate the suppression call automatically, and the author can't forget.

The same is true for trampolines: if "this element callback dispatches from this WinUI event" is a declarative pair, the wiring is generated. The author doesn't write subscription code at all.

---

## §6 Simplification direction: declarative control descriptors

Replace the imperative handler with a **descriptor** — a data structure that names properties, events, and their bindings. The reconciler interprets the descriptor:

```csharp
public static readonly ControlDescriptor<ToggleSwitchElement, ToggleSwitch> Descriptor =
    new ControlDescriptor<ToggleSwitchElement, ToggleSwitch>
    {
        Factory  = static () => new ToggleSwitch(),

        Properties =
        [
            Prop.OneWay  (get: e => e.OnContent,  set: (c, v) => c.OnContent  = v),
            Prop.OneWay  (get: e => e.OffContent, set: (c, v) => c.OffContent = v),

            // Controlled: every interaction is a strongly-typed delegate.
            // No reflection, no nameof-resolved event lookups — the descriptor
            // is callable as-is under Native AOT / full trim.
            Prop.Controlled(
                get:          e => e.IsOn,
                set:          (c, v) => c.IsOn = v,
                subscribe:    (c, h) => c.Toggled   += h,
                unsubscribe:  (c, h) => c.Toggled   -= h,
                readBack:     c => c.IsOn,
                callback:     e => e.OnIsOnChanged),
        ],

        Events =
        [
            // Non-prop-bound events (e.g., Tapped on a Card) declared here.
        ],
    };
```

**AOT note:** all descriptor entries are strongly-typed lambdas — no `nameof(...)`-resolved-via-reflection, no `GetEvent(...)`, no `GetProperty(...)`. Reactor's trimming and AOT story (§13 Q2) depends on this. A source generator (when it eventually lands per §7's deferred status) can validate `nameof(...)` consistency at compile time and emit the typed delegates, but the runtime API surface stays reflection-free even when authored by hand.

What this buys:

- **`Prop.OneWay`** is a property write on Mount and on diff during Update. The reconciler compares old/new element prop and skips the write when equal. No echo possible because nothing fires the change event.
- **`Prop.TwoWay`** (controlled) is a `(prop, event, readBack, callback)` quadruple. The reconciler:
  - Subscribes once to the change event via the shared trampoline.
  - Writes the prop on diff, suppressing the event for that write.
  - The trampoline reads back the post-event value via `readBack` and invokes `callback`.
- Setters and modifiers are still per-element; they remain on `ExtensibleElement<TControl>`.
- `ChildReconciler` integration is a separate descriptor field. The supported strategies are first-class on the public surface (per §1.1, containers are part of the split-library plan and cannot rely on a private escape hatch). **Resolved (Phase 0 §13 Q4):** the strategy types are concrete C#, not an enum, and ship in Phase 1 as part of the v1 protocol surface so L13 (split-library) has a stable shape to bind to:

  ```csharp
  public abstract record ChildrenStrategy<TElement, TControl>
      where TElement : Element where TControl : UIElement;

  // Leaf control, no children at all. TextBlock, Image, ToggleSwitch.
  public sealed record None<TElement, TControl>
      : ChildrenStrategy<TElement, TControl>;

  // One typed content slot. Border, ContentPresenter, Button-with-Content.
  public sealed record SingleContent<TElement, TControl>(
      Func<TElement, Element?> GetChild,
      Action<TControl, UIElement?> SetChild)
      : ChildrenStrategy<TElement, TControl>;

  // Flat panel of children written to a UIElementCollection.
  // StackPanel, Grid, Canvas. Engine handles the spec-042 keyed reconcile.
  public sealed record Panel<TElement, TControl>(
      Func<TElement, IReadOnlyList<Element>> GetChildren,
      Func<TControl, UIElementCollection> GetCollection)
      : ChildrenStrategy<TElement, TControl>;

  // Multiple named slots: header / content / footer; primary / secondary actions.
  // NavigationView, Expander, TabViewItem.
  public sealed record NamedSlots<TElement, TControl>(
      IReadOnlyList<NamedSlot<TElement, TControl>> Slots)
      : ChildrenStrategy<TElement, TControl>;

  public sealed record NamedSlot<TElement, TControl>(
      string Name,
      Func<TElement, Element?> GetChild,
      Action<TControl, UIElement?> SetChild);

  // Templated items host. Plugs into spec 042 keyed list reconciliation.
  // ListView, ItemsView, TemplatedList.
  public sealed record ItemsHost<TElement, TControl>(
      Func<TElement, IItemsSource> GetItemsSource,
      Func<TControl, IItemsContainer> GetContainer,
      ItemsHostOptions Options)
      : ChildrenStrategy<TElement, TControl>;

  // Escape hatch for irregular containers (PivotItem, custom virtualization).
  // Author takes responsibility for pool integration and key matching.
  public sealed record Imperative<TElement, TControl>(
      Action<ChildReconcileContext, TElement, TElement, TControl> Reconcile)
      : ChildrenStrategy<TElement, TControl>;
  ```

  **Attached props** declared on the *container* descriptor — `Grid.Row`, `Grid.Column`, `Canvas.Left`, `DockPanel.Dock`, etc. The container is responsible for writing them to child WinUI controls during child reconcile:

  ```csharp
  public sealed record AttachedPropWriter<TChildElement>(
      string Name,
      Func<TChildElement, object?> Get,
      Action<UIElement, object?> Write);

  // On the container descriptor:
  AttachedProps = [
      new AttachedPropWriter<Element>(
          "Grid.Row",
          e => e.GetAttached(GridAttached.Row),
          (ui, v) => Grid.SetRow((FrameworkElement)ui, (int)(v ?? 0))),
      // ...
  ],
  ```

  Every strategy goes through the same engine pipeline — child Mount/Update/Unmount, key matching, pool integration, modifier reapply. A control descriptor picks one strategy; `Imperative` exists only for cases nothing else fits.

### 6.1 Controlled vs uncontrolled vs initial — making the distinction explicit

React (and React Native) distinguish *controlled* inputs (state is authoritative, render writes the value, change events propagate to state) from *uncontrolled* inputs (the native control owns its value; the framework writes it once at mount and then only reads it via events). Reactor today implicitly assumes every prop is controlled, which is the wrong default for some real cases:

- A `TextBox` with an `InitialText` the framework writes once and never again — typing should not be "fought" by a re-render.
- A `Slider` that reports user drags but is never driven from state.
- A picker whose `SelectedIndex` is initial seed only.

These cases today require imperative escapes or fighting the diff. The descriptor model should make the distinction explicit and let the engine emit the right code for each:

```csharp
Properties =
[
    // Write on mount only. Never touched on update. Echo impossible.
    Prop.Initial    (e => e.InitialText,  (c, v) => c.Text = v),

    // Write on mount and on diff during update. No event subscription.
    // (Read-only props: foreground brush, header text, glyph, etc.)
    Prop.OneWay     (e => e.Header,       (c, v) => c.Header = v),

    // Controlled: framework writes from element state, suppresses or
    // round-trips the resulting change event, callback notifies state.
    Prop.Controlled (e => e.IsOn,         (c, v) => c.IsOn = v,
                     changeEvent: nameof(ToggleSwitch.Toggled),
                     readBack:    c => c.IsOn,
                     callback:    e => e.OnIsOnChanged),

    // Uncontrolled: framework never writes after mount; only subscribes
    // to the change event and invokes the callback. The control's own
    // value is authoritative.
    Prop.Uncontrolled(initialValue: e => e.InitialIsOn,
                      writeOnce:    (c, v) => c.IsOn = v,
                      changeEvent:  nameof(ToggleSwitch.Toggled),
                      readBack:     c => c.IsOn,
                      callback:     e => e.OnIsOnChanged),
],
```

Concrete savings: `Prop.Initial` and `Prop.OneWay` *never* need echo suppression because they don't subscribe to a change event from the engine side. `Prop.Uncontrolled` only ever writes once, so the suppression window is `Mount` only. Only `Prop.Controlled` is the case §8 worries about — and even there, the descriptor knows enough at registration time to drive whichever solution §8 picks (tight diff, suppression, or round-trip; see §8.1).

This subsumes a real fraction of the §8 audit by construction: if an author marks a prop `Initial` or `OneWay`, no audit is needed for it at all.

#### 6.1.1 `.HandCodedControlled` / `.HandCodedEvent` — multi-event composition (Phase 3 prerequisite)

**Resolved (Phase 2, 2026-05-26 — §13 Q1 follow-up).** The `.Controlled<TValue, TArgs>` classification above stores its trampoline in a closed-generic payload keyed by `(TElement, TControl, TValue, TArgs)`. That keying is fine for **single-event controls** (ToggleSwitch / Slider / Border — what Phase 2 measured) but breaks down for **multi-event controls** because the §9.2 `ControlEventStateBox` is a single-slot discriminated wrapper: a second `.Controlled` or `.Event` entry on the same control would request a different closed-generic payload type, hit the type discriminator's mismatch path, and clobber the first entry's slot.

Two new classifications cover the multi-event case without waiting for source-gen (§7):

```csharp
// Same shape as .Controlled, but the author supplies:
// - the static trampoline (hand-authored — direct field access, no per-fire indirection)
// - typed slot accessors into a per-descriptor TPayload class (typically reuses
//   the existing §9.2 per-control-class payload — TextBoxEventPayload, etc.)
.HandCodedControlled<TValue, TArgs>(
    get:        e => e.Text,
    set:        (c, v) => c.Text = v,
    subscribe:  (fe, h) => ((TextBox)fe).TextChanged += h,
    callback:   e => e.OnTextChanged,
    trampoline: TextChangedTrampoline,            // static — captures nothing
    slotIsNull: p => p.TextChangedTrampoline is null,
    setSlot:    (p, t) => p.TextChangedTrampoline = (TextChangedEventHandler)t)

// Fire-and-forget event (no DP round-trip). For Button.Click, ListView.ItemClick,
// MenuFlyoutItem.Click, NavigationView.ItemInvoked, Hyperlink.Click, etc.
.HandCodedEvent<TArgs>(
    subscribe:  (fe, h) => ((Button)fe).Click += h,
    callback:   e => e.OnClick,
    trampoline: ClickTrampoline,
    slotIsNull: p => p.ClickTrampoline is null,
    setSlot:    (p, t) => p.ClickTrampoline = (RoutedEventHandler)t)
```

The hand-coded shape pays the §9.2 per-control-class payload allocation **exactly once**, holds N trampoline slots in one box, and dispatches per-fire identically to the hand-coded handler. The Q1 +9.6% / +19.3% interpreter overhead (`PropEntry<>.Mount` virtual dispatch + delegate getter/setter) shrinks toward noise for controls authored this way because the trampoline body is open-coded and reads through `GetElementTag` directly — no entry-level lambda indirection.

**Author guidance** (Phase 3 default):

| Control shape | Classification |
|---|---|
| Zero events | `.OneWay` / `.OneWayConditional` / `.Initial` only |
| One event, round-trip with DP | **`.Controlled<TValue, TArgs>`** — uses the generic interpreter; no TPayload required |
| One event, fire-and-forget | `.HandCodedEvent<TArgs>` — needs a per-descriptor payload, but TPayload has just one slot |
| Two or more events on one control | **`.HandCodedControlled` / `.HandCodedEvent` exclusively** — reuses the §9.2 per-control-class payload (e.g., `TextBoxEventPayload`) |
| Perf-critical mount path (measured M2/M10 cost matters) | `.HandCodedControlled` even for single-event controls — collapses interpreter overhead |
| Truly irregular control (logic doesn't fit §6.1) | Fall through to `IElementHandler<,>` (§4) escape hatch |

**Source-gen interop.** When source-gen (§7) lands, the generator emits exactly this `.HandCodedControlled` / `.HandCodedEvent` shape from the descriptor declaration. Phase 3 controls authored hand-coded today port forward to source-gen by **deletion of boilerplate**, not rewrite.

See §9.2 for the per-descriptor payload storage shape (the `ControlDescriptor<TElement, TControl, TPayload>` overload).

### 6.2 Modifier × declarative-prop precedence

A descriptor that declares `Prop.OneWay(e => e.Background, (c,v) => c.Background = v)` and an element whose modifier chain includes `.Background(brush)` both want to write `Background`.

**Resolved (Phase 0 §13 Q13).** **Modifier-after-prop.** Descriptor `Prop.OneWay` writes first; `ApplyModifiers` runs after and wins. Element-record props act as defaults; modifiers override. This preserves today's `MountXxx → ApplyModifiers` ordering — existing apps don't shift, and the semantics already-shipped consumers depend on stay intact.

```
Mount sequence:
  1. descriptor.Properties[i] writes  c.Background = el.Background
  2. ApplyModifiers runs            → modifiers can override

Result: modifier always wins.
Element.Background acts as default; .Background(brush) modifier overrides.
```

**Per-field opt-in stays as a future, non-breaking extension.** A descriptor entry that wants prop-wins precedence (e.g. a control whose author asserts the element-record field is the authoritative source) can later declare `Prop.OneWay(..., precedence: Precedence.PropWins)` without breaking any existing consumer. Not in v1.

Rejected alternatives:
- **Props-after-modifiers** as the default — cleaner semantics for new strongly-typed descriptors, but a back-compat break for every shipping app that depends on modifier-final ordering.
- **Last-writer-wins by source order** — too easy to surprise; the source order of a setters chain isn't visually obvious at the call site.

The handler interface goes away. Mount and Update become **interpreters of the descriptor**. Authors write data, not code. This is closer to how XAML works (DPs + events declared metadata), but resolved against `Element` records instead of XAML markup.

**Risk:** the descriptor needs to cover every shape a control might want — sometimes there's no clean (prop, event) pair (e.g., `NumberBox.NumberFormatter` is a property whose change triggers internal recomputation but no event, `TextBox.PlaceholderForeground` is a `Brush` themed prop, `CanvasControl.Draw` is an event with a `DrawingSession` arg that has no element-prop counterpart). The descriptor model has to either grow special cases or fall back to imperative handlers for the irregular cases. Probably both: a descriptor with an `Imperative` escape hatch covers the long tail.

---

## §7 Simplification direction: source-generated handlers

**Status: deferred to future work.** This section describes a direction the framework could move toward later, but it is explicitly *not* on the path for the initial implementation of this spec. The reasoning:

- Hand-coded handlers have served Reactor well so far. The flexibility of being able to read and edit the mount/update code by hand has been load-bearing as the framework evolved. Source-gen ossifies that surface.
- The argument for source-gen is primarily *cycle time*: when adding a new WinUI control requires changes spread across multiple files, source-gen reduces that to "annotate the element record." That cycle-time cost is not the current bottleneck — Reactor's catalog stabilizes, and the controls that get added at this point are infrequent enough that the per-control authoring effort is not the rate-limiter.
- Most of the *performance* wins this spec attributes to source-gen (§11.3, §12.4) can also be achieved by hand-coded handlers, as long as the per-control event tables (§9) and `ReactorState` shape (§9.2) are designed to allow per-handler specialization. Source-gen *automates* those wins; it does not enable them.
- AOT correctness can be maintained with hand-coded handlers using the same `[DynamicallyAccessedMembers]` discipline Reactor already uses elsewhere (§"is there any reflection in mainline code paths" — none in the hot path; the `Activator.CreateInstance` fallback in `ComponentElement` is trim-annotated).

**Revisit source-gen when:**
- The WinUI-change → Reactor-update cycle becomes a felt bottleneck (new control or version of an existing control requires multi-file changes that slow down the team).
- A new platform target (Native AOT shipping, browser/WASM, etc.) makes the reflection footprint of hand-coded `RegisterType` lambdas a binary-size or trim problem.
- A descriptor-style authoring surface ships and we want compile-time validation of the descriptor entries.

The remainder of this section describes the source-gen shape *as it would look if revisited*, for completeness — but the implementation tracks (§14) target hand-coded handlers throughout.

### 7.1 What the source-gen shape would look like

Push the declaration earlier — into source attributes on the element record — and have a source generator emit the mount/update code at compile time:

```csharp
[ReactorControl(typeof(ToggleSwitch))]
public partial record ToggleSwitchElement : ExtensibleElement<ToggleSwitch>
{
    [Bind(nameof(ToggleSwitch.OnContent))]
    public string OnContent { get; init; } = "On";

    [Bind(nameof(ToggleSwitch.OffContent))]
    public string OffContent { get; init; } = "Off";

    [Bind(nameof(ToggleSwitch.IsOn),
        TwoWayChangeEvent = nameof(ToggleSwitch.Toggled))]
    public bool IsOn { get; init; }

    [Wire(nameof(ToggleSwitch.Toggled))]
    public Action<bool>? OnIsOnChanged { get; init; }
}
```

The generator emits:
- A `ToggleSwitchElementHandler` class (or whatever equivalent the runtime expects).
- Per-property diff-and-write code with echo-suppress wrapping where `TwoWayChangeEvent` is present.
- Per-event subscription code that goes through a *generated* per-control event handler state struct — no shared `EventHandlerState` mega-record with 30+ fields.
- A static registration call collected by a source-generated `BuiltinRegistrations.RegisterAll(Reconciler)`.

Performance properties:
- **Zero dictionary lookup at dispatch.** Generator emits a `Type → handler` switch that the JIT inlines.
- **Zero generic per-control state struct.** Each control's trampoline tables are static fields on its generated handler. ToggleSwitch needs one (Toggled); Slider needs one (ValueChanged); Button needs one (Click). No 30-field `EventHandlerState`.
- **Zero allocation per re-render** for value writes — generated diff code uses `EqualityComparer<T>.Default` and writes only on change.
- **AOT-perfect** — no reflection, no runtime code-gen.

This is the version of the design where the framework gets *smaller*, not larger. The handler interface and descriptor object both go away in steady state — they're an intermediate representation the generator uses internally. The runtime ships a tiny dispatcher + the modifier pipeline + the attached-DP state struct, and everything else is generated code per control.

**Risk:** source generators are heavier to maintain than runtime registrations. Bugs in the generator are harder to debug than bugs in a handler class. Editor tooling (IntelliSense on the generated handler) is workable but not great. Worth a spike before committing.

---

## §8 Simplification direction: eliminate the change-echo suppressor

> **Resolved (Phase 0 §13 Q3).** Ship "delete + tight diff" for the
> trivial sites, per-control tolerance metadata for the coercion / float
> sites, and a one-off imperative shim for ColorPicker. Do **not** build
> §8.1's `mostRecentEventCount` round-trip — only 1 / 24 sites needed it
> and that single site is solvable with a per-handler shim. Per-call-site
> data: [`docs/specs/047/audits/begin-suppress-audit.csv`](047/audits/begin-suppress-audit.csv);
> tally + reasoning: [`begin-suppress-audit.md`](047/audits/begin-suppress-audit.md);
> decision: [`decision-criteria.md`](047/decision-criteria.md#q3).

The echo suppressor exists because the engine writes value-bearing DPs from the update path, the WinUI control fires its change event, the trampoline invokes the user's callback with the value the engine just wrote, and (if user state has moved on between render and event-dispatch) that callback writes the *old* value back into the *new* state. Spec 030, issue #86, the PropertyGrid cross-row-swap bug.

The audit shrunk the problem materially. The headline tally across 24 production `BeginSuppress` call sites:

| Category | Count | Treatment |
|---|---:|---|
| `eliminable-tight-diff` | 14 | Delete the `BeginSuppress` call. Handler-side `lastFired != tag.X` check (or simply the existing element-prop diff) is sufficient. No new machinery. |
| `coercion` | 4 | Per-control tolerance metadata on the descriptor / handler. NumberBox/Slider declare `coercedBy: [Minimum, Maximum]`; engine records "expected Y, suppress one echo for Y ± tolerance." |
| `float-precision` | 4 | Same as coercion, with a numeric tolerance instead of a coercion source. Matches today's `AreNumberBoxValuesEquivalent` discipline. |
| `items-coercion` | 2 | `CalendarView.SelectedDates` stays as a per-control imperative shim — diff semantics don't generalize. |
| `user-state-races-render` | 1 | **ColorPicker only.** Per-handler `expectedColor` capture + tolerance comparison. The one site that would have driven §8.1. |
| `defensive-redundant` | 1 | `AutoSuggestBox.Text` — already documented as unnecessary in its own code comment. Delete outright. |

**Phase 4 plan**: 14 trivial deletions + 1 redundant deletion in one PR; the 8 coercion / float-precision sites get tolerance metadata declared by their descriptors; the 1 ColorPicker site gets an imperative shim; `ChangeEchoSuppressor.cs` is deleted at the end. `ReactorBinding<T>.WriteSuppressed` (the public primitive — §13 Q19) keeps its signature throughout; its implementation swaps under the hood without changing the API.

> **Phase 1 KD-1 (revisit during Phase 4).** Phase 1 ships an interim
> `ChangeEchoSuppressor.ShouldSuppress` drain inside the
> `ReactorBinding<T>.OnCustomEvent` trampoline so that programmatic writes
> via `WriteSuppressed` are consumed on the V1 path the same way the legacy
> per-control trampolines (`EnsureToggleSwitchWiring` etc.) consume them.
> When the Phase 4 work above replaces `ChangeEchoSuppressor` with
> per-control tolerance / coercion metadata, that interim drain migrates
> with it — the descriptor-declared echo shape takes over from the universal
> counter. Tracked in
> [`tasks/047-extensible-control-model-phase1-implementation.md`](tasks/047-extensible-control-model-phase1-implementation.md#kd-1--oncustomevent-must-drain-changeechosuppressor)
> ("Phase 1 known defects / Phase 4 followups", KD-1).

The remaining echo cases that *don't* appear in the audit but are worth naming for future controls:

- **Focus-property writes** (engine writes `IsTabStop`/`FocusState`/programmatic `Focus()`). No current sites; if one appears, it falls into `eliminable-tight-diff`.
- **Animation interpolation values firing change events mid-storyboard** (e.g., entrance-animated `Slider.Value`). No current sites; would need per-control "suppress during animation" handling if it surfaces.

Net effect: `ChangeEchoSuppressor` module deleted (Phase 4), echo handling moves into per-control descriptors with explicit tolerance / coercion metadata, the `WriteSuppressed` public primitive is preserved as the stable author-facing surface. One fewer invariant for handler authors to learn — the descriptor declares the echo shape, the engine handles it.

### 8.1 Considered and rejected — `mostRecentEventCount` round-tripping

§8.1 originally proposed React Native's monotonic event-counter round-trip as a uniform replacement for `BeginSuppress`. **Rejected** at Phase 0 ([`decision-criteria.md` Q3](047/decision-criteria.md#q3)): only 1 / 24 sites (`ColorPicker.Color`) genuinely required it, and one site is too thin an evidence base for protocol-level machinery (per-control `int` counter slot on `ReactorState`, descriptor-level wiring on every value-bearing prop, native-side ack tracking). The audit collapsed the design space; the §8 direction above plus a per-handler ColorPicker shim covers the same ground. The rejected alternative is preserved in [`decision-criteria.md`](047/decision-criteria.md#q3) for future reference if a *second* `user-state-races-render` site surfaces after the split-library plan.

### 8.2 Setters precedence — a current correctness hole

A latent issue worth fixing as part of this work, called out in §13 Q8 below: `ApplySetters` runs after declarative property writes today and bypasses any echo suppression scope. A user writing `Set(ts => ts.IsOn = true)` on a `ToggleSwitch` whose `el.IsOn = false` produces an unmasked write that *will* fire `Toggled` and feed back into state on the next event-loop tick. The descriptor model should require setters to either run inside the same suppression / round-trip scope as declared props, or be opted into an explicit raw-write mode (`Set.Raw(...)`) that the author has audited and accepted responsibility for. Default behavior should match declared props.

**Resolved** as a carve-out ahead of Phase 1 (per [`047/factoring-recommendation.md`](047/factoring-recommendation.md)). `ApplySetters` now enters a scope-based suppression mode on the control's `ReactorState` (a depth counter alongside the existing paired `EchoSuppressCount`) for the duration of the setter chain, so any change event raised by a setter-driven write is dropped without consuming a paired token. The M13 perf-bench check flips from `OnIsOnChangedFireCount = 1` to `0` on both `ReactorToday` and `ReactorV2`. Default behavior now matches declared props as called for above; an explicit raw-write opt-out (`Set.Raw(...)`) is still a future refinement should it become needed.

---

## §9 Simplification direction: per-control trampoline tables

`EventHandlerState` today carries one `Current<EventName>` field and one `<EventName>Trampoline` field for every event the reconciler knows how to wire — ~30 fields total. Most controls use 1–3 of these. A ToggleSwitch carries empty slots for `KeyDownTrampoline`, `PointerWheelChangedTrampoline`, `ButtonClickTrampoline`, … none of which it ever uses.

The reason for the shared struct is the modifier pipeline: pointer events, focus, key events can be attached to *any* element via modifiers, so the engine needs a uniform place to put them. That justifies *modifier-events* being shared. But control-intrinsic events (`Toggled`, `ValueChanged`, `Click`, `TextChanged`) don't need to live in the shared struct — they only fire from one control type.

### 9.1 The split is grounded in WinUI's own event taxonomy

The "modifier event vs. control event" split is not a Reactor invention — it mirrors a structural distinction WinUI makes in its own codegen model (`dxaml/xcp/tools/XCPTypesAutoGen/XamlOM/Model/`). Events fall into two categories:

**True routed events** — declared with `[EventFlags(IsControlEvent = true)]` (or `UseEventManager = true`) in the WinUI model. These tunnel and bubble through the visual tree, can be subscribed via `UIElement.AddHandler(routedEvent, handler, handledEventsToo)`, and have an associated `RoutedEvent` field. The list is short and essentially the UIElement input-event family:

- `PointerPressed / PointerMoved / PointerReleased / PointerEntered / PointerExited / PointerCaptureLost / PointerWheelChanged`
- `KeyDown / KeyUp / CharacterReceived / PreviewKeyDown / PreviewKeyUp`
- `Tapped / DoubleTapped / RightTapped / Holding`
- `GotFocus / LostFocus`
- `ContextRequested`
- The manipulation and drag-and-drop families

**Plain CLR events** — no `EventFlags` attribute in the model. Despite some declaring `Microsoft.UI.Xaml.RoutedEventHandler` as their delegate type (a historical naming quirk inherited from WPF), they are *not* registered with WinUI's routed-event manager. They have no `RoutedEvent` field, cannot be subscribed via `AddHandler`, do not tunnel or bubble, and fire only on the originating control instance. Examples:

- `ToggleSwitch.Toggled` (`Microsoft.UI.Xaml.Controls.cs:1051`)
- `ButtonBase.Click` (`Microsoft.UI.Xaml.Controls.Primitives.cs:327`)
- `RangeBase.ValueChanged` (`Microsoft.UI.Xaml.Controls.Primitives.cs:725`)
- `TextBox.TextChanged`, `NumberBox.ValueChanged`, `ColorPicker.ColorChanged`, `RatingControl.ValueChanged`, …

A click on a `Button` will not fire `Click` on the parent `Border` — only `Tapped` / `PointerPressed` will. The two categories really are different mechanisms underneath, and Reactor can match the WinUI structure 1:1.

### 9.2 A revised split

- **`ModifierEventHandlerState`** — shared across all controls, holds trampolines for the WinUI true-routed event family (pointer, key, tap, focus, context, manipulation, drag). Lives on `ReactorState`. These are the events that any modifier on any `FrameworkElement` might want to wire, so the slot has to be uniform.
- **Per-control event tables** — owned by each control's generated handler. Hold trampolines for the plain CLR events that fire only on that specific WinUI control type (`Toggled`, `Click`, `ValueChanged`, `TextChanged`, etc.).

**Placement matters.** The per-control table must live as a field *inside* the existing `ReactorState` payload (e.g., `object? ControlEventState` referencing a per-control-type struct), **not** as a separate attached DP per control type. WinUI's effective-value table costs ~24–32 bytes per attached DP per element; introducing a second attached DP per control type would cancel a meaningful fraction of the 424→32-byte saving and add a second dual-RCW–sensitive lookup. Keeping the per-control state inside the single `ReactorAttached.StateProperty` payload preserves the invariant from `Reconciler.cs:269-310` and keeps WinUI overhead flat. The shape is therefore:

```csharp
internal sealed class ReactorState
{
    public Element? Element;
    public ModifierEventHandlerState? Modifiers;   // routed-input events, rare
    public ControlEventStateBox? ControlEventState; // per-control payload, discriminated
    public ReactorListState? ListState;
}

// Discriminated wrapper. The Type pin is the source of truth; the Payload
// is the per-control struct boxed once (or held as a class instance).
internal sealed class ControlEventStateBox
{
    public Type HandlerType;   // identity of the handler that wrote Payload
    public object Payload;     // the per-control state struct/class
}
```

A handler reads `ControlEventState` only after verifying `HandlerType == typeof(this-handler)`. The cast is statically known per handler, so the JIT specializes the success path; the verification step is one reference compare.

**Why the discriminator matters** (and why a bare `object?` cast-by-handler is not safe enough):

- **Pool reuse.** A pooled `ToggleSwitch` rented for a new element must not see the previous tenant's event state. The pool's reset path clears `ControlEventStateBox` (or replaces it with a fresh one keyed to the new handler) on rent.
- **Handler override / `RegisterOverride`.** If an external assembly overrides `ToggleSwitchHandler` with a test fake, the override's per-control state shape may differ from the original. A bare cast would either silently misread or InvalidCastException at runtime; the discriminator turns this into a deterministic, diagnosable "stale handler-type" condition that the engine logs (and either resets or refuses).
- **Hot reload.** Re-loading a handler assembly under hot reload produces a new `Type` identity for the same handler class. A live element's `ControlEventState` still references the *old* `Type`. The engine must reset on detection, not cast across the version boundary.
- **Dual-RCW.** Two managed wrappers for the same native control still see the same `ReactorAttached.StateProperty`, so both see the same `ControlEventStateBox` — preserved invariant.

The reset contract: on `Pool.Return(control)`, the engine clears `ControlEventState`. On `Pool.Rent(control)`, the engine asserts `ControlEventState == null` (or resets it). The handler's `Mount` is responsible for allocating a fresh `ControlEventStateBox` with its own `HandlerType` stamp. `Update` reads only after verifying the stamp.

Each per-control payload struct is sized exactly to that control's event count (1–3 slots in practice).

#### 9.2.1 Per-descriptor payload composition (Phase 2 follow-up; §6.1.1)

The `ControlEventStateBox` is **one slot per control**. A handler reads it only after asserting `HandlerType == typeof(MyPayload)`. That single-slot discipline forces a design rule for descriptors with more than one event entry:

- The descriptor declares a `TPayload` parameter — typically reusing the **existing §9.2 per-control-class payload** (e.g., `ToggleSwitchEventPayload`, `TextBoxEventPayload`, `ImageEventPayload`). The hand-coded handler and the descriptor handler for the same control share the payload class; the box's `HandlerType` discriminator matches regardless of which shape authored the mount.
- Every event entry on the descriptor (`.Controlled`, `.HandCodedControlled`, `.HandCodedEvent`) writes into a distinct slot in that one payload — no second `ControlEventStateBox` ever appears.
- The descriptor builder's signature gains `TPayload`:
  ```csharp
  new ControlDescriptor<TextBoxElement, TextBox, TextBoxEventPayload>
  {
      Children = …,
      GetSetters = …,
      PayloadFactory = static () => new TextBoxEventPayload(),
  }
  ```
- Per-event slot access is typed via author-supplied `slotIsNull` + `setSlot` lambdas (see §6.1.1). The entry's `EnsureSubscribed` does `GetOrCreateControlEventPayload<TPayload>(ctrl)` and then writes through the supplied accessors. Per-fire indirection is zero (the static trampoline reads through `GetElementTag` directly).

**Why not closed-generic-per-entry?** The descriptor model's Phase 2 fast path (`DescriptorControlledPayload<TElement, TControl, TValue, TArgs>`) closes a fresh payload type per entry. That preserves typed slots without author-supplied accessors and is the right shape for **single-event** controls. It cannot compose to multi-event controls under the single-slot box constraint — two entries on the same control would clobber each other's `HandlerType` stamp. Phase 2 measured only single-event controls; Phase 3 multi-event controls use the per-descriptor TPayload shape instead.

**Why not source-gen yet?** §7 is the right long-term home for the per-control payload class — the generator emits it from the descriptor declaration. Until §7 lands, authors write the payload class (or reuse an existing §9.2 one) and the slot accessors by hand. The boilerplate is ~12 lines per event entry; source-gen removes it via deletion, not rewrite.

**`HandlerType` discriminator across hand-coded and descriptor shapes.** Because the payload class is shared, a `TextBoxEventPayload` written by `TextBoxHandler` is reused by `TextBoxDescriptor` — and vice versa — through pool rent/return cycles. The discriminator's hot-reload safety property (Phase 4+) survives: if `TextBoxDescriptor` is replaced by a recompiled descriptor at hot-reload time, the new descriptor still writes into `TextBoxEventPayload` and the box's stamp still matches.

There is no case where a `Toggled` handler attached to an ancestor needs a trampoline slot — `Toggled` cannot fire on an ancestor at all. So the per-control table only ever appears on attached state for the matching native control type, and never appears on unrelated controls.

### 9.3 Bubbling doesn't break the split

When a child Button's `PointerPressed` bubbles to an ancestor Border that subscribed via `.OnPointerPressed`, each `FrameworkElement` has its own `ModifierEventHandlerState` with its own pointer-trampoline slot. WinUI's routed-event manager walks the tree, firing each subscriber in turn; Reactor's per-element subscription is local to that one native FrameworkElement. The per-control tables don't participate because routed input events aren't in them.

### 9.4 Expected savings — most elements never wire a routed input event at all

The headline reason this split is worth doing is not just that `EventHandlerState` shrinks — it's that the *allocation* of any routed-input-event state can be skipped for the majority of elements.

In a typical app tree, control-intrinsic event subscriptions are far more common than modifier subscriptions to routed input events. A `ToggleSwitch` with an `OnIsOnChanged` callback, a `Button` with `OnClick`, a `Slider` with `OnValueChanged`, a `TextBox` with `OnTextChanged` — none of these inherently need any of the pointer/key/focus modifier surface. Today, wiring `OnIsOnChanged` allocates the full ~424-byte `EventHandlerState` to hold the one `Toggled` slot, even though the other ~50 slots (pointer, key, tap, focus, drag, manipulation) stay empty for the lifetime of the element.

After the split:

- The per-control table for `ToggleSwitch` is ~32 bytes (header + 1 Current + 1 Trampoline) and is the only thing allocated when only `OnIsOnChanged` is wired.
- `ModifierEventHandlerState` stays null on the element unless the user actually adds a `.OnPointerPressed` / `.OnTapped` / `.OnKeyDown` / `.OnGotFocus` modifier.

For interactive UIs that don't rely heavily on raw pointer/key hooks (most line-of-business apps, most forms-style screens, most settings UIs), `ModifierEventHandlerState` is null on the vast majority of elements. The 424-byte allocation that's mandatory today becomes a 32-byte allocation in the common case, with the routed-event state appearing only on the small minority of elements that actually use it.

This compounds with §11.3's bucketing: an element with no callbacks at all pays 0 bytes for event state; an element with one control-intrinsic callback pays ~32; an element with one routed-input modifier additionally pays for `ModifierEventHandlerState` but only that one; an element with everything pays no more than today.

Net effect: `ReactorState` shrinks, per-control event tables are exactly the size they need to be, and the routed-input event state is allocated only when actually needed. The source generator emits the per-control tables mechanically from `[Wire]` attributes or descriptor entries.

### 9.5 `handledEventsToo` — escape hatch, not trampoline doubling

**Resolved (Phase 0 §13 Q11).** Today's modifier API (`.OnPointerPressed(...)`, `.OnKeyDown(...)`, etc.) subscribes via `event +=`, which is equivalent to `UIElement.AddHandler(routedEvent, handler, handledEventsToo: false)`. Adding `.OnPointerPressedAny(...)` / `.OnKeyDownAny(...)` variants — one per routed-input event — would *double* the routed-input slot count in `ModifierEventHandlerState` and walk back a meaningful fraction of the §9 savings.

Phase 1 ships an imperative escape hatch instead:

```csharp
// On MountContext / UpdateContext. Bypasses the trampoline pattern entirely.
// Caller is responsible for unsubscribing on Unmount; no pool-survival.
public void AddRawRoutedHandler(
    UIElement target,
    RoutedEvent routedEvent,
    Delegate handler,
    bool handledEventsToo);
```

Authors who need `handledEventsToo: true` (catching a `KeyDown` an inner control already marked Handled, observing `PointerPressed` on a focus scope) take on the same correctness burden today's `RegisterType` lambdas have — no pool survival, no automatic trampoline reattach across re-mount. Acceptable because the use case is rare; the alternative was a permanent ~2× slot inflation paid by every element with a routed-input modifier.

Revisit only if Phase-2 macros (L4 / L8) show the §9 savings have headroom and authors are escaping the hatch frequently enough to be worth a typed surface.

---

## §10 What is the right delta over WinUI?

The user-facing prompt for this spec was: *"ideally we'd have something with less machinery to make the delta over WinUI directly be less."* Worth interrogating directly.

A hand-written WinUI control consumer does:

```csharp
var ts = new ToggleSwitch { IsOn = true };
ts.Toggled += (_, _) => HandleToggle(ts.IsOn);
parent.Children.Add(ts);
```

What does Reactor add that this doesn't have?
- **Declarative re-rendering** — the source-of-truth is C# state, and the tree rebuilds from it. The diff machinery is the price.
- **Pool survival** — the same `ToggleSwitch` instance gets reused across re-mounts and list recycling, which means the subscription on `Toggled` must outlive any individual handler closure. The trampoline pattern is the price.
- **Modifier composability** — `.OnPointerPressed(...)` works on any element. The shared modifier pipeline is the price.
- **Update echoes** — writing `ts.IsOn = false` programmatically fires `Toggled`. The echo suppressor (or, per §8, a tighter diff gate) is the price.

The honest answer: **a thin Reactor control over WinUI is allowed to be very thin.** Specifically — if a control has no two-way bindings, no callbacks the user can subscribe to, and no setters chain, it can degrade to "allocate, set props on mount, diff-and-set on update." The descriptor framework should let a control opt out of every piece of machinery it doesn't need, and the generated code should reflect that. A `RectangleElement` with a `Fill` brush and nothing else should generate code that's essentially `new Rectangle { Fill = brush }` plus a diff check on update.

The deltas worth keeping:
- Diff-driven property writes (the framework's reason to exist).
- Modifier pipeline (cross-cutting concern, justified surface).
- Child reconciliation (the framework's other reason to exist).
- Element-tag binding (single attached DP, cheap, single-purpose).

The deltas worth questioning:
- Echo suppressor (§8 — maybe replaced by tighter diff gates).
- Shared `EventHandlerState` mega-struct (§9 — maybe split modifier-events from control-events).
- `ApplySetters` re-running on every update (could be diffed: only re-run if the setters array reference changed).
- ElementPool itself (probably worth keeping, but worth re-measuring whether the cost of allocation is what we think it is on Modern CPUs/.NET 9).

§11 puts concrete byte numbers on this question: today a leaf with one callback adds ~800 bytes of overhead above the WinUI control, of which ~390 bytes are empty slots in the shared `EventHandlerState`. The simplification directions can drive that to ~280 bytes.

---

## §11 Per-element memory overhead — concrete bytes

To make the simplification targets in §6–§10 concrete, this section counts every byte of allocation we add **above the WinUI control itself** for each element in the shadow DOM. Modifier-related data structures are excluded — a performance-sensitive developer can construct element records directly via record initializer or factory call, bypassing the fluent `.Margin(8).Padding(4)…` chain that produces an `ElementModifiers` instance. The numbers below are for the **lean case**: an element with no modifiers attached.

All sizes are .NET 9 / x64. Object header = 16 bytes (sync block + method table). Reference fields = 8 bytes. `bool` and `int` may pack into alignment slack.

### 11.1 Today's per-element overhead

For a leaf control with one callback (`ToggleSwitchElement` with `OnIsOnChanged`) wired up:

| Object | Bytes | Notes |
|---|---|---|
| `ToggleSwitchElement` record | **~192** | base `Element` fields (16 nullable refs × 8 = 128) + record overhead (~16) + concrete fields (`IsOn` bool, `OnIsOnChanged` Action, `OnContent`, `OffContent`, `Header`, `Setters` array ref) ≈ 48 |
| `ReactorState` (attached DP value) | **~48** | `Element?` (8) + `EventHandlerState?` (8) + `EchoSuppressCount` int (4, padded) + `ReactorListState?` (8) + header (16) ≈ 48 |
| `EventHandlerState` (only when callback wired) | **~424** | 21 `Current<EventName>` fields × 8 = 168 + 29 `<EventName>Trampoline` fields × 8 = 232 + 1 bool + header (16) + padding ≈ 424 |
| Trampoline closure delegate | **~56** | one `RoutedEventHandler` per attached event; allocated lazily per WinUI event |
| User callback closure | **~56** | the `OnIsOnChanged` Action the user passed; allocated by the caller |
| WinUI attached-DP entry on the native control | **~24–32** | one row in the effective-value table; WinUI overhead but caused by us |
| **Total per element (one callback)** | **~800 bytes** | |

For a leaf control with **no callbacks** (e.g., a `TextBlockElement` with just text):

| Object | Bytes |
|---|---|
| `TextBlockElement` record | ~176 |
| `ReactorState` | 48 |
| `EventHandlerState` | 0 (stays null) |
| WinUI attached-DP entry | ~24 |
| **Total** | **~248 bytes** |

For an element with **many callbacks** (e.g., a `ButtonElement` plus pointer-event modifiers via `.OnPointerPressed(...).OnTapped(...).OnGotFocus(...)`): the user explicitly excluded modifiers from the count, but for reference, each additional wired modifier event adds one trampoline closure (~56) + one user callback closure (~56) to the running total, while filling previously-empty slots inside the shared `EventHandlerState` at no extra `EventHandlerState` cost. So `EventHandlerState` is a fixed ~424 bytes regardless of how many event slots are used — that's the design trade-off the shared struct made.

### 11.2 Where the bytes go — by mechanism

| Mechanism | Bytes per element | When | Why it exists |
|---|---|---|---|
| Element record base fields | ~128 | always | the 16 nullable cross-cutting fields on `Element` (`Modifiers`, `Attached`, `ThemeBindings`, `ImplicitTransitions`, `ThemeTransitions`, `LayoutAnimation`, `AnimationConfig`, `ElementTransition`, `InteractionStates`, `StaggerConfig`, `KeyframeAnimations`, `ScrollAnimation`, `ConnectedAnimationKey`, `ResourceOverrides`, `ContextValues`, `Key`) |
| Element record concrete fields | ~16–64 | always | per-control props + the `Setters` array reference |
| `ReactorState` | 48 | every element backed by a `FrameworkElement` | dual-RCW–safe attached-DP slot for the element pointer (and other per-control state) |
| `ReactorState.EchoSuppressCount` slot | 4 bytes inside the 48 | every element | echo-suppress counter; consumed only by value-bearing controls |
| `EventHandlerState` | 424 | any element with at least one wired event (control event or modifier event) | shared trampoline tables across all controls; most slots are empty for any given control |
| `EventHandlerState`'s empty slots | ~390 of the 424 | always (when EHS is allocated) | the shared design pays for slots a given control never uses |
| Trampoline closure | 56 each | one per attached WinUI event | the wire-once delegate the framework attaches to the native event |
| User callback closure | 56 each | one per user-supplied `OnX` lambda that captures | not framework overhead, but counted here because some controls (notably `Slider`) attach multiple |

The single largest offender is `EventHandlerState`. A `ToggleSwitch` uses 2 of its ~50 fields (1 Current, 1 Trampoline) yet pays for all 50. A `Button` with `.OnPointerPressed` and `.OnTapped` modifiers uses 4 fields. The average utilization is on the order of **5–10%**.

### 11.3 What each simplification direction buys

Applying the directions in §6–§9 to the same `ToggleSwitchElement` with one wired callback:

| Direction | New per-element overhead | Delta vs. today | How |
|---|---|---|---|
| **§6 Descriptor model alone** | ~800 bytes | 0 | Descriptors change the authoring surface, not the runtime allocation pattern. |
| **§8 Eliminate echo suppressor** | ~796 bytes | -4 | `EchoSuppressCount` field removed from `ReactorState`. Tiny on its own. |
| **§9 Per-control trampoline tables** | ~432 bytes | **-368** | Replace the shared 424-byte `EventHandlerState` with a per-control event-table object sized to its actual events. ToggleSwitch's table is ~32 bytes (1 Current + 1 Trampoline + header). |
| **§9 + bucketed `Element` base fields** | ~328 bytes | **-472** | The 14 cross-cutting fields on `Element` (animations, transitions, theme bindings, resource overrides, context values, attached props, etc.) bucket into a single nullable `ElementExtensions` sub-record. In the lean case (`Extensions == null`) the base shrinks from 128 to 16 bytes — only `Key` and `Modifiers` survive at the root. Same idea as the modifier bucketing from spec 034. |
| **§7 source-gen + all of the above** | ~280 bytes | **-520** | Generator inlines the trampoline-table directly into the user callback closure (no separate `EventHandlerState` allocation needed; the closure itself holds the current handler reference). `ReactorState` can shrink to 16 bytes (element pointer only) when the generator knows the control has no echo-prone props and no list state. |

The headline number: **a `ToggleSwitchElement` with one callback could shrink from ~800 bytes of overhead to ~280** — a ~65% reduction — purely from machinery changes, with no change to the user's authoring surface.

For the no-callback case (`TextBlockElement`):

| Direction | New per-element overhead | Delta vs. today |
|---|---|---|
| Today | ~248 bytes | — |
| §9 + bucketed `Element` base | ~152 bytes | -96 |
| §7 source-gen (no `ReactorState` if no callbacks ever wire) | **~88 bytes** | **-160** |

For pure-display elements the generator can prove `ReactorState` is never needed (no Tag refresh, no echo, no events) and skip allocating it entirely. The remaining ~88 bytes are the element record itself, which is mostly the cross-cutting field bucket plus `Content` and `FontSize`.

### 11.4 Allocation count, not just bytes

GC pressure scales with allocation *count* as much as total bytes — each Gen 0 allocation is a separate sweep candidate. Per leaf element today:

| Object | Count per element |
|---|---|
| Element record | 1 |
| `ReactorState` | 1 (per mounted control) |
| `EventHandlerState` | 0 or 1 |
| Trampoline closures | 0 to N (one per wired WinUI event) |
| User callback closures | 0 to N (caller-allocated) |
| **Total framework allocations per element** | **2–4** |

After §7 + §9:

| Object | Count per element |
|---|---|
| Element record | 1 |
| `ReactorState` | 0 or 1 (skipped when statically known unnecessary) |
| Per-control event table | 0 or 1 (only when control has events; even then, can be a struct field on `ReactorState` rather than a separate object) |
| **Total framework allocations per element** | **1–2** |

Halving the allocation count is meaningful for the high-frequency-list workloads spec 034 was motivated by (FlexColumn over 10k items in StressPerf).

### 11.5 Lower bound — how thin can a Reactor element get?

For a `RectangleElement` with one Brush prop, no callbacks, no modifiers, the absolute minimum allocation is:

- The element record itself — fundamental, can't be eliminated.
- Some pointer-equivalent linkage to the WinUI control — can it be eliminated?

If the element record is allocated on the heap (current model) and the WinUI control is allocated on the heap (mandatory), the floor is **one element-record allocation per element**. A bucketed-base `RectangleElement` with `Fill` + `Width` + `Height` is:

```
header (16) + Key (8) + Extensions (8, null) + Modifiers (8, null)
            + Fill (8) + Width (double 8) + Height (double 8) + Setters (8, empty array singleton)
            = ~72 bytes
```

So the answer to "**how thin can a Reactor element get?**" is roughly **70–90 bytes** for a simple leaf with no events. The WinUI Rectangle itself is hundreds of bytes (`UIElement` is heavy), so the Reactor delta is a small fraction of the total memory footprint.

This is the design target: the per-element overhead for a leaf with no callbacks should be in the ballpark of "one small record + nothing else." Today we're at ~250 bytes for that case (3× the floor); the simplifications in §7 and §9 close most of the gap.

### 11.6 Targets to commit to

> **Phase 0 update:** the byte columns below were estimates at draft time.
> They are now anchored to measured values from M1–M3 in the Phase 0
> baseline run on LAPTOP-4MEP83VI (see
> [`047/baseline-results/summary.md`](047/baseline-results/summary.md)).
> The targets use the Phase 0 deliverable 4 formula:
> `Target = min(Direct + 100, ReactorToday × 0.4)`.

If we adopt §7 + §9 + bucketed `Element` base, concrete targets for the design:

| Case | Bytes today (measured M1–M3) | Direct (measured) | Target (Phase 1 V2) |
|---|---:|---:|---:|
| Leaf, no callbacks (TextBlock) | **1018** [M1, mean of 5 reps] | **754** [M1] | **≤ 407** (= Today × 0.4; tighter than Direct + 100 = 854) |
| Leaf, one callback (ToggleSwitch) | **~3800** [M2, mean of 5 reps; high variance] | **~2660** [M2] | **≤ 1520** (= Today × 0.4; tighter than Direct + 100 = 2760) |
| Leaf, three callbacks (Button + 2 pointer modifiers) | **~48000** [M3, mean of 5 reps] | **~29000** [M3] | **≤ 19200** (= Today × 0.4; tighter than Direct + 100 = 29100) |

> Footnote — pre-Phase-0 estimates: TextBlock ~248 B, ToggleSwitch ~800 B,
> Button ~1200 B; targets were ≤100 / ≤320 / ≤500. The estimates were
> dramatically low because they counted only the Reactor element record's
> own field bytes, missing the inflated GC-pressure cost of
> `EventHandlerState` allocation under the actual mount/unmount loop. The
> measurement uses `GC.GetAllocatedBytesForCurrentThread` over a real
> mount + unmount cycle in a WinUI hosted process, so it captures the
> trampoline closure, the per-element `ReactorState` allocation, and the
> coordinator state too.

These are aggressive but tractable. The §11.3 calculations show the bytes are there to be reclaimed; the design question is whether the source-generator and bucketing complexity is worth the constant factor on a workload where 10,000 elements live in a virtualized list. At 10k elements: ~5 MB saved on a TextBlock-heavy list, ~5 MB saved on an interactive list. That's GC-noticeable.

### 11.7 Where this measurement lands in the design

The byte counts make two things concrete that the earlier prose only hinted at:

1. **The shared `EventHandlerState` is the single biggest target.** It's the difference between "every element with a callback pays for every event Reactor knows about" and "every element pays for what it uses." Source-generated per-control tables (§9) capture most of the win on their own — even without descriptors or source-gen for the rest of the protocol.

2. **The `Element` base record is the second-biggest target.** Sixteen cross-cutting nullable fields × 8 bytes = 128 bytes paid by every element whether it uses them or not. Bucketing them into a single nullable `ElementExtensions` sub-record (mirroring `ElementModifiers`) is a mechanical, low-risk change that produces meaningful savings on its own. **This change is independent of the rest of the proposal** and could ship as a precursor — spec 034 already established the bucketing pattern.

Both are worth landing regardless of which form (descriptor, source-gen, handler protocol) the rest of the extensibility design takes.

---

## §12 Runtime perf — dispatch, code size, cache, JIT

§11 quantified the memory wins. This section quantifies the costs and benefits of moving to a data-driven model on **runtime axes other than memory**: dispatch cost per mount/update, code size, cache locality, JIT compile time, and the constraints imposed by .NET 9 PGO.

> **Phase 0 update.** The ns figures in §12.1 / §12.2 / §12.4 / §12.10 were
> estimates at draft time. They are now anchored to the M4 / M5 / M7 / M9
> measurements committed under
> [`047/baseline-results/summary.md`](047/baseline-results/summary.md).
> See the per-section footnotes; the original estimates are preserved so
> the reasoning is not lost.

Numbers are estimated on .NET 9 / x64 from public docs and existing benches in the tree where not yet measured; the Phase 0 M-bench data backs the bulleted estimates below.

### 12.1 Today's dispatch — what does the switch actually compile to?

The current dispatcher (`Reconciler.Mount.cs:68-160+`) is a type-pattern switch with ~70 arms:

```csharp
control = element switch {
    TextBlockElement text => MountText(text),
    ButtonElement btn     => MountButton(btn, requestRerender),
    // ... 68 more ...
};
```

Roslyn lowers this to a sequence of `isinst` checks against each pattern's runtime type, falling through to a default. With ~70 arms it is **not** compiled to a jump table — jump tables only apply to integral switches. The JIT sees a linear chain of type checks.

| Property | Value |
|---|---|
| Worst-case checks per dispatch | ~70 |
| Average checks per dispatch (uniform distribution) | ~35 |
| Average checks per dispatch (realistic — TextBlock + Button + Stack + Border + Grid + TextBox ≈ 60% of mounts) | ~3–6 if PGO has ordered hot types first; ~35 if it hasn't |
| Cost per `isinst` check | ~1–3 cycles (cache-hot type handle compare) |
| Realistic dispatch cost | **~5–30 ns** depending on arm position |

.NET 9's PGO can reorder hot arms first if the dispatcher is identified as a hot method during tier-0 execution. It typically is — every reconcile pass funnels through it.

But the switch has one structural weakness: **it scales linearly in number of registered types**. Today's ~70 is fine; if Reactor's control catalog doubles, average dispatch cost roughly doubles. A dictionary is constant-time regardless.

### 12.2 Dictionary dispatch — the v1 protocol's cost

The `_typeRegistry.TryGetValue(element.GetType(), out var reg)` path (`Reconciler.cs:528`) is already in the dispatcher today (the check that gives registered types priority over built-ins). It's just only checked first; built-ins still go through the switch.

| Step | Cost |
|---|---|
| `Element.GetType()` | ~1 ns — the type handle is a method-table pointer, already in a register from any virtual call |
| `Type.GetHashCode()` | ~5 ns — `Type`'s hashcode comes from the EEClass pointer, no string hashing |
| Bucket walk in `Dictionary<Type, ITypeRegistration>` | ~10–15 ns — one or two cache lines, plus a reference equality compare on `Type` (Type instances are interned per runtime type, so reference equality is correct and fast) |
| Indirect call through `ITypeRegistration` interface | ~5–10 ns — vtable indirection plus the JIT typically cannot inline; .NET 9 monomorphic-call-site devirtualization helps only when PGO marks the site as such (it won't here — the call site is genuinely polymorphic across element types) |
| **Total dispatch** | **~25–40 ns** |

So dictionary + interface dispatch is slightly *slower* than a PGO-warmed switch (5–30 ns) on the average element, slightly *faster* on the worst case. The constant-time guarantee is the win, not the absolute number.

### 12.3 Direct call vs. interface call — the inlining question

Today's `MountButton(btn, requestRerender)` is a direct call to a private instance method on the `Reconciler` partial. The JIT *could* inline it; in practice it doesn't, because `MountButton`'s IL body is well over the ~32-byte inline threshold. The same is true for every `MountXxx`. So the "direct vs. virtual" distinction is mostly theoretical — neither version is inlined in practice.

What changes:

| Model | Call site | Cost | Inlinable in practice? |
|---|---|---|---|
| Current — direct instance call | `this.MountButton(btn, rr)` | 1 ns + call body | No (body too large) |
| v1 — interface call | `handler.Mount(ctx, btn)` | 5–10 ns indirection + call body | No (interface call + body too large) |
| Source-gen — generated direct static | `ButtonHandler.Mount(ctx, btn)` | 1 ns + call body | No (body too large) |

The interface variant is ~5–10 ns slower per dispatch than either direct variant. On a mount of 100 elements, that's 500 ns–1 µs added — well below the cost of allocating 100 WinUI controls (tens of µs). **Dispatch is not the perf story.**

### 12.4 Source-generated dispatch — the best-case shape

A source generator can emit a *generated* switch (or hash table) in a known assembly, calling generated static methods directly. The dispatcher becomes:

```csharp
// Generated by the source generator at compile time
internal static UIElement? Dispatch(Element el, MountContext ctx) => el switch {
    TextBlockElement t => TextBlockHandler.Mount(ctx, t),
    ButtonElement b    => ButtonHandler.Mount(ctx, b),
    // ... generated, one per registered control ...
    _ => DynamicRegistry.Mount(el, ctx),  // fallback to runtime registry for late-bound external controls
};
```

Properties:
- Built-in controls dispatch through the same `isinst` chain as today — no regression.
- External controls registered at runtime dispatch through the fallback `DynamicRegistry` (a `Dictionary<Type, IElementHandler>`), paying the ~30 ns dictionary cost for those types specifically.
- The handler methods are static, in known assemblies, so the JIT *can* devirtualize / direct-call them. Inlining still doesn't happen for the same reason (body size), but the call sequence is one instruction shorter than the interface path.

**Source-gen dispatch is the only model where dispatch overhead is unconditionally ≤ today's.** Every other approach pays a small constant for the extensibility.

### 12.5 Code size — what does adding 70 handler classes cost?

Current Reactor.dll sizes (measured 2026-05-24):

| Build | Size |
|---|---|
| ARM64 Release | 2,691,584 bytes (~2.57 MB) |
| ARM64 Debug | 3,414,016 bytes (~3.25 MB) |

The relevant files:

| File | Lines | Approximate contribution to DLL |
|---|---|---|
| `Reconciler.Mount.cs` | 3,944 | ~250 KB (rough — IL is ~30% of source lines for this dense style) |
| `Reconciler.Update.cs` | 4,370 | ~290 KB |
| `Reconciler.cs` | 3,825 | ~250 KB |
| `Element.cs` | 3,757 | ~100 KB (record types are mostly metadata + small generated methods) |

Now estimate the delta for the v1 handler model. For each of ~70 controls, we'd add a handler class:

| Per-handler addition | Bytes |
|---|---|
| Type metadata (TypeDef, MethodTable, interface map entries for `IElementHandler<T,U>`) | ~400 |
| Generic instantiation overhead per `IElementHandler<TElement, TControl>` pair (MethodTable, vtable slots) | ~150 |
| Mount/Update/Unmount/ReconcileChildren method IL (roughly same as existing `MountXxx`/`UpdateXxx` bodies — the work doesn't change) | ~same as today |
| Static-init / interface-impl glue | ~50–100 |
| **Net delta per handler** | **~600–700 bytes of metadata** beyond what exists today |

70 handlers × ~650 bytes = **~45 KB** added to Reactor.dll. That's ~1.7% of the Release DLL — measurable but trivial.

Source-gen variants change this:

| Approach | DLL size delta |
|---|---|
| v1 handler classes (one per control) | +45 KB |
| Descriptor objects (one static descriptor per control, no per-control class) | +20–25 KB (just the descriptor data) |
| Source-generated static handlers (one static class per control, no interface) | +30 KB (no interface metadata, no generic instantiations) |
| Source-generated static methods in one shared class | **+0–5 KB** (no per-control type metadata at all) |

The cheapest source-gen shape is essentially DLL-size-neutral.

### 12.6 JIT compile time — startup cost

A subtle factor: 70 small methods JIT slightly differently than two big ones.

Current state: `Reconciler.Mount.cs` and `Reconciler.Update.cs` contain large generated method bodies for the `Mount()` and `Update()` switches. The JIT, on first call, compiles the entire body of `Mount()` — which is the giant switch plus every `MountXxx` it calls. Tiered compilation will start at tier-0 (minimal optimization, faster JIT) and tier up to tier-1 once the method is hot.

In the handler model, `Mount()` is tiny — just the dispatch — and JITs almost instantly. Each `MountXxx` (or `<Control>Handler.Mount`) JITs on first use. So **first-frame startup is faster** with handlers because cold paths aren't JITted; but cumulative JIT cost across a session is *higher* because each handler enters tier-0 → tier-1 separately.

For a typical app: first frame mounts maybe 30–50 element types. Handler model JITs ~50 small methods instead of one big one. Tier-0 is fast (~1 ms per method). Total JIT delta: probably **~10–30 ms saved at startup**, depending on how cold the call site is. This is a real but small win.

For LiveReload / hot-reload scenarios where the dispatcher gets rebuilt: the handler model's small methods reJIT independently. Today's giant switch reJITs as a unit. Handler model is friendlier here.

### 12.7 Instruction cache — does dispatching to many handlers thrash?

A typical Reactor reconcile pass on a 100-item virtualized list of, say, mixed `Border + TextBlock + Button` per row:

- Today: all mount/update code lives in one method body in `Reconciler.Mount.cs`. The switch jumps between arms; each arm calls a small `MountXxx` further into the same compilation unit. The hot working set is a few KB of icache, all contiguous.
- Handler model: each control's mount lives in a different method address. `BorderHandler.Mount` → `TextBlockHandler.Mount` → `ButtonHandler.Mount` → repeat. Three separate methods cycling.

Modern CPUs have 32 KB+ L1 icache; three methods at ~1–2 KB each fit comfortably. **No measurable cache thrash expected** at typical control diversity per pass. The case where this *could* show up: a virtualized list where row template uses 6+ distinct controls and we render 1000 rows — 6 methods × 2 KB = 12 KB hot working set, still under L1. We'd have to engineer a pathological case to see this on a microbench.

Conclusion: cache effects are neutral. Slightly better data locality (each handler's call frame is smaller than the giant switch's), slightly worse code locality (more distinct hot methods). Probably a wash.

### 12.8 PGO and dynamic devirtualization

.NET 9's tier-1 JIT with PGO has a specific optimization for monomorphic interface call sites: if PGO data shows that 99% of calls through an interface go to one concrete type, the JIT inlines a type-check guarded direct call. This is *powerful* for handler dispatch — but only when the call site is monomorphic.

In Reactor's dispatcher, the handler call site is **polymorphic by construction** — every distinct element type goes through it. PGO can't devirtualize. The interface call stays a vtable indirect on every dispatch.

This is the strongest argument against the v1 interface model: PGO doesn't help us where it would help most. Source-gen sidesteps the problem by not using an interface at all.

### 12.9 Generic instantiation

`IElementHandler<TElement, TControl>` instantiates once per (element-type, control-type) pair — 70 pairs.

- All instantiations have reference-type generics, so they share a single canonical code body for shared portions of the interface. No code-size blowup from instantiation per se.
- Each instantiation gets a distinct MethodTable (~100–200 bytes) so the runtime can dispatch correctly. 70 × 150 = ~10 KB metadata.
- First call to each instantiation triggers a lazy generic dictionary lookup. This is fast (single-digit ns) and cached.

This is fully accounted for in the ~45 KB delta from §12.5.

### 12.10 Dispatch cost as a fraction of mount cost

The dispatch is one part of a much larger mount operation. The actual cost breakdown for mounting a single `ButtonElement`:

| Step | Approximate cost |
|---|---|
| Dispatch (switch arm or dictionary lookup) | 5–30 ns |
| WinUI `new Button()` (or pool rent) | 500–2,000 ns (XAML control init is heavy) |
| Property writes (Label, IsEnabled, …) | 100–500 ns total |
| Setter array iteration | ~50 ns per setter |
| `ApplyModifiers` (default modifier pipeline) | 200–1,000 ns |
| Tag binding / event trampoline attach | 50–200 ns |
| Add to parent's `Children` collection | 100–500 ns |
| **Total** | **~1,000–4,000 ns** |

**Dispatch is ~1% of total mount cost.** Even a 3× dispatch slowdown (from PGO-warm switch at ~10 ns to dictionary+interface at ~30 ns) moves total mount cost by less than 1%. Below noise on any realistic bench.

This reinforces the §11 conclusion: **the memory wins matter; dispatch mechanism doesn't.** Pick the model that's the right architectural shape; the perf falls out either way.

### 12.11 Summary scorecard

| Axis | Current (switch) | v1 (dictionary + interface) | Source-gen (direct static) |
|---|---|---|---|
| Dispatch cost (avg PGO-warm) | 5–30 ns | 25–40 ns | 5–30 ns |
| Dispatch cost (avg cold) | 30–50 ns | 25–40 ns | 30–50 ns |
| Constant-time dispatch | ❌ scales linearly | ✅ | ❌ but PGO-warm hot path is essentially constant |
| External-type dispatch cost | 25–40 ns (dictionary first) | 25–40 ns | 25–40 ns (fallback path) |
| DLL size delta | 0 | +45 KB (1.7%) | 0–30 KB (depending on shape) |
| JIT startup (first frame) | baseline | -10 to -30 ms | -10 to -30 ms |
| PGO devirtualization | N/A | ❌ polymorphic site | ✅ all calls direct |
| icache footprint | dense, ~one method | ~70 small methods, fits in L1 | ~70 small methods, fits in L1 |
| Mount cost contribution | ~1% | ~1.5% | ~1% |
| **Net runtime perf** | **baseline** | **~0.5% slower (noise)** | **same or slightly faster** |

### 12.12 Implications for the design decision

Three observations fall out:

1. **Dispatch-mechanism perf is in the noise.** Memory wins from §11 dominate the perf story by orders of magnitude. The choice between dictionary-and-interface vs. source-gen is *not* a perf decision; it's an architecture and ergonomics decision.

2. **Source-gen avoids the only real perf concern** — the polymorphic interface call site that PGO can't help. If we're going to source-gen anyway for the memory wins (per-control event tables, eliminated `ReactorState` where possible), the dispatch comes along for free.

3. **Code-size delta is trivial.** Even the worst case (v1 with full per-control handler classes) is +1.7% on a Release DLL. Not a constraint on design.

Concrete recommendation: pick the model based on §6/§7's authoring ergonomics (descriptor cleanness, compile-time validation, AOT trim-friendliness). The runtime perf will sort itself out as long as we keep one rule: **the hot dispatch path must be a direct call or PGO-friendly switch, not a polymorphic interface invocation.** Both the source-gen and bare-switch models meet that rule; v1's interface dispatch doesn't, but the cost is small enough that it's an acceptable interim step during the spike phase.

### 12.13 Risks worth measuring during the spike

Things that could surprise us — worth a microbench in Phase 1 before committing:

- Whether PGO actually orders the switch arms by hot-frequency. Pre-PGO, the arms are in source order, which is not by hot-frequency. The first reconciles in a session pay the cold cost; if startup-time matters, source ordering matters.
- Whether `Type.GetHashCode()` is actually as fast as advertised on Mono / Native AOT. On CoreCLR / RyuJIT it's near-free; other configurations may differ.
- Whether the v1 interface call site stays polymorphic forever, or PGO eventually picks up that "this specific reconciler instance only ever processes a small set of types." Probably stays polymorphic in practice — the type set is small but uniform across calls.
- Whether tier-1 JIT actually inlines small handler methods despite the interface call. On hot monomorphic-by-PGO sites, yes. On Reactor's dispatcher, no — but worth confirming with a profiler.

---

## §13 Future design-session questions

Phase 0 ratified decision criteria for the data-driven questions in [`decision-criteria.md`](047/decision-criteria.md); each question below carries a **Status** line indicating whether the decision has landed in the spec body, is gated on a later phase's measurement, or remains an open design call for a future session.

1. **Descriptor vs. hand-coded handler — which one ships?** **Status: Resolved (Phase 2, 2026-05-26) — descriptors primary, hand-coded `IElementHandler<,>` as escape hatch.** Source-gen (§7) remains deferred.

   **Verdict.** The pre-committed decision matrix
   ([`decision-criteria.md#q1`](047/decision-criteria.md#q1)) lands the
   Phase 2 measurement in the **5-15% judgment-call band**, with the worst
   gating bench (M2) at +9.6%. The matrix's qualitative inputs (LOC,
   readability) come down on descriptors at Phase 3 scope. Authors default
   to `ControlDescriptor<TElement, TControl>`; hand-coded
   `IElementHandler<TElement, TControl>` stays as the escape hatch for
   irregular controls, perf-critical mount paths, and multi-event composition
   shapes that the descriptor interpreter's single-payload-per-control
   storage doesn't natively cover (see §6.1's `.HandCodedControlled` /
   `.HandCodedEvent` classifications).

   **Capture lineage** (all on LAPTOP-4MEP83VI, ARM64-native, Release,
   .NET 10.0.8, 15 measurements per cell):

   | Capture | Descriptor event path | M1 | M2 | M5 | M7 | M10 (informative) | Matrix verdict at the time |
   |---|---|---:|---:|---:|---:|---:|---|
   | `2026-05-26-q1-spike-5x5/` | Public `OnCustomEvent` | +1.3% | +19.1% | +13.4% | -8.2% | +31.5% | Ship hand-coded |
   | `2026-05-26-q1-fastpath-3x5/` | Internal typed-payload fast path; capture noisy | +23.5% | +18.8% | +16.7% | -6.1% | +32.1% | Ship hand-coded (suspect — M1 anomaly) |
   | **`2026-05-26-q1-fastpath-3x5-stableac/`** | **Internal typed-payload fast path; stable AC, clean foreground** | **-1.0%** | **+9.6%** | **-2.3%** | **+8.1%** | **+19.3%** | **Judgment call → descriptors** |

   Numbers are descriptor-vs-`ReactorV2`-handler deltas. Full raw captures
   live under `docs/specs/047/phase2-results/LAPTOP-4MEP83VI/`. The stable-AC
   capture is the authoritative one — the prior two were degraded by
   capture-condition noise (the M1 +23.5% on a TextBlock that doesn't engage
   the descriptor path at all was the giveaway).

   **What the fast-path rewrite bought.** Phase 2's first capture used the
   descriptor model's public-surface event wiring (`OnCustomEvent`, which
   allocates a closure per first-mount and stores trampolines in a
   non-deduped list). The descriptor sources live inside `src/Reactor/`
   and have the same `internal` access the hand-coded handlers do, so the
   path was rewritten to use `GetOrCreateControlEventPayload<T>` with a
   static trampoline — mirroring `ToggleSwitchHandler.EnsureToggledWiring`.
   The rewrite cut roughly half of the M2 / M10 cost (M2: −9.5pp, M10: −12.2pp).
   The residual +9.6% on M2 is **intrinsic interpreter overhead** — virtual
   `PropEntry<,>.Mount` dispatch plus delegate getter/setter invocations
   versus the hand-coded handler's inlined property writes. Removable only
   via source-gen (§7).

   **LOC + readability inputs to the judgment call.** For the three Phase 2
   controls (LOC excluding interpreter):

   | Shape | LOC per control (avg) | Shared interpreter | Break-even N |
   |---|---:|---:|---:|
   | Hand-coded `IElementHandler<,>` | ~100 | 0 | — |
   | Descriptor + interpreter | ~66 | 586 (one-time) | ~17 controls |

   Phase 3 ports ~60 controls. At full scope descriptors save ~24% total LOC.
   Readability: the §6.1 prop classifications (`Initial` / `OneWay` /
   `Controlled` / `OneWayConditional` / `CoercingOneWay`) are visible at the
   call site, type-system-enforced, and read like spec tables. The descriptor
   shape is dramatically easier for external authors to ship correctly (they
   don't need to understand trampoline-storage internals to wire an event).

   **Decision matrix as applied** (copied verbatim from the original entry
   for the record; this is the bar Phase 2 cleared):
   - **Descriptor within 5% of handler on M1/M2/M5/M7:** ship descriptors as primary. (M1 / M5 met this; M2 / M7 fell into the next band.)
   - **Descriptor 5–15% slower:** judgment call, weigh LOC + readability against per-mount cost in context of L4/L7/L9 macros. (M2 / M7 landed here; LOC + readability won.)
   - **Descriptor >15% slower on any of M1/M2/M5/M7:** ship hand-coded handlers as primary. (Not triggered on the authoritative stable-AC capture.)

   **Carry-forward for Phase 3** — known defects that intersect Q1:
   - **KD-3** (Phase 1) — dispatch fast-path for the ported built-ins (M4 +88.9% V1 vs Today). Intersects with whichever Q1 shape wins; carries into Phase 3.
   - **KD-4** (Phase 1) — public typed-event surface for external descriptor authors. Scope **narrowed** by Phase 2: in-tree descriptors already use the internal fast path; KD-4 is now external-author-only.
   - **Multi-event composition** — Phase 2 measured only single-event controls (ToggleSwitch / Slider). Multi-event controls (TextBox / Image / proposed ListView) need the §6.1 `.HandCodedControlled` + `.HandCodedEvent` builders, which reuse the §9.2 per-control-class payload types. Phase 3 prerequisite.

   **Reopen condition.** Re-run Q1 only if source-gen (§7) lands, in which
   case the +9.6% residual is expected to collapse to noise. KD-4 is no
   longer expected to flip Q1 because the in-tree fast path already exists.

   **What is NOT in scope for re-opening.** The Phase 2 noisy captures
   are not grounds to reopen the verdict — they're documented as
   capture-condition artifacts. Future readers reaching for those numbers
   should use the stable-AC capture as the authoritative reference.
2. **What's the AOT story end-to-end?** **Status: Resolved (Phase 0).** Reactor is AOT-compatible today: the AOT test suite runs at ≥ 90% pass rate against the AOT-compiled bits. The full assembly is not yet marked `IsAotCompatible=true` because a small number of features remain unsafe; those land separately. **Commitment for spec 047: no new AOT warnings introduced by the v1 protocol surface, regardless of which Q1 shape wins.** Descriptor lambdas are strongly-typed (no `nameof()`-resolved reflection) precisely so the interpreter remains AOT-clean; hand-coded handlers are AOT-clean by construction. L14 (AOT publish of the split-library scenario) ships as a **regression guard** in Phase 1's exit gate — not an exploratory check. See [`decision-criteria.md#q2`](047/decision-criteria.md#q2).
3. **Can echo suppression be eliminated, and at what cost?** **Status: Resolved (Phase 0).** Ship "delete + tight diff" for the 14 trivial sites, per-control tolerance metadata for 8 coercion / float-precision sites, and a one-off ColorPicker shim. §8.1 (`mostRecentEventCount`) rejected — only 1 / 24 sites required it. See [`decision-criteria.md#q3`](047/decision-criteria.md#q3) and §8.
4. **What's the `ReconcileChildren` shape?** **Status: Resolved (Phase 0).** Concrete C# strategy types ship in Phase 1: `None` / `SingleContent` / `Panel` / `NamedSlots` / `ItemsHost` / `Imperative`, plus `AttachedPropWriter` on container descriptors. See §6's ChildrenStrategy block.
5. **Is `RegisterType` even the right verb?** **Status: Resolved (Phase 0).** Keep `RegisterType`. After the split-library plan, first-party and external registrations travel through the same surface — a single verb reflects that. Phase 1 promotes it to public with Q17's throw-on-duplicate rules. Renames / split verbs were considered and rejected (source-compat + the engine treats both paths identically).
6. **Should setters re-run on every update or only when the setters array changed?** **Status: Resolved (Phase 0) — Phase 1 M7 measurement pending baseline-machine run** (see [`phase1-results/1.19-final-perf-validation-deferral.md`](047/phase1-results/1.19-final-perf-validation-deferral.md)). Default to skip-on-ref-equality (`oldEl.Setters == newEl.Setters` → no-op); back-compat opt-out via a `SetterRunPolicy.Always` flag on the element record if a real consumer trips on it. The ported handlers in `src/Reactor/Core/V1Protocol/Handlers/` call `ctx.ApplySetters(n.Setters, ctrl)` on every Update — the ref-equality short-circuit lives inside `Reconciler.ApplySetters`. See [`decision-criteria.md#q6`](047/decision-criteria.md#q6).
7. **Pool integration with descriptors.** **Status: Resolved (Phase 0) — Phase 1 M12 measurement pending baseline-machine run** (see [`phase1-results/1.19-final-perf-validation-deferral.md`](047/phase1-results/1.19-final-perf-validation-deferral.md)). Phase 1 shipped `ctx.RentControl<T>(policy, factory)` as the documented mount path (Q18 contract); the legacy direct-`new` path is still permitted in legacy `MountXxx` arms during the Phase 1 / Phase 3 migration. M12 gates the perf claim once the baseline-machine run lands. See [`decision-criteria.md#q7`](047/decision-criteria.md#q7) and Q18.
8. **`Set(...)` modifier semantics — and a latent correctness hole.** **Status: Resolved (carve-out landed ahead of Phase 1).** `ApplySetters` now runs inside a scope-based suppression scope on the control's `ReactorState`; M13 baseline flipped from `OnIsOnChangedFireCount = 1` to `0`. See §8.2 and [`factoring-recommendation.md`](047/factoring-recommendation.md). An explicit `Set.Raw(...)` opt-out remains a future refinement if needed.
9. **Override semantics.** **Status: Resolved (Phase 0).** No override mechanism in v1 — duplicate registration throws. Test fakes compose a `Reconciler` from scratch with the registry they need; `RegisterOverride` can be added later as a non-breaking, additive verb if a real consumer scenario surfaces. See [`decision-criteria.md#q9`](047/decision-criteria.md#q9) and §2.1.
10. **Compile-time validation.** **Status: Resolved (Phase 0).** Compile-time validation of property and event references is **required**. The C# compiler handles it for free where the protocol uses strongly-typed delegates (hand-coded handler bodies, descriptor `get` / `set` / `subscribe` / `unsubscribe` lambdas, and `nameof(Type.Member)` references). For any portion of the protocol surface that would otherwise reduce to a string-form name lookup (e.g. raw `changeEvent: "Toggled"` strings), Phase 1 ships a Roslyn analyzer that flags the mismatch as a compile error. A descriptor with a typo or wrong type is never a runtime failure. See [`decision-criteria.md#q10`](047/decision-criteria.md#q10).
12. **`Update` return type and substitution semantics.** **Status: Resolved (Phase 0).** `void Update(...)` — substitution forbidden in v1. Type changes flow through unmount-and-remount. Matches React Native Fabric's `updateProps(oldProps, newProps) → void`. Widening to `UIElement? Update(...)` later is non-breaking if a real need surfaces. See §4 and [`decision-criteria.md#q12`](047/decision-criteria.md#q12).
13. **Modifier × declarative-prop precedence.** **Status: Resolved (Phase 0).** Modifier-after-prop default (status quo, back-compat). Element-record props act as defaults; modifiers override. Per-field opt-in (`Prop.OneWay(..., precedence: Precedence.PropWins)`) stays as a future non-breaking extension. See §6.2.
14. **Concurrency model — which thread can `Mount` / `Update` run on?** **Status: Resolved (Phase 0).** UI-thread-only. The protocol documents this guarantee on the `MountContext` surface in Phase 1; handlers may freely access control-state without synchronization. No `ThreadAffinity` flag in v1; off-thread mount is non-breaking to add later. See [`decision-criteria.md#q14`](047/decision-criteria.md#q14).
15. **Hot-reload behavior across descriptor / source-gen approaches.** **Status: Resolved (Phase 0).** Component-definition changes may require a process restart; that is acceptable hot-reload behavior for Reactor. Therefore hot-reload smoothness is **not** an input to Q1's decision matrix — neither shape (descriptor / handler / future source-gen) gets a tiebreaker for "easier hot-reload." L12 still ships as observability in Phase 2 to document actual behavior, but it does not gate any phase or shift Q1's outcome. See [`decision-criteria.md#q15`](047/decision-criteria.md#q15).
16. **External controls are permanently descriptor-bound.** **Status: Documented in §16 / Appendix A.** A third-party assembly that ships an element record cannot, in general, have the Reactor source generator run on it without the author opting in (`[ReactorControl]` annotation + generator package reference). For cooperating third parties, source-gen works; for non-cooperating cases (a control someone wraps at runtime via `RegisterType`-equivalent), the descriptor / runtime-handler path is permanent. This is not a transitional artifact — it is designed as a permanent surface from day one.
17. **Registry precedence and subtype behavior.** **Status: Resolved (Phase 0).** Exact-runtime-type lookup only; no assignable / base matches. Duplicate registration (including against a built-in element type) throws at registration time. No open generic registrations in v1. No `RegisterOverride` verb in v1 — additive later if needed. See §2.1 and [`decision-criteria.md#q17`](047/decision-criteria.md#q17).
18. **Pool policy as a public API, not just `ctx.AllocateControl`.** **Status: Resolved (Phase 0); concrete API design lands in Phase 1.** The contract:
    - **Poolability flag.** Descriptors / handlers declare `IsPoolable` explicitly. Controls with persistent native resources or non-resettable state opt out.
    - **Pool key.** `typeof(TControl)` only for v1. Finer keys (e.g., `(typeof(TextBlock), styleKey)`) revisited later.
    - **Reset contract.** On return: clear `ControlEventState` (§9.2), pending event subscriptions, `ModifierEventHandlerState`, attached-DP `Tag`, `DataContext` if Reactor sets one. Anything not in this list is a reuse hazard.
    - **What survives.** Layout caches, template state, `ListView` realized-container reuse. Enumerated separately.
    - **Dual-RCW.** Pool return is idempotent and does not double-clear (matches `ReactorAttached.StateProperty` discipline).
    - **Diagnostic.** A non-resettable property found dirty on rent emits a structured log entry.

    See [`decision-criteria.md#q18`](047/decision-criteria.md#q18). M12 plus a correctness scenario validates the contract in Phase 1.
19. **Keep `WriteSuppressed` as a public primitive day one, decoupled from suppressor *elimination*.** **Status: Resolved (Phase 0).** Phase 1 ships `ReactorBinding<T>.WriteSuppressed(...)` as a public method backed by today's `ChangeEchoSuppressor.BeginSuppress`. Phase 4's swap to "delete + tight diff + per-control tolerance" (per Q3) changes the body, not the signature. See [`decision-criteria.md#q19`](047/decision-criteria.md#q19).

## §14 Suggested phasing

This proposal needs to clear a *spec-process bar* before any implementation phase starts, then proceeds through hand-coded handler implementation phases. Source-generation is explicitly deferred (§7 status) and is not part of the implementation track.

### Phase 0 — Spec-process deliverables (pre-greenlight)

The audit work and the perf validation suite are *part of writing this spec*, not the first implementation phase. The proposal cannot be greenlit without these in hand because the §11/§12 numbers and the §13 open questions cannot otherwise be answered.

**Deliverables required before Phase 1 can start:**

1. **`BeginSuppress` audit.** Inventory every call site. Mark each as "eliminable via tighter diff," "genuine coercion (needs explicit handling)," "float-precision artifact," or "user-state-races-render (needs §8.1 round-trip)." Output: a CSV of `control × property × category × why`. Drives the §8 / §8.1 decision and the controlled/uncontrolled descriptor entries in §6.1.

2. **`EventHandlerState` field audit.** Walk every `Ensure*Subscribed` and `Current*` field. Mark each as "any element via modifier (stays in `ModifierEventHandlerState`)" or "this control only (moves to per-control table)." Output: a per-event row showing today's slot and §9's destination. Drives the §9.2 per-control struct shapes.

3. **Perf validation suite — infrastructure.** Build out the §15 suite scaffolding *before* any V2 implementation exists:
   - Add `StressPerf.ReactorV2` project skeleton (initially a copy of `StressPerf.Reactor`, so V2 numbers ≈ Today numbers at the start of Phase 1; this is intentional — Phase 1 work will show up as the delta).
   - Add `BlankReactorV2` to `startup_perf/`.
   - Add the M1-M13 microbench harnesses to `PerfBench`, with implementations matching `Direct` and `ReactorToday` first.
   - Implement the §15.4 macro scenarios L1-L11 (L12 hot-reload waits for Phase 2). Each runs against `Direct` and `ReactorToday`.
   - Build the reporting aggregator (§15.6 — JSON-Lines collector + comparison emitter).
   - Document the environment isolation runbook per §15.5 (foregrounded windows, AC power, fixed refresh, etc.).

4. **Baseline numbers.** Run the suite and capture the **`Direct` vs `ReactorToday` results** on representative hardware (at minimum: one x64 workstation, one ARM64 Surface-class). These numbers go into §11.1 (replacing today's estimates with measured allocations) and §12 (replacing today's estimated ns with measured ns). The gap between `Direct` and `ReactorToday` is the *budget* this spec proposes to close.

   Specifically, the §11.6 target table:

   | Case | Bytes today | Target |
   |---|---|---|
   | Leaf, no callbacks | ~248 (estimate) | ≤ 100 |

   …gets rewritten as:

   | Case | Bytes today (measured M1) | Direct (measured) | Target |
   |---|---|---|---|
   | Leaf, no callbacks | (Phase-0 measurement) | (Phase-0 measurement) | min(Direct + 100, ReactorToday × 0.4) |

   …with the explicit budget that V2 should close >60% of the gap between `Direct` and `ReactorToday`.

5. **Existing-API surface inventory.** Confirm Appendix A's mapping is current by walking the `internal` surface today and noting which members today's `RegisterType` callers actually fall back into (e.g., via runtime reflection escape hatches). Outputs a list of "what would break if these were left `internal` vs promoted."

6. **Decision criteria for §13 open questions.** Each open question that the suite can disambiguate gets a written success criterion (e.g., "Q3 §8.1 round-trip ships if it solves all three correctness tests AND adds ≤5% to M2"). Documented before Phase 1 starts, so the decisions in later phases follow the data without relitigation.

7. **Spec factoring decision.** Spec 047 is deliberately comprehensive — it bundles the external-control parity surface, the event-state memory optimization, the echo-suppression evolution, the setter-echo correctness fix, and the deferred source-gen direction into one document because they share invariants and trade-offs. A reviewer recommended splitting this into separable specs (047-core + memory + echo + setters-fix + source-gen). The right time to make that call is *after* Phase 0 produces the audit results and the baseline numbers, because the data will reveal:
    - Whether the echo-suppression evolution is small enough to leave in-line (one §8 section) or large enough to warrant its own spec.
    - Whether the `EventHandlerState` split lands as part of the v1 protocol PRs or as an independent precursor (and similarly for the bucketed-`Element`-base from §11.7).
    - Whether the setter-echo fix (§8.2) is small enough to ship as a standalone fix immediately, ahead of the rest of the work.
    Phase 0 deliverable: a written recommendation on factoring, with a proposed split (or "keep unified") justified by the audit findings. The recommendation is reviewed at the same gate as the rest of Phase 0, and any approved split is executed before Phase 1 starts.

**Phase 0 exit gate:** all seven deliverables complete, baseline numbers in `docs/specs/047/baseline-results/`, §11 and §12 of this spec updated with measured numbers replacing estimates, factoring recommendation reviewed and either ratified ("keep unified") or executed (specs split per the recommendation). Only then does the proposal move to greenlight.

### Phase 1 — v1 protocol behind a feature flag

- Promote `ApplySetters`, `SetElementTag`, `GetElementTag` to public (or to a `RestrictedAccess` namespace if API stability isn't ready to be locked — see the API-stability note in the exit gate below).
- Ship `IElementHandler<TElement, TControl>` + `MountContext` / `UpdateContext` / `ReactorBinding<TElement>` from §4. All hand-coded.
- Ship `WriteSuppressed` as a public primitive (§13 Q19), backed by today's `BeginSuppress` — independent of any §8 cleanup decision.
- Ship the pool-policy API (§13 Q18) with `IsPoolable`, the reset contract, and the `typeof(TControl)`-keyed pool. External authors get a real, documented pool contract from day one.
- Ship `Reactor.Compile.Analyzer` (working name) alongside the v1 protocol package, validating any string-form property / event references in descriptors against the control type (§13 Q10).

**Phase 1 minimum representative control set.** Two controls (ToggleSwitch + one external) isn't enough — it risks freezing an API that only works for leaf cases. The minimum set must exercise every load-bearing path the split-library plan will hit:

| Control | What it exercises | Built-in or external? |
|---|---|---|
| **`ToggleSwitch`** | Value-bearing single-event leaf. Echo handling for `IsOn`. Simplest case. | Built-in (port from `MountToggleSwitch`) |
| **`Slider`** | Value-bearing coercing control. `Value` clamping against `Minimum`/`Maximum`. Exercises whichever §8 echo direction is in play. | Built-in (port from `MountSlider`) |
| **`TextBox`** | Text/focus-heavy control. `Text`, `IsReadOnly`, `PlaceholderText`, `GotFocus`/`LostFocus`. Exercises focus-prop echo (§8 audit) and the `mostRecentEventCount` candidate scenario most directly. | Built-in (port from `MountTextBox`) |
| **`Border`** (or `Grid`) | Single-content / panel container. Exercises `Children.SingleContent` / `Children.Panel`, attached props (`Grid.Row`/`Grid.Column`), modifier-pipeline interaction. | Built-in |
| **`ListView`** (or `ItemsView`/`TemplatedList`) | Templated items host. Exercises spec 042 keyed reconciliation through the public protocol, pool/recycle survivability. | Built-in |
| **`Win2DCanvas`** (or equivalent) | External, downstream-consumer wrapped control. Exercises the registry path end-to-end with `WriteSuppressed`, `ctx.AllocateControl`, `ControlEventStateBox`, modifier pipeline — all from a separate assembly with no `InternalsVisibleTo`. | External (pix project / Reactor.Controls.Win2D) |

Together these six exercise: value-bearing + coerced + text/focus + container + items-host + external — every category that needs to work after the split-library plan. Anything that ships as a Reactor.Controls.* package post-split must fit into one of these shapes; if Phase 1's API can't express all six, the API isn't ready for the rest of Phase 3.

Existing controls keep their private `MountXxx` paths. No big-bang migration. `StressPerf.ReactorV2` switches its scenarios to use the ported controls; everything else still routes through `ReactorToday`.

**Phase 1 exit gate** (all must hold):

1. **Perf:** `ReactorV2` ≤ +10% on M1, M2, M5, M7, L1, L4 (per §15.7). No worse than `ReactorToday` on any macro test in §15.4.
2. **External-assembly proof:** At least one of the six controls is implemented in a *separate assembly* (e.g., `Reactor.Controls.Win2D.dll` or a deliberate test assembly that hosts `Slider` outside `Reactor.dll`), registered via public API, with no `InternalsVisibleTo` on Reactor's internals. Selftests pass for value writes, event callbacks, modifiers, setters, pooling/recycling, and child reconciliation where applicable.
3. **AOT/trim:** That external assembly publishes with `PublishTrimmed=true` and `IsAotCompatible=true` and produces zero new trim/AOT warnings.
4. **Correctness:** Existing test suite passes. M13 (setter suppression scope) passes — i.e., `Set(ts => ts.IsOn = true)` does not fire an unmasked `Toggled` event.
5. **API stability statement:** the Phase 1 surface is explicitly marked *provisional*; the surface lock happens after Phase 2's descriptor-vs-handler decision. Any consumer that takes a dependency on Phase 1 APIs accepts the breaking-change risk through Phase 2. This is documented in `docs/guide/extensibility-preview.md`.

Without (2) specifically, Phase 1 can't claim the split-library path works.

### Phase 2 — descriptor model spike + decision

**Status: Complete (2026-05-26).** §13 Q1 resolved — descriptors as primary first-party surface; hand-coded `IElementHandler<,>` as escape hatch.

- ✅ Built `ControlDescriptor<TElement, TControl>` interpreter using the v1 context surface internally (`src/Reactor/Core/V1Protocol/Descriptor/`).
- ✅ Implemented the three controls (`ToggleSwitch`, `Slider`, `Border`) in both hand-coded-handler and descriptor shapes. Behavior parity verified by 23/23 self-test assertions (`Desc_*` fixtures).
- ✅ Ran the §15.3 micro suite (M1, M2, M5, M7, M10) — three captures progressively de-noised. L4 / L9 macros not required for verdict (matrix gated on micro deltas; LOC + readability resolved the judgment-call band).
- ✅ Applied the §13 Q1 decision matrix to the stable-AC capture. Worst gating bench M2 +9.6%, landed in 5-15% judgment-call band; LOC + readability inputs resolved to descriptors at Phase 3 scope.
- **Phase 2 exit gate met:** descriptors are the primary first-party surface (§6.1). Hand-coded handlers stay as escape hatch (irregular controls, perf-critical mount paths, multi-event composition via §6.1.1 / §9.2.1).

See §13 Q1 for the full capture lineage and matrix application. Raw data under `docs/specs/047/phase2-results/LAPTOP-4MEP83VI/`.

### Phase 3 — controls migration

**Phase 3 prerequisites** (added by Phase 2 verdict — must land before bulk porting starts):

1. **Ship `.HandCodedControlled<TValue,TArgs>` and `.HandCodedEvent<TArgs>` builder methods** on `ControlDescriptor<TElement, TControl, TPayload>` (§6.1.1). Adds `TPayload` overload and two new `PropEntry` subclasses (`HandCodedControlledPropEntry`, `HandCodedEventPropEntry`). ~200 LOC in `src/Reactor/Core/V1Protocol/Descriptor/`.
2. **Port `TextBox` to descriptors as the 2-event proof point** (TextChanged + SelectionChanged). Reuses the existing `TextBoxEventPayload` class from `ControlEventPayloads.cs`. Confirms the §9.2.1 hand-coded-shape + per-descriptor-TPayload composition works end-to-end.
3. **Re-bench M2 / M10 against the TextBox descriptor port** — expect the +9.6% / +19.3% residuals to shrink substantially for hand-coded-shape descriptors (matches the hand-coded handler's per-fire shape exactly). Document in `docs/specs/047/phase3-results/`.
4. **Author guidance written into the Phase 3 onboarding doc** — the §6.1.1 classification table (when to use `.Controlled` vs `.HandCodedControlled` + `.HandCodedEvent` vs `IElementHandler<,>`).

**Phase 3 migration order** (~60 controls total):

- Migrate the value-bearing family first (`Slider`, `NumberBox`, `ColorPicker`, `RatingControl`). Closes out the echo-suppressor audit (§8.x). Single-event controls use `.Controlled<,>`.
- Then input controls (`Button`, `TextBox`, `CheckBox`). Multi-event cases (TextBox) use `.HandCodedControlled` + `.HandCodedEvent` per §6.1.1.
- Then containers (`Stack`, `Grid`, `Flex`). Exercises `ReconcileChildren`. Mostly zero-event.
- Then templated lists. Exercises keyed reconciliation interop with spec 042. Multi-event (selection + item-click).
- Then the long tail (`NavigationView`, dialogs, `MapControl`, …). Mix of event shapes.
- The private `MountXxx` switch shrinks one arm per PR.
- The §15 suite gates every PR — see §15.6 regression budgets.

**Phase 3 progress** (see `docs/specs/tasks/047-extensible-control-model-implementation.md` for the live tracker):

- Phase 3 prerequisites (`.HandCodedControlled` / `.HandCodedEvent` builders, `TextBoxDescriptor` 2-event proof, x64 advisory bench) — **landed** (PR #424).
- Value-bearing batch 1 — `CheckBox`, `RadioButton`, `RatingControl`, `ToggleSplitButton` — **landed** (this PR). Documented gaps on `CheckBoxDescriptor` (three-state mode + `OnCheckedStateChanged`) and `ToggleSplitButtonDescriptor` (Flyout child) carried forward to follow-ups.
- Value-bearing batch 2 — `ColorPicker`, `CalendarDatePicker`, `DatePicker`, `TimePicker` — **landed** (this PR). Date/time/color leaves with clean single-event shapes. `NumberBox` deferred — Immediate-mode keystroke handling and `NumberFormatter` reference-equality aren't expressible through the current builder surface.

**Carry-forward known defects from Phase 1:**
- **KD-3** — dispatch fast-path for the ported built-ins (M4 +88.9% V1 vs Today). Intersects with descriptor shape; fix during Phase 3 migration.
- **KD-4** — public typed-event surface for external descriptor authors. Scope narrowed by Phase 2 to external-author-only; in-tree descriptors already use the internal fast path via `DescriptorControlledPayload<T>`.

### Phase 4 — cleanup

- Delete the private switch.
- Delete `ChangeEchoSuppressor` if §8 audit succeeded (or finalize the §8.1 round-trip implementation if that path won).
- Split `EventHandlerState` per §9 — implement the per-control struct shapes from §9.2.
- Land the §11.6 hard byte gates (V2 must hit ≤100 / ≤320 / ≤500).
- Document the final author-facing surface in `docs/guide/`.

### Future: source generation (deferred, no committed timeline)

Source-gen (§7) is revisited when one of the triggers in §7's status section fires — WinUI-change → Reactor-update cycle time becomes a felt bottleneck, a new AOT-strict platform target ships, or descriptor declarations need compile-time validation. When that happens, the work plugs into whichever shape Phase 2 picked (descriptors → generator emits descriptors from attributes; handlers → generator emits handler classes from attributes). The §11 byte targets remain the gate; source-gen has to match or beat the hand-coded numbers without regressing any of the §13 questions Phase 2 already settled.

---

## §15 Performance validation suite

§11 and §12 make concrete byte and nanosecond claims. Those claims need to be validated with real measurements before any phase ships, and every subsequent phase needs a way to gate changes against regression. This section defines the test suite required to do that.

### 15.1 Goals

1. **Validate §11 byte targets** (≤100 / ≤320 / ≤500 per element by class) with measured allocations in real WinUI processes, not synthetic harness numbers.
2. **Validate §12 dispatch claims** (~1% of mount cost) with directly comparable per-mount cost across all three implementation models.
3. **Validate the §9.4 routed-event-rare hypothesis** by measuring `ModifierEventHandlerState`-allocation frequency across a representative app sample.
4. **Establish a regression budget** so Phase 4 control migrations can be merged with confidence that each PR doesn't silently break a win earned in an earlier phase.
5. **Surface unexpected costs** — animation interpolation, hot-reload roundtrip, ItemsControl realization — that the byte-counting in §11 can't predict.

### 15.2 Three-way baselining is non-negotiable

Every macro and most micro tests must compare three variants for the same scenario:

| Variant | What it measures | How it's built |
|---|---|---|
| **`Direct`** — raw WinUI | The floor. WinUI's own cost for the same UI, no framework overhead at all. | Hand-written `UIElement` construction in code-behind. Mirrors the WinUI control catalog one-for-one with the Reactor scenario. |
| **`ReactorToday`** — existing dispatch | The baseline to improve on. Today's switch + giant `EventHandlerState` + echo suppressor + private `MountXxx`. | Existing Reactor `main` branch, unchanged. |
| **`ReactorV2`** — new control model | The proof point. Whichever phase variant we're evaluating (v1 handlers, descriptors, or source-gen). | Feature-flagged build of Reactor on the work-in-progress branch. |

Three variants, same scenarios, same machine, same session, fixed environment. Without all three, you can't tell whether a `ReactorV2` improvement closed the gap to WinUI or just shuffled cost around within Reactor.

Existing infrastructure to lean on (avoid building parallel suites):
- `tests/stress_perf/` already has `StressPerf.Direct` (raw WinUI), `StressPerf.Reactor`, `StressPerf.Bound`, `StressPerf.Wpf`, `StressPerf.DirectX`. **Add `StressPerf.ReactorV2`** as a fourth variant with the same scenario surface. The `StressPerf.Shared` project already holds shared scenario definitions and `PerfTracker`.
- `tests/startup_perf/` already has `BlankReactor` / `BlankRNW` / `BlankWinUI3` for time-to-first-frame. **Add `BlankReactorV2`** alongside.
- `tests/perf_bench/` already has `BenchTracker` and CLI options for microbenchmarks. **Add a `PerfBench.ControlModel` project** for the new model's microbenches.

### 15.3 Micro suite — single-process BenchmarkDotNet

Microbenchmarks isolate one mechanism each, run in a tight loop, and report nanoseconds + allocation bytes per operation. Run in `PerfBench` infrastructure. Each test ships in three implementations matching §15.2.

| # | Bench | Scenario | Measures | Expected delta (V2 vs Today) |
|---|---|---|---|---|
| M1 | `Mount_Leaf_NoCallback` | Construct a `TextBlockElement("hi")` and mount it under a `Grid`. Loop 100k. | Per-mount ns and bytes for the floor case. | Today: ~248 B per §11.1. V2 target: ≤100 B. |
| M2 | `Mount_Leaf_OneCallback` | Construct a `ToggleSwitchElement` with `OnIsOnChanged`. Mount, unmount, repeat. | Per-mount ns and bytes when one control event is wired. | Today: ~800 B. V2 target: ≤320 B. |
| M3 | `Mount_Leaf_ThreeCallbacks` | `ButtonElement` with `OnClick` + `.OnPointerPressed` + `.OnTapped`. | Cost when one control event and two routed-input events are wired. | Today: ~1200 B. V2 target: ≤500 B. |
| M4 | `Dispatch_Switch_Cold` | First mount of each of 70 element types, measured per-arm. | Cold dispatch cost; PGO has not warmed yet. | V2 source-gen target: ≤ Today. |
| M5 | `Dispatch_Switch_Warm` | After 10k mounts in PGO-friendly distribution. | Hot dispatch cost. | V2 ≈ Today (per §12). |
| M6 | `Dispatch_ExternalType` | `RegisterType` external control, mounted 100k. | Dictionary path cost. | V2 ≈ Today. |
| M7 | `Update_NoChange` | Re-render a 1000-element tree where nothing changed. | Diff cost when work is purely "skip." | V2 should be ≤ Today. |
| M8 | `Update_OneLeafChanged` | Re-render where one leaf at depth 5 changed. | Diff specificity. | V2 ≈ Today; mostly a regression guard. |
| M9 | `Update_AllChanged` | Re-render where every value-bearing prop changed. | Worst-case echo handling. | V2 with §8 changes: depends on which echo direction wins. |
| M10 | `EventHandlerState_Alloc` | Wire one event, measure allocation count + bytes. | The §9 split's headline win. | Today: 424 B EHS. V2 target: ~32 B per-control table; ModifierEHS not allocated. |
| M11 | `ModifierEHS_Frequency` | Mount a 1000-element representative tree (mix of TextBlock, Button, Border, ToggleSwitch, Slider). Count how many elements allocated `ModifierEventHandlerState`. | Validates §9.4's "rare in practice" hypothesis. | V2 expectation: <20% of elements. |
| M12 | `Pool_Rent_HotPath` | ListView recycle scenario: 100 element instances cycling through 20 pool slots. | Pool effectiveness; regression guard for `ctx.AllocateControl` API change. | V2 ≈ Today. |
| M13 | `Setters_Suppression_Scope` | `Set(ts => ts.IsOn = true)` on a `ToggleSwitch` with `OnIsOnChanged`. Verify callback does NOT fire (correctness, not perf). | The §8.2 fix. | V2: callback count = 0. Today: callback count = 1 (the bug). |

Each micro test reports:
- Mean ns per operation (with 95% CI)
- Allocation bytes per operation
- Gen0/Gen1/Gen2 collections per 1M operations
- Final managed heap size delta

Run on Release / ARM64 and Release / x64. Reject results from `Debug` builds — JIT optimizations matter here.

### 15.4 Macro suite — separate-process scenario apps

Macrobenchmarks measure realistic workloads in real WinUI processes. Each variant is a separate executable (the `stress_perf` shape) so process-level costs (XAML init, dispatcher creation, DXGI swapchain) are included rather than amortized.

| # | Bench | Scenario | Measures |
|---|---|---|---|
| L1 | `TTFF_Blank` | Blank window, single `TextBlock`. Process spawn → first composited frame. | Time-to-first-frame for the floor case. Three variants from `startup_perf/`. |
| L2 | `TTFF_LoginForm` | Realistic login form: 6 controls (header, email, password, remember-checkbox, submit, link). Process spawn → first frame. | TTFF for a small-but-real first screen. |
| L3 | `TTFF_SettingsPage` | 50-control page: mixed ToggleSwitches, ComboBoxes, NumberBoxes, headers, dividers. | TTFF for a control-heavy page. |
| L4 | `WorkingSet_AtStartup` | After L2 reaches first frame, snapshot working set (private bytes + managed heap). | Initial cold memory. |
| L5 | `WorkingSet_Steady` | L3 + 5 minutes of idle interaction (mouse, focus changes, no scrolling). Snapshot working set every 30s. | Steady-state memory; detects leaks and GC drift. |
| L6 | `FPS_VirtualizedList_Scroll` | Existing `stress_perf` scenarios (10k items, scroll continuously, measure frame time + dropped frames). | Throughput under recycling pressure. Add `ReactorV2` variant. |
| L7 | `FPS_AnimatedTree` | 200-element tree with one prop animated continuously (color, transform). Measure frame time + GC pauses. | Update-path cost under steady mutation. |
| L8 | `FPS_HotStateUpdate` | 1000-element form bound to a `[NotifyPropertyChanged]` model; mutate one leaf at 60Hz. Measure dispatcher queue depth and frame time. | Selective-update cost under load. |
| L9 | `GC_PerFrame_AnimatedTree` | Same as L7 but record Gen0/Gen1/Gen2 collection counts per second and max pause time. | Per-frame allocation; detects "we allocated 50 KB this frame" regressions. |
| L10 | `Mount_Storm` | Construct a 10k-element tree all at once (e.g., expand a tree node). Measure wall time + max GC pause. | Burst-mount throughput. |
| L11 | `LongLived_HeapStability` | 30-minute synthetic user session: scroll, click, toggle, navigate between tabs. Sample heap every minute. | Heap drift; detects subtle leaks in handler / event-table lifetime. |
| L12 | `HotReload_Roundtrip` | Edit a descriptor / element record, trigger hot-reload, measure time to re-rendered frame + heap delta. | **Observability only.** Per §13 Q15 (Resolved), component-definition changes may require process restart; L12 documents actual round-trip cost but does not gate any phase or shift Q1. |
| L13 | `SplitLibrary_MixedTree` | Mount and update a realistic tree where ≥50% of the element types come from a *separate* `Reactor.Controls.*` assembly (registered via public API, no `InternalsVisibleTo`). Compare against the same tree where all elements are in-core. | Validates §1.1's split-library plan. Detects any per-element cost the registry path adds vs. the built-in switch. The post-split future is the production case — measure it directly. |
| L14 | `SplitLibrary_MixedTree_AOT` | L13 published with `PublishTrimmed=true` + `IsAotCompatible=true`. Same scenario, AOT binary. | Confirms the split-library path doesn't depend on reflection or trim-unsafe constructs that survive in a JIT build but break under AOT. |

Each macro test reports:
- Cold-process timings (one process, one measurement)
- Warm-cache timings (process pre-touched by previous run within the same session)
- Three repetitions minimum; report median + p95
- Working set delta (RSS), managed heap (`GC.GetTotalMemory(false)` and `Process.PrivateMemorySize64`)
- GC counts: Gen0/1/2 collections, total pause time, max single pause

### 15.5 Measurement methodology — keeping the numbers trustworthy

Existing memories about `stress_perf` (`memory/reference_stress_perf_window_throttling.md`, `memory/reference_stress_perf_drr_battery.md`) capture invariants the suite must respect:

- **Foreground & not occluded.** DWM pauses composition for occluded or background windows (~1.85× FPS drop). The harness must Z-order the bench window on top, position it on a real monitor (not off-screen), and assert non-occluded before timing begins.
- **AC power only.** Win11 Dynamic Refresh Rate scales display refresh based on GPU activity when on battery. Compare battery vs AC runs and you get garbage.
- **Fixed display refresh.** Disable DRR for the test session; lock to a known refresh (60 / 120 / 165 Hz). Record the refresh in the result row.
- **No virtual-desktop / RDP / projection switches during a run.** Detect and abort runs that experienced a session switch.
- **CPU governor / power plan.** Lock to "high performance" or document the power plan in the result row.
- **Process priority and affinity.** Optionally pin bench process to specific cores; document the pinning. Don't pin in normal CI — that distorts realistic-deployment numbers.
- **Warm-up.** Each macro scenario runs a 3-iteration warm-up before timed iterations. Each micro scenario uses BenchmarkDotNet's default warm-up.

Result records (one row per `(scenario, variant, machine)` tuple) carry: machine SKU, CPU model, OS build, .NET version, refresh rate, power source, monitor configuration, foreground-confirmed flag, timestamp. Two runs with mismatched environment metadata are not comparable; the reporting layer enforces this.

### 15.6 Reporting and regression budgets

The suite produces JSON-Lines output (one row per scenario × variant × iteration) consumed by an aggregator that emits:

1. **Absolute comparison table** — `Direct` / `ReactorToday` / `ReactorV2` side by side for every scenario, every metric.
2. **Reactor delta** — `ReactorV2 vs ReactorToday`, percent change with CI.
3. **WinUI gap** — `ReactorV2 vs Direct`, the absolute overhead Reactor still adds. The §10 question ("how thin can the Reactor delta be?") gets a number here.
4. **Trend chart** — per-PR results in CI so regressions are visible per-commit.

Regression budgets (block merge if exceeded):

| Metric class | Budget |
|---|---|
| Per-element allocation (M1–M3) | Must improve or stay equal vs current `ReactorToday` baseline. No regressions allowed once §11 targets are hit. |
| Dispatch cost (M4–M6) | ±10% of baseline (per §12 it's noise; this guards against an accidental cliff). |
| Update cost (M7–M9) | ±5% on the no-change case (M7); ≤10% regression on the one-leaf case (M8). |
| TTFF (L1–L3) | ≤5% regression. Better-than-baseline is the goal but not required per-PR. |
| Working set (L4–L5) | ≤2% regression on initial; ≤5% on steady-state. Larger regressions need explicit justification in the PR. |
| FPS / frame time (L6–L8) | p95 frame time ≤105% of baseline. Median ≤100%. |
| GC pauses (L9) | Max pause and total pause time ≤ baseline. Allocation rate is the input we're optimizing. |
| Heap stability (L11) | Slope of managed-heap-over-time within ±10% of baseline. |

The §11.6 targets become **hard gates** at Phase 5 cleanup: if `ReactorV2` Mount_Leaf_NoCallback hasn't hit ≤100 B by then, the cleanup PR is blocked.

### 15.7 Phase coupling — which tests gate which phases

| Phase | Tests required to pass | Tests that may fail (data-gathering) |
|---|---|---|
| **Phase 0 (spec process)** | Suite infrastructure builds and runs; `Direct` and `ReactorToday` numbers captured for every M and L test. Results published to `docs/specs/047/baseline-results/`. §11 / §12 updated with measured numbers. | — (this is the data-gathering phase by definition) |
| Phase 1 (v1 protocol) | M1, M2, M5, M7, L1, L4, **L13** (split-library mixed tree ≤ +10% vs all-in-core), **L14** (AOT build clean) | M10, M11, L6 (data only — informs descriptor design) |
| Phase 2 (descriptor decision) | M13 (setters correctness). Descriptor-vs-handler micro+macro head-to-head completes and produces a Phase-2 decision per §13 Q1 matrix. | L12 (observability only per Q15 — does not inform Q1) |
| Phase 3 (controls migration, per-PR) | All Phase 1 gates + the §15.6 regression budgets — the suite is the merge gate, every PR. | — |
| Phase 4 (cleanup) | §11.6 targets become hard gates: ≤100 B no-callback, ≤320 B one-callback, ≤500 B three-callback. M10 must show the §9 EHS-allocation drop. | — |
| Future (source-gen, when revisited) | Must match or beat the Phase-4 hand-coded numbers across the entire suite. No regression on any §13 question already settled. | — |

### 15.8 Test surface for §13's open questions

Each significant open question in §13 should have at least one test that disambiguates it. Mapping:

| §13 question | Test |
|---|---|
| Q1 (descriptor vs hand-coded handler) | Phase 2 head-to-head: implement `ToggleSwitch` + `Slider` + `Border` in both shapes, run M1/M2/M5/M7/M10/L4/L9 on both, apply the §13 Q1 decision matrix. Winner is decided by the matrix, not opinion. L12 runs for observability per Q15 but does not feed the matrix. Source-gen is deferred (see §7 status) — not a Phase-2 contender. |
| Q3 (echo suppression elimination, §8 / §8.1) | A correctness test pair: `Echo_Coercion_Slider` (write Value=1000 with Maximum=100; observe whether callback fires with stale or new value), `Echo_UserStateRacesRender` (queue an SetState between render and event-dispatch; observe state coherency). Run against `delete + tight diff`, `mostRecentEventCount`, and `suppression-as-is` to see which actually works. |
| Q6 (setters rerun) | M7 with two variants — setters always re-run vs setters skip-on-array-equality. Measure delta. |
| Q7 (pool integration) | M12 with `ctx.AllocateControl` vs direct `new T()` per-handler. Confirm pool still functions. |
| Q11 (`handledEventsToo`) | A scenario where a child Handled-marks `KeyDown`; the parent has `.OnKeyDownAny`. Verify the parent fires. |
| Q15 (hot-reload) | L12 against the Phase 2 winner (handler or descriptor). |
| Q17 (registry precedence) | Test scenario: register a handler for an element type whose base also has a handler. Verify exact-type lookup, verify duplicate-registration diagnostic fires, verify `RegisterOverride` is the only silent path. |
| Q18 (pool policy) | M12 plus a correctness scenario: rent → mount → mutate state → unmount/return → rent same control → verify no residual state from previous tenant. Run against pool-policy-aware and pool-policy-naive handlers. |
| Q19 (`WriteSuppressed` as public primitive) | M2/M13 against Phase 1's `WriteSuppressed` backed by today's suppressor. Phase 4 swap of the underlying mechanism must not change the test outcome. |

### 15.9 What the suite does NOT cover

- **AOT-specific behavior.** Trimming and Native AOT need their own validation pass; the runtime numbers above are all JIT/CoreCLR. Add a Phase-3 AOT variant if §7 lands.
- **Multi-window / multi-DispatcherQueue scenarios.** Out of scope for this spec.
- **Theming changes during a session.** Not on the path of the control-model rework.
- **Accessibility tree allocation.** Worth measuring but a separate work item — current spec doesn't change accessibility plumbing.

---

## Appendix A — relation to existing extension points

| Existing | This proposal |
|---|---|
| `RegisterType<TElement, TControl>(mount, update, unmount)` lambdas | Becomes a thin shim over `IElementHandler<TElement, TControl>` for source compatibility. |
| `internal Action<TControl>[] Setters` per element record | Universal base record `ExtensibleElement<TControl>` carries it. |
| `internal static ApplySetters<T>` | Method on `MountContext` / `UpdateContext`. |
| `internal SetElementTag` / `GetElementTag` | `MountContext.Bind` / `BindFor<T>`. Raw versions stay internal. |
| `internal ChangeEchoSuppressor` | `ReactorBinding<T>.WriteSuppressed`, then likely deleted in §8. |
| `internal EventHandlerState` + `Ensure*Subscribed` | `ReactorBinding<T>.On<Event>(...)` for modifier events; per-control generated tables for control events (§9). |
| Built-in `MountXxx` / `UpdateXxx` private methods | Per-control handlers, descriptors, or generated code (depending on which phase wins). |
| `_typeRegistry` dictionary lookup | Same, OR replaced by a generated type-switch (§7). |

## Appendix B — relation to spec 042 (keyed list reconciliation)

Spec 042 already established `ChildReconciler.Reconcile` as the keyed-LIS algorithm and `ReactorListState` as the templated-list state. The descriptor model in §6 needs a `Children` field that names which reconciliation strategy a control uses (none / slot / panel-of-children / templated-items-host). The `templated-items-host` strategy plugs directly into the spec 042 machinery; no new design is needed for the list-reconciliation layer itself. This spec only addresses the *single-control* extensibility surface; child reconciliation remains spec 042's territory.

## Appendix C — relation to spec 034 (modifier bucketing)

Spec 034 introduced `LayoutModifiers` / `VisualModifiers` sub-records on the modifier system to reduce allocation for high-frequency lists. The modifier pipeline this spec leans on (`ApplyModifiers` at `Reconciler.Mount.cs:184`) is the same machinery — the descriptor model doesn't change anything about modifiers, but a future evolution where modifiers themselves become descriptor-driven property writes (e.g., `Foreground` modifier as a `Prop.OneWay` against the control's `Foreground` DP) could collapse modifier handling and control handling into one pipeline. Out of scope here; flagged for §11.
