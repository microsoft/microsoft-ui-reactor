# UseIsActivePane

Returns true when the \*enclosing pane\* is the host's currently-active pane. Re-renders the consumer only on the false ↔ true transition.

```csharp
public static bool UseIsActivePane(this RenderContext ctx)
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `DockHooks`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Windows](../../windows.md), [Hooks](../../hooks.md)

## Remarks

Resolves "the enclosing pane" via [`UsePane`](UsePane.md); returns false outside any pane subtree.
