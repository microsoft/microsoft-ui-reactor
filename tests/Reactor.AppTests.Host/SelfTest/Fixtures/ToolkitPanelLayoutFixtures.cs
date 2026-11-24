using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using WctControls;
using Wct = CommunityToolkit.WinUI.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// Expected placements follow the pinned Toolkit source for package 8.3.260402-preview2
// (CommunityToolkit/Windows 39c45efed2601b22170b9f6258c9cbe7e51031d1,
// components/Primitives/src/WrapPanel and StaggeredPanel).
internal static class ToolkitPanelLayout
{
    // Layout rounding moves an edge by at most half a physical pixel: <= 0.5 DIP at any scale >= 100%.
    internal const double Tolerance = 0.5;

    internal sealed class Probe
    {
        public Action<int> SetPhase = null!;
    }

    internal readonly record struct Slot(double X, double Y, double Width, double Height)
    {
        public override string ToString() => $"({X},{Y} {Width}x{Height})";
    }

    internal static Slot Measure(FrameworkElement child, FrameworkElement panel)
    {
        var point = child.TransformToVisual(panel).TransformPoint(new global::Windows.Foundation.Point());
        return new(point.X, point.Y, child.ActualWidth, child.ActualHeight);
    }

    internal static bool Matches(Slot[] actual, Slot[] expected) =>
        actual.Length == expected.Length && actual.Zip(expected).All(p =>
            Near(p.First.X, p.Second.X) && Near(p.First.Y, p.Second.Y)
                && Near(p.First.Width, p.Second.Width) && Near(p.First.Height, p.Second.Height));

    internal static bool SameInstances(WinUI.Panel panel, UIElement[] mounted) =>
        panel.Children.Count == mounted.Length
            && panel.Children.Select((child, i) => ReferenceEquals(child, mounted[i])).All(same => same);

    internal static bool Near(double actual, double expected) => Math.Abs(actual - expected) <= Tolerance;

    internal static string Describe(Slot[] slots) => string.Join(" ", slots.Select(s => s.ToString()));
}

internal sealed class ToolkitWrapPanelOrientationFixture(Harness h) : SelfTestFixtureBase(h)
{
    private const string Name = "ToolkitWrapPanel_VerticalOrientation";

    private sealed class Content : Component<ToolkitPanelLayout.Probe>
    {
        public override Element Render()
        {
            var (phase, setPhase) = UseState(0);
            Props.SetPhase = setPhase;
            return WrapPanelElement.WrapPanel(
                horizontalSpacing: 10, verticalSpacing: 5,
                orientation: phase == 0 ? WinUI.Orientation.Vertical : WinUI.Orientation.Horizontal,
                children: Enumerable.Range(0, 5)
                    .Select(i => (Element)Border(TextBlock($"wrap-{i}")).Size(50, 40).WithKey($"wrap-{i}"))
                    .ToArray())
                .Size(240, 120)
                .HAlign(HorizontalAlignment.Left)
                .VAlign(VerticalAlignment.Top);
        }
    }

    public override async Task RunAsync()
    {
        var probe = new ToolkitPanelLayout.Probe();
        using var host = H.CreateHost();
        host.Mount(_ => Component<Content, ToolkitPanelLayout.Probe>(probe));
        await Harness.Render();
        var panel = FindPanel();
        var children = panel.Children.ToArray();

        // Vertical: U = height, so VerticalSpacing separates items within a column and
        // HorizontalSpacing separates columns. 40 + 5 + 40 = 85 fits; a third tile needs
        // 130 > 120, so it starts the second column at x = 50 + 10.
        var vertical = await CheckLayout("Vertical", WinUI.Orientation.Vertical, afterUpdate: false,
        [
            new(0, 0, 50, 40), new(0, 45, 50, 40),
            new(60, 0, 50, 40), new(60, 45, 50, 40),
            new(120, 0, 50, 40),
        ]);
        H.Check($"{Name}_Vertical_WrapsAtHeightBoundary",
            ToolkitPanelLayout.Near(vertical[1].Y + vertical[1].Height + 5 + 40, 130)
                && vertical[2].X > vertical[1].X + ToolkitPanelLayout.Tolerance
                && ToolkitPanelLayout.Near(vertical[2].Y, 0),
            ToolkitPanelLayout.Describe(vertical));

        // Horizontal: four tiles fit in 240 (180 + 50 = 230); the fifth wraps to y = 40 + 5.
        probe.SetPhase(1);
        var horizontal = await CheckLayout("Horizontal", WinUI.Orientation.Horizontal, afterUpdate: true,
        [
            new(0, 0, 50, 40), new(60, 0, 50, 40), new(120, 0, 50, 40),
            new(180, 0, 50, 40), new(0, 45, 50, 40),
        ]);
        H.Check($"{Name}_OrientationUpdate_ChangesPlacement",
            !ToolkitPanelLayout.Matches(horizontal[1..], vertical[1..]),
            $"vertical={ToolkitPanelLayout.Describe(vertical)}; horizontal={ToolkitPanelLayout.Describe(horizontal)}");

        async Task<ToolkitPanelLayout.Slot[]> CheckLayout(
            string phase, WinUI.Orientation orientation, bool afterUpdate, ToolkitPanelLayout.Slot[] expected)
        {
            await Harness.Render();
            var settled = await Harness.WaitFor(() =>
                ToolkitPanelLayout.Matches(Snapshot(FindPanel()), expected));
            var current = FindPanel();
            var actual = Snapshot(current);
            var detail = $"orientation={current.Orientation}; expected={ToolkitPanelLayout.Describe(expected)}; "
                + $"actual={ToolkitPanelLayout.Describe(actual)}";
            Console.WriteLine($"# {Name}_{phase}: {detail}");
            H.Check($"{Name}_{phase}_NativeOrientation", current.Orientation == orientation, detail);
            H.Check($"{Name}_{phase}_Placement", settled, detail);
            if (afterUpdate)
                H.Check($"{Name}_{phase}_RetainsNativeInstances",
                    ReferenceEquals(current, panel) && ToolkitPanelLayout.SameInstances(current, children));
            return actual;
        }
    }

    private static ToolkitPanelLayout.Slot[] Snapshot(Wct.WrapPanel panel) =>
        panel.Children.Cast<FrameworkElement>().Select(c => ToolkitPanelLayout.Measure(c, panel)).ToArray();

    private Wct.WrapPanel FindPanel() =>
        H.FindControl<Wct.WrapPanel>(_ => true)
            ?? throw new InvalidOperationException("Generated Toolkit WrapPanel was not mounted.");
}

internal sealed class ToolkitStaggeredPanelShortestColumnFixture(Harness h) : SelfTestFixtureBase(h)
{
    private const string Name = "ToolkitStaggeredPanel_ShortestColumn";
    private const double ColumnWidth = 100;

    private sealed class Content : Component<ToolkitPanelLayout.Probe>
    {
        public override Element Render()
        {
            var (phase, setPhase) = UseState(0);
            Props.SetPhase = setPhase;
            double[] heights = phase == 0 ? [100, 30, 40, 50, 20, 60] : [100, 90, 40, 50, 20, 60];
            return StaggeredPanelElement.StaggeredPanel(
                desiredColumnWidth: ColumnWidth, columnSpacing: 0, rowSpacing: 0,
                children: heights
                    .Select((height, i) => (Element)Border(TextBlock($"stag-{i}")).Height(height).WithKey($"stag-{i}"))
                    .ToArray())
                .Size(300, 200)
                .HAlign(HorizontalAlignment.Left)
                .VAlign(VerticalAlignment.Top);
        }
    }

    public override async Task RunAsync()
    {
        var probe = new ToolkitPanelLayout.Probe();
        using var host = H.CreateHost();
        host.Mount(_ => Component<Content, ToolkitPanelLayout.Probe>(probe));
        await Harness.Render();
        var panel = FindPanel();
        var children = panel.Children.ToArray();

        // 300 / 100 = 3 columns. Each child goes to the first strictly shortest column:
        // heights [100,30,40] fill row 0, then 3 -> col 1 (30), 4 -> col 2 (40), 5 -> col 2 (60).
        // Round-robin would instead place 3, 4 and 5 at columns 0, 1 and 2.
        var initial = await CheckLayout("Initial", afterUpdate: false,
        [
            Slot(0, 0, 100), Slot(1, 0, 30), Slot(2, 0, 40),
            Slot(1, 30, 50), Slot(2, 40, 20), Slot(2, 60, 60),
        ]);
        H.Check($"{Name}_Initial_NotRoundRobin",
            !ToolkitPanelLayout.Near(initial[3].X, 0) && !ToolkitPanelLayout.Near(initial[4].X, ColumnWidth),
            ToolkitPanelLayout.Describe(initial));

        // Growing child 1 to 90 makes column 2 (40) shortest for child 3; columns 1 and 2 then
        // tie at 90 and child 4 takes the lower index; child 5 then takes column 2 (90).
        probe.SetPhase(1);
        var updated = await CheckLayout("HeightUpdate", afterUpdate: true,
        [
            Slot(0, 0, 100), Slot(1, 0, 90), Slot(2, 0, 40),
            Slot(2, 40, 50), Slot(1, 90, 20), Slot(2, 90, 60),
        ]);
        H.Check($"{Name}_HeightUpdate_ChangesSubsequentPlacement",
            Enumerable.Range(3, 3).All(i => !ToolkitPanelLayout.Matches([updated[i]], [initial[i]])),
            $"initial={ToolkitPanelLayout.Describe(initial)}; updated={ToolkitPanelLayout.Describe(updated)}");

        async Task<ToolkitPanelLayout.Slot[]> CheckLayout(
            string phase, bool afterUpdate, ToolkitPanelLayout.Slot[] expected)
        {
            await Harness.Render();
            var settled = await Harness.WaitFor(() =>
                ToolkitPanelLayout.Matches(Snapshot(FindPanel()), expected));
            var current = FindPanel();
            var actual = Snapshot(current);
            var detail = $"panel={current.ActualWidth}x{current.ActualHeight}; "
                + $"expected={ToolkitPanelLayout.Describe(expected)}; actual={ToolkitPanelLayout.Describe(actual)}";
            Console.WriteLine($"# {Name}_{phase}: {detail}");
            H.Check($"{Name}_{phase}_Placement", settled, detail);
            if (afterUpdate)
                H.Check($"{Name}_{phase}_RetainsNativeInstances",
                    ReferenceEquals(current, panel) && ToolkitPanelLayout.SameInstances(current, children));
            return actual;
        }
    }

    private static ToolkitPanelLayout.Slot Slot(int column, double y, double height) =>
        new(column * ColumnWidth, y, ColumnWidth, height);

    private static ToolkitPanelLayout.Slot[] Snapshot(Wct.StaggeredPanel panel) =>
        panel.Children.Cast<FrameworkElement>().Select(c => ToolkitPanelLayout.Measure(c, panel)).ToArray();

    private Wct.StaggeredPanel FindPanel() =>
        H.FindControl<Wct.StaggeredPanel>(_ => true)
            ?? throw new InvalidOperationException("Generated Toolkit StaggeredPanel was not mounted.");
}
