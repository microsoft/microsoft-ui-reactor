---
title: "Property grid attributes"
description: "Attributes that change how the property grid shows a property."
---

**Learn more:** [Controls](/microsoft-ui-reactor/main/controls)

## `PropertyCategoryAttribute` [#propertycategoryattribute]

Assigns a property to a named category group in the PropertyGrid.

```csharp
public class PropertyCategoryAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors [#constructors]

#### `PropertyCategoryAttribute` [#propertycategoryattribute_1]

Assigns a property to a named category group in the PropertyGrid.

```csharp
public PropertyCategoryAttribute(string name)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `name` | `string` |  |

### Properties [#properties]

#### `Name` [#name]

```csharp
public string Name  // get
```

## `PropertyDescriptionAttribute` [#propertydescriptionattribute]

Provides tooltip/help text for a property in the PropertyGrid.

```csharp
public class PropertyDescriptionAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors [#constructors_1]

#### `PropertyDescriptionAttribute` [#propertydescriptionattribute_1]

Provides tooltip/help text for a property in the PropertyGrid.

```csharp
public PropertyDescriptionAttribute(string text)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `text` | `string` |  |

### Properties [#properties_1]

#### `Text` [#text]

```csharp
public string Text  // get
```

## `PropertyDisplayNameAttribute` [#propertydisplaynameattribute]

Overrides the display name for a property in the PropertyGrid.

```csharp
public class PropertyDisplayNameAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors [#constructors_2]

#### `PropertyDisplayNameAttribute` [#propertydisplaynameattribute_1]

Overrides the display name for a property in the PropertyGrid.

```csharp
public PropertyDisplayNameAttribute(string name)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `name` | `string` |  |

### Properties [#properties_2]

#### `Name` [#name_1]

```csharp
public string Name  // get
```

## `PropertyEditorAttribute` [#propertyeditorattribute]

Applied to a type to specify a custom editor for all properties of that type. The referenced type must have a static method: static Element CreateEditor(object value, Action\<object\> onChange)

```csharp
public class PropertyEditorAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors [#constructors_3]

#### `PropertyEditorAttribute` [#propertyeditorattribute_1]

Applied to a type to specify a custom editor for all properties of that type. The referenced type must have a static method: static Element CreateEditor(object value, Action\<object\> onChange)

```csharp
public PropertyEditorAttribute(Type editorType)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `editorType` | `Type` |  |

### Properties [#properties_3]

#### `EditorType` [#editortype]

```csharp
public Type EditorType  // get
```

## `PropertyHiddenAttribute` [#propertyhiddenattribute]

Marks a property as hidden from the PropertyGrid.

```csharp
public class PropertyHiddenAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors [#constructors_4]

#### `PropertyHiddenAttribute` [#propertyhiddenattribute_1]

```csharp
public PropertyHiddenAttribute()
```

## `PropertyOrderAttribute` [#propertyorderattribute]

Explicitly controls declaration order when the default MetadataToken ordering is insufficient (e.g., inherited properties).

```csharp
public class PropertyOrderAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors [#constructors_5]

#### `PropertyOrderAttribute` [#propertyorderattribute_1]

Explicitly controls declaration order when the default MetadataToken ordering is insufficient (e.g., inherited properties).

```csharp
public PropertyOrderAttribute(int order)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `order` | `int` |  |

### Properties [#properties_4]

#### `Order` [#order]

```csharp
public int Order  // get
```

## `PropertyReadOnlyAttribute` [#propertyreadonlyattribute]

Marks a property as read-only in the PropertyGrid, even if it has a public setter.

```csharp
public class PropertyReadOnlyAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Constructors [#constructors_6]

#### `PropertyReadOnlyAttribute` [#propertyreadonlyattribute_1]

```csharp
public PropertyReadOnlyAttribute()
```
