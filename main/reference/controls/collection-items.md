---
title: "Collection items"
description: "Records and enums for tree, swipe and items view content."
---

**Learn more:** [Collections](/microsoft-ui-reactor/main/controls/collections), [Controls](/microsoft-ui-reactor/main/controls)

## `ItemsViewLayoutKind` [#itemsviewlayoutkind]

```csharp
public enum ItemsViewLayoutKind
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `StackLayout` | `0` |  |
| `LinedFlowLayout` | `1` |  |
| `UniformGridLayout` | `2` |  |

## `SwipeItemData` [#swipeitemdata]

```csharp
public record SwipeItemData(string Text, Action? OnInvoked = null, IconSource? IconSource = null, Brush? Background = null, Brush? Foreground = null, SwipeBehaviorOnInvoked BehaviorOnInvoked = default)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors]

#### `SwipeItemData` [#swipeitemdata_1]

```csharp
public SwipeItemData(string Text, Action? OnInvoked = null, IconSource? IconSource = null, Brush? Background = null, Brush? Foreground = null, SwipeBehaviorOnInvoked BehaviorOnInvoked = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Text` | `string` |  |  |
| `OnInvoked` | `Action?` | `null` |  |
| `IconSource` | `IconSource?` | `null` |  |
| `Background` | `Brush?` | `null` |  |
| `Foreground` | `Brush?` | `null` |  |
| `BehaviorOnInvoked` | `SwipeBehaviorOnInvoked` | `default` |  |

### Properties [#properties]

#### `Background` [#background]

```csharp
public Brush? Background  // get, init
```

#### `BehaviorOnInvoked` [#behavioroninvoked]

```csharp
public SwipeBehaviorOnInvoked BehaviorOnInvoked  // get, init
```

#### `Foreground` [#foreground]

```csharp
public Brush? Foreground  // get, init
```

#### `IconSource` [#iconsource]

```csharp
public IconSource? IconSource  // get, init
```

#### `OnInvoked` [#oninvoked]

```csharp
public Action? OnInvoked  // get, init
```

#### `Text` [#text]

```csharp
public string Text  // get, init
```

## `TemplatedControlKind` [#templatedcontrolkind]

Which WinUI control type a templated list element targets.

```csharp
public enum TemplatedControlKind
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Values [#values_1]

| Name | Value | Description |
| --- | --- | --- |
| `ListView` | `0` |  |
| `GridView` | `1` |  |
| `FlipView` | `2` |  |

## `TreeViewNodeData` [#treeviewnodedata]

```csharp
public record TreeViewNodeData(string Content, TreeViewNodeData[]? Children = null) : IBindableCustomPropertyImplementation
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors_1]

#### `TreeViewNodeData` [#treeviewnodedata_1]

```csharp
public TreeViewNodeData(string Content, TreeViewNodeData[]? Children = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Content` | `string` |  |  |
| `Children` | [`TreeViewNodeData[]?`](#treeviewnodedata) | `null` |  |

### Properties [#properties_1]

#### `Children` [#children]

```csharp
public TreeViewNodeData[]? Children  // get, init
```

#### `Content` [#content]

```csharp
public string Content  // get, init
```

#### `ContentElement` [#contentelement]

Optional Reactor element to render as the node's visual content. When null, a TextBlock showing Content is rendered.

```csharp
public Element? ContentElement  // get, init
```

> **Obsolete.** Use the typed UI.TreeView\<T\>(items, keySelector, childrenSelector, viewBuilder) overload (the hierarchical peer of ListView\<T\>); a pre-built Element cannot be hosted in a node-mode TreeViewNode. See issue #447.

**Remarks**

Deprecated. WinUI's node-mode `TreeView` stringifies node content and cannot host a pre-built `UIElement`, so rich per-node visuals must come from a template (a `data → Element` function), never an element instance. Use the typed, data-driven `UI.TreeView<T>(items, keySelector, childrenSelector, viewBuilder)` — the hierarchical peer of `ListView<T>` — instead. The legacy path stays functional for back-compat but renders blank under virtualization recycling.

#### `IsExpanded` [#isexpanded]

```csharp
public bool IsExpanded  // get, init
```
