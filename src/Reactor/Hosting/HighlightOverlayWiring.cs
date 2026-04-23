using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Shared wiring for the reconcile-highlight overlay used by both
/// <see cref="ReactorHost"/> and <see cref="ReactorHostControl"/>.
/// Encapsulates the wrapper Grid (content slot + overlay Canvas),
/// snapshot-based scheduling, and the post-layout flush callback.
/// </summary>
internal sealed class HighlightOverlayWiring
{
    private readonly DispatcherQueue _dispatcherQueue;
    private Grid? _wrapperRoot;
    private Canvas? _overlayCanvas;
    private ReconcileHighlightOverlay? _overlay;
    private bool _flushPending;
    private List<UIElement>? _pendingMounted;
    private List<UIElement>? _pendingModified;

    public HighlightOverlayWiring(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    /// <summary>
    /// The wrapper Grid that holds both the content slot and the overlay Canvas.
    /// Created lazily by <see cref="SetContentViaWrapper"/>.
    /// </summary>
    public Grid? WrapperRoot => _wrapperRoot;

    /// <summary>
    /// Installs <paramref name="newControl"/> into a wrapper Grid that overlays
    /// a hit-test-invisible Canvas on top. The wrapper is created once; subsequent
    /// calls only swap the content slot. Returns the wrapper root (for the host
    /// to set as its Content / window.Content).
    /// </summary>
    public Grid SetContentViaWrapper(UIElement? newControl)
    {
        if (_wrapperRoot is null)
        {
            _overlayCanvas = new Canvas
            {
                IsHitTestVisible = false,
            };
            _wrapperRoot = new Grid();
            _wrapperRoot.Children.Add(new ContentControl
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            });
            _wrapperRoot.Children.Add(_overlayCanvas);
        }

        var slot = (ContentControl)_wrapperRoot.Children[0];
        slot.Content = newControl;
        return _wrapperRoot;
    }

    /// <summary>
    /// Snapshots the reconciler's highlight lists and schedules a low-priority
    /// flush so the overlay renders after layout completes.
    /// </summary>
    public void ScheduleHighlightFlush(Reconciler reconciler)
    {
        if (!ReactorFeatureFlags.HighlightReconcileChanges) return;
        if (reconciler.LastMountedElements.Count == 0 && reconciler.LastModifiedElements.Count == 0) return;

        // Snapshot now — the reconciler clears these lists at the start of the next pass.
        (_pendingMounted ??= new(reconciler.LastMountedElements.Count))
            .AddRange(reconciler.LastMountedElements);
        (_pendingModified ??= new(reconciler.LastModifiedElements.Count))
            .AddRange(reconciler.LastModifiedElements);

        if (!_flushPending)
        {
            _flushPending = true;
            _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, Flush);
        }
    }

    /// <summary>
    /// Swaps the content slot of the wrapper to show an error panel.
    /// Returns true if the wrapper was active and handled it; false if the
    /// host should fall back to its normal error path.
    /// </summary>
    public bool TryShowErrorInWrapper(UIElement errorPanel)
    {
        if (_wrapperRoot is null) return false;
        ((ContentControl)_wrapperRoot.Children[0]).Content = errorPanel;
        return true;
    }

    public void Dispose()
    {
        _overlay = null;
        _overlayCanvas = null;
        _wrapperRoot = null;
        _pendingMounted = null;
        _pendingModified = null;
    }

    private void Flush()
    {
        _flushPending = false;
        if (_overlayCanvas is null) return;
        if (_pendingMounted is null && _pendingModified is null) return;

        _overlay ??= new ReconcileHighlightOverlay(_overlayCanvas);

        var mounted = _pendingMounted;
        var modified = _pendingModified;
        _pendingMounted = null;
        _pendingModified = null;

        if ((mounted is null || mounted.Count == 0) && (modified is null || modified.Count == 0)) return;

        _overlay.Show(
            _overlayCanvas,
            mounted ?? (IReadOnlyList<UIElement>)Array.Empty<UIElement>(),
            modified ?? (IReadOnlyList<UIElement>)Array.Empty<UIElement>());
    }
}
