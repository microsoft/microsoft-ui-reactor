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
            // Always delegate to UpdateRichTextBlocks — even when prev/next
            // Paragraphs are reference-equal. A reference-equal Paragraphs
            // array (e.g. memoized at the parent) can still contain a Route A
            // InlineUIContainer whose embedded Reactor child has pending
            // state-driven renders to apply. UpdateRichTextBlocks does the
            // cheap per-paragraph ref-skip itself and still walks Route A
            // inlines for reconciliation, so adding a top-level fast path
            // would silently strand inner Component state updates.
            update: static (ctx, c, prev, next) =>
                ctx.Reconciler.UpdateRichTextBlocks(c, prev, next, ctx.RequestRerender))
        .OneWay(
            get: static e => e.IsTextSelectionEnabled,
            set: static (c, v) => c.IsTextSelectionEnabled = v)
        .OneWay(
            get: static e => e.TextWrapping.HasValue ? e.TextWrapping.Value : Optional<TextWrapping>.Unset,
            set: static (c, v) => c.TextWrapping = v,
            dp:  WinUI.RichTextBlock.TextWrappingProperty)
        .OneWay(
            get: static e => e.FontSize.HasValue ? e.FontSize.Value : Optional<double>.Unset,
            set: static (c, v) => c.FontSize = v,
            dp:  WinUI.RichTextBlock.FontSizeProperty)
        .OneWay(
            get: static e => e.FontFamily is null ? Optional<Microsoft.UI.Xaml.Media.FontFamily>.Unset : e.FontFamily,
            set: static (c, v) => c.FontFamily = v,
            dp:  WinUI.RichTextBlock.FontFamilyProperty)
        .OneWay(
            get: static e => e.FontWeight.HasValue ? e.FontWeight.Value : Optional<global::Windows.UI.Text.FontWeight>.Unset,
            set: static (c, v) => c.FontWeight = v,
            dp:  WinUI.RichTextBlock.FontWeightProperty)
        .OneWay(
            get: static e => e.FontStyle.HasValue ? e.FontStyle.Value : Optional<global::Windows.UI.Text.FontStyle>.Unset,
            set: static (c, v) => c.FontStyle = v,
            dp:  WinUI.RichTextBlock.FontStyleProperty)
        .OneWay(
            get: static e => e.FontStretch.HasValue ? e.FontStretch.Value : Optional<global::Windows.UI.Text.FontStretch>.Unset,
            set: static (c, v) => c.FontStretch = v,
            dp:  WinUI.RichTextBlock.FontStretchProperty)
        .OneWay(
            get: static e => e.Foreground is null ? Optional<Microsoft.UI.Xaml.Media.Brush>.Unset : e.Foreground,
            set: static (c, v) => c.Foreground = v,
            dp:  WinUI.RichTextBlock.ForegroundProperty)
        .OneWay(
            get: static e => e.MaxLines,
            set: static (c, v) => c.MaxLines = v)
        .OneWay(
            get: static e => e.LineHeight.HasValue ? e.LineHeight.Value : Optional<double>.Unset,
            set: static (c, v) => c.LineHeight = v,
            dp:  WinUI.RichTextBlock.LineHeightProperty)
        .OneWay(
            get: static e => e.LineStackingStrategy.HasValue ? e.LineStackingStrategy.Value : Optional<LineStackingStrategy>.Unset,
            set: static (c, v) => c.LineStackingStrategy = v,
            dp:  WinUI.RichTextBlock.LineStackingStrategyProperty)
        .OneWay(
            get: static e => e.TextAlignment.HasValue ? e.TextAlignment.Value : Optional<TextAlignment>.Unset,
            set: static (c, v) => c.TextAlignment = v,
            dp:  WinUI.RichTextBlock.TextAlignmentProperty)
        .OneWay(
            get: static e => e.HorizontalTextAlignment.HasValue ? e.HorizontalTextAlignment.Value : Optional<TextAlignment>.Unset,
            set: static (c, v) => c.HorizontalTextAlignment = v,
            dp:  WinUI.RichTextBlock.HorizontalTextAlignmentProperty)
        .OneWay(
            get: static e => e.TextTrimming.HasValue ? e.TextTrimming.Value : Optional<TextTrimming>.Unset,
            set: static (c, v) => c.TextTrimming = v,
            dp:  WinUI.RichTextBlock.TextTrimmingProperty)
        .OneWay(
            get: static e => e.CharacterSpacing,
            set: static (c, v) => c.CharacterSpacing = v)
        .OneWay(
            get: static e => e.TextDecorations.HasValue ? e.TextDecorations.Value : Optional<global::Windows.UI.Text.TextDecorations>.Unset,
            set: static (c, v) => c.TextDecorations = v,
            dp:  WinUI.RichTextBlock.TextDecorationsProperty)
        .OneWay(
            get: static e => e.TextIndent.HasValue ? e.TextIndent.Value : Optional<double>.Unset,
            set: static (c, v) => c.TextIndent = v,
            dp:  WinUI.RichTextBlock.TextIndentProperty)
        .OneWay(
            get: static e => e.TextLineBounds.HasValue ? e.TextLineBounds.Value : Optional<TextLineBounds>.Unset,
            set: static (c, v) => c.TextLineBounds = v,
            dp:  WinUI.RichTextBlock.TextLineBoundsProperty)
        .OneWay(
            get: static e => e.TextReadingOrder.HasValue ? e.TextReadingOrder.Value : Optional<TextReadingOrder>.Unset,
            set: static (c, v) => c.TextReadingOrder = v,
            dp:  WinUI.RichTextBlock.TextReadingOrderProperty)
        .OneWay(
            get: static e => e.IsTextScaleFactorEnabled.HasValue ? e.IsTextScaleFactorEnabled.Value : Optional<bool>.Unset,
            set: static (c, v) => c.IsTextScaleFactorEnabled = v,
            dp:  WinUI.RichTextBlock.IsTextScaleFactorEnabledProperty)
        .OneWay(
            get: static e => e.IsColorFontEnabled.HasValue ? e.IsColorFontEnabled.Value : Optional<bool>.Unset,
            set: static (c, v) => c.IsColorFontEnabled = v,
            dp:  WinUI.RichTextBlock.IsColorFontEnabledProperty)
        .OneWay(
            get: static e => e.OpticalMarginAlignment.HasValue ? e.OpticalMarginAlignment.Value : Optional<OpticalMarginAlignment>.Unset,
            set: static (c, v) => c.OpticalMarginAlignment = v,
            dp:  WinUI.RichTextBlock.OpticalMarginAlignmentProperty)
        .OneWay(
            get: static e => e.SelectionHighlightColor is null ? Optional<Microsoft.UI.Xaml.Media.SolidColorBrush>.Unset : e.SelectionHighlightColor,
            set: static (c, v) => c.SelectionHighlightColor = v,
            dp:  WinUI.RichTextBlock.SelectionHighlightColorProperty)
        // RichTextBlock.Padding maps from the standard Element.Padding modifier.
        // Use the Optional + DP path so padded -> unpadded updates clear stale
        // local padding on recycled controls.
        .OneWay(
            get: static e => e.Padding.HasValue ? e.Padding.Value : Optional<Thickness>.Unset,
            set: static (c, v) => c.Padding = v,
            dp:  WinUI.RichTextBlock.PaddingProperty);
}

/// <summary>
/// Spec 048 §7 — thin <c>new()</c>-able registration shim for
/// <see cref="RichTextBlockDescriptor"/>; touched via
/// <c>_ = Reg&lt;RichTextBlockElement, WinUI.RichTextBlock, RichTextBlockDescriptorHandler&gt;.Done</c>
/// from the <c>RichTextBlock</c> factory.
/// </summary>
internal sealed class RichTextBlockDescriptorHandler()
    : DescriptorHandler<RichTextBlockElement, WinUI.RichTextBlock>(RichTextBlockDescriptor.Descriptor);
