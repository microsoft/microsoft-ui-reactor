using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace NetPulse.Charts;

/// <summary>
/// Live sorted horizontal bar chart of the top 20 TCP connections.
/// Bars are keyed by connection tuple and sorted by port number,
/// so connections constantly reorder as new ones appear — maximum LIS stress
/// in the keyed child reconciler.
/// </summary>
sealed record TopConnectionsBarProps(IReadOnlyList<TcpConn> Connections);

sealed class TopConnectionsBar : Component<TopConnectionsBarProps>
{
    const double W = 660, H = 280;
    const double Left = 140, Top = 30, Right = 60, Bottom = 10;
    const int MaxBars = 20;
    static readonly double PlotW = W - Left - Right;
    static readonly double PlotH = H - Top - Bottom;

    static readonly Dictionary<TcpState, int> StateColorIndex = new()
    {
        [TcpState.Established] = 2,
        [TcpState.TimeWait] = 3,
        [TcpState.CloseWait] = 4,
        [TcpState.FinWait1] = 5,
        [TcpState.FinWait2] = 5,
        [TcpState.SynSent] = 6,
        [TcpState.SynReceived] = 6,
        [TcpState.Closing] = 7,
        [TcpState.LastAck] = 7,
        [TcpState.Listen] = 8,
        [TcpState.Closed] = 9,
    };

    public override Element Render()
    {
        var connections = Props.Connections;
        if (connections.Count == 0)
        {
            return D3Canvas(W, H,
                D3Charts.Text(Left, Top + PlotH / 2, "No TCP connections", 12, Gray(100)));
        }

        // Filter out listeners, take top N sorted by remote port (causes constant reordering)
        var top = connections
            .Where(c => c.State != TcpState.Listen)
            .OrderByDescending(c => c.RemotePort)
            .ThenBy(c => c.RemoteAddr)
            .Take(MaxBars)
            .ToArray();

        if (top.Length == 0)
        {
            return D3Canvas(W, H,
                D3Charts.Text(Left, Top + PlotH / 2, "No active connections", 12, Gray(100)));
        }

        double maxPort = top.Max(c => (double)c.RemotePort);
        if (maxPort < 1) maxPort = 65535;

        var xs = new LinearScale([0, maxPort], [0, PlotW]).Nice();
        double barH = Math.Max(2, PlotH / top.Length * 0.8);
        double barGap = PlotH / top.Length;

        var elements = new List<Element>();

        // Grid lines
        var gridBrush = Gray(128, 40);
        foreach (var t in xs.Ticks(5))
        {
            elements.Add(D3Line(Left + xs.Map(t), Top, Left + xs.Map(t), Top + PlotH) with
            {
                Stroke = gridBrush, StrokeThickness = 1
            });
        }

        // Bars — keyed by connection key for reconciler stress
        for (int i = 0; i < top.Length; i++)
        {
            var conn = top[i];
            double y = Top + i * barGap;
            double barW = xs.Map(conn.RemotePort);
            int colorIdx = StateColorIndex.GetValueOrDefault(conn.State, 0);

            // Bar (keyed)
            elements.Add(
                (D3Rect(Left, y, Math.Max(1, barW), barH) with
                {
                    Fill = Brush(Palette[colorIdx % Palette.Count], 0.85),
                    RadiusX = 2,
                    RadiusY = 2,
                }).WithKey($"bar-{conn.Key}"));

            // Label
            elements.Add(
                TextRight(2, y + barH / 2 - 7,
                    TruncateLabel(conn.ShortRemote, 22), Left - 6, 9, Gray(60))
                .WithKey($"lbl-{conn.Key}"));

            // State badge
            elements.Add(
                D3Charts.Text(Left + barW + 4, y + barH / 2 - 7,
                    conn.State.ToString(), 8, Brush(Palette[colorIdx % Palette.Count]))
                .WithKey($"st-{conn.Key}"));
        }

        // Axes
        var axisBrush = Gray(100, 180);
        elements.Add(D3Line(Left, Top + PlotH, Left + PlotW, Top + PlotH) with
        {
            Stroke = axisBrush, StrokeThickness = 1
        });
        elements.Add(D3Line(Left, Top, Left, Top + PlotH) with
        {
            Stroke = axisBrush, StrokeThickness = 1
        });

        foreach (var t in xs.Ticks(5))
        {
            elements.Add(D3Charts.Text(Left + xs.Map(t) - 12, Top + PlotH + 4,
                Fmt(t), 9, axisBrush));
        }

        elements.Add(D3Charts.Text(Left, 4, $"Top {top.Length} Connections (by remote port)", 13, Gray(40)));

        return D3Canvas(W, H, elements.ToArray());
    }

    static string TruncateLabel(string s, int max) =>
        s.Length <= max ? s : string.Concat(s.AsSpan(0, max - 1), "\u2026");
}
