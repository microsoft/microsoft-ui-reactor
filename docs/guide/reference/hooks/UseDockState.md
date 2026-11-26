# UseDockState

Returns the enclosing pane's current dock state (Docked / Floating / AutoHidden / AutoHiddenExpanded / Hidden). Re-renders the consumer only on transitions.

```csharp
public static DockPaneState UseDockState(this RenderContext ctx)
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `DockHooks`. Package: `Microsoft.UI.Reactor.Advanced`. Add `using Microsoft.UI.Reactor.Docking;`.

**Learn more:** [Docking Windows](../../docking.md)

## Remarks

Spec 045 §5.3.11. Defaults to [`DockPaneState.Docked`](../docking/layout-model.md#values_1) outside any host.
