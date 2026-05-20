using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.6 — floating windows are real Reactor Windows.
//
//  Opens a top-level Reactor `ReactorWindow` with the pane mounted as its
//  root (per spec §2.6: "Do not build a mini-window primitive"). The pane
//  Content is rendered inside the floating window with the same
//  DockContext envelope (PaneState = Floating) so hooks resolve the same
//  way inside a floating pane as inside a docked one.
//
//  P2 first cut covers the open + close path. Items intentionally deferred:
//    • Tear-out gesture — lands with §2.4 drag pipeline.
//    • Custom title-bar slot via IDockAdapter.GetFloatingWindowTitleBar
//      — wires into WindowSpec.ExtendsContentIntoTitleBar + TitleBar
//      factory once the adapter contract is finalized.
//    • Multi-display clamp via DisplayArea.FindAll — needs the saved
//      bounds from layout JSON (§2.7 already reserves the floating
//      x/y/w/h slots).
//    • Deferred HWND via Border host — the synchronous-open contract
//      from §2.6 is upheld by ReactorApp.OpenWindow which mounts the
//      content before the HWND is shown.
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Opens a real Reactor window hosting a single dock pane. The returned
/// <see cref="ReactorWindow"/> closes naturally on user dismissal; when
/// closed it removes itself from the manager's tracking list (see
/// <see cref="DockFloatingTracker"/>).
/// </summary>
public static class DockFloatingWindow
{
    /// <summary>
    /// Open a floating window containing the given pane. Must be called on
    /// the UI thread.
    /// </summary>
    /// <param name="pane">The pane to host. Required.</param>
    /// <param name="title">Window title; defaults to <c>pane.Title</c> or "Floating Window".</param>
    /// <param name="width">Initial width (DIPs). Defaults to 480.</param>
    /// <param name="height">Initial height (DIPs). Defaults to 320.</param>
    /// <param name="owner">Optional owner window for owned-window semantics.</param>
    /// <returns>The opened <see cref="ReactorWindow"/>.</returns>
    public static ReactorWindow Open(
        DockableContent pane,
        string? title = null,
        double width = 480,
        double height = 320,
        ReactorWindow? owner = null)
    {
        ArgumentNullException.ThrowIfNull(pane);

        var spec = new WindowSpec
        {
            Title = title ?? (string.IsNullOrEmpty(pane.Title) ? "Floating Window" : pane.Title),
            Width = width,
            Height = height,
            Owner = owner,
        };

        // Wrap the pane content with the same DockContext envelope used
        // by the docked tree, but flag PaneState as Floating so
        // UseDockState resolves correctly inside the floating window.
        var window = ReactorApp.OpenWindow(spec, ctx => BuildFloatingRoot(pane));
        DockFloatingTracker.Register(window);
        window.Closed += (_, _) => DockFloatingTracker.Unregister(window);
        return window;
    }

    private static Element BuildFloatingRoot(DockableContent pane)
    {
        var content = pane.Content ?? (Element)new BorderElement(null);
        var info = new DockPaneInfo(pane.Key, pane.Title ?? string.Empty, pane);
        return content
            .Padding(16)
            .Provide(DockContexts.Pane, (DockPaneInfo?)info)
            .Provide(DockContexts.PaneState, DockPaneState.Floating);
    }
}

/// <summary>
/// Tracks the set of floating windows opened by the docking subsystem so
/// the manager can enumerate / close-on-unmount them.
/// </summary>
internal static class DockFloatingTracker
{
    private static readonly object _lock = new();
    private static readonly HashSet<ReactorWindow> _open = new();

    public static void Register(ReactorWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_lock) { _open.Add(window); }
    }

    public static void Unregister(ReactorWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_lock) { _open.Remove(window); }
    }

    public static int Count
    {
        get { lock (_lock) return _open.Count; }
    }

    public static IReadOnlyList<ReactorWindow> Snapshot()
    {
        lock (_lock) return _open.ToArray();
    }
}
