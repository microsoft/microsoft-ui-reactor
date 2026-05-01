# Reconciliation Reference

The reconciler is the engine that makes declarative UI efficient. Instead of rebuilding the entire WinUI control tree on every state change, it diffs the old and new virtual element trees and applies only the differences.

## Overview

```
State change
  → Component.Render() produces new Element tree
  → Reconciler diffs old tree vs. new tree
  → Only changed properties/controls are patched on real WinUI controls
```

This is the same concept as React's virtual DOM reconciliation. The element tree is cheap to create (immutable C# records, no WinUI allocations); the reconciler makes it cheap to apply.

## Reconciliation strategy

The reconciler uses a set of heuristics to minimize work:

| Scenario | Action |
|----------|--------|
| Same element type, same position | Update the existing control's properties in place |
| Different element type at same position | Unmount old control, mount new one |
| Element → null/Empty | Remove the control |
| null/Empty → Element | Create and mount a new control |
| Keyed elements | Match by key for stable identity across reordering |

## Reconciler architecture

The reconciler is split across multiple partial class files:

- **`Reconciler.cs`** — orchestration, type registry, helpers (`SetElementTag`, `GetElementTag`)
- **`Reconciler.Mount.cs`** — `Mount()` dispatch + 40+ `MountXxx()` handlers that create WinUI controls from elements
- **`Reconciler.Update.cs`** — `Update()` dispatch + 30+ `UpdateXxx()` handlers that patch existing controls
- **`ChildReconciler.cs`** — keyed and unkeyed child list reconciliation

The reconciler is a single pure-C# implementation. An earlier experimental native Rust differ path was prototyped and removed; the design notes are preserved under `docs/specs/archived/` for reference.

## Child reconciliation

When a container element (VStack, HStack, Grid, etc.) has children, the reconciler must diff the child lists. This is handled by `ChildReconciler.cs`.

### Unkeyed children

Without keys, children are matched by **position**. The reconciler walks both lists in parallel:

```
Old:  [A, B, C]
New:  [A, B, C, D]

→ Update A (position 0)
→ Update B (position 1)
→ Update C (position 2)
→ Mount D (position 3)
```

Position-based matching breaks down with insertions:

```
Old:  [A, B, C]
New:  [X, A, B, C]

→ Update A→X (position 0 — wrong! A becomes X instead of shifting)
→ Update B→A (position 1)
→ Update C→B (position 2)
→ Mount C (position 3)
```

This produces correct visual output but does unnecessary work — every element gets updated instead of just inserting X at position 0.

### Keyed children

Keys solve this. When children have keys, the reconciler matches by key instead of position:

```csharp
ForEach(items, item => Text(item.Name).Key(item.Id))
```

```
Old:  [A:1, B:2, C:3]
New:  [X:4, A:1, B:2, C:3]

→ Mount X (key 4 is new)
→ Move A to position 1 (key 1 matched)
→ Move B to position 2 (key 2 matched)
→ C stays (key 3, already at correct position)
```

The keyed reconciler uses a **Longest Increasing Subsequence (LIS)** algorithm to minimize the number of DOM moves. Elements that are already in the correct relative order don't need to be moved.

## Gap nodes

Some WinUI controls have children that aren't in a simple `Panel.Children` collection. These are "gap nodes":

- **TabView** — tabs are in `TabItems`
- **NavigationView** — menu items are in `MenuItems`, content is separate
- **TreeView** — nodes are in `RootNodes` with nested `Children`
- **MenuBar** — items are in `Items` with nested sub-items
- **CommandBar** — primary/secondary commands

Gap nodes are reconciled imperatively in a second pass, separately from the main child-list diff.

## The mount/update dispatch

Both `Mount()` and `Update()` use type-based dispatch. When the reconciler encounters an element:

**Mount** — creates a WinUI control and configures it:
```
ButtonElement → new WinUI.Button { Content = el.Label, ... }
TextElement → new WinUI.TextBlock { Text = el.Text, ... }
StackElement → new WinUI.StackPanel { Orientation = ..., Children = [...] }
ComponentElement → instantiate Component, call Render(), mount subtree
```

**Update** — patches only changed properties:
```
old.Label != new.Label → button.Content = new.Label
old.IsEnabled != new.IsEnabled → button.IsEnabled = new.IsEnabled
```

## Tag-based event dispatch

Event handlers in WinUI are wired once at mount time. But Reactor elements are recreated every render with new closure captures. The Tag pattern solves this:

1. At mount: wire the event handler once (e.g., `button.Click += ...`)
2. At mount and update: store the current element in `control.Tag`
3. When the event fires: read the element from `sender.Tag` and call the handler

```csharp
// Mount (once)
button.Click += (sender, _) => {
    var el = GetElementTag<ButtonElement>(sender);
    el?.OnClick?.Invoke();
};

// Update (every render) — just update the tag
SetElementTag(button, newElement);
```

This avoids re-subscribing events on every render while ensuring handlers always use the latest closures.

## Element pool

The `ElementPool` recycles unmounted WinUI controls for reuse. When a control is unmounted, instead of being garbage collected, it's cleaned and returned to a pool. The next mount of the same control type pulls from the pool instead of allocating.

Currently pooled types: TextBlock, StackPanel, Grid, Border, ScrollViewer, Canvas, Image, and several other non-interactive controls.

Interactive controls (Button, TextBox, etc.) are not yet pooled because resetting their event state safely is more complex. This is a planned improvement.

## Custom element types

The reconciler supports custom element types via `RegisterType<TElement, TControl>()`. This allows extending Reactor with new control types without modifying the framework source.

## Performance characteristics

| Operation | Cost |
|-----------|------|
| Element tree creation | O(n) — cheap record allocations, no WinUI objects |
| Diffing | O(n) for tree structure, O(k) for keyed children where k = changed items |
| Patch application | O(changes) — only touched controls are updated |
| State batching | Multiple state changes → single render |
