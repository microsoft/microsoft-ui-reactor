using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Controls;

/// <summary>
/// A text box that enforces a mask pattern for structured input.
/// </summary>
public sealed record MaskedTextBoxElement(
    string Value,
    Action<string>? OnChanged = null,
    string? Mask = null,
    string? Header = null,
    char Placeholder = '_') : Element
{
    /// <summary>
    /// The raw value (without mask literals and placeholders).
    /// </summary>
    public string RawValue
    {
        get
        {
            if (Mask is null) return Value;
            var engine = new MaskEngine(Mask);
            return engine.GetRawValue(Value, Placeholder);
        }
    }

    /// <summary>
    /// Whether all required positions in the mask are filled.
    /// </summary>
    public bool IsComplete
    {
        get
        {
            if (Mask is null) return true;
            var engine = new MaskEngine(Mask);
            return engine.IsComplete(Value, Placeholder);
        }
    }
}

/// <summary>
/// DSL factory for <see cref="MaskedTextBoxElement"/>.
/// </summary>
public static class MaskedTextBoxDsl
{
    /// <summary>
    /// Creates a <see cref="MaskedTextBoxElement"/> — a Reactor-original
    /// masked input control (cf. WinForms <c>MaskedTextBox</c>; WinUI ships
    /// no first-party equivalent).
    /// </summary>
    /// <remarks>
    /// Named to align with WinUI's <c>TextBox</c> and Reactor's
    /// <c>TextBox()</c> factory. The previous <c>MaskedTextField</c> name
    /// remains as an <c>[Obsolete]</c> alias on <see cref="MaskedTextFieldDsl"/>
    /// for one release. (issue #389)
    /// </remarks>
    public static MaskedTextBoxElement MaskedTextBox(
        string value,
        Action<string>? onChanged = null,
        string? mask = null,
        string? header = null,
        char placeholder = '_') =>
        new(value, onChanged, mask, header, placeholder);
}

/// <summary>
/// Deprecated DSL factory shim that forwards to <see cref="MaskedTextBoxDsl.MaskedTextBox"/>.
/// </summary>
public static class MaskedTextFieldDsl
{
    /// <summary>
    /// Deprecated forwarding alias for <see cref="MaskedTextBoxDsl.MaskedTextBox"/>.
    /// </summary>
    [global::System.Obsolete(
        "Renamed to MaskedTextBox for parity with WinUI's TextBox naming and Reactor's TextBox() factory. " +
        "MaskedTextField will be removed in the next minor release. (issue #389)",
        error: false)]
    public static MaskedTextBoxElement MaskedTextField(
        string value,
        Action<string>? onChanged = null,
        string? mask = null,
        string? header = null,
        char placeholder = '_') =>
        MaskedTextBoxDsl.MaskedTextBox(value, onChanged, mask, header, placeholder);
}
