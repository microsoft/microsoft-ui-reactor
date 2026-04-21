using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for SelectorResolver's pure string-parsing helpers.
/// ExtractWindowFromNodeId parses "r:window/local" node identifiers;
/// DescribeCandidate generates human-readable selector descriptors.
/// </summary>
public class SelectorResolverTests
{
    // ════════════════════════════════════════════════════════════════
    //  ExtractWindowFromNodeId — parses r:<window>/<local> format
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ExtractWindowFromNodeId_Valid_Format_Returns_Window()
    {
        var window = SelectorResolver.ExtractWindowFromNodeId("r:main/local123");
        Assert.Equal("main", window);
    }

    [Fact]
    public void ExtractWindowFromNodeId_Complex_Window_Name()
    {
        var window = SelectorResolver.ExtractWindowFromNodeId("r:my-window-2/node456");
        Assert.Equal("my-window-2", window);
    }

    [Fact]
    public void ExtractWindowFromNodeId_No_R_Prefix_Returns_Null()
    {
        var window = SelectorResolver.ExtractWindowFromNodeId("main/local123");
        Assert.Null(window);
    }

    [Fact]
    public void ExtractWindowFromNodeId_No_Slash_Returns_Null()
    {
        var window = SelectorResolver.ExtractWindowFromNodeId("r:mainlocal123");
        Assert.Null(window);
    }

    [Fact]
    public void ExtractWindowFromNodeId_Empty_Window_Returns_Null()
    {
        var window = SelectorResolver.ExtractWindowFromNodeId("r:/local123");
        Assert.Null(window);
    }

    [Fact]
    public void ExtractWindowFromNodeId_Just_Prefix_Returns_Null()
    {
        var window = SelectorResolver.ExtractWindowFromNodeId("r:");
        Assert.Null(window);
    }

    [Fact]
    public void ExtractWindowFromNodeId_Empty_String_Returns_Null()
    {
        var window = SelectorResolver.ExtractWindowFromNodeId("");
        Assert.Null(window);
    }

    [Fact]
    public void ExtractWindowFromNodeId_With_Multiple_Slashes()
    {
        // Only the first slash after "r:" defines the window boundary
        var window = SelectorResolver.ExtractWindowFromNodeId("r:win/path/to/node");
        Assert.Equal("win", window);
    }
}
