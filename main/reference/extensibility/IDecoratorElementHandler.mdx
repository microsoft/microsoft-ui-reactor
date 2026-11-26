---
title: "IDecoratorElementHandler<TElement>"
seo:
  description: "Spec 047 §14 Phase 3 completion — decorator-style V1 handler contract for elements whose returned UIElement identity may change on update or whose unmount disposition diverges from the standard pool-return."
sidebar:
  label: "IDecoratorElementHandler"
---

Spec 047 §14 Phase 3 completion — decorator-style V1 handler contract for elements whose returned [`UIElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement) identity may change on update or whose unmount disposition diverges from the standard pool-return.

Use this contract instead of [`IElementHandler<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/IElementHandler) for:

- **Target-wrapping decorators** (Flyout, MenuFlyout, CommandBarFlyout) where the returned [`UIElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement) IS the user's inner `Target` child — the control type isn't known until the element is inspected, and may change on update if the user swaps Target's element type.
- **Modal lifecycle wrappers** (ContentDialog, Popup) where the returned control is a placeholder and the actual modal surface is created lazily or as a sibling.
- **Polymorphic mounts** (IconElement) where the concrete control type depends on the element's runtime subtype.
- **Interop bridges** (XamlHost, XamlPage) where the returned control hosts arbitrary user-side XAML.

Distinct from [`IElementHandler<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/IElementHandler) to keep the §13 Q12 "no substitution" invariant on the standard author-facing surface — only built-in V1 ports use this decorator shape. External authors continue to use [`IElementHandler<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/IElementHandler).

```csharp
public interface IDecoratorElementHandler<TElement> where TElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

**Learn more:** [Extending Reactor with Native Controls](/microsoft-ui-reactor/main/how-it-works/extending-reactor-controls), [The Control Reconciler Protocol](/microsoft-ui-reactor/main/how-it-works/control-reconciler-protocol)

## Methods [#methods]

### `GetSourceTarget` [#getsourcetarget]

Optional source-map override for target-wrapping decorators that return a child element's control but tag that control with the decorator element. Return the child element that created the control so [`ReactorSourceMap.GetSource`](/microsoft-ui-reactor/main/reference/diagnostics/ReactorSourceMap#getsource) reports the target's call site rather than the decorator's.

```csharp
public Element? GetSourceTarget(TElement element)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `element` | `TElement` |  |

### `Mount` [#mount]

Mount the element and return the [`UIElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement) the engine installs in the parent's slot. Decorator handlers may recursively mount inner elements via `ctx.MountChild(...)` and return one of them (e.g., FlyoutElement returns its Target's mounted control).

```csharp
public UIElement Mount(MountContext ctx, TElement element)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`MountContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#mountcontext) |  |
| `element` | `TElement` |  |

### `Unmount` [#unmount]

Unmount the element and return the engine's post-unmount disposition for the control. The `element` is read from the attached state DP at unmount time; it may be null if the tag was already detached (defensive — should not happen for well-behaved decorator handlers).

```csharp
public V1UnmountDisposition Unmount(UnmountContext ctx, TElement? element, UIElement control)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UnmountContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#unmountcontext) |  |
| `element` | `TElement?` |  |
| `control` | `UIElement` |  |

### `Update` [#update]

Diff old vs. new element and apply updates. Returns the [`UIElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement) that should occupy the parent's slot after the update — typically the same `control` instance for an in-place update, or a different instance when the wrapped target changed type. The engine installs the returned value in the slot (replacing `control` if different).

```csharp
public UIElement Update(UpdateContext ctx, TElement oldEl, TElement newEl, UIElement control)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UpdateContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#updatecontext) |  |
| `oldEl` | `TElement` |  |
| `newEl` | `TElement` |  |
| `control` | `UIElement` |  |
