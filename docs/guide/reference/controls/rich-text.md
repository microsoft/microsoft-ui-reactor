# Rich text

Inline records for rich text blocks.

**Learn more:** [Text and Media](../../text-and-media.md)

## `RichTextHyperlink`

Inline hyperlink fragment inside a [`RichTextBlockElement`](../elements/text.md#richtextblockelement).

**Two modes** (issue #479):

- **Navigate mode** — when [`RichTextHyperlink.OnClick`](#onclick) is `null` the WinUI `Hyperlink` is mounted with [`RichTextHyperlink.NavigateUri`](#navigateuri) set and clicks open the URI via the platform launcher.
- **Click mode** — when [`RichTextHyperlink.OnClick`](#onclick) is non-null the delegate fires on click and the WinUI `NavigateUri` is left unset, so the platform does not navigate. Use this for clickable inline fragments (open a menu, enter edit mode, dispatch an action) without escaping to a hosted native subtree.

```csharp
public record RichTextHyperlink(string Text, Uri NavigateUri) : RichTextInline
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`RichTextInline`](#richtextinline).

### Constructors

#### `RichTextHyperlink`

Inline hyperlink fragment inside a [`RichTextBlockElement`](../elements/text.md#richtextblockelement).

**Two modes** (issue #479):

- **Navigate mode** — when [`RichTextHyperlink.OnClick`](#onclick) is `null` the WinUI `Hyperlink` is mounted with [`RichTextHyperlink.NavigateUri`](#navigateuri) set and clicks open the URI via the platform launcher.
- **Click mode** — when [`RichTextHyperlink.OnClick`](#onclick) is non-null the delegate fires on click and the WinUI `NavigateUri` is left unset, so the platform does not navigate. Use this for clickable inline fragments (open a menu, enter edit mode, dispatch an action) without escaping to a hosted native subtree.

```csharp
public RichTextHyperlink(string Text, Uri NavigateUri)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Text` | `string` |  |
| `NavigateUri` | `Uri` |  |

### Properties

#### `IsTabStop`

```csharp
public bool? IsTabStop  // get, init
```

#### `NavigateUri`

```csharp
public Uri NavigateUri  // get, init
```

#### `OnClick`

Optional click handler. When non-null, fires on every click and suppresses navigation (the underlying WinUI `Hyperlink` is mounted with no `NavigateUri`). When null, the inline behaves as a pure navigation link using [`RichTextHyperlink.NavigateUri`](#navigateuri).

```csharp
public Action? OnClick  // get, init
```

#### `TabIndex`

```csharp
public int? TabIndex  // get, init
```

#### `Text`

```csharp
public string Text  // get, init
```

#### `UnderlineStyle`

```csharp
public UnderlineStyle? UnderlineStyle  // get, init
```

## `RichTextInline`

```csharp
public abstract record RichTextInline
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `RichTextInline`

```csharp
protected RichTextInline()
```

### Properties

#### `CharacterSpacing`

```csharp
public int? CharacterSpacing  // get, init
```

#### `FontFamily`

```csharp
public string? FontFamily  // get, init
```

#### `FontSize`

```csharp
public double? FontSize  // get, init
```

#### `FontStretch`

```csharp
public FontStretch? FontStretch  // get, init
```

#### `FontStyle`

```csharp
public FontStyle? FontStyle  // get, init
```

#### `FontWeight`

```csharp
public FontWeight? FontWeight  // get, init
```

#### `Foreground`

```csharp
public Brush? Foreground  // get, init
```

#### `IsTextScaleFactorEnabled`

```csharp
public bool? IsTextScaleFactorEnabled  // get, init
```

#### `Language`

```csharp
public string? Language  // get, init
```

#### `TextDecorations`

```csharp
public TextDecorations? TextDecorations  // get, init
```

## `RichTextInlineUIContainer`

Mirrors WinUI's `Microsoft.UI.Xaml.Documents.InlineUIContainer` — an inline that embeds a UIElement inside a flowing [`RichTextBlockElement`](../elements/text.md#richtextblockelement).

**Two embedding routes** (issue #480):

- **Route A — Reactor element ([`RichTextInlineUIContainer.Child`](#child)):** the embedded UI is described declaratively as any other Reactor [`Element`](../elements/Element.md) (e.g. `Button("+", () => ...)`) and is mounted through the reconciler. The descriptor uses an incremental rich-text update path: re-renders of the owning `RichTextBlock` preserve the existing inline UIElement and reconcile the Reactor child via the standard child-reconciliation pipeline, so embedded interactive controls (sliders, buttons, etc.) keep their drag focus and component state across renders. A structural change to the surrounding paragraph tree (different paragraph count, mismatched inline shape) falls back to a full rebuild that tears down and remounts the child along with the rest of the block.
- **Route B — imperative native factory ([`RichTextInlineUIContainer.Factory`](#factory)):** the embedded UI is produced by a caller-supplied lambda returning a raw WinUI [`FrameworkElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement). Useful as an escape hatch for native controls that have no Reactor element counterpart. The factory is opaque to the reconciler, so the incremental path re-invokes it only when the delegate identity itself changes (capture-equal lambdas are treated as unchanged) — pass a stable delegate reference if you want the native UIElement to survive renders.

Exactly one of [`RichTextInlineUIContainer.Child`](#child) / [`RichTextInlineUIContainer.Factory`](#factory) should be non-null. If both are null the inline expands to a zero-size empty container; if both are set, [`RichTextInlineUIContainer.Child`](#child) wins.

```csharp
public record RichTextInlineUIContainer : RichTextInline
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`RichTextInline`](#richtextinline).

### Constructors

#### `RichTextInlineUIContainer`

```csharp
public RichTextInlineUIContainer()
```

### Properties

#### `Child`

Reactor element to mount as the inline UI (Route A).

```csharp
public Element? Child  // get, init
```

#### `Factory`

Imperative factory producing a native [`FrameworkElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement) (Route B). Invoked once per rebuild of the owning `RichTextBlock`.

```csharp
public Func<FrameworkElement>? Factory  // get, init
```

## `RichTextLineBreak`

```csharp
public record RichTextLineBreak : RichTextInline
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`RichTextInline`](#richtextinline).

### Constructors

#### `RichTextLineBreak`

```csharp
public RichTextLineBreak()
```

## `RichTextParagraph`

```csharp
public record RichTextParagraph(RichTextInline[] Inlines)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `RichTextParagraph`

```csharp
public RichTextParagraph(RichTextInline[] Inlines)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Inlines` | [`RichTextInline[]`](#richtextinline) |  |

### Properties

#### `CharacterSpacing`

```csharp
public int? CharacterSpacing  // get, init
```

#### `FontFamily`

```csharp
public string? FontFamily  // get, init
```

#### `FontSize`

```csharp
public double? FontSize  // get, init
```

#### `FontStretch`

```csharp
public FontStretch? FontStretch  // get, init
```

#### `FontStyle`

```csharp
public FontStyle? FontStyle  // get, init
```

#### `FontWeight`

```csharp
public FontWeight? FontWeight  // get, init
```

#### `Foreground`

```csharp
public Brush? Foreground  // get, init
```

#### `HorizontalTextAlignment`

```csharp
public TextAlignment? HorizontalTextAlignment  // get, init
```

#### `Inlines`

```csharp
public RichTextInline[] Inlines  // get, init
```

#### `IsTextScaleFactorEnabled`

```csharp
public bool? IsTextScaleFactorEnabled  // get, init
```

#### `Language`

```csharp
public string? Language  // get, init
```

#### `LineHeight`

```csharp
public double? LineHeight  // get, init
```

#### `LineStackingStrategy`

```csharp
public LineStackingStrategy? LineStackingStrategy  // get, init
```

#### `Margin`

```csharp
public Thickness? Margin  // get, init
```

#### `TextAlignment`

```csharp
public TextAlignment? TextAlignment  // get, init
```

#### `TextDecorations`

```csharp
public TextDecorations? TextDecorations  // get, init
```

#### `TextIndent`

```csharp
public double? TextIndent  // get, init
```

## `RichTextRun`

```csharp
public record RichTextRun(string Text) : RichTextInline
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`RichTextInline`](#richtextinline).

### Constructors

#### `RichTextRun`

```csharp
public RichTextRun(string Text)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Text` | `string` |  |

### Properties

#### `FlowDirection`

```csharp
public FlowDirection? FlowDirection  // get, init
```

#### `IsBold`

```csharp
public bool IsBold  // get, init
```

#### `IsItalic`

```csharp
public bool IsItalic  // get, init
```

#### `IsStrikethrough`

```csharp
public bool IsStrikethrough  // get, init
```

#### `Text`

```csharp
public string Text  // get, init
```
