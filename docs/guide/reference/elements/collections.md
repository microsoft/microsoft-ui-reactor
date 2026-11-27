# Elements: Collections

Lists, grids, trees, flip views and virtualized collections.

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Collections](../../collections.md)

## `FlipViewElement`

The element record that describes a WinUI [`FlipView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.flipview) control.

```csharp
public record FlipViewElement(Element[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`FlipView`](../factories/collections.md#flipview)

**Modifiers:** [`SelectedIndexChanged`](../modifiers/shared-controls.md#selectedindexchanged), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Items` | [`Element[]`](Element.md) |  |
| `OnSelectedIndexChanged` | `Action<int>?` |  |
| `SelectedIndex` | [`Optional<int>`](../components/Optional.md) | Selected item index. Defaults to [`Optional.Unset`](../components/Optional.md#unset); use `Optional<int>.Of(-1)` to force no selection. |

## `GridViewElement`

```csharp
public record GridViewElement(Element[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`GridView`](../factories/collections.md#gridview)

**Modifiers:** [`IncrementalLoadingTrigger`](../modifiers/collections.md#incrementalloadingtrigger), [`ItemClick`](../modifiers/collections.md#itemclick), [`ItemContainerStyle`](../modifiers/collections.md#itemcontainerstyle), [`SelectedIndexChanged`](../modifiers/shared-controls.md#selectedindexchanged), [`SelectionChanged`](../modifiers/shared-controls.md#selectionchanged), [`SelectionMode`](../modifiers/shared-controls.md#selectionmode), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Header` | `string?` |  |
| `IncrementalLoadingTrigger` | `IncrementalLoadingTrigger` | Controls when incremental data sources fetch the next page. Defaults to `Edge`. |
| `ItemContainerStyle` | `Style?` | Style applied to each generated `GridViewItem` container. |
| `Items` | [`Element[]`](Element.md) |  |
| `OnItemClick` | `Action<int>?` |  |
| `OnSelectedIndexChanged` | `Action<int>?` |  |
| `OnSelectionChanged` | `Action<IReadOnlyList<int>>?` | Multi-select snapshot callback. See [`ListViewElement.OnSelectionChanged`](#listviewelement). |
| `SelectedIndex` | [`Optional<int>`](../components/Optional.md) | Selected item index. Defaults to [`Optional.Unset`](../components/Optional.md#unset); use `Optional<int>.Of(-1)` to force no selection. |
| `SelectionMode` | `ListViewSelectionMode` |  |

## `ItemContainerElement`

The element record that describes a WinUI [`ItemContainer`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.itemcontainer) control.

```csharp
public record ItemContainerElement(Element? Child) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`ItemContainer`](../factories/collections.md#itemcontainer)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Child` | [`Element?`](Element.md) |  |
| `IsSelected` | `bool` | Selection state as exposed by `ItemContainer.IsSelected`. |

## `ItemsRepeaterElement<T>`

Spec 047 §14 Phase 3 finish — Port (7). Typed peer of [`ItemsRepeaterElementBase`](#itemsrepeaterelementbase). The lambdas mirror the [`LazyVStackElement<T>`](#lazyvstackelementt) / [`LazyHStackElement<T>`](#lazyhstackelementt) shape (Items + KeySelector + ViewBuilder) so authors moving from Lazy\*Stack to a custom-layout ItemsRepeater find the surface identical.

```csharp
public record ItemsRepeaterElement<T>(IReadOnlyList<T> Items, Func<T, string> KeySelector, Func<T, int, Element> ViewBuilder) : ItemsRepeaterElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`ItemsRepeaterElementBase`](#itemsrepeaterelementbase).

**Created by:** [`ItemsRepeater`](../factories/collections.md#itemsrepeater)

| Property | Type | Description |
| --- | --- | --- |
| `ItemCount` | `int` |  |
| `Items` | `IReadOnlyList<T>` |  |
| `KeySelector` | `Func<T, string>` |  |
| `ViewBuilder` | `Func<T, int, Element>` |  |

| Member | Description |
| --- | --- |
| `BuildItemView(int)` |  |
| `CreateFactory(Reconciler, Action, ElementPool?)` |  |
| `RefreshRealizedItems(IElementFactory, ItemsRepeater)` |  |
| `TryUpdateFactory(IElementFactory)` |  |

## `ItemsRepeaterElementBase`

Spec 047 §14 Phase 3 finish — Port (7). Non-generic intermediate base for [`ItemsRepeaterElement<T>`](#itemsrepeaterelementt). Same role as [`LazyStackElementBase`](#lazystackelementbase): lets the reconciler match on a single type AND lets the descriptor's [`Reconciler.RegisterHandlerForDerivedTypes`](../extensibility/Reconciler.md#registerhandlerforderivedtypes) registration catch every closed-T variant. Implements `IKeyedItemSource` + `IItemsRepeaterFactorySource` for the Engine (1) ItemsRepeater arm in [`Reconciler.BindErasedKeyedItemsSource`](../extensibility/Reconciler.md).

**Distinct from [`LazyStackElementBase`](#lazystackelementbase):** no hard-coded [`StackLayout`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.stacklayout). The element exposes a nullable [`ItemsRepeaterElementBase.Layout`](#itemsrepeaterelementbase) property — the descriptor / legacy mount arm assigns it directly when non-null, otherwise leaves the ItemsRepeater on its default (Stack vertical). No [`ScrollViewer`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.scrollviewer) wrapping either — the rendered [`UIElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement) is the bare [`ItemsRepeater`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.itemsrepeater). Authors who need scrolling host the element inside their own `ScrollViewer` / `ScrollView` / `RefreshContainer`.

```csharp
public abstract record ItemsRepeaterElementBase : Element, IKeyedItemSource, IItemViewSource
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

| Property | Type | Description |
| --- | --- | --- |
| `ItemCount` | `int` | Total number of items in the source list. |
| `Layout` | `Layout?` | Optional WinUI [`Layout`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.layout). Null = leave the ItemsRepeater on its default layout (which itself defaults to vertical [`StackLayout`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.stacklayout)). Authors typically pass a `UniformGridLayout` or `LinedFlowLayout` instance configured up-front; the engine reuses it across renders by reference identity (no per-update Layout allocation). |

| Member | Description |
| --- | --- |
| `BuildItemView(int)` | Per-index Element factory — same shape as [`LazyStackElementBase.BuildItemView`](#lazystackelementbase). |
| `CreateFactory(Reconciler, Action, ElementPool?)` |  |
| `RefreshRealizedItems(IElementFactory, ItemsRepeater)` |  |
| `TryUpdateFactory(IElementFactory)` |  |

## `ItemsViewElement<T>`

```csharp
public record ItemsViewElement<T>(IReadOnlyList<T> Items, Func<T, string> KeySelector, Func<T, int, Element> ViewBuilder) : ItemsViewElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`ItemsViewElementBase`](#itemsviewelementbase).

**Created by:** [`ItemsView`](../factories/collections.md#itemsview)

**Modifiers:** [`ItemInvoked`](../modifiers/shared-controls.md#iteminvoked), [`SelectionChanged`](../modifiers/shared-controls.md#selectionchanged), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `ItemCount` | `int` |  |
| `Items` | `IReadOnlyList<T>` |  |
| `KeySelector` | `Func<T, string>` |  |
| `OnItemInvoked` | `Action<T>?` |  |
| `OnSelectionChanged` | `Action<IReadOnlyList<T>>?` | Multi-select snapshot callback. Receives the full list of currently selected items. Use this when [`ItemsViewElementBase.SelectionMode`](#itemsviewelementbase) is Multiple or Extended. |
| `ViewBuilder` | `Func<T, int, Element>` |  |

| Member | Description |
| --- | --- |
| `CreateFactory(Reconciler, Action, ElementPool?)` |  |
| `InvokeItemInvoked(int)` |  |
| `InvokeSelectionChanged(IReadOnlyList<int>)` |  |
| `RefreshRealizedItems(IElementFactory, ItemsRepeater)` |  |
| `TryUpdateFactory(IElementFactory)` |  |

## `ItemsViewElementBase`

Non-generic base so [`Reconciler`](../extensibility/Reconciler.md) can pattern-match an ItemsView element without knowing the user's item type. Mirrors the [`LazyStackElementBase`](#lazystackelementbase) / [`TemplatedListElementBase`](#templatedlistelementbase) shape: virtual hooks for factory creation, in-place update, per-row reconcile, and event callback dispatch.

```csharp
public abstract record ItemsViewElementBase : Element, IKeyedItemSource, IItemViewSource
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

| Property | Type | Description |
| --- | --- | --- |
| `IsItemInvokedEnabled` | `bool` |  |
| `ItemCount` | `int` | Total number of items in the source list. |
| `LayoutKind` | [`ItemsViewLayoutKind`](../controls/collection-items.md#itemsviewlayoutkind) |  |
| `SelectionMode` | `ItemsViewSelectionMode` |  |

| Member | Description |
| --- | --- |
| `CreateFactory(Reconciler, Action, ElementPool?)` |  |
| `InvokeItemInvoked(int)` | Dispatch an `ItemInvoked` event to the typed callback. |
| `InvokeSelectionChanged(IReadOnlyList<int>)` | Dispatch a `SelectionChanged` snapshot to the typed callback. |
| `RefreshRealizedItems(IElementFactory, ItemsRepeater)` |  |
| `TryUpdateFactory(IElementFactory)` |  |

## `LazyHStackElement<T>`

```csharp
public record LazyHStackElement<T>(IReadOnlyList<T> Items, Func<T, string> KeySelector, Func<T, int, Element> ViewBuilder) : LazyStackElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`LazyStackElementBase`](#lazystackelementbase).

**Created by:** [`LazyHStack`](../factories/collections.md#lazyhstack)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set), [`SetRepeater`](../modifiers/collections.md#setrepeater)

| Property | Type | Description |
| --- | --- | --- |
| `EstimatedItemSize` | `double` |  |
| `ItemCount` | `int` |  |
| `Items` | `IReadOnlyList<T>` |  |
| `KeySelector` | `Func<T, string>` |  |
| `Orientation` | `Orientation` |  |
| `Spacing` | `double` |  |
| `ViewBuilder` | `Func<T, int, Element>` |  |

| Member | Description |
| --- | --- |
| `BuildItemView(int)` |  |
| `CreateFactory(Reconciler, Action, ElementPool?)` |  |
| `GetItemsSource()` |  |
| `RefreshRealizedItems(IElementFactory, ItemsRepeater)` |  |
| `TryUpdateFactory(IElementFactory)` |  |

## `LazyStackElementBase`

Abstract base for virtualized lazy stacks. Non-generic so the reconciler can match on a single type in its switch expression.

Spec 047 §14 Phase 3 finish — Port (6): also implements `IKeyedItemSource` and `IItemsRepeaterFactorySource` so the descriptor-driven G2 port (`LazyStackDescriptor`) can flow Lazy\*Stack through [`Reconciler.BindErasedKeyedItemsSource`](../extensibility/Reconciler.md)'s ItemsRepeater arm — same realization plumbing as the hand-coded `MountLazyStack` / `UpdateLazyStack` bodies.

```csharp
public abstract record LazyStackElementBase : Element, IKeyedItemSource, IItemViewSource
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

| Property | Type | Description |
| --- | --- | --- |
| `EstimatedItemSize` | `double` |  |
| `ItemCount` | `int` | Total number of items in the source list. |
| `Orientation` | `Orientation` |  |
| `Spacing` | `double` |  |

| Member | Description |
| --- | --- |
| `BuildItemView(int)` | §14 Phase 3 finish — Port (6): build the per-item Element subtree for index N. Same shape as `TemplatedListElementBase.BuildItemView`; the descriptor binder reads this through the `IItemViewSource` contract (bridged via `IKeyedItemSource`) when the factory realizes a container. |
| `CreateFactory(Reconciler, Action, ElementPool?)` |  |
| `GetItemsSource()` |  |
| `RefreshRealizedItems(IElementFactory, ItemsRepeater)` | After updating the factory in place, reconcile all realized items with the new viewBuilder output (property diffs only, no collection changes). |
| `TryUpdateFactory(IElementFactory)` | Update an existing factory's items and viewBuilder in place, avoiding ItemsRepeater re-realization. Returns true if the factory was updated. |

## `LazyVStackElement<T>`

```csharp
public record LazyVStackElement<T>(IReadOnlyList<T> Items, Func<T, string> KeySelector, Func<T, int, Element> ViewBuilder) : LazyStackElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`LazyStackElementBase`](#lazystackelementbase).

**Created by:** [`LazyVStack`](../factories/collections.md#lazyvstack)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set), [`SetRepeater`](../modifiers/collections.md#setrepeater)

| Property | Type | Description |
| --- | --- | --- |
| `EstimatedItemSize` | `double` |  |
| `ItemCount` | `int` |  |
| `Items` | `IReadOnlyList<T>` |  |
| `KeySelector` | `Func<T, string>` |  |
| `Orientation` | `Orientation` |  |
| `Spacing` | `double` |  |
| `ViewBuilder` | `Func<T, int, Element>` |  |

| Member | Description |
| --- | --- |
| `BuildItemView(int)` |  |
| `CreateFactory(Reconciler, Action, ElementPool?)` |  |
| `GetItemsSource()` |  |
| `RefreshRealizedItems(IElementFactory, ItemsRepeater)` |  |
| `TryUpdateFactory(IElementFactory)` |  |

## `ListBoxElement`

The element record that describes a WinUI [`ListBox`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.listbox) control.

```csharp
public record ListBoxElement(string[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`ListBox`](../factories/collections.md#listbox)

**Modifiers:** [`SelectedIndexChanged`](../modifiers/shared-controls.md#selectedindexchanged), [`SelectionChanged`](../modifiers/shared-controls.md#selectionchanged), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Items` | `string[]` |  |
| `OnSelectedIndexChanged` | `Action<int>?` |  |
| `OnSelectionChanged` | `Action<IReadOnlyList<int>>?` | Multi-select snapshot callback. Receives the FULL list of currently selected indices. Use this in multi-select selection modes. |
| `SelectedIndex` | [`Optional<int>`](../components/Optional.md) | Selected item index. Defaults to [`Optional.Unset`](../components/Optional.md#unset); use `Optional<int>.Of(-1)` to force no selection. |

## `ListViewElement`

```csharp
public record ListViewElement(Element[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`ListView`](../factories/collections.md#listview)

**Modifiers:** [`IncrementalLoadingTrigger`](../modifiers/collections.md#incrementalloadingtrigger), [`ItemClick`](../modifiers/collections.md#itemclick), [`ItemContainerStyle`](../modifiers/collections.md#itemcontainerstyle), [`SelectedIndexChanged`](../modifiers/shared-controls.md#selectedindexchanged), [`SelectionChanged`](../modifiers/shared-controls.md#selectionchanged), [`SelectionMode`](../modifiers/shared-controls.md#selectionmode), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Header` | `string?` |  |
| `IncrementalLoadingTrigger` | `IncrementalLoadingTrigger` | Controls when incremental data sources fetch the next page. Defaults to `Edge`. |
| `ItemContainerStyle` | `Style?` | Style applied to each generated `ListViewItem` container (e.g. for padding, hover background). |
| `Items` | [`Element[]`](Element.md) |  |
| `OnItemClick` | `Action<int>?` |  |
| `OnSelectedIndexChanged` | `Action<int>?` |  |
| `OnSelectionChanged` | `Action<IReadOnlyList<int>>?` | Multi-select snapshot callback. Receives the FULL list of currently selected indices (snapshot semantics, matching [`CalendarViewElement.OnSelectedDatesChanged`](input.md#calendarviewelement)). Use this in Multiple / Extended selection modes — [`ListViewElement.OnSelectedIndexChanged`](#listviewelement) only carries the focused single index. Not raised on initial mount. |
| `SelectedIndex` | [`Optional<int>`](../components/Optional.md) | Selected item index. Defaults to [`Optional.Unset`](../components/Optional.md#unset); use `Optional<int>.Of(-1)` to force no selection. |
| `SelectionMode` | `ListViewSelectionMode` |  |

## `RefreshContainerElement`

The element record that describes a WinUI [`RefreshContainer`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.refreshcontainer) control.

```csharp
public record RefreshContainerElement(Element Content) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`RefreshContainer`](../factories/collections.md#refreshcontainer)

**Modifiers:** [`PullDirection`](../modifiers/collections.md#pulldirection), [`RefreshRequested`](../modifiers/collections.md#refreshrequested), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Content` | [`Element`](Element.md) |  |
| `OnRefreshRequested` | `Action?` |  |
| `PullDirection` | `RefreshPullDirection` | Direction the user pulls to trigger refresh. Defaults to `TopToBottom`. |

## `SemanticZoomElement`

The element record that describes a WinUI [`SemanticZoom`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.semanticzoom) control.

```csharp
public record SemanticZoomElement(Element ZoomedInView, Element ZoomedOutView) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`SemanticZoom`](../factories/collections.md#semanticzoom)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `ZoomedInView` | [`Element`](Element.md) |  |
| `ZoomedOutView` | [`Element`](Element.md) |  |

## `SwipeControlElement`

The element record that describes a WinUI [`SwipeControl`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.swipecontrol) control.

```csharp
public record SwipeControlElement(Element Content) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`SwipeControl`](../factories/collections.md#swipecontrol)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Content` | [`Element`](Element.md) |  |
| `LeftItems` | [`SwipeItemData[]?`](../controls/collection-items.md#swipeitemdata) | Items revealed when swiping from the left. When [`SwipeControlElement.LeftItemsMode`](#swipecontrolelement) is [`SwipeMode.Execute`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.swipemode.execute), WinUI supports only one item: Reactor keeps the first and ignores the rest (with a diagnostic warning). |
| `LeftItemsMode` | `SwipeMode` |  |
| `RightItems` | [`SwipeItemData[]?`](../controls/collection-items.md#swipeitemdata) | Items revealed when swiping from the right. When [`SwipeControlElement.RightItemsMode`](#swipecontrolelement) is [`SwipeMode.Execute`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.swipemode.execute), WinUI supports only one item: Reactor keeps the first and ignores the rest (with a diagnostic warning). |
| `RightItemsMode` | `SwipeMode` |  |

## `TemplatedFlipViewElement<T>`

```csharp
public record TemplatedFlipViewElement<T>(IReadOnlyList<T> Items, Func<T, string> KeySelector, Func<T, int, Element> ViewBuilder) : TemplatedFlipViewElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`TemplatedFlipViewElementBase`](#templatedflipviewelementbase).

**Created by:** [`FlipView`](../factories/collections.md#flipview)

**Modifiers:** [`SelectedIndexChanged`](../modifiers/shared-controls.md#selectedindexchanged), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `ItemCount` | `int` |  |
| `Items` | `IReadOnlyList<T>` |  |
| `KeySelector` | `Func<T, string>` |  |
| `OnSelectedIndexChanged` | `Action<int>?` |  |
| `SelectedIndex` | [`Optional<int>`](../components/Optional.md) | Selected item index. Defaults to [`Optional.Unset`](../components/Optional.md#unset); use `Optional<int>.Of(-1)` to force no selection. |
| `ViewBuilder` | `Func<T, int, Element>` |  |

| Member | Description |
| --- | --- |
| `ApplyControlSetters(object)` |  |
| `BuildItemView(int)` |  |
| `GetHeader()` |  |
| `GetIsItemClickEnabled()` |  |
| `GetSelectedIndex()` |  |
| `GetSelectionMode()` |  |
| `InvokeItemClick(int)` |  |
| `InvokeSelectionChanged(int)` |  |

## `TemplatedFlipViewElementBase`

Spec 047 §14 Phase 3 close-out — see [`TemplatedListViewElementBase`](#templatedlistviewelementbase). FlipView descriptor port is carved to Phase 4 because FlipView does not support `ContainerContentChanging` (pre-mounts items via a different shape); the marker is reserved so the symmetry is visible in the element hierarchy.

```csharp
public abstract record TemplatedFlipViewElementBase : TemplatedListElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`TemplatedListElementBase`](#templatedlistelementbase).

| Property | Type | Description |
| --- | --- | --- |
| `ControlKind` | [`TemplatedControlKind`](../controls/collection-items.md#templatedcontrolkind) |  |

## `TemplatedGridViewElement<T>`

```csharp
public record TemplatedGridViewElement<T>(IReadOnlyList<T> Items, Func<T, string> KeySelector, Func<T, int, Element> ViewBuilder) : TemplatedGridViewElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`TemplatedGridViewElementBase`](#templatedgridviewelementbase).

**Created by:** [`GridView`](../factories/collections.md#gridview)

**Modifiers:** [`ItemClick`](../modifiers/collections.md#itemclick), [`SelectedIndexChanged`](../modifiers/shared-controls.md#selectedindexchanged), [`SelectionChanged`](../modifiers/shared-controls.md#selectionchanged), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Header` | `string?` |  |
| `ItemCount` | `int` |  |
| `Items` | `IReadOnlyList<T>` |  |
| `KeySelector` | `Func<T, string>` |  |
| `OnItemClick` | `Action<T>?` |  |
| `OnSelectedIndexChanged` | `Action<int>?` |  |
| `OnSelectionChanged` | `Action<IReadOnlyList<T>>?` | Multi-select snapshot callback for the typed peer (see [`TemplatedListViewElement.OnSelectionChanged`](#templatedlistviewelementt)). |
| `SelectedIndex` | `int` |  |
| `SelectionMode` | `ListViewSelectionMode` |  |
| `ViewBuilder` | `Func<T, int, Element>` |  |

| Member | Description |
| --- | --- |
| `ApplyControlSetters(object)` |  |
| `BuildItemView(int)` |  |
| `GetHeader()` |  |
| `GetIsItemClickEnabled()` |  |
| `GetSelectedIndex()` |  |
| `GetSelectionMode()` |  |
| `InvokeItemClick(int)` |  |
| `InvokeSelectionChanged(int)` |  |

## `TemplatedGridViewElementBase`

Spec 047 §14 Phase 3 close-out — see [`TemplatedListViewElementBase`](#templatedlistviewelementbase).

```csharp
public abstract record TemplatedGridViewElementBase : TemplatedListElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`TemplatedListElementBase`](#templatedlistelementbase).

| Property | Type | Description |
| --- | --- | --- |
| `ControlKind` | [`TemplatedControlKind`](../controls/collection-items.md#templatedcontrolkind) |  |

## `TemplatedListElementBase`

Abstract base for data-driven items controls. Non-generic so the reconciler can match on a single type in its switch expression (same pattern as LazyStackElementBase).

```csharp
public abstract record TemplatedListElementBase : Element, IItemViewSource, IKeyedItemSource
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

| Property | Type | Description |
| --- | --- | --- |
| `ControlKind` | [`TemplatedControlKind`](../controls/collection-items.md#templatedcontrolkind) |  |
| `ItemCount` | `int` |  |

| Member | Description |
| --- | --- |
| `ApplyControlSetters(object)` |  |
| `BuildItemView(int)` |  |
| `GetHeader()` |  |
| `GetIsItemClickEnabled()` |  |
| `GetSelectedIndex()` |  |
| `GetSelectionMode()` |  |
| `InvokeItemClick(int)` |  |
| `InvokeSelectionChanged(int)` |  |

## `TemplatedListViewElement<T>`

```csharp
public record TemplatedListViewElement<T>(IReadOnlyList<T> Items, Func<T, string> KeySelector, Func<T, int, Element> ViewBuilder) : TemplatedListViewElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`TemplatedListViewElementBase`](#templatedlistviewelementbase).

**Created by:** [`ListView`](../factories/collections.md#listview)

**Modifiers:** [`ItemClick`](../modifiers/collections.md#itemclick), [`SelectedIndexChanged`](../modifiers/shared-controls.md#selectedindexchanged), [`SelectionChanged`](../modifiers/shared-controls.md#selectionchanged), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Header` | `string?` |  |
| `ItemCount` | `int` |  |
| `Items` | `IReadOnlyList<T>` |  |
| `KeySelector` | `Func<T, string>` |  |
| `OnItemClick` | `Action<T>?` |  |
| `OnSelectedIndexChanged` | `Action<int>?` |  |
| `OnSelectionChanged` | `Action<IReadOnlyList<T>>?` | Multi-select snapshot callback for the typed peer. Receives the full list of currently selected items (not just indices). Snapshot semantics — not raised on initial mount. |
| `SelectedIndex` | `int` |  |
| `SelectionMode` | `ListViewSelectionMode` |  |
| `ViewBuilder` | `Func<T, int, Element>` |  |

| Member | Description |
| --- | --- |
| `ApplyControlSetters(object)` |  |
| `BuildItemView(int)` |  |
| `GetHeader()` |  |
| `GetIsItemClickEnabled()` |  |
| `GetSelectedIndex()` |  |
| `GetSelectionMode()` |  |
| `InvokeItemClick(int)` |  |
| `InvokeSelectionChanged(int)` |  |

## `TemplatedListViewElementBase`

Spec 047 §14 Phase 3 close-out — empty marker intermediate that lets the v1 handler registry route every closed `TemplatedListViewElement<T>` through a single base-derived descriptor registration without an open-generic resolver. Adds no fields and no overrides, so record equality on the leaf type `TemplatedListViewElement<T>` is unchanged.

```csharp
public abstract record TemplatedListViewElementBase : TemplatedListElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`TemplatedListElementBase`](#templatedlistelementbase).

| Property | Type | Description |
| --- | --- | --- |
| `ControlKind` | [`TemplatedControlKind`](../controls/collection-items.md#templatedcontrolkind) |  |

## `TemplatedTreeViewElement<T>`

Typed, data-driven `TreeView`. The hierarchical peer of [`TemplatedListViewElement<T>`](#templatedlistviewelementt). See [`TemplatedTreeViewElementBase`](#templatedtreeviewelementbase).

```csharp
public record TemplatedTreeViewElement<T>(IReadOnlyList<T> Items, Func<T, string> KeySelector, Func<T, IReadOnlyList<T>?> ChildrenSelector, Func<T, Element> ViewBuilder) : TemplatedTreeViewElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`TemplatedTreeViewElementBase`](#templatedtreeviewelementbase).

**Created by:** [`TreeView`](../factories/collections.md#treeview)

**Modifiers:** [`Expanding`](../modifiers/collections.md#expanding), [`ItemInvoked`](../modifiers/shared-controls.md#iteminvoked)

| Property | Type | Description |
| --- | --- | --- |
| `AllowDrop` | `bool` |  |
| `CanDragItems` | `bool` |  |
| `CanReorderItems` | `bool` |  |
| `ChildrenSelector` | `Func<T, IReadOnlyList<T>?>` |  |
| `IsExpanded` | `Func<T, bool>?` | Per-item initial-expansion selector. Defaults to collapsed. |
| `Items` | `IReadOnlyList<T>` |  |
| `KeySelector` | `Func<T, string>` |  |
| `OnExpanding` | `Action<T>?` | Invoked with the developer's `T` just before a node expands. |
| `OnItemInvoked` | `Action<T>?` | Invoked with the developer's `T` when a node is clicked/invoked. |
| `SelectionMode` | `TreeViewSelectionMode` |  |
| `ViewBuilder` | `Func<T, Element>` |  |

| Member | Description |
| --- | --- |
| `ApplyControlSetters(object)` |  |
| `BuildView(object)` |  |
| `GetAllowDrop()` |  |
| `GetCanDragItems()` |  |
| `GetCanReorderItems()` |  |
| `GetChildren(object)` |  |
| `GetIsExpanded(object)` |  |
| `GetKey(object)` |  |
| `GetRoots()` |  |
| `GetSelectionMode()` |  |
| `InvokeExpanding(object)` |  |
| `InvokeItemInvoked(object)` |  |

## `TemplatedTreeViewElementBase`

Abstract non-generic base for the typed, data-driven `TreeView`. Non-generic so the reconciler can match a single type in its switch expression (same type-erasure pattern as [`TemplatedListElementBase`](#templatedlistelementbase)).

This is the hierarchical peer of [`TemplatedListViewElement<T>`](#templatedlistviewelementt): the developer supplies their own data items, a key selector, a children selector (the hierarchy), and a `viewBuilder` (`data → Element`, the WinUI `ItemTemplate` equivalent). It exists because WinUI's node-mode `TreeView` stringifies `TreeViewNode.Content` and cannot host a pre-built `UIElement` — rich per-node visuals must come from a template, never an element instance (the root cause of issue #447).

The base exposes object-erased accessors; the generic leaf casts back to `T`. Reference-type `T` flows through the covariant [`IReadOnlyList`](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1) → `IReadOnlyList<object>` conversion; value-type `T` is boxed once via the leaf's projection helper.

```csharp
public abstract record TemplatedTreeViewElementBase : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

| Member | Description |
| --- | --- |
| `ApplyControlSetters(object)` |  |
| `BuildView(object)` | Builds the per-node view (the `ItemTemplate` equivalent). |
| `GetAllowDrop()` |  |
| `GetCanDragItems()` |  |
| `GetCanReorderItems()` |  |
| `GetChildren(object)` | The children of `item`, or null for a leaf. |
| `GetIsExpanded(object)` | Whether `item`'s node should start expanded. |
| `GetKey(object)` | The stable identity string for `item` (the keyed-diff key). |
| `GetRoots()` | The root data items (object-erased), in document order. |
| `GetSelectionMode()` |  |
| `InvokeExpanding(object)` | Dispatches `OnExpanding` with the developer's own `T`. |
| `InvokeItemInvoked(object)` | Dispatches `OnItemInvoked` with the developer's own `T`. |

## `TreeViewElement`

The element record that describes a WinUI [`TreeView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.treeview) control.

```csharp
public record TreeViewElement(TreeViewNodeData[] Nodes) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`TreeView`](../factories/collections.md#treeview)

**Modifiers:** [`Expanding`](../modifiers/collections.md#expanding), [`ItemInvoked`](../modifiers/shared-controls.md#iteminvoked), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `AllowDrop` | `bool` |  |
| `CanDragItems` | `bool` |  |
| `CanReorderItems` | `bool` |  |
| `Nodes` | [`TreeViewNodeData[]`](../controls/collection-items.md#treeviewnodedata) |  |
| `OnExpanding` | `Action<TreeViewNodeData>?` |  |
| `OnItemInvoked` | `Action<TreeViewNodeData>?` |  |
| `SelectionMode` | `TreeViewSelectionMode` |  |

## `VirtualListElement`

Element record for the VirtualList component — a count-based virtualized list backed by WinUI's ItemsRepeater. Unlike LazyVStack which takes a concrete IReadOnlyList\<T\>, VirtualList works with an item count and a render callback, making it suitable for data-source-driven scenarios where items are fetched on demand.

```csharp
public record VirtualListElement : Element
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Element`](Element.md).

**Modifiers:** [`VisibleRangeChanged`](../modifiers/collections.md#visiblerangechanged)

| Property | Type | Description |
| --- | --- | --- |
| `EstimatedItemHeight` | `double` | Estimated item height for variable-height mode (default 40px). Used for initial scroll extent calculation before items are measured. Ignored when ItemHeight is set. |
| `GetItemKey` | `Func<int, string>?` | Optional key function for stable identity across re-renders. When provided, the reconciler uses this to match items across reorderings. When omitted, identity is positional: an unchanged item count preserves row identities without projecting all keys again. Realized content still refreshes. |
| `ItemCount` | `int` | Total number of items (drives the virtualizer's extent). |
| `ItemHeight` | `double?` | Fixed item height in pixels. When set, enables the fixed-height fast path with O(1) offset calculation — no per-item measurement needed. When null, variable-height mode is used with EstimatedItemHeight. |
| `OnVisibleRangeChanged` | `Action<int, int>?` | Callback fired when the visible range changes (viewport tracking). Receives the first and last visible item indices. Native view changes use the current callback, item height (or estimate), and spacing. Setting this to null stops notifications without remounting. |
| `Ref` | `Action<VirtualListRef>?` | Callback ref for scroll operations. When set, receives a VirtualListRef that exposes ScrollToIndex and scroll position save/restore. |
| `RenderItem` | `Func<int, Element>` | Render callback: given an index, return the Element for that row. Called only for visible items (virtualized). |
| `Spacing` | `double` | Spacing between items in pixels (default 0). |
