# Factories: Collections

Lists, grids, trees, flip views and virtualized collections.

Each factory method returns an element record. Most are on `Factories`, so `using static Microsoft.UI.Reactor.Factories;` covers them. A factory declared on another class names the `using static` it needs.

**Learn more:** [Collections](../../collections.md)

## `FlipView`

```csharp
FlipViewElement FlipView(params Element[] items)
TemplatedFlipViewElement<T> FlipView<T>(IReadOnlyList<T> items, Func<T, int, Element> viewBuilder) where T : IReactorKeyed
FlipViewElement FlipView(Optional<int> selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element[] items)
FlipViewElement FlipView(int selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element[] items)
FlipViewElement FlipView(int? selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element[] items)
TemplatedFlipViewElement<T> FlipView<T>(IReadOnlyList<T> items, Func<T, string> keySelector, Func<T, int, Element> viewBuilder)
```

| Overload | Description |
| --- | --- |
| `FlipView(Element[])` |  |
| `FlipView<T>(IReadOnlyList<T>, Func<T, int, Element>)` | [`IReactorKeyed`](../components/IReactorKeyed.md)-typed overload of [`FlipView`](#flipview); `KeySelector` defaults to `t => t.Key`. (spec 042 §5) |
| `FlipView(Optional<int>, Action<int>?, Element[])` | Creates a flip view whose selected index is controlled only when `selectedIndex` has a value. |
| `FlipView(int, Action<int>?, Element[])` | Creates a flip view with a controlled selected index. Disambiguates a plain [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32) argument, which converts equally well to [`Nullable`](https://learn.microsoft.com/dotnet/api/system.nullable-1) and [`Optional<T>`](../components/Optional.md). Any [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32) — including `default`, which is `0` — selects that index; pass `(int?)null` or [`Optional.Unset`](../components/Optional.md#unset) to leave the selection uncontrolled. |
| `FlipView(int?, Action<int>?, Element[])` | Creates a flip view whose selected index is unset when `selectedIndex` is `null`. |
| `FlipView<T>(IReadOnlyList<T>, Func<T, string>, Func<T, int, Element>)` | Creates a typed, data-driven [`TemplatedFlipViewElement<T>`](../elements/collections.md#templatedflipviewelementt) — the templated peer of WinUI's untyped `FlipView`. |

**Returns:** [`FlipViewElement`](../elements/collections.md#flipviewelement), [`TemplatedFlipViewElement<T>`](../elements/collections.md#templatedflipviewelementt).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `items` | [`Element[]`](../elements/Element.md) or `IReadOnlyList<T>` |  |  |
| `viewBuilder` | `Func<T, int, Element>` |  |  |
| `selectedIndex` | [`Optional<int>`](../components/Optional.md) or `int` or `int?` |  |  |
| `onSelectedIndexChanged` | `Action<int>?` | `null` |  |
| `keySelector` | `Func<T, string>` |  |  |

**Remarks**

Reactor-original — see [`ListView`](#listview) for the templated-peer naming rationale. (spec 039 §0.3)

## `GridView`

```csharp
GridViewElement GridView(params Element[] items)
TemplatedGridViewElement<T> GridView<T>(IReadOnlyList<T> items, Func<T, int, Element> viewBuilder) where T : IReactorKeyed
GridViewElement GridView(Optional<int> selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element[] items)
GridViewElement GridView(int selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element[] items)
GridViewElement GridView(int? selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element[] items)
TemplatedGridViewElement<T> GridView<T>(IReadOnlyList<T> items, Func<T, string> keySelector, Func<T, int, Element> viewBuilder)
```

| Overload | Description |
| --- | --- |
| `GridView(Element[])` |  |
| `GridView<T>(IReadOnlyList<T>, Func<T, int, Element>)` | [`IReactorKeyed`](../components/IReactorKeyed.md)-typed overload of [`GridView`](#gridview); `KeySelector` defaults to `t => t.Key`. (spec 042 §5) |
| `GridView(Optional<int>, Action<int>?, Element[])` | Creates a grid view whose selected index is controlled only when `selectedIndex` has a value. |
| `GridView(int, Action<int>?, Element[])` | Creates a grid view with a controlled selected index. Disambiguates a plain [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32) argument, which converts equally well to [`Nullable`](https://learn.microsoft.com/dotnet/api/system.nullable-1) and [`Optional<T>`](../components/Optional.md). Any [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32) — including `default`, which is `0` — selects that index; pass `(int?)null` or [`Optional.Unset`](../components/Optional.md#unset) to leave the selection uncontrolled. |
| `GridView(int?, Action<int>?, Element[])` | Creates a grid view whose selected index is unset when `selectedIndex` is `null`. |
| `GridView<T>(IReadOnlyList<T>, Func<T, string>, Func<T, int, Element>)` | Creates a typed, data-driven [`TemplatedGridViewElement<T>`](../elements/collections.md#templatedgridviewelementt) — the templated peer of WinUI's untyped `GridView`. |

**Returns:** [`GridViewElement`](../elements/collections.md#gridviewelement), [`TemplatedGridViewElement<T>`](../elements/collections.md#templatedgridviewelementt).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `items` | [`Element[]`](../elements/Element.md) or `IReadOnlyList<T>` |  |  |
| `viewBuilder` | `Func<T, int, Element>` |  |  |
| `selectedIndex` | [`Optional<int>`](../components/Optional.md) or `int` or `int?` |  |  |
| `onSelectedIndexChanged` | `Action<int>?` | `null` |  |
| `keySelector` | `Func<T, string>` |  |  |

**Remarks**

Reactor-original — see [`ListView`](#listview) for the templated-peer naming rationale. (spec 039 §0.3)

## `ItemContainer`

Wraps a child element in a WinUI `ItemContainer`. Required as the root element returned from an [`ItemsViewElement<T>`](../elements/collections.md#itemsviewelementt) view builder — ItemsView's selection / focus / animation infrastructure depends on it.

```csharp
ItemContainerElement ItemContainer(Element? child)
```

**Returns:** [`ItemContainerElement`](../elements/collections.md#itemcontainerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `child` | [`Element?`](../elements/Element.md) |  |

## `ItemsRepeater`

```csharp
ItemsRepeaterElement<T> ItemsRepeater<T>(IReadOnlyList<T> items, Func<T, int, Element> viewBuilder) where T : IReactorKeyed
ItemsRepeaterElement<T> ItemsRepeater<T>(IReadOnlyList<T> items, Func<T, string> keySelector, Func<T, int, Element> viewBuilder)
```

| Overload | Description |
| --- | --- |
| `ItemsRepeater<T>(IReadOnlyList<T>, Func<T, int, Element>)` | [`IReactorKeyed`](../components/IReactorKeyed.md)-typed overload of [`ItemsRepeater`](#itemsrepeater); `KeySelector` defaults to `t => t.Key`. |
| `ItemsRepeater<T>(IReadOnlyList<T>, Func<T, string>, Func<T, int, Element>)` | Creates a virtualized [`ItemsRepeaterElement<T>`](../elements/collections.md#itemsrepeaterelementt) — a bare `WinUI.ItemsRepeater` driven through the spec-042 keyed realization pipeline (same machinery LazyVStack/LazyHStack use, but without the implicit `ScrollViewer` wrap and with no hard-coded `StackLayout`). Author supplies a [`Layout`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.layout) instance via the `Layout` init-property (typically `UniformGridLayout` or `LinedFlowLayout`); host the result in a `ScrollViewer` / `ScrollView` / `RefreshContainer` for scrolling. Spec 047 §14 Phase 3 finish — Port (7). |

**Returns:** [`ItemsRepeaterElement<T>`](../elements/collections.md#itemsrepeaterelementt).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `items` | `IReadOnlyList<T>` |  |
| `viewBuilder` | `Func<T, int, Element>` |  |
| `keySelector` | `Func<T, string>` |  |

## `ItemsView`

```csharp
ItemsViewElement<T> ItemsView<T>(IReadOnlyList<T> items, Func<T, string> keySelector, Func<T, int, Element> viewBuilder)
```

**Returns:** [`ItemsViewElement<T>`](../elements/collections.md#itemsviewelementt).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `items` | `IReadOnlyList<T>` |  |
| `keySelector` | `Func<T, string>` |  |
| `viewBuilder` | `Func<T, int, Element>` |  |

## `LazyHStack`

```csharp
LazyHStackElement<T> LazyHStack<T>(IReadOnlyList<T> items, Func<T, int, Element> viewBuilder) where T : IReactorKeyed
LazyHStackElement<T> LazyHStack<T>(IReadOnlyList<T> items, Func<T, string> keySelector, Func<T, int, Element> viewBuilder)
```

| Overload | Description |
| --- | --- |
| `LazyHStack<T>(IReadOnlyList<T>, Func<T, int, Element>)` | [`IReactorKeyed`](../components/IReactorKeyed.md)-typed overload of [`LazyHStack`](#lazyhstack); `KeySelector` defaults to `t => t.Key`. (spec 042 §5) |
| `LazyHStack<T>(IReadOnlyList<T>, Func<T, string>, Func<T, int, Element>)` | Creates a virtualized horizontal stack of templated items — the horizontal peer of [`LazyVStack`](#lazyvstack). |

**Returns:** [`LazyHStackElement<T>`](../elements/collections.md#lazyhstackelementt).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `items` | `IReadOnlyList<T>` |  |
| `viewBuilder` | `Func<T, int, Element>` |  |
| `keySelector` | `Func<T, string>` |  |

**Remarks**

Reactor-original — see [`LazyVStack`](#lazyvstack) for the naming rationale. (spec 039 §0.3)

## `LazyVStack`

```csharp
LazyVStackElement<T> LazyVStack<T>(IReadOnlyList<T> items, Func<T, int, Element> viewBuilder) where T : IReactorKeyed
LazyVStackElement<T> LazyVStack<T>(IReadOnlyList<T> items, Func<T, string> keySelector, Func<T, int, Element> viewBuilder)
```

| Overload | Description |
| --- | --- |
| `LazyVStack<T>(IReadOnlyList<T>, Func<T, int, Element>)` | [`IReactorKeyed`](../components/IReactorKeyed.md)-typed overload of [`LazyVStack`](#lazyvstack); `KeySelector` defaults to `t => t.Key`. (spec 042 §5) |
| `LazyVStack<T>(IReadOnlyList<T>, Func<T, string>, Func<T, int, Element>)` | Creates a virtualized vertical stack of templated items. Backed by a WinUI `ItemsRepeater` inside a `ScrollViewer` — children are materialized on demand, so this scales to large item counts. |

**Returns:** [`LazyVStackElement<T>`](../elements/collections.md#lazyvstackelementt).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `items` | `IReadOnlyList<T>` |  |
| `viewBuilder` | `Func<T, int, Element>` |  |
| `keySelector` | `Func<T, string>` |  |

**Remarks**

Reactor-original — there is no WinUI `LazyVStack`; the name is borrowed from SwiftUI for the "vertical stack, lazy materialization" semantics. Prefer this over [`VStack`](layout.md#vstack) when the child count is large or the children are expensive to instantiate. (spec 039 §0.3)

## `ListBox`

Creates a [`ListBoxElement`](../elements/collections.md#listboxelement). `selectedIndex` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the control own its selection.

```csharp
ListBoxElement ListBox(string[] items, Optional<int> selectedIndex = default, Action<int>? onSelectedIndexChanged = null)
```

**Returns:** [`ListBoxElement`](../elements/collections.md#listboxelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `items` | `string[]` |  |  |
| `selectedIndex` | [`Optional<int>`](../components/Optional.md) | `default` |  |
| `onSelectedIndexChanged` | `Action<int>?` | `null` |  |

## `ListView`

```csharp
ListViewElement ListView(params Element[] items)
TemplatedListViewElement<T> ListView<T>(IReadOnlyList<T> items, Func<T, int, Element> viewBuilder) where T : IReactorKeyed
ListViewElement ListView(Optional<int> selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element[] items)
ListViewElement ListView(int selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element[] items)
ListViewElement ListView(int? selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element[] items)
TemplatedListViewElement<T> ListView<T>(IReadOnlyList<T> items, Func<T, string> keySelector, Func<T, int, Element> viewBuilder)
```

| Overload | Description |
| --- | --- |
| `ListView(Element[])` |  |
| `ListView<T>(IReadOnlyList<T>, Func<T, int, Element>)` | Creates a typed, data-driven [`TemplatedListViewElement<T>`](../elements/collections.md#templatedlistviewelementt) for items that implement [`IReactorKeyed`](../components/IReactorKeyed.md). The `KeySelector` defaults to `t => t.Key` so call sites can omit it. (spec 042 §5) |
| `ListView(Optional<int>, Action<int>?, Element[])` | Creates a list view whose selected index is controlled only when `selectedIndex` has a value. |
| `ListView(int, Action<int>?, Element[])` | Creates a list view with a controlled selected index. Disambiguates a plain [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32) argument, which converts equally well to [`Nullable`](https://learn.microsoft.com/dotnet/api/system.nullable-1) and [`Optional<T>`](../components/Optional.md). Any [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32) — including `default`, which is `0` — selects that index; pass `(int?)null` or [`Optional.Unset`](../components/Optional.md#unset) to leave the selection uncontrolled. |
| `ListView(int?, Action<int>?, Element[])` | Creates a list view whose selected index is unset when `selectedIndex` is `null`. |
| `ListView<T>(IReadOnlyList<T>, Func<T, string>, Func<T, int, Element>)` | Creates a typed, data-driven [`TemplatedListViewElement<T>`](../elements/collections.md#templatedlistviewelementt). The reconciler builds a WinUI `ListView` bound to `items` and instantiates one view per item via `viewBuilder`. |

**Returns:** [`ListViewElement`](../elements/collections.md#listviewelement), [`TemplatedListViewElement<T>`](../elements/collections.md#templatedlistviewelementt).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `items` | [`Element[]`](../elements/Element.md) or `IReadOnlyList<T>` |  |  |
| `viewBuilder` | `Func<T, int, Element>` |  |  |
| `selectedIndex` | [`Optional<int>`](../components/Optional.md) or `int` or `int?` |  |  |
| `onSelectedIndexChanged` | `Action<int>?` | `null` |  |
| `keySelector` | `Func<T, string>` |  |  |

**Remarks**

Reactor-original generic peer of WinUI's untyped `ListView`. The element record name is `TemplatedListViewElement<T>` (templated + typed) but the factory is the short `ListView`; the type parameter disambiguates from the existing untyped factory at the call site. (spec 039 §0.3)

## `RefreshContainer`

```csharp
RefreshContainerElement RefreshContainer(Element content, Action? onRefreshRequested = null)
```

**Returns:** [`RefreshContainerElement`](../elements/collections.md#refreshcontainerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `onRefreshRequested` | `Action?` | `null` |  |

## `SemanticZoom`

```csharp
SemanticZoomElement SemanticZoom(Element zoomedInView, Element zoomedOutView)
```

**Returns:** [`SemanticZoomElement`](../elements/collections.md#semanticzoomelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `zoomedInView` | [`Element`](../elements/Element.md) |  |
| `zoomedOutView` | [`Element`](../elements/Element.md) |  |

## `SwipeControl`

```csharp
SwipeControlElement SwipeControl(Element content, SwipeItemData[]? leftItems = null, SwipeItemData[]? rightItems = null)
```

**Returns:** [`SwipeControlElement`](../elements/collections.md#swipecontrolelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `leftItems` | [`SwipeItemData[]?`](../controls/collection-items.md#swipeitemdata) | `null` |  |
| `rightItems` | [`SwipeItemData[]?`](../controls/collection-items.md#swipeitemdata) | `null` |  |

## `TreeNode`

```csharp
TreeViewNodeData TreeNode(string content, params TreeViewNodeData[] children)
```

**Returns:** [`TreeViewNodeData`](../controls/collection-items.md#treeviewnodedata).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | `string` |  |
| `children` | [`TreeViewNodeData[]`](../controls/collection-items.md#treeviewnodedata) |  |

## `TreeView`

```csharp
TreeViewElement TreeView(params TreeViewNodeData[] nodes)
TemplatedTreeViewElement<T> TreeView<T>(IReadOnlyList<T> items, Func<T, IReadOnlyList<T>?> childrenSelector, Func<T, Element> viewBuilder) where T : IReactorKeyed
TemplatedTreeViewElement<T> TreeView<T>(IReadOnlyList<T> items, Func<T, string> keySelector, Func<T, IReadOnlyList<T>?> childrenSelector, Func<T, Element> viewBuilder)
```

| Overload | Description |
| --- | --- |
| `TreeView(TreeViewNodeData[])` |  |
| `TreeView<T>(IReadOnlyList<T>, Func<T, IReadOnlyList<T>?>, Func<T, Element>)` | [`IReactorKeyed`](../components/IReactorKeyed.md)-typed overload of [`TreeView`](#treeview); `KeySelector` defaults to `t => t.Key` so call sites can omit it. (spec 042 §5) |
| `TreeView<T>(IReadOnlyList<T>, Func<T, string>, Func<T, IReadOnlyList<T>?>, Func<T, Element>)` | Creates a typed, data-driven [`TemplatedTreeViewElement<T>`](../elements/collections.md#templatedtreeviewelementt) — the hierarchical peer of [`ListView`](#listview). The reconciler builds a WinUI `TreeView` from `items`, walks the hierarchy via `childrenSelector`, and renders each node via `viewBuilder` (the `ItemTemplate` equivalent — a `data → Element` function). |

**Returns:** [`TreeViewElement`](../elements/collections.md#treeviewelement), [`TemplatedTreeViewElement<T>`](../elements/collections.md#templatedtreeviewelementt).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `nodes` | [`TreeViewNodeData[]`](../controls/collection-items.md#treeviewnodedata) |  |
| `items` | `IReadOnlyList<T>` |  |
| `childrenSelector` | `Func<T, IReadOnlyList<T>?>` |  |
| `viewBuilder` | `Func<T, Element>` |  |
| `keySelector` | `Func<T, string>` |  |

**Remarks**

Heterogeneous nodes with per-shape visuals are expressed as a `switch` inside `viewBuilder` (the C# equivalent of WinUI's `ItemTemplateSelector`). This supersedes the deprecated [`TreeViewNodeData.ContentElement`](../controls/collection-items.md#contentelement) (issue #447). (spec 039 §0.3)
