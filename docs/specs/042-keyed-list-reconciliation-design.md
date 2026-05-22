# Keyed-List Reconciliation & ListView Animation — Design Spec

## Status

**Implemented (2026-05-17)** — Phases 0 through 6.3 landed on `feat/042-keyed-list-reconciliation`; Phase 6.4 issue close-out happens when the PR merges. Open questions answered (see §9). Perf gate captured at `tests/stress_perf/baselines/keyed-list-vs-winui-2026-05-17-104102/` — paired Microsoft.UI.Reactor (Reactor) vs. WinUI vanilla virtualizing-list matrix; reconciler matches WinUI within 0.3 % P50 at 1 k items, no measurable per-edit overhead. Implementation task list with detailed completion marks: [`docs/specs/tasks/042-keyed-list-reconciliation-implementation.md`](tasks/042-keyed-list-reconciliation-implementation.md).

Tracking bug: [microsoft/microsoft-ui-reactor#198](https://github.com/microsoft/microsoft-ui-reactor/issues/198) — *feat(ListView): route ObservableCollection to WinUI ItemsSource for incremental add/remove animation*.

This spec describes a unified keyed-identity model for collection reconciliation that fixes the bug in #198 and lays the groundwork for SwiftUI-style transactional animations across both templated controls (`ListView<T>` / `GridView<T>` / `LazyVStack<T>`) and hand-built element trees (`FlexColumn(items.Select(...))`).

---

## Table of Contents

- [§1 Motivation](#1-motivation)
- [§2 What already exists](#2-what-already-exists)
- [§3 The unified identity model](#3-the-unified-identity-model)
- [§4 Phase 1 — internal ObservableCollection delta](#4-phase-1--internal-observablecollection-delta)
- [§5 Phase 2 — identity-on-data convention](#5-phase-2--identity-on-data-convention)
- [§6 Phase 3 — ambient `Animate(...)` transaction](#6-phase-3--ambient-animate-transaction)
- [§7 Considered and rejected: op-capturing `UseList<T>`](#7-considered-and-rejected-op-capturing-uselistt)
- [§8 Industry comparison](#8-industry-comparison)
- [§9 Open questions](#9-open-questions)
- [§10 Implementation phasing](#10-implementation-phasing)

---

## §1 Motivation

When a single item is prepended to a `ListView<T>`, the user expects WinUI's `ItemContainerTransitions` to animate only the new row. Today, the entire visible list animates as if every container had just appeared. Issue #198 traces this to `Reconciler.Update.cs:2807-2808`:

```csharp
if (o.ItemCount != n.ItemCount)
    lv.ItemsSource = Enumerable.Range(0, n.ItemCount).ToList();
```

WinUI receives a brand-new `IList<int>` rather than an `INotifyCollectionChanged` delta. It cannot recover the structural change from "old list" → "new list", so it tears down all realized containers and replays the entrance theme transition for every visible row. The same pattern repeats in `UpdateTemplatedGridView` (`:2833-2834`) and `UpdateLazyStack` (`:2906-2912`).

The naive fix — "let users pass an `ObservableCollection<T>` directly" — is wrong for Reactor, because:

- Reactor's state model is **immutable replacement**: `setItems([..items, x])`. Component re-renders compare old and new element trees by value. An `ObservableCollection<T>` passed through `UseState` mutates in place; `ReferenceEquals(o.Items, n.Items)` returns true even when items changed, and the reconciler skips the work.
- The natural Reactor mutation idiom doesn't capture operations at all — it produces a new value. Forcing developers to call `oc.Insert(0, x)` instead of `setItems([..items, x])` breaks the model.

The fix has to be **reconciler-internal**: an internally-owned `ObservableCollection` per mounted items control, populated from a keyed diff of the user's immutable list. WinUI sees the delta; the developer keeps writing functional setState.

This spec generalizes that observation into a unified identity model that already partly exists in Reactor and just needs to be finished and applied consistently across templated and hand-built collection rendering.

---

## §2 What already exists

The keyed-identity primitive is already in place at the Element layer:

- `Element.Key` — `src/Reactor/Core/Element.cs:28`:
  ```csharp
  /// Optional key for stable identity across re-renders (like React's key prop).
  public string? Key { get; init; }
  ```
- `.WithKey(string)` fluent extension method.
- `ChildReconciler.Reconcile` (called from `Reconciler.cs:927` via `ReconcileChildren`) — described in the file header as *keyed LIS + positional*, performs React-style child diffing using `Element.Key` for matching across reorders.

What this means: this already works correctly today for hand-built element trees:

```csharp
FlexColumn(items.Select(item =>
    Border(TextBlock(item.Name)).WithKey(item.Id)).ToArray())
```

Insert / remove / move at any position reconciles cleanly because `ChildReconciler` reads `Element.Key` and produces an LIS-minimal sequence of mount / move / unmount operations on the underlying `Panel.Children`.

What does **not** work today, and what this spec fixes:

1. **Templated lists (`ListView<T>`, `GridView<T>`, `LazyVStack<T>`)** don't participate in keyed reconciliation. They have their own `KeySelector` parameter on `TemplatedListElementBase` (`src/Reactor/Core/Element.cs:2811`) but it is never threaded through to a delta channel — the reconciler short-circuits with `ItemsSource = Enumerable.Range(...)`.
2. **Identity is per-consumer, not per-data-type.** `KeySelector` must be passed at every `ListView<T>` call site; `.WithKey(item.Id)` must be remembered at every hand-built `.Select(...)`. The two can drift (key by `Id` here, `Slug` there). There's no convention that ties the key to the data type itself.
3. **Animation intent is per-element, not transactional.** Today animation is attached via `ThemeTransitions` / `ImplicitTransitions` / `LayoutAnimation` modifiers on each element. There is no equivalent to SwiftUI's `withAnimation { ... }` block that wraps a state update and propagates animation context through the resulting diff.

---

## §3 The unified identity model

The end-state design has three layers, with item identity flowing top-to-bottom:

| Layer | Carrier | Wired in |
|---|---|---|
| **Data** | `IReactorKeyed.Key` (optional convention) | Phase 2 |
| **Element** | `Element.Key` (set via `.WithKey()` or auto-discovered from data) | Already exists |
| **Reconciler dispatch** | `ChildReconciler` (panel children) / internal `ObservableCollection<ReactorRow>` (templated lists) | Phase 1 closes the gap |
| **Animation intent** | Ambient `AsyncLocal<AmbientAnimation>` set by `Animate(.Spring, () => setItems(...))` | Phase 3 |

The same conceptual key flows through both paths. The reconciler dispatches differently because WinUI virtualizes `ListView` via `ContainerContentChanging` (item-data-driven) and `Panel.Children` via direct UIElement insertion, but the component author sees one model.

### Matrix

| Scenario | Identity source | Delta channel | Animation channel |
|---|---|---|---|
| Hand-built `FlexColumn(items.Select(... .WithKey(item.Id)))` | `Element.Key` | `ChildReconciler` → `PanelChildCollection` move/insert/remove | Per-element transitions today; ambient `Animate(...)` after Phase 3 |
| Templated `ListView<T>(items, t => t.Id, ...)` | `KeySelector` → `ReactorRow.Key` | Internal `ObservableCollection<ReactorRow>` → WinUI `INotifyCollectionChanged` | WinUI `ItemContainerTransitions` triggered by the OC delta |
| Either path with `T : IReactorKeyed` | Auto-discovered from `T.Key` | (same as above) | (same as above) |

---

## §4 Phase 1 — internal ObservableCollection delta

Closes issue #198. No public API change.

### Internal state

Add an internal state struct, attached to the mounted ListView/GridView/ItemsRepeater control (via existing `SetElementTag` mechanism or a dedicated attached property):

```csharp
internal sealed class ReactorListState
{
    public ObservableCollection<ReactorRow> Source { get; } = new();
    public Dictionary<string, ReactorRow> ByKey { get; } = new();
    public List<string> LastKeys { get; } = new();
}

internal sealed class ReactorRow
{
    public int Index { get; set; }      // current position in current Items list
    public string Key { get; set; } = ""; // from KeySelector
}
```

`ReactorRow` is a reference type by design. WinUI's `INotifyCollectionChanged` consumers use object identity to track items across the event stream; reusing the same `ReactorRow` instance for surviving keys lets WinUI distinguish "this item moved" from "removed + inserted." Boxed `int` values would not give this (each indexer access re-boxes).

### Mount path

Replace the `ItemsSource = Enumerable.Range(...)` calls at:

- `Reconciler.Mount.cs:1852` (`MountTemplatedListView`)
- `Reconciler.Mount.cs:1896` (`MountTemplatedGridView`)
- `Reconciler.Mount.cs:2824` (`MountLazyStack` — see §4.4 for ItemsRepeater specifics)

with:

1. Allocate a `ReactorListState`.
2. For each `i` in `0..ItemCount`: create `ReactorRow { Index = i, Key = el.KeySelector(items[i]) }`, append to `Source`, record in `ByKey` and `LastKeys`.
3. `lv.ItemsSource = state.Source;`
4. Stash `state` on the control.

`ContainerContentChanging` keeps reading `args.ItemIndex` (`Reconciler.Mount.cs:1806`); since `Source[i]` corresponds positionally to `n.Items[i]`, the existing handler is unchanged. Selection/click handlers change slightly: `args.ClickedItem is ReactorRow row` → dispatch via `row.Index`.

### Update path — keyed diff

Replace `Reconciler.Update.cs:2807-2808`, `:2833-2834`, and `:2906-2912` with a single helper `ApplyKeyedDiff(state, newItems, keySelector)` using **React's children-reconciliation algorithm** (not LCS):

1. Walk `oldKeys` and `newKeys` in lockstep from index 0. While keys match, advance both. (Common case: append, no-op middle.)
2. On first mismatch, build a `Dictionary<string, ReactorRow>` from the remaining old rows.
3. Walk new keys from the mismatch point:
   - If key exists in the dict: it's a survivor. If its current OC index ≠ desired index, emit `Source.Move(currentIndex, desiredIndex)`. Update `ReactorRow.Index`. Remove from dict.
   - Else: new key. Emit `Source.Insert(desiredIndex, new ReactorRow { ... })`.
4. After the loop, any keys still in the dict are removed: emit `Source.RemoveAt(currentIndex)`. Process in descending index order so earlier indexes stay stable.
5. Update `state.LastKeys` and `state.ByKey`.

This is O(n) wall-clock with one hash map. Matches React's algorithm. Does not always produce minimal move counts (e.g., reversing a list yields N moves, not 1), which is acceptable — the animation reads correctly either way.

After the diff, the existing `RefreshRealizedContainers` (`Reconciler.Update.cs:2759`) call is preserved. New containers get their content via `ContainerContentChanging`; surviving containers get their per-row content reconciled via `RefreshRealizedContainers`. No double work, because `RefreshRealizedContainers` already iterates realized children only.

### Fast paths and fallbacks

For the hottest cases, short-circuit before the general algorithm:

- **No change** (`oldKeys.SequenceEqual(newKeys)`) → skip diff, call `RefreshRealizedContainers` for in-place row updates only.
- **Single append**, **single prepend**, **single remove-end**, **single remove-front** → one OC op, no dict allocation.
- **Bulk replace bailout**: if more than 25% of keys changed, OR if keys collide within `newKeys`, fall back to `lv.ItemsSource = newList` (today's behavior — degraded animation but correct).

### ItemsRepeater specifics

`ElementFactory<T>._mountedElements` (`src/Reactor/Core/ElementFactory.cs:20`) is currently keyed by `int` index. After an insert at position 0, every existing entry's effective index shifts by one. Re-key the dictionary by `string Key` instead:

```csharp
private readonly Dictionary<string, Element> _mountedElements = new();
```

`RefreshRealizedItems` iterates realized indexes from the repeater, reads `Source[i].Key`, looks up the old Element by key, and reconciles. `GetElement` / `RecycleElement` translate the WinUI index ↔ key via the same `state.Source` lookup.

### What changes for callers

Nothing. The public DSL — `ListView<T>(items, keySelector, viewBuilder)` — is unchanged. Component authors keep writing `setItems(...)`. The only observable behavioral difference: WinUI now sees deltas and animates the right containers.

---

## §5 Phase 2 — identity-on-data convention

Today `KeySelector` and `.WithKey(item.Key)` must be remembered at every call site that consumes a list of items. They can drift across consumers, and the boilerplate is repetitive. Introduce an optional convention so the key flows from the data type once.

### Option A — marker interface

```csharp
namespace Microsoft.UI.Reactor;

public interface IReactorKeyed
{
    string Key { get; }
}
```

When `T : IReactorKeyed`, all of these become optional:

- `KeySelector` on `ListView<T>` / `GridView<T>` / `LazyVStack<T>` / `LazyHStack<T>` defaults to `t => t.Key`.
- An overload `WithKey<T>(this Element el, T item) where T : IReactorKeyed` shortens hand-built sites.

### Option B — convention-based property discovery

Auto-discover a property named `Key` or `Id` on `T` via cached reflection at first use, fail at construction time if the type has neither (with a clear error pointing to either Option A or an explicit `KeySelector`).

**Recommendation: Option A.** Explicit and statically checkable. Option B has the SwiftUI feel but introduces invisible runtime contracts that are hard to discover when they break.

### Migration path

`KeySelector` and `.WithKey(string)` both remain. Option A adds defaulting; nothing changes for existing callers. Sample apps and docs get updated to use `IReactorKeyed` for the common case.

---

## §6 Phase 3 — ambient `Animate(...)` transaction

The SwiftUI analog. After Phase 1 the reconciler has a structured op stream out of `ApplyKeyedDiff`; this phase routes animation intent into that stream.

### Public API

```csharp
namespace Microsoft.UI.Reactor;

public static class Animation
{
    public static void Animate(AnimationKind kind, Action action);
    public static T Animate<T>(AnimationKind kind, Func<T> action);
}

public enum AnimationKind { None, Default, Spring, EaseIn, EaseOut, EaseInOut }
```

Usage:

```csharp
Animate(AnimationKind.Spring, () => setItems([..items, x]));
```

### Mechanism

1. `Animate` pushes an `AmbientAnimation { Kind = ... }` onto an `AsyncLocal<AmbientAnimation?>` stack and invokes `action`.
2. State setters from `UseState` / `UseReducer` read the ambient at dispatch time and stash it on the pending render.
3. The reconciler, when applying `ApplyKeyedDiff` ops, configures the WinUI `ItemContainerTransitions` for that single render to match the kind (or, for finer control, attaches a per-container Composition animation to the affected `ReactorRow` containers).
4. The same ambient is consumed by `ChildReconciler` for hand-built trees — it sets the appropriate `LayoutAnimation` / `ImplicitTransitions` on mount/move/unmount for the duration of that render.
5. After render commit, the ambient clears.

### Scope discipline

`Animate(...)` only affects:

- ListView/GridView/ItemsRepeater container animations driven by `ApplyKeyedDiff` ops, AND
- `ChildReconciler` mount/move/unmount for keyed children.

It does **not** animate arbitrary property changes (color, size) on existing leaves — that remains the job of `WithImplicitTransition` etc. on individual elements. Conflating the two would surprise users and is not what SwiftUI does either.

### Defer to Phase 1 evidence

This phase is sketched but not pinned down — the right surface depends on how Phase 1 surfaces the op stream internally. Revisit after Phase 1 lands.

---

## §7 Considered and rejected: op-capturing `UseList<T>`

A `UseList<T>()` hook that captures `Insert` / `RemoveAt` / `Move` operations during dispatch, exposing them to the reconciler so it can skip the keyed diff entirely.

```csharp
// REJECTED design — for context only.
var todos = UseList<Todo>(initial: []);
todos.Insert(0, newTodo);  // captured as Insert op
todos.RemoveAt(3);          // captured as RemoveAt op
```

This is what Jetpack Compose does (`mutableStateListOf`). It was tempting because it cleanly preserves *intent* — Move is distinct from Insert+Remove — and avoids any diff work at all.

**Why rejected:**

1. **Composability breaks through derived collections.** `var visible = todos.Where(t => !t.Done).ToList()` recomputes a new list. Ops on `todos` don't translate to ops on `visible`. The moment the user does anything beyond rendering the raw list, the reconciler falls back to diffing anyway — and `UseList<T>` becomes a footgun that only helps in the narrow top-level-passthrough case.
2. **Two-track state model.** `UseState<IReadOnlyList<T>>` would still need to exist for the common case; `UseList<T>` would be a separate track with different semantics. Doubling the surface for a marginal performance win.
3. **SwiftUI and React, the two frameworks Reactor most resembles, both rejected this path.** Both are pure state-based and recover ops via keyed diff. Compose succeeds with op-capture because *the whole* reactivity model is fine-grained-observable; bolting it onto one collection type in a re-render-everything framework doesn't pay.

The right answer to the "reducer is throwing away information" intuition is **not** to capture the op in the reducer — it's to (a) make the diff cheap and correct (Phase 1), (b) push identity to the data layer so the diff has good signal (Phase 2), and (c) carry *animation intent* (not operation) on a parallel ambient channel (Phase 3). That's the SwiftUI architecture, and it composes through derived collections without special cases.

---

## §8 Industry comparison

Surveyed during design (full sources retained in conversation history; abridged here):

| Framework | State model | Diff | Animation primitive |
|---|---|---|---|
| **React (fiber)** | State-based; `setState(newList)` | Single-pass keyed children diff: lockstep walk → `Map<key, fiber>` for tail. O(n), heuristic. Not Myers/LCS. | Not built-in; libraries (`react-transition-group`, `framer-motion`) wrap mount/unmount lifecycle externally |
| **SwiftUI** | State-based; `@Published`/`@Observable` | Identity diff via `Identifiable`. Simple ops handled cheaply; `Array.difference(from:)` (Myers) for complex reorders | First-class: `withAnimation { ... }` sets an ambient `Transaction`; diff result is tagged; views animate via `.transition(...)` |
| **Jetpack Compose** | Mixed; `mutableStateListOf<T>` is op-observable | N/A on op-tracked lists; per-key diff elsewhere | `animateContentSize`, `AnimatedVisibility`, `LookaheadLayout` |
| **Reactor (this spec, end state)** | State-based; `setItems(newList)` | React-style keyed diff into internal OC (Phase 1) | Ambient `Animate(...)` (Phase 3); per-element transitions remain |

CRDT-derived approaches (fractional indexing, RGA, LSEQ, Yjs YATA) were surveyed and **rejected for this problem**: they solve convergent ordering under concurrent inserts across distributed writers. Reactor is a single-writer in-process framework — the cost is wrong (key-length growth, tombstones, per-row clock state) for guarantees that aren't needed. Fractional indexing remains a valid *call-site helper* for users building drag-to-reorder UIs without natural DB IDs (separate utility, out of scope for this spec).

---

## §9 Open questions

### Phase 0 resolutions (2026-05-16)

- **Q1 — RESOLVED: warn-and-bailout.** A `KeySelector` that produces a duplicate key inside one update is almost always a developer bug, but it is recoverable: the diff falls back to the legacy `ItemsSource = Enumerable.Range(...)` path so the user still sees correct data. We emit a one-shot diagnostic via `ReactorDiagnostics` (gated to once per `(control, set-of-duplicate-keys)`) explaining the bailout. Hard-fail would punish a user whose data set transiently dedupes wrong — e.g. while two server-side IDs reconcile during a refresh.
- **Q2 — RESOLVED: deferred to a Phase 6 Roslyn analyzer (`REACTOR_DSL_001`).** Children produced by `.Select(...)` and passed to `FlexColumn` / `VStack` / `Column` will get a missing-key warning with a code-fix that inserts `.WithKey(item.Id)` when the lambda parameter exposes an `Id` or `Key` property. Doing this at runtime is too late (the user would see a flash of replaced UI before the warning fires); doing it at compile time catches the bug before merge.
- **Q3 — RESOLVED: AsyncLocal survives until render commit, with a caveat.** `AsyncLocal<T>` flows through `await` and through `DispatcherQueue.TryEnqueue` continuations *provided the continuation captures via `ExecutionContext`* — which `DispatcherQueue` does by default on WinUI 1.5+. We confirmed this against a `dotnet/winui` issue thread and a local unit test (see `tests/Reactor.Tests/AnimationAmbientTests.cs` in Phase 3.6). The caveat: if a user wraps the setter in `Task.Run(...)` and never `await`s back to the UI thread, the ambient is lost. We document this and provide a guard: `Animate(...)` snapshots the ambient at setter dispatch time and stores it on the pending render request (so even if the render commit runs on a later turn, the right ambient is read). This snapshot pattern also gives us the answer for nested `Animate(...)` blocks (inner kind wins; outer resumes after).
- **Q4 — RESOLVED: per-container Composition animations, not shared `ItemContainerTransitions` mutation.** Mutating `ItemContainerTransitions` on the control is a shared resource — two overlapping `Animate(...)` calls would race for ownership, and the second call's kind would silently leak onto containers that started animating under the first call. Per-container Composition (`ElementCompositionPreview.SetImplicitShowAnimation` / `SetImplicitHideAnimation` on the new container, attached at the moment the diff emits `Insert` / `Remove`) is scoped to that one container's lifetime, so two overlapping transactions cannot clobber each other. For `Move`, we attach a one-shot offset animation via `Visual.StartAnimation`. This is also what SwiftUI does internally on iOS.
- **Q5 — RESOLVED: manual smoke gate.** WinUI's `RepositionThemeTransition` produces a 200ms easeOut translate. On long-distance moves (e.g. row 0 → row 50 in a 100-row viewport) it still reads correctly because virtualization gates the visible portion of the move — the animation only plays on currently-realized containers, which is the right behavior. Task 1.13 adds a one-time shuffle button to `ListViewPage.cs` so a human verifies the animation reads correctly before merge. Removed before the PR lands.

---

## §10 Implementation phasing

| Phase | Work | Unblocks | Estimated scope |
|---|---|---|---|
| **0** | Spec review, decisions on §9 open questions | Phase 1 | — |
| **1** | Internal `ObservableCollection<ReactorRow>` + React-style keyed diff for `ListView<T>`, `GridView<T>`, `LazyVStack<T>`, `LazyHStack<T>`. Fast paths + bulk-replace bailout. Re-key `ElementFactory<T>._mountedElements` by string. New tests covering single insert/remove/move animation behavior and bulk-replace fallback. **Landed on `feat/042-keyed-list-reconciliation` 2026-05-16**: 41 new unit tests in `tests/Reactor.Tests/Internal/` + 11 selftest fixtures (45 assertions) in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/KeyedListReconciliationFixtures.cs`; all green. | Fixes #198 | ~200 LOC + ~6 tests |
| **2** | `IReactorKeyed` marker interface. Defaulting on `ListView<T>` / `GridView<T>` / `LazyVStack<T>` / `LazyHStack<T>` / `.WithKey<T>(item)`. Update sample apps and docs. **Landed on `feat/042-keyed-list-reconciliation` 2026-05-16**: 2-arg `where T : IReactorKeyed` factory overloads for all 5 templated/lazy collection factories; `WithKey<T, TKey>(this T el, TKey item) where TKey : IReactorKeyed` extension; 13 new unit tests in `tests/Reactor.Tests/IReactorKeyedTests.cs` (op-shape parity vs explicit selectors on insert / remove / move / reverse); `samples/TodoApp/` migrated as the worked example; collections guide updated. | Ergonomics | ~50 LOC + doc updates |
| **3** | Ambient `Animate(...)` transaction. AsyncLocal stack, reader in state-setter dispatch, consumer in `ApplyKeyedDiff` and `ChildReconciler`. Per-render WinUI transitions. **Landed on `feat/042-keyed-list-reconciliation` 2026-05-16**: `Animations.Animate(AnimationKind, …)` public surface + `AnimationAmbient` AsyncLocal scope; `ReactorHost` / `ReactorHostControl` capture-and-re-push around reconcile; `KeyedListDiff.Apply` tags inserted `ReactorRow.PendingEnterAnimation`; templated control `ContainerContentChanging` fires per-container fade-up Composition animation; survivor moves attach implicit `Offset` animation via `ContainerFromIndex` / `TryGetElement` (deferred one dispatcher turn for layout reconcile); `ChildReconciler` consumes the same ambient at insert / move / unmount sites — fade-out exit fabricated when no per-element `.Transition()` set; per-element modifiers continue to win. 11 new unit tests in `tests/Reactor.Tests/Internal/KeyedListDiffAnimationTests.cs` + 3 scope-discipline tests in `tests/Reactor.Tests/Animation/AnimateScopeDisciplineTests.cs` + 6 selftest fixtures in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/AnimateAmbientFixtures.cs` (all green). Docs: `docs/guide/animation.md` "Transactional animation" section. | SwiftUI-style ergonomics | ~150 LOC, design needs revisit after Phase 1 |

Phase 1 is shippable independently and closes #198. Phases 2 and 3 build on it and are independent of each other.

A companion implementation task list will land at `docs/specs/tasks/042-keyed-list-reconciliation-implementation.md` when Phase 1 is ready to start.
