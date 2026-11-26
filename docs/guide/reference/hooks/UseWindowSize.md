# UseWindowSize

Returns (width, height) of the given window and re-renders when the window resizes.

```csharp
public (double Width, double Height) UseWindowSize()
public (double Width, double Height) UseWindowSize(Window window)
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Windows](../../windows.md), [Advanced Windowing](../../windowing-advanced.md), [Hooks](../../hooks.md)

## Overloads

| Overload | Description |
| --- | --- |
| [`UseWindowSize()`](#usewindowsize_1) | Parameterless overload — resolves the window whose host renders this component and re-renders on resize. |
| [`UseWindowSize(Window)`](#usewindowsizewindow) | Returns (width, height) of the given window and re-renders when the window resizes. |

### `UseWindowSize()`

Parameterless overload — resolves the window whose host renders this component and re-renders on resize. Returns `(0, 0)` when the component has no owning window (tray-flyout content, `ReactorHostControl` embeds); no implicit fallback to `PrimaryWindow` or to another open window. (spec 036 §5.2 / §7.1)

```csharp
public (double Width, double Height) UseWindowSize()
```

### `UseWindowSize(Window)`

Returns (width, height) of the given window and re-renders when the window resizes.

```csharp
public (double Width, double Height) UseWindowSize(Window window)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `window` | `Window` |  |

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected (double Width, double Height) UseWindowSize()
protected (double Width, double Height) UseWindowSize(Window window)
```
