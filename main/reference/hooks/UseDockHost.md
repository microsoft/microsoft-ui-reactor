---
title: "UseDockHost"
seo:
  description: "Returns the nearest enclosing DockHostModel, or null when the component is rendered outside any docking host."
---

Returns the nearest enclosing [`DockHostModel`](/microsoft-ui-reactor/main/reference/docking/DockHostModel), or null when the component is rendered outside any docking host.

```csharp
public static DockHostModel? UseDockHost(this RenderContext ctx)
```

An extension method on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext), declared in `DockHooks`. Package: `Microsoft.UI.Reactor.Advanced`. Add `using Microsoft.UI.Reactor.Docking;`.

**Learn more:** [Docking Windows](/microsoft-ui-reactor/main/guides/docking)

## Remarks [#remarks]

Spec 045 §5.3.11.
