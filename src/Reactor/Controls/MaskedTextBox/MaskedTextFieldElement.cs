using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Controls;

/// <summary>
/// A text field that enforces a mask pattern for structured input.
/// </summary>
public sealed record MaskedTextFieldElement(
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
/// DSL factory for MaskedTextField.
/// </summary>
public static class MaskedTextFieldDsl
{
    /// <summary>
    /// Creates a <see cref="MaskedTextFieldElement"/> — a Reactor-original
    /// masked input control (cf. WinForms <c>MaskedTextBox</c>; WinUI ships
    /// no first-party equivalent).
    /// </summary>
    /// <remarks>
    /// Named for parity with the sibling <c>TextField</c> rather than
    /// <c>MaskedTextBox</c>; the deviation is deliberate and matches the
    /// <c>TextField</c> naming choice (see spec 039 §13 / §16.3). If
    /// <c>TextField</c> is ever renamed to <c>TextBox</c>, this is bundled
    /// in the same change.
    /// </remarks>
    public static MaskedTextFieldElement MaskedTextField(
        string value,
        Action<string>? onChanged = null,
        string? mask = null,
        string? header = null,
        char placeholder = '_') =>
        new(value, onChanged, mask, header, placeholder);
}
