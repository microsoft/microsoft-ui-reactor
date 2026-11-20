# Elements: Status and info

Info bars, badges and progress indicators.

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Status and Info](../../status-and-info.md)

## `InfoBadgeElement`

The element record that describes a WinUI [`InfoBadge`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.infobadge) control.

```csharp
public record InfoBadgeElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`InfoBadge`](../factories/status-and-info.md#infobadge)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Icon` | `string?` |  |
| `Value` | `int?` |  |

## `InfoBarElement`

The element record that describes a WinUI [`InfoBar`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.infobar) control.

```csharp
public record InfoBarElement(string? Title = null, string? Message = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`InfoBar`](../factories/status-and-info.md#infobar)

**Modifiers:** [`ActionButtonClick`](../modifiers/shared-controls.md#actionbuttonclick), [`Closable`](../modifiers/status-and-info.md#closable), [`Closed`](../modifiers/shared-controls.md#closed), [`Content`](../modifiers/shared-controls.md#content), [`Error`](../modifiers/status-and-info.md#error), [`IconSource`](../modifiers/shared-controls.md#iconsource), [`Informational`](../modifiers/status-and-info.md#informational), [`IsClosable`](../modifiers/status-and-info.md#isclosable), [`Set`](../modifiers/native-properties.md#set), [`Severity`](../modifiers/status-and-info.md#severity), [`Success`](../modifiers/status-and-info.md#success), [`Warning`](../modifiers/status-and-info.md#warning)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `ActionButtonContent` | `string?` |  |  |
| `Content` | [`Element?`](Element.md) |  | Custom rich content rendered below the message (e.g. links, buttons, an embedded form). |
| `IconSource` | [`IconData?`](../controls/icons.md#icondata) |  | Custom icon source. When set, overrides the severity-based icon. |
| `IsClosable` | `bool` |  | Whether the bar shows its built-in close button. Defaults to `true`. |
| `IsOpen` | `bool` |  | Whether the bar is shown. Defaults to `true`, so `InfoBar("Title", "Message")` is visible without ceremony.  **Edge-triggered.** This declares a *transition*, not a mirror: Reactor writes the control only when the declared value *changes*. The user can dismiss the bar with its built-in close button, which sets `IsOpen` to `false` on the control without touching the declared value — re-rendering the same declared `true` will **not** bring it back, and is not meant to. Wire [`InfoBarElement.OnClosed`](#infobarelement) (or `.Closed(...)`) into the state that drives this property; a later `false → true` transition then re-opens the bar. |
| `Message` | `string?` | `null` |  |
| `OnActionButtonClick` | `Action?` |  |  |
| `OnClosed` | `Action?` |  |  |
| `Severity` | `InfoBarSeverity` |  |  |
| `Title` | `string?` | `null` |  |

## `ProgressElement`

The element record that describes a WinUI [`ProgressBar`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.progressbar) control.

```csharp
public record ProgressElement(double? Value = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Progress`](../factories/status-and-info.md#progress), [`ProgressIndeterminate`](../factories/status-and-info.md#progressindeterminate)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `IsIndeterminate` | `bool` |  |  |
| `Maximum` | `double` |  |  |
| `Minimum` | `double` |  |  |
| `ShowError` | `bool` |  |  |
| `ShowPaused` | `bool` |  |  |
| `Value` | `double?` | `null` |  |

## `ProgressRingElement`

The element record that describes a WinUI [`ProgressRing`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.progressring) control.

```csharp
public record ProgressRingElement(double? Value = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`ProgressRing`](../factories/status-and-info.md#progressring)

**Modifiers:** [`Active`](../modifiers/status-and-info.md#active), [`IsActive`](../modifiers/status-and-info.md#isactive), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `IsActive` | `bool` |  |  |
| `IsIndeterminate` | `bool` |  |  |
| `Maximum` | `double` |  |  |
| `Minimum` | `double` |  |  |
| `Value` | `double?` | `null` |  |
