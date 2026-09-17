# UseDevtools

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseDevtoolsExtensions.UseDevtools(Microsoft.UI.Reactor.Core.RenderContext)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Returns `true` when the current process is running with the in-app
devtools UI enabled. This is the AND of two independent signals:

- The binary was built with `Reactor.DevtoolsSupport`
enabled (build-time capability gate).
- The process was launched with `--devtools app` or
`--devtools run` (session-scoped opt-in by the user running the app).


The value is frozen for the session; this call does not consume a hook
slot and does not cause re-renders. Components use it to gate dev-only
UX so the subtree is never constructed in retail sessions:
```csharp
var dev = ctx.UseDevtools();
return VStack(Content(), dev ? DebugOverlay() : null);
```


