using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Reactor.Core;
using Windows.Foundation;

namespace Microsoft.UI.Reactor.Advanced.Win2D;

/// <summary>
/// Reactor element for Win2D's tiled <see cref="CanvasVirtualControl"/>.
/// </summary>
public sealed record Win2DVirtualCanvasElement : Element
{
    /// <summary>
    /// Callback invoked for each invalidated virtual canvas region.
    /// </summary>
    public Action<CanvasDrawingSession, Rect>? OnRegionDraw { get; init; }

    /// <summary>
    /// Optional asynchronous resource creation callback tracked before region drawing.
    /// </summary>
    public Func<CanvasVirtualControl, Task>? OnCreateResources { get; init; }

    /// <summary>
    /// Logical size of the scrollable virtual surface, in DIPs.
    /// </summary>
    public Size ContentSize { get; init; } = new(800, 600);

    /// <summary>
    /// Regions to invalidate when the list instance changes.
    /// </summary>
    public IReadOnlyList<Rect>? InvalidateRegions { get; init; }

    /// <summary>
    /// Raw Win2D control setters applied after typed properties.
    /// </summary>
    public Action<CanvasVirtualControl>[] Setters { get; init; } = Array.Empty<Action<CanvasVirtualControl>>();

    internal Win2DVirtualCanvasElement() { }
}
