using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Microsoft.UI.Reactor;

// Named-style fluent helpers. Spec 039 §17.
//
// Promotes frequently-used WinUI named styles or enum-property values to
// fluent helpers so the common case never requires .ApplyStyle("…") or a
// verbose init-property assignment.
public static partial class ElementExtensions
{
    // ── §17.1 Button style fluents ─────────────────────────────────────

    /// <summary>Applies the WinUI <c>AccentButtonStyle</c> — the accent-color
    /// primary-button look. Theme-aware (re-resolves on light/dark/contrast
    /// switches via the underlying static-resource lookup).</summary>
    public static ButtonElement AccentButton(this ButtonElement el) =>
        el.ApplyStyle("AccentButtonStyle");

    /// <inheritdoc cref="AccentButton(ButtonElement)" />
    public static DropDownButtonElement AccentButton(this DropDownButtonElement el) =>
        el.ApplyStyle("AccentButtonStyle");

    /// <inheritdoc cref="AccentButton(ButtonElement)" />
    public static SplitButtonElement AccentButton(this SplitButtonElement el) =>
        el.ApplyStyle("AccentButtonStyle");

    /// <inheritdoc cref="AccentButton(ButtonElement)" />
    public static ToggleSplitButtonElement AccentButton(this ToggleSplitButtonElement el) =>
        el.ApplyStyle("AccentButtonStyle");

    /// <summary>Applies the WinUI <c>SubtleButtonStyle</c> — chromeless
    /// transparent-background button look. Theme-aware.</summary>
    public static ButtonElement SubtleButton(this ButtonElement el) =>
        el.ApplyStyle("SubtleButtonStyle");

    /// <inheritdoc cref="SubtleButton(ButtonElement)" />
    public static DropDownButtonElement SubtleButton(this DropDownButtonElement el) =>
        el.ApplyStyle("SubtleButtonStyle");

    /// <inheritdoc cref="SubtleButton(ButtonElement)" />
    public static SplitButtonElement SubtleButton(this SplitButtonElement el) =>
        el.ApplyStyle("SubtleButtonStyle");

    /// <inheritdoc cref="SubtleButton(ButtonElement)" />
    public static ToggleSplitButtonElement SubtleButton(this ToggleSplitButtonElement el) =>
        el.ApplyStyle("SubtleButtonStyle");

    // ── §17.2 TextLink ─────────────────────────────────────────────────

    /// <summary>Applies the WinUI <c>TextBlockButtonStyle</c> — chromeless
    /// inline-link rendering. Use for the "Learn more" pattern inside body
    /// text. Theme-aware.</summary>
    public static HyperlinkButtonElement TextLink(this HyperlinkButtonElement el) =>
        el.ApplyStyle("TextBlockButtonStyle");

    /// <inheritdoc cref="TextLink(HyperlinkButtonElement)" />
    public static ButtonElement TextLink(this ButtonElement el) =>
        el.ApplyStyle("TextBlockButtonStyle");

    // ── §17.3 InputScope fluents ───────────────────────────────────────
    // Promotes the most-common InputScope values to fluent helpers. The
    // generic .InputScope(InputScopeNameValue) escape hatch lives below for
    // the long tail. Applied via .Set() so we don't need an init property.

    /// <summary>Sets <c>InputScope = Number</c>. Drives the soft-keyboard
    /// layout and IME hints on platforms that respect it.</summary>
    public static TextBoxElement NumericInput(this TextBoxElement el) =>
        el.InputScope(InputScopeNameValue.Number);

    /// <summary>Sets <c>InputScope = EmailSmtpAddress</c>.</summary>
    public static TextBoxElement EmailInput(this TextBoxElement el) =>
        el.InputScope(InputScopeNameValue.EmailSmtpAddress);

    /// <summary>Sets <c>InputScope = Url</c>.</summary>
    public static TextBoxElement UrlInput(this TextBoxElement el) =>
        el.InputScope(InputScopeNameValue.Url);

    /// <summary>Sets <c>InputScope = TelephoneNumber</c>.</summary>
    public static TextBoxElement PhoneInput(this TextBoxElement el) =>
        el.InputScope(InputScopeNameValue.TelephoneNumber);

    /// <summary>Sets <c>InputScope = Search</c>.</summary>
    public static TextBoxElement SearchInput(this TextBoxElement el) =>
        el.InputScope(InputScopeNameValue.Search);

    /// <summary>Sets a specific <see cref="InputScopeNameValue"/>. Escape hatch
    /// for input scopes outside the named helpers above (e.g. <c>Chat</c>,
    /// <c>FormulaNumber</c>, <c>AlphanumericFullWidth</c>).</summary>
    public static TextBoxElement InputScope(this TextBoxElement el, InputScopeNameValue scope) =>
        el.Set(tb =>
        {
            var s = new InputScope();
            s.Names.Add(new InputScopeName(scope));
            tb.InputScope = s;
        });

    // ── §17.4 InfoBar severity fluents ─────────────────────────────────

    /// <summary>Sets severity to <see cref="InfoBarSeverity.Informational"/>
    /// — neutral blue/grey skin, info icon.</summary>
    public static InfoBarElement Informational(this InfoBarElement el) =>
        el with { Severity = InfoBarSeverity.Informational };

    /// <summary>Sets severity to <see cref="InfoBarSeverity.Success"/> —
    /// green skin, check icon.</summary>
    public static InfoBarElement Success(this InfoBarElement el) =>
        el with { Severity = InfoBarSeverity.Success };

    /// <summary>Sets severity to <see cref="InfoBarSeverity.Warning"/> —
    /// yellow skin, warning icon.</summary>
    public static InfoBarElement Warning(this InfoBarElement el) =>
        el with { Severity = InfoBarSeverity.Warning };

    /// <summary>Sets severity to <see cref="InfoBarSeverity.Error"/> — red
    /// skin, error icon.</summary>
    public static InfoBarElement Error(this InfoBarElement el) =>
        el with { Severity = InfoBarSeverity.Error };
}
