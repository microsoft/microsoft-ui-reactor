# Elements: Menus and commands

Menus, menu bars, command bars and keyboard accelerators.

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Commanding](../../commanding.md)

## `CommandBarElement`

```csharp
public record CommandBarElement(AppBarItemBase[]? PrimaryCommands = null, AppBarItemBase[]? SecondaryCommands = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`CommandBar`](../factories/menus-and-commands.md#commandbar)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Content` | [`Element?`](Element.md) |  |  |
| `DefaultLabelPosition` | `CommandBarDefaultLabelPosition` |  |  |
| `IsOpen` | `bool` |  |  |
| `PrimaryCommands` | [`AppBarItemBase[]?`](../controls/menu-items.md#appbaritembase) | `null` |  |
| `SecondaryCommands` | [`AppBarItemBase[]?`](../controls/menu-items.md#appbaritembase) | `null` |  |

## `CommandBarFlyoutElement`

```csharp
public record CommandBarFlyoutElement(Element Target, AppBarItemBase[]? PrimaryCommands = null, AppBarItemBase[]? SecondaryCommands = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`CommandBarFlyout`](../factories/menus-and-commands.md#commandbarflyout)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `IsOpen` | `bool` |  | Opens the flyout declaratively. Like [`FlyoutElement.IsOpen`](dialogs-and-flyouts.md#flyoutelement) this is a one-shot trigger: the flyout is shown on mount when already `true`, and on the `false` → `true` edge of an update. Setting it back to `false` does not close the flyout — the user dismisses it (outside click, Esc, or invoking a command). The target normally opens the flyout on click without this. |
| `Placement` | `FlyoutPlacementMode` |  | Preferred position of the flyout relative to its target. Defaults to [`FlyoutPlacementMode.Auto`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.auto), which means "no opinion — let WinUI decide": Reactor clears `FlyoutBase.Placement` so it falls back to the control's own default ([`FlyoutPlacementMode.Top`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.top)) instead of writing `Auto`, because WinUI's show-time validator rejects `Auto` and terminates the process. Changing an already-mounted flyout from an explicit placement back to [`FlyoutPlacementMode.Auto`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.auto) therefore returns it to that default, rather than leaving a stale local value that would outrank a `Style` setter. |
| `PrimaryCommands` | [`AppBarItemBase[]?`](../controls/menu-items.md#appbaritembase) | `null` |  |
| `SecondaryCommands` | [`AppBarItemBase[]?`](../controls/menu-items.md#appbaritembase) | `null` |  |
| `Target` | [`Element`](Element.md) |  |  |

## `CommandHostElement`

Scopes keyboard accelerators from a set of commands to a subtree. Accelerators are only active when the host or its descendants have focus.

```csharp
public record CommandHostElement(Command[] Commands, Element Child) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`CommandHost`](../factories/menus-and-commands.md#commandhost)

| Property | Type | Description |
| --- | --- | --- |
| `Child` | [`Element`](Element.md) |  |
| `Commands` | [`Command[]`](../input/Command.md) |  |

## `MenuBarElement`

```csharp
public record MenuBarElement(MenuBarItemData[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`MenuBar`](../factories/menus-and-commands.md#menubar)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Items` | [`MenuBarItemData[]`](../controls/menu-items.md#menubaritemdata) |  |

## `MenuFlyoutContentElement`

Describes a menu flyout (used as a slot value on buttons or as a modifier attachment). NOT independently mountable — the reconciler recognizes it in flyout slots.

```csharp
public record MenuFlyoutContentElement(MenuFlyoutItemBase[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`MenuItems`](../factories/menus-and-commands.md#menuitems)

| Property | Type | Description |
| --- | --- | --- |
| `Items` | [`MenuFlyoutItemBase[]`](../controls/menu-items.md#menuflyoutitembase) |  |
| `Placement` | `FlyoutPlacementMode` | Preferred position of the flyout relative to its target. Defaults to [`FlyoutPlacementMode.Auto`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.auto), which means "no opinion — let WinUI decide": Reactor clears `FlyoutBase.Placement` so it falls back to the control's own default ([`FlyoutPlacementMode.Top`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.top)) instead of writing `Auto`, because WinUI's show-time validator rejects `Auto` and terminates the process. Changing an already-mounted flyout from an explicit placement back to [`FlyoutPlacementMode.Auto`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.auto) therefore returns it to that default, rather than leaving a stale local value that would outrank a `Style` setter. |

## `MenuFlyoutElement`

```csharp
public record MenuFlyoutElement(Element Target, MenuFlyoutItemBase[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`MenuFlyout`](../factories/menus-and-commands.md#menuflyout)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Items` | [`MenuFlyoutItemBase[]`](../controls/menu-items.md#menuflyoutitembase) |  |
| `Target` | [`Element`](Element.md) |  |
