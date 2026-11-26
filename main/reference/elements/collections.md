---
title: "Elements: Collections"
description: "Lists, grids, trees, flip views and virtualized collections."
sidebar:
  label: "Collections"
---

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Collections](/microsoft-ui-reactor/main/controls/collections)

## `FlipViewElement` [#flipviewelement]

The element record that describes a WinUI [`FlipView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.flipview) control.

```csharp
public record FlipViewElement(Element[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`FlipView`](/microsoft-ui-reactor/main/reference/factories/collections#flipview)

**Modifiers:** [`SelectedIndexChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectedindexchanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Description |
| --- | --- | --- |
| `Items` | [`Element[]`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `OnSelectedIndexChanged` | `Action<int>?` |  |
| `SelectedIndex` | [`Optional<int>`](/microsoft-ui-reactor/main/reference/components/Optional) | Selected item index. Defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset); use `Optional<int>.Of(-1)` to force no selection. |

## `GridViewElement` [#gridviewelement]

```csharp
public record GridViewElement(Element[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`GridView`](/microsoft-ui-reactor/main/reference/factories/collections#gridview)

**Modifiers:** [`IncrementalLoadingTrigger`](/microsoft-ui-reactor/main/reference/modifiers/collections#incrementalloadingtrigger), [`ItemClick`](/microsoft-ui-reactor/main/reference/modifiers/collections#itemclick), [`ItemContainerStyle`](/microsoft-ui-reactor/main/reference/modifiers/collections#itemcontainerstyle), [`SelectedIndexChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectedindexchanged), [`SelectionChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectionchanged), [`SelectionMode`](/microsoft-ui-reactor/main/reference/modifiers/collections#selectionmode), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Description |
| --- | --- | --- |
| `Header` | `string?` |  |
| `IncrementalLoadingTrigger` | `IncrementalLoadingTrigger` | Controls when incremental data sources fetch the next page. Defaults to `Edge`. |
| `ItemContainerStyle` | `Style?` | Style applied to each generated `GridViewItem` container. |
| `Items` | [`Element[]`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `OnItemClick` | `Action<int>?` |  |
| `OnSelectedIndexChanged` | `Action<int>?` |  |
| `OnSelectionChanged` | `Action<IReadOnlyList<int>>?` | Multi-select snapshot callback. See [`ListViewElement.OnSelectionChanged`](#listviewelement). |
| `SelectedIndex` | [`Optional<int>`](/microsoft-ui-reactor/main/reference/components/Optional) | Selected item index. Defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset); use `Optional<int>.Of(-1)` to force no selection. |
| `SelectionMode` | `ListViewSelectionMode` |  |

## `ItemContainerElement` [#itemcontainerelement]

The element record that describes a WinUI [`ItemContainer`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.itemcontainer) control.

```csharp
public record ItemContainerElement(Element? Child) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`ItemContainer`](/microsoft-ui-reactor/main/reference/factories/collections#itemcontainer)

**Modifiers:** [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Description |
| --- | --- | --- |
| `Child` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `IsSelected` | `bool` | Selection state as exposed by `ItemContainer.IsSelected`. |

## `ItemsRepeaterElement<T>` [#itemsrepeaterelementt]

Spec 047 §14 Phase 3 finish — Port (7). Typed peer of [`ItemsRepeaterElementBase`](#itemsrepeaterelementbase). The lambdas mirror the [`LazyVStackElement<T>`](#lazyvstackelementt) / [`LazyHStackElement<T>`](#lazyhstackelementt) shape (Items + KeySelector + ViewBuilder) so authors moving from Lazy\*Stack to a custom-layout ItemsRepeater find the surface identical.

```csharp
public record ItemsRepeaterElement<T>(IReadOnlyList<T> Items, Func<T, string> KeySelector, Func<T, int, Element> ViewBuilder) : ItemsRepeaterElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`ItemsRepeaterElementBase`](#itemsrepeaterelementbase).

**Created by:** [`ItemsRepeater`](/microsoft-ui-reactor/main/reference/factories/collections#itemsrepeater)

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

## `ItemsRepeaterElementBase` [#itemsrepeaterelementbase]

Spec 047 §14 Phase 3 finish — Port (7). Non-generic intermediate base for [`ItemsRepeaterElement<T>`](#itemsrepeaterelementt). Same role as [`LazyStackElementBase`](#lazystackelementbase): lets the reconciler match on a single type AND lets the descriptor's [`Reconciler.RegisterHandlerForDerivedTypes`](/microsoft-ui-reactor/main/reference/extensibility/Reconciler#registerhandlerforderivedtypes) registration catch every closed-T variant. Implements `IKeyedItemSource` + `IItemsRepeaterFactorySource` for the Engine (1) ItemsRepeater arm in [`Reconciler.BindErasedKeyedItemsSource`](/microsoft-ui-reactor/main/reference/extensibility/Reconciler).

**Distinct from [`LazyStackElementBase`](#lazystackelementbase):** no hard-coded [`StackLayout`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.stacklayout). The element exposes a nullable [`ItemsRepeaterElementBase.Layout`](#itemsrepeaterelementbase) property — the descriptor / legacy mount arm assigns it directly when non-null, otherwise leaves the ItemsRepeater on its default (Stack vertical). No [`ScrollViewer`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.scrollviewer) wrapping either — the rendered [`UIElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement) is the bare [`ItemsRepeater`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.itemsrepeater). Authors who need scrolling host the element inside their own `ScrollViewer` / `ScrollView` / `RefreshContainer`.

```csharp
public abstract record ItemsRepeaterElementBase : Element, IKeyedItemSource, IItemViewSource
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

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

## `ItemsViewElement<T>` [#itemsviewelementt]

```csharp
public record ItemsViewElement<T>(IReadOnlyList<T> Items, Func<T, string> KeySelector, Func<T, int, Element> ViewBuilder) : ItemsViewElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`ItemsViewElementBase`](#itemsviewelementbase).

**Created by:** [`ItemsView`](/microsoft-ui-reactor/main/reference/factories/collections#itemsview)

**Modifiers:** [`ItemInvoked`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#iteminvoked), [`SelectionChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectionchanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

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

## `ItemsViewElementBase` [#itemsviewelementbase]

Non-generic base so [`Reconciler`](/microsoft-ui-reactor/main/reference/extensibility/Reconciler) can pattern-match an ItemsView element without knowing the user's item type. Mirrors the [`LazyStackElementBase`](#lazystackelementbase) / [`TemplatedListElementBase`](#templatedlistelementbase) shape: virtual hooks for factory creation, in-place update, per-row reconcile, and event callback dispatch.

```csharp
public abstract record ItemsViewElementBase : Element, IKeyedItemSource, IItemViewSource
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

| Property | Type | Description |
| --- | --- | --- |
| `IsItemInvokedEnabled` | `bool` |  |
| `ItemCount` | `int` | Total number of items in the source list. |
| `LayoutKind` | [`ItemsViewLayoutKind`](/microsoft-ui-reactor/main/reference/controls/collection-items#itemsviewlayoutkind) |  |
| `SelectionMode` | `ItemsViewSelectionMode` |  |

| Member | Description |
| --- | --- |
| `CreateFactory(Reconciler, Action, ElementPool?)` |  |
| `InvokeItemInvoked(int)` | Dispatch an `ItemInvoked` event to the typed callback. |
| `InvokeSelectionChanged(IReadOnlyList<int>)` | Dispatch a `SelectionChanged` snapshot to the typed callback. |
| `RefreshRealizedItems(IElementFactory, ItemsRepeater)` |  |
| `TryUpdateFactory(IElementFactory)` |  |

## `LazyHStackElement<T>` [#lazyhstackelementt]

```csharp
public record LazyHStackElement<T>(IReadOnlyList<T> Items, Func<T, string> KeySelector, Func<T, int, Element> ViewBuilder) : LazyStackElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`LazyStackElementBase`](#lazystackelementbase).

**Created by:** [`LazyHStack`](/microsoft-ui-reactor/main/reference/factories/collections#lazyhstack)

**Modifiers:** [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`SetRepeater`](/microsoft-ui-reactor/main/reference/modifiers/collections#setrepeater)

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

## `LazyStackElementBase` [#lazystackelementbase]

Abstract base for virtualized lazy stacks. Non-generic so the reconciler can match on a single type in its switch expression.

Spec 047 §14 Phase 3 finish — Port (6): also implements `IKeyedItemSource` and `IItemsRepeaterFactorySource` so the descriptor-driven G2 port (`LazyStackDescriptor`) can flow Lazy\*Stack through [`Reconciler.BindErasedKeyedItemsSource`](/microsoft-ui-reactor/main/reference/extensibility/Reconciler)'s ItemsRepeater arm — same realization plumbing as the hand-coded `MountLazyStack` / `UpdateLazyStack` bodies.

```csharp
public abstract record LazyStackElementBase : Element, IKeyedItemSource, IItemViewSource
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

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

## `LazyVStackElement<T>` [#lazyvstackelementt]

```csharp
public record LazyVStackElement<T>(IReadOnlyList<T> Items, Func<T, string> KeySelector, Func<T, int, Element> ViewBuilder) : LazyStackElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`LazyStackElementBase`](#lazystackelementbase).

**Created by:** [`LazyVStack`](/microsoft-ui-reactor/main/reference/factories/collections#lazyvstack)

**Modifiers:** [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`SetRepeater`](/microsoft-ui-reactor/main/reference/modifiers/collections#setrepeater)

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

## `ListBoxElement` [#listboxelement]

The element record that describes a WinUI [`ListBox`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.listbox) control.

```csharp
public record ListBoxElement(string[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`ListBox`](/microsoft-ui-reactor/main/reference/factories/collections#listbox)

**Modifiers:** [`SelectedIndexChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectedindexchanged), [`SelectionChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectionchanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Description |
| --- | --- | --- |
| `Items` | `string[]` |  |
| `OnSelectedIndexChanged` | `Action<int>?` |  |
| `OnSelectionChanged` | `Action<IReadOnlyList<int>>?` | Multi-select snapshot callback. Receives the FULL list of currently selected indices. Use this in multi-select selection modes. |
| `SelectedIndex` | [`Optional<int>`](/microsoft-ui-reactor/main/reference/components/Optional) | Selected item index. Defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset); use `Optional<int>.Of(-1)` to force no selection. |

## `ListViewElement` [#listviewelement]

```csharp
public record ListViewElement(Element[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`ListView`](/microsoft-ui-reactor/main/reference/factories/collections#listview)

**Modifiers:** [`IncrementalLoadingTrigger`](/microsoft-ui-reactor/main/reference/modifiers/collections#incrementalloadingtrigger), [`ItemClick`](/microsoft-ui-reactor/main/reference/modifiers/collections#itemclick), [`ItemContainerStyle`](/microsoft-ui-reactor/main/reference/modifiers/collections#itemcontainerstyle), [`SelectedIndexChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectedindexchanged), [`SelectionChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectionchanged), [`SelectionMode`](/microsoft-ui-reactor/main/reference/modifiers/collections#selectionmode), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Description |
| --- | --- | --- |
| `Header` | `string?` |  |
| `IncrementalLoadingTrigger` | `IncrementalLoadingTrigger` | Controls when incremental data sources fetch the next page. Defaults to `Edge`. |
| `ItemContainerStyle` | `Style?` | Style applied to each generated `ListViewItem` container (e.g. for padding, hover background). |
| `Items` | [`Element[]`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `OnItemClick` | `Action<int>?` |  |
| `OnSelectedIndexChanged` | `Action<int>?` |  |
| `OnSelectionChanged` | `Action<IReadOnlyList<int>>?` | Multi-select snapshot callback. Receives the FULL list of currently selected indices (snapshot semantics, matching [`CalendarViewElement.OnSelectedDatesChanged`](/microsoft-ui-reactor/main/reference/elements/input#calendarviewelement)). Use this in Multiple / Extended selection modes — [`ListViewElement.OnSelectedIndexChanged`](#listviewelement) only carries the focused single index. Not raised on initial mount. |
| `SelectedIndex` | [`Optional<int>`](/microsoft-ui-reactor/main/reference/components/Optional) | Selected item index. Defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset); use `Optional<int>.Of(-1)` to force no selection. |
| `SelectionMode` | `ListViewSelectionMode` |  |

## `RefreshContainerElement` [#refreshcontainerelement]

The element record that describes a WinUI [`RefreshContainer`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.refreshcontainer) control.

```csharp
public record RefreshContainerElement(Element Content) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`RefreshContainer`](/microsoft-ui-reactor/main/reference/factories/collections#refreshcontainer)

**Modifiers:** [`PullDirection`](/microsoft-ui-reactor/main/reference/modifiers/collections#pulldirection), [`RefreshRequested`](/microsoft-ui-reactor/main/reference/modifiers/collections#refreshrequested), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Description |
| --- | --- | --- |
| `Content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `OnRefreshRequested` | `Action?` |  |
| `PullDirection` | `RefreshPullDirection` | Direction the user pulls to trigger refresh. Defaults to `TopToBottom`. |

## `SemanticZoomElement` [#semanticzoomelement]

The element record that describes a WinUI [`SemanticZoom`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.semanticzoom) control.

```csharp
public record SemanticZoomElement(Element ZoomedInView, Element ZoomedOutView) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`SemanticZoom`](/microsoft-ui-reactor/main/reference/factories/collections#semanticzoom)

**Modifiers:** [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Description |
| --- | --- | --- |
| `ZoomedInView` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `ZoomedOutView` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

## `SwipeControlElement` [#swipecontrolelement]

The element record that describes a WinUI [`SwipeControl`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.swipecontrol) control.

```csharp
public record SwipeControlElement(Element Content) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`SwipeControl`](/microsoft-ui-reactor/main/reference/factories/collections#swipecontrol)

**Modifiers:** [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Description |
| --- | --- | --- |
| `Content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `LeftItems` | [`SwipeItemData[]?`](/microsoft-ui-reactor/main/reference/controls/collection-items#swipeitemdata) | Items revealed when swiping from the left. When [`SwipeControlElement.LeftItemsMode`](#swipecontrolelement) is [`SwipeMode.Execute`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.swipemode.execute), WinUI supports only one item: Reactor keeps the first and ignores the rest (with a diagnostic warning). |
| `LeftItemsMode` | `SwipeMode` |  |
| `RightItems` | [`SwipeItemData[]?`](/microsoft-ui-reactor/main/reference/controls/collection-items#swipeitemdata) | Items revealed when swiping from the right. When [`SwipeControlElement.RightItemsMode`](#swipecontrolelement) is [`SwipeMode.Execute`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.swipemode.execute), WinUI supports only one item: Reactor keeps the first and ignores the rest (with a diagnostic warning). |
| `RightItemsMode` | `SwipeMode` |  |

## `TemplatedFlipViewElement<T>` [#templatedflipviewelementt]

```csharp
public record TemplatedFlipViewElement<T>(IReadOnlyList<T> Items, Func<T, string> KeySelector, Func<T, int, Element> ViewBuilder) : TemplatedFlipViewElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`TemplatedFlipViewElementBase`](#templatedflipviewelementbase).

**Created by:** [`FlipView`](/microsoft-ui-reactor/main/reference/factories/collections#flipview)

**Modifiers:** [`SelectedIndexChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectedindexchanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Description |
| --- | --- | --- |
| `ItemCount` | `int` |  |
| `Items` | `IReadOnlyList<T>` |  |
| `KeySelector` | `Func<T, string>` |  |
| `OnSelectedIndexChanged` | `Action<int>?` |  |
| `SelectedIndex` | [`Optional<int>`](/microsoft-ui-reactor/main/reference/components/Optional) | Selected item index. Defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset); use `Optional<int>.Of(-1)` to force no selection. |
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

## `TemplatedFlipViewElementBase` [#templatedflipviewelementbase]

Spec 047 §14 Phase 3 close-out — see [`TemplatedListViewElementBase`](#templatedlistviewelementbase). FlipView descriptor port is carved to Phase 4 because FlipView does not support `ContainerContentChanging` (pre-mounts items via a different shape); the marker is reserved so the symmetry is visible in the element hierarchy.

```csharp
public abstract record TemplatedFlipViewElementBase : TemplatedListElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`TemplatedListElementBase`](#templatedlistelementbase).

| Property | Type | Description |
| --- | --- | --- |
| `ControlKind` | [`TemplatedControlKind`](/microsoft-ui-reactor/main/reference/controls/collection-items#templatedcontrolkind) |  |

## `TemplatedGridViewElement<T>` [#templatedgridviewelementt]

```csharp
public record TemplatedGridViewElement<T>(IReadOnlyList<T> Items, Func<T, string> KeySelector, Func<T, int, Element> ViewBuilder) : TemplatedGridViewElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`TemplatedGridViewElementBase`](#templatedgridviewelementbase).

**Created by:** [`GridView`](/microsoft-ui-reactor/main/reference/factories/collections#gridview)

**Modifiers:** [`ItemClick`](/microsoft-ui-reactor/main/reference/modifiers/collections#itemclick), [`SelectedIndexChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectedindexchanged), [`SelectionChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectionchanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

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

## `TemplatedGridViewElementBase` [#templatedgridviewelementbase]

Spec 047 §14 Phase 3 close-out — see [`TemplatedListViewElementBase`](#templatedlistviewelementbase).

```csharp
public abstract record TemplatedGridViewElementBase : TemplatedListElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`TemplatedListElementBase`](#templatedlistelementbase).

| Property | Type | Description |
| --- | --- | --- |
| `ControlKind` | [`TemplatedControlKind`](/microsoft-ui-reactor/main/reference/controls/collection-items#templatedcontrolkind) |  |

## `TemplatedListElementBase` [#templatedlistelementbase]

Abstract base for data-driven items controls. Non-generic so the reconciler can match on a single type in its switch expression (same pattern as LazyStackElementBase).

```csharp
public abstract record TemplatedListElementBase : Element, IItemViewSource, IKeyedItemSource
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

| Property | Type | Description |
| --- | --- | --- |
| `ControlKind` | [`TemplatedControlKind`](/microsoft-ui-reactor/main/reference/controls/collection-items#templatedcontrolkind) |  |
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

## `TemplatedListViewElement<T>` [#templatedlistviewelementt]

```csharp
public record TemplatedListViewElement<T>(IReadOnlyList<T> Items, Func<T, string> KeySelector, Func<T, int, Element> ViewBuilder) : TemplatedListViewElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`TemplatedListViewElementBase`](#templatedlistviewelementbase).

**Created by:** [`ListView`](/microsoft-ui-reactor/main/reference/factories/collections#listview)

**Modifiers:** [`ItemClick`](/microsoft-ui-reactor/main/reference/modifiers/collections#itemclick), [`SelectedIndexChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectedindexchanged), [`SelectionChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectionchanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

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

## `TemplatedListViewElementBase` [#templatedlistviewelementbase]

Spec 047 §14 Phase 3 close-out — empty marker intermediate that lets the v1 handler registry route every closed `TemplatedListViewElement<T>` through a single base-derived descriptor registration without an open-generic resolver. Adds no fields and no overrides, so record equality on the leaf type `TemplatedListViewElement<T>` is unchanged.

```csharp
public abstract record TemplatedListViewElementBase : TemplatedListElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`TemplatedListElementBase`](#templatedlistelementbase).

| Property | Type | Description |
| --- | --- | --- |
| `ControlKind` | [`TemplatedControlKind`](/microsoft-ui-reactor/main/reference/controls/collection-items#templatedcontrolkind) |  |

## `TemplatedTreeViewElement<T>` [#templatedtreeviewelementt]

Typed, data-driven `TreeView`. The hierarchical peer of [`TemplatedListViewElement<T>`](#templatedlistviewelementt). See [`TemplatedTreeViewElementBase`](#templatedtreeviewelementbase).

```csharp
public record TemplatedTreeViewElement<T>(IReadOnlyList<T> Items, Func<T, string> KeySelector, Func<T, IReadOnlyList<T>?> ChildrenSelector, Func<T, Element> ViewBuilder) : TemplatedTreeViewElementBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`TemplatedTreeViewElementBase`](#templatedtreeviewelementbase).

**Created by:** [`TreeView`](/microsoft-ui-reactor/main/reference/factories/collections#treeview)

**Modifiers:** [`Expanding`](/microsoft-ui-reactor/main/reference/modifiers/collections#expanding), [`ItemInvoked`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#iteminvoked)

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

## `TemplatedTreeViewElementBase` [#templatedtreeviewelementbase]

Abstract non-generic base for the typed, data-driven `TreeView`. Non-generic so the reconciler can match a single type in its switch expression (same type-erasure pattern as [`TemplatedListElementBase`](#templatedlistelementbase)).

This is the hierarchical peer of [`TemplatedListViewElement<T>`](#templatedlistviewelementt): the developer supplies their own data items, a key selector, a children selector (the hierarchy), and a `viewBuilder` (`data → Element`, the WinUI `ItemTemplate` equivalent). It exists because WinUI's node-mode `TreeView` stringifies `TreeViewNode.Content` and cannot host a pre-built `UIElement` — rich per-node visuals must come from a template, never an element instance (the root cause of issue #447).

The base exposes object-erased accessors; the generic leaf casts back to `T`. Reference-type `T` flows through the covariant [`IReadOnlyList`](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1) → `IReadOnlyList<object>` conversion; value-type `T` is boxed once via the leaf's projection helper.

```csharp
public abstract record TemplatedTreeViewElementBase : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

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

## `TreeViewElement` [#treeviewelement]

The element record that describes a WinUI [`TreeView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.treeview) control.

```csharp
public record TreeViewElement(TreeViewNodeData[] Nodes) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`TreeView`](/microsoft-ui-reactor/main/reference/factories/collections#treeview)

**Modifiers:** [`Expanding`](/microsoft-ui-reactor/main/reference/modifiers/collections#expanding), [`ItemInvoked`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#iteminvoked), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Description |
| --- | --- | --- |
| `AllowDrop` | `bool` |  |
| `CanDragItems` | `bool` |  |
| `CanReorderItems` | `bool` |  |
| `Nodes` | [`TreeViewNodeData[]`](/microsoft-ui-reactor/main/reference/controls/collection-items#treeviewnodedata) |  |
| `OnExpanding` | `Action<TreeViewNodeData>?` |  |
| `OnItemInvoked` | `Action<TreeViewNodeData>?` |  |
| `SelectionMode` | `TreeViewSelectionMode` |  |

## `VirtualListElement` [#virtuallistelement]

Element record for the VirtualList component — a count-based virtualized list backed by WinUI's ItemsRepeater. Unlike LazyVStack which takes a concrete IReadOnlyList\<T\>, VirtualList works with an item count and a render callback, making it suitable for data-source-driven scenarios where items are fetched on demand.

```csharp
public record VirtualListElement : Element
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Modifiers:** [`VisibleRangeChanged`](/microsoft-ui-reactor/main/reference/modifiers/collections#visiblerangechanged)

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
