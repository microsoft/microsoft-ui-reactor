using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;

namespace Reactor.External.TestControl;

/// <summary>
/// Issue #206 — a Slider-shaped external WinUI control authored outside
/// Microsoft.UI.Reactor.dll. Carries one numeric value-bearing prop (<see cref="Value"/>)
/// and one CLR change event (<see cref="ValueChanged"/>). It mirrors the
/// canonical value-bearing shape the issue calls out (<c>Slider.Value</c> /
/// <c>NumberBox.Value</c>): a programmatic write to <see cref="Value"/> raises
/// <see cref="ValueChanged"/> exactly as a user edit would, so the V1 handler
/// must suppress its own write echo through the public
/// <c>ReactorBinding.WriteSuppressed</c> primitive.
/// </summary>
public sealed partial class GaugeControl : UserControl
{
    private readonly TextBlock _text;
    private double _value;

    public GaugeControl()
    {
        _text = new TextBlock();
        Content = _text;
    }

    /// <summary>Numeric value-bearing property. The setter fires
    /// <see cref="ValueChanged"/> only when the value actually changes,
    /// so a no-op programmatic write never echoes and the suppression token
    /// is always consumed by a real change.</summary>
    public double Value
    {
        get => _value;
        set
        {
            // EqualityComparer (not ==) so NaN == NaN holds and a repeated
            // NaN write is a genuine no-op rather than a phantom change.
            if (EqualityComparer<double>.Default.Equals(_value, value)) return;
            _value = value;
            _text.Text = value.ToString();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Fires whenever <see cref="Value"/> actually changes
    /// (user-initiated or programmatic). The V1 handler subscribes via
    /// <c>BindFor.OnCustomEvent</c> and relies on <c>WriteSuppressed</c>
    /// to drop the echo from its own programmatic writes.</summary>
    public event EventHandler? ValueChanged;
}
