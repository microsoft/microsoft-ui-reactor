---
title: "Elements: Interop"
description: "Hosted XAML content, web views and maps."
sidebar:
  label: "Interop"
---

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Coming from XAML](/microsoft-ui-reactor/main/get-started/xaml-developers)

## `MapControlElement` [#mapcontrolelement]

The element record that describes a WinUI [`MapControl`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.mapcontrol) control.

```csharp
public record MapControlElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`MapControl`](/microsoft-ui-reactor/main/reference/factories/interop#mapcontrol)

**Modifiers:** [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Description |
| --- | --- | --- |
| `MapServiceToken` | `string?` |  |
| `ZoomLevel` | `double` |  |

## `WebView2Element` [#webview2element]

The element record that describes a WinUI [`WebView2`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.webview2) control.

```csharp
public record WebView2Element(Uri? Source = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`WebView2`](/microsoft-ui-reactor/main/reference/factories/interop#webview2)

**Modifiers:** [`CoreWebView2Initialized`](/microsoft-ui-reactor/main/reference/modifiers/interop#corewebview2initialized), [`NavigationCompleted`](/microsoft-ui-reactor/main/reference/modifiers/interop#navigationcompleted), [`NavigationStarting`](/microsoft-ui-reactor/main/reference/modifiers/interop#navigationstarting), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`WebMessageReceived`](/microsoft-ui-reactor/main/reference/modifiers/interop#webmessagereceived)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `OnCoreWebView2Initialized` | `Action?` |  | Raised once `CoreWebView2` initialization completes — the earliest point at which features like `AddScriptToExecuteOnDocumentCreatedAsync` or `AddHostObjectToScript` become available. Fires on the UI thread. |
| `OnNavigationCompleted` | `Action<Uri>?` |  |  |
| `OnNavigationStarting` | `Action<Uri>?` |  |  |
| `OnWebMessageReceived` | `Action<string>?` |  | Raised when the hosted page posts a message via `window.chrome.webview.postMessage(...)`. The callback receives the JSON payload as a string.  Threading: messages dispatch on the UI thread (the WinUI WebView2 raises `WebMessageReceived` via the control's dispatcher), so the handler is safe to mutate component state from directly. |
| `Source` | `Uri?` | `null` |  |

## `XamlHostElement` [#xamlhostelement]

An element that embeds an arbitrary FrameworkElement (UserControl, custom control, etc.) into the Reactor tree. The factory creates the control; the updater patches it.

```csharp
public record XamlHostElement(Func<FrameworkElement> Factory, Action<FrameworkElement>? Updater = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Hosting`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`XamlHost`](/microsoft-ui-reactor/main/reference/factories/interop#xamlhost)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Factory` | `Func<FrameworkElement>` |  |  |
| `TypeKey` | `string?` |  | Optional discriminator for the reconciler's CanUpdate check. When set, two XamlHostElements can only update in place if their TypeKeys match. Use this to prevent unrelated host elements from being reconciled against each other. |
| `Updater` | `Action<FrameworkElement>?` | `null` |  |

**Remarks**

Spec 058 §15 (P5.28) — migrated to a generated monomorphic decorator (`[WrapDecorator]`), replacing the hand-written `XamlHostDescriptor`.

## `XamlPageElement` [#xamlpageelement]

An element that embeds a XAML Page inside a Frame, enabling navigation to existing XAML pages from within a Reactor component tree.

```csharp
public record XamlPageElement(Type PageType, object? Parameter = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Hosting`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`XamlPage`](/microsoft-ui-reactor/main/reference/factories/interop#xamlpage)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `PageType` | `Type` |  |  |
| `Parameter` | `object?` | `null` |  |

**Remarks**

Spec 058 §15 (P5.28) — migrated to a generated monomorphic decorator (`[WrapDecorator]`): the generated Pattern-A cctor self-registers the decorator on first `new`, replacing the hand-written `XamlPageDescriptor`. The control is a [`Frame`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.frame) created once and re-navigated in place on update.
