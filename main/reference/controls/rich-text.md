---
title: "Rich text"
description: "Inline records for rich text blocks."
---

**Learn more:** [Text and Media](/microsoft-ui-reactor/main/controls/text-and-media)

## `RichTextHyperlink` [#richtexthyperlink]

Inline hyperlink fragment inside a [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement).

**Two modes** (issue #479):

- **Navigate mode** — when [`RichTextHyperlink.OnClick`](#onclick) is `null` the WinUI `Hyperlink` is mounted with [`RichTextHyperlink.NavigateUri`](#navigateuri) set and clicks open the URI via the platform launcher.
- **Click mode** — when [`RichTextHyperlink.OnClick`](#onclick) is non-null the delegate fires on click and the WinUI `NavigateUri` is left unset, so the platform does not navigate. Use this for clickable inline fragments (open a menu, enter edit mode, dispatch an action) without escaping to a hosted native subtree.

```csharp
public record RichTextHyperlink(string Text, Uri NavigateUri) : RichTextInline
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`RichTextInline`](#richtextinline).

### Constructors [#constructors]

#### `RichTextHyperlink` [#richtexthyperlink_1]

Inline hyperlink fragment inside a [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement).

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

### Properties [#properties]

#### `IsTabStop` [#istabstop]

```csharp
public bool? IsTabStop  // get, init
```

#### `NavigateUri` [#navigateuri]

```csharp
public Uri NavigateUri  // get, init
```

#### `OnClick` [#onclick]

Optional click handler. When non-null, fires on every click and suppresses navigation (the underlying WinUI `Hyperlink` is mounted with no `NavigateUri`). When null, the inline behaves as a pure navigation link using [`RichTextHyperlink.NavigateUri`](#navigateuri).

```csharp
public Action? OnClick  // get, init
```

#### `TabIndex` [#tabindex]

```csharp
public int? TabIndex  // get, init
```

#### `Text` [#text]

```csharp
public string Text  // get, init
```

#### `UnderlineStyle` [#underlinestyle]

```csharp
public UnderlineStyle? UnderlineStyle  // get, init
```

## `RichTextInline` [#richtextinline]

```csharp
public abstract record RichTextInline
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors_1]

#### `RichTextInline` [#richtextinline_1]

```csharp
protected RichTextInline()
```

### Properties [#properties_1]

#### `CharacterSpacing` [#characterspacing]

```csharp
public int? CharacterSpacing  // get, init
```

#### `FontFamily` [#fontfamily]

```csharp
public string? FontFamily  // get, init
```

#### `FontSize` [#fontsize]

```csharp
public double? FontSize  // get, init
```

#### `FontStretch` [#fontstretch]

```csharp
public FontStretch? FontStretch  // get, init
```

#### `FontStyle` [#fontstyle]

```csharp
public FontStyle? FontStyle  // get, init
```

#### `FontWeight` [#fontweight]

```csharp
public FontWeight? FontWeight  // get, init
```

#### `Foreground` [#foreground]

```csharp
public Brush? Foreground  // get, init
```

#### `IsTextScaleFactorEnabled` [#istextscalefactorenabled]

```csharp
public bool? IsTextScaleFactorEnabled  // get, init
```

#### `Language` [#language]

```csharp
public string? Language  // get, init
```

#### `TextDecorations` [#textdecorations]

```csharp
public TextDecorations? TextDecorations  // get, init
```

## `RichTextInlineUIContainer` [#richtextinlineuicontainer]

Mirrors WinUI's `Microsoft.UI.Xaml.Documents.InlineUIContainer` — an inline that embeds a UIElement inside a flowing [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement).

**Two embedding routes** (issue #480):

- **Route A — Reactor element ([`RichTextInlineUIContainer.Child`](#child)):** the embedded UI is described declaratively as any other Reactor [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) (e.g. `Button("+", () => ...)`) and is mounted through the reconciler. The descriptor uses an incremental rich-text update path: re-renders of the owning `RichTextBlock` preserve the existing inline UIElement and reconcile the Reactor child via the standard child-reconciliation pipeline, so embedded interactive controls (sliders, buttons, etc.) keep their drag focus and component state across renders. A structural change to the surrounding paragraph tree (different paragraph count, mismatched inline shape) falls back to a full rebuild that tears down and remounts the child along with the rest of the block.
- **Route B — imperative native factory ([`RichTextInlineUIContainer.Factory`](#factory)):** the embedded UI is produced by a caller-supplied lambda returning a raw WinUI [`FrameworkElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement). Useful as an escape hatch for native controls that have no Reactor element counterpart. The factory is opaque to the reconciler, so the incremental path re-invokes it only when the delegate identity itself changes (capture-equal lambdas are treated as unchanged) — pass a stable delegate reference if you want the native UIElement to survive renders.

Exactly one of [`RichTextInlineUIContainer.Child`](#child) / [`RichTextInlineUIContainer.Factory`](#factory) should be non-null. If both are null the inline expands to a zero-size empty container; if both are set, [`RichTextInlineUIContainer.Child`](#child) wins.

```csharp
public record RichTextInlineUIContainer : RichTextInline
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`RichTextInline`](#richtextinline).

### Constructors [#constructors_2]

#### `RichTextInlineUIContainer` [#richtextinlineuicontainer_1]

```csharp
public RichTextInlineUIContainer()
```

### Properties [#properties_2]

#### `Child` [#child]

Reactor element to mount as the inline UI (Route A).

```csharp
public Element? Child  // get, init
```

#### `Factory` [#factory]

Imperative factory producing a native [`FrameworkElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement) (Route B). Invoked once per rebuild of the owning `RichTextBlock`.

```csharp
public Func<FrameworkElement>? Factory  // get, init
```

## `RichTextLineBreak` [#richtextlinebreak]

```csharp
public record RichTextLineBreak : RichTextInline
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`RichTextInline`](#richtextinline).

### Constructors [#constructors_3]

#### `RichTextLineBreak` [#richtextlinebreak_1]

```csharp
public RichTextLineBreak()
```

## `RichTextParagraph` [#richtextparagraph]

```csharp
public record RichTextParagraph(RichTextInline[] Inlines)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors [#constructors_4]

#### `RichTextParagraph` [#richtextparagraph_1]

```csharp
public RichTextParagraph(RichTextInline[] Inlines)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Inlines` | [`RichTextInline[]`](#richtextinline) |  |

### Properties [#properties_3]

#### `CharacterSpacing` [#characterspacing_1]

```csharp
public int? CharacterSpacing  // get, init
```

#### `FontFamily` [#fontfamily_1]

```csharp
public string? FontFamily  // get, init
```

#### `FontSize` [#fontsize_1]

```csharp
public double? FontSize  // get, init
```

#### `FontStretch` [#fontstretch_1]

```csharp
public FontStretch? FontStretch  // get, init
```

#### `FontStyle` [#fontstyle_1]

```csharp
public FontStyle? FontStyle  // get, init
```

#### `FontWeight` [#fontweight_1]

```csharp
public FontWeight? FontWeight  // get, init
```

#### `Foreground` [#foreground_1]

```csharp
public Brush? Foreground  // get, init
```

#### `HorizontalTextAlignment` [#horizontaltextalignment]

```csharp
public TextAlignment? HorizontalTextAlignment  // get, init
```

#### `Inlines` [#inlines]

```csharp
public RichTextInline[] Inlines  // get, init
```

#### `IsTextScaleFactorEnabled` [#istextscalefactorenabled_1]

```csharp
public bool? IsTextScaleFactorEnabled  // get, init
```

#### `Language` [#language_1]

```csharp
public string? Language  // get, init
```

#### `LineHeight` [#lineheight]

```csharp
public double? LineHeight  // get, init
```

#### `LineStackingStrategy` [#linestackingstrategy]

```csharp
public LineStackingStrategy? LineStackingStrategy  // get, init
```

#### `Margin` [#margin]

```csharp
public Thickness? Margin  // get, init
```

#### `TextAlignment` [#textalignment]

```csharp
public TextAlignment? TextAlignment  // get, init
```

#### `TextDecorations` [#textdecorations_1]

```csharp
public TextDecorations? TextDecorations  // get, init
```

#### `TextIndent` [#textindent]

```csharp
public double? TextIndent  // get, init
```

## `RichTextRun` [#richtextrun]

```csharp
public record RichTextRun(string Text) : RichTextInline
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`RichTextInline`](#richtextinline).

### Constructors [#constructors_5]

#### `RichTextRun` [#richtextrun_1]

```csharp
public RichTextRun(string Text)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Text` | `string` |  |

### Properties [#properties_4]

#### `FlowDirection` [#flowdirection]

```csharp
public FlowDirection? FlowDirection  // get, init
```

#### `IsBold` [#isbold]

```csharp
public bool IsBold  // get, init
```

#### `IsItalic` [#isitalic]

```csharp
public bool IsItalic  // get, init
```

#### `IsStrikethrough` [#isstrikethrough]

```csharp
public bool IsStrikethrough  // get, init
```

#### `Text` [#text_1]

```csharp
public string Text  // get, init
```
