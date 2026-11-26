# Modifiers: Focus and keyboard

Tab order, access keys, directional focus and focus traps.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Input and gestures](../../input-and-gestures.md), [Accessibility](../../accessibility.md)

## `AccessKey`

Sets UIElement.AccessKey — the Alt+Key shortcut (underlined hint shown on Alt press). When used on a button bound to a [`Command`](../input/Command.md), this per-site access key overrides [`Command.AccessKey`](../input/Command.md#accesskey) (per-site override always wins).

```csharp
T AccessKey<T>(this T el, string key) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |

**Example**

```csharp
Button("File", onClick).AccessKey("F")
```

## `AccessKeyDisplayRequested`

```csharp
T AccessKeyDisplayRequested<T>(this T el, Action handler) where T : Element
T AccessKeyDisplayRequested<T>(this T el, Action<UIElement, AccessKeyDisplayRequestedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

> **Obsolete.** Use OnAccessKeyDisplayRequested. See #260.

| Overload | Description |
| --- | --- |
| `AccessKeyDisplayRequested<T>(T, Action)` | Handler for UIElement.AccessKeyDisplayRequested — fires when the access-key bubble should appear (e.g., user pressed Alt). Use to customize the visual. |
| `AccessKeyDisplayRequested<T>(T, Action<UIElement, AccessKeyDisplayRequestedEventArgs>)` | Handler for UIElement.AccessKeyDisplayRequested with full event args. |

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action` or `Action<UIElement, AccessKeyDisplayRequestedEventArgs>` |  |

## `FocusMeta`

Registers any element with a FocusManager via attached metadata (for custom types).

```csharp
T FocusMeta<T>(this T el, FocusManager fm, string fieldName, bool autoFocus = false) where T : Element
```

Declared in `FocusExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `fm` | [`FocusManager`](../hooks/focus-helpers.md#focusmanager) |  |  |
| `fieldName` | `string` |  |  |
| `autoFocus` | `bool` | `false` |  |

## `FocusTrap`

Attaches a focus trap to this element. When the trap is active, Tab/Shift+Tab cycles within this element's visual subtree.

```csharp
T FocusTrap<T>(this T el, FocusTrapHandle handle) where T : Element
```

Declared in `FocusTrapElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handle` | [`FocusTrapHandle`](../hooks/hook-types.md#focustraphandle) |  |

## `IsTabStop`

```csharp
RichTextHyperlink IsTabStop(this RichTextHyperlink link, bool isTabStop = true)
T IsTabStop<T>(this T el, bool isTabStop = true) where T : Element
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `IsTabStop(RichTextHyperlink, bool)` |  |
| `IsTabStop<T>(T, bool)` | Sets UIElement.IsTabStop — whether the element participates in Tab navigation. Works on any element type (Panel, Control, etc.) in WinUI 3. |

**Applies to:** any element, [`RichTextHyperlink`](../controls/rich-text.md#richtexthyperlink).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `isTabStop` | `bool` | `true` |  |

**Example**

```csharp
Border(content).IsTabStop(false)
```

## `OnAccessKeyDisplayRequested`

```csharp
T OnAccessKeyDisplayRequested<T>(this T el, Action handler) where T : Element
T OnAccessKeyDisplayRequested<T>(this T el, Action<UIElement, AccessKeyDisplayRequestedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `OnAccessKeyDisplayRequested<T>(T, Action)` | Handler for UIElement.AccessKeyDisplayRequested — fires when the access-key bubble should appear (e.g., user pressed Alt). Use to customize the visual. |
| `OnAccessKeyDisplayRequested<T>(T, Action<UIElement, AccessKeyDisplayRequestedEventArgs>)` | Handler for UIElement.AccessKeyDisplayRequested with full event args. |

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action` or `Action<UIElement, AccessKeyDisplayRequestedEventArgs>` |  |

## `SoundMode`

Sets ElementSoundMode on the element's control. Usage: Button("Play", onClick).SoundMode(ElementSoundMode.Off)

```csharp
T SoundMode<T>(this T el, ElementSoundMode mode) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `ElementSoundMode` |  |

## `TabIndex`

```csharp
RichTextHyperlink TabIndex(this RichTextHyperlink link, int tabIndex)
T TabIndex<T>(this T el, int index) where T : Element
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `TabIndex(RichTextHyperlink, int)` |  |
| `TabIndex<T>(T, int)` | Sets Control.TabIndex — Tab order position. Lower values receive focus first. |

**Applies to:** any element, [`RichTextHyperlink`](../controls/rich-text.md#richtexthyperlink).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `tabIndex` | `int` |  |
| `index` | `int` |  |

**Example**

```csharp
Button("Submit").TabIndex(1)
```

## `TabNavigation`

Sets UIElement.TabFocusNavigation — how Tab navigates within a container. Local = cycle within container. Once = enter once then leave. Cycle = loop forever.

```csharp
T TabNavigation<T>(this T el, KeyboardNavigationMode mode) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `KeyboardNavigationMode` |  |

**Example**

```csharp
ToolBar(buttons).TabNavigation(KeyboardNavigationMode.Once)
```

## `XYFocusDown`

```csharp
T XYFocusDown<T>(this T el, ElementRef target) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | [`ElementRef`](../input/element-refs.md#elementref) |  |

## `XYFocusKeyboardNavigation`

Sets UIElement.XYFocusKeyboardNavigation — enables directional (Xbox-style) focus navigation with arrow keys or gamepad DPad.

```csharp
T XYFocusKeyboardNavigation<T>(this T el, XYFocusKeyboardNavigationMode mode) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `XYFocusKeyboardNavigationMode` |  |

**Example**

```csharp
Grid(tiles).XYFocusKeyboardNavigation(XYFocusKeyboardNavigationMode.Enabled)
```

## `XYFocusLeft`

```csharp
T XYFocusLeft<T>(this T el, ElementRef target) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | [`ElementRef`](../input/element-refs.md#elementref) |  |

## `XYFocusRight`

```csharp
T XYFocusRight<T>(this T el, ElementRef target) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | [`ElementRef`](../input/element-refs.md#elementref) |  |

## `XYFocusUp`

```csharp
T XYFocusUp<T>(this T el, ElementRef target) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | [`ElementRef`](../input/element-refs.md#elementref) |  |
