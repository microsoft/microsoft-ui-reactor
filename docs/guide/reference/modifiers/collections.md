# Control modifiers: Collections

Modifiers that only the controls in the Collections area have.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Collections](../../collections.md)

## `Expanding`

```csharp
TreeViewElement Expanding(this TreeViewElement el, Action<TreeViewNodeData>? handler)
TemplatedTreeViewElement<T> Expanding<T>(this TemplatedTreeViewElement<T> el, Action<T>? handler)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Expanding(TreeViewElement, Action<TreeViewNodeData>?)` | Wires the expanding handler. Passing `null` clears. |
| `Expanding<T>(TemplatedTreeViewElement<T>, Action<T>?)` | Wires the expanding handler for the typed tree (receives the developer's `T`). Passing `null` clears. |

**Applies to:** [`TreeViewElement`](../elements/collections.md#treeviewelement), [`TemplatedTreeViewElement<T>`](../elements/collections.md#templatedtreeviewelementt).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TreeViewNodeData>?` or `Action<T>?` |  |

## `IncrementalLoadingTrigger`

Controls when incremental data sources fetch the next page.

```csharp
GridViewElement IncrementalLoadingTrigger(this GridViewElement el, IncrementalLoadingTrigger trigger)
ListViewElement IncrementalLoadingTrigger(this ListViewElement el, IncrementalLoadingTrigger trigger)
```

Declared in `ElementExtensions`.

**Applies to:** [`GridViewElement`](../elements/collections.md#gridviewelement), [`ListViewElement`](../elements/collections.md#listviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `trigger` | `IncrementalLoadingTrigger` |  |

## `ItemClick`

```csharp
GridViewElement ItemClick(this GridViewElement el, Action<int>? handler)
ListViewElement ItemClick(this ListViewElement el, Action<int>? handler)
TemplatedGridViewElement<T> ItemClick<T>(this TemplatedGridViewElement<T> el, Action<T>? handler)
TemplatedListViewElement<T> ItemClick<T>(this TemplatedListViewElement<T> el, Action<T>? handler)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `ItemClick(GridViewElement, Action<int>?)` | Wires the item-click handler (requires `IsItemClickEnabled`). Passing `null` clears. |
| `ItemClick(ListViewElement, Action<int>?)` | Wires the item-click handler (requires `IsItemClickEnabled`). Passing `null` clears. |
| `ItemClick<T>(TemplatedGridViewElement<T>, Action<T>?)` | Wires the item-click handler. Passing `null` clears. |
| `ItemClick<T>(TemplatedListViewElement<T>, Action<T>?)` | Wires the item-click handler. Passing `null` clears. |

**Applies to:** [`GridViewElement`](../elements/collections.md#gridviewelement), [`ListViewElement`](../elements/collections.md#listviewelement), [`TemplatedGridViewElement<T>`](../elements/collections.md#templatedgridviewelementt), [`TemplatedListViewElement<T>`](../elements/collections.md#templatedlistviewelementt).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<int>?` or `Action<T>?` |  |

## `ItemContainerStyle`

```csharp
GridViewElement ItemContainerStyle(this GridViewElement el, Style style)
ListViewElement ItemContainerStyle(this ListViewElement el, Style style)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `ItemContainerStyle(GridViewElement, Style)` | Style applied to each generated `GridViewItem` container. |
| `ItemContainerStyle(ListViewElement, Style)` | Style applied to each generated `ListViewItem` container. |

**Applies to:** [`GridViewElement`](../elements/collections.md#gridviewelement), [`ListViewElement`](../elements/collections.md#listviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `style` | `Style` |  |

## `PullDirection`

Direction the user pulls to trigger refresh.

```csharp
RefreshContainerElement PullDirection(this RefreshContainerElement el, RefreshPullDirection direction)
```

Declared in `ElementExtensions`.

**Applies to:** [`RefreshContainerElement`](../elements/collections.md#refreshcontainerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `direction` | `RefreshPullDirection` |  |

## `RefreshRequested`

Wires the refresh-requested handler. Passing `null` clears.

```csharp
RefreshContainerElement RefreshRequested(this RefreshContainerElement el, Action? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`RefreshContainerElement`](../elements/collections.md#refreshcontainerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `SetRepeater`

```csharp
LazyHStackElement<T> SetRepeater<T>(this LazyHStackElement<T> el, Action<ItemsRepeater> configure)
LazyVStackElement<T> SetRepeater<T>(this LazyVStackElement<T> el, Action<ItemsRepeater> configure)
```

Declared in `ElementExtensions`.

**Applies to:** [`LazyHStackElement<T>`](../elements/collections.md#lazyhstackelementt), [`LazyVStackElement<T>`](../elements/collections.md#lazyvstackelementt).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `configure` | `Action<ItemsRepeater>` |  |

## `VisibleRangeChanged`

Wires the visible-range-changed handler (receives `firstVisibleIndex` and `lastVisibleIndex`). Passing `null` clears.

```csharp
VirtualListElement VisibleRangeChanged(this VirtualListElement el, Action<int, int>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`VirtualListElement`](../elements/collections.md#virtuallistelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<int, int>?` |  |
