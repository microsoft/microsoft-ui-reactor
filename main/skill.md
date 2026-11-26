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

### API reference

- [API reference](https://microsoft.github.io/microsoft-ui-reactor/main/reference.md): The API reference lists every public type, hook, factory method and modifier in Reactor. It is generated from the XML doc comments in the source code.

#### Hooks

- [Hooks](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks.md): State, effects, context and the other hooks a component calls from Render.

##### State

- [UseCollection](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseCollection.md): Subscribes to an ObservableCollection and re-renders on Add/Remove/Reset. Returns the collection as IReadOnlyList.
- [UseExternalStore](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseExternalStore.md): Subscribes to an external store that exposes a current snapshot getter. Re-renders only when a change notification yields a different snapshot.
- [UseObservable](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseObservable.md): Subscribes to an INotifyPropertyChanged source and re-renders when any property changes. Returns the same source object.
- [UseObservableProperty](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseObservableProperty.md): Subscribes to a specific property on an INotifyPropertyChanged source. Re-renders only when that property changes.
- [UseObservableTree](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseObservableTree.md): Observes an object and all nested INotifyPropertyChanged values reachable through its properties. Re-renders when any property at any depth changes. Automatically subscribes/unsubscribes as property values change.
- [UsePersisted](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UsePersisted.md): Like UseState, but the value survives unmount/remount via an in-memory cache. On first mount, uses cached value if available, otherwise uses initialValue. Value is saved to cache on unmount.
- [UseReducer](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseReducer.md): Declares a piece of state with a functional updater variant. The updater receives the previous value and returns the next. Cross-thread updater calls are auto-marshaled onto the captured UI dispatcher (same semantics as UseState); pass threadSafe: true for locked in-place updates that serialize many concurrent writers without an intervening UI tick.
- [UseRef](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseRef.md): Returns a mutable ref object that persists across renders.
- [UseState](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseState.md): Declares a piece of state. Returns (currentValue, setter). Must be called in the same order every render (just like React hooks).

##### Effects and memoization

- [UseCallback](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseCallback.md): Returns a stable callback reference that doesn't change between renders.
- [UseEffect](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseEffect.md): Runs a side effect after render. The effect re-runs when any dependency changes. Pass an empty array for "run once on mount" semantics. Returns a cleanup action that runs before the next effect or on unmount.
- [UseMemo](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseMemo.md): Memoizes a computed value, recomputing only when dependencies change.
- [UseMemoCells](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseMemoCells.md): Memoize cell construction for items. On the first render the builder runs for every index; on subsequent renders, an item that compares Object.Equals against the previous render's value at the same index reuses the previous element. Any change to dependencies invalidates the entire cache and rebuilds every cell.
- [UseMemoCellsByIndex](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseMemoCellsByIndex.md)
- [UseMemoCellsByKey](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseMemoCellsByKey.md): Memoize cell construction keyed by keySelector. Cells are reused when both the item's key and value match the previous render. Keys that recur with mutated content rebuild that cell only. Reordered keys reuse cells (the reconciler's keyed- children path keeps the underlying control without unmount/remount).

##### Context

- [UseContext](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseContext.md): Reads the nearest ancestor's provided value for the given context. Returns the context's DefaultValue if no provider exists in the ancestor chain. Follows hook rules — must be called in the same order every render.
- [UseIntl](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseIntl.md): Returns an IntlAccessor for the current locale. Re-renders this component when the locale changes via a parent LocaleProvider. If no LocaleProvider is present, returns a default accessor using the OS locale. Uses Context internally — the context system handles re-renders automatically.

##### Data and commands

- [UseCommand](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseCommand.md)
- [UseDataSource](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseDataSource.md): Projects an IDataSource<T> through UseInfiniteResource, surfacing its pages as an InfiniteResource<TItem>. Deps are identity-based on source and value-based on the request descriptors, so a new sort/filter/search restarts the pagination cleanly.
- [UseInfiniteResource](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseInfiniteResource.md): Returns the InfiniteResource<TItem> owned by this hook slot. The resource's state is driven by fetchPage; deps controls cache-keying and deps-change restart.
- [UseMutation](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseMutation.md): Registers a Mutation<TInput, TResult> for this hook slot. The handle is stable across renders (pass it to buttons, context menus, etc.).
- [UseResource](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseResource.md): Runs an async fetch keyed on deps, returning an AsyncValue<T> that tracks the fetch's lifecycle. The hook owns the cancellation token, stores results in cache, and re-renders when new results land.

##### Forms and validation

- [UseChildValidationContext](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseChildValidationContext.md): Creates a child validation context independent from any parent. Returns both the child context and the parent (if one exists). The child collects its own messages; use the parent reference to bubble manually.
- [UseValidationContext](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseValidationContext.md): Returns the nearest ancestor's ValidationContext, or creates a new one scoped to this component. The created context persists across re-renders.

##### Windows and display

- [UseBreakpoint](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseBreakpoint.md): Returns true when the given window's width is >= minWidth. Re-renders when the window resizes across the breakpoint.
- [UseClosingGuard](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseClosingGuard.md): Registers a synchronous "can the window close right now?" predicate. Multiple guards stack — any returning false cancels the close. Runs on the UI thread; for async confirmation, return false and re-trigger ReactorWindow.Close from the dialog callback. No-op outside a window. (spec 036 §7 / §13.4)
- [UseDisplays](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseDisplays.md): Returns the current display snapshot and re-renders when the display layout changes.
- [UseDpi](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseDpi.md): Returns the current per-window DPI (ReactorWindow.Dpi) and re-renders on DPI change. Returns the system primary-monitor DPI when called outside a window. (spec 036 §5.2)
- [UseIsActive](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseIsActive.md): Subscribes to the host window's activation events and re-renders on change. Returns true outside a window (the surface is "active" while shown). (spec 036 §7)
- [UseIsCovered](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseIsCovered.md): Returns a covered hint from the host window's z-order transitions and re-renders on change. The value is not pixel-accurate occlusion state.
- [UseOpenWindow](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseOpenWindow.md): Open or reuse a secondary window keyed by key. Renders that pass the same key share the same ReactorWindow; if the spec changes across renders the live window is updated via ReactorWindow.Update. The returned handle is identity-stable across renders so long as the key is stable.
- [UseTrayIcon](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseTrayIcon.md): Open (or reuse-by-TrayIconSpec.Key) a system-tray icon scoped to the calling component. The icon closes automatically on unmount — that's the only difference from ReactorApp.OpenTrayIcon, which is app-scoped and keeps the icon alive until explicit ReactorTrayIcon.Close.
- [UseWindow](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseWindow.md): Resolves the ReactorWindow whose host renders this component — including nested components and virtualized rows realized outside the host's render pass — or null when it has no owning window (tray-flyout content, ReactorHostControl embeds). O(1) field read; no subscription, no re-render trigger. (spec 036 §7 / §7.1)
- [UseWindowAspectRatio](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseWindowAspectRatio.md): Applies a lifetime-bound width/height aspect lock to the owning window. Last mounted hook wins; unmounting restores the previous writer.
- [UseWindowDragMove](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseWindowDragMove.md): Returns a stable callback that starts a framework-managed window drag/move loop (cursor polling + AppWindow.Move at ~60Hz until the left mouse button is released).
- [UseWindowPosition](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseWindowPosition.md): Subscribes to the host window's PositionChanged event and re-renders on change. Returns (0, 0) outside a window. (spec 054 §5.5)
- [UseWindowSize](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseWindowSize.md): Returns (width, height) of the given window and re-renders when the window resizes.
- [UseWindowState](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseWindowState.md): Subscribes to the host window's StateChanged event and re-renders on change. Returns WindowState.Normal outside a window. (spec 036 §7)

##### Theme and accessibility

- [UseAnnounce](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseAnnounce.md): Creates an AnnounceHandle for making screen reader announcements. The handle persists across re-renders.
- [UseColorScheme](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseColorScheme.md): Returns the app-global ColorScheme — ColorScheme.Light or ColorScheme.Dark, read from Application.Current.RequestedTheme.
- [UseHighContrast](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseHighContrast.md): Returns true when the system is in a High Contrast (forced colors) theme. Automatically re-renders the component when high contrast is toggled.
- [UseHighContrastScheme](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseHighContrastScheme.md): Returns the high contrast scheme name (e.g., "High Contrast Black", "High Contrast White") or null if not in high contrast mode. Automatically re-renders the component when the scheme changes.
- [UseIsDarkTheme](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseIsDarkTheme.md): Convenience wrapper — returns true when the app-global color scheme is ColorScheme.Dark. Exactly UseColorScheme() == ColorScheme.Dark, so it inherits that hook's semantics: it does not observe per-element RequestedTheme.
- [UseReducedMotion](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseReducedMotion.md): Returns true when the user or system prefers reduced motion (e.g., Windows "Show animations" is off, or SPI_GETCLIENTAREAANIMATION returns false). Automatically re-renders the component when the preference changes.

##### Focus and element refs

- [UseElementFocus](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseElementFocus.md): Creates (or retrieves) a component-scoped ElementRef and pairs it with a RequestFocus action. Bind the ref to an element via .Ref(ref). Calling RequestFocus schedules FocusManager.Focus on the UI dispatcher; if the ref's target has not mounted yet the call is a no-op.
- [UseElementRef](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseElementRef.md): Returns a stable ElementRef<T> for the current component scope.
- [UseFocus](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseFocus.md): Creates and returns a FocusManager scoped to this component. The manager persists across re-renders.
- [UseFocusTrap](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseFocusTrap.md): Creates a focus trap handle that traps keyboard focus within a container when active. Use with the .FocusTrap() element modifier.

##### Navigation

- [UseNavigation](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseNavigation.md): Child mode: retrieves an ancestor's NavigationHandle<TRoute> from context. Throws if no ancestor provides one (i.e., no root UseNavigation with a NavigationHost exists above this component in the tree).
- [UseNavigationLifecycle](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseNavigationLifecycle.md): Registers lifecycle callbacks that fire during navigation events.
- [UseSystemBackButton](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseSystemBackButton.md): Subscribes to Alt+Left and VirtualKey.GoBack keyboard events on the given window's content to call NavigationHandle.GoBack. Unsubscribes on unmount.

##### Developer tools

- [UseDevtools](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/UseDevtools.md): Returns true when the current process is running with the in-app devtools UI enabled. This is the AND of two independent signals:

##### Types and options

- [Focus helpers](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/focus-helpers.md): The focus manager and the modifiers that the UseFocus hooks work with.
- [Hook results and options](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/hook-types.md): Small types that hooks return or take as options.
- [Pending](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hooks/pending.md): The Pending element that shows a fallback while resources below it load.

#### Components

- [Components](https://microsoft.github.io/microsoft-ui-reactor/main/reference/components.md): Component base classes, the render context, refs, context and error types.
- [Render errors](https://microsoft.github.io/microsoft-ui-reactor/main/reference/components/render-errors.md): Types that describe an error thrown while a component renders.
- [Component<TProps>](https://microsoft.github.io/microsoft-ui-reactor/main/reference/components/Component-1.md): Base class for components that receive typed props (e.g., navigation parameters). Props are set by the host before rendering.
- [AppContexts](https://microsoft.github.io/microsoft-ui-reactor/main/reference/components/AppContexts.md): Static context registrations shared across the Reactor core. Install the cache via ReactorHost bootstrap (or override per test).
- [Callbacks<T>](https://microsoft.github.io/microsoft-ui-reactor/main/reference/components/Callbacks.md): An opt-in, always-equal wrapper for the delegate (callback) portion of a component's props, so callback identity never forces a re-render.
- [Component](https://microsoft.github.io/microsoft-ui-reactor/main/reference/components/Component.md): Base class for stateful components (like React class components, but using hooks). Components hold a RenderContext that tracks their hook state across re-renders.
- [Context<T>](https://microsoft.github.io/microsoft-ui-reactor/main/reference/components/Context.md): A typed, named context that can be provided to a subtree and consumed by any descendant. Define as a static field. Provide via .Provide() modifier. Consume via UseContext() hook.
- [ContextBase](https://microsoft.github.io/microsoft-ui-reactor/main/reference/components/ContextBase.md): Non-generic base for type-erased storage in the context scope stack.
- [ElementFactory<T>](https://microsoft.github.io/microsoft-ui-reactor/main/reference/components/ElementFactory.md): Bridges WinUI's ItemsRepeater/IElementFactory to Reactor's Reconciler. GetElement calls the view builder then mounts; RecycleElement unmounts.
- [IReactorKeyed](https://microsoft.github.io/microsoft-ui-reactor/main/reference/components/IReactorKeyed.md): Identity-on-data convention for items rendered into Reactor collection elements (TemplatedListElementBase and LazyStackElementBase) and into hand-built keyed children (FlexColumn(items.Select(... .WithKey(item)))).
- [Observable<T>](https://microsoft.github.io/microsoft-ui-reactor/main/reference/components/Observable.md): A single-value cell that raises INotifyPropertyChanged.PropertyChanged when Observable.Value is assigned a different value. Exists to let developers declare small pieces of global app state (dev flags, light/dark mode, currently-signed-in user, etc.) in one line without hand-writing an INotifyPropertyChanged class.
- [Optional<T>](https://microsoft.github.io/microsoft-ui-reactor/main/reference/components/Optional.md): Represents either an explicit value or an unset state.
- [Ref<T>](https://microsoft.github.io/microsoft-ui-reactor/main/reference/components/Ref.md): A mutable reference that persists across renders (like React's useRef).
- [RenderContext](https://microsoft.github.io/microsoft-ui-reactor/main/reference/components/RenderContext.md): Passed to function components and provides access to hooks. Each component instance gets its own RenderContext which tracks hook call order.
- [SourceLocation](https://microsoft.github.io/microsoft-ui-reactor/main/reference/components/SourceLocation.md): Spec 010 — the C# source location that produced an Element.

#### Elements

- [Elements](https://microsoft.github.io/microsoft-ui-reactor/main/reference/elements.md): The immutable element records that describe each control.
- [Elements: Layout](https://microsoft.github.io/microsoft-ui-reactor/main/reference/elements/layout.md): Panels, stacks, grids, scrolling and other containers.
- [Elements: Text](https://microsoft.github.io/microsoft-ui-reactor/main/reference/elements/text.md): Text blocks, rich text and the typography factories.
- [Elements: Buttons](https://microsoft.github.io/microsoft-ui-reactor/main/reference/elements/buttons.md): Buttons, toggle buttons, split buttons and hyperlink buttons.
- [Elements: Input](https://microsoft.github.io/microsoft-ui-reactor/main/reference/elements/input.md): Text boxes, check boxes, combo boxes, sliders, pickers and other input controls.
- [Elements: Collections](https://microsoft.github.io/microsoft-ui-reactor/main/reference/elements/collections.md): Lists, grids, trees, flip views and virtualized collections.
- [Elements: Navigation](https://microsoft.github.io/microsoft-ui-reactor/main/reference/elements/navigation.md): Navigation views, tabs, pivots, breadcrumbs and the title bar.
- [Elements: Menus and commands](https://microsoft.github.io/microsoft-ui-reactor/main/reference/elements/menus-and-commands.md): Menus, menu bars, command bars and keyboard accelerators.
- [Elements: Dialogs and flyouts](https://microsoft.github.io/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts.md): Content dialogs, flyouts, popups and teaching tips.
- [Elements: Media and icons](https://microsoft.github.io/microsoft-ui-reactor/main/reference/elements/media.md): Images, icons, animated visuals, media playback and person pictures.
- [Elements: Shapes](https://microsoft.github.io/microsoft-ui-reactor/main/reference/elements/shapes.md): Rectangles, ellipses, lines, paths and brushes.
- [Elements: Status and info](https://microsoft.github.io/microsoft-ui-reactor/main/reference/elements/status-and-info.md): Info bars, badges and progress indicators.
- [Elements: Forms](https://microsoft.github.io/microsoft-ui-reactor/main/reference/elements/forms.md): Form fields, validation rules, validation visualizers and the property grid.
- [Elements: Composition](https://microsoft.github.io/microsoft-ui-reactor/main/reference/elements/composition.md): Components, memoization, error boundaries, conditionals and other structural elements.
- [Elements: Interop](https://microsoft.github.io/microsoft-ui-reactor/main/reference/elements/interop.md): Hosted XAML content, web views and maps.

##### Types

- [Element](https://microsoft.github.io/microsoft-ui-reactor/main/reference/elements/Element.md): A lightweight, immutable description of a UI node (the "virtual DOM"). Elements are cheap to create and diff — they never touch real controls directly.

#### Factories

- [Factories](https://microsoft.github.io/microsoft-ui-reactor/main/reference/factories.md): The DSL factory methods that create element records.
- [Factories: Layout](https://microsoft.github.io/microsoft-ui-reactor/main/reference/factories/layout.md): Panels, stacks, grids, scrolling and other containers.
- [Factories: Text](https://microsoft.github.io/microsoft-ui-reactor/main/reference/factories/text.md): Text blocks, rich text and the typography factories.
- [Factories: Buttons](https://microsoft.github.io/microsoft-ui-reactor/main/reference/factories/buttons.md): Buttons, toggle buttons, split buttons and hyperlink buttons.
- [Factories: Input](https://microsoft.github.io/microsoft-ui-reactor/main/reference/factories/input.md): Text boxes, check boxes, combo boxes, sliders, pickers and other input controls.
- [Factories: Collections](https://microsoft.github.io/microsoft-ui-reactor/main/reference/factories/collections.md): Lists, grids, trees, flip views and virtualized collections.
- [Factories: Navigation](https://microsoft.github.io/microsoft-ui-reactor/main/reference/factories/navigation.md): Navigation views, tabs, pivots, breadcrumbs and the title bar.
- [Factories: Menus and commands](https://microsoft.github.io/microsoft-ui-reactor/main/reference/factories/menus-and-commands.md): Menus, menu bars, command bars and keyboard accelerators.
- [Factories: Dialogs and flyouts](https://microsoft.github.io/microsoft-ui-reactor/main/reference/factories/dialogs-and-flyouts.md): Content dialogs, flyouts, popups and teaching tips.
- [Factories: Media and icons](https://microsoft.github.io/microsoft-ui-reactor/main/reference/factories/media.md): Images, icons, animated visuals, media playback and person pictures.
- [Factories: Shapes](https://microsoft.github.io/microsoft-ui-reactor/main/reference/factories/shapes.md): Rectangles, ellipses, lines, paths and brushes.
- [Factories: Status and info](https://microsoft.github.io/microsoft-ui-reactor/main/reference/factories/status-and-info.md): Info bars, badges and progress indicators.
- [Factories: Forms](https://microsoft.github.io/microsoft-ui-reactor/main/reference/factories/forms.md): Form fields, validation rules, validation visualizers and the property grid.
- [Factories: Composition](https://microsoft.github.io/microsoft-ui-reactor/main/reference/factories/composition.md): Components, memoization, error boundaries, conditionals and other structural elements.
- [Factories: Interop](https://microsoft.github.io/microsoft-ui-reactor/main/reference/factories/interop.md): Hosted XAML content, web views and maps.

#### Modifiers

- [Modifiers](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers.md): Fluent extension methods that change layout, appearance, input and behavior.

##### Common modifiers

- [Modifiers: Layout and spacing](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/layout-and-spacing.md): Size, margin, padding, alignment and the attached layout properties.
- [Modifiers: Appearance](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/appearance.md): Background, foreground, borders, corners, opacity, transforms, styles and theme.
- [Modifiers: Typography](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/typography.md): Fonts, text alignment, wrapping, trimming and other text formatting.
- [Modifiers: Accessibility](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/accessibility.md): Automation names, landmarks, live regions and other accessibility properties.
- [Modifiers: Focus and keyboard](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/focus-and-keyboard.md): Tab order, access keys, directional focus and focus traps.
- [Modifiers: Input and events](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/input-and-events.md): Pointer, keyboard, gesture and drag and drop events, and whether an element takes input.
- [Modifiers: Lifecycle and identity](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/lifecycle.md): Keys, refs, mount and unmount callbacks, size changes and context values.
- [Modifiers: Animation](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/animation.md): Transitions, keyframes, layout animations, interaction states and scroll-linked animation.
- [Modifiers: Tooltips and flyouts](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/tooltips-and-flyouts.md): Tooltips, flyouts and context flyouts attached to any element.
- [Modifiers: Validation](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/validation.md): Validators, required fields and error styling.
- [Modifiers: Native properties](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/native-properties.md): The Set modifier, which changes the real WinUI control directly.

##### Control modifiers

- [Control modifiers: Shared controls](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/shared-controls.md): Modifiers that several kinds of controls share, such as headers, placeholders and selection events.
- [Control modifiers: Layout](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/layout.md): Modifiers that only the controls in the Layout area have.
- [Control modifiers: Buttons](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/buttons.md): Modifiers that only the controls in the Buttons area have.
- [Control modifiers: Input](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/input.md): Modifiers that only the controls in the Input area have.
- [Control modifiers: Collections](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/collections.md): Modifiers that only the controls in the Collections area have.
- [Control modifiers: Navigation](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/navigation.md): Modifiers that only the controls in the Navigation area have.
- [Control modifiers: Dialogs and flyouts](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/dialogs-and-flyouts.md): Modifiers that only the controls in the Dialogs and flyouts area have.
- [Control modifiers: Media and icons](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/media.md): Modifiers that only the controls in the Media and icons area have.
- [Control modifiers: Shapes](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/shapes.md): Modifiers that only the controls in the Shapes area have.
- [Control modifiers: Status and info](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/status-and-info.md): Modifiers that only the controls in the Status and info area have.
- [Control modifiers: Forms](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/forms.md): Modifiers that only the controls in the Forms area have.
- [Control modifiers: Interop](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/interop.md): Modifiers that only the controls in the Interop area have.

##### Types

- [AccessibilityModifiers](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/AccessibilityModifiers.md): Advanced accessibility properties (WCAG Tier 2/3). Stored as a lazy sub-record on ElementModifiers to avoid allocating storage for elements that don't need advanced accessibility annotations. All fluent extension methods create/merge this record automatically — developers never need to construct it directly.
- [ElementExtensions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/ElementExtensions.md): Fluent modifier extension methods for elements. Modifiers are stored inline on Element.Modifiers, preserving the concrete type through the entire fluent chain. This means .Set() works after any modifier:
- [ElementExtras](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/ElementExtras.md)
- [ElementModifiers](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/ElementModifiers.md)
- [LayoutModifiers](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/LayoutModifiers.md): Layout-related modifiers (sizing, alignment, spacing, visibility, theme, logical-direction insets). Stored as a lazy sub-record on ElementModifiers so that the common case of a few fields set allocates a small bucket rather than bloating the parent record. Public properties on ElementModifiers (Padding, Margin, Width, …) read from / write into this sub-record transparently — most callers never see this type.
- [VisualModifiers](https://microsoft.github.io/microsoft-ui-reactor/main/reference/modifiers/VisualModifiers.md): Visual-related modifiers (brushes, borders, transforms, opacity). Stored as a lazy sub-record on ElementModifiers in the same pattern as LayoutModifiers. Public properties on ElementModifiers (Foreground, Background, BorderBrush, …) shim through.

#### Hosting and windows

- [Hosting and windows](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting.md): Start the app, open windows and integrate with the Windows shell.
- [Displays](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/displays.md): Information about the connected displays.
- [File pickers](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/file-pickers.md): Options for the open file, save file and folder pickers.
- [Launch and activation](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/launch.md): How the app was started, and requests to embed it.
- [Persistence](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/persistence.md): Stores and scopes that save window placement and persisted state.
- [Taskbar and jump lists](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/taskbar.md): Types for the taskbar button, its progress, overlay, thumbnail buttons and jump list.
- [Tray icon](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/tray-icon.md): Types for an icon in the notification area.
- [Window events](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/window-events.md): Event arguments raised by a Reactor window.
- [Window options](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/window-options.md): Enums that describe the style, size and position of a window.
- [XAML interop](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/xaml-interop.md): Helpers for hosting Reactor inside an existing XAML app.
- [ReactorApp](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/ReactorApp.md)
- [ReactorAppContext](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/ReactorAppContext.md): The argument to ReactorApp.Run's startup callback. Carries the launch activation; all surface-management operations (open / find a window or tray icon, register events) go directly through the static ReactorApp API — call sites mix freely. (spec 036 §4.3)
- [ReactorApplication](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/ReactorApplication.md): Application subclass that implements IXamlMetadataProvider so the native XAML schema context can resolve managed types from XBF theme resources. No App.xaml needed — XamlControlsResources are loaded programmatically. The IXamlMetadataProvider implementation delegates to the WinUI controls' built-in provider so that custom control types (TextCommandBarFlyout, etc.) can be instantiated from XBF theme resources.
- [ReactorHost](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/ReactorHost.md): Hosts a Reactor component tree inside a WinUI Window. Manages the render loop: when state changes, re-renders the component and reconciles the virtual tree against the real WinUI control tree.
- [ReactorHostControl](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/ReactorHostControl.md): A fully self-contained WinUI ContentControl that hosts a Reactor component tree. Drop this into any vanilla WinUI app — no ReactorApp, ReactorApplication, or special bootstrapping needed. Each instance owns its own Reconciler and render loop.
- [ReactorWindow](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/ReactorWindow.md): Owns one OS top-level Window and one ReactorHost. Created via ReactorApp.OpenWindow. (spec 036 §3.2 / §4.2)
- [RenderStats](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/RenderStats.md): Snapshot of render loop performance, updated every ~1 second by ReactorHost. Always available: FPS, frame timing, render count. DEBUG builds add per-reconcile element counters from the last render pass.
- [UIThreadOnlyAttribute](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/UIThreadOnlyAttribute.md)
- [WindowIcon](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/WindowIcon.md): Abstraction over AppWindow.SetIcon and AppWindow.SetIcon. Pass to WindowSpec.Icon, to a tray icon, or to a taskbar overlay. (spec 036 §4.1)
- [WindowKey](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/WindowKey.md): Stable identity for a top-level surface within a process. Used by ReactorApp.FindWindow, WindowSpec.Key, and the UseOpenWindow hook to dedupe re-renders that should reuse a window rather than open a new one. (spec 036 §4.4)
- [WindowSpec](https://microsoft.github.io/microsoft-ui-reactor/main/reference/hosting/WindowSpec.md): Immutable (init-only) declarative description of a top-level Reactor window. Hand to ReactorApp.OpenWindow to open a window; hand to ReactorWindow.Update to diff against the current spec and apply only changed fields.

#### Theming

- [Theming](https://microsoft.github.io/microsoft-ui-reactor/main/reference/theming.md): Theme tokens, theme resources, brushes and window backdrops.
- [Enums](https://microsoft.github.io/microsoft-ui-reactor/main/reference/theming/enums.md): The enumerations in this category.
- [BackdropChoice](https://microsoft.github.io/microsoft-ui-reactor/main/reference/theming/BackdropChoice.md): Tagged value stored in ElementModifiers.Backdrop. Holds either a BackdropKind selector (the common case, materialized by the host into a built-in WinUI backdrop) or a custom factory delegate (escape hatch for tinted Mica or third-party SystemBackdrop subclasses) — never both, never neither. The "exactly one set" invariant is enforced by the private constructor; construct via BackdropChoice.Of or BackdropChoice.Of.
- [BrushHelper](https://microsoft.github.io/microsoft-ui-reactor/main/reference/theming/BrushHelper.md): Color and brush parsing utilities. Supports named colors, hex (#RRGGBB, #AARRGGBB), and direct Color values. Parsed colors are cached by string (an immutable value type, safe to share), but a fresh SolidColorBrush is created per call: a brush is a DependencyObject with thread affinity and mutable Color/Opacity, so a single instance cannot be safely shared across controls.
- [ResourceBuilder](https://microsoft.github.io/microsoft-ui-reactor/main/reference/theming/ResourceBuilder.md): Fluent builder for per-control resource overrides (WinUI lightweight styling). Each Set() call registers a resource key override that will be injected into the control's FrameworkElement.Resources dictionary at mount time.
- [ResourceOverrides](https://microsoft.github.io/microsoft-ui-reactor/main/reference/theming/ResourceOverrides.md): Immutable snapshot of per-control resource overrides built by ResourceBuilder. Contains both literal resource values and theme-reactive ThemeRef entries that are re-resolved on theme change.
- [Theme](https://microsoft.github.io/microsoft-ui-reactor/main/reference/theming/Theme.md): Provides semantic theme tokens and custom resource references. All tokens resolve from WinUI's resource system and automatically adapt when the theme changes (Light ↔ Dark).
- [ThemeRef](https://microsoft.github.io/microsoft-ui-reactor/main/reference/theming/ThemeRef.md): A reference to a WinUI theme resource that resolves at render time. Use via Theme tokens or Theme.Ref for custom keys.
- [ThemeResource](https://microsoft.github.io/microsoft-ui-reactor/main/reference/theming/ThemeResource.md): Lookup helpers for WinUI theme resources. These resolve against Application.Current.Resources, which includes all merged dictionaries (XamlControlsResources, custom themes, etc.).

#### Layout

- [Layout](https://microsoft.github.io/microsoft-ui-reactor/main/reference/layout.md): The flex panel, grid sizes and the attached layout records.
- [Attached layout properties](https://microsoft.github.io/microsoft-ui-reactor/main/reference/layout/attached.md): Records that hold the attached properties set by the layout modifiers.
- [Flex enums](https://microsoft.github.io/microsoft-ui-reactor/main/reference/layout/flex-enums.md): Enums that control the flex layout.
- [GridSize](https://microsoft.github.io/microsoft-ui-reactor/main/reference/layout/GridSize.md): Strongly-typed grid track size, used by the typed Microsoft.UI.Reactor.Factories.Grid(GridSize[], GridSize[], Element?[]) factory in place of the legacy string-form ("Auto", "*", "200", "1.5*") tracks. Optional Min and Max properties are available to constrain the track size.
- [GridSizeExtensions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/layout/GridSizeExtensions.md): Fluent extension methods for GridSize to set the optional Min and Max properties.

#### Input and commands

- [Input and commands](https://microsoft.github.io/microsoft-ui-reactor/main/reference/input.md): Commands, element refs, focus, drag and drop, and gestures.
- [Drag and drop](https://microsoft.github.io/microsoft-ui-reactor/main/reference/input/drag-and-drop.md): Types for dragging data out of and dropping it onto elements.
- [Element refs](https://microsoft.github.io/microsoft-ui-reactor/main/reference/input/element-refs.md): Refs to mounted controls.
- [Gestures](https://microsoft.github.io/microsoft-ui-reactor/main/reference/input/gestures.md): Gesture events and their options.
- [Command<T>](https://microsoft.github.io/microsoft-ui-reactor/main/reference/input/Command-1.md): Immutable (init-only) parameterized command descriptor. The action receives an argument of type T, enabling a single command definition to operate on different targets: var delete = new Command<Item> { Label = "Delete", Execute = item => Remove(item) }; MenuItem(delete, selectedItem)
- [Command](https://microsoft.github.io/microsoft-ui-reactor/main/reference/input/Command.md): Immutable (init-only) command descriptor that bundles an action with its metadata (label, icon, keyboard accelerator, enabled state). Define once, use in any surface: var save = new Command { Label = "Save", Execute = () => Save(), Icon = SymbolIcon("Save") }; AppBarButton(save) // toolbar MenuItem(save) // menu Button(save) // inline
- [CommandInterop](https://microsoft.github.io/microsoft-ui-reactor/main/reference/input/CommandInterop.md): Bridges ICommand (MVVM/CommunityToolkit) to Command, enabling migration from ViewModel-based patterns to Reactor's declarative commanding. var reactorCmd = CommandInterop.FromCommand(viewModel.SaveCommand, "Save", icon: new SymbolIconData("Save"), accelerator: Accelerator(VirtualKey.S, Control));
- [FocusManager](https://microsoft.github.io/microsoft-ui-reactor/main/reference/input/FocusManager.md): Imperative focus helpers (spec 027 Tier 5). These operate on ElementRef refs obtained via the UseElementFocus hook or an explicit .Ref(...) modifier; use them when declarative focus (.Focus(...) form helpers, UIElement.IsTabStop) is not enough.
- [StandardCommand](https://microsoft.github.io/microsoft-ui-reactor/main/reference/input/StandardCommand.md)

#### Data

- [Data](https://microsoft.github.io/microsoft-ui-reactor/main/reference/data.md): Async values, resources, the query cache and data sources.
- [Async values](https://microsoft.github.io/microsoft-ui-reactor/main/reference/data/async-values.md): The value a resource hook returns while it loads, after it loads and after it fails.
- [Column attributes](https://microsoft.github.io/microsoft-ui-reactor/main/reference/data/column-attributes.md): Attributes that change how a property shows as a column.
- [Enums](https://microsoft.github.io/microsoft-ui-reactor/main/reference/data/enums.md): The enumerations in this category.
- [Paging](https://microsoft.github.io/microsoft-ui-reactor/main/reference/data/paging.md): Types for infinite and paged resources.
- [Query cache](https://microsoft.github.io/microsoft-ui-reactor/main/reference/data/query-cache.md): The process-wide cache that resource hooks share.
- [Sort and filter descriptors](https://microsoft.github.io/microsoft-ui-reactor/main/reference/data/descriptors.md): Requests, sort, filter and field descriptors that data sources receive.
- [FocusRevalidationService](https://microsoft.github.io/microsoft-ui-reactor/main/reference/data/FocusRevalidationService.md): Central service that invalidates stale QueryCache entries when the app window is reactivated or resumed, so consumers that opted in via ResourceOptions.RefetchOnWindowFocus = true see fresh data after an Alt-Tab or resume. Disabled by default — see ReactorFeatureFlags.FocusRevalidation.
- [IDataSource<T>](https://microsoft.github.io/microsoft-ui-reactor/main/reference/data/IDataSource.md): A read-only data source that provides paged access to items.
- [IKeyedDataSource<T>](https://microsoft.github.io/microsoft-ui-reactor/main/reference/data/IKeyedDataSource.md): A data source that supports fetching individual items by key.
- [IMutableDataSource<T>](https://microsoft.github.io/microsoft-ui-reactor/main/reference/data/IMutableDataSource.md): A mutable data source that supports CRUD operations.
- [IObservableDataSource<T>](https://microsoft.github.io/microsoft-ui-reactor/main/reference/data/IObservableDataSource.md): A data source that fires events when data changes.
- [ListDataSource<T>](https://microsoft.github.io/microsoft-ui-reactor/main/reference/data/ListDataSource.md): In-memory data source backed by a list. Supports client-side sorting, filtering, text search, paging, and CRUD mutations.
- [ObservableListDataSource<T>](https://microsoft.github.io/microsoft-ui-reactor/main/reference/data/ObservableListDataSource.md): An observable data source that watches an ObservableCollection for changes and fires DataChanged when items are added, removed, or modified. The collection is the authoritative source — mutations go through the collection, not through the data source's CRUD methods.

#### Navigation

- [Navigation](https://microsoft.github.io/microsoft-ui-reactor/main/reference/navigation.md): Navigation state, routes, lifecycle contexts and page transitions.
- [Enums](https://microsoft.github.io/microsoft-ui-reactor/main/reference/navigation/enums.md): The enumerations in this category.
- [Navigation diagnostics](https://microsoft.github.io/microsoft-ui-reactor/main/reference/navigation/diagnostics.md): Diagnostic events raised by the navigation system.
- [Navigation lifecycle](https://microsoft.github.io/microsoft-ui-reactor/main/reference/navigation/lifecycle.md): Contexts passed to the navigation lifecycle callbacks.
- [Navigation transitions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/navigation/transitions.md): Transitions that play when the navigation host changes pages.
- [DeepLinkMap<TRoute>](https://microsoft.github.io/microsoft-ui-reactor/main/reference/navigation/DeepLinkMap.md): Maps URI patterns to route constructors for deep linking. Patterns use /segment/{param:type} syntax where type is int or string.
- [DeepLinkResult<TRoute>](https://microsoft.github.io/microsoft-ui-reactor/main/reference/navigation/DeepLinkResult.md): Result of a deep link resolution attempt.
- [NavigateOptions](https://microsoft.github.io/microsoft-ui-reactor/main/reference/navigation/NavigateOptions.md): Options for a single navigation action.
- [NavigationEventArgs<TRoute>](https://microsoft.github.io/microsoft-ui-reactor/main/reference/navigation/NavigationEventArgs.md): Event args fired after a successful navigation.

…and 158 more. [llms.txt](https://microsoft.github.io/microsoft-ui-reactor/main/llms.txt) lists every page.
