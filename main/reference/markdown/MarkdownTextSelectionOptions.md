---
title: "MarkdownTextSelectionOptions"
description: "Copy policy for one selection scope; a non-null parser value only enables semantic surfaces."
---

```csharp
public sealed record MarkdownTextSelectionOptions
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Text and Media](/microsoft-ui-reactor/main/controls/text-and-media)

## Constructors [#constructors]

### `MarkdownTextSelectionOptions` [#markdowntextselectionoptions_1]

```csharp
public MarkdownTextSelectionOptions()
```

## Properties [#properties]

### `OnCopyResult` [#oncopyresult]

Feedback only, called after Reactor's single clipboard write. The Boolean is true only on success. Expected clipboard-access failures report false; unexpected exceptions propagate. This callback must not write the clipboard again.

```csharp
public Action<string, bool>? OnCopyResult  // get, init
```
