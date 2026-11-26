# Element refs

Refs to mounted controls.

**Learn more:** [Input and gestures](../../input-and-gestures.md)

## `ElementRef`

Opaque ref to a mounted [`FrameworkElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement). Populated by the reconciler when the element is mounted; consumed by [`FocusManager.Focus`](FocusManager.md#focus) and friends. Null until the target element is mounted. Survives re-renders — the same [`ElementRef`](#elementref) instance is reused by the reconciler while the underlying control is recycled from the element pool.

```csharp
public sealed class ElementRef
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors

#### `ElementRef`

```csharp
public ElementRef()
```

### Properties

#### `Current`

The currently-mounted control, or null if the referenced element has not mounted yet (or has been unmounted without a replacement).

```csharp
public FrameworkElement? Current  // get
```

## `ElementRef<T>`

Strongly-typed wrapper around [`ElementRef`](#elementref) that surfaces the mounted element as `T` instead of the base [`FrameworkElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement), removing the `(Button)ref.Current` cast at every consumer.

```csharp
public sealed class ElementRef<T> where T : FrameworkElement
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Remarks

Spec 033 §3. Construct via `ctx.UseElementRef<T>()` and bind to an element via `.Ref(typedRef)`. The typed wrapper is reflection-free and AOT-safe — `T` is used only for the cast on read.

**Threading:** the reconciler populates the underlying ref on the UI thread during element mount/update. Reading [`ElementRef.Current`](#current_1) from a non-UI thread is permitted but the value may be stale by the time the caller dereferences it.

**Accessibility:** programmatic focus via `Current?.Focus()` moves the keyboard focus and triggers WinUI's standard focus-changed UIA event, but it does *not* by itself produce a screen-reader announcement of surrounding content. When the focus change should be accompanied by an announcement (e.g. "saved", "validation error") use [`UseAnnounce`](../hooks/UseAnnounce.md) in addition to the focus call. Modifiers applied to the element (notably `.AutomationName(...)`) are preserved across re-renders, so the typed ref does not affect the UIA tree shape.

### Type parameters

| Name | Description |
| --- | --- |
| `T` | The concrete WinUI control type the ref will be attached to. Constrained to [`FrameworkElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement) so the safe-cast in [`ElementRef.Current`](#current_1) is well-defined. |

### Properties

#### `Current`

The mounted control as `T`, or `null` when the referenced element has not mounted yet, has been unmounted, or the concrete control is not assignable to `T`.

```csharp
public T? Current  // get
```

**Remarks**

In RELEASE builds a type mismatch silently yields `null`; in DEBUG builds the reconciler raises [`Debug.Fail`](https://learn.microsoft.com/dotnet/api/system.diagnostics.debug.fail) at attach time so the mismatch is loud during development.

### Methods

#### `ToString`

Returns a string that represents this object.

```csharp
public override string ToString()
```

### Operators

#### `op_Implicit`

Implicit conversion to the untyped [`ElementRef`](#elementref) so the typed wrapper composes with the existing `.Ref(...)` modifier and [`FocusManager`](FocusManager.md) overloads without overload bloat.

```csharp
public static implicit operator ElementRef(ElementRef<T> typed)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `typed` | [`ElementRef<T>`](#elementreft) |  |

## `TypedElementRef`

Factory helpers for [`ElementRef<T>`](#elementreft). Prefer the `ctx.UseElementRef<T>()` hook from a render path; this helper is for the rare case of constructing a typed ref outside a render context (e.g. for tests).

```csharp
public static class TypedElementRef
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Methods

#### `Create`

Creates a fresh, unbound typed ref.

```csharp
public static ElementRef<T> Create<T>() where T : FrameworkElement
```
