using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Regression coverage for issue #950's concrete-panel border-box gates.
/// Grid, StackPanel, and RelativePanel declare these properties themselves;
/// their Panel base does not.
/// </summary>
internal static class Issue950PanelBorderBoxFixture
{
    internal class MountUpdateUnsetReturnsToStyle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var gridStyle = StyleFor<WinUI.Grid>(
                (WinUI.Grid.PaddingProperty, new Thickness(2, 3, 4, 5)),
                (WinUI.Grid.CornerRadiusProperty, new CornerRadius(6)));
            var stackStyle = StyleFor<WinUI.StackPanel>(
                (WinUI.StackPanel.CornerRadiusProperty, new CornerRadius(7)));
            var relativeStyle = StyleFor<WinUI.RelativePanel>(
                (WinUI.RelativePanel.PaddingProperty, new Thickness(8, 9, 10, 11)),
                (WinUI.RelativePanel.CornerRadiusProperty, new CornerRadius(12)));
            var logicalGridStyle = StyleFor<WinUI.Grid>(
                (WinUI.Grid.PaddingProperty, new Thickness(61, 62, 63, 64)));
            var logicalRelativeStyle = StyleFor<WinUI.RelativePanel>(
                (WinUI.RelativePanel.PaddingProperty, new Thickness(65, 66, 67, 68)),
                (FrameworkElement.FlowDirectionProperty, FlowDirection.RightToLeft));
            var borderStyle = StyleFor<WinUI.Border>(
                (WinUI.Border.CornerRadiusProperty, new CornerRadius(13)));
            var buttonStyle = StyleFor<WinUI.Button>(
                (WinUI.Control.CornerRadiusProperty, new CornerRadius(14)));

            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (step, setStep) = ctx.UseState(0);
                var grid = Grid([GridSize.Auto], [GridSize.Auto], TextBlock("Issue950Panel_GridChild"))
                    .Set(g => { g.Tag = "Issue950Panel_Grid"; g.Style = gridStyle; });
                var stack = VStack(TextBlock("Issue950Panel_StackChild"))
                    .Set(s => { s.Tag = "Issue950Panel_Stack"; s.Style = stackStyle; });
                var relative = RelativePanel(TextBlock("Issue950Panel_RelativeChild"))
                    .Set(r => { r.Tag = "Issue950Panel_Relative"; r.Style = relativeStyle; });
                var logicalGrid = Grid([GridSize.Auto], [GridSize.Auto], TextBlock("Issue950Panel_LogicalGridChild"))
                    .Set(g => { g.Tag = "Issue950Panel_LogicalGrid"; g.Style = logicalGridStyle; });
                var logicalRelative = RelativePanel(TextBlock("Issue950Panel_LogicalRelativeChild"))
                    .Set(r => { r.Tag = "Issue950Panel_LogicalRelative"; r.Style = logicalRelativeStyle; });
                var border = Border(TextBlock("Issue950Panel_BorderChild"))
                    .Set(b => { b.Tag = "Issue950Panel_Border"; b.Style = borderStyle; });
                var button = Button("Issue950Panel_Button", () => { })
                    .Set(b => b.Style = buttonStyle);

                return VStack(
                    Button("Issue950Panel_Next", () => setStep(step + 1)),
                    step switch
                    {
                        0 => grid.Padding(21, 22, 23, 24).CornerRadius(25, 26, 27, 28),
                        1 => grid.Padding(31, 32, 33, 34).CornerRadius(35),
                        _ => grid,
                    },
                    step switch
                    {
                        0 => stack.CornerRadius(36, 37, 38, 39),
                        1 => stack.CornerRadius(40),
                        _ => stack,
                    },
                    step switch
                    {
                        0 => relative.Padding(41, 42, 43, 44).CornerRadius(45, 46, 47, 48),
                        1 => relative.Padding(51, 52, 53, 54).CornerRadius(55),
                        _ => relative,
                    },
                    logicalGrid.PaddingInlineStart(69),
                    logicalRelative.PaddingInlineStart(72),
                    step switch
                    {
                        0 => border.CornerRadius(56),
                        1 => border.CornerRadius(57),
                        _ => border,
                    },
                    step switch
                    {
                        0 => button.CornerRadius(58),
                        1 => button.CornerRadius(59),
                        _ => button,
                    });
            });

            await Harness.Render();

            var grid = H.FindControl<WinUI.Grid>(g => Equals(g.Tag, "Issue950Panel_Grid"));
            var stack = H.FindControl<WinUI.StackPanel>(s => Equals(s.Tag, "Issue950Panel_Stack"));
            var relative = H.FindControl<WinUI.RelativePanel>(r => Equals(r.Tag, "Issue950Panel_Relative"));
            var logicalGrid = H.FindControl<WinUI.Grid>(g => Equals(g.Tag, "Issue950Panel_LogicalGrid"));
            var logicalRelative = H.FindControl<WinUI.RelativePanel>(r => Equals(r.Tag, "Issue950Panel_LogicalRelative"));
            var border = H.FindControl<WinUI.Border>(b => Equals(b.Tag, "Issue950Panel_Border"));
            var button = H.FindButton("Issue950Panel_Button");
            H.Check("Issue950Panel_Mount_AllFound",
                grid is not null && stack is not null && relative is not null
                && logicalGrid is not null && logicalRelative is not null
                && border is not null && button is not null);
            if (grid is null || stack is null || relative is null
                || logicalGrid is null || logicalRelative is null
                || border is null || button is null) return;

            H.Check("Issue950Panel_Mount_GridPadding", grid.Padding == new Thickness(21, 22, 23, 24));
            H.Check("Issue950Panel_Mount_GridCornerRadius", grid.CornerRadius == new CornerRadius(25, 26, 27, 28));
            H.Check("Issue950Panel_Mount_StackCornerRadius", stack.CornerRadius == new CornerRadius(36, 37, 38, 39));
            H.Check("Issue950Panel_Mount_RelativePadding", relative.Padding == new Thickness(41, 42, 43, 44));
            H.Check("Issue950Panel_Mount_RelativeCornerRadius", relative.CornerRadius == new CornerRadius(45, 46, 47, 48));
            H.Check("Issue950Panel_Mount_LogicalPadding",
                logicalGrid.Padding == new Thickness(69, 62, 63, 64)
                && logicalRelative.Padding == new Thickness(65, 66, 72, 68));

            H.ClickButton("Issue950Panel_Next");
            await Harness.Render();

            H.Check("Issue950Panel_Update_SameInstances",
                ReferenceEquals(grid, H.FindControl<WinUI.Grid>(g => Equals(g.Tag, "Issue950Panel_Grid")))
                && ReferenceEquals(stack, H.FindControl<WinUI.StackPanel>(s => Equals(s.Tag, "Issue950Panel_Stack")))
                && ReferenceEquals(relative, H.FindControl<WinUI.RelativePanel>(r => Equals(r.Tag, "Issue950Panel_Relative"))));
            H.Check("Issue950Panel_Update_GridValues",
                grid.Padding == new Thickness(31, 32, 33, 34) && grid.CornerRadius == new CornerRadius(35));
            H.Check("Issue950Panel_Update_StackValue", stack.CornerRadius == new CornerRadius(40));
            H.Check("Issue950Panel_Update_RelativeValues",
                relative.Padding == new Thickness(51, 52, 53, 54) && relative.CornerRadius == new CornerRadius(55));

            H.ClickButton("Issue950Panel_Next");
            await Harness.Render();

            H.Check("Issue950Panel_Unset_GridReturnsToStyle",
                grid.Padding == new Thickness(2, 3, 4, 5) && grid.CornerRadius == new CornerRadius(6));
            H.Check("Issue950Panel_Unset_StackReturnsToStyle", stack.CornerRadius == new CornerRadius(7));
            H.Check("Issue950Panel_Unset_RelativeReturnsToStyle",
                relative.Padding == new Thickness(8, 9, 10, 11) && relative.CornerRadius == new CornerRadius(12));
            H.Check("Issue950Panel_Unset_BorderReturnsToStyle", border.CornerRadius == new CornerRadius(13));
            H.Check("Issue950Panel_Unset_ControlReturnsToStyle", button.CornerRadius == new CornerRadius(14));
            H.Check("Issue950Panel_Unset_AllLocalsCleared",
                IsUnset(grid, WinUI.Grid.PaddingProperty)
                && IsUnset(grid, WinUI.Grid.CornerRadiusProperty)
                && IsUnset(stack, WinUI.StackPanel.CornerRadiusProperty)
                && IsUnset(relative, WinUI.RelativePanel.PaddingProperty)
                && IsUnset(relative, WinUI.RelativePanel.CornerRadiusProperty)
                && IsUnset(border, WinUI.Border.CornerRadiusProperty)
                && IsUnset(button, WinUI.Control.CornerRadiusProperty));
        }
    }

    internal class ValuesDoNotLeakAcrossPoolReuse(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var next = Button("Issue950Panel_PoolNext", () => setPhase(phase + 1));
                return phase switch
                {
                    0 => FlexColumn(
                        next,
                        Grid([GridSize.Auto], [GridSize.Auto], TextBlock("Issue950Panel_PooledGrid")).Padding(16).CornerRadius(17),
                        VStack(TextBlock("Issue950Panel_PooledStack")).CornerRadius(18),
                        RelativePanel(TextBlock("Issue950Panel_PooledRelative")).Padding(19).CornerRadius(20)),
                    1 => FlexColumn(next),
                    _ => FlexColumn(
                        next,
                        Grid([GridSize.Auto], [GridSize.Auto], TextBlock("Issue950Panel_RecycledGrid")),
                        VStack(TextBlock("Issue950Panel_RecycledStack")),
                        RelativePanel(TextBlock("Issue950Panel_RecycledRelative"))),
                };
            });

            await Harness.Render();
            var grid = H.FindControl<WinUI.Grid>(g => HasTextChild(g, "Issue950Panel_PooledGrid"));
            var stack = H.FindControl<WinUI.StackPanel>(s => HasTextChild(s, "Issue950Panel_PooledStack"));
            var relative = H.FindControl<WinUI.RelativePanel>(r => HasTextChild(r, "Issue950Panel_PooledRelative"));
            H.Check("Issue950Panel_Pool_InitialValues",
                grid?.Padding == new Thickness(16) && grid.CornerRadius == new CornerRadius(17)
                && stack?.CornerRadius == new CornerRadius(18)
                && relative?.Padding == new Thickness(19) && relative.CornerRadius == new CornerRadius(20));

            H.ClickButton("Issue950Panel_PoolNext");
            await Harness.Render();
            H.ClickButton("Issue950Panel_PoolNext");
            await Harness.Render();

            var recycledGrid = H.FindControl<WinUI.Grid>(g => HasTextChild(g, "Issue950Panel_RecycledGrid"));
            var recycledStack = H.FindControl<WinUI.StackPanel>(s => HasTextChild(s, "Issue950Panel_RecycledStack"));
            var recycledRelative = H.FindControl<WinUI.RelativePanel>(r => HasTextChild(r, "Issue950Panel_RecycledRelative"));
            H.Check("Issue950Panel_Pool_GridReused",
                grid is not null && ReferenceEquals(grid, recycledGrid));
            H.Check("Issue950Panel_Pool_StackReused",
                stack is not null && ReferenceEquals(stack, recycledStack));
            H.Check("Issue950Panel_Pool_RelativeRemainsNonPoolable",
                relative is not null && recycledRelative is not null
                && !ReferenceEquals(relative, recycledRelative));
            H.Check("Issue950Panel_Pool_PoolablePropertiesCleared",
                recycledGrid is not null
                && IsUnset(recycledGrid, WinUI.Grid.PaddingProperty)
                && IsUnset(recycledGrid, WinUI.Grid.CornerRadiusProperty)
                && recycledStack is not null
                && IsUnset(recycledStack, WinUI.StackPanel.CornerRadiusProperty));
        }
    }

    internal class GridCornerRadiusPaintsRoundedPixels(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            host.Mount(_ => Grid([GridSize.Px(80)], [GridSize.Px(80)])
                .Set(g => g.Tag = "Issue950Panel_VisualGrid")
                .Size(80, 80)
                .Background("#FFFF0000")
                .CornerRadius(30));
            await Harness.Render();

            var grid = H.FindControl<WinUI.Grid>(g => Equals(g.Tag, "Issue950Panel_VisualGrid"));
            H.Check("Issue950Panel_Visual_Mounted",
                grid is not null && grid.ActualWidth >= 79 && grid.ActualHeight >= 79);
            if (grid is null) return;

            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(grid);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            var hasExpectedExtent =
                width >= 79 && height >= 79 && pixels.Length >= width * height * 4;
            H.Check("Issue950Panel_Visual_BitmapHasExpectedExtent", hasExpectedExtent);
            if (!hasExpectedExtent) return;

            byte AlphaAt(int x, int y) => pixels[((y * width) + x) * 4 + 3];
            H.Check("Issue950Panel_Visual_CenterIsPainted", AlphaAt(width / 2, height / 2) > 240);
            H.Check("Issue950Panel_Visual_SquareCornerIsClipped", AlphaAt(1, 1) < 15);
        }
    }

    private static Style StyleFor<T>(params (DependencyProperty Property, object Value)[] setters)
    {
        var style = new Style(typeof(T));
        foreach (var (property, value) in setters)
            style.Setters.Add(new Setter(property, value));
        return style;
    }

    private static bool IsUnset(DependencyObject target, DependencyProperty property) =>
        ReferenceEquals(DependencyProperty.UnsetValue, target.ReadLocalValue(property));

    private static bool HasTextChild(WinUI.Panel panel, string text) =>
        panel.Children.Count == 1 && panel.Children[0] is WinUI.TextBlock child && child.Text == text;
}
