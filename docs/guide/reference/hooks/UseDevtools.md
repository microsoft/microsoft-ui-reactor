# UseDevtools

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseDevtoolsExtensions.UseDevtools(Microsoft.UI.Reactor.Core.RenderContext)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Returns <c>true</c> when the current process is running with the in-app
devtools UI enabled. This is the AND of two independent signals:
<list type="number"><item><c>ReactorApp.Run&lt;TRoot&gt;</c>
was called with <c>devtools: true</c> (build-time capability gate).</item><item>The process was launched with <c>--devtools app</c> or
<c>--devtools run</c> (session-scoped opt-in by the user running the app).</item></list>

The value is frozen for the session; this call does not consume a hook
slot and does not cause re-renders. Components use it to gate dev-only
UX so the subtree is never constructed in retail sessions:
<code>
var dev = ctx.UseDevtools();
return VStack(Content(), dev ? DebugOverlay() : null);
</code>


