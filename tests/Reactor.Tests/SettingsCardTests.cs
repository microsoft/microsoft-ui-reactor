using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Handlers;
using Xunit;
using Regex = global::System.Text.RegularExpressions.Regex;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 065 — the pure parts of SettingsCard and SettingsExpander: the Toolkit's layout state
/// rules, the factory overloads, the expander's item mapping and the markup substitution.
/// Mounting, visual states, theming and UI Automation run against live WinUI controls in the
/// <c>SettingsCard_*</c> / <c>SettingsExpander_*</c> selftests.
/// </summary>
public sealed class SettingsCardTests
{
    [Theory]
    [InlineData(SettingsCardContentAlignment.Right, double.NaN, "Right")]
    [InlineData(SettingsCardContentAlignment.Right, 640, "Right")]
    [InlineData(SettingsCardContentAlignment.Right, 476, "Right")]
    [InlineData(SettingsCardContentAlignment.Right, 475.9, "RightWrapped")]
    [InlineData(SettingsCardContentAlignment.Right, 286, "RightWrapped")]
    [InlineData(SettingsCardContentAlignment.Right, 285.9, "RightWrappedNoIcon")]
    [InlineData(SettingsCardContentAlignment.Vertical, 640, "Vertical")]
    [InlineData(SettingsCardContentAlignment.Vertical, 400, "RightWrapped")]
    [InlineData(SettingsCardContentAlignment.Left, 640, "Left")]
    [InlineData(SettingsCardContentAlignment.Left, 250, "RightWrappedNoIcon")]
    [InlineData(SettingsCardContentAlignment.Vertical, double.NaN, "Vertical")]
    public void ContentAlignmentState_FollowsTheToolkitTriggers(SettingsCardContentAlignment alignment, double width, string expected)
        => Assert.Equal(expected, SettingsCardControl.ResolveContentAlignmentState(alignment, width));

    [Theory]
    [InlineData("RightWrapped", true, true, true)]
    [InlineData("RightWrappedNoIcon", true, true, true)]
    [InlineData("Vertical", true, true, true)]
    [InlineData("Right", true, true, false)]
    [InlineData("Left", true, true, false)]
    [InlineData("Vertical", false, true, false)]
    [InlineData("Vertical", true, false, false)]
    public void ContentSpacing_OnlyWhenStackedBelowAHeader(string state, bool hasContent, bool hasHeader, bool expected)
        => Assert.Equal(expected, SettingsCardControl.NeedsContentSpacing(state, hasContent, hasHeader));

    [Fact]
    public void StringOverload_FillsTheCard()
    {
        Action open = () => { };
        var icon = FontIcon("\uE7E7");
        var card = SettingsCard("Notifications", description: "Alerts", icon: icon, content: TextBlock("x"), onClick: open);

        Assert.Equal("Notifications", card.Header);
        Assert.Equal("Alerts", card.Description);
        Assert.Same(icon, card.Icon);
        Assert.Same(open, card.OnClick);
        Assert.True(card.IsClickable);
        Assert.Null(card.HeaderContent);
        Assert.Equal(SettingsCardContentAlignment.Right, card.ContentAlignment);
        Assert.True(card.IsActionIconVisible);
        Assert.False(SettingsCard("Plain").IsClickable);
    }

    [Fact]
    public void ElementOverload_KeepsPlainTextAsTextAndRichContentAsElements()
    {
        var link = HyperlinkButton("Learn more");
        var card = SettingsCard("Header", description: link);

        Assert.Equal("Header", card.Header);
        Assert.Null(card.HeaderContent);
        Assert.Null(card.Description);
        Assert.Same(link, card.DescriptionContent);

        var bold = TextBlock("Bold").Bold();
        var styled = SettingsCard(bold, description: TextBlock("Plain description"));
        Assert.Null(styled.Header);
        Assert.Same(bold, styled.HeaderContent);
        Assert.Equal("Plain description", styled.Description);
        Assert.Null(styled.DescriptionContent);

        var named = SettingsCard(TextBlock("Named").AutomationName("Other"));
        Assert.Null(named.Header);
        Assert.NotNull(named.HeaderContent);
    }

    [Fact]
    public void CommandOverload_MapsLabelDescriptionIconActionAndEnabledState()
    {
        var runs = 0;
        var icon = FontIcon("\uE895");
        var command = new Command { Label = "Sync", Description = "Last synced now", Icon = icon, Execute = () => runs++ };
        var card = SettingsCard(command);

        Assert.Equal("Sync", card.Header);
        Assert.Equal("Last synced now", card.Description);
        Assert.Same(icon, card.Icon);
        Assert.Same(command, card.Command);
        Assert.Null(card.OnClick);
        Assert.True(card.IsClickable);
        Assert.True(card.HasCallbacks);

        Assert.False(SettingsCard(command with { Execute = null }).IsClickable);
        Assert.True(SettingsCard(new Command { Label = "Async", ExecuteAsync = () => Task.CompletedTask }).IsClickable);
        Assert.Throws<ArgumentNullException>(() => SettingsCard((Command)null!));
    }

    [Fact]
    public void ExpanderOverloads_FillTheExpander()
    {
        Action<bool> changed = _ => { };
        var items = new Element[] { SettingsCard("A"), TextBlock("B") };
        var expander = SettingsExpander("Display", description: "Scale", content: TextBlock("125%"),
            isExpanded: true, onIsExpandedChanged: changed, items: items);

        Assert.Equal("Display", expander.Header);
        Assert.Equal("Scale", expander.Description);
        Assert.True(expander.IsExpanded is { HasValue: true, Value: true });
        Assert.Same(changed, expander.OnIsExpandedChanged);
        Assert.Same(items, expander.Items);
        Assert.True(expander.HasCallbacks);
        Assert.False(SettingsExpander("Uncontrolled").IsExpanded.HasValue);

        var rich = SettingsExpander(HyperlinkButton("Display"), description: TextBlock("Plain"));
        Assert.Null(rich.Header);
        Assert.NotNull(rich.HeaderContent);
        Assert.Equal("Plain", rich.Description);
    }

    [Fact]
    public void Modifiers_SetTheirFields()
    {
        var card = SettingsCard("Storage", onClick: () => { })
            .ContentAlignment(SettingsCardContentAlignment.Vertical)
            .ActionIcon(FontIcon("\uE8A7"))
            .ActionIconToolTip("Open")
            .ActionIconVisible(false);
        Assert.Equal(SettingsCardContentAlignment.Vertical, card.ContentAlignment);
        Assert.Equal(FontIcon("\uE8A7"), card.ActionIcon);
        Assert.Equal("Open", card.ActionIconToolTip);
        Assert.False(card.IsActionIconVisible);
        Assert.Null(card.ActionIcon(null).ActionIcon);

        var header = TextBlock("Above");
        var footer = TextBlock("Below");
        var expander = SettingsExpander("Display").ItemsHeader(header).ItemsFooter(footer);
        Assert.Same(header, expander.ItemsHeader);
        Assert.Same(footer, expander.ItemsFooter);
    }

    [Fact]
    public void ExpanderItems_CardsTakeTheItemLookAndKeepTheirKeys()
    {
        var card = SettingsCard("Brightness").WithKey("brightness");
        var other = TextBlock("Not a card");
        var body = Assert.IsType<StackElement>(SettingsExpanderHandler.BuildItems([card, other]));

        Assert.Equal(0, body.Spacing);
        Assert.Equal(2, body.Children.Length);
        var mapped = Assert.IsType<SettingsCardElement>(body.Children[0]);
        Assert.True(mapped.IsExpanderItem);
        Assert.Equal("brightness", mapped.Key);
        Assert.Equal("Brightness", mapped.Header);
        Assert.False(card.IsExpanderItem);
        Assert.Same(other, body.Children[1]);

        Assert.Null(SettingsExpanderHandler.BuildItems(null));
        Assert.Null(SettingsExpanderHandler.BuildItems([]));
    }

    [Fact]
    public void Markup_UsesEveryBrushTokenAndNoOther()
    {
        var used = Regex.Matches(SettingsCardResources.Xaml, @"\{ThemeResource (ReactorSettingsCard\w+)\}")
            .Select(m => m.Groups[1].Value).ToHashSet();
        var declared = SettingsCardResources.ThemeBrushes.Select(b => b.Token).ToHashSet();

        Assert.Equal(11, declared.Count);
        Assert.Equal(declared.OrderBy(t => t), used.OrderBy(t => t));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Markup_SpecializesBrushesAndKeysPerVariant(bool highContrast)
    {
        var xaml = SettingsCardResources.Specialize(SettingsCardResources.Xaml, highContrast);

        Assert.DoesNotContain("{ThemeResource ReactorSettingsCard", xaml);
        foreach (var (_, standard, contrast) in SettingsCardResources.ThemeBrushes)
            Assert.Contains("{ThemeResource " + (highContrast ? contrast : standard) + "}", xaml);

        var keys = Regex.Matches(xaml, "x:Key=\"(Reactor\\w+)\"").Select(m => m.Groups[1].Value).ToList();
        var references = Regex.Matches(xaml, @"\{StaticResource (Reactor\w+)\}").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(keys);
        Assert.NotEmpty(references);
        Assert.All(keys.Concat(references), k => Assert.Equal(highContrast, k.EndsWith("Contrast", StringComparison.Ordinal)));
        Assert.All(references, r => Assert.Contains(r, keys));
    }
}
