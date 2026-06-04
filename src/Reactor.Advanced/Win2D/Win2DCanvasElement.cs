using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Reactor.Core;
using Windows.UI;

namespace Microsoft.UI.Reactor.Advanced.Win2D;

/// <summary>
/// Reactor element for Win2D's manual-invalidate <see cref="CanvasControl"/>.
/// </summary>
public sealed record Win2DCanvasElement : Element
{
    /// <summary>
    /// Draw callback invoked on the UI thread when the canvas invalidates.
    /// </summary>
    public Action<CanvasDrawingSession, CanvasDrawEventArgs>? OnDraw { get; init; }

    /// <summary>
    /// Optional asynchronous resource creation callback tracked by Win2D before the first draw.
    /// </summary>
    public Func<CanvasControl, Task>? OnCreateResources { get; init; }

    /// <summary>
    /// Background clear color applied to the underlying <see cref="CanvasControl"/>.
    /// </summary>
    public Color ClearColor { get; init; } = new() { A = 0, R = 0, G = 0, B = 0 };

    /// <summary>
    /// Opaque key that triggers <see cref="CanvasControl.Invalidate"/> when the key object changes.
    /// </summary>
    public object? RedrawKey { get; init; }

    /// <summary>
    /// Raw Win2D control setters applied after typed properties.
    /// </summary>
    public Action<CanvasControl>[] Setters { get; init; } = Array.Empty<Action<CanvasControl>>();

    internal Win2DCanvasElement() { }
}
