# Property grid attributes

Attributes that change how the property grid shows a property.

**Learn more:** [Controls](../../controls.md)

## `PropertyCategoryAttribute`

Assigns a property to a named category group in the PropertyGrid.

```csharp
public class PropertyCategoryAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors

#### `PropertyCategoryAttribute`

Assigns a property to a named category group in the PropertyGrid.

```csharp
public PropertyCategoryAttribute(string name)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `name` | `string` |  |

### Properties

#### `Name`

```csharp
public string Name  // get
```

## `PropertyDescriptionAttribute`

Provides tooltip/help text for a property in the PropertyGrid.

```csharp
public class PropertyDescriptionAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors

#### `PropertyDescriptionAttribute`

Provides tooltip/help text for a property in the PropertyGrid.

```csharp
public PropertyDescriptionAttribute(string text)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `text` | `string` |  |

### Properties

#### `Text`

```csharp
public string Text  // get
```

## `PropertyDisplayNameAttribute`

Overrides the display name for a property in the PropertyGrid.

```csharp
public class PropertyDisplayNameAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors

#### `PropertyDisplayNameAttribute`

Overrides the display name for a property in the PropertyGrid.

```csharp
public PropertyDisplayNameAttribute(string name)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `name` | `string` |  |

### Properties

#### `Name`

```csharp
public string Name  // get
```

## `PropertyEditorAttribute`

Applied to a type to specify a custom editor for all properties of that type. The referenced type must have a static method: static Element CreateEditor(object value, Action\<object\> onChange)

```csharp
public class PropertyEditorAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors

#### `PropertyEditorAttribute`

Applied to a type to specify a custom editor for all properties of that type. The referenced type must have a static method: static Element CreateEditor(object value, Action\<object\> onChange)

```csharp
public PropertyEditorAttribute(Type editorType)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `editorType` | `Type` |  |

### Properties

#### `EditorType`

```csharp
public Type EditorType  // get
```

## `PropertyHiddenAttribute`

Marks a property as hidden from the PropertyGrid.

```csharp
public class PropertyHiddenAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors

#### `PropertyHiddenAttribute`

```csharp
public PropertyHiddenAttribute()
```

## `PropertyOrderAttribute`

Explicitly controls declaration order when the default MetadataToken ordering is insufficient (e.g., inherited properties).

```csharp
public class PropertyOrderAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors

#### `PropertyOrderAttribute`

Explicitly controls declaration order when the default MetadataToken ordering is insufficient (e.g., inherited properties).

```csharp
public PropertyOrderAttribute(int order)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `order` | `int` |  |

### Properties

#### `Order`

```csharp
public int Order  // get
```

## `PropertyReadOnlyAttribute`

Marks a property as read-only in the PropertyGrid, even if it has a public setter.

```csharp
public class PropertyReadOnlyAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors

#### `PropertyReadOnlyAttribute`

```csharp
public PropertyReadOnlyAttribute()
```
