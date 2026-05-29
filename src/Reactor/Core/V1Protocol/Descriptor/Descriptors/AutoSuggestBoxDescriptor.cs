using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 6) — descriptor variant of the hand-coded
/// <c>MountAutoSuggestBox</c> / <c>UpdateAutoSuggestBox</c> arms in
/// <see cref="Reconciler"/>. First multi-event descriptor port that mixes
/// one controlled-DP event with two fire-only events on a single shared
/// payload.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Text</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedControlled{TPayload,TValue,TDelegate}"/>
///   with a typed <c>TextChanged</c> trampoline. The trampoline gates on
///   <c>args.Reason == UserInput</c> (matching the legacy arm's filter)
///   AND on <c>ChangeEchoSuppressor.ShouldSuppress</c> so programmatic
///   <c>Text=</c> writes never echo through <c>OnTextChanged</c>.</item>
///   <item><c>QuerySubmitted</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedEvent{TPayload,TDelegate}"/>
///   passing <c>args.QueryText</c> to the user callback.</item>
///   <item><c>SuggestionChosen</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedEvent{TPayload,TDelegate}"/>
///   passing the stringified <c>args.SelectedItem</c> (mirrors the
///   legacy arm's <c>?.ToString() ?? ""</c> coercion).</item>
///   <item><c>Suggestions</c> — one-way ItemsSource assignment when the
///   incoming array is non-empty (matches the legacy mount guard;
///   transitions to empty leave the previous ItemsSource attached, same
///   as the legacy arm).</item>
///   <item><c>PlaceholderText</c>, <c>Header</c>, <c>QueryIcon</c>,
///   <c>IsSuggestionListOpen</c> — one-way / one-way-conditional per the
///   legacy guards.</item>
/// </list></para>
///
/// <para><b>Known gaps vs. hand-coded handler:</b>
/// <list type="bullet">
///   <item><c>QueryIcon</c> is re-resolved unconditionally when present
///   (the legacy arm gates on <c>!ReferenceEquals(o.QueryIcon, n.QueryIcon)</c>;
///   the descriptor's OneWay path can't see the previous element ref,
///   so a re-render with the same IconData reference still produces a
///   single <c>ResolveIcon</c> call. Acceptable — same visual result,
///   slightly more work on each pass when QueryIcon is set).</item>
///   <item><c>Suggestions</c> transition to empty does not clear the
///   previous <c>ItemsSource</c> (mirrors the legacy mount guard's
///   gate; the legacy Update arm has the same gap by construction).</item>
/// </list></para>
/// </summary>
internal static class AutoSuggestBoxDescriptor
{
    // ── Trampolines ───────────────────────────────────────────────────
    // Each reads the live element via Reconciler.GetElementTag on every
    // fire so the same trampoline serves a pool-rented control through
    // any element identity change.

    private static readonly TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxTextChangedEventArgs>
        TextChangedTrampoline = (s, args) =>
        {
            // Mirror the legacy filter — only echo user-driven edits, not
            // programmatic Text= writes or suggestion-pick reflows.
            if (args.Reason != WinUI.AutoSuggestionBoxTextChangeReason.UserInput) return;
            var asb = (WinUI.AutoSuggestBox)s!;
            if (ChangeEchoSuppressor.ShouldSuppress(asb)) return;
            (Reconciler.GetElementTag(asb) as AutoSuggestBoxElement)
                ?.OnTextChanged?.Invoke(asb.Text);
        };

    private static readonly TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxQuerySubmittedEventArgs>
        QuerySubmittedTrampoline = (s, args) =>
            (Reconciler.GetElementTag((WinUI.AutoSuggestBox)s!) as AutoSuggestBoxElement)
                ?.OnQuerySubmitted?.Invoke(args.QueryText);

    private static readonly TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxSuggestionChosenEventArgs>
        SuggestionChosenTrampoline = (s, args) =>
            (Reconciler.GetElementTag((WinUI.AutoSuggestBox)s!) as AutoSuggestBoxElement)
                ?.OnSuggestionChosen?.Invoke(args.SelectedItem?.ToString() ?? "");

    public static readonly ControlDescriptor<AutoSuggestBoxElement, WinUI.AutoSuggestBox> Descriptor =
        new ControlDescriptor<AutoSuggestBoxElement, WinUI.AutoSuggestBox>
        {
            Children = new None<AutoSuggestBoxElement, WinUI.AutoSuggestBox>(),
            GetSetters = static e => e.Setters,
        }
        // Suggestions BEFORE Text so the items collection is in place
        // before any controlled Text echo can cross-reference it.
        .OneWayConditional(
            get:         static e => e.Suggestions,
            set:         static (c, v) => c.ItemsSource = v,
            shouldWrite: static e => e.Suggestions.Length > 0)
        .HandCodedControlled<AutoSuggestBoxEventPayload, string,
            TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxTextChangedEventArgs>>(
            get:         static e => e.Text,
            set:         static (c, v) => c.Text = v,
            readBack:    static c => c.Text,
            subscribe:   static (c, h) => c.TextChanged += h,
            callback:    static e => e.OnTextChanged,
            trampoline:  TextChangedTrampoline,
            slotIsNull:  static p => p.TextChangedTrampoline is null,
            setSlot:     static (p, h) => p.TextChangedTrampoline = h)
        .HandCodedEvent<AutoSuggestBoxEventPayload,
            TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxQuerySubmittedEventArgs>>(
            subscribe:        static (c, h) => c.QuerySubmitted += h,
            callbackPresent:  static e => e.OnQuerySubmitted,
            trampoline:       QuerySubmittedTrampoline,
            slotIsNull:       static p => p.QuerySubmittedTrampoline is null,
            setSlot:          static (p, h) => p.QuerySubmittedTrampoline = h)
        .HandCodedEvent<AutoSuggestBoxEventPayload,
            TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxSuggestionChosenEventArgs>>(
            subscribe:        static (c, h) => c.SuggestionChosen += h,
            callbackPresent:  static e => e.OnSuggestionChosen,
            trampoline:       SuggestionChosenTrampoline,
            slotIsNull:       static p => p.SuggestionChosenTrampoline is null,
            setSlot:          static (p, h) => p.SuggestionChosenTrampoline = h)
        .OneWay(
            get: static e => e.PlaceholderText ?? "",
            set: static (c, v) => c.PlaceholderText = v)
        .OneWayConditional(
            get:         static e => e.Header,
            set:         static (c, v) => c.Header = v,
            shouldWrite: static e => e.Header is not null)
        .OneWayConditional(
            get:         static e => e.QueryIcon,
            set:         static (c, v) => c.QueryIcon = Reconciler.ResolveIconForDescriptor(v),
            shouldWrite: static e => e.QueryIcon is not null)
        .OneWay(
            get: static e => e.IsSuggestionListOpen,
            set: static (c, v) => c.IsSuggestionListOpen = v);
}
