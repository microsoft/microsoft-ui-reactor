# PendingScope

`type`  
_cref_: `T:Microsoft.UI.Reactor.Hooks.PendingScope`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Shared loading-state ref-count consumed by the <c>Pending</c> element and populated by
<c>UseResource</c> / <c>UseInfiniteResource</c> hooks inside the scope. When the scope
observes <b>any</b> registered resource in the <c>Loading</c> state (not <c>Reloading</c>),
the owning <c>Pending</c> element renders its fallback instead of the child subtree.

## Discussion

<para><b>Semantics.</b> Only <c>Loading</c> triggers the fallback — spec §10.1. A
<c>Reloading(previous)</c> is "we already have something to show" and the subtree
continues to render normally.</para><para><b>Threading.</b> All members are thread-safe. [Changed](Changed.md) ([guide](../../hooks.md)) fires on the
thread that caused the mutation — consumers (typically <c>Pending</c>'s re-render
trigger) marshal it to the dispatcher themselves.</para><para><b>Scope nesting.</b> Each <c>Pending</c> provides a fresh scope to its subtree,
so nested <c>Pending</c>s are independent. A hook registers only with its nearest
ancestor scope.</para>


