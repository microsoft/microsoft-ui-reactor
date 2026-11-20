# Elements: Layout

Panels, stacks, grids, scrolling and other containers.

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Layout](../../layout.md), [Flex Layout](../../flex-layout.md)

## `AnnotatedScrollBarElement`

The element record that describes a WinUI [`AnnotatedScrollBar`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.annotatedscrollbar) control.

```csharp
public record AnnotatedScrollBarElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`AnnotatedScrollBar`](../factories/layout.md#annotatedscrollbar)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set)

## `BorderElement`

Border element — a single-child container with brush/corner/thickness chrome.

```csharp
public record BorderElement(Element? Child) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Border`](../factories/layout.md#border), [`Card`](../factories/layout.md#card)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Background` | `Brush?` |  |
| `BorderBrush` | `Brush?` |  |
| `BorderThickness` | `double?` |  |
| `Child` | [`Element?`](Element.md) |  |
| `CornerRadius` | `double?` |  |

## `CanvasElement`

The element record that describes a WinUI [`Canvas`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.canvas) control.

```csharp
public record CanvasElement(Element[] Children) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Canvas`](../factories/layout.md#canvas)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Background` | `Brush?` |  |
| `Children` | [`Element[]`](Element.md) |  |
| `Height` | `double?` |  |
| `Width` | `double?` |  |

## `ExpanderElement`

Expander element. `IsExpanded` defaults to [`Optional.Unset`](../components/Optional.md#unset) so the control owns its expansion state until explicitly set.

```csharp
public record ExpanderElement(string Header, Element Content, Optional<bool> IsExpanded = default, Action<bool>? OnIsExpandedChanged = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Expander`](../factories/layout.md#expander)

**Modifiers:** [`ContentTransitions`](../modifiers/animation.md#contenttransitions), [`Direction`](../modifiers/layout.md#direction), [`HeaderContent`](../modifiers/layout.md#headercontent), [`IsExpandedChanged`](../modifiers/layout.md#isexpandedchanged), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Content` | [`Element`](Element.md) |  |  |
| `ContentTransitions` | `TransitionCollection?` |  | Custom `TransitionCollection` applied to the expanding content area. |
| `ExpandDirection` | `ExpandDirection` |  |  |
| `Header` | `string` |  |  |
| `HeaderContent` | [`Element?`](Element.md) |  | Custom Element content assigned to the native Expander's Header, overriding the string [`ExpanderElement.Header`](#expanderelement). This is a mounted child, not a DataTemplate. |
| `IsExpanded` | [`Optional<bool>`](../components/Optional.md) | `default` |  |
| `OnIsExpandedChanged` | `Action<bool>?` | `null` |  |

## `FlexElement`

The element record that describes a [`FlexPanel`](../layout/flex-enums.md#flexpanel) control.

```csharp
public record FlexElement(Element[] Children) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Flex`](../factories/layout.md#flex), [`FlexColumn`](../factories/layout.md#flexcolumn), [`FlexRow`](../factories/layout.md#flexrow)

**Modifiers:** [`FlexPadding`](../modifiers/layout-and-spacing.md#flexpadding), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `AlignContent` | [`FlexAlign`](../layout/flex-enums.md#flexalign) |  |
| `AlignItems` | [`FlexAlign`](../layout/flex-enums.md#flexalign) |  |
| `Children` | [`Element[]`](Element.md) |  |
| `ColumnGap` | `double` |  |
| `Direction` | [`FlexDirection`](../layout/flex-enums.md#flexdirection) |  |
| `FlexPadding` | `Thickness` |  |
| `JustifyContent` | [`FlexJustify`](../layout/flex-enums.md#flexjustify) |  |
| `RowGap` | `double` |  |
| `Wrap` | [`FlexWrap`](../layout/flex-enums.md#flexwrap) |  |

## `GridElement`

The element record that describes a WinUI [`Grid`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.grid) control.

```csharp
public record GridElement(GridDefinition Definition, Element[] Children) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Grid`](../factories/layout.md#grid), [`InterspersedGrid`](../factories/layout.md#interspersedgrid), [`UniformGrid`](../factories/layout.md#uniformgrid)

**Modifiers:** [`BackgroundTransition`](../modifiers/animation.md#backgroundtransition), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Children` | [`Element[]`](Element.md) |  |
| `ColumnSpacing` | `double` |  |
| `Definition` | [`GridDefinition`](../layout/attached.md#griddefinition) |  |
| `RowSpacing` | `double` |  |

## `ParallaxViewElement`

The element record that describes a WinUI [`ParallaxView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.parallaxview) control.

```csharp
public record ParallaxViewElement(Element Child) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`ParallaxView`](../factories/layout.md#parallaxview)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set), [`Source`](../modifiers/layout.md#source), [`VerticalSourceEndOffset`](../modifiers/layout.md#verticalsourceendoffset), [`VerticalSourceStartOffset`](../modifiers/layout.md#verticalsourcestartoffset)

| Property | Type | Description |
| --- | --- | --- |
| `Child` | [`Element`](Element.md) |  |
| `HorizontalShift` | `double` |  |
| `Source` | `UIElement?` | Source UIElement that drives the parallax (typically a ScrollViewer / ListView). `null` uses the nearest scroller. |
| `VerticalShift` | `double` |  |
| `VerticalSourceEndOffset` | `double` | Vertical-axis source offset (in pixels) at which parallax ends. Defaults to 0 (auto). |
| `VerticalSourceStartOffset` | `double` | Vertical-axis source offset (in pixels) at which parallax begins. Defaults to 0. |

## `RelativePanelElement`

The element record that describes a WinUI [`RelativePanel`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.relativepanel) control.

```csharp
public record RelativePanelElement(Element[] Children) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`RelativePanel`](../factories/layout.md#relativepanel)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Children` | [`Element[]`](Element.md) |  |

## `ScrollViewElement`

Maps to the modern [`ScrollView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.scrollview) (InteractionTracker-backed, derives from `FrameworkElement`), as opposed to [`ScrollViewerElement`](#scrollviewerelement), which targets the classic `ScrollViewer`. Issue #348.

Exposes capabilities that exist only on the new control: `ContentOrientation`, `HorizontalAnchorRatio` / `VerticalAnchorRatio`, and the `Scrolling*` enum surface.

```csharp
public record ScrollViewElement(Element Child) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`ScrollView`](../factories/layout.md#scrollview)

**Modifiers:** [`AnchorRatio`](../modifiers/layout.md#anchorratio), [`ContentOrientation`](../modifiers/layout.md#contentorientation), [`HorizontalScrollMode`](../modifiers/layout.md#horizontalscrollmode), [`Set`](../modifiers/native-properties.md#set), [`VerticalScrollMode`](../modifiers/layout.md#verticalscrollmode), [`ViewChanged`](../modifiers/layout.md#viewchanged), [`ZoomMode`](../modifiers/layout.md#zoommode), [`ZoomRange`](../modifiers/layout.md#zoomrange)

| Property | Type | Description |
| --- | --- | --- |
| `Child` | [`Element`](Element.md) |  |
| `ContentOrientation` | `ScrollingContentOrientation` |  |
| `HorizontalAnchorRatio` | `double` |  |
| `HorizontalScrollBarVisibility` | `ScrollingScrollBarVisibility` |  |
| `HorizontalScrollMode` | `ScrollingScrollMode` |  |
| `MaxZoomFactor` | `double` |  |
| `MinZoomFactor` | `double` |  |
| `OnViewChanged` | `Action?` | Raised after the view (offset or zoom factor) changes. The modern control reports only the settled value — there is no intermediate flag like the classic `ScrollViewer.ViewChanged`. |
| `VerticalAnchorRatio` | `double` |  |
| `VerticalScrollBarVisibility` | `ScrollingScrollBarVisibility` |  |
| `VerticalScrollMode` | `ScrollingScrollMode` |  |
| `ZoomMode` | `ScrollingZoomMode` |  |

## `ScrollViewerElement`

The element record that describes a WinUI [`ScrollViewer`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.scrollviewer) control.

```csharp
public record ScrollViewerElement(Element Child) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`ScrollViewer`](../factories/layout.md#scrollviewer)

**Modifiers:** [`HorizontalScrollMode`](../modifiers/layout.md#horizontalscrollmode), [`Set`](../modifiers/native-properties.md#set), [`VerticalScrollMode`](../modifiers/layout.md#verticalscrollmode), [`ViewChanged`](../modifiers/layout.md#viewchanged), [`ZoomMode`](../modifiers/layout.md#zoommode)

| Property | Type | Description |
| --- | --- | --- |
| `Child` | [`Element`](Element.md) |  |
| `HorizontalScrollBarVisibility` | `ScrollBarVisibility` |  |
| `HorizontalScrollMode` | `ScrollMode` |  |
| `OnViewChanged` | `Action<ScrollViewerViewChangedEventArgs>?` | Raised when the scroll view's offset or zoom factor changes. The args expose `IsIntermediate` for callers who want to debounce until the scroll settles. |
| `Orientation` | `Orientation` |  |
| `VerticalScrollBarVisibility` | `ScrollBarVisibility` |  |
| `VerticalScrollMode` | `ScrollMode` |  |
| `ZoomMode` | `ZoomMode` |  |

## `SplitViewElement`

The element record that describes a WinUI [`SplitView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.splitview) control.

```csharp
public record SplitViewElement(Element? Pane = null, Element? Content = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`SplitView`](../factories/layout.md#splitview)

**Modifiers:** [`IsPaneOpen`](../modifiers/shared-controls.md#ispaneopen), [`LightDismissOverlayMode`](../modifiers/layout.md#lightdismissoverlaymode), [`PaneBackground`](../modifiers/layout.md#panebackground), [`PaneOpenChanged`](../modifiers/shared-controls.md#paneopenchanged), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `CompactPaneLength` | `double` |  |  |
| `Content` | [`Element?`](Element.md) | `null` |  |
| `DisplayMode` | `SplitViewDisplayMode` |  |  |
| `IsPaneOpen` | `bool` |  | Whether the pane is open. Defaults to `true`.  **Edge-triggered.** This declares a *transition*, not a mirror: Reactor writes the control only when the declared value *changes*. The control opens and closes its own pane (light dismiss, display-mode changes on resize) without touching the declared value, so re-rendering the same declared value will not force the pane back. Feed [`SplitViewElement.OnPaneOpenChanged`](#splitviewelement) into the state that drives this property. |
| `LightDismissOverlayMode` | `LightDismissOverlayMode` |  | How the light-dismiss overlay reacts to taps in Overlay mode. Defaults to `Auto`. |
| `OnPaneOpenChanged` | `Action<bool>?` |  |  |
| `OpenPaneLength` | `double` |  |  |
| `Pane` | [`Element?`](Element.md) | `null` |  |
| `PaneBackground` | `Brush?` |  | Brush behind the pane. Pair with the `.PaneBackground(ThemeRef)` overload for theme-aware backgrounds. |

## `StackElement`

The element record that describes a WinUI [`StackPanel`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.stackpanel) control.

```csharp
public record StackElement(Orientation Orientation, Element[] Children) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`HStack`](../factories/layout.md#hstack), [`VStack`](../factories/layout.md#vstack)

**Modifiers:** [`BackgroundTransition`](../modifiers/animation.md#backgroundtransition), [`Set`](../modifiers/native-properties.md#set), [`Spacing`](../modifiers/layout.md#spacing)

| Property | Type | Description |
| --- | --- | --- |
| `Children` | [`Element[]`](Element.md) |  |
| `HorizontalAlignment` | `HorizontalAlignment?` |  |
| `Orientation` | `Orientation` |  |
| `Spacing` | `double` | Spacing between children, in DIPs. |
| `VerticalAlignment` | `VerticalAlignment?` |  |

## `ViewboxElement`

The element record that describes a WinUI [`Viewbox`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.viewbox) control.

```csharp
public record ViewboxElement(Element Child) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Viewbox`](../factories/layout.md#viewbox)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set), [`Stretch`](../modifiers/layout.md#stretch), [`StretchDirection`](../modifiers/layout.md#stretchdirection)

| Property | Type | Description |
| --- | --- | --- |
| `Child` | [`Element`](Element.md) |  |
| `Stretch` | `Stretch?` |  |
| `StretchDirection` | `StretchDirection?` |  |

## `WrapGridElement`

The element record that describes a WinUI [`VariableSizedWrapGrid`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.variablesizedwrapgrid) control.

```csharp
public record WrapGridElement(Element[] Children) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`WrapGrid`](../factories/layout.md#wrapgrid)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Children` | [`Element[]`](Element.md) |  |
| `ItemHeight` | `double` |  |
| `ItemWidth` | `double` |  |
| `MaximumRowsOrColumns` | `int` |  |
| `Orientation` | `Orientation` |  |
