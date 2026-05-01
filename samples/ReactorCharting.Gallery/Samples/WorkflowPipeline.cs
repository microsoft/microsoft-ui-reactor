using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorCharting.Gallery;

/// <summary>
/// A CI/CD-style workflow pipeline where each stage is a composite Reactor card
/// (icon + title + ProgressBar + status) connected by horizontal D3 bezier links.
/// All controls are Reactor elements positioned inside a D3Canvas.
/// </summary>
public sealed class WorkflowPipelineSample : GallerySample
{
    public override string Title => "Workflow Pipeline";
    public override string Description =>
        "A directed workflow graph where each node is a composite panel (icon, title, progress bar) " +
        "connected by curved bezier links. Shows how Reactor controls participate in D3 layout.";
    public override string Category => "Controls";

    public override string SourceCode => """
        return D3Canvas(W, H,
            [.. edges.Select(e => HBezier(cx[e.From], cy[e.From], cx[e.To], cy[e.To])),
             .. stages.Select((s, i) =>
                StageCard(s).Canvas(cx[i] - cardW/2, cy[i] - cardH/2)),
            ]);

        Element StageCard(stage) =>
            Border(VStack(2,
                TextBlock(stage.Icon).Set(tb => tb.FontFamily = mdl2),
                TextBlock(stage.Name).SemiBold(),
                Progress(stage.Progress),
                TextBlock(stage.Status),
            )) with { CornerRadius = 8, BorderBrush = statusColor, ... };
        .AutomationName("Workflow Pipeline")
        .FullDescription("CI/CD pipeline with 8 stages from Source to Deploy connected by dashed bezier links.");
        """;

    record Stage(string Name, string Icon, double Progress, string Status, int Column, int Row);
    record Edge(int From, int To);

    const double W = 780, H = 420;
    const double ColW = 130, RowH = 140;
    const double PadX = 50, PadY = 60;
    const double CardW = 100, CardH = 80;

    static readonly Stage[] Stages =
    [
        new("Source",    "\uE943", 100, "Done",       0, 0),
        new("Build",    "\uE71A", 100, "Done",       1, 0),
        new("Unit Test","\uE9D5", 100, "Done",       2, 0),
        new("Lint",     "\uE71C", 100, "Done",       2, 1),
        new("Package",  "\uE7B8", 75,  "Running...", 3, 0),
        new("Staging",  "\uE753", 0,   "Pending",    4, 0),
        new("Approve",  "\uE8FB", 0,   "Pending",    4, 1),
        new("Deploy",   "\uE968", 0,   "Pending",    5, 0),
    ];

    static readonly Edge[] Edges =
    [
        new(0, 1), new(1, 2), new(1, 3),
        new(2, 4), new(3, 4), new(4, 5),
        new(4, 6), new(5, 7), new(6, 7),
    ];

    public override Element Render()
    {
        // Compute node center positions from column/row grid
        var cx = Stages.Select(s => PadX + s.Column * ColW + CardW / 2).ToArray();
        var cy = Stages.Select(s => PadY + s.Row * RowH + CardH / 2).ToArray();

        var edgeBrush = ChartAxis;

        return D3Canvas(W, H,
        [
            // Horizontal bezier edges (dashed)
            .. Edges.Select(e =>
                HBezier(cx[e.From], cy[e.From], cx[e.To], cy[e.To], edgeBrush)),

            // Stage cards
            .. Stages.Select((s, i) =>
                StageCard(s).Canvas(cx[i] - CardW / 2, cy[i] - CardH / 2)),
        ])
            .AutomationName("Workflow Pipeline")
            .FullDescription("CI/CD pipeline with 8 stages from Source to Deploy connected by dashed bezier links.");
    }

    static Element StageCard(Stage s)
    {
        var color = StatusColor(s.Status);
        var bgColor = Brush(StatusPaletteColor(s.Status), opacity: 0.06);

        return (Border(
            VStack(2,
                (TextBlock(s.Icon) with { FontSize = 18 })
                    .FontFamily("Segoe MDL2 Assets")
                    .Foreground(color).HAlign(HorizontalAlignment.Center),
                (TextBlock(s.Name) with { FontSize = 11 })
                    .SemiBold().HAlign(HorizontalAlignment.Center),
                Progress(s.Progress).Width(80).Height(4).Margin(0, 4, 0, 0),
                (TextBlock(s.Status) with { FontSize = 9 })
                    .Foreground(color).HAlign(HorizontalAlignment.Center)
            ).HAlign(HorizontalAlignment.Center)
        ) with
        {
            CornerRadius = 8,
            BorderBrush = color,
            BorderThickness = 1.5,
            Background = bgColor,
            Padding = new Thickness(8),
        }).Size(CardW, CardH);
    }

    /// <summary>Horizontal bezier link — control points at midpoint X (for left-to-right DAGs).</summary>
    static Element HBezier(double x1, double y1, double x2, double y2, Microsoft.UI.Xaml.Media.Brush stroke)
    {
        double mx = (x1 + x2) / 2;
        var pb = new PathBuilder(3);
        pb.MoveTo(x1, y1);
        pb.BezierCurveTo(mx, y1, mx, y2, x2, y2);
        return D3Path(pb.ToString(), stroke: stroke, strokeWidth: 2)
            .StrokeDashArray(4, 3);
    }

    static Microsoft.UI.Xaml.Media.Brush StatusColor(string status) => status switch
    {
        "Done" => Brush(Palette[2]),         // green
        "Running..." => Brush(Palette[0]),   // blue
        _ => Brush(Palette[7]),              // gray
    };

    static D3Color StatusPaletteColor(string status) => status switch
    {
        "Done" => Palette[2],
        "Running..." => Palette[0],
        _ => Palette[7],
    };
}
