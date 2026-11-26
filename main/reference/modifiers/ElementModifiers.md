---
title: "ElementModifiers"
---

```csharp
public record ElementModifiers
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Modifier System](/microsoft-ui-reactor/main/how-it-works/modifier-system)

## Constructors [#constructors]

### `ElementModifiers` [#elementmodifiers_1]

```csharp
public ElementModifiers()
```

## Properties [#properties]

### `AccessKey` [#accesskey]

```csharp
public string? AccessKey  // get, init
```

### `Accessibility` [#accessibility]

```csharp
public AccessibilityModifiers? Accessibility  // get, init
```

### `AttachedFlyout` [#attachedflyout]

```csharp
public Element? AttachedFlyout  // get, init
```

### `AutomationId` [#automationid]

```csharp
public string? AutomationId  // get, init
```

### `AutomationName` [#automationname]

```csharp
public string? AutomationName  // get, init
```

### `Backdrop` [#backdrop]

Declarative system backdrop (Mica / Acrylic) for the host window. Read by `ReactorHost` from the root tree's modifiers and applied to the owning `Window.SystemBackdrop`. Ignored by `ReactorHostControl` that does not own its window.

```csharp
public BackdropChoice? Backdrop  // get, init
```

### `Background` [#background]

```csharp
public Brush? Background  // get, init
```

### `BorderBrush` [#borderbrush]

```csharp
public Brush? BorderBrush  // get, init
```

### `BorderInlineStart` [#borderinlinestart]

```csharp
public Thickness? BorderInlineStart  // get, init
```

### `BorderThickness` [#borderthickness]

```csharp
public Thickness? BorderThickness  // get, init
```

### `CenterPoint` [#centerpoint]

```csharp
public Vector3? CenterPoint  // get, init
```

### `ContextFlyout` [#contextflyout]

```csharp
public Element? ContextFlyout  // get, init
```

### `CornerRadius` [#cornerradius]

```csharp
public CornerRadius? CornerRadius  // get, init
```

### `DragSource` [#dragsource]

```csharp
public DragSourceConfig? DragSource  // get, init
```

### `DropTarget` [#droptarget]

```csharp
public DropTargetConfig? DropTarget  // get, init
```

### `ElementSoundMode` [#elementsoundmode]

```csharp
public ElementSoundMode? ElementSoundMode  // get, init
```

### `FontFamily` [#fontfamily]

```csharp
public FontFamily? FontFamily  // get, init
```

### `FontSize` [#fontsize]

```csharp
public double? FontSize  // get, init
```

### `FontWeight` [#fontweight]

```csharp
public FontWeight? FontWeight  // get, init
```

### `Foreground` [#foreground]

```csharp
public Brush? Foreground  // get, init
```

### `HeadingLevel` [#headinglevel]

```csharp
public AutomationHeadingLevel? HeadingLevel  // get, init
```

### `Height` [#height]

```csharp
public double? Height  // get, init
```

### `HorizontalAlignment` [#horizontalalignment]

```csharp
public HorizontalAlignment? HorizontalAlignment  // get, init
```

### `HorizontalContentAlignment` [#horizontalcontentalignment]

```csharp
public HorizontalAlignment? HorizontalContentAlignment  // get, init
```

### `IsDragRegion` [#isdragregion]

When set, writes the `Microsoft.UI.Xaml.Controls.TitleBar.IsDragRegion` attached property on this element's control (WinApp SDK ≥ 2.1.3): `true` = draggable, `false` = clickable. Unset leaves the title bar to decide (interactive controls are auto-excluded from the drag region). Inert on elements that are not inside a TitleBar. See spec 059.

```csharp
public bool? IsDragRegion  // get, init
```

### `IsEnabled` [#isenabled]

```csharp
public bool? IsEnabled  // get, init
```

### `IsHitTestVisible` [#ishittestvisible]

```csharp
public bool? IsHitTestVisible  // get, init
```

### `IsTabStop` [#istabstop]

```csharp
public bool? IsTabStop  // get, init
```

### `IsVisible` [#isvisible]

```csharp
public bool? IsVisible  // get, init
```

### `Layout` [#layout]

Layout-bucket sub-record. Set directly only in perf-critical inner loops; ordinary code uses the field shim properties (Padding, Margin, Width, …) and never observes this slot.

```csharp
public LayoutModifiers? Layout  // get, init
```

**Remarks**

Spec 034 §A.

### `LongPress` [#longpress]

```csharp
public LongPressGestureConfig? LongPress  // get, init
```

### `Margin` [#margin]

```csharp
public Thickness? Margin  // get, init
```

### `MarginInlineEnd` [#margininlineend]

```csharp
public double? MarginInlineEnd  // get, init
```

### `MarginInlineStart` [#margininlinestart]

```csharp
public double? MarginInlineStart  // get, init
```

### `MaxHeight` [#maxheight]

```csharp
public double? MaxHeight  // get, init
```

### `MaxWidth` [#maxwidth]

```csharp
public double? MaxWidth  // get, init
```

### `MinHeight` [#minheight]

```csharp
public double? MinHeight  // get, init
```

### `MinWidth` [#minwidth]

```csharp
public double? MinWidth  // get, init
```

### `OnAccessKeyDisplayRequested` [#onaccesskeydisplayrequested]

```csharp
public Action<UIElement, AccessKeyDisplayRequestedEventArgs>? OnAccessKeyDisplayRequested  // get, init
```

### `OnCharacterReceived` [#oncharacterreceived]

```csharp
public Action<UIElement, CharacterReceivedRoutedEventArgs>? OnCharacterReceived  // get, init
```

### `OnDoubleTapped` [#ondoubletapped]

```csharp
public Action<object, DoubleTappedRoutedEventArgs>? OnDoubleTapped  // get, init
```

### `OnGotFocus` [#ongotfocus]

```csharp
public Action<object, RoutedEventArgs>? OnGotFocus  // get, init
```

### `OnHolding` [#onholding]

```csharp
public Action<object, HoldingRoutedEventArgs>? OnHolding  // get, init
```

### `OnKeyDown` [#onkeydown]

```csharp
public Action<object, KeyRoutedEventArgs>? OnKeyDown  // get, init
```

### `OnKeyUp` [#onkeyup]

```csharp
public Action<object, KeyRoutedEventArgs>? OnKeyUp  // get, init
```

### `OnLostFocus` [#onlostfocus]

```csharp
public Action<object, RoutedEventArgs>? OnLostFocus  // get, init
```

### `OnMountAction` [#onmountaction]

```csharp
public Action<FrameworkElement>? OnMountAction  // get, init
```

### `OnPointerCanceled` [#onpointercanceled]

```csharp
public Action<object, PointerRoutedEventArgs>? OnPointerCanceled  // get, init
```

### `OnPointerCaptureLost` [#onpointercapturelost]

```csharp
public Action<object, PointerRoutedEventArgs>? OnPointerCaptureLost  // get, init
```

### `OnPointerEntered` [#onpointerentered]

```csharp
public Action<object, PointerRoutedEventArgs>? OnPointerEntered  // get, init
```

### `OnPointerExited` [#onpointerexited]

```csharp
public Action<object, PointerRoutedEventArgs>? OnPointerExited  // get, init
```

### `OnPointerMoved` [#onpointermoved]

```csharp
public Action<object, PointerRoutedEventArgs>? OnPointerMoved  // get, init
```

### `OnPointerPressed` [#onpointerpressed]

```csharp
public Action<object, PointerRoutedEventArgs>? OnPointerPressed  // get, init
```

### `OnPointerReleased` [#onpointerreleased]

```csharp
public Action<object, PointerRoutedEventArgs>? OnPointerReleased  // get, init
```

### `OnPointerWheelChanged` [#onpointerwheelchanged]

```csharp
public Action<object, PointerRoutedEventArgs>? OnPointerWheelChanged  // get, init
```

### `OnPreviewKeyDown` [#onpreviewkeydown]

```csharp
public Action<object, KeyRoutedEventArgs>? OnPreviewKeyDown  // get, init
```

### `OnPreviewKeyUp` [#onpreviewkeyup]

```csharp
public Action<object, KeyRoutedEventArgs>? OnPreviewKeyUp  // get, init
```

### `OnRightTapped` [#onrighttapped]

```csharp
public Action<object, RightTappedRoutedEventArgs>? OnRightTapped  // get, init
```

### `OnSizeChanged` [#onsizechanged]

```csharp
public Action<object, SizeChangedEventArgs>? OnSizeChanged  // get, init
```

### `OnTapped` [#ontapped]

```csharp
public Action<object, TappedRoutedEventArgs>? OnTapped  // get, init
```

### `OnUnmountAction` [#onunmountaction]

```csharp
public Action<FrameworkElement>? OnUnmountAction  // get, init
```

### `Opacity` [#opacity]

```csharp
public double? Opacity  // get, init
```

### `Padding` [#padding]

```csharp
public Thickness? Padding  // get, init
```

### `PaddingInlineEnd` [#paddinginlineend]

```csharp
public double? PaddingInlineEnd  // get, init
```

### `PaddingInlineStart` [#paddinginlinestart]

```csharp
public double? PaddingInlineStart  // get, init
```

### `Pan` [#pan]

```csharp
public PanGestureConfig? Pan  // get, init
```

### `Pinch` [#pinch]

```csharp
public PinchGestureConfig? Pinch  // get, init
```

### `Ref` [#ref]

Imperative ref slot (spec 027 Tier 5). The reconciler writes the mounted [`FrameworkElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement) into [`ElementRef._current`](/microsoft-ui-reactor/main/reference/input/element-refs#elementref) so `FocusManager.Focus(ref)` (and future ref-based imperative APIs) can target it.

```csharp
public ElementRef? Ref  // get, init
```

### `RequestedTheme` [#requestedtheme]

Sets [`FrameworkElement.RequestedTheme`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement.requestedtheme) on the control, forcing a subtree to render in a specific theme variant (e.g., dark sidebar in a light app). Applied before ThemeRef bindings resolve so that theme resources pick up the correct variant.

```csharp
public ElementTheme? RequestedTheme  // get, init
```

### `RichToolTip` [#richtooltip]

```csharp
public Element? RichToolTip  // get, init
```

### `Rotate` [#rotate]

```csharp
public RotateGestureConfig? Rotate  // get, init
```

### `Rotation` [#rotation]

```csharp
public float? Rotation  // get, init
```

### `Scale` [#scale]

```csharp
public Vector3? Scale  // get, init
```

### `TabIndex` [#tabindex]

```csharp
public int? TabIndex  // get, init
```

### `ToolTip` [#tooltip]

```csharp
public string? ToolTip  // get, init
```

### `ToolTipPlacement` [#tooltipplacement]

`ToolTipService.Placement` — which side of the element the tooltip opens on. Applies to both [`ElementModifiers.ToolTip`](#tooltip) and [`ElementModifiers.RichToolTip`](#richtooltip). `PlacementMode` has exactly five members (`Top`, `Bottom`, `Left`, `Right`, `Mouse`); the edge-aligned placements belong to the separate `FlyoutPlacementMode` enum and are not expressible here.

```csharp
public PlacementMode? ToolTipPlacement  // get, init
```

### `ToolTipPlacementTargetRef` [#tooltipplacementtargetref]

`ToolTipService.PlacementTarget` — position the tooltip relative to a different element than the one it is attached to. Resolved through the same deferred reference-edge machinery as the XYFocus refs, so the target does not have to be mounted before this element.

```csharp
public ElementRef? ToolTipPlacementTargetRef  // get, init
```

### `TransformOrigin` [#transformorigin]

```csharp
public Vector2? TransformOrigin  // get, init
```

### `Translation` [#translation]

```csharp
public Vector3? Translation  // get, init
```

### `VerticalAlignment` [#verticalalignment]

```csharp
public VerticalAlignment? VerticalAlignment  // get, init
```

### `VerticalContentAlignment` [#verticalcontentalignment]

```csharp
public VerticalAlignment? VerticalContentAlignment  // get, init
```

### `Visual` [#visual]

Visual-bucket sub-record. Set directly only in perf-critical inner loops; ordinary code uses the field shim properties (Foreground, Background, BorderBrush, …) and never observes this slot.

```csharp
public VisualModifiers? Visual  // get, init
```

**Remarks**

Spec 034 §A.

### `Width` [#width]

```csharp
public double? Width  // get, init
```

### `XYFocusDownRef` [#xyfocusdownref]

```csharp
public ElementRef? XYFocusDownRef  // get, init
```

### `XYFocusKeyboardNavigation` [#xyfocuskeyboardnavigation]

```csharp
public XYFocusKeyboardNavigationMode? XYFocusKeyboardNavigation  // get, init
```

### `XYFocusLeftRef` [#xyfocusleftref]

```csharp
public ElementRef? XYFocusLeftRef  // get, init
```

### `XYFocusRightRef` [#xyfocusrightref]

```csharp
public ElementRef? XYFocusRightRef  // get, init
```

### `XYFocusUpRef` [#xyfocusupref]

```csharp
public ElementRef? XYFocusUpRef  // get, init
```

## Methods [#methods]

### `Merge` [#merge]

```csharp
public ElementModifiers Merge(ElementModifiers other)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `other` | [`ElementModifiers`](/microsoft-ui-reactor/main/reference/modifiers/ElementModifiers) |  |
