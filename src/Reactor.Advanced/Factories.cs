using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Reactor.Advanced.Win2D;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Windows.Foundation;

namespace Microsoft.UI.Reactor.Advanced;

/// <summary>
/// DSL factories for Reactor.Advanced. Mirrors <c>Microsoft.UI.Reactor.Factories</c>.
/// Import alongside the core Reactor factories:
/// <code>
/// using static Microsoft.UI.Reactor.Factories;
/// using static Microsoft.UI.Reactor.Advanced.Factories;
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <b>Registration model — per-library trim unit.</b> The static constructor of this class
/// registers all three Win2D handlers with <see cref="ControlRegistry"/> on first touch of any
/// factory below. The CLR guarantees precise before-first-use semantics, so the trimmer keeps
/// <c>Factories</c> (and through it, all three Win2D handler types) iff at least one factory is
/// reachable from the app entry point. Apps that never call into Reactor.Advanced have the entire
/// surface stripped; apps that touch any factory root the full set.
/// </para>
/// <para>
/// This is the recommended pattern for third-party Reactor control libraries: a single
/// <c>public static partial class Factories</c> in the library's root namespace, one static
/// constructor that registers every handler, and natural-name factory methods that mirror the
/// in-tree <c>TextBlock(...)</c> / <c>Button(...)</c> shape. For libraries with many controls
/// where per-control trim isolation matters more than ergonomics, use the per-factory
/// <c>Reg&lt;TElement, TControl, THandler&gt;</c> shim used by Reactor core (spec 048 §7) instead.
/// </para>
/// </remarks>
public static partial class Factories
{
    static Factories()
    {
        ControlRegistry.Register<Win2DCanvasElement, CanvasControl>(
            static () => new Win2DCanvasHandler());
        ControlRegistry.Register<Win2DAnimatedCanvasElement, CanvasAnimatedControl>(
            static () => new Win2DAnimatedCanvasHandler());
        ControlRegistry.Register<Win2DVirtualCanvasElement, CanvasVirtualControl>(
            static () => new Win2DVirtualCanvasHandler());
    }

    // ── Win2D — manual-invalidate canvas (CanvasControl) ─────────────

    /// <summary>
    /// Creates a manual Win2D canvas that redraws when <paramref name="redrawKey"/> changes.
    /// </summary>
    /// <param name="onDraw">UI-thread draw callback.</param>
    /// <param name="redrawKey">Opaque key used to trigger canvas invalidation.</param>
    public static Win2DCanvasElement Win2DCanvas(
        Action<CanvasDrawingSession, CanvasDrawEventArgs> onDraw,
        object? redrawKey = null) =>
        new() { OnDraw = onDraw, RedrawKey = redrawKey };

    /// <summary>
    /// Creates a manual Win2D canvas with an asynchronous resource creation callback.
    /// </summary>
    /// <param name="onDraw">UI-thread draw callback.</param>
    /// <param name="onCreateResources">Resource creation callback tracked by Win2D.</param>
    /// <param name="redrawKey">Opaque key used to trigger canvas invalidation.</param>
    public static Win2DCanvasElement Win2DCanvas(
        Action<CanvasDrawingSession, CanvasDrawEventArgs> onDraw,
        Func<CanvasControl, Task> onCreateResources,
        object? redrawKey = null) =>
        new() { OnDraw = onDraw, OnCreateResources = onCreateResources, RedrawKey = redrawKey };

    // ── Win2D — animated game-loop canvas (CanvasAnimatedControl) ────

    /// <summary>
    /// Creates an animated Win2D canvas whose update and draw callbacks run on the Win2D game thread.
    /// </summary>
    /// <remarks>
    /// See <see href="docs/guide/win2d-canvas.md#threading">the Win2D canvas threading guide</see>
    /// before touching shared state from the callbacks.
    /// </remarks>
    /// <param name="onUpdate">Per-tick update callback.</param>
    /// <param name="onDraw">Per-frame draw callback.</param>
    /// <param name="drawState">Optional user state passed to both callbacks.</param>
    /// <param name="isPaused">Whether the game loop starts paused.</param>
    public static Win2DAnimatedCanvasElement Win2DAnimatedCanvas(
        Action<CanvasAnimatedUpdateEventArgs, object?> onUpdate,
        Action<CanvasDrawingSession, CanvasAnimatedDrawEventArgs, object?> onDraw,
        object? drawState = null,
        bool isPaused = false) =>
        new() { OnUpdate = onUpdate, OnDraw = onDraw, DrawState = drawState, IsPaused = isPaused };

    // ── Win2D — virtual tiled canvas (CanvasVirtualControl) ──────────

    /// <summary>
    /// Creates a virtual Win2D canvas with the specified logical content size.
    /// </summary>
    /// <param name="onRegionDraw">Region draw callback invoked for invalidated tiles.</param>
    /// <param name="contentSize">Logical size of the virtual surface, in DIPs.</param>
    public static Win2DVirtualCanvasElement Win2DVirtualCanvas(
        Action<CanvasDrawingSession, Rect> onRegionDraw,
        Size contentSize) =>
        new() { OnRegionDraw = onRegionDraw, ContentSize = contentSize };
}
