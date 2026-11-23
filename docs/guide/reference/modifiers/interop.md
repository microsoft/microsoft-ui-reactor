# Control modifiers: Interop

Modifiers that only the controls in the Interop area have.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Reactor for XAML Developers](../../xaml-developers.md)

## `CoreWebView2Initialized`

Wires the `CoreWebView2Initialized` handler. Fires once on the UI thread when the underlying `CoreWebView2` is available. Passing `null` clears.

```csharp
WebView2Element CoreWebView2Initialized(this WebView2Element el, Action? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`WebView2Element`](../elements/interop.md#webview2element).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `NavigationCompleted`

Wires the navigation-completed handler. Passing `null` clears.

```csharp
WebView2Element NavigationCompleted(this WebView2Element el, Action<Uri>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`WebView2Element`](../elements/interop.md#webview2element).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<Uri>?` |  |

## `NavigationStarting`

Wires the navigation-starting handler. Passing `null` clears.

```csharp
WebView2Element NavigationStarting(this WebView2Element el, Action<Uri>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`WebView2Element`](../elements/interop.md#webview2element).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<Uri>?` |  |

## `WebMessageReceived`

Wires the host-bound `WebMessageReceived` handler. The callback runs on the UI thread and receives the message payload as a string (JSON when the page sends structured data via `postMessage(...)`). Passing `null` clears.

```csharp
WebView2Element WebMessageReceived(this WebView2Element el, Action<string>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`WebView2Element`](../elements/interop.md#webview2element).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string>?` |  |
