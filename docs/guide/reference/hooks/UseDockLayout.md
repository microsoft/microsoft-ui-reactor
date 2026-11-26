# UseDockLayout

Returns a wide snapshot of the dock layout — re-renders the consumer on any structural change. Documented as "used sparingly — devtools, not pane content."

```csharp
public static DockLayoutSnapshot? UseDockLayout(this RenderContext ctx)
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `DockHooks`. Package: `Microsoft.UI.Reactor.Advanced`. Add `using Microsoft.UI.Reactor.Docking;`.

**Learn more:** [Docking Windows](../../docking.md)

## Remarks

Spec 045 §5.3.11. Returns null outside any host.
