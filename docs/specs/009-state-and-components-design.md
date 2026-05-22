# Reactor Core State & Component Model — Detailed Design

## Status

**Implemented** — 2026-04-07. Phases 1–4 complete. Context system, memoization, state persistence all landed with full test coverage.

---

## Problem Statement

The [critical review](../critical-review.md) §2–§3 and scorecard identify three core
framework areas that block an "A" grade:

| Area | Current Grade | Target | Primary Gaps |
|---|---|---|---|
| **Global State** | F | A | No Context / EnvironmentObject / CompositionLocal equivalent |
| **Component Model** | C | A | No memoization, no slots convention, no global state |
| **Local State** | B+ | A | Boxing in HookState, synchronous effect cleanup, no state persistence |

These are the foundation of any declarative UI framework. React, SwiftUI, and Compose
all score A across the board here. Closing these gaps is prerequisite to Microsoft.UI.Reactor (Reactor) being
taken seriously as a production framework.

---

## Goals

1. **General-purpose context system** — tree-scoped ambient state that any component can
   provide and any descendant can consume, with automatic re-render on change
2. **Default-on component memoization** — skip re-renders when props haven't changed,
   with no developer opt-in required for the common case
3. **Eliminate boxing in hooks** — generic hook state storage so `UseState<int>` doesn't
   box on every render
4. **Post-render effect cleanup** — move cleanup from the render phase to post-render,
   matching React's behavior and avoiding render-blocking side effects
5. **State persistence across unmount/remount** — in-memory state cache keyed by
   developer-provided keys, surviving component lifecycle
6. **Document the slots pattern** — establish a convention for named children using
   Element-typed props, with `Lazy<Element>` noted as future work

### Non-goals

- Disk-based state persistence (e.g., SwiftUI's `@SceneStorage`) — future work
- Automatic memoization for function components (requires compiler support) — future work
- Redux-style global store — Context covers the use cases; external stores can layer on top
- Navigation system — separate spec

---

## 1. Context System (Global State)

### The gap

Every real app needs to share state across the component tree without threading it through
every intermediate component's props: theme preference, user session, feature flags, router
state, localization. Reactor has zero mechanism for this. `LocaleContext` proves the pattern
works as a hand-rolled one-off; this section generalizes it.

### How the competition does it

| Framework | Mechanism | Define | Provide | Consume |
|---|---|---|---|---|
| **React** | Context | `createContext(default)` | `<Ctx.Provider value={v}>` | `useContext(Ctx)` |
| **SwiftUI** | Environment | `EnvironmentKey` protocol | `.environment(\.key, v)` | `@Environment(\.key)` |
| **Compose** | CompositionLocal | `compositionLocalOf { default }` | `CompositionLocalProvider(L provides v)` | `L.current` |

All three are tree-scoped (inner providers shadow outer), type-safe, and trigger re-renders
only for consumers of the changed context.

### Design: Context\<T\> + .Provide() modifier + UseContext() hook

Reactor follows React's mental model, so the API mirrors React Context — but the provide
mechanism uses a modifier (SwiftUI-style) to match Reactor's fluent API conventions.

#### Defining a context

```csharp
// A context is a static, typed, named container with a default value.
// The default is used when no provider exists in the ancestor chain.
public static readonly Context<ThemeConfig> ThemeContext =
    new(defaultValue: ThemeConfig.Light);

public static readonly Context<UserSession?> SessionContext =
    new(defaultValue: null);

// Contexts are typically defined as static fields on a static class:
public static class AppContexts
{
    public static readonly Context<ThemeConfig> Theme = new(ThemeConfig.Light);
    public static readonly Context<UserSession?> Session = new(defaultValue: null);
    public static readonly Context<FeatureFlags> Features = new(FeatureFlags.Default);
}
```

#### Providing a value to a subtree

```csharp
// .Provide() is a modifier on any element — it scopes the value to that element's subtree
VStack(
    Component<Header>(),
    Component<Sidebar>(),
    Component<MainContent>()
).Provide(AppContexts.Theme, darkTheme)
 .Provide(AppContexts.Session, currentUser)

// Nesting: inner .Provide() shadows outer for the same context
VStack(
    // Everything here sees darkTheme...
    Component<MainContent>(),

    // ...except this subtree, which sees highContrastTheme
    VStack(
        Component<AccessibleWidget>()
    ).Provide(AppContexts.Theme, highContrastTheme)
).Provide(AppContexts.Theme, darkTheme)
```

Multiple `.Provide()` calls on the same element are supported — each scopes a different
context to that subtree. Providing the same context twice on the same element is a
last-write-wins merge (not an error).

#### Consuming a context value

```csharp
// In a class component:
class UserGreeting : Component
{
    public override Element Render()
    {
        var session = UseContext(AppContexts.Session);
        var theme = UseContext(AppContexts.Theme);

        return Text(session is not null ? $"Hello, {session.Name}" : "Sign in")
            .Foreground(theme.PrimaryTextColor);
    }
}

// In a function component:
Func(ctx =>
{
    var theme = ctx.UseContext(AppContexts.Theme);
    return Border(children).Background(theme.SurfaceColor);
})
```

`UseContext<T>` is a hook — it follows hook rules (same call order every render). It:
1. Reads the nearest ancestor's provided value for this context
2. Returns the context's `DefaultValue` if no provider exists
3. Subscribes the component to re-render when the provided value changes

#### Providing dynamic values (state-driven contexts)

The typical pattern is a component that owns state and provides it:

```csharp
class ThemeProvider : Component
{
    public override Element Render()
    {
        var (theme, setTheme) = UseState(ThemeConfig.Light);

        // Provide both the current theme and the setter so descendants can change it
        var ctx = new ThemeContext(theme, setTheme);

        return VStack(
            Component<App>()
        ).Provide(AppContexts.ThemeFull, ctx);
    }
}

// Descendant toggles the theme:
class ThemeToggle : Component
{
    public override Element Render()
    {
        var themeCtx = UseContext(AppContexts.ThemeFull);
        return Button("Toggle", () =>
            themeCtx.SetTheme(themeCtx.Current == ThemeConfig.Light
                ? ThemeConfig.Dark : ThemeConfig.Light));
    }
}
```

When `setTheme` fires, `ThemeProvider` re-renders, the `.Provide()` value changes, and
all `UseContext(AppContexts.ThemeFull)` consumers re-render with the new value.

### Implementation approach

#### Context\<T\> class

```csharp
namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// A typed, named context that can be provided to a subtree and consumed by any descendant.
/// Define as a static field. Provide via .Provide() modifier. Consume via UseContext() hook.
/// </summary>
public sealed class Context<T>
{
    public T DefaultValue { get; }
    internal string? DebugName { get; }

    public Context(T defaultValue, [CallerMemberName] string? name = null)
    {
        DefaultValue = defaultValue;
        DebugName = name;
    }
}

/// <summary>
/// Non-generic base for type-erased storage in the context scope stack.
/// </summary>
public abstract class ContextBase
{
    internal abstract object? DefaultValueBoxed { get; }
}
```

#### Element storage: ContextValues property

```csharp
public abstract record Element
{
    // ... existing properties (Key, Modifiers, Attached, etc.) ...

    /// <summary>
    /// Context values provided to this element's subtree via .Provide().
    /// The reconciler pushes these onto the context scope when entering
    /// this element's subtree and pops them when leaving.
    /// </summary>
    public IReadOnlyDictionary<ContextBase, object?>? ContextValues { get; init; }
}
```

#### .Provide() modifier

```csharp
public static class ContextExtensions
{
    public static T Provide<T, TValue>(this T element, Context<TValue> context, TValue value)
        where T : Element
    {
        var existing = element.ContextValues;
        var dict = existing is not null
            ? new Dictionary<ContextBase, object?>(existing) { [context] = value }
            : new Dictionary<ContextBase, object?> { [context] = value };
        return element with { ContextValues = dict };
    }
}
```

#### Reconciler: context scope stack

The reconciler maintains a scope stack that tracks the current context values during
tree traversal. This is the same pattern as `LocaleContext.Current` but generalized
and tree-scoped rather than thread-static.

```csharp
// Inside Reconciler — maintains current context scope during tree walk
private readonly ContextScope _contextScope = new();

internal sealed class ContextScope
{
    // Stack of (context, value) pairs — pushed on entering an element with ContextValues,
    // popped on leaving. Most recent entry for a given context wins (shadowing).
    private readonly List<(ContextBase Context, object? Value)> _stack = new();
    // Snapshot version — incremented on every push/pop, used by Memo to detect changes
    private long _version;

    internal void Push(IReadOnlyDictionary<ContextBase, object?> values)
    {
        foreach (var (ctx, val) in values)
            _stack.Add((ctx, val));
        _version++;
    }

    internal void Pop(int count)
    {
        _stack.RemoveRange(_stack.Count - count, count);
        _version++;
    }

    internal T Read<T>(Context<T> context)
    {
        // Walk backward (most recent first) for shadowing
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_stack[i].Context, context))
                return (T)_stack[i].Value!;
        }
        return context.DefaultValue;
    }

    internal long Version => _version;
}
```

During reconciliation, the reconciler wraps subtree traversal:

```csharp
// Pseudocode — in Mount/Update when processing any element with ContextValues
if (element.ContextValues is { Count: > 0 } ctxValues)
{
    _contextScope.Push(ctxValues);
    try { /* mount/reconcile children */ }
    finally { _contextScope.Pop(ctxValues.Count); }
}
```

#### UseContext\<T\> hook

```csharp
// In RenderContext:
public T UseContext<T>(Context<T> context)
{
    // Track which contexts this component reads (for Memo interaction — see §2)
    if (_hookIndex >= _hooks.Count)
    {
        _hooks.Add(new ContextHookState { Context = context });
    }

    var hook = _hooks[_hookIndex] as ContextHookState
        ?? throw new InvalidOperationException("Hook order violation: expected ContextHookState");
    _hookIndex++;

    // Read from the reconciler's current context scope
    var value = _reconcilerScope!.Read(context);
    hook.LastValue = value;
    return value;
}

private class ContextHookState : HookState
{
    public ContextBase Context = default!;
    public object? LastValue;
}
```

The reconciler passes its `ContextScope` reference to `RenderContext.BeginRender()` so
the hook can read from it.

#### Re-render on context change

When a provider element re-renders with a new value (because the providing component's
state changed), the reconciler:

1. Pushes the new context values onto the scope
2. Reconciles the subtree as normal
3. Components in the subtree that call `UseContext()` read the new value
4. Memo (§2) detects that a consumed context value has changed and allows the re-render

No explicit subscriber list is needed — the reconciler's normal top-down re-render
propagation handles it. The key interaction is with Memo: **context changes must bypass
memoization** (see §2).

### Migration: LocaleContext → Context

Once the context system ships, `LocaleContext` can be reimplemented on top of it:

```csharp
// Before (hand-rolled thread-static):
internal static LocaleContext? Current { get; set; }
// UseIntl() reads LocaleContext.Current and subscribes manually

// After (general-purpose context):
public static readonly Context<IntlAccessor> LocaleContext =
    new(defaultValue: IntlAccessor.Default);
// UseIntl() becomes: return UseContext(LocaleContext);
// LocaleProvider becomes: children.Provide(LocaleContext, accessor)
```

This is a non-breaking internal refactor — the public `UseIntl()` API is unchanged.

---

## 2. Component Memoization

### The gap

Every state change in a parent re-renders all descendants, regardless of whether their
inputs changed. React has `React.memo()`, SwiftUI has automatic view diffing, Compose has
automatic skipping via stable parameters. Reactor has nothing — a deeply nested component
tree re-renders entirely on every ancestor state change.

### Design: default-on for class components, explicit Memo() for function components

#### Class components (Component\<TProps\>): automatic

When the reconciler is about to re-render a `ComponentElement`, it compares the new props
to the old props. If equal, the render is skipped entirely.

```csharp
// In Reconciler.ReconcileComponent():
if (newEl is ComponentElement newComp && oldEl is ComponentElement oldComp)
{
    // Check if props changed
    bool propsEqual = Equals(oldComp.Props, newComp.Props);

    // Check if any consumed context changed
    bool contextChanged = HasConsumedContextChanged(node);

    if (propsEqual && !contextChanged)
    {
        // Skip re-render — reuse previous element tree
        node.Element = newEl;  // update element reference (modifiers may differ)
        return;
    }
}
```

**Why this works well for Reactor:**
- Props are typically C# records, which have compiler-generated structural `Equals()`
- Records with value-type fields (string, int, bool, enum) compare correctly out of the box
- No developer opt-in needed — it's the default behavior

**Props comparison rules:**
- `null == null` → skip (component with no props, parent re-rendered but this component unchanged)
- Record props → structural equality via `Equals()` (compiler-generated)
- Class props that override `Equals()` → custom equality
- Class props without `Equals()` override → reference equality (conservative: re-renders if new instance)

**Context interaction:**
The reconciler checks whether any `Context` consumed by this component (via
`UseContext()` hooks) has a different value in the current scope than it did last render.
If any consumed context changed, the memo is bypassed and the component re-renders.

```csharp
private bool HasConsumedContextChanged(ComponentNode node)
{
    var ctx = node.Component?.Context ?? node.Context;
    if (ctx is null) return false;

    foreach (var hook in ctx.ContextHooks)
    {
        var currentValue = _contextScope.Read(hook.Context);
        if (!Equals(currentValue, hook.LastValue))
            return true;
    }
    return false;
}
```

#### Opting out: ShouldUpdate override

For components that need to re-render on every parent change (e.g., animation-driven
components, components that read ambient mutable state):

```csharp
class AnimatedComponent : Component<AnimationProps>
{
    // Return true to always re-render, bypassing memo
    protected virtual bool ShouldUpdate(AnimationProps? oldProps, AnimationProps? newProps)
        => true;  // opt out of memo
}
```

The default `ShouldUpdate` implementation uses `Equals()`:

```csharp
// In Component<TProps> base class:
protected virtual bool ShouldUpdate(TProps? oldProps, TProps? newProps)
    => !Equals(oldProps, newProps);
```

For non-generic `Component` (no props), the default always returns `false` (never
re-renders due to parent change — only re-renders from own state changes):

```csharp
// In Component base class:
protected virtual bool ShouldUpdate() => false;
```

#### Function components: explicit Memo()

Function components (`Func(ctx => ...)`) use lambdas that are new closures every render.
Lambda identity always changes, so automatic memoization isn't possible without a
compiler plugin. Instead, offer an explicit `Memo()` wrapper with dependencies:

```csharp
// Memo with dependency array — re-renders only when deps change
Memo(ctx =>
{
    return Text($"Count: {count}").FontSize(24);
}, count)  // only re-renders when count changes

// Memo with no deps — renders once, never re-renders from parent
Memo(ctx =>
{
    var (localState, setLocalState) = ctx.UseState(0);
    return Text($"Local: {localState}");
})  // no deps = render once + own state changes only
```

**Implementation:**

```csharp
public record MemoElement(
    Func<RenderContext, Element> RenderFunc,
    object?[]? Dependencies = null
) : Element;

// DSL factory:
public static MemoElement Memo(
    Func<RenderContext, Element> render,
    params object?[] dependencies)
    => new MemoElement(render, dependencies.Length > 0 ? dependencies : null);
```

The reconciler treats `MemoElement` like `FuncElement` but adds a dependency check:

```csharp
// In reconciler update path for MemoElement:
if (oldEl is MemoElement oldMemo && newEl is MemoElement newMemo)
{
    bool depsEqual = oldMemo.Dependencies is not null
        && newMemo.Dependencies is not null
        && DepsEqual(oldMemo.Dependencies, newMemo.Dependencies);

    bool contextChanged = HasConsumedContextChanged(node);

    if (depsEqual && !contextChanged)
    {
        node.Element = newEl;
        return;  // skip re-render
    }
}
```

#### Self-triggered re-renders always execute

Memoization only gates **parent-triggered** re-renders (where the parent re-rendered
and included this component in its output). When a component's own `UseState` setter
fires, it always re-renders — this is a self-triggered update that memo does not block.

The reconciler distinguishes these two cases:
- **Parent-triggered:** reconciler calls `ReconcileComponent(oldEl, newEl, ...)` — memo check applies
- **Self-triggered:** `_requestRerender` callback fires → component re-renders with its own latest element — no memo check

### Memo and children / slots: what gets compared, what doesn't

Understanding how memoization interacts with children and slots is critical to using
it effectively. The rules are simple but the consequences are subtle.

#### Container children are NOT props — memo doesn't compare them

Most components appear as children of container elements (VStack, HStack, Grid).
Container children are part of the container's element record, not the component's
props. The reconciler diffs the container's children list and then reconciles each
child individually. The memo check only looks at the ComponentElement's `Props`:

```csharp
// Parent re-renders due to state change
VStack(
    Component<Header>(),               // Props: null == null → SKIP ✓
    Component<Sidebar>(new(Width: 300)), // Record equality → SKIP if unchanged ✓
    Text($"Count: {count}")            // TextElement — not a component, no memo
)
```

This is the most common case, and memo works automatically with zero effort.

#### Static slot content — memo works

When props contain Element-typed fields (slots) with no event handlers, record
structural equality compares them correctly:

```csharp
Component<Card>(new CardProps(
    Header: Text("Title").Bold(),       // TextElement("Title") == TextElement("Title") ✓
    Body: VStack(
        Text("Line 1"),                 // structural equality on children ✓
        Text("Line 2")
    )
))
// Both renders produce structurally identical records → SKIP ✓
```

#### Slots with event handlers — memo is defeated

Delegates use reference equality. A new closure is a new instance every render,
even if the function body and captures are identical:

```csharp
Component<Dialog>(new DialogProps(
    Title: Text("Confirm"),
    Footer: Button("OK", () => DoSomething())  // ← new Action instance every render
))
// DialogProps.Equals() → ButtonElement.Equals() → OnClick equality → FALSE
// Memo sees "changed" → re-renders every time
```

**This cannot be "fixed" by ignoring delegates in comparison.** If the delegate
captures component state, skipping the re-render would leave stale closures:

```csharp
var (count, setCount) = UseState(0);

// If memo skipped this re-render, the Button would keep printing 0 forever
Component<Dialog>(new DialogProps(
    Footer: Button("Show count", () => Console.WriteLine(count))  // captures count
))
```

This is the exact same limitation React.memo has with inline callbacks in JSX.

#### Mitigations: UseCallback and UseMemo

**Stabilize callbacks with UseCallback:**

```csharp
// UseCallback returns the same Action instance across renders (when deps unchanged)
var onConfirm = UseCallback(() => DoSomething(), /* empty deps = stable forever */);

Component<Dialog>(new DialogProps(
    Footer: Button("OK", onConfirm)  // same instance → memo works ✓
))
```

**Stabilize entire slot subtrees with UseMemo:**

```csharp
var footer = UseMemo(() =>
    HStack(
        Button("Cancel", onCancel),
        Button("OK", onConfirm)
    ),
    onCancel, onConfirm  // recreate only when callbacks change
);

Component<Dialog>(new DialogProps(Footer: footer))  // stable reference → memo works ✓
```

**When capturing state, include it in UseCallback deps:**

```csharp
var (count, setCount) = UseState(0);

// Callback updates when count changes — no stale closure
var showCount = UseCallback(() => Console.WriteLine(count), count);

Component<Dialog>(new DialogProps(
    Footer: Button("Show count", showCount)
))
// Memo skips when count hasn't changed, re-renders when it has ✓
```

#### Summary: when does memo help?

| Scenario | Memo skips re-render? | Developer action needed |
|---|---|---|
| Component with no props | **Yes** | None |
| Component with value/record props | **Yes** | Use record props |
| Slots with static content (Text, Image, layout) | **Yes** | None |
| Slots with event handlers (Button, Input) | **No** | UseCallback / UseMemo to stabilize |
| Function components (`Func(ctx => ...)`) | **No** | Use `Memo(ctx => ..., deps)` wrapper |
| Container children (VStack, HStack, Grid) | N/A | Children aren't props — reconciler diffs directly |

The common case — components as children of containers with simple or no props —
benefits automatically. Interactive slot content requires explicit stabilization
via UseCallback/UseMemo, matching React's established pattern. A future source
generator could auto-stabilize closures (similar to React Compiler), but that is
out of scope for this spec.

### Expected impact

For a typical app with a deeply nested component tree where a root-level state change
(e.g., theme toggle) causes a full re-render cascade:

- **Before:** Every component in the tree re-renders, even if its inputs are unchanged
- **After:** Only components whose props or consumed contexts actually changed re-render
- **Class components:** Automatic, zero developer effort (assuming record props)
- **Function components:** Opt-in via `Memo()` with dependency array
- **Slot-heavy components:** Automatic for static content; UseCallback/UseMemo for interactive slots

---

## 3. Hooks Improvements (Local State)

### 3a. Eliminate boxing in hook state

#### The problem

```csharp
// Current implementation:
private class HookState
{
    public object Value = default!;  // ← boxes int, bool, double on every read/write
}

// Every UseState<int> does:
_hooks.Add(new HookState { Value = initialValue! });  // box
T current = (T)hook.Value;                             // unbox
h.Value = newValue!;                                   // box
```

For a component with 5 integer state hooks rendering 60 times per second, that's
300 box/unbox allocations per second from state alone — plus the `object[]` dependency
arrays in UseEffect/UseMemo.

#### The fix: generic HookState\<T\>

```csharp
// Base class for heterogeneous list storage:
private abstract class HookState { }

// Generic subclass — value stored in T, no boxing:
private sealed class ValueHookState<T> : HookState
{
    public T Value;
    public ValueHookState(T initial) => Value = initial;
}

// Effect and Memo retain their own subclasses:
private sealed class EffectHookState : HookState
{
    public object[]? Dependencies;
    public Action? Effect;
    public Func<Action>? EffectWithCleanup;
    public Action? Cleanup;
    public bool Pending;
}

private sealed class MemoHookState<T> : HookState
{
    public T Value = default!;
    public object[]? Dependencies;
}

private sealed class ContextHookState : HookState
{
    public ContextBase Context = default!;
    public object? LastValue;  // boxed for comparison, but only on context read
}
```

Updated UseState:

```csharp
public (T Value, Action<T> Set) UseState<T>(T initialValue)
{
    if (_hookIndex >= _hooks.Count)
    {
        _hooks.Add(new ValueHookState<T>(initialValue));
    }

    var hook = _hooks[_hookIndex] as ValueHookState<T>
        ?? throw new InvalidOperationException(
            $"Hook at index {_hookIndex} expected ValueHookState<{typeof(T).Name}>. " +
            "Hooks must be called in the same order every render.");

    var currentIndex = _hookIndex;
    _hookIndex++;

    T current = hook.Value;

    void Setter(T newValue)
    {
        var h = (ValueHookState<T>)_hooks[currentIndex];
        if (!EqualityComparer<T>.Default.Equals(h.Value, newValue))
        {
            h.Value = newValue;
            _requestRerender?.Invoke();
        }
    }

    return (current, Setter);
}
```

**Breaking changes:** None to the public API. Internal `HookState` class hierarchy
changes — no external consumers.

**Cost:** Marginally more memory per hook (each `ValueHookState<T>` is a separate
generic instantiation). But the GC pressure reduction from eliminating boxing far
outweighs this.

#### Dependency array boxing

The `object[]` dependency arrays in UseEffect/UseMemo still box value types. A full
fix would require a generic dependency comparison mechanism, which is significantly
more complex. Two pragmatic options:

**Option A (recommended): Accept boxing in dependency arrays.** Dependencies are
compared once per render per hook — the cost is low. The hot path (UseState value
storage) is where boxing elimination matters most.

**Option B (future): ReadOnlySpan-based comparison.** Requires significant API
changes and is incompatible with `params object[]`. Defer to a future C# language
improvement (e.g., params spans).

### 3b. Post-render effect cleanup

#### The problem

```csharp
// Current — cleanup runs DURING the render phase (inside UseEffect call):
if (hook.Dependencies is null || !DepsEqual(hook.Dependencies, dependencies))
{
    hook.Cleanup?.Invoke();  // ← blocks render if cleanup is expensive
    hook.Dependencies = dependencies.ToArray();
    hook.Effect = effect;
    hook.Pending = true;
}
```

If an effect's cleanup involves network cancellation, timer disposal, or other I/O,
it blocks the render. React runs cleanups asynchronously after the render commits.

#### The fix: queue cleanup, run in FlushEffects

```csharp
// Updated UseEffect — only marks cleanup as pending, doesn't run it:
public void UseEffect(Action effect, params object[] dependencies)
{
    if (_hookIndex >= _hooks.Count)
    {
        _hooks.Add(new EffectHookState { Dependencies = null, Effect = effect });
    }

    var hook = _hooks[_hookIndex] as EffectHookState
        ?? throw new InvalidOperationException(/* ... */);
    _hookIndex++;

    if (hook.Dependencies is null || !DepsEqual(hook.Dependencies, dependencies))
    {
        hook.PendingCleanup = hook.Cleanup;  // queue old cleanup
        hook.Cleanup = null;
        hook.Dependencies = dependencies.ToArray();
        hook.Effect = effect;
        hook.Pending = true;
    }
}

// Updated FlushEffects — runs queued cleanups BEFORE new effects:
internal void FlushEffects()
{
    // Phase 1: Run all pending cleanups
    for (int i = 0; i < _hooks.Count; i++)
    {
        if (_hooks[i] is EffectHookState hook && hook.PendingCleanup is not null)
        {
            try { hook.PendingCleanup(); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Reactor] Cleanup at index {i} threw: {ex}");
            }
            hook.PendingCleanup = null;
        }
    }

    // Phase 2: Run all pending effects
    for (int i = 0; i < _hooks.Count; i++)
    {
        if (_hooks[i] is not EffectHookState hook || !hook.Pending) continue;
        hook.Pending = false;
        try
        {
            if (hook.EffectWithCleanup is not null)
            {
                hook.Cleanup = hook.EffectWithCleanup();
                hook.EffectWithCleanup = null;
            }
            else if (hook.Effect is not null)
            {
                hook.Effect();
                hook.Effect = null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] Effect at index {i} threw: {ex}");
        }
    }
}
```

**Execution order after this change:**

```
1. Component.Render()          — builds element tree (pure, no side effects)
2. Reconciler commits          — applies changes to WinUI controls
3. FlushEffects Phase 1        — runs queued cleanups from previous render
4. FlushEffects Phase 2        — runs new effects
```

This matches React's model: render → commit → cleanup old effects → run new effects.

**Breaking change risk:** Low. Effects that depend on cleanup completing before the
new element tree is built would break — but that pattern is already incorrect (effects
should not influence the render output). The existing behavior of cleanup-during-render
is a latent bug source that this change eliminates.

### 3c. State persistence across unmount/remount

#### The problem

When a component unmounts (removed from the tree) and later remounts (added back),
all hook state is lost. SwiftUI has `@SceneStorage`, Compose has `rememberSaveable`.
For desktop apps where users switch between views and expect state to be preserved
(scroll position, form input, collapsed sections), this is a real gap.

#### Design: UsePersisted\<T\> hook

```csharp
// In a class component:
class SettingsPanel : Component
{
    public override Element Render()
    {
        // State survives unmount/remount (keyed by "settings-scroll-pos")
        var (scrollPos, setScrollPos) = UsePersisted("settings-scroll-pos", 0.0);

        // Regular state — lost on unmount
        var (filter, setFilter) = UseState("");

        return ScrollViewer(
            VStack(/* settings items */)
        );
    }
}
```

`UsePersisted<T>(key, initialValue)` behaves exactly like `UseState<T>` but:
1. On **first mount**: checks a framework-level cache for an existing value under `key`
2. On **unmount**: stores the current value in the cache
3. On **remount**: restores the cached value instead of using `initialValue`

#### Implementation

```csharp
// Framework-level in-memory cache:
internal static class PersistedStateCache
{
    private static readonly Dictionary<string, object?> _cache = new();

    internal static bool TryGet<T>(string key, out T value)
    {
        if (_cache.TryGetValue(key, out var boxed))
        {
            value = (T)boxed!;
            return true;
        }
        value = default!;
        return false;
    }

    internal static void Set<T>(string key, T value)
        => _cache[key] = value;

    internal static void Remove(string key)
        => _cache.Remove(key);
}
```

```csharp
// In RenderContext:
public (T Value, Action<T> Set) UsePersisted<T>(string key, T initialValue)
{
    // On first mount, check cache for existing value
    if (_hookIndex >= _hooks.Count)
    {
        T initial = PersistedStateCache.TryGet<T>(key, out var cached)
            ? cached
            : initialValue;
        _hooks.Add(new PersistedHookState<T>(initial) { PersistKey = key });
    }

    var hook = _hooks[_hookIndex] as PersistedHookState<T>
        ?? throw new InvalidOperationException(/* ... */);

    var currentIndex = _hookIndex;
    _hookIndex++;

    T current = hook.Value;

    void Setter(T newValue)
    {
        var h = (PersistedHookState<T>)_hooks[currentIndex];
        if (!EqualityComparer<T>.Default.Equals(h.Value, newValue))
        {
            h.Value = newValue;
            _requestRerender?.Invoke();
        }
    }

    return (current, Setter);
}

private sealed class PersistedHookState<T> : ValueHookState<T>
{
    public string PersistKey = default!;
    public PersistedHookState(T initial) : base(initial) { }
}
```

On unmount (`RunCleanups`), persisted hooks save their current value:

```csharp
internal void RunCleanups()
{
    for (int i = 0; i < _hooks.Count; i++)
    {
        switch (_hooks[i])
        {
            case EffectHookState effect:
                try { effect.Cleanup?.Invoke(); }
                catch (Exception ex) { /* log */ }
                break;

            // Save persisted state on unmount
            case PersistedHookState<var T> persisted:  // conceptual — actual impl uses reflection or typed dispatch
                PersistedStateCache.Set(persisted.PersistKey, persisted.Value);
                break;
        }
    }
}
```

> **Implementation note:** Since `PersistedHookState<T>` is generic, the `RunCleanups`
> method needs a non-generic base with a `SaveToCache()` method to avoid reflection:
>
> ```csharp
> private abstract class PersistedHookStateBase : HookState
> {
>     public string PersistKey = default!;
>     internal abstract void SaveToCache();
> }
>
> private sealed class PersistedHookState<T> : PersistedHookStateBase
> {
>     public T Value;
>     public PersistedHookState(T initial) => Value = initial;
>     internal override void SaveToCache()
>         => PersistedStateCache.Set(PersistKey, Value);
> }
> ```

**Cache lifetime:** The cache lives for the duration of the application process.
No disk serialization. Future work could add `UseDiskPersisted<T>` with JSON
serialization for app restart scenarios.

**Key collisions:** Developer-provided string keys. If two components use the same
key, they share state — this is intentional (allows sibling components to share
persisted state) but should be documented as a footgun.

---

## 4. Slots (Named Children Pattern)

### The pattern

Reactor doesn't need a special slot mechanism — **Element-typed props are slots.** This
section documents the convention so it's discoverable and consistent.

A "slot" is a prop of type `Element` (or `Element?` for optional slots) on a component's
props record. The component renders the slot elements at the appropriate positions in
its layout.

#### Single default slot: params Element?[] children

Most components have one content area. Use the existing `params Element?[]` pattern:

```csharp
// Container with single content slot — already idiomatic Reactor
VStack(
    Text("Child 1"),
    Text("Child 2")
)
```

#### Named slots via props records

Components with multiple distinct content areas use Element-typed props:

```csharp
// Props record — each Element property is a named slot
public record DialogProps(
    Element Title,
    Element Body,
    Element? Footer = null   // optional slot with default
);

class Dialog : Component<DialogProps>
{
    public override Element Render()
    {
        var (isOpen, setIsOpen) = UseState(true);

        return Border(
            VStack(
                // Title slot
                HStack(
                    Props.Title,
                    Button("✕", () => setIsOpen(false))
                ).Padding(16),

                // Body slot
                Border(Props.Body).Padding(16, 24),

                // Footer slot (optional — render only if provided)
                Props.Footer is not null
                    ? Border(Props.Footer).Padding(16).HAlign(HorizontalAlignment.Right)
                    : null
            )
        ).Background(Theme.CardBackground)
         .CornerRadius(8);
    }
}
```

Usage:

```csharp
Component<Dialog>(new DialogProps(
    Title: Text("Confirm Delete").Bold(),
    Body: VStack(
        Text("Are you sure you want to delete this item?"),
        Text("This action cannot be undone.").Foreground(Theme.SecondaryText)
    ),
    Footer: HStack(
        Button("Cancel", onCancel),
        Button("Delete", onDelete).Background(Theme.Danger)
    )
))
```

#### Conventions

1. **Use `Element` for required slots, `Element? = null` for optional slots.**
   The compiler enforces required slots at the call site.

2. **Props records, not classes.** Records give structural equality for free, which
   Memo uses to skip re-renders. A `DialogProps` with the same slot content across
   two renders will compare equal if the slot elements are structurally identical.

3. **Name slots semantically** — `Title`, `Body`, `Actions`, `Header`, `Footer`,
   `Leading`, `Trailing`, `Icon`, `Label`, `Content`. Avoid generic names like
   `Slot1`, `Slot2`.

4. **Default content for optional slots** — handle in the component's `Render()`:
   ```csharp
   var footer = Props.Footer ?? HStack(Button("OK", onOk));
   ```

5. **Multiple children in a slot** — wrap in a layout element at the call site:
   ```csharp
   new DialogProps(
       Title: Text("Hello"),
       Body: VStack(Text("Line 1"), Text("Line 2")),  // wrap multiple children
       Footer: HStack(Button("A"), Button("B"))
   )
   ```

#### Function component slots

Function components can use slots via tuple or anonymous type captures:

```csharp
// Define named content at the parent level, pass via closure capture
Element MakeCard(Element header, Element body)
{
    return Func(ctx =>
    {
        var (expanded, setExpanded) = ctx.UseState(false);
        return Border(
            VStack(
                HStack(header, Button(expanded ? "▲" : "▼", () => setExpanded(!expanded))),
                expanded ? body : null
            )
        );
    });
}

// Usage:
MakeCard(
    header: Text("Section Title").Bold(),
    body: Text("Expandable content here")
)
```

### Future work: Lazy\<Element\> for deferred slots

Some components should avoid creating slot content until it's needed. A tab control
with 10 tabs should only render the active tab's content:

```csharp
// FUTURE — not in this spec
public record TabItem(string Header, Lazy<Element> Content);

TabView(
    new TabItem("Home",    new(() => Component<HomePage>())),
    new TabItem("Settings", new(() => Component<SettingsPage>())),
    new TabItem("About",    new(() => Component<AboutPage>()))
)
```

`Lazy<Element>` uses `System.Lazy<T>` — the factory is called at most once, and the
result is cached. The tab control calls `.Value` only on the active tab.

This integrates naturally with `System.Lazy<T>` (no framework type needed) and is
straightforward to implement. It's deferred from this spec because:

1. It requires establishing patterns for when lazy vs eager is appropriate
2. The interaction with Memo needs careful design (Lazy instances are reference-equal
   only if they're the same instance, which they won't be across renders)
3. The common case (eager Element) should be established first

---

## 5. Implementation Phases

### Phase 1: Hooks improvements (lowest risk, highest local-state impact)

1. **Generic HookState\<T\>** — refactor internal hook storage classes
2. **Post-render effect cleanup** — move cleanup from UseEffect to FlushEffects
3. **Update RenderContext tests** to validate no boxing and correct cleanup ordering

Estimated scope: `RenderContext.cs` only. No reconciler changes.

**Local State grade after Phase 1: B+ → A-** (boxing and cleanup timing fixed)

### Phase 2: Context system (highest impact, moderate risk)

1. **Context\<T\> type** — new file `Reactor/Core/Context.cs`
2. **ContextValues on Element** — add property to base Element record
3. **.Provide() modifier** — extension method in `Reactor/Core/ContextExtensions.cs`
4. **ContextScope in Reconciler** — scope stack, push/pop during traversal
5. **UseContext\<T\> hook** — new hook type in RenderContext
6. **Migrate LocaleContext** — reimplement as Context consumer (non-breaking)
7. **Tests** — context scoping, nesting/shadowing, re-render on change

Estimated scope: New files for Context and extensions. Modifications to Element,
RenderContext, Reconciler (mount + update paths).

**Global State grade after Phase 2: F → A**

### Phase 3: Component memoization (highest perf impact, depends on Phase 2)

1. **ShouldUpdate on Component / Component\<TProps\>** — virtual method with default
2. **Memo check in ReconcileComponent** — props comparison + context change detection
3. **MemoElement** — new element type for function component memoization
4. **Memo() DSL factory** — function component wrapper
5. **Tests** — verify skip on equal props, re-render on context change, opt-out

Depends on Phase 2 because memo must interact correctly with context changes.

**Component Model grade after Phase 3: C → A-**
(remaining gap: FuncElement auto-memo requires compiler support — documented as future)

### Phase 4: State persistence + slots documentation

1. **UsePersisted\<T\> hook** — new hook type + PersistedStateCache
2. **Slots documentation** — patterns documented in this spec, no code changes
3. **Update sample apps** — demonstrate context, memo, persisted state, and slots

**Local State grade after Phase 4: A- → A**

---

## 6. Scorecard Projection

| Area | Before | After Phase 1 | After Phase 2 | After Phase 3 | After Phase 4 |
|---|---|---|---|---|---|
| **Global State** | F | F | **A** | A | A |
| **Component Model** | C | C | C+ | **A-** | A- |
| **Local State** | B+ | **A-** | A- | A- | **A** |

### Remaining gaps to A (not covered in this spec)

- **Component Model A- → A:** FuncElement auto-memoization (needs compiler plugin or
  source generator), typed props at element level (ComponentElement stores `object?`)
- **Local State A → A:** `ReadOnlySpan`-based dependency comparison (needs C# language
  evolution), batching control / transition API (React 18's `startTransition`)

These are genuine improvements but represent diminishing returns. The changes in this
spec close the structural gaps; the remaining items are polish.

---

## Appendix A: API Summary

### New types

| Type | File | Description |
|---|---|---|
| `Context<T>` | `Core/Context.cs` | Typed context definition with default value |
| `MemoElement` | `Core/Element.cs` | Function component wrapper with dependency-based memoization |

### New hooks

| Hook | Signature | Description |
|---|---|---|
| `UseContext<T>` | `T UseContext<T>(Context<T> context)` | Read nearest provider's value; re-render on change |
| `UsePersisted<T>` | `(T, Action<T>) UsePersisted<T>(string key, T initial)` | Like UseState but survives unmount/remount |

### New modifiers

| Modifier | Signature | Description |
|---|---|---|
| `.Provide()` | `.Provide<TValue>(Context<TValue> ctx, TValue value)` | Scope a context value to this element's subtree |

### Modified types

| Type | Change |
|---|---|
| `Element` | New `ContextValues` property |
| `Component` | New `UseContext<T>()` and `UsePersisted<T>()` convenience methods, virtual `ShouldUpdate()` |
| `Component<TProps>` | Virtual `ShouldUpdate(TProps?, TProps?)` with default equality comparison |
| `RenderContext` | Generic hook state classes, post-render cleanup, UseContext, UsePersisted |
| `Reconciler` | ContextScope stack, memo check in ReconcileComponent, MemoElement support |

### Slots convention (no new types)

| Pattern | When to use |
|---|---|
| `params Element?[]` | Single default children slot |
| `Element` prop on record | Required named slot |
| `Element?` prop on record | Optional named slot |
| `Lazy<Element>` (future) | Deferred slot evaluation for inactive content |
