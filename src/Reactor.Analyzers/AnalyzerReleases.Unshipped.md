### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
REACTOR_THEME_001 | Reactor.Style | Warning | UseThemeRefAnalyzer - Use ThemeRef instead of hard-coded color
REACTOR_THEME_002 | Reactor.Style | Info | UseLightweightStylingAnalyzer - Consider lightweight styling for visual-state overrides
REACTOR_THEME_003 | Reactor.Style | Info | RequestedThemeSetAnalyzer - RequestedTheme modifier available
REACTOR_THEME_004 | Reactor.Style | Warning | UseThemeRefAnalyzer - Hard-coded Brush/Color object bypasses theme tokens
REACTOR_HOOKS_001 | Reactor.Hooks | Warning | HookRulesAnalyzer - Hook called conditionally
REACTOR_HOOKS_004 | Reactor.Hooks | Warning | HookRulesAnalyzer - Hook deps contains freshly allocated value
REACTOR_HOOKS_005 | Reactor.Hooks | Warning | HookRulesAnalyzer - Hook called outside Render or custom-hook method
REACTOR_HOOKS_006 | Reactor.Hooks | Info | HookRulesAnalyzer - UseResource fetcher looks non-idempotent (use UseMutation for writes)
REACTOR_HOOKS_007 | Reactor.Hooks | Warning | UseMemoCellsAnalyzer - Builder closure capture missing from dependencies
REACTOR_HOOKS_008 | Reactor.Hooks | Info | HookRulesAnalyzer - State variable read after its setter was called in the same synchronous handler (stale read)
REACTOR_HOOKS_009 | Reactor.Hooks | Warning | CommandDebounceAnalyzer - Command.DebounceMs is inert unless the command is routed through UseCommand
REACTOR_HOOKS_011 | Reactor.Hooks | Warning | ControlledInputAnalyzer - Controlled input has a state-derived value but an inert change callback
REACTOR_A11Y_001 | Microsoft.UI.Reactor.Accessibility | Warning | AccessibilityAnalyzers - Icon-only button needs an accessible name
REACTOR_A11Y_002 | Microsoft.UI.Reactor.Accessibility | Warning | AccessibilityAnalyzers - Image needs alt text or AccessibilityHidden
REACTOR_A11Y_003 | Microsoft.UI.Reactor.Accessibility | Warning | AccessibilityAnalyzers - Form field needs a label
REACTOR_A11Y_004 | Microsoft.UI.Reactor.Accessibility | Warning | AccessibilityAnalyzers - Clickable container (.OnTapped) is not keyboard-reachable; add .IsTabStop(true)
REACTOR_REF_001 | Reactor.Reference | Warning | ReferenceCurrentReadAnalyzer - Use descriptor.Reference/binding.Reference instead of assigning ElementRef.Current to reference properties
REACTOR_DSL_001 | Reactor.Dsl | Warning | MissingWithKeyAnalyzer - Dynamic list item missing .WithKey
REACTOR_DSL_002 | Reactor.Dsl | Info | MissingWithKeyAnalyzer - Non-stable .WithKey (index / Guid.NewGuid / DateTime.Now)
REACTOR_DSL_003 | Reactor.Dsl | Warning | ConstantKeySelectorAnalyzer - Typed collection keySelector never keys by item (returns constant/null or ignores the item), forcing a keyed-diff bailout
REACTOR_DSL_004 | Reactor.Dsl | Info | MissingWithKeyAnalyzer - Redundant .WithKey inside a ForEach over IReactorKeyed items; the factory already supplies that exact key
REACTOR_DOCK_001 | Reactor.Docking | Warning | OnLiveLayoutRoundTripAnalyzer - OnLiveLayoutChanged feeds the live layout back into state
REACTOR_EVENT_001 | Reactor.Events | Warning | SetEventSubscriptionAnalyzer - Event wired via .Set(+=/-=) re-subscribes every render; use a declarative On* modifier or .OnMountAdd/.OnUnmountAdd
REACTOR_POOL_001 | Reactor.Pool | Warning | PoolResetSetAnalyzer - .Set assigns to a property reset on pool return; use the surviving Reactor modifier
REACTOR_ITEMS_001 | Reactor.Collections | Warning | SetOwnedItemsSourceAnalyzer - .Set(ItemsSource=...) on a Reactor-owned collection
REACTOR_CTRL_001 | Reactor.Controls | Warning | SetSelectedItemAnalyzer - .Set(SelectedItem/SelectedValue) fights controlled SelectedIndex
REACTOR_VIS_001 | Reactor.Layout | Warning | PoolResetSetAnalyzer - Imperative .Set(Visibility=...) instead of .IsVisible(...)
REACTOR_WIN2D_001 | Reactor.Win2D | Error | Win2DSharedDeviceAnalyzer - Win2D canvas draws UseCanvasResources output without .UseSharedDevice() (fatal cross-device draw)
REACTOR0050 | Reactor.Descriptor | Warning | OneWayClearValueAnalyzer - Optional<T> OneWay descriptor entries should provide dp: for ClearValue fallback
REACTOR_PERSIST_001 | Reactor.Persistence | Warning | UsePersistedScopeAnalyzer - 2-arg UsePersisted defaults to Application scope; specify scope
REACTOR_DESC_001 | Reactor.Descriptor | Warning | StaticRegisterLambdaAnalyzer - ControlRegistry.Register* lambda should be static (trim hygiene)
REACTOR_STATE_001 | Reactor.State | Warning | ComponentInpcAnalyzer - INotifyPropertyChanged on a Component is invisible to the render loop
REACTOR_THREAD_002 | Reactor.Threading | Warning | BlockingTaskAnalyzer - Blocking a Task (.Result/.Wait) in Render/effect
REACTOR_OPT_001 | Reactor.Controlled | Info | OptionalSentinelAnalyzer - Selection sentinel literal force-asserts instead of Optional<T>.Unset
REACTOR_CMD_001 | Reactor.Commanding | Info | RawCommandCallbackAnalyzer - Raw-init Command + own click callback both set (callback wins; command never runs)
REACTOR_THREAD_001 | Reactor.Threading | Warning | UIThreadAffinityAnalyzer - UI-thread-only mutator called on a background thread
REACTOR_HOOKS_002 | Reactor.Hooks | Info | HookRulesAnalyzer - Hook after an early-return guard
REACTOR_HOOKS_003 | Reactor.Hooks | Warning | HookRulesAnalyzer - async-void UseEffect body
REACTOR_HOOKS_010 | Reactor.Hooks | Warning | HookRulesAnalyzer - Mutate-then-set reference state (same ref re-passed to setter)
REACTOR_HOOKS_012 | Reactor.Hooks | Warning | HookRulesAnalyzer - Memo dependency lacks value equality
REACTOR_HOOKS_013 | Reactor.Hooks | Warning | HookRulesAnalyzer - UseState/UsePersisted initial value allocated every render
REACTOR_CTX_001 | Reactor.Context | Info | ContextProvideAnalyzer - Context value re-allocated each render (reference-equality type)
REACTOR_GRID_001 | Reactor.Layout | Warning | UnusedGridTrackAnalyzer - Declared Grid column/row that no child occupies (unused track)
REACTOR_INPUT_001 | Reactor.Input | Warning | OnKeyDownChordAnalyzer - Ctrl/Alt chord on .OnKeyDown is focus-scoped; use a Command accelerator
REACTOR_PERF_FUNCREF | Reactor.Performance | Info | MemoizeCommandAnalyzer - Command constructed inline in the render path is re-allocated every render; wrap it in UseMemo
REACTOR_ANIM_002 | Reactor.Animation | Info | KeyframeTriggerAnalyzer - Unstable .Keyframes trigger (DateTime.Now / Guid.NewGuid / per-render allocation) restarts the animation every render
REACTOR_INPUT_002 | Reactor.Input | Warning | UnsafeDropFilesAnalyzer - Unsafe TryGetFiles in .OnDrop returns UNC/reparse/virtual files; use TryGetSafeLocalFiles
REACTOR_NAV_001 | Reactor.Navigation | Warning | StaticNavigationHandleAnalyzer - UseNavigation handle captured into a static field or property outlives the page and pins its dispatcher
REACTOR_DIALOG_001 | Reactor.Lifecycle | Warning | ImperativeContentDialogAnalyzer - Imperative ContentDialog.ShowAsync escapes the render tree; use the controlled ContentDialog(...) element with IsOpen
REACTOR_MOD_001 | Reactor.Modifier | Info | DuplicateAtomicModifierAnalyzer - Same atomic-replace placement modifier (.Grid/.Canvas/.RelativePanel/.Flex) applied twice in one chain; last-wins overwrite drops earlier args (ships a merge fix)
REACTOR_MOD_002 | Reactor.Modifier | Info | PoolResetSetAnalyzer - .Set assigns a property that has a first-class fluent modifier (non-pool-reset counterpart of REACTOR_POOL_001); receiver-gated for Padding/CornerRadius/BorderThickness/BorderBrush/Background/fonts, which the reconciler only applies to specific control types, and element-type-gated for the type-specific modifiers
REACTOR_MOD_003 | Reactor.Modifier | Warning | NoOpModifierAnalyzer - Generic common modifier applied to an element whose mounted control is outside the types ApplyModifiers writes it to, so the value is silently dropped (e.g. .Background(...) on a Rectangle renders an invisible shape); ships a did-you-mean fix to .Fill/.Stroke for shapes
REACTOR_MEDIA_001 | Reactor.Layout | Info | UnsizedWebViewInStackAnalyzer - WebView2 is a direct child of an auto-layout stack (HStack/VStack/FlexRow/FlexColumn) without explicit .Width/.Height
REACTOR_ANIM_003 | Reactor.Animation | Warning | AnimationScopeAsyncAnalyzer - async lambda to WithAnimation loses the ThreadStatic scope after await
REACTOR_LIFECYCLE_002 | Reactor.Lifecycle | Warning | EffectCleanupAnalyzer - UseEffect(Action) allocates a timer/subscription/event with no returned cleanup
REACTOR_MEMO_001 | Reactor.Performance | Info | MemoWrapperModifierAnalyzer - Modifiers on a keyed Memo(key,factory) wrapper opt the row out of the recycle cache
REACTOR_DYM_001 | Reactor.DidYouMean | Warning | NonInvocableMemberParensAnalyzer - Reactor property/field invoked like a method (e.g. GridSize.Auto()); remove the parentheses
REACTOR_DYM_002 | Reactor.DidYouMean | Warning | ThemeBackgroundSuffixAnalyzer - Invented Theme.*Background token (e.g. Theme.AppBackground); use Theme.SolidBackground (Theme.LayerBackground -> Theme.LayerFill)
REACTOR_DYM_003 | Reactor.DidYouMean | Warning | FuzzyFactoryNameAnalyzer - Mistyped Reactor factory name in call position (e.g. Buton(...)); did-you-mean the closest factory
REACTOR_DYM_004 | Reactor.DidYouMean | Warning | MissingFactoryArgumentAnalyzer - Reactor factory called with too few arguments (CS7036); suggests the full parameter shape (single unique overload only)
REACTOR_DYM_005 | Reactor.DidYouMean | Warning | StringForElementArgumentAnalyzer - String passed where a Reactor Element is expected (CS1503); wrap it in a text factory (e.g. TextBlock)
REACTOR_ITEMS_002 | Reactor.Collections | Warning | ItemsViewContainerRootAnalyzer - ItemsView viewBuilder returns a non-ItemContainer root (mount-time InvalidOperationException); wrap it with ItemContainer(...)
