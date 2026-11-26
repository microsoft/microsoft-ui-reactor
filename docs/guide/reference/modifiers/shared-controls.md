# Control modifiers: Shared controls

Modifiers that several kinds of controls share, such as headers, placeholders and selection events.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Controls](../../controls.md)

## `ActionButtonClick`

Wires the action-button click handler. Passing `null` clears.

```csharp
InfoBarElement ActionButtonClick(this InfoBarElement el, Action? handler)
TeachingTipElement ActionButtonClick(this TeachingTipElement el, Action? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`InfoBarElement`](../elements/status-and-info.md#infobarelement), [`TeachingTipElement`](../elements/dialogs-and-flyouts.md#teachingtipelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `CheckedStateChanged`

Wires the three-state checked-changed handler (`null` = indeterminate). Passing `null` as the handler clears it.

```csharp
CheckBoxElement CheckedStateChanged(this CheckBoxElement el, Action<bool?>? handler)
ToggleButtonElement CheckedStateChanged(this ToggleButtonElement el, Action<bool?>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`CheckBoxElement`](../elements/input.md#checkboxelement), [`ToggleButtonElement`](../elements/buttons.md#togglebuttonelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<bool?>?` |  |

## `Closed`

```csharp
ContentDialogElement Closed(this ContentDialogElement el, Action<ContentDialogResult>? handler)
FlyoutElement Closed(this FlyoutElement el, Action? handler)
InfoBarElement Closed(this InfoBarElement el, Action? handler)
PopupElement Closed(this PopupElement el, Action? handler)
TeachingTipElement Closed(this TeachingTipElement el, Action? handler)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Closed(ContentDialogElement, Action<ContentDialogResult>?)` | Wires the closed handler. Receives the `ContentDialogResult` indicating which button dismissed the dialog. Passing `null` clears. |
| `Closed(FlyoutElement, Action?)` | Wires the closed handler. Passing `null` clears. |
| `Closed(InfoBarElement, Action?)` | Wires the closed handler. Passing `null` clears. |
| `Closed(PopupElement, Action?)` | Wires the closed handler. Passing `null` clears. |
| `Closed(TeachingTipElement, Action?)` | Wires the closed handler. Passing `null` clears. |

**Applies to:** [`ContentDialogElement`](../elements/dialogs-and-flyouts.md#contentdialogelement), [`FlyoutElement`](../elements/dialogs-and-flyouts.md#flyoutelement), [`InfoBarElement`](../elements/status-and-info.md#infobarelement), [`PopupElement`](../elements/dialogs-and-flyouts.md#popupelement), [`TeachingTipElement`](../elements/dialogs-and-flyouts.md#teachingtipelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<ContentDialogResult>?` or `Action?` |  |

## `Content`

```csharp
InfoBarElement Content(this InfoBarElement el, Element content)
TitleBarElement Content(this TitleBarElement el, Element content)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Content(InfoBarElement, Element)` | Custom rich content rendered below the message (overload to the message string). |
| `Content(TitleBarElement, Element)` | Sets the centered content slot of the title bar (typically a search box). |

**Applies to:** [`InfoBarElement`](../elements/status-and-info.md#infobarelement), [`TitleBarElement`](../elements/navigation.md#titlebarelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | [`Element`](../elements/Element.md) |  |

## `IconSource`

```csharp
InfoBarElement IconSource(this InfoBarElement el, IconData icon)
TeachingTipElement IconSource(this TeachingTipElement el, IconData icon)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `IconSource(InfoBarElement, IconData)` | Custom icon source (overrides the severity-default icon). |
| `IconSource(TeachingTipElement, IconData)` | Custom icon source rendered in the tip's leading slot. |

**Applies to:** [`InfoBarElement`](../elements/status-and-info.md#infobarelement), [`TeachingTipElement`](../elements/dialogs-and-flyouts.md#teachingtipelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `icon` | [`IconData`](../controls/icons.md#icondata) |  |

## `IsCheckedChanged`

```csharp
CheckBoxElement IsCheckedChanged(this CheckBoxElement el, Action<bool>? handler)
RadioButtonElement IsCheckedChanged(this RadioButtonElement el, Action<bool>? handler)
ToggleButtonElement IsCheckedChanged(this ToggleButtonElement el, Action<bool>? handler)
ToggleSplitButtonElement IsCheckedChanged(this ToggleSplitButtonElement el, Action<bool>? handler)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `IsCheckedChanged(CheckBoxElement, Action<bool>?)` | Wires the two-state checked-changed handler. Passing `null` clears. |
| `IsCheckedChanged(RadioButtonElement, Action<bool>?)` | Wires the checked-changed handler. Passing `null` clears. |
| `IsCheckedChanged(ToggleButtonElement, Action<bool>?)` | Wires the toggle-state-changed handler. Passing `null` clears. |
| `IsCheckedChanged(ToggleSplitButtonElement, Action<bool>?)` | Wires the toggle-state-changed handler for the primary button. Passing `null` clears. |

**Applies to:** [`CheckBoxElement`](../elements/input.md#checkboxelement), [`RadioButtonElement`](../elements/input.md#radiobuttonelement), [`ToggleButtonElement`](../elements/buttons.md#togglebuttonelement), [`ToggleSplitButtonElement`](../elements/buttons.md#togglesplitbuttonelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<bool>?` |  |

## `IsPaneOpen`

```csharp
NavigationViewElement IsPaneOpen(this NavigationViewElement el, bool isPaneOpen, Action<bool> onPaneOpenChanged)
SplitViewElement IsPaneOpen(this SplitViewElement el, bool isPaneOpen, Action<bool> onPaneOpenChanged)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `IsPaneOpen(NavigationViewElement, bool, Action<bool>)` | Controlled pane state: sets `IsPaneOpen` and wires the matching change handler in one call. The handler is required because the control opens and closes its own pane (light dismiss, adaptive display-mode changes on resize) — driving `IsPaneOpen` from state without feeding those changes back leaves the state stale, so the next toggle writes a value the control already holds and appears to do nothing (issue #916). Use the `IsPaneOpen` initializer directly only for a pane whose state the app never changes. |
| `IsPaneOpen(SplitViewElement, bool, Action<bool>)` | Controlled pane state: sets `IsPaneOpen` and wires the matching change handler in one call. The handler is required because the control closes its own pane (a light dismiss tap in `Overlay`/`CompactOverlay` mode) — driving `IsPaneOpen` from state without feeding those changes back leaves the state stale, so the next toggle writes a value the control already holds and appears to do nothing (issue #916). Use the `IsPaneOpen` initializer directly only for a pane whose state the app never changes. |

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement), [`SplitViewElement`](../elements/layout.md#splitviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `isPaneOpen` | `bool` |  |
| `onPaneOpenChanged` | `Action<bool>` |  |

## `ItemInvoked`

```csharp
NavigationViewElement ItemInvoked(this NavigationViewElement el, Action<string?>? handler)
TreeViewElement ItemInvoked(this TreeViewElement el, Action<TreeViewNodeData>? handler)
ItemsViewElement<T> ItemInvoked<T>(this ItemsViewElement<T> el, Action<T>? handler)
TemplatedTreeViewElement<T> ItemInvoked<T>(this TemplatedTreeViewElement<T> el, Action<T>? handler)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `ItemInvoked(NavigationViewElement, Action<string?>?)` | Wires the item-invoked handler, which also fires when the already-selected item is re-invoked. Receives the item's tag, or [`NavigationViewElement.SettingsTag`](../elements/navigation.md#navigationviewelement) for the settings item. Passing `null` clears. |
| `ItemInvoked(TreeViewElement, Action<TreeViewNodeData>?)` | Wires the item-invoked handler. Passing `null` clears. |
| `ItemInvoked<T>(ItemsViewElement<T>, Action<T>?)` | Wires the item-invoked handler. Passing `null` clears. |
| `ItemInvoked<T>(TemplatedTreeViewElement<T>, Action<T>?)` | Wires the item-invoked handler for the typed tree (receives the developer's `T`). Passing `null` clears. |

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement), [`TreeViewElement`](../elements/collections.md#treeviewelement), [`ItemsViewElement<T>`](../elements/collections.md#itemsviewelementt), [`TemplatedTreeViewElement<T>`](../elements/collections.md#templatedtreeviewelementt).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string?>?` or `Action<TreeViewNodeData>?` or `Action<T>?` |  |

## `PaneOpenChanged`

```csharp
NavigationViewElement PaneOpenChanged(this NavigationViewElement el, Action<bool>? handler)
SplitViewElement PaneOpenChanged(this SplitViewElement el, Action<bool>? handler)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `PaneOpenChanged(NavigationViewElement, Action<bool>?)` | Wires the pane-open-state-changed handler. Fires for pane changes the app never requested (light dismiss, adaptive display-mode changes) as well as the echo of a programmatic `IsPaneOpen` write — feed the value back into the state that drives `IsPaneOpen` to keep controlled pane state in sync. Passing `null` clears. |
| `PaneOpenChanged(SplitViewElement, Action<bool>?)` | Wires the pane-open-state-changed handler. Passing `null` clears. |

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement), [`SplitViewElement`](../elements/layout.md#splitviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<bool>?` |  |

## `SelectedIndexChanged`

Wires the selected-index-changed handler. Passing `null` clears.

```csharp
ComboBoxElement SelectedIndexChanged(this ComboBoxElement el, Action<int>? handler)
FlipViewElement SelectedIndexChanged(this FlipViewElement el, Action<int>? handler)
GridViewElement SelectedIndexChanged(this GridViewElement el, Action<int>? handler)
ListBoxElement SelectedIndexChanged(this ListBoxElement el, Action<int>? handler)
ListViewElement SelectedIndexChanged(this ListViewElement el, Action<int>? handler)
PivotElement SelectedIndexChanged(this PivotElement el, Action<int>? handler)
RadioButtonsElement SelectedIndexChanged(this RadioButtonsElement el, Action<int>? handler)
SelectorBarElement SelectedIndexChanged(this SelectorBarElement el, Action<int>? handler)
TabViewElement SelectedIndexChanged(this TabViewElement el, Action<int>? handler)
TemplatedFlipViewElement<T> SelectedIndexChanged<T>(this TemplatedFlipViewElement<T> el, Action<int>? handler)
TemplatedGridViewElement<T> SelectedIndexChanged<T>(this TemplatedGridViewElement<T> el, Action<int>? handler)
TemplatedListViewElement<T> SelectedIndexChanged<T>(this TemplatedListViewElement<T> el, Action<int>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`ComboBoxElement`](../elements/input.md#comboboxelement), [`FlipViewElement`](../elements/collections.md#flipviewelement), [`GridViewElement`](../elements/collections.md#gridviewelement), [`ListBoxElement`](../elements/collections.md#listboxelement), [`ListViewElement`](../elements/collections.md#listviewelement), [`PivotElement`](../elements/navigation.md#pivotelement), [`RadioButtonsElement`](../elements/input.md#radiobuttonselement), [`SelectorBarElement`](../elements/navigation.md#selectorbarelement), [`TabViewElement`](../elements/navigation.md#tabviewelement), [`TemplatedFlipViewElement<T>`](../elements/collections.md#templatedflipviewelementt), [`TemplatedGridViewElement<T>`](../elements/collections.md#templatedgridviewelementt), [`TemplatedListViewElement<T>`](../elements/collections.md#templatedlistviewelementt).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<int>?` |  |

## `SelectionChanged`

```csharp
GridViewElement SelectionChanged(this GridViewElement el, Action<IReadOnlyList<int>>? handler)
ListBoxElement SelectionChanged(this ListBoxElement el, Action<IReadOnlyList<int>>? handler)
ListViewElement SelectionChanged(this ListViewElement el, Action<IReadOnlyList<int>>? handler)
TextBoxElement SelectionChanged(this TextBoxElement el, Action<string, int, int>? handler)
DataGridElement<T> SelectionChanged<T>(this DataGridElement<T> el, Action<IReadOnlySet<RowKey>>? handler)
ItemsViewElement<T> SelectionChanged<T>(this ItemsViewElement<T> el, Action<IReadOnlyList<T>>? handler)
TemplatedGridViewElement<T> SelectionChanged<T>(this TemplatedGridViewElement<T> el, Action<IReadOnlyList<T>>? handler)
TemplatedListViewElement<T> SelectionChanged<T>(this TemplatedListViewElement<T> el, Action<IReadOnlyList<T>>? handler)
```

Declared in `ElementExtensions`, `DataGridEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

| Overload | Description |
| --- | --- |
| `SelectionChanged(GridViewElement, Action<IReadOnlyList<int>>?)` | Wires the multi-select snapshot handler (see [`SelectionChanged`](#selectionchanged)). |
| `SelectionChanged(ListBoxElement, Action<IReadOnlyList<int>>?)` | Wires the multi-select snapshot handler. Passing `null` clears. |
| `SelectionChanged(ListViewElement, Action<IReadOnlyList<int>>?)` | Wires the multi-select snapshot handler. Receives the FULL list of selected indices on every change (not added/removed deltas). Snapshot semantics match `CalendarView.SelectedDatesChanged`. Passing `null` clears. |
| `SelectionChanged(TextBoxElement, Action<string, int, int>?)` | Wires the selection-changed handler. Receives (selectedText, selectionStart, selectionLength). Passing `null` clears. |
| `SelectionChanged<T>(DataGridElement<T>, Action<IReadOnlySet<RowKey>>?)` | Wires the multi-select snapshot handler for [`DataGridElement<T>`](../elements/data-grid.md#datagridelementt). Receives the full set of currently-selected `RowKey`s on every change (not added/removed deltas). Passing `null` clears. |
| `SelectionChanged<T>(ItemsViewElement<T>, Action<IReadOnlyList<T>>?)` | Wires the multi-select snapshot handler. Receives the full list of currently selected items (not indices). Passing `null` clears. |
| `SelectionChanged<T>(TemplatedGridViewElement<T>, Action<IReadOnlyList<T>>?)` | Wires the multi-select snapshot handler for the typed peer. Passing `null` clears. |
| `SelectionChanged<T>(TemplatedListViewElement<T>, Action<IReadOnlyList<T>>?)` | Wires the multi-select snapshot handler for the typed peer. Receives the full list of currently selected items. Passing `null` clears. |

**Applies to:** [`GridViewElement`](../elements/collections.md#gridviewelement), [`ListBoxElement`](../elements/collections.md#listboxelement), [`ListViewElement`](../elements/collections.md#listviewelement), [`TextBoxElement`](../elements/input.md#textboxelement), [`DataGridElement<T>`](../elements/data-grid.md#datagridelementt), [`ItemsViewElement<T>`](../elements/collections.md#itemsviewelementt), [`TemplatedGridViewElement<T>`](../elements/collections.md#templatedgridviewelementt), [`TemplatedListViewElement<T>`](../elements/collections.md#templatedlistviewelementt).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<IReadOnlyList<int>>?` or `Action<string, int, int>?` or `Action<IReadOnlySet<RowKey>>?` or `Action<IReadOnlyList<T>>?` |  |

## `TransformOrigin`

Sets the pivot for `.Scale()` and `.Rotation()` as a fraction of the element's laid-out size: `(0.5f, 0.5f)` is the centre, `(0, 0)` the leading top corner. The pivot follows resizes and size animations, and `x` is flow-relative, so `0` is the right edge in a right-to-left layout. A `.CenterPoint()` on the same element is added as a pixel offset. Without this modifier the pivot is the `.CenterPoint()` value, or the leading top corner.

**Grow from the centre on hover:** `Border(card).TransformOrigin(0.5f, 0.5f).InteractionStates(s => s.PointerOver(scale: 1.05f))`

```csharp
T TransformOrigin<T>(this T el, float x, float y) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `float` |  |
| `y` | `float` |  |

**Remarks**

Spec 066 §8. The pivot is an expression on the element's composition visual, so the modifier takes the visual (like the other transform modifiers). While `.LayoutAnimation()` animates the size, the pivot follows the animating size, not the final layout size.
