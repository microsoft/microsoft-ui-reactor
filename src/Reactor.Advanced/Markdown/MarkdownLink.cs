using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Markdown;

/// <summary>
/// A Markdown link passed to <see cref="MarkdownOptions.LinkBuilder"/>.
/// </summary>
public sealed record MarkdownLink
{
    /// <summary>
    /// The label as plain text: its text runs concatenated, the alternative text of
    /// any rendered image in place of the image, and line breaks as spaces. Use it for
    /// an accessible name or tooltip. Empty for an empty label.
    /// </summary>
    /// <remarks>
    /// The label is written by the document author and can claim any destination
    /// (for example <c>[https://bank.example](https://other.example)</c>). Anything
    /// that tells the user where the link goes, such as a confirmation prompt, must
    /// show <see cref="Uri"/>, not this text.
    /// </remarks>
    public required string Text { get; init; }

    /// <summary>
    /// The label as one selectable rich-text element preserving inline formatting,
    /// default heading/table-header typography, and images, or <see langword="null"/>
    /// for an empty label.
    /// </summary>
    public RichTextBlockElement? Label { get; init; }

    /// <summary>
    /// The link target. Always absolute with an <c>http</c>, <c>https</c>, or
    /// <c>mailto</c> scheme.
    /// </summary>
    public required Uri Uri { get; init; }
}
