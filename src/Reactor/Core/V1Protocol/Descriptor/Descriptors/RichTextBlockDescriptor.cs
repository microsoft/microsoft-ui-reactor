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
///   <item><c>Paragraphs</c> — single
///   <see cref="ControlDescriptor{TElement,TControl}.OneWay{TValue}"/>
///   entry whose set lambda calls into the shared
///   <c>Reconciler.RebuildRichTextBlocks</c> helper (also used by the legacy
///   mount arm). Comparer is
///   <see cref="ReferenceEqualityComparer.Instance"/> so a rebuild only
///   happens when the <c>Paragraphs</c> array reference changes.</item>
///   <item><c>Text</c> — included as an <c>.Initial</c> entry against the
///   element's positional <c>Text</c> field. The rebuild helper also reads
///   this when <c>Paragraphs</c> is null (mirrors the legacy
///   <c>MountRichTextBlock</c> fallback).</item>
///   <item><c>IsTextSelectionEnabled</c>, <c>FontSize</c>, <c>MaxLines</c>,
///   <c>LineHeight</c>, <c>TextAlignment</c>, <c>TextTrimming</c>,
///   <c>TextWrapping</c>, <c>CharacterSpacing</c> — simple <c>.OneWay</c> /
///   <c>.OneWayConditional</c> matching the legacy guards.</item>
/// </list></para>
///
/// <para><b>Known limitation:</b> the descriptor's <c>Paragraphs</c> entry
/// uses <see cref="ReferenceEqualityComparer.Instance"/>, so two different
/// <c>RichTextParagraph[]</c> arrays with structurally identical content
/// trigger a full <c>Blocks.Clear() + rebuild</c>. The legacy
/// <c>UpdateRichTextBlock</c> arm does an incremental
/// per-paragraph / per-inline diff that preserves any in-place WinUI
/// document state across reference-changed renders. Authors who need that
/// incremental behavior should stay on V1 OFF (legacy arm). The
/// reference-equality fast path is the right call for the common case where
/// authors hoist a static <c>RichTextParagraph[]</c> at module scope —
/// re-renders skip the rebuild entirely.</para>
/// </summary>
internal static class RichTextBlockDescriptor
{
    public static readonly ControlDescriptor<RichTextBlockElement, WinUI.RichTextBlock> Descriptor =
        new ControlDescriptor<RichTextBlockElement, WinUI.RichTextBlock>
        {
            Children = new None<RichTextBlockElement, WinUI.RichTextBlock>(),
            GetSetters = static e => e.Setters,
        }
        // Block-list build: comparer is ReferenceEqualityComparer so we only
        // rebuild on actual array swap. The set lambda receives the new array
        // but the rebuild helper takes the WHOLE element (it also reads .Text
        // for the no-Paragraphs fallback) — we pass the element via a
        // dedicated entry below that captures the element back through .Text.
        // For OneWay we need element-level access; piggyback on .OneWay over
        // the element itself so the set lambda gets the full record.
        .OneWay<RichTextBlockElement>(
            get:      static e => e,
            set:      static (c, e) => Reconciler.RebuildRichTextBlocks(e, c),
            comparer: new RichTextBlockRebuildComparer())
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

    /// <summary>Comparer that only treats two elements as equal when their
    /// <c>Paragraphs</c> array reference (AND <c>Text</c> fallback) match
    /// — drives the reference-equality rebuild gate documented in the type
    /// summary.</summary>
    private sealed class RichTextBlockRebuildComparer : IEqualityComparer<RichTextBlockElement>
    {
        public bool Equals(RichTextBlockElement? x, RichTextBlockElement? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            // Reference-equal Paragraphs (incl. both null) AND equal Text
            // (the no-Paragraphs fallback) → no rebuild needed.
            return ReferenceEquals(x.Paragraphs, y.Paragraphs)
                && string.Equals(x.Text, y.Text, global::System.StringComparison.Ordinal);
        }

        public int GetHashCode(RichTextBlockElement obj)
            => global::System.HashCode.Combine(
                global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Paragraphs ?? (object)""),
                obj.Text);
    }
}
