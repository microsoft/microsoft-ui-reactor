using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class TitleBarLeftHeaderTests
{
    [Fact]
    public void LeftHeaderIsOptional()
    {
        Assert.Null(TitleBar("App").LeftHeader);
    }

    [Fact]
    public void LeftHeaderAcceptsCompositionWithoutChangingOtherSlots()
    {
        var content = TextBox("search");
        var right = Button("Profile");
        var left = HStack(TextBlock("Navigation"), Button("Back"));
        var original = TitleBar("App").Content(content).RightHeader(right).Tall();
        var configured = original.LeftHeader(left);

        Assert.Same(left, configured.LeftHeader);
        Assert.Same(content, configured.Content);
        Assert.Same(right, configured.RightHeader);
        Assert.Equal(original.Title, configured.Title);
        Assert.Equal(original.HeightOption, configured.HeightOption);
        Assert.Null(original.LeftHeader);

        var cleared = configured with { LeftHeader = null };
        Assert.Null(cleared.LeftHeader);
        Assert.Same(content, cleared.Content);
        Assert.Same(right, cleared.RightHeader);
    }
}
