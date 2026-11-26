---
title: "ElementExtras"
---

Cross-cutting "extras" bucket for [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) (spec 047 §4.4). Holds the 14 rarely-set fields (attached properties, transitions, theme bindings, animation configs, resource overrides, context values) that used to sit inline on every element. Bucketing them into a lazy sub-record lets the common case (a leaf with none of these) pay one reference slot instead of 14. The fields are exposed on [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) via get/init shim properties that read from / write into this record, so call sites see no API change. Value-equality (default record) so equality helpers and `with`-writers behave exactly as before. Unlike [`ElementModifiers`](/microsoft-ui-reactor/main/reference/modifiers/ElementModifiers) nothing merges Extras, so there is no Merge.

**Construction-cost tradeoff (spec 047 §4.4 / §11.6):** the common no-extras leaf now saves a reference slot (the §4.4 win), but each extra-setting fluent call (`.Grid()`, `.Background(Theme…)`, `.Animate()`, `.Provide()`, …) clones this sub-record *in addition to* the [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) clone — so an element that sets several extras does N small `ElementExtras` allocations during construction where the inline layout did zero. Net win for the common case; watch the §11.6 byte-gate M2/M3 (callback/modifier-heavy leaves) when it is measured on the baseline box, as those are the construction-cost-sensitive scenarios.

```csharp
public record ElementExtras
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Modifier System](/microsoft-ui-reactor/main/how-it-works/modifier-system)

## Remarks [#remarks]

Spec 047 §4.4.

## Constructors [#constructors]

### `ElementExtras` [#elementextras_1]

```csharp
public ElementExtras()
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

Spec 010 — the DSL call site that produced the owning element.

Bucketed here rather than declared inline on [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) so that an element nobody stamped pays nothing. An inline `SourceLocation?` widens EVERY element instance by 24 bytes in every build and every configuration — measured at +24.0 B/op on the M12 control-model bench with source mapping switched off — which is a permanent retail tax for a debug-only feature. Living in the §4.4 extras bucket makes the cost proportional to the number of stamped elements instead of the number of elements.

It is NOT free, and the trade is worth stating precisely. As a nullable struct this field is stored inline, so it widens the bucket itself: measured at 152 B/instance with it and 24 B of that attributable to this field (an 8-byte path reference, a 4-byte line, the nullable flag, padded). Any element carrying a behavioral extra — Attached, ThemeBindings, animations, ResourceOverrides, ContextValues — pays that whether or not source mapping is on, and whether or not the consumer even has the generator. The M12 benchmark cannot see it, because M12 leaves are extras-free and allocate no bucket at all. Accepted because it is bounded and proportional to elements that already allocate this object, versus +24 B on EVERY element (measured) for the inline-on-the-record alternative. ElementExtrasAllocationTests pins both halves.

Read through `Element.CallSite`, which is the public shim; the bucket is an implementation detail.

```csharp
public SourceLocation? CallSite  // get, init
```

**Remarks**

Spec 047 §4.4 bucketing; spec 010 slot.

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
