# Jetpack Compose — Framework Analysis

**Purpose:** Critical technical analysis for comparison against Microsoft UI frameworks (WinForms, WPF, WinUI 3, Microsoft.UI.Reactor).

**Version analyzed:** Jetpack Compose 1.7+ / Compose Multiplatform 1.8-1.9 (2025-2026 era)

---

## Overview

Jetpack Compose is Google's modern declarative UI toolkit for Android, first reaching stable 1.0 in July 2021. It replaces the 14-year-old Android View system with composable functions annotated with `@Composable`. A Kotlin compiler plugin transforms these functions into a slot-table-based rendering engine that tracks state, manages composition, and performs smart recomposition. JetBrains extends this via **Compose Multiplatform** (iOS stable May 2025, Desktop stable, Web beta September 2025).

---

## 1. Declarative Model & Syntax

**Architecture:** `@Composable` functions are the building blocks. The Kotlin compiler plugin transforms them at compile time, inserting a `Composer` parameter that manages the slot table. Kotlin's language features provide an excellent DSL: trailing lambdas for children, named parameters for configuration, extension functions for modifiers.

**Slot APIs:** A pattern where composable functions accept `@Composable` lambdas as parameters, allowing callers to "fill in" content slots:
```kotlin
Scaffold(
    topBar = { TopAppBar(...) },
    floatingActionButton = { FloatingActionButton(...) }
) { paddingValues -> Content(paddingValues) }
```

**Strengths:**
- Kotlin's trailing lambda syntax makes child content elegant: `Column { Text("A"); Text("B") }`
- Named parameters with defaults reduce boilerplate: `Text("Hello", fontSize = 24.sp, fontWeight = FontWeight.Bold)`
- No separate markup language — everything is Kotlin, full IDE support (refactoring, find usages, debugging)
- Slot APIs provide a clean pattern for component customization that's more explicit than React's children prop
- No artificial child count limits (unlike SwiftUI's 10-child TupleView)

**Weaknesses:**
- The Kotlin compiler plugin is a "magic" transformation — developers must understand what `@Composable` actually does to avoid subtle bugs
- Modifier chains can become very long and order-dependent (padding before vs. after background produces different results)
- No JSX-like syntax — UI code is valid Kotlin, which means it visually looks identical to business logic

**Sources:** [Android Developers: Compose Mental Model](https://developer.android.com/develop/ui/compose/mental-model), [Compose Compiler Plugin](https://developer.android.com/develop/ui/compose/compiler)

---

## 2. Component Architecture

**Functions, not classes:** Composable functions are the component model. There are no base classes to extend, no lifecycle methods to override, no `this` context. State is managed through `remember` and state hoisting. Identity is positional (call-site order in the slot table) unless overridden with `key()`.

**Positional memoization:** Compose remembers values by call-site position. `remember { expensive() }` caches a value at the current composition position. `rememberSaveable` persists across configuration changes and process death.

**Strengths:**
- Functions are simpler than classes — no lifecycle confusion (unlike Flutter's StatefulWidget + State two-object dance)
- `key()` provides explicit identity when needed
- Composition is natural — call a composable inside another
- No "widget splitting" problem — you don't need to decide between StatelessWidget and StatefulWidget upfront

**Weaknesses:**
- Positional memoization is surprising — reordering calls or adding conditional composables can break remembered state
- No equivalent to React's `forwardRef` or Swift's `@Binding` for passing fine-grained write access
- Component "identity" is implicit and non-obvious to beginners

---

## 3. State Management & Reactivity

**Core primitives:** `mutableStateOf()` creates observable state. Compose's **Snapshot system** tracks reads and writes: any composable that reads a state object is automatically subscribed to changes. When state changes, only reading composables recompose.

**State hoisting:** The pattern of moving state to the caller: the composable function takes `value` and `onValueChange` parameters instead of holding state internally. This is the officially recommended pattern for most state.

**CompositionLocal:** Dependency injection via the composition tree (similar to React Context or SwiftUI Environment). Used for theming (`MaterialTheme`), configuration, and cross-cutting concerns.

**`derivedStateOf`:** Computes a value from other state objects, only recomposing when the result changes (not when inputs change). Useful for expensive transformations.

**Strengths:**
- The Snapshot system is architecturally elegant — state objects are first-class with automatic tracking
- Fine-grained recomposition (only composables that read changed state recompose)
- `snapshotFlow` bridges Compose state to Kotlin coroutines Flow, enabling reactive pipelines
- State hoisting encourages clean, testable architecture by default
- No external state management library needed for most apps (unlike React)

**Weaknesses:**
- **Stability system complexity** — Compose's recomposition skipping depends on parameter stability. Classes not marked `@Stable` or `@Immutable` always cause recomposition. Understanding and debugging stability is a major pain point in production
- `CompositionLocal` has the same problem as React Context — no selector, all consumers recompose on any change
- Lambda stability is a hidden performance trap — unstable lambdas prevent composable skipping. Google's docs barely mention this

**Sources:** [Jetpack Compose Performance](https://developer.android.com/develop/ui/compose/performance), [Diagnose Stability Issues](https://developer.android.com/develop/ui/compose/performance/stability/diagnose), [Top 5 Compose Pitfalls 2025](https://medium.com/@innerarchitecture/top-5-jetpack-compose-pitfalls-in-2025-and-proven-ways-to-avoid-them-234d26a8c2c6)

---

## 4. Rendering & Performance

**Slot table:** Compose stores the composition in a **gap buffer** data structure (like a text editor). This allows O(1) insertions and deletions at the current position. The slot table records composable call sites, parameters, state, and remembered values.

**Smart recomposition:** When state changes, Compose identifies the smallest scope that reads that state and recomposes only that scope. **Strong Skipping** (enabled by default in Kotlin 2.0.20+) uses reference equality to skip recomposition even for unstable parameters if the instance hasn't changed.

**Performance characteristics:**
- Baseline profiles are critical for Compose performance — without them, first-launch has noticeable jank due to JIT compilation
- Recomposition count tracking (via Composition Tracing) helps identify over-recomposition
- Compose does NOT replace Android's rendering pipeline — it generates Android Views/RenderNodes underneath

**Weaknesses:**
- **Unnecessary recomposition is the #1 cause of jank** in Compose apps — requires understanding stability, lambda captures, and scope boundaries
- Compose has measurably higher memory overhead than the View system for equivalent UIs
- First-launch cold-start penalty without baseline profiles
- The rendering pipeline is two-phase: Composition → Layout → Drawing. Blocking either the main or render thread drops frames — Compose cannot save you from expensive operations

**Sources:** [Compose Performance Checklist](https://medium.com/@nvineet02/jetpack-compose-performance-checklist-for-production-d697abe3f50c), [Revisiting Compose Performance 2025](https://a64.in/posts/revisiting-compose-perf-land-2025/), [The Pitfall No One Talks About](https://medium.com/android-alchemy/the-jetpack-compose-performance-pitfall-no-one-talks-about-35702beb009a)

---

## 5. Layout System

**Primitives:** `Column`, `Row`, `Box` (stacking), `LazyColumn`/`LazyRow` (virtualized), `ConstraintLayout`, custom `Layout` composable.

**Modifier system:** `Modifier` is a linked list of layout, drawing, and input instructions. Order matters: `Modifier.padding(16.dp).background(Color.Red)` pads then colors (red extends to padding), while `.background(Color.Red).padding(16.dp)` colors then pads (red only inside padding). This is powerful but a constant source of confusion.

**Intrinsic measurements:** Composables can query children's intrinsic sizes (`IntrinsicSize.Min`, `IntrinsicSize.Max`) for size-based layout decisions, similar to WPF's DesiredSize concept.

**SubcomposeLayout:** Allows measuring children in a first pass, then composing additional content based on those measurements. Used internally by `LazyColumn` and `Scaffold`.

**Strengths:**
- Custom Layout composable is powerful and well-documented
- Modifier chains are composable and reusable (`val cardModifier = Modifier.padding(8.dp).shadow(4.dp)`)
- Intrinsic measurements solve real layout problems elegantly
- No separate layout file — layout is code

**Weaknesses:**
- Modifier order-dependence is a perpetual gotcha
- No built-in grid for non-lazy contexts (use LazyVerticalGrid or third-party)
- ConstraintLayout in Compose is a separate dependency and less mature than its View counterpart

---

## 6. Styling & Theming

**MaterialTheme:** Compose ships with Material Design 3 as the default design system. `MaterialTheme` provides `colorScheme`, `typography`, and `shapes` via CompositionLocal.

**Custom design systems:** Developers can create entirely custom theme systems using CompositionLocal:
```kotlin
val LocalAppColors = staticCompositionLocalOf { AppColors() }
```

**Strengths:**
- Material 3 theming is comprehensive — color, typography, shapes, motion all configured in one place
- Dynamic color (Material You / wallpaper-based) works out of the box on Android 12+
- Theming is composable — nested MaterialTheme blocks override parent themes
- Custom design systems are first-class via CompositionLocal (not an afterthought)

**Weaknesses:**
- Material-flavored by default — non-Material apps require significant theme overriding
- No equivalent to WPF's ControlTemplate — you can't redefine a component's internal structure via theming
- Theme resource management is in-memory (unlike WPF/WinUI's ResourceDictionary files)

---

## 7. Navigation

**Navigation Compose (legacy):** NavHost + NavController with string routes. Functional but stringly-typed and error-prone.

**Navigation 3 (stable November 2025):** Complete rewrite built for Compose. Key design decisions:
- **Developer-owned back stack** — a `SnapshotStateList<NavKey>` that the developer controls directly. Navigation is literally `backStack.add(MyRoute(id = 42))` and `backStack.removeLastOrNull()`
- **Type-safe routes** — `@Serializable data class` implementing `NavKey`. Arguments are part of the type definition
- **Scenes API** — adaptive layouts that transition between single-pane (phone) and multi-pane (tablet/desktop) from the same route definitions

**Strengths:**
- Navigation 3 is architecturally the strongest navigation system in any declarative framework
- Type safety is genuine — routes are data classes with typed parameters
- Developer-owned back stack is transparent and testable (it's just a list)
- Scenes API solves adaptive multi-pane layouts, which most frameworks lack
- Deep linking via Kotlin serialization

**Weaknesses:**
- Navigation 3 is new (stable Nov 2025) — limited production battle-testing
- Migration from Navigation Compose legacy is non-trivial
- Nested navigation with independent back stacks requires careful management

**Sources:** [Announcing Navigation 3](https://android-developers.googleblog.com/2025/05/announcing-jetpack-navigation-3-for-compose.html), [Navigation 3 is Stable](https://android-developers.googleblog.com/2025/11/jetpack-navigation-3-is-stable.html), [Say Goodbye to NavController](https://medium.com/@rodrigokirch/jetpack-compose-navigation-3-is-stable-say-goodbye-to-navcontroller-8afd20a21fa2)

---

## 8. Animation

**Comprehensive built-in system:**
- `animate*AsState` — single-value animations triggered by state changes
- `AnimatedVisibility` — enter/exit animations for composables
- `AnimatedContent` — crossfade/transform between content states
- `updateTransition` — coordinated multi-property animations
- `Animatable` — imperative animation control within coroutines
- `rememberInfiniteTransition` — looping animations
- Spring physics, keyframe specs, tween, snap, repeatable animation specs

**Strengths:**
- Rich API surface covering nearly all animation needs
- Spring physics is the default (natural motion)
- Animations compose well — multiple animated values update in sync
- `AnimatedVisibility` makes enter/exit trivial (React needs AnimatePresence library)
- Coroutine-based imperative animations for complex orchestration

**Weaknesses:**
- The API surface is large and the documentation doesn't clearly explain when to use which API
- No visual animation editor (unlike WPF's Blend)
- Performance-sensitive animations require understanding of composition vs. drawing phases

**Rating: A-** — Comprehensive and well-designed, slightly less intuitive than SwiftUI's one-liner approach.

---

## 9. Accessibility

**Semantics tree:** Compose builds a parallel semantics tree that maps to platform accessibility services. The `semantics` modifier attaches semantic properties: `contentDescription`, `stateDescription`, `role`, `heading`, `testTag`.

**MergeDescendants:** Groups children into a single accessibility node (like SwiftUI's `accessibilityElement(children: .combine)`).

**Strengths:**
- Material components have correct accessibility out of the box (button announces as button, checkbox announces state)
- Semantics-based testing (query by content description, role) encourages accessible code
- Custom actions are well-supported
- TalkBack integration is mature

**Weaknesses:**
- Custom composables require manual semantics annotation
- Semantics tree debugging is less intuitive than UIKit's Accessibility Inspector
- Focus order for complex layouts can require explicit `focusOrder` or `traversalIndex` configuration

---

## 10. Input & Gestures

**Modifier-based:** `Modifier.clickable`, `Modifier.combinedClickable` (click + long-click + double-click), `Modifier.pointerInput` for raw pointer events. `detectTapGestures`, `detectDragGestures`, `detectTransformGestures` provide higher-level gesture detection.

**Focus system:** `FocusRequester`, `Modifier.focusRequester()`, `Modifier.focusProperties()`, `Modifier.focusTarget()`. Keyboard handling via `Modifier.onKeyEvent`.

**Strengths:**
- `pointerInput` provides access to raw pointer data when needed
- Gesture detection utilities cover common cases well
- Focus system is explicit and composable
- Touch target minimum sizes enforced by Material components (48dp)

**Weaknesses:**
- Gesture disambiguation between parent and child is complex
- No gesture arena concept (unlike Flutter) — nested gesture handlers use the modifier chain's hit-testing order
- Scroll + drag interaction conflicts require careful handling

---

## 11. Developer Experience

**Android Studio integration:**
- `@Preview` annotation renders composables directly in the IDE
- Layout Inspector shows the composition tree, recomposition counts, and modifier chains
- Composition Tracing in Android Studio Profiler
- Live Edit (hot reload equivalent) — preserves state across code changes

**Strengths:**
- `@Preview` is more reliable than Xcode's SwiftUI Canvas
- Recomposition count visualization helps identify performance issues
- Layout Inspector is genuinely useful for debugging
- Documentation quality is excellent (developer.android.com)
- Kotlin's tooling (autocomplete, refactoring, null safety) enhances DX

**Weaknesses:**
- Android Studio's memory consumption is high
- Live Edit has limitations (doesn't work for all code changes)
- Build times for large Compose projects can be slow (Kotlin/Native compilation for multiplatform)

---

## 12. Platform Reach & Ecosystem

**Android:** Primary target, fully mature.

**Compose Multiplatform (JetBrains):**
- **iOS:** Stable as of May 2025 (CMP 1.8). UI renders via Skia/Skiko (not UIKit). Native interop via `UIKitView`.
- **Desktop:** Stable (JVM-based). Window management, keyboard shortcuts, system tray.
- **Web:** Beta as of September 2025 (CMP 1.9). Canvas-based rendering (like Flutter web).

**Strengths:**
- Compose Multiplatform gives genuine cross-platform reach from a single Kotlin codebase
- Kotlin Multiplatform (KMP) for shared business logic is mature and Google-endorsed
- The same composable code runs on Android, iOS, desktop, and (beta) web

**Weaknesses:**
- CMP on iOS renders via Skia, not UIKit — apps don't feel fully native
- Web support is beta with canvas-based rendering (same limitations as Flutter web — no SEO, large download)
- Desktop apps via JVM have JVM startup overhead and higher memory baseline
- iOS interop requires platform-specific code for native features

**Sources:** [CMP 1.8: iOS Stable](https://blog.jetbrains.com/kotlin/2025/05/compose-multiplatform-1-8-0-released-compose-multiplatform-for-ios-is-stable-and-production-ready/), [CMP 1.9: Web Beta](https://blog.jetbrains.com/kotlin/2025/09/compose-multiplatform-1-9-0-compose-for-web-beta/)

---

## 13. Testing

**ComposeTestRule:** Official testing API. Uses the semantics tree for queries — `onNodeWithText("Hello")`, `onNodeWithContentDescription("Submit")`, `onNodeWithTag("my-tag")`. Tests run on JVM (no device needed) via `createComposeRule()`.

**Strengths:**
- Semantics-based testing encourages accessible code
- Tests are fast (JVM-based, no emulator)
- Rich assertion API (`assertIsDisplayed()`, `assertIsEnabled()`, `assertTextEquals()`)
- Official, first-party, well-documented
- Screenshot testing via `@ScreenshotTest` annotation (Compose 1.7+)

**Weaknesses:**
- Testing animated content requires `advanceTimeBy()` manipulation
- Robolectric integration has gaps for Compose-specific features
- Testing navigation flows requires additional setup

---

## 14. Error Handling & Resilience

**No error boundary concept.** A crash during composition (recomposition) crashes the app. There is no mechanism to catch errors at a subtree level and render fallback UI.

The only error handling is standard Kotlin try/catch, which doesn't work in `@Composable` functions (you can't catch a recomposition failure).

**Rating: D** — Same gap as SwiftUI. No framework-level error resilience.

---

## 15. Data Loading & Async

**Architecture:** Compose uses Kotlin coroutines as its async backbone. `LaunchedEffect(key)` launches a coroutine scoped to the composition — cancelled and relaunched when `key` changes. `rememberCoroutineScope()` provides a scope for event-driven launches. `produceState(initialValue) { ... }` combines `LaunchedEffect` with `mutableStateOf` into a single call.

**Capabilities:** `collectAsStateWithLifecycle()` collects a Kotlin `Flow` into Compose `State` and is lifecycle-aware — stops collection when UI is not visible. `SideEffect` runs after recomposition. `DisposableEffect` provides cleanup.

**Strengths:**
- Kotlin coroutines are the most powerful async runtime of any framework in this comparison
- `collectAsStateWithLifecycle()` prevents wasted work when UI is backgrounded
- `produceState` provides a clean single-call async-to-state bridge
- Lifecycle-scoped side effects prevent resource leaks

**Weaknesses:**
- No built-in tri-state (loading/success/error) primitive — developers use sealed classes
- No built-in `AsyncImage` (Coil fills this role)
- No equivalent to React's Suspense for declarative loading boundaries
- Side effect API surface is large (LaunchedEffect vs SideEffect vs DisposableEffect vs rememberCoroutineScope)

**Rating: A-** — Excellent coroutine integration; large API surface, no built-in loading patterns.

**Sources:** [Android Developers: Side-effects](https://developer.android.com/develop/ui/compose/side-effects)

---

## 16. Lists & Virtualization

**Architecture:** `LazyColumn`/`LazyRow` compose and lay out only visible items, analogous to `RecyclerView`. `LazyVerticalGrid` and `LazyVerticalStaggeredGrid` handle grids. Paging 3 library integrates via `collectAsLazyPagingItems()`.

**Capabilities:** Item `key` stabilizes identity. `contentType` tells lazy layouts which items share composition pools. Sticky headers via `stickyHeader {}`. Pull-to-refresh via Accompanist or Material 3's `pullToRefresh`.

**Strengths:**
- Lazy lists are built-in and performant — no external virtualization library needed
- `contentType` pool optimization reduces composition cost for heterogeneous lists
- Paging 3 integration is first-class for infinite scrolling
- Item animations have improved significantly

**Weaknesses:**
- Nested lazy lists are explicitly unsupported (`LazyColumn` inside `LazyColumn`)
- Less flexible than `RecyclerView` for some advanced scenarios (custom item animators)
- Variable-height items with scroll bar positioning can be imprecise

**Rating: A-** — Strong built-in virtualization; nested scrolling limitation.

---

## 17. Internationalization & Localization

**Architecture:** Standard Android resource system. `stringResource(R.string.key)` in composables. `strings.xml` per locale with CLDR plural categories via `<plurals>`. ICU MessageFormat via `android.icu.text.MessageFormat`.

**Strengths:**
- Mature Android resource system with tooling support
- CLDR plural rules built into `<plurals>` XML elements
- Automatic RTL when manifest declares `supportsRtl="true"`
- Per-app language preferences (Android 13+ API)
- Compose recomposes automatically on locale change

**Weaknesses:**
- XML resource files are verbose compared to SwiftUI's string catalogs
- ICU MessageFormat requires programmatic usage (not integrated into resource files)
- No compile-time validation of string format parameters

**Rating: B+** — Solid but verbose; platform-dependent.

---

## 18. Interop & Incremental Adoption

**Architecture:** `ComposeView` hosts Compose inside any XML layout, Fragment, or Activity. `AndroidView`/`AndroidViewBinding` embed existing Views inside composables. State bridges: `LiveData.observeAsState()`, `Flow.collectAsState()`.

**Strengths:**
- **Best incremental adoption story of any framework** — ComposeView drops into XML with zero architecture changes
- `AndroidView` wraps existing Views seamlessly
- Google benchmarks show <1ms overhead per frame for bridging
- `navigation-compose` mixes Compose and Fragment destinations in the same NavGraph
- Official migration strategy: screen-by-screen, leaf-first

**Weaknesses:**
- Mixing Compose and View system in the same screen can be complex for touch event handling
- Fragment lifecycle + Compose lifecycle interaction has edge cases
- Legacy libraries using custom Views may need `AndroidView` wrappers indefinitely

**Rating: A** — Best-in-class incremental adoption; minimal overhead.

**Sources:** [Android Developers: Interop APIs](https://developer.android.com/develop/ui/compose/migrate/interoperability-apis), [Android Developers: Migration Strategy](https://developer.android.com/develop/ui/compose/migrate/strategy)

---

## 19. Forms & Data Entry

**Architecture:** Controlled-component pattern — `TextField(value = text, onValueChange = { text = it })`. State hoisting is the canonical pattern. No built-in form or validation framework.

**Capabilities:** `OutlinedTextField` with `isError` parameter, `supportingText` for error messages. `BasicTextField` with `VisualTransformation` for masking. `FocusRequester` for programmatic focus. `Modifier.focusProperties { next = ... }` for custom traversal.

**Strengths:**
- Material 3 `TextField` has built-in error styling (`isError`, `supportingText`)
- `VisualTransformation` handles password masking and custom formatting cleanly
- `FocusRequester` + `focusProperties` enable explicit focus management
- State hoisting naturally separates form logic from UI

**Weaknesses:**
- **No built-in form or validation framework** — validation is entirely manual or ViewModel-based
- No equivalent to WPF's `INotifyDataErrorInfo`, Flutter's `Form.validate()`, or React Hook Form
- No transactional form editing (submit-all-or-nothing)
- Input masking beyond passwords requires custom `VisualTransformation` implementation

**Rating: B-** — Good individual controls; no form-level validation framework.

---

## Summary Ratings

| Category | Grade | Notes |
|---|---|---|
| Declarative Syntax | A | Kotlin DSL is excellent; trailing lambdas, named params |
| Component Architecture | A- | Functions-as-components is clean; positional memoization is surprising |
| State & Reactivity | A | Snapshot system is elegant; stability is a pain point |
| Rendering & Performance | B+ | Smart recomposition; stability/lambda traps; baseline profiles needed |
| Layout | A- | Powerful modifier system; order-dependence is a gotcha |
| Styling & Theming | A | Material 3 default; custom themes are first-class |
| Navigation | A | Navigation 3 is best-in-class; new and less battle-tested |
| Animation | A- | Comprehensive but large API surface |
| Accessibility | A- | Strong semantics tree; custom composables need manual work |
| Input & Gestures | B+ | Good coverage; gesture disambiguation is complex |
| Developer Experience | A- | Excellent tooling; build times can be slow |
| Platform Reach | B+ | CMP brings iOS/Desktop/Web; iOS/Web not fully native |
| Testing | A | First-party semantics-based testing; fast JVM execution |
| Error Handling | D | No error boundaries |
| Data Loading & Async | A- | Excellent coroutines; large side effect API surface |
| Lists & Virtualization | A- | Strong lazy lists; no nested scrolling |
| Internationalization | B+ | Solid Android resources; verbose, platform-dependent |
| Interop & Adoption | A | Best-in-class incremental adoption; <1ms overhead |
| Forms & Data Entry | B- | Good controls; no validation framework |

---

## Sources

- [Android Developers: Compose Performance](https://developer.android.com/develop/ui/compose/performance)
- [Diagnose Stability Issues](https://developer.android.com/develop/ui/compose/performance/stability/diagnose)
- [Announcing Navigation 3](https://android-developers.googleblog.com/2025/05/announcing-jetpack-navigation-3-for-compose.html)
- [Navigation 3 is Stable](https://android-developers.googleblog.com/2025/11/jetpack-navigation-3-is-stable.html)
- [CMP 1.8: iOS Stable](https://blog.jetbrains.com/kotlin/2025/05/compose-multiplatform-1-8-0-released-compose-multiplatform-for-ios-is-stable-and-production-ready/)
- [CMP 1.9: Web Beta](https://blog.jetbrains.com/kotlin/2025/09/compose-multiplatform-1-9-0-compose-for-web-beta/)
- [Top 5 Compose Pitfalls 2025](https://medium.com/@innerarchitecture/top-5-jetpack-compose-pitfalls-in-2025-and-proven-ways-to-avoid-them-234d26a8c2c6)
- [Compose Performance Checklist](https://medium.com/@nvineet02/jetpack-compose-performance-checklist-for-production-d697abe3f50c)
- [The Pitfall No One Talks About](https://medium.com/android-alchemy/the-jetpack-compose-performance-pitfall-no-one-talks-about-35702beb009a)
- [Say Goodbye to NavController](https://medium.com/@rodrigokirch/jetpack-compose-navigation-3-is-stable-say-goodbye-to-navcontroller-8afd20a21fa2)
- [Compose Multiplatform Roadmap](https://blog.jetbrains.com/kotlin/2025/08/kmp-roadmap-aug-2025/)
- [Is KMP Production-Ready in 2026?](https://volpis.com/blog/is-kotlin-multiplatform-production-ready/)
- [Revisiting Compose Performance 2025](https://a64.in/posts/revisiting-compose-perf-land-2025/)
- [compose-performance GitHub](https://github.com/skydoves/compose-performance)
- [Android Developers: Side-effects](https://developer.android.com/develop/ui/compose/side-effects)
- [Android Developers: Interop APIs](https://developer.android.com/develop/ui/compose/migrate/interoperability-apis)
- [Android Developers: Lists](https://developer.android.com/develop/ui/compose/lists)
