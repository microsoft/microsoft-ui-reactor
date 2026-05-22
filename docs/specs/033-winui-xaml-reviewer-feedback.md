# Reviewer Feedback Response — WinUI/XAML Lifer Review

## Status

**Implemented (in part)** as of 2026-05-01. See
`docs/specs/tasks/033-winui-xaml-reviewer-feedback-implementation.md` for the
phase-by-phase landing log. The seven items §1–§7 each ship the core
behaviorial change in this round; three pieces are deliberately deferred to
a follow-up:

1. The three custom Roslyn analyzers (`REACTOR_GRID_001`, `REACTOR_FUNC_001`,
   `REACTOR_PERSIST_001`) and their code-fixers. The `[Obsolete]` attributes
   already produce the deprecation diagnostic at every call site; the
   analyzers' value-add is the auto-fix.
2. The host-side wiring of `WindowPersistedScope` into `RenderContext` (§7
   item §7.5). The interface, classes, capacity, lifecycle, memory-pressure
   handler, and `UsePersisted(scope)` overload are all in place; the
   per-host scope resolution at hook-entry time is a focused follow-up that
   touches `BeginRender` overloads.
3. The `samples/InteropFirst` UI-driver tests under WinAppDriver — the
   sample itself ships and builds; the test infrastructure is a separate
   piece.

Source: `C:\temp\reactor-winui-xaml-expert-review.md` (an external review by a
WPF → WinUI3 veteran on first contact with the repo).

This spec gathers the design responses to the reviewer's actionable feedback
items. Some items are accepted as written, some are scoped down, and one
("reflection on every memo check") is already resolved in product code; that
item is excluded from this spec on the basis of the audit summarized in
[§0](#0-out-of-scope).

---

## Table of contents

- [§0 Out of scope (reviewer #7 — memo reflection)](#0-out-of-scope)
- [§1 Strongly-typed Grid tracks (reviewer #2)](#1-strongly-typed-grid-tracks)
- [§2 PersistedStateCache lifecycle (reviewer #8)](#2-persistedstatecache-lifecycle)
- [§3 Typed element refs — `ElementRef<T>` (reviewer #9)](#3-typed-element-refs)
- [§4 Two component flavors (reviewer #10)](#4-two-component-flavors)
- [§5 Block-expression escape hatch — `Expr(...)` (reviewer wishlist #8)](#5-block-expression-escape-hatch--expr)
- [§6 SystemBackdrop modifiers — Mica/Acrylic (reviewer wishlist #9)](#6-systembackdrop-modifiers)
- [§7 Interop-first sample (reviewer wishlist #10)](#7-interop-first-sample)
- [§8 Rollout / sequencing](#8-rollout--sequencing)
- [§9 Open questions](#9-open-questions)

---

## §0 Out of scope

### Reviewer #7 — "Reflection on every memo check"

> *"`compType.GetMethod("ShouldUpdate", ...)` runs on every parent re-render,
> no `MethodInfo` caching. 50 components in a tree → 50 reflection calls per
> render cycle."*

**Already resolved in product.** The reconciler dispatches via the
`IPropsComparable` interface — no reflection, no `MethodInfo`:

- `src/Reactor/Core/Component.cs:166-169` — `IPropsComparable.CompareProps`
- `src/Reactor/Core/Component.cs:184-185` —
  `Component<TProps>` implements `IPropsComparable` directly
- `src/Reactor/Core/Reconciler.cs:737-744` — `ShouldUpdateWithProps` is a
  one-liner interface dispatch with a "fallback: always re-render" branch
- `MemoElement` never used reflection; the memo path is array-equality on
  the `dependencies` (`Reconciler.cs:588-597`)

The reviewer was looking at the test-only mirror in
`tests/Reactor.Tests/MemoizationSelfHostTests.cs:74` and
`MemoizationPropagationTests.cs:92`, which intentionally re-derives
`ShouldUpdate(TProps?, TProps?)` via reflection so the harness doesn't depend
on the production `IPropsComparable` interface. Those mirrors are kept for
test-independence, not for performance.

**Action taken (not pending):** an explanatory comment was added to both
test files calling out that the reflection in the harness is not indicative
of product behavior. No further work in this spec.

The completed product fix is also tracked at
`reviewer/reports/fix-list.md` ("Complete (2026-04-11)").

---

## §1 Strongly-typed Grid tracks

### Problem

`Grid(["*", "Auto", "200"], ["*"], children…)` — track sizes are passed as
strings, parsed at runtime, and entirely string-typed. From the review:

> *"They left XAML and brought XAML's most string-typed corner with them.
> This should be `Grid(Star(1), Auto, Px(200))` from day one."*

Concrete consequences:

- No compile-time check on track syntax. `"*"` vs `"1*"` vs `"Star"` all
  produce different runtime parser outcomes.
- No IntelliSense for the legal forms (`Auto`, `*`, `<n>*`, `<n>`,
  `<n>.<n>`, `<n>.<n>*`).
- Star weights live in a string format that parsers and humans must agree
  on (`"0.33*"`).
- Spec 027 (`Accelerator(VirtualKey.S, VirtualKeyModifiers.Control)`) set
  the precedent that input is typed; grid tracks are inconsistent with that
  direction.

### Surface today

`src/Reactor/Elements/Dsl.cs:272-275`
`src/Reactor/Core/Element.cs:1155` (`GridDefinition(string[] Columns, string[] Rows)`)

Used by ~60 call sites (Reactor.TestApp demos, ReactorGallery, DataGrid,
StylingGallery, samples). Two known external consumers per user note —
break gently.

### Proposed API

A new `GridSize` value type with three smart-constructor statics, public on
`Microsoft.UI.Reactor`:

```csharp
public readonly record struct GridSize(double Value, GridUnitType Type)
{
    public static GridSize Auto { get; } = new(1, GridUnitType.Auto);
    public static GridSize Star(double weight = 1) => new(weight, GridUnitType.Star);
    public static GridSize Px(double pixels)        => new(pixels, GridUnitType.Pixel);

    // Implicit conversion to WinUI's Microsoft.UI.Xaml.GridLength so the
    // typed form composes with anything that takes a GridLength (RowDefinition,
    // ColumnDefinition, attached property setters, .Set(...) escape hatches).
    public static implicit operator Microsoft.UI.Xaml.GridLength(GridSize s)
        => new(s.Value, s.Type);
}
```

Re-exports `Microsoft.UI.Xaml.GridUnitType` directly — no parallel enum.

#### Why a new type, not the WinUI `GridLength`

Three constraints decided this:

1. **Naming collision.** Microsoft.UI.Reactor (Reactor) users routinely `using Microsoft.UI.Xaml;`
   for `Thickness`/`Visibility`/`Window`/etc. Introducing
   `Microsoft.UI.Reactor.GridLength` produces `CS0104: ambiguous reference`
   in those files.
2. **No static helpers on the WinUI type.** `Microsoft.UI.Xaml.GridLength`
   is a struct we don't own. We can't add `Star(...)`/`Px(...)` to it
   directly; the closest options are extension members (C# 14, methods
   only) or a sibling helpers class.
3. **`GridLengths` (plural) helpers class is a typo bug factory.**
   `GridLength` vs `GridLengths` differing by one character is exactly the
   kind of thing that compiles 80% of the time and ships wrong. Rejected
   for that reason.

`GridSize` clears all three: distinct from any WinUI type, owns its own
statics, has no plural-form trap. The name reads naturally to a XAML
audience ("the size of a row/column in a Grid") without leaning on CSS
Grid jargon.

#### Call site

```csharp
Grid(
    columns: [GridSize.Star(), GridSize.Auto, GridSize.Px(200)],
    rows:    [GridSize.Star()],
    children: …)
```

With `using static Microsoft.UI.Reactor.GridSize` (when one wants brevity
in a single file), it shortens to:

```csharp
Grid(columns: [Star(), Auto, Px(200)], rows: [Star()], …)
```

We do **not** add a namespace-level `Track`/`GridSizes` static of free
`Auto`/`Star`/`Px` symbols — `Auto` at the factory namespace level would
collide with too much user code, and the per-file `using static` opt-in
keeps the choice in the developer's hands.

### Migration path

Both shapes coexist for one release:

```csharp
public static GridElement Grid(
    GridSize[] columns, GridSize[] rows,
    params Element?[] children);

[Obsolete("Use Grid(GridSize[], GridSize[], ...) — GridSize.Star/.Auto/.Px helpers. " +
          "String-track overload will be removed in the next minor release.",
          error: false)]
public static GridElement Grid(
    string[] columns, string[] rows,
    params Element?[] children);
```

`GridDefinition` itself flips to typed storage (`GridSize[]`); the string
overload internally parses to `GridSize[]` and forwards. Internal users
(`InterspersedGrid`, the DataGrid resize overlay, etc.) move to the typed
form in the same change.

### Implementation steps

1. Add `GridSize` (~30 LoC, unit-tested).
2. Add the typed `Grid(GridSize[], GridSize[], …)` overload alongside the
   string one. Both build the same `GridElement`.
3. Flip `GridDefinition` internal representation to `GridSize[]`. The
   string ctor stays public (for compat) and parses on construction.
4. Migrate all in-repo callers (samples, demos, internal helpers) in the
   same PR — the `[Obsolete]` warning is for external consumers, not us.
5. Add a Roslyn analyzer `REACTOR_GRID_001` ("string track form is
   deprecated; prefer GridSize.Star/Auto/Px") so the obsolete warning
   lands as a diagnostic rather than a build wall.

### Why not break harder

User note: a couple of external consumers exist. A one-release
`[Obsolete(error: false)]` window with an analyzer + the typed form
side-by-side is the lowest-friction migration. Hard break is reserved for
the release after.

### Tests

- Unit: `GridSize` equality, parsing roundtrip from string form,
  implicit conversion to `Microsoft.UI.Xaml.GridLength`.
- Reconciler: identical `GridElement` produced by both factory shapes for
  the equivalent input.
- Analyzer: golden test for `REACTOR_GRID_001` firing on the string form.

---

## §2 PersistedStateCache lifecycle

### Problem

`src/Reactor/Core/PersistedStateCache.cs` — process-global
`ConcurrentDictionary<string, object?>`. Has a `MaxEntries = 4096` cap with
"reject new keys when full" semantics, but:

- No per-window or per-component-tree scoping. A modal opened 100 times
  with `UsePersisted("dialog-state-{id}", …)` accumulates 100 entries for
  the lifetime of the process.
- No TTL.
- Reject-when-full is the wrong policy for caches: it pins the *first*
  4096 keys and starves any later, hotter keys. LRU is the standard
  answer.
- No way to scope state to a window (SwiftUI's `@SceneStorage`) or to the
  app session (Compose's `rememberSaveable` is per-Activity).

The reviewer's complaint about "no eviction, no size cap" is half-correct
— a cap exists, but its policy is poor and its scope is global.

### Goals

- **Bounded memory:** any non-pathological app should converge to a
  steady-state cache size, not grow with usage.
- **Scoped state:** `UsePersisted` should default to a sensible scope
  (the hosting window's lifetime), with `UsePersisted(global: true, …)`
  as an opt-in for cross-window persistence.
- **Predictable eviction:** LRU within a scope, capped per-scope.
- **No silent drops:** if the cap is hit, evict — never refuse.

### Proposed design

#### `IPersistedStateScope`

```csharp
public interface IPersistedStateScope
{
    bool TryGet<T>(string key, out T value);
    void Set<T>(string key, T value);
    void Remove(string key);
    int Count { get; }
}
```

#### Two built-in scopes

- `WindowPersistedScope` — keyed off `ReactorHost`/`ReactorHostControl`
  instance. Disposed when the host unloads. Default scope.
- `ApplicationPersistedScope` — process-lifetime. Replaces today's
  `PersistedStateCache`. Used when the developer passes `global: true`.

Both wrap a small `LruCache<string, object?>` (custom — no dependency on
`Microsoft.Extensions.Caching` is needed). Cap is configurable; defaults:

| Scope | Default cap | Eviction |
|---|---|---|
| Window | 1024 | LRU |
| Application | 4096 | LRU |

#### `UsePersisted` API change

```csharp
public (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue);

// New overload:
public (T Value, Action<T> Set) UsePersisted<T>(
    string key, T initialValue, PersistedScope scope = PersistedScope.Window);

public enum PersistedScope { Window, Application }
```

The single-arg form keeps source-compat. Its behavior changes from
"global cache, reject-when-full" to "**window** cache, LRU". This is a
behavioral change for users who today rely on cross-window persistence
implicitly — see the migration note below.

#### Resolving the scope from a hook

`RenderContext` already has access to its hosting `Reconciler` via
`BeginRender`. We extend `Reconciler` (or a new `IHostScope` it exposes)
with an `IPersistedStateScope` field per scope kind. The hook resolves
the scope at hook-call time and stores it in the `PersistedHookState`.

### Migration

The reviewer is right that this is a leak. But for users today,
`UsePersisted` *was* implicitly process-global. Flipping the default to
window-scoped is a behavioral change that could surprise.

Two-release migration:

1. **Release N.** Ship both APIs. Default is window-scoped *if and only
   if* the developer opts in to the new default by setting
   `ReactorAppOptions.PersistedStateScopeDefault`. Otherwise, single-arg
   `UsePersisted` continues to mean "application scope," and we surface
   an analyzer warning `REACTOR_PERSIST_001` recommending the explicit
   form.
2. **Release N+1.** Default flips to `PersistedScope.Window`.
3. The `MaxEntries = 4096` reject-when-full policy is replaced by LRU
   immediately in N — no compat concern there since it was a bug.

### Memory pressure escape valve

`ApplicationPersistedScope` registers for
`Windows.ApplicationModel.Core.MemoryManager.AppMemoryUsageIncreased`.
On `OverLimit` it shrinks to 25% capacity (LRU). Window scope doesn't
need this — it's bounded by window lifetime.

### Tests

- LRU correctness (touch-on-access).
- Window scope: state survives a component unmount/remount within a
  window; *does not* leak across windows.
- Application scope: state survives across windows.
- Memory-pressure shrink callback hits LRU eviction, not a `Clear()`.
- Analyzer fixture for `REACTOR_PERSIST_001`.

### Files touched

- `src/Reactor/Core/PersistedStateCache.cs` (rewritten as
  `ApplicationPersistedScope` + `LruCache<TKey,TValue>` helper).
- New `src/Reactor/Core/PersistedStateScope.cs`,
  `WindowPersistedScope.cs`.
- `src/Reactor/Core/RenderContext.cs:344-380` — `UsePersisted` resolves
  scope at hook entry.
- `src/Reactor.Analyzers` — new `REACTOR_PERSIST_001`.

---

## §3 Typed element refs

### Problem

> *"No `x:Name` analog with type safety. `WithKey($"tile-{i}")` is reconciler
> identity, not 'give me a `Button` reference I can hand to the Composition
> API.' `UseElementRef<Button>()` exists in spirit but isn't a first-class
> hook the way `useRef` in React or `@FocusState` in SwiftUI are."*

### Surface today

- `Microsoft.UI.Reactor.Input.ElementRef` — opaque, exposes
  `FrameworkElement? Current` (`src/Reactor/Input/FocusManager.cs:14-23`).
- `.Ref(ElementRef)` modifier on every element
  (`src/Reactor/Elements/ElementExtensions.cs:1413`).
- `UseElementFocus(ctx)` returns `(ElementRef, RequestFocus)`
  (`src/Reactor/Hooks/UseElementFocus.cs:27`).

So we have the *plumbing*, just no typed surface. Calls into Composition,
Ink, the Input Pointer API — anything wanting a `Button` or
`ScrollViewer` — have to cast `ref.Current as Button`, which is exactly
the friction the reviewer is calling out.

### Proposed API

```csharp
public sealed class ElementRef<T> where T : FrameworkElement
{
    private readonly ElementRef _inner;
    internal ElementRef(ElementRef inner) { _inner = inner; }

    public T? Current => _inner.Current as T;

    // Implicit conversion to the untyped form so existing FocusManager,
    // .Ref() modifier, etc. all keep working without overload bloat.
    public static implicit operator ElementRef(ElementRef<T> typed) => typed._inner;
}

public static class TypedElementRef
{
    public static ElementRef<T> Create<T>() where T : FrameworkElement
        => new(new ElementRef());
}
```

Hook surface:

```csharp
public static ElementRef<T> UseElementRef<T>(this RenderContext ctx)
    where T : FrameworkElement
{
    var (untyped, _) = ctx.UseState(new ElementRef());
    return new ElementRef<T>(untyped);
}

public static ElementRef<T> UseElementRef<T>(this Component component)
    where T : FrameworkElement
    => component.Context.UseElementRef<T>();
```

The `.Ref(...)` modifier overloads to accept the typed form — but because
of the implicit conversion this is basically a no-op:

```csharp
var btn = ctx.UseElementRef<Button>();
return Button("Press me", onPress).Ref(btn);

// Later, in a UseEffect:
btn.Current?.Focus(FocusState.Programmatic);
btn.Current?.StartAnimation(...);   // Composition handle — typed.
```

### Mismatch detection

If the developer writes
`Button(...).Ref(useElementRef<TextBox>())`, today's reconciler would
silently leave `Current` as `null` (the `as` cast fails). We add a
`DEBUG`-only assertion in the reconciler's mount path: when populating an
`ElementRef`, if `_current` is non-null but the user passed a typed
`ElementRef<T>` whose `T` doesn't match, `Debug.Fail` with a clear
message. Release builds keep silent-null behavior — no perf cost on the
hot path, loud failure during dev-loop.

### Trim/AOT

`ElementRef<T>` carries no reflection. `T` is only used for the cast.
Fully AOT-safe.

### Tests

- Mount a control, verify `Current` is the typed instance.
- Unmount: `Current` returns `null`.
- Type mismatch: in DEBUG, `Debug.Fail` triggers; in RELEASE, `Current`
  is `null`.
- Same `ElementRef<T>` survives parent re-renders (identity stable).

### Files touched

- `src/Reactor/Input/FocusManager.cs` — add `ElementRef<T>`.
- `src/Reactor/Hooks/UseElementRef.cs` — new file.
- `src/Reactor/Elements/ElementExtensions.cs:1413` — overload `.Ref(...)`
  to accept typed form (free via implicit conv, but explicit overload
  helps IntelliSense).

---

## §4 Two component flavors

### Problem

> *"Class `Component` / `Component<TProps>` and `Func(ctx => ...)` /
> `Memo(ctx => ..., deps)`. Pick one. Or unify them via a source generator.
> Right now, the class form has the better story (props equality for free
> via records, default `ShouldUpdate`) but the lambda form is what shows
> up in samples. Newcomers get whiplash."*

### Surface today

There are actually **five** ways to compose a subtree, not two. The
reviewer's "two flavors" framing collapses raw methods (no
`RenderContext`) into background; surfacing them is part of the answer.

| Form | Where | Own hook scope? | Reconciler boundary? | Memoization | Props typed? | Identity for Devtools |
|---|---|---|---|---|---|---|
| Raw method `static Element X(…) => …` | user code | **No** — hooks would attach to the *caller's* context | No — output inlined into parent's children | n/a — re-evaluated whenever the call site re-renders | n/a (just method args) | No |
| `Component` (propless class) | `Component.cs:9-24` | Yes | Yes | `ShouldUpdate() => false` default — only self-triggered + context | n/a | **Yes** (class name) |
| `Component<TProps>` | `Component.cs:175-195` | Yes | Yes | Record-`Equals` default | **Yes** | **Yes** (class name) |
| `Func(ctx => …)` | `Dsl.cs:530` | Yes | Yes | **None** — re-renders every parent render | No (closure capture) | No (anonymous) |
| `Memo(ctx => …, deps)` | `Dsl.cs:537` | Yes | Yes | Deps array | No (closure capture) | No (anonymous) |

**What `Func` actually buys you over a raw method:** its own
`RenderContext`, so hooks (`UseState`, `UseEffect`, …) attach here
instead of bubbling up to the caller. **What it buys you over `Memo` with
matched deps:** essentially nothing — `Func` is "I want hook state and
explicitly *no* memoization," which is a niche slice of cases.

### What "unify" could look like (no source generator)

Per user direction, **no code generator** in this round. The decision is
purely about API canonicalization, samples, and documentation. Here are
the three options on the table:

#### Option A — keep all four, sharpen the docs

Status-quo plus a docs page that says "use this form when X, that form
when Y." Specifically:

- `Component<TProps>` — **default for stateful, multi-parameter, or
  named-and-reused** components. Free props equality.
- `Memo(ctx => …, deps)` — **default for inline subtrees** that need
  memoization. Lambda capture stays explicit via `deps`.
- `Func(ctx => …)` — **soft-deprecated**: it is a strict subset of
  `Memo` with the deps array unset. We add a `Memo(ctx => …)` zero-deps
  overload that matches `Func(...)` semantics (re-render on every parent
  render), then mark `Func` `[Obsolete]`.
- Propless `Component` — keep, it has a real role for components that
  are referenced multiple times by class name.

**Pro:** Smallest blast radius. No behavior change. No migration cost.
**Con:** Doesn't fully address the "two flavors" smell — there are still
two ways to write a component (class vs lambda). Mitigated by docs and
sample consistency.

**Sample shape under Option A:**

```csharp
// Inline, anonymous, has state:
Memo(ctx => {
    var (count, setCount) = ctx.UseState(0);
    return Button($"{count}", () => setCount(count + 1));
})

// Named, reusable, typed props:
public sealed class Counter : Component<CounterProps>
{
    public override Element Render() {
        var (count, setCount) = UseState(Props.Initial);
        return Button($"{count}", () => setCount(count + 1));
    }
}
public sealed record CounterProps(int Initial = 0);
// Use with: Component<Counter, CounterProps>(new(Initial: 5))
```

#### Option B — collapse `Func`/`Memo` into one factory, keep the class form

Make `Memo(...)` the single inline form. `Func` gets a deprecation
warning. Leaves `Component<TProps>` as the only "named" form.

Concretely:

```csharp
// Today:
public static FuncElement  Func(Func<RenderContext, Element> render);
public static MemoElement  Memo(Func<RenderContext, Element> render,
                                 params object?[] dependencies);

// Proposed:
public static MemoElement  Memo(Func<RenderContext, Element> render,
                                 params object?[] dependencies);
// New zero-arg behavior: dependencies.Length == 0 means "always re-render"
// (matching old Func behavior). Today, empty deps means "render once."

[Obsolete("Use Memo(ctx => …) with no deps for the same behavior. " +
          "Func will be removed in the next minor release.", error: false)]
public static FuncElement  Func(Func<RenderContext, Element> render);
```

**Behavioral catch:** today, `Memo(ctx => …)` with empty `params` deps
maps to "render once and never again." If we collapse `Func` into
`Memo`, the empty-deps case has to mean "render on every parent
render" — a *breaking change* for anyone relying on empty-deps
"render-once" semantics today.

We can split this two ways:

- **B1.** Keep `Memo` semantics as-is (empty deps = render once). Add a
  new `Memo.OnEveryRender(ctx => …)` static (or a sentinel `null` deps
  array) for "always re-render." Deprecate `Func` to point at the new
  overload. Cleanest from a semantics standpoint, but introduces a third
  spelling.
- **B2.** Keep `Memo(...)` *behavior* unchanged for backward compat, but
  promote it as the canonical form and demote `Func`. Don't try to make
  one factory cover all three behaviors. Outcome: same as **Option A**
  in practice.

**Pro:** One inline form to teach.
**Con:** B1 introduces a third surface; B2 collapses to A. Either way,
the *real* duality (class vs lambda) remains.

#### Option C — collapse class form into lambda form, keep `Memo`

The reviewer's "lambda form is what shows up in samples" implies he'd
rather see the lambda form win. But the class form has features the
lambda form *cannot* express today without source generation:

- **Typed props** (`Component<TProps>`) — `Memo` only has the
  closure-captured variables, no compile-time props record.
- **Default `ShouldUpdate`** based on record equality, not array
  equality of deps.
- **Reflectable identity** for Devtools, navigation, hot reload, error
  boundaries (the reconciler's `node.Component.GetType().Name`).
- **Lifecycle hooks via `override`** instead of `UseEffect(...)`, which
  some teams prefer for testability.

To collapse class form into lambda form *without* a source generator
means giving up some of these features or duplicating them in the lambda
form. Not recommended.

#### Recommendation — four-way "when to use which"

**Option A + the `Func`-into-`Memo` deprecation half of B2**, plus an
explicit "raw method" doc story that the reviewer didn't see.

1. **Raw method (`static Element X(...) => …`)** — *the default for shared
   layout chunks.* No state, no effects, no own context. Just a
   composition helper. Use this whenever you don't need hooks. Cheapest
   form — no reconciler node, no boundary.

   ```csharp
   static Element Greeting(string name) =>
       VStack(Text($"Hello, {name}"), Text("Welcome back"));
   ```

2. **`Memo(ctx => …, deps)`** — *the canonical inline form for stateful
   subtrees.* Has hooks, has memoization, has a reconciler boundary. Use
   for inline pieces that need state/effects.

   ```csharp
   Memo(ctx => {
       var (count, setCount) = ctx.UseState(0);
       return Button($"{count}", () => setCount(count + 1));
   })
   ```

3. **`Component<TProps>`** — *the named, reusable, typed-props form.* Use
   when the component is referenced from multiple places, the parameter
   list deserves a `record TProps`, or you want lifecycle as `override`
   methods. Free record-equality memoization; reflectable identity for
   Devtools, navigation, error boundaries, hot reload.

4. **Propless `Component` class** — niche but real. A named, stateful,
   parameterless subtree that *defaults to not re-rendering on parent
   re-render* (`ShouldUpdate() => false`). Use when you want
   class-name identity without a props record.

5. **`Func(ctx => …)`** — *soft-deprecated.* It's "inline + own hooks +
   no memoization," which is rarely what you actually want. The
   replacement is `Memo` with appropriate deps (or no deps for
   render-once). Auto-fixer rewrites to `Memo`.

#### Custom-hooks pattern (open question)

A raw method that takes a `RenderContext` and calls hooks on it is
React's "custom hook" pattern by another name:

```csharp
static (string Value, Action<string> Set) UseDebouncedText(
    RenderContext ctx, string initial, TimeSpan delay) { /* … */ }
```

Today this *works* — the hooks attach to the caller's context — but
Reactor doesn't formally bless or guard the pattern. It's a footgun if
the caller forgets the rule "hooks called in the same order every
render," because a raw method makes it less obvious that the call
participates in the caller's hook list.

**Decision deferred** to a separate spec. Options range from "document
and bless the pattern with a `Use*` naming convention + analyzer" to
"introduce a marker attribute that the analyzer enforces." Tracked in
§9.

### Why no source generator (for now)

Per user direction, but also: **a source generator that generates a
class from a free function is a non-trivial amount of code-gen** — it
needs to derive a props record from the parameter list, emit
`ShouldUpdate(TProps?, TProps?)`, route hook calls to the
`RenderContext`, handle defaults, route diagnostics back to the
declaration site. It's the right long-term answer (the reviewer's #1
wishlist item) but it's a separate project. Open as a follow-up in §9.

### Migration

- `Func(...)` → `[Obsolete]` with `error: false`. Auto-fixer in the
  Roslyn analyzer rewrites `Func(ctx => …)` to `Memo(ctx => …)` (deps
  unspecified — render-once is the safer default; opt back in via
  explicit deps if needed).
- All in-repo samples migrate in the same PR.

### Files touched

- `src/Reactor/Elements/Dsl.cs:530` — mark `Func` `[Obsolete]`.
- New `src/Reactor.Analyzers/MemoMigrationAnalyzer.cs`.
- Sample audit across `samples/`.

---

## §5 Block-expression escape hatch — `Expr(...)`

### Problem

The reviewer's "bracket-counting" complaint is about DSL shape
(`params Element?[]` mixing children and properties), and the real fix is
a C# language change tracked in spec 008. **Conditionals themselves are
not the problem** — native `?:` and `switch` expressions already work
inside the DSL because `null` filters out via `FilterChildren`:

```csharp
VStack(
    Header(),
    isError ? ErrorBanner() : null,                    // ternary — fine
    state switch {                                     // switch expr — fine
        LoadingState.Loading => Spinner(),
        LoadingState.Error   => ErrorBanner(),
        LoadingState.Loaded  => Content(items),
        _ => null
    },
    Footer())
```

So we are *not* adding `Switch(...)` helpers. They were solving a problem
that doesn't exist.

The remaining gap is **multi-statement bodies**. C# switch expressions
require expression arms; if a branch needs locals, you have to either
extract a method or fall back to one of these unpleasant shapes:

```csharp
// Extract a local function — works but moves the code far from the call site:
Element Totals() {
    var summary = ComputeSummary(orders);
    return summary.Total > 0 ? TotalsBanner(summary) : null;
}
VStack(Header(), Totals() ?? EmptyElement.Instance, Footer())

// Or an inline IIFE — works but the cast is ugly:
VStack(
    Header(),
    ((Func<Element?>)(() => {
        var summary = ComputeSummary(orders);
        return summary.Total > 0 ? TotalsBanner(summary) : null;
    }))() ?? EmptyElement.Instance,
    Footer())
```

Local functions force a separate declaration. The IIFE works inline but
needs a `Func<Element?>` cast (C# can't infer a delegate type for a bare
`(() => …)`). Both pull the eye away from the surrounding tree.

### Proposal — `Expr(...)`

A single helper:

```csharp
public static Element Expr(Func<Element?> render) => render() ?? EmptyElement.Instance;
```

Use:

```csharp
VStack(
    Header(),
    Expr(() => {
        var summary = ComputeSummary(orders);
        var emphasis = summary.IsToday ? Theme.Primary : Theme.Subtle;
        return summary.Total > 0
            ? TotalsBanner(summary).Foreground(emphasis)
            : null;
    }),
    Footer())
```

### Why a helper for this

The `Expr(...)` form removes the `((Func<Element?>)(…))()` cast
ceremony, which is the only real win — and it's enough of a win.
Quantitatively: the cast is ~25 characters of noise per use, and the
result obscures intent (the reader has to recognize the cast-then-invoke
shape before parsing the body). `Expr(...)` is six characters of intent.

Other reasons:

- **Forward-compatible with C# block expressions.** When the language
  ships block expressions (the in-flight proposal where
  `{ var x = …; yield x; }` becomes a first-class expression), `Expr(...)`
  rewrites to native syntax mechanically. The name "Expr" is short for
  "expression" specifically to mark that mapping. We `[Obsolete]` it
  pointing at native syntax when that day comes.
- **Composes with everything.** The body is plain C# — early returns,
  locals, native `?:` / `switch`, `await` (if the surrounding flow is
  async), pattern matching. No DSL-specific rules.
- **Zero reconciler involvement.** No node, no hook scope, no
  memoization. Identical to inlining the lambda yourself; just less
  ceremonial.

### What it is not

- **Not a hook-bearing component.** `Expr(...)` is pure composition —
  it doesn't take a `RenderContext`, doesn't get its own hook scope,
  doesn't act as a memo boundary. If the body needs hooks, use
  `Memo(...)` or a `Component<TProps>` (see §4).
- **Not a Switch replacement.** Native `switch` expressions are the
  answer for branching over a value.
- **Not the DSL ergonomics fix.** That's spec 008.

### Cost

The lambda allocates per render at the call site (closure capture). For
hot render paths, that matters. Documentation should say: "use raw
expressions (`?:`, `switch`) when the body fits; reach for `Expr(...)`
only when you need locals, and prefer `Memo(...)` if the same
computation is stable across renders."

### Existing `If` / `When`

Stay as-is (`Dsl.cs:555-563`) — shipped, harmless, used by some external
consumers. No deprecation. No new `Switch` overloads.

### Files touched

- `src/Reactor/Elements/Dsl.cs` — add `Expr(Func<Element?>)` near the
  existing `If`/`When` helpers.

---

## §6 SystemBackdrop modifiers

### Problem

> *"Mica/Acrylic/SystemBackdrop modifiers. They're missing and the
> WinUI3-ness of an app is the backdrop. If the answer is
> `.Set(x => x.SystemBackdrop = new MicaBackdrop())`, that's not
> declarative."*

`Grep` confirms zero `SystemBackdrop` references in `src/Reactor`. The
backdrop API in WinUI lives on `Microsoft.UI.Xaml.Window.SystemBackdrop`
and the types are `MicaBackdrop`, `DesktopAcrylicBackdrop`, and
`SystemBackdrop` (the abstract base).

### API

```csharp
public enum BackdropKind { None, Mica, MicaAlt, DesktopAcrylic, AcrylicThin }

public static class BackdropExtensions
{
    /// <summary>
    /// Sets the system backdrop on the hosting Window. No-op if the element
    /// is not the root of a Reactor tree mounted into a Window (i.e. not
    /// inside a ReactorHostControl that doesn't own its window).
    /// </summary>
    public static T Backdrop<T>(this T el, BackdropKind kind) where T : Element;

    /// <summary>
    /// Escape hatch for custom SystemBackdrop subclasses (e.g. a tinted Mica).
    /// </summary>
    public static T Backdrop<T>(this T el, Func<SystemBackdrop> factory) where T : Element;
}
```

Usage:

```csharp
// In the root render of a ReactorApp:
VStack(...).Backdrop(BackdropKind.Mica)
```

### Implementation

The modifier stores the backdrop choice on the element (via
`ElementModifiers`). The reconciler, on root mount, walks up to the
hosting `Window` (already known to `ReactorApp` and `ReactorHostControl`)
and sets `Window.SystemBackdrop`. On a re-render where the modifier
value changes, it swaps it.

For `ReactorHostControl` embedded inside a XAML page (not owning the
window), the modifier no-ops and emits an analyzer-style debug log
("Backdrop is a window-level concept; ignoring on host-control root").

### Compatibility / fallback

`MicaBackdrop` and friends are available on Windows 11 (build 22000+).
On Windows 10, `Window.SystemBackdrop` setter is a no-op (WinUI handles
this). We don't reimplement the fallback ladder ourselves — we delegate
to WinUI's behavior and document it.

### Tests

- Unit (reconciler): root render sets `Window.SystemBackdrop` to the
  correct concrete type for each `BackdropKind`.
- Integration: change the backdrop value across renders and confirm
  swap, not duplicate-set.
- `ReactorHostControl`-without-window scenario: no exception, no setter
  call.

### Files touched

- New `src/Reactor/Elements/BackdropExtensions.cs`.
- New `src/Reactor/Core/BackdropKind.cs`.
- `src/Reactor/Core/ElementModifiers.cs` — add a `Backdrop` slot.
- `src/Reactor/Core/Reconciler.Mount.cs` — apply the backdrop on root
  mount.
- `src/Reactor/Hosting/ReactorApp.cs`, `ReactorHostControl.cs` — surface
  the hosting `Window` to the reconciler so the modifier can resolve it.

---

## §7 Interop-first sample

### Problem

> *"An 'interop-first' sample. Show me a real XAML app — a Page with
> x:Bind, a ViewModel, an INotifyPropertyChanged model — embedding a
> Reactor DataGrid for one panel, with shared theme resources and shared
> commanding."*

User direction: literal interpretation. No fake scenario — just a clean
demonstration of XAML-page-hosts-Reactor.

### Sample location

`samples/InteropFirst/`. New csproj alongside the existing
`samples/ReactorHostControlDemo/` (which is the closest existing sample,
but it's smaller-scoped — just demonstrates `ReactorHostControl` with a
counter).

### Sample shape

A single-window WinUI 3 app:

- `App.xaml` — standard `Microsoft.UI.Xaml.Application`.
- `MainWindow.xaml` — a `Window` with a `Frame` and `Page`.
- `MainPage.xaml` — a XAML page with:
  - A `NavigationView` on the left (XAML-authored).
  - A central area split between:
    1. A XAML `ListView` with `x:Bind` to a `ViewModel.Items` collection
       (showing the conventional XAML/MVVM/INPC story).
    2. A `<reactor:ReactorHostControl>` panel rendering a Reactor
       `DataGrid` over the same data.
  - A bottom command bar with a XAML `AppBar` whose buttons invoke
    `ICommand` properties on the ViewModel — *and* a Reactor-rendered
    overflow toolbar that uses the Reactor `Command` system on the
    same actions, demonstrating the commanding bridge.
- `MainPageViewModel.cs` — `INotifyPropertyChanged`,
  `ObservableCollection<Order>`, two `ICommand`s (Add, Delete).
- `OrdersDataGrid.cs` — Reactor component
  (`Component<OrdersDataGridProps>`) that takes the
  `ObservableCollection<Order>` and renders a
  Reactor `DataGrid`. Data flows in via props; selection flows out via
  a callback.
- Shared resources: `App.xaml` defines `<Color>`/`<SolidColorBrush>`
  resources. The Reactor side reads them via the existing
  `ThemeRef`/`ThemeResource` bridge. **No** redefinition on the Reactor
  side.
- Shared commanding: the same `ICommand` instances are wrapped as
  Reactor `Command`s via a small bridge helper
  (`Reactor.Command FromICommand(ICommand)`) — the reviewer's bridge
  ask. We add this helper if it doesn't exist already.

### What the sample is *not*

- Not a fake scenario (no chat, no settings, no file manager). Just
  XAML hosts Reactor, side-by-side.
- Not a `ReactorApp` sample — `MainWindow.xaml` is a vanilla XAML
  window. Reactor is the *guest*.
- Not a port of an existing sample.

### Why this sample matters

Per the review, the existing samples (`WordPuzzle`, `ReactorFiles`)
demonstrate that Reactor *can* build apps. They don't demonstrate that
Reactor *integrates* with apps. The audience the reviewer is
representing — XAML/MVVM developers with existing apps — doesn't
identify with the all-Reactor samples and has no migration story
without an interop-first sample.

### Files / structure

```
samples/InteropFirst/
    InteropFirst.csproj
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    MainWindow.xaml.cs
    MainPage.xaml
    MainPage.xaml.cs
    MainPageViewModel.cs
    Models/
        Order.cs
    Components/
        OrdersDataGrid.cs
    Bridges/
        ICommandBridge.cs   (Reactor.Command FromICommand(ICommand))
```

The csproj uses the same `Microsoft.WindowsAppSDK` version as
`samples/ReactorHostControlDemo` and references the in-tree
`src/Reactor/Reactor.csproj`.

### Tests

The sample itself is the demonstration. We add an
`InteropFirst.UITests` smoke test that boots the app under WinAppDriver
and verifies:

- The XAML `ListView` and the Reactor `DataGrid` show the same row
  count.
- Invoking the XAML command bar Add button increases both row counts
  in lockstep.
- Invoking the Reactor toolbar overflow Add button does the same
  (proves the commanding bridge round-trips).

UI tests live behind the existing self-host test infrastructure if
present, otherwise they're a new project gated by a `RunUITests`
property like the existing test apps.

### Files touched

- New `samples/InteropFirst/` (full project as above).
- `Reactor.sln` — add the new csproj.
- README updates: top-level `README.md` "Samples" section gets an
  entry, and `docs/_pipeline/templates/getting-started.md.dt` gets a
  "Interop story" callout pointing here.

---

## §8 Rollout / sequencing

| § | Item | Risk | Touches public API | Suggested order |
|---|---|---|---|---|
| 5 | `Expr(...)` helper | Trivial | Additive | 1st (warm-up) |
| 3 | `ElementRef<T>` | Low | Additive | 2nd |
| 6 | Backdrop modifiers | Low | Additive | 3rd |
| 7 | Interop-first sample | Low | None (sample only) | 4th |
| 1 | Strongly-typed Grid tracks | Medium | Additive + `[Obsolete]` overload | 5th — coordinate with internal callers |
| 4 | Two component flavors (`Func` deprecation) | Medium | `[Obsolete]` + analyzer | 6th |
| 2 | `PersistedStateCache` lifecycle | Medium-high (behavioral default flip across releases) | Additive in N, default flip in N+1 | 7th — biggest blast radius, ship last |

Each item is independent. Most can be parallelized; the only sequencing
constraint is that §1 and §4 add analyzers, so we want one analyzer-PR
landing pattern established (likely via §1 first) before we layer the
others.

---

## §9 Open questions

1. **Source-generator for component unification** (the reviewer's #1
   wishlist). Out of scope per user direction, but we should track the
   spec 008 dependencies — specifically what C# language sugar
   (primary constructors on records, `field` keyword, source-generator
   incrementality) we'd want before this becomes pleasant. **Track as
   a follow-up in spec 008's task list.**

2. **`PersistedStateCache` default-scope flip.** The two-release
   migration in §2 buys a release of warning, but there's a behavior
   regression possible for users today relying on cross-window
   implicit persistence. Worth a forum post / changelog
   call-out before N+1.

3. **Reviewer items intentionally not addressed in this spec:**
   - **#1 DSL ergonomics.** Tracked in spec 008.
   - **#3 Implicit `string → TextElement`.** The reviewer would "kill
     it tomorrow." This is a separable design discussion — the
     ergonomic loss of `VStack("Hello", "World")` is real. Recommended
     follow-up but not in this spec.
   - **#4 Modifier allocation pressure.** Tracked separately in
     `docs/specs/007-perf-experiments.md`.
   - **#5 `XamlReader.Load`.** Tracked in
     `docs/specs/proposals/winui3-integration.md`.
   - **#6 Designer story.** Real gap; bigger than a spec response can
     address. Track as roadmap item.
   - **Wishlist #5 — VS designer extension on top of MCP devtools.**
     Tracked in spec 028 / 024.

4. **Custom-hooks pattern (§4 callout).** Raw methods that take a
   `RenderContext` and call hooks on it work today, but Reactor doesn't
   formally bless or guard the pattern. Decision range: **(a)** document
   and bless with a `Use*` naming convention enforced by an analyzer
   (`REACTOR_HOOKS_001`: "method taking RenderContext that calls hooks
   should be named `Use*`"); **(b)** introduce a marker attribute
   (`[ReactorHook]`) the analyzer keys off; **(c)** discourage the
   pattern entirely and require `Memo`/`Component<TProps>` for any
   hook-bearing logic. Recommend (a) — lowest friction, matches React's
   convention, lets the analyzer catch the "called outside a render
   path" mistake. Track as a follow-up spec.

5. **`Expr(...)` deprecation path when C# block expressions land.**
   When the language ships block expressions, `Expr(...)` becomes a
   shorthand for syntax that's natively expressible. We should
   `[Obsolete]` it at that point with an analyzer auto-fixer. Tracked
   in spec 008's "language asks" section.
