using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class ButtonContentTests
{
    public static TheoryData<Func<Element, Element>, Func<Element, Element?>> ContentFactories => new()
    {
        { c => Button(c), e => ((ButtonElement)e).ContentElement },
        { c => HyperlinkButton(c), e => ((HyperlinkButtonElement)e).ContentElement },
        { c => RepeatButton(c), e => ((RepeatButtonElement)e).ContentElement },
        { c => ToggleButton(c), e => ((ToggleButtonElement)e).ContentElement },
        { c => ThreeStateToggleButton(c), e => ((ToggleButtonElement)e).ContentElement },
        { c => DropDownButton(c), e => ((DropDownButtonElement)e).ContentElement },
        { c => SplitButton(c), e => ((SplitButtonElement)e).ContentElement },
        { c => ToggleSplitButton(c), e => ((ToggleSplitButtonElement)e).ContentElement },
        { c => CheckBox(default, null, c), e => ((CheckBoxElement)e).ContentElement },
        { c => ThreeStateCheckBox(default, null, c), e => ((CheckBoxElement)e).ContentElement },
        { c => RadioButton(c), e => ((RadioButtonElement)e).ContentElement },
    };

    [Theory]
    [MemberData(nameof(ContentFactories))]
    public void ContentIsRetainedAndDistinctSubtreesCannotBeSkipped(
        Func<Element, Element> create, Func<Element, Element?> readContent)
    {
        var first = HStack(TextBlock("first"));
        var second = HStack(TextBlock("second"));
        var oldElement = create(first);
        var newElement = create(second);

        Assert.Same(first, readContent(oldElement));
        Assert.Same(second, readContent(newElement));
        Assert.False(Element.ShallowEquals(oldElement, newElement));
    }

    [Fact]
    public void ContentOverloadsRetainControlSpecificArguments()
    {
        var child = TextBlock("label");
        var flyout = ContentFlyout(TextBlock("options"));
        var uri = new Uri("https://example.com/");
        var clicks = 0;
        Action click = () => clicks++;
        bool? lastChecked = false;
        Action<bool> changed = value => lastChecked = value;
        Action<bool?> threeStateChanged = value => lastChecked = value;

        var link = HyperlinkButton(child, uri, click);
        Assert.Equal(uri, link.NavigateUri);
        link.OnClick!();
        RepeatButton(child, click).OnClick!();
        var split = SplitButton(child, click, flyout);
        Assert.Same(flyout, split.Flyout);
        split.OnClick!();
        Assert.Equal(3, clicks);
        Assert.Same(flyout, DropDownButton(child, flyout).Flyout);

        var toggle = ToggleButton(child, true, changed);
        Assert.True(toggle.IsChecked);
        toggle.OnIsCheckedChanged!(false);
        Assert.False(lastChecked);
        var toggleSplit = ToggleSplitButton(child, true, changed, flyout);
        Assert.True(toggleSplit.IsChecked.Value);
        Assert.Same(flyout, toggleSplit.Flyout);
        toggleSplit.OnIsCheckedChanged!(true);
        Assert.True(lastChecked);
        var check = CheckBox(true, changed, child);
        Assert.True(check.IsChecked.Value);
        check.OnIsCheckedChanged!(false);
        Assert.False(lastChecked);
        var radio = RadioButton(child, true, changed, "group");
        Assert.True(radio.IsChecked.Value);
        Assert.Equal("group", radio.GroupName);
        radio.OnIsCheckedChanged!(true);
        Assert.True(lastChecked);

        var threeToggle = ThreeStateToggleButton(child, null, threeStateChanged);
        Assert.True(threeToggle.IsThreeState);
        threeToggle.OnCheckedStateChanged!(null);
        Assert.Null(lastChecked);
        var threeCheck = ThreeStateCheckBox((bool?)true, threeStateChanged, child);
        Assert.True(threeCheck.IsThreeState);
        Assert.True(threeCheck.CheckedState);
        threeCheck.OnCheckedStateChanged!(false);
        Assert.False(lastChecked);
    }

    [Theory]
    [MemberData(nameof(ContentFactories))]
    public void ScannerFlagsUnnamedIconContentAndAcceptsExplicitNames(
        Func<Element, Element> create, Func<Element, Element?> _)
    {
        var unnamed = create(Icon("Save")).AutomationId("action");
        var diagnostic = Assert.Single(AccessibilityScanner.Scan(unnamed),
            finding => finding.Id == "A11Y_001" && finding.AutomationId == "action");
        Assert.Equal(unnamed.GetType().Name, diagnostic.ElementType);

        var named = unnamed.AutomationName("Save");
        Assert.DoesNotContain(AccessibilityScanner.Scan(named), finding => finding.Id == "A11Y_001");
        Assert.DoesNotContain(AccessibilityScanner.Scan(create(TextBlock("Save"))), finding => finding.Id == "A11Y_001");
    }

    [Theory]
    [MemberData(nameof(ContentFactories))]
    public void ScannerVisitsContentSubtree(
        Func<Element, Element> create, Func<Element, Element?> _)
    {
        var image = Image("ms-appx:///Assets/example.png").AutomationId("unlabeled-image");
        var button = create(HStack(image, TextBlock("Save"))).AutomationName("Save");
        Assert.Contains(AccessibilityScanner.Scan(button),
            finding => finding.Id == "A11Y_002" && finding.AutomationId == "unlabeled-image");

        var labeled = create(HStack(image.AutomationName("Preview"), TextBlock("Save"))).AutomationName("Save");
        Assert.DoesNotContain(AccessibilityScanner.Scan(labeled), finding => finding.Id == "A11Y_002");
    }
}
