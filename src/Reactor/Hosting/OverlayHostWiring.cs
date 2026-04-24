using System.Diagnostics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting.LayoutCost;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Single wrapper surface shared by every dev overlay (reconcile highlight,
/// layout cost, and whatever comes next). The host installs this ONE wrapper
/// when any overlay flag is on; each feature registers a sub-renderer that
/// paints into the shared <see cref="ContainerVisual"/> attached to the
/// wrapper's Canvas. Adding a new overlay is a matter of creating another
/// sub-renderer that takes (Canvas, parent ContainerVisual) — no host-side
/// wrapper wrangling, no per-feature mid-session reparent dance.
/// </summary>
/// <remarks>
/// Wrapper tree: <c>Grid</c> → <c>[ ContentControl(app content), Canvas(overlay) ]</c>.
/// The Canvas is hit-test invisible; a single Composition <see cref="ContainerVisual"/>
/// is attached via <c>SetElementChildVisual</c> and every sub-overlay adds
/// its own child container into that.
/// </remarks>
internal sealed class OverlayHostWiring : IDisposable
{
    /// <summary>Max elements to buffer per highlight list between flushes.</summary>
    private const int MaxPendingElements = 200;

    /// <summary>
    /// Minimum interval between highlight flush dispatches. Highlight draws
    /// faded sprites per reconcile pass — 80 ms is plenty.
    /// </summary>
    private const int HighlightFlushIntervalMs = 80;

    /// <summary>
    /// Minimum interval between layout-cost flush dispatches. Lower than
    /// highlight because the cost overlay tracks live bounds and we want
    /// smooth re-anchoring during drag-resize (~30 Hz).
    /// </summary>
    private const int LayoutCostFlushIntervalMs = 33;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Stopwatch _highlightCooldown = new();
    private readonly Stopwatch _layoutCostCooldown = new();

    /// <summary>
    /// Keeps the layout-cost sparkline advancing even when the app is idle
    /// (no reconciles, no layout updates). Without it, the sparkline freezes
    /// at the last-seen state and reads as "stuck" the moment the user stops
    /// interacting. Ticks at 50 ms — faster than the 100 ms bucket duration,
    /// slower than is worth paying for when nothing is happening.
    /// </summary>
    private DispatcherQueueTimer? _idleTicker;

    // Wrapper + canvas + shared container — all lazy so a host that never
    // turns on any overlay flag pays only for the field reads.
    private Grid? _wrapperRoot;
    private Canvas? _overlayCanvas;
    private ContainerVisual? _rootContainer;
    private Compositor? _compositor;

    // Reconcile-highlight state
    private ReconcileHighlightOverlay? _highlightOverlay;
    private bool _highlightFlushPending;
    private List<UIElement>? _pendingMounted;
    private List<UIElement>? _pendingModified;

    // Layout-cost state
    private LayoutCostAttribution? _attribution;
    private LayoutCostOverlay? _layoutCostOverlay;
    private bool _layoutCostFlushPending;

    public OverlayHostWiring(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public Grid? WrapperRoot => _wrapperRoot;

    /// <summary>
    /// Install <paramref name="newControl"/> into the shared wrapper Grid.
    /// The wrapper + Canvas are created on first call and reused forever
    /// after; repeat calls only swap the ContentControl's Content.
    /// </summary>
    public Grid SetContentViaWrapper(UIElement? newControl)
    {
        if (_wrapperRoot is null)
        {
            _overlayCanvas = new Canvas
            {
                IsHitTestVisible = false,
            };
            LayoutCostOverlayAttached.SetIsOverlayChrome(_overlayCanvas, true);

            _wrapperRoot = new Grid();
            _wrapperRoot.Children.Add(new ContentControl
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            });
            _wrapperRoot.Children.Add(_overlayCanvas);

            // LayoutUpdated fires whenever *any* layout pass in this visual
            // tree completes — scrolls, window resizes, animated property
            // settling, etc. Tie overlay flushes to it so meter positions
            // always track current bounds. Throttled by the 80 ms cooldown
            // so storms don't overwhelm the dispatcher.
            _wrapperRoot.LayoutUpdated += (_, _) =>
            {
                if (ReactorFeatureFlags.HighlightReconcileChanges)
                {
                    // Highlight doesn't re-snapshot on layout; it draws
                    // per-element sprites on reconcile only. No action.
                }
                if (ReactorFeatureFlags.ShowLayoutCost)
                    ScheduleLayoutCostFlush();
            };
        }

        var slot = (ContentControl)_wrapperRoot.Children[0];
        slot.Content = newControl;
        return _wrapperRoot;
    }

    /// <summary>
    /// Swap the content slot to an error panel. Returns true if the wrapper
    /// was active and took the panel, false to let the host fall back.
    /// </summary>
    public bool TryShowErrorInWrapper(UIElement errorPanel)
    {
        if (_wrapperRoot is null) return false;
        ((ContentControl)_wrapperRoot.Children[0]).Content = errorPanel;
        return true;
    }

    /// <summary>Bind the attribution aggregator so <see cref="ScheduleLayoutCostFlush"/> can drain it.</summary>
    public void AttachLayoutCostAttribution(LayoutCostAttribution attribution)
    {
        _attribution = attribution;
        EnsureIdleTicker();
    }

    private void EnsureIdleTicker()
    {
        if (_idleTicker is not null) return;
        _idleTicker = _dispatcherQueue.CreateTimer();
        _idleTicker.Interval = TimeSpan.FromMilliseconds(50);
        _idleTicker.IsRepeating = true;
        _idleTicker.Tick += (_, _) =>
        {
            if (ReactorFeatureFlags.ShowLayoutCost)
                ScheduleLayoutCostFlush();
        };
        _idleTicker.Start();
    }

    // ───────────────────────────────────────────────────────────────────────
    // Reconcile-highlight scheduling
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Snapshot the reconciler's highlight lists and schedule a post-layout
    /// flush. Caps pending lists and throttles flush frequency to stay
    /// responsive under high cadence.
    /// </summary>
    public void ScheduleHighlightFlush(Reconciler reconciler)
    {
        if (!ReactorFeatureFlags.HighlightReconcileChanges) return;
        if (reconciler.LastMountedElements.Count == 0 && reconciler.LastModifiedElements.Count == 0) return;

        if (reconciler.LastMountedElements.Count > 0)
        {
            _pendingMounted ??= new(Math.Min(reconciler.LastMountedElements.Count, MaxPendingElements));
            int room = MaxPendingElements - _pendingMounted.Count;
            if (room > 0)
            {
                int take = Math.Min(reconciler.LastMountedElements.Count, room);
                for (int i = 0; i < take; i++)
                    _pendingMounted.Add(reconciler.LastMountedElements[i]);
            }
        }
        if (reconciler.LastModifiedElements.Count > 0)
        {
            _pendingModified ??= new(Math.Min(reconciler.LastModifiedElements.Count, MaxPendingElements));
            int room = MaxPendingElements - _pendingModified.Count;
            if (room > 0)
            {
                int take = Math.Min(reconciler.LastModifiedElements.Count, room);
                for (int i = 0; i < take; i++)
                    _pendingModified.Add(reconciler.LastModifiedElements[i]);
            }
        }

        if (_highlightFlushPending) return;
        if (_highlightCooldown.IsRunning && _highlightCooldown.ElapsedMilliseconds < HighlightFlushIntervalMs) return;

        _highlightFlushPending = true;
        if (!_dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, FlushHighlight))
        {
            _highlightFlushPending = false;
            _pendingMounted = null;
            _pendingModified = null;
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    // Layout-cost scheduling
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>Drain attribution + repaint the layout-cost overlay. Throttled.</summary>
    public void ScheduleLayoutCostFlush()
    {
        if (!ReactorFeatureFlags.ShowLayoutCost) return;
        if (_attribution is null) return;
        if (_layoutCostFlushPending) return;
        if (_layoutCostCooldown.IsRunning && _layoutCostCooldown.ElapsedMilliseconds < LayoutCostFlushIntervalMs)
            return;

        _layoutCostFlushPending = true;
        if (!_dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, FlushLayoutCost))
            _layoutCostFlushPending = false;
    }

    // ───────────────────────────────────────────────────────────────────────
    // Internal: flush handlers
    // ───────────────────────────────────────────────────────────────────────

    private void FlushHighlight()
    {
        _highlightFlushPending = false;
        _highlightCooldown.Restart();

        if (_overlayCanvas is null) { _pendingMounted = null; _pendingModified = null; return; }
        if (_pendingMounted is null && _pendingModified is null) return;
        if (!EnsureRootContainer()) return;

        _highlightOverlay ??= new ReconcileHighlightOverlay(_overlayCanvas, _rootContainer!);

        var mounted = _pendingMounted;
        var modified = _pendingModified;
        _pendingMounted = null;
        _pendingModified = null;

        if ((mounted is null || mounted.Count == 0) && (modified is null || modified.Count == 0)) return;

        _highlightOverlay.Show(
            _overlayCanvas,
            mounted ?? (IReadOnlyList<UIElement>)Array.Empty<UIElement>(),
            modified ?? (IReadOnlyList<UIElement>)Array.Empty<UIElement>());
    }

    private void FlushLayoutCost()
    {
        _layoutCostFlushPending = false;
        _layoutCostCooldown.Restart();

        if (_overlayCanvas is null || _attribution is null) return;
        if (!EnsureRootContainer()) return;

        // Walk the live visual tree BEFORE draining so per-Component bounds +
        // authored counts land on the rollups. Drain() then snapshots them
        // alongside any ETW-derived layoutMs / renderedCount (when ETW is
        // available) or with zeros (when it isn't).
        _attribution.RefreshComponentMetricsFromVisualTree(_overlayCanvas);
        _attribution.Drain();
        var snapshot = _attribution.GetSnapshot();

        if (_layoutCostOverlay is null)
        {
            _layoutCostOverlay = new LayoutCostOverlay(_overlayCanvas, _rootContainer!);
            Debug.WriteLine($"[Reactor.LayoutCost] Flush: constructed overlay; canvas={_overlayCanvas.ActualWidth}x{_overlayCanvas.ActualHeight}, snapshots={snapshot.Count}");
        }
        _layoutCostOverlay.Show(snapshot);
    }

    private bool EnsureRootContainer()
    {
        if (_rootContainer is not null) return true;
        if (_overlayCanvas is null) return false;

        var hostVisual = ElementCompositionPreview.GetElementVisual(_overlayCanvas);
        _compositor = hostVisual.Compositor;
        _rootContainer = _compositor.CreateContainerVisual();
        ElementCompositionPreview.SetElementChildVisual(_overlayCanvas, _rootContainer);
        return true;
    }

    public void Dispose()
    {
        try { _idleTicker?.Stop(); } catch { }
        _idleTicker = null;

        try { _highlightOverlay?.Dispose(); } catch { }
        _highlightOverlay = null;
        try { _layoutCostOverlay?.Dispose(); } catch { }
        _layoutCostOverlay = null;

        if (_overlayCanvas is not null && _rootContainer is not null)
        {
            try { ElementCompositionPreview.SetElementChildVisual(_overlayCanvas, null); } catch { }
        }
        try { _rootContainer?.Dispose(); } catch { }
        _rootContainer = null;
        _compositor = null;
        _overlayCanvas = null;
        _wrapperRoot = null;
        _pendingMounted = null;
        _pendingModified = null;
        _attribution = null;
    }
}
