using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Elements;

public class ExpanderHeaderContentTests
{
    [Fact]
    public void HeaderContentDefaultsToNullForStringHeaders()
    {
        var expander = Expander("plain header", TextBlock("body"));
        Assert.Null(expander.HeaderContent);
        Assert.Equal("plain header", expander.Header);
    }

    [Fact]
    public void HeaderContentSupportsCompositionAndClearingWithoutChangingTheFallbackOrBody()
    {
        var body = TextBlock("body");
        var header = HStack(TextBlock("label"), Button("action"));
        var original = Expander("fallback", body, isExpanded: true);
        var configured = original.HeaderContent(header);

        Assert.Null(original.HeaderContent);
        Assert.Same(header, configured.HeaderContent);
        Assert.Equal("fallback", configured.Header);
        Assert.Same(body, configured.Content);
        Assert.Equal(original.IsExpanded, configured.IsExpanded);

        var cleared = configured with { HeaderContent = null };
        Assert.Null(cleared.HeaderContent);
        Assert.Equal("fallback", cleared.Header);
        Assert.Same(body, cleared.Content);
    }
}
