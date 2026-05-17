# IHookDispatcher

`type`  
_cref_: `T:Microsoft.UI.Reactor.Hooks.IHookDispatcher`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Abstraction for marshalling continuations back to the render thread. Null-safe —
if the callback is absent the continuation runs inline on the thread-pool
completion thread (tests and headless hosts).

## Discussion

The production implementation wraps <c>DispatcherQueue.TryEnqueue</c>; tests typically
install a synchronous stub that records invocations.


