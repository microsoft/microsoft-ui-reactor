# Elements: Composition

Components, memoization, error boundaries, conditionals and other structural elements.

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Components](../../components.md), [Advanced Patterns](../../advanced.md)

## `ComponentElement`

Wraps a Component class so it can participate in the element tree. Created automatically by Component\<T\>() factory method.

```csharp
public record ComponentElement(Type ComponentType, object? Props = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Component`](../factories/composition.md#component)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `ComponentType` | `Type` |  |  |
| `Props` | `object?` | `null` |  |

## `ComponentElement<TProps>`

Strongly-typed [`ComponentElement`](#componentelement) that exposes [`ComponentElement.Props`](#componentelementtprops) as `TProps` instead of `object?`, so callers can use a record `with`-expression to produce a modified copy of the element with updated props:

```csharp
var grid = DataGrid<Foo>(source, columns);
var taller = grid with { Props = grid.Props with { RowHeight = 60 } };
```

The typed [`ComponentElement.Props`](#componentelementtprops) is a thin view over the base [`ComponentElement.Props`](#componentelement) slot — there is no second storage field, so the reconciler (which reads `base.Props`) always sees the same value as the typed accessor on the cloned record.

```csharp
public record ComponentElement<TProps> : ComponentElement
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`ComponentElement`](#componentelement).

**Created by:** [`Component`](../factories/composition.md#component), [`LocaleProvider`](../factories/composition.md#localeprovider), [`PropertyGrid`](../factories/composition.md#propertygrid), [`VirtualList`](../factories/composition.md#virtuallist)

| Property | Type | Description |
| --- | --- | --- |
| `Props` | `TProps` |  |

## `EmptyElement`

An element that renders nothing (used for conditional rendering).

```csharp
public record EmptyElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

| Member | Description |
| --- | --- |
| `Instance` |  |

## `ErrorBoundaryElement`

Catches render errors in its child subtree and displays fallback UI. Like React's ErrorBoundary — catches errors during rendering, not event handlers. When the ErrorBoundary re-renders, it retries the child (error recovery).

```csharp
public record ErrorBoundaryElement(Element Child, Func<Exception, Element> Fallback) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`ErrorBoundary`](../factories/composition.md#errorboundary)

| Property | Type | Description |
| --- | --- | --- |
| `Child` | [`Element`](Element.md) |  |
| `Fallback` | `Func<Exception, Element>` |  |

## `FuncElement`

A component defined inline via a render function (like a React function component).

```csharp
public record FuncElement(Func<RenderContext, Element> RenderFunc) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`RenderEachTime`](../factories/composition.md#rendereachtime)

| Property | Type | Description |
| --- | --- | --- |
| `RenderFunc` | `Func<RenderContext, Element>` |  |

## `GroupElement`

A transparent grouping element (like React's Fragment). Does not introduce any layout container — its children are flattened into the parent. Produced by `ForEach` and `Group()` in the DSL.

```csharp
public record GroupElement(Element[] Children) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

| Property | Type | Description |
| --- | --- | --- |
| `Children` | [`Element[]`](Element.md) |  |

## `KeyedMemoElement`

Issue #327 — an opt-in, typed keyed memo wrapper produced by the `Memo(key, factory)` factory (see [`Factories`](../factories/index.md)). The author asserts [`KeyedMemoElement.Factory`](#keyedmemoelement) is a **pure function of [`KeyedMemoElement.MemoKey`](#keyedmemoelement)**: when a virtualized [`ElementFactory<T>`](../components/ElementFactory.md) recycles a container and asks for the same key again, the factory-scoped LRU returns the previously-built inner [`Element`](Element.md) instance unchanged instead of rebuilding+diffing the subtree. Returning the *same* instance lets `Element.ShallowEquals` hit its `ReferenceEquals` fast-path so the per-row reconcile descent is skipped entirely.

**Purity contract.** The key must capture *every* input the factory reads. Closing over unkeyed mutable state (a `UseState` cell, an external counter, the theme) without folding it into the key will serve stale content — that is the author's responsibility. Widen the key to a tuple (e.g. `Memo((item.Id, isSelected), …)`) to capture extra inputs. [`KeyedMemoElement.MemoKey`](#keyedmemoelement) is compared with [`Object.Equals`](https://learn.microsoft.com/dotnet/api/system.object.equals) / [`Object.GetHashCode`](https://learn.microsoft.com/dotnet/api/system.object.gethashcode), so value keys (ints, strings, records, value tuples) dedupe by value — this is what makes the int-index VirtualList path hit the cache where the reference-identity `_viewBuilderCache` cannot.

**Used outside a virtualized factory** (e.g. a plain `VStack` child), the reconciler treats it as a *transparent, keyed* wrapper: a re-render with the same [`KeyedMemoElement.MemoKey`](#keyedmemoelement) is a no-op (by the purity contract the factory output is identical to the mounted inner, so there is nothing to diff), and a CHANGED key replaces the inner (unmount + fresh mount of the new [`KeyedMemoElement.Factory`](#keyedmemoelement) output). The old factory is never re-invoked at update time, so it is always safe to drop a `Memo(key, …)` anywhere a normal element is expected. The one exception to the no-op: when a component inside the mounted subtree updates its own state, the reconciler runs the current [`KeyedMemoElement.Factory`](#keyedmemoelement) again and walks that output against itself to reach the component, so the update renders. The cross-recycle cache benefit only applies on the [`ElementFactory<T>`](../components/ElementFactory.md) recycle path.

The positional parameter is named `MemoKey` (not `Key`) so it does not clash with the inherited [`Element.Key`](Element.md#key) string used for keyed reconciliation.

```csharp
public sealed record KeyedMemoElement(object MemoKey, Func<Element> Factory) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Memo`](../factories/composition.md#memo)

| Property | Type | Description |
| --- | --- | --- |
| `Factory` | `Func<Element>` |  |
| `MemoKey` | `object` |  |

## `LocaleProviderElement`

A Reactor element that establishes a locale context for its subtree. Sets FlowDirection on the host and provides IntlAccessor to descendants via UseIntl().

```csharp
public record LocaleProviderElement(string Locale, Element Child, IStringResourceProvider? ResourceProvider = null, string DefaultLocale = "en-US", bool PseudoLocalize = false) : Element
```

Namespace: `Microsoft.UI.Reactor.Localization`. Inherits from [`Element`](Element.md).

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Child` | [`Element`](Element.md) |  |  |
| `DefaultLocale` | `string` | `"en-US"` |  |
| `Locale` | `string` |  |  |
| `PseudoLocalize` | `bool` | `false` |  |
| `ResourceProvider` | [`IStringResourceProvider?`](../localization/IStringResourceProvider.md) | `null` |  |

## `MemoElement`

A memoized function component. Skips re-render when Dependencies haven't changed. null Dependencies = render once on mount + self-triggered state changes only.

```csharp
public record MemoElement(Func<RenderContext, Element> RenderFunc, object?[]? Dependencies = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Memo`](../factories/composition.md#memo)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Dependencies` | `object?[]?` | `null` |  |
| `RenderFunc` | `Func<RenderContext, Element>` |  |  |

## `ModifiedElement`

Wraps any element with layout modifiers (margin, alignment, size, etc.). Kept for backward compatibility. New code stores modifiers inline on Element.Modifiers.

```csharp
public record ModifiedElement(Element Inner, ElementModifiers WrappedModifiers) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

| Property | Type | Description |
| --- | --- | --- |
| `Inner` | [`Element`](Element.md) |  |
| `WrappedModifiers` | [`ElementModifiers`](../modifiers/ElementModifiers.md) |  |

## `SemanticElement`

Wraps a child element in a SemanticPanel that provides custom automation semantics to screen readers. This solves the problem where Reactor components can't override OnCreateAutomationPeer().

```csharp
public record SemanticElement(Element Child, SemanticDescription Semantics) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

| Property | Type | Description |
| --- | --- | --- |
| `Child` | [`Element`](Element.md) |  |
| `Semantics` | [`SemanticDescription`](../accessibility/SemanticDescription.md) |  |
