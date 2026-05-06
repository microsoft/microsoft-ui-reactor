using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class ReconcilerFixtures
{
    internal class MountText(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => TextBlock("Hello from Reactor"));

            await Harness.Render();

            H.Check("Reconciler_MountText_TextAppears",
                H.FindText("Hello from Reactor") is not null);

            H.Check("Reconciler_MountText_IsTextBlock",
                H.FindControl<TextBlock>(tb => tb.Text == "Hello from Reactor") is not null);
        }
    }

    internal class UpdateText(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (text, setText) = ctx.UseState("Before");
                return VStack(
                    TextBlock(text),
                    Button("Change", () => setText("After"))
                );
            });

            await Harness.Render();

            H.Check("Reconciler_UpdateText_InitialText",
                H.FindText("Before") is not null);

            H.ClickButton("Change");
            await Harness.Render();

            H.Check("Reconciler_UpdateText_UpdatedText",
                H.FindText("After") is not null);

            H.Check("Reconciler_UpdateText_OldTextGone",
                H.FindText("Before") is null);
        }
    }

    internal class AddRemoveChildren(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (count, setCount) = ctx.UseState(3);
                return VStack(
                    HStack(
                        Button("Add", () => setCount(count + 1)),
                        Button("Remove", () => setCount(Math.Max(0, count - 1)))
                    ),
                    VStack(Enumerable.Range(0, count).Select(i => TextBlock($"Item {i}")).ToArray())
                );
            });

            await Harness.Render();

            H.Check("Reconciler_AddRemoveChildren_InitialCount",
                H.FindText("Item 0") is not null && H.FindText("Item 2") is not null);

            H.ClickButton("Add");
            await Harness.Render();

            H.Check("Reconciler_AddRemoveChildren_AfterAdd",
                H.FindText("Item 3") is not null);

            H.ClickButton("Remove");
            await Harness.Render();
            H.ClickButton("Remove");
            await Harness.Render();

            H.Check("Reconciler_AddRemoveChildren_AfterRemove",
                H.FindText("Item 3") is null && H.FindText("Item 2") is null);
        }
    }

    // Simple counter component to test re-rendering
    private class CounterComponent : Component
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            return VStack(
                TextBlock($"Count: {count}"),
                Button("Increment", () => setCount(count + 1))
            );
        }
    }

    internal class ComponentRerender(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(new CounterComponent());

            await Harness.Render();

            H.Check("Reconciler_ComponentRerender_InitialState",
                H.FindText("Count: 0") is not null);

            H.ClickButton("Increment");
            await Harness.Render();

            H.Check("Reconciler_ComponentRerender_AfterClick",
                H.FindText("Count: 1") is not null);

            H.ClickButton("Increment");
            await Harness.Render();
            H.ClickButton("Increment");
            await Harness.Render();

            H.Check("Reconciler_ComponentRerender_MultipleClicks",
                H.FindText("Count: 3") is not null);
        }
    }

    internal class KeyedList(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (reversed, setReversed) = ctx.UseState(false);
                var items = new[] { "Alpha", "Beta", "Gamma" };
                var ordered = reversed ? items.Reverse().ToArray() : items;

                return VStack(
                    Button("Reverse", () => setReversed(!reversed)),
                    VStack(ordered.Select(item => TextBlock(item).WithKey(item)).ToArray())
                );
            });

            await Harness.Render();

            // Capture the initial TextBlock references
            var alpha1 = H.FindText("Alpha");
            H.Check("Reconciler_KeyedList_InitialOrder",
                alpha1 is not null && H.FindText("Beta") is not null && H.FindText("Gamma") is not null);

            H.ClickButton("Reverse");
            await Harness.Render();

            // After reversal, keyed items should be reused (same TextBlock instances)
            var alpha2 = H.FindText("Alpha");
            H.Check("Reconciler_KeyedList_AfterReverse",
                alpha2 is not null && H.FindText("Gamma") is not null);

            H.Check("Reconciler_KeyedList_ControlReused",
                ReferenceEquals(alpha1, alpha2));
        }
    }

    /// <summary>
    /// Regression test for the docking window crash: dynamically adding/removing
    /// children from a Grid. The original bug was in UpdateGrid which did inline
    /// positional add/remove instead of delegating to ChildReconciler.
    /// </summary>
    internal class GridDynamicChildCount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (paneCount, setPaneCount) = ctx.UseState(1);

                var panes = Enumerable.Range(0, paneCount)
                    .Select(i => TextBlock($"Pane {i}").Grid(row: 0, column: i))
                    .ToArray();
                var cols = Enumerable.Range(0, Math.Max(1, paneCount)).Select(_ => GridSize.Star()).ToArray();

                return VStack(
                    HStack(
                        Button("Split", () => setPaneCount(paneCount + 1)),
                        Button("Collapse", () => setPaneCount(Math.Max(0, paneCount - 1)))
                    ),
                    Grid(cols, [GridSize.Star()], panes)
                );
            });

            await Harness.Render();

            // Initial: 1 pane
            H.Check("Reconciler_GridDynamic_InitialPane",
                H.FindText("Pane 0") is not null);

            // Split: 1 → 2 panes
            H.ClickButton("Split");
            await Harness.Render();

            H.Check("Reconciler_GridDynamic_AfterFirstSplit",
                H.FindText("Pane 0") is not null && H.FindText("Pane 1") is not null);

            // Split again: 2 → 3 panes
            H.ClickButton("Split");
            await Harness.Render();

            H.Check("Reconciler_GridDynamic_AfterSecondSplit",
                H.FindText("Pane 2") is not null);

            // Verify all three panes exist
            var grid = H.FindControl<Microsoft.UI.Xaml.Controls.Grid>(g => g.Children.Count >= 3);
            H.Check("Reconciler_GridDynamic_ThreePanesInGrid",
                grid is not null);

            // Collapse: 3 → 2 panes
            H.ClickButton("Collapse");
            await Harness.Render();

            H.Check("Reconciler_GridDynamic_AfterCollapse",
                H.FindText("Pane 2") is null && H.FindText("Pane 1") is not null);

            // Collapse to 1, then to 0
            H.ClickButton("Collapse");
            await Harness.Render();
            H.ClickButton("Collapse");
            await Harness.Render();

            H.Check("Reconciler_GridDynamic_CollapsedToEmpty",
                H.FindText("Pane 0") is null);

            // Grow back from empty: 0 → 1
            H.ClickButton("Split");
            await Harness.Render();

            H.Check("Reconciler_GridDynamic_GrowFromEmpty",
                H.FindText("Pane 0") is not null);
        }
    }

    /// <summary>
    /// Verifies all layout containers (Stack, Grid, Flex, WrapGrid, Canvas)
    /// handle dynamic child count changes without crashing. Ensures the
    /// same class of bug fixed in UpdateGrid doesn't exist elsewhere.
    /// </summary>
    internal class AllLayoutsDynamicChildCount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (count, setCount) = ctx.UseState(2);

                var stackChildren = Enumerable.Range(0, count)
                    .Select(i => TextBlock($"S{i}"))
                    .ToArray();
                var gridChildren = Enumerable.Range(0, count)
                    .Select(i => TextBlock($"G{i}").Grid(row: 0, column: i))
                    .ToArray();
                var flexChildren = Enumerable.Range(0, count)
                    .Select(i => TextBlock($"F{i}"))
                    .ToArray();
                var wrapChildren = Enumerable.Range(0, count)
                    .Select(i => TextBlock($"W{i}"))
                    .ToArray();
                var canvasChildren = Enumerable.Range(0, count)
                    .Select(i => TextBlock($"C{i}"))
                    .ToArray();
                var cols = Enumerable.Range(0, Math.Max(1, count)).Select(_ => GridSize.Star()).ToArray();

                return VStack(
                    HStack(
                        Button("Add", () => setCount(count + 1)),
                        Button("Remove", () => setCount(Math.Max(0, count - 1)))
                    ),
                    VStack(stackChildren),
                    Grid(cols, [GridSize.Star()], gridChildren),
                    FlexRow(flexChildren),
                    WrapGrid(wrapChildren),
                    Canvas(canvasChildren)
                );
            });

            await Harness.Render();

            // Initial: 2 items in each layout
            H.Check("Reconciler_AllLayouts_Initial",
                H.FindText("S0") is not null && H.FindText("S1") is not null &&
                H.FindText("G0") is not null && H.FindText("G1") is not null &&
                H.FindText("F0") is not null && H.FindText("F1") is not null &&
                H.FindText("W0") is not null && H.FindText("W1") is not null &&
                H.FindText("C0") is not null && H.FindText("C1") is not null);

            // Grow: 2 → 4
            H.ClickButton("Add");
            await Harness.Render();
            H.ClickButton("Add");
            await Harness.Render();

            H.Check("Reconciler_AllLayouts_AfterGrow",
                H.FindText("S3") is not null &&
                H.FindText("G3") is not null &&
                H.FindText("F3") is not null &&
                H.FindText("W3") is not null &&
                H.FindText("C3") is not null);

            // Shrink: 4 → 1
            H.ClickButton("Remove");
            await Harness.Render();
            H.ClickButton("Remove");
            await Harness.Render();
            H.ClickButton("Remove");
            await Harness.Render();

            H.Check("Reconciler_AllLayouts_AfterShrink",
                H.FindText("S0") is not null && H.FindText("S1") is null &&
                H.FindText("G0") is not null && H.FindText("G1") is null &&
                H.FindText("F0") is not null && H.FindText("F1") is null &&
                H.FindText("W0") is not null && H.FindText("W1") is null &&
                H.FindText("C0") is not null && H.FindText("C1") is null);

            // Shrink to empty: 1 → 0
            H.ClickButton("Remove");
            await Harness.Render();

            H.Check("Reconciler_AllLayouts_Empty",
                H.FindText("S0") is null &&
                H.FindText("G0") is null &&
                H.FindText("F0") is null &&
                H.FindText("W0") is null &&
                H.FindText("C0") is null);

            // Grow from empty: 0 → 2
            H.ClickButton("Add");
            await Harness.Render();
            H.ClickButton("Add");
            await Harness.Render();

            H.Check("Reconciler_AllLayouts_GrowFromEmpty",
                H.FindText("S0") is not null && H.FindText("S1") is not null &&
                H.FindText("G0") is not null && H.FindText("G1") is not null &&
                H.FindText("F0") is not null && H.FindText("F1") is not null &&
                H.FindText("W0") is not null && H.FindText("W1") is not null &&
                H.FindText("C0") is not null && H.FindText("C1") is not null);
        }
    }

    /// <summary>
    /// Regression coverage for the chart-primitive ShallowEquals fast paths in
    /// Element.cs. The brush + transform structural-equality helpers can't be
    /// exercised in headless xUnit tests because SolidColorBrush / TranslateTransform
    /// constructors require a XAML dispatcher, so the coverage lives here.
    /// </summary>
    internal class ShallowEqualsChartPrimitives(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            // ── PathElement.Fill — distinct SolidColorBrush, same color → equal ──
            // D3Charts.Brush(...) allocates a fresh SolidColorBrush on every call,
            // so reference equality never holds for the chart hot path.
            var redA = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 200, 0, 0));
            var redB = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 200, 0, 0));
            var blue = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 0, 0, 200));

            H.Check("ShallowEquals_PathElement_Same_Color_Distinct_Brush_Returns_True",
                Element.ShallowEquals(
                    new PathElement { PathDataString = "M0,0 L10,10", Fill = redA },
                    new PathElement { PathDataString = "M0,0 L10,10", Fill = redB }));

            H.Check("ShallowEquals_PathElement_Different_Color_Returns_False",
                !Element.ShallowEquals(
                    new PathElement { PathDataString = "M0,0 L10,10", Fill = redA },
                    new PathElement { PathDataString = "M0,0 L10,10", Fill = blue }));

            H.Check("ShallowEquals_PathElement_Different_Stroke_Color_Returns_False",
                !Element.ShallowEquals(
                    new PathElement { PathDataString = "M0,0 L10,10", Stroke = redA },
                    new PathElement { PathDataString = "M0,0 L10,10", Stroke = blue }));

            H.Check("ShallowEquals_PathElement_One_Brush_Null_Returns_False",
                !Element.ShallowEquals(
                    new PathElement { PathDataString = "M0,0 L10,10", Fill = redA },
                    new PathElement { PathDataString = "M0,0 L10,10" }));

            // ── PathElement.RenderTransform — TransformsEqual coverage ──
            // D3PathTranslated allocates a fresh TranslateTransform per render —
            // structural compare is the whole point of the helper.
            var t1 = new TranslateTransform { X = 100, Y = 50 };
            var t2 = new TranslateTransform { X = 100, Y = 50 };
            var tDifferentX = new TranslateTransform { X = 200, Y = 50 };
            var tDifferentY = new TranslateTransform { X = 100, Y = 75 };
            var rotate1 = new RotateTransform { Angle = 30 };
            var rotate2 = new RotateTransform { Angle = 30 };

            H.Check("ShallowEquals_PathElement_Distinct_TranslateTransforms_Same_XY_Returns_True",
                Element.ShallowEquals(
                    new PathElement { PathDataString = "M0,0 L10,10", RenderTransform = t1 },
                    new PathElement { PathDataString = "M0,0 L10,10", RenderTransform = t2 }));

            H.Check("ShallowEquals_PathElement_TranslateTransform_Different_X_Returns_False",
                !Element.ShallowEquals(
                    new PathElement { PathDataString = "M0,0 L10,10", RenderTransform = t1 },
                    new PathElement { PathDataString = "M0,0 L10,10", RenderTransform = tDifferentX }));

            H.Check("ShallowEquals_PathElement_TranslateTransform_Different_Y_Returns_False",
                !Element.ShallowEquals(
                    new PathElement { PathDataString = "M0,0 L10,10", RenderTransform = t1 },
                    new PathElement { PathDataString = "M0,0 L10,10", RenderTransform = tDifferentY }));

            // Non-TranslateTransform falls back to ReferenceEquals — distinct
            // RotateTransform instances must NOT be treated as equal even with same Angle.
            H.Check("ShallowEquals_PathElement_Distinct_RotateTransforms_Returns_False",
                !Element.ShallowEquals(
                    new PathElement { PathDataString = "M0,0 L10,10", RenderTransform = rotate1 },
                    new PathElement { PathDataString = "M0,0 L10,10", RenderTransform = rotate2 }));

            // Mismatched transform types must not pretend to be equal.
            H.Check("ShallowEquals_PathElement_Mismatched_Transform_Types_Returns_False",
                !Element.ShallowEquals(
                    new PathElement { PathDataString = "M0,0 L10,10", RenderTransform = t1 },
                    new PathElement { PathDataString = "M0,0 L10,10", RenderTransform = rotate1 }));

            H.Check("ShallowEquals_PathElement_One_Transform_Null_Returns_False",
                !Element.ShallowEquals(
                    new PathElement { PathDataString = "M0,0 L10,10", RenderTransform = t1 },
                    new PathElement { PathDataString = "M0,0 L10,10" }));

            // ── PathElement.StrokeDashArray — reference compare ──
            // Distinct DoubleCollection instances force the slow path even when
            // their values match. Mirrors how Children references are compared.
            var dashA = new DoubleCollection { 2, 2 };
            var dashB = new DoubleCollection { 2, 2 };

            H.Check("ShallowEquals_PathElement_Distinct_StrokeDashArray_Returns_False",
                !Element.ShallowEquals(
                    new PathElement { PathDataString = "M0,0 L10,10", StrokeDashArray = dashA },
                    new PathElement { PathDataString = "M0,0 L10,10", StrokeDashArray = dashB }));

            H.Check("ShallowEquals_PathElement_Same_StrokeDashArray_Reference_Returns_True",
                Element.ShallowEquals(
                    new PathElement { PathDataString = "M0,0 L10,10", StrokeDashArray = dashA },
                    new PathElement { PathDataString = "M0,0 L10,10", StrokeDashArray = dashA }));

            // ── LineElement + CanvasElement with brushes ──
            H.Check("ShallowEquals_LineElement_Same_Color_Distinct_Brush_Returns_True",
                Element.ShallowEquals(
                    new LineElement { X1 = 0, Y1 = 0, X2 = 100, Y2 = 100, Stroke = redA },
                    new LineElement { X1 = 0, Y1 = 0, X2 = 100, Y2 = 100, Stroke = redB }));

            H.Check("ShallowEquals_LineElement_Different_Color_Returns_False",
                !Element.ShallowEquals(
                    new LineElement { X1 = 0, Y1 = 0, X2 = 100, Y2 = 100, Stroke = redA },
                    new LineElement { X1 = 0, Y1 = 0, X2 = 100, Y2 = 100, Stroke = blue }));

            var canvasChildren = new Element[] { new TextBlockElement("a") };
            H.Check("ShallowEquals_CanvasElement_Same_Children_And_Background_Returns_True",
                Element.ShallowEquals(
                    new CanvasElement(canvasChildren) { Width = 400, Background = redA },
                    new CanvasElement(canvasChildren) { Width = 400, Background = redB }));

            H.Check("ShallowEquals_CanvasElement_Different_Background_Returns_False",
                !Element.ShallowEquals(
                    new CanvasElement(canvasChildren) { Width = 400, Background = redA },
                    new CanvasElement(canvasChildren) { Width = 400, Background = blue }));

            return Task.CompletedTask;
        }
    }
}
