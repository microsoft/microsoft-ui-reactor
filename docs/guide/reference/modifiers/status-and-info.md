# Control modifiers: Status and info

Modifiers that only the controls in the Status and info area have.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Status and Info](../../status-and-info.md)

## `Active`

Sets whether the ProgressRing is active (spinning).

```csharp
ProgressRingElement Active(this ProgressRingElement el, bool active = true)
```

Declared in `ElementExtensions`.

> **Obsolete.** Use IsActive() — aligns with WinUI naming (see #268).

**Applies to:** [`ProgressRingElement`](../elements/status-and-info.md#progressringelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `active` | `bool` | `true` |  |

## `Closable`

Sets whether the InfoBar can be closed by the user.

```csharp
InfoBarElement Closable(this InfoBarElement el, bool closable = true)
```

Declared in `ElementExtensions`.

> **Obsolete.** Use IsClosable() — aligns with WinUI naming (see #268).

**Applies to:** [`InfoBarElement`](../elements/status-and-info.md#infobarelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `closable` | `bool` | `true` |  |

## `Error`

Sets severity to [`InfoBarSeverity.Error`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.infobarseverity.error) — red skin, error icon.

```csharp
InfoBarElement Error(this InfoBarElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`InfoBarElement`](../elements/status-and-info.md#infobarelement).

## `Informational`

Sets severity to [`InfoBarSeverity.Informational`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.infobarseverity.informational) — neutral blue/grey skin, info icon.

```csharp
InfoBarElement Informational(this InfoBarElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`InfoBarElement`](../elements/status-and-info.md#infobarelement).

## `IsActive`

Sets whether the ProgressRing is active (spinning).

```csharp
ProgressRingElement IsActive(this ProgressRingElement el, bool active = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`ProgressRingElement`](../elements/status-and-info.md#progressringelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `active` | `bool` | `true` |  |

## `IsClosable`

Sets whether the InfoBar can be closed by the user.

```csharp
InfoBarElement IsClosable(this InfoBarElement el, bool closable = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`InfoBarElement`](../elements/status-and-info.md#infobarelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `closable` | `bool` | `true` |  |

## `Severity`

```csharp
InfoBarElement Severity(this InfoBarElement el, InfoBarSeverity severity)
```

Declared in `ElementExtensions`.

**Applies to:** [`InfoBarElement`](../elements/status-and-info.md#infobarelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `severity` | `InfoBarSeverity` |  |

## `Success`

Sets severity to [`InfoBarSeverity.Success`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.infobarseverity.success) — green skin, check icon.

```csharp
InfoBarElement Success(this InfoBarElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`InfoBarElement`](../elements/status-and-info.md#infobarelement).

## `Warning`

Sets severity to [`InfoBarSeverity.Warning`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.infobarseverity.warning) — yellow skin, warning icon.

```csharp
InfoBarElement Warning(this InfoBarElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`InfoBarElement`](../elements/status-and-info.md#infobarelement).
