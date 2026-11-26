---
title: "Delegates"
description: "The delegate types in this category."
---

**Learn more:** [Text and Media](/microsoft-ui-reactor/main/controls/text-and-media)

## `MarkdownDebugLogCallback` [#markdowndebuglogcallback]

```csharp
public delegate void MarkdownDebugLogCallback(string message)
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

## `MarkdownEnterBlockCallback` [#markdownenterblockcallback]

Callback delegates for the SAX-style parser. All callbacks may abort parsing by returning non-zero.

```csharp
public delegate int MarkdownEnterBlockCallback(MarkdownBlockType type, object? detail)
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

## `MarkdownEnterSpanCallback` [#markdownenterspancallback]

```csharp
public delegate int MarkdownEnterSpanCallback(MarkdownSpanType type, object? detail)
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

## `MarkdownLeaveBlockCallback` [#markdownleaveblockcallback]

```csharp
public delegate int MarkdownLeaveBlockCallback(MarkdownBlockType type, object? detail)
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

## `MarkdownLeaveSpanCallback` [#markdownleavespancallback]

```csharp
public delegate int MarkdownLeaveSpanCallback(MarkdownSpanType type, object? detail)
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

## `MarkdownTextCallback` [#markdowntextcallback]

```csharp
public delegate int MarkdownTextCallback(MarkdownTextType type, ReadOnlySpan<char> text)
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.
