# Extensibility

The control protocol, control descriptors, the reconciler and element pooling.

Use these types to add your own controls to Reactor or to change how built-in controls are created and updated.

**Learn more:** [Extending Reactor with Native Controls](../../extending-reactor-controls.md), [The Control Reconciler Protocol](../../control-reconciler-protocol.md), [Element Pool](../../element-pool.md)

## Types

| Type | Description |
| --- | --- |
| [Children strategies](children-strategies.md) | Records that tell the reconciler how a control holds its children. |
| [Descriptor callbacks](descriptor-delegates.md) | Delegates and attributes used by control descriptors. |
| [Handler contexts](lifecycle-contexts.md) | Contexts passed to an element handler when it mounts, updates or unmounts a control. |
| [`ControlDescriptor<TElement, TControl>`](ControlDescriptor.md) | Spec 047 §6 / §14 Phase 2 (Q1 spike) — declarative control description. |
| [`ControlRegistry`](ControlRegistry.md) | Spec 048 §8 — the global, lazy, lock-free control registry that backs the factory-as-registration pattern. |
| [`DescriptorHandler<TElement, TControl>`](DescriptorHandler.md) | Spec 047 §6 / §14 Phase 2 (Q1 spike) — interpreter that drives a `ControlDescriptor<TElement, TControl>` against the v1 protocol surface. |
| [`ElementPool`](ElementPool.md) | Per-CLR-type pool (cap 32) that recycles unmounted WinUI FrameworkElement instances. |
| [`IDecoratorElementHandler<TElement>`](IDecoratorElementHandler.md) | Spec 047 §14 Phase 3 completion — decorator-style V1 handler contract for elements whose returned `UIElement` identity may change on update or whose unmount disposition diverges from the standard pool-return. |
| [`IElementHandler<TElement, TControl>`](IElementHandler.md) | Spec 047 §4 / §14 Phase 1 (1.6) — author-facing handler contract for porting a control onto the v1 protocol. |
| [`PoolPolicy<TControl>`](PoolPolicy.md) | Spec 047 §13 Q18 / §14 Phase 1 (1.5) — author-facing pool policy. |
| [`PropEntry<TElement, TControl>`](PropEntry.md) | Spec 047 §6 / §14 Phase 2 (Q1 spike) — base class for descriptor entries declared on a `ControlDescriptor<TElement, TControl>`. |
| [`ReactorBinding`](ReactorBinding.md) | Spec 047 §14 Phase 1 (1.4) — public author-facing surface for the echo suppression primitive (§13 Q19). |
| [`ReactorBinding<TElement>`](ReactorBinding-1.md) | Spec 047 §9 / §14 Phase 1 (1.6) — per-binding event helper. |
| [`ReactorFeatureFlags`](ReactorFeatureFlags.md) | Process-wide feature flags that gate risky or in-progress behavior. |
| [`Reconciler`](Reconciler.md) | The reconciler diffs old and new element trees and patches the real WinUI control tree. |
