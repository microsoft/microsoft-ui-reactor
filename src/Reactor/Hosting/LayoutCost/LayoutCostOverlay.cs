using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using WColor = global::Windows.UI.Color;

namespace Microsoft.UI.Reactor.Hosting.LayoutCost;

// ADR: this file uses only Composition visuals — no TextBlock, no DrawingSurface,
// no DirectWrite. That's a load-bearing constraint: the overlay's own authored
// elements would otherwise show up in ETW layout events and pollute the
// rollups. Keep it that way. See spec §2.7 (IsOverlayChrome filter).

/// <summary>
/// The layout-cost overlay's renderer. Owns a single <see cref="ContainerVisual"/>
/// attached to the passed-in <see cref="Canvas"/>; per-Component
/// <see cref="MeterVisual"/> badges and <see cref="ComponentOutlineVisual"/>
/// subtree outlines are pooled beneath it.
/// </summary>
/// <remarks>
/// <see cref="Show"/> is the single entry point called per flush. It takes
/// the latest <see cref="ComponentSnapshot"/> list, positions each Component's
/// outline rectangle at the subtree bounds, anchors a meter badge at the
/// subtree's top-right, and hides/reaps everything else.
/// </remarks>
internal sealed class LayoutCostOverlay
{
    /// <summary>Green outline color. Alpha high enough to read clearly against app chrome.</summary>
    private static readonly WColor OutlineColor = WColor.FromArgb(200, 0x3C, 0xB0, 0x43);

    private readonly Canvas _overlayCanvas;
    private readonly ContainerVisual _parentContainer;
    private readonly Compositor _compositor;
    private readonly ContainerVisual _container;
    private readonly MeterVisualPool _pool;
    private readonly MeterBox _meterBox = new(MeterVisual.InnerWidth, MeterVisual.BarHeight);
    private readonly HashSet<ComponentIdentity> _alive = new();
    private readonly Dictionary<ComponentIdentity, ComponentOutlineVisual> _outlines = new();

    /// <summary>
    /// Ctor takes both the Canvas (for size queries) and the shared parent
    /// <see cref="ContainerVisual"/> owned by <see cref="OverlayHostWiring"/>.
    /// </summary>
    public LayoutCostOverlay(Canvas overlayCanvas, ContainerVisual parentContainer)
    {
        _overlayCanvas = overlayCanvas;
        _parentContainer = parentContainer;
        _compositor = parentContainer.Compositor;
        _container = _compositor.CreateContainerVisual();
        _parentContainer.Children.InsertAtBottom(_container);

        _pool = new MeterVisualPool(_compositor, _container);
        Debug.WriteLine($"[Reactor.LayoutCost] overlay constructed — canvas initial size = {overlayCanvas.ActualWidth}x{overlayCanvas.ActualHeight}");
    }

    /// <summary>Show badges + subtree outlines for the given snapshots.</summary>
    public void Show(IReadOnlyList<ComponentSnapshot> snapshots)
    {
        float canvasW = (float)_overlayCanvas.ActualWidth;
        float canvasH = (float)_overlayCanvas.ActualHeight;

        // If layout hasn't run yet, skip the paint — we'd have nothing
        // sensible to clip to.
        if (canvasW <= 0 || canvasH <= 0) return;

        _pool.BeginFlush();
        _alive.Clear();

        var byDepth = new List<ComponentSnapshot>(snapshots);
        byDepth.Sort((a, b) => a.Depth.CompareTo(b.Depth));

        int painted = 0, suppressed = 0;
        foreach (var s in byDepth)
        {
            _alive.Add(s.Id);
            if (s.Id.IsChrome) continue; // chrome has no visual in Components mode

            // Suppress if bounds are degenerate (Component not yet laid out
            // or below the minimum-size threshold).
            if (s.SubtreeW < MeterAnchor.MinSubtreeDimension ||
                s.SubtreeH < MeterAnchor.MinSubtreeDimension)
            {
                if (_outlines.TryGetValue(s.Id, out var deadOutline))
                    deadOutline.Hide();
                suppressed++;
                continue;
            }

            // Subtree outline: hollow green rectangle tracing the Component's bounds.
            if (!_outlines.TryGetValue(s.Id, out var outline))
            {
                outline = new ComponentOutlineVisual(_compositor, OutlineColor);
                _container.Children.InsertAtBottom(outline.Root);
                _outlines[s.Id] = outline;
            }
            outline.Show();
            outline.SetBounds(s.SubtreeX, s.SubtreeY, s.SubtreeW, s.SubtreeH);

            // Meter badge anchored at top-right (with canvas clamping).
            if (!MeterAnchor.TryComputePosition(
                s.SubtreeX, s.SubtreeY, s.SubtreeW, s.SubtreeH,
                canvasW, canvasH,
                out var x, out var y))
            {
                suppressed++;
                continue;
            }

            var meter = _pool.GetOrCreate(s.Id);
            meter.SetPosition(x, y);
            meter.UpdateFromSnapshot(s, _meterBox);
            painted++;
        }

        // Hide outlines that weren't seen this flush (Components unmounted).
        foreach (var kv in _outlines)
        {
            if (!_alive.Contains(kv.Key))
                kv.Value.Hide();
        }

        // Reap truly-dead Components.
        ReapOutlines();

        if (painted != _lastPainted || suppressed != _lastSuppressed)
        {
            Debug.WriteLine($"[Reactor.LayoutCost] Show: snapshots={snapshots.Count} painted={painted} suppressed={suppressed}");
            _lastPainted = painted;
            _lastSuppressed = suppressed;
        }

        _pool.EndFlush();
        _pool.Reap(_alive);
    }

    private int _lastPainted = -1;
    private int _lastSuppressed = -1;

    private void ReapOutlines()
    {
        if (_outlines.Count == 0) return;
        List<ComponentIdentity>? dead = null;
        foreach (var kv in _outlines)
        {
            if (!_alive.Contains(kv.Key))
                (dead ??= new()).Add(kv.Key);
        }
        if (dead is null) return;
        foreach (var id in dead)
        {
            if (_outlines.TryGetValue(id, out var v))
            {
                _container.Children.Remove(v.Root);
                v.Dispose();
                _outlines.Remove(id);
            }
        }
    }

    public int LiveMeterCount => _pool.LiveCount;

    public void Dispose()
    {
        try { _pool.Dispose(); } catch { }
        foreach (var v in _outlines.Values)
        {
            try { v.Dispose(); } catch { }
        }
        _outlines.Clear();
        try { _parentContainer.Children.Remove(_container); } catch { }
        try { _container.Dispose(); } catch { }
    }
}
