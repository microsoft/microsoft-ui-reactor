using System;
using Microsoft.UI.Xaml.Controls;

namespace Reactor.External.TestControl;

/// <summary>
/// Spec 047 §14 Phase 1 (1.16) — minimal external WinUI control authored
/// outside Reactor.dll. Carries one value-bearing prop (<see cref="Caption"/>)
/// and one CLR event (<see cref="CaptionChanged"/>). The shape mirrors the
/// invariants the V1 handler protocol must cover for an external author:
/// programmatic write echo-suppression, custom-event subscription with
/// trampoline refresh, modifier pipeline, setter chain, pool rent/return.
/// </summary>
public sealed partial class MarqueeControl : UserControl
{
    private readonly TextBlock _text;

    public MarqueeControl()
    {
        _text = new TextBlock();
        Content = _text;
    }

    /// <summary>Value-bearing property. Setter fires
    /// <see cref="CaptionChanged"/> only when the value actually changes;
    /// this lets the V1 echo-suppression scope drop programmatic writes.</summary>
    public string Caption
    {
        get => _text.Text;
        set
        {
            if (_text.Text == value) return;
            _text.Text = value;
            CaptionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Fires whenever <see cref="Caption"/> actually changes
    /// (user-initiated or programmatic). The V1 handler subscribes via
    /// <c>BindFor.OnCustomEvent</c> and relies on <c>WriteSuppressed</c>
    /// to drop reentrancy on programmatic writes.</summary>
    public event EventHandler? CaptionChanged;
}
