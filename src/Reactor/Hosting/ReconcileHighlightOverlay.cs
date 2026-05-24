using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Draws solid-color overlay rectangles over UIElements that were mounted (red)
/// or modified (yellow) during a reconcile pass. Uses the Composition visual
/// layer to avoid creating XAML elements (which would themselves show up as
/// reconcile churn). One sprite per live target — repeat hits within
/// <see cref="DefaultHoldDurationMs"/> refresh the existing sprite (geometry +
/// brush) and restart its expiry timer instead of stacking duplicates. Sprites
/// snap on/off via direct Opacity assignment + a one-shot
/// <see cref="DispatcherQueueTimer"/>; no Composition animation is involved.
/// </summary>
/// <remarks>
/// Issue #167 — sub-1.0 opacity sprites with a tiled <c>LinearGradientBrush</c>
/// (transparent gradient stops, <c>ExtendMode=Wrap</c>, rotated
/// <c>TransformMatrix</c>) cause a persistent ~50%-blend-with-white wash on
/// nearby content (e.g. chart slice paths) until the next layout pass. The
/// previous design used such a brush to paint the diagonal stripe pattern.
/// Empirical bisect confirmed the gradient brush itself is the trigger:
/// solid <c>CompositionColorBrush</c> at any opacity (1.0 or 0.33) clears
/// the bug; the gradient brush at 0.33 reproduces it. This file uses solid
/// color brushes only. Distinguishability stays via color (red vs yellow).
/// See https://github.com/microsoft/microsoft-ui-reactor/issues/167 for
/// the full repro investigation and bisect data.
/// </remarks>
internal sealed class ReconcileHighlightOverlay : IDisposable
{
    private const float MountedOpacity = 0.33f;
    private const float ModifiedOpacity = 0.33f;
    private const int DefaultHoldDurationMs = 600;

    /// <summary>
    /// Test-only override for the hold duration (ms). Set before constructing
    /// an overlay to make lifecycle tests run against a tighter window. Null
    /// (default) means use <see cref="DefaultHoldDurationMs"/>.
    /// </summary>
    internal static int? TestHoldDurationOverrideMs { get; set; }

    /// <summary>Max NEW sprites to add per flush call (refreshes don't count).</summary>
    private const int MaxSpritesPerFlush = 200;

    /// <summary>Max live sprites in the container — skip adding new ones if exceeded.</summary>
    private const int MaxLiveSprites = 500;

    private static readonly global::Windows.UI.Color MountedColor =
        global::Windows.UI.Color.FromArgb(255, 220, 40, 40);   // red — mounted
    private static readonly global::Windows.UI.Color ModifiedColor =
        global::Windows.UI.Color.FromArgb(255, 240, 200, 20);  // yellow — modified

    private readonly Canvas _overlayCanvas;
    private readonly ContainerVisual _parentContainer;
    private readonly Compositor _compositor;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ContainerVisual _container;
    private readonly Dictionary<UIElement, ActiveHighlight> _active = new();
    private readonly int _holdDurationMs;
    private CompositionBrush? _mountedBrush;
    private CompositionBrush? _modifiedBrush;

    private sealed class ActiveHighlight
    {
        public SpriteVisual Sprite = default!;
        public DispatcherQueueTimer Timer = default!;
    }

    /// <summary>
    /// Ctor takes both the Canvas (for hit-testing / size queries) and the
    /// shared parent <see cref="ContainerVisual"/> owned by
    /// <see cref="OverlayHostWiring"/>. This overlay creates its own
    /// sub-container as a child of <paramref name="parentContainer"/>, so
    /// every dev overlay can paint into the same Canvas without fighting
    /// for the single <c>SetElementChildVisual</c> slot.
    /// </summary>
    public ReconcileHighlightOverlay(Canvas overlayCanvas, ContainerVisual parentContainer)
    {
        _overlayCanvas = overlayCanvas;
        _parentContainer = parentContainer;
        _compositor = parentContainer.Compositor;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "ReconcileHighlightOverlay must be constructed on a UI thread with a DispatcherQueue.");
        _container = _compositor.CreateContainerVisual();
        _parentContainer.Children.InsertAtTop(_container);
        _holdDurationMs = TestHoldDurationOverrideMs ?? DefaultHoldDurationMs;
    }

    /// <summary>
    /// Shows highlight overlays for the given mounted/modified elements.
    /// Positions are computed relative to <paramref name="host"/>.
    /// Call this from a post-layout callback so elements have final bounds.
    /// </summary>
    public void Show(
        UIElement host,
        IReadOnlyList<UIElement> mounted,
        IReadOnlyList<UIElement> modified)
    {
        _mountedBrush ??= _compositor.CreateColorBrush(MountedColor);
        _modifiedBrush ??= _compositor.CreateColorBrush(ModifiedColor);

        int newBudget = MaxSpritesPerFlush;

        for (int i = 0; i < mounted.Count; i++)
            RefreshOrAdd(host, mounted[i], _mountedBrush, MountedOpacity, ref newBudget);

        for (int i = 0; i < modified.Count; i++)
            RefreshOrAdd(host, modified[i], _modifiedBrush, ModifiedOpacity, ref newBudget);
    }

    private void RefreshOrAdd(UIElement host, UIElement target, CompositionBrush brush,
        float opacity, ref int newBudget)
    {
        if (target is not FrameworkElement fe) return;
        if (fe.ActualWidth <= 0 || fe.ActualHeight <= 0) return;

        Vector2 size;
        Vector3 offset;
        try
        {
            var transform = target.TransformToVisual(host);
            var pos = transform.TransformPoint(default);
            size = new Vector2((float)fe.ActualWidth, (float)fe.ActualHeight);
            offset = new Vector3((float)pos.X, (float)pos.Y, 0);
        }
        catch (ArgumentException)
        {
            // TransformToVisual throws if target is in a different visual tree (popup/flyout)
            return;
        }

        // Refresh path: same target already has a live sprite — update geometry/brush
        // and restart the expiry timer. No new SpriteVisual, no stacking.
        if (_active.TryGetValue(target, out var existing))
        {
            existing.Sprite.Brush = brush;
            existing.Sprite.Opacity = opacity;
            existing.Sprite.Size = size;
            existing.Sprite.Offset = offset;
            // Dispatcher-queue contract: DispatcherQueueTimer.Stop() does NOT
            // dequeue a Tick that was already enqueued before the call. If the
            // original interval elapsed concurrently with this refresh, the old
            // Tick can fire AFTER Start() and tear down a sprite that the user
            // still expects to see. Swap in a fresh timer with its own Tick
            // lambda so the old tick — if it fires — finds the active entry
            // no longer owns it, and bails (see the identity check below).
            try { existing.Timer.Stop(); } catch { }
            var refreshedTimer = CreateExpiryTimer(target, existing);
            existing.Timer = refreshedTimer;
            refreshedTimer.Start();
            return;
        }

        // New sprite path — gated by per-flush and global caps.
        if (newBudget <= 0) return;
        if (_container.Children.Count >= MaxLiveSprites) return;

        var sprite = _compositor.CreateSpriteVisual();
        sprite.Size = size;
        sprite.Offset = offset;
        sprite.Opacity = opacity;
        sprite.Brush = brush;
        _container.Children.InsertAtTop(sprite);

        var entry = new ActiveHighlight { Sprite = sprite };
        var timer = CreateExpiryTimer(target, entry);
        entry.Timer = timer;
        _active[target] = entry;
        timer.Start();
        newBudget--;
    }

    private DispatcherQueueTimer CreateExpiryTimer(UIElement capturedTarget, ActiveHighlight owner)
    {
        var timer = _dispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(_holdDurationMs);
        timer.IsRepeating = false;

        var container = _container;
        timer.Tick += (s, _) =>
        {
            try
            {
                // Identity guard: a Refresh between this tick's enqueue and its
                // dispatch will have swapped in a new timer; the stale tick must
                // not tear down the sprite that the new timer still owns.
                if (_active.TryGetValue(capturedTarget, out var ah)
                    && ReferenceEquals(ah, owner)
                    && ReferenceEquals(ah.Timer, s))
                {
                    try { container.Children.Remove(ah.Sprite); } catch { }
                    try { ah.Sprite.Dispose(); } catch { }
                    _active.Remove(capturedTarget);
                }
            }
            finally
            {
                try { ((DispatcherQueueTimer)s).Stop(); } catch { }
            }
        };
        return timer;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test-only accessors (gated by InternalsVisibleTo on Reactor.AppTests.Host)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Test-only: live sprite count in the overlay container.</summary>
    internal int LiveSpriteCount => _container.Children.Count;

    /// <summary>Test-only: number of distinct targets currently tracked.</summary>
    internal int ActiveTargetCount => _active.Count;

    /// <summary>Test-only: snapshot of the current sprites for inspection.</summary>
    internal IReadOnlyList<SpriteVisual> TestActiveSprites()
    {
        var list = new List<SpriteVisual>(_active.Count);
        foreach (var ah in _active.Values) list.Add(ah.Sprite);
        return list;
    }

    /// <summary>
    /// Test-only: synchronously fire all pending expiry timers, removing
    /// every active sprite. Use to make lifecycle tests deterministic without
    /// waiting on real wall-clock time.
    /// </summary>
    internal void TestForceExpire()
    {
        var snapshot = new List<KeyValuePair<UIElement, ActiveHighlight>>(_active);
        foreach (var kv in snapshot)
        {
            try { kv.Value.Timer.Stop(); } catch { }
            try { _container.Children.Remove(kv.Value.Sprite); } catch { }
            try { kv.Value.Sprite.Dispose(); } catch { }
            _active.Remove(kv.Key);
        }
    }

    public void Dispose()
    {
        foreach (var ah in _active.Values)
        {
            try { ah.Timer.Stop(); } catch { }
            try { _container.Children.Remove(ah.Sprite); } catch { }
            try { ah.Sprite.Dispose(); } catch { }
        }
        _active.Clear();

        try { _parentContainer.Children.Remove(_container); } catch { }
        try { _container.Dispose(); } catch { }
        try { _mountedBrush?.Dispose(); } catch { }
        try { _modifiedBrush?.Dispose(); } catch { }
    }
}
