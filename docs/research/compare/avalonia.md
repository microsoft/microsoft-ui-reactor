# Avalonia — Framework Analysis

**Purpose:** Critical technical analysis for comparison against Microsoft UI frameworks (WinForms, WPF, WinUI 3, Microsoft.UI.Reactor).

**Version analyzed:** Avalonia 11.x–12.0 (2024-2026 era)

---

## Overview

Avalonia is a free, open-source, cross-platform XAML-based UI framework for .NET, first committed in December 2013 (originally named "Perspex") by Steven Kirk. It reached its first major stable release (11.0) in 2023 and the current stable is **Avalonia 12.0** (April 7, 2026). Avalonia is developed by AvaloniaUI OU (Estonian company, ~19 people) under the MIT license.

Avalonia's defining architectural choice is **self-rendering via Skia** — like Flutter, it draws every pixel using its own rendering engine rather than wrapping platform-native controls. This gives pixel-perfect consistency across Windows, macOS, Linux, iOS, Android, WebAssembly, and embedded Linux (7 platforms). The framework is spiritually a cross-platform successor to WPF: it shares XAML markup, dependency properties, data binding, MVVM, routed events, control templates, the logical/visual tree split, and two-pass layout (Measure/Arrange). However, it is a from-scratch reimplementation that modernizes many WPF patterns — notably replacing WPF's trigger-based styling with a CSS-like selector system.

**Adoption:** 30,300+ GitHub stars, 87M+ lifetime NuGet downloads, 2.1M unique projects in the last year, 400+ contributors. Notable production users include JetBrains (dotTrace, dotMemory on macOS/Linux), Autodesk, Unity, NASA, Devolutions ($3M sponsorship), Schneider Electric, GitHub (Git Credential Manager), Icons8 Lunacy, Wasabi Wallet, and 60+ showcased apps.

**Commercial model:** The core framework is MIT-licensed and free. Advanced developer tools (VS extension, DevTools Complete, premium controls, charts) are available through paid tiers: Plus ($17/month), Pro ($49/month), Enterprise (custom). Avalonia XPF (WPF binary compatibility layer) is EUR 29,500 perpetual per application.

---

## 1. Declarative Model & Syntax

**AXAML:** Avalonia uses `.axaml` files (Avalonia XAML), syntactically near-identical to WPF XAML with the same object-element, property-attribute, and content-property conventions. Code-behind files pair with AXAML just like WPF.

**Key improvements over WPF XAML:**

| Feature | WPF | Avalonia |
|---|---|---|
| Compiled bindings | No (`x:Bind` in WinUI 3 only) | Yes — `x:CompileBindings="True"` resolves at build time, catches typos as errors, no reflection |
| Grid shorthand | Verbose `<RowDefinition>` elements | `ColumnDefinitions="Auto,*,200"` inline string |
| Spacing | Manual margins | `StackPanel.Spacing="8"` built-in |
| DataTemplates | Stored in Resources | `DataTemplates` collection; supports interface/derived-type matching |
| Platform conditionals | None | `{OnPlatform}` and `{OnFormFactor}` markup extensions |
| Styling | Resource dictionary + triggers | CSS-like selectors + pseudo-classes (see Styling section) |

**Compiled bindings** are the most significant syntax improvement. They produce compiler errors for invalid property paths, eliminate reflection at runtime, and are required for NativeAOT deployment. Both `{CompiledBinding}` and `{ReflectionBinding}` can be used explicitly.

**C# declarative alternative:** The `Avalonia.Markup.Declarative` package provides extension methods for building UI in pure C# instead of AXAML. AXAML and C# markup can be mixed in the same project, though AXAML remains the primary path.

**Strengths:**
- Compiled bindings catch binding errors at build time — a genuine advantage over WPF
- Grid shorthand and `Spacing` reduce boilerplate for common layouts
- `{OnPlatform}` and `{OnFormFactor}` enable cross-platform adaptive UI in markup
- WPF developers read AXAML fluently — near-zero learning cost for syntax
- Full C# alternative exists for teams that prefer code over markup

**Weaknesses:**
- Still XAML — angle brackets, explicit closing tags, namespace declarations, property-element syntax for complex values. The same verbosity as WPF, just with better defaults
- No trailing closures (Swift/Kotlin), no result builders, no JSX — the gap to SwiftUI/Compose/React's declarative syntax remains large
- The C# declarative alternative is community-maintained and not the primary supported path
- XML-based markup makes UI code visually heavy compared to code-first declarative frameworks

**Sources:** [Avalonia XAML Docs](https://docs.avaloniaui.net/docs/fundamentals/avalonia-xaml), [Markup Extensions](https://docs.avaloniaui.net/docs/concepts/markupextensions), [Avalonia.Markup.Declarative](https://github.com/AvaloniaUI/Avalonia.Markup.Declarative)

---

## 2. Component Architecture

**Three authoring models:**

1. **UserControl** — Composes existing controls in a fixed AXAML layout. The standard model for application views/pages, each paired with a ViewModel
2. **TemplatedControl** — "Lookless" controls whose visual appearance is defined in a separate `ControlTheme`. All built-in controls (Button, TextBox, ListBox) are TemplatedControls. This is WPF's `Control` + `ControlTemplate` pattern, modernized
3. **Basic Control** — Inherits directly from `Control` and self-renders by overriding `Visual.Render()`. Used for low-level primitives like `TextBlock` and `Image`

**Content model:** `ContentControl` (single `Content` property with `ContentPresenter`), `ItemsControl` (`ItemsSource` + `ItemTemplate`), `HeaderedContentControl` (`Header` + `Content`), and decorators (`Border`, `Viewbox`). Template parts use the `PART_` naming convention, accessed via `OnApplyTemplate()`.

**Strengths:**
- The TemplatedControl + ControlTheme separation is architecturally clean — cleaner than WPF's conflation of implicit styles with template definition
- Full control template power — any built-in control's visual tree can be completely redefined
- WPF's proven component patterns (MVVM, template parts, content presenters) transfer directly
- Custom controls can target all platforms from a single implementation

**Weaknesses:**
- Class-based component model — inheriting from `Control` subclasses is a generation behind function-as-component (React, Compose) or value-type-as-component (SwiftUI)
- No hooks, no composable functions — lifecycle is managed via method overrides (`OnApplyTemplate`, `OnAttachedToVisualTree`, `OnPropertyChanged`)
- `TemplateBinding` is OneWay only (unlike WPF) — two-way requires verbose `RelativeSource={RelativeSource TemplatedParent}` bindings
- Building reusable library controls requires understanding ControlTheme authoring, which is more complex than React/Compose component authoring

**Sources:** [Types of Control](https://docs.avaloniaui.net/docs/guides/custom-controls/types-of-control), [Templated Controls](https://docs.avaloniaui.net/docs/custom-controls/templated-controls)

---

## 3. State Management & Reactivity

**Property system:** Three types — `StyledProperty` (full styling, animation, inheritance, binding support), `DirectProperty` (lightweight, CLR-backed, no styling/animation), and `AttachedProperty` (registered to a different owner, e.g., `Grid.Row`). Value precedence is well-defined: Animation > Local > Style trigger > Template > Style > Inherited > Default.

**Data binding:** Standard `INotifyPropertyChanged` (INPC) is fully supported. Two-way binding for input controls, one-way for display. Compiled bindings (no reflection) are the recommended default and enabled by default in Avalonia 12. `FallbackValue` and `TargetNullValue` are supported.

**Observable integration:** Avalonia has **first-class `IObservable<T>` support** built into the binding engine. The stream binding operator `^` subscribes to an observable directly in XAML:
```xml
<TextBlock Text="{Binding CurrentTime^}" />
```
Subscription lifecycle is automatic: subscribes when the control enters the visual tree, unsubscribes when it leaves. This integrates with System.Reactive (Rx.NET) for throttled search, debouncing, and real-time data feeds.

**MVVM framework support:**
- **ReactiveUI:** First-class integration. `ReactiveCommand`, `WhenAnyValue()` for property observation, built-in routing/navigation, DynamicData for reactive collections
- **CommunityToolkit.Mvvm:** Fully supported. `[ObservableProperty]`, `[RelayCommand]` source generators. Simpler learning curve. Can be combined with ReactiveUI

**Strengths:**
- `IObservable<T>` stream binding is unique among XAML frameworks — enables reactive data flows directly in markup
- Compiled bindings eliminate binding errors and reflection overhead
- ReactiveUI provides the most powerful reactive MVVM story of any .NET framework
- Both ReactiveUI and CommunityToolkit.Mvvm work out of the box — teams choose based on complexity needs
- `DynamicData` reactive collections provide declarative sort/filter/transform pipelines

**Weaknesses:**
- No automatic fine-grained state tracking (unlike SwiftUI's `@Observable` or Compose's Snapshot system) — all observation requires manual `INotifyPropertyChanged` or reactive wrappers
- Same coarse-grained invalidation as WPF — changing any property on an observed object notifies the binding engine, which must then evaluate whether the specific bound property changed
- ReactiveUI's learning curve is steep — `WhenAnyValue`, `ObservableAsPropertyHelper`, Rx operators are powerful but non-obvious
- No built-in selector pattern for subscribing to slices of state

**Sources:** [Observable Binding](https://docs.avaloniaui.net/docs/data-binding/how-to-bind-to-an-observable), [ReactiveUI Docs](https://docs.avaloniaui.net/docs/concepts/reactiveui/), [Architecture](https://docs.avaloniaui.net/docs/fundamentals/architecture)

---

## 4. Rendering & Performance

**Retained-mode rendering:** Controls declare visual structure through templates; the framework builds a scene graph (lightweight tree of drawing instructions) rendered each frame. Damage invalidation ensures only changed screen regions are redrawn.

**Rendering backends:**

| Platform | GPU Backend | Fallback |
|---|---|---|
| Windows | Direct3D 11 | Software (Skia CPU) |
| macOS/iOS | Metal | Software (Skia CPU) |
| Linux | Vulkan (11.1+), OpenGL | Software (Skia CPU) |
| Android | OpenGL ES, Vulkan | Software |
| Web | WebGL | — |

**Primary engine:** SkiaSharp 3.0 (Google's Skia 2D graphics library — the same engine powering Chrome, Android, and Flutter).

**Impeller partnership:** Avalonia has partnered with Google's Flutter team to bring **Impeller** (Flutter's next-gen GPU-first renderer) to .NET. Benefits include pre-compiled shader pipelines (no shader jank), 12x less power draw than Vello, and dramatically improved complex clipping (11ms vs 450ms on Skia). Status: **experimental** as of April 2026.

**Avalonia 12 benchmarks:**
- 350K visual elements: up to **1,867% FPS improvement** (~20x over previous versions)
- Android startup with NativeAOT: **1,960ms → 460ms** (4x improvement)
- Android scrolling: **42 → 120 FPS**
- Android idle CPU: **20x reduction** (0.20% → <0.01%)
- vs MAUI: 3-6x faster rendering, half the memory on Windows

**Strengths:**
- GPU-accelerated on every platform via Skia with platform-native backends (Metal, Vulkan, D3D11)
- NativeAOT support eliminates JIT startup overhead — competitive with native app startup
- Self-rendering provides pixel-perfect cross-platform consistency (same visual output everywhere)
- The Impeller partnership positions Avalonia for a significant rendering leap
- Avalonia 12's compositor rework delivered dramatic improvements for complex scenes

**Weaknesses:**
- Self-rendering means apps never look truly platform-native — they look like Avalonia, not macOS/Windows/Linux (same trade-off as Flutter)
- Impeller is experimental — production apps still run on Skia
- Higher memory baseline than native frameworks due to the Skia engine
- No concurrent rendering or priority-based updates (unlike React Fiber)
- Desktop benchmarks vs WPF/WinUI 3 need nuance — WPF's DirectX rendering is mature and well-optimized for Windows

**Sources:** [Avalonia 12 Blog](https://avaloniaui.net/blog/avalonia-12), [Avalonia vs MAUI](https://avaloniaui.net/maui-compare), [Impeller Partnership](https://avaloniaui.net/blog/avalonia-partners-with-google-s-flutter-t-eam-to-bring-impeller-rendering-to-net), [Performance Guide](https://docs.avaloniaui.net/docs/guides/development-guides/improving-performance)

---

## 5. Layout System

**Two-pass model:** Identical to WPF. Parent calls `child.Measure(availableSize)`, child sets `DesiredSize`. Parent calls `child.Arrange(rect)`, child receives final position and size. Layout invalidation propagates up the tree; the framework coalesces invalidations so only one layout pass runs per frame.

**Built-in panels:**

| Panel | Behavior |
|---|---|
| StackPanel | Single-line stacking with `Spacing` property (WPF improvement) |
| WrapPanel | Line-wrapping flow layout |
| Grid | Row/column with `*`/Auto/pixel sizing; inline shorthand `ColumnDefinitions="Auto,*,200"` |
| DockPanel | Edge docking with `LastChildFill` |
| Canvas | Absolute positioning via attached properties |
| UniformGrid | Equal-size cells |
| RelativePanel | Constraint-based positioning relative to siblings or panel edges (from UWP) |
| Panel | Base class; children overlap (stacking without positioning) |

**Custom layout:** Subclass `Panel` and override `MeasureOverride(Size)` and `ArrangeOverride(Size)`. Same pattern as WPF — full control over child measurement and positioning.

**Responsive design:** `{OnPlatform}` for platform-specific values, `{OnFormFactor}` for Desktop vs Mobile, `RelativePanel` for constraint-based responsive layout, `Grid` with proportional sizing for fluid layouts, `SplitView` for collapsible pane patterns. Avalonia 12 added mobile page types (`ContentPage`, `DrawerPage`, `CarouselPage`, `TabbedPage`).

**Strengths:**
- WPF's two-pass layout system is architecturally proven and powerful — Avalonia inherits this wholesale
- `Grid` shorthand and `StackPanel.Spacing` are ergonomic improvements over WPF
- `RelativePanel` (from UWP) adds constraint-based layout not available in WPF
- Custom panel authoring is straightforward (override two methods)
- Cross-platform layout — same code on all 7 platforms

**Weaknesses:**
- No CSS Grid or CSS Flexbox equivalent — layout concepts don't transfer from web development
- No single-line responsive layout primitive like SwiftUI's `ViewThatFits` or Compose's adaptive Scenes
- No `AdaptiveTrigger` equivalent (WinUI 3 has this for responsive breakpoints)
- `{OnFormFactor}` is binary (Desktop/Mobile) — no fine-grained breakpoint system

**Sources:** [Layout Docs](https://docs.avaloniaui.net/docs/basics/user-interface/building-layouts/), [Panels Overview](https://docs.avaloniaui.net/docs/basics/user-interface/building-layouts/panels-overview), [Custom Panel](https://docs.avaloniaui.net/docs/custom-controls/custom-panel)

---

## 6. Styling & Theming

**CSS-like selector system:** Avalonia replaces WPF's resource-dictionary styling with selector-based matching. Styles use a CSS-inspired syntax with type selectors, name selectors (`#name`), class selectors (`.class`), pseudo-class selectors (`:pointerover`, `:pressed`, `:focus`, `:disabled`, `:checked`), child/descendant combinators (`>`/space), negation (`:not()`), nth-child formulas, and template selectors (`/template/`). Controls accept multiple style classes simultaneously, toggleable at runtime.

**ControlTheme vs Style:** Avalonia separates concerns that WPF conflates:
- **ControlTheme** — Defines the `ControlTemplate` and base visual appearance. Lives in `Resources`, keyed by type. Does **not** cascade. Only one ControlTheme applies at a time
- **Style** — Uses CSS-like selectors. Lives in `Styles` collection. Cascades and composes. Multiple styles can apply simultaneously

**WPF triggers → Pseudo-classes:** `IsMouseOver` → `:pointerover`, `IsPressed` → `:pressed`, `IsEnabled=False` → `:disabled`, `IsChecked` → `:checked`, `IsFocused` → `:focus`. No separate WPF-style trigger mechanism needed.

**Built-in themes:**
- **FluentTheme** — Microsoft Fluent Design-inspired. Dark/Light variants, `DensityStyle` (standard/compact), `ColorPaletteResources` for palette customization
- **SimpleTheme** — Minimal, lightweight. Clean foundation for custom styling. Best for embedded devices

**Community themes:** Material.Avalonia (Google Material Design), Semi.Avalonia (Semi Design), FluentAvalonia (extended Fluent with Windows 11 controls like NavigationView).

**Theme variants:** `RequestedThemeVariant` accepts `Default` (follows OS), `Light`, or `Dark`. Can be set at Application, Window, or control level. `ThemeVariantScope` enables per-subtree overrides. Runtime switching from code: `Application.Current.RequestedThemeVariant = ThemeVariant.Dark`.

**Strengths:**
- CSS-like selectors are more expressive than WPF for common styling patterns — descendant selectors, nth-child, negation, class combinators
- ControlTheme separation from Style is architecturally cleaner than WPF's implicit style conflation
- Full ControlTemplate power retained — any control's visual tree can be completely redefined (unlike SwiftUI/Compose which lack this)
- Multiple style classes on a single control enable component-library-style composition
- Theme variant with per-subtree override is well-designed
- Community themes provide genuine choice

**Weaknesses:**
- CSS-like system requires learning a new mental model — WPF developers must unlearn triggers and implicit style patterns
- `TemplateBinding` is OneWay only (WPF supports TwoWay) — requires verbose `RelativeSource` binding for two-way
- `ColorPaletteResources` only supports runtime binding for the `Accent` color — other palette properties are read once at startup
- No visual theme editor (WPF has Blend)
- ControlTheme authoring for custom controls is complex — more ceremony than React/Compose component styling

**Sources:** [Styles Docs](https://docs.avaloniaui.net/docs/basics/user-interface/styling/styles), [Selector Syntax](https://docs.avaloniaui.net/docs/styling/style-selector-syntax), [Control Themes](https://docs.avaloniaui.net/docs/basics/user-interface/styling/control-themes), [Fluent Theme](https://docs.avaloniaui.net/docs/basics/user-interface/styling/themes/fluent), [Theme Variants](https://docs.avaloniaui.net/docs/guides/styles-and-resources/how-to-use-theme-variants)

---

## 7. Navigation

Avalonia does not have a single unified navigation framework. Multiple approaches coexist:

**NavigationPage (Avalonia 12+, built-in):** Stack-based page navigation with `INavigation` interface. `PushAsync(page)`, `PopAsync()`, `PopToRootAsync()`, `ReplaceAsync(page)`. Modal navigation via `PushModalAsync`/`PopModalAsync`. Built-in `NavigationBar` with back button and title. Configurable page transitions (`PageSlide`, `CrossFade`). Edge-swipe gesture navigation. Additional page types: `ContentPage`, `DrawerPage`, `CarouselPage`, `TabbedPage`. Primarily designed for mobile navigation patterns.

**ReactiveUI routing:** `IScreen` + `RoutingState` + `RoutedViewHost`. ViewModel-first navigation where `Router.Navigate.Execute(viewModel)` pushes view models onto a stack and `RoutedViewHost` resolves the corresponding view. String-based `UrlPathSegment` identifiers. Supports nested navigation.

**RouteNav.Avalonia (third-party):** URI-based routing. `Navigation.PushAsync(uri, NavigationTarget)`. Layout types for stacks, tabs, sidebars. DI-integrated page instantiation.

**ViewLocator pattern:** Convention-based View-to-ViewModel mapping by namespace/name. Not routing per se, but automatic view resolution for data binding.

**Strengths:**
- NavigationPage provides a genuine first-party mobile navigation system with transitions and gestures
- ReactiveUI routing is mature and well-integrated for ViewModel-first navigation
- Multiple approaches give teams choice based on app complexity

**Weaknesses:**
- **No unified navigation story** — three competing approaches (NavigationPage, ReactiveUI routing, third-party) with different philosophies
- NavigationPage is new (Avalonia 12) and lacks production battle-testing
- No type-safe routes — ReactiveUI uses string `UrlPathSegment`, NavigationPage uses page instances. Nothing comparable to Compose Navigation 3's typed data class routes
- No built-in deep linking in NavigationPage
- No adaptive multi-pane layout that transitions between phone/tablet/desktop (Compose's Scenes API)

**Sources:** [NavigationPage Docs](https://docs.avaloniaui.net/controls/navigation/navigationpage), [ReactiveUI Routing](https://www.reactiveui.net/docs/handbook/routing.html), [RouteNav.Avalonia](https://github.com/profix898/RouteNav.Avalonia)

---

## 8. Animation

**Three animation systems:**

**1. Keyframe animations (XAML-declarative):** Defined within styles using `Animation` and `KeyFrame` elements. Triggered by style selector changes (pseudo-class transitions). Timing via `Cue` (percentage: `Cue="50%"`) or `KeyTime` (absolute). Configuration: `Duration`, `Delay`, `IterationCount` (including `INFINITE`), `PlaybackDirection` (Normal/Reverse/Alternate/AlternateReverse), `FillMode`, `Easing`. 30+ built-in easing functions. Programmatic triggering via `animation.RunAsync(control, token)`.

**2. Control transitions (CSS-like):** Automatically animate property changes without explicit keyframes. Defined via `Transitions` collection on any `Control`. Available types: `DoubleTransition`, `BrushTransition`, `ColorTransition`, `IntegerTransition`, `PointTransition`, `ThicknessTransition`, `TransformOperationsTransition`, `CornerRadiusTransition`, `SizeTransition`, `BoxShadowsTransition`, `VectorTransition`. CSS-like render transforms: `translate()`, `scale()`, `rotate()`, `skew()`.

**3. Composition animations (render-thread):** Run on the render thread, independent of the UI thread. Operate through `CompositionVisual`. Explicit animations via `compositionVisual.StartAnimation("Offset", animation)`. Implicit animations that auto-trigger on property changes via `ImplicitAnimationCollection`. Reduced memory footprint and smoother updates.

**Strengths:**
- Three-layer system covers simple (transitions), intermediate (keyframes), and advanced (composition) animation needs
- CSS-like transitions are intuitive for web developers and far more ergonomic than WPF's Storyboard boilerplate
- Composition animations on the render thread prevent UI thread blocking — genuine architectural advantage
- 30+ easing functions, looping, alternate playback, fill modes
- Avalonia 12's compositor rework delivered 1,867% FPS improvement in complex animated scenes

**Weaknesses:**
- No equivalent to SwiftUI's `withAnimation { }` one-liner — animations require explicit transition/keyframe/composition setup
- No visual animation editor (WPF has Blend)
- Composition animations are code-only, no XAML support
- No built-in shared-element/hero transitions (SwiftUI's `matchedGeometryEffect`, Flutter's Hero)
- Page transitions exist (NavigationPage) but custom inter-page animations are limited

**Rating: B+** — Well-designed three-layer system with render-thread composition animations. Behind SwiftUI's ergonomics and WinUI 3's Composition API depth, but comprehensive.

**Sources:** [Keyframe Animations](https://docs.avaloniaui.net/docs/guides/graphics-and-animation/keyframe-animations), [Transitions](https://docs.avaloniaui.net/docs/guides/graphics-and-animation/transitions), [Composition Animations](https://dev.to/adirh3/using-the-new-composition-renderer-in-avalonia-11-1k0p)

---

## 9. Accessibility

**Cross-platform accessibility:**

| Platform | Backend | Screen Reader |
|---|---|---|
| Windows | UI Automation (UIA) | Narrator, NVDA, JAWS |
| macOS | NSAccessibility | VoiceOver |
| Linux | AT-SPI2 | Orca |
| iOS | UIAccessibility | VoiceOver |
| Android | AccessibilityNodeInfo | TalkBack |

**Automation peers:** `AutomationPeer` base class exposes elements to platform accessibility services. Built-in controls provide automation peers automatically. Avalonia 12 added automation support for validation errors and landmarks (e.g., HamburgerMenu).

**Linux accessibility (AT-SPI2):** Avalonia 12 is the **first .NET UI framework to ship a native Linux accessibility backend**. It directly exposes applications to AT-SPI2 (the standard Linux accessibility infrastructure) following the same approach as GTK4, rather than using the deprecated ATK wrapper.

**Additional features:** Full keyboard navigation, focus adorner support (`:focus-visible` pseudo-class), IME support for CJK languages, `TextInputOptions` for assistive input methods, XY Focus for spatial navigation (gamepad/D-pad).

**Strengths:**
- Cross-platform accessibility from a single codebase — the same app is accessible on Windows (UIA), macOS (VoiceOver), Linux (Orca), iOS, and Android
- Linux AT-SPI2 is a first for .NET — no other .NET framework provides native Linux screen reader support
- Built-in controls have correct automation peers by default
- Avalonia 12 added validation error and landmark accessibility

**Weaknesses:**
- Windows UIA is the most mature; macOS and Linux backends have less community documentation of edge cases
- Linux AT-SPI2 is brand new (April 2026) — limited production validation
- Custom control accessibility requires manual automation peer implementation (same as WPF)
- No accessibility linting or build-time checks (unlike `eslint-plugin-jsx-a11y` for React)
- Self-rendering means the platform's native accessibility infrastructure must be bridged, not used directly

**Rating: B+** — Strong cross-platform coverage with a landmark Linux achievement. Windows is mature; other platforms are newer.

**Sources:** [Avalonia 12 Blog](https://avaloniaui.net/blog/avalonia-12), [Avalonia 11.1 Blog](https://avaloniaui.net/blog/avalonia-11-1-a-quantum-leap-in-cross-platform-ui-development), [Linux A11y Issue #14275](https://github.com/AvaloniaUI/Avalonia/issues/14275)

---

## 10. Input & Gestures

**Unified pointer system:** All input (mouse, touch, pen) flows through a single pointer event system: `PointerPressed`, `PointerMoved`, `PointerReleased`, `PointerEntered`, `PointerExited`, `PointerWheelChanged`. Device type (`Mouse`/`Touch`/`Pen`) detected via `PointerPoint.Pointer.Type`. Pen properties include pressure, tilt, twist, eraser, and barrel button.

**Gesture recognizers:** `ScrollGestureRecognizer`, `PinchGestureRecognizer`, `PullGestureRecognizer`, `SwipeGestureRecognizer`. Multiple recognizers can be attached to a control; only one activates at a time. Custom recognizers subclass `GestureRecognizer` and override pointer event methods.

**Simple gesture events:** `Tapped`, `DoubleTapped`, `RightTapped`, `Holding` on all input elements.

**Keyboard and focus:** `Focusable`, `IsTabStop`, `TabIndex` properties. `GotFocus`/`LostFocus` events with `NavigationMethod`. `:focus`, `:focus-within`, `:focus-visible` pseudo-classes. Tab navigation modes: `Cycle`, `Continue`, `Contained`, `Once`, `None`, `Local`. `KeyBindings` for keyboard shortcuts.

**XY Focus:** Spatial/directional navigation for gamepad/remote/D-pad scenarios. Strategies include Projection, NavigationDirectionDistance, RectilinearDistance. Explicit targets via `XYFocus.Up`/`Down`/`Left`/`Right`.

**Routed events:** Both tunneling and bubbling routing strategies, with `handledEventsToo` support.

**Strengths:**
- Unified pointer system handles mouse, touch, and pen from a single event model — genuinely cross-platform
- Pen support with pressure/tilt is comprehensive (important for creative/drawing apps)
- XY Focus for gamepad/remote scenarios is a niche but important capability (shared with WinUI 3)
- Gesture recognizers cover the main mobile patterns (scroll, pinch, pull-to-refresh, swipe)
- Routed events with tunneling/bubbling provide the same flexibility as WPF

**Weaknesses:**
- No gesture arena concept (unlike Flutter) — gesture disambiguation is simpler but less sophisticated
- Gesture recognizer set is smaller than Flutter's (no rotation, no long-press drag, no custom force press)
- No built-in drag-and-drop gesture recognizer (must use lower-level pointer events)
- Cross-platform differences exist: Wayland is in private preview, WASM has cursor/pen limitations

**Sources:** [Pointer Docs](https://docs.avaloniaui.net/docs/input-interaction/pointer), [Gestures](https://docs.avaloniaui.net/docs/input-interaction/gestures), [Focus](https://docs.avaloniaui.net/docs/input-interaction/focus)

---

## 11. Developer Experience

**Hot reload:** Avalonia does **not have official built-in hot reload**. The team has stated it would require approximately a year of senior developer time and, if implemented, is planned as a paid feature. Community solutions exist: **HotAvalonia** (IDE-agnostic XAML hot reload, supports all desktop platforms + Android) and **Live.Avalonia** (rebuild-and-inject via `dotnet watch`).

**IDE support:**
- **Visual Studio:** Avalonia extension provides XAML previewer, IntelliSense, Go to Definition, auto namespace imports, event handler generation. Essentials version free for non-commercial; Complete version requires Plus ($17/month)
- **VS Code:** Free, fully featured extension with XAML previewing and IntelliSense
- **JetBrains Rider:** AvaloniaRider plugin adds live XAML preview. ReSharper/Rider have built-in Avalonia improvements

**DevTools:** F12 activates in-app DevTools:
- **Essentials (free):** Logical/Visual Tree Inspector, live property editing, layout inspection (margin/padding/border), style viewer with pseudo-class snapshots, visual overlays
- **Complete (paid):** 3D Layout View, event inspection, performance profiling, mobile debugging, remote connections

**Strengths:**
- DevTools are genuinely useful — Logical/Visual Tree Inspector with live property editing rivals WPF's Live Visual Tree
- VS Code extension is free and full-featured — lower barrier than VS extension
- Rider integration via JetBrains is strong (JetBrains uses Avalonia in production)
- Documentation has improved substantially with v11 and v12 (progressive structure, multiple tutorials)
- WPF developers transition smoothly — familiar concepts reduce learning curve dramatically

**Weaknesses:**
- **No built-in hot reload is a significant gap** — Flutter, React, and Compose all have this. Community HotAvalonia works but is not first-party
- Advanced IDE tooling requires paid subscription ($17/month) — creates friction for evaluation
- No visual designer for AXAML (WPF has Blend, WinForms has drag-and-drop designer)
- Documentation is improving but not at React/Flutter level — advanced scenarios have gaps
- Smaller community than WPF/React/Flutter means fewer Stack Overflow answers and tutorials

**Sources:** [Avalonia DevTools](https://avaloniaui.net/devtools), [IDE Support](https://docs.avaloniaui.net/docs/getting-started/ide-support), [Avalonia Pricing](https://avaloniaui.net/pricing), [HotAvalonia](https://github.com/Kira-NT/HotAvalonia)

---

## 12. Platform Reach & Ecosystem

**Seven platforms:** Windows, macOS, Linux, iOS, Android, WebAssembly, Embedded Linux (Raspberry Pi framebuffer).

**Maturity by platform:**
- **Desktop (Windows/macOS/Linux):** Most mature. Production-proven at JetBrains, Autodesk, NASA, Schneider Electric. Linux is a particular strength — no other .NET UI framework supports Linux well (MAUI has no Linux support at all)
- **Mobile (iOS/Android):** Dramatically improved in Avalonia 12 (4x Android startup improvement, 120 FPS scrolling). New page-based navigation with drawer, tabs, bottom sheets, gestures. Still newer and less battle-tested than desktop
- **WebAssembly:** Runs client-side via .NET 8+ WASM. Functional but less mature than desktop. Same canvas-rendering trade-offs as Flutter web (no SEO, large download)
- **Embedded Linux:** Composition renderer optimized for low-powered devices. Raspberry Pi is Tier 1 supported

**Ecosystem:**
- 70+ free, open-source built-in controls
- Avalonia Pro: 6 premium controls (media player, virtual keyboard, rich text editor, markdown viewer) + charts (coming soon)
- Third-party: Actipro Avalonia Controls (commercial suite), FluentAvalonia (extended Fluent), Semi.Avalonia (Semi Design), DynamicData (reactive collections)
- Third-party ecosystem is **smaller than WPF, WinForms, React, or Flutter** — some scenarios require custom development

**Strengths:**
- **Broadest platform reach of any .NET UI framework** — 7 platforms vs MAUI's 4 (no Linux, no embedded)
- Linux desktop support is unique in .NET — fills a genuine gap
- Embedded Linux support enables IoT/kiosk scenarios no other .NET framework addresses
- Active development trajectory — Avalonia 12 shipped major mobile improvements
- Single codebase for all platforms with platform conditionals where needed

**Weaknesses:**
- Desktop is the strength; mobile/web are catching up but less mature
- WebAssembly has the same limitations as Flutter web (canvas rendering, no SEO, large payload)
- Third-party control ecosystem is smaller than all other frameworks analyzed
- Some advanced tooling is behind a paywall (paid tiers)
- Community is growing but smaller than React (85M npm downloads/week) or Flutter (173K GitHub stars)

**Sources:** [Supported Platforms](https://docs.avaloniaui.net/docs/supported-platforms), [Avalonia 12 Blog](https://avaloniaui.net/blog/avalonia-12), [App Showcase](https://avaloniaui.net/showcase)

---

## 13. Testing

**Headless platform:** Avalonia's primary testing story. Runs the full control tree, layout, styling, and data binding with in-memory implementations replacing windowing and rendering backends. Tests execute without a display, fast enough for CI/CD.

**Framework integration:**
- **XUnit:** `[AvaloniaFact]` / `[AvaloniaTheory]` attributes
- **NUnit:** `[AvaloniaTest]` attribute

**Input simulation:** Extension methods on Window — `KeyPress()`, `KeyRelease()`, `KeyTextInput()`, `MouseDown()`, `MouseUp()`, `MouseMove()`, `MouseWheel()`, `DragDrop()`.

**Visual regression testing:** Enable the Skia renderer in headless mode, call `CaptureRenderedFrame()` to get a `WriteableBitmap` for pixel comparison against baselines. Works in CI without a display.

**Appium UI testing:** For end-to-end testing, Avalonia supports Appium which drives the application through its accessibility tree. Launches the compiled app in a real window and interacts like a user. Avalonia uses this internally. Linux limitation: no stable Appium desktop driver.

**Strengths:**
- Headless testing is well-designed — runs the real framework (layout, binding, styling) without a window
- Input simulation covers keyboard, mouse, and drag-and-drop
- Visual regression testing via Skia capture is built-in (no third-party tool required)
- Tests are fast (no device/emulator needed) and CI-friendly
- First-party support with dedicated test attributes

**Weaknesses:**
- No semantics-based testing philosophy (unlike Compose's semantic tree queries or React Testing Library's role-based queries)
- Test queries are positional/type-based, not accessibility-role-based — doesn't encourage accessible code the way Compose/RTL do
- No component-level isolated testing (everything requires a full headless Window)
- Appium not supported on Linux desktop
- Visual regression tests can be fragile across platform/Skia version differences (same issue as Flutter's goldens)

**Rating: B** — Solid headless testing with visual regression support. Missing the semantics-based philosophy that makes Compose/React testing a force multiplier for accessibility.

**Sources:** [Headless Testing](https://docs.avaloniaui.net/docs/concepts/headless/), [Appium Testing](https://docs.avaloniaui.net/docs/testing/ui-testing-with-appium)

---

## 14. Error Handling & Resilience

**Layered exception strategy:**

| Layer | Mechanism | Recovery? |
|---|---|---|
| UI Thread | `Dispatcher.UIThread.UnhandledException` | Yes — set `e.Handled = true` |
| UI Thread Filter | `Dispatcher.UIThread.UnhandledExceptionFilter` | Can prevent specific exceptions from propagating |
| Background Tasks | `TaskScheduler.UnobservedTaskException` | Call `e.SetObserved()` |
| Non-Task Threads | `AppDomain.UnhandledException` | Informational only |
| Global | try-catch in `Program.Main` | Logging/cleanup only |

**No error boundary concept.** Unlike React or Reactor, Avalonia has no component-level error boundary that isolates failures to a subtree. An uncaught exception during rendering crashes the application. The closest equivalent is try-catch in view models or command handlers.

**ReactiveUI integration:** `RxApp.DefaultExceptionHandler` must be set for `ReactiveCommand` error handling.

**Rating: C+** — Same level as Flutter: layered exception handling mechanisms exist, but no component-level error isolation. Better than SwiftUI/Compose (which crash on any view error) due to the dispatcher-level handling options.

**Sources:** [Unhandled Exceptions](https://docs.avaloniaui.net/docs/concepts/unhandledexceptions)

---

## 15. Data Loading & Async

**Architecture:** Standard .NET `async`/`await` with UI thread marshaling via `Dispatcher.UIThread.InvokeAsync()`. No framework-specific async primitives — relies on MVVM patterns.

**ReactiveUI async:** `ReactiveCommand.CreateFromTask()` handles busy state (`IsExecuting`), error routing, and cancellation automatically. `WhenAnyValue()` combined with `SelectMany()` enables reactive data loading pipelines. `ObservableAsPropertyHelper<T>` bridges async results to bindable properties.

**CommunityToolkit async:** `[RelayCommand]` attribute generates async commands from async methods. `IAsyncRelayCommand.IsRunning` tracks execution state. Supports `CancellationToken`.

**DynamicData:** `SourceCache<T, TKey>` and `SourceList<T>` provide reactive collection transformations — `.Filter()`, `.Sort()`, `.Transform()`, `.Bind()` produce incremental change sets automatically, avoiding full collection rebuilds.

**Strengths:**
- .NET's `async`/`await` is mature and well-understood
- ReactiveUI's `ReactiveCommand` provides the cleanest async command lifecycle in any MVVM framework — automatic busy tracking, error routing, cancellation
- DynamicData's reactive collection pipelines are powerful for large dataset transformations
- CommunityToolkit source generators minimize boilerplate for simple async scenarios

**Weaknesses:**
- No lifecycle-scoped async primitives — no equivalent to SwiftUI's `.task` (automatic cancellation on view disappear), Compose's `LaunchedEffect`, or React's Suspense
- Manual UI thread marshaling required (Dispatcher.InvokeAsync) — not automatic like SwiftUI's `@MainActor`
- No built-in tri-state (loading/success/error) primitive — developers model this manually with enums or rely on ReactiveUI
- No declarative loading boundaries (no Suspense equivalent)
- Data loading patterns are entirely ViewModel-layer decisions — the framework provides no guidance

**Rating: B** — Standard .NET async with excellent ReactiveUI integration. Missing lifecycle-scoped async and declarative loading patterns.

**Sources:** [ReactiveUI Docs](https://docs.avaloniaui.net/docs/concepts/reactiveui/), [DynamicData](https://github.com/reactivemarbles/DynamicData)

---

## 16. Lists & Virtualization

**Virtualized controls:**

| Control | Virtualizes? | Notes |
|---|---|---|
| ListBox | Yes (default) | VirtualizingStackPanel. Requires constrained height |
| ItemsRepeater | Yes | Lower-level, best for 10,000+ items. Ported from UWP |
| DataGrid | Yes | Column + row virtualization |
| TreeDataGrid | Yes | Combined tree/data grid with column sorting |
| TreeView | Yes | Via VirtualizingStackPanel |

**Key constraint:** Virtualization requires a constrained height — placing a ListBox inside a StackPanel without fixed height disables virtualization. Using non-virtualizing panels (StackPanel, WrapPanel) as `ItemsPanel` disables virtualization.

**Sorting, filtering, grouping:**
- **No built-in `ICollectionView`** (unlike WPF). Sorting, filtering, and grouping are handled in the ViewModel layer
- DataGrid supports grouping via `DataGridCollectionView` with collapsible group headers
- DynamicData reactive pipelines are the recommended approach for ItemsControls
- Manual approach: rebuild filtered `ObservableCollection` in the ViewModel

**Strengths:**
- Built-in virtualization for all major list/grid controls — no external library needed
- TreeDataGrid is a genuine addition not found in most competitors
- ItemsRepeater (from UWP) provides flexible virtualizing layout
- DataGrid column + row virtualization handles large tabular datasets

**Weaknesses:**
- **No `ICollectionView`** — WPF's most powerful collection feature (unified sort/filter/group with change notification) has no equivalent. This is a real gap for LOB apps
- ItemsRepeater has known issues with non-uniform item heights
- No built-in pagination or infinite scrolling primitive
- Grouping requires either DataGrid or manual flattened-collection workarounds

**Rating: B+** — Good built-in virtualization with TreeDataGrid as a differentiator. The ICollectionView gap hurts for data-heavy apps.

**Sources:** [Performance Optimization](https://docs.avaloniaui.net/docs/app-development/performance), [TreeDataGrid](https://docs.avaloniaui.net/docs/reference/controls/treedatagrid/), [10 Performance Tips](https://avaloniaui.net/blog/10-avalonia-performance-tips-to-supercharge-your-app)

---

## 17. Internationalization & Localization

**Architecture:** Standard .NET ResX resource files with satellite assemblies. `x:Static` references resource strings in AXAML. Standard .NET `ResourceManager` for code access.

**Runtime language switching:** `x:Static` resolves once at load time and does not update on culture change. Runtime switching requires a localization service implementing `INotifyPropertyChanged` that exposes localized strings as bindable properties. Third-party `I18N.Avalonia` provides this for ReactiveUI and Prism.

**RTL support:** `FlowDirection` property (`LeftToRight` / `RightToLeft`). Mirrors layout of child controls.

**Strengths:**
- Standard .NET localization story — ResX files, satellite assemblies, `CultureInfo` all work
- `{OnPlatform}` and `{OnFormFactor}` enable platform-specific text/layout in markup
- Third-party I18N.Avalonia adds runtime switching for ReactiveUI/Prism apps

**Weaknesses:**
- **RTL has known issues** — not all controls fully respect `FlowDirection` (CheckBox, Grid have been reported lagging WPF's implementation). Text trimming inconsistent between RTL and LTR. No automatic `FlowDirection` detection from text content
- No built-in plural/gender support — requires third-party .NET libraries (MessageFormat.NET, Smart.Format)
- `x:Static` doesn't update on locale change — dynamic switching requires INPC wrapper pattern, not built-in
- No implicit localization (unlike SwiftUI's auto-lookup from string literals)
- No string catalog or compile-time validation of localization keys

**Rating: B-** — Standard .NET localization works but RTL issues and lack of plural/gender are real gaps. Behind WPF (which has better RTL) and well behind SwiftUI/Flutter's i18n ergonomics.

**Sources:** [Localization Docs](https://docs.avaloniaui.net/docs/app-development/localizing), [RTL Issue #7345](https://github.com/AvaloniaUI/Avalonia/issues/7345), [FlowDirection Issue #1809](https://github.com/AvaloniaUI/Avalonia/issues/1809)

---

## 18. Interop & Incremental Adoption

**Native control embedding:** `NativeControlHost` embeds platform-specific native controls (web browser, media player, map view) within the Avalonia visual tree. Native window handles retrievable via `handle.Handle` with platform type descriptor ("HWND", "NSWindow", "XID").

**WPF migration — two paths:**
1. **Avalonia XPF (Commercial, EUR 29,500/app):** Binary/API compatibility with WPF. Change the `.csproj`, recompile, run on macOS/Linux. Third-party WPF controls work without modification. **Hybrid XPF** allows mixing Avalonia and WPF controls in the same app for gradual migration
2. **Manual port (Free, MIT):** Requires code changes. CSS-like styling replaces resource dictionary styles. Some API renames. Expert porting guide and known-differences document detail specific gaps

**Platform interop:** P/Invoke via `LibraryImport` (AOT-compatible). GPU interop via `ICustomDrawOperation` with SkiaSharp for direct canvas drawing. Platform-specific code via multi-targeting (`net10.0-android`, `net10.0-ios`).

**Strengths:**
- XPF binary compatibility is unique — recompile a WPF app for macOS/Linux without code changes
- Hybrid XPF enables gradual migration (mix Avalonia and WPF controls)
- NativeControlHost embeds platform views (maps, media, browsers)
- Standard .NET interop (P/Invoke, COM) works naturally
- WebView is now open-source in Avalonia 12 (was previously behind paywall)

**Weaknesses:**
- XPF is expensive (EUR 29,500 per application) — out of reach for many teams
- Manual migration requires real work — CSS-like styling is a significant departure from WPF's resource dictionaries
- No equivalent to Reactor's `UseObservable` (one-line MVVM ViewModel bridge) or Compose's seamless View interop
- NativeControlHost has the same airspace issues as WPF's HwndHost — native content renders above Avalonia content
- No incremental adoption story for WinUI 3 apps (XPF is WPF-only)

**Rating: B+** — XPF is a unique and powerful WPF migration story. NativeControlHost works. But XPF's cost and the manual migration effort for the free path are limiting factors.

**Sources:** [Native Interop](https://docs.avaloniaui.net/docs/app-development/native-interop), [WPF Porting Guide](https://avaloniaui.net/blog/the-expert-guide-to-porting-wpf-applications-to-avalonia), [XPF Docs](https://docs.avaloniaui.net/xpf), [XPF Pricing](https://avaloniaui.net/xpf/pricing/business)

---

## 19. Forms & Data Entry

**Validation — three mechanisms:**
1. **Data Annotations** (`[Required]`, `[Range]`, `[EmailAddress]`): `DataAnnotationsValidationPlugin` — disabled by default in v12, must enable with `.WithDataAnnotationsValidation()`
2. **`INotifyDataErrorInfo`:** Standard .NET interface for property-level validation. Avalonia automatically picks up errors from implementing ViewModels
3. **Exception-based:** Exceptions thrown during binding updates are caught and displayed as validation errors

**Error display:** `DataValidationErrors` control inside `ControlTemplate`. Default: red border + tooltip with error message. Customizable via styling. CommunityToolkit.Mvvm's `ValidateAllProperties()` enables submit-button validation.

**Input controls:** `TextBox` (watermark, password, multiline), `MaskedTextBox` (rich mask patterns — phone, VAT, currency), `NumericUpDown`, `DatePicker`, `TimePicker`, `ComboBox`, `AutoCompleteBox`, `Slider`, `ToggleSwitch`, `CheckBox`, `RadioButton`.

**Strengths:**
- `INotifyDataErrorInfo` is the same validation interface as WPF — mature, well-understood, supports multiple errors per property
- `MaskedTextBox` with rich mask patterns is a genuine strength for LOB forms
- Data Annotations integrate with the standard .NET validation ecosystem
- CommunityToolkit.Mvvm's `ObservableValidator` provides source-generated validation boilerplate reduction
- Cross-platform forms — same validation code on all 7 platforms

**Weaknesses:**
- Data Annotations disabled by default in v12 — must be explicitly opted in (surprising for WPF developers)
- No built-in `Form` widget with submit/reset/validate-all semantics (unlike Flutter's `Form.validate()`)
- No transactional editing (WPF's `BindingGroup`)
- No custom error templates beyond the default `DataValidationErrors` control — less flexible than WPF's `ErrorTemplate`
- No built-in form layout control — manual Grid/StackPanel arrangement required

**Rating: B** — Solid validation via INotifyDataErrorInfo and Data Annotations. MaskedTextBox is a strength. Behind WPF's depth (BindingGroup, custom ErrorTemplate) and Flutter's built-in Form widget.

**Sources:** [Data Validation](https://docs.avaloniaui.net/docs/data-binding/data-validation), [MaskedTextBox](https://docs.avaloniaui.net/docs/reference/controls/maskedtextbox)

---

## Summary Ratings

| Category | Grade | Notes |
|---|---|---|
| Declarative Syntax | B- | Improved XAML (compiled bindings, shorthand) but still XAML verbosity |
| Component Architecture | B | WPF-style class-based model, modernized; a generation behind function-as-component |
| State & Reactivity | B+ | IObservable bindings + ReactiveUI; no automatic fine-grained tracking |
| Rendering & Performance | B+ | Skia GPU rendering, excellent benchmarks; Impeller partnership promising |
| Layout | A- | WPF's proven two-pass model with ergonomic improvements |
| Styling & Theming | A- | CSS-like selectors are genuinely innovative; full ControlTemplate power |
| Navigation | B- | Multiple approaches, none mature; no type-safe routes |
| Animation | B+ | Three-layer system with render-thread composition; no one-liner animations |
| Accessibility | B+ | Cross-platform a11y with first-ever .NET Linux AT-SPI2; newer platforms less tested |
| Input & Gestures | B+ | Unified pointer system; gesture recognizers; XY Focus |
| Developer Experience | B- | No built-in hot reload; DevTools are good; paid tooling creates friction |
| Platform Reach | A- | 7 platforms including Linux + embedded; desktop mature, mobile improving |
| Testing | B | Solid headless testing; no semantics-based philosophy |
| Error Handling | C+ | Layered exception handling; no error boundaries |
| Data Loading & Async | B | Standard .NET async; ReactiveUI commands; no lifecycle-scoped primitives |
| Lists & Virtualization | B+ | Good built-in virtualization; no ICollectionView |
| Internationalization | B- | Standard .NET ResX; RTL issues; no plural/gender |
| Interop & Adoption | B+ | XPF is unique for WPF migration; NativeControlHost; XPF cost is high |
| Forms & Data Entry | B | INotifyDataErrorInfo + MaskedTextBox; behind WPF depth |

---

## Sources

- [Avalonia 12 Release Blog](https://avaloniaui.net/blog/avalonia-12)
- [Avalonia 11.1 Blog](https://avaloniaui.net/blog/avalonia-11-1-a-quantum-leap-in-cross-platform-ui-development)
- [Avalonia GitHub Repository](https://github.com/AvaloniaUI/Avalonia)
- [Avalonia vs MAUI Comparison](https://avaloniaui.net/maui-compare)
- [Impeller Partnership Announcement](https://avaloniaui.net/blog/avalonia-partners-with-google-s-flutter-t-eam-to-bring-impeller-rendering-to-net)
- [Supported Platforms](https://docs.avaloniaui.net/docs/supported-platforms)
- [Avalonia Pricing](https://avaloniaui.net/pricing)
- [Avalonia XAML Fundamentals](https://docs.avaloniaui.net/docs/fundamentals/avalonia-xaml)
- [Architecture Docs](https://docs.avaloniaui.net/docs/fundamentals/architecture)
- [Styles Docs](https://docs.avaloniaui.net/docs/basics/user-interface/styling/styles)
- [Style Selector Syntax](https://docs.avaloniaui.net/docs/styling/style-selector-syntax)
- [Control Themes](https://docs.avaloniaui.net/docs/basics/user-interface/styling/control-themes)
- [Fluent Theme](https://docs.avaloniaui.net/docs/basics/user-interface/styling/themes/fluent)
- [Theme Variants](https://docs.avaloniaui.net/docs/guides/styles-and-resources/how-to-use-theme-variants)
- [Keyframe Animations](https://docs.avaloniaui.net/docs/guides/graphics-and-animation/keyframe-animations)
- [Transitions](https://docs.avaloniaui.net/docs/guides/graphics-and-animation/transitions)
- [Composition Renderer](https://dev.to/adirh3/using-the-new-composition-renderer-in-avalonia-11-1k0p)
- [Gestures](https://docs.avaloniaui.net/docs/input-interaction/gestures)
- [Pointer Input](https://docs.avaloniaui.net/docs/input-interaction/pointer)
- [Focus](https://docs.avaloniaui.net/docs/input-interaction/focus)
- [NavigationPage](https://docs.avaloniaui.net/controls/navigation/navigationpage)
- [ReactiveUI Routing](https://www.reactiveui.net/docs/handbook/routing.html)
- [RouteNav.Avalonia](https://github.com/profix898/RouteNav.Avalonia)
- [Headless Testing](https://docs.avaloniaui.net/docs/concepts/headless/)
- [Appium Testing](https://docs.avaloniaui.net/docs/testing/ui-testing-with-appium)
- [Unhandled Exceptions](https://docs.avaloniaui.net/docs/concepts/unhandledexceptions)
- [Data Validation](https://docs.avaloniaui.net/docs/data-binding/data-validation)
- [MaskedTextBox](https://docs.avaloniaui.net/docs/reference/controls/maskedtextbox)
- [Localization](https://docs.avaloniaui.net/docs/app-development/localizing)
- [Native Interop](https://docs.avaloniaui.net/docs/app-development/native-interop)
- [WPF Porting Guide](https://avaloniaui.net/blog/the-expert-guide-to-porting-wpf-applications-to-avalonia)
- [XPF Docs](https://docs.avaloniaui.net/xpf)
- [Observable Binding](https://docs.avaloniaui.net/docs/data-binding/how-to-bind-to-an-observable)
- [ReactiveUI for Avalonia](https://docs.avaloniaui.net/docs/concepts/reactiveui/)
- [DynamicData](https://github.com/reactivemarbles/DynamicData)
- [Avalonia DevTools](https://avaloniaui.net/devtools)
- [HotAvalonia](https://github.com/Kira-NT/HotAvalonia)
- [App Showcase](https://avaloniaui.net/showcase)
- [Performance Guide](https://docs.avaloniaui.net/docs/guides/development-guides/improving-performance)
- [TreeDataGrid](https://docs.avaloniaui.net/docs/reference/controls/treedatagrid/)
- [RTL Issue #7345](https://github.com/AvaloniaUI/Avalonia/issues/7345)
- [FlowDirection Issue #1809](https://github.com/AvaloniaUI/Avalonia/issues/1809)
- [Linux A11y Issue #14275](https://github.com/AvaloniaUI/Avalonia/issues/14275)
- [Wikipedia: Avalonia](https://en.wikipedia.org/wiki/Avalonia_(software_framework))
