# UseExternalStore

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseExternalStore``1(System.Func{System.Action,System.Action},System.Func{``0},System.Collections.Generic.IEqualityComparer{``0})`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Subscribes to an external store that exposes a current snapshot getter.
Re-renders only when a change notification yields a different snapshot.

## Discussion

<para>`subscribe` is the effect dependency that decides whether the
subscription is torn down and re-established, so it must be a <b>stable</b> delegate
across renders. Pass a method group (e.g. <c>store.Subscribe</c>) or a
[UseCallback](UseCallback.md#usecallbackaction-object)-memoized delegate. A fresh capturing lambda
(<c>onChanged =&gt; store.Subscribe(onChanged)</c>) is a new delegate every render and
will unsubscribe/resubscribe on each one. This mirrors React's guidance for
<c>useSyncExternalStore</c>.
</para><para>`getSnapshot` must return a <b>cached/stable</b> value that only changes
identity when the underlying data changes. Returning a fresh, never-equal value on every
call (e.g. <c>() =&gt; items.ToArray()</c>) combined with an unstable `subscribe`
can spin: re-render re-runs the effect, the immediate re-check observes a "change", and that
forces another render. Memoize the snapshot or return a value the supplied comparer treats as
equal when nothing changed.
</para>

## Featured in

- [Hooks](../../hooks.md)

