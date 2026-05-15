// Recipe: absolute positioning with Canvas.
//
// Pattern: Canvas() factory + .Canvas(left, top) modifier on children.
// ⚠️ `using static Factories` brings a Canvas() factory that shadows
// Microsoft.UI.Xaml.Controls.Canvas — do NOT call Canvas.SetLeft()/SetTop(),
// use the fluent .Canvas(left, top) modifier instead.

// In this clone, run `mur pack-local` once. Bump the version below to match
// whatever `mur pack-local` printed (default: 0.0.0-local). For a real NuGet
// consumer, set Version to a published Microsoft.UI.Reactor release.
#:package Microsoft.UI.Reactor@0.0.0-local
#:package Microsoft.WindowsAppSDK@2.0.1
#:property OutputType=WinExe
#:property TargetFramework=net10.0-windows10.0.22621.0
#:property UseWinUI=true
#:property WindowsPackageType=None

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Canvas demo", width: 600, height: 500);

class App : Component
{
    public override Element Render()
    {
        return Canvas(
            // Position children with the .Canvas(left, top) extension
            Border(TextBlock("A").Padding(8))
                .Background(Theme.CardBackground)
                .WithBorder(Theme.CardStroke, 1)
                .CornerRadius(4)
                .Canvas(left: 50, top: 30),

            Border(TextBlock("B").Padding(8))
                .Background(Theme.CardBackground)
                .WithBorder(Theme.CardStroke, 1)
                .CornerRadius(4)
                .Canvas(left: 200, top: 100),

            // Center on a point (anchor 0.5, 0.5)
            Ellipse().Width(20).Height(20)
                .Fill(Theme.Accent)
                .CenterAt(x: 150, y: 75),

            // Shapes
            Line(50, 55, 200, 125).Stroke(Theme.DividerStroke, 1),
            Rectangle().Width(60).Height(40)
                .Fill(Theme.SubtleFill)
                .Canvas(left: 350, top: 50)
        ) with { Width = 500, Height = 400, Background = Theme.SolidBackground };
    }
}
