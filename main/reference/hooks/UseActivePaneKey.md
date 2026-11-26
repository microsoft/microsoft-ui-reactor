---
title: "UseActivePaneKey"
seo:
  description: "Returns the active pane's DockableContent.Key, or null when no pane is active. Re-renders the consumer only when the active key changes (selector-style scope)."
---

Returns the active pane's [`DockableContent.Key`](/microsoft-ui-reactor/main/reference/docking/layout-model#key_1), or null when no pane is active. Re-renders the consumer only when the active key changes (selector-style scope).

```csharp
public static object? UseActivePaneKey(this RenderContext ctx)
```

An extension method on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext), declared in `DockHooks`. Package: `Microsoft.UI.Reactor.Advanced`. Add `using Microsoft.UI.Reactor.Docking;`.

**Learn more:** [Docking Windows](/microsoft-ui-reactor/main/guides/docking)

## Remarks [#remarks]

Spec 045 §5.3.11.
