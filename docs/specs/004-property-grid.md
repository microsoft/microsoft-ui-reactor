# Reactor PropertyGrid — Detailed Design

## Design Goals

1. **Metadata-driven** — type-to-editor mapping via an explicit registry, not pure reflection
2. **Recursive decomposition** — records, structs, and custom types decompose into editable parts that themselves resolve through the registry
3. **Mutable and immutable** — side-effect mutation for mutable objects; reconstruct-and-replace for immutable types (records, `readonly struct`)
4. **Built-in primitives** — `int`, `long`, `double`, `float`, `bool`, `string`, `enum` get editors out of the box
5. **Array support** — add, remove, reorder with caller-supplied factory
6. **Windows 11 design** — compact typographic layout (File Explorer density, not Settings density), collapsible categories, tooltip help text
7. **INPC integration** — PropertyGrid observes `INotifyPropertyChanged` on the target object; edits mutate the object, and external mutations re-render the grid

---

## INPC Support in Reactor (Phase 1)

The PropertyGrid needs to observe live mutable objects and re-render when they change. This phase audits the existing observable hooks, identifies gaps, and adds what's needed — as general Microsoft.UI.Reactor (Reactor) infrastructure, not PropertyGrid-specific.

### Existing Hooks (Audit)

Reactor already has three observable hooks in `RenderContext`:

| Hook | Signature | Behavior |
|------|-----------|----------|
| `UseObservable<T>` | `T UseObservable<T>(T source) where T : INPC` | Subscribes to `source.PropertyChanged`. Re-renders on **any** property change. Returns the same source object. |
| `UseObservableProperty<T,P>` | `P UseObservableProperty<T,P>(T source, Func<T,P> selector, string propertyName)` | Subscribes to `source.PropertyChanged`, but only re-renders when `e.PropertyName` matches (or is null/empty). Returns the selected property value. |
| `UseCollection<T>` | `IReadOnlyList<T> UseCollection<T>(ObservableCollection<T> collection)` | Subscribes to `CollectionChanged`. Re-renders on add/remove/reset/move. Returns the collection as `IReadOnlyList<T>`. |

**Implementation pattern (all three):**
```csharp
// All use the same toggle-reducer trick to force re-render:
var (_, forceRender) = UseReducer(false);
UseEffect(() =>
{
    void handler(object? s, PropertyChangedEventArgs e) => forceRender(v => !v);
    source.PropertyChanged += handler;
    return () => source.PropertyChanged -= handler;  // cleanup
}, source);  // re-subscribes if source reference changes
return source;
```

**What works well:**
- Cleanup is correct — unsubscribes on unmount and on source reference change
- Source reference changes between renders are handled (old unsubscribed, new subscribed)
- `UseObservableProperty` provides fine-grained control when only one property matters
- Pattern is composable — multiple hooks can watch different objects independently

### Gap: No Nested/Deep INPC Observation

The critical gap for PropertyGrid is that **none of the hooks observe nested objects**. If a target object has a property `Settings` that itself implements `INotifyPropertyChanged`, changes to `Settings.Theme` will not trigger a re-render of the component that called `UseObservable(target)`.

This matters because the PropertyGrid recursively decomposes objects. A user editing `target.Settings.Theme.AccentColor.R` needs the grid to re-render, but only `target.PropertyChanged` is subscribed.

### New Hook: UseObservableTree

To solve this generally (not just for PropertyGrid), we add a new hook that recursively subscribes to all INPC objects reachable from a root:

```csharp
/// <summary>
/// Observes an object and all nested INotifyPropertyChanged values
/// reachable through its properties. Re-renders when any property
/// at any depth changes. Automatically subscribes/unsubscribes as
/// property values change (e.g., if target.Settings is replaced
/// with a new INPC object, the old one is unsubscribed and the
/// new one subscribed).
/// </summary>
public T UseObservableTree<T>(T source) where T : INotifyPropertyChanged
```

### UseObservableTree — Detailed Design

#### Core Data Structures

```csharp
/// <summary>
/// Manages recursive INPC subscriptions for a single UseObservableTree call.
/// Stored as a UseRef so it persists across renders without re-creation.
/// </summary>
internal class ObservableTreeTracker : IDisposable
{
    private readonly Action _requestRerender;
    private readonly Dictionary<INotifyPropertyChanged, PropertyChangedEventHandler> _subscriptions = new();
    private readonly HashSet<INotifyPropertyChanged> _visiting = new(); // cycle detection

    public ObservableTreeTracker(Action requestRerender)
        => _requestRerender = requestRerender;

    /// <summary>
    /// Synchronize subscriptions to match the current object graph.
    /// Called on mount and whenever the source reference changes.
    /// </summary>
    public void SyncSubscriptions(INotifyPropertyChanged root) { ... }

    public void Dispose() { /* unsubscribe all */ }
}
```

#### Subscription Walk Algorithm

```
SyncSubscriptions(root):
  1. Let desiredSet = new HashSet<INPC>()
  2. Walk(root, desiredSet):
     a. If root is null or already in _visiting → return (cycle detection)
     b. Add root to _visiting
     c. Add root to desiredSet
     d. For each property P of root's type (from cache):
        - If P.PropertyType can implement INPC (is class/interface):
          - Get value = P.GetValue(root)
          - If value is INPC: Walk(value, desiredSet)
     e. Remove root from _visiting
  3. Unsubscribe from objects in _subscriptions.Keys that are NOT in desiredSet
  4. Subscribe to objects in desiredSet that are NOT in _subscriptions.Keys
```

**When a PropertyChanged fires on any subscribed object:**
```
OnNestedPropertyChanged(sender, e):
  1. Request re-render (via the forceRender toggle, same as UseObservable)
  2. Get the new property value: sender.GetType().GetProperty(e.PropertyName)?.GetValue(sender)
  3. If old value was INPC: unsubscribe recursively from old subtree
  4. If new value is INPC: subscribe recursively to new subtree
```

Step 2-4 handles the case where `parent.Child = new ChildModel()` — the old child gets unsubscribed, the new child gets subscribed, all in response to the single `PropertyChanged("Child")` event.

#### Reflection Cache

```csharp
/// <summary>
/// Per-type cache of properties that could hold INPC values.
/// Filters to: public instance properties, getter accessible,
/// property type is class or interface (value types can't be INPC).
/// </summary>
private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _inpcPropertyCache = new();

private static PropertyInfo[] GetInpcCandidateProperties(Type type)
    => _inpcPropertyCache.GetOrAdd(type, t =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
         .Where(p => p.CanRead && !p.PropertyType.IsValueType)
         .ToArray());
```

#### Hook Implementation

```csharp
public T UseObservableTree<T>(T source) where T : INotifyPropertyChanged
{
    var (_, forceRender) = UseReducer(false);
    var trackerRef = UseRef<ObservableTreeTracker?>(null);

    UseEffect(() =>
    {
        var tracker = new ObservableTreeTracker(() => forceRender(v => !v));
        trackerRef.Current = tracker;
        tracker.SyncSubscriptions(source);
        return () => tracker.Dispose();
    }, source);

    return source;
}
```

**Key behaviors:**
- `UseRef` stores the tracker so it persists across renders without triggering re-subscription
- `UseEffect` with `source` as dependency means: if the root object reference changes, dispose old tracker, create new one, re-walk
- Within a stable root, nested changes are handled by the tracker's `OnNestedPropertyChanged` without re-running the effect
- `Dispose()` unsubscribes from every object in `_subscriptions` — called on unmount or root swap

#### Cycle Detection

Object graphs can have circular references (e.g., parent ↔ child back-references). The `_visiting` HashSet prevents infinite recursion during the walk:

```
A.Child → B
B.Parent → A   ← cycle detected, skip
```

Only the walk uses `_visiting` (cleared after each walk). The `_subscriptions` dictionary is the durable set of live subscriptions.

#### ObservableCollection Integration

If a property value is an `ObservableCollection<T>` (which implements `INotifyPropertyChanged` for the collection itself), `UseObservableTree` will subscribe to its `PropertyChanged` event. However, it does **not** automatically subscribe to `CollectionChanged` or to individual items in the collection. For those scenarios, callers should additionally use `UseCollection` or handle item observation explicitly. The PropertyGrid does this internally for array properties.

#### Performance Considerations

| Concern | Mitigation |
|---------|------------|
| Reflection cost | `PropertyInfo[]` cached per `Type` in a `ConcurrentDictionary` — one-time cost per type |
| Large object trees | Walk is O(N) where N = total INPC objects in graph; subscription is O(1) per object |
| Frequent property changes | Handler only re-walks the changed property's subtree, not the whole graph |
| Many subscriptions | Each subscription is one delegate — lightweight; dictionary lookup is O(1) |
| Deep nesting | Walk is depth-first with cycle detection; practical depth is bounded by the object model |

For very large object graphs (hundreds of INPC objects), callers should prefer `UseObservable` (shallow) and manage re-rendering explicitly.

### When to Use Which Hook

| Scenario | Hook | Why |
|----------|------|-----|
| Single object, re-render on any change | `UseObservable` | Lightest weight, no reflection |
| Single object, one property matters | `UseObservableProperty` | Avoids unnecessary re-renders |
| Object tree with nested INPC | `UseObservableTree` | Recursive subscription |
| ObservableCollection structure changes | `UseCollection` | Add/remove/move/reset |
| Immutable state (records, value types) | `UseState` | No INPC needed; replace-and-re-render |

### PropertyGrid's Usage

The PropertyGrid component will use `UseObservableTree` when the target implements `INotifyPropertyChanged`, giving it automatic re-render on any nested property change:

```csharp
class PropertyGridComponent : Component
{
    public override Element Render()
    {
        var element = (PropertyGridElement)Props;

        // Deep observation — any nested INPC change triggers re-render
        if (element.Target is INotifyPropertyChanged inpc)
            UseObservableTree(inpc);

        // Build property tree, render editors, etc.
        // ...
    }
}
```

For non-INPC targets (immutable records edited via `OnRootChanged`), no observation is needed — the parent component holds the state via `UseState` and passes a new target reference on change, which naturally triggers re-render.

### UseCollection Gap for PropertyGrid Arrays

When the PropertyGrid edits an `ObservableCollection<T>` property, it needs to observe both structural changes (add/remove) and item property changes. The existing `UseCollection` only covers structural changes.

**Solution:** The PropertyGrid will combine `UseCollection` (for structure) with `UseObservableTree` on each item that implements INPC. This is handled internally by the PropertyGrid's array rendering logic, not by a new general hook — observing every item in a collection is a PropertyGrid-specific concern.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│  PropertyGrid Component                                      │
│                                                              │
│  ┌─────────────┐    ┌──────────────┐    ┌────────────────┐  │
│  │ TypeRegistry │───>│ PropertyNode │───>│ EditorElement  │  │
│  │ (metadata)   │    │ (tree model) │    │ (Reactor Element) │  │
│  └─────────────┘    └──────────────┘    └────────────────┘  │
│         │                   │                    │            │
│         │           Decompose/Compose     Render editors     │
│         │           to build tree         per leaf node      │
│         ▼                   ▼                    ▼            │
│  ┌──────────────────────────────────────────────────────┐    │
│  │  Rendered UI                                          │    │
│  │  ┌─ Category: "Appearance" ─────────────────────┐     │    │
│  │  │  Name     [TextBox·····················]     │     │    │
│  │  │  Color    [#FF5733] ▶                        │     │    │
│  │  │    ├─ R   [255·····]                         │     │    │
│  │  │    ├─ G   [87······]                         │     │    │
│  │  │    └─ B   [51······]                         │     │    │
│  │  └──────────────────────────────────────────────┘     │    │
│  │  ┌─ Category: "Layout" ─────────────────────────┐     │    │
│  │  │  Width    [100·····]                         │     │    │
│  │  │  Height   [200·····]                         │     │    │
│  │  └──────────────────────────────────────────────┘     │    │
│  └──────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
```

**Flow:**

1. Caller provides a target object + `TypeRegistry`
2. PropertyGrid reads the object's properties (via registry metadata or reflection fallback)
3. Each property resolves to a `TypeMetadata` entry — either an atomic editor, or a decomposition into sub-properties
4. The tree is rendered: categories as collapsible Expanders, properties as name/editor rows, decomposed types as indented expandable sub-trees
5. Edits flow back: atomic editors call a setter; for mutable objects this mutates in-place, for immutable objects this reconstructs up to the nearest mutable ancestor

---

## Type Registry

The `TypeRegistry` is the core configuration object. It maps `Type` to `TypeMetadata`, which tells the PropertyGrid how to display and edit values of that type.

### TypeMetadata

```csharp
/// <summary>
/// Describes how to edit values of a given type in the PropertyGrid.
/// A type either has an Editor (leaf/atomic) or a Decomposition (composite),
/// or both (e.g., a Color has a hex editor AND can expand to R/G/B).
/// </summary>
public record TypeMetadata
{
    /// <summary>
    /// Creates an editor Element for a value of this type.
    /// Null if this type is only editable through decomposition.
    /// </summary>
    public Func<object, Action<object>, Element>? Editor { get; init; }

    /// <summary>
    /// Breaks a value into named sub-properties for recursive editing.
    /// Null if this type is atomic (edited only via Editor).
    /// </summary>
    public Func<object, IReadOnlyList<PropertyDescriptor>>? Decompose { get; init; }

    /// <summary>
    /// Reconstructs a value from its decomposed parts. Required for
    /// immutable types that have a Decompose. For mutable types where
    /// Decompose returns descriptors with working setters, this is null.
    /// </summary>
    public Func<object, IReadOnlyDictionary<string, object>, object>? Compose { get; init; }

    /// <summary>
    /// Display name for the type (used in array item headers, etc.).
    /// Falls back to Type.Name if null.
    /// </summary>
    public string? DisplayName { get; init; }
}
```

### PropertyDescriptor

```csharp
/// <summary>
/// Describes a single property within a decomposed type.
/// </summary>
public record PropertyDescriptor
{
    /// <summary>Property name (used as key in Compose dictionary).</summary>
    public required string Name { get; init; }

    /// <summary>Display label shown in the grid.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The CLR type of this property's value.</summary>
    public required Type PropertyType { get; init; }

    /// <summary>Gets the current value from the parent object.</summary>
    public required Func<object> GetValue { get; init; }

    /// <summary>
    /// Sets the value on the parent object. Non-null for mutable properties.
    /// Null for immutable properties (use parent's Compose instead).
    /// </summary>
    public Action<object>? SetValue { get; init; }

    /// <summary>Category for grouping. Null = default/uncategorized.</summary>
    public string? Category { get; init; }

    /// <summary>Help text shown as tooltip.</summary>
    public string? Description { get; init; }

    /// <summary>Declaration order for stable sorting.</summary>
    public int Order { get; init; }

    /// <summary>Whether this property is read-only in the grid.</summary>
    public bool IsReadOnly { get; init; }
}
```

### TypeRegistry

```csharp
public class TypeRegistry
{
    private readonly Dictionary<Type, TypeMetadata> _map = new();

    /// <summary>Register metadata for a type.</summary>
    public TypeRegistry Register<T>(TypeMetadata metadata)
    {
        _map[typeof(T)] = metadata;
        return this; // fluent
    }

    /// <summary>
    /// Resolve metadata for a type. Falls back to built-in rules:
    /// 1. Exact match in registry
    /// 2. Enum → auto-generated ComboBox editor
    /// 3. CLR primitive → built-in editor
    /// 4. Array/IList<T> → array editor
    /// 5. Record/class/struct → reflection-based decomposition
    /// </summary>
    public TypeMetadata Resolve(Type type) { ... }
}
```

### Built-in Resolution Rules

The `Resolve` method applies the following fallback chain when no explicit registration exists:

| Type | Strategy |
|------|----------|
| `string` | `TextField` editor |
| `bool` | `ToggleSwitch` editor |
| `int`, `long`, `short`, `byte` | `NumberBox` editor (integer mode, appropriate min/max) |
| `float`, `double`, `decimal` | `NumberBox` editor (decimal mode) |
| `enum` | `ComboBox` editor, items from `Enum.GetValues()` |
| `T[]`, `IList<T>`, `List<T>` | Array decomposition (see Array Support) |
| Any class/struct | Reflection-based decomposition of public instance properties |

Reflection-based decomposition is handled by the `ReflectionTypeMetadataProvider` described below.

---

## Reflection-Based Type Metadata Provider

The `ReflectionTypeMetadataProvider` is a utility that generates `TypeMetadata` (with full `Decompose`/`Compose` and `PropertyDescriptor` lists) from a CLR type's public properties and attributes. It is the default fallback used by `TypeRegistry.Resolve` for class/struct types, but can also be called explicitly to get a metadata object that the caller then customizes before registering.

### CLR Attributes

The provider recognizes both standard `System.ComponentModel` attributes and Reactor-specific attributes. The Reactor attributes take precedence when both are present.

#### Standard System.ComponentModel (supported for compatibility)

| Attribute | Maps to |
|-----------|---------|
| `[Category("...")]` | `PropertyDescriptor.Category` |
| `[Description("...")]` | `PropertyDescriptor.Description` |
| `[DisplayName("...")]` | `PropertyDescriptor.DisplayName` |
| `[ReadOnly(true)]` | `PropertyDescriptor.IsReadOnly` |
| `[Browsable(false)]` | Property excluded from decomposition |

#### Reactor-Specific Attributes

```csharp
/// <summary>
/// Assigns a property to a named category group in the PropertyGrid.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class PropertyCategoryAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// Provides tooltip/help text for a property in the PropertyGrid.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class PropertyDescriptionAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}

/// <summary>
/// Overrides the display name for a property in the PropertyGrid.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class PropertyDisplayNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// Marks a property as hidden from the PropertyGrid.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class PropertyHiddenAttribute : Attribute { }

/// <summary>
/// Marks a property as read-only in the PropertyGrid,
/// even if it has a public setter.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class PropertyReadOnlyAttribute : Attribute { }

/// <summary>
/// Explicitly controls declaration order when the default
/// MetadataToken ordering is insufficient (e.g., inherited properties).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class PropertyOrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}

/// <summary>
/// Applied to a type to specify a custom editor for all properties
/// of that type. The referenced type must have a static method:
///   static Element CreateEditor(object value, Action&lt;object&gt; onChange)
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class PropertyEditorAttribute(Type editorType) : Attribute
{
    public Type EditorType { get; } = editorType;
}
```

### ReflectionTypeMetadataProvider

```csharp
public static class ReflectionTypeMetadataProvider
{
    /// <summary>
    /// Generates TypeMetadata for a CLR type by reflecting over its
    /// public instance properties and reading attributes.
    /// </summary>
    /// <remarks>
    /// - Properties with [PropertyHidden] or [Browsable(false)] are excluded
    /// - Decompose returns PropertyDescriptors with getters/setters wired
    ///   to the reflected PropertyInfo
    /// - For types with init-only or no setters, SetValue is null and
    ///   Compose is generated using a constructor or 'with' expression
    /// - [PropertyEditor] on the type itself sets the Editor delegate
    /// - Results are cached per type
    /// </remarks>
    public static TypeMetadata CreateMetadata(Type type) { ... }

    /// <summary>
    /// Generates a PropertyDescriptor for a single PropertyInfo,
    /// reading all recognized attributes.
    /// </summary>
    public static PropertyDescriptor CreateDescriptor(
        PropertyInfo property, int defaultOrder) { ... }
}
```

### Mutability Detection & Compose Generation

The provider inspects each property to determine mutability:

| Property shape | Mutable? | SetValue | Compose strategy |
|----------------|----------|----------|------------------|
| Public get + public set | Yes | Direct setter | Not needed (setters mutate in place) |
| Public get + init-only set | No | null | Constructor or `with` expression |
| Public get + no set | No | null | Constructor or `with` expression |
| Mixed (some mutable, some not) | Partial | Per-property | Compose for immutable properties only |

For immutable types, the provider generates a `Compose` function that:

1. Looks for a constructor whose parameters match property names (case-insensitive)
2. Calls that constructor with the current values, substituting updated fields from the parts dictionary
3. Falls back to creating via `Activator.CreateInstance` + init-only setter reflection if no matching constructor exists

### Annotated Type Example

```csharp
public class SpriteSettings : INotifyPropertyChanged
{
    [PropertyCategory("Appearance")]
    [PropertyDescription("Display name of the sprite")]
    public string Name { get; set; } = "";

    [PropertyCategory("Appearance")]
    [PropertyDescription("Whether the sprite is visible in the scene")]
    public bool Visible { get; set; } = true;

    [PropertyCategory("Appearance")]
    [PropertyDescription("Tint color applied to the sprite")]
    public Color Tint { get; set; } = Colors.White;

    [PropertyCategory("Transform")]
    [PropertyDisplayName("X Position")]
    [PropertyOrder(0)]
    public double X { get; set; }

    [PropertyCategory("Transform")]
    [PropertyDisplayName("Y Position")]
    [PropertyOrder(1)]
    public double Y { get; set; }

    [PropertyCategory("Transform")]
    [PropertyOrder(2)]
    public double Rotation { get; set; }

    [PropertyHidden]
    public int InternalId { get; set; }

    [PropertyReadOnly]
    [PropertyCategory("Info")]
    [PropertyDescription("Unique identifier (auto-generated)")]
    public Guid Id { get; } = Guid.NewGuid();

    // ... INotifyPropertyChanged implementation
}

// Usage — no explicit TypeMetadata registration needed:
PropertyGrid(spriteSettings, registry)
```

This produces:

```
▼ Appearance
    Name          [TextBox·············]
    Visible       [Toggle·]
    Tint          [#FFFFFF]  ▶
▼ Transform
    X Position    [0.0····]
    Y Position    [0.0····]
    Rotation      [0.0····]
▼ Info
    Id            b7e3f1a2-...  (read-only)
```

### Explicit Override of Reflected Metadata

Since `CreateMetadata` returns a regular `TypeMetadata` record, callers can take the reflected result and tweak it before registering:

```csharp
// Start from reflection, then override the editor for this specific type
var meta = ReflectionTypeMetadataProvider.CreateMetadata(typeof(MyConfig));
registry.Register<MyConfig>(meta with
{
    Editor = (val, onChange) => CustomConfigEditor((MyConfig)val, onChange)
});
```

---

## Immutable Object Edit Propagation

When an edit occurs on a property within an immutable object, the PropertyGrid must reconstruct the object and propagate the new value upward to the nearest mutable ancestor.

```
MutableRoot.Settings (immutable record)
  └─ .Theme (immutable record)
       └─ .AccentColor (Color, immutable struct)
            └─ .R = 255 → user edits to 200
```

**Propagation steps:**

1. User edits `R` to `200`
2. `Color` is immutable → `Compose` creates new `Color(200, G, B)`
3. `Theme` is immutable → `Compose` creates new `Theme` with updated `AccentColor`
4. `Settings` is immutable → `Compose` creates new `Settings` with updated `Theme`
5. `MutableRoot` is mutable → `SetValue` assigns the new `Settings` to `MutableRoot.Settings`
6. `MutableRoot` raises `PropertyChanged("Settings")`

The PropertyGrid maintains the path from each leaf editor back to the root. When an edit occurs on an immutable node, it walks up the path calling `Compose` at each immutable level until it reaches a mutable ancestor with a working `SetValue`.

If the root object itself is immutable, the PropertyGrid fires an `OnRootChanged` callback with the newly constructed root.

---

## Array Support

Arrays (and `IList<T>`) are decomposed into indexed child entries with add/remove/reorder operations.

### Array Metadata

```csharp
/// <summary>
/// Extended metadata for array/list types. Inherits from TypeMetadata.
/// </summary>
public record ArrayTypeMetadata : TypeMetadata
{
    /// <summary>
    /// Factory to create a new element for "Add" operations.
    /// Async to allow dialogs/pickers.
    /// </summary>
    public required Func<Task<T?>>? CreateElement { get; init; }
}
```

For built-in resolution of `T[]`/`List<T>`, the default `CreateElement` uses `Activator.CreateInstance<T>()` for types with a parameterless constructor, and `null` (add disabled) otherwise. The caller can override with a richer factory.

### Array UI

```
Items (3)                              [+]
  ┌─ [0] "Widget A"              [▲][▼][✕]
  │    Name     [Widget A··········]
  │    Size     [42·····]
  ├─ [1] "Widget B"              [▲][▼][✕]
  │    ...
  └─ [2] "Widget C"              [▲][▼][✕]
       ...
```

- Each array item is an expandable node showing its index and a summary (via `ToString()` or `DisplayName`)
- `[+]` adds a new element via `CreateElement` (hidden if `CreateElement` is null)
- `[▲][▼]` reorder within the list
- `[✕]` removes the element
- Mutations happen directly on the `IList<T>`; for arrays (`T[]`), the PropertyGrid replaces the array on the parent via the property setter

---

## Rendering Templates

Every visual element the PropertyGrid produces is created through a template function. The defaults produce the Windows 11 compact-typographic layout described in the Visual Design section, but any template can be overridden to change the look without touching the data/editing logic.

### Template Definitions

```csharp
/// <summary>
/// Renders a category section. Receives the category name, whether it's
/// expanded, an expand toggle callback, and the already-rendered child
/// property rows. Returns the complete category Element.
/// </summary>
public delegate Element CategoryTemplate(
    string name,
    bool isExpanded,
    Action<bool> onExpandedChanged,
    Element[] children
);

/// <summary>
/// Renders a single property row. Receives the descriptor (for name,
/// tooltip, etc.), the already-rendered label Element, and the
/// already-rendered editor Element. Returns the composed row.
/// </summary>
public delegate Element PropertyRowTemplate(
    PropertyDescriptor descriptor,
    Element label,
    Element editor,
    int indentLevel
);

/// <summary>
/// Renders the label/name portion of a property row.
/// Receives the descriptor. Returns the label Element.
/// </summary>
public delegate Element PropertyLabelTemplate(
    PropertyDescriptor descriptor,
    int indentLevel
);

/// <summary>
/// Renders an array item header row. Receives the index, a display
/// summary string, whether the item is expanded, an expand toggle,
/// and action callbacks for move up/down/remove. Any action may be
/// null if the operation is unavailable (e.g., move-up on index 0).
/// Returns the header Element.
/// </summary>
public delegate Element ArrayItemTemplate(
    int index,
    string summary,
    bool isExpanded,
    Action<bool> onExpandedChanged,
    Action? onMoveUp,
    Action? onMoveDown,
    Action? onRemove
);

/// <summary>
/// Renders the array toolbar (the header with count + add button).
/// Receives the property name, element count, and an add callback
/// (null if add is unavailable). Returns the toolbar Element.
/// </summary>
public delegate Element ArrayToolbarTemplate(
    string propertyName,
    int count,
    Func<Task>? onAdd
);
```

### Default Implementations

The PropertyGrid ships with static default implementations that produce the compact Windows 11 layout:

```csharp
public static class PropertyGridDefaults
{
    public static Element CategoryTemplate(
        string name, bool isExpanded,
        Action<bool> onExpandedChanged, Element[] children)
    =>
        Expander(name,
            VStack(2, children),
            isExpanded: isExpanded,
            onExpandedChanged: onExpandedChanged);

    public static Element PropertyLabelTemplate(
        PropertyDescriptor descriptor, int indentLevel)
    =>
        Text(descriptor.DisplayName ?? descriptor.Name)
            .Foreground(Theme.SecondaryText)
            .Tooltip(descriptor.Description);

    public static Element PropertyRowTemplate(
        PropertyDescriptor descriptor, Element label,
        Element editor, int indentLevel)
    =>
        FlexRow(
            label.Flex(grow: 0, shrink: 0, basis: 160),
            editor.Flex(grow: 1)
        )
        .Height(32)
        .Padding(left: indentLevel * 16);

    public static Element ArrayItemTemplate(
        int index, string summary, bool isExpanded,
        Action<bool> onExpandedChanged,
        Action? onMoveUp, Action? onMoveDown, Action? onRemove)
    =>
        // ... compact item header with icon buttons
        ;

    public static Element ArrayToolbarTemplate(
        string propertyName, int count, Func<Task>? onAdd)
    =>
        // ... label with count badge and add button
        ;
}
```

### Customization Example

```csharp
// A "spacious" theme that adds descriptions inline instead of tooltips
PropertyGrid(target, registry)
{
    PropertyLabelTemplate = (descriptor, indent) =>
        VStack(2,
            Text(descriptor.DisplayName ?? descriptor.Name)
                .Bold(),
            descriptor.Description is { } desc
                ? Text(desc)
                    .Foreground(Theme.TertiaryText)
                    .FontSize(11)
                : null
        ),

    PropertyRowTemplate = (descriptor, label, editor, indent) =>
        VStack(4,
            label,
            editor
        )
        .Padding(left: indent * 20, top: 4, bottom: 4)
}
```

---

## PropertyGrid Component API

### Element Definition

```csharp
public record PropertyGridElement(
    object Target,
    TypeRegistry Registry,
    Action<object>? OnRootChanged = null
) : Element
{
    // ── Templates (null = use PropertyGridDefaults) ──────────

    public CategoryTemplate? CategoryTemplate { get; init; }
    public PropertyRowTemplate? PropertyRowTemplate { get; init; }
    public PropertyLabelTemplate? PropertyLabelTemplate { get; init; }
    public ArrayItemTemplate? ArrayItemTemplate { get; init; }
    public ArrayToolbarTemplate? ArrayToolbarTemplate { get; init; }

    // ── Behavior ─────────────────────────────────────────────

    /// <summary>
    /// Filter which properties to show. Null = show all.
    /// Receives the PropertyDescriptor, returns true to include.
    /// </summary>
    public Func<PropertyDescriptor, bool>? Filter { get; init; }

    /// <summary>
    /// Whether to show the search/filter box at the top.
    /// </summary>
    public bool ShowSearch { get; init; } = false;

    internal Action<WinUI.Control>[] Setters { get; init; } = [];
}
```

### DSL Factory

```csharp
public static PropertyGridElement PropertyGrid(
    object target,
    TypeRegistry registry,
    Action<object>? onRootChanged = null
) => new(target, registry, onRootChanged);
```

### Usage Example

```csharp
// ── Setup ──────────────────────────────────────────────────

var registry = new TypeRegistry()
    .Register<Color>(new TypeMetadata
    {
        Editor = (val, onChange) =>
        {
            var c = (Color)val;
            return TextField(c.ToHex(), hex => onChange(Color.FromHex(hex)));
        },
        Decompose = val =>
        {
            var c = (Color)val;
            return
            [
                new PropertyDescriptor
                {
                    Name = "R", PropertyType = typeof(byte),
                    GetValue = () => c.R, Order = 0
                },
                new PropertyDescriptor
                {
                    Name = "G", PropertyType = typeof(byte),
                    GetValue = () => c.G, Order = 1
                },
                new PropertyDescriptor
                {
                    Name = "B", PropertyType = typeof(byte),
                    GetValue = () => c.B, Order = 2
                },
            ];
        },
        Compose = (_, parts) =>
            Color.FromRgb((byte)parts["R"], (byte)parts["G"], (byte)parts["B"])
    });

// ── In a Component ─────────────────────────────────────────

class SettingsEditor : Component
{
    private readonly MySettings _settings = new(); // mutable, INPC

    public override Element Render()
    {
        UseObservable(_settings);

        return PropertyGrid(_settings, registry);
    }
}
```

### Immutable Root Example

```csharp
class ConfigEditor : Component
{
    public override Element Render()
    {
        var (config, setConfig) = UseState(new AppConfig("Default", 8080));

        return PropertyGrid(config, registry, onRootChanged: obj => setConfig((AppConfig)obj));
    }
}
```

---

## UI Layout & Visual Design

### Design Principles

- **Windows 11 / WinUI 3 design language** — Segoe UI Variable, rounded corners, subtle dividers
- **File Explorer density** — compact but typographic; not as spacious as the Settings app
- **Row height** ~32px — enough for comfortable touch targets without waste
- **Property name column** — fixed-width left column (~40% of grid width), right-aligned text, secondary text color
- **Editor column** — fills remaining space
- **Indentation** — 16px per decomposition level
- **Category headers** — Expander style, bold text, top border as separator

### Layout Structure

```
┌─────────────────────────────────────────────────────┐
│ ▼ Appearance                                         │  Category Expander
│─────────────────────────────────────────────────────│
│   Name            [TextBox·····················]     │  Property row
│   Visible         [Toggle·]                          │  Property row
│   Color           [#FF5733]  ▶                       │  Expandable property
│     R             [255·····]                         │  Decomposed child
│     G             [87······]                         │  Decomposed child
│     B             [51······]                         │  Decomposed child
│─────────────────────────────────────────────────────│
│ ▶ Layout                                             │  Collapsed category
│─────────────────────────────────────────────────────│
│ ▼ Items                                              │  Array category
│   Items (3)       [+]                                │  Array header
│   ▶ [0] Widget A                          [▲][▼][✕] │  Array item
│   ▶ [1] Widget B                          [▲][▼][✕] │  Array item
│   ▼ [2] Widget C                          [▲][▼][✕] │  Expanded item
│       Name        [Widget C···········]              │  Item property
│       Size        [12·····]                          │  Item property
└─────────────────────────────────────────────────────┘
```

### Rendering Pipeline

The PropertyGrid component internally renders by calling templates in order. Each step delegates to the configured template (or the default):

```csharp
// 1. For each property, render the label via PropertyLabelTemplate
var label = (PropertyLabelTemplate ?? PropertyGridDefaults.PropertyLabelTemplate)
    (descriptor, indentLevel);

// 2. Resolve the editor Element from the TypeRegistry
var editor = ResolveEditor(descriptor);

// 3. Compose label + editor into a row via PropertyRowTemplate
var row = (PropertyRowTemplate ?? PropertyGridDefaults.PropertyRowTemplate)
    (descriptor, label, editor, indentLevel);

// 4. Group rows by category, render each group via CategoryTemplate
var category = (CategoryTemplate ?? PropertyGridDefaults.CategoryTemplate)
    (cat.Name, isExpanded, onExpandedChanged, rows);

// 5. For arrays, render toolbar via ArrayToolbarTemplate
//    and each item header via ArrayItemTemplate
```

Properties are grouped by `Category` and rendered in declaration order within each group. Properties with no category appear in a "General" group at the top.

This pipeline means a caller can override just the label (e.g., add inline descriptions) without reimplementing row layout, or override the row (e.g., vertical stacking) without reimplementing category collapsing.

---

## Implementation Phases

### Phase 1: INPC Foundation (General Reactor Infrastructure)
- Implement `UseObservableTree<T>` hook in `RenderContext`
  - Recursive property walk with reflection (cache property metadata per `Type`)
  - `HashSet<INotifyPropertyChanged>` for cycle detection
  - `Dictionary<INotifyPropertyChanged, EventHandler>` for subscription tracking
  - Re-walk on nested `PropertyChanged` to pick up replaced references
  - Cleanup disposes all subscriptions
- Add convenience wrapper in `Component` (matching existing `UseObservable` pattern)
- Tests: nested change triggers re-render, circular references don't infinite-loop, replaced nested objects resubscribe, disposal cleans all subscriptions

### Phase 2: TypeRegistry + Metadata
- `TypeMetadata`, `PropertyDescriptor`, `TypeRegistry` classes
- Built-in resolution for primitives, enums
- `ReflectionTypeMetadataProvider` with attribute support
  - Reactor attributes: `[PropertyCategory]`, `[PropertyDescription]`, `[PropertyDisplayName]`, `[PropertyHidden]`, `[PropertyReadOnly]`, `[PropertyOrder]`, `[PropertyEditor]`
  - `System.ComponentModel` fallback: `[Category]`, `[Description]`, `[DisplayName]`, `[Browsable]`, `[ReadOnly]`
- Reflection-based decomposition fallback for records/classes/structs
- Mutability detection and `Compose` generation for immutable types

### Phase 3: Core PropertyGrid Component
- `PropertyGridElement` + DSL factory
- Property tree construction from target + registry
- Category grouping and collapsible rendering
- Atomic property editing (primitives, enums, string, bool)
- Mutable object mutation via property setters

### Phase 4: Decomposition & Immutable Support
- Expandable composite properties (decompose/compose)
- Immutable edit propagation (walk-up-to-mutable-ancestor)
- `OnRootChanged` callback for fully immutable roots
- Custom type editors (Color example)

### Phase 5: Array Support
- Array/list decomposition and rendering
- Add (async factory), remove, reorder operations
- Array item expand/collapse
- `ArrayTypeMetadata` with `CreateElement`

### Phase 6: Polish
- Search/filter box (optional, `ShowSearch`)
- Keyboard navigation
- Accessibility (AutomationId, labels)
- Performance: virtualize long property lists if needed
