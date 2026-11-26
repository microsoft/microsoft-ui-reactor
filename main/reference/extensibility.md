---
title: "Extensibility"
description: "The control protocol, control descriptors, the reconciler and element pooling."
sidebar:
  label: "Overview"
---

Use these types to add your own controls to Reactor or to change how built-in controls are created and updated.

**Learn more:** [Extending Reactor with Native Controls](/microsoft-ui-reactor/main/how-it-works/extending-reactor-controls), [The Control Reconciler Protocol](/microsoft-ui-reactor/main/how-it-works/control-reconciler-protocol), [Element Pool](/microsoft-ui-reactor/main/how-it-works/element-pool)

## Types [#types]

| Type | Description |
| --- | --- |
| [Children strategies](/microsoft-ui-reactor/main/reference/extensibility/children-strategies) | Records that tell the reconciler how a control holds its children. |
| [Descriptor callbacks](/microsoft-ui-reactor/main/reference/extensibility/descriptor-delegates) | Delegates and attributes used by control descriptors. |
| [Handler contexts](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts) | Contexts passed to an element handler when it mounts, updates or unmounts a control. |
| [`ControlDescriptor<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/ControlDescriptor) | Spec 047 §6 / §14 Phase 2 (Q1 spike) — declarative control description. |
| [`ControlRegistry`](/microsoft-ui-reactor/main/reference/extensibility/ControlRegistry) | Spec 048 §8 — the global, lazy, lock-free control registry that backs the factory-as-registration pattern. |
| [`DescriptorHandler<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/DescriptorHandler) | Spec 047 §6 / §14 Phase 2 (Q1 spike) — interpreter that drives a `ControlDescriptor<TElement, TControl>` against the v1 protocol surface. |
| [`ElementPool`](/microsoft-ui-reactor/main/reference/extensibility/ElementPool) | Per-CLR-type pool (cap 32 per lane) that recycles unmounted WinUI FrameworkElement instances. |
| [`IDecoratorElementHandler<TElement>`](/microsoft-ui-reactor/main/reference/extensibility/IDecoratorElementHandler) | Spec 047 §14 Phase 3 completion — decorator-style V1 handler contract for elements whose returned `UIElement` identity may change on update or whose unmount disposition diverges from the standard pool-return. |
| [`IElementHandler<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/IElementHandler) | Spec 047 §4 / §14 Phase 1 (1.6) — author-facing handler contract for porting a control onto the v1 protocol. |
| [`PoolPolicy<TControl>`](/microsoft-ui-reactor/main/reference/extensibility/PoolPolicy) | Spec 047 §13 Q18 / §14 Phase 1 (1.5) — author-facing pool policy. |
| [`PropEntry<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/PropEntry) | Spec 047 §6 / §14 Phase 2 (Q1 spike) — base class for descriptor entries declared on a `ControlDescriptor<TElement, TControl>`. |
| [`ReactorBinding`](/microsoft-ui-reactor/main/reference/extensibility/ReactorBinding) | Spec 047 §14 Phase 1 (1.4) — public author-facing surface for the echo suppression primitive (§13 Q19). |
| [`ReactorBinding<TElement>`](/microsoft-ui-reactor/main/reference/extensibility/ReactorBinding-1) | Spec 047 §9 / §14 Phase 1 (1.6) — per-binding event helper. |
| [`ReactorFeatureFlags`](/microsoft-ui-reactor/main/reference/extensibility/ReactorFeatureFlags) | Process-wide feature flags that gate risky or in-progress behavior. |
| [`Reconciler`](/microsoft-ui-reactor/main/reference/extensibility/Reconciler) | The reconciler diffs old and new element trees and patches the real WinUI control tree. |
