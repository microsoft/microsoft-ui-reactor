# Factories: Menus and commands

Menus, menu bars, command bars and keyboard accelerators.

Each factory method returns an element record. Call them through `using static Microsoft.UI.Reactor.Factories;`.

**Learn more:** [Commanding](../../commanding.md)

## `Accelerator`

```csharp
KeyboardAcceleratorData Accelerator(VirtualKey key, VirtualKeyModifiers modifiers = default)
```

**Returns:** [`KeyboardAcceleratorData`](../controls/menu-items.md#keyboardacceleratordata).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `key` | `VirtualKey` |  |  |
| `modifiers` | `VirtualKeyModifiers` | `default` |  |

## `AppBarButton`

```csharp
AppBarButtonData AppBarButton(Command command)
AppBarButtonData AppBarButton(string label, Action? onClick = null, string? icon = null)
```

| Overload | Description |
| --- | --- |
| `AppBarButton(Command)` | Creates an AppBarButton driven by a Command. Maps Label, Icon, Execute, Accelerator, IsEnabled, AccessKey, and Description. |
| `AppBarButton(string, Action?, string?)` |  |

**Returns:** [`AppBarButtonData`](../controls/menu-items.md#appbarbuttondata).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `command` | [`Command`](../input/Command.md) |  |  |
| `label` | `string` |  |  |
| `onClick` | `Action?` | `null` |  |
| `icon` | `string?` | `null` |  |

## `AppBarSeparator`

```csharp
AppBarSeparatorData AppBarSeparator()
```

**Returns:** [`AppBarSeparatorData`](../controls/menu-items.md#appbarseparatordata).

## `AppBarToggleButton`

```csharp
AppBarToggleButtonData AppBarToggleButton(string label, bool isChecked = false, Action<bool>? onIsCheckedChanged = null, string? icon = null)
```

**Returns:** [`AppBarToggleButtonData`](../controls/menu-items.md#appbartogglebuttondata).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `label` | `string` |  |  |
| `isChecked` | `bool` | `false` |  |
| `onIsCheckedChanged` | `Action<bool>?` | `null` |  |
| `icon` | `string?` | `null` |  |

## `CommandBar`

```csharp
CommandBarElement CommandBar(AppBarItemBase[]? primaryCommands = null, AppBarItemBase[]? secondaryCommands = null)
```

**Returns:** [`CommandBarElement`](../elements/menus-and-commands.md#commandbarelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `primaryCommands` | [`AppBarItemBase[]?`](../controls/menu-items.md#appbaritembase) | `null` |  |
| `secondaryCommands` | [`AppBarItemBase[]?`](../controls/menu-items.md#appbaritembase) | `null` |  |

## `CommandBarFlyout`

```csharp
CommandBarFlyoutElement CommandBarFlyout(Element target, AppBarItemBase[]? primaryCommands = null, AppBarItemBase[]? secondaryCommands = null)
```

**Returns:** [`CommandBarFlyoutElement`](../elements/menus-and-commands.md#commandbarflyoutelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `target` | [`Element`](../elements/Element.md) |  |  |
| `primaryCommands` | [`AppBarItemBase[]?`](../controls/menu-items.md#appbaritembase) | `null` |  |
| `secondaryCommands` | [`AppBarItemBase[]?`](../controls/menu-items.md#appbaritembase) | `null` |  |

## `CommandHost`

Scopes keyboard accelerators from the given commands to the child subtree. Only commands with an Accelerator produce keyboard accelerators on the host element.

```csharp
CommandHostElement CommandHost(Command[] commands, Element child)
```

**Returns:** [`CommandHostElement`](../elements/menus-and-commands.md#commandhostelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `commands` | [`Command[]`](../input/Command.md) |  |
| `child` | [`Element`](../elements/Element.md) |  |

## `DevtoolsMenu`

Renders the optional in-app devtools menu when Microsoft.UI.Reactor.Devtools is referenced and active; otherwise returns [`Empty`](composition.md#empty).

```csharp
Element DevtoolsMenu(Func<IEnumerable<MenuFlyoutItemBase>>? items = null, string glyph = "⚡", string toolTip = "Devtools", string? automationId = null)
```

**Returns:** [`Element`](../elements/Element.md).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `items` | `Func<IEnumerable<MenuFlyoutItemBase>>?` | `null` |  |
| `glyph` | `string` | `"⚡"` |  |
| `toolTip` | `string` | `"Devtools"` |  |
| `automationId` | `string?` | `null` |  |

## `Menu`

```csharp
MenuBarItemData Menu(string title, params MenuFlyoutItemBase[] items)
```

**Returns:** [`MenuBarItemData`](../controls/menu-items.md#menubaritemdata).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `title` | `string` |  |
| `items` | [`MenuFlyoutItemBase[]`](../controls/menu-items.md#menuflyoutitembase) |  |

## `MenuBar`

```csharp
MenuBarElement MenuBar(params MenuBarItemData[] items)
```

**Returns:** [`MenuBarElement`](../elements/menus-and-commands.md#menubarelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `items` | [`MenuBarItemData[]`](../controls/menu-items.md#menubaritemdata) |  |

## `MenuFlyout`

```csharp
MenuFlyoutElement MenuFlyout(Element target, params MenuFlyoutItemBase[] items)
```

**Returns:** [`MenuFlyoutElement`](../elements/menus-and-commands.md#menuflyoutelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | [`Element`](../elements/Element.md) |  |
| `items` | [`MenuFlyoutItemBase[]`](../controls/menu-items.md#menuflyoutitembase) |  |

## `MenuItem`

```csharp
MenuFlyoutItemData MenuItem(Command command)
MenuFlyoutItemData MenuItem<T>(Command<T> command, T parameter)
MenuFlyoutItemData MenuItem(string text, Action? onClick = null, string? icon = null)
```

| Overload | Description |
| --- | --- |
| `MenuItem(Command)` | Creates a MenuFlyoutItem driven by a Command. Maps Label → Text, Icon, Execute → OnClick, Accelerator, IsEnabled, AccessKey. |
| `MenuItem<T>(Command<T>, T)` | Creates a MenuFlyoutItem driven by a parameterized Command. Wraps the action to invoke with the bound parameter. |
| `MenuItem(string, Action?, string?)` |  |

**Returns:** [`MenuFlyoutItemData`](../controls/menu-items.md#menuflyoutitemdata).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `command` | [`Command`](../input/Command.md) or [`Command<T>`](../input/Command-1.md) |  |  |
| `parameter` | `T` |  |  |
| `text` | `string` |  |  |
| `onClick` | `Action?` | `null` |  |
| `icon` | `string?` | `null` |  |

## `MenuItems`

```csharp
MenuFlyoutContentElement MenuItems(params MenuFlyoutItemBase[] items)
MenuFlyoutContentElement MenuItems(FlyoutPlacementMode placement, params MenuFlyoutItemBase[] items)
```

**Returns:** [`MenuFlyoutContentElement`](../elements/menus-and-commands.md#menuflyoutcontentelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `items` | [`MenuFlyoutItemBase[]`](../controls/menu-items.md#menuflyoutitembase) |  |
| `placement` | `FlyoutPlacementMode` |  |

## `MenuSeparator`

```csharp
MenuFlyoutSeparatorData MenuSeparator()
```

**Returns:** [`MenuFlyoutSeparatorData`](../controls/menu-items.md#menuflyoutseparatordata).

## `MenuSubItem`

```csharp
MenuFlyoutSubItemData MenuSubItem(string text, params MenuFlyoutItemBase[] items)
```

**Returns:** [`MenuFlyoutSubItemData`](../controls/menu-items.md#menuflyoutsubitemdata).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `text` | `string` |  |
| `items` | [`MenuFlyoutItemBase[]`](../controls/menu-items.md#menuflyoutitembase) |  |

## `RadioMenuItem`

```csharp
RadioMenuFlyoutItemData RadioMenuItem(string text, string groupName, bool isChecked = false, Action? onClick = null, string? icon = null)
```

**Returns:** [`RadioMenuFlyoutItemData`](../controls/menu-items.md#radiomenuflyoutitemdata).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `text` | `string` |  |  |
| `groupName` | `string` |  |  |
| `isChecked` | `bool` | `false` |  |
| `onClick` | `Action?` | `null` |  |
| `icon` | `string?` | `null` |  |

## `ToggleMenuItem`

```csharp
ToggleMenuFlyoutItemData ToggleMenuItem(string text, bool isChecked = false, Action<bool>? onIsCheckedChanged = null, string? icon = null)
```

**Returns:** [`ToggleMenuFlyoutItemData`](../controls/menu-items.md#togglemenuflyoutitemdata).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `text` | `string` |  |  |
| `isChecked` | `bool` | `false` |  |
| `onIsCheckedChanged` | `Action<bool>?` | `null` |  |
| `icon` | `string?` | `null` |  |
