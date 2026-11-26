---
title: "Control modifiers: Interop"
description: "Modifiers that only the controls in the Interop area have."
sidebar:
  label: "Interop"
---

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Reactor for XAML Developers](/microsoft-ui-reactor/main/get-started/xaml-developers)

## `CoreWebView2Initialized` [#corewebview2initialized]

Wires the `CoreWebView2Initialized` handler. Fires once on the UI thread when the underlying `CoreWebView2` is available. Passing `null` clears.

```csharp
WebView2Element CoreWebView2Initialized(this WebView2Element el, Action? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`WebView2Element`](/microsoft-ui-reactor/main/reference/elements/interop#webview2element).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `NavigationCompleted` [#navigationcompleted]

Wires the navigation-completed handler. Passing `null` clears.

```csharp
WebView2Element NavigationCompleted(this WebView2Element el, Action<Uri>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`WebView2Element`](/microsoft-ui-reactor/main/reference/elements/interop#webview2element).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<Uri>?` |  |

## `NavigationStarting` [#navigationstarting]

Wires the navigation-starting handler. Passing `null` clears.

```csharp
WebView2Element NavigationStarting(this WebView2Element el, Action<Uri>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`WebView2Element`](/microsoft-ui-reactor/main/reference/elements/interop#webview2element).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<Uri>?` |  |

## `WebMessageReceived` [#webmessagereceived]

Wires the host-bound `WebMessageReceived` handler. The callback runs on the UI thread and receives the message payload as a string (JSON when the page sends structured data via `postMessage(...)`). Passing `null` clears.

```csharp
WebView2Element WebMessageReceived(this WebView2Element el, Action<string>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`WebView2Element`](/microsoft-ui-reactor/main/reference/elements/interop#webview2element).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string>?` |  |
