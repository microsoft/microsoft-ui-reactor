using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting.Etw;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Microsoft.UI.Reactor.Hosting.LayoutCost;

/// <summary>
/// The attribution aggregator. Drains paired ETW events on the UI thread,
/// resolves each to an owning <see cref="ComponentIdentity"/> via the
/// <see cref="PointerMap"/> (primary) / <see cref="SpatialIndex"/> (fallback)
/// / chrome bucket (last resort), updates per-Component rollups, and closes
/// out each drain tick by folding the accumulators into the EMA and
/// snapshotting the read-side buffer.
/// </summary>
/// <remarks>
/// Thread-affine: all methods must be called on the UI thread. The one
/// exception is <see cref="IsEtwUnavailable"/>, which is only ever written
/// once at startup.
/// </remarks>
internal sealed class LayoutCostAttribution : ILayoutCostReporter
{
    private readonly LayoutEventRing _ring;
    private readonly PointerMap _pointerMap;
    private readonly SpatialIndex _spatial;

    private readonly Dictionary<ComponentIdentity, ComponentRollup> _rollups = new();
    private readonly ComponentRollup _chromeRollup;

    // Double-buffered read-side snapshot so GetSnapshot() can hand out an
    // immutable view without copying per call inside the render loop.
    private IReadOnlyList<ComponentSnapshot> _snapshot = Array.Empty<ComponentSnapshot>();

    private readonly PairedLayoutEvent[] _drainBuffer = new PairedLayoutEvent[1024];

    /// <summary>Root of the overlay's own visual subtree — events under it are ignored.</summary>
    private readonly HashSet<ulong> _overlayChromeElementIds = new();

    /// <summary>Reconciler wrapper-Border → assigned ComponentIdentity.</summary>
    private readonly Dictionary<UIElement, ComponentIdentity> _wrapperToId = new();
    private long _nextComponentId;
    private Reconciler? _boundReconciler;

    public LayoutCostAttribution(LayoutEventRing ring, PointerMap pointerMap, SpatialIndex spatial)
    {
        _ring = ring;
        _pointerMap = pointerMap;
        _spatial = spatial;
        _chromeRollup = new ComponentRollup(ComponentIdentity.Chrome, "<chrome>", depth: -1);
    }

    public long DroppedEventCount => _ring.DroppedCount;
    public bool IsEtwUnavailable { get; set; }

    public IReadOnlyList<ComponentSnapshot> GetSnapshot() => _snapshot;

    /// <summary>
    /// Register a newly mounted Component with the attribution layer. Called
    /// from the reconciler's mount path when
    /// <see cref="Core.ReactorFeatureFlags.ShowLayoutCost"/> is true.
    /// </summary>
    public ComponentRollup RegisterComponent(ComponentIdentity id, string displayName, int depth)
    {
        if (!_rollups.TryGetValue(id, out var rollup))
        {
            rollup = new ComponentRollup(id, displayName, depth);
            _rollups[id] = rollup;
        }
        return rollup;
    }

    public void UnregisterComponent(ComponentIdentity id)
    {
        _rollups.Remove(id);
        _spatial.RemoveComponent(id);
    }

    /// <summary>
    /// Subscribe to Reconciler component-lifecycle events. Each Component
    /// mount gets a fresh <see cref="ComponentIdentity"/> and a matching
    /// rollup; unmount removes them. Idempotent — unbinds any previous
    /// Reconciler first.
    /// </summary>
    public void BindReconciler(Reconciler reconciler)
    {
        UnbindReconciler();
        _boundReconciler = reconciler;
        reconciler.LayoutCostComponentMounted += OnReconcilerComponentMounted;
        reconciler.LayoutCostComponentUnmounted += OnReconcilerComponentUnmounted;

        // Back-fill rollups for Components that mounted before we bound. This
        // is the common case when ShowLayoutCost is flipped on mid-session.
        int backfilled = 0;
        foreach (var (wrapper, displayName) in reconciler.EnumerateComponentWrappers())
        {
            if (_wrapperToId.ContainsKey(wrapper)) continue;
            OnReconcilerComponentMounted(wrapper, displayName, depth: 0);
            backfilled++;
        }
        global::System.Diagnostics.Debug.WriteLine($"[Reactor.LayoutCost] BindReconciler: back-filled {backfilled} existing component wrapper(s)");
    }

    public void UnbindReconciler()
    {
        if (_boundReconciler is null) return;
        _boundReconciler.LayoutCostComponentMounted -= OnReconcilerComponentMounted;
        _boundReconciler.LayoutCostComponentUnmounted -= OnReconcilerComponentUnmounted;
        _boundReconciler = null;
        _wrapperToId.Clear();
    }

    private void OnReconcilerComponentMounted(UIElement wrapper, string displayName, int depth)
    {
        var id = new ComponentIdentity(++_nextComponentId);
        _wrapperToId[wrapper] = id;
        RegisterComponent(id, displayName, depth);
        global::System.Diagnostics.Debug.WriteLine(
            $"[Reactor.LayoutCost] +mounted {displayName}@{id.Value} depth={depth} (total={_wrapperToId.Count})");
    }

    private void OnReconcilerComponentUnmounted(UIElement wrapper)
    {
        if (_wrapperToId.Remove(wrapper, out var id))
        {
            UnregisterComponent(id);
            global::System.Diagnostics.Debug.WriteLine(
                $"[Reactor.LayoutCost] -unmounted @{id.Value} (total={_wrapperToId.Count})");
        }
    }

    /// <summary>
    /// Mark <paramref name="elementId"/> as belonging to the overlay's own
    /// visual subtree so its events never show up in anyone's rollup.
    /// </summary>
    public void MarkOverlayChrome(ulong elementId) => _overlayChromeElementIds.Add(elementId);

    public void ClearOverlayChrome() => _overlayChromeElementIds.Clear();

    /// <summary>
    /// Drain the ring buffer, attribute each event, and close out the frame
    /// (EMAs / snapshot swap). Called from the overlay wiring's low-priority
    /// flush.
    /// </summary>
    public void Drain()
    {
        int n;
        int attributedComponent = 0, attributedChrome = 0;
        while ((n = _ring.Drain(_drainBuffer)) > 0)
        {
            for (int i = 0; i < n; i++)
            {
                var before = _chromeRollup.FrameMeasureTicks + _chromeRollup.FrameArrangeTicks;
                Attribute(_drainBuffer[i]);
                var after = _chromeRollup.FrameMeasureTicks + _chromeRollup.FrameArrangeTicks;
                if (after > before) attributedChrome++;
                else attributedComponent++;
            }
        }
        if (attributedComponent + attributedChrome > 0)
        {
            global::System.Diagnostics.Debug.WriteLine(
                $"[Reactor.LayoutCost] Drain: toComponents={attributedComponent} toChrome={attributedChrome}");
        }
        CloseFrameAndSnapshot();
    }

    /// <summary>
    /// Walk the live visual tree to populate each rollup's subtree bounds
    /// and authored-element count. Gives the overlay something to paint
    /// even when ETW isn't available (the common case on non-admin dev boxes).
    /// </summary>
    /// <param name="overlayAnchor">
    /// The Canvas (or any element sharing an ancestor with the Component
    /// wrappers) whose coord space the bounds will be expressed in.
    /// </param>
    public void RefreshComponentMetricsFromVisualTree(UIElement overlayAnchor)
    {
        if (_wrapperToId.Count == 0) return;

        // Bounds-only refresh: one TransformToVisual + bounds per Component
        // wrapper. Cost is O(_wrapperToId.Count), not O(visual-tree-size),
        // so this stays cheap even when a Component renders thousands of
        // descendants (e.g. a 500-row DataGrid). The previous variant did a
        // full-tree authored-count walk on every flush which dominated the
        // UI thread under heavy reconciles.
        foreach (var kv in _wrapperToId)
        {
            var wrapper = kv.Key;
            if (!_rollups.TryGetValue(kv.Value, out var rollup)) continue;
            if (wrapper is not FrameworkElement fe) continue;
            if (fe.ActualWidth <= 0 || fe.ActualHeight <= 0)
            {
                rollup.SubtreeW = 0; rollup.SubtreeH = 0;
                continue;
            }

            // Skip wrappers that are no longer in any visual tree.
            if (VisualTreeHelper.GetParent(fe) is null && fe != overlayAnchor)
                continue;

            try
            {
                var transform = fe.TransformToVisual(overlayAnchor);
                var bounds = transform.TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
                rollup.SubtreeX = (float)bounds.X;
                rollup.SubtreeY = (float)bounds.Y;
                rollup.SubtreeW = (float)bounds.Width;
                rollup.SubtreeH = (float)bounds.Height;
                _spatial.SetComponentBounds(
                    kv.Value, rollup.Depth,
                    (float)bounds.X, (float)bounds.Y,
                    (float)bounds.Width, (float)bounds.Height);
            }
            catch
            {
                // TransformToVisual throws if `wrapper` is in a different
                // visual tree (popup/flyout). Leave last-known bounds.
            }
        }
    }

    private int _sampledMeasure, _sampledArrange;

    private void Attribute(in PairedLayoutEvent evt)
    {
        if (_overlayChromeElementIds.Contains(evt.ElementId))
            return;

        bool haveRect = evt.RectW > 0 || evt.RectH > 0;

        // For Arrange events, always refresh the spatial index with the
        // latest rect — we learn an element's actual position on every
        // Arrange pass.
        if (evt.Kind == Etw.LayoutEventKind.Arrange && haveRect)
            _spatial.RecordElementRect(evt.ElementId, evt.RectX, evt.RectY, evt.RectW, evt.RectH);

        ComponentIdentity owner;
        bool cacheHit = _pointerMap.TryGetComponent(evt.ElementId, out owner);

        // Never cache attribution based on a zero-rect Measure event — it
        // carries no placement data, so spatial lookup can't possibly succeed.
        // Fall through and try to resolve via the element's remembered
        // Arrange rect (if we've seen one for this id), else defer.
        if (!cacheHit)
        {
            float cx = evt.RectX + evt.RectW * 0.5f;
            float cy = evt.RectY + evt.RectH * 0.5f;
            bool usedRemembered = false;
            if (!haveRect)
            {
                if (_spatial.TryGetElementRect(evt.ElementId, out var remembered)
                    && (remembered.w > 0 || remembered.h > 0))
                {
                    cx = remembered.x + remembered.w * 0.5f;
                    cy = remembered.y + remembered.h * 0.5f;
                    usedRemembered = true;
                }
                else
                {
                    // No placement data anywhere — we can't attribute this
                    // event. Drop it (don't pollute the chrome bucket).
                    if (evt.Kind == Etw.LayoutEventKind.Measure && _sampledMeasure < 10)
                    {
                        _sampledMeasure++;
                        global::System.Diagnostics.Debug.WriteLine(
                            $"[Reactor.LayoutCost]   sample M: id=0x{evt.ElementId:X} rect=0 (deferred, no placement yet)");
                    }
                    return;
                }
            }

            var attributed = _spatial.AttributeByPoint(cx, cy);
            owner = attributed ?? ComponentIdentity.Chrome;

            // Only cache when we had real rect data (or used a remembered
            // Arrange rect). Otherwise the next Arrange event will retry.
            if (haveRect || usedRemembered)
                _pointerMap.RegisterElementId(evt.ElementId, owner);

            if (evt.Kind == Etw.LayoutEventKind.Arrange && _sampledArrange < 10)
            {
                _sampledArrange++;
                string ownerName = owner.IsChrome ? "<chrome>"
                    : (_rollups.TryGetValue(owner, out var or) ? or.DisplayName : "?");
                global::System.Diagnostics.Debug.WriteLine(
                    $"[Reactor.LayoutCost]   sample A: id=0x{evt.ElementId:X} rect=({evt.RectX:F0},{evt.RectY:F0} {evt.RectW:F0}x{evt.RectH:F0}) center=({cx:F0},{cy:F0}) -> {ownerName}");
            }
        }

        var rollup = owner.IsChrome
            ? _chromeRollup
            : (_rollups.TryGetValue(owner, out var r) ? r : _chromeRollup);

        if (evt.Kind == Etw.LayoutEventKind.Measure)
            rollup.FrameMeasureTicks += evt.InclusiveTicks;
        else // Arrange
        {
            rollup.FrameArrangeTicks += evt.InclusiveTicks;
            rollup.FrameRenderedCount++;
        }
    }

    private void CloseFrameAndSnapshot()
    {
        long totalMeasure = 0, totalArrange = 0;
        var list = new List<ComponentSnapshot>(_rollups.Count + 1);
        foreach (var r in _rollups.Values)
        {
            if (_spatial.ComponentBounds.TryGetValue(r.Id, out var b))
            {
                r.SubtreeX = b.x; r.SubtreeY = b.y; r.SubtreeW = b.w; r.SubtreeH = b.h;
            }
            totalMeasure += r.FrameMeasureTicks;
            totalArrange += r.FrameArrangeTicks;
            if (r.FrameMeasureTicks + r.FrameArrangeTicks > 0)
            {
                global::System.Diagnostics.Debug.WriteLine(
                    $"[Reactor.LayoutCost]   rollup '{r.DisplayName}' m={r.FrameMeasureTicks / 10_000.0:F2}ms a={r.FrameArrangeTicks / 10_000.0:F2}ms rendered={r.FrameRenderedCount} ema={r.EmaLayoutMs:F2}ms");
            }
            r.CloseFrame();
            list.Add(r.ToSnapshot());
        }
        if (_chromeRollup.FrameMeasureTicks + _chromeRollup.FrameArrangeTicks > 0)
        {
            global::System.Diagnostics.Debug.WriteLine(
                $"[Reactor.LayoutCost]   rollup <chrome> m={_chromeRollup.FrameMeasureTicks / 10_000.0:F2}ms a={_chromeRollup.FrameArrangeTicks / 10_000.0:F2}ms rendered={_chromeRollup.FrameRenderedCount}");
        }
        _chromeRollup.CloseFrame();
        list.Add(_chromeRollup.ToSnapshot());
        _snapshot = list;
    }
}
