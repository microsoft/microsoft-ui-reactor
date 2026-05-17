# Mutation

`type`  
_cref_: `T:Microsoft.UI.Reactor.Hooks.Mutation`2`

## Summary

Handle returned by the <c>UseMutation</c> hook. Carries
the pending/error/last-result state and the [RunAsync](RunAsync.md) entry point.

## Discussion

<para><b>Concurrency.</b> Overlapping [RunAsync](RunAsync.md) calls each get their own
cancellation token; both complete and fire their callbacks in completion order.
[LastResult](LastResult.md) is whichever finishes last. If you want strictly-serialized
mutations, wrap [RunAsync](RunAsync.md) behind your own gate (or disable the trigger
control while [IsPending](IsPending.md) is true).</para><para><b>Reset.</b>[Reset](Reset.md) clears [Error](Error.md) and
[LastResult](LastResult.md) but does <b>not</b> cancel in-flight work — this is an explicit
choice so a "dismiss the error banner" action doesn't abort the user's retry.</para>

