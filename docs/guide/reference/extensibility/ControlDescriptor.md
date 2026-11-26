# ControlDescriptor\<TElement, TControl\>

Spec 047 §6 / §14 Phase 2 (Q1 spike) — declarative control description.

A descriptor is the data-driven alternative to a hand-coded [`IElementHandler<TElement, TControl>`](IElementHandler.md). Authors declare property bindings (`OneWay`, [`ControlDescriptor.InitialOnly`](#initialonly), [`ControlDescriptor.Controlled`](#controlled)), an optional children strategy, and an optional factory; the interpreter ([`DescriptorHandler<TElement, TControl>`](DescriptorHandler.md)) executes them against the same v1 protocol surface a hand-coded handler would use.

**Phase 2 measurement gate (§13 Q1):** the descriptor and the hand-coded handler shapes are evaluated head-to-head on the same M1 / M2 / M5 / M7 / M10 micro-benches and the L4 / L9 macros, then the §13 Q1 decision matrix determines which surface ships as the primary author path. See `docs/specs/047-extensible-control-model.md` §13 Q1.

**Echo suppression (issue #206).** A `.Controlled` / `.HandCodedControlled` entry suppresses the echo of its own programmatic write automatically — the interpreter wraps the write in the public [`ReactorBinding`](ReactorBinding.md) `WriteSuppressed` primitive (or arms a value-diff predicate when `valueDiffEcho` is set), so a framework-driven write never echoes back into the user's `OnChanged` callback. Descriptor authors therefore never touch the suppressor directly; hand-coded [`IElementHandler<TElement, TControl>`](IElementHandler.md) authors call `WriteSuppressed` themselves. See `docs/guide/extending-reactor-controls.md`.

**Fluent author pattern:**

```csharp
public record ToggleSwitchElement(Optional<bool> IsOn = default) : Element;

public static readonly ControlDescriptor<ToggleSwitchElement, ToggleSwitch> Descriptor =
    new ControlDescriptor<ToggleSwitchElement, ToggleSwitch>()
        .OneWay(e => e.OnContent, (c, v) => c.OnContent = v)
        .OneWayConditional(e => e.Header, (c, v) => c.Header = v, e => e.Header is not null)
        .Controlled<bool, RoutedEventArgs>(
            get:          e => e.IsOn,
            set:          (c, v) => c.IsOn = v,
            subscribe:    (fe, h) => ((ToggleSwitch)fe).Toggled += (s, e) => h(s, e),
            unsubscribe:  (fe, h) => { /* trampoline lives for control lifetime */ },
            callback:     e => e.OnIsOnChanged,
            readBack:     c => c.IsOn);
```

```csharp
public sealed class ControlDescriptor<TElement, TControl> where TElement : Element where TControl : FrameworkElement, new()
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol.Descriptor`.

**Learn more:** [Extending Reactor with Native Controls](../../extending-reactor-controls.md), [The Control Reconciler Protocol](../../control-reconciler-protocol.md)

## Constructors

### `ControlDescriptor`

```csharp
public ControlDescriptor()
```

## Properties

### `AfterChildrenMount`

Spec 047 §14 Phase 3 prelude (Engine A1) — optional post-children mount hook. The interpreter ([`DescriptorHandler<TElement, TControl>`](DescriptorHandler.md)) surfaces it through [`IElementHandler.AfterChildrenMount`](IElementHandler.md#afterchildrenmount), which the adapter invokes after every child has mounted. Use for events that must subscribe after children-add (TabView SelectionChanged), or for imperative work that must run strictly after the prop loop AND the event subscriptions (FrameElement's mount-time navigation, which would otherwise complete before its own Navigated/NavigationFailed handlers exist). Defaults to `null` (no hook). Set it from an object initializer, or — from a generated descriptor's `Customize` hook, which receives an already-constructed descriptor — via [`ControlDescriptor.WithAfterChildrenMount`](#withafterchildrenmount).

```csharp
public AfterChildrenMountCallback<TElement, TControl>? AfterChildrenMount  // get, init
```

### `Children`

Optional children strategy. Engine dispatches through it via `V1HandlerAdapter` after each Mount / Update body returns. Defaults to `null` (treated as [`None<TElement, TControl>`](children-strategies.md#nonetelement-tcontrol) by the adapter).

```csharp
public ChildrenStrategy<TElement, TControl>? Children  // get, set
```

### `Factory`

Optional factory the engine invokes when the pool is empty or [`ControlDescriptor.PoolPolicy`](#poolpolicy) opts out. Defaults to `new TControl()` via the `new()` constraint.

```csharp
public Func<TControl>? Factory  // get, init
```

### `GetSetters`

`Setters` selector — the descriptor needs an opaque way to pull the element's `Action<TControl>[]` setters chain because each element subclass declares its own `Setters` property (no common base interface). Authors register `e => e.Setters`; the interpreter passes the result to `Reconciler.ApplySetters` after the property entries have run. Defaults to "no setters" if not configured.

```csharp
public Func<TElement, Action<TControl>[]>? GetSetters  // get, init
```

### `OnUnmount`

Optional teardown hook. The interpreter ([`DescriptorHandler<TElement, TControl>`](DescriptorHandler.md)) surfaces it through [`IElementHandler.Unmount`](IElementHandler.md#unmount), which the engine invokes when the control leaves the tree. Use it to invalidate control-scoped state the descriptor created during mount and that the pool reset contract cannot see — a pending deferred write, a one-shot `Loaded` subscription, a disposable. Defaults to `null` (no hook). Set it from an object initializer, or — from a generated descriptor's `Customize` hook, which receives an already-constructed descriptor — via [`ControlDescriptor.WithUnmount`](#withunmount).

Not for event trampolines: those anchor to the control's lifetime by design (see the `unsubscribe` no-op contract on [`ControlDescriptor.Controlled`](#controlled)).

Declaring this hook makes [`DescriptorHandler<TElement, TControl>`](DescriptorHandler.md) force the control's `ReactorState` into existence at mount, because the engine's unmount dispatch is tag-gated. That costs one small allocation per mounted control of this type, so declare it only when there is real teardown to do.

```csharp
public DescriptorUnmountCallback<TControl>? OnUnmount  // get, init
```

### `PoolPolicy`

Optional pool policy. When null the engine uses the default (poolable if the control type opts in via `PoolableTypes`).

```csharp
public PoolPolicy<TControl>? PoolPolicy  // get, init
```

### `Properties`

Read-only view of the property entries declared on this descriptor (in declaration order).

```csharp
public IReadOnlyList<PropEntry<TElement, TControl>> Properties  // get
```

## Methods

### `CoercingOneWay`

Add a one-way property whose write may coerce a sibling controlled prop's value (e.g. `Slider.Minimum` coercing `Slider.Value`). When `coercesController` returns true the write is wrapped in `ReactorBinding.WriteSuppressed` so the coercion-driven change event is dropped.

Declarative replacement for the imperative `if (ctrl.Value < n.Min) WriteSuppressed(...)` pattern in the hand-coded `SliderHandler`. Encodes the §8 audit's "coercion-tolerance" treatment for the 8 audited coercion sites (NumberBox / Slider Min/Max etc.) as a single declarative entry.

```csharp
public ControlDescriptor<TElement, TControl> CoercingOneWay<TValue>(Func<TElement, TValue> get, Action<TControl, TValue> set, Func<TControl, TValue, bool> coercesController, IEqualityComparer<TValue>? comparer = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `get` | `Func<TElement, TValue>` |  |  |
| `set` | `Action<TControl, TValue>` |  |  |
| `coercesController` | `Func<TControl, TValue, bool>` |  |  |
| `comparer` | `IEqualityComparer<TValue>?` | `null` |  |

### `CollectionDiffControlled`

§14 Phase 3-final — `.CollectionDiffControlled`. Two-way bound prop whose value is an [`IReadOnlyList`](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1) on the element side and an `IList<TItem>` on the control side. Each Update applies a keyed hash-set diff and emits per-element `Add`/`Remove` ops inside `ChangeEchoSuppressor.BeginSuppress`. Used by CalendarView for `SelectedDates`; reusable for any control with a typed `IObservableVector<T>` two-way slot.

```csharp
public ControlDescriptor<TElement, TControl> CollectionDiffControlled<TPayload, TItem, TKey, TDelegate>(Func<TElement, IReadOnlyList<TItem>> get, Func<TControl, IList<TItem>> getVector, Func<TItem, TKey> key, Action<TControl, TDelegate> subscribe, Func<TElement, Delegate?> callbackPresent, TDelegate trampoline, Func<TPayload, bool> slotIsNull, Action<TPayload, TDelegate> setSlot, IEqualityComparer<TKey>? keyComparer = null) where TPayload : class, new() where TKey : notnull where TDelegate : Delegate
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `get` | `Func<TElement, IReadOnlyList<TItem>>` |  |  |
| `getVector` | `Func<TControl, IList<TItem>>` |  |  |
| `key` | `Func<TItem, TKey>` |  |  |
| `subscribe` | `Action<TControl, TDelegate>` |  |  |
| `callbackPresent` | `Func<TElement, Delegate?>` |  |  |
| `trampoline` | `TDelegate` |  |  |
| `slotIsNull` | `Func<TPayload, bool>` |  |  |
| `setSlot` | `Action<TPayload, TDelegate>` |  |  |
| `keyComparer` | `IEqualityComparer<TKey>?` | `null` |  |

### `Controlled`

Add a controlled (two-way) property binding. The engine writes from element state when `get` returns a value and leaves the control authoritative when it returns [`Optional.Unset`](../components/Optional.md#unset); writes use echo suppression on Update. The control raises `subscribe`'s event on user interaction; the trampoline invokes the element's `callback` with `readBack`'s value.

**Callback gating:** if `callback` returns `null` on the element passed at Mount, no subscription happens at all — mirrors the hand-coded `Ensure*Wiring` gate.

```csharp
public ControlDescriptor<TElement, TControl> Controlled<TValue, TArgs>(Func<TElement, Optional<TValue>> get, Action<TControl, TValue> set, Action<FrameworkElement, EventHandler<TArgs>> subscribe, Action<FrameworkElement, EventHandler<TArgs>> unsubscribe, Func<TElement, Action<TValue>?> callback, Func<TControl, TValue> readBack, IEqualityComparer<TValue>? comparer = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `get` | `Func<TElement, Optional<TValue>>` |  |  |
| `set` | `Action<TControl, TValue>` |  |  |
| `subscribe` | `Action<FrameworkElement, EventHandler<TArgs>>` |  |  |
| `unsubscribe` | `Action<FrameworkElement, EventHandler<TArgs>>` |  |  |
| `callback` | `Func<TElement, Action<TValue>?>` |  |  |
| `readBack` | `Func<TControl, TValue>` |  |  |
| `comparer` | `IEqualityComparer<TValue>?` | `null` |  |

### `HandCodedControlled`

§14 Phase 3 (3.0.1) — escape-hatch builder for controlled (two-way) props on a control that needs a hand-authored trampoline against a user-supplied per-control event payload (`TPayload`). Required for multi-event controls (e.g. `TextBox.Text` alongside `SelectionChanged`) where the single-event fast-path `DescriptorControlledPayload` would collide if two controlled entries shared the same closed generic.

The payload type is typically reused from `ControlEventPayloads.cs` (e.g. `TextBoxEventPayload`) which already has a slot per event. `slotIsNull` + `setSlot` address the specific slot on the payload this entry owns; `subscribe` wires the user-supplied native `TDelegate` directly to the control's event (no `EventHandler<TArgs>` bridge closure).

Mount/Update prop-write semantics are identical to [`ControlDescriptor.Controlled`](#controlled): unset skips writes, set values perform a bare initial write and a suppressed write on control drift. Subscription is gated on `callback` returning non-null on the mounted element.

`valueDiffEcho` replaces the Update write's suppress-counter token with the spec 047 §8 value-diff arm, which the trampoline consumes on the next change event and which drops that event only when its readback matches the written value. `armOnMount` (requires `valueDiffEcho`) arms the Mount write too. Set it when the control raises its change event after the write returns (e.g. `RichEditBox`'s `Document.SetText`) and the trampoline cannot tell that echo apart on its own: the echo lands after the trampoline is wired and would otherwise reach the callback. The `set` lambda stays a plain write; the entry does the arming.

```csharp
public ControlDescriptor<TElement, TControl> HandCodedControlled<TPayload, TValue, TDelegate>(Func<TElement, Optional<TValue>> get, Action<TControl, TValue> set, Func<TControl, TValue> readBack, Action<TControl, TDelegate> subscribe, Func<TElement, Action<TValue>?> callback, TDelegate trampoline, Func<TPayload, bool> slotIsNull, Action<TPayload, TDelegate> setSlot, IEqualityComparer<TValue>? comparer = null, bool valueDiffEcho = false, bool armOnMount = false) where TPayload : class, new() where TDelegate : Delegate
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `get` | `Func<TElement, Optional<TValue>>` |  |  |
| `set` | `Action<TControl, TValue>` |  |  |
| `readBack` | `Func<TControl, TValue>` |  |  |
| `subscribe` | `Action<TControl, TDelegate>` |  |  |
| `callback` | `Func<TElement, Action<TValue>?>` |  |  |
| `trampoline` | `TDelegate` |  |  |
| `slotIsNull` | `Func<TPayload, bool>` |  |  |
| `setSlot` | `Action<TPayload, TDelegate>` |  |  |
| `comparer` | `IEqualityComparer<TValue>?` | `null` |  |
| `valueDiffEcho` | `bool` | `false` |  |
| `armOnMount` | `bool` | `false` |  |

### `HandCodedEvent`

§14 Phase 3 (3.0.1) — escape-hatch builder for fire-and-forget control-intrinsic events that do not round-trip a DP (e.g. `TextBox.SelectionChanged`, `Image.ImageOpened`). Authors supply a per-control payload type and a native trampoline delegate; the entry wires subscription with the same payload-slot gating shape as [`ControlDescriptor.HandCodedControlled`](#handcodedcontrolled) but performs no Mount/Update prop writes.

Subscription is gated on `callbackPresent` returning non-null — the element's callback shape can be anything (e.g. `Action<string, int, int>`) so the gate is checked against the untyped `Delegate`.

```csharp
public ControlDescriptor<TElement, TControl> HandCodedEvent<TPayload, TDelegate>(Action<TControl, TDelegate> subscribe, Func<TElement, Delegate?> callbackPresent, TDelegate trampoline, Func<TPayload, bool> slotIsNull, Action<TPayload, TDelegate> setSlot) where TPayload : class, new() where TDelegate : Delegate
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `subscribe` | `Action<TControl, TDelegate>` |  |
| `callbackPresent` | `Func<TElement, Delegate?>` |  |
| `trampoline` | `TDelegate` |  |
| `slotIsNull` | `Func<TPayload, bool>` |  |
| `setSlot` | `Action<TPayload, TDelegate>` |  |

### `Immediate`

§14 Phase 3-final — `.Immediate` entry. Pure subscription wiring for the "observed-DP callback + Loaded → inner template-part trampoline" pattern. The control's primary DP round-trip stays on a sibling `.HandCodedControlled` entry. See `ImmediatePropEntry` for the shape contract (NumberBox is the canonical proof point). Pass `loadedHook` as `null` when the DP observation is the whole subscription and there is no template part to walk.

```csharp
public ControlDescriptor<TElement, TControl> Immediate<TPayload>(Func<TElement, Delegate?> callbackGate, DependencyProperty observeProperty, DependencyPropertyChangedCallback observeCallback, Func<TPayload, bool> observeSlotIsNull, Action<TPayload, DependencyPropertyChangedCallback> setObserveSlot, RoutedEventHandler? loadedHook) where TPayload : class, new()
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `callbackGate` | `Func<TElement, Delegate?>` |  |
| `observeProperty` | `DependencyProperty` |  |
| `observeCallback` | `DependencyPropertyChangedCallback` |  |
| `observeSlotIsNull` | `Func<TPayload, bool>` |  |
| `setObserveSlot` | `Action<TPayload, DependencyPropertyChangedCallback>` |  |
| `loadedHook` | `RoutedEventHandler?` |  |

### `Imperative`

§14 Phase 3 finish — Engine (4). Escape hatch entry that receives `mount`+(`update` with BOTH old and new TElement, so the entry can express a diff the per-value get/set lambdas of [`ControlDescriptor.OneWayConditional`](#onewayconditional) can't: comparing one element field while writing a derived value elsewhere, or multi-source error context like `Path.PathDataString`'s path-markup converter/pre-built Geometry branches.

**When to reach for it:** only when the standard entries can't express the diff or the write. Imperative entries skip the engine's value-comparer fast-path, so a misused Imperative entry re-runs on every render. The other entry shapes (`OneWay`, [`ControlDescriptor.OneWayConditional`](#onewayconditional), [`ControlDescriptor.CoercingOneWay`](#coercingoneway), [`ControlDescriptor.HandCodedControlled`](#handcodedcontrolled)) still cover the 95% case.

**Distinct from** [`Imperative<TElement, TControl>`](children-strategies.md#imperativetelement-tcontrol) (a [`ChildrenStrategy<TElement, TControl>`](children-strategies.md#childrenstrategytelement-tcontrol) that drives the whole CHILD subtree imperatively) — this is a property-level escape hatch that participates in the normal prop loop.

```csharp
public ControlDescriptor<TElement, TControl> Imperative(Action<TControl, TElement> mount, Action<TControl, TElement, TElement> update)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mount` | `Action<TControl, TElement>` |  |
| `update` | `Action<TControl, TElement, TElement>` |  |

### `ImperativeBridged`

#### `ImperativeBridged(Action<MountContext, TControl, TElement>, Action<UpdateContext, TControl, TElement, TElement>)`

§14 Phase 3 finish — Engine (2). Bridged variant of [`ControlDescriptor.Imperative`](#imperative) — the lambdas receive the [`MountContext`](lifecycle-contexts.md#mountcontext) / [`UpdateContext`](lifecycle-contexts.md#updatecontext) so they can call engine-internal helpers for bindings without owned child lifetime.

For an owned secondary Element slot, use [`ControlDescriptor.ImperativeBridged`](#imperativebridgedactionmountcontext-tcontrol-telement-actionupdatecontext-tcontrol-telement-telement-actionunmountcontext-tcontrol) so the mounted child also receives terminal teardown. This overload is for bindings that do not own child lifetime.

Same no-fast-path warning as [`ControlDescriptor.Imperative`](#imperative) — the Update lambda runs on every render. Reach for the standard shapes when they fit.

```csharp
public ControlDescriptor<TElement, TControl> ImperativeBridged(Action<MountContext, TControl, TElement> mount, Action<UpdateContext, TControl, TElement, TElement> update)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mount` | `Action<MountContext, TControl, TElement>` |  |
| `update` | `Action<UpdateContext, TControl, TElement, TElement>` |  |

#### `ImperativeBridged(Action<MountContext, TControl, TElement>, Action<UpdateContext, TControl, TElement, TElement>, Action<UnmountContext, TControl>)`

Engine-bridged entry with terminal teardown, for owned secondary Element slots. Unmount the child while it is still parented, then detach it. The callback runs before, and independently of, the descriptor's [`ControlDescriptor.OnUnmount`](#onunmount) hook. Declaring this callback roots a ReactorState per control, including keyless controls without element callbacks. For example, Expander.HeaderContent owns a secondary Element child while the primary children strategy owns Content. Pair it with OneWayConditional for a string Header fallback when the Element slot is null.

```csharp
public ControlDescriptor<TElement, TControl> ImperativeBridged(Action<MountContext, TControl, TElement> mount, Action<UpdateContext, TControl, TElement, TElement> update, Action<UnmountContext, TControl> unmount)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mount` | `Action<MountContext, TControl, TElement>` |  |
| `update` | `Action<UpdateContext, TControl, TElement, TElement>` |  |
| `unmount` | `Action<UnmountContext, TControl>` |  |

### `Initial`

Add an initial-only property binding (write on Mount, never on Update). Use for seed-only props (e.g. `TextBox.InitialText`).

```csharp
public ControlDescriptor<TElement, TControl> Initial<TValue>(Func<TElement, TValue> get, Action<TControl, TValue> set)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `get` | `Func<TElement, TValue>` |  |
| `set` | `Action<TControl, TValue>` |  |

### `InitialOnly`

Add an initial-only property binding (write on Mount, never on Update). Use for default-value-style props where the control owns the value after its initial seed.

```csharp
public ControlDescriptor<TElement, TControl> InitialOnly<TValue>(Func<TElement, TValue> get, Action<TControl, TValue> set)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `get` | `Func<TElement, TValue>` |  |
| `set` | `Action<TControl, TValue>` |  |

### `OneWay`

#### `OneWay<TValue>(Func<TElement, TValue>, Action<TControl, TValue>, IEqualityComparer<TValue>?)`

Add a one-way property binding (write on Mount, diff-and-write on Update). Engine never subscribes to a change event for this prop. Use for control props the framework writes but the user never subscribes to (Header text, brushes, layout values).

```csharp
public ControlDescriptor<TElement, TControl> OneWay<TValue>(Func<TElement, TValue> get, Action<TControl, TValue> set, IEqualityComparer<TValue>? comparer = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `get` | `Func<TElement, TValue>` |  |  |
| `set` | `Action<TControl, TValue>` |  |  |
| `comparer` | `IEqualityComparer<TValue>?` | `null` |  |

#### `OneWay<TValue>(Func<TElement, Optional<TValue>>, Action<TControl, TValue>, DependencyProperty, IEqualityComparer<TValue>?)`

Add a one-way property binding for an [`Optional<T>`](../components/Optional.md) element value backed by a dependency property. Explicit values write via `set`; unset values call [`DependencyObject.ClearValue`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.dependencyobject.clearvalue) for `dp` so WinUI value precedence can fall back.

```csharp
public ControlDescriptor<TElement, TControl> OneWay<TValue>(Func<TElement, Optional<TValue>> get, Action<TControl, TValue> set, DependencyProperty dp, IEqualityComparer<TValue>? comparer = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `get` | `Func<TElement, Optional<TValue>>` |  |  |
| `set` | `Action<TControl, TValue>` |  |  |
| `dp` | `DependencyProperty` |  |  |
| `comparer` | `IEqualityComparer<TValue>?` | `null` |  |

### `OneWayBridged`

§14 Phase 3-final — engine-bridged one-way property. Same diff-and-write contract as [`ControlDescriptor.OneWayConditional`](#onewayconditional), but the set lambda receives the [`Reconciler`](Reconciler.md) and the rerender callback so it can call engine-internal helpers like `Reconciler.CreateFlyoutForDescriptor`. Used by the button-family Flyout port.

```csharp
public ControlDescriptor<TElement, TControl> OneWayBridged<TValue>(Func<TElement, TValue> get, OneWayBridgedSetter<TControl, TValue> set, Func<TElement, bool> shouldWrite, IEqualityComparer<TValue>? comparer = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `get` | `Func<TElement, TValue>` |  |  |
| `set` | [`OneWayBridgedSetter<TControl, TValue>`](descriptor-delegates.md#onewaybridgedsettertcontrol-tvalue) |  |  |
| `shouldWrite` | `Func<TElement, bool>` |  |  |
| `comparer` | `IEqualityComparer<TValue>?` | `null` |  |

### `OneWayConditional`

Add a one-way property with a "should I write?" predicate. The write is skipped when the predicate returns false — useful for nullable props where leaving the control at its default is the intent (e.g. `Border.Background` when the element has no background set).

```csharp
public ControlDescriptor<TElement, TControl> OneWayConditional<TValue>(Func<TElement, TValue> get, Action<TControl, TValue> set, Func<TElement, bool> shouldWrite, IEqualityComparer<TValue>? comparer = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `get` | `Func<TElement, TValue>` |  |  |
| `set` | `Action<TControl, TValue>` |  |  |
| `shouldWrite` | `Func<TElement, bool>` |  |  |
| `comparer` | `IEqualityComparer<TValue>?` | `null` |  |

### `Reference`

#### `Reference(Func<TElement, ElementRef?>, Action<TControl, FrameworkElement?>)`

CR-004 untyped reference binding: accepts an element-record slot that stores an untyped [`ElementRef`](../input/element-refs.md#elementref) (so authors can supply any `ElementRef<TConcrete>`). The resolved target is surfaced as a [`FrameworkElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement).

```csharp
public ControlDescriptor<TElement, TControl> Reference(Func<TElement, ElementRef?> get, Action<TControl, FrameworkElement?> set)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `get` | `Func<TElement, ElementRef?>` |  |
| `set` | `Action<TControl, FrameworkElement?>` |  |

#### `Reference<TTarget>(Func<TElement, ElementRef<TTarget>?>, Action<TControl, TTarget?>)`

Add a reference property binding. The control property is written from the referenced cell at mount and whenever the cell changes.

```csharp
public ControlDescriptor<TElement, TControl> Reference<TTarget>(Func<TElement, ElementRef<TTarget>?> get, Action<TControl, TTarget?> set) where TTarget : FrameworkElement
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `get` | `Func<TElement, ElementRef<TTarget>?>` |  |
| `set` | `Action<TControl, TTarget?>` |  |

### `ReferenceList`

Add a list-valued reference binding. The control list is rebuilt from currently-resolved targets, preserving the author's declaration order.

```csharp
public ControlDescriptor<TElement, TControl> ReferenceList<TTarget>(Func<TElement, IReadOnlyList<ElementRef<TTarget>>?> get, Action<TControl, IReadOnlyList<TTarget>> apply) where TTarget : FrameworkElement
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `get` | `Func<TElement, IReadOnlyList<ElementRef<TTarget>>?>` |  |
| `apply` | `Action<TControl, IReadOnlyList<TTarget>>` |  |

### `WithAfterChildrenMount`

Fluent form of [`ControlDescriptor.AfterChildrenMount`](#afterchildrenmount), for authors whose descriptor is already constructed by the time they can customize it (the `[WrapManual]` `Customize` hook is handed a built descriptor, so the init-only property is out of reach there).

```csharp
public ControlDescriptor<TElement, TControl> WithAfterChildrenMount(AfterChildrenMountCallback<TElement, TControl> callback)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `callback` | [`AfterChildrenMountCallback<TElement, TControl>`](descriptor-delegates.md#afterchildrenmountcallbacktelement-tcontrol) |  |

### `WithUnmount`

Fluent form of [`ControlDescriptor.OnUnmount`](#onunmount), for authors whose descriptor is already constructed by the time they can customize it (the `[WrapManual]` `Customize` hook is handed a built descriptor, so the init-only property is out of reach there).

```csharp
public ControlDescriptor<TElement, TControl> WithUnmount(DescriptorUnmountCallback<TControl> callback)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `callback` | [`DescriptorUnmountCallback<TControl>`](descriptor-delegates.md#descriptorunmountcallbacktcontrol) |  |
