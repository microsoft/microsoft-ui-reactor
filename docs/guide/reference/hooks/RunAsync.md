# RunAsync

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.Mutation`2.RunAsync(`0)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Kick off the mutator with <paramref name="input" />. Returns a task that completes
with the mutator's result or fault.

## Discussion

<para>Ordering: `OnOptimistic` fires
synchronously before the mutator starts. `OnSuccess`
or `OnError` fires on the hook's dispatcher
thread after completion.</para><para>If `OnOptimistic` throws, the
mutator is never invoked and the returned task is faulted with the optimistic
exception — prevents half-applied state where the optimistic patch ran but the
real request can't.</para>


