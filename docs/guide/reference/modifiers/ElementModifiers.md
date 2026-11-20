# ElementModifiers

```csharp
public record ElementModifiers
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Modifier System](../../modifier-system.md)

## Constructors

### `ElementModifiers`

```csharp
public ElementModifiers()
```

## Properties

### `AccessKey`

```csharp
public string? AccessKey  // get, init
```

### `Accessibility`

```csharp
public AccessibilityModifiers? Accessibility  // get, init
```

### `AttachedFlyout`

```csharp
public Element? AttachedFlyout  // get, init
```

### `AutomationId`

```csharp
public string? AutomationId  // get, init
```

### `AutomationName`

```csharp
public string? AutomationName  // get, init
```

### `Backdrop`

Declarative system backdrop (Mica / Acrylic) for the host window. Read by `ReactorHost` from the root tree's modifiers and applied to the owning `Window.SystemBackdrop`. Ignored by `ReactorHostControl` that does not own its window.

```csharp
public BackdropChoice? Backdrop  // get, init
```

### `Background`

```csharp
public Brush? Background  // get, init
```

### `BorderBrush`

```csharp
public Brush? BorderBrush  // get, init
```

### `BorderInlineStart`

```csharp
public Thickness? BorderInlineStart  // get, init
```

### `BorderThickness`

```csharp
public Thickness? BorderThickness  // get, init
```

### `CenterPoint`

```csharp
public Vector3? CenterPoint  // get, init
```

### `ContextFlyout`

```csharp
public Element? ContextFlyout  // get, init
```

### `CornerRadius`

```csharp
public CornerRadius? CornerRadius  // get, init
```

### `DragSource`

```csharp
public DragSourceConfig? DragSource  // get, init
```

### `DropTarget`

```csharp
public DropTargetConfig? DropTarget  // get, init
```

### `ElementSoundMode`

```csharp
public ElementSoundMode? ElementSoundMode  // get, init
```

### `FontFamily`

```csharp
public FontFamily? FontFamily  // get, init
```

### `FontSize`

```csharp
public double? FontSize  // get, init
```

### `FontWeight`

```csharp
public FontWeight? FontWeight  // get, init
```

### `Foreground`

```csharp
public Brush? Foreground  // get, init
```

### `HeadingLevel`

```csharp
public AutomationHeadingLevel? HeadingLevel  // get, init
```

### `Height`

```csharp
public double? Height  // get, init
```

### `HorizontalAlignment`

```csharp
public HorizontalAlignment? HorizontalAlignment  // get, init
```

### `HorizontalContentAlignment`

```csharp
public HorizontalAlignment? HorizontalContentAlignment  // get, init
```

### `IsDragRegion`

When set, writes the `Microsoft.UI.Xaml.Controls.TitleBar.IsDragRegion` attached property on this element's control (WinApp SDK ≥ 2.1.3): `true` = draggable, `false` = clickable. Unset leaves the title bar to decide (interactive controls are auto-excluded from the drag region). Inert on elements that are not inside a TitleBar. See spec 059.

```csharp
public bool? IsDragRegion  // get, init
```

### `IsEnabled`

```csharp
public bool? IsEnabled  // get, init
```

### `IsHitTestVisible`

```csharp
public bool? IsHitTestVisible  // get, init
```

### `IsTabStop`

```csharp
public bool? IsTabStop  // get, init
```

### `IsVisible`

```csharp
public bool? IsVisible  // get, init
```

### `Layout`

Layout-bucket sub-record. Set directly only in perf-critical inner loops; ordinary code uses the field shim properties (Padding, Margin, Width, …) and never observes this slot.

```csharp
public LayoutModifiers? Layout  // get, init
```

**Remarks**

Spec 034 §A.

### `LongPress`

```csharp
public LongPressGestureConfig? LongPress  // get, init
```

### `Margin`

```csharp
public Thickness? Margin  // get, init
```

### `MarginInlineEnd`

```csharp
public double? MarginInlineEnd  // get, init
```

### `MarginInlineStart`

```csharp
public double? MarginInlineStart  // get, init
```

### `MaxHeight`

```csharp
public double? MaxHeight  // get, init
```

### `MaxWidth`

```csharp
public double? MaxWidth  // get, init
```

### `MinHeight`

```csharp
public double? MinHeight  // get, init
```

### `MinWidth`

```csharp
public double? MinWidth  // get, init
```

### `OnAccessKeyDisplayRequested`

```csharp
public Action<UIElement, AccessKeyDisplayRequestedEventArgs>? OnAccessKeyDisplayRequested  // get, init
```

### `OnCharacterReceived`

```csharp
public Action<UIElement, CharacterReceivedRoutedEventArgs>? OnCharacterReceived  // get, init
```

### `OnDoubleTapped`

```csharp
public Action<object, DoubleTappedRoutedEventArgs>? OnDoubleTapped  // get, init
```

### `OnGotFocus`

```csharp
public Action<object, RoutedEventArgs>? OnGotFocus  // get, init
```

### `OnHolding`

```csharp
public Action<object, HoldingRoutedEventArgs>? OnHolding  // get, init
```

### `OnKeyDown`

```csharp
public Action<object, KeyRoutedEventArgs>? OnKeyDown  // get, init
```

### `OnKeyUp`

```csharp
public Action<object, KeyRoutedEventArgs>? OnKeyUp  // get, init
```

### `OnLostFocus`

```csharp
public Action<object, RoutedEventArgs>? OnLostFocus  // get, init
```

### `OnMountAction`

```csharp
public Action<FrameworkElement>? OnMountAction  // get, init
```

### `OnPointerCanceled`

```csharp
public Action<object, PointerRoutedEventArgs>? OnPointerCanceled  // get, init
```

### `OnPointerCaptureLost`

```csharp
public Action<object, PointerRoutedEventArgs>? OnPointerCaptureLost  // get, init
```

### `OnPointerEntered`

```csharp
public Action<object, PointerRoutedEventArgs>? OnPointerEntered  // get, init
```

### `OnPointerExited`

```csharp
public Action<object, PointerRoutedEventArgs>? OnPointerExited  // get, init
```

### `OnPointerMoved`

```csharp
public Action<object, PointerRoutedEventArgs>? OnPointerMoved  // get, init
```

### `OnPointerPressed`

```csharp
public Action<object, PointerRoutedEventArgs>? OnPointerPressed  // get, init
```

### `OnPointerReleased`

```csharp
public Action<object, PointerRoutedEventArgs>? OnPointerReleased  // get, init
```

### `OnPointerWheelChanged`

```csharp
public Action<object, PointerRoutedEventArgs>? OnPointerWheelChanged  // get, init
```

### `OnPreviewKeyDown`

```csharp
public Action<object, KeyRoutedEventArgs>? OnPreviewKeyDown  // get, init
```

### `OnPreviewKeyUp`

```csharp
public Action<object, KeyRoutedEventArgs>? OnPreviewKeyUp  // get, init
```

### `OnRightTapped`

```csharp
public Action<object, RightTappedRoutedEventArgs>? OnRightTapped  // get, init
```

### `OnSizeChanged`

```csharp
public Action<object, SizeChangedEventArgs>? OnSizeChanged  // get, init
```

### `OnTapped`

```csharp
public Action<object, TappedRoutedEventArgs>? OnTapped  // get, init
```

### `OnUnmountAction`

```csharp
public Action<FrameworkElement>? OnUnmountAction  // get, init
```

### `Opacity`

```csharp
public double? Opacity  // get, init
```

### `Padding`

```csharp
public Thickness? Padding  // get, init
```

### `PaddingInlineEnd`

```csharp
public double? PaddingInlineEnd  // get, init
```

### `PaddingInlineStart`

```csharp
public double? PaddingInlineStart  // get, init
```

### `Pan`

```csharp
public PanGestureConfig? Pan  // get, init
```

### `Pinch`

```csharp
public PinchGestureConfig? Pinch  // get, init
```

### `Ref`

Imperative ref slot (spec 027 Tier 5). The reconciler writes the mounted [`FrameworkElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement) into [`ElementRef._current`](../input/element-refs.md#elementref) so `FocusManager.Focus(ref)` (and future ref-based imperative APIs) can target it.

```csharp
public ElementRef? Ref  // get, init
```

### `RequestedTheme`

Sets [`FrameworkElement.RequestedTheme`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement.requestedtheme) on the control, forcing a subtree to render in a specific theme variant (e.g., dark sidebar in a light app). Applied before ThemeRef bindings resolve so that theme resources pick up the correct variant.

```csharp
public ElementTheme? RequestedTheme  // get, init
```

### `RichToolTip`

```csharp
public Element? RichToolTip  // get, init
```

### `Rotate`

```csharp
public RotateGestureConfig? Rotate  // get, init
```

### `Rotation`

```csharp
public float? Rotation  // get, init
```

### `Scale`

```csharp
public Vector3? Scale  // get, init
```

### `TabIndex`

```csharp
public int? TabIndex  // get, init
```

### `ToolTip`

```csharp
public string? ToolTip  // get, init
```

### `ToolTipPlacement`

`ToolTipService.Placement` — which side of the element the tooltip opens on. Applies to both [`ElementModifiers.ToolTip`](#tooltip) and [`ElementModifiers.RichToolTip`](#richtooltip). `PlacementMode` has exactly five members (`Top`, `Bottom`, `Left`, `Right`, `Mouse`); the edge-aligned placements belong to the separate `FlyoutPlacementMode` enum and are not expressible here.

```csharp
public PlacementMode? ToolTipPlacement  // get, init
```

### `ToolTipPlacementTargetRef`

`ToolTipService.PlacementTarget` — position the tooltip relative to a different element than the one it is attached to. Resolved through the same deferred reference-edge machinery as the XYFocus refs, so the target does not have to be mounted before this element.

```csharp
public ElementRef? ToolTipPlacementTargetRef  // get, init
```

### `Translation`

```csharp
public Vector3? Translation  // get, init
```

### `VerticalAlignment`

```csharp
public VerticalAlignment? VerticalAlignment  // get, init
```

### `VerticalContentAlignment`

```csharp
public VerticalAlignment? VerticalContentAlignment  // get, init
```

### `Visual`

Visual-bucket sub-record. Set directly only in perf-critical inner loops; ordinary code uses the field shim properties (Foreground, Background, BorderBrush, …) and never observes this slot.

```csharp
public VisualModifiers? Visual  // get, init
```

**Remarks**

Spec 034 §A.

### `Width`

```csharp
public double? Width  // get, init
```

### `XYFocusDownRef`

```csharp
public ElementRef? XYFocusDownRef  // get, init
```

### `XYFocusKeyboardNavigation`

```csharp
public XYFocusKeyboardNavigationMode? XYFocusKeyboardNavigation  // get, init
```

### `XYFocusLeftRef`

```csharp
public ElementRef? XYFocusLeftRef  // get, init
```

### `XYFocusRightRef`

```csharp
public ElementRef? XYFocusRightRef  // get, init
```

### `XYFocusUpRef`

```csharp
public ElementRef? XYFocusUpRef  // get, init
```

## Methods

### `Merge`

```csharp
public ElementModifiers Merge(ElementModifiers other)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `other` | [`ElementModifiers`](ElementModifiers.md) |  |
