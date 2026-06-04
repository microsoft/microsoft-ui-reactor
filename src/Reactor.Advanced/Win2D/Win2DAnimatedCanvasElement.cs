using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Reactor.Core;
using Windows.UI;

namespace Microsoft.UI.Reactor.Advanced.Win2D;

/// <summary>
/// Reactor element for Win2D's game-loop <see cref="CanvasAnimatedControl"/>.
/// </summary>
public sealed record Win2DAnimatedCanvasElement : Element
{
    /// <summary>
    /// Per-tick update callback. Runs on the Win2D game thread; see
    /// <see href="docs/guide/win2d-canvas.md#threading">the Win2D canvas threading guide</see>.
    /// </summary>
    public Action<CanvasAnimatedUpdateEventArgs, object?>? OnUpdate { get; init; }

    /// <summary>
    /// Per-frame draw callback. Runs on the Win2D game thread; see
    /// <see href="docs/guide/win2d-canvas.md#threading">the Win2D canvas threading guide</see>.
    /// </summary>
    public Action<CanvasDrawingSession, CanvasAnimatedDrawEventArgs, object?>? OnDraw { get; init; }

    /// <summary>
    /// Optional asynchronous resource creation callback tracked before the animated loop starts.
    /// </summary>
    public Func<CanvasAnimatedControl, Task>? OnCreateResources { get; init; }

    /// <summary>
    /// Target update interval for the Win2D game loop.
    /// </summary>
    public TimeSpan TargetElapsedTime { get; init; } = TimeSpan.FromTicks(166_667);

    /// <summary>
    /// Whether the canvas simulation is paused without unmounting the control. When
    /// <c>true</c>, <see cref="OnUpdate"/> is skipped (no state advancement) but
    /// <see cref="OnDraw"/> continues to fire each tick so the last frame stays
    /// visible. The underlying Win2D game loop also continues to tick (~16 ms of
    /// empty work per pause-second).
    /// </summary>
    /// <remarks>
    /// The Reactor handler intentionally does <b>not</b> write Win2D's own
    /// <c>CanvasAnimatedControl.Paused</c> property: under WinUI 3, toggling that
    /// property wakes the game thread for exactly one tick and then permanently
    /// parks it (known Win2D-on-WinUI-3 limitation), leaving the canvas frozen
    /// with no way to resume on the same control instance. Gating <see cref="OnUpdate"/>
    /// is a reliable declarative "pause simulation". If your app needs to actually
    /// suspend the game thread (e.g. for CPU savings on a backgrounded window),
    /// use <see cref="Setters"/> to mutate <c>ctrl.Paused</c> directly and accept
    /// the one-way trip.
    /// </remarks>
    public bool IsPaused { get; init; }

    /// <summary>
    /// Optional user state passed to <see cref="OnUpdate"/> and <see cref="OnDraw"/>.
    /// </summary>
    public object? DrawState { get; init; }

    /// <summary>
    /// Background clear color applied to the underlying <see cref="CanvasAnimatedControl"/>.
    /// </summary>
    public Color ClearColor { get; init; } = new() { A = 0, R = 0, G = 0, B = 0 };

    /// <summary>
    /// Raw Win2D control setters applied after typed properties.
    /// </summary>
    public Action<CanvasAnimatedControl>[] Setters { get; init; } = Array.Empty<Action<CanvasAnimatedControl>>();

    internal Win2DAnimatedCanvasElement() { }
}
