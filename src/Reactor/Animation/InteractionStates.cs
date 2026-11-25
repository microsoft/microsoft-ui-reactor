using System.Numerics;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Animation;

/// <summary>
/// Values for a single interaction state (PointerOver, Pressed, Focused).
/// Only visual/brush properties — no layout properties (Width, Margin, etc.).
/// The record IS the boundary: adding a field is a deliberate API decision.
/// <c>Rotation</c> is in degrees, clockwise, like <c>.Rotation()</c>.
/// </summary>
public record InteractionStateValues(
    // Compositor-accelerated (zero cost during interaction)
    float? Opacity = null,
    float? Scale = null,        // Uniform scale (convenience)
    Vector3? ScaleV = null,     // Non-uniform scale
    Vector3? Translation = null,
    float? Rotation = null,
    // Direct property set (pre-cached brush swap, ~1us)
    Brush? Background = null,
    Brush? Foreground = null,
    Brush? BorderBrush = null)
{
    /// <summary>
    /// Compares solid brushes by color and opacity, so brushes rebuilt on every render compare
    /// equal (issue #1702). Other brushes compare by reference.
    /// </summary>
    public virtual bool Equals(InteractionStateValues? other)
        => ReferenceEquals(this, other)
            || (other is not null && EqualityContract == other.EqualityContract
                && Opacity == other.Opacity && Scale == other.Scale && ScaleV == other.ScaleV
                && Translation == other.Translation && Rotation == other.Rotation
                && AnimationEquality.BrushesEqual(Background, other.Background)
                && AnimationEquality.BrushesEqual(Foreground, other.Foreground)
                && AnimationEquality.BrushesEqual(BorderBrush, other.BorderBrush));

    /// <summary>Hashes the brushes' presence only, without reading the WinUI brushes.</summary>
    public override int GetHashCode()
        => HashCode.Combine(EqualityContract, Opacity, Scale, ScaleV, Translation, Rotation,
            HashCode.Combine(Background is null, Foreground is null, BorderBrush is null));
}

/// <summary>
/// Configuration for the .InteractionStates() modifier.
/// Defines visual state values for PointerOver, Pressed, and Focused states,
/// plus an optional animation curve for state transitions.
/// </summary>
public record InteractionStatesConfig(
    InteractionStateValues? PointerOver = null,
    InteractionStateValues? Pressed = null,
    InteractionStateValues? Focused = null,
    Curve? Curve = null);

/// <summary>
/// Builder for constructing InteractionStatesConfig fluently.
/// </summary>
public class InteractionStatesBuilder
{
    private InteractionStateValues? _pointerOver;
    private InteractionStateValues? _pressed;
    private InteractionStateValues? _focused;

    /// <summary>Values while the pointer is over the element. <c>rotation</c> is in degrees.</summary>
    public InteractionStatesBuilder PointerOver(
        float? opacity = null, float? scale = null, Vector3? scaleV = null,
        Vector3? translation = null, float? rotation = null,
        Brush? background = null, Brush? foreground = null, Brush? borderBrush = null)
    {
        _pointerOver = new InteractionStateValues(opacity, scale, scaleV, translation, rotation, background, foreground, borderBrush);
        return this;
    }

    /// <summary>Values while the element is pressed; unset values inherit from <see cref="PointerOver"/>. <c>rotation</c> is in degrees.</summary>
    public InteractionStatesBuilder Pressed(
        float? opacity = null, float? scale = null, Vector3? scaleV = null,
        Vector3? translation = null, float? rotation = null,
        Brush? background = null, Brush? foreground = null, Brush? borderBrush = null)
    {
        _pressed = new InteractionStateValues(opacity, scale, scaleV, translation, rotation, background, foreground, borderBrush);
        return this;
    }

    /// <summary>Values while the element has focus. <c>rotation</c> is in degrees.</summary>
    public InteractionStatesBuilder Focused(
        float? opacity = null, float? scale = null, Vector3? scaleV = null,
        Vector3? translation = null, float? rotation = null,
        Brush? background = null, Brush? foreground = null, Brush? borderBrush = null)
    {
        _focused = new InteractionStateValues(opacity, scale, scaleV, translation, rotation, background, foreground, borderBrush);
        return this;
    }

    internal InteractionStatesConfig Build() =>
        new(_pointerOver, _pressed, _focused);
}
