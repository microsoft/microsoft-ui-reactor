# Flux.UI Comparative Analysis — Lessons for Reactor

A comprehensive analysis of what Flux.UI does better than Reactor, where Flux.UI has
solved problems identified in Reactor's critical review, and specific technical
approaches Reactor should consider adopting.

---

## Executive Summary

Flux.UI is a compile-time declarative UI framework for .NET that takes a
fundamentally different approach than Microsoft.UI.Reactor (Reactor). Where Reactor follows React's model
(virtual tree + reconciliation), Flux follows a reactive subscription model
(build once, subscribe to changes). Where Reactor uses CsWinRT managed objects for
WinUI interop, Flux bypasses the managed layer entirely with a custom C++/WinRT
P/Invoke DLL (DirectXaml). Where Reactor transforms UI at runtime (render → diff →
patch), Flux transforms UI at compile time (MSBuild code generation).

**Key findings:**

1. **DirectXaml native interop is dramatically faster** — Flux's C++/WinRT
   P/Invoke layer bypasses CsWinRT's Runtime Callable Wrappers, achieving up to
   128x faster element mounting. This is the single most impactful technical
   difference.

2. **No reconciliation by design** — Flux eliminates tree diffing entirely via
   subscription-based reactivity. State changes fire directly to subscribed
   property handlers. This solves many of the reconciler issues identified in
   Reactor's critical review.

3. **Compile-time DSL transformation** — Flux's MSBuild code generator transforms
   declarative C# into optimized element construction at build time, preserving
   exact line mapping for debugging. Reactor's DSL is pure runtime.

4. **Fine-grained reactivity** — Flux's `IState<T>` system updates individual
   properties at O(subscribers) cost, vs Reactor's O(subtree) re-render on state
   change. This directly addresses Reactor's "performance concerns accumulate" finding.

5. **Animation via mutation-site scoping** — Flux's `Animation.Animate()` wraps
   any state change in an animation context, solving Reactor's "animate any value"
   gap without requiring per-property transition declarations.

6. **Cross-platform via partial classes** — Flux supports WinUI, UWP, and Mock
   targets from a single source, while Reactor is WinUI-only.

---

## Table of Contents

1. [The DirectXaml Native Interop Layer (Faster WinUI Calls)](#1-the-directxaml-native-interop-layer)
2. [No Reconciliation: Subscription-Based Reactivity](#2-no-reconciliation-subscription-based-reactivity)
3. [Compile-Time Code Generation vs Runtime DSL](#3-compile-time-code-generation-vs-runtime-dsl)
4. [State Management: Fine-Grained Reactivity vs Hooks](#4-state-management-fine-grained-reactivity-vs-hooks)
5. [Animation: Mutation-Site Scoping vs 5-Property Transitions](#5-animation-mutation-site-scoping-vs-5-property-transitions)
6. [Layout System](#6-layout-system)
7. [DSL Design](#7-dsl-design)
8. [Theming and Styling](#8-theming-and-styling)
9. [Component Model and Lifecycle](#9-component-model-and-lifecycle)
10. [Ambient/Context System](#10-ambientcontext-system)
11. [Event Handling](#11-event-handling)
12. [Cross-Platform Story](#12-cross-platform-story)
13. [Problems from Reactor's Critical Review That Flux Solves](#13-problems-from-ducts-critical-review-that-flux-solves)
14. [Where Reactor Is Still Better](#14-where-duct-is-still-better)
15. [Recommendations: What Reactor Should Adopt](#15-recommendations-what-duct-should-adopt)

---

## 1. The DirectXaml Native Interop Layer

**This is the most significant technical difference between Flux and Reactor.**

### The Problem Flux Solves

When a .NET application sets a property on a WinUI control (e.g., `textBlock.Text
= "Hello"`), CsWinRT creates a Runtime Callable Wrapper (RCW) that marshals the
call through the COM interop layer. For individual calls, this overhead is
negligible. At scale — mounting hundreds of elements, setting dozens of properties
each — the overhead compounds dramatically.

Reactor uses standard CsWinRT managed objects throughout. Every `new Button()`,
every property set, every event subscription goes through the RCW layer.

### How Flux Solves It

Flux built **DirectXaml** — a C++/WinRT native DLL that provides direct property
access via P/Invoke, bypassing RCW entirely.

**Architecture:**

```
Reactor:    C# code → CsWinRT RCW → COM interop → WinUI property
Flux:    C# code → P/Invoke → C++ direct call → WinUI property
```

**Handle Model:**
- Each XAML element is stored as a raw `IInspectable*` pointer, marshalled as
  `nint` in C#
- No handle table — the pointer IS the handle
- `FluxCreateElement(typeId)` returns an AddRef'd pointer; caller owns the ref
- Zero-cost borrows via C++/WinRT's `borrow<T>()` (no QI or AddRef)

**X-Macro Dispatch Pattern:**
All property types, element types, and event subscriptions are defined in a single
file (`FluxIds.def`) using X-macros. A three-phase preprocessor expansion generates:

1. **Constants phase** — type aliases and PropertyId integers
   (`PropertyId = TypeId << 16 | LocalPropertyId`)
2. **Functions phase** — per-property typed setters and getters
3. **Lookup tables phase** — static arrays for O(1) dispatch

```cpp
// FluxIds.def (single source of truth)
FLUX_TYPE(TextBlock, 7, winrt::Controls::TextBlock)
FLUX_STRING(TextBlock, Text, 0)
FLUX_DOUBLE(TextBlock, FontSize, 2)

// Generated dispatch
FLUXAPI void FluxSetString(void* element, int32_t prop, const wchar_t* value, int32_t len);
FLUXAPI void FluxSetDouble(void* element, int32_t prop, double value);
```

**C# Consumption:**
```csharp
// Modern LibraryImport (not legacy DllImport)
[LibraryImport("Flux.DirectXaml")]
internal static partial nint FluxCreateElement(int typeId);

[LibraryImport("Flux.DirectXaml", StringMarshalling = StringMarshalling.Utf16)]
internal static partial void FluxSetString(nint element, int prop, string value, int len);

// Usage in element binding
WatchState(ContentState, text =>
    Native.DirectXaml.FluxSetString(_nativeHandle,
        (int)Native.FluxPropertyId.TextBlock_Text, text, text.Length));
```

**Event Handling:**
A unified callback signature packs all event data into 4 fields:

```cpp
typedef void(__cdecl* FluxEventCb)(void* owner, float f1, float f2, int32_t i1, int32_t i2);
```

| Event Type | f1 | f2 | i1 | i2 |
|---|---|---|---|---|
| Pointer | x | y | pointerId | buttonFlags |
| Wheel | x | y | delta | 0 |
| SizeChanged | width | height | 0 | 0 |
| KeyDown | 0 | 0 | VirtualKey | 0 |

Rich WinUI event args objects (PointerRoutedEventArgs, etc.) never cross the
interop boundary. The C++ side extracts the needed fields and packs them into
primitives. C# handlers use `[UnmanagedCallersOnly]` function pointers — no
delegate allocation, no closure capture.

**Scrolling is entirely native:** Instead of marshalling ScrollViewer events,
Flux encapsulates the entire scroll behavior in C++ using InteractionTracker +
VisualInteractionSource + expression animations. Only scroll position changes
cross the boundary as `(float x, float y)`.

### Measured Performance Impact

| Metric | CsWinRT (Reactor-style) | DirectXaml (Flux) | Ratio |
|--------|---------------------|-------------------|-------|
| Element creation ×1000 | 8,709 ms | ~2,500 ms (near-native) | **3.4× faster** |
| Memory (1000 elements) | 54 MB | ~5 MB | **11× less** |
| Mount phase (full page) | 4,248 ms | ~50-100 ms | **~50-128× faster** |

The mount phase improvement is staggering — this is the phase where elements are
created and properties are set, which is exactly what DirectXaml optimizes.

### What This Means for Reactor

Reactor's reconciler creates WinUI controls via `new Button()`, `new TextBlock()`
etc., sets properties directly, and wires events through managed delegates. For
the typical render cycle where most elements are unchanged, this is fine — Reactor's
ShallowEquals skips them. But for initial mount, large list renders, navigation
transitions (destroy old page, mount new page), and any scenario with many
element creations, Reactor pays the full CsWinRT overhead.

**Flux's element pool is effectively the entire WinUI layer** — elements persist
and only properties change via P/Invoke. Reactor's element pool (32 controls per
type, non-interactive only) is a band-aid on a fundamentally slower interop model.

---

## 2. No Reconciliation: Subscription-Based Reactivity

### Reactor's Approach (React Model)

```
State change → Component.Render() rebuilds element tree →
Reconciler diffs old vs new → Mount/Update patches WinUI controls
```

Cost: O(subtree size) for every state change, even if only one property changed.

### Flux's Approach (Reactive Subscriptions)

```
State change → IState<T>.Changed event fires →
Subscribed WatchState handlers run → Native property set directly
```

Cost: O(subscribers) — only the properties that actually depend on the changed
state are updated. No tree rebuilding, no diffing.

**How it works:**

1. **Build phase (once):** Element tree is constructed during `OnAttaching()`.
   Each element subscribes to its state dependencies via `WatchState()`:

   ```csharp
   protected override void OnAttaching(AttachPoint attachPoint)
   {
       WatchState(ContentState, text =>
           DirectXaml.FluxSetString(_nativeHandle, TextBlock_Text, text, text.Length));
       WatchState(FontSizeState, size =>
           DirectXaml.FluxSetDouble(_nativeHandle, TextBlock_FontSize, size));
   }
   ```

2. **State change:** When a `MutableState<T>` value changes, only subscribed
   handlers fire. If `FontSize` changes but `Text` doesn't, only the FontSize
   handler runs.

3. **Detach:** Subscriptions are automatically disposed in `OnDetaching()`.
   No manual cleanup needed.

**Control flow re-evaluation:**
- `IfElement` watches a condition state. When it changes, it destroys the old
  branch and builds the new one. No diffing — just destroy + create.
- `ForEachElement<T>` watches an items state. Item scopes are cached by key.
  When items change, removed items are destroyed, added items are created. No
  list reconciliation algorithm needed.

### What Problems This Solves (from Reactor's Critical Review)

| Reactor Problem | How Flux Avoids It |
|---|---|
| "Massive switch/dispatch for every element type" in reconciler | No reconciler exists. Elements update themselves via subscriptions. |
| "ShallowEquals is conservative to the point of being useless" | No ShallowEquals needed. Only changed properties update. |
| "Every component adds a hidden Border to the visual tree" | Components are layout containers directly. No identity wrapper needed. |
| "Modifier chains allocate on every render" | No re-render. Modifiers set at build time, never recreated. |
| "Event handler re-attachment on every update" | Event handlers wired once during OnAttaching, never re-wired. |
| "No concurrent/interruptible rendering" | No rendering phase to block. Updates are individual property sets dispatched to UI thread. |
| "Reflection in memo checks" | No memo checks. Components don't re-render from parent changes. |
| "FuncElement identity is still problematic" | No FuncElements. Build functions run once. |

### Trade-offs

Flux's model eliminates entire categories of performance problems, but at a cost:

- **Compile-time tooling required** — the DSL transformation is a build step,
  not pure C#. Build errors are harder to diagnose.
- **Less flexible control flow** — `IfElement` destroys and recreates branches.
  Reactor's reconciler can diff structurally different trees (though it usually
  doesn't need to).
- **No incremental migration** — you can't gradually adopt Flux's model. Reactor's
  React-style model is more familiar to web developers.

---

## 3. Compile-Time Code Generation vs Runtime DSL

### Flux's Approach

Flux uses an MSBuild Task (not a Roslyn Source Generator) to transform
`[Declarative]` method bodies at compile time. This is a two-pass process:

**Pass 1 (Structural):** Generates state backing fields, prop accessors,
modifier methods, and DSL factory methods from attributes:

```csharp
// Source
[State(0)] private partial int Count { get; set; }

// Generated
private readonly MutableState<int> _countState = new(0);
private partial int Count {
    get => _countState.Value;
    set => _countState.SetValue(value);
}
```

**Pass 2 (Semantic):** Transforms DSL method bodies using the full semantic
model (including Pass 1 outputs):

```csharp
// Source (inside [Declarative] method)
Text($"Count: {Count}")

// Generated
__ctx.Add(new Text(CountState.Map(__sc => $"Count: {__sc}")))
```

**Why MSBuild Task instead of Source Generator:**

| Aspect | Source Generator | MSBuild Task (Flux) |
|---|---|---|
| Semantic model | Partial (pre-generation) | Full (all generated code visible) |
| Multi-pass | Not possible | Supported (UpdateCompilation between passes) |
| Cross-project resolution | Limited | Full via referenced assemblies |

Flux needs the full semantic model to resolve DSL factory methods generated in
Pass 1 before transforming method bodies in Pass 2. Source Generators can't do
multi-pass.

**Streaming Emit API for debugger accuracy:**
The generated code preserves exact line mapping via a streaming Emit API that
interleaves generated text with Roslyn trivia (whitespace, comments). Every
breakpoint in the generated code lands on the developer's DSL source line.
A post-generation validation (FLUX900) ensures line counts match.

**Analyzers (FLUX001-FLUX600):**
Flux ships Roslyn analyzers that catch DSL errors at compile time — invalid
state usage, incorrect prop declarations, missing attributes, etc. Reactor has
no compile-time DSL validation.

### DSL Language Transformations: Complete Catalog

The code generator is not just an optimizer — it introduces a **new language
construct** that doesn't exist in C#. The most fundamental transformation is
**implicit child collection**: bare expression statements inside a lambda are
automatically collected as children of a parent element. In standard C#, writing
`Text("hello");` as a statement would call the method and discard the return
value. In Flux's DSL, it adds a child element.

This works because the DSL factory methods (e.g., `Text()`, `Box()`) are
generated as **marker stubs that throw at runtime**:

```csharp
// Generated DSL factory — exists only for the code generator to see in the syntax tree
public static Text Text([State] string text)
    => DslMarkerException.Throw<Text>("DSL.Text()");
```

The code generator gives these statements meaning by detecting DSL invocations
and wrapping them in `__ctx.Add(...)` calls, with an injected `BuildContext`
parameter that doesn't exist in the user's code. This is Flux's equivalent of
Swift's `@resultBuilder` / `@ViewBuilder` or Kotlin Compose's `@Composable`
compiler plugin — but implemented through a Roslyn source generator because C#
has no result builder or compiler plugin API.

**The full set of transformations the code generator performs:**

#### 1. Implicit Child Collection (Novel Construct)

```csharp
// User writes:                          // Generated:
Box(() =>                                new Box((__c) =>
{                                        {
    Text("hello");                           __c.Add(new Text(new ImmutableState<string>("hello")));
    Text("world");                           __c.Add(new Text(new ImmutableState<string>("world")));
});                                      });
```

The `BuildContext` parameter (`__c`) is injected, bare DSL statements become
`__c.Add(...)` calls, and element factory calls become constructor calls. The
same implicit collection applies at every nesting level — inside `if`/`else`,
`foreach`, `switch` — which is why all control flow transforms below exist.

#### 2. Local Variables → Reactive State Wrappers

Dataflow analysis determines mutability:

```csharp
// Never reassigned → immutable (zero-cost):
var padding = 24;                        var padding = new ImmutableState<int>(24);

// Reassigned → mutable:
var count = 0;                           var count = __ctx.Mutable<int>(0);
count = 5;                               count.SetValue(5);

// References [State] property → mapped (reactive):
var visible = Count > 0;                 var visible = CountState.Map(__sc => __sc > 0);

// References multiple states → computed:
var total = Width + Height;              var total = ComputedState.Create(
                                             (IReadOnlyList<IState>)[WidthState, HeightState],
                                             () => WidthState.Value + HeightState.Value);
```

#### 3. Built-in DSL Methods → State Constructors

```csharp
var count = State(0);                    var count = __ctx.Mutable<int>(0);
var doubled = Computed(() => Count * 2); var doubled = ComputedState<int>.Create([CountState], ...);
var style = Ambients.ButtonStyle.Use();  var style = __ctx.Read(Ambients.ButtonStyle);
var opacity = Animated(IsVisible ? 1 : 0, Fade());
                                         var opacity = AnimatedState<float>.Create(
                                             IsVisibleState.Map(...), Fade());
```

#### 4. Control Flow → Reactive Builder Chains

```csharp
// if/else → reactive conditional:
if (IsVisible)                           __ctx.If(IsVisibleState, (__c) =>
{                                        {
    Text("Hello");                           __c.Add(new Text(...));
}                                        }).Else((__c) =>
else                                     {
{                                            __c.Add(new Text(...));
    Text("Hidden");                      }).End();
}

// foreach → reactive list:
foreach (var item in Items)              __ctx.ForEach<T>(ItemsState, (__c, item) =>
{                                        {
    Text(item.Name);                         __c.Add(new Text(...));
}                                        });

// switch → reactive switch builder:
switch (Mode)                            __ctx.Switch(ModeState)
{                                            .Case(ViewMode.Edit, (__c) => { ... })
    case ViewMode.Edit: ...                  .Case(ViewMode.View, (__c) => { ... })
    case ViewMode.View: ...                  .Default((__c) => { ... })
    default: ...                             .End();
}
```

Type patterns (`case string s when s.Length > 0:`) are also supported, becoming
`.CaseWhen(__x => __x is string __p && __p.Length > 0, ...)`.

#### 5. [State] Parameter Arguments → Automatic Wrapping

```csharp
// Static value:
view.Background(Colors.Red)              view.Background(new ImmutableState<Color>(Colors.Red))

// State property reference:
view.Background(MyColor)                 view.Background(MyColorState)

// Expression with state:
view.Opacity(IsActive ? 1f : 0f)         view.Opacity(IsActiveState.Map(__sa => __sa ? 1f : 0f))
```

#### 6. Method Signatures → BuildContext Injection

```csharp
// [State] T → IState<T>:
void Build([State] int count)            void Build(BuildContext __ctx, IState<int> count)

// [Declarative] Action → Action<BuildContext>:
void Build([Declarative] Action? content)
                                         void Build(BuildContext __ctx, Action<BuildContext>? content)
```

#### 7. Interpolated Strings → Reactive String Mapping

```csharp
$"Count: {Count}"                        CountState.Map(__sc => $"Count: {__sc}")

$"{A} + {B}"                             ComputedState<string>.Create(
                                             [AState, BState],
                                             () => $"{AState.Value} + {BState.Value}")
```

#### 8. Mutation Operators → SetValue Calls (in lambdas)

```csharp
count = 5;                               count.SetValue(5);
count++;                                 count.SetValue(count.Value + 1);
--count;                                 count.SetValue(count.Value - 1);
```

#### 9. Member Access on State → .Value Unwrapping

```csharp
Items.Count                              ItemsState.Value.Count
```

#### 10. Type Qualification and Immutable Caching

All type references get `global::` prefix to prevent namespace conflicts. Pure
compile-time constants are hoisted into a static `Immutables` inner class with
FNV-1a hashing for deduplication. Transitively immutable `.Map()` results (where
all source states are compile-time constants) are also cached.

### What This Means for Reactor

Reactor's DSL is pure runtime C# — method calls that create element records. This
means:

- **Every render allocates element records** — even for unchanged UI. Flux
  builds once and subscribes.
- **No compile-time optimization** — modifier chains, string interpolation
  with state, and control flow all execute at runtime. Flux optimizes these
  at build time (e.g., single-state string interpolation becomes a
  `MappedState` with zero-allocation transform).
- **No static analysis** — DSL misuse is a runtime error. Flux catches it
  at compile time with analyzers.
- **Debugging is straightforward** — Reactor's DSL is plain C#, so debugging
  works naturally. Flux needs the Emit API to preserve line mapping (a complex
  requirement they've solved).

---

## 4. State Management: Fine-Grained Reactivity vs Hooks

### Flux's State System

Flux has a type hierarchy of reactive state primitives:

| Type | Purpose | Subscription Cost |
|---|---|---|
| `ImmutableState<T>` | Static values | Zero (no subscriptions) |
| `MutableState<T>` | Read/write state | Per-subscriber |
| `MappedState<TSource, T>` | Single-source transform | One subscription |
| `ComputedState<T>` | Multi-source derived | N subscriptions |
| `AnimatedState<T>` | State with animation scoping | Per-subscriber |
| `MutableListState<T>` | Fine-grained list changes | Per-subscriber |

**Key insight: Static values have zero cost.** When Flux encounters a static
string like `Text("Hello")`, the code generator wraps it in `ImmutableState<string>`
which has no subscription mechanism — zero overhead, zero allocations for
observing. Reactor creates a `TextElement` record on every render, even for static
text.

**Smart transform selection:** String interpolation with state is automatically
optimized:

| Expression | Flux Transform | Reactor Equivalent |
|---|---|---|
| `"Hello"` | `ImmutableState<string>` (zero cost) | `TextElement` record (allocated every render) |
| `$"Count: {Count}"` | `CountState.Map(...)` (1 subscription) | Render rebuilds entire component |
| `$"{A} + {B}"` | `ComputedState<string>.Create(...)` (2 subscriptions) | Render rebuilds entire component |

**Attribute-driven ownership semantics:**

| Attribute | Purpose | Backing |
|---|---|---|
| `[Bound]` | Required input from parent | Constructor parameter |
| `[Prop]` | Configurable default | `ImmutableState<T>` or parent-provided |
| `[State]` | Internal mutable state | `MutableState<T>` |
| `[Computed]` | Derived from other state | `ComputedState<T>` |

This makes data flow explicit and analyzable at compile time. Reactor's hooks
(`UseState`, `UseReducer`, `UseMemo`) declare the same concepts but at runtime
with no compile-time visibility.

### Comparison to Reactor's Hooks

| Aspect | Flux | Reactor |
|---|---|---|
| State declaration | Compile-time attributes | Runtime hooks |
| Change propagation | Direct subscription to specific state | Full component re-render |
| Static value cost | Zero (ImmutableState) | Same as dynamic (record allocation) |
| Dependency tracking | Automatic (compile-time analysis) | Manual (`params object[]` deps) |
| Boxing | Never (generic throughout) | Fixed for hook state, but deps still box |
| Computed values | `ComputedState<T>` — only recomputes when dependencies change | `UseMemo` — recomputes when component re-renders with changed deps |
| List changes | `MutableListState<T>` with fine-grained notifications | Full list rebuild on any change |

### What Reactor's Critical Review Says

> "Dependency comparison still uses `object.Equals` on `object[]`" — deps are
> boxed into `object[]` and compared with `Equals()`.

Flux avoids this entirely. There are no dependency arrays. State dependencies
are tracked by subscriptions established at build time.

> "Context values are boxed in the scope stack" — `ContextScope` stores
> `List<(ContextBase, object?)>`.

Flux's Ambient system uses typed `Ambient<T>` objects with generic
`WatchAmbient<T>()` handlers. No boxing, no type erasure.

---

## 5. Animation: Mutation-Site Scoping vs 5-Property Transitions

### Reactor's Animation Problem (from Critical Review)

> "Implicit transitions are still limited to 5 properties... You can't implicitly
> animate width, height, corner radius, margin, padding, font size, color..."
>
> "No declarative value-driven animation API... no way to declaratively say
> 'animate this value from A to B with this curve'"
>
> "No UseAnimation hook... no bridge between the hooks system and the animation
> system"

Reactor scored **C** on animation. The fundamental issue: Reactor can only animate what
WinUI exposes transition properties for (5 properties), and there's no way to
wrap arbitrary state changes in animation.

### Flux's Animation Approach

**Mutation-site animation scoping:**

```csharp
Animation.Animate(TimingCurve.Spring(0.8f, 0.05f), () =>
{
    Count = 5;        // This state change animates
    IsExpanded = true; // This one too
});
```

Any state change inside the `Animation.Animate` scope is animated. The animation
context is stored as a `thread_local` static — `Animation.Current` and
`Animation.HasScope` — so any `MutableState.SetValue()` called during the scope
picks up the animation.

This is architecturally identical to SwiftUI's `withAnimation { state = newValue }`.

**Animation types:**

| Type | Description | Reactor Equivalent |
|---|---|---|
| `SpringAnimation` | Physics-based (damping + period) | Only for layout animations |
| `EaseAnimation` | Timed curve with cubic bezier easing | Not available |
| `LinearAnimation` | Constant speed over duration | Not available |
| `RepeatingAnimation` | Wraps inner, with optional autoreversal | Not available |

**Declaration-site animation:**
Views can set default animation on state properties via `AnimatedState<T>` —
any change to that state always animates, no explicit scope needed.

**Transitions for navigation/routing:**

```csharp
Switcher(navStack, () => { ... })
    .Transition(Transition.Opacity + Transition.Slide(Edge.End))
```

Transitions are composable with `+` operator. Enter/exit effects combine
naturally. Reactor has separate `NavigationTransition` types that can't compose.

**Native composition animation via DirectXaml:**

```cpp
struct FluxCurve {
    uint8_t  type;        // 0=Spring, 1=Ease, 2=Linear
    uint8_t  autoreverses;
    float    param1;      // Spring: DampingRatio | Ease: Duration(ms)
    float    param2;      // Spring: Period
    int32_t  repeatCount; // 0=once, -1=forever, >0=N
    float    cp1x, cp1y;  // cubic bezier control point 1
    float    cp2x, cp2y;  // cubic bezier control point 2
};

FLUXAPI void FluxAnimateFloat(void* handle, int32_t handleKind,
    const wchar_t* propertyName, float target, FluxCurve curve);
```

Animation runs entirely on the composition thread. Reactor's layout animations
do the same thing for Offset/Size, but Flux generalizes it to any visual
property with full curve control.

### What This Solves

| Reactor Animation Gap | Flux Solution |
|---|---|
| "Only 5 implicit transition properties" | `Animation.Animate()` wraps any state change |
| "No value-driven animation" | Spring/Ease/Linear animation types on any state |
| "No enter/exit for individual elements" | `Transition.Opacity + Transition.Slide()` compositions |
| "No easing function DSL" | `TimingCurve.Spring()`, `TimingCurve.Ease()` with cubic bezier |
| "No UseAnimation hook" | `AnimatedState<T>` + mutation-site scoping |
| "No keyframe/sequence DSL" | `RepeatingAnimation` with autoreversal (partial) |

---

## 6. Layout System

### Flux's Layout Approach

Flux owns the entire layout pipeline rather than delegating to WinUI panels:

**Custom layout via HostPanel:**
Flux creates a C++/WinRT `FluxHostPanel` that delegates `MeasureOverride` and
`ArrangeOverride` back to C# via function pointers:

```csharp
[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
internal static void HostPanelMeasure(void* owner, float availW, float availH,
    float* outW, float* outH)
{
    var layout = (Layout)GCHandle.FromIntPtr((nint)owner).Target!;
    var result = layout.MeasureChildren(constraints);
    *outW = result.Width; *outH = result.Height;
}
```

**Built-in layouts:**
- `Column` / `Row` — flex layout with `Weight` for proportional sizing
- `Box` — overlay stacking with alignment
- `Scrollable` — scroll with reactive `ScrollOffset` state

**LayoutKey system:**
Per-child layout metadata (Weight, Align, FillMaxWidth, etc.) is stored via
`LayoutKey<T>` — an int-keyed dictionary, not property objects. This avoids
WinUI's attached property overhead.

**Constraints pipeline:**
```
View.Measure(Constraints)
  → Apply LayoutKey constraints (Width/Height/Min/Max)
  → Deflate by Margin
  → PlatformMeasure (WinUI's Measure)
  → Add Margin back
```

### Comparison to Reactor

| Aspect | Flux | Reactor |
|---|---|---|
| Layout ownership | Flux owns measurement + arrangement | Delegates to WinUI panels |
| Flex layout | Built-in Column/Row with Weight | FlexPanel via Yoga port (dual layout system) |
| Per-child metadata | LayoutKey<T> (int-keyed) | WinUI attached properties |
| Grid | Not provided (Column/Row with Weight covers most cases) | String-typed: `Grid(["*", "Auto"])` |
| Spacer | Weight-based: `Box().Weight(1)` | No equivalent (SwiftUI gap noted in review) |

Reactor's critical review notes: "FlexPanel is impressive but duplicates WinUI's
layout system" and "Two layout systems with different mental models." Flux avoids
this by owning layout from the start — there's one layout system, not two.

However, Reactor's approach of using WinUI's native Grid, StackPanel, etc. means
it inherits all the platform's layout optimizations and existing developer
knowledge. Flux's custom layout is more flexible but requires re-implementing
layout algorithms that WinUI already has.

---

## 7. DSL Design

### Flux's DSL

Flux uses regular C# with compile-time transformation:

```csharp
using static Flux.DSL;

Column(() => {
    Text($"Count: {Count}");
    Button("Increment").OnTapped(() => Count++);
    if (ShowDetails) {
        DetailView();
    }
})
```

The code generator transforms `if`, `foreach`, `switch` into reactive elements:
- `if (condition)` → `__ctx.If(conditionState, ...)`
- `foreach (var item in items)` → `__ctx.ForEach(itemsState, ...)`
- `switch (value)` → `__ctx.Switch(valueState).Case(...)`

### Comparison to Reactor's DSL

| Aspect | Flux | Reactor |
|---|---|---|
| Children syntax | Lambda blocks: `Column(() => { ... })` | Params arrays: `VStack(child1, child2)` |
| Control flow | Real `if`/`foreach` (transformed at compile time) | Ternary: `cond ? elem : null`, `ForEach()` |
| Modifiers | Fluent methods, set at build time (no allocation per render) | Fluent methods, allocate `ElementModifiers` record every render |
| String interpolation | `$"Count: {Count}"` → reactive `MappedState` | `$"Count: {count}"` → new string every render |
| Type safety | Compile-time analyzers (FLUX001-FLUX600) | Runtime errors only |

**Flux addresses Reactor's DSL weaknesses from the critical review:**

> "No block syntax for children" — Flux uses lambda blocks for children, providing
> clear visual nesting:
> ```csharp
> Column(() => {
>     Row(() => {
>         Text("Label");
>         Text("Value");
>     });
> })
> ```

> "Modifier chains allocate on every render" — Flux modifiers are set once during
> element construction, never re-allocated. The state subscriptions handle updates.

> "Null-based conditional rendering is fragile" — Flux uses real `if` statements
> that the code generator transforms into reactive `IfElement` directives.

---

## 8. Theming and Styling

### Reactor's Theming Problem (from Critical Review, C+ grade)

> "XamlReader.Load on every theme-bound element, every render"
> "Only 3 brush properties support ThemeRef bindings"
> "No custom branded theme resources"
> "Hard-coded colors are a trap with no guardrails"

### Flux's Approach

Flux uses an **Ambient system** for theming — values propagate down the element
tree without prop drilling:

```csharp
// Built-in theme ambients
public static readonly Ambient<Brush> Foreground = ...;
public static readonly Ambient<Brush> Background = ...;
public static readonly Ambient<double> FontSize = ...;
public static readonly Ambient<FontWeight> FontWeight = ...;
public static readonly Ambient<double> ContentAlpha = ...;  // 0.74 secondary, 0.38 tertiary

// Override in subtree
protected override void ConfigureAmbients()
{
    Ambients.Foreground.Override(CurrentThemeState).Apply();
}

// Consume (auto-reactive via WatchAmbient)
Box(() => { Content(); }).Background(Ambients.Foreground.Use())
```

**Key differences from Reactor:**
- No `XamlReader.Load` — theme values are direct state subscriptions
- No XAML string construction or parsing per element per render
- Typography, colors, and content alpha are all first-class ambients
- Custom theme resources are just new `Ambient<T>` declarations
- Ambients work on ANY property, not just 3 brush properties

**Content alpha system:** Flux has a sophisticated text hierarchy system via
`ContentAlpha` ambient — primary text (1.0), secondary (0.74), tertiary (0.38).
This cascades through the tree automatically. Reactor has no equivalent.

---

## 9. Component Model and Lifecycle

### Flux's Element Hierarchy

```
Element (platform-agnostic base)
├── View (visual elements backed by UIElement)
│   ├── Layout (container with custom measure/arrange)
│   │   ├── Column, Row, Box, Scrollable
│   │   └── Component (user-defined, with Build() method)
│   ├── Text, Image, etc.
│   └── Control subtypes
└── Directive (non-visual, control flow)
    ├── IfElement
    ├── ForEachElement<T>
    └── SwitchBuilder
```

**Key difference: Directives are non-visual.** `IfElement`, `ForEachElement`,
etc. don't create WinUI elements. They're transparent in the visual tree. This
means control flow adds ZERO layout cost — no hidden Borders, Grids, or other
wrapper elements.

**Reactor comparison:** Every `Component` and `FuncElement` in Reactor creates a
hidden `Border` wrapper. `NavigationHost` creates a `Grid`. `CommandHost`
creates another `Grid`. Flux has none of this overhead.

### Lifecycle (Virtual Methods, Not Events)

```csharp
OnInitializing()   // once per element, during first attach
OnAttaching()      // each attach (subscriptions created here)
OnDetaching()      // each detach (subscriptions auto-disposed)
OnDestroy()        // permanent destruction
```

**Subscriptions are lifecycle-managed:** `WatchState` and `WatchAmbient` are
called during `OnAttaching()` and automatically disposed during `OnDetaching()`.
There's no possibility of leaked subscriptions or stale callbacks.

**Reactor comparison:** Reactor's hooks (`UseEffect` with cleanup, `UseCallback`) can
leak if cleanup functions are incorrect. Effect cleanup timing was a bug that had
to be fixed (now post-render, matching React). Flux's lifecycle is simpler and
more deterministic.

---

## 10. Ambient/Context System

### Comparison

| Aspect | Flux Ambients | Reactor Context |
|---|---|---|
| Value storage | Typed `Ambient<T>` | Type-erased `object?` in scope stack |
| Value boxing | Never (generic throughout) | Yes (value types boxed) |
| Override mechanism | `ambient.Override(state).Apply()` in `ConfigureAmbients()` | `.Provide(context, value)` modifier |
| Consumption | `WatchAmbient<T>(ambient, handler)` — direct subscription | `UseContext<T>` — read during render |
| Change detection | Direct state subscription (zero overhead for unchanged) | `Equals(currentValue, lastValue)` boxed comparison |
| Built-in ambients | ~15 (Foreground, Background, FontSize, FontWeight, Enabled, Dispatcher, etc.) | Locale (single built-in) |

**Flux's ambient system is significantly richer.** Typography, colors, content
opacity, enabled state, focus handling, and more are all first-class ambients
that cascade through the tree. Reactor's context system is a general mechanism
but has minimal built-in usage beyond locale.

**Performance:** Flux's WatchAmbient is a direct subscription — when the ambient
value changes, only elements that watch it update. Reactor's UseContext triggers a
re-render of the entire component, which then diffs its children.

---

## 11. Event Handling

### Flux's Gesture System

Flux has a sophisticated gesture system built on top of DirectXaml pointer events:

- `DetectTap` — tap gesture recognition
- `DetectDrag` — drag with stable deltas
- `DetectHover` — hover in/out with pointer tracking
- Pointer capture management via DirectXaml's `FluxCapturePointer()`

All gestures use the unified callback signature — no COM event args objects
cross the interop boundary.

### Reactor's Event Problem (from Critical Review)

> "The Tag-based event dispatch is a fragile workaround"
> "Event handlers wired in .Set() accumulate without lifecycle cleanup"
> ".Set() still carries too much weight" (30-40% of WinUI features)

Flux avoids the Tag pattern entirely — event callbacks are `[UnmanagedCallersOnly]`
function pointers stored via GCHandle. The element's identity is tracked by a
pinned GCHandle, not a WinUI control property that could be clobbered.

Flux also avoids `.Set()` entirely — all pointer events, keyboard events, focus
events, and gestures are first-class. Reactor's critical review notes that
"30-40% of WinUI features still require .Set()." Flux has no escape hatch
because it doesn't need one — the DirectXaml layer exposes everything the
framework uses.

---

## 12. Cross-Platform Story

Flux supports multiple platforms via partial classes:

```
src/Flux/Elements/View.cs          // Shared logic
src/Flux/Elements/View.Win.cs      // WinUI 3 platform code
src/Flux/Elements/View.Uwp.cs      // UWP platform code
src/Flux/Elements/View.Mock.cs     // Test mock
```

Two global using aliases control the platform type mapping:
```csharp
// WinUI
global using PlatVisual = Microsoft.UI.Xaml.UIElement;
global using PlatVisualCollection = Microsoft.UI.Xaml.Controls.UIElementCollection;

// UWP
global using PlatVisual = Windows.UI.Xaml.UIElement;
global using PlatVisualCollection = Windows.UI.Xaml.Controls.UIElementCollection;
```

Adding a new platform requires only the two aliases and platform-specific partial
class files. No virtual dispatch overhead, no runtime polymorphism.

Reactor is WinUI 3-only with no cross-platform story.

---

## 13. Problems from Reactor's Critical Review That Flux Solves

This section maps every major criticism from Reactor's critical review to Flux's
approach:

### Reconciler Issues

| # | Reactor Problem | Flux Solution |
|---|---|---|
| 1 | "Massive switch/dispatch for every element type" — monolithic reconciler | No reconciler. Elements self-update via subscriptions. |
| 2 | "Tag-based event dispatch is fragile" — WinUI Tag property repurposed | GCHandle-based ownership. No WinUI property dependency. |
| 3 | "ShallowEquals is useless for interactive elements" | No diffing needed. Subscriptions fire only on change. |
| 4 | "Every Component adds hidden Border to visual tree" | Directives are non-visual. No wrapper elements. |
| 5 | "Element pool only handles non-interactive controls" | No pool needed. Elements persist; only properties change. |
| 6 | "ForceDetach hack via scratch StackPanel" | DirectXaml manages tree operations natively. |
| 7 | "No concurrent/interruptible rendering" | No rendering phase. Individual property dispatches to UI thread. |

### State Management Issues

| # | Reactor Problem | Flux Solution |
|---|---|---|
| 8 | "Context values are boxed" | Generic `Ambient<T>` throughout. No boxing. |
| 9 | "ShouldUpdateWithProps uses reflection" | No memo checks. Subscriptions handle all updates. |
| 10 | "PersistedStateCache is unbounded" | No persisted state cache. State lives in elements. |
| 11 | "Dependency comparison boxes into object[]" | No dependency arrays. State tracked by subscriptions. |

### DSL Issues

| # | Reactor Problem | Flux Solution |
|---|---|---|
| 12 | "No block syntax for children" | Lambda blocks: `Column(() => { ... })` |
| 13 | "Modifier chains allocate every render" | Modifiers set once, subscriptions handle updates. |
| 14 | "Null-based conditional rendering is fragile" | Real `if` statements transformed to reactive `IfElement`. |
| 15 | "String-typed Grid definitions" | No Grid. Column/Row with Weight covers most cases. |

### Performance Issues

| # | Reactor Problem | Flux Solution |
|---|---|---|
| 16 | "XamlReader.Load on every themed element" | Ambient subscriptions. No XAML parsing. |
| 17 | "Accelerator rebuild per render" | No re-render. Gestures are persistent subscriptions. |
| 18 | "Event handler re-attachment on every update" | Handlers attached once in OnAttaching. |
| 19 | "Reflection in memo checks" | No memo checks exist. |
| 20 | "Visual tree depth from wrapper elements" | Directive elements are non-visual. Zero tree depth overhead. |

### Animation Issues

| # | Reactor Problem | Flux Solution |
|---|---|---|
| 21 | "Only 5 implicit transition properties" | `Animation.Animate()` wraps any state change. |
| 22 | "No value-driven animation" | Spring/Ease/Linear types on any property. |
| 23 | "No enter/exit for individual elements" | Composable `Transition` types (Opacity + Slide). |
| 24 | "No easing function DSL" | `TimingCurve.Spring()`, `TimingCurve.Ease()` with cubic bezier. |
| 25 | "No UseAnimation hook" | `AnimatedState<T>` + mutation-site scoping. |

### Other Issues

| # | Reactor Problem | Flux Solution |
|---|---|---|
| 26 | "No Spacer equivalent" | `Box().Weight(1)` — weight-based flexible sizing. |
| 27 | "Responsive hooks force full re-render" | Ambients only update subscribers, not entire component. |
| 28 | "Only 3 theme properties supported" | Ambients work on any property. |
| 29 | ".Set() carries 30-40% of WinUI" | DirectXaml provides first-class access to all needed APIs. |

---

## 14. Where Reactor Is Still Better

Flux isn't uniformly superior. Reactor has genuine advantages:

### 1. Control Coverage (Reactor: 94% of WinUI, Flux: ~15 element types)

Reactor wraps 81/86 WinUI controls with 200+ factory methods. Flux has a small set
of primitives (Box, Column, Row, Text, Scrollable) and builds everything from
those + custom controls. For apps that need WinUI's rich control library
(NavigationView, TreeView, ListView, ComboBox, etc.), Reactor provides them
out-of-box while Flux requires building equivalents from scratch.

### 2. Commanding (Reactor has it, Flux doesn't)

Reactor's Command system is a genuine differentiator — no competing framework
provides define-once commands with metadata bundling, standard commands, async
lifecycle, and focus-scoped accelerators. Flux has no commanding abstraction.

### 3. Navigation (Reactor has a full system, Flux has basic routing)

Reactor's type-safe navigation with developer-owned back stack, GPU transitions,
lifecycle guards, LRU caching, and deep linking is comprehensive. Flux has
`Route<T>` and `Switcher` with basic transitions but lacks the full application
architecture layer.

### 4. Familiarity (React model vs novel reactive model)

Reactor's React-style model transfers mental models from the web ecosystem. Millions
of developers understand hooks, reconciliation, and virtual trees. Flux's
compile-time reactive model is novel — powerful, but unfamiliar. Migration from
existing React knowledge to Reactor is easier than to Flux.

### 5. No Build Tooling Required

Reactor's DSL is plain C# — no code generators, no build steps, no MSBuild tasks.
The framework is a NuGet package that just works. Flux requires the code generator
to run before compilation, which adds build complexity and potential failure modes.

### 6. Observable/MVVM Interop

Reactor has rich hooks for bridging with existing MVVM codebases:
`UseObservable<T>`, `UseObservableTree<T>`, `UseCollection<T>`, and ICommand
interop. Flux has no MVVM bridge — it expects adoption of its own state system.

### 7. Lists and Virtualization

Reactor wraps ListView, GridView, and LazyVStack/LazyHStack with virtualization.
Flux has ForEachElement but no virtualization story for large lists.

### 8. Accessibility (Reactor has 16 modifiers + UIA tests)

Reactor has 16 first-class accessibility modifiers with tiered storage and 12 E2E
UIA tests. Flux has basic accessibility via AutomationProperties (3 properties
exposed through DirectXaml: A11yName, A11yHint, A11yHidden) but no comparable
accessibility story.

---

## 15. Recommendations: What Reactor Should Adopt

### Priority 1: Investigate DirectXaml-Style Native Interop

**Impact: Very High | Effort: Very High | Risk: High**

Flux's DirectXaml layer is the single most impactful difference. The 50-128x
improvement in mount phase is not a micro-optimization — it's a fundamental
architecture advantage.

**What to investigate:**
- Profile Reactor's actual mount overhead per CsWinRT interop call
- Measure the real cost of `new Button()` + property sets vs P/Invoke equivalent
- Consider a hybrid approach: DirectXaml for hot paths (element creation, property
  sets during reconciliation), standard CsWinRT for cold paths (one-time setup)
- Evaluate whether Reactor's element pool improvements could narrow the gap enough
  that the DirectXaml complexity isn't justified

**Key learning from Flux:**
The X-macro pattern (`FluxIds.def` as single source of truth → C++ dispatch +
C# enum generation) is elegant. If Reactor adopts native interop, this pattern
should be studied.

**Risk factors:**
- Adds C++/WinRT build dependency (currently Reactor is pure C#)
- Requires maintaining native DLL across architectures (x64, arm64)
- Testing burden increases (native + managed interop)
- Platform-specific code paths multiply

### Priority 2: Reduce Reconciler Overhead for Unchanged Elements

**Impact: High | Effort: Medium | Risk: Low**

Even without adopting Flux's full reactive model, Reactor can learn from Flux's
"only update what changed" principle:

**Concrete improvements:**
1. **Cache ShouldUpdateWithProps MethodInfo** — the reflection lookup on every
   memo check is an obvious fix. Flux doesn't need this because it has no memo
   checks, but if Reactor keeps hooks, cache the method.
2. **Make ShallowEquals useful for interactive elements** — the current
   "conservative: return false for unknown types" defeats the optimization for
   the majority of elements. Consider comparing delegate targets (not just
   references) or accepting that handlers from the same component are likely
   the same.
3. **Eliminate hidden wrapper elements** — Flux proves that component identity
   can be tracked without visual tree wrappers. Consider tracking component
   identity via a side map instead of Border wrappers.
4. **Cache XamlReader.Load results for theming** — key by (targetType, binding
   set). If the same theme tokens are used on the same control type, reuse the
   Style object. Flux avoids this entirely via ambient subscriptions, but Reactor
   can still significantly reduce the overhead.

### Priority 3: Adopt Animation Scoping Pattern

**Impact: High | Effort: Medium | Risk: Low**

Flux's `Animation.Animate(() => { state = newValue; })` pattern is a direct
solution to Reactor's biggest animation gap ("animate any state change").

**How to adapt for Reactor:**
```csharp
// Proposed API
ReactorAnimation.WithAnimation(TimingCurve.Spring(0.8, 0.05), () =>
{
    setExpanded(true);    // This state change animates
    setOffset(100);       // This one too
});
```

Implementation approach:
- Store animation context in `[ThreadStatic]` during the scope
- In `ReactorHost.RequestRender()`, detect if animation context is active
- During reconciliation, for elements with changed properties where an animation
  context exists, apply composition-layer implicit animations instead of instant
  property sets
- This can coexist with existing implicit transitions

### Priority 4: Consider Compile-Time State Analysis

**Impact: Medium | Effort: High | Risk: Medium**

Flux's code generator can distinguish static values from reactive ones and
optimize accordingly. Reactor could benefit from a simpler version:

**Source Generator for element records:**
- Detect which fields in element records are always the same (static text, fixed
  sizes, constant colors)
- Generate optimized ShallowEquals that only compares dynamic fields
- Detect dependency arrays that are provably stable (no closures, only value types)
  and skip the comparison

This doesn't require Flux's full MSBuild task approach — a Roslyn Source Generator
would suffice for analysis-only optimizations.

### Priority 5: Evaluate Ambient-Style Theming

**Impact: Medium | Effort: Medium | Risk: Low**

Flux's ambient system for theming is cleaner than Reactor's XamlReader.Load approach:

**What to adopt:**
- Use `Context<ThemeBrush>` to propagate theme values instead of generating
  XAML Style objects
- Components that need theme colors consume them via `UseContext` — already exists
- Apply brushes directly to controls during reconciliation, not via Style injection
- This eliminates all XamlReader.Load overhead

**Trade-off:** Loses WinUI's native `{ThemeResource}` resolution for
Light/Dark/HighContrast. Would need to implement theme switching logic in Reactor
rather than delegating to WinUI. May not be worth it if the perf impact of
XamlReader.Load isn't measured to be significant.

### Priority 6: Study Flux's Element Lifecycle for Subscription Ideas

**Impact: Medium | Effort: Low | Risk: Low**

Even within Reactor's React model, some ideas from Flux's lifecycle are adoptable:

- **Directive-style non-visual elements** — Reactor's `ErrorBoundary`, conditional
  elements, and `ForEach` could be non-visual (no Border/Grid wrapper) if
  component identity is tracked separately
- **Lifecycle-managed subscriptions** — a `UseSubscription` hook that
  automatically cleans up on unmount would be cleaner than `UseEffect` with
  manual cleanup for observable subscriptions
- **Typed ambient propagation** — migrate `ContextScope` from
  `List<(ContextBase, object?)>` to a typed store to eliminate boxing

---

## Appendix A: Architecture Comparison Summary

| Aspect | Reactor | Flux |
|---|---|---|
| **Core model** | Virtual tree + reconciliation (React) | Reactive subscriptions (build once) |
| **WinUI interop** | CsWinRT managed objects | C++/WinRT P/Invoke (DirectXaml) |
| **DSL processing** | Runtime (pure C# method calls) | Compile-time (MSBuild code generator) |
| **State changes** | Full component re-render + diff | Direct property subscription update |
| **Update cost** | O(subtree) per state change | O(subscribers) per state change |
| **Static value cost** | Same as dynamic (record allocation) | Zero (ImmutableState) |
| **Visual tree overhead** | Border/Grid wrappers per component | Directives are non-visual |
| **Theme mechanism** | XamlReader.Load (XAML parsing) | Ambient subscriptions (zero parsing) |
| **Animation** | 5 implicit transitions + layout animations | Mutation-site scoping (any property) |
| **Control coverage** | 94% of WinUI (81/86 controls) | ~15 primitives (build from scratch) |
| **Platform support** | WinUI 3 only | WinUI 3, UWP, Mock |
| **Build requirements** | NuGet package only | NuGet + code generator + native DLL |
| **Commanding** | Full system (Command, StandardCommand) | None |
| **Navigation** | Full system (type-safe, GPU transitions) | Basic routing (Route<T> + Switcher) |
| **Accessibility** | 16 modifiers + UIA tests | 3 AutomationProperties |
| **MVVM interop** | Rich (Observable, Collection, ICommand) | None |
| **Familiarity** | React mental model (millions of devs) | Novel (learning curve) |

## Appendix B: Flux File Reference

Key files for deeper investigation:

| Area | Path |
|---|---|
| DirectXaml API (C++) | `src/Flux.DirectXaml/src/FluxApi.h` |
| Property/Event definitions | `src/Flux.DirectXaml/src/FluxIds.def` |
| X-Macro dispatch | `src/Flux.DirectXaml/src/Dispatch.h` |
| Native implementation | `src/Flux.DirectXaml/src/FluxNative.cpp` |
| Type converters + event extractors | `src/Flux.DirectXaml/src/Converters.h` |
| C# P/Invoke declarations | `src/Flux/Native/DirectXaml.Win.cs` |
| Reactive state system | `src/Flux.State/` |
| Element base class | `src/Flux/Elements/Element.cs` |
| View (visual element) base | `src/Flux/Elements/View.cs`, `View.Win.cs` |
| Component system | `src/Flux/Elements/Component.cs`, `Component.Win.cs` |
| Code generator core | `src/Flux.CodeGenerator/` |
| DSL transformation | `src/Flux.CodeGenerator/Generators/DslTransformGeneration.cs` |
| Ambient system | `src/Flux/Attachment/Ambient.cs` |
| Animation system | `src/Flux/Animations/` |
| Benchmark harness | `src/Flux.GalleryApp/Benchmark/` |
| Architecture docs | `docs/architecture.md` |
| DirectXaml design doc | `docs/directxaml.md` |
