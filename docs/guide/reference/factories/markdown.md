# Factories: Markdown

Rendered Markdown, from the Microsoft.UI.Reactor.Advanced package.

Each factory method returns an element record. These are on `Microsoft.UI.Reactor.Advanced.Factories` in the `Microsoft.UI.Reactor.Advanced` package, so add `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Learn more:** [Text and Media](../../text-and-media.md)

## `Markdown`

```csharp
Element Markdown(string markdown)
Element Markdown(string markdown, MarkdownOptions options)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

| Overload | Description |
| --- | --- |
| `Markdown(string)` | Render a markdown string as a Reactor element tree. |
| `Markdown(string, MarkdownOptions)` | Render a markdown string as a Reactor element tree with custom rendering options. |

**Returns:** [`Element`](../elements/Element.md).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `markdown` | `string` |  |
| `options` | [`MarkdownOptions`](../markdown/MarkdownOptions.md) |  |
