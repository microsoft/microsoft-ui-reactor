using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 5) — descriptor variant of the hand-coded
/// <c>MountRichEditBox</c> / <c>UpdateRichEditBox</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Text</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedControlled{TPayload,TValue,TDelegate}"/>
///   with a <c>TextChangedEventHandler</c> trampoline. RichEditBox surfaces
///   text via the document object, not a <c>Text</c> DP — the descriptor
///   writes <c>Document.SetText</c> (skipping the write when the element's
///   Text is empty, mirroring the legacy mount guard) and reads back via
///   <c>Document.GetText</c>, trimming a trailing <c>\r</c> the same way the
///   hand-coded trampoline does.</item>
///   <item><c>IsReadOnly</c>, <c>TextWrapping</c>, <c>AcceptsReturn</c> —
///   one-way (always written; the element has non-null defaults that match
///   the WinUI defaults so the unconditional writes are no-ops on identity).</item>
///   <item><c>Header</c>, <c>PlaceholderText</c>, <c>IsSpellCheckEnabled</c>,
///   <c>MaxLength</c>, <c>SelectionHighlightColor</c> — one-way conditional
///   per the legacy guards.</item>
/// </list></para>
///
/// <para><b>Known gaps vs. hand-coded handler:</b>
/// <list type="bullet">
///   <item>The <c>Text</c> read-back comparison uses the descriptor's
///   default <c>EqualityComparer&lt;string&gt;</c>; the legacy arm performs
///   no symmetric controlled-mode snap-back (see <see cref="TextBoxDescriptor"/>
///   for the same gap pattern). User edits flow into <c>OnTextChanged</c>
///   normally.</item>
///   <item><c>SelectionHighlightColor</c> reference-equality check is
///   approximated via the conditional-write predicate; the descriptor will
///   re-write an identical brush instance whereas the legacy arm short-
///   circuits on ReferenceEquals. No behavior delta on rendered output.</item>
/// </list></para>
/// </summary>
internal static class RichEditBoxDescriptor
{
    private static readonly RoutedEventHandler TextChangedTrampoline = (s, _) =>
    {
        var r = (WinUI.RichEditBox)s!;
        // RichEditBox.TextChanged on Document.SetText is asynchronous — even
        // the Mount-time SetText fires after subscription. Gate on
        // ChangeEchoSuppressor so programmatic writes (Mount AND Update) drop
        // the echo. Mirrors the PasswordBox/Slider/NumberBox idiom.
        if (ChangeEchoSuppressor.ShouldSuppress(r)) return;
        r.Document.GetText(Microsoft.UI.Text.TextGetOptions.None, out var text);
        (Reconciler.GetElementTag(r) as RichEditBoxElement)
            ?.OnTextChanged?.Invoke(text?.TrimEnd('\r') ?? "");
    };

    public static readonly ControlDescriptor<RichEditBoxElement, WinUI.RichEditBox> Descriptor =
        new ControlDescriptor<RichEditBoxElement, WinUI.RichEditBox>
        {
            Children = new None<RichEditBoxElement, WinUI.RichEditBox>(),
            GetSetters = static e => e.Setters,
        }
        // AcceptsReturn / TextWrapping BEFORE Text-document writes, mirroring
        // TextBoxDescriptor's invariant. RichEditBox doesn't strip newlines
        // on single-line, but keep the ordering canonical.
        .OneWay(
            get: static e => e.AcceptsReturn,
            set: static (c, v) => c.AcceptsReturn = v)
        .OneWay(
            get: static e => e.TextWrapping,
            set: static (c, v) => c.TextWrapping = v)
        .HandCodedControlled<RichEditBoxEventPayload, string, RoutedEventHandler>(
            get:         static e => e.Text,
            set:         static (c, v) =>
            {
                // Mount-arm guard: only write the document when the element
                // has non-empty text. SetText("") would clear an empty doc
                // but is a no-op on a freshly minted control, so the guard
                // here mostly affects Update — where re-writing the same
                // text wastes work and may reset the caret. The descriptor
                // accepts that minor delta for the bulk-port; authors who
                // need a fully controlled RichEditBox stay on the legacy arm.
                //
                // RichEditBox.Document.SetText fires TextChanged asynchronously,
                // so even the Mount-time write (which the framework does NOT
                // wrap in WriteSuppressed — bare initial write) needs a manual
                // BeginSuppress token here. The trampoline reads it via
                // ShouldSuppress so the echo never reaches OnTextChanged.
                if (!string.IsNullOrEmpty(v))
                {
                    ChangeEchoSuppressor.BeginSuppress(c);
                    c.Document.SetText(Microsoft.UI.Text.TextSetOptions.None, v);
                }
            },
            readBack:    static c =>
            {
                c.Document.GetText(Microsoft.UI.Text.TextGetOptions.None, out var text);
                return text?.TrimEnd('\r') ?? "";
            },
            subscribe:   static (c, h) => c.TextChanged += h,
            callback:    static e => e.OnTextChanged,
            trampoline:  TextChangedTrampoline,
            slotIsNull:  static p => p.TextChangedTrampoline is null,
            setSlot:     static (p, h) => p.TextChangedTrampoline = h)
        .OneWay(
            get: static e => e.IsReadOnly,
            set: static (c, v) => c.IsReadOnly = v)
        .OneWayConditional(
            get:         static e => e.Header,
            set:         static (c, v) => c.Header = v,
            shouldWrite: static e => e.Header is not null)
        .OneWayConditional(
            get:         static e => e.PlaceholderText,
            set:         static (c, v) => c.PlaceholderText = v,
            shouldWrite: static e => e.PlaceholderText is not null)
        .OneWayConditional(
            get:         static e => e.IsSpellCheckEnabled,
            set:         static (c, v) => c.IsSpellCheckEnabled = v!.Value,
            shouldWrite: static e => e.IsSpellCheckEnabled.HasValue)
        .OneWayConditional(
            get:         static e => e.MaxLength,
            set:         static (c, v) => c.MaxLength = v,
            shouldWrite: static e => e.MaxLength != 0)
        .OneWayConditional(
            get:         static e => e.SelectionHighlightColor,
            set:         static (c, v) => c.SelectionHighlightColor = v,
            shouldWrite: static e => e.SelectionHighlightColor is not null);
}
