---
title: "Reconciler"
description: "The reconciler diffs old and new element trees and patches the real WinUI control tree."
---

Split across partial classes, one file per concern, so unrelated changes don't collide: - Reconciler.cs — orchestration: Reconcile entry, components, children, CanUpdate - Reconciler.Mount.cs — Mount() dispatch + composition-primitive handlers - Reconciler.Update.cs — Update() dispatch + composition-primitive handlers - Reconciler.\*.cs — one topic each (Modifiers, Keyframes, Transitions, ThemeBindings, Events, Unmount, ReactorState, ...); add code to the matching file

```csharp
public sealed class Reconciler : IDisposable
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Reconciliation](/microsoft-ui-reactor/main/how-it-works/reconciliation)

## Constructors [#constructors]

### `Reconciler` [#reconciler_1]

Spec 047 §14 — the V1 protocol is the unconditional production path. The Phase 1 `UseV1Protocol` feature flag, its AppContext switch, and the descriptor-vs-handler A|B harness ctor were all removed in §4.6 once the legacy `MountXxx`/`UpdateXxx` switch was deleted (§4.5).

Built-in V1 handlers register automatically into the internal `V1HandlerRegistry`; external authors add handlers via [`Reconciler.RegisterHandler`](#registerhandler) / [`Reconciler.RegisterType`](#registertype) (which populate `_typeRegistry`). Dispatch order is V1 → external → the four composition primitives that sit above the protocol.

```csharp
public Reconciler(ILogger? logger = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `logger` | `ILogger?` | `null` |  |

## Properties [#properties]

### `EnableBitmaskDiff` [#enablebitmaskdiff]

```csharp
public static bool EnableBitmaskDiff  // get, set
```

### `LastModifiedElements` [#lastmodifiedelements]

UIElements that were modified in-place during the last top-level Reconcile pass. Only populated when [`ReactorFeatureFlags.HighlightReconcileChanges`](/microsoft-ui-reactor/main/reference/extensibility/ReactorFeatureFlags#highlightreconcilechanges) is true; returns an empty list otherwise so callers never see stale data after the flag is toggled off.

```csharp
public IReadOnlyList<UIElement> LastModifiedElements  // get
```

### `LastMountedElements` [#lastmountedelements]

UIElements that were newly mounted during the last top-level Reconcile pass. Only populated when [`ReactorFeatureFlags.HighlightReconcileChanges`](/microsoft-ui-reactor/main/reference/extensibility/ReactorFeatureFlags#highlightreconcilechanges) is true; returns an empty list otherwise so callers never see stale data after the flag is toggled off.

```csharp
public IReadOnlyList<UIElement> LastMountedElements  // get
```

### `Pool` [#pool]

The element pool used by this reconciler. Disable via Pool.Enabled = false to prevent recycled controls from retaining stale property state.

```csharp
public ElementPool Pool  // get
```

## Methods [#methods]

### `ApplyDefaultAutomationName` [#applydefaultautomationname]

Spec 047 §14 Phase 1 (1.3) — promoted from internal. Mirrors a control's caption into `AutomationProperties.Name` when the author has not set one explicitly, so UIA clients that read the attached property directly (not via the peer) still find the name. Provisional API; see `REACTOR_V1_PREVIEW`.

```csharp
public static void ApplyDefaultAutomationName(FrameworkElement fe, string? caption)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fe` | `FrameworkElement` |  |
| `caption` | `string?` |  |

### `ApplyResourceOverrides` [#applyresourceoverrides]

#### `ApplyResourceOverrides(FrameworkElement, ResourceOverrides?, ResourceOverrides?)` [#applyresourceoverridesframeworkelement-resourceoverrides-resourceoverrides]

Applies per-control resource overrides (lightweight styling) to a [`FrameworkElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement). Literal values are set directly; [`ThemeRef`](/microsoft-ui-reactor/main/reference/theming/ThemeRef)-based values are resolved from `Application.Current.Resources` using the element's own effective theme.

```csharp
public static void ApplyResourceOverrides(FrameworkElement fe, ResourceOverrides? oldOverrides, ResourceOverrides? newOverrides)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fe` | `FrameworkElement` |  |
| `oldOverrides` | [`ResourceOverrides?`](/microsoft-ui-reactor/main/reference/theming/ResourceOverrides) |  |
| `newOverrides` | [`ResourceOverrides?`](/microsoft-ui-reactor/main/reference/theming/ResourceOverrides) |  |

**Remarks**

Binary-compatible original 3-arg surface (Spec 047 §14 Phase 1 (1.3), promoted from private per audit existing-api-surface.md). Delegates to the 4-arg overload with [`ElementTheme.Default`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.elementtheme.default); the reconciler uses the 4-arg form to pass an explicit ancestor-aware theme (needed at mount, before the control is parented). Provisional API; see `REACTOR_V1_PREVIEW`.

#### `ApplyResourceOverrides(FrameworkElement, ResourceOverrides?, ResourceOverrides?, ElementTheme)` [#applyresourceoverridesframeworkelement-resourceoverrides-resourceoverrides-elementtheme]

Applies per-control resource overrides (lightweight styling), resolving [`ThemeRef`](/microsoft-ui-reactor/main/reference/theming/ThemeRef)-backed values against an explicit `effectiveTheme`. Provisional API (Spec 047 §14 Phase 1); see `REACTOR_V1_PREVIEW`.

```csharp
public static void ApplyResourceOverrides(FrameworkElement fe, ResourceOverrides? oldOverrides, ResourceOverrides? newOverrides, ElementTheme effectiveTheme)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fe` | `FrameworkElement` | The control whose `Resources` receive the overrides. |
| `oldOverrides` | [`ResourceOverrides?`](/microsoft-ui-reactor/main/reference/theming/ResourceOverrides) | The previously-applied overrides, or null at mount. |
| `newOverrides` | [`ResourceOverrides?`](/microsoft-ui-reactor/main/reference/theming/ResourceOverrides) | The overrides to apply, or null to clear managed keys. |
| `effectiveTheme` | `ElementTheme` | The ancestor-aware effective theme threaded by the reconciler. When non-Default, ThemeRef-backed overrides resolve their concrete brush against it via [`ThemeRef.Resolve`](/microsoft-ui-reactor/main/reference/theming/ThemeRef#resolve) — necessary at mount, where the element is not yet parented and so an ancestor `RequestedTheme` is unreachable through `fe`'s own visual-tree walk. Pass [`ElementTheme.Default`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.elementtheme.default) to fall back to fe-based resolution (for example, callers of the 3-arg overload). |

### `ApplySetters` [#applysetters]

Spec 047 §14 Phase 1 (1.3) — promoted from internal. Runs each setter inside the §8.2 echo-suppression scope so user-authored `.Set(c => c.IsOn = true)` writes don't echo through the engine-wired OnXChanged callback. Provisional API; see `REACTOR_V1_PREVIEW`.

```csharp
public static void ApplySetters<T>(Action<T>[] setters, T control) where T : class
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `setters` | `Action<T>[]` |  |
| `control` | `T` |  |

### `ApplyThemeBindings` [#applythemebindings]

Applies [`ThemeRef`](/microsoft-ui-reactor/main/reference/theming/ThemeRef) bindings as a synthesized `{ThemeResource}`-driven Style. Spec 047 §14 Phase 1 (1.3), promoted from private (per audit existing-api-surface.md); provisional API, see `REACTOR_V1_PREVIEW`.

```csharp
public static void ApplyThemeBindings(FrameworkElement fe, IReadOnlyDictionary<string, ThemeRef> bindings)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fe` | `FrameworkElement` |  |
| `bindings` | `IReadOnlyDictionary<string, ThemeRef>` |  |

**Remarks**

WinUI handles theme-reactive resolution natively: the live `{ThemeResource}` Style setter re-resolves on the control's effective-theme change, which tracks the per-element/inherited `ActualTheme` (an ancestor `RequestedTheme` override included), not just the app-level theme. This native self-heal is why a ThemeBindings-only element is safe to structurally skip (#758; empirically verified in `ThemeBindingsSkipSelfHealFixtures` across immediate and inherited ancestor `RequestedTheme` toggles). Contrast a `ResourceOverrides.ThemeRef`, which resolves to a CONCRETE brush at reconcile and does NOT self-heal (so it stays theme-sensitive for the skip gate).

A Style already on the control is kept: the themed Style is built `BasedOn` it, and calling this again re-wraps the same base rather than chaining another level. When that base cannot be a `BasedOn` of the control's WinUI type (a Style whose `TargetType` is an app subclass), the themed Style replaces it, as before Styles were kept, and a warning is logged once. Failures are logged, never thrown.

### `DetachReactorState` [#detachreactorstate]

Fully detaches reactor state from a control: nulls the Element and clears the ModifierEventHandlerState's Current\* delegates so any already- attached trampoline on the native event source becomes a no-op if it fires. Use when the control leaves reactor's ownership for good (XamlHost / XamlPage unmount) but may remain alive because the app holds a reference — without this, captured closures stay reachable and stale reactor callbacks can still fire.

```csharp
public static void DetachReactorState(FrameworkElement fe)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fe` | `FrameworkElement` |  |

### `Dispose` [#dispose]

Releases the resources that this object holds.

```csharp
public void Dispose()
```

### `FlushConnectedAnimations` [#flushconnectedanimations]

Starts all queued connected animations. Call AFTER the new tree has been attached to the visual tree (e.g., after Window.Content = newControl).

```csharp
public void FlushConnectedAnimations()
```

**Remarks**

Source and destination must appear in the SAME reconcile pass — that is the shape Reactor's model produces, since a single state change swaps both subtrees.

### `GetElementTag` [#getelementtag]

#### `GetElementTag(FrameworkElement)` [#getelementtagframeworkelement]

Spec 047 §14 Phase 1 (1.3) — promoted from internal. Provisional API; see `REACTOR_V1_PREVIEW`.

```csharp
public static Element? GetElementTag(FrameworkElement fe)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fe` | `FrameworkElement` |  |

**Remarks**

For the built-in Flyout, MenuFlyout, and CommandBarFlyout decorators, this reads the target's tag; decorator identity is stored separately.

#### `GetElementTag(UIElement)` [#getelementtaguielement]

Spec 047 §14 Phase 1 (1.3) — promoted from internal. Retrieves the element associated with a control, or null. Provisional API; see `REACTOR_V1_PREVIEW`.

```csharp
public static Element? GetElementTag(UIElement control)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `control` | `UIElement` |  |

**Remarks**

For the built-in Flyout, MenuFlyout, and CommandBarFlyout decorators, this reads the target's tag; decorator identity is stored separately.

### `Mount` [#mount]

Creates a WinUI control tree from an Element tree. Returns null for EmptyElement.

```csharp
public UIElement? Mount(Element element, Action requestRerender)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `element` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `requestRerender` | `Action` |  |

### `Reconcile` [#reconcile]

Reconciles one child slot: brings `existingControl`, the control mounted for `oldElement`, up to date with `newElement`, and returns the control the slot should now hold.

```csharp
public UIElement? Reconcile(Element? oldElement, Element? newElement, UIElement? existingControl, Action requestRerender)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `oldElement` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `newElement` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `existingControl` | `UIElement?` |  |
| `requestRerender` | `Action` |  |

**Remarks**

When `newElement` has the element type and key of `oldElement`, the control is patched in place and returned unchanged, unless the update had to build a new control. In every other case the old control is unmounted before the new one is returned: a changed element type or key, an update that built a new control, or a null or empty `newElement`, which returns null. Without an `existingControl` the new element is mounted. Put the result in the slot when it differs from `existingControl`. A custom control updates the child elements it hosts with this method; in an `IElementHandler`, `UpdateContext.ReconcileChild` does the same.

### `RegisterHandler` [#registerhandler]

Spec 047 §14 Phase 1 (1.6 / 1.9) — register a v1 [`IElementHandler<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/IElementHandler) for the given element type. Dispatched ahead of [`Reconciler.RegisterType`](#registertype) callbacks. Throws on duplicate (including across registries) and on open-generic element types (spec §13 Q17).

```csharp
public void RegisterHandler<TElement, TControl>(IElementHandler<TElement, TControl> handler) where TElement : Element where TControl : UIElement
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | [`IElementHandler<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/IElementHandler) |  |

**Remarks**

Value-bearing handlers (a control whose programmatic property write raises the same change event as a user edit) must suppress their own write echo with the supported public primitive [`ReactorBinding<TElement>`](/microsoft-ui-reactor/main/reference/extensibility/ReactorBinding-1)'s `WriteSuppressed` method (or the static [`ReactorBinding`](/microsoft-ui-reactor/main/reference/extensibility/ReactorBinding)); descriptor authors get it via `.Controlled` / `.HandCodedControlled`. See [`IElementHandler<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/IElementHandler) and `docs/guide/extending-reactor-controls.md` (issue #206).

### `RegisterHandlerForDerivedTypes` [#registerhandlerforderivedtypes]

§14 Phase 3 close-out — register a v1 handler that catches every closed runtime type whose chain reaches `TBase`. Used by the typed templated-list descriptor ports (`TemplatedListViewElement<T>` family) so a single registration on a non-generic intermediate base routes every closed-T variant — same T-erasure pattern the legacy [`Reconciler.Mount`](#mount) switch uses.

Exact-type registrations via [`Reconciler.RegisterHandler`](#registerhandler) always win over a derived-type registration. Throws on duplicate base-type registration.

```csharp
public void RegisterHandlerForDerivedTypes<TBase, TControl>(IElementHandler<TBase, TControl> handler) where TBase : Element where TControl : UIElement
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | [`IElementHandler<TBase, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/IElementHandler) |  |

### `RegisterType` [#registertype]

Registers a custom element type so the reconciler knows how to mount, update, and unmount it. Registered types take priority over built-in types.

Spec 047 §13 Q17 / §14 Phase 1 (1.9) — v1 semantics: - Exact-type lookup via `element.GetType()`; no base-class fallback. - Throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) on duplicate registration, including duplicates against a previously-registered V1 handler (`V1HandlerRegistry`). - Throws on open-generic element types.

The mount and update handlers receive the Reconciler instance so they can recursively mount/update/unmount child elements without capturing external state.

The reconciler records the element on the control it gets back from `mount` or `update` (the element tag) wherever it reads the tag back itself: keyed reconciliation, the `unmount` lookup, `.Ref(...)` cleanup and exit transitions. The callbacks don't call [`Reconciler.SetElementTag`](#setelementtag) for those. Code of your own that reads the element back with [`Reconciler.GetElementTag`](#getelementtagframeworkelement), such as an event handler, still tags the control itself. The tag lives on a [`FrameworkElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement). `TControl` may be declared as [`UIElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement), but in WinUI 3 [`FrameworkElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement) is its only subclass, so every control a registration returns is tagged the same way.

`mount` and `update` are expected to return a control the registration owns. The tag holds one element, so a control that already carries the tag of an element of another type (registration is by exact type, so a derived type counts), for example one `mount` got from [`Reconciler.Mount`](#mount) for a child element, keeps that tag: overwriting it would silence that element's own callbacks (the collision issue #942 tracks for target-wrapping decorators). The registration's key, ref, transitions and `unmount` are not tracked for such a control. To wrap built-in elements, compose them in a component instead.

`update` returns null after patching the control in place; returning the control it was handed means the same. Return a different control only when you replaced it.

`unmount` runs when the reconciler unmounts the control, in place of its own walk over the control's children: a child the callbacks mounted through the reconciler has to be torn down there, for example with [`Reconciler.UnmountChild`](#unmountchild). Calling [`Reconciler.UnmountChild`](#unmountchild) on the control itself from `unmount` walks its children once rather than calling `unmount` again. Without an `unmount` callback the reconciler walks the control's children itself. A control that `update` replaced is unmounted wherever the reconciler reconciles the slot that holds it: a panel, a `Border`, a named slot such as `SplitView.Pane`, a tab's content. A few slots that a control fills by hand still swap a replaced control out without `unmount`: `CommandBar` content, `Expander` content and header template, `ContentDialog`, `Flyout` and `Popup` content, content attached with `.WithFlyout`, `.WithContextFlyout` or `.WithToolTip(element)`, and the realized item content of `ListView`, `GridView`, `TreeView<T>` and templated `FlipView`.

Not part of the `REACTOR_V1_PREVIEW` surface — this is the legacy type-registry path, public since before Spec 047. The §13 Q17 hardening (throw on duplicate, no base-class fallback, no open generics) tightens previously-undefined behavior; it does not change the shape of this API.

```csharp
public void RegisterType<TElement, TControl>(Func<Reconciler, TElement, Action, TControl> mount, Func<Reconciler, TElement, TElement, TControl, Action, UIElement?> update, Action<Reconciler, TControl>? unmount = null) where TElement : Element where TControl : UIElement
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `mount` | `Func<Reconciler, TElement, Action, TControl>` |  |  |
| `update` | `Func<Reconciler, TElement, TElement, TControl, Action, UIElement?>` |  |  |
| `unmount` | `Action<Reconciler, TControl>?` | `null` |  |

**Remarks**

A value-bearing custom control (one that raises the same change event for a programmatic write as for a user edit) must suppress its own write echo from the `update` callback. Use the supported public primitive [`ReactorBinding`](/microsoft-ui-reactor/main/reference/extensibility/ReactorBinding)'s `WriteSuppressed` method — wrap the programmatic write, gate it on an equality check, and pair it 1:1 with the write. For new controls prefer the V1 path: a [`ControlDescriptor<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/ControlDescriptor) (the primary, descriptor-first authoring shape — its `.Controlled` / `.HandCodedControlled` handle echo suppression for you), or a hand-coded [`IElementHandler<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/IElementHandler) via [`Reconciler.RegisterHandler`](#registerhandler) only for irregular controls (its [`ReactorBinding<TElement>`](/microsoft-ui-reactor/main/reference/extensibility/ReactorBinding-1) wraps the same primitive). See `docs/guide/extending-reactor-controls.md` (issue #206).

### `RentControl` [#rentcontrol]

Spec 047 §14 Phase 1 (1.5) — rent a control instance, either from the per-type pool (if poolable and a pooled instance is available) or via `factory` / `new T()`. Pool key is `typeof(T)` only.

```csharp
public T RentControl<T>(PoolPolicy<T>? policy = null, Func<T>? factory = null) where T : class, new()
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `policy` | [`PoolPolicy<T>?`](/microsoft-ui-reactor/main/reference/extensibility/PoolPolicy) | `null` |  |
| `factory` | `Func<T>?` | `null` |  |

### `ReturnControl` [#returncontrol]

Spec 047 §14 Phase 1 (1.5) — return a control to its per-type pool, after running the Q18 reset contract:

- Clear `ControlEventState` (per-control event payload box, §9.2).
- Clear `ModifierEventHandlerState` (modifier / routed-input handler state).
- Clear `ReactorAttached.StateProperty` Tag / DataContext set by Reactor.
- Invoke `policy.Reset` last (author-defined extra cleanup).

Double-return is safe: [`ElementPool.Return`](/microsoft-ui-reactor/main/reference/extensibility/ElementPool#return) dedupes via the per-type stack cap (`MaxPerType`).

```csharp
public void ReturnControl<T>(T control, PoolPolicy<T>? policy = null) where T : class
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `control` | `T` |  |  |
| `policy` | [`PoolPolicy<T>?`](/microsoft-ui-reactor/main/reference/extensibility/PoolPolicy) | `null` |  |

### `SetElementTag` [#setelementtag]

Spec 047 §14 Phase 1 (1.3) — promoted from internal. Associates a control with its current Reactor element via the [`ReactorAttached.StateProperty`](/microsoft-ui-reactor/main/reference/extensibility/Reconciler) attached DP so that trampolines can resolve the live element on dispatch. Provisional API; see `REACTOR_V1_PREVIEW`.

```csharp
public static void SetElementTag(FrameworkElement control, Element? element)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `control` | `FrameworkElement` |  |
| `element` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

### `UnmountChild` [#unmountchild]

Unmounts a child control. Public so registered type handlers can unmount children.

```csharp
public void UnmountChild(UIElement control)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `control` | `UIElement` |  |

### `UpdateChild` [#updatechild]

Low-level update of one child control, for callers that have already checked the new element can update it in place. Code outside the framework should call [`Reconciler.Reconcile`](#reconcile), which makes that check and unmounts a control it replaces.

```csharp
public UIElement? UpdateChild(Element oldEl, Element newEl, UIElement control, Action requestRerender)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `oldEl` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `newEl` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `control` | `UIElement` |  |
| `requestRerender` | `Action` |  |

**Remarks**

Patches `control` to match `newEl` and returns null, or returns a new control when the update had to build one. The caller then unmounts `control` with [`Reconciler.UnmountChild`](#unmountchild) and puts the new control in its slot, in that order, as [`Reconciler.Reconcile`](#reconcile) does: unmounting reads state from the control while it is still in the tree, such as a connected-animation snapshot. It doesn't check that `newEl` has the element type and key of `oldEl`: a changed type throws `InvalidCastException` for a built-in control. The analyzer rule `REACTOR_LIFECYCLE_003` flags calls to this method and offers to switch them to [`Reconciler.Reconcile`](#reconcile).

### `UpdateDefaultAutomationName` [#updatedefaultautomationname]

Spec 047 §14 Phase 1 (1.3) — promoted from internal. Update-time pair for [`Reconciler.ApplyDefaultAutomationName`](#applydefaultautomationname); overwrites the automation Name only when it matches the previous caption (i.e. the author hasn't intervened). Clears that default when the new content has no caption. Provisional API; see `REACTOR_V1_PREVIEW`.

```csharp
public static void UpdateDefaultAutomationName(FrameworkElement fe, string? oldCaption, string? newCaption)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fe` | `FrameworkElement` |  |
| `oldCaption` | `string?` |  |
| `newCaption` | `string?` |  |

## Fields [#fields]

### `DebugElementsDiffed` [#debugelementsdiffed]

Per-reconcile counters for diagnosing diff and mount/update volume. Reset before each top-level Reconcile() call; read afterward. Always populated (including Release builds) so ETW trace consumers can read them off the `ReconcileStop` event payload.

```csharp
public int DebugElementsDiffed
```

### `DebugElementsSkipped` [#debugelementsskipped]

```csharp
public int DebugElementsSkipped
```

### `DebugUIElementsCreated` [#debuguielementscreated]

```csharp
public int DebugUIElementsCreated
```

### `DebugUIElementsModified` [#debuguielementsmodified]

```csharp
public int DebugUIElementsModified
```
