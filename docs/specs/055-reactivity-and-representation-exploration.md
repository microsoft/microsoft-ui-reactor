# Reactivity & Representation — Four-Prototype Exploration

## Status

**Exploration / RFC** — 2026-06-05. Updated 2026-06-12 with initial
P1/P3 prototype results.

Tracking context: GitHub discussion
[microsoft/microsoft-ui-reactor#531 — *Architectural Inquiry: Rethinking State
Paradigms and Render Boundaries*](https://github.com/microsoft/microsoft-ui-reactor/discussions/531).
The OP (`@yinhx3`) challenges the React-hook reactivity model that Reactor
inherited and asks why Reactor didn't adopt a SolidJS-style signal model or a
SwiftUI / Lit-style identity-slot model. The maintainer reply
([codemonkeychris](https://github.com/microsoft/microsoft-ui-reactor/discussions/531#discussioncomment-…))
agreed the comparison would be worth running concretely. This spec is that
experiment.

> Related prior work:
> [spec 009 — State & Components Design](009-state-and-components-design.md)
> (current hook model),
> [spec 042 — Keyed List Reconciliation](042-keyed-list-reconciliation-design.md),
> [spec 047 — Extensible Control Model](047-extensible-control-model.md)
> (descriptor + echo-suppression hybrid),
> [spec 048 — Control Registration & Trimming](048-control-registration-and-trimming.md),
> [spec 034 — Element Allocation Reduction](034-element-allocation-reduction.md).

---

## Table of contents

- [§1 Motivation](#1-motivation)
- [§2 Goals / non-goals](#2-goals--non-goals)
- [§3 The two axes & the four prototypes](#3-the-two-axes--the-four-prototypes)
- [§4 Reactivity axis — Solid vs Lit](#4-reactivity-axis--solid-vs-lit)
- [§5 Representation axis — Element tree vs direct UIElement](#5-representation-axis--element-tree-vs-direct-uielement)
- [§6 Sketch — the four prototypes (counter)](#6-sketch--the-four-prototypes-counter)
- [§7 Collections — where the counter doesn't go](#7-collections--where-the-counter-doesnt-go)
- [§8 Evaluation axes & scorecard](#8-evaluation-axes--scorecard)
- [§9 Performance hypothesis](#9-performance-hypothesis)
- [§10 Methodology — what to build, what to measure](#10-methodology--what-to-build-what-to-measure)
- [§11 Disqualifying outcomes](#11-disqualifying-outcomes)
- [§12 Open questions](#12-open-questions)
- [§13 Out of scope](#13-out-of-scope)
- [Appendix A — What source generators and language changes could buy](#appendix-a--what-source-generators-and-language-changes-could-buy)
- [Appendix B — Prototype syntax and hook mappings](#appendix-b--prototype-syntax-and-hook-mappings)

---

## §1 Motivation

Reactor's current programming model is "React in C#": function components,
hooks tracked by call order, immutable element records produced each render,
reconciler diffs old-vs-new and patches WinUI controls. The model works; ~2200
unit tests pass; complex samples (Minesweeper, Outlook, TodoApp) ship.

But it carries React's well-documented mental tax:

1. **Stale closures** in async handlers — `count` captured at render time
   becomes wrong by the time the `await` resumes.
2. **Manual memoization** — `UseMemo` / `UseCallback` ceremony to keep
   identities stable across re-runs of `Render()`.
3. **Indiscriminate re-runs** — any `setState` re-executes the entire
   component body, even though only one bound DP needed to change.
4. **Hook rules** — call order matters, no conditional hooks, branchy code
   gets refactored to satisfy the framework rather than the problem.

Simultaneously, Reactor maintains a **parallel class hierarchy** mirroring
WinUI: ~80 `*Element` records, ~1400 lines of fluent modifier extensions, a
descriptor protocol per control, mount/update handler bodies, plus the
reconciler itself. This is real surface area to maintain, document, and teach.

The OP's hypothesis (and the maintainer's instinct it's worth testing) is that
**a signal-based or property-based reactivity model could shed both the
React tax and, possibly, the Element-tree machinery itself** — keeping the
declarative authoring feel while collapsing the framework to something
smaller.

We don't know if that's true. The cost of getting it wrong is high. This
spec proposes a structured 4-prototype experiment so we can answer the
question with code instead of armchair architecture.

---

## §2 Goals / non-goals

### Goals

- Build four small but real prototypes covering the 2×2 of
  *reactivity model* × *element representation*.
- Port the **same fixed slice of existing samples** through each prototype
  so cross-comparison is apples-to-apples.
- Score each prototype against an explicit, written-down rubric — not vibes.
- Land a written recommendation: stay on the current model, migrate to one
  of the four, or pursue a hybrid.

### Non-goals

- Shipping any of the four as the production model in this spec.
- Re-architecting the reconciler before the rubric scoring is done.
- Litigating "is signals better than hooks?" in the abstract. We're
  measuring on *this* codebase, *this* platform, *this* team.
- Replacing the factory DSL (`VStack`, `Button`, fluent modifiers) — author
  ergonomics at the call site should look as similar as possible across all
  four prototypes so we're measuring the *model*, not the syntax.

---

## §3 The two axes & the four prototypes

|  | **Element tree retained** | **Direct UIElement construction** |
|---|---|---|
| **Lit-style** (long-lived component instance, reactive instance properties, render-on-change) | **P1** — Lit × Element | **P2** — Lit × Direct |
| **Solid-style** (one-shot setup, signals, per-slot fine-grained updates) | **P3** — Solid × Element | **P4** — Solid × Direct |

The matrix is deliberate: the two questions are independent, and conflating
them ("we adopted Solid *therefore* we ditched the element tree") would hide
which change is actually paying for which result.

The **representation axis** is the higher-stakes question for *this*
codebase. The Element tree is ~80 records, ~1400 lines of fluent modifiers,
the descriptor protocol, mount/update bodies, pooling, and a substantial
reconciler. Earlier discussion treated the Element tree as load-bearing for
threading and headless testing; on review those rationales are weak (WinUI
is STA so everything funnels to the UI thread anyway; the test infrastructure
can adapt). The remaining honest reasons are **pooling**, **structural
reconcile** (conditionals, keyed lists), and **hot reload / preview** — and
each is something a direct-UIElement prototype must answer for, not skirt.

The **reactivity axis** is the OP's question: even with the element tree
intact, swapping hooks for signals or reactive properties changes the
authoring experience and the runtime profile.

---

## §4 Reactivity axis — Solid vs Lit

| | Reactor today (React) | Solid-style | Lit-style |
|---|---|---|---|
| Component body re-runs on update | Every change | **Mount once** | Method runs, but `this` is stable |
| State storage | Hook slot array per render | Long-lived `Signal<T>` cells | Instance field with generated setter |
| Tracking | Hook call order; manual `[deps]` | Automatic by-access in tracked scopes | Per-field opt-in (`SignalField<T>` wrapper) |
| Update granularity | Subtree diff | Per-bound slot patch | Per-component re-render, then subtree diff |
| Stale closure footgun | Yes | No | No |
| Memoization ceremony | Common | Rare | Rare |
| New mental tax | Hook rules + deps arrays | "Reads must be inside tracked scopes" (lambda-wrap) | "Mark what's reactive" + reactive-controller composition |
| Cross-cutting reuse | Custom hooks | Derived signals + factories | Reactive controllers |
| Source-generator dependency | None required | None required (call-site rewriting is out of scope; see Appendix A) | None required (instance state lives in plain wrapper fields; see Appendix A for sugar) |

Both alternatives are **push-based**: the framework knows what changed and
notifies subscribers, rather than React's pull-based "re-run everything and
diff." Where they differ is **scope**: Solid notifies *individual bound
slots*; Lit notifies *the host element, which re-renders its template*.

---

## §5 Representation axis — Element tree vs direct UIElement

### What the Element tree does today

1. **Snapshot description** that the reconciler diffs against — old vs new.
2. **Pooling indirection** — the record says "I want a TextBox here," the
   reconciler decides whether to rent a pooled instance or create one.
3. **Structural reconciliation** — `count switch { 0 => TextBlock(...), >0
   => Button(...) }` works because the reconciler sees the type change and
   swaps controls. Same for `Key()`-driven reorder/identity.
4. **Hot reload / preview** — re-render produces a new tree; diff →
   patch. VS Code preview ships the tree to a host.
5. **Off-UI-thread construction** (historically claimed) — element records
   are POCOs, buildable off-thread. **In practice WinUI is STA**, so the
   tree is almost always built on the UI thread anyway. This is not a real
   reason to keep the tree.
6. **Headless tests** — `Reactor.Tests` builds trees without a window. This
   is a real benefit *today*, but it's a property of the *test
   infrastructure*, not an inherent requirement. A direct-UIElement model
   can adapt the tests (or replace them with selftests).

### What "direct UIElement" would mean

Factories return `UIElement` instances (or `UIElement` + bound subscriptions
under Solid; `UIElement` + instance host under Lit). No `*Element` records.
No descriptor protocol. No mount/update dispatch. No pooling indirection
unless we add one explicitly. The reconciler shrinks to *structural*
duties only (swap-on-type-change, keyed list reorder), and even those happen
against live controls in a parent's `Children` collection.

The honest pros and cons:

| | Element tree retained | Direct UIElement |
|---|---|---|
| Lines of framework code | Higher (records + descriptors + reconciler) | Substantially lower — kills the parallel hierarchy |
| Fluent modifier surface | Stays as-is (~1400 LoC) | Becomes either WinUI direct setters or a thin builder layer |
| Adding a new control | Element record + descriptor + register | Just a factory method |
| Pooling | Built in | Must be re-added on top if we want it |
| Conditional / keyed reconcile | Reconciler handles it | Hand-rolled per call site OR small dedicated swap helper |
| Hot reload story | Re-render + diff | Tear down + rebuild subtree |
| Off-UI-thread potential | Theoretical | Gone — but already mostly gone today |
| Headless tests | Free | Need to adapt or move to selftests |
| Third-party extensibility | Descriptor + registration | "Write a factory" |
| Risk if we get it wrong | Low (we know this model works) | High (lots of code rewritten before we know) |

The user-driven framing for this spec: **removing the parallel hierarchy is
a huge potential win** that deserves a serious prototype, not dismissal. The
prototypes must demonstrate concretely whether the lost capabilities
(pooling, structural reconcile ergonomics, hot reload) can be recovered
cheaply or whether they were load-bearing in ways we don't yet appreciate.

---

## §6 Sketch — the four prototypes (counter)

The same `CounterDemo` shown four ways. None of these are final API
proposals — they're the directional shape so the rest of the spec is
concrete.

### Baseline — Reactor today (for reference)

```csharp
class CounterDemo : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        var (step,  setStep)  = UseState(1);

        return VStack(12,
            SubHeading($"Count: {count}"),
            HStack(8,
                Button($"- {step}", () => setCount(count - step)),
                Button("Reset", () => setCount(0)).IsEnabled(count != 0),
                Button($"+ {step}", () => setCount(count + step))),
            Slider(step, 1, 10, v => setStep((int)v))
        );
    }
}
```

### P1 — Lit × Element

Component is a long-lived class; state lives in **wrapper fields** whose
setter calls back into the host. `Render()` returns Element records as
today. Reconciler unchanged. No source generators, no attributes, no
reflection — the entire mechanism is plain C#.

```csharp
class CounterDemo : ReactiveComponent
{
    SignalField<int> _count;
    SignalField<int> _step;

    public CounterDemo()
    {
        _count = Field(0);    // Field<T> records `this` so its setter can RequestUpdate
        _step  = Field(1);
    }

    public override Element Render() =>
        VStack(12,
            SubHeading($"Count: {_count}"),                              // implicit T conversion
            HStack(8,
                Button($"- {_step}", () => _count.Value -= _step),
                Button("Reset", () => _count.Value = 0)
                    .IsEnabled(_count != 0),                             // compares as int
                Button($"+ {_step}", () => _count.Value += _step)),
            Slider(_step, 1, 10, v => _step.Value = (int)v));
}
```

How it works under the hood — note the `implicit operator T` that makes
reads transparent:

```csharp
public sealed class SignalField<T>
{
    readonly ReactiveComponent _host;
    T _value;
    internal SignalField(ReactiveComponent host, T value) { _host = host; _value = value; }

    public T Value
    {
        get => _value;
        set { if (EqualityComparer<T>.Default.Equals(_value, value)) return;
              _value = value; _host.RequestUpdate(); }
    }

    // Reads in `$"{_count}"`, `_count != 0`, `_count + 1` etc. all work via this.
    public static implicit operator T(SignalField<T> f) => f._value;
}

public abstract class ReactiveComponent : Component
{
    protected SignalField<T> Field<T>(T initial) => new(this, initial);
    internal void RequestUpdate() { /* schedule next Render() */ }
}
```

Reads are clean (implicit conversion); **writes stay verbose** (`_count.Value
= 5`) because C# has no assignment-operator overload. A `Set` method is an
alternative the spike should try (`_count.Set(5)` or
`_count.Set(c => c + 1)`) so authors never type `.Value` directly.

Cross-cutting concerns become `IReactiveController` implementations
(`OnMount` / `OnUnmount` / `OnBeforeUpdate`).

> A source-generator-based variant that lets authors write
> `[ReactiveProperty] public partial int Count { get; set; }` is sketched
> in [Appendix A](#appendix-a--what-source-generators-and-language-changes-could-buy)
> with explicit discussion of the cost of changing what an existing C#
> declaration *means* at runtime.

### P2 — Lit × Direct

Same long-lived component with the same wrapper-field pattern (and same
implicit-conversion ergonomics), but `Build()` returns a `UIElement`
directly. No element records, no reconciler. The host re-runs `Build()`
on any reactive change; a small structural-swap helper handles parent
children.

```csharp
class CounterDemo : ReactiveComponent
{
    SignalField<int> _count;
    SignalField<int> _step;

    public CounterDemo()
    {
        _count = Field(0);
        _step  = Field(1);
    }

    public override UIElement Build()
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(SubHeading($"Count: {_count}"));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(Button($"- {_step}", () => _count.Value -= _step));
        row.Children.Add(Button("Reset", () => _count.Value = 0,
                                isEnabled: _count != 0));
        row.Children.Add(Button($"+ {_step}", () => _count.Value += _step));
        stack.Children.Add(row);

        stack.Children.Add(Slider(_step, 1, 10, v => _step.Value = (int)v));
        return stack;
    }
}
```

On any wrapper-field change, `Build()` re-runs producing a fresh subtree;
a tiny structural diff swaps the parent's children. Open question whether
we cache per-call-site UIElements and reuse them (one of the things the
prototype must answer).

### P3 — Solid × Element

`Build()` runs **once** at mount. Returns Element records, but reactive
values are passed as getter delegates so the reconciler can wire them as
subscriptions, not snapshot them.

```csharp
class CounterDemo : SignalComponent
{
    public override Element Build()
    {
        var count = Signal(0);
        var step  = Signal(1);

        return VStack(12,
            SubHeading(() => $"Count: {count.Value}"),
            HStack(8,
                Button(() => $"- {step.Value}", () => count.Value -= step.Value),
                Button("Reset", () => count.Value = 0)
                    .IsEnabled(() => count.Value != 0),
                Button(() => $"+ {step.Value}", () => count.Value += step.Value)),
            Slider(step, 1, 10));   // two-way bind to the signal
    }
}
```

Factory overloads accept `T | Func<T> | Signal<T>` at every reactive prop
position. Conditional rendering needs a reactive boundary helper:

```csharp
When(() => count.Value > 10,
     then: () => SubHeading("getting big"),
     @else: () => Empty())
```

> **`Signal<T>` deliberately does NOT have `implicit operator T`** under
> the Solid prototypes — unlike P1/P2's `SignalField<T>`. The asymmetry is
> intentional. In a Solid model the framework must distinguish "snapshot at
> mount" (`Button(label, ...)` — `T` overload) from "subscribe and update"
> (`Button(() => label.Value, ...)` — `Func<T>` overload). If `Signal<T>`
> implicitly converted to `T`, the snapshot overload would silently win via
> the implicit conversion, the binding would never update, and there would
> be no error — exactly the bug class Solid's model exists to prevent. The
> verbosity of `.Value` / `() =>` at read sites is the price of explicit
> tracking. See §12 Q-N.

### P4 — Solid × Direct

Mount-once setup that builds WinUI controls and binds signals directly to
DPs. No element records, no reconciler.

```csharp
class CounterDemo : SignalComponent
{
    public override UIElement Build()
    {
        var count = Signal(0);
        var step  = Signal(1);

        var label = new TextBlock();
        Bind(label, TextBlock.TextProperty, () => $"Count: {count.Value}");

        var dec = new Button();
        Bind(dec, ContentControl.ContentProperty, () => $"- {step.Value}");
        dec.Click += (s, e) => count.Value -= step.Value;

        var reset = new Button { Content = "Reset" };
        Bind(reset, Control.IsEnabledProperty, () => count.Value != 0);
        reset.Click += (s, e) => count.Value = 0;

        var inc = new Button();
        Bind(inc, ContentControl.ContentProperty, () => $"+ {step.Value}");
        inc.Click += (s, e) => count.Value += step.Value;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(dec); row.Children.Add(reset); row.Children.Add(inc);

        var slider = new Slider { Minimum = 1, Maximum = 10 };
        BindTwoWay(slider, RangeBase.ValueProperty, step);

        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(label); stack.Children.Add(row); stack.Children.Add(slider);
        return stack;
    }
}
```

`Bind` registers a tracked effect on the signal getter that writes to the
DP. `BindTwoWay` wires both directions and reuses Reactor's echo-suppression
primitive. Conditional content uses a `Show` helper that swaps the parent's
child when its predicate signal flips.

> Same constraint as P3: **`Signal<T>` must not expose `implicit operator T`**.
> The whole point of the Solid model is that `Bind(label, prop, () => signal.Value)`
> subscribes while `prop = signal.Value` snapshots — and the framework
> can't tell the difference if a silent implicit conversion makes those
> two call sites identical to the compiler. The lambda-wrap noise is the
> cost of unambiguous tracking semantics. See §12 Q-N.

---

## §7 Collections — where the counter doesn't go

`CounterDemo` is a worst-case-light sample: scalar state, no collections,
no conditionals, no nesting, no per-item state, no templated controls.
These are the dimensions where the four models *actually* diverge. This
section walks two scenarios so the prototypes are designed to cover them
from the start, and so the evaluation rubric in §8 has the right
ammunition.

**Scenarios:**

1. **Transformation** — `items.Select(it => Button(it.Name, …))` — the
   bread-and-butter list pattern in TodoApp, Outlook samples, etc.
2. **Templated WinUI control** — `TemplatedListView<T>(items, item => …)`
   — real virtualization, container recycling, ItemTemplate replacement.

### §7.1 Scenario A — transformation

#### Baseline (today)

```csharp
return VStack(0, _state.Items.Select(it => TodoRow(it, dispatch)).ToArray());
```

`Render()` re-runs on each change. Reconciler diffs positionally, or
keyed when authors use `.Key(it.Id)` (spec 042). Per-item handlers
`() => Delete(it)` are reallocated each render but capture the current
render's `it`, so they're correct.

#### P1 — Lit × Element

At the call site, identical to the baseline (thanks to implicit conversion
on `SignalField<T>`):

```csharp
return VStack(0, _items.Select(it => Button(it.Name, () => Delete(it))).ToArray());
//                ^^^^^^ implicit SignalField<List<Item>> → List<Item>
```

**New question — who triggers `Render`?** Wrapper-field setters only fire
on field *assignment* (`_items.Value = newList`). Mutating an existing
item (`it.Name = "x"`) is invisible to the framework. Two patterns work:

- **Immutable items.** Parent replaces the whole list on any change.
  Simple, correct, allocations proportional to list size.
- **Per-item child `ReactiveComponent`.** `TodoRow : ReactiveComponent`
  with its own `SignalField<bool> _editing`. Now we need **keyed
  component identity** across re-renders so child state survives — the
  framework must match `(componentType, key)` → previous instance and
  patch in new props rather than recreate. This is the React `key=`
  problem reborn with long-lived component instances.

The Lit-style model inherits Reactor's existing keyed reconcile (spec
042) directly — this is one of the places where retaining the Element
tree *pays off*.

#### P2 — Lit × Direct

```csharp
var stack = new StackPanel();
foreach (var it in _items)
    stack.Children.Add(MakeButton(it));
return stack;
```

On `_items` change, `Build()` re-runs and returns a *fresh* StackPanel
with *fresh* Buttons. Naive replacement of the parent's subtree loses
scroll, focus, and animations. The four ways out:

1. **Tear-and-rebuild** — wrong (kills UX).
2. **Diff `Build()` output against last render** — we've reinvented the
   reconciler against live UIElements.
3. **Cache per-call-site UIElement, key by item** — needs stable keys
   *and* call-site identity tracking. Solid does this with its compiler;
   we can't in vanilla C#.
4. **Smart-collection primitive** — `BoundList(_items, it => CreateButton(it))`
   owns the parent's `Children` collection and handles add / remove /
   move / update against live UIElements.

(4) is the only honest answer. P2 will need a small palette of these
primitives — `BoundList`, `BoundContent`, `BoundChildren` — that
reimplement keyed reconcile against `UIElementCollection`. **The cost
the Element tree was paying didn't vanish; it relocated into these
primitives.** Whether that's a net LoC win is one of the things the
prototype must measure.

#### P3 — Solid × Element

Solid's model **forces a structural change in user code**. The naive
form:

```csharp
return VStack(0, items.Value.Select(it => Button(() => it.Name)).ToArray());
```

…snapshots `items.Value` at mount and never updates, because `Build()`
runs once. The Solid pattern instead:

```csharp
return VStack(0,
    For(() => items, (it, index) =>
        Button(() => it.Name, () => Delete(it))));
```

`For` is a framework primitive that:

- Subscribes to `items` as a signal
- On change, diffs by reference identity (or by `key:` argument)
- Creates / disposes / moves a **per-item reactive scope** holding the
  template's bindings and any inner signals
- Reuses scopes for matched items, so their inner state survives

Per-item state is just `var editing = Signal(false)` declared *inside
the For body*, and it persists across reorders because the scope follows
the item. **Solid's `For` is the single best per-item-state ergonomic
across the four prototypes.** Event handlers are allocated once per item
ever (not per render).

The cost: `For`, `Show`, `Switch`, `Index` become framework vocabulary
the author must learn. Pure C# `.Select` / `if` / `switch` / `foreach`
either don't work or only work at mount time. **This is a real
ergonomic regression** — the language's natural collection and
control-flow operators stop being reactive, and authors have to remember
which form is allowed where. `CounterDemo` doesn't expose this; the
list ports will.

#### P4 — Solid × Direct

Same `For` primitive, but template returns `UIElement` and `For`
directly mutates the parent's `Children`. Per-item scope and per-item
`Signal` survive as in P3:

- Insert → run template → insert UIElement
- Remove → dispose scope (cleans up effects, unbinds UIElement) → remove
- Move → relocate existing UIElement

Solid+Direct and Solid+Element are **nearly identical** for this scenario
— `For` already owns the diff, so the Element tree was carrying very
little weight here. **One place where ditching the Element tree costs
almost nothing.**

### §7.2 Scenario B — templated WinUI control

WinUI owns virtualization, container recycling, and template inflation.
The `DataTemplate` in XAML is a XAML construct — you can't pass a C#
lambda to a stock `ListView.ItemTemplate`. Reactor today wraps this with
a managed templating layer (spec 047 §13).

#### Element representations (baseline / P1 / P3)

All pass `Func<T, Element>` to `TemplatedListView`. The framework
registers a managed `DataTemplate` whose factory calls back into the
template fn, walks the returned subtree, mounts WinUI controls.

- **Baseline** — template fn re-runs on every container realize;
  hooks-per-item via call order.
- **P1 Lit** — template fn could return a `ReactiveComponent` keyed by
  item; per-item state survives container recycle *if* the framework
  reuses the instance (open question — depends on whether we make
  recycling preserve component identity).
- **P3 Solid** — template fn runs once per *logical* item, not per
  container. When WinUI recycles a container, the per-item scope must be
  **re-pointed** to the new data — Solid scopes don't have a built-in
  "rebind to new item" operation. A `ScopeContext.RebindItem(newItem)`
  primitive would have to be designed. Possible, but a non-trivial design
  task; the elegant per-item-state story has to bend to fit
  virtualization.

#### Direct representations (P2 / P4)

Template fn returns `UIElement`. The managed templating layer is
**essentially unchanged** — it was already producing UIElements via the
reconciler; now the template fn produces them directly. Templated
controls don't benefit from the Element tree's diff anyway (they own
their own recycling), so **this is a place where direct representation
is roughly free**.

What both P2 and P4 lose: template-shape reuse via element-record
equality (today, two identical Element records can share descriptor
metadata). Not clearly material in practice; measure under Phase 3.

### §7.3 Cross-cutting findings

1. **The "reactive control-flow vocabulary" tax for Solid is real and
   counter-hidden.** `.Select`, `if`, `switch`, `foreach` go inert under
   Solid; authors must use `For` / `Show` / `Switch` / `Index`. The list
   port (TodoApp) must be in Phase 1, not deferred to Phase 2 — this
   tax dominates any honest ergonomic critique.

2. **Per-item state ergonomics ranking (best → worst):**
   - Solid (P3 / P4) — `Signal` inside `For` body; scope follows item.
   - Lit (P1 / P2) — lift each row to a child `ReactiveComponent`,
     framework reuses by key.
   - Baseline (today) — hooks-in-list via call order, fragile under
     reorder.

3. **The Element tree pulls *less* weight than expected for Solid
   collections.** `For` is the primitive doing the reconcile work;
   whether it operates on Element records or UIElements is largely an
   implementation detail. **P3 vs P4 may be a smaller delta than P1 vs
   P2.**

4. **Conversely, P1 vs P2 is the *bigger* delta.** Lit's "re-run
   `Render()` on any change" leans heavily on the element-record diff
   being cheap. Going direct forces either a reinvention of that diff
   against live UIElements (BoundList / BoundContent / BoundChildren)
   or tear-and-rebuild semantics that break focus / scroll / animation.

5. **Templated WinUI controls already have a reconciliation abstraction**
   (the managed DataTemplate shim) that all four prototypes share with
   minor adaptation. They are **not** the differentiator. Plain
   `.Select`-style collections are.

6. **Event-handler closures quietly favor Solid.** `() => Delete(it)`
   allocated once per item ever (in `For` template) vs once per
   render-per-item under React / Lit. For a 1000-row list updating at
   60Hz this matters; budget a benchmark for it in §10 Phase 3.

7. **Hot reload × Solid is a genuine concern.** Solid's "component body
   runs once" semantics fight hot reload — to re-execute the edited
   body, you must dispose and rebuild the entire reactive graph, losing
   all `Signal`-held state. Solid's web ecosystem accepts this;
   Reactor's iterative dev loop (specs 049, 050) currently preserves
   state across hot reload. This is captured in §11 (disqualifying-risk
   floor) and §12 (open question for the spike).

8. **Predicted finding to test, not assert:** P1 and P4 end up looking
   similarly sized in framework LoC — "pay the right cost in the right
   place" (Lit benefits from the diff for ad-hoc trees; Solid benefits
   from per-scope ownership for collections). P2 and P3 may end up as
   the worst-of-both combinations. This is a hypothesis; the experiment
   exists to test it.

### §7.4 Implications elsewhere in this spec

- **§10 Methodology Phase 1** ports three samples beyond CounterDemo: a
  list with insert / remove / reorder, a list with per-row local state,
  and a `TemplatedListView` realization. CounterDemo alone is
  insufficient to surface the divergences this section identifies.
- **§11 Disqualifying outcomes** includes a hot-reload floor that
  applies specifically to the Solid prototypes.
- **§12 Open questions** includes "vocabulary tax acceptability" and
  "WinUI virtualization × Solid scopes" as Q-V and Q-R respectively.

---

## §8 Evaluation axes & scorecard

Each prototype gets scored 1–5 on every axis, with a written justification.
Scores without justification do not count. Where an axis has a measurable
proxy (LoC, allocations, ms), use the measurement and only assign a score
from it.

### A. Developer ergonomics

| Sub-axis | What we look for |
|---|---|
| **Mental model clarity** | Can a developer who knows C# but not Reactor predict what re-runs on a state change? |
| **Stale-closure risk** | Async/event handlers that capture state — does the model surface or hide staleness? Concrete test: a 500ms delayed button handler reading state. |
| **Memoization ceremony** | Count of `UseMemo` / `UseCallback` / equivalents needed in the ported samples. Fewer = better. |
| **Conditional rendering ergonomics** | `if`/`switch` inside `Render()` — how natural is it? Does it require a special helper? |
| **Keyed list / collection ergonomics** | Mapping a list to UI — line count, ceremony, "feels like C#"? |
| **Error locality** | When something is wired wrong, does the error point at the bug or somewhere far away? |
| **Compile-time guarantees** | What classes of bug move from runtime to compile time (or vice versa)? |

### B. Agent (AI) ergonomics

This deserves explicit weighting given Reactor's emphasis on AI agent
authoring (specs 024, 037).

| Sub-axis | What we look for |
|---|---|
| **Pattern uniformity** | Does the same problem always have the same shape? LLMs predict the next token based on shape regularity. |
| **Snippet portability** | Can a snippet from one component be pasted into another with no adjustments? |
| **Recovery from typos** | If an agent omits `.Value` or misspells a signal, does the compiler catch it? Or does it silently snapshot? |
| **Search/retrieval surface** | Are there enough similar examples in the codebase for retrieval-augmented authoring to work? |
| **DSL composability under refactor** | When an agent wraps a snippet in a helper method, does the wrapping break reactivity? (Solid's lambda-everywhere model is *especially* vulnerable here — extracting a helper that takes `string` instead of `Func<string>` silently breaks the binding.) |
| **Error-message readability** | Stack traces from the framework — do they point at user code or framework internals? |

### C. Performance

Measured, not scored.

| Metric | How we measure | Target context |
|---|---|---|
| **Cold-start time-to-first-frame** | Stopwatch from `ReactorApp.Run` to first composition tick. | Ported `TodoApp` + Minesweeper. |
| **Working-set (private bytes)** | `Process.WorkingSet64` 5s after first idle. | Same. |
| **Steady-state allocations per update** | `GC.GetAllocatedBytesForCurrentThread` delta across 100 counter increments. | Counter prototype. |
| **Update latency (signal → DP write)** | High-resolution stopwatch around `setState` / `signal.Value = …` to the bound DP change firing. | All four. |
| **GC pause budget** | Gen0/Gen1 collection count over a 60s stress (1000 updates/s). | Counter + a 1000-row list. |
| **Hot-path allocation rate** | dotMemory snapshot during a sustained list-scroll. | TodoApp + Outlook mailbox sample. |
| **Binary size** | Output `.dll` size for `Microsoft.UI.Reactor.dll`. | Each prototype as its own assembly. |

### D. Maintenance / framework code size

| Metric | How we measure |
|---|---|
| **Framework LoC** | Total non-blank, non-comment lines in `src/Reactor/Core/` for each prototype. |
| **Public API surface** | Count of public types and members exposed (via PublicApi tracker). |
| **Adding a new WinUI control** | Concrete diff to add `MenuFlyout` to each prototype. Lines + files touched. |
| **Reconciler complexity** | Cyclomatic complexity of the reconciler module (where one exists). |
| **Number of partial-class branches** | Today's `Mount.cs` / `Update.cs` per-control switch arms. How many remain in each prototype? |

### E. Extensibility

| Sub-axis | What we look for |
|---|---|
| **Third-party control authoring** | Steps to ship a custom control from an external NuGet package. (Today: see spec 048 Pattern A.) |
| **Custom modifier authoring** | Can a consumer add a fluent modifier without touching framework code? |
| **Composable behaviors** | Equivalent of custom hooks — how easy is reuse? (Lit's reactive controllers vs Solid's primitive composition vs React-style hooks.) |
| **Decorator-style wrapping** | `Button` wraps `ButtonBase` etc. — does the model support layered control authoring? |
| **Trim/AOT impact** | Does the model add reflection or codegen that breaks trimming? |

### F. Migration cost

| Sub-axis | What we look for |
|---|---|
| **Effort to port `samples/apps/minesweeper`** | Person-hours. |
| **Effort to port `samples/Reactor.TestApp/Demos/TodoDemo`** | Person-hours. |
| **Test suite re-cost** | What fraction of `Reactor.Tests` (~2200 tests) survives unchanged vs needs rewriting vs becomes selftest? |
| **Docs re-cost** | Pages under `docs/guide/` and `docs/_pipeline/templates/` that need rewrites. |
| **Hot reload story** | Does `dotnet watch` still iterate in < 2s on a property edit? |

### G. Runtime properties (non-perf)

| Sub-axis | What we look for |
|---|---|
| **Echo suppression / controlled values** | Spec 047 §8.3 was hard-won. Does each prototype need to reinvent it? How? |
| **Two-way binding ergonomics** | TextBox / Slider / Toggle — line count to wire two-way. |
| **Async / data fetching** | Spec 020 (async resources). Does the model break or improve current `UseAsync` patterns? |
| **Diagnostics** | Can DevTools (specs 024, 028) still inspect the live tree and render counts? |
| **Threading model** | Where is UI-thread affinity enforced? Any new gotchas? |

### Scorecard template

```
Prototype: P{1|2|3|4}

A. Developer ergonomics
   Mental model clarity:           [1–5]  — <one sentence>
   Stale-closure risk:             [1–5]  — <one sentence>
   Memoization ceremony:           [1–5]  — <count from samples>
   Conditional rendering:          [1–5]  — <example>
   Keyed list ergonomics:          [1–5]  — <example>
   Error locality:                 [1–5]  — <example>
   Compile-time guarantees:        [1–5]  — <example>

B. Agent ergonomics
   Pattern uniformity:             [1–5]
   Snippet portability:            [1–5]
   Typo recovery:                  [1–5]
   Search/retrieval surface:       [1–5]
   DSL composability:              [1–5]
   Error-message readability:      [1–5]

C. Performance (measurements, not scores)
   TTFF (ms):                      <measured>
   Working set (MB):               <measured>
   Bytes/update:                   <measured>
   Update latency (µs):            <measured>
   GC gen0/min @ 1000 updates/s:   <measured>
   Hot-path allocs/scroll:         <measured>
   Binary size (KB):               <measured>

D. Maintenance
   Framework LoC:                  <measured>
   Public API count:               <measured>
   "Add MenuFlyout" diff:          <lines, files>
   Reconciler cyclomatic:          <measured>

E. Extensibility:                  [1–5 per sub-axis]
F. Migration cost:                 <person-hours, %-of-tests-survives>
G. Runtime properties:             [1–5 per sub-axis]

Overall recommendation: <one paragraph>
Known unknowns: <list>
```

### Weighting (proposed)

Not all axes carry equal weight in the final recommendation. Proposed
weighting, to be confirmed before scoring begins:

| Axis | Weight | Rationale |
|---|---|---|
| Developer ergonomics | 25% | Primary customer. |
| Agent ergonomics | 20% | Reactor's strategic bet on AI authoring. |
| Performance | 15% | Floor, not ceiling — must not regress; small wins don't decide. |
| Maintenance (LoC, surface) | 20% | The "remove the parallel hierarchy" win lives here. |
| Extensibility | 10% | Third-party control authors (spec 048) matter. |
| Migration cost | 10% | One-time cost, but real. |
| Runtime properties (echo, async, diag) | weight bundled with perf/ergonomics | Disqualifying if broken. |

### §8.1 Initial measured results — P1 and P3

Two Element-retained prototypes now have local StocksGrid stress results:

- **P1 Lit × Element / dependency-tracked Element bindings** retained Reactor's
  Element representation but proved a more fine-grained scalar-binding shape
  than the original "whole component re-renders on property change" P1 sketch
  in §6: `Build()` runs once, binding lambdas subscribe to the signals they
  read, and later signal writes update only those bindings.
- **P3 Solid × Element** retained Reactor's Element representation and used
  mount-once signals/effects for bound slots, avoiding per-tick virtual
  render/reconcile work in the measured value-update path.

The numbers below are **initial smoke measurements**, not final scorecards.
Both runs used x64 Release, the 70×70 StocksGrid stress workload, 10-second
duration, update percentages 10/50/100, and ReactorOptimized as the nearest
fair baseline. The P1 and P3 runs happened in different sessions, so compare
each prototype against its paired ReactorOptimized row rather than comparing
P1 and P3 FPS directly. In-app FPS / render-like counters are not ETW Present
rate. TTFF/mount, Gen0/min, and bytes/update were not measured comparably.

| Update % | P1 FPS vs paired ReactorOptimized | P1 render/update proxy | P3 FPS vs paired ReactorOptimized | P3 combined managed cost vs paired ReactorOptimized |
|---:|---:|---:|---:|---:|
| 10% | **21.9** vs 17.1 (+28%) | **10.52/sec** vs 7.41/sec (+42%) | **22.7** vs 19.7 (+15%) | **3.5 ms** vs 33.5 ms (9.6× lower) |
| 50% | **7.7** vs 7.3 (+5%) | **3.62/sec** vs 3.01/sec (+20%) | **8.2** vs 6.6 (+24%) | **15.0 ms** vs 75.3 ms (5.0× lower) |
| 100% | **5.1** vs 4.7 (+9%) | **2.37/sec** vs 1.90/sec (+25%) | **5.8** vs 4.8 (+21%) | **23.1 ms** vs 107.3 ms (4.6× lower) |

The shared read is that **fine-grained scalar reactivity works in this
value-heavy workload**. Both prototypes update work proportional to unique
changed cells rather than rebuilding and reconciling the whole 4,900-cell
tree. P1 proves the lower-risk Element-retained scalar-binding path can beat
ReactorOptimized on the available in-app smoke metrics. P3 shows the stronger
managed-cost result: its per-tick combined cost is 4.6×–9.6× lower than
ReactorOptimized, and the latest P3 report recorded zero component builds,
zero native mounts, and zero full-grid refresh fallbacks during measured ticks.

The binding-work counters also line up across the two spikes:

| Update % | Unique changed cells/tick | P1 recomputes/tick | P1 estimated DP sets/tick | P3 signal writes/tick | P3 effect runs/tick | P3 DP writes/tick |
|---:|---:|---:|---:|---:|---:|---:|
| 10% | ~466 | 466.1 | 932.2 | 490.0 | 932.2 | 698.2 |
| 50% | ~1,927 | 1,926.3 | 3,852.5 | 2,450.0 | 3,853.6 | 2,884.4 |
| 100% | ~3,096 | 3,096.0 | 6,192.0 | 4,900.0 | 6,192.0 | 4,641.2 |

At 100%, unique changed cells are below 4,900 because the stress data source
samples random indices with replacement. P3 reports more signal writes than
unique cells because every configured update write still hits a cell signal,
but downstream binding effects and DP writes collapse to the actual dependent
slots that change.

What these results do **not** decide:

- They do not test the direct-UIElement representation axis (P2/P4), so the
  "remove the parallel hierarchy" maintenance hypothesis remains open.
- They do not prove structural-list ergonomics. P1 still needs a scoped
  structural region for insert/remove/reorder, and P3 still requires
  `For`/`When`-style reactive control-flow instead of ordinary C# `.Select`
  when structure must update after mount.
- They do not close production-runtime gaps: ETW Present rate, TTFF/mount,
  Gen0/min, bytes/update, hot reload, DevTools inspection, and production
  echo-suppression parity remain required before recommendation.

Interim implication: **continue the fine-grained reactivity line of
investigation**. The measured evidence is already strong enough to retire the
idea that signal/binding machinery is merely theoretical; the next gating
question is whether the same win survives structural UI, virtualization,
hot reload, and service/hook replacement without unacceptable authoring cost.

---

## §9 Performance hypothesis

This section is the original **best-informed pre-measurement hypothesis**,
kept as the falsifiable baseline for the experiment. The initial P1/P3
measurements in §8.1 partially validate the fine-grained value-update bet, but
they do not yet cover cold start, GC, hot reload, structural UI, virtualization,
or the direct-UIElement representation axis.

The hypothesis folds in the architectural facts established in §3–§7:

- **Element record allocation** per render (~64 B per node; baseline
  re-allocates on every render of a re-rendering subtree; P2/P4 skip
  these entirely).
- **WinUI control instantiation cost** is 10–100× heavier than an
  element record per node. A `Microsoft.UI.Xaml.Controls.Button` is
  ~5–10 KB with dozens of DependencyProperty backings and a template
  inflation; a `ButtonElement` record is ~64 B. **This asymmetry
  dominates many of the numbers below.**
- **Lambda allocation patterns**: Lit re-allocates per render (one
  closure per event-handler call site, per render); Solid allocates
  once at mount and freezes — every reactive prop is a long-lived
  delegate captured in an effect.
- **Reconciler tree walks** — baseline, P1, P3 pay; P2 and P4 don't.
- **Effect-subscription graph** — P3 / P4 pay setup cost at mount
  (one subscription object per reactive prop slot, ~80 B);
  amortized benefit at steady state.
- **WinRT marshaling per DP write** is constant across prototypes for
  any given set of changed properties. What varies is the *managed-side
  overhead between writes* — render + diff vs. direct effect dispatch.

All numbers below are order-of-magnitude estimates for a typical desktop
LOB workload (TodoApp / Minesweeper scale), not benchmark commitments.

### §9.1 Cold start (time-to-first-frame)

For a ~10,000-element-node app with ~200 UIElement instances. WinUI
control inflation dominates total cold-start cost in every prototype;
prototype-specific deltas are second-order.

| Rank | Prototype | Δ vs baseline | Reason |
|---|---|---|---|
| 1 | **P2 (Lit × Direct)** | **5–15% faster** | Skips element-record allocation pass and the first-render diff. |
| 2 | **Baseline ≈ P1 (Lit × Element)** | reference | P1 inherits the baseline mount path; SignalField setup is microseconds. |
| 3 | **P4 (Solid × Direct)** | **5–10% slower** | UIElement materialization + effect-graph setup per reactive prop. |
| 4 | **P3 (Solid × Element)** | **5–15% slower** | Element-record allocation *and* effect-graph setup at mount — pays both costs. |

Baseline cold-start for Minesweeper is currently in the ~80ms range on
a modern desktop; the four prototypes likely fall within a ~70–95ms
band. **None of the prototypes radically change cold-start** because
WinUI control inflation is the dominant cost in every case.

### §9.2 Steady-state update cost — the headline number

For a single state change that affects one bound DP — the pathological
"tiny change in a big tree" case where reactive architecture differences
compound:

| Rank | Prototype | Δ vs baseline | Reason |
|---|---|---|---|
| 1 | **P3 / P4 (Solid)** | **5–20× faster** | Signal change fires one effect, one DP write. No render, no diff, no allocation. |
| 2 | **P1 (Lit × Element)** — well-decomposed | **2–5× faster** | Per-component re-render scope means only the state-owning component allocates and diffs. |
| 3 | **P1 — monolithic** ≈ **Baseline** | reference | Whole-component re-render whether or not state matters. |
| 4 | **P2 (Lit × Direct)** | **2× faster to 5× slower** | Entirely depends on smart-collection primitive design — the **single largest perf uncertainty** in this experiment. |

The 5–20× spread for Solid is wide because the multiplier scales with
tree size — bigger tree → bigger win for skipping the diff. For an app
making 100+ state changes per second (charting, animation, real-time
data), this compounds into qualitatively different application classes,
not just a percentage win.

**P2's range is the experiment's biggest performance question.** If
BoundList / BoundContent primitives preserve UIElement identity well,
P2 is comparable to baseline. If they don't, P2 silently reallocates
WinUI controls on every update — and at 5–10 KB per `Button`, this
dwarfs every other allocation source.

### §9.3 Allocations per update

Order-of-magnitude estimates for "click increment in CounterDemo":

| Prototype | Element records | Lambda closures | UIElement allocs | Total transient |
|---|---|---|---|---|
| Baseline | ~5 | ~3 (event handlers) | 0 (reconciler reuses) | ~600 B |
| P1 | ~5 | ~3 | 0 | ~600 B |
| P2 | 0 | ~3 (re-execute `Build`) | 0 *if* smart primitive caches | ~200 B |
| P3 | 0 | 0 (captured at mount) | 0 | ~50 B (int box only) |
| P4 | 0 | 0 | 0 | ~50 B |

For "insert one row into a 100-row list" (a structural change, not a
value change):

| Prototype | Transient allocations |
|---|---|
| Baseline | ~100 fresh element records for unchanged rows + diff. ~6 KB. |
| P1 | 1 new child component (long-lived) + ~64 B per surrounding element. |
| P2 | 1 new UIElement subtree via BoundList. ~5 KB. |
| P3 | `For` template runs once for new item; scope + UIElement subtree. ~5 KB. |
| P4 | Same as P3 minus element-record overhead. ~5 KB. |

UIElement allocations dominate structural inserts in all prototypes —
WinUI controls have to come from somewhere, and that's the lion's
share of the budget. **Allocation savings are concentrated in the
"value updated, structure unchanged" path** — and that's the case
where Solid's diff-skipping wins.

### §9.4 Working set (sustained memory, idle 1000-item TodoApp)

| Prototype | Notable additions vs baseline | Estimated delta |
|---|---|---|
| Baseline | reference | — |
| P1 | `SignalField<T>` wrappers, ReactiveComponent host bookkeeping | +0.5–1% |
| P2 | Wrappers + smart-primitive metadata per BoundList | +1–2% |
| P3 | Wrappers + effect subscription per reactive prop slot | +5–15% |
| P4 | Same as P3 minus element-record retention | +3–10% |

For a tree with ~10,000 reactive prop slots, ~800 KB of subscription
objects is a reasonable estimate. **A real but modest delta** — not
disqualifying, but worth tracking. The Solid prototypes pay this
because every "bound slot" is a long-lived effect object that
subscribes to its source signals; baseline tosses equivalent state
between renders and lets GC reclaim.

### §9.5 GC pressure and pause budget

Gen-0 collections per minute under sustained 1000 updates/sec on a
single bound int (worst-case GC pressure scenario):

| Prototype | Est. Gen-0 rate | Notes |
|---|---|---|
| Baseline | 50–200/min | Element + closure churn per render |
| P1 | 50–200/min | Same churn pattern, possibly less if state-owning component is small |
| P2 | 0–500/min | Depends entirely on smart-primitive caching |
| P3 | 0–20/min | Only the int boxing on signal change |
| P4 | 0–20/min | Same |

**P3 and P4 enable workloads that are simply infeasible under
baseline** without aggressive manual memoization — sustained
1000-update/sec without GC pause hitches. This opens application
classes (real-time charts, audio-rate animation, live diagnostics
dashboards) that the React-style model effectively rules out.

### §9.6 Hot-path: virtualized list scrolling (60fps)

WinUI virtualization keeps ~20 containers alive; the template fn is
invoked per recycle. Per-frame cost for a smooth-scroll workload:

| Prototype | Per-recycle cost | Notes |
|---|---|---|
| Baseline | ~50 µs | Template → Element subtree → reconciler diff → DP writes |
| P1 | ~50 µs | Identical to baseline |
| P2 | ~30 µs (cached) to ~500 µs (naive) | **Largest variance** — naive Build allocates UIElements per recycle, defeating virtualization |
| P3 | ~5–10 µs | *Requires* `ScopeContext.RebindItem` (Q-R); if implemented, scope rebind triggers effects → DP writes only |
| P4 | ~5–10 µs | Same; doesn't pay the element-record cost |

Solid prototypes have the largest theoretical win but the largest
design risk — Q-R must be solved. If we can't make scope-rebind work,
disposing and recreating per recycle would regress badly. **The Phase-1
templated-list sample is the gating test for Q-R.**

### §9.7 Binary size (framework IL)

Reactor.dll today is dominated by Yoga + element-record + reconciler
infrastructure. Framework code itself is ~150 KB of IL (rough estimate
from the current source tree).

| Prototype | Framework IL delta | Net direction |
|---|---|---|
| P1 | +5–10 KB (SignalField, ReactiveComponent, controllers) | Slight growth |
| P2 | −30–50 KB (reconciler, mount/update handlers) +15 KB (smart primitives) | **Net −20–35 KB** |
| P3 | +15–25 KB (Signal, effect, `For`/`Show`/`Switch`) | Modest growth |
| P4 | −30–50 KB (reconciler, element records) +25 KB (Signal infra + primitives) | **Net −10–25 KB** |

The Direct-UIElement prototypes deliver a meaningful binary-size
reduction; the Lit×Element variant pays the most because it inherits
existing infrastructure and adds its own. This is the user-visible
"removing the parallel hierarchy is a huge win" payoff in concrete
terms — but it's measured in tens of KB, not MB. The binary-size
argument is best framed as *maintenance surface*, not *download size*.

### §9.8 Net hypothesis — aggregate ranking

Ranked by likely aggregate performance for a typical desktop LOB app,
weighting cold-start + steady-state + working-set roughly equally:

1. **P4 (Solid × Direct)** — best steady-state, smallest binary,
   modest cold-start cost, modest working-set cost. The "everything is
   paid for at mount" tax is real but acceptable for long-lived desktop
   apps. **Highest-ceiling prototype.**
2. **P2 (Lit × Direct)** — best cold-start, comparable steady-state to
   baseline *if* smart primitives work as designed; worst-case if they
   don't. **Highest-variance prototype.**
3. **P3 (Solid × Element)** — same steady-state wins as P4, but pays
   element-record alloc *and* effect graph at mount. Per §7.3 finding
   #3, the Element tree carries little value under Solid; **P4
   dominates P3 on perf grounds** unless the Element tree turns out
   load-bearing for something we haven't identified.
4. **Baseline ≈ P1 (Lit × Element)** — most stable, fewest surprises,
   no new infrastructure. P1's per-component re-render is a small win
   over baseline but doesn't move the macro picture.

### §9.9 What would falsify this hypothesis

Treat each of these as a Phase-3 check that would force a re-think:

- **P2 with naive `Build()` doesn't catastrophically allocate.** If
  WinUI's own internal recycling, or our reconciler's pooling moved to
  the smart-primitive layer, catches the case better than expected,
  the worst-case scenario doesn't materialize — and P2 becomes more
  attractive.
- **Effect-graph setup cost in P3 / P4 is inside measurement noise.**
  If WinUI control inflation dominates so completely that the
  predicted 5–15% cold-start regression is invisible, the cold-start
  ranking flattens.
- **Real apps update state in batches.** If state changes naturally
  cluster (e.g., one user action triggers 10 related updates),
  baseline's "diff once per batch" amortizes well and Solid's
  steady-state win shrinks toward 2–3× instead of 5–20×.
- **Working-set delta for Solid effect graph exceeds 30%.** That
  would shift the calculus toward Lit prototypes regardless of
  steady-state wins.
- **Hot reload genuinely costs P3 / P4 their per-component state.**
  Per §11, this is disqualifying regardless of how good steady-state
  perf is — no amount of update-latency wins is worth losing the
  iterative dev loop.

### §9.10 The two biggest perf bets

If the experiment confirms two things, the perf case for migrating is
decisively made:

1. **Solid's elimination of the diff walk for value-only updates is
   real at scale** — translating "5–20× faster updates" from
   benchmark to user-perceptible smoothness on a Minesweeper / TodoApp
   workload. This would mean Reactor could address application classes
   it currently can't (real-time, high-frequency-update UIs).
2. **The direct-UIElement representation costs us nothing in practice
   that smart-collection primitives can't recover.** This is the
   architectural simplification — removing ~80 element records, ~1400
   lines of fluent modifiers, and an entire reconciler — that pays the
   long-tail maintenance and extensibility costs.

P4 is the prototype that wins both bets. P3 wins one. P2 might win the
second. P1 wins neither but loses nothing.

---

## §10 Methodology — what to build, what to measure

### Phase 1 — Spike each prototype (1–2 weeks each)

Goal: prove the model can render and update at all, and surface the
divergences §7 identifies *before* committing further. Score ergonomic
axes (A, B) and capture initial perf numbers (C).

Each prototype should be isolated as a self-contained sketch. Do **not** modify
`src/Reactor/`. Each prototype may copy whatever subset of the current
reconciler / element records it wants (or none of them) — the point is to see
the irreducible shape.

Phase 1 ports **all** of these (not just CounterDemo — §7 explains why
counter alone hides the most important questions):

1. **CounterDemo** — scalar state baseline.
2. **List with insert / remove / reorder** — exposes the `For` vs
   `.Select` divergence and the BoundList primitive in P2.
3. **List with per-row local state** (e.g., a TodoApp row that can be
   inline-edited) — exposes child-component identity in Lit prototypes
   vs scope-per-item in Solid prototypes.
4. **A `TemplatedListView` realization** — exposes the
   container-recycling × Solid-scope question (§7.2) and the managed-
   templating layer.

### Phase 2 — Port a real sample (2 weeks each)

Goal: see what happens at scale. Each prototype ports **the same** sample
slice:

1. `samples/Reactor.TestApp/Demos/TodoDemo.cs` (or current equivalent)
2. `samples/apps/minesweeper/` (the hardest of the realistic ones — has
   conditional rendering, keyed lists, animations, and a non-trivial state
   model)

Score axes D, E, F. Re-measure C with realistic UI.

### Phase 3 — Stress test (1 week)

Goal: surface the things that only break under load.

- 1000-row virtualized list with item updates at 60Hz.
- Counter at 1000 updates/sec for 60s — GC pressure.
- Hot reload — measure edit-to-window-update latency.
- Trim/AOT build of each prototype.

### Phase 4 — Writeup & recommendation (1 week)

A summary doc (sibling to this spec) with the filled scorecards, weighted
totals, and a written recommendation. The recommendation may be:

- **Stay** on the current model and document why we evaluated alternatives.
- **Migrate** to one of the four (with a phased rollout spec).
- **Hybrid** — adopt the reactivity model of one prototype with the
  representation of another (e.g., Lit-style reactive properties + retain
  Element tree, or Solid-style signals + go direct).
- **Inconclusive** — identify what we'd need to know to decide.

---

## §11 Disqualifying outcomes

A prototype is **disqualified** (not merely down-scored) if any of these
prove true under the Phase 2 port:

1. **Echo handling regresses.** Controlled inputs (TextBox, Slider, ComboBox)
   produce visible jitter, lost keystrokes, or oscillation under realistic
   typing speed.
2. **Hot reload roundtrip > 5s** on a single-property edit. We've invested
   too much in the iterative loop to lose it (specs 049, 050).
3. **Hot reload loses component-local state across an edit.** This applies
   specifically to the Solid prototypes (P3, P4) — Solid's "body runs
   once at mount" model fights hot reload, and the Solid web ecosystem
   accepts state loss across hot reload as a tradeoff. Reactor today
   preserves state; dropping that floor is disqualifying unless the
   spike demonstrates a credible recovery mechanism (e.g., scope
   serialization, snapshot-and-restore, or a marker that protects
   selected signals).
4. **Trim/AOT publish fails** without code changes per-app. Spec 051 set
   that bar; we won't drop below it.
5. **DevTools cannot inspect the live tree.** Specs 024, 028 depend on a
   walkable model.
6. **Update latency regresses > 2× vs current model** for any of the
   measured workloads.

Any of these can be revisited if a prototype is otherwise compelling, but
the burden of proof shifts to the prototype.

---

## §12 Open questions

1. **Does Solid-style "lambda-wrap every reactive read" survive contact
   with real C# code?** This is the single biggest open question. The Solid
   compiler hides it; we'd have to live with it. If `() => x.Value`
   shows up at every reactive prop position, does the call site become
   noisier than the React baseline?
2. **Q-N: Implicit-conversion asymmetry between the two reactivity
   models.** P1 / P2 use `SignalField<T>` *with* `implicit operator T`
   because reads inside `Render()` are always at the "whole component re-
   runs on change" granularity — implicit conversion safely snapshots the
   current value. P3 / P4 use `Signal<T>` *without* the implicit operator
   because the framework needs to distinguish snapshot-at-mount (`T`
   overload) from subscribe-and-update (`Func<T>` / `Signal<T>` overload);
   a silent conversion would route the wrong way and break reactivity with
   no error.

   Sub-questions the spike must answer:
   - Confirm the trap is real by deliberately writing the buggy form in P3 /
     P4 with a hypothetical implicit operator and demonstrating the silent
     failure mode. Document it.
   - Test ergonomic feel: does the write-side asymmetry in P1 / P2
     (`_count.Value = 5` vs the implicit-read `_count`) feel inconsistent
     enough that a `Set(value)` / `Set(Func<T,T>)` method ends up preferred,
     so authors never type `.Value` directly?
   - Decide whether the two prototype families should share the same
     wrapper type (and accept one model's compromise) or stay as two
     distinct types (`SignalField<T>` for Lit-style, `Signal<T>` for
     Solid-style). Different types is honest but increases conceptual
     surface; same type with two semantics is invitation to bugs.

3. **Q-V: Is the Solid "reactive control-flow vocabulary" tax
   acceptable?** Under P3 / P4, ordinary C# `.Select` / `if` / `switch`
   / `foreach` become inert at update time; authors must use framework
   primitives (`For`, `Show`, `Switch`, `Index`). This is a genuine
   ergonomic regression — the language's natural operators stop being
   reactive in this one context, and authors have to remember which form
   is allowed where (mount-time vs reactive). Whether this is acceptable
   is judged on the Phase 1 list ports (§10). If authors / agents
   consistently reach for `.Select` and get silently-non-updating UIs,
   that's a strong signal against P3 / P4 regardless of other wins.
4. **Q-R: How do Solid per-item reactive scopes interact with WinUI
   container recycling?** (§7.2.) WinUI ListView / TreeView / TabView
   recycle containers under virtualization, but Solid's scope model
   assumes one scope per logical item, not per container. Possible
   designs: dispose+recreate scope per recycle (loses per-item Signal
   state on scroll), or design a `ScopeContext.RebindItem(newItem)`
   primitive that re-targets bindings without disposing. The latter is
   the right answer but a non-trivial design task. Required output of
   the Phase 1 templated-list sample.
5. **Q-K: Lit-style keyed component identity for collections.** (§7.1.)
   `_items.Select(it => new TodoRow { Item = it })` must — somehow —
   match `(componentType, key)` → previous instance across re-renders
   so per-row state (`_editing`, scroll position, etc.) survives. This
   is React's `key=` problem with long-lived component instances. We
   already have spec-042 keyed reconcile to lean on; the open question
   is the API surface (do authors write `.Key(it.Id)` as today, or does
   the framework infer keys from the iteration?).
6. **Does removing the Element tree force us to re-add pooling on top?**
   For controls whose template inflation is genuinely expensive
   (TabView, RichEditBox, the ContentControl family), pooling pays for
   itself. Direct construction doesn't preclude pooling, but it doesn't
   come for free either.
7. **How does each prototype handle the `IDecoratorElementHandler` family?**
   Button / CheckBox / Canvas / CommandBar — controls that today wrap a
   child element. Direct UIElement might make this easier (just compose) or
   harder (lose the descriptor metadata).
8. **Lit-style instance properties + nested components.** What's the parent's
   relationship to child component instances? Does the parent re-render
   create new child instances, or reuse them? (Lit answers "reuse, because
   custom elements are persistent in the DOM" — we don't have that anchor.)
9. **Two-way binding under the direct-UIElement model.** Today's
   `WriteSuppressed` primitive is tightly coupled to mount/update paths.
   Where does it live without those?
10. **Naming.** If we keep "Reactor" and ship a different model, what do we
    call the old one? Versioning story matters for adoption.

---

## §13 Out of scope

- Replacing the factory DSL (`VStack`, `Button`, fluent modifiers). Call-
  site syntax should look similar across prototypes so we're comparing the
  model, not the surface.
- Replacing the YOGA Flexbox engine, the docking system, animation, or any
  other subsystem orthogonal to reactivity & representation.
- Multi-language support (TS / F# / VB). Spec 018 covers that separately;
  the reactivity model choice can interact with it, but isn't gated on it.
- A formal RFC process for the eventual migration. If we decide to migrate,
  that gets its own spec.
- A modified C# compiler or any DSL that requires one. Per the discussion,
  vanilla C# is a hard constraint.
- **Source generators that change the runtime meaning of user-written
  declarations** (e.g., `[ReactiveProperty]` on a `partial` property where
  the generator emits a setter that calls into the framework). Prototype
  snippets in §6 use only plain C# patterns — wrapper fields or explicit
  setters. [Appendix A](#appendix-a--what-source-generators-and-language-changes-could-buy)
  collects what could be done if we relaxed this constraint, so the cost
  of *not* using those tools is explicit.

---

## Appendix A — What source generators and language changes could buy

The prototype snippets in §6 deliberately use only patterns that work in
unaided vanilla C#: wrapper fields with a `.Value` accessor, or explicit
backing fields with hand-written setters. This appendix is the honest
inventory of what additional tooling could buy us, and what each option
costs, so the decision to *not* use them in the main prototypes is
informed.

### The categorical line being drawn

Reactor's existing source generator (`Reactor.Localization.Generator`)
takes *external* input (resource files) and produces *new* strongly-typed
accessors. It doesn't change what existing user-written C# code means at
runtime. Reading the user's `.cs` file in isolation tells you the whole
story.

The generators considered below cross that line: they change what an
existing user declaration *means* once the code runs. The user writes
`public int Count { get; set; }` and at runtime `Count` has different
behavior than the source suggests. That is qualitatively different
infrastructure even though both are technically "Roslyn source generators"
within the vanilla-C# rule.

The spec deliberately keeps this in the appendix to flag: *if* the
ergonomic gain is large, we can adopt it later — but the prototypes
must stand on their own without it, so the cost of the wrapper-field
ergonomic is honestly priced.

### Option A1 — `[ReactiveProperty]` generator for P1 / P2

What it looks like at the call site:

```csharp
partial class CounterDemo : ReactiveComponent
{
    [ReactiveProperty] public partial int Count { get; set; }
    [ReactiveProperty] public partial int Step  { get; set; } = 1;

    public override Element Render() =>
        VStack(12,
            SubHeading($"Count: {Count}"),
            Button($"+ {Step}", () => Count += Step));
}
```

What the generator emits into a companion `CounterDemo.g.cs`:

```csharp
partial class CounterDemo
{
    private int _Count_backing;
    public partial int Count
    {
        get => _Count_backing;
        set
        {
            if (EqualityComparer<int>.Default.Equals(_Count_backing, value)) return;
            _Count_backing = value;
            RequestUpdate(nameof(Count));
        }
    }
    // ... same for Step
}
```

Mechanically requires C# 13's **partial properties** feature, which is
shipped. The generator is bounded — it only fills in `partial` declarations
the author has explicitly opted in to.

**Buys:** clean `Count = 5` syntax; no `.Value` noise; properties show up
in DevTools as ordinary CLR properties; data-binding interop is free.

**Costs:**
- The generator is now load-bearing — without it, the project doesn't
  compile (the partial property has no implementation).
- Debugging steps into generated code. Source-link / `[GeneratedCode]`
  attributes help but don't eliminate this.
- The runtime semantics of `Count = 5` are no longer obvious from the
  user-visible source — the framework setter does work the user can't see.
- Reflection-based property enumeration (e.g., DevTools, serializers) sees
  ordinary CLR properties; this is a feature, not a bug, but it means the
  framework can't rely on field-based interception.
- A second tool (like an XML doc-comment extractor or a different
  Roslyn-based tool) has to be able to round-trip the partial declaration —
  source-generator interactions can be subtle.

**When to revisit:** if Phase-2 ports show that `.Value` at every read
site dominates the ergonomic critique of P1/P2, this is the obvious
next step.

### Option A2 — overload-selection generator for Solid-style P3 / P4

The Solid prototypes pay an ergonomic tax at every reactive prop: instead
of `Button($"+ {step}", ...)`, the author writes
`Button(() => $"+ {step.Value}", ...)`. Each factory has both `T` and
`Func<T>` overloads, and the author has to pick.

A generator *could* in principle look at the call site and rewrite
`Button(some.Value, ...)` (which would snapshot) into
`Button(() => some.Value, ...)` (which would subscribe). But:

- **C# source generators cannot modify call sites.** They can only emit
  new code. The "rewrite the user's call" model would require either
  **interceptors** (C# 12 preview feature, still experimental, and exactly
  the kind of "make existing code mean something different" surgery that
  is being deliberately deferred) or an IL post-processor.
- The most a generator can do without interceptors is **synthesize
  per-component helper methods** that accept the signal type and forward
  to the right overload — useful but limited.

A more honest framing: design factory overloads such that **the type
system picks the right binding semantics**. If `Button` accepts
`Signal<string>` directly (not just `string` or `Func<string>`), then
`Button(buttonText, ...)` works without any wrapper.

```csharp
// Factory has three overloads:
public static ButtonElement Button(string text, Action onClick);
public static ButtonElement Button(Func<string> text, Action onClick);
public static ButtonElement Button(Signal<string> text, Action onClick);

// Author code with no lambdas:
Button(label, () => count.Value += step.Value);
// where `label` is Signal<string> derived from count/step
```

This is no longer Solid-pure (you're back to passing *the signal*, not
*reading it*) but it sidesteps the lambda-everywhere tax. Worth a Phase-1
spike on its own.

### Option A3 — language features that would change the calculus

Not part of this experiment, but worth listing so we know what we'd ask
the C# team for if the prototypes show one of these would unblock a
materially better design:

- **Property accessor interceptors.** Today's interceptors target method
  calls. A property-accessor interceptor would let a generator inject
  setter behavior on plain auto-properties without requiring the partial
  declaration. Cleaner but more invasive.
- **`observable` modifier.** Hypothetical first-class language support for
  "this property setter raises a change notification." Would obviate
  `[ReactiveProperty]` and INPC ceremony across the .NET ecosystem.
- **Implicit lambda conversion at call sites.** A language rule that, in
  the right context, converted `count.Value` (where `count` is `Signal<T>`)
  into `() => count.Value` automatically. Powerful but global — invisible
  side effects across all C# code, not just Reactor's.
- **Tagged template literals.** Would let us match Lit's
  `` html`<button>${count}</button>` `` ergonomic for direct-UIElement
  prototypes. C# doesn't have these; interpolated string handlers
  (C# 10+) cover part of the space.

### Decision rule for this experiment

Phase 1 and Phase 2 use **only** the vanilla-C# patterns shown in §6 (wrapper
fields, explicit setters). If — and only if — those phases produce a
prototype whose ergonomic critique is *specifically* "the `.Value` /
lambda-wrap noise is the dominant pain point," Phase 4 considers Appendix
A options as a follow-up. We do not let a hypothetical future generator
mask a real ergonomic problem in the prototype.

---

## Appendix B — Prototype syntax and hook mappings

This appendix captures the authoring shapes proven by the two measured
Element-retained spikes. These are **syntax snapshots**, not final API
proposals. They are included so the spec preserves the learning even if the
prototype code is deleted.

### B.1 P1 Lit × Element / dependency-tracked binding syntax

The measured P1 spike kept Reactor's Element representation but shifted scalar
updates to dependency-tracked bindings:

```csharp
Build() runs once -> binding lambdas track Signal reads -> signal writes
re-run only dependent bindings.
```

The important ergonomic rule is that reactive values must be read inside a
deferred getter:

```csharp
Lit.TextBlock(() => $"Count: {_count.Value}")
```

not:

```csharp
TextBlock($"Count: {_count.Value}") // snapshots before the tracker can see it
```

#### Simple counter

```csharp
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Lit;
using static Microsoft.UI.Reactor.Factories;
using Lit = Microsoft.UI.Reactor.Lit.Bindings;

class CounterApp : ReactiveComponent
{
    private readonly Signal<int> _count;

    public CounterApp()
    {
        _count = Signal(0);
    }

    protected override Element Build()
    {
        return VStack(
            Lit.TextBlock(() => $"Count: {_count.Value}")
                .FontSize(24),

            HStack(8,
                Button("-", () => _count.Set(c => c - 1)),
                Button("+", () => _count.Set(c => c + 1))
            )
        ).Padding(16);
    }
}
```

When `+` is clicked, only the `TextBlock` binding that read `_count.Value`
re-runs and writes the native `TextBlock.Text` property. `Build()` does not run
again, and there is no whole-component diff.

#### Projected list with per-row updates

```csharp
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Lit;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using Lit = Microsoft.UI.Reactor.Lit.Bindings;

record Todo(int Id, string Title, bool Done);
record TodoRow(int Id, string Title, string StatusText, bool Visible);

class TodoListApp : ReactiveComponent
{
    private readonly Signal<Todo>[] _todos;
    private readonly Signal<string> _filter;

    public TodoListApp()
    {
        _todos =
        [
            Signal(new Todo(1, "Write prototype", true)),
            Signal(new Todo(2, "Run perf sweep", true)),
            Signal(new Todo(3, "Review ergonomics", false)),
        ];

        _filter = Signal("");
    }

    protected override Element Build()
    {
        return VStack(
            HStack(8,
                Button("All", () => _filter.Set("")),
                Button("Open", () => _filter.Set("open")),
                Button("Done", () => _filter.Set("done"))
            ),

            VStack(_todos.Select((todo, index) => TodoRowElement(todo, index)).ToArray())
        ).Padding(16);
    }

    private Element TodoRowElement(Signal<Todo> todo, int index)
    {
        TodoRow Project()
        {
            var value = todo.Value;
            var filter = _filter.Value;
            var visible = filter switch
            {
                "open" => !value.Done,
                "done" => value.Done,
                _ => true,
            };

            return new TodoRow(
                value.Id,
                value.Title,
                value.Done ? "done" : "open",
                visible);
        }

        return HStack(8,
                Lit.TextBlock(() => Project().StatusText).Width(48),
                Lit.TextBlock(() => Project().Title).Width(180),
                Button("Toggle", () => todo.Set(t => t with { Done = !t.Done })),
                Button("Rename", () => todo.Set(t => t with { Title = t.Title + "!" }))
            )
            .BindNative<StackPanelElement, WinUI.StackPanel, bool>(
                () => Project().Visible,
                static (panel, visible) =>
                    panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed,
                Scheduler,
                name: $"todo-row-{index}-visible");
    }
}
```

Updating one row re-runs only that row's bindings. Changing the filter re-runs
only bindings that read `_filter.Value`. This scalar path is promising, but
structural insert/remove/reorder still needs a scoped structural-region helper.

### B.2 P3 Solid × Element syntax

The P3 spike also retained Element records, but used Solid-style owner scopes,
signals, effects, and structural helpers:

```csharp
Build() runs once -> signals/effects subscribe by read -> changed signals
flush only dependent bindings.
```

Ordinary C# control flow is mount-time only. Use `When`/`For` when structure
must change reactively after mount.

#### Simple counter

```csharp
using Reactor.P3SolidElement;
using static Reactor.P3SolidElement.Factories;

public sealed class CounterDemo : SignalComponent
{
    protected override void Build()
    {
        var count = Signal(0);

        Render(VStack(12,
            Text(() => $"Count: {count.Value}")
                .FontSize(24),

            HStack(8,
                Button("-", () => count.Value--)
                    .IsEnabled(() => count.Value > 0),

                Button("+", () => count.Value++)),

            Slider(
                () => (double)count.Value,
                min: 0,
                max: 10,
                onValueChanged: value => count.Value = (int)value),

            When(
                () => count.Value >= 10,
                () => Text("Max reached"),
                () => Text(() => $"{10 - count.Value} steps remaining"))
        ));
    }
}
```

Direct two-way binding is shorter when the signal type matches the control:

```csharp
var count = Signal(0d);
Slider(count, min: 0, max: 10);
```

#### Keyed list with projection, inline update, and reorder

```csharp
using Reactor.P3SolidElement;
using static Reactor.P3SolidElement.Factories;

public readonly record struct TodoItem(int Id, string Title, bool Done);

public sealed class TodoListDemo : SignalComponent
{
    private Signal<TodoItem[]>? _items;
    private Signal<int>? _nextId;

    protected override void Build()
    {
        var items = _items = Signal<TodoItem[]>([
            new TodoItem(1, "Write scorecard", done: true),
            new TodoItem(2, "Review structural questions", done: false),
        ]);
        var nextId = _nextId = Signal(3);

        Render(VStack(10,
            HStack(8,
                Button("Add", Add),
                Button("Rotate", RotateFirstToEnd)
                    .IsEnabled(() => items.Value.Length > 1)),

            Text(() =>
            {
                var done = items.Value.Count(item => item.Done);
                return $"{done}/{items.Value.Length} complete";
            }),

            VStack(For(
                () => items.Value,
                item => item.Id,
                (item, index) =>
                {
                    var editing = new Signal<bool>(false);

                    return HStack(8,
                        Text(() => $"{index.Value + 1}."),

                        When(
                            () => editing.Value,
                            () => HStack(4,
                                TextBox(
                                    () => item.Value.Title,
                                    text => Rename(item.Value.Id, text)),
                                Button("Done", () => editing.Value = false)),
                            () => HStack(4,
                                Text(() => item.Value.Done
                                    ? $"[x] {item.Value.Title}"
                                    : $"[ ] {item.Value.Title}"),
                                Button("Edit", () => editing.Value = true))),

                        Button("Toggle", () => Toggle(item.Value.Id)),
                        Button("Remove", () => Remove(item.Value.Id)));
                }))
        ));
    }

    private void Add()
    {
        var id = _nextId!.Value;
        _nextId.Value = id + 1;
        _items!.Value = [.. _items.Value, new TodoItem(id, $"Item {id}", false)];
    }

    private void Toggle(int id) =>
        _items!.Value = [.. _items.Value.Select(item =>
            item.Id == id ? item with { Done = !item.Done } : item)];

    private void Rename(int id, string title) =>
        _items!.Value = [.. _items.Value.Select(item =>
            item.Id == id ? item with { Title = title } : item)];

    private void Remove(int id) =>
        _items!.Value = [.. _items.Value.Where(item => item.Id != id)];

    private void RotateFirstToEnd()
    {
        var current = _items!.Value;
        if (current.Length > 1)
        {
            _items.Value = [.. current[1..], current[0]];
        }
    }
}
```

The `For` template receives item and index signals, so moves preserve row-local
state. Ordinary `.Select(...)` in `Build()` is a mount-time snapshot in P3; use
`For(...)` when child structure needs to insert/remove/reorder reactively.

### B.3 Current hook/API mapping

The mapping below is conceptual. The measured prototypes implemented the core
scalar primitives; service-layer APIs such as navigation, resources, docking,
and accessibility remain design work.

| Current Reactor hook/API | P1 dependency-tracked Element shape | P3 Solid × Element shape |
|---|---|---|
| `UseState<T>(initial)` | `Signal<T>` / signal field stored on the component; read inside binding lambdas and update with `.Set(...)`. | `var s = Signal(initial);` read/write `s.Value`. |
| `UseReducer<T>(initial)` | Signal field plus an instance dispatch method that applies the reducer into `.Set(...)`. | `Signal<T>` plus `s.Update(reducer)` or `s.Value = reducer(s.Value)`. |
| `UseReducer<TState,TAction>(initial, reducer)` | Store `Signal<TState>`; `Dispatch(TAction action) => _state.Set(s => reducer(s, action))`. | `Signal<TState>` plus `void Dispatch(TAction action) => state.Value = reducer(state.Value, action);`. |
| `UseEffect(Action, deps)` | `ReactiveScope` for signal-dependent effects, or `IReactiveController.OnMount/OnUnmount` for lifecycle. | `Effect(() => { ... signal reads ... });` dependencies are inferred from signal reads. |
| `UseEffect(Func<Action>, deps)` | Controller or disposable `ReactiveScope` owned by the component. | `Effect(...)` plus `OnCleanup(cleanup)` or owner-scope cleanup. |
| `UseMemo<T>(factory, deps)` | Usually unnecessary because `Build()` runs once; use derived getter lambdas or a future `Computed<T>` for expensive derived values. | `Memo(factory)` when cached derived state matters, or inline reactive getter `() => expr` for simple bound props. |
| `UseCallback(callback, deps)` | Usually unnecessary; instance methods and captured fields are stable across the one-time `Build()`. | Usually unnecessary; event handlers read current signals at invocation time. |
| `UseRef<T>(initial)` | Plain instance field for non-reactive state; `Signal<T>` for reactive state; `ElementRef<T>` for controls. | Plain field for imperative handles, or `Signal<T>` if changes should update UI. |
| `UsePersisted<T>(key, initial)` | Signal initialized from storage; controller writes back on signal changes. | Likely `PersistedSignal<T>(key, initial, scope)` wrapper over `Signal<T>`. |
| `UsePersisted<T>(key, initial, scope)` | Same as above, with scope included in the storage key/provider. | Persisted signal scoped by the supplied owner/context. |
| `UseObservableTree<T>(source)` | Adapter converts object/property changes into one or more signals. | Subscribe once in an effect/owner scope and copy changed state into signals. |
| `UseObservable<T>(source)` | Controller subscribes to `PropertyChanged`; writes a version signal or property signals. | Observable adapter that writes signals and cleans up with the owner scope. |
| `UseObservableProperty<T,TProp>(source, selector, propertyName)` | Property-specific controller updates a `Signal<TProp>`. | `Signal<TProp>` updated by `PropertyChanged`; cleanup owned by scope. |
| `UseCollection<T>(ObservableCollection<T>)` | Collection adapter maintains item signals plus a structural version signal; needs structural-region design for add/remove/reorder. | Adapter to `Signal<T[]>` or `For` source invalidation; structural UI should use `For`. |
| `UseNavigation<TRoute>(initial)` / `UseNavigation<TRoute>()` | Long-lived navigation controller with current-route signal. | `Signal<TRoute>` plus navigation service/context. |
| `UseNavigationLifecycle(...)` | Navigation controller callbacks registered once. | Route signals plus effect/cleanup callbacks. |
| `UseSystemBackButton(...)` | Controller subscribes/unsubscribes to back-button events. | Effect subscribing to back-button events with cleanup. |
| `UseContext<T>(Context<T>)` | `ContextSignal<T>` or dependency-injected context service; bindings read `.Value`. | Owner-scope ambient context or explicit service injection. |
| `UseColorScheme()` | Theme controller exposes `Signal<ColorScheme>`. | App/theme service exposing `Signal<ColorScheme>`. |
| `UseIsDarkTheme()` | Derived getter over the color-scheme signal. | Derived getter/memo from color-scheme signal. |
| `UseHighContrast()` | Accessibility/settings controller exposes `Signal<bool>`. | Accessibility/theme service signal. |
| `UseHighContrastScheme()` | Accessibility/settings controller exposes `Signal<string?>`. | Derived signal/getter from high-contrast service. |
| `UseReducedMotion()` | Accessibility/settings controller exposes `Signal<bool>`. | Accessibility service signal. |
| `UseIntl()` | Localization accessor backed by a locale signal; needs context design. | Localization service/context, plus culture signal if dynamic. |
| `UseCommand(Command)` / `UseCommand<T>(Command<T>)` | Command wrapper with `Signal<bool> IsExecuting`, stable `Execute`, optional controller for cancellation. | Direct command/event handler binding; command state can be signals. |
| `UseWindowSize(window)` / `UseWindowSize()` | `Signal<(double Width,double Height)>` updated by a window controller. | Signal updated from window size events; cleanup on scope dispose. |
| `UseWindow()` | Direct service/property from component/controller; no signal unless window changes. | Host/window context or service. |
| `UseWindowPosition()` | Window-position signal updated by a controller. | Window service signal updated from move events. |
| `UseIsCovered()` | Window z-order/coverage controller exposes `Signal<bool>`. | Window/activation/visibility service signal. |
| `UseDisplays()` | Display-change controller exposes `Signal<IReadOnlyList<DisplayInfo>>`. | Display service signal/list. |
| `UseWindowAspectRatio(double?)` | Controller applies/removes aspect lock on mount/update/unmount. | Imperative host/window effect with cleanup/update. |
| `UseWindowDragMove()` | Instance method or service callback field. | Direct host/window action exposed by context/service. |
| `UseFilePickerAsync(options)` | Plain async service method; store result in a signal only if UI should react. | Direct injected service call; not reactive unless result is stored in a signal. |
| `UseFolderPickerAsync(options)` | Same as file picker. | Same as file picker. |
| `UseWindowState()` | Window-state controller exposes `Signal<WindowState>`. | Window state signal. |
| `UseIsActive()` | Activation controller exposes `Signal<bool>`. | Activation signal. |
| `UseClosingGuard(canClose)` | Controller registers/unregisters the predicate. | Effect registering closing handler; cleanup unregisters. |
| `UseDpi()` | DPI/window controller exposes `Signal<uint>`. | DPI signal from window/display service. |
| `UseOpenWindow(...)` | Window controller/service keyed by `WindowKey`; expose signals for state if needed. | Imperative host service; returned window handle can be stored in a field/signal. |
| `UseTrayIcon(spec)` | Controller creates/reuses tray icon on mount and disposes on unmount. | Imperative host service owned by scope; cleanup disposes tray icon. |
| `UseBreakpoint(window, minWidth)` / `UseBreakpoint(minWidth)` | Derived bool from a window-width signal. | Derived bool from window-size signal. |
| `UseAnnounce()` | Announcement service/controller field. | Accessibility announcer service handle. |
| `UseDevtools()` | Direct read of session flag; no signal unless state changes at runtime. | Requires a public debug snapshot before parity. |
| `UseElementRef<T>()` | `ElementRef<T>` instance field. | Native ref/handle API still needed. |
| `UseElementFocus()` | ElementRef field plus instance `RequestFocus` method/controller. | Element/native ref plus action, likely an `OnMount`/ref-style API. |
| `UseFocus()` | `FocusManager` instance field. | Focus manager service/context. |
| `UseFocusTrap(isActive)` | `FocusTrapHandle` field; active state driven by signal or binding. | Effect installs/uninstalls trap based on signal/condition. |
| `UseResource<T>(fetcher, deps, options)` | `ResourceSignal<T>` or controller owning cancellation, status signals, and reload; needs design. | `Resource<T>` primitive built from `Signal<AsyncValue<T>>`, effect, cancellation cleanup, and cache signals. |
| `UseInfiniteResource<TItem,TCursor>(...)` | Paged resource controller with item signals, cursor signal, loading/error signals; needs design. | Paged resource object with signals for pages/items/loading/error. |
| `UseDataSource<T>(...)` | DataSource controller projecting pages into item signals and a structural region; needs design. | Wrapper over an infinite resource/page signals; needs design. |
| `UseMutation<TInput,TResult>(...)` | Mutation controller with status/result/error signals and stable `Run`; needs design. | Stable mutation service object with `Signal<bool> IsPending`, `Signal<Exception?> Error`, and cache invalidation. |
| `UseMemoCells(...)` | Usually replaced by per-cell signals and one-time row/cell construction. | Usually unnecessary for value-only updates; fixed grids use per-cell signals. |
| `UseMemoCellsByKey(...)` | Keyed item-signal dictionary plus structural region for insert/remove/reorder. | Keyed `For` if structure changes; per-item scope follows the key. |
| `UseMemoCellsByIndex(...)` | One signal per index; update changed indices only. This is the P1 stress pattern. | Fixed indexed signal array for grids; keyed/indexed `For` for dynamic structure. |
| `UseDockHost()` | Docking context/service signal; needs design. | Docking context/service signal; needs design. |
| `UseActivePaneKey()` | Active-pane signal. | Active-pane signal. |
| `UseIsActivePane()` | Derived bool from pane identity and active-pane signal. | Derived bool from pane identity and active-pane signal. |
| `UsePane()` | Pane context/service; needs design. | Pane context/service; needs design. |
| `UseDockState()` | Pane state signal. | Pane state signal. |
| `UseDockLayout()` | Dock-layout snapshot signal. | Dock-layout snapshot signal. |
| `UseDockPanePersisted<T>(key, initial)` | Persisted signal scoped by pane key. | Persisted signal scoped by pane key. |
