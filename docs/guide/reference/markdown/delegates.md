# Delegates

The delegate types in this category.

**Learn more:** [Text and Media](../../text-and-media.md)

## `MarkdownDebugLogCallback`

```csharp
public delegate void MarkdownDebugLogCallback(string message)
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

## `MarkdownEnterBlockCallback`

Callback delegates for the SAX-style parser. All callbacks may abort parsing by returning non-zero.

```csharp
public delegate int MarkdownEnterBlockCallback(MarkdownBlockType type, object? detail)
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

## `MarkdownEnterSpanCallback`

```csharp
public delegate int MarkdownEnterSpanCallback(MarkdownSpanType type, object? detail)
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

## `MarkdownLeaveBlockCallback`

```csharp
public delegate int MarkdownLeaveBlockCallback(MarkdownBlockType type, object? detail)
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

## `MarkdownLeaveSpanCallback`

```csharp
public delegate int MarkdownLeaveSpanCallback(MarkdownSpanType type, object? detail)
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.

## `MarkdownTextCallback`

```csharp
public delegate int MarkdownTextCallback(MarkdownTextType type, ReadOnlySpan<char> text)
```

Namespace: `Microsoft.UI.Reactor.Markdown`. Package: `Microsoft.UI.Reactor.Advanced`.
