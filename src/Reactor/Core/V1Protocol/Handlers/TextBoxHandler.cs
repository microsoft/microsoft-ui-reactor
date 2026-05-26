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
[Experimental("REACTOR_V1_PREVIEW")]
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

        // Bare Text write at mount — the TextChanged subscription below is
        // wired *after* this write on first wire, and reused on pool rents.
        // On first wire the subscription doesn't exist yet, so no echo. On
        // pool rent the trampoline already exists but the GetElementTag
        // lookup returns the new element with the new OnChanged callback;
        // the controlled-write echo is handled by Update's WriteSuppressed.
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
                if (ChangeEchoSuppressor.ShouldSuppress(tb)) return;
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

    public void Update(UpdateContext ctx, TextBoxElement oldEl, TextBoxElement newEl, WinUI.TextBox ctrl)
    {
        var bind = ctx.BindFor(ctrl, newEl);

        if (oldEl.Value != newEl.Value)
        {
            // Element value changed — always enforce.
            if (ctrl.Text != newEl.Value)
                bind.WriteSuppressed(() => ctrl.Text = newEl.Value);
        }
        else if (newEl.OnChanged is not null && ctrl.Text != newEl.Value)
        {
            // Controlled mode snap-back: callback filtered the user input back
            // to the same state. Restore the controlled value, preserve caret.
            var caret = ctrl.SelectionStart;
            bind.WriteSuppressed(() => ctrl.Text = newEl.Value);
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
