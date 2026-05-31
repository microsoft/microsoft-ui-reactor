using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3-final Batch B — descriptor variant of the hand-coded
/// <c>MountRichTextBlock</c> / <c>UpdateRichTextBlock</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Paragraphs</c> / <c>Text</c> — single
///   <see cref="ControlDescriptor{TElement,TControl}.ImperativeBridged"/>
///   entry. <b>Mount</b> calls the shared <c>Reconciler.RebuildRichTextBlocks</c>
///   helper (legacy mount arm uses the same path). <b>Update</b> calls
///   <c>Reconciler.UpdateRichTextBlocks</c> which does an in-place per-run
///   incremental diff with full-rebuild fallback. Issue #480 follow-up:
///   the incremental path preserves <see cref="RichTextInlineUIContainer"/>
///   Route A children (Reactor element) across renders so embedded
///   interactive controls (sliders, buttons) keep their drag focus +
///   component state.</item>
///   <item><c>IsTextSelectionEnabled</c>, <c>FontSize</c>, <c>MaxLines</c>,
///   <c>LineHeight</c>, <c>TextAlignment</c>, <c>TextTrimming</c>,
///   <c>TextWrapping</c>, <c>CharacterSpacing</c> — simple <c>.OneWay</c> /
///   <c>.OneWayConditional</c> matching the legacy guards.</item>
/// </list></para>
/// </summary>
internal static class RichTextBlockDescriptor
{
    public static readonly ControlDescriptor<RichTextBlockElement, WinUI.RichTextBlock> Descriptor =
        new ControlDescriptor<RichTextBlockElement, WinUI.RichTextBlock>
        {
            Children = new None<RichTextBlockElement, WinUI.RichTextBlock>(),
            GetSetters = static e => e.Setters,
        }
        // Block-list build/diff: ImperativeBridged gives the update lambda
        // BOTH old and new elements, so we can incrementally diff paragraphs
        // and inlines without stashing prior state in a CWT. The mount
        // lambda just delegates to the existing wholesale-mount helper.
        // Issue #480 follow-up: the update lambda was promoted from a
        // wholesale rebuild (via OneWayBridged + reference-equality
        // comparer) to a true incremental path so Route A inline UI
        // children survive parent re-renders with stable state.
        .ImperativeBridged(
            mount: static (ctx, c, e) =>
                ctx.Reconciler.RebuildRichTextBlocks(e, c, ctx.RequestRerender),
            update: static (ctx, c, prev, next) =>
            {
                // Fast path: identical Paragraphs reference AND identical
                // Text → nothing for the block-list path to do. Other
                // descriptor entries (FontSize, IsTextSelectionEnabled,
                // etc.) still run their own diffs.
                if (ReferenceEquals(prev.Paragraphs, next.Paragraphs)
                    && string.Equals(prev.Text, next.Text, global::System.StringComparison.Ordinal))
                    return;
                ctx.Reconciler.UpdateRichTextBlocks(c, prev, next, ctx.RequestRerender);
            })
        .OneWay(
            get: static e => e.IsTextSelectionEnabled,
            set: static (c, v) => c.IsTextSelectionEnabled = v)
        .OneWayConditional(
            get:         static e => e.TextWrapping,
            set:         static (c, v) => c.TextWrapping = v!.Value,
            shouldWrite: static e => e.TextWrapping.HasValue)
        .OneWayConditional(
            get:         static e => e.FontSize,
            set:         static (c, v) => c.FontSize = v!.Value,
            shouldWrite: static e => e.FontSize.HasValue)
        .OneWayConditional(
            get:         static e => e.MaxLines,
            set:         static (c, v) => c.MaxLines = v,
            shouldWrite: static e => e.MaxLines > 0)
        .OneWayConditional(
            get:         static e => e.LineHeight,
            set:         static (c, v) => c.LineHeight = v!.Value,
            shouldWrite: static e => e.LineHeight.HasValue)
        .OneWayConditional(
            get:         static e => e.TextAlignment,
            set:         static (c, v) => c.TextAlignment = v!.Value,
            shouldWrite: static e => e.TextAlignment.HasValue)
        .OneWayConditional(
            get:         static e => e.TextTrimming,
            set:         static (c, v) => c.TextTrimming = v!.Value,
            shouldWrite: static e => e.TextTrimming.HasValue)
        .OneWayConditional(
            get:         static e => e.CharacterSpacing,
            set:         static (c, v) => c.CharacterSpacing = v,
            shouldWrite: static e => e.CharacterSpacing != 0);
}
