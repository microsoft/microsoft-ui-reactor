using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace NetPulse.Charts;

/// <summary>
/// Protocol breakdown donut chart: TCP vs UDP connection counts.
/// Arcs resize in real-time as the connection mix shifts.
/// Also shows a breakdown of TCP states as an inner ring.
/// </summary>
sealed record ProtocolDonutProps(int TcpCount, int UdpCount, IReadOnlyList<TcpConn> TcpConnections);

sealed class ProtocolDonut : Component<ProtocolDonutProps>
{
    const double W = 320, H = 280;

    public override Element Render()
    {
        int tcpCount = Props.TcpCount;
        int udpCount = Props.UdpCount;
        double cx = W / 2, cy = H / 2 + 12;
        double outerR = 100, innerR = 60;

        var elements = new List<Element>();
        elements.Add(D3Charts.Text(10, 4, "Protocol Mix", 13, Gray(40)));

        if (tcpCount + udpCount == 0)
        {
            elements.Add(D3Charts.Text(cx - 40, cy - 7, "No data", 12, Gray(100)));
            return D3Canvas(W, H, elements.ToArray());
        }

        // Outer ring: TCP vs UDP
        var outerData = new (string Name, double Value)[]
        {
            ("TCP", tcpCount),
            ("UDP", udpCount),
        };

        var outerArcs = PieGenerator.Generate(outerData, d => d.Value, sort: false, padAngle: 0.03);
        var outerArcGen = new ArcGenerator().SetInnerRadius(innerR).SetOuterRadius(outerR);

        foreach (var arc in outerArcs)
        {
            string? pd = outerArcGen.Generate(arc);
            if (pd == null) continue;
            elements.Add(D3PathTranslated(pd, cx, cy,
                fill: Brush(Palette[arc.Index]), strokeWidth: 1));
        }

        // Outer labels
        foreach (var arc in outerArcs)
        {
            var (lx, ly) = ArcGenerator.Centroid(arc.StartAngle, arc.EndAngle,
                innerRadius: outerR + 16, outerRadius: outerR + 16);
            elements.Add(D3Charts.Text(cx + lx - 16, cy + ly - 7,
                $"{arc.Data.Name} ({(int)arc.Data.Value})", 10, Gray(60)));
        }

        // Inner ring: TCP state breakdown
        var tcpConns = Props.TcpConnections;
        if (tcpConns.Count > 0)
        {
            var stateCounts = tcpConns
                .Where(c => c.State != TcpState.Listen)
                .GroupBy(c => c.State)
                .Select(g => (Name: g.Key.ToString(), Value: (double)g.Count()))
                .Where(x => x.Value > 0)
                .OrderByDescending(x => x.Value)
                .ToArray();

            if (stateCounts.Length > 0)
            {
                double innerOuterR = innerR - 4;
                double innerInnerR = innerOuterR - 24;
                var innerArcs = PieGenerator.Generate(stateCounts, d => d.Value, sort: false, padAngle: 0.04);
                var innerArcGen = new ArcGenerator().SetInnerRadius(innerInnerR).SetOuterRadius(innerOuterR);

                foreach (var arc in innerArcs)
                {
                    string? pd = innerArcGen.Generate(arc);
                    if (pd == null) continue;
                    elements.Add(D3PathTranslated(pd, cx, cy,
                        fill: Brush(Palette[(arc.Index + 2) % Palette.Count], 0.7),
                        strokeWidth: 0.5));
                }
            }
        }

        // Center text
        int total = tcpCount + udpCount;
        elements.Add(TextCenter(cx - 30, cy - 12, $"{total}", 60, 16, Gray(40)));
        elements.Add(TextCenter(cx - 20, cy + 6, "total", 40, 10, Gray(100)));

        return D3Canvas(W, H, elements.ToArray());
    }
}
