# Menu and command bar items

Records that describe menu, menu bar, command bar and keyboard accelerator items.

**Learn more:** [Controls](../../controls.md), [Commanding](../../commanding.md)

## `AppBarButtonData`

```csharp
public record AppBarButtonData(string Label, Action? OnClick = null, string? Icon = null) : AppBarItemBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`AppBarItemBase`](#appbaritembase).

### Constructors

#### `AppBarButtonData`

```csharp
public AppBarButtonData(string Label, Action? OnClick = null, string? Icon = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Label` | `string` |  |  |
| `OnClick` | `Action?` | `null` |  |
| `Icon` | `string?` | `null` |  |

### Properties

#### `AccessKey`

```csharp
public string? AccessKey  // get, init
```

#### `Description`

```csharp
public string? Description  // get, init
```

#### `Icon`

```csharp
public string? Icon  // get, init
```

#### `IconElement`

```csharp
public IconData? IconElement  // get, init
```

#### `IsEnabled`

```csharp
public bool IsEnabled  // get, init
```

#### `KeyboardAccelerators`

```csharp
public KeyboardAcceleratorData[]? KeyboardAccelerators  // get, init
```

#### `Label`

```csharp
public string Label  // get, init
```

#### `OnClick`

```csharp
public Action? OnClick  // get, init
```

## `AppBarItemBase`

```csharp
public abstract record AppBarItemBase
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `AppBarItemBase`

```csharp
protected AppBarItemBase()
```

### Properties

#### `CallSite`

Where this command was created in C#, stamped by the source map on the `AppBarButton(…)` / `AppBarToggleButton(…)` / `AppBarSeparator()` factory call (the same mechanism as [`Element.CallSite`](../elements/Element.md#callsite)). The realized command publishes it for inspectors. Not part of equality, like [`Element.CallSite`](../elements/Element.md#callsite).

```csharp
public SourceLocation? CallSite  // get, init
```

## `AppBarSeparatorData`

```csharp
public record AppBarSeparatorData : AppBarItemBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`AppBarItemBase`](#appbaritembase).

### Constructors

#### `AppBarSeparatorData`

```csharp
public AppBarSeparatorData()
```

## `AppBarToggleButtonData`

```csharp
public record AppBarToggleButtonData(string Label, bool IsChecked = false, Action<bool>? OnIsCheckedChanged = null, string? Icon = null) : AppBarItemBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`AppBarItemBase`](#appbaritembase).

### Constructors

#### `AppBarToggleButtonData`

```csharp
public AppBarToggleButtonData(string Label, bool IsChecked = false, Action<bool>? OnIsCheckedChanged = null, string? Icon = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Label` | `string` |  |  |
| `IsChecked` | `bool` | `false` |  |
| `OnIsCheckedChanged` | `Action<bool>?` | `null` |  |
| `Icon` | `string?` | `null` |  |

### Properties

#### `Icon`

```csharp
public string? Icon  // get, init
```

#### `IconElement`

```csharp
public IconData? IconElement  // get, init
```

#### `IsChecked`

```csharp
public bool IsChecked  // get, init
```

#### `Label`

```csharp
public string Label  // get, init
```

#### `OnIsCheckedChanged`

```csharp
public Action<bool>? OnIsCheckedChanged  // get, init
```

## `KeyboardAcceleratorData`

```csharp
public record KeyboardAcceleratorData(VirtualKey Key, VirtualKeyModifiers Modifiers = default)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `KeyboardAcceleratorData`

```csharp
public KeyboardAcceleratorData(VirtualKey Key, VirtualKeyModifiers Modifiers = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Key` | `VirtualKey` |  |  |
| `Modifiers` | `VirtualKeyModifiers` | `default` |  |

### Properties

#### `Key`

```csharp
public VirtualKey Key  // get, init
```

#### `Modifiers`

```csharp
public VirtualKeyModifiers Modifiers  // get, init
```

## `MenuBarItemData`

```csharp
public record MenuBarItemData(string Title, MenuFlyoutItemBase[] Items)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `MenuBarItemData`

```csharp
public MenuBarItemData(string Title, MenuFlyoutItemBase[] Items)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Title` | `string` |  |
| `Items` | [`MenuFlyoutItemBase[]`](#menuflyoutitembase) |  |

### Properties

#### `Items`

```csharp
public MenuFlyoutItemBase[] Items  // get, init
```

#### `Title`

```csharp
public string Title  // get, init
```

## `MenuFlyoutItemBase`

```csharp
public abstract record MenuFlyoutItemBase
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `MenuFlyoutItemBase`

```csharp
protected MenuFlyoutItemBase()
```

### Properties

#### `CallSite`

Where this item was created in C#, stamped by the source map on the `MenuItem(…)` / `ToggleMenuItem(…)` / … factory call (the same mechanism as [`Element.CallSite`](../elements/Element.md#callsite)). The realized menu item publishes it for inspectors. Not part of equality, like [`Element.CallSite`](../elements/Element.md#callsite).

```csharp
public SourceLocation? CallSite  // get, init
```

## `MenuFlyoutItemData`

```csharp
public record MenuFlyoutItemData(string Text, Action? OnClick = null, string? Icon = null) : MenuFlyoutItemBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`MenuFlyoutItemBase`](#menuflyoutitembase).

### Constructors

#### `MenuFlyoutItemData`

```csharp
public MenuFlyoutItemData(string Text, Action? OnClick = null, string? Icon = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Text` | `string` |  |  |
| `OnClick` | `Action?` | `null` |  |
| `Icon` | `string?` | `null` |  |

### Properties

#### `AccessKey`

```csharp
public string? AccessKey  // get, init
```

#### `Description`

```csharp
public string? Description  // get, init
```

#### `Icon`

```csharp
public string? Icon  // get, init
```

#### `IconElement`

```csharp
public IconData? IconElement  // get, init
```

#### `IsEnabled`

```csharp
public bool IsEnabled  // get, init
```

#### `KeyboardAccelerators`

```csharp
public KeyboardAcceleratorData[]? KeyboardAccelerators  // get, init
```

#### `OnClick`

```csharp
public Action? OnClick  // get, init
```

#### `Text`

```csharp
public string Text  // get, init
```

## `MenuFlyoutSeparatorData`

```csharp
public record MenuFlyoutSeparatorData : MenuFlyoutItemBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`MenuFlyoutItemBase`](#menuflyoutitembase).

### Constructors

#### `MenuFlyoutSeparatorData`

```csharp
public MenuFlyoutSeparatorData()
```

## `MenuFlyoutSubItemData`

```csharp
public record MenuFlyoutSubItemData(string Text, MenuFlyoutItemBase[] Items, string? Icon = null) : MenuFlyoutItemBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`MenuFlyoutItemBase`](#menuflyoutitembase).

### Constructors

#### `MenuFlyoutSubItemData`

```csharp
public MenuFlyoutSubItemData(string Text, MenuFlyoutItemBase[] Items, string? Icon = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Text` | `string` |  |  |
| `Items` | [`MenuFlyoutItemBase[]`](#menuflyoutitembase) |  |  |
| `Icon` | `string?` | `null` |  |

### Properties

#### `Icon`

```csharp
public string? Icon  // get, init
```

#### `IconElement`

```csharp
public IconData? IconElement  // get, init
```

#### `Items`

```csharp
public MenuFlyoutItemBase[] Items  // get, init
```

#### `Text`

```csharp
public string Text  // get, init
```

## `RadioMenuFlyoutItemData`

```csharp
public record RadioMenuFlyoutItemData(string Text, string GroupName, bool IsChecked = false, Action? OnClick = null, string? Icon = null) : MenuFlyoutItemBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`MenuFlyoutItemBase`](#menuflyoutitembase).

### Constructors

#### `RadioMenuFlyoutItemData`

```csharp
public RadioMenuFlyoutItemData(string Text, string GroupName, bool IsChecked = false, Action? OnClick = null, string? Icon = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Text` | `string` |  |  |
| `GroupName` | `string` |  |  |
| `IsChecked` | `bool` | `false` |  |
| `OnClick` | `Action?` | `null` |  |
| `Icon` | `string?` | `null` |  |

### Properties

#### `GroupName`

```csharp
public string GroupName  // get, init
```

#### `Icon`

```csharp
public string? Icon  // get, init
```

#### `IconElement`

```csharp
public IconData? IconElement  // get, init
```

#### `IsChecked`

```csharp
public bool IsChecked  // get, init
```

#### `OnClick`

```csharp
public Action? OnClick  // get, init
```

#### `Text`

```csharp
public string Text  // get, init
```

## `ToggleMenuFlyoutItemData`

```csharp
public record ToggleMenuFlyoutItemData(string Text, bool IsChecked = false, Action<bool>? OnIsCheckedChanged = null, string? Icon = null) : MenuFlyoutItemBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`MenuFlyoutItemBase`](#menuflyoutitembase).

### Constructors

#### `ToggleMenuFlyoutItemData`

```csharp
public ToggleMenuFlyoutItemData(string Text, bool IsChecked = false, Action<bool>? OnIsCheckedChanged = null, string? Icon = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Text` | `string` |  |  |
| `IsChecked` | `bool` | `false` |  |
| `OnIsCheckedChanged` | `Action<bool>?` | `null` |  |
| `Icon` | `string?` | `null` |  |

### Properties

#### `Icon`

```csharp
public string? Icon  // get, init
```

#### `IconElement`

```csharp
public IconData? IconElement  // get, init
```

#### `IsChecked`

```csharp
public bool IsChecked  // get, init
```

#### `OnIsCheckedChanged`

```csharp
public Action<bool>? OnIsCheckedChanged  // get, init
```

#### `Text`

```csharp
public string Text  // get, init
```
