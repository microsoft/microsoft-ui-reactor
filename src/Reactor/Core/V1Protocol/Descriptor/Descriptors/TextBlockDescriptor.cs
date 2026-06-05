using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 3) — descriptor variant of the hand-coded
/// <c>MountText</c> / <c>UpdateText</c> arms in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a zero-event display leaf. Every nullable
/// styling prop is routed through the
/// <c>ControlDescriptor.OneWay(get, set, dp)</c>
/// overload (the <see cref="Optional{T}"/> + ClearValue path) so that a
/// transition from "set" to "unset" on a recycled control calls
/// <see cref="DependencyObject.ClearValue(DependencyProperty)"/> and the
/// prop falls back to the theme default. This is the fix for
/// https://github.com/microsoft/microsoft-ui-reactor/issues/522 — without
/// the ClearValue arm, e.g. transitioning <c>Heading → TextBlock</c>
/// would leave the prior <c>FontSize=28</c> / <c>Weight=700</c> on the
/// recycled control, bleeding into the plain render.</para>
///
/// <para><b>Phase 1 parity note:</b> <c>Content</c> is unconditional (the
/// legacy arm writes it without a HasValue gate). All other nullable props
/// adapt the element's <c>T?</c> field to <see cref="Optional{T}"/> at the
/// descriptor edge — the public element-record shape is unchanged.
/// <c>MaxLines</c>, <c>CharacterSpacing</c> and <c>TextDecorations</c>
/// are non-nullable on the element record so they round-trip via plain
/// <c>ControlDescriptor.OneWay</c>.</para>
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
        .OneWay(
            get: static e => e.FontSize.HasValue ? e.FontSize.Value : Optional<double>.Unset,
            set: static (c, v) => c.FontSize = v,
            dp:  WinUI.TextBlock.FontSizeProperty)
        .OneWay(
            get: static e => e.Weight.HasValue ? e.Weight.Value : Optional<global::Windows.UI.Text.FontWeight>.Unset,
            set: static (c, v) => c.FontWeight = v,
            dp:  WinUI.TextBlock.FontWeightProperty)
        .OneWay(
            get: static e => e.FontStyle.HasValue ? e.FontStyle.Value : Optional<global::Windows.UI.Text.FontStyle>.Unset,
            set: static (c, v) => c.FontStyle = v,
            dp:  WinUI.TextBlock.FontStyleProperty)
        .OneWay(
            get: static e => e.HorizontalAlignment.HasValue ? e.HorizontalAlignment.Value : Optional<HorizontalAlignment>.Unset,
            set: static (c, v) => c.HorizontalAlignment = v,
            dp:  FrameworkElement.HorizontalAlignmentProperty)
        .OneWay(
            get: static e => e.TextWrapping.HasValue ? e.TextWrapping.Value : Optional<TextWrapping>.Unset,
            set: static (c, v) => c.TextWrapping = v,
            dp:  WinUI.TextBlock.TextWrappingProperty)
        .OneWay(
            get: static e => e.TextAlignment.HasValue ? e.TextAlignment.Value : Optional<TextAlignment>.Unset,
            set: static (c, v) => c.TextAlignment = v,
            dp:  WinUI.TextBlock.TextAlignmentProperty)
        .OneWay(
            get: static e => e.TextTrimming.HasValue ? e.TextTrimming.Value : Optional<TextTrimming>.Unset,
            set: static (c, v) => c.TextTrimming = v,
            dp:  WinUI.TextBlock.TextTrimmingProperty)
        .OneWay(
            get: static e => e.IsTextSelectionEnabled.HasValue ? e.IsTextSelectionEnabled.Value : Optional<bool>.Unset,
            set: static (c, v) => c.IsTextSelectionEnabled = v,
            dp:  WinUI.TextBlock.IsTextSelectionEnabledProperty)
        .OneWay(
            get: static e => e.FontFamily is null ? Optional<Microsoft.UI.Xaml.Media.FontFamily>.Unset : e.FontFamily,
            set: static (c, v) => c.FontFamily = v,
            dp:  WinUI.TextBlock.FontFamilyProperty)
        .OneWay(
            get: static e => e.LineHeight.HasValue ? e.LineHeight.Value : Optional<double>.Unset,
            set: static (c, v) => c.LineHeight = v,
            dp:  WinUI.TextBlock.LineHeightProperty)
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

/// <summary>
/// Spec 048 §7 — thin <c>new()</c>-able registration shim that lets the
/// <see cref="Reg{TElement,TControl,THandler}"/> mechanism register the
/// descriptor-backed <see cref="TextBlockDescriptor"/> the same way it
/// registers a hand-coded handler. Touched once per process via
/// <c>_ = Reg&lt;TextBlockElement, WinUI.TextBlock, TextBlockDescriptorHandler&gt;.Done</c>
/// from the <c>TextBlock</c> / <c>Heading</c> / <c>SubHeading</c> / <c>Caption</c>
/// factories.
/// </summary>
internal sealed class TextBlockDescriptorHandler()
    : DescriptorHandler<TextBlockElement, WinUI.TextBlock>(TextBlockDescriptor.Descriptor);
