# Mutation

`type`  
_cref_: `T:Microsoft.UI.Reactor.Hooks.Mutation`2`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Handle returned by the <c>UseMutation</c> hook. Carries
the pending/error/last-result state and the [RunAsync](RunAsync.md) ([guide](../../hooks.md)) entry point.

## Discussion

<para><b>Concurrency.</b> Overlapping [RunAsync](RunAsync.md) ([guide](../../hooks.md)) calls each get their own
cancellation token; both complete and fire their callbacks in completion order.
[LastResult](LastResult.md) ([guide](../../hooks.md)) is whichever finishes last. If you want strictly-serialized
mutations, wrap [RunAsync](RunAsync.md) ([guide](../../hooks.md)) behind your own gate (or disable the trigger
control while [IsPending](IsPending.md) ([guide](../../hooks.md)) is true).</para><para><b>Reset.</b>[Reset](Reset.md) ([guide](../../hooks.md)) clears [Error](Error.md) ([guide](../../hooks.md)) and
[LastResult](LastResult.md) ([guide](../../hooks.md)) but does <b>not</b> cancel in-flight work — this is an explicit
choice so a "dismiss the error banner" action doesn't abort the user's retry.</para>


