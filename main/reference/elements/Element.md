---
title: "Element"
description: "A lightweight, immutable description of a UI node (the \"virtual DOM\"). Elements are cheap to create and diff — they never touch real controls directly."
---

```csharp
public abstract record Element
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Reconciliation](/microsoft-ui-reactor/main/how-it-works/reconciliation)

## Constructors [#constructors]

### `Element` [#element_1]

```csharp
protected Element()
```

## Properties [#properties]

### `AnimationConfig` [#animationconfig]

Compositor property animation configuration (.Animate() modifier). When set, the reconciler creates ImplicitAnimationCollection entries on the element's Visual for Opacity/Scale/Rotation/Offset/CenterPoint.

```csharp
public AnimationConfig? AnimationConfig  // get, init
```

### `Attached` [#attached]

Attached properties from parent containers (Grid.Row, Canvas.Left, etc.). Set via fluent extension methods: TextBlock("hi").Grid(row: 1, column: 2) Stored as a type-keyed dictionary so each provider defines its own data record.

```csharp
public IReadOnlyDictionary<Type, object>? Attached  // get, init
```

### `CallSite` [#callsite]

Spec 010 — the C# source location that created this element, or `null` when nothing stamped it.

This is a property of the ELEMENT, not of the current flag state. An element stamped while mapping was on keeps its location after the flag is turned off, and a hand-assigned value is honoured regardless of the flag; conversely an element built while mapping was off, or produced by a factory the provider does not reach, stays `null` forever. Elements are immutable, so this is fixed at construction.

NOTE ON THE NAME: spec 010 proposes `Element.Source`. That name is unusable — five existing element records already declare a positional `Source` member of an incompatible type (`ImageElement(string Source)`, `WebView2Element(Uri? Source)`, `MediaPlayerElementElement(string? Source)`, plus `AnimatedIconElement` and `ParallaxViewElement`), and a base property of a different type makes their positional parameters fail to bind (CS8866). `CallSite` is the collision-free equivalent.

The slot is present in every build configuration (the library ships as a Release-built NuGet package, so a `#if DEBUG` gate would make the property invisible to consumers). The *cost* is what is gated: nothing writes this slot unless a source-map provider is wired into the consuming compilation, and the generated interceptors check [`ReactorSourceMap.Enabled`](/microsoft-ui-reactor/main/reference/diagnostics/ReactorSourceMap#enabled) before stamping.

Tagging is NOT keyed on that runtime flag. A stamped element carries this value in its [`Element.Extensions`](#extensions) bucket, and `Reconciler.NeedsTag` already tags anything with a non-null bucket — so the back-pointer follows the stamp, not the flag. A hand-assigned `CallSite` is therefore tagged and readable even with the flag off (see the `HandStampedLeafIsTaggedWithFlagOff` selftest), and an element the provider never reached stays untagged and reports no location rather than a wrong one.

Populated per call site, so it is a compile-time constant for a given DSL invocation and survives fluent modifier chains automatically (record `with` copies it). It is deliberately NOT consulted by [`Element.ShallowEquals`](/microsoft-ui-reactor/main/reference/elements/Element) — see the reconciler's allow-list.

STORAGE: this is a shim over the spec 047 §4.4 [`Element.Extensions`](#extensions) bucket, exactly like [`Element.Attached`](#attached) and [`Element.ThemeBindings`](#themebindings). Declaring it inline on the record instead costs +24 bytes on every element ever allocated, measured, in every build — a permanent retail tax for a debug-only feature. The public shape is identical either way, so this is a storage decision, not an API one.

```csharp
public SourceLocation? CallSite  // get, init
```

### `ConnectedAnimationKey` [#connectedanimationkey]

Connected animation key for cross-container transitions. When set, the reconciler automatically captures a visual snapshot on unmount (via ConnectedAnimationService.PrepareToAnimate) and starts the animation on mount if a prepared animation with the same key exists. Set via fluent extension method: Border(child).ConnectedAnimation("hero")

```csharp
public string? ConnectedAnimationKey  // get, init
```

### `ContextValues` [#contextvalues]

Context values provided to this element's subtree via .Provide(). The reconciler pushes these onto the context scope when entering this element's subtree and pops them when leaving.

```csharp
public IReadOnlyDictionary<ContextBase, object?>? ContextValues  // get, init
```

### `ElementTransition` [#elementtransition]

Element enter/exit transition configuration (.Transition() modifier). When set, the reconciler animates mount (enter) and unmount (exit) with compositor animations, deferring removal until exit animation completes.

```csharp
public ElementTransition? ElementTransition  // get, init
```

### `Extensions` [#extensions]

Cross-cutting extras (attached properties, transitions, theme bindings, animation configs, resource overrides, context values) bucketed into a lazy sub-record (spec 047 §4.4). The common case — a leaf with none of these — leaves this slot null, so the 14 fields below cost one reference instead of 14 inline slots on every [`Element`](/microsoft-ui-reactor/main/reference/elements/Element). The setter is `internal` (PR #455 CR item #6): ordinary code uses the field shim properties (Attached, ThemeBindings, AnimationConfig, …) below and never observes this slot. Keeping the setter off the public surface removes an initializer-ordering footgun — a public `with { Extensions = X }` after a shim write would have silently discarded the shim. The engine and perf-critical internal paths may still set it directly.

```csharp
public ElementExtras? Extensions  // get
```

**Remarks**

Spec 047 §4.4.

### `ImplicitTransitions` [#implicittransitions]

Implicit transitions (opacity, scale, rotation, translation, background). Set via fluent extension methods: Rectangle().OpacityTransition() Applied by the reconciler after mount/update, so they are always present when property values are set via .Set() callbacks.

```csharp
public ImplicitTransitions? ImplicitTransitions  // get, init
```

### `InteractionStates` [#interactionstates]

Interaction states configuration (.InteractionStates() modifier). When set, the reconciler registers pointer event handlers that drive zero-reconcile visual state transitions (hover, pressed, focused).

```csharp
public InteractionStatesConfig? InteractionStates  // get, init
```

### `Key` [#key]

Optional key for stable identity across re-renders (like React's key prop). When set, the reconciler uses it to match elements across list reorderings.

```csharp
public string? Key  // get, init
```

### `KeyframeAnimations` [#keyframeanimations]

Keyframe animation definitions (.Keyframes() modifier). Trigger-based: plays when the trigger value changes between renders.

```csharp
public KeyframeEntry[]? KeyframeAnimations  // get, init
```

### `LayoutAnimation` [#layoutanimation]

Composition-layer layout animation configuration. When set, the reconciler attaches implicit animations to the element's Visual so that layout-driven position (and optionally size) changes animate smoothly. Set via fluent extension methods: Border(child).LayoutAnimation()

```csharp
public LayoutAnimationConfig? LayoutAnimation  // get, init
```

### `Margin` [#margin]

Outer margin shim that routes to [`Element.Modifiers`](#modifiers). Lets `el with { Margin = new Thickness(8) }` work directly on a record initializer (where extension methods are not visible). Identical semantics to `.Margin(...)`.

```csharp
public Thickness? Margin  // get, init
```

### `Modifiers` [#modifiers]

Layout modifiers (margin, padding, size, alignment, etc.) applied to this element. Set via fluent extension methods: TextBlock("hi").Margin(10).Width(200) Modifiers are stored inline so the concrete element type is preserved through chaining.

```csharp
public ElementModifiers? Modifiers  // get, init
```

### `Padding` [#padding]

Inner padding shim that routes to [`Element.Modifiers`](#modifiers). Lets `el with { Padding = new Thickness(8) }` work directly on a record initializer (where extension methods are not visible). Identical semantics to `.Padding(...)`.

```csharp
public Thickness? Padding  // get, init
```

### `ResourceOverrides` [#resourceoverrides]

Per-control resource overrides (lightweight styling). When set, the reconciler injects these into [`FrameworkElement.Resources`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement.resources) so that the control's VisualStateManager picks them up for hover/pressed/disabled states. Set via fluent extension: `Button("Go").Resources(r => r.Set("ButtonBackground", "#0078D4"))`

```csharp
public ResourceOverrides? ResourceOverrides  // get, init
```

### `ScrollAnimation` [#scrollanimation]

Scroll-linked expression animation configuration (.ScrollLinked() modifier). Expression animations run on the compositor, driven by ScrollViewer position.

```csharp
public ScrollAnimationConfig? ScrollAnimation  // get, init
```

### `StaggerConfig` [#staggerconfig]

Stagger configuration for container children (.Stagger() modifier). When set, child animations (enter, layout, property) have incrementing DelayTime = childIndex \* staggerDelay.

```csharp
public StaggerConfig? StaggerConfig  // get, init
```

### `ThemeBindings` [#themebindings]

Theme-resource bindings for brush properties (Background, Foreground, BorderBrush). When set, the reconciler resolves from WinUI theme resources instead of using local values. Set via fluent extension methods: TextBlock("hi").Background(Theme.Accent)

```csharp
public IReadOnlyDictionary<string, ThemeRef>? ThemeBindings  // get, init
```

### `ThemeTransitions` [#themetransitions]

Theme transitions (children, item container). Set via fluent extension methods: VStack(children).WithTransitions(...)

```csharp
public ThemeTransitions? ThemeTransitions  // get, init
```

## Operators [#operators]

### `op_Implicit` [#op_implicit]

Convenience: implicitly convert a string to a TextBlockElement. Allows writing: VStack("Hello", "World") instead of VStack(TextBlock("Hello"), TextBlock("World"))

```csharp
public static implicit operator Element(string text)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `text` | `string` |  |
