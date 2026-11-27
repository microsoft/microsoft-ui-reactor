# Hooks

State, effects, context and the other hooks a component calls from Render.

Hooks are how Reactor components subscribe to state, schedule effects, and reach into the framework's lifecycle from a function-shaped `Render` body. Call them on the `RenderContext` (or from a `Component`), always in the same order, and never inside a condition or a loop.

**Learn more:** [Hooks](../../hooks.md), [Effects and Lifecycle](../../effects.md), [Rules of Reactor](../../rules-of-reactor.md)

## State

Hooks that keep values between renders.

| Hook | Description |
| --- | --- |
| [`UseCollection`](UseCollection.md) | Subscribes to an ObservableCollection and re-renders on Add/Remove/Reset. |
| [`UseExternalStore`](UseExternalStore.md) | Subscribes to an external store that exposes a current snapshot getter. |
| [`UseObservable`](UseObservable.md) | Subscribes to an INotifyPropertyChanged source and re-renders when any property changes. |
| [`UseObservableProperty`](UseObservableProperty.md) | Subscribes to a specific property on an INotifyPropertyChanged source. |
| [`UseObservableTree`](UseObservableTree.md) | Observes an object and all nested INotifyPropertyChanged values reachable through its properties. |
| [`UsePersisted`](UsePersisted.md) | Like UseState, but the value survives unmount/remount via an in-memory cache. |
| [`UseReducer`](UseReducer.md) | Declares a piece of state with a functional updater variant. |
| [`UseRef`](UseRef.md) | Returns a mutable ref object that persists across renders. |
| [`UseState`](UseState.md) | Declares a piece of state. |

## Effects and memoization

Hooks that run side effects or cache values between renders.

| Hook | Description |
| --- | --- |
| [`UseCallback`](UseCallback.md) | Returns a stable callback reference that doesn't change between renders. |
| [`UseEffect`](UseEffect.md) | Runs a side effect after render. |
| [`UseMemo`](UseMemo.md) | Memoizes a computed value, recomputing only when dependencies change. |
| [`UseMemoCells`](UseMemoCells.md) | Memoize cell construction for `items`. |
| [`UseMemoCellsByIndex`](UseMemoCellsByIndex.md) | Memoize cell construction when the data source already knows which indices changed. |
| [`UseMemoCellsByKey`](UseMemoCellsByKey.md) | Memoize cell construction keyed by `keySelector`. |

## Context

Hooks that read values provided higher in the tree.

| Hook | Description |
| --- | --- |
| [`UseContext`](UseContext.md) | Reads the nearest ancestor's provided value for the given context. |
| [`UseIntl`](UseIntl.md) | Returns an IntlAccessor for the current locale. |

## Data and commands

Hooks that load data, run mutations and expose commands.

| Hook | Description |
| --- | --- |
| [`UseCommand`](UseCommand.md) | Processes a Command for use in a component. |
| [`UseDataSource`](UseDataSource.md) | Projects an `IDataSource<T>` through `UseInfiniteResource`, surfacing its pages as an `InfiniteResource<TItem>`. |
| [`UseInfiniteResource`](UseInfiniteResource.md) | Returns the `InfiniteResource<TItem>` owned by this hook slot. |
| [`UseMutation`](UseMutation.md) | Registers a `Mutation<TInput, TResult>` for this hook slot. |
| [`UseResource`](UseResource.md) | Runs an async fetch keyed on `deps`, returning an `AsyncValue<T>` that tracks the fetch's lifecycle. |

## Forms and validation

Hooks that connect a component to a validation context.

| Hook | Description |
| --- | --- |
| [`UseChildValidationContext`](UseChildValidationContext.md) | Creates a child validation context independent from any parent. |
| [`UseValidationContext`](UseValidationContext.md) | Returns the nearest ancestor's ValidationContext, or creates a new one scoped to this component. |

## Windows and display

Hooks that read or change the window, the display and the tray icon.

| Hook | Description |
| --- | --- |
| [`UseBreakpoint`](UseBreakpoint.md) | Returns true when the given window's width is \>= minWidth. |
| [`UseClosingGuard`](UseClosingGuard.md) | Registers a synchronous "can the window close right now?" predicate. |
| [`UseDisplays`](UseDisplays.md) | Returns the current display snapshot and re-renders when the display layout changes. |
| [`UseDpi`](UseDpi.md) | Returns the current per-window DPI (`ReactorWindow.Dpi`) and re-renders on DPI change. |
| [`UseIsActive`](UseIsActive.md) | Subscribes to the host window's activation events and re-renders on change. |
| [`UseIsCovered`](UseIsCovered.md) | Returns a covered hint from the host window's z-order transitions and re-renders on change. |
| [`UseOpenWindow`](UseOpenWindow.md) | Open or reuse a secondary window keyed by `key`. |
| [`UseTrayIcon`](UseTrayIcon.md) | Open (or reuse-by-`TrayIconSpec.Key`) a system-tray icon scoped to the calling component. |
| [`UseWindow`](UseWindow.md) | Resolves the `ReactorWindow` whose host renders this component — including nested components and virtualized rows realized outside the host's render pass — or `null` when it has no owning window (tray-flyout content, `ReactorHostControl` embeds). |
| [`UseWindowAspectRatio`](UseWindowAspectRatio.md) | Applies a lifetime-bound width/height aspect lock to the owning window. |
| [`UseWindowDragMove`](UseWindowDragMove.md) | Returns a stable callback that starts a framework-managed window drag/move loop (cursor polling + `AppWindow.Move` at ~60Hz until the left mouse button is released). |
| [`UseWindowPosition`](UseWindowPosition.md) | Subscribes to the host window's `PositionChanged` event and re-renders on change. |
| [`UseWindowSize`](UseWindowSize.md) | Returns (width, height) of the given window and re-renders when the window resizes. |
| [`UseWindowState`](UseWindowState.md) | Subscribes to the host window's `StateChanged` event and re-renders on change. |

## Theme and accessibility

Hooks that follow the system theme and accessibility settings.

| Hook | Description |
| --- | --- |
| [`UseAnnounce`](UseAnnounce.md) | Creates an `AnnounceHandle` for making screen reader announcements. |
| [`UseColorScheme`](UseColorScheme.md) | Returns the app-global `ColorScheme` — `ColorScheme.Light` or `ColorScheme.Dark`, read from `Application.Current.RequestedTheme`. |
| [`UseHighContrast`](UseHighContrast.md) | Returns `true` when the system is in a High Contrast (forced colors) theme. |
| [`UseHighContrastScheme`](UseHighContrastScheme.md) | Returns the high contrast scheme name (e.g., "High Contrast Black", "High Contrast White") or `null` if not in high contrast mode. |
| [`UseIsDarkTheme`](UseIsDarkTheme.md) | Convenience wrapper — returns `true` when the app-global color scheme is `ColorScheme.Dark`. |
| [`UseReducedMotion`](UseReducedMotion.md) | Returns `true` when the user or system prefers reduced motion (e.g., Windows "Show animations" is off, or `SPI_GETCLIENTAREAANIMATION` returns false). |

## Focus and element refs

Hooks that move focus or reach a mounted control.

| Hook | Description |
| --- | --- |
| [`UseElementFocus`](UseElementFocus.md) | Creates (or retrieves) a component-scoped `ElementRef` and pairs it with a `RequestFocus` action. |
| [`UseElementRef`](UseElementRef.md) | Returns a stable `ElementRef<T>` for the current component scope. |
| [`UseFocus`](UseFocus.md) | Creates and returns a FocusManager scoped to this component. |
| [`UseFocusTrap`](UseFocusTrap.md) | Creates a focus trap handle that traps keyboard focus within a container when active. |

## Navigation

Hooks for page navigation and the system back button.

| Hook | Description |
| --- | --- |
| [`UseNavigation`](UseNavigation.md) | Child mode: retrieves an ancestor's `NavigationHandle<TRoute>` from context. |
| [`UseNavigationLifecycle`](UseNavigationLifecycle.md) | Registers lifecycle callbacks that fire during navigation events. |
| [`UseSystemBackButton`](UseSystemBackButton.md) | Subscribes to Alt+Left and VirtualKey.GoBack keyboard events on the given window's content to call `NavigationHandle.GoBack`. |

## Developer tools

Hooks for the Reactor developer tools.

| Hook | Description |
| --- | --- |
| [`UseDevtools`](UseDevtools.md) | Returns `true` when the current process is running with the in-app devtools UI enabled. |

## Docking

Hooks that read the dock layout and the enclosing pane. From the Microsoft.UI.Reactor.Advanced package.

| Hook | Description |
| --- | --- |
| [`UseActivePaneKey`](UseActivePaneKey.md) | Returns the active pane's `DockableContent.Key`, or null when no pane is active. |
| [`UseDockHost`](UseDockHost.md) | Returns the nearest enclosing `DockHostModel`, or null when the component is rendered outside any docking host. |
| [`UseDockLayout`](UseDockLayout.md) | Returns a wide snapshot of the dock layout — re-renders the consumer on any structural change. |
| [`UseDockPanePersisted`](UseDockPanePersisted.md) | Per-pane persisted state. |
| [`UseDockState`](UseDockState.md) | Returns the enclosing pane's current dock state (Docked / Floating / AutoHidden / AutoHiddenExpanded / Hidden). |
| [`UseIsActivePane`](UseIsActivePane.md) | Returns true when the \*enclosing pane\* is the host's currently-active pane. |
| [`UsePane`](UsePane.md) | Returns identity info for the enclosing pane subtree. |

## Win2D drawing

Hooks that keep Win2D draw state and device resources. From the Microsoft.UI.Reactor.Advanced package.

| Hook | Description |
| --- | --- |
| [`UseCanvasResources`](UseCanvasResources.md) | Creates resources for the current Win2D shared device, recreates them after `CanvasDevice.DeviceLost`, and disposes them on unmount. |
| [`UseDrawCommand`](UseDrawCommand.md) | Memoizes a Win2D draw callback and rebuilds it only when `deps` change. |
| [`UseDrawState`](UseDrawState.md) | Creates a stable mutable draw-state reference for Win2D callbacks. |

## Types and options

| Type | Description |
| --- | --- |
| [Focus helpers](focus-helpers.md) | The focus manager and the modifiers that the UseFocus hooks work with. |
| [Hook results and options](hook-types.md) | Small types that hooks return or take as options. |
| [Pending](pending.md) | The Pending element that shows a fallback while resources below it load. |
