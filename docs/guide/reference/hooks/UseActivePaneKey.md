# UseActivePaneKey

Returns the active pane's [`DockableContent.Key`](../docking/layout-model.md#key_1), or null when no pane is active. Re-renders the consumer only when the active key changes (selector-style scope).

```csharp
public static object? UseActivePaneKey(this RenderContext ctx)
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `DockHooks`. Package: `Microsoft.UI.Reactor.Advanced`. Add `using Microsoft.UI.Reactor.Docking;`.

**Learn more:** [Docking Windows](../../docking.md)

## Remarks

Spec 045 §5.3.11.
