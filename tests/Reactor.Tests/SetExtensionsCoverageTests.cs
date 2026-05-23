using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests all uncovered .Set() extension methods on various element types.
/// Each Set() appends a native-property setter delegate. We verify the setter
/// is stored (not that it runs — that's a reconciler test).
/// </summary>
public class SetExtensionsCoverageTests
{
    // ── Input controls ──────────────────────────────────────────────

    [Fact]
    public void Set_TextField_Appends_Setter()
    {
        var el = TextBox("val").Set(tb => tb.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_PasswordBox_Appends_Setter()
    {
        var el = PasswordBox("pw").Set(pb => pb.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_NumberBox_Appends_Setter()
    {
        var el = NumberBox(0).Set(nb => nb.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_AutoSuggestBox_Appends_Setter()
    {
        var el = AutoSuggestBox("q").Set(asb => asb.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_CheckBox_Appends_Setter()
    {
        var el = CheckBox(false).Set(cb => cb.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_RadioButton_Appends_Setter()
    {
        var el = RadioButton("opt").Set(rb => rb.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_RadioButtons_Appends_Setter()
    {
        var el = RadioButtons(["a", "b"]).Set(rb => rb.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_ComboBox_Appends_Setter()
    {
        var el = ComboBox(["a", "b"]).Set(cb => cb.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_Slider_Appends_Setter()
    {
        var el = Slider(50).Set(s => s.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_ToggleSwitch_Appends_Setter()
    {
        var el = ToggleSwitch(true).Set(ts => ts.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_RatingControl_Appends_Setter()
    {
        var el = RatingControl(3).Set(rc => rc.IsReadOnly = true);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_ColorPicker_Appends_Setter()
    {
        var el = ColorPicker(global::Windows.UI.Color.FromArgb(255, 0, 0, 0))
            .Set(cp => cp.IsAlphaEnabled = false);
        Assert.Single(el.Setters);
    }

    // ── Date/Time ───────────────────────────────────────────────────

    [Fact]
    public void Set_CalendarDatePicker_Appends_Setter()
    {
        var el = CalendarDatePicker().Set(cdp => cdp.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_DatePicker_Appends_Setter()
    {
        var el = DatePicker(DateTimeOffset.Now).Set(dp => dp.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_TimePicker_Appends_Setter()
    {
        var el = TimePicker(TimeSpan.FromHours(12)).Set(tp => tp.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    // ── Progress ────────────────────────────────────────────────────

    [Fact]
    public void Set_Progress_Appends_Setter()
    {
        var el = Progress(50).Set(pb => pb.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_ProgressRing_Appends_Setter()
    {
        var el = ProgressRing().Set(pr => pr.IsActive = true);
        Assert.Single(el.Setters);
    }

    // ── Media ───────────────────────────────────────────────────────

    [Fact]
    public void Set_Image_Appends_Setter()
    {
        var el = Image("test.png").Set(img => img.Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_PersonPicture_Appends_Setter()
    {
        var el = PersonPicture().Set(pp => pp.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    // ── Layout ──────────────────────────────────────────────────────

    [Fact]
    public void Set_Stack_Appends_Setter()
    {
        var el = VStack(TextBlock("a"))
            .Set(sp => sp.Spacing = 8);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_Grid_Appends_Setter()
    {
        var el = Grid(new[] { GridSize.Auto }, new[] { GridSize.Star() }, TextBlock("a"))
            .Set(g => g.RowSpacing = 4);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_ScrollView_Appends_Setter()
    {
        var el = ScrollViewer(TextBlock("content"))
            .Set(sv => sv.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_Border_Appends_Setter()
    {
        var el = Border(TextBlock("inner"))
            .Set(b => b.IsHitTestVisible = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_Expander_Appends_Setter()
    {
        var el = Expander("hdr", TextBlock("cnt"))
            .Set(e => e.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_SplitView_Appends_Setter()
    {
        var el = SplitView().Set(sv => sv.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_Viewbox_Appends_Setter()
    {
        var el = Viewbox(TextBlock("a"))
            .Set(vb => vb.Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_Canvas_Appends_Setter()
    {
        var el = Canvas(TextBlock("a"))
            .Set(c => c.Background = null);
        Assert.Single(el.Setters);
    }

    // ── Navigation ──────────────────────────────────────────────────

    [Fact]
    public void Set_NavigationView_Appends_Setter()
    {
        var el = NavigationView([]).Set(nv => nv.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_TabView_Appends_Setter()
    {
        var el = TabView().Set(tv => tv.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_BreadcrumbBar_Appends_Setter()
    {
        var el = BreadcrumbBar([]).Set(bb => bb.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_Pivot_Appends_Setter()
    {
        var el = Pivot().Set(p => p.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    // ── Collections ─────────────────────────────────────────────────

    [Fact]
    public void Set_ListView_Appends_Setter()
    {
        var el = ListView(TextBlock("a")).Set(lv => lv.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_GridView_Appends_Setter()
    {
        var el = GridView(TextBlock("a")).Set(gv => gv.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_TreeView_Appends_Setter()
    {
        var el = TreeView().Set(tv => tv.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_FlipView_Appends_Setter()
    {
        var el = FlipView(TextBlock("a")).Set(fv => fv.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    // ── Dialogs / Overlays ──────────────────────────────────────────

    [Fact]
    public void Set_ContentDialog_Appends_Setter()
    {
        var el = ContentDialog("title", TextBlock("body"))
            .Set(cd => cd.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_Flyout_Appends_Setter()
    {
        var el = Flyout(TextBlock("target"), TextBlock("content"))
            .Set(f => f.Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_TeachingTip_Appends_Setter()
    {
        var el = TeachingTip("title").Set(tt => tt.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_InfoBar_Appends_Setter()
    {
        var el = InfoBar("title").Set(ib => ib.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_InfoBadge_Appends_Setter()
    {
        var el = InfoBadge(3).Set(ib => ib.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    // ── Menus ───────────────────────────────────────────────────────

    [Fact]
    public void Set_MenuBar_Appends_Setter()
    {
        var el = MenuBar().Set(mb => mb.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_CommandBar_Appends_Setter()
    {
        var el = CommandBar().Set(cb => cb.IsEnabled = false);
        Assert.Single(el.Setters);
    }

    [Fact]
    public void Set_MenuFlyout_Appends_Setter()
    {
        var el = MenuFlyout(TextBlock("target"))
            .Set(mf => mf.Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top);
        Assert.Single(el.Setters);
    }
}
