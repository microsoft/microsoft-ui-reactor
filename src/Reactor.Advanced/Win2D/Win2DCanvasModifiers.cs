using Microsoft.Graphics.Canvas.UI.Xaml;
using Windows.UI;

namespace Microsoft.UI.Reactor.Advanced.Win2D;

/// <summary>
/// Fluent modifiers for Reactor.Advanced Win2D canvas elements.
/// </summary>
/// <remarks>
/// <c>ClearColor</c> intentionally remains the modifier name: C# resolves
/// <c>.ClearColor(color)</c> as an extension method call and <c>with { ClearColor = ... }</c>
/// as a property initializer, so typed overloads avoid the generic property-collision issue.
/// </remarks>
public static class Win2DCanvasModifiers
{
    /// <summary>
    /// Sets the clear color for a manual Win2D canvas.
    /// </summary>
    public static Win2DCanvasElement ClearColor(this Win2DCanvasElement el, Color color) =>
        el with { ClearColor = color };

    /// <summary>
    /// Sets the clear color for an animated Win2D canvas.
    /// </summary>
    public static Win2DAnimatedCanvasElement ClearColor(this Win2DAnimatedCanvasElement el, Color color) =>
        el with { ClearColor = color };

    /// <summary>
    /// Sets whether an animated Win2D canvas is paused.
    /// </summary>
    public static Win2DAnimatedCanvasElement Paused(this Win2DAnimatedCanvasElement el, bool paused = true) =>
        el with { IsPaused = paused };

    /// <summary>
    /// Sets the target frame rate for an animated Win2D canvas.
    /// </summary>
    public static Win2DAnimatedCanvasElement TargetFps(this Win2DAnimatedCanvasElement el, double fps)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fps);
        return el with { TargetElapsedTime = TimeSpan.FromSeconds(1.0 / fps) };
    }

    /// <summary>
    /// Adds a raw <see cref="CanvasControl"/> setter to a manual Win2D canvas.
    /// </summary>
    public static Win2DCanvasElement Set(this Win2DCanvasElement el, Action<CanvasControl> setter) =>
        el with { Setters = [.. el.Setters, setter] };

    /// <summary>
    /// Adds a raw <see cref="CanvasAnimatedControl"/> setter to an animated Win2D canvas.
    /// </summary>
    public static Win2DAnimatedCanvasElement Set(this Win2DAnimatedCanvasElement el, Action<CanvasAnimatedControl> setter) =>
        el with { Setters = [.. el.Setters, setter] };

    /// <summary>
    /// Adds a raw <see cref="CanvasVirtualControl"/> setter to a virtual Win2D canvas.
    /// </summary>
    public static Win2DVirtualCanvasElement Set(this Win2DVirtualCanvasElement el, Action<CanvasVirtualControl> setter) =>
        el with { Setters = [.. el.Setters, setter] };
}
