# Reactor Core State & Component Model — Implementation Tasks

Reference: [docs/specs/009-state-and-components-design.md](../009-state-and-components-design.md)

### Test classification

Tests are classified by the infrastructure they require:

| Level | Project | What it needs | Speed | When to use |
|---|---|---|---|---|
| **Unit** | `Reactor.Tests` (xUnit) | `RenderContext`, element records, pure logic. No reconciler, no WinUI control tree. | Fast | Hook behavior, cache logic, record equality, scope algorithms |
| **Self-host** | `Reactor.Tests` (xUnit) | Reconciler + WinUI controls instantiated in-process. No visible window, no event loop. | Medium | Component mount/update/unmount, context propagation through tree, memo skip verification |
| **E2E UIA** | `Reactor.AppTests` (MSTest + Appium) | Full app launched, WinAppDriver out-of-process automation. | **Slow** | Only for real UIA properties visible to screen readers, or user interaction flows |

**None of the features in this spec require E2E UIA tests.** Context, memo, hooks, and
persistence are internal framework plumbing invisible to accessibility tools. The
existing `Reactor.AppTests` suite should be run for regression only (Phase 5).

---

## Phase 1: Hooks Improvements (Local State B+ → A-)

Scope: `Reactor/Core/RenderContext.cs` only. No reconciler changes. Lowest risk.

### 1.1 Refactor HookState to generic type hierarchy
- [x] Create abstract base `HookState` class (no `Value` property)
- [x] Create `ValueHookState<T> : HookState` with typed `T Value` field
- [x] Create `MemoHookState<T> : HookState` with typed `T Value` field and `object[]? Dependencies`
- [x] Keep `EffectHookState : HookState` with existing fields (Dependencies, Effect, EffectWithCleanup, Cleanup, Pending)
- [x] Update `_hooks` list type from `List<HookState>` to `List<HookState>` (base type unchanged, contents change)
- [x] Remove `object Value` from old `HookState` — verify no direct references remain

### 1.2 Update UseState to use ValueHookState\<T\>
- [x] Change `UseState<T>` to create `ValueHookState<T>(initialValue)` instead of `new HookState { Value = initialValue! }`
- [x] Change value read from `(T)hook.Value` cast to `hook.Value` direct typed access
- [x] Change Setter closure to cast `_hooks[currentIndex]` to `ValueHookState<T>` and assign `h.Value = newValue` (no boxing)
- [x] Update hook type check: `_hooks[_hookIndex] as ValueHookState<T>` with clear error message on mismatch
- [x] Remove `UseStateSetterByIndex` or update it to use `ValueHookState<T>` (internal debug-only method)

### 1.3 Update UseReducer to use ValueHookState\<T\>
- [x] Update `UseReducer<T>(T initialValue)` — same pattern as UseState: `ValueHookState<T>`, typed access
- [x] Update `UseReducer<TState, TAction>(reducer, initialValue)` — same pattern
- [x] Verify Updater/Dispatch closures cast to `ValueHookState<T>` correctly

### 1.4 Update UseMemo to use MemoHookState\<T\>
- [x] Change `UseMemo<T>` to create `MemoHookState<T>` instead of generic `MemoHookState`
- [x] Change value read from `(T)hook.Value` to `hook.Value` direct typed access
- [x] Ensure `UseCallback` still works (it delegates to `UseMemo<Action>`)

### 1.5 Update UseRef (no boxing change needed, verify correctness)
- [x] UseRef stores `Ref<T>` which is already a reference type — verify it works with new `ValueHookState<Ref<T>>` or keep using base `HookState` wrapper
- [x] Decide: UseRef can use `ValueHookState<Ref<T>>` since the Ref itself is a class (no boxing of Ref)

### 1.6 Post-render effect cleanup
- [x] Add `PendingCleanup` field to `EffectHookState` (type `Action?`)
- [x] Update `UseEffect(Action, params object[])`: when deps change, set `hook.PendingCleanup = hook.Cleanup` instead of invoking `hook.Cleanup?.Invoke()` inline
- [x] Update `UseEffect(Func<Action>, params object[])`: same — queue cleanup instead of running inline
- [x] Update `FlushEffects()` Phase 1: iterate all hooks, run and clear `PendingCleanup` on EffectHookStates
- [x] Update `FlushEffects()` Phase 2: run pending effects (existing logic, after cleanups)
- [x] Verify `RunCleanups()` (unmount path) still runs `Cleanup` directly — unmount cleanup is immediate, not deferred

### 1.7 Phase 1 tests

All Phase 1 tests are **unit tests** (`Reactor.Tests`). They exercise `RenderContext`
directly — no reconciler, no WinUI controls, no UI thread. Instantiate a
`RenderContext`, call `BeginRender()`, invoke hooks, call `FlushEffects()`, assert
values and ordering.

**Hook type correctness (unit):**
- [x] Test: `UseState<int>` no longer boxes — create RenderContext, call UseState\<int\>, verify hook internals use `ValueHookState<int>` (reflection or InternalsVisibleTo accessor)
- [x] Test: `UseState<string>` works correctly with generic hook state
- [x] Test: `UseState<bool>` setter correctly compares and triggers re-render
- [x] Test: `UseReducer<int>` functional updater works with typed hook state
- [x] Test: `UseReducer<TState, TAction>` dispatch works with typed hook state
- [x] Test: `UseMemo<int>` returns cached value when deps unchanged
- [x] Test: `UseMemo<int>` recomputes when deps change
- [x] Test: `UseCallback` returns stable reference when deps unchanged
- [x] Test: `UseRef<int>` persists across renders
- [x] Test: Hook order violation throws descriptive error with new type names

**Effect cleanup ordering (unit):**
- [x] Test: Effect cleanup runs AFTER render commits, not during UseEffect call
  - Call BeginRender, call UseEffect with changed deps, verify cleanup was NOT invoked yet
  - Call FlushEffects, verify cleanup ran then new effect ran
- [x] Test: Effect cleanup runs BEFORE new effect in same FlushEffects pass
- [x] Test: Multiple effects with pending cleanups — all cleanups run before any new effects
- [x] Test: Unmount cleanup (RunCleanups) still runs immediately (not deferred)

**Regression:**
- [x] Verify all existing `Reactor.Tests` pass after refactor (no public API change)

---

## Phase 2: Context System (Global State F → A)

Depends on: Phase 1 (for ContextHookState type in generic hierarchy).

### 2.1 Context\<T\> type
- [x] Create `Reactor/Core/Context.cs`
- [x] Define `ContextBase` abstract class with `internal abstract object? DefaultValueBoxed { get; }`
- [x] Define `Context<T> : ContextBase` with:
  - `T DefaultValue { get; }` property
  - Constructor: `Context(T defaultValue, [CallerMemberName] string? name = null)`
  - `string? DebugName { get; }` for diagnostics
  - Override `DefaultValueBoxed => DefaultValue`

### 2.2 ContextValues on Element
- [x] Add `IReadOnlyDictionary<ContextBase, object?>? ContextValues { get; init; }` to `Element` base record
- [x] Update `ShallowEquals` in `Element.cs` to handle `ContextValues` comparison (dictionary content equality by reference on keys, Equals on values)
- [x] Verify record equality includes `ContextValues` (compiler-generated Equals will include it)

### 2.3 .Provide() modifier
- [x] Create `Reactor/Core/ContextExtensions.cs`
- [x] Implement `Provide<T, TValue>(this T element, Context<TValue> context, TValue value) where T : Element`
  - Merge into existing `ContextValues` dictionary if present, or create new
  - Return `element with { ContextValues = dict }`
- [x] Handle multiple `.Provide()` calls on same element (merge into single dictionary)
- [x] Handle same context provided twice on same element (last-write-wins)

### 2.4 ContextScope in Reconciler
- [x] Create `ContextScope` internal class in `Reconciler.cs` (or separate file)
  - `List<(ContextBase Context, object? Value)> _stack`
  - `Push(IReadOnlyDictionary<ContextBase, object?> values)` — add all entries
  - `Pop(int count)` — remove last N entries
  - `Read<T>(Context<T> context)` — walk backward for shadowing, return default if not found
  - `long Version` — incremented on push/pop for cheap change detection
- [x] Add `private readonly ContextScope _contextScope = new()` field to `Reconciler`
- [x] Wire push/pop into reconciler Mount path:
  - Before mounting children of any element with `ContextValues`, push onto scope
  - After mounting children, pop (use try/finally)
- [x] Wire push/pop into reconciler Update path:
  - Same pattern in `Reconcile()` / `Update*` methods for elements with `ContextValues`
- [x] Handle nested context providers (inner push shadows outer for same context)
- [x] Pass `ContextScope` reference to `RenderContext.BeginRender()` so hooks can read from it

### 2.5 UseContext\<T\> hook
- [x] Create `ContextHookState : HookState` with `ContextBase Context` and `object? LastValue` fields
- [x] Implement `UseContext<T>(Context<T> context)` on `RenderContext`:
  - On first render: create `ContextHookState`, store context reference
  - Read current value from `_reconcilerScope.Read(context)`
  - Store value in `hook.LastValue` for memo comparison (see Phase 3)
  - Return typed value
- [x] Store reference to `ContextScope` in RenderContext (set during `BeginRender`)
- [x] Add `_reconcilerScope` field to `RenderContext` (type: `ContextScope`, set by reconciler)
- [x] Update `BeginRender(Action requestRerender)` signature to `BeginRender(Action requestRerender, ContextScope scope)`
- [x] Update all `BeginRender` call sites in `Reconciler.cs`, `Reconciler.Mount.cs`, `Reconciler.Update.cs`

### 2.6 UseContext convenience method on Component
- [x] Add `protected T UseContext<T>(Context<T> context) => Context.UseContext(context)` to `Component` base class

### 2.7 Migrate LocaleContext to Context
- [x] Define `public static readonly Context<IntlAccessor?> IntlContexts.Locale` (in Localization namespace)
- [x] Update `LocaleProviderComponent` to use `.Provide(IntlContexts.Locale, accessor)` on child element
- [x] Update `UseIntl()` to use `UseContext(IntlContexts.Locale)` internally with fallback to OS default
- [x] Legacy `LocaleContext.cs` kept for backward compat, marked with deprecation comment
- [x] `UseIntl()` public API unchanged (non-breaking)
- [x] All localization tests updated and passing (7/7)

### 2.8 Phase 2 tests

Tests split across two levels. No E2E UIA tests needed — context is purely internal
state plumbing invisible to accessibility tools.

**Unit tests** (`Reactor.Tests`) — exercise ContextScope, Context, .Provide() modifier,
and UseContext hook in isolation. No reconciler, no WinUI controls.

- [x] Test: `ContextScope.Read()` returns default when stack is empty
- [x] Test: `ContextScope.Push()` then `Read()` returns pushed value
- [x] Test: `ContextScope` nested push — inner shadows outer for same context
- [x] Test: `ContextScope` nested push — different contexts both readable
- [x] Test: `ContextScope.Pop()` restores previous value
- [x] Test: `.Provide()` modifier sets ContextValues on element record
- [x] Test: Chained `.Provide().Provide()` merges into single dictionary
- [x] Test: Same context provided twice on same element — last-write-wins
- [x] Test: `UseContext` on RenderContext with mock ContextScope — returns scope value
- [x] Test: `UseContext` with no provider in scope — returns Context default value
- [x] Test: `UseContext` follows hook rules — calling in different order throws

**Self-host tests** (`Reactor.Tests`) — exercise full reconciler tree traversal with
context push/pop. Instantiate Reconciler, mount element trees with `.Provide()`,
render components that call `UseContext()`. Same pattern as existing
`ReconcilerCorrectnessTests` / `ComponentPropsTests`.

- [x] Test: Mount tree with `.Provide()` → child component `UseContext()` returns provided value
- [x] Test: No provider in ancestor chain → `UseContext()` returns default
- [x] Test: Nested providers for same context — inner component sees inner value, outer component sees outer
- [x] Test: Context scope cleanup — sibling subtree does NOT see context from adjacent subtree
- [x] Test: Context value change triggers consumer re-render (provider component changes state → consumer re-renders with new value)
- [x] Test: Deep nesting — context passes through 5+ intermediate components that don't consume it
- [x] Test: Two components sharing a context, both update when provider changes
- [x] Test: LocaleContext migration — UseIntl() works via Context internally (7 localization tests passing)

---

## Phase 3: Component Memoization (Component Model C → A-)

Depends on: Phase 2 (memo must detect context changes to bypass skip).

### 3.1 ShouldUpdate on Component base classes
- [x] Add `protected virtual bool ShouldUpdate() => false` to `Component` base class
  - Default: propless components never re-render due to parent (only self-triggered)
- [x] Add `protected virtual bool ShouldUpdate(TProps? oldProps, TProps? newProps) => !Equals(oldProps, newProps)` to `Component<TProps>`
  - Default: structural equality via record Equals
- [x] Document that record props get auto-comparison for free; class props need Equals override

### 3.2 Memo check in ReconcileComponent
- [x] Store previous props on `ComponentNode` (add `object? PreviousProps` field)
- [x] In `ReconcileComponent()`, before calling `Render()`, check:
  1. Is this a parent-triggered re-render (not self-triggered)?
  2. If `ComponentElement`: compare `oldComp.Props` vs `newComp.Props` via component's `ShouldUpdate()`
  3. If any consumed context changed (call `HasConsumedContextChanged`)
  4. If props unchanged AND no context changed → skip Render(), reuse `node.RenderedElement`
- [x] Implement `HasConsumedContextChanged(ComponentNode node)`:
  - Iterate `ContextHookState` entries in the component's RenderContext
  - For each, read current scope value and compare with `hook.LastValue`
  - Return true if any differ
- [x] Add internal method/property on `RenderContext` to expose context hooks for memo check (e.g., `IEnumerable<ContextHookState> ContextHooks`)
- [x] When skipping render: still update `node.Element = newEl` (modifiers on the ComponentElement may have changed)
- [x] When skipping render: still apply modifiers to the wrapper Border if they differ (the ComponentElement itself can have .Margin(), .Opacity(), etc.)

### 3.3 Self-triggered re-render bypass
- [x] Ensure self-triggered re-renders (from `_requestRerender` callback) do NOT go through memo check
- [x] Identify how self-triggered re-renders enter the reconciler — verify they call a different path or have a flag
- [x] If both paths go through `ReconcileComponent`, add a `bool force` parameter or equivalent to distinguish
- [x] Test: component's own UseState setter always triggers render even when props haven't changed

### 3.4 MemoElement for function components
- [x] Add `public record MemoElement(Func<RenderContext, Element> RenderFunc, object?[]? Dependencies = null) : Element` to `Element.cs`
- [x] Add DSL factory: `public static MemoElement Memo(Func<RenderContext, Element> render, params object?[] dependencies)` in `Dsl.cs` or equivalent
  - Empty deps array → `null` (render once + own state changes only)
  - Non-empty deps → store array for comparison
- [x] Add mount handler in `Reconciler.Mount.cs`:
  - Same as `MountFuncComponent` but stores deps on the ComponentNode
  - Create RenderContext, BeginRender, call RenderFunc, FlushEffects, wrap in Border
- [x] Add update handler in `Reconciler.Update.cs`:
  - Compare `oldMemo.Dependencies` vs `newMemo.Dependencies` via `DepsEqual`
  - Check consumed contexts via `HasConsumedContextChanged`
  - If deps equal and no context change → skip render
  - Otherwise → re-render with new RenderFunc
- [x] Register MemoElement in reconciler type dispatch (Mount switch + Update switch)
- [x] Handle `CanUpdate`: MemoElement can update to MemoElement (same type check)

### 3.5 Phase 3 tests

All memo tests are **self-host tests** (`Reactor.Tests`) — they require the reconciler
to mount components, trigger parent re-renders, and verify whether child components
actually re-rendered. Use a render-count tracking pattern: test components increment
a counter in Render() so the test can assert skip vs re-render. No E2E UIA tests
needed — memoization is invisible to the user.

**Props-based memo (self-host):**
- [x] Test: ComponentElement with null props — memo skips re-render on parent change
- [x] Test: ComponentElement with record props — memo skips when props structurally equal
- [x] Test: ComponentElement with changed props — memo allows re-render
- [x] Test: ComponentElement with class props (no Equals override) — re-renders every time (reference equality)

**ShouldUpdate override (self-host):**
- [x] Test: ShouldUpdate override returning true — always re-renders
- [x] Test: ShouldUpdate override with custom comparison — only re-renders when custom check says so

**Self-triggered bypass (self-host):**
- [x] Test: Self-triggered re-render (UseState setter) — always executes, bypasses memo

**Context + memo interaction (self-host):**
- [x] Test: Context change bypasses memo — props unchanged but consumed context changed → re-renders
- [x] Test: Context change on non-consumed context — does NOT trigger re-render

**MemoElement for function components (self-host):**
- [x] Test: MemoElement with deps — skips render when deps unchanged
- [x] Test: MemoElement with changed deps — re-renders
- [x] Test: MemoElement with no deps (null) — renders once, never re-renders from parent
- [x] Test: MemoElement self-triggered — UseState inside Memo component triggers re-render

**Slots + memo interaction (self-host):**
- [x] Test: Memo and slots interaction — static slot content (Text) allows memo skip
- [x] Test: Memo and slots interaction — slot with event handler defeats memo (delegate inequality)
- [x] Test: UseCallback stabilizes delegate — slot with UseCallback-wrapped handler allows memo skip

**Tree-level behavior (self-host):**
- [x] Test: Deeply nested memo — parent re-renders, memoized child skips, grandchild also skips (no cascading renders)
- [x] Test: Component unmount/remount after memo — state is fresh (memo doesn't cache state across unmount)

---

## Phase 4: State Persistence + Slots Documentation (Local State A- → A)

Depends on: Phase 1 (for generic hook hierarchy — PersistedHookState extends ValueHookState).

### 4.1 PersistedStateCache
- [x] Create `Reactor/Core/PersistedStateCache.cs` — internal static class
- [x] Implement `Dictionary<string, object?> _cache` (static, process-lifetime)
- [x] Implement `bool TryGet<T>(string key, out T value)`
- [x] Implement `void Set<T>(string key, T value)`
- [x] Implement `void Remove(string key)`
- [x] Implement `void Clear()` (for testing — reset all cached state)

### 4.2 PersistedHookState type hierarchy
- [x] Create `PersistedHookStateBase : HookState` abstract class with `string PersistKey` and `abstract void SaveToCache()`
- [x] Create `PersistedHookState<T> : PersistedHookStateBase` with `T Value` field
  - `SaveToCache()` → `PersistedStateCache.Set(PersistKey, Value)`
- [x] Verify type hierarchy: `HookState` ← `PersistedHookStateBase` ← `PersistedHookState<T>`

### 4.3 UsePersisted\<T\> hook
- [x] Implement `UsePersisted<T>(string key, T initialValue)` on `RenderContext`:
  - On first mount: check `PersistedStateCache.TryGet<T>(key, out var cached)` — use cached if found, else initialValue
  - Create `PersistedHookState<T>(initial) { PersistKey = key }`
  - Return `(T Value, Action<T> Set)` — same setter pattern as UseState
- [x] Add `UsePersisted<T>` convenience method on `Component` base class

### 4.4 Persist on unmount
- [x] Update `RunCleanups()` in `RenderContext`:
  - After running effect cleanups, iterate hooks for `PersistedHookStateBase` instances
  - Call `SaveToCache()` on each
- [x] Verify: only saves on unmount (not on every render)
- [x] Verify: effect cleanups still run before persisted state is saved

### 4.5 Phase 4 tests — state persistence

No E2E UIA tests needed — persisted state is invisible to accessibility tools.

**PersistedStateCache (unit — `Reactor.Tests`):** Pure dictionary logic, no WinUI.
- [x] Test: `TryGet` returns false when key not present
- [x] Test: `Set` then `TryGet` returns stored value
- [x] Test: `Clear()` removes all entries
- [x] Test: `Set` with same key overwrites previous value

**UsePersisted hook (unit — `Reactor.Tests`):** Exercise RenderContext directly.
- [x] Test: UsePersisted returns initial value on first mount (no cached value)
- [x] Test: UsePersisted setter updates value and triggers re-render callback
- [x] Test: Value type (int) persisted without boxing issues (uses PersistedHookState\<int\>)
- [x] Test: Multiple UsePersisted hooks in same RenderContext — each keyed independently

**UsePersisted lifecycle (self-host — `Reactor.Tests`):** Requires reconciler to mount,
unmount, and remount components.
- [x] Test: After unmount + remount, UsePersisted returns cached value (not initial)
- [x] Test: UsePersisted with same key in different components — shares cached state
- [x] Test: UsePersisted with different keys — independent state
- [x] Test: PersistedStateCache.Clear() resets all — subsequent mount gets initial value
- [x] Test: UsePersisted coexists with UseState — UseState lost on unmount, UsePersisted preserved

### 4.6 Slots pattern documentation
- [ ] Create or update a developer guide / README section documenting the slots pattern:
  - Single default slot: `params Element?[]` children
  - Named slots: `Element`-typed props on record
  - Optional slots: `Element?` with `= null` default
  - Default content: handle in Render() with `??` fallback
  - Multiple children in a slot: wrap in layout element at call site
  - Function component slots: closure capture pattern
- [ ] Document naming conventions: Title, Body, Actions, Header, Footer, Leading, Trailing, Icon, Label, Content
- [ ] Document memo interaction:
  - Static slot content (Text, Image) → memo works automatically
  - Slots with event handlers → use UseCallback/UseMemo to stabilize
  - Include code examples for both cases
- [ ] Document `Lazy<Element>` as future work for deferred slot evaluation (TabView use case)

### 4.7 Sample app updates
- [x] Update D3 Gallery or create new sample page demonstrating Context (provide + consume)
- [x] Update or create sample demonstrating memoized components (before/after render count)
- [x] Update or create sample demonstrating UsePersisted (tab switch preserving scroll position or form state)
- [x] Update or create sample demonstrating slots pattern (Dialog or Card with named content areas)
- [x] Verify all existing sample apps still compile and run after framework changes (2354 passed, 0 failed)

---

## Phase 5: Validation & Critical Review Update

### 5.1 Full test suite
- [x] Run all `Reactor.Tests` (unit + self-host) — verify zero regressions (2403 passed, 0 failed)
- [ ] Run all `Reactor.AppTests` (E2E/Appium) — verify zero regressions from internal changes (no new E2E tests needed for these features)
- [ ] Run stress/perf tests if applicable — verify no performance regression from hook refactor

### 5.2 Update critical review scorecard
- [ ] Update `docs/critical-review.md` §2 (Component Model): document memoization, note remaining gaps (FuncElement auto-memo, typed props at element level)
- [ ] Update `docs/critical-review.md` §3 (State Management): document boxing fix, post-render cleanup, UsePersisted, UseContext
- [ ] Update `docs/critical-review.md` §14 scorecard table:
  - Global State: F → A
  - Component Model: C → A-
  - Local State: B+ → A
- [ ] Update Executive Summary to reflect new capabilities
- [ ] Update Conclusion "What prevents Microsoft.UI.Reactor (Reactor) from being production-ready" — remove global state, update component model language

### 5.3 Update spec status
- [x] Change spec status from "Proposal — pending review" to "Implemented" with date
