---
name: reactor
description: "Read the Reactor docs instead of answering from memory. Declarative UI framework for native Windows desktop apps in pure C#. Use when a task involves Reactor: how it works, how to set it up or configure it, or its API."
---

# Reactor

Declarative UI framework for native Windows desktop apps in pure C#.

This skill points you at the Reactor docs, at https://microsoft.github.io/microsoft-ui-reactor/main. When a task involves Reactor, read the relevant page before relying on what you remember: the docs are the source of truth, and they change.

## Reading the docs

- Any page as Markdown: add `.md` to its URL (the home page is https://microsoft.github.io/microsoft-ui-reactor/main/index.md). Every page link below already points at the Markdown.
- [llms.txt](https://microsoft.github.io/microsoft-ui-reactor/main/llms.txt): every page with a one-line summary.
- [llms-full.txt](https://microsoft.github.io/microsoft-ui-reactor/main/llms-full.txt): every page in one file, for when you need all of it.
- [JSON API](https://microsoft.github.io/microsoft-ui-reactor/main/api/docs/pages.json): the page index, with each page as JSON and Markdown.

## Pages

### Controls

- [Controls](https://microsoft.github.io/microsoft-ui-reactor/main/controls.md): Every visible thing on screen is a control. In Microsoft.UI.Reactor (Reactor) a control is the return value of a factory like TextBlock(...) or Button(...) — a plain element you compose, chain modifiers onto, and return from Render().
- [Forms and Input](https://microsoft.github.io/microsoft-ui-reactor/main/controls/forms.md): Every form control in Reactor follows the controlled-input pattern: you own the value, you provide the change handler, and the control reflects your state. There is no two-way binding. The data always flows one direction.
- [Collections](https://microsoft.github.io/microsoft-ui-reactor/main/controls/collections.md): When you need to render a list of data, Reactor provides three typed collection elements and a simple ForEach helper.
- [Text and Media](https://microsoft.github.io/microsoft-ui-reactor/main/controls/text-and-media.md): This page covers display text, inline rich text, rich-text editing, and media controls in Reactor.
- [Status and Info](https://microsoft.github.io/microsoft-ui-reactor/main/controls/status-and-info.md): Microsoft.UI.Reactor (Reactor)'s status and info controls report what the app is doing without stealing focus. Reach for them when the user needs to know something — a save succeeded, a long operation is running, a feature exists — and doesn't need to decide anything.
- [Dialogs and Flyouts](https://microsoft.github.io/microsoft-ui-reactor/main/controls/dialogs-and-flyouts.md): ContentDialog, MenuFlyout, CommandBarFlyout and Popup: when to reach for each one, and how to drive them from component state.
- [Data System](https://microsoft.github.io/microsoft-ui-reactor/main/controls/data-system.md): Reactor's data system provides a virtualized DataGrid<T> backed by a pluggable data source abstraction. You define columns (or auto-generate them), connect a data source, and the grid handles sorting, filtering, searching, selection, and inline editing.
- [Charting](https://microsoft.github.io/microsoft-ui-reactor/main/controls/charting.md): ReactorD3 brings data visualization to Microsoft.UI.Reactor (Reactor). The chart DSL provides high-level LineChart, BarChart, AreaChart, and PieChart factories that produce standard Reactor elements. You bind data, configure appearance with a fluent API, and the chart renders as native WinUI shapes on a Canvas.
- [Win2D canvas](https://microsoft.github.io/microsoft-ui-reactor/main/controls/win2d-canvas.md): Reactor.Advanced exposes the three Win2D canvas controls as Reactor elements. Add the package when you need immediate-mode drawing.

### Get started

- [Reactor](https://microsoft.github.io/microsoft-ui-reactor/main/get-started.md): Microsoft.UI.Reactor (Reactor) is a declarative UI framework for building native Windows desktop apps in pure C#. No XAML. No data binding. No view models. You describe your UI as a function of state, and Reactor keeps the screen in sync.
- [Getting Started with Reactor](https://microsoft.github.io/microsoft-ui-reactor/main/get-started/quickstart.md): Install Reactor, scaffold a project, and grow it from hello world to a todo list and a calculator.
- [Thinking in Reactor](https://microsoft.github.io/microsoft-ui-reactor/main/get-started/thinking-in-reactor.md): Coming soon — this page is the mental-model essay that frames Microsoft.UI.Reactor (Reactor) for new readers: UI as a function of state, why there is no XAML, when to reach for hooks, and how components compose.
- [Reactor for XAML Developers](https://microsoft.github.io/microsoft-ui-reactor/main/get-started/xaml-developers.md): If you already know XAML, Reactor is not a different Windows UI stack. It still renders real WinUI controls. The shift is in how you describe the UI: instead of splitting a page across XAML, bindings, converters, and code-behind, you return the UI directly from C# and let Reactor keep the native control tree in sync.
- [Reactor vs XAML](https://microsoft.github.io/microsoft-ui-reactor/main/get-started/reactor-vs-xaml.md): This page is for the XAML developer who wants the why behind Reactor's design.
- [Cheat Sheet](https://microsoft.github.io/microsoft-ui-reactor/main/get-started/cheat-sheet.md): Microsoft.UI.Reactor (Reactor) at a glance. Every row links to the page that covers it in depth.
- [Rules of Reactor](https://microsoft.github.io/microsoft-ui-reactor/main/get-started/rules-of-reactor.md): Microsoft.UI.Reactor (Reactor)'s render loop has a small set of invariants. Most are enforced by analyzers, so a violation surfaces at build time with a specific code; the rest are conventions that the framework expects. This page lists them, names the analyzer where one exists, and shows the before/after pair so the catch is visible.

### Guides

#### Core concepts

- [Components](https://microsoft.github.io/microsoft-ui-reactor/main/guides/components.md): Components are the building blocks of a Reactor app. Each component is a class with a Render() method that returns an element tree describing its UI.
- [Hooks](https://microsoft.github.io/microsoft-ui-reactor/main/guides/hooks.md): Hooks are functions you call inside Render() to manage state, side effects, and memoization. They replace the need for view models, event handlers, and lifecycle methods.
- [Effects and Lifecycle](https://microsoft.github.io/microsoft-ui-reactor/main/guides/effects.md): UseEffect runs side effects — code that reaches outside the component's render function. Timers, data fetching, subscriptions, and DOM manipulation all belong in effects, not in Render().
- [Context](https://microsoft.github.io/microsoft-ui-reactor/main/guides/context.md): Context lets you pass data through the component tree without threading it through every level of props. Define a Context<T>, provide a value at any level, and any descendant can read it with UseContext.
- [Commanding](https://microsoft.github.io/microsoft-ui-reactor/main/guides/commanding.md): A Command bundles an action with its metadata. Define it once and use it across buttons, menus, toolbars, and dialogs — the metadata stays consistent everywhere.
- [Advanced Patterns](https://microsoft.github.io/microsoft-ui-reactor/main/guides/advanced.md): Escape hatches for the few jobs the declarative model doesn't fit: error boundaries, memoized subtrees, element refs, direct property writes, view-model bridges, and custom hooks.

#### Layout and styling

- [Layout](https://microsoft.github.io/microsoft-ui-reactor/main/guides/layout.md): Reactor provides a small set of layout primitives that compose to build any screen structure. Every layout element accepts children and optional spacing.
- [Flex Layout](https://microsoft.github.io/microsoft-ui-reactor/main/guides/flex-layout.md): FlexPanel is a Yoga-backed CSS Flexbox implementation, exposed through the FlexRow and FlexColumn factories. Reach for it when the layout needs alignment control, wrapping, or proportional sizing that VStack and HStack cannot express.
- [Styling and Theming](https://microsoft.github.io/microsoft-ui-reactor/main/guides/styling.md): Color and style your UI with theme tokens, named-style modifiers, and resource overrides that follow the Light, Dark, and high-contrast themes automatically.
- [Theming Tokens](https://microsoft.github.io/microsoft-ui-reactor/main/guides/theming-tokens.md): Reactor exposes 36 named theme tokens on the static Theme class plus a Theme.Ref(string) escape hatch for any XAML resource key.
- [Animation](https://microsoft.github.io/microsoft-ui-reactor/main/guides/animation.md): Reactor animations are declarative. You set the target value (opacity, scale, translation) and attach a transition modifier.
- [Input and gestures](https://microsoft.github.io/microsoft-ui-reactor/main/guides/input-and-gestures.md): Reactor's input surface is declarative: attach handlers via .On* modifiers and the reconciler wires WinUI events, auto-enables the matching flags (for example IsDoubleTapEnabled), and survives re-renders without re-subscribing the underlying WinUI event.

#### Building apps

- [Navigation](https://microsoft.github.io/microsoft-ui-reactor/main/guides/navigation.md): Reactor uses a stack-based navigation model with type-safe routes. You define your routes as an enum, create a navigation handle with UseNavigation, and render the current page with NavigationHost.
- [Windows](https://microsoft.github.io/microsoft-ui-reactor/main/guides/windows.md): Most Microsoft.UI.Reactor (Reactor) apps start with the single window created by ReactorApp.Run. Larger desktop apps can open multiple native WinUI top-level windows with WindowSpec and ReactorApp.OpenWindow, while keeping the same declarative component model used inside a page.
- [Advanced Windowing](https://microsoft.github.io/microsoft-ui-reactor/main/guides/windowing-advanced.md): Some shell-window ideas are possible on Windows only by leaving the safe Reactor contract. We document the recipe so expert apps can make an informed choice, but Reactor does not ship these primitives as first-class WindowSpec fields.
- [Docking Windows](https://microsoft.github.io/microsoft-ui-reactor/main/guides/docking.md): Microsoft.UI.Reactor's docking system lets a single shell host multiple user-rearrangeable surfaces — the Visual Studio / VS Code / Photoshop / Figma layout idiom. Users drag tabs between groups, split panes, pin tool windows to a side, and tear panes out into floating sub-windows; layouts persist across sessions.
- [Async Resources](https://microsoft.github.io/microsoft-ui-reactor/main/guides/async-resources.md): Microsoft.UI.Reactor (Reactor)'s async hooks — UseResource, UseInfiniteResource, and UseMutation — replace the "one UseState for data, one for loading, one for error, one UseEffect to tie them together" pattern. They own the fetch lifecycle: cancellation on deps-change, drop late results after unmount, shared caching across siblings, and stale-while-revalidate by default.
- [Persistence](https://microsoft.github.io/microsoft-ui-reactor/main/guides/persistence.md): UsePersisted holds a value past a component re-mount — the user navigates away, the component unmounts, the user navigates back, and the captured state is still there.

#### Ship your app

- [Accessibility](https://microsoft.github.io/microsoft-ui-reactor/main/guides/accessibility.md): Reactor provides accessibility modifiers on every component. They map directly to WinUI's automation properties, so screen readers, keyboard navigation, and test tools work out of the box.
- [Localization](https://microsoft.github.io/microsoft-ui-reactor/main/guides/localization.md): Microsoft.UI.Reactor (Reactor)'s localization system wraps your component tree in a LocaleProvider that supplies an IntlAccessor to every descendant via context. You look up messages, format numbers and dates, and detect RTL layouts — all locale-aware, all reactive.
- [Testing](https://microsoft.github.io/microsoft-ui-reactor/main/guides/testing.md): Microsoft.UI.Reactor (Reactor)'s render loop is deterministic and synchronous. A Component mounted in a unit test renders, runs effects, accepts state updates, re-renders, and disposes through the same code path the WinUI host uses — minus the WinUI tree at the bottom.
- [Performance](https://microsoft.github.io/microsoft-ui-reactor/main/guides/performance.md): A top-down performance workflow: take a trace with dotnet-trace or PerfView, learn what each span means, find the outlier, and reproduce a benchmark so a regression has a number behind it.
- [Packaging](https://microsoft.github.io/microsoft-ui-reactor/main/guides/packaging.md): A Microsoft.UI.Reactor (Reactor) app is a normal WinUI 3 / Windows App SDK executable — dotnet publish produces the deployable artifact and the framework itself adds nothing exotic to the project file.

#### Developer tools

- [Dev Tooling](https://microsoft.github.io/microsoft-ui-reactor/main/guides/dev-tooling.md): Reactor's inner loop centers on dotnet watch and preview mode: you edit code, save, and see changes in a running window — no manual restart, no state-resetting class reload most of the time.
- [Visual Studio Embedded Preview](https://microsoft.github.io/microsoft-ui-reactor/main/guides/vs-extension.md): The Visual Studio Embedded Preview hosts a live Microsoft.UI.Reactor (Reactor) component inside a Visual Studio tool window — Visual Studio 2026 (18.x), with the Visual Studio extension development workload. Unlike the VS Code preview, which streams screenshots, the Visual Studio extension embeds the real WinUI surface so mouse, keyboard, focus, popups, and accessibility tree inspection work against the running app.
- [Diagnostics](https://microsoft.github.io/microsoft-ui-reactor/main/guides/diagnostics.md): This page is about reading Reactor's diagnostics from a real app — not about adding new events.

#### Interop and migration

- [WinForms Interop](https://microsoft.github.io/microsoft-ui-reactor/main/guides/winforms-interop.md): Reactor components can run inside WinForms applications using XAML Islands. The Reactor.Interop.WinForms package provides XamlIslandControl — a standard WinForms control that hosts a Reactor component tree with full keyboard, accessibility, and theming support.
- [WPF Interop](https://microsoft.github.io/microsoft-ui-reactor/main/guides/wpf-interop.md): Host Reactor components inside a WPF app with a XAML island, until a first-class WPF host control ships.
- [Migration: Optional<T> controlled props](https://microsoft.github.io/microsoft-ui-reactor/main/guides/optional-t.md): Spec 050 changes controlled element-record properties from plain T to Optional<T>. Factory calls such as TextBox(text, setText), Slider(value, 0, 100, setValue), and ComboBox(items, index, setIndex) keep their existing plain-value signatures; they wrap the value in Optional<T>.Of(value) internally. Most app code therefore keeps compiling unchanged.
- [Migration: Windowing evolution](https://microsoft.github.io/microsoft-ui-reactor/main/guides/windowing-evolution.md): Spec 054 intentionally removes a few legacy windowing fields in favor of more explicit shapes. The new APIs are additive except for the breaking changes below.

### How it works

#### Runtime

- [Architecture Overview](https://microsoft.github.io/microsoft-ui-reactor/main/how-it-works/architecture-overview.md): This page maps Reactor's runtime end-to-end so the rest of the Under-the-hood track has a shared diagram to point at.
- [Reactivity Model](https://microsoft.github.io/microsoft-ui-reactor/main/how-it-works/reactivity-model.md): This page is the why behind the hooks surface. If you've come from XAML and INotifyPropertyChanged, the change of model is larger than the syntax.
- [Hooks Internals](https://microsoft.github.io/microsoft-ui-reactor/main/how-it-works/hooks-internals.md): One description of the storage shape behind every hook — enough to debug a HookOrderException, predict whether UseState returns a stable setter, and know when reaching for UseRef is correct.
- [Reconciliation](https://microsoft.github.io/microsoft-ui-reactor/main/how-it-works/reconciliation.md): This page absorbs the legacy docs/reference/reconciliation.md. The goal is one place to learn how Reactor diffs trees — the same information, in the structure the rest of the guide uses, plus the source pointers that let you verify it.
- [Effects Scheduling](https://microsoft.github.io/microsoft-ui-reactor/main/how-it-works/effects-scheduling.md): This page is about when UseEffect fires and why.
- [Threading and Dispatch](https://microsoft.github.io/microsoft-ui-reactor/main/how-it-works/threading-and-dispatch.md): Microsoft.UI.Reactor (Reactor) has one UI thread invariant: the reconciler, every hook setter, and every WinUI property write must happen on the thread that captured the dispatcher at app bootstrap.
- [Element Pool](https://microsoft.github.io/microsoft-ui-reactor/main/how-it-works/element-pool.md): The Microsoft.UI.Reactor (Reactor) element pool recycles unmounted WinUI controls so a scroll-heavy list or fast-mounting subtree doesn't allocate a fresh TextBlock / Button / Border on every render.

#### Controls

- [The Control Reconciler Protocol](https://microsoft.github.io/microsoft-ui-reactor/main/how-it-works/control-reconciler-protocol.md): This page documents the data path between the reconciler and the WinUI control it edits. It is for anyone extending Reactor with a new native control, or debugging why an existing element does not update the way it should.
- [Extending Reactor with Native Controls](https://microsoft.github.io/microsoft-ui-reactor/main/how-it-works/extending-reactor-controls.md): How to wrap a native Windows control as a Reactor element — a third-party WinUI control, a custom Control subclass, or an existing WinUI control that the built-in catalog has not yet covered.
- [Modifier System](https://microsoft.github.io/microsoft-ui-reactor/main/how-it-works/modifier-system.md): This page is the runtime view of the same fluent chain that Styling introduces from the surface.

#### Input and motion

- [Animation Pipeline](https://microsoft.github.io/microsoft-ui-reactor/main/how-it-works/animation-pipeline.md): This page maps the four authoring surfaces onto the underlying compositor and explains the trade-offs that pick one surface over another.
- [Focus and Input Internals](https://microsoft.github.io/microsoft-ui-reactor/main/how-it-works/focus-and-input-internals.md): This page covers the input subsystem from the routed-event boundary through the Reactor hooks that observe it.

#### Tooling

- [Source Mapping](https://microsoft.github.io/microsoft-ui-reactor/main/how-it-works/source-mapping.md): "Source mapping" in Microsoft.UI.Reactor (Reactor) is the chain that ties a runtime artifact — an ETW event, a --preview overlay highlight, a thrown exception — back to the C# source that produced it.
- [Analyzer Architecture](https://microsoft.github.io/microsoft-ui-reactor/main/how-it-works/analyzer-architecture.md): This page walks through the Reactor analyzer pipeline end-to-end: how a diagnostic descriptor wires into the C# compilation, what happens on each InvocationExpression, and how to add your own rule.
- [DevTools Internals](https://microsoft.github.io/microsoft-ui-reactor/main/how-it-works/devtools-internals.md): This page covers the internals of Reactor's devtools — the MCP server that external agents talk to, the in-app dev menu, the runtime overlays, and the gate that keeps the whole subsystem out of retail.
- [Perf Instrumentation](https://microsoft.github.io/microsoft-ui-reactor/main/how-it-works/perf-instrumentation.md): This page covers what Reactor publishes, where to read it, and how to get a clean trace.

### Recipes

- [Recipes](https://microsoft.github.io/microsoft-ui-reactor/main/recipes.md): A recipe is a small composition of Microsoft.UI.Reactor (Reactor) primitives that solves a real UI problem — login, master-detail, settings, paginated list, modal confirmation, multi-step form, search-with-suggestions, command palette, drag-to-reorder. The recipes here are not exhaustive apps; each is a single screen showing the pattern, and each ships a tiny doc app you can clone and adapt.
- [Recipe: Command Palette](https://microsoft.github.io/microsoft-ui-reactor/main/recipes/command-palette.md): A command palette is a filter-as-you-type list with a keyboard accelerator to open and Enter to execute. In Microsoft.UI.Reactor (Reactor) it's three UseState hooks, a static catalog of Command records, and an OnPreviewKeyDown handler — no menubar, no modal manager, no view model.
- [Recipe: Drag to Reorder](https://microsoft.github.io/microsoft-ui-reactor/main/recipes/drag-reorder.md): Drag-reorder is a UseState<List<T>> plus a function that splices one item from one index to another. Microsoft.UI.Reactor (Reactor)'s keyed reconciler does the rest — moves preserve identity, so the same row element keeps its focus, its hover state, and its place in the visual tree. Pointer drag is one entry point; an Alt+Up / Alt+Down keyboard shortcut is the second, and it carries the accessibility story.
- [Recipe: Login](https://microsoft.github.io/microsoft-ui-reactor/main/recipes/login.md): A Microsoft.UI.Reactor (Reactor) login form is four pieces working together: validated input, submit-gated state, an async call, and an error-display surface. The recipe below wires them with two UseState hooks and one UseMutation — no view model, no event handler classes, and no hand-rolled submitting / error flags.
- [Recipe: Master-detail](https://microsoft.github.io/microsoft-ui-reactor/main/recipes/master-detail.md): Master-detail in Microsoft.UI.Reactor (Reactor) is the canonical multi-pane shape: a list of records on one side, the selected record's details on the other. The whole thing is one UseState for the selected id and two slots in an HStack.
- [Recipe: Modal dialog](https://microsoft.github.io/microsoft-ui-reactor/main/recipes/modal-dialog.md): The modal is an element you return conditionally. There is no special "open dialog" API — when open is true the tree includes the modal panel layered over the page; when it's false the modal element is absent and the page renders alone.
- [Recipe: Multi-Step Form](https://microsoft.github.io/microsoft-ui-reactor/main/recipes/multi-step-form.md): A wizard form is a step-index UseState plus a switch on which step renders. The field state lives in the same component as the step index, so moving forward and back never loses what the user typed — Microsoft.UI.Reactor (Reactor) doesn't unmount the surrounding component when the visible slot changes.
- [Recipe: Paginated List](https://microsoft.github.io/microsoft-ui-reactor/main/recipes/paginated-list.md): A paginated list is four states sharing one source of truth: an initial load, a populated list, a "load the next page" affordance, and a terminal "no more results." UseInfiniteResource owns all four in Microsoft.UI.Reactor (Reactor) — the component below pattern-matches on LoadState and reads Items directly; there is no local UseState for loading or errors.
- [Recipe: Search with suggestions](https://microsoft.github.io/microsoft-ui-reactor/main/recipes/search-with-suggestions.md): The pattern is two slots: an input that owns the query string, and a dropdown that shows zero or more suggestions filtered against a catalog. UseMemo keys the filter on the query, so a re-render that didn't touch the query is free — important for the keyboard-up- arrow-down-arrow loop where the suggestion index changes but the query doesn't.
- [Recipe: Settings page](https://microsoft.github.io/microsoft-ui-reactor/main/recipes/settings-page.md): A settings page is many small persisted prefs side-by-side. Each preference is its own UsePersisted call with its own key — there's no central settings object that needs migration when one preference moves. The row layout is a single helper.

### Reference

#### Hooks API

- [Hooks reference](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks.md): Hooks are how Reactor components subscribe to state, schedule effects, and reach into the framework's lifecycle from a function-shaped Render body. Each entry on this index is generated from the <summary> XML doc comments authored alongside the source in src/Reactor/Hooks/ — the canonical surface — and is grouped by the hooks category in docs/_pipeline/reference-map.yaml.

##### Hooks

- [UseAnnounce](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseAnnounce.md)
- [UseAnnounceExtensions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseAnnounceExtensions.md): Extension methods for the UseAnnounce hook.
- [UseBreakpoint](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseBreakpoint.md)
- [UseCallback](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseCallback.md)
- [UseClosingGuard](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseClosingGuard.md): Registers a synchronous "can the window close right now?" predicate. Multiple guards stack — any returning false cancels the close. Runs on the UI thread; for async confirmation, return false and re-trigger ReactorWindow.Close from the dialog callback. No-op outside a window. (spec 036 §7 / §13.4)
- [UseCollection](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseCollection.md): Subscribes to an ObservableCollection and re-renders on Add/Remove/Reset. Returns the collection as IReadOnlyList.
- [UseColorScheme](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseColorScheme.md): Returns the app-global ColorScheme — ColorScheme.Light or ColorScheme.Dark, read from Application.Current.RequestedTheme.
- [UseCommand](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseCommand.md)
- [UseCommandState](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseCommandState.md): Allocates the stable hook shape shared by both UseCommand (guide) overloads: the IsExecuting / IsDebouncing state, the re-entrance guard and debounce-slot refs, and an unmount effect that disposes any live re-enable timer so it cannot fire setIsDebouncing / request a re-render against a torn-down context. These hooks are allocated unconditionally so the hook order is identical regardless of the command's shape.
- [UseContext](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseContext.md): Reads the nearest ancestor's provided value for the given context. Returns the context's DefaultValue if no provider exists in the ancestor chain. Follows hook rules — must be called in the same order every render.
- [UseDevtools](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseDevtools.md): Returns true when the current process is running with the in-app devtools UI enabled. This is the AND of two independent signals:
- [UseDisplays](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseDisplays.md): Returns the current display snapshot and re-renders when the display layout changes.
- [UseDpi](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseDpi.md): Returns the current per-window DPI (ReactorWindow.Dpi) and re-renders on DPI change. Returns the system primary-monitor DPI when called outside a window. (spec 036 §5.2)
- [UseEffect](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseEffect.md)
- [UseElementFocus](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseElementFocus.md)
- [UseElementFocusExtensions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseElementFocusExtensions.md): Hook for imperative element focus (spec 027 Tier 5). Returns a stable ElementRef (survives re-renders) plus a RequestFocus action that schedules Input.FocusManager.Focus on the UI dispatcher. Scheduling defers focus past the current reconcile pass so callers can safely request focus from effects or event handlers without racing against layout.
- [UseElementRef](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseElementRef.md)
- [UseElementRefExtensions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseElementRefExtensions.md): Hook that returns a strongly-typed ElementRef for binding to a concrete WinUI control via the .Ref(...) modifier. The typed ref removes the (Button)ref.Current cast at consumers (Composition, Ink, focus, …).
- [UseExternalStore](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseExternalStore.md): Subscribes to an external store that exposes a current snapshot getter. Re-renders only when a change notification yields a different snapshot.
- [UseFocus](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseFocus.md)
- [UseFocusExtensions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseFocusExtensions.md): Extension methods for the UseFocus hook.
- [UseFocusTrap](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseFocusTrap.md)
- [UseFocusTrapExtensions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseFocusTrapExtensions.md): Extension methods for the UseFocusTrap hook.
- [UseHighContrast](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseHighContrast.md): Returns true when the system is in a High Contrast (forced colors) theme. Automatically re-renders the component when high contrast is toggled.
- [UseHighContrastScheme](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseHighContrastScheme.md): Returns the high contrast scheme name (e.g., "High Contrast Black", "High Contrast White") or null if not in high contrast mode. Automatically re-renders the component when the scheme changes.
- [UseInfiniteResource](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseInfiniteResource.md)
- [UseInfiniteResourceExtensions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseInfiniteResourceExtensions.md): Extension methods providing UseInfiniteResource on RenderContext.
- [UseIntl](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseIntl.md): Returns an IntlAccessor for the current locale. Re-renders this component when the locale changes via a parent LocaleProvider. If no LocaleProvider is present, returns a default accessor using the OS locale. Uses Context internally — the context system handles re-renders automatically.
- [UseIsActive](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseIsActive.md): Subscribes to the host window's activation events and re-renders on change. Returns true outside a window (the surface is "active" while shown). (spec 036 §7)
- [UseIsCovered](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseIsCovered.md): Returns a covered hint from the host window's z-order transitions and re-renders on change. The value is not pixel-accurate occlusion state.
- [UseIsDarkTheme](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseIsDarkTheme.md): Convenience wrapper — returns true when the app-global color scheme is ColorScheme.Dark. Exactly UseColorScheme() == ColorScheme.Dark, so it inherits that hook's semantics: it does not observe per-element RequestedTheme.
- [UseMemo](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseMemo.md)
- [UseMemoCells](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseMemoCells.md)
- [UseMemoCellsByIndex](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseMemoCellsByIndex.md)
- [UseMemoCellsByKey](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseMemoCellsByKey.md)
- [UseMemoCellsExtensions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseMemoCellsExtensions.md): Cell-level memoization hook for high-frequency list / grid bodies. Reuses element references for cells whose item value (and declared dependencies) haven't changed since the previous render. The reconciler short-circuits on Object.ReferenceEquals, so reused cells skip diffing entirely.
- [UseMutation](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseMutation.md)
- [UseMutationExtensions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseMutationExtensions.md): Extension methods providing UseMutation on RenderContext.
- [UseNavigation](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseNavigation.md)
- [UseNavigationLifecycle](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseNavigationLifecycle.md): Registers lifecycle callbacks that fire during navigation events.
- [UseObservable](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseObservable.md): Subscribes to an INotifyPropertyChanged source and re-renders when any property changes. Returns the same source object.
- [UseObservableProperty](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseObservableProperty.md): Subscribes to a specific property on an INotifyPropertyChanged source. Re-renders only when that property changes.
- [UseObservableTree](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseObservableTree.md): Observes an object and all nested INotifyPropertyChanged values reachable through its properties. Re-renders when any property at any depth changes. Automatically subscribes/unsubscribes as property values change.
- [UseOpenWindow](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseOpenWindow.md): Open or reuse a secondary window keyed by key. Renders that pass the same key share the same ReactorWindow; if the spec changes across renders the live window is updated via ReactorWindow.Update. The returned handle is identity-stable across renders so long as the key is stable.
- [UsePersisted](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UsePersisted.md)
- [UseReducedMotion](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseReducedMotion.md): Returns true when the user or system prefers reduced motion (e.g., Windows "Show animations" is off, or SPI_GETCLIENTAREAANIMATION returns false). Automatically re-renders the component when the preference changes.
- [UseReducer](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseReducer.md)
- [UseRef](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseRef.md): Returns a mutable ref object that persists across renders.
- [UseResource](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseResource.md)
- [UseResourceExtensions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseResourceExtensions.md): Extension methods providing UseResource on RenderContext.
- [UseState](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseState.md): Declares a piece of state. Returns (currentValue, setter). Must be called in the same order every render (just like React hooks).
- [UseStateSetterByIndex](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseStateSetterByIndex.md): DEBUG ONLY: Directly set a UseState hook value by index and trigger re-render. Used for testing state changes without event handlers.
- [UseSystemBackButton](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseSystemBackButton.md): Subscribes to Alt+Left and VirtualKey.GoBack keyboard events on the given window's content to call NavigationHandle.GoBack. Unsubscribes on unmount.
- [UseTrayIcon](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseTrayIcon.md): Open (or reuse-by-TrayIconSpec.Key) a system-tray icon scoped to the calling component. The icon closes automatically on unmount — that's the only difference from ReactorApp.OpenTrayIcon, which is app-scoped and keeps the icon alive until explicit ReactorTrayIcon.Close.
- [UseWindow](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseWindow.md): Resolves the ReactorWindow whose host renders this component — including nested components and virtualized rows realized outside the host's render pass — or null when it has no owning window (tray-flyout content, ReactorHostControl embeds). O(1) field read; no subscription, no re-render trigger. (spec 036 §7 / §7.1)
- [UseWindowAspectRatio](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseWindowAspectRatio.md): Applies a lifetime-bound width/height aspect lock to the owning window. Last mounted hook wins; unmounting restores the previous writer.
- [UseWindowDragMove](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseWindowDragMove.md): Returns a stable callback that starts a framework-managed window drag/move loop (cursor polling + AppWindow.Move at ~60Hz until the left mouse button is released).
- [UseWindowPosition](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseWindowPosition.md): Subscribes to the host window's PositionChanged event and re-renders on change. Returns (0, 0) outside a window. (spec 054 §5.5)
- [UseWindowSize](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseWindowSize.md)
- [UseWindowState](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseWindowState.md): Subscribes to the host window's StateChanged event and re-renders on change. Returns WindowState.Normal outside a window. (spec 036 §7)

##### Types and options

- [Announce](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/Announce.md)
- [AnnounceHandle](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/AnnounceHandle.md): Handle returned by UseAnnounce (guide). Provides an imperative Announce (guide) method for screen reader live-region announcements plus a zero-size Region (guide) element that must be included somewhere in the component's rendered tree.
- [AnnounceRegionElement](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/AnnounceRegionElement.md): Internal Reactor element that mounts a hidden TextBlock with LiveSetting for announcements.
- [AnyLoading](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/AnyLoading.md): True iff any tracked token is currently Loading.
- [CacheKey](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/CacheKey.md): Overrides the generated cache key; null uses a key derived from deps. When set, the key identifies the query; deps changes alone do not cancel an in-flight request or start a new fetch. Include values that select different data in the key, for example $"user:{userId}".
- [CacheTime](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/CacheTime.md): Retention after the last subscriber leaves; null uses five minutes.
- [Changed](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/Changed.md): Fires when a resource joins, leaves, or changes its loading state.
- [Clear](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/Clear.md): Clears all registrations.
- [ComponentUseMemoCellsExtensions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/ComponentUseMemoCellsExtensions.md): Component-class shims for UseMemoCellsExtensions (guide) so subclasses can call UseMemoCells without going through this.Context. Same semantics as the RenderContext extension methods.
- [Count](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/Count.md): Snapshot the number of registered tokens (loading or not). Diagnostic only.
- [CursorFromPageIndex](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/CursorFromPageIndex.md): Optional: computes the cursor for an arbitrary page index directly, bypassing the "wait for page N-1" constraint. Used by UseDataSource to expose the offset semantics of IDataSource's ContinuationToken, so deep scrolls fetch pages in parallel instead of walking the chain one round-trip at a time.
- [Customize](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/Customize.md): Author hook — add the bespoke descriptor entries for the [WrapManual] props.
- [Descriptor](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/Descriptor.md): The descriptor the engine interprets for every AnnounceRegionElement (guide).
- [Error](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/Error.md): Most recent error, or null if the last completion was a success or Reset (guide) was called.
- [Fields](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/Fields.md): Registered field names in order.
- [Focus](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/Focus.md)
- [FocusAttached](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/FocusAttached.md): Attached metadata for focus registration on elements.
- [FocusExtensions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/FocusExtensions.md): Fluent extensions for attaching focus management to elements. Uses .Set() to capture the WinUI control reference during mount/update.
- [FocusField](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/FocusField.md): Focuses a specific field by name.
- [FocusFirst](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/FocusFirst.md): Focuses the first field from the given list that exists in this manager. Useful with ValidationContext.InvalidFields.
- [FocusManager](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/FocusManager.md): Manages focus for a set of registered form fields. Supports programmatic focus control, enter-to-advance, and field ordering.
- [FocusMeta](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/FocusMeta.md): Registers any element with a FocusManager via attached metadata (for custom types).
- [FocusNext](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/FocusNext.md): Focuses the field after the given field in registration order. If the current field is the last one, triggers submit.
- [FocusPrevious](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/FocusPrevious.md): Focuses the field before the given field in registration order.
- [FocusTrap](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/FocusTrap.md): Attaches a focus trap to this element. When the trap is active, Tab/Shift+Tab cycles within this element's visual subtree.
- [FocusTrapElementExtensions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/FocusTrapElementExtensions.md): Fluent extension to attach a FocusTrapHandle to an element. The handle captures the mounted WinUI element for focus trapping.
- [FocusTrapHandle](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/FocusTrapHandle.md): Handle returned by UseFocusTrap. When active, traps keyboard focus within the container element so Tab/Shift+Tab cycles within the trapped scope. Essential for modal dialogs and flyouts.
- [GetFocus](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/GetFocus.md): Gets the FocusAttached metadata from an element.
- [IHookDispatcher](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/IHookDispatcher.md): Abstraction for marshalling continuations back to the render thread. Null-safe — if the callback is absent the continuation runs inline on the thread-pool completion thread (tests and headless hosts).
- [InfiniteHookState](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/InfiniteHookState.md): Per-hook state for UseInfiniteResource. Coordinates in-flight page fetches, cache subscriptions, deps-change restart, and unmount teardown.
- [IsActive](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/IsActive.md): Whether the focus trap is currently active.
- [IsFirstField](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/IsFirstField.md): Returns true if the given field is the first registered field.
- [IsLastField](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/IsLastField.md): Returns true if the given field is the last registered field.
- [IsPending](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/IsPending.md): True while at least one RunAsync (guide) call is in-flight.
- [LastResult](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/LastResult.md): Most recent successful result, or default if none yet.
- [Mutation](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/Mutation.md): Handle returned by the UseMutation hook. Carries the pending/error/last-result state and the RunAsync (guide) entry point.
- [MutationHookState](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/MutationHookState.md): Mutable state backing a single UseMutation hook instance. Owned per-component, per-hook-slot; disposed on unmount.
- [MutationOptions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/MutationOptions.md): Callbacks and side-effects for the UseMutation hook. All callbacks are optional; all run on the dispatcher thread except OnOptimistic which runs synchronously on the caller of RunAsync (guide) — the typical case is a render-thread click handler, so the optimistic update lands in the very next frame without a dispatcher hop.
- [NotifyPending](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/NotifyPending.md): Called from RequestPage / page-completion paths to keep the Pending scope in sync.
- [Pending](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/Pending.md): Wraps child with a fresh PendingScope (guide). Renders fallback instead of child while any UseResource/UseInfiniteResource in the subtree is in the Loading state. Reloading(previous) does not trigger the fallback — spec §10.1.
- [PendingComponent](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/PendingComponent.md): Component backing the Pending element. Hosts a PendingScope (guide), subscribes to its Changed (guide) event, and chooses between Fallback and Child.
- [PendingFactory](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/PendingFactory.md): Entry-point factory for the Pending bubble-up element. See docs/specs/020-async-resources-design.md §10 for the full mechanism.
- [PendingProps](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/PendingProps.md): Props for PendingComponent (guide).
- [PendingScope](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/PendingScope.md): Shared loading-state ref-count consumed by the Pending element and populated by UseResource / UseInfiniteResource hooks inside the scope. When the scope observes any registered resource in the Loading state (not Reloading), the owning Pending element renders its fallback instead of the child subtree.
- [Post](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/Post.md): Enqueue action to run on the dispatcher thread.
- [RefetchOnMount](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/RefetchOnMount.md): Whether to fetch on a cache miss.
- [RefetchOnWindowFocus](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/RefetchOnWindowFocus.md): Whether to opt into window-focus revalidation.
- [Region](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/Region.md): A zero-size, invisible Reactor element that acts as the live-region anchor. Include this anywhere in your component tree (it renders nothing visible).
- [Register](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/Register.md)
- [Reset](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/Reset.md): Clear Error (guide) and LastResult (guide). Does not cancel or affect any in-flight RunAsync (guide) call.
- [ResourceHookState](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/ResourceHookState.md): Mutable state backing a single UseResource hook instance. Instances are owned per-component, per-hook-slot, and are disposed on unmount.
- [ResourceOptions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/ResourceOptions.md): Tuning for the UseResource hook. Defaults mirror TanStack Query — zero StaleTime (guide) (always refetch-on-mount but dedup concurrent requests), five-minute CacheTime (guide).
- [RetryCount](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/RetryCount.md): Number of retries after a failed fetch.
- [RunAsync](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/RunAsync.md): Kick off the mutator with input. Returns a task that completes with the mutator's result or fault.
- [SetControl](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/SetControl.md): Associates a WinUI control with a field name. Called from .Set() during mount/update.
- [SetLoading](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/SetLoading.md): Update token's loading state. Silently ignored if the token was never registered (defensive — avoids forcing the caller to track whether they registered).
- [SetSubmitHandler](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/SetSubmitHandler.md): Sets the callback for submit (Enter on last field).
- [StaleTime](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/StaleTime.md): Freshness interval; null uses zero.
- [Unregister](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/Unregister.md): Stop tracking token. Idempotent.
- [WindowsDispatcherHookDispatcher](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/WindowsDispatcherHookDispatcher.md): Default IHookDispatcher (guide) backed by DispatcherQueue.GetForCurrentThread(). Falls back to inline invocation when called outside a WinUI dispatcher (unit tests).
