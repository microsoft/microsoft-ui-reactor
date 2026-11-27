# MarkdownTextSelectionOptions

Copy policy for one selection scope; a non-null parser value only enables semantic surfaces.

```csharp
public sealed record MarkdownTextSelectionOptions
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Text and Media](../../text-and-media.md)

## Constructors

### `MarkdownTextSelectionOptions`

```csharp
public MarkdownTextSelectionOptions()
```

## Properties

### `OnCopyResult`

Feedback only, called after Reactor's single clipboard write. The Boolean is true only on success. Expected clipboard-access failures report false; unexpected exceptions propagate. This callback must not write the clipboard again.

```csharp
public Action<string, bool>? OnCopyResult  // get, init
```
