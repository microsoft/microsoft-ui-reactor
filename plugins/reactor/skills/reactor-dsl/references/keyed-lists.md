---
name: keyed-lists
description: >
  Reference for Reactor's keyed-list APIs — IReactorKeyed, the three
  WithKey overloads, the templated ListView / GridView / LazyVStack
  KeySelector argument, and the bulk-replace bailout. Use this when
  choosing between identity-on-data and call-site keys, when an item
  list re-mounts on insert instead of animating, or when the
  duplicate-key diagnostic fires.
---

# Keyed lists

Reactor reconciles list children by key, not by position. If the data
shape changes — insert at top, swap two rows, remove from middle — the
reconciler must be told which "row 3 today" matches "row 4 yesterday";
otherwise it tears down the realized container and rebuilds it, losing
focus, animation state, ElementRef identity, and triggering a full
re-paint of the affected viewport.

Three call sites in three APIs need a key. Pick the matching pattern and
move on.

| Where | API | Key comes from |
|---|---|---|
| Templated `ListView<T>` / `GridView<T>` / `FlipView<T>` / `LazyVStack<T>` / `LazyHStack<T>` | `keySelector: t => …` (3-arg overload) **or** omit when `T : IReactorKeyed` (2-arg overload) | A `Func<T, string>` |
| Hand-built children (`.Select(...)` into a panel) | `.WithKey(string)` per child **or** `.WithKey(item)` when `item : IReactorKeyed` | A string per child |
| `Component<TChild>(props)` siblings of the same type | `.WithKey(string)` per sibling | A string per sibling |

(spec 042 §3, §5)

## `IReactorKeyed` — identity on the data

When a record / class implements `IReactorKeyed`, every keyed call site
defaults to `t => t.Key`. This is the recommended shape for any model
you own — it eliminates the per-call-site `keySelector:` boilerplate and
makes "I have an ID" part of the data's contract.

```csharp
record TodoItem(string Id, string Text, bool Done) : IReactorKeyed
{
    string IReactorKeyed.Key => Id;
}

// 2-arg overload — keySelector defaults to t => t.Key
ListView<TodoItem>(items, (item, _) => TodoRow(item))

// 3-arg overload still works for explicit override
ListView<TodoItem>(items, t => $"row-{t.Id}", (item, _) => TodoRow(item))
```

The interface uses **explicit** implementation in the example above
(`string IReactorKeyed.Key => Id`). That's by convention — it keeps
`.Key` off the model's public surface and out of `.Equals` / `.GetHashCode`
machinery, so the field is reachable through the interface but doesn't
bloat the record's data contract.

When **not** to use `IReactorKeyed`: types you don't own (database
entities from an ORM you don't control, third-party DTOs), or when the
natural identifier isn't a string (in which case wrap the call site with
an explicit `keySelector: t => t.Id.ToString("D")`).

## `KeySelector` — explicit at the call site

```csharp
ListView<Order>(
    orders,
    keySelector: o => o.OrderNumber,
    viewBuilder: (o, _) => OrderRow(o))
```

Use this when:
- `Order` is owned by a service / persistence layer that shouldn't pick
  up a UI-flavored interface.
- The same model is rendered in two lists with different identity
  semantics (e.g. one keyed by `OrderNumber`, another by
  `OrderNumber + Variant`).
- You need a compound key: `o => $"{o.OrderId}|{o.LineNumber}"`.

## `.WithKey(...)` — three overloads

For hand-built children rendered via `items.Select(...)` into a panel
factory (`FlexColumn`, `VStack`, `HStack`, `Grid`, …), every child needs
a key:

```csharp
// 1) String literal / expression
FlexColumn(items.Select(item => TodoRow(item).WithKey(item.Id)).ToArray<Element?>())

// 2) Identity-on-data — when item : IReactorKeyed
FlexColumn(items.Select(item => TodoRow(item).WithKey(item)).ToArray<Element?>())

// 3) Component<TChild> with props
items.Select(i => Component<Card, CardProps>(new CardProps(i)).WithKey(i.Id))
```

The element-type fluent return is preserved across all overloads, so
type-specific modifiers still chain after `.WithKey`. `null` keys throw
`ArgumentNullException` rather than being silently accepted.

## What the diff actually does

Templated lists (`ListView<T>` etc.) and `LazyVStack` / `LazyHStack` run
through `KeyedListDiff.Apply`. It emits `ObservableCollection<ReactorRow>`
ops (`Add`, `Move`, `Remove`) that WinUI consumes as incremental
container updates — the visible viewport keeps its realized containers,
inserts get the platform's per-container theme transition, and inserts
at index 0 don't cascade-rebuild the trailing rows.

Hand-built `.WithKey(...)` children run through `ChildReconciler`, which
uses keyed longest-increasing-subsequence (LIS) matching — same shape, no
`ObservableCollection`. Survivors keep their `RuntimeHelpers.GetHashCode`
across re-renders.

**The two paths produce the same observable identity contract.** A row
keyed on `item.Id` keeps focus, animation state, and ElementRef stability
regardless of which list factory you used.

## Edge cases — bailout, diagnostics, gotchas

### Duplicate or null keys → bulk-replace bailout

Returning a duplicate key from any keyed factory triggers a one-shot
diagnostic and the diff falls through to a full `Reset` of the underlying
`ObservableCollection`. The list is still correct — just non-incremental
for that render. Common causes:

- A `Where(...)` that returns the same item twice (model not filtered
  for de-duplication upstream).
- A `keySelector` that returns a derived value that collides for
  semantically distinct items (`o => o.CustomerId` when the same
  customer appears in multiple orders).
- A `null` key, which is always treated as a developer bug and bails out.

### Bulk-replace heuristic

When churn exceeds **25%** of the new list size **and** at least **8
absolute ops** would fire, the diff bails to `Reset`. The absolute floor
keeps a single edit on a 3-item list from bailing out just because 1 / 3
is over 25%. For lists smaller than 32 items, you'll generally never see
this fire.

### Mutations that don't change list identity

Toggling a checkbox inside a row, editing a text field — these are
**property mutations**, not list mutations. The reconciler diffs the
existing realized container; no Add / Move / Remove ops fire. You don't
need `IReactorKeyed` to be the *only* thing that changes; just make sure
the key stays stable across the property change.

### Mutating `ObservableCollection<T>` from `UseState`

Don't. `UseState(new ObservableCollection<T>())` then mutating that
collection in place won't re-render — the reference is unchanged, so the
state comparer treats it as a no-op. Use an `IReadOnlyList<T>` /
`ImmutableList<T>` state shape and rebuild on every reducer, exactly
like every other Reactor list pattern. The keyed diff still produces
incremental WinUI ops because the diff is computed *internally* against
the prior render — your state can stay immutable.

### Mixing keyed and unkeyed siblings under a panel

Don't. Either every child of a panel has `.WithKey`, or none do. Mixing
defeats LIS matching for the unkeyed subset and degrades to positional
reconciliation — surviving keyed children still hold their identity, but
unkeyed siblings re-mount on any sibling insert.

## See also

- Design: [spec 042 — Keyed-list reconciliation & ListView animation](../../../../../docs/specs/042-keyed-list-reconciliation-design.md)
- Guide: [`collections.md` — Keyed reconciliation, in one paragraph](../../../../../docs/guide/collections.md)
- Guide: [`animation.md` — Transactional animation](../../../../../docs/guide/animation.md#transactional-animation--animationsanimate)
- Recipe: [`reactor-recipes/references/animated-list.md`](../../reactor-recipes/references/animated-list.md)
- Recipe: [`reactor-recipes/references/list-add-delete.cs`](../../reactor-recipes/references/list-add-delete.cs)
- Sample: `samples/apps/animated-list-demo/` — side-by-side templated vs. hand-built
