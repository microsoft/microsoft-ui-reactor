# Reactor + WinUI3 Integration Proposals

> Analysis of how Reactor's declarative reconciler could integrate more deeply with the WinUI3 native framework to unlock performance, correctness, and developer experience improvements.

---

## 1. Programmatic DataTemplate Construction Without XamlReader (Unblock)

**Problem:** WinUI's `DataTemplate` can only be created from XAML markup — there is no public API to construct one programmatically from code. Reactor works around this by calling `XamlReader.Load()` with a XAML string at runtime to create a DataTemplate wrapping a ContentControl shell:

```csharp
// Current hack in Reconciler.Mount.cs (lines 687-690, repeated for GridView at 746-749):
listView.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
    "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
    "<ContentControl HorizontalContentAlignment='Stretch' VerticalContentAlignment='Stretch'/>" +
    "</DataTemplate>");
```

This is a significant hack with real costs:
1. **XML parsing at runtime** — `XamlReader.Load()` invokes the full XAML parser, allocating an `XamlTextReader`, parsing the XML, resolving namespaces, and instantiating objects through the XAML type system. This is orders of magnitude more expensive than a constructor call.
2. **Called per ListView/GridView mount** — every time Reactor mounts a list, it re-parses this string. No caching is possible because DataTemplate instances can't be reliably shared across different ItemsControls.
3. **Fragile string-based API** — a typo in the XAML string is a runtime crash, not a compile error. The xmlns declaration is boilerplate noise.
4. **Incompatible with AOT/trimming** — `XamlReader.Load()` depends on runtime type resolution that may not survive aggressive trimming.

The underlying need is simple: Reactor wants a DataTemplate that produces a single ContentControl, then takes over content management via `ContainerContentChanging`. The ContentControl is just a shell — Reactor mounts its own element tree into `ContentControl.Content`.

**Gallery migration workarounds:** During the WinUI Gallery migration (70+ pages migrated), every page that used data-driven collections (FlipViewPage, TreeViewPage, SemanticZoomPage, SelectorBarPage) required imperative `XamlReader.Load()` workarounds to construct DataTemplates. The FlipView data-template example was the worst case — the migrated page had to create a hidden placeholder element, hook into its `Loaded` event, walk up to the parent panel, remove existing controls, then programmatically construct a FlipView with a parsed DataTemplate and inject it into the visual tree. This pattern completely defeats the declarative model Reactor provides.

To partially address this on the Reactor side, we built typed collection elements (`FlipView<T>`, `ListView<T>`, `GridView<T>`) that accept a `Func<T, int, Element> viewBuilder` instead of a DataTemplate. The reconciler drives mounting/updating/recycling of templated items natively, using a shared cached `DataTemplate` with a ContentControl shell. This eliminates the per-mount parsing cost and gives Gallery pages a declarative API. However, the underlying WinUI limitation remains — the cached template still requires one `XamlReader.Load()` call at startup, and any scenario that needs a DataTemplate outside of these typed wrappers (e.g., TreeView `ItemTemplate`, custom `ItemTemplateSelector`, grouped `GridView` with `GroupStyle`) still falls back to string-based XAML parsing.

**Proposal:** WinUI should expose a code-based DataTemplate construction API. The minimal version:

```csharp
// Option A: Factory-based DataTemplate constructor
var template = new DataTemplate(() => new ContentControl
{
    HorizontalContentAlignment = HorizontalAlignment.Stretch,
    VerticalContentAlignment = VerticalAlignment.Stretch,
});

// Option B: Static helper (lower API surface)
var template = DataTemplate.FromFactory(() => new ContentControl { ... });
```

Internally, this would create a `DataTemplate` whose `LoadContent()` method calls the factory delegate instead of instantiating from a parsed XAML tree. WinUI's `CDataTemplate` already has the concept of a template content factory (`IDataTemplateComponent`) — the proposal is to make this accessible from managed code without XAML markup.

**Impact:** Eliminates runtime XAML parsing for every ListView/GridView mount. Makes Reactor's list virtualization compatible with NativeAOT trimming. Removes a class of potential runtime errors from string-based XAML. Any declarative framework that manages its own item rendering (not just Reactor) would benefit from this API.

**Cross-reference:** The theming system (Proposal #22) has the same fundamental problem — `ApplyThemeBindings` in `Reconciler.cs` calls `XamlReader.Load()` to create `Style` objects with `{ThemeResource}` setters because there's no code API to create a theme-resource-referencing Setter. Both proposals stem from WinUI lacking code-based equivalents for XAML markup extensions. A unified approach that exposes `{ThemeResource}`, `{StaticResource}`, and DataTemplate factories from code would address both.

**WinUI3 files:**
- `src/dxaml/xcp/core/core/elements/DataTemplate.cpp` — `DataTemplate::LoadContent()` instantiation path
- `src/dxaml/xcp/dxaml/lib/DataTemplate_Partial.cpp` — Managed peer, `LoadContentImpl()`
- `src/dxaml/xcp/core/Parser/XamlReader.cpp` — `XamlReader::Load()` full parser invocation
- `src/dxaml/xcp/core/inc/DataTemplate.h` — Template content factory interfaces

**Reactor files:**
- `Reactor/Core/Reconciler.Mount.cs` — ListView mount (lines 687-690) and GridView mount (lines 746-749) both use the `XamlReader.Load()` hack
- `Reactor/Core/Reconciler.Mount.cs` — `ContainerContentChanging` handler (lines 692-712) that populates the ContentControl shell with Reactor-mounted content
- `Reactor/Core/Element.cs` — `TemplatedListElementBase` / `TemplatedFlipViewElement<T>` / `TemplatedListViewElement<T>` / `TemplatedGridViewElement<T>` — typed collection elements that work around the DataTemplate gap
- `Reactor/Elements/Dsl.cs` — `FlipView<T>()`, `ListView<T>()`, `GridView<T>()` DSL functions

---

## 2. XAML-Free Navigation via Custom Navigation Stack (Unblock)

**Problem:** WinUI's `Frame.Navigate()` requires page types registered in the XAML type metadata system (`MetadataAPI::GetClassInfoByFullName()` in `NavigationCache.cpp`), parameterless constructors (via `ActivationAPI::ActivateInstance()`), and typically XAML code-behind files. Reactor components are render functions with props — they don't have parameterless constructors and aren't registered in the XAML type system. This means Reactor apps can't use Frame-based navigation at all, leaving them with no back stack, no navigation lifecycle events, and no state serialization for page history.

**Proposal:** Build a declarative navigation system that bypasses Frame entirely. Introduce a `UseNavigation()` hook that manages a navigation stack internally, and a `NavigationHost` element that renders the current page component. The stack tracks page type + props + scroll position, supports back/forward, and provides lifecycle hooks (`OnNavigatedTo`, `OnNavigatedFrom`, `OnNavigatingFrom` with cancellation). Navigation transitions would use the Composition animation system from Proposal #15. The key insight is that Reactor doesn't need Frame's type activation — it already knows how to instantiate components from their type + props.

```csharp
var nav = UseNavigation(initialPage: Component<HomePage>());

return NavigationView(
    MenuItems: [...],
    Content: NavigationHost(nav),
    OnSelectionChanged: tag => nav.Navigate(tag switch {
        "home" => Component<HomePage>(),
        "settings" => Component<SettingsPage>(new { userId = currentUser }),
        _ => Component<NotFoundPage>()
    }),
    IsBackEnabled: nav.CanGoBack,
    OnBackRequested: () => nav.GoBack()
);
```

**Impact:** Enables full navigation patterns (master-detail, wizard flows, deep linking) without XAML files. Back stack support means browser-like back/forward. State serialization enables app suspend/resume with navigation state preserved.

**WinUI3 files:**
- `src/dxaml/xcp/dxaml/lib/Frame_Partial.cpp` — Frame.Navigate() implementation (lines 310-405), shows the type resolution + cache + lifecycle pattern to replicate
- `src/dxaml/xcp/dxaml/lib/NavigationCache.cpp` — `LoadContent()` (lines 125-145) uses `ActivationAPI::ActivateInstance()` — the parameterless constructor requirement we bypass
- `src/dxaml/xcp/dxaml/lib/Page_Partial.h` — OnNavigatedTo/From/OnNavigatingFrom lifecycle (lines 16-47) — pattern to mirror in Reactor hooks
- `src/controls/dev/NavigationView/NavigationView.cpp` — NavigationView is decoupled from Frame (fires SelectionChanged only), so it works naturally with a custom stack

**Reactor files:**
- `Reactor/Core/Element.cs` — `NavigationViewElement` (lines 528-543) already wraps NavigationView with Content + OnSelectionChanged
- `Reactor/Core/Reconciler.Mount.cs` — `MountNavigationView()` (lines 576-610) sets Content to mounted subtree — NavigationHost would replace this content on navigation
- `Reactor/Hosting/ReactorHost.cs` — `RenderLoop()` / root content management — navigation changes trigger re-render naturally
- `Reactor/Core/RenderContext.cs` — `UseState` pattern to model `UseNavigation` after

---

## 3. Deep Virtualization Hooks for ListView, GridView, and ItemsRepeater (Unblock)

**Problem:** Reactor's virtualized list integration has three major gaps:

1. **Full-reset on data change:** ListView/GridView updates replace the entire `ItemsSource` (`lv.ItemsSource = Enumerable.Range(0, n.Items.Length).ToList()`), which destroys and recreates all containers. Inserting 1 item into a 10,000-item list causes a full reset — hundreds of milliseconds of work.

2. **No element lifecycle awareness:** ItemsRepeater fires `ElementPrepared`, `ElementClearing`, and `ElementIndexChanged` events that Reactor ignores entirely. Components inside virtualized lists have no way to know when they become visible or get recycled, preventing deferred loading patterns (lazy image loading, analytics visibility tracking).

3. **Recycling gap:** `ElementFactory.RecycleElementCore()` calls `UnmountChild()` but explicitly skips element pooling because modifying the visual tree during ItemsRepeater's layout pass causes `COMException 0x800F1000`. This means every scroll creates fresh allocations instead of reusing pooled controls.

**Proposal:** Three targeted fixes:

**(a) Fine-grained collection change notifications:** Replace the index-list `ItemsSource` with a custom `ObservableCollection<int>` (or a custom `INotifyCollectionChanged` implementation) that emits Add/Remove/Replace/Move notifications. On update, diff the old and new item arrays by key, then emit granular change events. ListView/GridView handle these natively — they'll create/remove/move only the affected containers.

**(b) Expose ItemsRepeater lifecycle events:** Wire `ElementPrepared` and `ElementClearing` to new Reactor component lifecycle hooks (`OnAppeared` / `OnDisappeared`). When an element enters the realization window, fire `OnAppeared` on its component context; when it leaves, fire `OnDisappeared`. This enables lazy loading, visibility-driven prefetch, and resource cleanup without framework-level changes.

**(c) Deferred recycling via Dispatcher:** Instead of recycling synchronously during ItemsRepeater's layout pass, queue cleanup work to `DispatcherQueue.TryEnqueue()` at low priority. After layout completes, the queued work safely unmounts Reactor state and returns the control to `ElementPool`. This bridges the timing gap between ItemsRepeater's recycling cadence and Reactor's cleanup requirements.

```csharp
// (a) Granular collection updates
private ReactorObservableSource<T> _source;
private void UpdateLazyStack(LazyStackElement<T> n) {
    _source.DiffAndApply(n.Items, n.KeySelector); // emits Add/Remove/Move
}

// (b) Lifecycle hooks
repeater.ElementPrepared += (s, args) => {
    var ctx = GetComponentContext(args.Element);
    ctx?.RaiseOnAppeared();
};
repeater.ElementClearing += (s, args) => {
    var ctx = GetComponentContext(args.Element);
    ctx?.RaiseOnDisappeared();
};

// (c) Deferred recycling
protected override void RecycleElementCore(ElementFactoryRecycleArgs args) {
    DispatcherQueue.GetForCurrentThread().TryEnqueue(
        DispatcherQueuePriority.Low,
        () => { _reconciler.UnmountChild(args.Element); _pool.Return(args.Element); });
}
```

**Impact:** (a) Inserting/removing items in a 10k list goes from full reset (~500ms) to O(changed) (~1ms). (b) Components gain visibility awareness, enabling lazy image loading and scroll-driven analytics. (c) Element reuse during scrolling reduces GC pressure — a fast scroll through 1000 items reuses ~32 pooled controls instead of allocating 1000.

**WinUI3 files:**
- `src/controls/dev/Repeater/ViewManager.cpp` — `GetElement()` cascade (lines 22-96) and `ClearElement()` flow (lines 98-137) showing the full element lifecycle Reactor must coordinate with
- `src/controls/dev/Repeater/ViewManager.h` — PinnedPool, UniqueIdResetPool, Animator ownership states
- `src/controls/dev/Repeater/VirtualizationInfo.h` — `ElementOwner` enum (lines 26-117), `AutoRecycleCandidate` / `KeepAlive` flags that control when elements are cleared
- `src/controls/dev/Repeater/ItemsRepeater.cpp` — `MeasureOverride` auto-clear logic (lines 136-150) where realized elements outside the viewport are recycled
- `src/controls/dev/Repeater/BuildTreeScheduler.h` — Frame-budget work scheduling with `QueryPerformanceCounter` timing — pattern for Reactor's deferred recycling
- `src/controls/dev/Repeater/ViewportManager.h` — VisibleWindow / RealizationWindow / CacheLength concepts
- `src/dxaml/xcp/dxaml/lib/ListViewBase_Partial.cpp` — ContainerContentChanging event, container lifecycle for ListView/GridView

**Reactor files:**
- `Reactor/Core/ElementFactory.cs` — `GetElementCore()` / `RecycleElementCore()` — recycling gap fix site
- `Reactor/Core/Reconciler.Mount.cs` — ListView mounting (lines 675-733), GridView mounting (lines 735-800), LazyStack mounting (lines 1046-1071)
- `Reactor/Core/Reconciler.Update.cs` — ListView update (lines 457-472) full-reset pattern, LazyStack update (lines 499-511)
- `Reactor/Core/ElementPool.cs` — `CleanElement()` / pool management — target for deferred return
- `Reactor/Core/Element.cs` — `LazyVStackElement<T>` / `LazyHStackElement<T>` (lines 733-774) — KeySelector exists but unused

---

## 4. Bypass DependencyProperty for Reactor Element↔UIElement Binding (Tactical)

**Problem:** Reactor stores its `Element` reference in `FrameworkElement.Tag` (a DependencyProperty) so that generic event handlers can find the current Element at invocation time. `SetElementTag()` is called ~80 times across Mount and Update — on every control type including layout-only containers (StackPanel, Grid, Border, ScrollViewer, Canvas), not just interactive controls. Each call is a COM property set through the DP system via `CDependencyObject::SetValue` (`src/dxaml/xcp/core/core/elements/depends.cpp`). The Tag property is read on every event handler invocation (button clicks, text changes, toggle switches) via `GetElementTag()`.

**What didn't work:** Replacing Tag with a managed-side `ConditionalWeakTable<UIElement, Element>` or `Dictionary<nint, Element>` was **empirically slower**. WinUI's C# objects are CsWinRT-generated projections — thin RCW wrappers with only an `IObjectReference _inner` field. There is no CLR-side layer where fields could be added. The Tag DP is optimized internally — it's a known property index with no change notification, no layout invalidation, and no coercion, making it close to a bare field write on the native `CDependencyObject`. A `ConditionalWeakTable` adds ephemeron GC handle tracking, hash computation, and lock contention on every access, which outweighs the COM marshaling cost. A plain `Dictionary` adds hash + resize overhead and requires manual cleanup.

**Proposal:** Two solutions, deployable independently:

**(a) Stop setting Tag on non-interactive controls.** The comment on `SetElementTag` already says "Only call for interactive controls" but the code sets it on StackPanel, Grid, Border, ScrollViewer, Canvas, Expander, SplitView — none of which use the Tag-based event dispatch pattern. Removing these ~35 unnecessary `SetElementTag` calls from Mount and Update eliminates the COM cost entirely for layout-only elements, which are the majority of any tree.

**(b) For interactive controls, use a `StrongBox<T>` closure capture instead of Tag lookup.** At mount time, allocate a `StrongBox<ButtonElement>` and capture it in the event handler closure. During Update, mutate `box.Value` to point at the new Element. The event handler reads from the box directly — no Tag get, no sender cast, no dictionary lookup. This is a single pointer dereference at event dispatch time.

```csharp
// Mount:
private (WinUI.Button, StrongBox<ButtonElement>) MountButton(ButtonElement btn)
{
    var button = new WinUI.Button { Content = btn.Label, IsEnabled = btn.IsEnabled };
    var elementRef = new StrongBox<ButtonElement>(btn);
    button.Click += (s, _) => elementRef.Value?.OnClick?.Invoke();
    return (button, elementRef);
}

// Update (no Tag set needed — just mutate the box):
private void UpdateButton(WinUI.Button b, ButtonElement n, StrongBox<ButtonElement> elementRef)
{
    b.Content = n.Label; b.IsEnabled = n.IsEnabled;
    elementRef.Value = n; // Single managed pointer write, no COM
}
```

The reconciler would store the `StrongBox` alongside the control in its tracking structures (e.g., a parallel array or tuple in the element-to-control mapping). `ElementPool.CleanElement()` would null out the box value instead of clearing Tag.

**Impact:** (a) Removes ~35 COM `SetValue` calls per reconciliation pass for layout-only controls — these are the most numerous elements in any tree. (b) Reduces interactive control update cost from 1 COM SetValue (Tag write) to 1 managed field write (StrongBox mutation). Event dispatch goes from COM GetValue + cast to a direct pointer read. On a 200-element screen with 50 interactive controls, this eliminates ~150 COM calls on the update path and makes event dispatch essentially free.

**WinUI3 files:**
- `src/dxaml/xcp/core/core/elements/depends.cpp` — `SetValue` path (~line 477). Tag goes through `SetValueByKnownIndex` which is optimized but still involves COM marshaling, effective value computation, and thread affinity checks. The key finding: this is *fast enough* that a managed hash table can't beat it, but *not free* — eliminating it entirely via closure capture is still a win.
- `src/dxaml/xcp/core/inc/CDependencyObject.h` — `SetValueByKnownIndex` overloads

**Reactor files:**
- `Reactor/Core/Reconciler.cs` — `SetElementTag()` / `GetElementTag()` (lines 44-50) — would be replaced by StrongBox pattern for interactive controls
- `Reactor/Core/Reconciler.Mount.cs` — ~80 `SetElementTag` call sites. ~35 are on layout-only controls (StackPanel line 464, Grid line 488, ScrollViewer line 499, Border line 513, Canvas line 571, etc.) that should simply be removed. ~45 are on interactive controls that should migrate to StrongBox.
- `Reactor/Core/Reconciler.Update.cs` — ~35 `SetElementTag` calls on Update path, all replaceable with `elementRef.Value = n`
- `Reactor/Core/ElementPool.cs` — `CleanElement()` clears `fe.Tag = null` — would null the StrongBox instead

---

## 5. Native Layout Coalescing: Hook Into LayoutManager's Batch Queue (Tactical)

**Problem:** Reactor's `ReactorHost.RenderLoop()` reconciles the entire tree in one shot, which can trigger hundreds of `InvalidateMeasure()` / `InvalidateArrange()` calls as individual properties are patched. Each invalidation propagates up the ancestor chain via `PropagateOnMeasureDirtyPath()`.

**Proposal:** Bracket Reactor's reconciliation pass with WinUI's layout suppression. Call `LayoutManager::EnterMeasure()` before patching and `ExitMeasure()` after, so all layout invalidations are batched into a single pass. Alternatively, expose a `BeginDeferUpdates()` / `EndDeferUpdates()` API on the WinUI side that suppresses layout until the batch completes.

**Impact:** A reconciliation that patches 200 properties currently triggers 200 individual invalidation propagations. Batching would reduce this to a single layout pass.

**WinUI3 files:**
- `src/dxaml/xcp/core/layout/LayoutManager.cpp` — Enter/ExitMeasure (line ~83-87 in header)
- `src/dxaml/xcp/core/core/elements/uielement.cpp` — `InvalidateMeasure()` (lines 3551-3586), `InvalidateArrange()` (lines 3611-3646)

**Reactor files:**
- `Reactor/Hosting/ReactorHost.cs` — `RenderLoop()` / `Render()`
- `Reactor/Core/Reconciler.Update.cs` — property patching triggers layout invalidation

---

## 6. Pool Interactive Controls by Resetting Event State (Tactical)

**Problem:** `ElementPool.cs` only pools 12 non-interactive control types (TextBlock, Grid, Border, etc.). Interactive controls like Button, TextBox, CheckBox, ToggleSwitch are created fresh on every mount and discarded on unmount — never reused.

**Proposal:** Extend pooling to interactive controls by adding a `ResetEventState()` step. Since Reactor's Tag-based event pattern means handlers are generic (they read from Tag at invocation time), the actual event subscriptions don't need to change. The only work needed is: (1) clear the Tag, (2) reset visual state (IsPressed, IsChecked, etc.), (3) return to pool. This is safe because Reactor never stores per-instance closures in event handlers.

**Impact:** In a virtualized list of 1000 buttons, scrolling currently creates and destroys Button instances. Pooling would amortize allocation to ~32 instances.

**WinUI3 files:**
- `src/dxaml/xcp/core/core/elements/Control.cpp` — Control state reset
- `src/controls/dev/Repeater/ViewManager.h` — WinUI's own element reuse patterns

**Reactor files:**
- `Reactor/Core/ElementPool.cs` — `PoolableTypes` set, `CleanElement()` method
- `Reactor/Core/Reconciler.Mount.cs` — event handler wiring (generic Tag-based pattern)

---

## 7. Replace Remount-On-Update Controls with Incremental Patching (Tactical)

**Problem:** Several complex controls in `Reconciler.Update.cs` use a `RemountOnUpdate` pattern — they unmount and remount entirely instead of patching properties. This includes: `RadioButtonsElement`, `ComboBoxElement`, `SplitViewElement`, `TabViewElement`, `TreeViewElement`, `MenuBarElement`, `CommandBarElement`.

**Proposal:** Implement incremental update paths for these controls. The reason they remount is that their WinUI counterparts use `Items` collections that don't support efficient diffing. For ComboBox and RadioButtons, use `ChildReconciler` on their Items collection. For TabView, patch individual `TabViewItem` properties. For TreeView, use hierarchical reconciliation matching WinUI's `TreeViewNode` structure.

**Impact:** TabView with 10 tabs currently destroys and recreates all 10 TabViewItems when any tab label changes. Incremental patching would update only the changed label — a 10x improvement for tab-heavy UIs.

**WinUI3 files:**
- `src/controls/dev/TabView/TabView.h` — TabViewItem collection management
- `src/controls/dev/RadioButtons/RadioButtons.h` — Items collection
- `src/dxaml/xcp/core/core/elements/ItemsControl.cpp` — Items collection patterns

**Reactor files:**
- `Reactor/Core/Reconciler.Update.cs` — RemountOnUpdate controls
- `Reactor/Core/ChildReconciler.cs` — keyed reconciliation (could be reused)

---

## 8. Frame-Budget-Aware Reconciliation (Tactical)

**Problem:** `ReactorHost.RenderLoop()` reconciles the entire tree synchronously. If reconciliation takes longer than 16ms (one frame at 60fps), the UI stutters. There's no mechanism to yield mid-reconciliation and continue on the next frame.

**Proposal:** Implement time-sliced reconciliation inspired by React's Fiber architecture. Break reconciliation into units of work (one component = one unit). After each unit, check elapsed time. If approaching the frame deadline, yield to the dispatcher and resume on the next frame. Priority levels: user input > animations > data updates > off-screen content.

**Impact:** Prevents jank during large tree updates. A 2000-node tree update that takes 40ms would be split across 3 frames instead of causing a single 40ms stutter.

**WinUI3 files:**
- `src/dxaml/xcp/core/layout/LayoutManager.cpp` — MaxLayoutIterations=250, layout cycle management
- WinUI's own layout manager already has iteration limits; Reactor could mirror this

**Reactor files:**
- `Reactor/Hosting/ReactorHost.cs` — `RenderLoop()` / `Render()` — currently synchronous
- `Reactor/Core/Reconciler.cs` — `ReconcileComponent()` — natural unit-of-work boundary

---

## 9. Leverage WinUI's Built-In Implicit Style Resolution for Theming (Tactical)

**Problem:** Reactor elements specify visual properties (FontSize, Foreground, FontWeight) explicitly via modifiers. There's no participation in WinUI's implicit style system — a Reactor TextBlock doesn't pick up the app's implicit TextBlock style. This means Reactor apps can't inherit theme customizations.

**Proposal:** After mounting a control, allow WinUI's implicit style resolution to run before applying Reactor's explicit properties. Reactor properties would override implicit styles (specificity: explicit > implicit), but unset properties would inherit from the theme. This is already how WinUI works — the fix is to *not* reset properties that Reactor hasn't explicitly set, rather than clearing everything in `CleanElement()`.

**Impact:** Reactor apps would automatically respect system themes, accessibility settings (high contrast), and app-level style overrides without any Reactor-side changes.

**Cross-reference — theming conflict:** The theming system (Proposal #22) creates dynamic `Style` objects via `ApplyThemeBindings` and assigns them via `fe.Style = style`, which actively overrides implicit styles. Even with `BasedOn` chaining, the dynamic style takes precedence and can block implicit style resolution. A solution for this proposal must account for theme binding styles coexisting with implicit styles — possibly by applying theme bindings as local value overrides (Proposal #22 Option C) rather than through the Style system, leaving the Style slot free for implicit resolution.

**WinUI3 files:**
- `src/dxaml/xcp/core/core/elements/Style.cpp` — Implicit style lookup, BasedOn chain (lines 28-100)
- `src/dxaml/xcp/core/core/elements/Control.cpp` — `OnApplyTemplate()` and style application

**Reactor files:**
- `Reactor/Core/ElementPool.cs` — `CleanElement()` resets all properties
- `Reactor/Core/Reconciler.Mount.cs` — Property application during mount
- `Reactor/Core/Reconciler.cs` — `ApplyThemeBindings` (lines 1751-1809) — the Style-based theme binding that conflicts with implicit styles

---

## 10. Children.Move() Instead of Remove+Insert (Tactical)

**Problem:** `ChildReconciler.cs` reorders children by removing and re-inserting them. Each `Panel.Children.Remove()` and `Panel.Children.Insert()` triggers visual tree updates, DComp visual reparenting, and layout invalidation independently.

**Proposal:** Use `UIElementCollection.Move()` (or the equivalent native `MoveInternal()`) which reorders the child in-place without remove+insert overhead. This preserves the visual's composition state and avoids the double layout invalidation. If the public API doesn't expose Move, add it to WinUI.

**Impact:** A list reorder of 10 items currently does 10 removes + 10 inserts = 20 visual tree operations. With Move, it's 10 operations with no reparenting overhead.

**WinUI3 files:**
- `src/dxaml/xcp/core/inc/panel.h` — Children collection management
- `src/dxaml/xcp/core/core/elements/panel.cpp` — SetValue for children transitions

**Reactor files:**
- `Reactor/Core/ChildReconciler.cs` — `ReconcileKeyed()` uses Remove+Insert for moves
- `Reactor/Core/ChildCollection.cs` — Abstraction over Panel.Children

---

## 11. Pre-Warm Element Pools During Idle Time (Tactical)

**Problem:** Element pools start empty. The first render of a complex component creates all controls from scratch — the "cold start" penalty. Subsequent renders benefit from pooling, but the initial render is the most performance-critical (it's what the user sees first).

**Proposal:** During `DispatcherQueue` idle time, pre-allocate common control types into the pool. Use heuristics from the component tree structure: if the root component contains a `LazyVStack<Item>` with a view builder that produces `HStack(Image, VStack(Text, Text))`, pre-warm the pool with 32 StackPanels, 32 Images, and 64 TextBlocks before the first scroll event.

**Impact:** Eliminates cold-start allocation stutter for the first screenful of virtualized items.

**Reactor files:**
- `Reactor/Core/ElementPool.cs` — Pool management, max 32 per type
- `Reactor/Hosting/ReactorHost.cs` — Could schedule pre-warming on idle
- `Reactor/Core/ElementFactory.cs` — Could analyze view builder output types

---

## 12. Incremental Tree Serialization with Dirty Tracking (Medium)

**Problem:** `TreeSerializer.SerializeWithMapping()` does a full BFS traversal and serializes the entire Element tree on every reconciliation pass. For a 1000-node tree where only 3 nodes changed, this wastes ~99.7% of serialization work.

**Proposal:** Add dirty tracking to the Element tree. When `UseState` produces a new value, mark the owning component and its subtree as dirty. `TreeSerializer` would then only re-serialize dirty subtrees, reusing cached ViewNode/ViewProp arrays for clean subtrees. The Rust differ already handles subtree replacement via its `Replace` patch — it just needs the serializer to provide stable subtree references.

**Impact:** Reduces serialization cost from O(n) to O(changed) per render. For typical UI updates (user types in a text field, counter increments), this could be 100x faster serialization.

**Reactor files:**
- `Reactor/Core/TreeSerializer.cs` — `Serialize()` / `SerializeWithMapping()` BFS traversal
- `Reactor/Core/RenderContext.cs` — `UseState` / state change triggers
- `Reactor/Hosting/ReactorHost.cs` — `RequestRender()` could carry dirty component info

---

## 13. Unified Element Recycling with ItemsRepeater's ViewManager (Medium)

**Problem:** Reactor has its own `ElementPool` and ItemsRepeater has its own `ViewManager` with separate recycling pools (PinnedPool, UniqueIdResetPool). These two systems don't know about each other. When Reactor unmounts a virtualized item, it explicitly does NOT pool (comment in `ElementFactory.RecycleElementCore` explains why), losing the recycling opportunity.

**Proposal:** Integrate Reactor's recycling with ItemsRepeater's ViewManager lifecycle. Register Reactor's element pool as a custom recycling backend for ViewManager. When ItemsRepeater recycles an element, instead of clearing it to the factory, transition it to Reactor's pool with element state preserved. When ItemsRepeater requests a new element, check Reactor's pool first. This creates a single unified recycling pipeline.

**Impact:** Eliminates the "recycling gap" where ItemsRepeater recycles a control but Reactor can't reuse it, forcing fresh allocation.

**WinUI3 files:**
- `src/controls/dev/Repeater/ViewManager.h` — `GetElement()` cascade, `ClearElement()` methods
- `src/controls/dev/Repeater/ViewManager.cpp` — Element lifecycle (lines 22-137)
- `src/controls/dev/Repeater/VirtualizationInfo.h` — Per-element state machine

**Reactor files:**
- `Reactor/Core/ElementFactory.cs` — `GetElementCore()` / `RecycleElementCore()`
- `Reactor/Core/ElementPool.cs` — Current standalone pool

---

## 14. Fine-Grained Component Boundaries via WinUI's ContentPresenter (Medium)

**Problem:** Reactor components are opaque to the Rust differ — they appear as "gap nodes" that require imperative C# reconciliation. This means the native diff path can't optimize across component boundaries, falling back to the slower C# path for every component in the tree.

**Proposal:** Map Reactor components to WinUI `ContentPresenter` instances. ContentPresenter already manages content lifecycle and template instantiation natively. Each Reactor component would own a ContentPresenter, and its rendered subtree would be the presenter's content. This gives WinUI native awareness of component boundaries — the presenter's content can be diffed independently, and WinUI's own content transition system provides free animation support.

**Impact:** Components would no longer be opaque to the differ. A tree with 20 components would go from 20 imperative reconciliation fallbacks to 20 independently diffable subtrees.

**WinUI3 files:**
- `src/dxaml/xcp/core/core/elements/ContentPresenter.cpp` — Content lifecycle (lines 76-145)
- `src/dxaml/xcp/core/core/elements/ContentControl.cpp` — Content hosting

**Reactor files:**
- `Reactor/Core/Reconciler.cs` — `ReconcileComponent()`, gap node handling
- `Reactor/Core/TreeSerializer.cs` — Component serialization (treated as leaf/gap)

---

## 15. Expose WinUI's Composition Animations for Reactor Transitions (Medium)

**Problem:** Reactor has no transition/animation system. When elements are inserted, removed, or reordered, changes are instant. WinUI has a full composition animation system (implicit animations, connected animations, layout transitions) that Reactor can't access because it bypasses the template/style system.

**Proposal:** Add a `.Transition()` modifier to Reactor elements that maps to WinUI's `UIElement.TransitionCollection`. For layout changes, use `RepositionThemeTransition`. For inserts/removes, use `AddDeleteThemeTransition`. For connected animations, expose `ConnectedAnimationService` via a `UseConnectedAnimation()` hook. The reconciler would set these during Mount and they'd animate automatically.

**Impact:** Reactor apps get polished, native-feeling animations with zero custom code. List reorders would animate smoothly instead of snapping.

**WinUI3 files:**
- `src/dxaml/xcp/core/core/elements/panel.cpp` — Panel_ChildrenTransitions property
- `src/dxaml/xcp/core/hw/` — Composition animation infrastructure

**Reactor files:**
- `Reactor/Elements/ElementExtensions.cs` — Would add `.Transition()` modifier
- `Reactor/Core/Element.cs` — ElementModifiers would gain transition fields
- `Reactor/Core/Reconciler.Mount.cs` — Would apply TransitionCollection during mount

---

## 16. Native Property Diffing in Rust (Medium)

**Problem:** `TreeSerializer` serializes properties as `(dp_id, value_hash)` pairs, and the Rust differ compares hashes to detect changes. But the actual property application still happens in C# via per-control switch statements in `Reconciler.Update.cs`. The Rust differ knows *which* properties changed but can't *apply* them.

**Proposal:** Extend the Rust differ to emit typed property patches with actual values (not just hashes). For simple properties (strings, doubles, booleans, enums), the patch would carry the new value directly. A thin C interop layer would call WinUI's `SetValue` with the right `KnownPropertyIndex` and value, bypassing the C# switch dispatch entirely.

**Impact:** Eliminates the C# property dispatch overhead for simple properties. For a TextBlock content update, the path becomes: Rust diff → emit `UpdateProp(TextBlock, Content, "new text")` → native SetValue. No C# involved.

**WinUI3 files:**
- `src/dxaml/xcp/core/core/elements/depends.cpp` — `SetValueByKnownIndex()` for direct property access
- `src/dxaml/xcp/core/inc/KnownPropertyIndex.h` — Property index enum

**Reactor files:**
- `Reactor/Native/differ/src/types.rs` — DifferProp, DifferPatch definitions
- `Reactor/Core/Reconciler.Update.cs` — Per-control property switch statements
- `Reactor/Core/PropValueRegistry.cs` — Complex value storage

---

## 17. Direct Composition Visuals for Layout-Only Elements (Wild)

**Problem:** Reactor creates full WinUI FrameworkElement instances for layout-only elements like Border, StackPanel, and Grid. Each one carries the full UIElement allocation overhead: DComp render data (`PrimitiveCompositionPropertyData`), layout storage, automation peer infrastructure, managed peer linking, and property system participation.

**Proposal:** For elements that are purely structural (Border with just margin/padding, StackPanel with orientation/spacing), bypass UIElement creation entirely and create lightweight `Visual` objects directly via the Windows.UI.Composition API. These would participate in the DComp visual tree but skip the entire XAML framework overhead — no DependencyProperty storage, no layout manager participation, no event routing. Reactor's reconciler would calculate layout positions itself (it already knows the constraints) and set `Visual.Offset` and `Visual.Size` directly.

**Impact:** Could reduce element creation cost by 10-50x for layout-only containers. A deeply nested component tree with 5 levels of VStack/HStack nesting would go from 5 FrameworkElement allocations to 5 lightweight Visual allocations.

**WinUI3 files:**
- `src/dxaml/xcp/core/core/elements/uielement.cpp` — UIElement construction overhead (lines 1-150)
- `src/dxaml/xcp/core/hw/hwwalk.cpp` — DComp visual creation
- `src/dxaml/xcp/core/hw/CompositorTreeHost.cpp` — Composition tree management

**Reactor files:**
- `Reactor/Core/Reconciler.Mount.cs` — MountStack, MountBorder, MountGrid create full FrameworkElements
- `Reactor/Core/Element.cs` — StackElement, BorderElement, GridElement definitions

---

## 18. Rust Differ at the Composition Layer (Wild)

**Problem:** The Rust differ currently operates on serialized Element trees (ViewNode/ViewProp arrays) and produces patches that the C# reconciler applies to the WinUI control tree. This means: serialize → diff in Rust → deserialize patches → apply via COM interop → trigger layout → render. Three language boundaries per update.

**Proposal:** Move the Rust differ to operate directly on the DComp visual tree. The differ would read the current visual tree state from shared memory and emit DComp operations (visual inserts, property changes, offset updates) directly, bypassing the XAML layer entirely for layout-only subtrees. Interactive controls would still go through XAML, but pure layout subtrees could be updated in a single Rust→DComp pass.

**Impact:** Eliminates the C# ↔ Rust ↔ C# round-trip for structural updates. For a 500-node tree where 90% are layout-only, this could reduce update latency by 40-60%.

**WinUI3 files:**
- `src/dxaml/xcp/core/hw/CompositorTreeHost.cpp` — DComp tree access
- `src/dxaml/xcp/core/hw/hwcompnode.cpp` — Composition node management

**Reactor files:**
- `Reactor/Native/differ/src/diff.rs` — `diff_subtree()` algorithm
- `Reactor/Native/differ/src/ffi.rs` — FFI boundary
- `Reactor/Core/TreeSerializer.cs` — Serialization for Rust differ

---

## 19. Shared Memory Ring Buffer for Rust ↔ C# Communication (Wild)

**Problem:** Every Rust differ invocation involves marshaling flat arrays across the FFI boundary: `ViewNode[]`, `ViewProp[]` in, `ViewPatch[]` out. While the current zero-copy design (patches point into Rust heap) is efficient for reads, the serialization of the input tree still copies data.

**Proposal:** Use a shared memory ring buffer for bidirectional communication. C# writes serialized tree nodes directly into a memory-mapped region. Rust reads from the same region without copying. Patches are written to a separate output region. The ring buffer supports pipelining: C# can begin serializing the next frame while Rust is still diffing the current one.

**Impact:** Eliminates all FFI marshaling overhead. For a 1000-node tree, this removes ~40KB of array copying per reconciliation pass. The pipelining benefit is larger — it overlaps serialization and diffing.

**Reactor files:**
- `Reactor/Native/differ/src/ffi.rs` — Current FFI boundary
- `Reactor/Core/ViewDiffer.cs` — P/Invoke wrappers, pointer management
- `Reactor/Core/TreeSerializer.cs` — Could write directly to shared memory

---

## 20. Reactor as WinUI's Official Declarative Layer (Wild)

**Problem:** WinUI's declarative story is XAML + data binding + MVVM. This requires: .xaml files, code-behind, INotifyPropertyChanged boilerplate, DataTemplate definitions, converter classes, and style resources. The cognitive overhead is enormous compared to Reactor's `Text("hello").Bold()`.

**Proposal:** Ship Reactor as a first-party WinUI package (`Microsoft.UI.Xaml.Declarative`). This would involve:
1. Adding Reactor-aware APIs to WinUI controls (e.g., `IReconcilable` interface for incremental updates)
2. Exposing internal WinUI APIs to Reactor (layout suppression, direct property access, composition visuals)
3. Making Reactor's Element types part of the WinUI SDK
4. Providing migration tooling (XAML → Reactor converter)

**Impact:** Every WinUI developer gets a modern, React-like development experience. Eliminates the XAML/MVVM learning curve. Microsoft ships a competitive declarative UI framework alongside SwiftUI and Jetpack Compose.

**WinUI3 files:**
- `src/controls/dev/` — Every control would get an `IReconcilable` implementation
- `src/dxaml/xcp/core/layout/LayoutManager.cpp` — Would expose batch APIs
- `src/dxaml/xcp/core/core/elements/depends.cpp` — Would expose fast property paths

---

## 21. Parallel Subtree Reconciliation (Wild)

**Problem:** Reconciliation is single-threaded. For a tree with independent subtrees (e.g., a split-pane view with left navigation and right content), both sides are reconciled sequentially even though they have no data dependencies.

**Proposal:** Identify independent subtrees (components with no shared state) and reconcile them in parallel on separate threads. Each thread produces a list of patches. Patches are applied on the UI thread in a single batch. The Rust differ already supports this — `DiffContext` is per-thread, and patches are returned as arrays that can be concatenated.

**Impact:** A complex app with 4 independent panels could reconcile 4x faster on multi-core machines. The constraint is that WinUI controls can only be touched on the UI thread, so only the diff phase parallelizes — patch application remains single-threaded.

**Reactor files:**
- `Reactor/Core/Reconciler.cs` — `ReconcileComponent()` as parallelization boundary
- `Reactor/Native/differ/src/arena.rs` — `DiffContext` is already per-instance (not global)
- `Reactor/Hosting/ReactorHost.cs` — Would coordinate parallel diff + sequential apply

---

## 22. Programmatic ThemeResource Setter — Eliminate XamlReader.Load for Theme Bindings (Unblock)

**Problem:** Reactor's theming system uses a three-tier theme value model (local concrete > theme token > default) where tier-2 "theme tokens" reference WinUI's semantic theme resources (e.g., `AccentFillColorDefaultBrush`, `TextFillColorPrimaryBrush`). The `ApplyThemeBindings` method in `Reconciler.cs` (lines 1751-1809) applies these by constructing a WinUI `Style` with `{ThemeResource}` setters — but WinUI has **no code-based API** to create a Setter that references a ThemeResource. The only path is to build a XAML string and parse it via `XamlReader.Load()`:

```csharp
// Current implementation in Reconciler.cs:1751-1809
private static void ApplyThemeBindings(FrameworkElement fe, IReadOnlyDictionary<string, ThemeRef> bindings)
{
    var setters = new StringBuilder();
    var targetType = GetStyleTargetType(fe);
    foreach (var (property, themeRef) in bindings)
    {
        var dp = GetDependencyPropertyName(fe, property);
        var escapedResourceKey = SecurityElement.Escape(themeRef.ResourceKey);
        setters.Append($"<Setter Property='{dp}' Value='{{ThemeResource {escapedResourceKey}}}'/>");
    }
    var xaml = $"<Style xmlns='...' TargetType='{targetType}'>" + setters + "</Style>";
    var style = (Style)XamlReader.Load(xaml); // Full XML parse per element per render
    if (fe.Style is Style existing && existing.TargetType == style.TargetType)
        style.BasedOn = existing; // Fragile chaining
    fe.Style = style;
}
```

This fires on **every mount AND every update** for every theme-bound element. A screen with 50 themed elements produces 50 `XamlReader.Load` calls per render cycle — string building, XML parsing, namespace resolution, and Style object instantiation on the hot path. There is no caching: 20 buttons with `.Background(Theme.Accent)` create 20 separate Style objects from 20 separate XAML parses.

Beyond performance, the Style-based approach creates correctness issues:
1. **Style clobbering:** `fe.Style = style` overrides any existing style, including those set by `.ApplyStyle("AccentButtonStyle")` or WinUI's implicit style resolution (see Proposal #9 cross-reference). The `BasedOn` chaining is fragile — it only works when the existing style's TargetType matches.
2. **Limited property coverage:** `GetDependencyPropertyName` (lines 1802-1809) hard-codes only 3 properties — Background, Foreground, and BorderBrush. Fill/Stroke on shapes, PlaceholderForeground on TextBox, SelectionHighlightColor, CaretBrush, and any other brush property cannot be theme-bound. Expanding coverage requires knowing each property's XAML string name and valid control types — a maintenance burden that grows linearly.
3. **AOT/trimming incompatibility:** Same issue as Proposal #1.

The architecture decision to use `{ThemeResource}` is correct — it delegates theme resolution to WinUI's native machinery, which correctly handles Light/Dark/HighContrast and per-element `RequestedTheme` overrides. The problem is that the only way to get a `{ThemeResource}` reference from code is to round-trip through XML parsing.

**Proposal:** WinUI should expose a programmatic API to set theme-resource references on DependencyProperties:

```csharp
// Option A: Setter-level theme resource reference
var setter = new Setter(Control.BackgroundProperty, new ThemeResourceReference("AccentFillColorDefaultBrush"));
var style = new Style(typeof(Button)) { Setters = { setter } };

// Option B: Direct per-element theme resource binding (preferred)
fe.SetThemeResourceBinding(Control.BackgroundProperty, "AccentFillColorDefaultBrush");
// Registers the property for re-evaluation on ActualThemeChanged, same as {ThemeResource} in XAML

// Option C: Batch binding API for frameworks
fe.SetThemeResourceBindings(new[] {
    (Control.BackgroundProperty, "AccentFillColorDefaultBrush"),
    (Control.ForegroundProperty, "TextFillColorPrimaryBrush"),
    (Control.BorderBrushProperty, "CardStrokeColorDefaultBrush"),
});
```

Option B is the most impactful — it bypasses the Style system entirely, setting a theme-reactive binding directly on the element's DependencyProperty. Internally, this would register the property for re-evaluation when `ActualTheme` changes, identical to how `{ThemeResource}` markup works in the XAML parser but triggered from code. This also resolves the style clobbering problem (Proposal #9 cross-reference) because theme bindings wouldn't occupy the Style slot.

**Impact:** Eliminates all `XamlReader.Load` calls from the theming hot path. A 50-element themed screen goes from 50 XML parses to 50 property sets (~1000x cheaper per operation). Removes string allocation, XML parsing, namespace resolution, and Style object creation from every render cycle. Unblocks expanding ThemeRef beyond 3 properties — with a programmatic API, any brush DependencyProperty could be theme-bound (Fill, Stroke, PlaceholderForeground, SelectionHighlightColor, etc.) without maintaining a hard-coded property-name map. Option B also eliminates the style clobbering issue entirely, making Proposals #9 and #22 complementary instead of conflicting.

**WinUI3 files:**
- `src/dxaml/xcp/core/core/elements/Style.cpp` — Style/Setter creation, SetValue path for themed setters
- `src/dxaml/xcp/core/core/elements/framework.cpp` — `{ThemeResource}` resolution during property evaluation — the behavior to expose programmatically
- `src/dxaml/xcp/core/Parser/XamlReader.cpp` — `XamlReader.Load()`, the path being eliminated
- `src/dxaml/xcp/core/core/elements/depends.cpp` — `CDependencyObject::SetValue`, theme-aware property evaluation
- `src/dxaml/xcp/core/theming/ThemeResource.cpp` — Theme resource tracking and re-evaluation on theme change

**Reactor files:**
- `Reactor/Core/Reconciler.cs` — `ApplyThemeBindings` (lines 1751-1809), `GetStyleTargetType` (lines 1786-1800), `GetDependencyPropertyName` (lines 1802-1809) — all three methods would be replaced by direct `SetThemeResourceBinding` calls
- `Reactor/Core/Theme.cs` — `ThemeRef` struct (lines 10-127), `Theme` static class (lines 55-127) — `ThemeRef.Resolve()` would become fallback-only; the struct itself remains as declarative intent
- `Reactor/Elements/ElementExtensions.cs` — `ModifyTheme` (lines 1254-1261), `Background(ThemeRef)` (line 262), `Foreground(ThemeRef)` (line 278), `WithBorder(ThemeRef)` (line 302) — API stays the same, implementation simplifies dramatically
- `Reactor/Core/Element.cs` — `ThemeBindings` property (lines 57-63) — still stores declarative intent, reconciler applies differently
- `Reactor/Hosting/ReactorHostControl.cs` — `ActualThemeChanged` listener — with Option B, WinUI handles re-evaluation natively; the full re-render on theme change could become optional

---

## 23. Lightweight Styling API from Code (Tactical)

**Problem:** WinUI's lightweight styling system allows per-control customization of visual states by overriding resource keys in a control's `Resources` dictionary. This is how every WinUI control implements its hover, pressed, disabled, and focused visual states — the control template references resource keys like `ButtonBackgroundPointerOver`, `ButtonForegroundPressed`, `TextBoxBorderBrushFocused`, and the app can override them per-instance:

```xml
<!-- Only works in XAML -->
<Button Content="Custom hover">
  <Button.Resources>
    <SolidColorBrush x:Key="ButtonBackgroundPointerOver" Color="Red"/>
    <SolidColorBrush x:Key="ButtonBackgroundPressed" Color="DarkRed"/>
  </Button.Resources>
</Button>
```

Reactor's theming covers Background, Foreground, and BorderBrush in their **resting state only**. A button with `.Background(Theme.Accent)` gets the correct accent color in both Light and Dark themes, but its hover/pressed/disabled colors remain WinUI defaults. When the resting state is customized but visual states aren't, the result is visually jarring — the button snaps from a custom accent color to the default hover color on mouse-over.

There are three sub-problems:

1. **No discovery API:** There is no way to programmatically enumerate which lightweight styling keys a control supports. The keys are string constants scattered across XAML theme resource files (`Button_themeresources.xaml`, `TextBox_themeresources.xaml`, etc.). A developer must read WinUI source to find valid key names.

2. **No type safety:** Keys are plain strings — `"ButtonBackgoundPointerOver"` (typo) silently does nothing. No compile-time or runtime validation.

3. **No theme-variant overrides from code:** Setting `button.Resources["ButtonBackgroundPointerOver"] = redBrush` works from code but applies a single value regardless of theme. To make lightweight styling theme-aware, you'd need to construct a `ResourceDictionary` with `ThemeDictionaries` containing Light/Dark/HighContrast variants — the same verbose setup as custom theme resources (Proposal #24), but repeated per-control.

**Proposal:** WinUI should expose lightweight styling as a typed, discoverable API:

```csharp
// Option A: Typed resource key constants per control
button.Resources[Button.ResourceKeys.BackgroundPointerOver] = hoverBrush;
button.Resources[Button.ResourceKeys.BackgroundPressed] = pressedBrush;
// ResourceKeys would be generated from the control's theme resource XAML

// Option B: Visual state resource override with theme awareness
button.SetStateThemeResource("PointerOver", Control.BackgroundProperty, "AccentFillColorSecondaryBrush");
button.SetStateThemeResource("Pressed", Control.BackgroundProperty, "AccentFillColorTertiaryBrush");

// Option C: Batch visual state customization
button.SetLightweightStyle(new LightweightStyle {
    { ButtonStates.PointerOver, Control.BackgroundProperty, Theme.AccentSecondary },
    { ButtonStates.Pressed, Control.BackgroundProperty, Theme.AccentTertiary },
    { ButtonStates.Disabled, Control.BackgroundProperty, Theme.AccentDisabled },
});
```

Reactor would then expose this as fluent modifiers:
```csharp
Button("Click me")
    .Background(Theme.Accent)
    .StateBackground("PointerOver", Theme.AccentSecondary)
    .StateBackground("Pressed", Theme.AccentTertiary)
    .StateBackground("Disabled", Theme.AccentDisabled)
```

**Impact:** Enables full visual state theming from code. Reactor could offer per-state theme token bindings that adapt to Light/Dark/HighContrast. Eliminates the visual discontinuity where resting-state colors are themed but hover/pressed/disabled states snap back to defaults. Makes lightweight styling discoverable and type-safe. Combined with Proposal #22 (programmatic ThemeResource), this would give Reactor feature parity with CSS pseudo-class styling (`:hover`, `:active`, `:disabled`) and SwiftUI's `buttonStyle` system.

**WinUI3 files:**
- `src/controls/dev/CommonStyles/Button_themeresources.xaml` — Button lightweight styling keys (pattern repeated for every control)
- `src/controls/dev/CommonStyles/TextBox_themeresources.xaml` — TextBox lightweight styling keys
- `src/dxaml/xcp/core/core/elements/Control.cpp` — Control template resource resolution, visual state application
- `src/dxaml/xcp/core/core/elements/ResourceDictionary.cpp` — Per-element Resources dictionary lookup chain
- `src/dxaml/xcp/core/core/elements/VisualStateManager.cpp` — Visual state transitions and resource application

**Reactor files:**
- `Reactor/Elements/ElementExtensions.cs` — Would add state-aware theme modifiers (`.StateBackground()`, `.StateForeground()`, etc.)
- `Reactor/Core/Reconciler.cs` — `ApplyThemeBindings` would expand to handle per-state overrides, or a parallel `ApplyLightweightStyle` method
- `Reactor/Core/Reconciler.Mount.cs` — Would set control `Resources` entries during mount for lightweight styling
- `Reactor/Core/Element.cs` — `ElementModifiers` would gain a `StateOverrides` dictionary: `IReadOnlyDictionary<(string state, string property), ThemeRef>`

---

## 24. Programmatic Custom Theme Resource Definitions (Tactical)

**Problem:** Reactor's `Theme` static class (in `Theme.cs`, lines 55-127) provides 60+ semantic tokens mapped to WinUI's built-in theme resources, and `Theme.Ref("key")` can reference any existing resource by name. But there is no Reactor-level API — and no streamlined WinUI API — to **define new** theme-aware resources that provide different values for Light, Dark, and HighContrast themes.

An app that wants branded colors ("BrandPrimary" = corporate blue in Light, lighter blue in Dark) must manually construct a `ResourceDictionary` with `ThemeDictionaries` in code-behind:

```csharp
// Current workaround — 15+ lines of imperative setup, typically in App.xaml.cs
var lightDict = new ResourceDictionary();
lightDict["BrandPrimary"] = new SolidColorBrush(Color.FromArgb(255, 0, 90, 158));
lightDict["BrandSecondary"] = new SolidColorBrush(Color.FromArgb(255, 96, 94, 92));
var darkDict = new ResourceDictionary();
darkDict["BrandPrimary"] = new SolidColorBrush(Color.FromArgb(255, 100, 180, 255));
darkDict["BrandSecondary"] = new SolidColorBrush(Color.FromArgb(255, 161, 159, 157));
var themeDict = new ResourceDictionary();
themeDict.ThemeDictionaries["Light"] = lightDict;
themeDict.ThemeDictionaries["Dark"] = darkDict;
Application.Current.Resources.MergedDictionaries.Add(themeDict);
// Theme.Ref("BrandPrimary") now works — but this setup has no Reactor integration,
// no validation, and must run before any UI renders
```

This defeats Reactor's declarative model entirely. The theming design spec (`docs/spec/duct-theming-design.md`) proposed a `ReactorThemeResources` class for declarative custom theme definition, but it was never implemented. Without it:
- No branded colors that adapt to Light/Dark (must hard-code or use the verbose workaround above)
- No app-specific semantic tokens (e.g., "PricingPositive" = green in light, lighter green in dark)
- No component-level theme scoping (a component can't define theme tokens for its subtree)
- Every competitor has this: React/Material UI has `createTheme()`, SwiftUI has Color asset catalogs with Light/Dark/HighContrast variants, Compose has `lightColorScheme()`/`darkColorScheme()`

**Proposal:** Two levels — a WinUI API improvement and a Reactor-level DSL:

**(a) WinUI: Streamlined theme resource registration API:**

```csharp
// Current: 15+ lines of nested ResourceDictionary construction
// Proposed: single-call registration
ThemeResources.Define("BrandPrimary")
    .Light(new SolidColorBrush(Color.FromArgb(255, 0, 90, 158)))
    .Dark(new SolidColorBrush(Color.FromArgb(255, 100, 180, 255)))
    .HighContrast(new SolidColorBrush(Colors.Yellow))
    .Register();  // Adds to Application.Current.Resources.ThemeDictionaries

// Batch registration
ThemeResources.RegisterBatch(new[] {
    ("BrandPrimary",   lightBlue, darkLightBlue, hcYellow),
    ("BrandSecondary", lightGray, darkLightGray, hcWhite),
});
```

**(b) Reactor: Declarative theme definition in component model:**

```csharp
// In ReactorApp configuration — runs before first render
ReactorApp.DefineTheme(theme =>
{
    theme.Color("BrandPrimary",     light: "#005A9E", dark: "#64B4FF");
    theme.Color("BrandSecondary",   light: "#605E5C", dark: "#A19F9D");
    theme.Color("PricingPositive",  light: "#107C10", dark: "#6CCB5F");
    theme.Color("PricingNegative",  light: "#D13438", dark: "#FF6B6E");
});

// In components — uses existing ThemeRef system seamlessly
Text("+$12.50").Foreground(Theme.Ref("PricingPositive"))
Border(content).Background(Theme.Ref("BrandPrimary"))
```

**Impact:** Enables branded and domain-specific theme tokens that work with Reactor's existing `ThemeRef` system and `ApplyThemeBindings` pipeline. Developers define colors once and they adapt to Light/Dark/HighContrast automatically. Combined with Proposals #22 and #23, this completes the theming story: #22 makes theme binding fast, #23 extends it to visual states, and #24 lets apps define their own tokens. Puts Reactor's theming on par with React's `createTheme()`, SwiftUI's asset catalogs, and Compose's `MaterialTheme` color schemes.

**WinUI3 files:**
- `src/dxaml/xcp/core/core/elements/ResourceDictionary.cpp` — `ThemeDictionaries` property, merged dictionary resolution, resource lookup chain
- `src/dxaml/xcp/core/theming/ThemeResource.cpp` — Theme resource tracking — custom resources must participate in the same re-evaluation pipeline as built-in ones
- `src/dxaml/xcp/core/core/elements/framework.cpp` — Resource lookup chain (element → parent → app → system) — custom resources must be discoverable at the right level

**Reactor files:**
- `Reactor/Core/Theme.cs` — `Theme` static class (lines 55-127) — would gain `DefineTheme()` / `RegisterCustom()` APIs, potentially a `ThemeBuilder` class
- `Reactor/Elements/ThemeResource.cs` — `Brush()`, `Double()`, `Get<T>()` lookup helpers — would include custom resource resolution
- `Reactor/Hosting/ReactorHost.cs` — Initialization path — custom theme registration must happen before first render
- `Reactor/Hosting/ReactorHostControl.cs` — `ActualThemeChanged` listener — already triggers re-render, which re-resolves custom theme resources automatically

---

## 25. Per-Element RequestedTheme with Programmatic ThemeResource (Unblock)

**Problem:** WinUI's `FrameworkElement.RequestedTheme` lets a subtree opt into a different theme variant (e.g., a dark sidebar in an otherwise light app). This works perfectly for native XAML controls because their templates use `{ThemeResource}` markup that WinUI re-evaluates when the effective theme changes. However, there is **no code-based equivalent** that works with the same per-element theme scoping.

Reactor's theming system applies theme-resource-bound brushes by building `Style` objects at runtime via `XamlReader.Load()` with `{ThemeResource}` setters (see Proposal #22). This approach has a critical interaction failure with `RequestedTheme`:

1. **Bottom-up mounting:** Reactor's reconciler creates child controls before placing them in the parent's visual tree. A parent with `RequestedTheme = Dark` has its children mounted as standalone controls — at that point, `{ThemeResource}` in children's dynamically-loaded Styles resolves against the **application theme** (Light), not the parent's intended Dark theme.

2. **{ThemeResource} in XamlReader.Load is not live:** Unlike `{ThemeResource}` in XAML-declared templates (which participates in WinUI's theme-change tracking system), `{ThemeResource}` in Styles created via `XamlReader.Load()` appears to resolve once at parse time. When a child element later enters a parent's visual tree with a different `RequestedTheme`, the `{ThemeResource}` values in the dynamically-loaded Style do **not** re-resolve. Native control templates (Button, TextBlock, etc.) DO update correctly because their `{ThemeResource}` references are registered through the XAML parser's theme-tracking infrastructure.

3. **No programmatic alternative:** There is no code-based API to create a theme-reactive property binding that participates in the same per-element theme resolution as XAML's `{ThemeResource}`. Manual resolution via `Application.Current.Resources.ThemeDictionaries` can look up the correct brush for a given theme name, but the result is a static brush — it doesn't re-resolve when the effective theme changes. The only way to get live theme tracking is through XAML markup, which is inaccessible programmatically.

**Current workaround in Reactor:** The `.RequestedTheme()` modifier correctly sets `FrameworkElement.RequestedTheme` on the control, which makes **native WinUI controls** (Button, TextBlock, ToggleSwitch, etc.) adopt the correct theme variant through their built-in XAML templates. However, Reactor's own `ThemeRef` bindings (`.Background(Theme.Accent)`, `.Foreground(Theme.PrimaryText)`) on child elements do **not** follow the override. Users must avoid `ThemeRef` inside `RequestedTheme` subtrees and instead rely on native WinUI implicit styling, which limits the usefulness of per-element theme overrides in a declarative framework.

**What we tried and why it failed:**
- **Style caching with {ThemeResource}:** Cached the parsed Style and re-applied on update. Even with `fe.Style = null; fe.Style = cachedStyle;` to force re-evaluation, `{ThemeResource}` in the Style did not re-resolve against the element's effective theme.
- **Early RequestedTheme application:** Set `RequestedTheme` on parent controls before mounting children. This helped when children were added directly to the parent (StackPanel.Children.Add), but not for single-child containers (Border.Child) where the entire subtree is created before assignment.
- **Thread-static theme scope with manual resolution:** Carried the effective theme through recursive mount calls and resolved brushes manually via `ThemeRef.Resolve()`. Partially worked (text foreground resolved correctly) but Background on the parent container did not update reliably on theme toggle, suggesting deeper issues with the interaction between programmatic brush sets and WinUI's theme evaluation pipeline.

**Proposal:** WinUI should ensure that code-based theme resource bindings participate in the same per-element theme scoping as XAML-declared `{ThemeResource}`:

```csharp
// Option A: SetThemeResourceBinding respects RequestedTheme (extends Proposal #22)
// When the element or an ancestor has RequestedTheme = Dark, the binding
// resolves from the Dark theme dictionary, just like {ThemeResource} in XAML.
fe.SetThemeResourceBinding(Control.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
// Later: parent.RequestedTheme = Dark → fe re-evaluates against Dark resources

// Option B: Theme-scoped resource resolution API
// Resolve a theme resource against a specific element's effective theme,
// including its ancestors' RequestedTheme overrides.
var brush = ThemeResources.Resolve("TextFillColorPrimaryBrush", fe);
// Uses fe.ActualTheme (which inherits RequestedTheme from ancestors)

// Option C: Ensure XamlReader.Load Styles participate in theme tracking
// Styles created via XamlReader.Load should have their {ThemeResource}
// setters registered with the same theme-tracking infrastructure as
// XAML-parsed styles, so they re-resolve on RequestedTheme changes.
```

Option A (extending Proposal #22) is the most complete solution — it eliminates both the `XamlReader.Load` performance issue and the `RequestedTheme` scoping issue in a single API. Option C is the lowest-effort fix on the WinUI side but doesn't address the `XamlReader.Load` performance cost.

**Impact:** Unblocks per-element theme overrides in declarative frameworks. Without this, any C# framework that applies theme resources programmatically (not just Reactor) cannot support mixed-theme UIs. This is a common UX pattern: dark sidebars, dark media controls, dark code editors embedded in light apps. Currently only achievable via XAML templates, which declarative/code-first frameworks cannot use.

**WinUI3 files:**
- `src/dxaml/xcp/core/theming/ThemeResource.cpp` — Theme resource tracking and re-evaluation; the registration mechanism that XAML-parsed `{ThemeResource}` uses but `XamlReader.Load`-created Styles may not
- `src/dxaml/xcp/core/core/elements/framework.cpp` — `RequestedTheme` propagation, `ActualTheme` computation, and the theme-change notification chain that triggers `{ThemeResource}` re-evaluation
- `src/dxaml/xcp/core/Parser/XamlReader.cpp` — Investigate whether `XamlReader.Load` registers `{ThemeResource}` references with the theme-tracking system identically to the main XAML parser
- `src/dxaml/xcp/core/core/elements/depends.cpp` — `SetValue` / `GetValue` paths for theme-aware property evaluation; ensure programmatic `SetThemeResourceBinding` (Proposal #22) participates in per-element theme scoping

**Reactor files:**
- `Reactor/Core/Reconciler.cs` — `ApplyThemeBindings` would use `SetThemeResourceBinding` (Option A) or `ThemeResources.Resolve(key, fe)` (Option B), eliminating both `XamlReader.Load` and the `RequestedTheme` scoping issue
- `Reactor/Core/Reconciler.Mount.cs` — Would no longer need early `RequestedTheme` application in container MountXxx methods; the API would handle scoping automatically
- `Reactor/Elements/ElementExtensions.cs` — `.RequestedTheme()` modifier would work seamlessly with `.Background(Theme.X)` — no workaround documentation needed

**Cross-references:** This proposal extends Proposal #22 (programmatic ThemeResource setter) with the additional requirement of per-element theme scoping via `RequestedTheme`. Solving #22 without this scoping requirement still provides major performance benefits but leaves the `RequestedTheme` + `ThemeRef` interaction broken.

---

## Summary Table

| # | Proposal | Ambition | Effort | Impact |
|---|----------|----------|--------|--------|
| 1 | Programmatic DataTemplate (no XamlReader) | Unblock | S | High |
| 2 | XAML-free navigation stack | Unblock | M | High |
| 3 | Deep virtualization hooks (ListView/Repeater) | Unblock | L | High |
| 4 | Bypass DependencyProperty for Tag | Tactical | S | Medium |
| 5 | Layout coalescing via LayoutManager | Tactical | M | High |
| 6 | Pool interactive controls | Tactical | S | Medium |
| 7 | Replace remount-on-update controls | Tactical | M | Medium |
| 8 | Frame-budget-aware reconciliation | Tactical | M | High |
| 9 | Implicit style participation | Tactical | S | Medium |
| 10 | Children.Move() for reorders | Tactical | S | Medium |
| 11 | Pre-warm element pools on idle | Tactical | S | Low-Medium |
| 12 | Incremental tree serialization | Medium | L | High |
| 13 | Unified recycling with ViewManager | Medium | L | Medium |
| 14 | Component → ContentPresenter mapping | Medium | L | High |
| 15 | Composition animations for transitions | Medium | M | High |
| 16 | Native property diffing in Rust | Medium | L | Medium |
| 17 | Direct Composition visuals for layout | Wild | XL | Very High |
| 18 | Rust differ at Composition layer | Wild | XL | Very High |
| 19 | Shared memory ring buffer | Wild | XL | Medium |
| 20 | Reactor as WinUI's declarative layer | Wild | XXL | Transformative |
| 21 | Parallel subtree reconciliation | Wild | XL | High |
| 22 | Programmatic ThemeResource setter (no XamlReader for themes) | Unblock | M | Very High |
| 23 | Lightweight styling API from code | Tactical | M | Medium |
| 24 | Programmatic custom theme resource definitions | Tactical | S | High |
| 25 | Per-element RequestedTheme with programmatic ThemeResource | Unblock | M | High |
