using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Tests for DSL factory methods (Dsl.cs) and fluent extension methods
/// (ElementExtensions.cs) that are not exercised by other fixtures.
/// Targets: fluent modifiers, transition extensions, shape extensions,
/// uncommon factory methods, and the hosting layer.
/// </summary>
internal static class DslExtensionFixtures
{
    // ════════════════════════════════════════════════════════════════════
    //  Fluent modifier chain coverage
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Exercises the full chain of fluent modifiers on ElementExtensions.cs:
    /// Bold, Italic, FontSize, FontFamily, Foreground, Background,
    /// Padding, Margin, Width, Height, MinWidth, MaxWidth, Opacity,
    /// HorizontalAlignment, VerticalAlignment, CornerRadius, IsEnabled,
    /// AutomationName, Tooltip.
    /// </summary>
    internal class FluentModifierChain(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                TextBlock("Styled")
                    .Bold()
                    .FontSize(18)
                    .Foreground(new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red))
                    .Background(new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGray))
                    .Padding(8)
                    .Margin(4)
                    .Width(200)
                    .Height(40)
                    .MinWidth(100)
                    .MaxWidth(300)
                    .MinHeight(20)
                    .MaxHeight(60)
                    .Opacity(0.8)
                    .CornerRadius(4),
                new ButtonElement("StyledBtn") { IsEnabled = false }
                    .AutomationName("auto-btn")
                    .AutomationId("btn-id"),
                new TextBlockElement("Selectable") { IsTextSelectionEnabled = true }
            ));

            await Harness.Render();
            var tb = H.FindText("Styled");
            H.Check("Fluent_TextPresent", tb is not null);
            H.Check("Fluent_FontSize", tb!.FontSize == 18);
            H.Check("Fluent_Width", tb.Width == 200);
            H.Check("Fluent_Opacity", tb.Opacity < 1.0);

            var btn = H.FindButton("StyledBtn");
            H.Check("Fluent_BtnDisabled", btn is not null && !btn!.IsEnabled);

            var sel = H.FindText("Selectable");
            H.Check("Fluent_Selectable", sel is not null && sel!.IsTextSelectionEnabled);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Implicit transition extensions
    // ════════════════════════════════════════════════════════════════════

    internal class TransitionExtensions(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return VStack(
                    TextBlock("TransExt")
                        .OpacityTransition(TimeSpan.FromMilliseconds(100))
                );
            });

            await Harness.Render();
            var tb = H.FindText("TransExt");
            H.Check("TransExt_Present", tb is not null);
            H.Check("TransExt_Opacity", tb!.OpacityTransition is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  DSL factory methods — uncommon controls
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Exercises DSL factory methods not covered by other tests:
    /// Heading, SubHeading, Caption, ThreeStateCheckBox, ProgressIndeterminate,
    /// Func component, HStack with spacing, VStack with spacing, GroupElement.
    /// </summary>
    internal class DslFactoryMethods(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Heading("DslHead"),
                SubHeading("DslSub"),
                Caption("DslCaption"),
                HStack(4, TextBlock("Spaced1"), TextBlock("Spaced2")),
                VStack(4, TextBlock("VSpaced1"), TextBlock("VSpaced2")),
                ProgressIndeterminate(),
                ThreeStateCheckBox(null, label: "ThreeState")
            ));

            await Harness.Render();
            H.Check("Dsl_Heading", H.FindText("DslHead") is not null);
            H.Check("Dsl_SubHeading", H.FindText("DslSub") is not null);
            H.Check("Dsl_Caption", H.FindText("DslCaption") is not null);
            H.Check("Dsl_HSpacing", H.FindText("Spaced1") is not null);
            H.Check("Dsl_VSpacing", H.FindText("VSpaced1") is not null);
            H.Check("Dsl_ProgressIndet",
                H.FindControl<ProgressBar>(p => p.IsIndeterminate) is not null);
            H.Check("Dsl_ThreeState", H.FindText("ThreeState") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Shape element extensions and fluent setters
    // ════════════════════════════════════════════════════════════════════

    internal class ShapeExtensions(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var brush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Blue);
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                new RectangleElement
                {
                    Fill = brush,
                    Stroke = brush,
                    StrokeThickness = 2,
                    RadiusX = 4,
                    RadiusY = 4,
                    Modifiers = new ElementModifiers { Width = 50, Height = 30 }
                },
                new EllipseElement
                {
                    Fill = brush,
                    Stroke = brush,
                    StrokeThickness = 1,
                    Modifiers = new ElementModifiers { Width = 40, Height = 40 }
                },
                new LineElement
                {
                    X1 = 0, Y1 = 0, X2 = 100, Y2 = 50,
                    Stroke = brush,
                    StrokeThickness = 2,
                    Modifiers = new ElementModifiers { Width = 100, Height = 50 }
                }
            ));

            await Harness.Render();
            H.Check("Shape_Rect",
                H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(_ => true) is not null);
            H.Check("Shape_Ellipse",
                H.FindControl<Microsoft.UI.Xaml.Shapes.Ellipse>(_ => true) is not null);
            H.Check("Shape_Line",
                H.FindControl<Microsoft.UI.Xaml.Shapes.Line>(_ => true) is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Grid layout builders (UniformGrid, InterspersedGrid)
    // ════════════════════════════════════════════════════════════════════

    internal class GridBuilders(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                // UniformGrid — equal columns
                UniformGrid(Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                    TextBlock("U1"), TextBlock("U2"), TextBlock("U3")),
                // InterspersedGrid — items with separators
                InterspersedGrid(
                    Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                    [TextBlock("I1"), TextBlock("I2"), TextBlock("I3")],
                    [0.33, 0.34, 0.33],
                    6,
                    i => TextBlock("|"))
            ));

            await Harness.Render();
            H.Check("GridBuilder_Uniform", H.FindText("U1") is not null && H.FindText("U3") is not null);
            H.Check("GridBuilder_Interspersed", H.FindText("I1") is not null && H.FindText("I3") is not null);
            H.Check("GridBuilder_Separator", H.FindText("|") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Menu DSL methods
    // ════════════════════════════════════════════════════════════════════

    internal class MenuDslMethods(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                MenuBar(
                    Menu("File",
                        MenuItem("New"),
                        MenuSeparator(),
                        MenuSubItem("Recent", MenuItem("Doc1"), MenuItem("Doc2")),
                        ToggleMenuItem("AutoSave", isChecked: true)
                    ),
                    Menu("Edit",
                        MenuItem("Cut"),
                        MenuItem("Copy"),
                        MenuItem("Paste")
                    )
                )
            ));

            await Harness.Render();
            H.Check("MenuDsl_FileMenu", H.FindText("File") is not null);
            H.Check("MenuDsl_EditMenu", H.FindText("Edit") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Attached property helpers (Grid.Row/Column, Canvas.Left/Top)
    // ════════════════════════════════════════════════════════════════════

    internal class AttachedProperties(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                Grid([GridSize.Star(), GridSize.Star()], [GridSize.Star(), GridSize.Star()],
                    TextBlock("TL").Grid(row: 0, column: 0),
                    TextBlock("TR").Grid(row: 0, column: 1),
                    TextBlock("BL").Grid(row: 1, column: 0),
                    TextBlock("BR").Grid(row: 1, column: 1, rowSpan: 1, columnSpan: 1)
                ),
                Canvas(
                    TextBlock("Canvas1").Canvas(left: 10, top: 20),
                    TextBlock("Canvas2").Canvas(left: 50, top: 60)
                )
            ));

            await Harness.Render();
            H.Check("Attached_GridCells",
                H.FindText("TL") is not null && H.FindText("BR") is not null);
            H.Check("Attached_CanvasItems",
                H.FindText("Canvas1") is not null && H.FindText("Canvas2") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ErrorBoundary element
    // ════════════════════════════════════════════════════════════════════

    private class ThrowingComponent : Component
    {
        public override Element Render() => throw new InvalidOperationException("TestCrash");
    }

    internal class ErrorBoundaryTest(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                new Microsoft.UI.Reactor.Core.ErrorBoundaryElement(
                    Component<ThrowingComponent>(),
                    ex => TextBlock($"Caught: {ex.Message}"))
            ));

            await Harness.Render();
            H.Check("ErrorBoundary_CaughtError", H.FindTextContaining("Caught:") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  GroupElement (fragment-like container)
    // ════════════════════════════════════════════════════════════════════

    internal class GroupElementTest(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                new GroupElement([TextBlock("G1"), TextBlock("G2"), TextBlock("G3")])
            ));

            await Harness.Render();
            H.Check("Group_AllPresent",
                H.FindText("G1") is not null && H.FindText("G2") is not null && H.FindText("G3") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Brush-typed and FontFamily/FontWeight modifier coverage
    //  (these allocate WinUI types and only succeed inside a host)
    // ════════════════════════════════════════════════════════════════════

    internal class BrushAndFontModifiers(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var brush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Crimson);
            var brush2 = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Navy);
            var ff = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI");

            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                TextBlock("BgFromString").Background("#445566"),
                TextBlock("BgFromBrush").Background(brush),
                TextBlock("FgFromString").Foreground("#778899"),
                TextBlock("FgFromBrush").Foreground(brush2),
                TextBlock("BorderStr").WithBorder("#112233", 1.5),
                TextBlock("BorderBrush").WithBorder(brush, 2.0),
                TextBlock("FontFamilyStr").FontFamily("Cascadia Code"),
                TextBlock("FontFamilyObj").FontFamily(ff),
                Button("FontWeightBtn").FontWeight(Microsoft.UI.Text.FontWeights.SemiBold),
                TextBlock("BoldText").Bold(),
                Path().StrokeDashArray(2, 4, 1).Stroke(brush).StrokeThickness(1)
                    .Set(p => p.Data = new Microsoft.UI.Xaml.Media.PathGeometry()),
                Rectangle().Fill(brush).Set(r => r.Width = 10).Set(r => r.Height = 10),
                Ellipse().Fill(brush2).Set(e => e.Width = 10).Set(e => e.Height = 10),
                Line(0, 0, 10, 10).Stroke(brush).StrokeThickness(1),
                // Implicit transitions and theme transitions
                TextBlock("TransOpacity").OpacityTransition(TimeSpan.FromMilliseconds(50)),
                TextBlock("TransRotation").RotationTransition(),
                TextBlock("TransScale").ScaleTransition(),
                TextBlock("TransTranslation").TranslationTransition(),
                Grid(global::System.Array.Empty<Microsoft.UI.Reactor.GridSize>(), global::System.Array.Empty<Microsoft.UI.Reactor.GridSize>()).BackgroundTransition(TimeSpan.FromMilliseconds(50)),
                VStack().BackgroundTransition(),
                TextBlock("ThemeTrans").WithTransitions(
                    new Microsoft.UI.Xaml.Media.Animation.ContentThemeTransition()),
                ListView().ItemContainerTransitions(
                    new Microsoft.UI.Xaml.Media.Animation.ContentThemeTransition()),
                Button("ResBtn").Resources(r => r.Set("ButtonBackground", "#0078D4"))
            ));

            await Harness.Render();
            H.Check("BrushModifiers_BgFromString", H.FindText("BgFromString") is not null);
            H.Check("BrushModifiers_FgFromString", H.FindText("FgFromString") is not null);
            H.Check("BrushModifiers_FontFamilyStr", H.FindText("FontFamilyStr") is not null);
            H.Check("BrushModifiers_BoldText", H.FindText("BoldText") is not null);
            H.Check("BrushModifiers_TransOpacity", H.FindText("TransOpacity") is not null);
        }
    }
}
