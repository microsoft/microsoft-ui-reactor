---
title: "UseDockState"
description: "Returns the enclosing pane's current dock state (Docked / Floating / AutoHidden / AutoHiddenExpanded / Hidden). Re-renders the consumer only on transitions."
---

```csharp
public static DockPaneState UseDockState(this RenderContext ctx)
```

An extension method on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext), declared in `DockHooks`. Package: `Microsoft.UI.Reactor.Advanced`. Add `using Microsoft.UI.Reactor.Docking;`.

**Learn more:** [Docking Windows](/microsoft-ui-reactor/main/guides/docking)

## Remarks [#remarks]

Spec 045 §5.3.11. Defaults to [`DockPaneState.Docked`](/microsoft-ui-reactor/main/reference/docking/layout-model#values_1) outside any host.
