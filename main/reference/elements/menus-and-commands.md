---
title: "Elements: Menus and commands"
description: "Menus, menu bars, command bars and keyboard accelerators."
sidebar:
  label: "Menus and commands"
---

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Commanding](/microsoft-ui-reactor/main/guides/commanding)

## `CommandBarElement` [#commandbarelement]

```csharp
public record CommandBarElement(AppBarItemBase[]? PrimaryCommands = null, AppBarItemBase[]? SecondaryCommands = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`CommandBar`](/microsoft-ui-reactor/main/reference/factories/menus-and-commands#commandbar)

**Modifiers:** [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Content` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `DefaultLabelPosition` | `CommandBarDefaultLabelPosition` |  |  |
| `IsOpen` | `bool` |  |  |
| `PrimaryCommands` | [`AppBarItemBase[]?`](/microsoft-ui-reactor/main/reference/controls/menu-items#appbaritembase) | `null` |  |
| `SecondaryCommands` | [`AppBarItemBase[]?`](/microsoft-ui-reactor/main/reference/controls/menu-items#appbaritembase) | `null` |  |

## `CommandBarFlyoutElement` [#commandbarflyoutelement]

```csharp
public record CommandBarFlyoutElement(Element Target, AppBarItemBase[]? PrimaryCommands = null, AppBarItemBase[]? SecondaryCommands = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`CommandBarFlyout`](/microsoft-ui-reactor/main/reference/factories/menus-and-commands#commandbarflyout)

**Modifiers:** [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `IsOpen` | `bool` |  | Opens the flyout declaratively. Like [`FlyoutElement.IsOpen`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#flyoutelement) this is a one-shot trigger: the flyout is shown on mount when already `true`, and on the `false` → `true` edge of an update. Setting it back to `false` does not close the flyout — the user dismisses it (outside click, Esc, or invoking a command). The target normally opens the flyout on click without this. |
| `Placement` | `FlyoutPlacementMode` |  | Preferred position of the flyout relative to its target. Defaults to [`FlyoutPlacementMode.Auto`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.auto), which means "no opinion — let WinUI decide": Reactor clears `FlyoutBase.Placement` so it falls back to the control's own default ([`FlyoutPlacementMode.Top`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.top)) instead of writing `Auto`, because WinUI's show-time validator rejects `Auto` and terminates the process. Changing an already-mounted flyout from an explicit placement back to [`FlyoutPlacementMode.Auto`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.auto) therefore returns it to that default, rather than leaving a stale local value that would outrank a `Style` setter. |
| `PrimaryCommands` | [`AppBarItemBase[]?`](/microsoft-ui-reactor/main/reference/controls/menu-items#appbaritembase) | `null` |  |
| `SecondaryCommands` | [`AppBarItemBase[]?`](/microsoft-ui-reactor/main/reference/controls/menu-items#appbaritembase) | `null` |  |
| `Target` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |

## `CommandHostElement` [#commandhostelement]

Scopes keyboard accelerators from a set of commands to a subtree. Accelerators are only active when the host or its descendants have focus.

```csharp
public record CommandHostElement(Command[] Commands, Element Child) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`CommandHost`](/microsoft-ui-reactor/main/reference/factories/menus-and-commands#commandhost)

| Property | Type | Description |
| --- | --- | --- |
| `Child` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `Commands` | [`Command[]`](/microsoft-ui-reactor/main/reference/input/Command) |  |

## `MenuBarElement` [#menubarelement]

```csharp
public record MenuBarElement(MenuBarItemData[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`MenuBar`](/microsoft-ui-reactor/main/reference/factories/menus-and-commands#menubar)

**Modifiers:** [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Description |
| --- | --- | --- |
| `Items` | [`MenuBarItemData[]`](/microsoft-ui-reactor/main/reference/controls/menu-items#menubaritemdata) |  |

## `MenuFlyoutContentElement` [#menuflyoutcontentelement]

Describes a menu flyout (used as a slot value on buttons or as a modifier attachment). NOT independently mountable — the reconciler recognizes it in flyout slots.

```csharp
public record MenuFlyoutContentElement(MenuFlyoutItemBase[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`MenuItems`](/microsoft-ui-reactor/main/reference/factories/menus-and-commands#menuitems)

| Property | Type | Description |
| --- | --- | --- |
| `Items` | [`MenuFlyoutItemBase[]`](/microsoft-ui-reactor/main/reference/controls/menu-items#menuflyoutitembase) |  |
| `Placement` | `FlyoutPlacementMode` | Preferred position of the flyout relative to its target. Defaults to [`FlyoutPlacementMode.Auto`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.auto), which means "no opinion — let WinUI decide": Reactor clears `FlyoutBase.Placement` so it falls back to the control's own default ([`FlyoutPlacementMode.Top`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.top)) instead of writing `Auto`, because WinUI's show-time validator rejects `Auto` and terminates the process. Changing an already-mounted flyout from an explicit placement back to [`FlyoutPlacementMode.Auto`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode.auto) therefore returns it to that default, rather than leaving a stale local value that would outrank a `Style` setter. |

## `MenuFlyoutElement` [#menuflyoutelement]

```csharp
public record MenuFlyoutElement(Element Target, MenuFlyoutItemBase[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`MenuFlyout`](/microsoft-ui-reactor/main/reference/factories/menus-and-commands#menuflyout)

**Modifiers:** [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Description |
| --- | --- | --- |
| `Items` | [`MenuFlyoutItemBase[]`](/microsoft-ui-reactor/main/reference/controls/menu-items#menuflyoutitembase) |  |
| `Target` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
