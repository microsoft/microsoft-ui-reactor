# UseDockHost

Returns the nearest enclosing [`DockHostModel`](../docking/DockHostModel.md), or null when the component is rendered outside any docking host.

```csharp
public static DockHostModel? UseDockHost(this RenderContext ctx)
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `DockHooks`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Docking Windows](../../docking.md)

## Remarks

Spec 045 §5.3.11.
