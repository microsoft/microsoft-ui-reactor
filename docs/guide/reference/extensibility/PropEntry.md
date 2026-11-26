# PropEntry\<TElement, TControl\>

Spec 047 §6 / §14 Phase 2 (Q1 spike) — base class for descriptor entries declared on a [`ControlDescriptor<TElement, TControl>`](ControlDescriptor.md).

The interpreter (see [`DescriptorHandler<TElement, TControl>`](DescriptorHandler.md)) iterates the entry list during Mount and Update. The property's value type stays inside each concrete entry — the interpreter never sees it, so iterating a heterogeneous list of entries does not box value types (each generic specialization keeps its own monomorphic code path).

**Phase ordering inside Mount:**

1. [`PropEntry.Mount`](#mounttcontrol-telement) runs once per entry. Controlled entries write their initial value bare — event subscription has not yet been wired, so a synchronous change event has no trampoline to fire (no echo). Mirrors the KD-1b fix on the hand-coded handlers. A control whose change event is raised after the write returns echoes after the wiring below; its entry arms the Mount write instead (`HandCodedControlled`'s `armOnMount`).
2. [`PropEntry.EnsureSubscribed`](#ensuresubscribed) runs once per entry. Controlled entries subscribe to their change event here, guarded by [`DescriptorHandler<TElement, TControl>`](DescriptorHandler.md)'s per-control subscription gate (so pool-reused controls do not double-subscribe).

On Update, [`PropEntry.Update`](#updatetcontrol-telement-telement) runs and writes through `ReactorBinding.WriteSuppressed` when the entry is controlled.

```csharp
public abstract class PropEntry<TElement, TControl> where TElement : Element where TControl : UIElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol.Descriptor`.

**Learn more:** [Extending Reactor with Native Controls](../../extending-reactor-controls.md), [The Control Reconciler Protocol](../../control-reconciler-protocol.md)

## Constructors

### `PropEntry`

```csharp
protected PropEntry()
```

## Methods

### `EnsureSubscribed`

Event subscription hook. Default = no-op (one-way / initial entries do not subscribe to events). Controlled entries override.

The interpreter calls this after every [`PropEntry.Mount`](#mounttcontrol-telement) in the entry list so all bare initial writes complete before any event trampoline goes live (KD-1b ordering invariant).

```csharp
public virtual void EnsureSubscribed(ReactorBinding<TElement> binding, TControl ctrl, TElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `binding` | [`ReactorBinding<TElement>`](ReactorBinding-1.md) |  |
| `ctrl` | `TControl` |  |
| `el` | `TElement` |  |

### `Mount`

#### `Mount(TControl, TElement)`

Initial write at Mount. Controlled entries write bare here (no echo suppression — subscription has not yet been wired).

```csharp
public abstract void Mount(TControl ctrl, TElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctrl` | `TControl` |  |
| `el` | `TElement` |  |

#### `Mount(MountContext, TControl, TElement)`

Spec 047 §14 Phase 3-final — context-carrying Mount overload. Default implementation forwards to the parameterless [`PropEntry.Mount`](#mounttcontrol-telement); entry types that need reconciler/rerender access (e.g. `OneWayBridgedPropEntry` for Flyout descriptor bridging) override this overload instead.

```csharp
public virtual void Mount(in MountContext ctx, TControl ctrl, TElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`MountContext`](lifecycle-contexts.md#mountcontext) |  |
| `ctrl` | `TControl` |  |
| `el` | `TElement` |  |

### `Update`

#### `Update(TControl, TElement, TElement)`

Diff old/new element and apply the minimal write. Controlled entries write through `ReactorBinding.WriteSuppressed`.

```csharp
public abstract void Update(TControl ctrl, TElement oldEl, TElement newEl)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctrl` | `TControl` |  |
| `oldEl` | `TElement` |  |
| `newEl` | `TElement` |  |

#### `Update(UpdateContext, TControl, TElement, TElement)`

Spec 047 §14 Phase 3-final — context-carrying Update overload. Same forwarding contract as [`PropEntry.Mount`](#mountmountcontext-tcontrol-telement).

```csharp
public virtual void Update(in UpdateContext ctx, TControl ctrl, TElement oldEl, TElement newEl)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UpdateContext`](lifecycle-contexts.md#updatecontext) |  |
| `ctrl` | `TControl` |  |
| `oldEl` | `TElement` |  |
| `newEl` | `TElement` |  |
