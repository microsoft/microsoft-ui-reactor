# Fully Extensible Control Model — Design Proposal

## Status

**Proposal — not yet scheduled.** This spec documents a design conversation about removing the asymmetry between *built-in Reactor controls* and *externally-authored controls registered via `Reconciler.RegisterType`*. The conversation started concretely (could a Win2D `CanvasControl` wrapper live downstream in the pix project without Reactor changes?), and ended in a broader question: what would it take for every mechanism Reactor uses to implement its own controls to be available to third-party authors — and could that protocol be smaller, more data-driven, and lower-overhead than what we have today?

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

---

## §1 Motivation

Reactor today has a public extension hook, `Reconciler.RegisterType<TElement, TControl>(mount, update, unmount)` at `src/Reactor/Core/Reconciler.cs:521`. A downstream project (Pix's WinUI port, the Monaco sample at `samples/apps/monaco-editor/Monaco/MonacoEditorElement.cs`, the in-tree docking system, several utility controls) can register a custom element type with mount/update/unmount lambdas and use it indistinguishably from a built-in element *as far as the dispatch table is concerned*.

However: **the lambdas registered via `RegisterType` cannot reach most of the machinery that built-in controls use.** Specifically, the following are `internal`:

| Mechanism | Location | What it does |
|---|---|---|
| `ApplySetters<T>` | `Reconciler.cs:1436` | Runs the `Action<TControl>[]` from the element's `.Set(...)` modifier chain |
| `SetElementTag` / `GetElementTag` | `Reconciler.cs:331-352` | Writes/reads the current `Element` on the `ReactorAttached.StateProperty` attached DP — feeds the event trampoline |
| `ChangeEchoSuppressor` | `ChangeEchoSuppressor.cs` | Suppresses the change-event echo that fires when the engine programmatically writes a value-bearing DP (`ColorPicker.Color`, `ToggleSwitch.IsOn`, `NumberBox.Value`, …) |
| `EventHandlerState` + `Ensure*Subscribed` family | `Reconciler.cs:2780+`, `2963-3069+` | Attach exactly one stable trampoline per WinUI event per native DependencyObject; update handler delegates by swapping a `Current*` field on the state object rather than `event +=` / `-=` |
| `ApplyDefaultAutomationName` / `ApplyThemeBindings` / `ApplyResourceOverrides` | `Reconciler.cs` | Per-control accessibility, theming, resource override pipelines |
| `_pool` (`ElementPool`) | `Reconciler.cs` | Control rental/return for re-mount and ListView recycling |

In other words: an external author who tries to wire `control.PointerPressed += ...` themselves silently bypasses pool-survivable subscription and re-introduces double-subscribe on re-mount (issue #114). An external author who writes a value-bearing DP without `BeginSuppress` re-introduces the cross-state-echo bug from spec 030. The asymmetry isn't just "first-party gets nicer helpers"; it's "first-party gets correctness."

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
    UIElement? Update(UpdateContext ctx, TElement oldEl, TElement newEl, TControl control);
    void Unmount(UnmountContext ctx, TControl control) { }
    void ReconcileChildren(ChildReconcileContext ctx, TElement oldEl, TElement newEl, TControl control) { }
}
```

The `MountContext` / `UpdateContext` expose the engine's mechanisms as typed operations:

```csharp
public readonly ref struct MountContext
{
    public ElementPool Pool { get; }
    public Action RequestRerender { get; }
    public UIElement? MountChild(Element child);
    public void ApplySetters<T>(Action<T>[] setters, T c) where T : class;
    public ReactorBinding<TElement> BindFor<TElement>(FrameworkElement c, TElement el)
        where TElement : Element;
    public IDisposable PushContextScope(IReadOnlyDictionary<object, object?> values);
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
        var ctrl = ctx.Pool.TryRent<ToggleSwitch>() ?? new ToggleSwitch();
        ctrl.IsOn = el.IsOn;
        var bind = ctx.BindFor(ctrl, el);
        bind.OnCustomEvent<RoutedEventArgs>(
            subscribe: (c, h) => ((ToggleSwitch)c).Toggled += (s, e) => h(c, e),
            handler:   (cur, _) => cur.OnIsOnChanged?.Invoke(((ToggleSwitch)ctrl).IsOn));
        ctx.ApplySetters(el.Setters, ctrl);
        return ctrl;
    }

    public UIElement? Update(UpdateContext ctx, ToggleSwitchElement o, ToggleSwitchElement n, ToggleSwitch ctrl)
    {
        if (o.IsOn != n.IsOn)
            ctx.BindFor(ctrl, n).WriteSuppressed(() => ctrl.IsOn = n.IsOn);
        ctx.ApplySetters(n.Setters, ctrl);
        return null;
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
        Factory  = () => new ToggleSwitch(),

        Properties =
        [
            Prop.OneWay  (e => e.OnContent,  (c, v) => c.OnContent  = v),
            Prop.OneWay  (e => e.OffContent, (c, v) => c.OffContent = v),
            Prop.TwoWay  (e => e.IsOn,       (c, v) => c.IsOn = v,
                          changeEvent: nameof(ToggleSwitch.Toggled),
                          readBack:    c => c.IsOn,
                          callback:    e => e.OnIsOnChanged),
        ],

        Events =
        [
            // Non-prop-bound events (e.g., Tapped on a Card) declared here.
        ],
    };
```

What this buys:

- **`Prop.OneWay`** is a property write on Mount and on diff during Update. The reconciler compares old/new element prop and skips the write when equal. No echo possible because nothing fires the change event.
- **`Prop.TwoWay`** is a `(prop, event, readBack, callback)` quadruple. The reconciler:
  - Subscribes once to the change event via the shared trampoline.
  - Writes the prop on diff, suppressing the event for that write.
  - The trampoline reads back the post-event value via `readBack` and invokes `callback`.
- Setters and modifiers are still per-element; they remain on `ExtensibleElement<TControl>`.
- `ChildReconciler` integration is a separate descriptor field (`Children = ChildHandling.None` / `Slot(...)` / `Panel(...)` / `ItemsHost(...)`).

The handler interface goes away. Mount and Update become **interpreters of the descriptor**. Authors write data, not code. This is closer to how XAML works (DPs + events declared metadata), but resolved against `Element` records instead of XAML markup.

**Risk:** the descriptor needs to cover every shape a control might want — sometimes there's no clean (prop, event) pair (e.g., `NumberBox.NumberFormatter` is a property whose change triggers internal recomputation but no event, `TextBox.PlaceholderForeground` is a `Brush` themed prop, `CanvasControl.Draw` is an event with a `DrawingSession` arg that has no element-prop counterpart). The descriptor model has to either grow special cases or fall back to imperative handlers for the irregular cases. Probably both: a descriptor with an `Imperative` escape hatch covers the long tail.

---

## §7 Simplification direction: source-generated handlers

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

The echo suppressor exists because the engine writes value-bearing DPs from the update path, the WinUI control fires its change event, the trampoline invokes the user's callback with the value the engine just wrote, and (if user state has moved on between render and event-dispatch) that callback writes the *old* value back into the *new* state. Spec 030, issue #86, the PropertyGrid cross-row-swap bug.

But: **most built-ins already guard with `if (oldEl.Foo != newEl.Foo) ctrl.Foo = newEl.Foo`.** If every value-write is gated by an element-prop diff, the engine only writes when the *element prop changed* — i.e., when the user state genuinely moved. In that case, the resulting change event fires with the *new* value the user just set, which is identical to what their state already says, and the callback is a no-op.

The remaining echo cases are:
- Control-internal coercion (e.g., `Slider.Value = 1000` when `Maximum = 100` coerces to 100, and the event fires with 100, not 1000). The user's state says 1000; the engine wrote 1000; the event delivers 100. Without suppression, callback overwrites user state with 100.
- Float precision (engine writes 0.3, control stores 0.30000001, event fires with 0.30000001).
- Equality semantics on reference-typed values (engine writes `Color.Red`, control's internal `Color` doesn't `Equals` the same instance, event fires).

These are real but **enumerable**. The proposal: audit every `BeginSuppress` call site. Most are likely eliminable by a tighter diff gate. The genuinely-needed ones get documented as "this control coerces; use `TwoWayCoerced` in the descriptor" — a different binding kind, not a generic suppression mechanism. The ChangeEchoSuppressor module goes away.

Net effect: one fewer thing on the attached-DP state, one fewer invariant for handler authors to learn, one fewer call-site discipline to maintain. The few coercion-prone controls (Slider, NumberBox, ColorPicker, possibly DatePicker) declare it explicitly in their descriptor.

---

## §9 Simplification direction: per-control trampoline tables

`EventHandlerState` today carries one `Current<EventName>` field and one `<EventName>Trampoline` field for every event the reconciler knows how to wire — ~30 fields total. Most controls use 1–3 of these. A ToggleSwitch carries empty slots for `KeyDownTrampoline`, `PointerWheelChangedTrampoline`, `ButtonClickTrampoline`, … none of which it ever uses.

The reason for the shared struct is the modifier pipeline: pointer events, focus, key events can be attached to *any* element via modifiers, so the engine needs a uniform place to put them. That justifies *modifier-events* being shared. But control-intrinsic events (`Toggled`, `ValueChanged`, `Click`, `TextChanged`) don't need to live in the shared struct — they only fire from one control type.

A revised split:
- **`ModifierEventHandlerState`** — shared across all controls, holds the pointer/focus/key trampolines that modifiers attach. Lives on `ReactorState`.
- **Per-control event tables** — owned by each control's generated handler, stored either as static fields on the handler class (if the handler is a singleton, which it can be) or as a per-instance generated struct attached on its own attached DP keyed to that control type.

Net effect: `ReactorState` shrinks; per-control event tables are exactly the size they need to be; the source generator emits them mechanically.

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

If we adopt §7 + §9 + bucketed `Element` base, concrete targets for the design:

| Case | Bytes today | Target | Allocations today | Target |
|---|---|---|---|---|
| Leaf, no callbacks (TextBlock) | ~248 | **≤ 100** | 2 | **1** |
| Leaf, one callback (ToggleSwitch) | ~800 | **≤ 320** | 3–4 | **2** |
| Leaf, three callbacks (a Button with pointer modifiers) | ~1,200 | **≤ 500** | 5–6 | **2–3** |

These are aggressive but tractable. The §11.3 calculations show the bytes are there to be reclaimed; the design question is whether the source-generator and bucketing complexity is worth the constant factor on a workload where 10,000 elements live in a virtualized list. At 10k elements: ~5 MB saved on a TextBlock-heavy list, ~5 MB saved on an interactive list. That's GC-noticeable.

### 11.7 Where this measurement lands in the design

The byte counts make two things concrete that the earlier prose only hinted at:

1. **The shared `EventHandlerState` is the single biggest target.** It's the difference between "every element with a callback pays for every event Reactor knows about" and "every element pays for what it uses." Source-generated per-control tables (§9) capture most of the win on their own — even without descriptors or source-gen for the rest of the protocol.

2. **The `Element` base record is the second-biggest target.** Sixteen cross-cutting nullable fields × 8 bytes = 128 bytes paid by every element whether it uses them or not. Bucketing them into a single nullable `ElementExtensions` sub-record (mirroring `ElementModifiers`) is a mechanical, low-risk change that produces meaningful savings on its own. **This change is independent of the rest of the proposal** and could ship as a precursor — spec 034 already established the bucketing pattern.

Both are worth landing regardless of which form (descriptor, source-gen, handler protocol) the rest of the extensibility design takes.

---

## §12 Runtime perf — dispatch, code size, cache, JIT

§11 quantified the memory wins. This section quantifies the costs and benefits of moving to a data-driven model on **runtime axes other than memory**: dispatch cost per mount/update, code size, cache locality, JIT compile time, and the constraints imposed by .NET 9 PGO. Numbers are estimated on .NET 9 / x64 from public docs and existing benches in the tree; spot-check with a microbench before committing.

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

To revisit before committing to an implementation:

1. **Descriptor vs. handler vs. source-gen — which one ships?** The three are not mutually exclusive; the source generator could emit handlers that interpret descriptors. But picking the *primary* author-facing surface determines what gets documented, taught, and ossified. A spike on a single control through each approach would clarify ergonomics.
2. **What's the AOT story end-to-end?** Source generators are AOT-clean by construction. Runtime descriptor interpretation requires careful avoidance of reflection in the binding evaluators. Runtime handlers are fine. Pick the constraint-set first.
3. **Can echo suppression be eliminated, and at what cost?** §8 hypothesizes yes for most cases; needs a per-call-site audit of every `BeginSuppress` in the tree, with a measured before/after on the resulting handler code. The audit itself is a worthwhile cleanup regardless of which protocol ships.
4. **What's the `ReconcileChildren` shape?** The hardest controls (`ListView`, `ItemsView`, `TemplatedList`, `Grid` with attached row/col DPs) need a child-reconciliation hook. The descriptor model handles "I have one child" or "I have a flat list of children" cleanly, but custom keyed reconciliation (per spec 042) needs an escape hatch. Probably: descriptor names a strategy enum, escape hatch via `Imperative` for the few that need it.
5. **Is `RegisterType` even the right verb?** If the steady-state is "every control is described declaratively and code is generated," then `RegisterType` is a runtime artifact for *late-bound* external controls. First-party controls don't register; they exist statically. External controls register the generated descriptor. Worth designing the registration API around what makes the steady state cleanest.
6. **Should setters re-run on every update or only when the setters array changed?** Today they re-run unconditionally (idempotent, but wasteful). A reference-equality check on the array would skip most updates. Worth measuring.
7. **Pool integration with descriptors.** If the generator knows a control is poolable, it can emit `Pool.Rent` vs. `new T()` directly. If not, descriptors need a `Poolable: true/false` field.
8. **What does the `Set(...)` modifier mean in a descriptor world?** It's the escape hatch for "the descriptor doesn't expose this property; I want to write it manually." Worth keeping, but `ApplySetters` runs after declarative property writes, so the precedence rule needs to be documented (setters win, presumably).
9. **Override semantics.** If an external author wants to swap in a fake `ButtonHandler` for testing, what's the API? `RegisterOverride<ButtonElement, Button>(handler)`? With a diagnostic log so accidental overrides are visible? The current `RegisterType` is "last writer wins" silently — under a fully extensible model, it should probably be louder.
10. **Compile-time validation.** A descriptor where `ChangeEvent: nameof(ToggleSwitch.Toggled)` is misspelled (or the property name is wrong) is a runtime failure today. A source generator can validate at compile time. A runtime descriptor can't, unless we add a registration-time check. Worth designing for compile-time validation from the start.

---

## §14 Suggested phasing

If this proposal is greenlit, an honest phasing avoids committing to the full source-generator design before validating that the simpler descriptor model holds up:

**Phase 0 — Audit and groundwork (no API changes).**
- Inventory every `BeginSuppress` call site. Mark which are eliminable via tighter diff, which represent genuine coercion, which are float-precision artifacts. Output: a CSV of "control × property × why."
- Inventory every `EventHandlerState` field. Mark which are "any element via modifier" vs. "this control only." Output: a split list.
- Measure the cost of the dictionary lookup in `RegisterType` dispatch vs. the existing switch. Microbench, isolated.

**Phase 1 — v1 protocol behind a feature flag.**
- Promote `ApplySetters`, `SetElementTag`, `GetElementTag` to public.
- Ship `IElementHandler<TElement, TControl>` + `MountContext` / `UpdateContext` / `ReactorBinding<TElement>` from §4.
- Port two controls through the new protocol: **`ToggleSwitch` (built-in, echo-prone) and `Win2DCanvas` (external).** Together they exercise every load-bearing piece.
- Existing controls keep their private MountXxx paths. No big-bang migration.
- Measure: API ergonomics, code size, runtime cost vs. the private path.

**Phase 2 — descriptor model spike.**
- Build a `ControlDescriptor<TElement, TControl>` interpreter using the v1 context surface internally.
- Re-port `ToggleSwitch` through the descriptor.
- Compare LOC, readability, runtime overhead vs. the imperative v1 handler.
- Decision point: descriptor as primary API, or descriptor as optional sugar?

**Phase 3 — source generator spike.**
- Add `[ReactorControl]` / `[Bind]` / `[Wire]` attributes.
- Generate a handler (or descriptor) for `ToggleSwitchElement` from attributes.
- Compare LOC of the element declaration (with attributes) vs. the descriptor declaration. Compare the generated handler against the hand-written v1 handler.
- Decision point: ship the source generator, or stop at descriptors?

**Phase 4 — controls migration.**
- Migrate the value-bearing family first (Slider, NumberBox, ColorPicker, RatingControl). Closes out the echo-suppressor audit.
- Then input controls (Button, TextBox, CheckBox). Exercises shared trampolines.
- Then containers (Stack, Grid, Flex). Exercises `ReconcileChildren`.
- Then templated lists. Exercises keyed reconciliation interop with spec 042.
- Then the long tail (NavigationView, dialogs, MapControl, …).
- The private MountXxx switch shrinks one arm per PR.

**Phase 5 — cleanup.**
- Delete the private switch.
- Delete `ChangeEchoSuppressor` if §8 audit succeeded.
- Split `EventHandlerState` per §9.
- Document the final author-facing surface in `docs/guide/`.

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
