# Children strategies

Records that tell the reconciler how a control holds its children.

**Learn more:** [Extending Reactor with Native Controls](../../extending-reactor-controls.md), [The Control Reconciler Protocol](../../control-reconciler-protocol.md)

## `AttachedPropWriter<TChildElement>`

Spec 047 §6 / §14 Phase 1 (1.8) — writer for an attached DP from a parent container onto a child control (Grid.Row, Canvas.Left, DockPanel.Dock, etc.). Authors of container handlers register these on the child element type; the engine invokes them during child mount.

**Type-safety contract:** [`AttachedPropWriter.Get`](#get) pulls the value off the typed child element; [`AttachedPropWriter.Write`](#write) applies it to the [`UIElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement) control. Both lambdas are strongly typed — no reflection, AOT-compatible.

```csharp
public sealed record AttachedPropWriter<TChildElement>(string Name, Func<TChildElement, object?> Get, Action<UIElement, object?> Write) where TChildElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

### Constructors

#### `AttachedPropWriter`

Spec 047 §6 / §14 Phase 1 (1.8) — writer for an attached DP from a parent container onto a child control (Grid.Row, Canvas.Left, DockPanel.Dock, etc.). Authors of container handlers register these on the child element type; the engine invokes them during child mount.

**Type-safety contract:** [`AttachedPropWriter.Get`](#get) pulls the value off the typed child element; [`AttachedPropWriter.Write`](#write) applies it to the [`UIElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement) control. Both lambdas are strongly typed — no reflection, AOT-compatible.

```csharp
public AttachedPropWriter(string Name, Func<TChildElement, object?> Get, Action<UIElement, object?> Write)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Name` | `string` |  |
| `Get` | `Func<TChildElement, object?>` |  |
| `Write` | `Action<UIElement, object?>` |  |

### Properties

#### `Get`

```csharp
public Func<TChildElement, object?> Get  // get, init
```

#### `Name`

```csharp
public string Name  // get, init
```

#### `Write`

```csharp
public Action<UIElement, object?> Write  // get, init
```

## `ChildrenStrategy<TElement, TControl>`

Spec 047 §6 / §14 Phase 1 (1.8) — children-handling strategy declared by a handler. Engine dispatches through the strategy in `V1HandlerAdapter` after the handler's Mount / Update body has returned. Phase 1 ships shape + dispatch; the keyed-reconcile integration with spec-042 lands in Phase 3.

```csharp
public abstract record ChildrenStrategy<TElement, TControl> where TElement : Element where TControl : UIElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

### Constructors

#### `ChildrenStrategy`

```csharp
protected ChildrenStrategy()
```

## `Imperative<TElement, TControl>`

Escape hatch — the handler drives child reconciliation imperatively via [`Imperative.Reconcile`](#reconcile). Use sparingly; the typed strategies above cover the 95% case.

```csharp
public sealed record Imperative<TElement, TControl>(Action<MountContext, TElement, TElement, TControl> Reconcile) where TElement : Element where TControl : UIElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

### Constructors

#### `Imperative`

Escape hatch — the handler drives child reconciliation imperatively via [`Imperative.Reconcile`](#reconcile). Use sparingly; the typed strategies above cover the 95% case.

```csharp
public Imperative(Action<MountContext, TElement, TElement, TControl> Reconcile)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Reconcile` | `Action<MountContext, TElement, TElement, TControl>` |  |

### Properties

#### `Reconcile`

```csharp
public Action<MountContext, TElement, TElement, TControl> Reconcile  // get, init
```

#### `Unmount`

Optional teardown for the children [`Imperative.Reconcile`](#reconcile) mounted. The engine has no view onto imperative slots, so when unmount dispatches to the handler it calls this instead of walking them. Unmount each owned child while it is still parented, then detach it. Declaring it does not tag every control: a control without an element tag is torn down by the engine's generic child walk, which must therefore be able to reach the owned children. That walk also tears down what the handler does not own, so tag the control ([`Reconciler.SetElementTag`](Reconciler.md#setelementtag)) when a slot can hold such UI.

```csharp
public Action<UnmountContext, TControl>? Unmount  // get, init
```

## `ItemsHost<TElement, TControl>`

Flat items host for native ItemCollection sinks and third-party controls that forward an IList\<object\> to an ItemsRepeater. Element children are reconciled in place: keys match across insertions and moves; unkeyed children retain positional identity. Raw items are compared with ItemEquals (object.Equals by default).

The live collection is incrementally inserted, removed and reordered, never reset on update. ObservableCollection moves emit one Move notification. Property-only and descendant-state updates do not mutate the collection.

SetCollection opts interface-typed, writable Items properties into a stable observable backing collection when their initial list has no change notifications. Native read-only ItemCollection properties keep their sink. Removed and terminally unmounted Element children receive real teardown.

```csharp
public sealed record ItemsHost<TElement, TControl>(Func<TElement, IReadOnlyList<object>> GetItems, Func<TControl, IList<object>> GetCollection) where TElement : Element where TControl : UIElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

### Constructors

#### `ItemsHost`

Flat items host for native ItemCollection sinks and third-party controls that forward an IList\<object\> to an ItemsRepeater. Element children are reconciled in place: keys match across insertions and moves; unkeyed children retain positional identity. Raw items are compared with ItemEquals (object.Equals by default).

The live collection is incrementally inserted, removed and reordered, never reset on update. ObservableCollection moves emit one Move notification. Property-only and descendant-state updates do not mutate the collection.

SetCollection opts interface-typed, writable Items properties into a stable observable backing collection when their initial list has no change notifications. Native read-only ItemCollection properties keep their sink. Removed and terminally unmounted Element children receive real teardown.

```csharp
public ItemsHost(Func<TElement, IReadOnlyList<object>> GetItems, Func<TControl, IList<object>> GetCollection)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `GetItems` | `Func<TElement, IReadOnlyList<object>>` |  |
| `GetCollection` | `Func<TControl, IList<object>>` |  |

### Properties

#### `GetCollection`

```csharp
public Func<TControl, IList<object>> GetCollection  // get, init
```

#### `GetItems`

```csharp
public Func<TElement, IReadOnlyList<object>> GetItems  // get, init
```

#### `ItemEquals`

Optional equality predicate for raw (non-Element) items. A positionally equal list retains its existing raw instances, matching the legacy unchanged-list fast path. On a structural update, matched raw value-equal instances are retained; Id-equal objects with changed payloads are replaced. Element children follow panel skip rules: dirty paths, changed context, theme sensitivity, resource refresh and force-rendering decline the skip. Defaults to object.Equals.

```csharp
public Func<object?, object?, bool>? ItemEquals  // get, init
```

#### `SetCollection`

Optional setter for an interface-typed Items property. On mount the engine replaces a non-observable backing list with a stable observable collection. Read-only native ItemCollection sinks are never replaced.

```csharp
public Action<TControl, IList<object>>? SetCollection  // get, init
```

## `ItemsHostOptions`

Placeholder for future ItemsHost options (virtualization mode, container template etc.). Phase 1 carries no fields — Phase 3 may add when more handler authors arrive. Retained for source compat with the Phase 1 ItemsHost shape.

```csharp
public sealed record ItemsHostOptions
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

### Constructors

#### `ItemsHostOptions`

```csharp
public ItemsHostOptions()
```

## `NamedSlot<TElement, TControl>`

One named slot on a [`NamedSlots<TElement, TControl>`](#namedslotstelement-tcontrol) host. [`NamedSlot.Name`](#name_1) is informational; binding is by lambda.

```csharp
public sealed record NamedSlot<TElement, TControl>(string Name, Func<TElement, Element?> GetChild, Action<TControl, UIElement?> SetChild) where TElement : Element where TControl : UIElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

### Constructors

#### `NamedSlot`

One named slot on a [`NamedSlots<TElement, TControl>`](#namedslotstelement-tcontrol) host. [`NamedSlot.Name`](#name_1) is informational; binding is by lambda.

```csharp
public NamedSlot(string Name, Func<TElement, Element?> GetChild, Action<TControl, UIElement?> SetChild)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Name` | `string` |  |
| `GetChild` | `Func<TElement, Element?>` |  |
| `SetChild` | `Action<TControl, UIElement?>` |  |

### Properties

#### `GetChild`

```csharp
public Func<TElement, Element?> GetChild  // get, init
```

#### `GetCurrentChild`

Optional: read the current child from the control. Required for structural child reconciliation; same contract as [`SingleContent.GetCurrentChild`](#getcurrentchild_1).

```csharp
public Func<TControl, UIElement?>? GetCurrentChild  // get, init
```

#### `Name`

```csharp
public string Name  // get, init
```

#### `SetChild`

```csharp
public Action<TControl, UIElement?> SetChild  // get, init
```

## `NamedSlots<TElement, TControl>`

Named-slot host (SplitView with Pane + Content, NavigationView with Header + Content + PaneFooter, etc.). Each [`NamedSlot<TElement, TControl>`](#namedslottelement-tcontrol) binds one slot.

```csharp
public sealed record NamedSlots<TElement, TControl>(IReadOnlyList<NamedSlot<TElement, TControl>> Slots) where TElement : Element where TControl : UIElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

### Constructors

#### `NamedSlots`

Named-slot host (SplitView with Pane + Content, NavigationView with Header + Content + PaneFooter, etc.). Each [`NamedSlot<TElement, TControl>`](#namedslottelement-tcontrol) binds one slot.

```csharp
public NamedSlots(IReadOnlyList<NamedSlot<TElement, TControl>> Slots)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Slots` | `IReadOnlyList<NamedSlot<TElement, TControl>>` |  |

### Properties

#### `Slots`

```csharp
public IReadOnlyList<NamedSlot<TElement, TControl>> Slots  // get, init
```

## `None<TElement, TControl>`

Leaf — no children. Engine performs no dispatch beyond the handler's Mount/Update body.

```csharp
public sealed record None<TElement, TControl> where TElement : Element where TControl : UIElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

### Constructors

#### `None`

Leaf — no children. Engine performs no dispatch beyond the handler's Mount/Update body.

```csharp
public None()
```

## `Panel<TElement, TControl>`

Panel host (StackPanel, Grid, Canvas). Engine mounts each child and appends to the panel's [`UIElementCollection`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.uielementcollection).

**Phase 1 limitation:** the dispatch is append-only; structural diff against the previous render goes through the host's child collection wholesale. Spec-042 keyed-reconcile integration is a Phase 3 follow-up.

**§14 Phase 3-final addition:** [`Panel.PerChildAttached`](#perchildattached) — optional callback invoked after each child mount AND after each child Update. Receives the mounted [`UIElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement) alongside the child element so the descriptor can write WinUI attached DPs (e.g. `Grid.SetRow`, `Canvas.SetLeft`) based on attached-prop hints carried on the child element. No-op by default.

**§14 Phase 3 close-out addition:** [`Panel.PerChildAttachedAfterAll`](#perchildattachedafterall) — optional two-pass callback invoked once after every child has been mounted (Mount path) or reconciled (Update path). Receives the full `(UIElement, Element)` pair list in collection order so the descriptor can apply attached DPs that reference OTHER children by name (e.g. `RelativePanel.SetRightOf(b, a)`). Distinct from [`Panel.PerChildAttached`](#perchildattached), which fires per-child mid-pass and cannot see siblings that haven't mounted yet. Most descriptors set only one of the two; `RelativePanel` is the canonical consumer of the after-all shape.

```csharp
public sealed record Panel<TElement, TControl>(Func<TElement, IReadOnlyList<Element>> GetChildren, Func<TControl, UIElementCollection> GetCollection) where TElement : Element where TControl : UIElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

### Constructors

#### `Panel`

Panel host (StackPanel, Grid, Canvas). Engine mounts each child and appends to the panel's [`UIElementCollection`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.uielementcollection).

**Phase 1 limitation:** the dispatch is append-only; structural diff against the previous render goes through the host's child collection wholesale. Spec-042 keyed-reconcile integration is a Phase 3 follow-up.

**§14 Phase 3-final addition:** [`Panel.PerChildAttached`](#perchildattached) — optional callback invoked after each child mount AND after each child Update. Receives the mounted [`UIElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement) alongside the child element so the descriptor can write WinUI attached DPs (e.g. `Grid.SetRow`, `Canvas.SetLeft`) based on attached-prop hints carried on the child element. No-op by default.

**§14 Phase 3 close-out addition:** [`Panel.PerChildAttachedAfterAll`](#perchildattachedafterall) — optional two-pass callback invoked once after every child has been mounted (Mount path) or reconciled (Update path). Receives the full `(UIElement, Element)` pair list in collection order so the descriptor can apply attached DPs that reference OTHER children by name (e.g. `RelativePanel.SetRightOf(b, a)`). Distinct from [`Panel.PerChildAttached`](#perchildattached), which fires per-child mid-pass and cannot see siblings that haven't mounted yet. Most descriptors set only one of the two; `RelativePanel` is the canonical consumer of the after-all shape.

```csharp
public Panel(Func<TElement, IReadOnlyList<Element>> GetChildren, Func<TControl, UIElementCollection> GetCollection)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `GetChildren` | `Func<TElement, IReadOnlyList<Element>>` |  |
| `GetCollection` | `Func<TControl, UIElementCollection>` |  |

### Properties

#### `GetChildren`

```csharp
public Func<TElement, IReadOnlyList<Element>> GetChildren  // get, init
```

#### `GetCollection`

```csharp
public Func<TControl, UIElementCollection> GetCollection  // get, init
```

#### `PerChildAttached`

Optional per-child attached-prop writer. Called by the engine after each child is mounted (Mount path) AND after each child is reconciled (Update path). The descriptor reads attached-prop hints off the child element (via `Element.GetAttached<T>()` or similar) and writes the corresponding WinUI attached DPs onto the mounted [`UIElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement). Defaults to `null` for containers that don't carry per-child attached props (e.g. `StackPanel`).

```csharp
public Action<TControl, UIElement, Element>? PerChildAttached  // get, init
```

#### `PerChildAttachedAfterAll`

Optional two-pass callback fired once after every child has been mounted/reconciled, receiving the full ordered list of `(UIElement, Element)` pairs. Use for attached DPs that reference siblings by name — e.g. `RelativePanel.SetRightOf`. Defaults to `null`; only RelativePanel-shaped descriptors set it.

```csharp
public Action<TControl, IReadOnlyList<(UIElement Mounted, Element ChildElement)>>? PerChildAttachedAfterAll  // get, init
```

## `PreMountedItems<TElement, TControl>`

§14 Phase 3 completion — pre-mounted, Element-aware positional items host for templated controls whose host has no `ContainerContentChanging` (i.e. [`FlipView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.flipview) templated peer). Items come from the element's `IItemViewSource` implementation — `ItemCount` + `BuildItemView(int)` — and are pre-mounted up-front into the control's flat `IList<object>` sink (e.g. `FlipView.Items`). Closes the carry-forward from §14 Phase 3 finish where `TemplatedFlipViewElement<T>` stayed legacy because the FlipView host can't share the [`TemplatedItemsErased<TElement, TControl>`](#templateditemserasedtelement-tcontrol) realization pipeline used by ListView / GridView (those route through [`Reconciler.BindKeyedItemsSource`](Reconciler.md) which assumes `ContainerContentChanging`).

**Update semantics:** positional reconcile against the previous element. For each shared index, the engine helper [`Reconciler.ReconcileV1Child`](Reconciler.md) runs the standard CanUpdate-or-Mount-or-Unmount slot decision. Excess old slots are `UnmountChild`-ed and removed; surplus new slots are mounted and appended. Mirrors the legacy `UpdateTemplatedFlipView` arm 1:1.

**Invariants asserted in Debug:** the dispatched old element is the same closed type as the new (the engine's `CanUpdate` gate guarantees this for Update arms); `items.Count` tracks the previous source's `ItemCount`. Release builds full-rebuild when an invariant is violated rather than indexing into a stale collection.

```csharp
public sealed record PreMountedItems<TElement, TControl>(Func<TElement, IItemViewSource> GetSource, Func<TControl, IList<object>> GetCollection) where TElement : Element where TControl : FrameworkElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

### Constructors

#### `PreMountedItems`

§14 Phase 3 completion — pre-mounted, Element-aware positional items host for templated controls whose host has no `ContainerContentChanging` (i.e. [`FlipView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.flipview) templated peer). Items come from the element's `IItemViewSource` implementation — `ItemCount` + `BuildItemView(int)` — and are pre-mounted up-front into the control's flat `IList<object>` sink (e.g. `FlipView.Items`). Closes the carry-forward from §14 Phase 3 finish where `TemplatedFlipViewElement<T>` stayed legacy because the FlipView host can't share the [`TemplatedItemsErased<TElement, TControl>`](#templateditemserasedtelement-tcontrol) realization pipeline used by ListView / GridView (those route through [`Reconciler.BindKeyedItemsSource`](Reconciler.md) which assumes `ContainerContentChanging`).

**Update semantics:** positional reconcile against the previous element. For each shared index, the engine helper [`Reconciler.ReconcileV1Child`](Reconciler.md) runs the standard CanUpdate-or-Mount-or-Unmount slot decision. Excess old slots are `UnmountChild`-ed and removed; surplus new slots are mounted and appended. Mirrors the legacy `UpdateTemplatedFlipView` arm 1:1.

**Invariants asserted in Debug:** the dispatched old element is the same closed type as the new (the engine's `CanUpdate` gate guarantees this for Update arms); `items.Count` tracks the previous source's `ItemCount`. Release builds full-rebuild when an invariant is violated rather than indexing into a stale collection.

```csharp
public PreMountedItems(Func<TElement, IItemViewSource> GetSource, Func<TControl, IList<object>> GetCollection)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `GetSource` | `Func<TElement, IItemViewSource>` |  |
| `GetCollection` | `Func<TControl, IList<object>>` |  |

### Properties

#### `GetCollection`

```csharp
public Func<TControl, IList<object>> GetCollection  // get, init
```

#### `GetSource`

```csharp
public Func<TElement, IItemViewSource> GetSource  // get, init
```

## `SingleContent<TElement, TControl>`

Single-content host (Border, ContentControl, Viewbox). The engine mounts `GetChild`'s result and assigns it via `SetChild`.

A null child on mount, or null on both sides of an update, leaves native content supplied through setters alone. Declare an Empty element to intentionally clear content without rendering a child.

**Structural reconcile:** set [`SingleContent.GetCurrentChild`](#getcurrentchild_1) so the engine can read the existing slot value during `Update` and route through `Reconciler.ReconcileV1Child` — that preserves descendant component state across parent re-renders. When left null, the engine remounts the child on every update (only safe for slots that are reset every render anyway). All built-in handlers set it.

```csharp
public sealed record SingleContent<TElement, TControl>(Func<TElement, Element?> GetChild, Action<TControl, UIElement?> SetChild) where TElement : Element where TControl : UIElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

### Constructors

#### `SingleContent`

Single-content host (Border, ContentControl, Viewbox). The engine mounts `GetChild`'s result and assigns it via `SetChild`.

A null child on mount, or null on both sides of an update, leaves native content supplied through setters alone. Declare an Empty element to intentionally clear content without rendering a child.

**Structural reconcile:** set [`SingleContent.GetCurrentChild`](#getcurrentchild_1) so the engine can read the existing slot value during `Update` and route through `Reconciler.ReconcileV1Child` — that preserves descendant component state across parent re-renders. When left null, the engine remounts the child on every update (only safe for slots that are reset every render anyway). All built-in handlers set it.

```csharp
public SingleContent(Func<TElement, Element?> GetChild, Action<TControl, UIElement?> SetChild)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `GetChild` | `Func<TElement, Element?>` |  |
| `SetChild` | `Action<TControl, UIElement?>` |  |

### Properties

#### `GetChild`

```csharp
public Func<TElement, Element?> GetChild  // get, init
```

#### `GetCurrentChild`

Optional: read the current child from the control. Required for structural child reconciliation; if null the engine falls back to remounting the child every Update.

```csharp
public Func<TControl, UIElement?>? GetCurrentChild  // get, init
```

#### `SetChild`

```csharp
public Action<TControl, UIElement?> SetChild  // get, init
```

## `TabItemsHost<TElement, TControl, TItem>`

§14 Phase 3 finish — Ports (10) + (11). Heterogeneous items host for [`TabView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.tabview) and [`Pivot`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.pivot). Each item declares a header + an Element content + (TabView-only) an optional IsClosable / icon hint; the strategy mounts the content, builds the per-control container (e.g. `WinUI.TabViewItem`), and adds it to the host's items sink.

**Update semantics:** in-place positional reconcile against the previous element. For each shared index the existing container is kept and its `Content` reconciled through [`Reconciler.ReconcileV1Child`](Reconciler.md) (CanUpdate-or-mount-or-unmount); the container's own metadata (header / icon / closable) is refreshed via the optional [`TabItemsHost.UpdateContainer`](#updatecontainer) callback. Excess containers are unmounted + removed; surplus new items are created + appended. Containers (and the mounted Content subtree underneath them) therefore survive renders that don't change the tab/pivot set — re-adding every `TabViewItem` / `PivotItem` would re-trigger the tab-strip entrance animation and steal focus from descendant controls. Mirrors the legacy `UpdateTabView` / `UpdatePivot` in-place arms.

**Not covered (still legacy-only):** keyed reorder, pinnable tab headers, and the docking drag pipeline. Index-keyed pairing assumes the item at index `i` in the old list corresponds to index `i` in the new list.

```csharp
public sealed record TabItemsHost<TElement, TControl, TItem>(Func<TElement, IReadOnlyList<TItem>> GetItems, Func<TControl, IList<object>> GetCollection, Func<TItem, Element> GetContent, Func<TItem, UIElement?, object> CreateContainer, Action<TItem, TItem, object>? UpdateContainer = null) where TElement : Element where TControl : FrameworkElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

### Constructors

#### `TabItemsHost`

§14 Phase 3 finish — Ports (10) + (11). Heterogeneous items host for [`TabView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.tabview) and [`Pivot`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.pivot). Each item declares a header + an Element content + (TabView-only) an optional IsClosable / icon hint; the strategy mounts the content, builds the per-control container (e.g. `WinUI.TabViewItem`), and adds it to the host's items sink.

**Update semantics:** in-place positional reconcile against the previous element. For each shared index the existing container is kept and its `Content` reconciled through [`Reconciler.ReconcileV1Child`](Reconciler.md) (CanUpdate-or-mount-or-unmount); the container's own metadata (header / icon / closable) is refreshed via the optional [`TabItemsHost.UpdateContainer`](#updatecontainer) callback. Excess containers are unmounted + removed; surplus new items are created + appended. Containers (and the mounted Content subtree underneath them) therefore survive renders that don't change the tab/pivot set — re-adding every `TabViewItem` / `PivotItem` would re-trigger the tab-strip entrance animation and steal focus from descendant controls. Mirrors the legacy `UpdateTabView` / `UpdatePivot` in-place arms.

**Not covered (still legacy-only):** keyed reorder, pinnable tab headers, and the docking drag pipeline. Index-keyed pairing assumes the item at index `i` in the old list corresponds to index `i` in the new list.

```csharp
public TabItemsHost(Func<TElement, IReadOnlyList<TItem>> GetItems, Func<TControl, IList<object>> GetCollection, Func<TItem, Element> GetContent, Func<TItem, UIElement?, object> CreateContainer, Action<TItem, TItem, object>? UpdateContainer = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `GetItems` | `Func<TElement, IReadOnlyList<TItem>>` |  |  |
| `GetCollection` | `Func<TControl, IList<object>>` |  |  |
| `GetContent` | `Func<TItem, Element>` |  |  |
| `CreateContainer` | `Func<TItem, UIElement?, object>` |  |  |
| `UpdateContainer` | `Action<TItem, TItem, object>?` | `null` |  |

### Properties

#### `CreateContainer`

```csharp
public Func<TItem, UIElement?, object> CreateContainer  // get, init
```

#### `GetCollection`

```csharp
public Func<TControl, IList<object>> GetCollection  // get, init
```

#### `GetContent`

```csharp
public Func<TItem, Element> GetContent  // get, init
```

#### `GetItems`

```csharp
public Func<TElement, IReadOnlyList<TItem>> GetItems  // get, init
```

#### `UpdateContainer`

```csharp
public Action<TItem, TItem, object>? UpdateContainer  // get, init
```

## `TemplatedItems<TItem, TElement, TControl>`

Keyed templated-items host for descriptor-driven typed lists (`ListView<T>`, `GridView<T>`, future `LazyVStack<T>` / `LazyHStack<T>` / `ItemsRepeater<T>`). The strategy declares only the data shape; the engine partial [`Reconciler.BindKeyedItemsSource`](Reconciler.md) owns the realization plumbing (spec-042 `ReactorListState` + `KeyedListDiff`, the shared `ContainerContentChanging` handler, the per-control `ItemsSource` binding).

**Lifecycle:** Mount runs once on first render; Update runs on every subsequent render with the same control instance. Both go through [`Reconciler.BindKeyedItemsSource`](Reconciler.md); the boolean `isMount` parameter selects between fresh-state construction (Mount) and keyed-diff application (Update).

**Key contract:** [`TemplatedItems.KeySelector`](#keyselector) must produce stable, non-null, non-duplicate strings across renders for any given user item. Null / duplicate keys trigger the same `DiffStats.Bailout` path the legacy element-based binder uses — correctness preserved, animation degraded for that diff.

**Per-item Element:** [`TemplatedItems.BuildItemView`](#builditemview) is called lazily by the realization machinery as containers materialize, so large lists never realize all items up front. The returned [`Element`](../elements/Element.md) is reconciled into the container's `ContentControl`.

```csharp
public sealed record TemplatedItems<TItem, TElement, TControl>(Func<TElement, IReadOnlyList<TItem>> GetItems, Func<TItem, int, string> KeySelector, Func<TItem, int, Element> BuildItemView) where TElement : Element where TControl : FrameworkElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

### Constructors

#### `TemplatedItems`

Keyed templated-items host for descriptor-driven typed lists (`ListView<T>`, `GridView<T>`, future `LazyVStack<T>` / `LazyHStack<T>` / `ItemsRepeater<T>`). The strategy declares only the data shape; the engine partial [`Reconciler.BindKeyedItemsSource`](Reconciler.md) owns the realization plumbing (spec-042 `ReactorListState` + `KeyedListDiff`, the shared `ContainerContentChanging` handler, the per-control `ItemsSource` binding).

**Lifecycle:** Mount runs once on first render; Update runs on every subsequent render with the same control instance. Both go through [`Reconciler.BindKeyedItemsSource`](Reconciler.md); the boolean `isMount` parameter selects between fresh-state construction (Mount) and keyed-diff application (Update).

**Key contract:** [`TemplatedItems.KeySelector`](#keyselector) must produce stable, non-null, non-duplicate strings across renders for any given user item. Null / duplicate keys trigger the same `DiffStats.Bailout` path the legacy element-based binder uses — correctness preserved, animation degraded for that diff.

**Per-item Element:** [`TemplatedItems.BuildItemView`](#builditemview) is called lazily by the realization machinery as containers materialize, so large lists never realize all items up front. The returned [`Element`](../elements/Element.md) is reconciled into the container's `ContentControl`.

```csharp
public TemplatedItems(Func<TElement, IReadOnlyList<TItem>> GetItems, Func<TItem, int, string> KeySelector, Func<TItem, int, Element> BuildItemView)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `GetItems` | `Func<TElement, IReadOnlyList<TItem>>` |  |
| `KeySelector` | `Func<TItem, int, string>` |  |
| `BuildItemView` | `Func<TItem, int, Element>` |  |

### Properties

#### `BuildItemView`

```csharp
public Func<TItem, int, Element> BuildItemView  // get, init
```

#### `GetItems`

```csharp
public Func<TElement, IReadOnlyList<TItem>> GetItems  // get, init
```

#### `KeySelector`

```csharp
public Func<TItem, int, string> KeySelector  // get, init
```

## `TemplatedItemsErased<TElement, TControl>`

§14 Phase 3 close-out — keyed templated-items host that erases the per-item type at the strategy level by reading items through the element's `IKeyedItemSource` implementation. The element is the carrier of T; the descriptor is non-generic in TItem.

This is the shape used to port the existing `TemplatedListViewElement<T>` / `TemplatedGridViewElement<T>` family — registered once on a non-generic intermediate base ([`TemplatedListViewElementBase`](../elements/collections.md#templatedlistviewelementbase) / [`TemplatedGridViewElementBase`](../elements/collections.md#templatedgridviewelementbase)) via [`Reconciler.RegisterHandlerForDerivedTypes`](Reconciler.md#registerhandlerforderivedtypes); the engine's base-derived registry walk routes every closed-T variant to the same descriptor. Same realization plumbing as [`TemplatedItems<TItem, TElement, TControl>`](#templateditemstitem-telement-tcontrol): [`Reconciler.BindKeyedItemsSource`](Reconciler.md) owns the `ReactorListState` + `KeyedListDiff` + container-realization pipeline.

```csharp
public sealed record TemplatedItemsErased<TElement, TControl>(Func<TElement, IKeyedItemSource> GetSource) where TElement : Element where TControl : FrameworkElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

### Constructors

#### `TemplatedItemsErased`

§14 Phase 3 close-out — keyed templated-items host that erases the per-item type at the strategy level by reading items through the element's `IKeyedItemSource` implementation. The element is the carrier of T; the descriptor is non-generic in TItem.

This is the shape used to port the existing `TemplatedListViewElement<T>` / `TemplatedGridViewElement<T>` family — registered once on a non-generic intermediate base ([`TemplatedListViewElementBase`](../elements/collections.md#templatedlistviewelementbase) / [`TemplatedGridViewElementBase`](../elements/collections.md#templatedgridviewelementbase)) via [`Reconciler.RegisterHandlerForDerivedTypes`](Reconciler.md#registerhandlerforderivedtypes); the engine's base-derived registry walk routes every closed-T variant to the same descriptor. Same realization plumbing as [`TemplatedItems<TItem, TElement, TControl>`](#templateditemstitem-telement-tcontrol): [`Reconciler.BindKeyedItemsSource`](Reconciler.md) owns the `ReactorListState` + `KeyedListDiff` + container-realization pipeline.

```csharp
public TemplatedItemsErased(Func<TElement, IKeyedItemSource> GetSource)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `GetSource` | `Func<TElement, IKeyedItemSource>` |  |

### Properties

#### `GetSource`

```csharp
public Func<TElement, IKeyedItemSource> GetSource  // get, init
```

## `TreeChildren<TElement, TControl>`

§14 Phase 3 finish — Port (8). Hierarchical children for [`TreeView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.treeview). The strategy declares only the data shape (a [`TreeViewNodeData`](../controls/collection-items.md#treeviewnodedata) tree); the engine builds a matching [`TreeViewNode`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.treeviewnode) tree on [`TreeView.RootNodes`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.treeview.rootnodes), mounting per-node `ContentElement` through the reconciler when any node uses one.

**MVP scope:** positional rebuild on Update — old `ContentElement` subtrees are unmounted and the WinUI tree is reconstructed. No keyed reconcile (descendant component state inside ContentElement nodes is lost across renders that touch the tree). Same correctness contract as the legacy `UpdateTreeView` arm.

```csharp
public sealed record TreeChildren<TElement, TControl>(Func<TElement, IReadOnlyList<TreeViewNodeData>> GetNodes) where TElement : Element where TControl : TreeView
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

### Constructors

#### `TreeChildren`

§14 Phase 3 finish — Port (8). Hierarchical children for [`TreeView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.treeview). The strategy declares only the data shape (a [`TreeViewNodeData`](../controls/collection-items.md#treeviewnodedata) tree); the engine builds a matching [`TreeViewNode`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.treeviewnode) tree on [`TreeView.RootNodes`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.treeview.rootnodes), mounting per-node `ContentElement` through the reconciler when any node uses one.

**MVP scope:** positional rebuild on Update — old `ContentElement` subtrees are unmounted and the WinUI tree is reconstructed. No keyed reconcile (descendant component state inside ContentElement nodes is lost across renders that touch the tree). Same correctness contract as the legacy `UpdateTreeView` arm.

```csharp
public TreeChildren(Func<TElement, IReadOnlyList<TreeViewNodeData>> GetNodes)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `GetNodes` | `Func<TElement, IReadOnlyList<TreeViewNodeData>>` |  |

### Properties

#### `GetNodes`

```csharp
public Func<TElement, IReadOnlyList<TreeViewNodeData>> GetNodes  // get, init
```
