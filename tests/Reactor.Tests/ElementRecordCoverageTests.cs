using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Element.cs is mostly small record types whose constructors and default
/// initializers don't get exercised unless the type is actually instantiated.
/// Each test below builds one of the rarely-used data records to cover the
/// trailing <c>internal Action&lt;X&gt;[] Setters = [];</c> initializers and
/// secondary IconData/ItemData record bodies.
/// </summary>
public class ElementRecordCoverageTests
{
    [Fact]
    public void Icon_Data_Variants_Construct()
    {
        var sym = new SymbolIconData("Home");
        var fnt = new FontIconData("\uE700", "Segoe Fluent Icons", 16);
        var bmp = new BitmapIconData(new Uri("ms-appx:///icon.png"), ShowAsMonochrome: false);
        var pth = new PathIconData("M 0,0 L 10,10");
        var img = new ImageIconData(new Uri("ms-appx:///image.png"));
        Assert.Equal("Home", sym.Symbol);
        Assert.Equal("\uE700", fnt.Glyph);
        Assert.False(bmp.ShowAsMonochrome);
        Assert.Equal("M 0,0 L 10,10", pth.Data);
        Assert.NotNull(img.Source);
    }

    [Fact]
    public void IconElement_Wraps_IconData()
    {
        var sym = new SymbolIconData("Home");
        var el = new IconElement(sym);
        Assert.Same(sym, el.Data);
        Assert.Empty(el.Setters);
    }

    [Fact]
    public void IconElement_With_Expression_Preserves_Data()
    {
        var el = new IconElement(new FontIconData("\uE700", "Segoe Fluent Icons", 16));
        var copy = el with { Setters = [_ => { }] };
        Assert.Same(el.Data, copy.Data);
        Assert.Single(copy.Setters);
    }

    [Fact]
    public void Icon_Factory_From_IconData()
    {
        var data = new PathIconData("M 0,0 L 10,10");
        var el = Factories.Icon(data);
        Assert.IsType<PathIconData>(el.Data);
        Assert.Equal("M 0,0 L 10,10", ((PathIconData)el.Data).Data);
    }

    [Fact]
    public void Icon_Factory_From_String_Creates_SymbolIconData()
    {
        var el = Factories.Icon("Home");
        Assert.IsType<SymbolIconData>(el.Data);
        Assert.Equal("Home", ((SymbolIconData)el.Data).Symbol);
    }

    [Fact]
    public void Icon_Factory_From_Symbol_Enum_Creates_SymbolIconData()
    {
        var el = Factories.Icon(Microsoft.UI.Xaml.Controls.Symbol.Home);
        Assert.IsType<SymbolIconData>(el.Data);
        Assert.Equal("Home", ((SymbolIconData)el.Data).Symbol);
    }

    [Fact]
    public void AppBar_Data_Variants_Construct()
    {
        var btn = new AppBarButtonData("Open", () => { }, "Open");
        var toggle = new AppBarToggleButtonData("Pin", IsChecked: true);
        var sep = new AppBarSeparatorData();
        Assert.Equal("Open", btn.Label);
        Assert.True(toggle.IsChecked);
        Assert.NotNull(sep);
    }

    [Fact]
    public void MenuFlyout_Data_Variants_Construct()
    {
        var item = new MenuFlyoutItemData("New");
        var sep = new MenuFlyoutSeparatorData();
        var sub = new MenuFlyoutSubItemData("Recent", new MenuFlyoutItemBase[] { item });
        var toggle = new ToggleMenuFlyoutItemData("AutoSave", IsChecked: true);
        var radio = new RadioMenuFlyoutItemData("Sort", "GroupA", IsChecked: false);
        Assert.Equal("New", item.Text);
        Assert.NotNull(sep);
        Assert.Equal("Recent", sub.Text);
        Assert.True(toggle.IsChecked);
        Assert.Equal("GroupA", radio.GroupName);
    }

    [Fact]
    public void TabView_Pivot_Item_Data_Construct()
    {
        var tvi = new TabViewItemData("Tab1", new TextBlockElement("x")) { Icon = "PinFavorite", IsClosable = false };
        Assert.Equal("Tab1", tvi.Header);
        Assert.False(tvi.IsClosable);
        var pi = new PivotItemData("Pivot1", new TextBlockElement("x"));
        Assert.Equal("Pivot1", pi.Header);
        var bb = new BreadcrumbBarItemData("Crumb", Tag: "tag1");
        Assert.Equal("Crumb", bb.Label);
        var tn = new TreeViewNodeData("Node", null) { IsExpanded = true };
        Assert.True(tn.IsExpanded);
    }

    [Fact]
    public void Input_Element_Records_Construct()
    {
        // Drives the Setters initializer for input elements that aren't
        // instantiated by other tests.
        Assert.NotNull(new ToggleSplitButtonElement("X").Setters);
        Assert.NotNull(new AutoSuggestBoxElement("Q") { Suggestions = new[] { "A", "B" } }.Setters);
        Assert.NotNull(new RadioButtonsElement(new[] { "A", "B" }, 0).Setters);
        Assert.NotNull(new ComboBoxElement(new[] { "A" }) { PlaceholderText = "P" }.Setters);
        Assert.NotNull(new RadioButtonElement("X").Setters);
        Assert.NotNull(new ColorPickerElement(global::Windows.UI.Color.FromArgb(255, 0, 0, 0)).Setters);
    }

    [Fact]
    public void SemanticDescription_Record_Construct()
    {
        var s = new SemanticDescription(
            Role: "slider", Value: "3", RangeMin: 0, RangeMax: 5, RangeValue: 3, IsReadOnly: false);
        Assert.Equal("slider", s.Role);
        Assert.False(s.IsReadOnly);
    }
}
