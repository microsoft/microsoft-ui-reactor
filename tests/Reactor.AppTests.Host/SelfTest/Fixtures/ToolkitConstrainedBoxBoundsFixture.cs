using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using WctControls;
using Wct = CommunityToolkit.WinUI.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal sealed class ToolkitConstrainedBoxBoundsFixture(Harness h) : SelfTestFixtureBase(h)
{
    private const double Tolerance = 0.5;

    private sealed class Probe
    {
        public Action<int> SetPhase = null!;
    }

    private sealed class Content : Component<Probe>
    {
        public override Element Render()
        {
            var (phase, setPhase) = UseState(0);
            Props.SetPhase = setPhase;
            var box = ConstrainedBoxElement.ConstrainedBox(
                aspectRatio: new Wct.AspectRatio(2, 1),
                content: Border(TextBlock("bounds")))
                .Width(phase < 3 ? 300 : 180)
                .VAlign(VerticalAlignment.Top);

            // Keep the min/max cases independent, with room for their 2:1 layout.
            return phase switch
            {
                1 => box.MaxWidth(240),
                4 => box.MinWidth(240),
                _ => box,
            };
        }
    }

    public override async Task RunAsync()
    {
        var probe = new Probe();
        using var host = H.CreateHost();
        host.Mount(_ => Component<Content, Probe>(probe));
        await Harness.Render();
        var original = FindBox();
        var originalChild = FindChild(original);

        var baseline = await CheckGeometry("Unconstrained", 300, 150);
        probe.SetPhase(1);
        var maximum = await CheckGeometry("MaxWidth", 240, 120);
        H.Check("ToolkitConstrainedBox_MaxWidth_ChangesNativeLayout",
            maximum.Width <= 240 + Tolerance
                && maximum.Width < baseline.Width - Tolerance
                && maximum.Height < baseline.Height - Tolerance,
            $"unconstrained={baseline}; maximum={maximum}");

        probe.SetPhase(2);
        var maxReset = await CheckGeometry("MaxWidthReset", 300, 150);
        H.Check("ToolkitConstrainedBox_MaxWidthReset_RestoresNativeLayout",
            Near(maxReset.Width, baseline.Width) && Near(maxReset.Height, baseline.Height)
                && maxReset.Width > maximum.Width + Tolerance,
            $"unconstrained={baseline}; maximum={maximum}; reset={maxReset}");

        probe.SetPhase(3);
        var narrow = await CheckGeometry("MinimumUnconstrained", 180, 90);
        probe.SetPhase(4);
        var minimum = await CheckGeometry("MinWidth", 240, 120);
        H.Check("ToolkitConstrainedBox_MinWidth_ChangesNativeLayout",
            minimum.Width >= 240 - Tolerance
                && minimum.Width > narrow.Width + Tolerance
                && minimum.Height > narrow.Height + Tolerance,
            $"requestedWidth=180; unconstrained={narrow}; minimum={minimum}");

        probe.SetPhase(5);
        var minReset = await CheckGeometry("MinWidthReset", 180, 90);
        H.Check("ToolkitConstrainedBox_MinWidthReset_RestoresNativeLayout",
            Near(minReset.Width, narrow.Width) && Near(minReset.Height, narrow.Height)
                && minReset.Width < minimum.Width - Tolerance,
            $"unconstrained={narrow}; minimum={minimum}; reset={minReset}");

        async Task<(double Width, double Height)> CheckGeometry(string phase, double width, double height)
        {
            await Harness.Render();
            var settled = await Harness.WaitFor(() =>
            {
                var current = FindBox();
                var child = FindChild(current);
                return Near(current.ActualWidth, width) && Near(current.ActualHeight, height)
                    && Near(child.ActualWidth, width) && Near(child.ActualHeight, height);
            });
            var box = FindBox();
            var child = FindChild(box);
            var detail = $"expected={width}x{height}; box={box.ActualWidth}x{box.ActualHeight}; "
                + $"child={child.ActualWidth}x{child.ActualHeight}";
            Console.WriteLine($"# ToolkitConstrainedBox_{phase}: {detail}");
            H.Check($"ToolkitConstrainedBox_{phase}_Geometry", settled, detail);
            H.Check($"ToolkitConstrainedBox_{phase}_RetainsNativeInstances",
                ReferenceEquals(box, original) && ReferenceEquals(child, originalChild));
            H.Check($"ToolkitConstrainedBox_{phase}_FinitePositive",
                Positive(box.ActualWidth) && Positive(box.ActualHeight)
                    && Positive(child.ActualWidth) && Positive(child.ActualHeight), detail);
            H.Check($"ToolkitConstrainedBox_{phase}_AspectRatio",
                Near(box.ActualWidth, 2 * box.ActualHeight)
                    && Near(child.ActualWidth, 2 * child.ActualHeight), detail);
            return (box.ActualWidth, box.ActualHeight);
        }
    }

    private Wct.ConstrainedBox FindBox() =>
        H.FindControl<Wct.ConstrainedBox>(_ => true)
            ?? throw new InvalidOperationException("Generated Toolkit ConstrainedBox was not mounted.");

    private static WinUI.Border FindChild(Wct.ConstrainedBox box) =>
        box.Content as WinUI.Border
            ?? throw new InvalidOperationException("ConstrainedBox content was not a native Border.");

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) <= Tolerance;
}
