using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Tests that exercise the Update dispatch path (Reconciler.Update.cs) for every
/// control type. Each test mounts a control with initial props, changes the props
/// via state, and verifies the control was updated in-place (not remounted).
///
/// Also exercises ApplyModifiers (Reconciler.cs lines 462-600+) by applying and
/// changing modifiers during re-renders.
/// </summary>
internal static class ControlUpdateFixtures
{
    // ════════════════════════════════════════════════════════════════════
    //  Text update paths
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Updates Text content, font size, weight, wrapping, and alignment.
    /// Exercises UpdateText (lines 229-243) and multiple property branches.
    /// </summary>
    internal class TextPropertyUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var text = phase == 0
                    ? TextBlock("Before").FontSize(14)
                    : TextBlock("After").FontSize(20).Bold().TextWrapping(TextWrapping.Wrap);
                return VStack(Button("UpdText", () => set(1)), text);
            });

            await Harness.Render();
            var tb = H.FindText("Before");
            H.Check("TextUpdate_Initial", tb is not null);

            H.ClickButton("UpdText");
            await Harness.Render();

            var tb2 = H.FindText("After");
            H.Check("TextUpdate_ContentChanged", tb2 is not null);
            H.Check("TextUpdate_Reused", ReferenceEquals(tb, tb2));
            H.Check("TextUpdate_FontSizeChanged", tb2!.FontSize == 20);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Button update path
    // ════════════════════════════════════════════════════════════════════

    internal class ButtonPropertyUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            int clickCount = 0;
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var btn = phase == 0
                    ? Button("BtnBefore", () => clickCount++)
                    : Button("BtnAfter", () => clickCount += 10);
                return VStack(Button("UpdBtn", () => set(1)), btn);
            });

            await Harness.Render();
            H.Check("BtnUpdate_Initial", H.FindButton("BtnBefore") is not null);

            H.ClickButton("UpdBtn");
            await Harness.Render();

            H.Check("BtnUpdate_LabelChanged", H.FindButton("BtnAfter") is not null);
            H.Check("BtnUpdate_OldGone", H.FindButton("BtnBefore") is null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Input controls update paths
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Updates TextBox, CheckBox, Slider, ToggleSwitch, NumberBox, PasswordBox,
    /// and RadioButton in a single test to hit all their UpdateXxx methods.
    /// </summary>
    internal class InputControlsUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdInputs", () => set(1)),
                    phase == 0
                        ? VStack(
                            TextBox("hello", placeholder: "type here", header: "FieldHdr"),
                            CheckBox(false, label: "ChkLabel"),
                            Slider(25, 0, 100),
                            ToggleSwitch(false, header: "TsHdr"),
                            NumberBox(10, header: "NbHdr"),
                            PasswordBox("secret"),
                            RadioButton("RB1", isChecked: false),
                            RatingControl(2),
                            Progress(30),
                            ProgressRing(0.5)
                          )
                        : VStack(
                            TextBox("world", placeholder: "changed", header: "FieldHdr2"),
                            CheckBox(true, label: "ChkLabel2"),
                            Slider(75, 0, 100),
                            ToggleSwitch(true, header: "TsHdr2"),
                            NumberBox(99, header: "NbHdr2"),
                            PasswordBox("changed"),
                            RadioButton("RB1", isChecked: true),
                            RatingControl(4),
                            Progress(80),
                            ProgressRing(0.9)
                          )
                );
            });

            await Harness.Render();
            H.Check("InputUpdate_Initial", H.FindText("FieldHdr") is not null);

            H.ClickButton("UpdInputs");
            await Harness.Render();

            H.Check("InputUpdate_TextBoxHeader", H.FindText("FieldHdr2") is not null);
            H.Check("InputUpdate_CheckBoxLabel", H.FindText("ChkLabel2") is not null);
            H.Check("InputUpdate_ToggleSwitchHeader", H.FindText("TsHdr2") is not null);
            H.Check("InputUpdate_NumberBoxHeader", H.FindText("NbHdr2") is not null);

            var slider = H.FindControl<Slider>(s => s.Value > 50);
            H.Check("InputUpdate_SliderValue", slider is not null);

            var rating = H.FindControl<RatingControl>(r => r.Value == 4);
            H.Check("InputUpdate_RatingValue", rating is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Date/Time pickers update
    // ════════════════════════════════════════════════════════════════════

    internal class DateTimePickerUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var date = phase == 0
                    ? new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
                    : new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero);
                var time = phase == 0
                    ? TimeSpan.FromHours(9)
                    : TimeSpan.FromHours(17);
                return VStack(
                    Button("UpdDT", () => set(1)),
                    DatePicker(date),
                    TimePicker(time),
                    CalendarDatePicker(date)
                );
            });

            await Harness.Render();
            H.Check("DTUpdate_Initial",
                H.FindControl<DatePicker>(_ => true) is not null);

            H.ClickButton("UpdDT");
            await Harness.Render();

            H.Check("DTUpdate_DatePickerExists", H.FindControl<DatePicker>(_ => true) is not null);
            H.Check("DTUpdate_TimePickerExists", H.FindControl<TimePicker>(_ => true) is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Container update paths (Stack, Grid, Border, ScrollView, Expander)
    // ════════════════════════════════════════════════════════════════════

    internal class ContainerUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdContainers", () => set(1)),
                    Border(phase == 0 ? TextBlock("BdrChild1") : TextBlock("BdrChild2")),
                    ScrollView(phase == 0 ? TextBlock("SvChild1") : TextBlock("SvChild2")),
                    Expander("Exp", phase == 0 ? TextBlock("ExpChild1") : TextBlock("ExpChild2"), isExpanded: true),
                    VStack(phase == 0 ? TextBlock("StackChild1") : TextBlock("StackChild2")),
                    Canvas(phase == 0 ? TextBlock("CanvasChild1") : TextBlock("CanvasChild2")),
                    WrapGrid(phase == 0 ? TextBlock("WrapChild1") : TextBlock("WrapChild2"))
                );
            });

            await Harness.Render();
            H.Check("ContainerUpd_Initial",
                H.FindText("BdrChild1") is not null && H.FindText("SvChild1") is not null);

            H.ClickButton("UpdContainers");
            await Harness.Render();

            H.Check("ContainerUpd_Border", H.FindText("BdrChild2") is not null);
            H.Check("ContainerUpd_ScrollView", H.FindText("SvChild2") is not null);
            H.Check("ContainerUpd_Expander", H.FindText("ExpChild2") is not null);
            H.Check("ContainerUpd_Stack", H.FindText("StackChild2") is not null);
            H.Check("ContainerUpd_Canvas", H.FindText("CanvasChild2") is not null);
            H.Check("ContainerUpd_WrapGrid", H.FindText("WrapChild2") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Collection update paths (ListView, TreeView, FlipView)
    // ════════════════════════════════════════════════════════════════════

    internal class CollectionUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdColl", () => set(1)),
                    ListView(phase == 0
                        ? [TextBlock("LV_A"), TextBlock("LV_B")]
                        : [TextBlock("LV_A"), TextBlock("LV_B"), TextBlock("LV_C")]),
                    TreeView(phase == 0
                        ? [TreeNode("TV_Root")]
                        : [TreeNode("TV_Root", TreeNode("TV_Child"))]),
                    FlipView(phase == 0
                        ? [TextBlock("FV_1")]
                        : [TextBlock("FV_1"), TextBlock("FV_2")])
                );
            });

            await Harness.Render();
            H.Check("CollUpdate_Initial", H.FindText("LV_A") is not null);

            H.ClickButton("UpdColl");
            await Harness.Render();

            H.Check("CollUpdate_LVGrew", H.FindText("LV_C") is not null);
            H.Check("CollUpdate_TVPresent", H.FindText("TV_Root") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Navigation update paths
    // ════════════════════════════════════════════════════════════════════

    internal class NavigationUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdNav", () => set(1)),
                    BreadcrumbBar(phase == 0
                        ? [Breadcrumb("NavBC1")]
                        : [Breadcrumb("NavBC1"), Breadcrumb("NavBC2")]),
                    CommandBar(phase == 0
                        ? [AppBarButton("CmdA")]
                        : [AppBarButton("CmdA"), AppBarButton("CmdB")])
                );
            });

            await Harness.Render();
            H.Check("NavUpdate_Initial", H.FindText("NavBC1") is not null);

            H.ClickButton("UpdNav");
            await Harness.Render();

            H.Check("NavUpdate_BCGrew", H.FindText("NavBC2") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Modifier update paths (margin, padding, width, opacity, visibility)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tests ApplyModifiers by changing Width, Height, Margin, Padding, Opacity,
    /// Visibility, HorizontalAlignment, and CornerRadius during update.
    /// Exercises Reconciler.cs lines 462-600.
    /// </summary>
    internal class ModifierUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);

                var mods = phase == 0
                    ? new ElementModifiers
                    {
                        Width = 200,
                        Height = 50,
                        Margin = new Thickness(4),
                        Opacity = 1.0,
                        IsVisible = true,
                        HorizontalAlignment = HorizontalAlignment.Left,
                    }
                    : new ElementModifiers
                    {
                        Width = 400,
                        Height = 100,
                        Margin = new Thickness(8),
                        MinWidth = 100,
                        MinHeight = 30,
                        MaxWidth = 500,
                        MaxHeight = 200,
                        Opacity = 0.5,
                        IsVisible = true,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = "ModTooltip",
                        IsEnabled = false,
                    };

                return VStack(
                    Button("UpdMods", () => set(1)),
                    TextBlock("ModTarget") with { Modifiers = mods }
                );
            });

            await Harness.Render();
            var target = H.FindText("ModTarget");
            H.Check("ModUpdate_Initial", target is not null && target!.Width == 200);

            H.ClickButton("UpdMods");
            await Harness.Render();

            target = H.FindText("ModTarget");
            H.Check("ModUpdate_WidthChanged", target is not null && target!.Width == 400);
            H.Check("ModUpdate_HeightChanged", target!.Height == 100);
            H.Check("ModUpdate_OpacityChanged", target!.Opacity == 0.5);
            H.Check("ModUpdate_AlignChanged",
                target!.HorizontalAlignment == HorizontalAlignment.Center);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Modifier with padding on Control and Border
    // ════════════════════════════════════════════════════════════════════

    internal class PaddingModifierUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var padding = phase == 0 ? new Thickness(4) : new Thickness(16);
                return VStack(
                    Button("UpdPad", () => set(1)),
                    Border(TextBlock("PadBorder"))
                        .Padding(padding.Left)
                        .CornerRadius(phase == 0 ? 0 : 8),
                    Button("PadBtn") with
                    {
                        Modifiers = new ElementModifiers { Padding = padding }
                    }
                );
            });

            await Harness.Render();
            H.Check("PadUpdate_Initial", H.FindText("PadBorder") is not null);

            H.ClickButton("UpdPad");
            await Harness.Render();

            H.Check("PadUpdate_BorderPresent", H.FindText("PadBorder") is not null);
            H.Check("PadUpdate_BtnPresent", H.FindButton("PadBtn") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Shape update paths (Rectangle, Ellipse)
    // ════════════════════════════════════════════════════════════════════

    internal class ShapeUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                double size = phase == 0 ? 20 : 40;
                return VStack(
                    Button("UpdShape", () => set(1)),
                    new RectangleElement { Modifiers = new ElementModifiers { Width = size, Height = size } },
                    new EllipseElement { Modifiers = new ElementModifiers { Width = size, Height = size } }
                );
            });

            await Harness.Render();
            H.Check("ShapeUpdate_Initial",
                H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(_ => true) is not null);

            H.ClickButton("UpdShape");
            await Harness.Render();

            var rect = H.FindControl<Microsoft.UI.Xaml.Shapes.Rectangle>(_ => true);
            H.Check("ShapeUpdate_RectPresent", rect is not null);
            H.Check("ShapeUpdate_RectResized", rect!.Width == 40);

            var ellipse = H.FindControl<Microsoft.UI.Xaml.Shapes.Ellipse>(_ => true);
            H.Check("ShapeUpdate_EllipsePresent", ellipse is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  InfoBar / InfoBadge update paths
    // ════════════════════════════════════════════════════════════════════

    internal class StatusControlUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdStatus", () => set(1)),
                    InfoBar(phase == 0 ? "Info1" : "Info2", phase == 0 ? "Msg1" : "Msg2"),
                    InfoBadge(phase == 0 ? 1 : 99)
                );
            });

            await Harness.Render();
            H.Check("StatusUpd_Initial", H.FindText("Info1") is not null);

            H.ClickButton("UpdStatus");
            await Harness.Render();

            H.Check("StatusUpd_InfoBarChanged", H.FindText("Info2") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Grid update (definition + children change)
    // ════════════════════════════════════════════════════════════════════

    internal class GridUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdGrid", () => set(1)),
                    phase == 0
                        ? Grid([GridSize.Star(), GridSize.Star()], [GridSize.Star()],
                            TextBlock("G_A").Grid(row: 0, column: 0),
                            TextBlock("G_B").Grid(row: 0, column: 1))
                        : Grid([GridSize.Star(), GridSize.Star(), GridSize.Star()], [GridSize.Star()],
                            TextBlock("G_A").Grid(row: 0, column: 0),
                            TextBlock("G_B").Grid(row: 0, column: 1),
                            TextBlock("G_C").Grid(row: 0, column: 2))
                );
            });

            await Harness.Render();
            H.Check("GridUpd_Initial", H.FindText("G_A") is not null && H.FindText("G_B") is not null);

            H.ClickButton("UpdGrid");
            await Harness.Render();

            H.Check("GridUpd_ChildAdded", H.FindText("G_C") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ModifiedElement wrapping during update
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tests the ModifiedElement unwrapping path in Update() (lines 16-28).
    /// When an element is wrapped with fluent modifiers (.Width(), .Margin(), etc.),
    /// the reconciler must unwrap ModifiedElement before dispatching to UpdateXxx.
    /// </summary>
    internal class ModifiedElementUnwrap(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdModified", () => set(1)),
                    phase == 0
                        ? TextBlock("MW_Before").Width(100).Margin(4)
                        : TextBlock("MW_After").Width(200).Margin(8)
                );
            });

            await Harness.Render();
            var tb = H.FindText("MW_Before");
            H.Check("ModWrap_Initial", tb is not null);

            H.ClickButton("UpdModified");
            await Harness.Render();

            var tb2 = H.FindText("MW_After");
            H.Check("ModWrap_Updated", tb2 is not null);
            H.Check("ModWrap_WidthApplied", tb2!.Width == 200);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  HyperlinkButton update
    // ════════════════════════════════════════════════════════════════════

    internal class HyperlinkButtonUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdHL", () => set(1)),
                    HyperlinkButton(phase == 0 ? "Link1" : "Link2")
                );
            });

            await Harness.Render();
            H.Check("HLUpdate_Initial", H.FindText("Link1") is not null);

            H.ClickButton("UpdHL");
            await Harness.Render();

            H.Check("HLUpdate_Changed", H.FindText("Link2") is not null);
        }
    }
}
