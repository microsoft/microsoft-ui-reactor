using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

/// <summary>
/// Spec 047 §14 Phase 1 (1.13) — text + focus port. <see cref="WinUI.TextBox"/>
/// exercises:
/// <list type="bullet">
///   <item>Controlled <c>Text</c> (echo-suppressed; controlled-mode snap-back
///         on <c>TextChanged</c>).</item>
///   <item><c>SelectionChanged</c> via routed-event subscribe.</item>
///   <item>Routed <c>GotFocus</c> / <c>LostFocus</c> via the bind helper.</item>
///   <item>Pool participation (<c>TextBox</c> is in <c>PoolableTypes</c>).</item>
/// </list>
///
/// <para><b>Prop classification per spec §6.1</b> (ambition; the descriptor
/// model itself lands in Phase 4):</para>
/// <list type="bullet">
///   <item><c>Value</c> → <b>Controlled</b> (round-trips through OnChanged).</item>
///   <item><c>PlaceholderText</c>, <c>IsReadOnly</c>, <c>Header</c>,
///         <c>MaxLength</c>, <c>IsSpellCheckEnabled</c>, <c>CharacterCasing</c>,
///         <c>TextAlignment</c>, <c>Description</c> → <b>OneWay</b>.</item>
///   <item><c>AcceptsReturn</c>, <c>TextWrapping</c> → ideally <b>Initial</b>
///         (written at mount and not on update because changing them mid-life
///         is surprising for end-users), but for parity with legacy
///         <c>UpdateTextBox</c> they are treated as OneWay here. A future
///         <c>TextBoxElement</c> variant with <c>InitialText</c> would
///         validate the Initial classification; deferred to Phase 4
///         descriptor work.</item>
/// </list>
/// </summary>
internal sealed class TextBoxHandler : IElementHandler<TextBoxElement, WinUI.TextBox>
{
    // Per-control-lifetime trampolines. SelectionChanged is fully static
    // (no rerender request). TextChanged needs the rerender callback for
    // controlled-mode snap-back, so it captures one Action — but only once
    // per control (first wire); subsequent rents reuse the existing slot.
    private static readonly RoutedEventHandler SelectionChangedTrampoline = (s, _) =>
    {
        var tb = (WinUI.TextBox)s!;
        (Reconciler.GetElementTag(tb) as TextBoxElement)
            ?.OnSelectionChanged?.Invoke(tb.SelectedText, tb.SelectionStart, tb.SelectionLength);
    };

    public WinUI.TextBox Mount(MountContext ctx, TextBoxElement el)
    {
        var ctrl = ctx.RentControl<WinUI.TextBox>();

        // AcceptsReturn / TextWrapping BEFORE Text — single-line mode strips
        // embedded \r\n on the Text assignment (legacy parity).
        if (el.AcceptsReturn == true) ctrl.AcceptsReturn = true;
        if (el.TextWrapping.HasValue) ctrl.TextWrapping = el.TextWrapping.Value;

        // §8 PoC: clear any value-diff arm left on a pooled payload from a prior
        // lifecycle so it can't suppress this lifecycle's first real event. The
        // bare mount write below stays UN-armed (matches the legacy mount write,
        // which was never echo-suppressed).
        if (Reconciler.TryGetControlEventPayload<TextBoxEventPayload>(ctrl) is { } pooled)
        {
            pooled.HasExpectedEchoText = false;
            pooled.ExpectedEchoText = null;
        }

        // Bare Text write at mount — the TextChanged subscription below is
        // wired *after* this write on first wire, and reused on pool rents.
        // On first wire the subscription doesn't exist yet, so no echo. On
        // pool rent the trampoline already exists but the GetElementTag
        // lookup returns the new element with the new OnChanged callback;
        // the controlled-write echo is handled by Update's WriteTextSuppressed.
        if (ctrl.Text != el.Value)
            ctrl.Text = el.Value;

        ctrl.PlaceholderText = el.PlaceholderText ?? "";
        if (el.Header is not null) ctrl.Header = el.Header;
        if (el.IsReadOnly == true) ctrl.IsReadOnly = true;
        if (el.SelectionStart.HasValue) ctrl.SelectionStart = el.SelectionStart.Value;
        if (el.SelectionLength.HasValue) ctrl.SelectionLength = el.SelectionLength.Value;
        if (el.MaxLength != 0) ctrl.MaxLength = el.MaxLength;
        if (el.IsSpellCheckEnabled.HasValue) ctrl.IsSpellCheckEnabled = el.IsSpellCheckEnabled.Value;
        if (el.CharacterCasing != WinUI.CharacterCasing.Normal) ctrl.CharacterCasing = el.CharacterCasing;
        if (el.TextAlignment != Microsoft.UI.Xaml.TextAlignment.Left) ctrl.TextAlignment = el.TextAlignment;
        if (el.Description is not null) ctrl.Description = el.Description;

        EnsureTextBoxWiring(ctrl, el, ctx.RequestRerender);
        ctx.ApplySetters(el.Setters, ctrl);
        return ctrl;
    }

    // Gate per-control event wiring on the user actually having wired a
    // callback — matches legacy EnsureTextBoxWiring's early exit when both
    // OnChanged and OnSelectionChanged are null. Without this gate every
    // callback-less TextBox pays subscription cost legacy skips.
    private static void EnsureTextBoxWiring(WinUI.TextBox ctrl, TextBoxElement el, Action requestRerender)
    {
        if (el.OnChanged is null && el.OnSelectionChanged is null) return;
        var payload = Reconciler.GetOrCreateControlEventPayload<TextBoxEventPayload>(ctrl);
        if (el.OnChanged is not null && payload.TextChangedTrampoline is null)
        {
            payload.TextChangedTrampoline = (s, _) =>
            {
                var tb = (WinUI.TextBox)s!;
                var pl = Reconciler.TryGetControlEventPayload<TextBoxEventPayload>(tb);

                // Counter / setter-scope suppression still wins (external public
                // ReactorBinding.WriteSuppressed + ApplySetters .Set scope keep
                // the counter). Drain a matching value-diff arm on that branch
                // too so a counter-suppressed echo can't strand the arm and then
                // swallow the user's next real edit.
                if (ChangeEchoSuppressor.ShouldSuppress(tb))
                {
                    if (pl is { HasExpectedEchoText: true })
                    {
                        pl.HasExpectedEchoText = false;
                        pl.ExpectedEchoText = null;
                    }
                    return;
                }

                // §8 value-diff echo suppression (PoC): a programmatic controlled
                // write armed ExpectedEchoText. Consume the one-shot arm; if the
                // readback equals it, THIS is that echo — drop it. Otherwise the
                // text genuinely changed (e.g. real input, or WinUI coerced our
                // write) so fall through and fire the callback.
                if (pl is { HasExpectedEchoText: true })
                {
                    var expected = pl.ExpectedEchoText;
                    pl.HasExpectedEchoText = false;
                    pl.ExpectedEchoText = null;
                    if (string.Equals(tb.Text, expected, StringComparison.Ordinal)) return;
                }

                var tag = Reconciler.GetElementTag(tb) as TextBoxElement;
                tag?.OnChanged?.Invoke(tb.Text);
                // Controlled input: when OnChanged is wired, request a
                // re-render so Update can enforce the controlled value
                // (matches legacy EnsureTextBoxWiring semantics).
                if (tag?.OnChanged is not null) requestRerender();
            };
            ctrl.TextChanged += payload.TextChangedTrampoline;
        }
        if (el.OnSelectionChanged is not null && payload.SelectionChangedTrampoline is null)
        {
            payload.SelectionChangedTrampoline = SelectionChangedTrampoline;
            ctrl.SelectionChanged += SelectionChangedTrampoline;
        }
    }

    // Spec 047 §8 value-diff echo suppression (PoC). Replaces the legacy
    // counter-based bind.WriteSuppressed for the controlled `Text` write: arm
    // ExpectedEchoText with the value we're about to write, then write directly.
    // The TextChanged trampoline consumes the one-shot arm and drops the single
    // event whose readback matches (the echo). The arm is left PENDING after the
    // write (NOT cleared synchronously) because WinUI may raise TextChanged
    // deferred via the dispatcher rather than inline on the assignment — exactly
    // like the legacy counter, which also left its suppression elevated for the
    // event to consume. Stale arms are cleared on the next Mount (pool reuse).
    //
    // <paramref name="willWireTrampoline"/> is true when the caller's element has
    // OnChanged set: EnsureTextBoxWiring runs at the END of Update and wires the
    // TextChanged trampoline whenever OnChanged is non-null, so even on a
    // null→non-null transition (trampoline not yet wired at write time) we still
    // arm — and create the payload if needed — because the trampoline goes live
    // before any deferred echo is delivered. This matches the legacy counter,
    // which suppressed regardless of subscription timing. When no trampoline is
    // or will be wired (e.g. a SelectionChanged-only TextBox) we skip arming so
    // the arm can't strand with no event to drain it.
    //
    // CAVEAT (accepted PoC tradeoff): the legacy counter dropped the next
    // TextChanged regardless of readback. Value-diff only drops on an exact
    // readback match, so a write WinUI coerces (e.g. single-line mode stripping
    // embedded \r\n) is NOT echo-dropped — it surfaces as a real change. If this
    // proves problematic in practice we migrate this site back to the counter
    // (spec §8 migrate-back path).
    private static void WriteTextSuppressed(WinUI.TextBox ctrl, string value, bool willWireTrampoline)
    {
        var payload = willWireTrampoline
            ? Reconciler.GetOrCreateControlEventPayload<TextBoxEventPayload>(ctrl)
            : Reconciler.TryGetControlEventPayload<TextBoxEventPayload>(ctrl);
        if (payload is not null && (willWireTrampoline || payload.TextChangedTrampoline is not null))
        {
            payload.ExpectedEchoText = value;
            payload.HasExpectedEchoText = true;
        }
        ctrl.Text = value;
    }

    public void Update(UpdateContext ctx, TextBoxElement oldEl, TextBoxElement newEl, WinUI.TextBox ctrl)
    {
        if (oldEl.Value != newEl.Value)
        {
            // Element value changed — always enforce.
            if (ctrl.Text != newEl.Value)
                WriteTextSuppressed(ctrl, newEl.Value, newEl.OnChanged is not null);
        }
        else if (newEl.OnChanged is not null && ctrl.Text != newEl.Value)
        {
            // Controlled mode snap-back: callback filtered the user input back
            // to the same state. Restore the controlled value, preserve caret.
            var caret = ctrl.SelectionStart;
            WriteTextSuppressed(ctrl, newEl.Value, willWireTrampoline: true);
            ctrl.SelectionStart = Math.Min(caret, ctrl.Text.Length);
        }

        ctrl.PlaceholderText = newEl.PlaceholderText ?? "";
        if (newEl.Header is not null) ctrl.Header = newEl.Header;
        else if (oldEl.Header is not null) ctrl.ClearValue(WinUI.TextBox.HeaderProperty);
        if (newEl.IsReadOnly.HasValue) ctrl.IsReadOnly = newEl.IsReadOnly.Value;
        if (newEl.AcceptsReturn.HasValue) ctrl.AcceptsReturn = newEl.AcceptsReturn.Value;
        if (newEl.TextWrapping.HasValue) ctrl.TextWrapping = newEl.TextWrapping.Value;
        if (ctrl.MaxLength != newEl.MaxLength) ctrl.MaxLength = newEl.MaxLength;
        if (newEl.IsSpellCheckEnabled.HasValue && ctrl.IsSpellCheckEnabled != newEl.IsSpellCheckEnabled.Value)
            ctrl.IsSpellCheckEnabled = newEl.IsSpellCheckEnabled.Value;
        if (ctrl.CharacterCasing != newEl.CharacterCasing) ctrl.CharacterCasing = newEl.CharacterCasing;
        if (ctrl.TextAlignment != newEl.TextAlignment) ctrl.TextAlignment = newEl.TextAlignment;
        if (newEl.Description is not null) ctrl.Description = newEl.Description;
        else if (oldEl.Description is not null) ctrl.ClearValue(WinUI.TextBox.DescriptionProperty);

        // Apply selection position after text — must come after Text is set.
        if (newEl.SelectionStart.HasValue)
            ctrl.SelectionStart = Math.Min(newEl.SelectionStart.Value, ctrl.Text.Length);
        if (newEl.SelectionLength.HasValue)
            ctrl.SelectionLength = Math.Min(newEl.SelectionLength.Value, ctrl.Text.Length - ctrl.SelectionStart);

        // Lazy-wire on null→non-null callback transition.
        EnsureTextBoxWiring(ctrl, newEl, ctx.RequestRerender);

        ctx.ApplySetters(newEl.Setters, ctrl);
    }

    public ChildrenStrategy<TextBoxElement, WinUI.TextBox>? Children { get; } =
        new None<TextBoxElement, WinUI.TextBox>();
}
