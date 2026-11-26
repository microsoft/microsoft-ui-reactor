# UseBreakpoint

Returns true when the given window's width is \>= minWidth. Re-renders when the window resizes across the breakpoint.

```csharp
public bool UseBreakpoint(double minWidth)
public bool UseBreakpoint(Window window, double minWidth)
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Layout](../../layout.md), [Hooks](../../hooks.md)

## Overloads

| Overload | Description |
| --- | --- |
| [`UseBreakpoint(double)`](#usebreakpointdouble) | Window-inferring overload — takes only `minWidth` and resolves the window whose host renders this component (see the parameterless `UseWindowSize`). |
| [`UseBreakpoint(Window, double)`](#usebreakpointwindow-double) | Returns true when the given window's width is \>= minWidth. |

### `UseBreakpoint(double)`

Window-inferring overload — takes only `minWidth` and resolves the window whose host renders this component (see the parameterless [`UseWindowSize`](UseWindowSize.md#usewindowsize_1)). Returns false when the component has no owning window. (spec 036 §5.2)

```csharp
public bool UseBreakpoint(double minWidth)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `minWidth` | `double` |  |

### `UseBreakpoint(Window, double)`

Returns true when the given window's width is \>= minWidth. Re-renders when the window resizes across the breakpoint.

```csharp
public bool UseBreakpoint(Window window, double minWidth)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `window` | `Window` |  |
| `minWidth` | `double` |  |

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected bool UseBreakpoint(double minWidth)
protected bool UseBreakpoint(Window window, double minWidth)
```
