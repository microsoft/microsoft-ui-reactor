---
title: "Factories: Composition"
description: "Components, memoization, error boundaries, conditionals and other structural elements."
sidebar:
  label: "Composition"
---

Each factory method returns an element record. Most are on `Factories`, so `using static Microsoft.UI.Reactor.Factories;` covers them. A factory declared on another class names the `using static` it needs.

**Learn more:** [Components](/microsoft-ui-reactor/main/guides/components), [Advanced Patterns](/microsoft-ui-reactor/main/guides/advanced)

## `Component` [#component]

```csharp
ComponentElement Component<T>() where T : Component, new()
ComponentElement<TProps> Component<T, TProps>(TProps props) where T : Component<TProps>, new()
```

| Overload | Description |
| --- | --- |
| `Component<T>()` | Embed a Component class as a child element. Usage: Component\<MyWidget\>() |
| `Component<T, TProps>(TProps)` | Embed a Component class with typed props as a child element. Returns [`ComponentElement<TProps>`](/microsoft-ui-reactor/main/reference/elements/composition#componentelementtprops) so callers can use a record `with`-expression to produce a modified copy with updated typed props (records are immutable — `with` clones, it does not mutate). Usage: Component\<MyWidget, string\>("param") |

**Returns:** [`ComponentElement`](/microsoft-ui-reactor/main/reference/elements/composition#componentelement), [`ComponentElement<TProps>`](/microsoft-ui-reactor/main/reference/elements/composition#componentelementtprops).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `props` | `TProps` |  |

## `Empty` [#empty]

Renders nothing. Useful as a default/fallback.

```csharp
Element Empty()
```

**Returns:** [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

## `ErrorBoundary` [#errorboundary]

```csharp
ErrorBoundaryElement ErrorBoundary(Element child, Element fallback)
ErrorBoundaryElement ErrorBoundary(Element child, Func<Exception, Element> fallback)
```

| Overload | Description |
| --- | --- |
| `ErrorBoundary(Element, Element)` | Wraps a child subtree in an error boundary with a static fallback element. |
| `ErrorBoundary(Element, Func<Exception, Element>)` | Wraps a child subtree in an error boundary. If any component in the subtree throws during rendering, the fallback function is called with the exception. When the ErrorBoundary re-renders, it retries the child (error recovery). |

**Returns:** [`ErrorBoundaryElement`](/microsoft-ui-reactor/main/reference/elements/composition#errorboundaryelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `child` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `fallback` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) or `Func<Exception, Element>` |  |

## `Expr` [#expr]

Inline block-expression escape hatch: invokes `render` and returns its element, or `EmptyElement.Instance` when the lambda returns `null`. Lets callers write multi-statement bodies inside a DSL tree without extracting a local function or relying on the `((Func<Element?>)(() => …))()` cast trick.

```csharp
Element Expr(Func<Element?> render)
```

**Returns:** [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `render` | `Func<Element?>` |  |

**Remarks**

Spec 033 §5. `Expr` performs no memoization, owns no `RenderContext`, and is not a reconciler boundary — it is purely composition sugar. If the body needs hooks, use `Memo(...)` or a `Component<TProps>` instead.

Exceptions thrown inside `render` propagate unchanged so the surrounding error-boundary path applies.

**Example**

```csharp
VStack(
    Header(),
    Expr(() => {
        var summary = ComputeSummary(orders);
        return summary.Total > 0
            ? TotalsBanner(summary)
            : null;
    }),
    Footer())
```

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`ArgumentNullException`](https://learn.microsoft.com/dotnet/api/system.argumentnullexception) | `render` is `null`. |

## `ForEach` [#foreach]

```csharp
Element ForEach<T>(IEnumerable<T> items, Func<T, Element> render)
Element ForEach<T>(IEnumerable<T> items, Func<T, int, Element> render)
```

| Overload | Description |
| --- | --- |
| `ForEach<T>(IEnumerable<T>, Func<T, Element>)` | Map a list to elements (like .map() in React JSX): ForEach(items, item =\> TextBlock(item.Name))  When `T` implements [`IReactorKeyed`](/microsoft-ui-reactor/main/reference/components/IReactorKeyed), each projected element is keyed from `item.Key` unless the projection already set one, so the reconciler matches rows by identity instead of by position. |
| `ForEach<T>(IEnumerable<T>, Func<T, int, Element>)` | Map with index: ForEach(items, (item, i) =\> TextBlock($"\{i\}: \{item\}"))  Keys from [`IReactorKeyed`](/microsoft-ui-reactor/main/reference/components/IReactorKeyed) items exactly as the single-parameter overload does. |

**Returns:** [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `items` | `IEnumerable<T>` |  |
| `render` | `Func<T, Element>` or `Func<T, int, Element>` |  |

## `Group` [#group]

Groups elements without introducing a layout container (like React's Fragment). Children are flattened into the parent container.

```csharp
Element Group(params Element?[] children)
```

**Returns:** [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `children` | [`Element?[]`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

## `If` [#if]

If/else as an expression: If(loggedIn, () =\> TextBlock("Welcome"), () =\> Button("Login", ...))

```csharp
Element If(bool condition, Func<Element> then, Func<Element>? otherwise = null)
```

**Returns:** [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `condition` | `bool` |  |  |
| `then` | `Func<Element>` |  |  |
| `otherwise` | `Func<Element>?` | `null` |  |

## `LocaleProvider` [#localeprovider]

```csharp
ComponentElement<LocaleProviderElement> LocaleProvider(string locale, Element child, IStringResourceProvider? resourceProvider = null, string defaultLocale = "en-US", bool pseudoLocalize = false)
```

**Returns:** [`ComponentElement<TProps>`](/microsoft-ui-reactor/main/reference/elements/composition#componentelementtprops).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `locale` | `string` |  |  |
| `child` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `resourceProvider` | [`IStringResourceProvider?`](/microsoft-ui-reactor/main/reference/localization/IStringResourceProvider) | `null` |  |
| `defaultLocale` | `string` | `"en-US"` |  |
| `pseudoLocalize` | `bool` | `false` |  |

## `Memo` [#memo]

```csharp
MemoElement Memo(Func<RenderContext, Element> render, params object?[] dependencies)
KeyedMemoElement Memo<TKey>(TKey key, Func<Element> factory)
```

| Overload | Description |
| --- | --- |
| `Memo(Func<RenderContext, Element>, object?[])` | Define a memoized inline function component. Skips re-render when dependencies haven't changed. Empty deps array = render once + own state changes only. Non-empty = re-render when any dep changes. Usage: Memo(ctx =\> TextBlock("stable"), someProp, otherProp) |
| `Memo<TKey>(TKey, Func<Element>)` | Opt-in typed keyed memo for virtualized rows (issue #327). Wrap a row body in `Memo(key, () => …)` to assert the body is a **pure function of `key`**. Inside a virtualized list (`VirtualList`, `LazyVStack<T>`, …) the owning [`ElementFactory<T>`](/microsoft-ui-reactor/main/reference/components/ElementFactory) caches the built element per key in a bounded LRU, so a container recycle that re-asks for the same key returns the *same* element instance — the reconciler then skips the row's rebuild + per-row diff entirely (sub-µs). This targets the fast-scroll cost of variable-height rows, where every recycle otherwise rebuilds and diffs the row from scratch.  Usage: `renderItem: i => Memo(items[i].Id, () => Border(BigVariableHeightRow(items[i])))`  **Purity is your responsibility.** The key must capture every input the factory reads. If the body also depends on, say, a selection flag, fold it into the key: `Memo((items[i].Id, isSelected), () => …)`. Closing over unkeyed mutable state will serve stale content. The cache is cleared automatically when the list's items/renderItem change.  **Apply modifiers inside the factory, not on the wrapper.** The cross-recycle cache only unwraps a *bare* `Memo(key, …)` — one with no fluent modifiers, no `.WithKey(…)`, and no attached state (Grid/Canvas attached properties, `.Provide(…)` context, or theme bindings) on the wrapper itself. Decorating the wrapper (`Memo(id, () => …).Padding(8)`) opts the row out of caching and silently loses the perf benefit; put modifiers on the element the factory returns instead: `Memo(id, () => Border(…).Padding(8))`.  Outside a virtualized factory the wrapper is transparent but *keyed*: a re-render with the same key is a no-op (the factory is not re-invoked and the inner subtree is not diffed), while a changed key replaces the inner (unmount + fresh mount of the new factory output). The exception is a component inside the subtree that updates its own state: the factory then runs again so the reconciler can walk down to that component. The cross-recycle cache only applies on the virtualized-list path. It is always safe to use anywhere an element is expected. |

**Returns:** [`MemoElement`](/microsoft-ui-reactor/main/reference/elements/composition#memoelement), [`KeyedMemoElement`](/microsoft-ui-reactor/main/reference/elements/composition#keyedmemoelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `render` | `Func<RenderContext, Element>` |  |
| `dependencies` | `object?[]` |  |
| `key` | `TKey` | Stable identity that fully determines `factory`'s output. |
| `factory` | `Func<Element>` | Builds the row element. Invoked once per key on a cache miss. |

**Type parameters**

| Name | Description |
| --- | --- |
| `TKey` | Key type. Boxed to [`Object`](https://learn.microsoft.com/dotnet/api/system.object) and compared with [`Object.Equals`](https://learn.microsoft.com/dotnet/api/system.object.equals) / [`Object.GetHashCode`](https://learn.microsoft.com/dotnet/api/system.object.gethashcode), so value keys (ints, strings, records, value tuples) dedupe by value. This is what lets the int-index `VirtualList` path hit the cache. |

## `PropertyGrid` [#propertygrid]

Creates a PropertyGrid element for editing the target object's properties. Keyed by target type so switching between different target types creates a fresh component (avoiding broken reconciliation of different structures).

```csharp
ComponentElement<PropertyGridElement> PropertyGrid(object target, TypeRegistry registry, Action<object>? onRootChanged = null)
```

**Returns:** [`ComponentElement<TProps>`](/microsoft-ui-reactor/main/reference/elements/composition#componentelementtprops).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `target` | `object` |  |  |
| `registry` | [`TypeRegistry`](/microsoft-ui-reactor/main/reference/controls/property-grid-templates#typeregistry) |  |  |
| `onRootChanged` | `Action<object>?` | `null` |  |

## `RenderEachTime` [#rendereachtime]

Define an inline function component that re-renders on every parent render (no memoization), keeping its own hook scope. Made explicit so the reader can tell the always-re-render case apart from a missing deps array.

```csharp
FuncElement RenderEachTime(Func<RenderContext, Element> render)
```

**Returns:** [`FuncElement`](/microsoft-ui-reactor/main/reference/elements/composition#funcelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `render` | `Func<RenderContext, Element>` |  |

**Remarks**

Spec 033 §4. Use sparingly — components that re-render on every parent render defeat the memoization story and can amplify render storms. Prefer [`Memo`](#memo) with an explicit deps array whenever the re-render trigger can be enumerated.

## `VirtualList` [#virtuallist]

Creates a VirtualList element — a count-based virtualized list. Items are rendered on demand via the renderItem callback.

```csharp
ComponentElement<VirtualListElement> VirtualList(int itemCount, Func<int, Element> renderItem, Func<int, string>? getItemKey = null, double? itemHeight = null, double estimatedItemHeight = 40, double spacing = 0, Action<VirtualListRef>? ref = null, Action<int, int>? onVisibleRangeChanged = null)
```

**Returns:** [`ComponentElement<TProps>`](/microsoft-ui-reactor/main/reference/elements/composition#componentelementtprops).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `itemCount` | `int` |  |  |
| `renderItem` | `Func<int, Element>` |  |  |
| `getItemKey` | `Func<int, string>?` | `null` |  |
| `itemHeight` | `double?` | `null` |  |
| `estimatedItemHeight` | `double` | `40` |  |
| `spacing` | `double` | `0` |  |
| `ref` | `Action<VirtualListRef>?` | `null` |  |
| `onVisibleRangeChanged` | `Action<int, int>?` | `null` |  |

## `When` [#when]

Renders element only when condition is true. Reads nicely: When(items.Any(), () =\> TextBlock("Has items"))

```csharp
Element When(bool condition, Func<Element> then)
```

**Returns:** [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `condition` | `bool` |  |
| `then` | `Func<Element>` |  |
