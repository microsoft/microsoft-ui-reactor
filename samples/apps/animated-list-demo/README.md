# Animated List Demo

A focused showcase for spec **042 — Keyed list reconciliation & ListView
animation**. Shows the three layers of the design working together:

1. **Phase 1 — Internal `ObservableCollection<ReactorRow>` delta.**
   Insert / remove / shuffle on the templated `ListView<Row>` reuse the
   realized containers instead of re-paginating the visible viewport.
2. **Phase 2 — `IReactorKeyed` identity-on-data.**
   `Row` implements `IReactorKeyed`, so the `ListView<T>` call site drops
   the explicit `keySelector:` argument and the hand-built `FlexColumn`
   uses `.WithKey(item)` instead of `.WithKey(item.Id)`.
3. **Phase 3 — `Animations.Animate(...)` ambient transaction.**
   The single `Mutate(...)` chokepoint either calls the setter directly or
   wraps it in `Animations.Animate((AnimationKind)kind, commit)`. The
   ambient flows through an `AsyncLocal` snapshot into the reconciler so
   every resulting Add / Move / Remove picks up the chosen kind without a
   per-element transition modifier.

The page renders the same data twice — once via the templated `ListView<T>`,
once via a hand-built `FlexColumn(items.Select(...).WithKey(item))` — so you
can compare the two paths side by side under the same edit. The diff
output, the keyed-LIS in `ChildReconciler`, and the ambient consumption all
exercise the same intent.

## Run

```pwsh
dotnet run --project samples/apps/animated-list-demo
```

Toggle **Animate edits**, change **Kind**, and click through the toolbar:

| Button       | What it shows                                                  |
| ------------ | -------------------------------------------------------------- |
| `+1 at top`  | Single prepend — fast path; one `Add(index: 0)` op.            |
| `+1 at end`  | Single append — fast path; one `Add(index: count)` op.         |
| `− middle`   | Removes the middle row — suffix-walk fall-through, one `Remove`.|
| `− last`     | Removes the trailing row — fast path; one `Remove`.            |
| `Shuffle`    | Re-orders 8 rows — keyed LIS keeps survivors in place, emits `Move` ops. |
| `Reverse`    | Pure moves; no insert / remove. Inspect with the devtools.     |
| `Bulk reset` | Replaces every row — exceeds the churn threshold, falls through to the bulk-replace bailout (still correct, just non-incremental). |

## Accessibility

The demo respects the OS reduced-motion preference. Enable
**Settings → Accessibility → Visual effects → Animation effects = Off** and
the "Reduced motion ON" badge appears in the toolbar — every mutation goes
through the bare setter path, no `Animate` wrapper. WCAG 2.3.3.

## See also

- Design: [`docs/specs/042-keyed-list-reconciliation-design.md`](../../../docs/specs/042-keyed-list-reconciliation-design.md)
- Guide: [`docs/guide/animation.md` — Transactional animation](../../../docs/guide/animation.md#transactional-animation--animationsanimate)
- Guide: [`docs/guide/collections.md` — Keyed reconciliation](../../../docs/guide/collections.md)
