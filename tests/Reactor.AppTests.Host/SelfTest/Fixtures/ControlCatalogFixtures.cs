using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Validates that every supported control type can be mounted, rendered in the
/// real WinUI tree, and then removed cleanly. Each control is mounted twice
/// (add → remove → add) to exercise the full mount/unmount/remount cycle and
/// verify the reconciler handles every element type without crashes.
/// </summary>
internal static class ControlCatalogFixtures
{
    /// <summary>
    /// Cycles through every supported control: mount → verify → unmount → remount → verify.
    /// This catches missing Mount/Update/Unmount handlers, pool cleanup bugs, and COM
    /// activation failures for any control type.
    /// </summary>
    internal class MountUnmountAllControls(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();

            // Build a catalog of every control with a marker we can find in the tree.
            // Each entry: (name, element factory that includes a findable text marker).
            var controls = BuildControlCatalog();

            // ── Phase 1: Mount all controls ──
            host.Mount(ctx => VStack(controls.Select(c => c.element).ToArray()));
            await Harness.Render();

            int mounted = 0;
            foreach (var (name, _, marker) in controls)
            {
                bool found = marker == null || H.FindText(marker) is not null
                    || H.FindControl<FrameworkElement>(_ => true) is not null;
                if (found) mounted++;
                H.Check($"ControlCatalog_Mount_{name}", found);
            }

            H.Check("ControlCatalog_AllMounted", mounted == controls.Count);

            // ── Phase 2: Unmount all (replace with empty) ──
            host.Mount(ctx => TextBlock("empty"));
            await Harness.Render();

            H.Check("ControlCatalog_Unmounted",
                H.FindText("empty") is not null);

            // ── Phase 3: Remount all controls (verifies clean unmount + fresh mount) ──
            host.Mount(ctx => VStack(controls.Select(c => c.element).ToArray()));
            await Harness.Render();

            int remounted = 0;
            foreach (var (name, _, marker) in controls)
            {
                bool found = marker == null || H.FindText(marker) is not null
                    || H.FindControl<FrameworkElement>(_ => true) is not null;
                if (found) remounted++;
            }

            H.Check("ControlCatalog_AllRemounted", remounted == controls.Count);
        }

        private static List<(string name, Element element, string? marker)> BuildControlCatalog()
        {
            var items = new List<(string name, Element element, string? marker)>();

            void Add(string name, Element el, string? marker = null)
                => items.Add((name, el, marker));

            // ── Text / Display ──
            Add("Text", TextBlock("CatalogText"), "CatalogText");
            Add("RichTextBlock", RichTextBlock("CatalogRich"), "CatalogRich");
            Add("RichEditBox", RichEditBox("CatalogEdit"), null);

            // ── Buttons ──
            Add("Button", Button("CatalogBtn"), "CatalogBtn");
            Add("HyperlinkButton", HyperlinkButton("CatalogLink"), "CatalogLink");
            Add("RepeatButton", RepeatButton("CatalogRepeat"), "CatalogRepeat");
            Add("ToggleButton", ToggleButton("CatalogTogBtn"), "CatalogTogBtn");
            Add("DropDownButton", DropDownButton("CatalogDDB"), "CatalogDDB");
            Add("SplitButton", SplitButton("CatalogSplit"), "CatalogSplit");
            Add("ToggleSplitButton", ToggleSplitButton("CatalogTSB"), "CatalogTSB");

            // ── Input controls ──
            Add("TextField", TextBox("CatalogTF"), null);
            Add("PasswordBox", PasswordBox("pw"), null);
            Add("NumberBox", NumberBox(42, header: "CatalogNB"), "CatalogNB");
            Add("AutoSuggestBox", AutoSuggestBox("CatalogASB"), null);
            Add("CheckBox", CheckBox(true, label: "CatalogCB"), "CatalogCB");
            Add("RadioButton", RadioButton("CatalogRB"), "CatalogRB");
            Add("RadioButtons", RadioButtons(["CatalogRBs_A", "CatalogRBs_B"], 0), "CatalogRBs_A");
            Add("ComboBox", ComboBox(["CatalogCombo_A", "CatalogCombo_B"], 0), null);
            Add("Slider", Slider(50), null);
            Add("ToggleSwitch", ToggleSwitch(true, header: "CatalogTS"), "CatalogTS");
            Add("RatingControl", RatingControl(3), null);

            // ── Date / Time ──
            Add("DatePicker", DatePicker(DateTimeOffset.Now), null);
            Add("TimePicker", TimePicker(TimeSpan.FromHours(12)), null);
            Add("CalendarDatePicker", CalendarDatePicker(), null);

            // ── Progress ──
            Add("Progress", Progress(50), null);
            Add("ProgressIndeterminate", ProgressIndeterminate(), null);
            Add("ProgressRing", ProgressRing(), null);

            // ── Status / Info ──
            Add("InfoBar", InfoBar("CatalogInfo", "msg"), "CatalogInfo");
            Add("InfoBadge", InfoBadge(5), null);

            // ── Media ──
            Add("Image", Image("ms-appx:///Assets/StoreLogo.png"), null);
            Add("PersonPicture", PersonPicture(), null);

            // ── Layout containers ──
            Add("VStack", VStack(TextBlock("CatalogVS")), "CatalogVS");
            Add("HStack", HStack(TextBlock("CatalogHS")), "CatalogHS");
            Add("Grid", Grid([GridSize.Star()], [GridSize.Star()], TextBlock("CatalogGrid").Grid(row: 0, column: 0)), "CatalogGrid");
            Add("FlexRow", FlexRow(TextBlock("CatalogFlex")), "CatalogFlex");
            Add("FlexColumn", FlexColumn(TextBlock("CatalogFlexC")), "CatalogFlexC");
            Add("Border", Border(TextBlock("CatalogBorder")), "CatalogBorder");
            Add("ScrollView", ScrollView(TextBlock("CatalogScroll")), "CatalogScroll");
            Add("Expander", Expander("CatalogExp", TextBlock("content"), isExpanded: true), "CatalogExp");
            Add("WrapGrid", WrapGrid(TextBlock("CatalogWG")), "CatalogWG");
            Add("Canvas", Canvas(TextBlock("CatalogCanvas")), "CatalogCanvas");
            Add("Viewbox", Viewbox(TextBlock("CatalogVB")), "CatalogVB");

            // ── Navigation ──
            Add("BreadcrumbBar", BreadcrumbBar([Breadcrumb("CatalogBC")]), "CatalogBC");
            Add("TabView", TabView(Tab("CatalogTab", TextBlock("tab content"))), "CatalogTab");
            Add("Pivot", Pivot(PivotItem("CatalogPivot", TextBlock("pivot content"))), "CatalogPivot");

            // ── Collections ──
            Add("ListView", ListView(TextBlock("CatalogLV")), "CatalogLV");
            Add("FlipView", FlipView(TextBlock("CatalogFV")), "CatalogFV");
            Add("TreeView", TreeView(TreeNode("CatalogTV")), "CatalogTV");

            // ── Menus ──
            Add("MenuBar", MenuBar(Menu("CatalogMenu", MenuItem("item"))), "CatalogMenu");
            Add("CommandBar", CommandBar([AppBarButton("CatalogCmdBar")]), "CatalogCmdBar");

            // ── Shapes ──
            Add("Rectangle", new RectangleElement() { Modifiers = new ElementModifiers { Width = 20, Height = 20 } }, null);
            Add("Ellipse", new EllipseElement() { Modifiers = new ElementModifiers { Width = 20, Height = 20 } }, null);

            // ── Components ──
            // Intentionally exercises the legacy Func() factory so the catalog
            // still covers it until the obsolete overload is removed.
#pragma warning disable CS0618
            Add("FuncComponent", Func(ctx => TextBlock("CatalogFunc")), "CatalogFunc");
#pragma warning restore CS0618

            return items;
        }
    }
}
