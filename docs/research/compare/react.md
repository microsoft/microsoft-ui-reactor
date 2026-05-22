# React — Framework Analysis

**Purpose:** Critical technical analysis for comparison against Microsoft UI frameworks (WinForms, WPF, WinUI 3, Microsoft.UI.Reactor).

**Version analyzed:** React 19.x (2025-2026 era)

---

## Overview

React is Meta's JavaScript library for building user interfaces, first open-sourced in 2013. It pioneered the component model and virtual DOM approach that influenced every subsequent declarative UI framework. As of 2026, React is the most widely adopted frontend framework: 44.7% of developers in the Stack Overflow 2025 survey, ~85M weekly npm downloads, and 216K+ GitHub stars. React 19 (released late 2024) introduced Server Components, Actions, and the React Compiler.

React is fundamentally a **library**, not a full framework — it provides component rendering and state management but delegates routing, styling, animation, and data fetching to the ecosystem. This is both its greatest strength (flexibility, ecosystem breadth) and greatest weakness (fragmentation, decision fatigue).

---

## 1. Declarative Model & Syntax

**JSX:** React uses JSX, a syntax extension that embeds XML-like markup in JavaScript. JSX transpiles to function calls — `<div className="foo">` becomes `jsx("div", { className: "foo" })`. Since React 17, the JSX transform no longer requires importing React in every file.

**Strengths:**
- JSX co-locates markup with logic in a single file — what you see is what renders
- Conditional rendering uses native JavaScript (`ternary`, `&&`, `switch`)
- List rendering uses `.map()` — standard JS, no special directive
- Fragments (`<>...</>`) avoid unnecessary wrapper DOM nodes
- Full JavaScript expressiveness — any JS expression works inside `{}`
- TypeScript integration is mature and first-class

**Weaknesses:**
- JSX is not HTML — different attribute names (`className` vs `class`, `htmlFor` vs `for`)
- Complex conditional trees become deeply nested and hard to read
- No compile-time view validation (unlike SwiftUI's type-checked body or Compose's compiler plugin)
- JSX produces runtime objects (virtual DOM nodes) that must be diffed — compile-time frameworks (Svelte, Solid) avoid this overhead entirely

**Sources:** [Babel JSX Transform Plugin](https://babeljs.io/docs/babel-plugin-transform-react-jsx), [New JSX Transform](https://legacy.reactjs.org/blog/2020/09/22/introducing-the-new-jsx-transform.html)

---

## 2. Component Architecture

**Function components:** The modern component model. Components are functions that accept `props` and return JSX. Hooks (`useState`, `useEffect`, `useMemo`, `useCallback`, `useRef`, `useContext`) manage state and side effects. Custom hooks enable logic extraction and reuse.

**Key patterns:**
- **React.memo:** HOC that prevents re-render when props are unchanged (shallow compare)
- **forwardRef:** Required to pass refs through component boundaries (simplified in React 19 — `ref` is now a regular prop)
- **Compound components:** Parent + children share implicit state via Context
- **Render props and HOCs:** Legacy patterns superseded by hooks but still in use

**Strengths:**
- Function components are maximally simple — just a function
- Custom hooks enable powerful logic reuse without UI coupling
- The composition model is flexible and well-understood
- Largest body of patterns, tutorials, and community knowledge of any UI framework

**Weaknesses:**
- **Hooks mental model has a steep learning curve** — closures over stale state, dependency arrays in useEffect, the need for useCallback/useMemo
- **Rules of Hooks** (call at top level, no conditionals) are unintuitive restrictions
- Error boundaries still require class components — no hook-based error boundary exists in core React
- The React Compiler (v1.0, Oct 2025) auto-memoizes but silently skips components it can't optimize

**Sources:** [React Design Patterns](https://refine.dev/blog/react-design-patterns/), [React Compiler v1.0](https://react.dev/blog/2025/10/07/react-compiler-1)

---

## 3. State Management & Reactivity

**Built-in:** `useState` (local), `useReducer` (complex local with actions), `useContext` (tree-scoped shared state). Context re-renders all consumers on any change, making it unsuitable for high-frequency global state.

**React 19 additions:** `useActionState` (async action lifecycle), `useOptimistic` (optimistic UI), `use()` (unwrap promises/context in render). Server Components eliminate client-side state for read-only data.

**External ecosystem (massive):**
- **Redux Toolkit:** Strict unidirectional flow, time-travel debugging. Enterprise standard
- **Zustand:** ~1KB, no providers, selective subscriptions. Dominant for mid-size apps in 2025
- **Jotai:** Atomic state (bottom-up). Independent atoms
- **MobX:** Observable-based, mutable state with automatic tracking (closest to WPF/MVVM's INotifyPropertyChanged)

**Strengths:**
- Ecosystem offers a solution for every state management philosophy
- Zustand and Jotai provide elegant, minimal APIs
- Server Components eliminate entire categories of client state
- React Compiler reduces the need for manual memoization

**Weaknesses:**
- **20+ state management libraries is fragmentation, not abundance** — every team must make an architectural decision React itself defers
- `useContext` is too coarse-grained for performant global state (no selectors)
- No built-in reactive state model (unlike Compose's Snapshot system or SwiftUI's @Observable)
- Dependency array bugs in `useEffect` are the #1 source of React bugs in production

**Sources:** [State Management in 2025](https://dev.to/hijazi313/state-management-in-2025-when-to-use-context-redux-zustand-or-jotai-2d2k), [React v19](https://react.dev/blog/2024/12/05/react-19)

---

## 4. Rendering & Performance

**React Fiber:** The reconciliation engine (since React 16). Represents the component tree as a linked list of fiber nodes. Enables incremental rendering — work can be paused, resumed, aborted, or prioritized.

**Virtual DOM diffing:** O(n) heuristic. Two assumptions: (1) different element types produce different trees, (2) `key` props identify stable elements in lists.

**Concurrent rendering (React 18+):** React yields to the browser every ~5ms, preventing long renders from blocking interaction. `startTransition` marks updates as low-priority. `useDeferredValue` defers expensive re-renders.

**React Compiler (v1.0, Oct 2025):** Auto-memoizes components and values at build time. Meta reports 12% faster initial loads and 2.5x faster interactions. Eliminates ~90% of manual useMemo/useCallback/React.memo.

**Strengths:**
- Concurrent rendering is a genuine architectural advantage — no other framework has priority-based interruptible rendering
- React Compiler dramatically reduces the memoization burden
- Suspense provides declarative loading states
- Server Components eliminate hydration cost for server-rendered content

**Weaknesses:**
- **Virtual DOM overhead is real and measurable** — every render creates a new tree, diffs it, patches the DOM. Svelte and SolidJS bypass this entirely. React 19 took 47ms for operations Svelte 5 completed significantly faster
- React's compressed bundle is ~42KB vs Svelte 5's 3-10KB
- Hydration cost for SSR apps is significant
- The Compiler silently skips components it can't optimize — edge cases still require manual tuning

**Sources:** [React Fiber Architecture](https://github.com/acdlite/react-fiber-architecture), [React Compiler at Meta](https://www.infoq.com/news/2025/12/react-compiler-meta/), [React 19 vs Svelte 5 Benchmark](https://www.sitepoint.com/react-19-compiler-vs-svelte-5-virtual-dom-latency-benchmark/)

---

## 5. Layout System

**Web React:** Delegates entirely to CSS. No built-in layout primitives. Developers use CSS Flexbox, CSS Grid, or utility frameworks (Tailwind). Layout is browser-managed, not framework-managed.

**React Native:** Uses Yoga, Meta's C++ Flexbox engine. Yoga 3.0 (2025) improved web-standards compliance. Layout is specified via style objects: `{ flexDirection: 'row', gap: 8 }`.

**Strengths:**
- Full CSS ecosystem compatibility on web (any CSS technique works)
- CSS Grid and Flexbox are more capable than any framework-specific layout system
- Tailwind CSS provides rapid utility-class-based layout

**Weaknesses:**
- **No framework-level layout abstraction** — React has zero opinions about layout
- CSS-in-JS solutions add complexity and runtime cost
- React Native's Yoga historically lacked CSS Grid (experimental in late 2025)
- No responsive breakpoint system built in (unlike WinUI3's AdaptiveTrigger or SwiftUI's ViewThatFits)

---

## 6. Styling & Theming

**No built-in system.** The ecosystem provides:
- **CSS Modules:** Scoped CSS, zero runtime cost
- **Tailwind CSS:** Utility-first, dominant in 2025-2026, zero runtime
- **styled-components / Emotion:** CSS-in-JS with runtime injection — **falling out of favor** due to performance overhead and RSC incompatibility
- **Theming:** Implemented via React Context (`<ThemeProvider>`) regardless of approach

**The CSS-in-JS retreat:** The industry moved from runtime CSS-in-JS back to static CSS after recognizing runtime overhead. One team reported build times dropping from 12s to 1.2s switching from styled-components to Tailwind v4.

**Strengths:**
- Maximum flexibility — use any CSS approach
- Tailwind + CSS Modules give zero-runtime, scoped styling
- CSS custom properties provide native theming without JavaScript

**Weaknesses:**
- **No default styling or theming** — every project must choose and configure a styling approach
- No design system (unlike Material for Compose/Flutter or Apple's design language for SwiftUI)
- Theme provider pattern is a community convention, not a framework feature

**Sources:** [CSS-in-JS Trends 2025](https://jeffbruchado.com.br/en/blog/css-in-js-2025-tailwind-styled-components-trends)

---

## 7. Navigation

**No built-in router.** Options:
- **React Router (v7):** De facto standard. Nested routes, data loaders, actions. v7 added framework mode with file-based routing and SSR
- **TanStack Router:** Fully type-safe routing (route params, search params validated at compile time). Built-in SWR caching. Growing rapidly
- **Next.js App Router:** File-system-based routing with nested layouts, Server Components integration, parallel routes

**Strengths:**
- TanStack Router's type safety rivals Compose Navigation 3
- Data loader pattern (fetch data in parallel with route rendering) is architecturally sound
- Mature, battle-tested solutions exist

**Weaknesses:**
- **Router fragmentation is a real problem** — React Router, TanStack Router, and Next.js's router have fundamentally different architectures and are not interchangeable
- No official, blessed router from the React team
- Deep linking is web-native (URLs) but requires framework-specific configuration for SSR

**Sources:** [Router Comparison](https://thenewstack.io/next-js-react-router-tanstack-when-to-use-each/), [TanStack Router](https://tanstack.com/router/latest)

---

## 8. Animation

**No built-in animation system.** Options:
- **Motion (formerly Framer Motion):** Dominant library. Declarative API, layout animations, exit animations (AnimatePresence), gestures, scroll-linked. Rebranded 2025
- **React Spring:** Physics-based. Animates outside React's render cycle
- **CSS transitions/animations:** Zero-JS, limited to CSS-expressible

**Strengths:**
- Motion library is genuinely excellent — declarative, composable, performant
- Layout animations (automatic FLIP) are best-in-class
- AnimatePresence solves exit animations elegantly

**Weaknesses:**
- **Animation fundamentally fights React's render model** — React wants to own the DOM, but performant animation requires direct DOM manipulation
- Exit animations are awkward because React removes DOM nodes synchronously — libraries must work around this
- No framework-level animation support means yet another dependency decision

**Sources:** [Motion Docs](https://motion.dev/docs/react)

---

## 9. Accessibility

**Architecture:** React renders to HTML, so accessibility follows web standards — semantic HTML, ARIA attributes, keyboard events.

**Tooling:** `eslint-plugin-jsx-a11y` provides static analysis for common a11y violations.

**Strengths:**
- Semantic HTML is the accessibility model — the most mature and well-understood accessibility system
- The web platform's accessibility infrastructure is the gold standard
- React Aria (Adobe) and Radix UI provide accessible component primitives

**Weaknesses:**
- **React provides no accessibility primitives** beyond what HTML offers
- Building accessible custom components (comboboxes, date pickers, dialogs) from scratch is extremely difficult — libraries like React Aria exist specifically for this
- No automatic accessibility — everything must be explicitly coded
- No accessibility linting in the framework itself (eslint plugin is third-party)

---

## 10. Input & Gestures

**Synthetic events:** React wraps native DOM events for cross-browser consistency. Event delegation to root container (since React 17).

**Controlled vs uncontrolled inputs:** Controlled (`value={state}` + `onChange`) gives React full control. Uncontrolled (`useRef`) reads DOM directly. React 19's `<form action={fn}>` adds form actions with built-in pending state.

**Strengths:**
- Web events are standardized and well-documented
- Form actions (React 19) simplify form handling significantly
- Controlled inputs provide predictable state management

**Weaknesses:**
- **No built-in gesture system** — `@use-gesture/react` is required for drag, pinch, hover
- Synthetic event wrapper adds a layer of indirection
- For performance-critical scenarios (canvas, games), developers bypass React's event system

**Sources:** [use-gesture Library](https://github.com/pmndrs/use-gesture)

---

## 11. Developer Experience

**Tooling:**
- **React DevTools:** Component tree, props, state, hooks inspection, profiling
- **StrictMode:** Double-invokes renders/effects to surface impure code bugs
- **Fast Refresh:** Preserves component state across edits (in Vite, Next.js)
- **Create React App:** Officially deprecated Feb 2025. Replaced by Vite, Next.js, React Router v7 framework mode

**React Compiler (Oct 2025):** Auto-memoization eliminates most manual performance optimization. DevTools 5.2 shows "Memo" badges for compiler-optimized components.

**Strengths:**
- DevTools are the gold standard for component inspection
- Fast Refresh is reliable and state-preserving
- TypeScript support is mature with excellent type definitions
- react.dev documentation is excellent (interactive tutorials, hooks-first)

**Weaknesses:**
- **Project setup requires choosing a meta-framework** — no simple "just React" starting point after CRA deprecation
- StrictMode's double-invocation confuses beginners
- The ecosystem moves fast — patterns and libraries change yearly

**Sources:** [Sunsetting CRA](https://react.dev/blog/2025/02/14/sunsetting-create-react-app), [State of React Community 2025](https://blog.isquaredsoftware.com/2025/06/react-community-2025/)

---

## 12. Platform Reach & Ecosystem

**Platforms:**
- **Web** (primary) — React DOM
- **Mobile/Desktop** — React Native (iOS, Android, Windows, macOS). Powers 2B+ users including Meta's apps. New Architecture (JSI, Fabric, TurboModules) stable in 0.84 (2025)
- **3D** — React Three Fiber
- **Server** — Server Components render on server with zero client JS

**Ecosystem scale:** npm has more React packages than any other framework. For almost any need, multiple React libraries exist.

**Strengths:**
- **Largest ecosystem and hiring pool of any UI framework** — 85M+ weekly npm downloads
- React Native provides genuine cross-platform mobile/desktop
- Server Components are a paradigm shift for web performance

**Weaknesses:**
- React Native still has a "bridge" reputation despite New Architecture improvements
- Web-centric — React Native's desktop story is less mature than Flutter's
- Ecosystem abundance creates decision fatigue and fragmentation

---

## 13. Testing

**Philosophy:** React Testing Library (RTL) promotes "test behavior, not implementation" — query by accessible roles and text, not component internals.

**Stack:**
- **Vitest:** Modern, fast. Preferred for new projects in 2025
- **Jest:** Mature, widely used, slower
- **Playwright / Cypress:** E2E testing
- **Snapshot testing:** Serializes output to text files; fragile and often rubber-stamped

**Strengths:**
- RTL's philosophy produces tests that resemble how users interact with the app
- Vitest provides near-instant test execution
- The testing ecosystem is the most mature of any framework

**Weaknesses:**
- Testing async behavior (Suspense, transitions, Server Components) is complex
- Server Components are particularly difficult to unit test (server context)
- No visual/golden testing built in (third-party tools required)

---

## 14. Error Handling & Resilience

**Error Boundaries:** Class components implementing `getDerivedStateFromError` and/or `componentDidCatch`. They catch errors during rendering, lifecycle methods, and constructors of their subtree.

**Strengths:**
- **React is the only major framework with a built-in error boundary concept** — this is a genuine differentiator
- Error boundaries prevent a single component crash from taking down the entire app
- `react-error-boundary` package provides a function component API with `useErrorBoundary` hook

**Weaknesses:**
- Error boundaries must be class components — no hook-based alternative in core React
- Don't catch errors in event handlers, async code, or SSR
- Since React 16, uncaught errors unmount the entire React tree — intentional but aggressive

**Rating: B+** — Error boundaries are a significant advantage, tempered by the class-component requirement and scope limitations.

---

## 15. Data Loading & Async

**Architecture:** React's async story has evolved dramatically. **Suspense** lets components "suspend" while data loads — a `<Suspense fallback={<Spinner/>}>` boundary catches the suspension. React 19's `use()` hook unwraps Promises directly in render, integrating with Suspense for loading and Error Boundaries for errors. `useTransition` marks updates as non-urgent, keeping the UI interactive. **Server Components** are async by default — they `await` directly during server rendering.

**Ecosystem:** TanStack Query and SWR provide declarative data fetching hooks with automatic caching, background refetching, stale-while-revalidate, deduplication, retry logic, and `{ data, error, isLoading }` return types.

**Strengths:**
- **Suspense + Error Boundaries is the most elegant declarative loading/error pattern** of any framework
- Server Components eliminate entire categories of client-side async complexity
- TanStack Query's caching and deduplication are best-in-class
- `useTransition` keeps UI responsive during heavy async updates

**Weaknesses:**
- Built-in primitives (Suspense, `use()`) still require a framework (Next.js) for full data fetching integration
- The number of async patterns (useEffect, Suspense, Server Components, TanStack Query, SWR) creates decision fatigue
- Thread marshaling is N/A (single-threaded), which limits CPU-bound async offloading

**Rating: A** — Most comprehensive async story when ecosystem is included; Suspense is genuinely innovative.

**Sources:** [React: Suspense](https://react.dev/reference/react/Suspense), [React: use](https://react.dev/reference/react/use), [TanStack Query](https://tanstack.com/query/latest)

---

## 16. Lists & Virtualization

**Architecture:** React provides **no built-in virtualization**. For large lists:
- **TanStack Virtual:** Headless, framework-agnostic, ~10-15KB. Handles 1M+ items. Most popular in 2025
- **react-virtuoso:** Automatic dynamic height measurement, infinite scrolling, sticky headers
- **react-window:** Lightweight, fixed/variable-size lists and grids

**Strengths:**
- TanStack Virtual's headless approach is maximally flexible
- The `key` prop mechanism for list identity is well-designed
- Ecosystem solutions handle complex cases (variable heights, infinite scroll, grids)

**Weaknesses:**
- **No built-in virtualization is a genuine gap** — every team must evaluate and integrate a third-party solution
- Without virtualization, rendering 10,000+ items will freeze the UI
- React's O(n) virtual DOM diffing makes large flat lists expensive even with keys
- No built-in Section/grouping concept (unlike SwiftUI/Flutter)

**Rating: C+** — Ecosystem compensates, but the lack of any built-in solution is a real weakness.

**Sources:** [TanStack Virtual](https://tanstack.com/virtual/latest)

---

## 17. Internationalization & Localization

**Architecture:** No built-in i18n. Ecosystem libraries:
- **react-intl (FormatJS):** ICU MessageFormat with `<FormattedMessage>` components and `useIntl()` hook
- **react-i18next:** `useTranslation()` with `t('key')` function, CLDR plurals via key suffixes
- **next-intl:** For Next.js, with server-side locale detection

**Strengths:**
- ICU MessageFormat support (via FormatJS) handles plural, select, gender with full CLDR
- Runtime locale switching via React Context
- `Intl` ECMAScript API provides native date/number formatting
- Ecosystem solutions are mature and well-documented

**Weaknesses:**
- No built-in i18n — yet another library decision
- RTL is a CSS concern (`direction: rtl`) with no framework abstraction
- No compile-time validation of i18n keys or message parameters

**Rating: B** — Mature ecosystem solutions; no built-in support; RTL is manual.

---

## 18. Interop & Incremental Adoption

**Architecture:** `createRoot(container).render(<App/>)` mounts React into any DOM element. Multiple independent React roots coexist on a page. `createPortal` renders into external DOM nodes. Web Components interop (with caveats). React Native uses JSI (JavaScript Interface) for synchronous C++ native bindings, TurboModules for type-safe native access, and Fabric for direct native view management.

**Strengths:**
- **Most flexible adoption story** — mount React into any existing page, framework, or CMS
- Multiple independent React roots with no conflicts
- `useSyncExternalStore` bridges non-React state stores
- Module Federation enables micro-frontends with shared React dependencies
- React Native's JSI eliminates bridge serialization overhead

**Weaknesses:**
- Multiple React roots have independent reconcilers with no shared scheduling
- Web Components interop has rough edges (event handling, property vs attribute)
- React Native interop still requires platform-specific code for many native features

**Rating: A** — Most flexible mount/embed story of any framework.

**Sources:** [React: createRoot](https://react.dev/reference/react-dom/client/createRoot), [React: createPortal](https://react.dev/reference/react-dom/createPortal)

---

## 19. Forms & Data Entry

**Architecture:** Controlled components (`value={state}` + `onChange`), uncontrolled components (`useRef`), and React 19 form actions (`<form action={fn}>`). No built-in validation. Ecosystem: React Hook Form (40M+ weekly npm downloads), Formik, Zod/Yup for schema validation.

**Strengths:**
- **React Hook Form + Zod is the most productive form solution** across any framework — type-safe schema validation, minimal re-renders, excellent DX
- React 19 form actions with `useActionState` simplify server-side form handling
- Native HTML5 validation attributes work directly
- `useFormStatus` lets child components read submission state

**Weaknesses:**
- No built-in form or validation framework — requires third-party for any non-trivial form
- Controlled components cause re-render on every keystroke (React Hook Form works around this with refs)
- No built-in input masking
- Focus management is manual via refs

**Rating: B+** — Ecosystem solutions are excellent (React Hook Form + Zod); nothing built-in.

**Sources:** [React Hook Form](https://react-hook-form.com/), [React: form actions](https://react.dev/reference/react-dom/components/form)

---

## Summary Ratings

| Category | Grade | Notes |
|---|---|---|
| Declarative Syntax | A- | JSX is expressive and well-known; not HTML, no compile-time validation |
| Component Architecture | A- | Function components + hooks; steep hook learning curve |
| State & Reactivity | B+ | Built-in is basic; ecosystem fills gaps but is fragmented |
| Rendering & Performance | B+ | Concurrent rendering is unique; virtual DOM overhead is real |
| Layout | B+ | Delegates to CSS (most powerful layout on web); no framework opinion |
| Styling & Theming | B | No built-in; ecosystem solutions are good but add decisions |
| Navigation | B+ | TanStack Router is excellent; fragmented ecosystem |
| Animation | B | Motion library is great; no built-in support |
| Accessibility | B | Web a11y standards are mature; React adds nothing on top |
| Input & Gestures | B | Web events plus libraries; no built-in gesture system |
| Developer Experience | A | Best-in-class DevTools, docs, TypeScript; setup complexity |
| Platform Reach | A | Web + RN + SSR + 3D; largest ecosystem |
| Testing | A- | RTL philosophy is right; async/RSC testing is hard |
| Error Handling | B+ | Error boundaries are unique; class-component-only limitation |
| Data Loading & Async | A | Suspense + TanStack Query; ecosystem-dependent |
| Lists & Virtualization | C+ | No built-in; TanStack Virtual fills the gap |
| Internationalization | B | Mature libraries; nothing built-in; manual RTL |
| Interop & Adoption | A | Mount into anything; most flexible adoption |
| Forms & Data Entry | B+ | React Hook Form + Zod is excellent; nothing built-in |

---

## Sources

- [React v19](https://react.dev/blog/2024/12/05/react-19)
- [React Compiler v1.0](https://react.dev/blog/2025/10/07/react-compiler-1)
- [React Compiler at Meta](https://www.infoq.com/news/2025/12/react-compiler-meta/)
- [React 19 vs Svelte 5 Benchmark](https://www.sitepoint.com/react-19-compiler-vs-svelte-5-virtual-dom-latency-benchmark/)
- [State Management in 2025](https://dev.to/hijazi313/state-management-in-2025-when-to-use-context-redux-zustand-or-jotai-2d2k)
- [CSS-in-JS Trends 2025](https://jeffbruchado.com.br/en/blog/css-in-js-2025-tailwind-styled-components-trends)
- [Router Comparison](https://thenewstack.io/next-js-react-router-tanstack-when-to-use-each/)
- [TanStack Router](https://tanstack.com/router/latest)
- [Motion Docs](https://motion.dev/docs/react)
- [use-gesture Library](https://github.com/pmndrs/use-gesture)
- [Sunsetting CRA](https://react.dev/blog/2025/02/14/sunsetting-create-react-app)
- [State of React Community 2025](https://blog.isquaredsoftware.com/2025/06/react-community-2025/)
- [React Fiber Architecture](https://github.com/acdlite/react-fiber-architecture)
- [React Design Patterns](https://refine.dev/blog/react-design-patterns/)
- [SolidJS vs React 2026](https://www.boundev.com/blog/solidjs-vs-react-2026-performance-guide)
- [Babel JSX Transform](https://babeljs.io/docs/babel-plugin-transform-react-jsx)
- [New JSX Transform](https://legacy.reactjs.org/blog/2020/09/22/introducing-the-new-jsx-transform.html)
- [React: Suspense](https://react.dev/reference/react/Suspense)
- [TanStack Virtual](https://tanstack.com/virtual/latest)
- [TanStack Query](https://tanstack.com/query/latest)
- [React Hook Form](https://react-hook-form.com/)
