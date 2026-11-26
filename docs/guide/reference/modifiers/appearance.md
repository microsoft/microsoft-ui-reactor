# Modifiers: Appearance

Background, foreground, borders, corners, opacity, transforms, styles and theme.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Styling and Theming](../../styling.md), [Theming Tokens](../../theming-tokens.md)

## `ApplyStyle`

Apply a named WinUI Style to the element's control. Style is on FrameworkElement — works on any element. Usage: TextBlock("Hello").ApplyStyle("BodyTextBlockStyle")

**Applied at mount only.** This routes through `OnMount`, and the reconciler runs `OnMountAction` only when there is no previous element (first mount). Conditionally chaining a style therefore will not apply or remove it when the condition flips on an in-place update — give the variants different [`WithKey`](lifecycle.md#withkey) values to force a remount when the style itself must change.

**Unresolved keys do not throw.** If `styleName` is not found in the application's resources (including merged dictionaries), or is found but is not a [`Style`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.style), the element keeps its default appearance and a warning naming the key is emitted on the `Microsoft-UI-Reactor` ETW provider (and to `Debug.WriteLine` in DEBUG builds). Earlier versions threw out of the mount action instead. The warning fires once per distinct key, so one bad key in a virtualized list does not warn per realized item; past a few hundred distinct unresolved keys the de-duplication stops and every miss warns again, because staying quiet there would hide a real typo. Under NativeAOT the ETW half is compiled out unless the app sets `EventSourceSupport=true`; the DEBUG mirror is unaffected.

```csharp
T ApplyStyle<T>(this T el, string styleName) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `styleName` | `string` |  |

## `Backdrop`

```csharp
T Backdrop<T>(this T el, BackdropKind kind) where T : Element
T Backdrop<T>(this T el, Func<SystemBackdrop?> factory) where T : Element
```

Declared in `BackdropExtensions`.

| Overload | Description |
| --- | --- |
| `Backdrop<T>(T, BackdropKind)` | Sets the system backdrop on the hosting window using a built-in [`BackdropKind`](../theming/enums.md#backdropkind) selector. |
| `Backdrop<T>(T, Func<SystemBackdrop?>)` | Sets the system backdrop on the hosting window using a custom factory. Use this overload to apply tinted Mica, custom blur tints, or any other [`SystemBackdrop`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.systembackdrop) subclass. |

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `kind` | [`BackdropKind`](../theming/enums.md#backdropkind) |  |
| `factory` | `Func<SystemBackdrop?>` |  |

## `Background`

```csharp
T Background<T>(this T el, ThemeRef theme) where T : Element
T Background<T>(this T el, Brush brush) where T : Element
T Background<T>(this T el, string color) where T : Element
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Background<T>(T, ThemeRef)` | Sets the background from a WinUI theme resource. Resolves at render time and adapts when the theme changes (Light ↔ Dark). Usage: `VStack(children).Background(Theme.CardBackground)` |
| `Background<T>(T, Brush)` |  |
| `Background<T>(T, string)` | Sets the background from a color string (named color or #RRGGBB / #AARRGGBB). Returns a fresh, caller-independent brush per call (see [`BrushHelper.Parse`](../theming/BrushHelper.md#parse) — the parsed color is cached, the brush is not); the diff compares brushes structurally (color + opacity). Use the [`Background`](#background) overload to supply an explicit brush. |

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `theme` | [`ThemeRef`](../theming/ThemeRef.md) |  |
| `brush` | `Brush` |  |
| `color` | `string` |  |

## `BorderBrush`

```csharp
T BorderBrush<T>(this T el, ThemeRef theme) where T : Element
T BorderBrush<T>(this T el, Brush brush) where T : Element
T BorderBrush<T>(this T el, string color) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `theme` | [`ThemeRef`](../theming/ThemeRef.md) |  |
| `brush` | `Brush` |  |
| `color` | `string` |  |

## `BorderInlineStart`

```csharp
T BorderInlineStart<T>(this T el, Thickness thickness) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `thickness` | `Thickness` |  |

## `BorderThickness`

```csharp
T BorderThickness<T>(this T el, Thickness thickness) where T : Element
T BorderThickness<T>(this T el, double thickness) where T : Element
T BorderThickness<T>(this T el, double horizontal, double vertical) where T : Element
T BorderThickness<T>(this T el, double left = 0, double top = 0, double right = 0, double bottom = 0) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `thickness` | `Thickness` or `double` |  |  |
| `horizontal` | `double` |  |  |
| `vertical` | `double` |  |  |
| `left` | `double` | `0` |  |
| `top` | `double` | `0` |  |
| `right` | `double` | `0` |  |
| `bottom` | `double` | `0` |  |

## `CenterPoint`

Sets the pivot for `.Scale()` and `.Rotation()` in pixels from the element's leading top corner. Combined with [`TransformOrigin`](shared-controls.md#transformorigin), it is an offset added to the size-relative origin, like CSS `calc(50% + 10px)`.

```csharp
T CenterPoint<T>(this T el, Vector3 center) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `center` | `Vector3` |  |

## `CornerRadius`

```csharp
T CornerRadius<T>(this T el, CornerRadius radius) where T : Element
T CornerRadius<T>(this T el, double radius) where T : Element
T CornerRadius<T>(this T el, double topLeft, double topRight, double bottomRight, double bottomLeft) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `radius` | `CornerRadius` or `double` |  |
| `topLeft` | `double` |  |
| `topRight` | `double` |  |
| `bottomRight` | `double` |  |
| `bottomLeft` | `double` |  |

## `Foreground`

```csharp
RichTextBlockElement Foreground(this RichTextBlockElement el, Brush brush)
RichTextBlockElement Foreground(this RichTextBlockElement el, string color)
RichTextHyperlink Foreground(this RichTextHyperlink link, Brush brush)
RichTextHyperlink Foreground(this RichTextHyperlink link, string color)
RichTextParagraph Foreground(this RichTextParagraph paragraph, Brush brush)
RichTextParagraph Foreground(this RichTextParagraph paragraph, string color)
RichTextRun Foreground(this RichTextRun run, Brush brush)
RichTextRun Foreground(this RichTextRun run, string color)
T Foreground<T>(this T el, ThemeRef theme) where T : Element
T Foreground<T>(this T el, Brush brush) where T : Element
T Foreground<T>(this T el, string color) where T : Element
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Foreground(RichTextBlockElement, Brush)` |  |
| `Foreground(RichTextBlockElement, string)` |  |
| `Foreground(RichTextHyperlink, Brush)` |  |
| `Foreground(RichTextHyperlink, string)` |  |
| `Foreground(RichTextParagraph, Brush)` |  |
| `Foreground(RichTextParagraph, string)` |  |
| `Foreground(RichTextRun, Brush)` |  |
| `Foreground(RichTextRun, string)` |  |
| `Foreground<T>(T, ThemeRef)` | Sets the foreground from a WinUI theme resource on [`Control`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.control), [`TextBlock`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.textblock), [`RichTextBlock`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.richtextblock), or [`IconElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.iconelement). WinUI updates the brush when the element's effective theme changes. For example, `RichTextBlock("Hello").Foreground(Theme.PrimaryText)`. |
| `Foreground<T>(T, Brush)` |  |
| `Foreground<T>(T, string)` | Sets the foreground from a color string (named color or #RRGGBB / #AARRGGBB). Returns a fresh, caller-independent brush per call (see [`BrushHelper.Parse`](../theming/BrushHelper.md#parse) — the parsed color is cached, the brush is not); the diff compares brushes structurally (color + opacity). Use the [`Foreground`](#foreground) overload to supply an explicit brush. |

**Applies to:** any element, [`RichTextBlockElement`](../elements/text.md#richtextblockelement), [`RichTextHyperlink`](../controls/rich-text.md#richtexthyperlink), [`RichTextParagraph`](../controls/rich-text.md#richtextparagraph), [`RichTextRun`](../controls/rich-text.md#richtextrun).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `brush` | `Brush` |  |
| `color` | `string` |  |
| `theme` | [`ThemeRef`](../theming/ThemeRef.md) |  |

## `Opacity`

```csharp
T Opacity<T>(this T el, double opacity) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `opacity` | `double` |  |

## `RequestedTheme`

Sets [`FrameworkElement.RequestedTheme`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement.requestedtheme) on this element's control, forcing the subtree to render in a specific theme variant.

**Dark sidebar:** `VStack(children).RequestedTheme(ElementTheme.Dark)`

**Restore system default:** `panel.RequestedTheme(ElementTheme.Default)`

```csharp
T RequestedTheme<T>(this T el, ElementTheme theme) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `theme` | `ElementTheme` |  |

## `Resources`

Configures per-control resource overrides via WinUI's lightweight styling mechanism. Overrides are injected into [`FrameworkElement.Resources`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement.resources) so the control's [`VisualStateManager`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.visualstatemanager) picks them up automatically — hover, pressed, and disabled states all respect the overrides without requiring a custom template.

**Brand-colored button:**

```csharp
Button("Submit").Resources(r => r
    .Set("ButtonBackground", "#0078D4")
    .Set("ButtonBackgroundPointerOver", "#106EBE")
    .Set("ButtonBackgroundPressed", "#005A9E"))
```

**Scoped cascading:** resources set on a parent panel cascade to child controls, matching WinUI's resource lookup behavior.

```csharp
T Resources<T>(this T el, Action<ResourceBuilder> configure) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `configure` | `Action<ResourceBuilder>` |  |

## `Rotation`

```csharp
T Rotation<T>(this T el, float degrees) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `degrees` | `float` |  |

## `Scale`

```csharp
T Scale<T>(this T el, Vector3 scale) where T : Element
T Scale<T>(this T el, float uniform) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `scale` | `Vector3` |  |
| `uniform` | `float` |  |

## `Translation`

Sets the Translation property (Vector3) on the element's control. Commonly used with ThemeShadow for z-depth effects. Routes through AnimationHelper so WithAnimation scopes animate the change.

```csharp
T Translation<T>(this T el, float x, float y, float z) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `float` |  |
| `y` | `float` |  |
| `z` | `float` |  |

## `WithBorder`

```csharp
T WithBorder<T>(this T el, ThemeRef theme, double thickness = 1) where T : Element
T WithBorder<T>(this T el, Brush brush, double thickness = 1) where T : Element
T WithBorder<T>(this T el, string color, double thickness = 1) where T : Element
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `WithBorder<T>(T, ThemeRef, double)` | Sets the border from a WinUI theme resource. Resolves at render time and adapts when the theme changes (Light ↔ Dark). Usage: `VStack(children).WithBorder(Theme.CardStroke)` |
| `WithBorder<T>(T, Brush, double)` |  |
| `WithBorder<T>(T, string, double)` | Sets the border from a color string (named color or #RRGGBB / #AARRGGBB). Returns a fresh, caller-independent brush per call (see [`BrushHelper.Parse`](../theming/BrushHelper.md#parse) — the parsed color is cached, the brush is not); the diff compares brushes structurally (color + opacity). Use the [`WithBorder`](#withborder) overload to supply an explicit brush. |

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `theme` | [`ThemeRef`](../theming/ThemeRef.md) |  |  |
| `thickness` | `double` | `1` |  |
| `brush` | `Brush` |  |  |
| `color` | `string` |  |  |
