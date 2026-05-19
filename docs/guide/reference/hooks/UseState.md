# UseState

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseState``1(``0,System.Boolean)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Declares a piece of state. Returns (currentValue, setter).
Must be called in the same order every render (just like React hooks).
<para>
When <paramref name="threadSafe" /> is true, reads and writes are synchronized
with a per-hook lock so concurrent setter calls from many threads serialize.
When false (default), cross-thread setter calls are auto-marshaled onto the
captured UI dispatcher — the write and the rerender both run on the UI thread,
so background-thread setters from <c>Task.Run</c>, <c>PeriodicTimer</c>, or
callbacks after <c>await … ConfigureAwait(false)</c> work correctly without
any extra opt-in. Use <paramref name="threadSafe" />: <c>true</c> when you need
many concurrent setters to apply in-place (i.e., without an intervening UI
thread hop) or when the setter result must be visible to its caller before
the next UI tick.
</para>

## Featured in

- [Hooks](../../hooks.md)

