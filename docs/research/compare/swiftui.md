# SwiftUI — Framework Analysis

**Purpose:** Critical technical analysis for comparison against Microsoft UI frameworks (WinForms, WPF, WinUI 3, Microsoft.UI.Reactor).

**Version analyzed:** SwiftUI as of iOS 18 / macOS 15 (WWDC 2024-2025 era)

---

## Overview

SwiftUI is Apple's declarative UI framework, introduced at WWDC 2019 (iOS 13). It targets all Apple platforms: iOS, macOS, watchOS, tvOS, and visionOS. SwiftUI uses Swift's result builder feature to provide a domain-specific language for describing UI hierarchically. Views are value types (structs) conforming to the `View` protocol, and the framework manages rendering, diffing, and state propagation automatically through an internal mechanism called AttributeGraph.

SwiftUI's philosophy: **describe what the UI should look like for a given state, and the framework handles transitions.** It is tightly integrated with the Apple platform — leveraging UIKit/AppKit under the hood — and is the successor to both UIKit (iOS) and AppKit (macOS).

---

## 1. Declarative Model & Syntax

**Architecture:** Views are structs with a `body` computed property returning `some View`. The `@ViewBuilder` result builder enables a DSL where code blocks are transformed into view composition at compile time. Opaque return types (`some View`) hide concrete types, enabling the compiler to optimize.

**Strengths:**
- The syntax is remarkably clean. `VStack { Text("Hello"); Button("Tap") { action() } }` reads naturally
- Trailing closure syntax eliminates boilerplate for children
- Conditional views (`if/else`, `switch`) work directly in body
- `ForEach` integrates list iteration naturally
- Modifier chains (`.bold().font(.title)`) are ergonomic and discoverable

**Weaknesses:**
- **10-child limit per container** — ViewBuilder supports up to 10 children via generic overloads (TupleView). Exceeding this requires grouping with `Group`. This is a compiler limitation, not a runtime one
- **Type-checker performance** — Complex views with many modifiers can cause the Swift compiler to take exponentially longer to type-check. The infamous "the compiler is unable to type-check this expression in reasonable time" error forces developers to break views into smaller pieces for compilation, not for architectural reasons
- **Opaque types obscure errors** — `some View` means error messages reference internal generic types like `ModifiedContent<VStack<TupleView<...>>>` which are unintelligible

**Sources:** [SwiftUI 2025: What's Fixed, What's Not](https://juniperphoton.substack.com/p/swiftui-2025-whats-fixed-whats-not), [Apple Developer: ViewBuilder](https://developer.apple.com/documentation/SwiftUI/ViewBuilder)

---

## 2. Component Architecture

**How it works:** Components are structs conforming to `View`. Composition is achieved by nesting views. There are no "slots" per se — children are passed via `@ViewBuilder` closures or as generic `Content` parameters. `ViewModifier` protocol enables reusable modifier chains. `PreferenceKey` allows child-to-parent communication (inverse data flow).

**Strengths:**
- Value-type views are cheap to create and diff
- Custom ViewModifiers enable composable styling
- Environment and EnvironmentObject enable clean dependency injection
- Extensions on View allow framework-like capabilities

**Weaknesses:**
- No equivalent to React's Error Boundary — a crash in a child view crashes the entire app
- `PreferenceKey` is powerful but poorly documented and non-obvious
- Complex generic view types make library authoring difficult (type erasure via `AnyView` erases optimization opportunities)

---

## 3. State Management & Reactivity

**Property wrappers (pre-iOS 17):** `@State` (local value-type state), `@Binding` (two-way reference to parent's state), `@StateObject` (owned reference-type state), `@ObservedObject` (non-owned reference-type state), `@EnvironmentObject` (injected shared state), `@Environment` (system-provided values like color scheme, locale).

**Observation framework (iOS 17+):** The `@Observable` macro replaces `ObservableObject` + `@Published`. It provides **property-level tracking** — views only re-render when the specific properties they read change, not when any property on the object changes. This is a major performance improvement over the old model. Under the hood, the Observation framework uses access tracking during `body` evaluation to register which properties each view depends on.

**Strengths:**
- Fine-grained invalidation with `@Observable` is best-in-class among declarative frameworks
- The property wrapper model is elegant once understood
- `@Environment` provides clean access to system settings (color scheme, locale, size class)
- State flows unidirectionally by default (parent to child via props)

**Weaknesses:**
- **@StateObject vs @ObservedObject lifecycle confusion** is a well-known pitfall — using `@ObservedObject` for an object the view owns causes the object to be recreated on re-render, losing state
- **@Observable requires iOS 17+**, forcing apps supporting iOS 15-16 to use the old model, creating a fractured ecosystem
- No built-in global state management pattern (no Redux/Zustand equivalent — developers use singletons or environment objects)
- **No selector pattern** — you cannot subscribe to a slice of an observed object; any read property triggers re-render

**Sources:** [Observation Framework Explained](https://www.sagarunagar.com/blog/swiftui-observation-framework/), [Discover Observation in SwiftUI - WWDC23](https://developer.apple.com/videos/play/wwdc2023/10149/), [SwiftUI in Production: 25 Hard Lessons](https://medium.com/@rahulnimje94/swiftui-in-production-25-hard-lessons-i-learned-the-painful-way-2a72261abcae)

---

## 4. Rendering & Performance

**How it works:** SwiftUI uses an internal engine called **AttributeGraph** that tracks view dependencies, diffs state changes, and determines which views need re-evaluation. AttributeGraph is not public API — it's a C++ engine that Apple has not documented, making performance debugging a black box.

**Structural identity vs explicit identity:** SwiftUI identifies views by their position in the view hierarchy (structural identity) unless an explicit `.id()` modifier is provided. This means that `if/else` branches in a body destroy and recreate state when switching, because the structural identity changes.

**Performance characteristics:**
- SwiftUI has overhead from diffing view states and dynamic layout computation — architecturally, it may never be as fast as raw UIKit/AppKit for extreme scenarios
- `@Observable` (iOS 17+) dramatically reduces unnecessary re-renders
- List performance with 10,000+ items has been a historical problem; improvements in iOS 17-18 address scheduling and lazy loading
- AttributeGraph memory leaks have been reported in production apps

**Weaknesses:**
- The rendering engine is opaque — no equivalent to React DevTools' "why did this render" or Compose's recomposition counts
- No concurrent rendering or priority-based updates (unlike React Fiber)
- Complex view hierarchies can exhibit surprising re-render behavior due to structural identity rules

**Sources:** [SwiftUI vs UIKit in 2025](https://www.alimertgulec.com/en/blog/swiftui-vs-uikit-2025), [The Year SwiftUI Died](https://blog.jacobstechtavern.com/p/the-year-swiftui-died)

---

## 5. Layout System

**Architecture:** Two-phase layout: parent proposes size, child reports size. `VStack`, `HStack`, `ZStack` provide directional layout. `LazyVStack`/`LazyHStack` defer creation for performance. `Grid` (iOS 16+) provides a CSS Grid-like multi-column layout. `GeometryReader` exposes parent size for responsive layouts (but is expensive and widely considered an anti-pattern for most uses).

**Layout protocol (iOS 16+):** Custom layout containers via the `Layout` protocol — similar in concept to WPF's `MeasureOverride`/`ArrangeOverride`. Enables arbitrary custom layouts.

**Strengths:**
- The propose/report negotiation is elegant and composable
- `alignment` guides enable sophisticated cross-container alignment
- `ViewThatFits` (iOS 16+) automatically selects the first child that fits available space
- `LazyVGrid`/`LazyHGrid` provide simple grid layouts

**Weaknesses:**
- `GeometryReader` is both essential (for responsive design) and problematic (takes all proposed space, causes extra layout passes)
- No constraint-based layout (no equivalent to Auto Layout's constraint solver)
- SafeArea handling requires explicit `.ignoresSafeArea()` modifiers

---

## 6. Styling & Theming

**How it works:** Styling is applied via modifier chains. Custom styles are created via protocols: `ButtonStyle`, `ToggleStyle`, `LabelStyle`, `TextFieldStyle`, etc. Each style protocol has a `makeBody(configuration:)` method that receives the content and returns a styled view.

**Theming:** `.preferredColorScheme(.dark)` sets light/dark mode. `.tint(.blue)` applies accent colors. Environment values propagate theme-related settings. Each platform applies its native design language automatically.

**Strengths:**
- Platform-native styling by default — apps look native on iOS, macOS, watchOS, visionOS without effort
- Custom style protocols are well-designed and composable
- Dark mode support is automatic for all standard components
- Design language is cohesive across Apple platforms

**Weaknesses:**
- Custom design systems (non-Apple aesthetic) require extensive work — there's no "unstyled" layer
- Cross-platform styling (iOS vs macOS) can produce unexpected differences
- No equivalent to WPF's ControlTemplate — you can't completely redefine a control's visual tree

---

## 7. Navigation

**NavigationStack (iOS 16+):** Replaced NavigationView with a value-driven navigation model. `NavigationPath` holds a type-erased stack. `navigationDestination(for:)` maps types to views.

**NavigationSplitView:** Multi-column navigation (sidebar, content, detail).

**Strengths:**
- Type-safe navigation destinations (map data types to views)
- Deep linking support via URL handling
- Programmatic navigation via path manipulation
- Back stack is value-type and inspectable

**Weaknesses:**
- **NavigationPath is type-erased** — you lose compile-time type safety for heterogeneous stacks. This is the fundamental weakness vs Compose Navigation 3's typed `SnapshotStateList<NavKey>` or Reactor's type-safe route records
- Pre-iOS 16 NavigationView is buggy and widely considered broken (especially on iPad)
- No built-in adaptive multi-pane layout that transitions between phone/tablet/desktop layouts
- Deep linking configuration is platform-specific and fragile

**Sources:** [Apple Developer: NavigationStack](https://developer.apple.com/documentation/SwiftUI/NavigationStack)

---

## 8. Animation

**Architecture:** SwiftUI has the most intuitive animation system of any declarative framework. `withAnimation { state = newValue }` wraps state changes in an animation context. The `.animation(.spring, value:)` modifier binds animations to specific value changes.

**Capabilities:**
- `matchedGeometryEffect` for shared-element transitions across views (equivalent to hero animations)
- `PhaseAnimator` (iOS 17+) for multi-step animations
- Keyframe animations (iOS 17+) for complex property curves
- Spring animations with configurable parameters
- `transition` modifier for enter/exit animations
- All standard views animate interpolable property changes automatically

**Strengths:**
- **Best-in-class developer ergonomics** — animating a state change is literally one line
- Spring physics are the default (natural motion)
- Animations compose naturally — multiple animated state changes interleave correctly
- GPU-accelerated through Core Animation

**Weaknesses:**
- Fine-tuning animation timing requires understanding the opaque animation scheduling
- Complex coordinated animations can be difficult to orchestrate
- No timeline editor or visual animation tools (unlike WPF's Blend)

**Rating: A** — The animation system is SwiftUI's crown jewel.

---

## 9. Accessibility

**Architecture:** SwiftUI generates accessibility elements automatically. Standard views (Text, Button, Toggle, etc.) have correct labels, traits, and actions out of the box. The framework creates an accessibility tree from the view hierarchy.

**Capabilities:** `.accessibilityLabel()`, `.accessibilityHint()`, `.accessibilityValue()`, `.accessibilityAction()`, `.accessibilityElement(children:)` for grouping/merging, rotor support, VoiceOver integration across all platforms.

**Strengths:**
- **Best-in-class automatic accessibility** — standard views work with VoiceOver without any code
- Semantic view grouping is natural
- VoiceOver order matches view declaration order
- Custom actions and rotor items are well-supported
- Deep integration with all Apple accessibility features (Dynamic Type, Reduce Motion, etc.)

**Weaknesses:**
- Custom views with complex interaction patterns still need manual accessibility annotation
- Some SwiftUI bugs cause incorrect accessibility trees (reported across multiple iOS versions)

**Rating: A** — Apple's tight platform integration makes accessibility a genuine strength.

---

## 10. Input & Gestures

**Built-in gestures:** `TapGesture`, `LongPressGesture`, `DragGesture`, `MagnificationGesture`, `RotationGesture`. Gestures compose with `.simultaneously(with:)`, `.sequenced(before:)`, and `.exclusively(before:)`.

**Focus system:** `@FocusState` property wrapper tracks focus state. `FocusedValue` and `FocusedObject` enable focused-element data flow.

**Strengths:**
- Gesture composition is elegant and type-safe
- `@FocusState` is clean for keyboard/focus management
- `onSubmit` for form-like keyboard handling
- ScrollView gesture interaction (scroll + drag) mostly works

**Weaknesses:**
- Drag gesture + ScrollView interaction has persistent bugs requiring OS-version-specific workarounds
- No raw pointer event access (unlike Flutter's pointer events)
- Complex gesture disambiguation can be surprising

---

## 11. Developer Experience

**Xcode Previews (Canvas):** Live, interactive previews of SwiftUI views directly in Xcode. Multiple preview configurations (light/dark, device sizes) can render simultaneously.

**Strengths:**
- Previews are genuinely useful for rapid iteration
- Live editing with #Preview macro is fast
- Instruments profiling works for SwiftUI (Core Animation, Time Profiler)
- Documentation quality is good (Apple developer docs + WWDC videos)

**Weaknesses:**
- **Previews frequently crash or hang** — the preview engine is notoriously unreliable, especially for views with complex dependencies
- No equivalent to React DevTools or Flutter Widget Inspector for SwiftUI-specific debugging
- Error messages from the Swift type checker are often cryptic for complex view hierarchies
- **Learning curve is steep** for the state management system (when to use @State vs @StateObject vs @ObservedObject vs @Observable is non-obvious)

**Sources:** [SwiftUI 2025 Review](https://juniperphoton.substack.com/p/swiftui-2025-whats-fixed-whats-not)

---

## 12. Platform Reach & Ecosystem

**Platforms:** iOS, iPadOS, macOS, watchOS, tvOS, visionOS — all from a single codebase with platform-adaptive behavior.

**Ecosystem:** Swift Package Manager for dependencies. Libraries like The Composable Architecture (TCA), SwiftUI-Introspect (access UIKit underneath), ViewInspector (testing).

**Strengths:**
- **Unmatched Apple platform coverage** — one framework for phone, tablet, desktop, watch, TV, and spatial computing
- Platform-adaptive behavior is automatic (NavigationSplitView adapts to screen size)
- visionOS support is exclusive to SwiftUI

**Weaknesses:**
- **Apple platforms only** — no cross-platform story whatsoever
- Community ecosystem is smaller than React or Flutter (Swift Package Manager has ~8,000 packages vs npm's 2M+)
- Breaking changes between OS versions force minimum deployment target decisions

---

## 13. Testing

**XCTest:** Standard unit testing framework. SwiftUI views are testable by inspecting state mutations, but testing the view body output is difficult because `some View` is opaque.

**ViewInspector:** Third-party library that uses reflection to inspect SwiftUI view hierarchies. The de facto standard for SwiftUI view testing but relies on private implementation details.

**Snapshot testing:** SnapshotTesting library (by Point-Free) captures rendered view snapshots for regression testing.

**Weaknesses:**
- **SwiftUI views are fundamentally hard to test** — the opaque `some View` return type resists programmatic inspection
- ViewInspector relies on implementation details that can break across Xcode versions
- No official SwiftUI testing framework from Apple (unlike Compose's ComposeTestRule or Flutter's testWidgets)
- Preview-based testing is manual, not automated

---

## 14. Error Handling & Resilience

**Critical weakness: No error boundary concept.** An uncaught error in any view crashes the entire application. There is no mechanism to catch errors at a subtree level and display fallback UI (unlike React's ErrorBoundary or Flutter's ErrorWidget).

SwiftUI views that hit assertion failures, force-unwrap nils, or throw unhandled errors cause full app crashes. The only mitigation is defensive coding with optional handling throughout the view hierarchy.

**Rating: D** — This is the single biggest gap compared to frameworks with error boundaries.

---

## 15. Data Loading & Async

**Architecture:** SwiftUI integrates Swift's structured concurrency directly into the view lifecycle. The `.task` modifier (iOS 15+) launches an async operation tied to the view's lifetime — automatically cancelled when the view disappears. `@State` properties updated from async code automatically trigger re-render, and SwiftUI marshals updates to the main actor since views are `@MainActor`-isolated.

**Capabilities:** `AsyncImage` (iOS 15+) is a rare built-in component that encapsulates loading/placeholder/error states with an in-memory image cache. `.refreshable { await ... }` integrates pull-to-refresh with async/await. Combine publishers bridge via `.onReceive()`, though structured concurrency with `.task` has largely supplanted Combine for view-layer code.

**Strengths:**
- `.task` is the most elegant lifecycle-scoped async of any framework — one modifier, automatic cancellation
- `async/await` is native Swift — no external library or runtime needed
- `@MainActor` isolation eliminates manual thread marshaling entirely

**Weaknesses:**
- No built-in tri-state (loading/success/error) primitive — developers must manually model this with enums
- No built-in data caching layer beyond `AsyncImage`'s memory cache
- No equivalent to React's Suspense (declarative loading boundaries)

**Rating: A-** — Excellent async integration; missing tri-state primitives and caching.

**Sources:** [Apple Developer: View.task](https://developer.apple.com/documentation/swiftui/view/task(priority:_:)), [Apple Developer: AsyncImage](https://developer.apple.com/documentation/swiftui/asyncimage)

---

## 16. Lists & Virtualization

**Architecture:** `List` wraps UITableView/NSTableView internally and provides **view recycling**, significantly more efficient than `LazyVStack` for large datasets. `LazyVStack` loads lazily but does not recycle — scrolling to distant positions instantiates all intermediate views.

**Capabilities:** `ForEach` with `Identifiable` conformance. `Section` for grouping with headers/footers. `LazyVGrid`/`LazyHGrid` for grid layouts. `.searchable()` for integrated search. Selection handling via `List(selection:)`.

**Strengths:**
- `List` with view recycling is efficient for large datasets
- `.searchable()` is the most ergonomic list search of any framework
- Section/header/footer support is built-in and clean
- Selection API is declarative

**Weaknesses:**
- Critical gotcha: adding `.id()` to children inside `ForEach` defeats `List`'s recycling optimization
- `LazyVStack` lacks recycling — performance degrades with 10k+ items for variable-height content
- No built-in pagination or infinite scrolling primitive
- List styling customization is limited (removing separators, custom swipe actions require workarounds)

**Rating: B+** — Good built-in lists; recycling limited to `List`, no pagination.

**Sources:** [Fatbobman: List or LazyVStack](https://fatbobman.com/en/posts/list-or-lazyvstack/)

---

## 17. Internationalization & Localization

**Architecture:** SwiftUI provides implicit localization — `Text("Hello")` automatically looks up "Hello" in `Localizable.strings`/`.xcstrings` via `LocalizedStringKey`. String catalogs (`.xcstrings`, Xcode 15+) consolidate all translations into one JSON file with per-locale variations and built-in plural/device category support.

**Strengths:**
- Implicit localization with zero boilerplate (string literals auto-resolve)
- CLDR plural categories (zero, one, two, few, many, other) via string catalogs
- RTL automatic — layouts use leading/trailing by default
- Runtime locale switching via `.environment(\.locale, ...)`
- `FormatStyle` handles date/number formatting locale-aware automatically

**Weaknesses:**
- Custom brand strings without `.strings` files require manual `Bundle` management
- No ICU MessageFormat for complex select/gender patterns (only plurals)

**Rating: A** — Best-in-class implicit localization with automatic RTL.

---

## 18. Interop & Incremental Adoption

**Architecture:** `UIHostingController` wraps any SwiftUI `View` as a `UIViewController`, droppable into UIKit navigation stacks. `UIViewRepresentable`/`UIViewControllerRepresentable` embed UIKit views inside SwiftUI. `@UIApplicationDelegateAdaptor` bridges UIKit lifecycle. On macOS, `NSHostingController`/`NSViewRepresentable`.

**Strengths:**
- Bidirectional embedding is mature and well-documented
- Apple's recommended migration path: leaf-first, one screen at a time
- `UIHostingController` overhead is negligible (<1% per Apple's measurement)
- `@Observable` objects work bidirectionally between SwiftUI and UIKit

**Weaknesses:**
- SwiftUI cannot participate in UIKit navigation controller animation coordination (visual artifacts during push/pop in mixed stacks)
- `UIViewRepresentable` requires a `Coordinator` boilerplate for delegate-based event bridging
- Many UIKit features still lack SwiftUI equivalents (e.g., compositional collection layouts), forcing continued UIKit embedding

**Rating: A-** — Mature bidirectional interop; some animation coordination gaps.

**Sources:** [Apple Developer: UIHostingController](https://developer.apple.com/documentation/swiftui/uihostingcontroller), [WWDC 2023: Migrate to SwiftUI](https://developer.apple.com/videos/play/wwdc2023/10156/)

---

## 19. Forms & Data Entry

**Architecture:** `@Binding` creates two-way connections between parent state and child controls. `TextField("Name", text: $name)` binds directly to state. `Form` view provides platform-appropriate grouped layout (grouped table on iOS). `Section` divides fields with headers/footers.

**Strengths:**
- `@Binding` is the most ergonomic two-way binding of any declarative framework
- `Form` and `Section` provide clean, platform-native form layout
- `@FocusState` enables programmatic focus management between fields
- `FormatStyle` on `TextField` handles numeric/date/currency input formatting
- `SecureField` for password entry
- `.onSubmit` for keyboard submit handling

**Weaknesses:**
- **No built-in validation framework** — developers must implement validation manually
- No equivalent to WPF's `ErrorTemplate`, `INotifyDataErrorInfo`, or `ValidationRule`
- Error display requires manual `Text` views styled in red — no framework support
- No built-in input masking (unlike WinForms' `MaskedTextBox`)

**Rating: B** — Excellent binding ergonomics; no validation framework is a significant gap for LOB apps.

**Sources:** [Apple Developer: Form](https://developer.apple.com/documentation/swiftui/form), [Apple Developer: FocusState](https://developer.apple.com/documentation/swiftui/focusstate)

---

## Summary Ratings

| Category | Grade | Notes |
|---|---|---|
| Declarative Syntax | A | Clean DSL via result builders; 10-child limit and type-checker perf are minor |
| Component Architecture | A- | Value-type views, ViewModifier; no error boundaries, opaque generics |
| State & Reactivity | A | @Observable is best-in-class; pre-iOS 17 model has pitfalls |
| Rendering & Performance | B+ | Good perf, opaque engine makes debugging hard |
| Layout | A- | Elegant propose/report; GeometryReader issues |
| Styling & Theming | A | Native styling, automatic dark mode; custom design systems harder |
| Navigation | B+ | Type-safe with NavigationStack; NavigationPath is type-erased |
| Animation | A | Best-in-class declarative animation |
| Accessibility | A | Best-in-class automatic accessibility |
| Input & Gestures | A- | Elegant gesture composition; ScrollView+drag bugs |
| Developer Experience | B+ | Great previews when they work; crash-prone tooling |
| Platform Reach | B | All Apple platforms; Apple-only |
| Testing | C+ | Hard to test; ViewInspector is fragile third-party |
| Error Handling | D | No error boundaries; crashes the app |
| Data Loading & Async | A- | .task is excellent; no tri-state primitive or caching |
| Lists & Virtualization | B+ | List has recycling; LazyVStack doesn't; no pagination |
| Internationalization | A | Implicit localization, CLDR plurals, automatic RTL |
| Interop & Adoption | A- | Mature bidirectional UIKit bridge; animation coordination gaps |
| Forms & Data Entry | B | Elegant @Binding; no validation framework |

---

## Sources

- [SwiftUI 2025: What's Fixed, What's Not](https://juniperphoton.substack.com/p/swiftui-2025-whats-fixed-whats-not)
- [SwiftUI in Production: 25 Hard Lessons](https://medium.com/@rahulnimje94/swiftui-in-production-25-hard-lessons-i-learned-the-painful-way-2a72261abcae)
- [The Year SwiftUI Died](https://blog.jacobstechtavern.com/p/the-year-swiftui-died)
- [SwiftUI vs UIKit in 2025](https://www.alimertgulec.com/en/blog/swiftui-vs-uikit-2025)
- [State of Swift 2026](https://devnewsletter.com/p/state-of-swift-2026/)
- [Observation Framework Explained](https://www.sagarunagar.com/blog/swiftui-observation-framework/)
- [Discover Observation in SwiftUI - WWDC23](https://developer.apple.com/videos/play/wwdc2023/10149/)
- [SwiftUI Accessibility: Semantic Views](https://mobilea11y.com/guides/swiftui/swiftui-semantic-views/)
- [Catch up on accessibility in SwiftUI - WWDC24](https://developer.apple.com/videos/play/wwdc2024/10073/)
- [Migrating from Observable Object to Observable Macro](https://developer.apple.com/documentation/SwiftUI/Migrating-from-the-observable-object-protocol-to-the-observable-macro)
- [SwiftUI for Mac 2025](https://troz.net/post/2025/swiftui-mac-2025/)
- [SwiftUI at WWDC 2025](https://mjtsai.com/blog/2025/06/18/swiftui-at-wwdc-2025/)
- [Apple Developer: ViewBuilder](https://developer.apple.com/documentation/SwiftUI/ViewBuilder)
- [Apple Developer: NavigationStack](https://developer.apple.com/documentation/SwiftUI/NavigationStack)
- [SwiftUI vs UIKit in 2026](https://7span.com/blog/swiftui-vs-uikit)
- [Fatbobman: List or LazyVStack](https://fatbobman.com/en/posts/list-or-lazyvstack/)
- [Apple Developer: View.task](https://developer.apple.com/documentation/swiftui/view/task(priority:_:))
- [Apple Developer: UIHostingController](https://developer.apple.com/documentation/swiftui/uihostingcontroller)
- [Apple Developer: Form](https://developer.apple.com/documentation/swiftui/form)
