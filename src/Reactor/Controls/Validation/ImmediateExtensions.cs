using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Controls.Validation;

/// <summary>
/// Marker attached to a control to request keystroke-level value commit instead
/// of the WinUI default of commit-on-blur. Read by the reconciler for controls
/// that otherwise only raise their value-changed event when focus leaves.
/// </summary>
internal sealed record ImmediateValueAttached;

/// <summary>
/// Fluent extension that switches commit-on-blur controls (NumberBox today) to
/// fire their change callback on every parseable keystroke. The opt-in name
/// reflects the intent: the trap this avoids is validation lag, where a
/// disabled-while-invalid Submit is skipped by Tab navigation because the
/// previous control hadn't yet committed its new value.
/// </summary>
public static class ImmediateExtensions
{
    /// <summary>
    /// Requests that the control commit its value on every keystroke instead
    /// of on blur. Has no effect on controls whose default already fires
    /// per-keystroke (TextBox, PasswordBox, AutoSuggestBox, etc.).
    /// </summary>
    public static T Immediate<T>(this T el) where T : Element
        => (T)el.SetAttached(new ImmediateValueAttached());
}
