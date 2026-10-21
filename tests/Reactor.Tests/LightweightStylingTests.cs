using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Elements;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for lightweight styling (ResourceBuilder, ResourceOverrides, and the
/// Resources() fluent extension method). Pure C# record tests — no WinUI activation
/// needed for the builder/record tests.
/// </summary>
public class LightweightStylingTests
{
    // ════════════════════════════════════════════════════════════════
    //  ResourceBuilder
    // ════════════════════════════════════════════════════════════════

    // ResourceBuilder_Set_String_Creates_Literal_Brush moved to selfhost fixtures (WinUIActivationFixtures).

    [Fact]
    public void ResourceBuilder_Set_ThemeRef_Creates_ThemeRef_Entry()
    {
        var builder = new ResourceBuilder();
        builder.Set("ButtonBackground", Theme.Accent);
        var overrides = builder.Build();

        Assert.Empty(overrides.Literals);
        Assert.Single(overrides.ThemeRefs);
        Assert.Equal("AccentFillColorDefaultBrush", overrides.ThemeRefs["ButtonBackground"].ResourceKey);
    }

    [Fact]
    public void ResourceBuilder_Set_Double_Creates_Literal()
    {
        var builder = new ResourceBuilder();
        builder.Set("ButtonBorderThemeThickness", 2.0);
        var overrides = builder.Build();

        Assert.Single(overrides.Literals);
        Assert.Equal(2.0, overrides.Literals["ButtonBorderThemeThickness"]);
    }

    [Fact]
    public void ResourceBuilder_Set_CornerRadius_Creates_Literal()
    {
        var builder = new ResourceBuilder();
        builder.Set("ControlCornerRadius", new CornerRadius(8));
        var overrides = builder.Build();

        Assert.Single(overrides.Literals);
        Assert.Equal(new CornerRadius(8), overrides.Literals["ControlCornerRadius"]);
    }

    // ResourceBuilder_Fluent_Chaining_Works moved to selfhost fixtures (WinUIActivationFixtures).

    [Fact]
    public void ResourceBuilder_Set_Thickness_Preserves_All_Four_Sides()
    {
        var value = new Thickness(8, 4, 6, 2);
        var builder = new ResourceBuilder();
        Assert.Same(builder, builder.Set("BreadcrumbBarChevronPadding", value));

        var overrides = builder.Build();
        Assert.Equal(value, Assert.IsType<Thickness>(overrides.Literals["BreadcrumbBarChevronPadding"]));
        Assert.Single(overrides.Literals);
        Assert.Empty(overrides.ThemeRefs);
    }

    [Fact]
    public void ResourceBuilder_Set_FontWeight_Preserves_Weight()
    {
        var value = new global::Windows.UI.Text.FontWeight(700);
        var builder = new ResourceBuilder();
        Assert.Same(builder, builder.Set("BreadcrumbBarItemFontWeight", value));
        Assert.Equal(value, Assert.IsType<global::Windows.UI.Text.FontWeight>(
            builder.Build().Literals["BreadcrumbBarItemFontWeight"]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResourceBuilder_Set_Boolean_Preserves_Value(bool value)
    {
        var builder = new ResourceBuilder();
        Assert.Same(builder, builder.Set("GridViewItemSelectionCheckMarkVisualEnabled", value));
        Assert.Equal(value, Assert.IsType<bool>(builder.Build().Literals["GridViewItemSelectionCheckMarkVisualEnabled"]));
    }

    [Fact]
    public void ResourceBuilder_Set_Color_Preserves_Channels_Without_Brush_Conversion()
    {
        var value = global::Windows.UI.Color.FromArgb(127, 12, 34, 56);
        var builder = new ResourceBuilder();
        Assert.Same(builder, builder.SetColor("AccentFillColorDisabled", value));
        Assert.Equal(value, Assert.IsType<global::Windows.UI.Color>(builder.Build().Literals["AccentFillColorDisabled"]));
    }

    [Theory]
    [InlineData(GridUnitType.Auto, 1)]
    [InlineData(GridUnitType.Pixel, 12)]
    [InlineData(GridUnitType.Star, 2)]
    public void ResourceBuilder_Set_GridLength_Preserves_Unit_And_Value(GridUnitType unit, double size)
    {
        var value = new GridLength(size, unit);
        var builder = new ResourceBuilder();
        Assert.Same(builder, builder.Set("ContentDialogButtonSpacing", value));
        Assert.Equal(value, Assert.IsType<GridLength>(builder.Build().Literals["ContentDialogButtonSpacing"]));
    }

    [Fact]
    public void ResourceBuilder_Set_Integer_Still_Uses_Double_Overload()
    {
        var builder = new ResourceBuilder();
        builder.Set("BreadcrumbBarItemThemeFontSize", 28);
        Assert.Equal(28d, Assert.IsType<double>(builder.Build().Literals["BreadcrumbBarItemThemeFontSize"]));
    }

    [Fact]
    public void Typed_Resource_Equality_Uses_Values_Not_Box_Identity()
    {
        Check(r => r.Set("x", new Thickness(1, 2, 3, 4)), r => r.Set("x", new Thickness(4, 3, 2, 1)));
        Check(r => r.Set("x", new global::Windows.UI.Text.FontWeight(700)), r => r.Set("x", new global::Windows.UI.Text.FontWeight(400)));
        Check(r => r.Set("x", true), r => r.Set("x", false));
        Check(r => r.SetColor("x", global::Windows.UI.Color.FromArgb(127, 12, 34, 56)),
            r => r.SetColor("x", global::Windows.UI.Color.FromArgb(255, 12, 34, 56)));
        Check(r => r.Set("x", new GridLength(2, GridUnitType.Star)), r => r.Set("x", new GridLength(2, GridUnitType.Pixel)));

        static void Check(Action<ResourceBuilder> same, Action<ResourceBuilder> different)
        {
            var first = TextBlock("value").Resources(same);
            Assert.True(Element.ShallowEquals(first, TextBlock("value").Resources(same)));
            Assert.False(Element.ShallowEquals(first, TextBlock("value").Resources(different)));
        }
    }

    [Fact]
    public void ResourceBuilder_Replacing_Typed_Value_Does_Not_Mutate_Earlier_Snapshot()
    {
        var builder = new ResourceBuilder();
        var first = new Thickness(1, 2, 3, 4);
        var second = new Thickness(8, 4, 8, 0);
        builder.Set("BreadcrumbBarChevronPadding", first);
        var before = builder.Build();
        builder.Set("BreadcrumbBarChevronPadding", second);
        var after = builder.Build();

        Assert.Equal(first, Assert.IsType<Thickness>(before.Literals["BreadcrumbBarChevronPadding"]));
        Assert.Equal(second, Assert.IsType<Thickness>(after.Literals["BreadcrumbBarChevronPadding"]));
        Assert.Single(after.Literals);
    }

    [Fact]
    public void Resources_Extension_Chains_Typed_Values_And_ThemeRef()
    {
        var padding = new Thickness(8, 4, 8, 0);
        var weight = new global::Windows.UI.Text.FontWeight(700);
        var el = Button("Go").Resources(r => r
            .Set("BreadcrumbBarChevronPadding", padding)
            .Set("BreadcrumbBarItemFontWeight", weight)
            .Set("ButtonBackground", Theme.Accent));

        Assert.Equal(padding, Assert.IsType<Thickness>(el.ResourceOverrides!.Literals["BreadcrumbBarChevronPadding"]));
        Assert.Equal(weight, Assert.IsType<global::Windows.UI.Text.FontWeight>(el.ResourceOverrides.Literals["BreadcrumbBarItemFontWeight"]));
        Assert.Equal(2, el.ResourceOverrides.Literals.Count);
        Assert.Single(el.ResourceOverrides.ThemeRefs);
        Assert.Equal(Theme.Accent, el.ResourceOverrides.ThemeRefs["ButtonBackground"]);
    }

    [Fact]
    public void ResourceBuilder_Build_Returns_Immutable_Snapshot()
    {
        var builder = new ResourceBuilder();
        builder.Set("A", 1.0);
        var snapshot1 = builder.Build();

        builder.Set("B", 2.0);
        var snapshot2 = builder.Build();

        // First snapshot should not be affected by subsequent additions
        Assert.Single(snapshot1.Literals);
        Assert.Equal(2, snapshot2.Literals.Count);
    }

    // ════════════════════════════════════════════════════════════════
    //  ResourceOverrides record
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ResourceOverrides_AllKeys_Returns_Union()
    {
        var overrides = new ResourceOverrides(
            new Dictionary<string, object> { ["A"] = 1.0, ["B"] = 2.0 },
            new Dictionary<string, ThemeRef> { ["B"] = Theme.Accent, ["C"] = Theme.PrimaryText });

        var allKeys = overrides.AllKeys.ToHashSet();
        Assert.Equal(3, allKeys.Count);
        Assert.Contains("A", allKeys);
        Assert.Contains("B", allKeys);
        Assert.Contains("C", allKeys);
    }

    // ════════════════════════════════════════════════════════════════
    //  Element.Resources() extension method
    // ════════════════════════════════════════════════════════════════

    // Resources_Extension_Sets_ResourceOverrides_On_Element and
    // Resources_Extension_Preserves_Other_Element_Properties_Brush moved to
    // selfhost fixtures (WinUIActivationFixtures).

    [Fact]
    public void Resources_Extension_Preserves_Other_Element_Properties()
    {
        var el = Button("Go", () => { })
            .Margin(10)
            .Resources(r => r.Set("ButtonForeground", Theme.PrimaryText));

        Assert.Equal("Go", el.Label);
        Assert.NotNull(el.OnClick);
        Assert.Equal(new Thickness(10), el.Modifiers!.Margin);
        Assert.NotNull(el.ResourceOverrides);
    }

    [Fact]
    public void Resources_Extension_Works_With_ThemeRef_Overrides()
    {
        var el = Button("Go").Resources(r => r
            .Set("ButtonBackground", Theme.Accent)
            .Set("ButtonForeground", Theme.PrimaryText));

        Assert.Equal(2, el.ResourceOverrides!.ThemeRefs.Count);
        Assert.Empty(el.ResourceOverrides.Literals);
    }

    // Resources_Extension_Works_With_Mixed_Literal_And_ThemeRef moved to
    // selfhost fixtures (WinUIActivationFixtures).

    [Fact]
    public void Resources_Extension_Works_With_Double_And_CornerRadius()
    {
        var el = Button("Go").Resources(r => r
            .Set("ButtonBorderThemeThickness", 2.0)
            .Set("ControlCornerRadius", new CornerRadius(4)));

        Assert.Equal(2, el.ResourceOverrides!.Literals.Count);
        Assert.Empty(el.ResourceOverrides.ThemeRefs);
    }

    [Fact]
    public void Removing_Resources_Sets_Null()
    {
        var el1 = Button("Go").Resources(r => r.Set("ButtonForeground", Theme.PrimaryText));
        Assert.NotNull(el1.ResourceOverrides);

        // Without Resources(), ResourceOverrides should be null (default)
        var el2 = Button("Go");
        Assert.Null(el2.ResourceOverrides);
    }
}
