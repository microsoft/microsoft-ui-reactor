---
title: "IReactorKeyed"
seo:
  description: "Identity-on-data convention for items rendered into Reactor collection elements (TemplatedListElementBase and LazyStackElementBase) and into hand-built keyed children (FlexColumn(items.Select(... .WithKey(item))))."
---

Identity-on-data convention for items rendered into Reactor collection elements ([`TemplatedListElementBase`](/microsoft-ui-reactor/main/reference/elements/collections#templatedlistelementbase) and [`LazyStackElementBase`](/microsoft-ui-reactor/main/reference/elements/collections#lazystackelementbase)) and into hand-built keyed children (`FlexColumn(items.Select(... .WithKey(item)))`).

```csharp
public interface IReactorKeyed
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Collections](/microsoft-ui-reactor/main/controls/collections)

## Remarks [#remarks]

When a data type implements [`IReactorKeyed`](/microsoft-ui-reactor/main/reference/components/IReactorKeyed), collection elements default their `KeySelector` to `t => t.Key` and a `WithKey<T, TKey>(this T el, TKey item) where T: Element where TKey: IReactorKeyed` overload becomes available (the open `T` preserves the element's concrete type through the fluent chain). Explicit `KeySelector` and `WithKey(string)` continue to work for types you do not own.

The returned [`IReactorKeyed.Key`](#key) value must be:

- **Stable** across re-renders for the lifetime of the item — using a value that changes (e.g. row index) defeats keyed reconciliation and produces the same visual churn as having no key.
- **Unique** within the containing list — duplicate keys trigger a bulk-replace bailout and a one-shot diagnostic.
- **Non-null** — a null key is treated as a developer bug and bails out the diff for the affected list.

See spec 042 §3 (the unified identity model) and §5 (identity-on-data convention) for the design rationale.

## Properties [#properties]

### `Key` [#key]

Stable, unique, non-null identifier for this item within the list it belongs to. Read at every reconcile pass; must be cheap to compute (ideally a field read, not a derived value).

```csharp
public abstract string Key  // get
```
