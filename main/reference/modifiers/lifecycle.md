---
title: "Modifiers: Lifecycle and identity"
description: "Keys, refs, mount and unmount callbacks, size changes and context values."
sidebar:
  label: "Lifecycle and identity"
---

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Components](/microsoft-ui-reactor/main/guides/components), [Context](/microsoft-ui-reactor/main/guides/context)

## `OnMount` [#onmount]

Runs an action once when the element is first mounted (not on re-renders). Use this instead of .Set() when attaching event handlers to avoid accumulation. Usage: Button("Go", null).OnMount(fe =\> \{ ((Button)fe).Click += ...; \})

```csharp
T OnMount<T>(this T el, Action<FrameworkElement> action) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `action` | `Action<FrameworkElement>` |  |

## `OnMountAdd` [#onmountadd]

Like [`OnMount`](#onmount), but composes with any mount action already on the element instead of overwriting it. Useful for framework code that wants to apply defensive defaults (e.g. `IsHitTestVisible = false`) to a caller-supplied [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) without silently dropping the caller's own mount-time wiring. The pre-existing action runs first, then the new one.

```csharp
T OnMountAdd<T>(this T el, Action<FrameworkElement> action) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `action` | `Action<FrameworkElement>` |  |

## `OnSizeChanged` [#onsizechanged]

```csharp
T OnSizeChanged<T>(this T el, Action<object, SizeChangedEventArgs> handler) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object, SizeChangedEventArgs>` |  |

## `OnUnmount` [#onunmount]

Runs an action once when the element is unmounted (removed from the tree), receiving the native control. The teardown half of [`OnMount`](#onmount) — the React `useEffect` cleanup / Compose `DisposableEffect { onDispose }` shape — for controls that need an imperative stop/dispose (e.g. stopping a camera preview, releasing a media session). Runs on genuine unmount, not on re-render/update. Disposing the host drops its tree without unmounting it, so this callback is not guaranteed to run then. Release anything that must not outlive the host from a `UseEffect` cleanup, which disposal does run. Usage: CameraPreview(...).OnMount(fe =\> Start(fe)).OnUnmount(fe =\> Stop(fe))

```csharp
T OnUnmount<T>(this T el, Action<FrameworkElement> action) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `action` | `Action<FrameworkElement>` |  |

## `OnUnmountAdd` [#onunmountadd]

Like [`OnUnmount`](#onunmount), but composes with any unmount action already on the element instead of overwriting it (pre-existing action runs first).

```csharp
T OnUnmountAdd<T>(this T el, Action<FrameworkElement> action) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `action` | `Action<FrameworkElement>` |  |

## `Provide` [#provide]

Provides a context value to this element's subtree. Any descendant can consume it via UseContext(). Multiple .Provide() calls on the same element merge into a single dictionary. Providing the same context twice on the same element is last-write-wins.

```csharp
T Provide<T, TValue>(this T element, Context<TValue> context, TValue value) where T : Element
```

Declared in `ContextExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `context` | [`Context<TValue>`](/microsoft-ui-reactor/main/reference/components/Context) |  |
| `value` | `TValue` |  |

## `Ref` [#ref]

```csharp
T Ref<T>(this T el, ElementRef target) where T : Element
T Ref<T, TElement>(this T el, ElementRef<TElement> target) where T : Element where TElement : FrameworkElement
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Ref<T>(T, ElementRef)` | Binds this element to an imperative [`ElementRef`](/microsoft-ui-reactor/main/reference/input/element-refs#elementref). Obtain the ref from `ctx.UseElementFocus()` (or construct one manually) and use [`FocusManager.Focus`](/microsoft-ui-reactor/main/reference/input/FocusManager#focus) to imperatively focus the referenced element after mount. |
| `Ref<T, TElement>(T, ElementRef<TElement>)` | Strongly-typed overload of [`Ref`](#ref). Accepts a typed [`ElementRef<T>`](/microsoft-ui-reactor/main/reference/input/element-refs#elementreft) and forwards to the untyped form via the implicit conversion. |

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | [`ElementRef`](/microsoft-ui-reactor/main/reference/input/element-refs#elementref) or [`ElementRef<TElement>`](/microsoft-ui-reactor/main/reference/input/element-refs#elementreft) |  |

**Remarks**

Spec 033 §3. Behaviorally identical to passing the inner untyped ref; the typed overload exists so callers see the typed surface in IntelliSense and so the reconciler's DEBUG mismatch-assert can fire on a wrong-type binding.

**Example**

```csharp
var (inputRef, requestFocus) = ctx.UseElementFocus();
return TextBox(value, setValue).Ref(inputRef);
```

## `WithKey` [#withkey]

```csharp
T WithKey<T>(this T el, string key) where T : Element
T WithKey<T, TKey>(this T el, TKey item) where T : Element where TKey : IReactorKeyed
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `WithKey<T>(T, string)` | Assigns a stable reconciliation key. Reuses the element when it already holds the same string reference; otherwise returns an immutable copy. |
| `WithKey<T, TKey>(T, TKey)` | Assigns the stable identity from an [`IReactorKeyed`](/microsoft-ui-reactor/main/reference/components/IReactorKeyed) data item directly to a hand-built element — equivalent to `el.WithKey(item.Key)` but lets call sites avoid hoisting the item to read `.Key`. (spec 042 §5) |

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `item` | `TKey` |  |

**Remarks**

Typical use: `FlexColumn(items.Select(item => TextBlock(item.Name).WithKey(item)))`. The `TKey` parameter is independent of the element type so the usual fluent inference (return the element type) is preserved.
