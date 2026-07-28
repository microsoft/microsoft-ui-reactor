using System;
using Microsoft.UI.Xaml.Controls;

namespace Reactor.External.TestControl;

/// <summary>
/// Spec 062 §14 — a value-bearing external string control wrapped by a
/// source-generated <c>[GenerateReactorWrapper]</c> +
/// <c>[WrapControlled("Text", Deferred = true)]</c> record
/// (<see cref="ExternalEchoTextBoxElement"/>), authored OUTSIDE Reactor.dll with
/// NO <c>InternalsVisibleTo</c>.
///
/// <para><c>Deferred = true</c> selects the <b>suppress-counter</b> echo channel —
/// the channel the deferred / coercing value boxes (<c>PasswordBox.Password</c>,
/// <c>AutoSuggestBox.Text</c>, <c>RichEditBox</c>) require because their change
/// event is not a synchronous, exact-comparable round-trip that the value-diff arm
/// can use. The generated trampoline for that channel gates the echo on the
/// <b>public</b> <c>ReactorBinding.ShouldSuppressEcho</c> primitive. Before that
/// primitive was public the generator emitted the <i>internal</i>
/// <c>ChangeEchoSuppressor.ShouldSuppress</c>, and this assembly (no
/// <c>InternalsVisibleTo</c> from Reactor.dll) would fail to compile with CS0122.
/// The very fact that this project compiles is the stable-ABI proof for issue #163
/// / spec 062 §14.</para>
///
/// <para>The setter notifies on a genuine change only (mirroring the trusted
/// <see cref="GaugeControl"/> shape), so the live echo round-trip fixture is
/// deterministic: a framework reconcile write happens inside <c>WriteSuppressed</c>
/// and is dropped by <c>ShouldSuppressEcho</c>; a direct user edit outside that
/// scope fires the callback exactly once.</para>
/// </summary>
public sealed partial class EchoTextBox : Control
{
    private string _text = string.Empty;

    /// <summary>Value-bearing property. Fires <see cref="TextChanged"/> only when
    /// the value actually changes (user-initiated or programmatic), so a no-op
    /// programmatic write never echoes and the suppression token is always
    /// consumed by a real change.</summary>
    public string Text
    {
        get => _text;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_text, next, StringComparison.Ordinal)) return;
            _text = next;
            TextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Fires whenever <see cref="Text"/> actually changes. The generated
    /// deferred-controlled trampoline subscribes here and gates the echo on
    /// <c>ReactorBinding.ShouldSuppressEcho</c>.</summary>
    public event EventHandler? TextChanged;
}
