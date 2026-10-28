using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Markdown;

/// <summary>
/// A Markdown link passed to <see cref="MarkdownOptions.LinkRenderer"/>.
/// </summary>
/// <param name="Text">
/// The label as plain text: its text runs concatenated, the alternative text of
/// any rendered image in place of the image, and line breaks as spaces. Use it for
/// an accessible name, tooltip, or confirmation prompt. Empty for an empty label.
/// </param>
/// <param name="Label">
/// The label as one selectable rich-text element preserving inline formatting,
/// default heading/table-header typography, and images, or <see langword="null"/>
/// for an empty label.
/// </param>
/// <param name="Uri">
/// The link target. Always absolute with an <c>http</c>, <c>https</c>, or
/// <c>mailto</c> scheme.
/// </param>
public sealed record MarkdownLink(string Text, RichTextBlockElement? Label, Uri Uri);
