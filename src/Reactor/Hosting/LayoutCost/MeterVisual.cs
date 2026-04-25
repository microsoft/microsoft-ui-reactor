using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using WColor = global::Windows.UI.Color;

namespace Microsoft.UI.Reactor.Hosting.LayoutCost;

/// <summary>
/// A per-Component badge rendered as a sparkline of the last ~2 s of
/// layout cost. Each column is one flush tick (~33 ms) and its height
/// encodes measure+arrange ms for that tick; color comes from
/// <see cref="ColorRamps.MsRamp"/>.
/// </summary>
/// <remarks>
/// <para>Mutates size/offset/brush colors in place — never recreates the
/// column sprites, so per-flush cost is O(SampleCount) field writes.</para>
/// <para>Only flat <see cref="SpriteVisual"/> / <see cref="CompositionColorBrush"/>
/// primitives are used: no <see cref="ShapeVisual"/>, no DirectWrite —
/// keeps the overlay's own authored-element count at zero so it never
/// pollutes its own rollups (spec §2.7).</para>
/// </remarks>
internal sealed class MeterVisual
{
    /// <summary>Number of historical buckets kept.</summary>
    public const int SampleCount = 60;

    /// <summary>Time window each bucket represents, in milliseconds. 60 buckets × 100 ms = 6 s of history.</summary>
    public const long BucketDurationMs = 100;

    /// <summary>
    /// Bucket duration measured in <see cref="Stopwatch.GetTimestamp"/>
    /// ticks. We use the monotonic Stopwatch clock (rather than
    /// <c>DateTime.UtcNow.Ticks</c>) so system clock adjustments — DST,
    /// NTP, manual changes — can't make buckets advance backwards or get
    /// stuck in a long catch-up loop.
    /// </summary>
    private static readonly long BucketDurationStopwatchTicks =
        Stopwatch.Frequency * BucketDurationMs / 1000;

    /// <summary>Width of each sample column in DIPs.</summary>
    public const float ColumnWidth = 1f;

    /// <summary>Vertical padding above/below the sparkline area.</summary>
    public const float VPad = 1f;

    /// <summary>Horizontal padding left/right of the sparkline area.</summary>
    public const float HPad = 2f;

    /// <summary>Total badge height.</summary>
    public const float BoxHeight = 20f;

    /// <summary>Height of the drawable sparkline area inside the badge.</summary>
    public const float SparklineHeight = BoxHeight - 2 * VPad;

    /// <summary>Total badge width — wide enough for SampleCount columns + padding.</summary>
    public const float BoxWidth = (SampleCount * ColumnWidth) + 2 * HPad;

    /// <summary>Inner-width placeholder kept for legacy <see cref="MeterBox"/> callers.</summary>
    public const float InnerWidth = BoxWidth - 2 * HPad;

    /// <summary>Bar-height placeholder kept for legacy <see cref="MeterBox"/> callers.</summary>
    public const float BarHeight = SparklineHeight;

    /// <summary>Frame-ms ceiling for sparkline Y-scaling. 33 ms = one 30 Hz frame.</summary>
    public const double MsCeiling = 33.0;

    private readonly SpriteVisual _root;
    private readonly SpriteVisual _background;
    private readonly CompositionColorBrush _bgBrush;
    private readonly SpriteVisual[] _columns;
    private readonly CompositionColorBrush[] _colBrushes;

    // Sample ring. `_samples[_writeIdx]` is the in-progress bucket (max ms
    // seen since the bucket opened). When `BucketDurationTicks` elapses we
    // advance `_writeIdx` — the just-closed bucket becomes "newest
    // committed", a fresh zero slot starts accumulating at the new
    // `_writeIdx`. Reads run (_writeIdx + 1) → ... → _writeIdx (oldest →
    // newest, left → right).
    private readonly double[] _samples = new double[SampleCount];
    private int _writeIdx;
    private long _liveBucketStartTicks;

    public MeterVisual(Compositor compositor)
    {
        _bgBrush = compositor.CreateColorBrush(ColorRamps.BoxBackground);

        _background = compositor.CreateSpriteVisual();
        _background.Brush = _bgBrush;
        _background.Size = new Vector2(BoxWidth, BoxHeight);

        _root = compositor.CreateSpriteVisual();
        _root.Size = new Vector2(BoxWidth, BoxHeight);
        _root.Children.InsertAtTop(_background);

        _columns = new SpriteVisual[SampleCount];
        _colBrushes = new CompositionColorBrush[SampleCount];
        for (int i = 0; i < SampleCount; i++)
        {
            var brush = compositor.CreateColorBrush(ColorRamps.MsRampGreen);
            var col = compositor.CreateSpriteVisual();
            col.Brush = brush;
            col.Size = new Vector2(ColumnWidth, 0);
            col.Offset = new Vector3(HPad + i * ColumnWidth, BoxHeight - VPad, 0);
            _colBrushes[i] = brush;
            _columns[i] = col;
            _root.Children.InsertAtTop(col);
        }
    }

    public Visual Root => _root;

    /// <summary>Set the badge's top-left in the overlay canvas's coord space.</summary>
    public void SetPosition(float x, float y)
    {
        _root.Offset = new Vector3(x, y, 0);
    }

    /// <summary>
    /// Accumulate the snapshot's <see cref="ComponentSnapshot.LastFrameMs"/>
    /// into the currently-open bucket (max-wins so spikes dominate) and
    /// advance buckets when their time window elapses.
    /// </summary>
    public void UpdateFromSnapshot(in ComponentSnapshot s, in MeterBox _)
    {
        double ms = double.IsNaN(s.LastFrameMs) ? 0 : global::System.Math.Max(0, s.LastFrameMs);
        long now = Stopwatch.GetTimestamp();
        if (_liveBucketStartTicks == 0) _liveBucketStartTicks = now;

        // Max-accumulate into the live bucket.
        if (ms > _samples[_writeIdx]) _samples[_writeIdx] = ms;

        // Advance as many buckets as time elapsed. Idle periods show as
        // trailing zeros — we don't collapse them.
        while (now - _liveBucketStartTicks >= BucketDurationStopwatchTicks)
        {
            _writeIdx = (_writeIdx + 1) % SampleCount;
            _samples[_writeIdx] = 0;
            _liveBucketStartTicks += BucketDurationStopwatchTicks;
        }
        Redraw();
    }

    private void Redraw()
    {
        // Oldest-committed lives at (_writeIdx + 1) (next-to-be-overwritten
        // after a full cycle wrap); newest committed / in-progress lives at
        // _writeIdx. Render oldest → newest, left → right.
        for (int i = 0; i < SampleCount; i++)
        {
            int ringIdx = (_writeIdx + 1 + i) % SampleCount;
            double ms = _samples[ringIdx];

            double frac = global::System.Math.Min(ms / MsCeiling, 1.0);
            if (frac < 0) frac = 0;
            float h = (float)(frac * SparklineHeight);

            var col = _columns[i];
            col.Size = new Vector2(ColumnWidth, h);
            // Anchor at the bottom: offset Y = BoxHeight - VPad - h.
            col.Offset = new Vector3(HPad + i * ColumnWidth, BoxHeight - VPad - h, 0);

            var color = ColorRamps.MsRamp(ms);
            if (_colBrushes[i].Color != color)
                _colBrushes[i].Color = color;
        }
    }

    public void Hide() => _root.IsVisible = false;
    public void Show() => _root.IsVisible = true;

    public void Dispose()
    {
        for (int i = 0; i < _columns.Length; i++)
        {
            _columns[i].Dispose();
            _colBrushes[i].Dispose();
        }
        _background.Dispose();
        _root.Dispose();
        _bgBrush.Dispose();
    }
}
