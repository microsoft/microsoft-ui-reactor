---
title: "UseIsActivePane"
description: "Returns true when the \\enclosing pane\\ is the host's currently-active pane. Re-renders the consumer only on the false ↔ true transition."
---

```csharp
public static bool UseIsActivePane(this RenderContext ctx)
```

An extension method on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext), declared in `DockHooks`. Package: `Microsoft.UI.Reactor.Advanced`. Add `using Microsoft.UI.Reactor.Docking;`.

**Learn more:** [Windows](/microsoft-ui-reactor/main/guides/windows), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Remarks [#remarks]

Resolves "the enclosing pane" via [`UsePane`](/microsoft-ui-reactor/main/reference/hooks/UsePane); returns false outside any pane subtree.
