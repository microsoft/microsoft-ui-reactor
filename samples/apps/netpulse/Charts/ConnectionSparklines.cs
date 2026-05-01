using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace NetPulse.Charts;

/// <summary>
/// Per-connection sparklines: a grid of mini line charts, one per active TCP connection.
/// Each sparkline plots the TCP state value (1-12) over time.
///
/// This is the element count bomb: 60 connections x ~50 elements each = ~3000 D3 shapes.
/// Connections appear and disappear constantly, so sparklines mount/unmount with key churn.
/// The entire grid is keyed by connection tuple for maximum reconciler stress.
/// </summary>
sealed record ConnectionSparklinesProps(IReadOnlyList<SparklineEntry> Entries);

sealed class ConnectionSparklines : Component<ConnectionSparklinesProps>
{
    const double CellW = 200, CellH = 52;
    const double Pad = 6;
    const double SparkW = 120, SparkH = 28;
    const double SparkLeft = 70, SparkTop = 18;

    // Map TCP state to color for the sparkline
    static readonly Dictionary<int, int> StateColor = new()
    {
        [(int)TcpState.Established] = 2,  // green
        [(int)TcpState.TimeWait] = 3,     // orange
        [(int)TcpState.CloseWait] = 4,    // purple
        [(int)TcpState.FinWait1] = 5,
        [(int)TcpState.FinWait2] = 5,
        [(int)TcpState.SynSent] = 6,
        [(int)TcpState.SynReceived] = 6,
        [(int)TcpState.Closing] = 7,
    };

    public override Element Render()
    {
        var entries = Props.Entries;
        if (entries.Count == 0)
        {
            return D3Canvas(100, 40,
                D3Charts.Text(4, 12, "No connection history yet...", 11, Gray(100)));
        }

        // Compute grid dimensions — fill available width (~1340px minus margins)
        int cols = Math.Max(1, (int)(1340 / (CellW + Pad)));
        int rows = (entries.Count + cols - 1) / cols;
        double totalW = cols * (CellW + Pad);
        double totalH = rows * (CellH + Pad) + 26;

        var elements = new List<Element>();
        elements.Add(D3Charts.Text(4, 4, $"Connection Sparklines ({entries.Count})", 13, Gray(40)));

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            int col = i % cols;
            int row = i / cols;
            double cx = col * (CellW + Pad);
            double cy = 24 + row * (CellH + Pad);

            // Background card
            elements.Add(
                (D3Rect(cx, cy, CellW, CellH) with
                {
                    Fill = Gray(245),
                    RadiusX = 4,
                    RadiusY = 4,
                }).WithKey($"bg-{entry.Key}"));

            // Connection label
            string label = entry.Label.Length > 24
                ? string.Concat(entry.Label.AsSpan(0, 23), "\u2026")
                : entry.Label;
            elements.Add(
                D3Charts.Text(cx + 4, cy + 2, label, 8, Gray(80))
                .WithKey($"lbl-{entry.Key}"));

            // Sparkline: one Path per connection (not N Lines)
            var history = entry.StateHistory;
            if (history.Length >= 2)
            {
                double xStep = SparkW / (history.Length - 1);
                double yScale = SparkH / 12.0;

                int latestState = history[^1];
                int colorIdx = StateColor.GetValueOrDefault(latestState, 0);
                var strokeBrush = Brush(Palette[colorIdx % Palette.Count], 0.8);

                var pts = history.Select((s, j) => (
                    x: cx + SparkLeft + j * xStep,
                    y: cy + SparkTop + SparkH - s * yScale
                )).ToArray();

                elements.Add(
                    D3LinePath(pts, x: p => p.x, y: p => p.y,
                        stroke: strokeBrush, strokeWidth: 1.5)
                    .WithKey($"sp-{entry.Key}"));

                string stateName = ((TcpState)latestState).ToString();
                elements.Add(
                    D3Charts.Text(cx + 4, cy + CellH - 16, stateName, 8,
                        Brush(Palette[colorIdx % Palette.Count]))
                    .WithKey($"stn-{entry.Key}"));
            }
        }

        return D3Canvas(totalW, totalH, elements.ToArray());
    }
}
