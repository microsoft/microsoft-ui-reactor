# Property grid templates

Delegates and metadata the property grid uses to build its rows.

**Learn more:** [Controls](../../controls.md)

## `ArrayItemTemplate`

```csharp
public delegate Element ArrayItemTemplate(int index, string summary, bool isExpanded, Action<bool> onExpandedChanged, Action? onMoveUp, Action? onMoveDown, Action? onRemove)
```

Namespace: `Microsoft.UI.Reactor.Controls`.

## `ArrayToolbarTemplate`

```csharp
public delegate Element ArrayToolbarTemplate(string propertyName, int count, Func<Task>? onAdd)
```

Namespace: `Microsoft.UI.Reactor.Controls`.

## `ArrayTypeMetadata`

Extended metadata for array/list types.

```csharp
public record ArrayTypeMetadata : TypeMetadata
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`TypeMetadata`](#typemetadata).

### Constructors

#### `ArrayTypeMetadata`

```csharp
public ArrayTypeMetadata()
```

### Properties

#### `CreateElement`

Factory to create a new element for "Add" operations. Null means add is disabled.

```csharp
public Func<Task<object?>>? CreateElement  // get, init
```

## `CategoryTemplate`

```csharp
public delegate Element CategoryTemplate(string name, bool isExpanded, Action<bool> onExpandedChanged, Element[] children)
```

Namespace: `Microsoft.UI.Reactor.Controls`.

## `CellRenderers`

Catalog of read-mode cell renderers for DataGrid (and anywhere else that needs a "display this value" factory). Each method returns a `Func<object, Element>` matching [`FieldDescriptor.CellRenderer`](../data/descriptors.md#cellrenderer).

Paired with [`Editors`](#editors) — the convention is that a specialized column uses the matching pair (e.g. `CellRenderers.Date` + `Editors.DateTime`).

```csharp
public static class CellRenderers
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Methods

#### `CheckMark`

```csharp
public static Func<object, Element> CheckMark()
```

#### `ColorSwatch`

```csharp
public static Func<object, Element> ColorSwatch()
```

#### `Date`

```csharp
public static Func<object, Element> Date(string format = "d")
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `format` | `string` | `"d"` |  |

#### `Enum`

```csharp
public static Func<object, Element> Enum()
```

#### `Hyperlink`

```csharp
public static Func<object, Element> Hyperlink(string? displayTextFormat = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `displayTextFormat` | `string?` | `null` |  |

#### `Number`

```csharp
public static Func<object, Element> Number(string? format = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `format` | `string?` | `null` |  |

#### `Text`

```csharp
public static Func<object, Element> Text(string? format = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `format` | `string?` | `null` |  |

#### `Time`

```csharp
public static Func<object, Element> Time(string format = "t")
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `format` | `string` | `"t"` |  |

#### `ToggleIndicator`

```csharp
public static Func<object, Element> ToggleIndicator()
```

## `EditorTier`

Specifies which editor tier to resolve.

```csharp
public enum EditorTier
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Standard` | `0` | Standard editor for PropertyGrid/FormField. |
| `Compact` | `1` | Compact editor for grid inline cells. |
| `Full` | `2` | Full editor for expanded/flyout editing. |

## `Editors`

Catalog of inline editor factories for DataGrid and PropertyGrid. Each method returns a `Func<object, Action<object>, Element>` matching the [`FieldDescriptor.Editor`](../data/descriptors.md#editor) contract: takes the current cell value and an onChange callback, returns the editor Element.

Use these via:

- `FieldDescriptor`'s `Editor` slot directly, or
- A typed column factory like `NumberColumn<T>` (thin wrappers over this catalog), or
- `TypeRegistry` registration for metadata-driven discovery.

```csharp
public static class Editors
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Methods

#### `CheckBox`

```csharp
public static Func<object, Action<object>, Element> CheckBox()
```

#### `Color`

```csharp
public static Func<object, Action<object>, Element> Color()
```

#### `ColorCompact`

```csharp
public static Func<object, Action<object>, Element> ColorCompact()
```

#### `Combo`

```csharp
public static Func<object, Action<object>, Element> Combo<TValue>(IReadOnlyList<TValue> choices)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `choices` | `IReadOnlyList<TValue>` |  |

#### `Date`

```csharp
public static Func<object, Action<object>, Element> Date()
```

#### `DateOffset`

```csharp
public static Func<object, Action<object>, Element> DateOffset()
```

#### `DateOnly`

```csharp
public static Func<object, Action<object>, Element> DateOnly()
```

#### `Decimal`

```csharp
public static Func<object, Action<object>, Element> Decimal(decimal? min = null, decimal? max = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `min` | `decimal?` | `null` |  |
| `max` | `decimal?` | `null` |  |

#### `Double`

```csharp
public static Func<object, Action<object>, Element> Double(double? min = null, double? max = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `min` | `double?` | `null` |  |
| `max` | `double?` | `null` |  |

#### `EnumCombo`

```csharp
public static Func<object, Action<object>, Element> EnumCombo(Type enumType)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `enumType` | `Type` |  |

#### `Float`

```csharp
public static Func<object, Action<object>, Element> Float(float? min = null, float? max = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `min` | `float?` | `null` |  |
| `max` | `float?` | `null` |  |

#### `Int`

```csharp
public static Func<object, Action<object>, Element> Int(int? min = null, int? max = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `min` | `int?` | `null` |  |
| `max` | `int?` | `null` |  |

#### `Long`

```csharp
public static Func<object, Action<object>, Element> Long(long? min = null, long? max = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `min` | `long?` | `null` |  |
| `max` | `long?` | `null` |  |

#### `Number`

```csharp
public static Func<object, Action<object>, Element> Number(Type numberType, double? min = null, double? max = null, double smallChange = 1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `numberType` | `Type` |  |  |
| `min` | `double?` | `null` |  |
| `max` | `double?` | `null` |  |
| `smallChange` | `double` | `1` |  |

#### `Text`

```csharp
public static Func<object, Action<object>, Element> Text(string? placeholderText = null, int? maxLength = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `placeholderText` | `string?` | `null` |  |
| `maxLength` | `int?` | `null` |  |

#### `TimeOfDay`

```csharp
public static Func<object, Action<object>, Element> TimeOfDay()
```

#### `TimeOnlyEditor`

```csharp
public static Func<object, Action<object>, Element> TimeOnlyEditor()
```

#### `TimeSpanEditor`

```csharp
public static Func<object, Action<object>, Element> TimeSpanEditor()
```

#### `Toggle`

```csharp
public static Func<object, Action<object>, Element> Toggle(string? onContent = null, string? offContent = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `onContent` | `string?` | `null` |  |
| `offContent` | `string?` | `null` |  |

#### `Uri`

```csharp
public static Func<object, Action<object>, Element> Uri()
```

## `PropertyLabelTemplate`

```csharp
public delegate Element PropertyLabelTemplate(FieldDescriptor descriptor, int indentLevel)
```

Namespace: `Microsoft.UI.Reactor.Controls`.

## `PropertyRowTemplate`

```csharp
public delegate Element PropertyRowTemplate(FieldDescriptor descriptor, Element label, Element editor, int indentLevel)
```

Namespace: `Microsoft.UI.Reactor.Controls`.

## `ReflectionTypeMetadataProvider`

Generates TypeMetadata for CLR types by reflecting over public instance properties and reading attributes. Used as the default fallback by TypeRegistry.

```csharp
public static class ReflectionTypeMetadataProvider
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Methods

#### `CreateDescriptor`

Generates a FieldDescriptor for a single PropertyInfo (unbound — for registry use).

```csharp
public static FieldDescriptor CreateDescriptor(PropertyInfo property, int defaultOrder)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `property` | `PropertyInfo` |  |
| `defaultOrder` | `int` |  |

#### `CreateMetadata`

Generates TypeMetadata for a CLR type by reflecting over its public instance properties and reading attributes.

```csharp
public static TypeMetadata CreateMetadata(Type type)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `type` | `Type` |  |

## `TypeMetadata`

Describes how to edit values of a given type in the PropertyGrid. A type either has an Editor (leaf/atomic) or a Decomposition (composite), or both (e.g., a Color has a hex editor AND can expand to R/G/B).

```csharp
public record TypeMetadata
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Constructors

#### `TypeMetadata`

```csharp
public TypeMetadata()
```

### Properties

#### `CompactEditor`

Compact editor variant for grid inline cells. Falls back to Editor when null.

```csharp
public Func<object, Action<object>, Element>? CompactEditor  // get, init
```

#### `Compose`

Reconstructs a value from its decomposed parts. Required for immutable types that have a Decompose. For mutable types where Decompose returns descriptors with working setters, this is null.

```csharp
public Func<object, IReadOnlyDictionary<string, object>, object>? Compose  // get, init
```

#### `Decompose`

Breaks a value into named sub-properties for recursive editing. Null if this type is atomic (edited only via Editor).

```csharp
public Func<object, IReadOnlyList<FieldDescriptor>>? Decompose  // get, init
```

#### `DisplayName`

Display name for the type (used in array item headers, etc.). Falls back to Type.Name if null.

```csharp
public string? DisplayName  // get, init
```

#### `Editor`

Creates an editor Element for a value of this type. Null if this type is only editable through decomposition.

```csharp
public Func<object, Action<object>, Element>? Editor  // get, init
```

#### `FullEditor`

Full editor variant for expanded/flyout editing. Null when no expanded editing is available.

```csharp
public Func<object, Action<object>, Element>? FullEditor  // get, init
```

## `TypeRegistry`

Maps CLR types to TypeMetadata for the PropertyGrid. Provides a fluent Register API and a Resolve method with built-in fallbacks.

```csharp
public class TypeRegistry
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Constructors

#### `TypeRegistry`

```csharp
public TypeRegistry()
```

### Methods

#### `GetCellRenderer`

Try to get a cell renderer for a type. Falls back to built-in defaults for common primitive/date/uri/color types so `AutoColumns` picks up sensible display renderers without requiring explicit registration.

```csharp
public Func<object, Element>? GetCellRenderer(Type type)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `type` | `Type` |  |

#### `GetFormatter`

Try to get a registered formatter for a type.

```csharp
public Func<object?, string>? GetFormatter(Type type)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `type` | `Type` |  |

#### `Register`

Register metadata for a type.

```csharp
public TypeRegistry Register<T>(TypeMetadata metadata)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `metadata` | [`TypeMetadata`](#typemetadata) |  |

#### `RegisterCellRenderer`

Register a custom cell renderer for grid display.

```csharp
public TypeRegistry RegisterCellRenderer<T>(Func<object, Element> renderer)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `renderer` | `Func<object, Element>` |  |

#### `RegisterFormatter`

Register a custom value formatter for grid display.

```csharp
public TypeRegistry RegisterFormatter<T>(Func<object?, string> formatter)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `formatter` | `Func<object?, string>` |  |

#### `Resolve`

Resolve metadata for a type. Falls back to built-in rules: 1. Exact match in registry 2. Enum — auto-generated ComboBox editor 3. CLR primitive — built-in editor 4. Collection/enumerable shapes — array editor 5. Record/class/struct — reflection-based decomposition

```csharp
public TypeMetadata Resolve(Type type)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `type` | `Type` |  |

#### `ResolveEditor`

Resolves the editor for a type using tiered resolution: For compact (grid): CompactEditor ?? Editor ?? built-in For standard (PropertyGrid/FormField): Editor ?? built-in For full (expand): FullEditor (null when not registered)

```csharp
public Func<object, Action<object>, Element>? ResolveEditor(Type type, EditorTier tier)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `type` | `Type` |  |
| `tier` | [`EditorTier`](#editortier) |  |
