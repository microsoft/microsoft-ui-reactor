using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 3) — descriptor variant of the hand-coded
/// <c>MountText</c> / <c>UpdateText</c> arms in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a zero-event display leaf. Every prop is either
/// <see cref="ControlDescriptor{TElement,TControl}.OneWay"/> or
/// <see cref="ControlDescriptor{TElement,TControl}.OneWayConditional"/>,
/// mirroring the legacy arm's "write only when the element provides a value"
/// pattern.</para>
///
/// <para><b>Phase 1 parity note:</b> <c>Content</c> is unconditional (the
/// legacy arm writes it without a HasValue gate). All other nullable props
/// use <see cref="ControlDescriptor{TElement,TControl}.OneWayConditional"/>
/// with the same <c>HasValue</c> / <c>is not null</c> guards. <c>MaxLines</c>,
/// <c>CharacterSpacing</c> and <c>TextDecorations</c> are non-nullable on
/// the element record so they round-trip via plain
/// <see cref="ControlDescriptor{TElement,TControl}.OneWay"/>.</para>
///
/// <para><b>Known gaps vs. hand-coded handler:</b>
/// <list type="bullet">
///   <item><b>Bitmask fast-path:</b> the legacy arm has an
///   <c>EnableBitmaskDiff</c>-gated optimization (<c>UpdateTextBitmask</c>)
///   that avoids COM interop reads for unchanged props. The descriptor
///   relies on the engine's general per-prop comparer instead — same
///   write set, slightly different read pattern. No behavior delta.</item>
/// </list></para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class TextBlockDescriptor
{
    public static readonly ControlDescriptor<TextBlockElement, WinUI.TextBlock> Descriptor =
        new ControlDescriptor<TextBlockElement, WinUI.TextBlock>
        {
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.Content,
            set: static (c, v) => c.Text = v)
        .OneWayConditional(
            get:         static e => e.FontSize,
            set:         static (c, v) => c.FontSize = v!.Value,
            shouldWrite: static e => e.FontSize.HasValue)
        .OneWayConditional(
            get:         static e => e.Weight,
            set:         static (c, v) => c.FontWeight = v!.Value,
            shouldWrite: static e => e.Weight.HasValue)
        .OneWayConditional(
            get:         static e => e.FontStyle,
            set:         static (c, v) => c.FontStyle = v!.Value,
            shouldWrite: static e => e.FontStyle.HasValue)
        .OneWayConditional(
            get:         static e => e.HorizontalAlignment,
            set:         static (c, v) => c.HorizontalAlignment = v!.Value,
            shouldWrite: static e => e.HorizontalAlignment.HasValue)
        .OneWayConditional(
            get:         static e => e.TextWrapping,
            set:         static (c, v) => c.TextWrapping = v!.Value,
            shouldWrite: static e => e.TextWrapping.HasValue)
        .OneWayConditional(
            get:         static e => e.TextAlignment,
            set:         static (c, v) => c.TextAlignment = v!.Value,
            shouldWrite: static e => e.TextAlignment.HasValue)
        .OneWayConditional(
            get:         static e => e.TextTrimming,
            set:         static (c, v) => c.TextTrimming = v!.Value,
            shouldWrite: static e => e.TextTrimming.HasValue)
        .OneWayConditional(
            get:         static e => e.IsTextSelectionEnabled,
            set:         static (c, v) => c.IsTextSelectionEnabled = v!.Value,
            shouldWrite: static e => e.IsTextSelectionEnabled.HasValue)
        .OneWayConditional(
            get:         static e => e.FontFamily,
            set:         static (c, v) => c.FontFamily = v,
            shouldWrite: static e => e.FontFamily is not null)
        .OneWayConditional(
            get:         static e => e.LineHeight,
            set:         static (c, v) => c.LineHeight = v!.Value,
            shouldWrite: static e => e.LineHeight.HasValue)
        .OneWay(
            get: static e => e.MaxLines,
            set: static (c, v) => c.MaxLines = v)
        .OneWay(
            get: static e => e.CharacterSpacing,
            set: static (c, v) => c.CharacterSpacing = v)
        .OneWay(
            get: static e => e.TextDecorations,
            set: static (c, v) => c.TextDecorations = v);
}
