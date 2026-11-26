# UsePane

Returns identity info for the enclosing pane subtree. Throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) when called from a component that isn't rendered inside a docked pane.

```csharp
public static DockPaneInfo UsePane(this RenderContext ctx)
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `DockHooks`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Docking Windows](../../docking.md)

## Remarks

Spec 045 §5.3.11.
