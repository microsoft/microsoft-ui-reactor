using Microsoft.UI.Reactor.Cli.Figma;
using Xunit;

namespace Reactor.Tests.Figma;

public class FigmaClientTests
{
    [Theory]
    [InlineData(
        "https://www.figma.com/design/t7yLwpMUOWJSYt5ahz3ROC/Windows-UI-kit?node-id=29792-125378",
        "t7yLwpMUOWJSYt5ahz3ROC", "29792-125378", "29792:125378")]
    [InlineData(
        "https://www.figma.com/design/abc123/My-Design?node-id=100-200&t=xyz",
        "abc123", "100-200", "100:200")]
    [InlineData(
        "https://www.figma.com/file/XYZ789/Legacy-Format?node-id=5-10",
        "XYZ789", "5-10", "5:10")]
    public void ParseUrl_WithNodeId_ExtractsAllParts(
        string url, string expectedFileKey, string expectedNodeId, string expectedApiNodeId)
    {
        var result = FigmaClient.ParseUrl(url);

        Assert.NotNull(result);
        Assert.Equal(expectedFileKey, result.FileKey);
        Assert.Equal(expectedNodeId, result.NodeId);
        Assert.Equal(expectedApiNodeId, result.ApiNodeId);
    }

    [Theory]
    [InlineData("https://www.figma.com/design/t7yLwpMUOWJSYt5ahz3ROC/Windows-UI-kit",
        "t7yLwpMUOWJSYt5ahz3ROC")]
    [InlineData("https://www.figma.com/design/abc123/My-Design",
        "abc123")]
    [InlineData("https://www.figma.com/file/abc123/My-Design",
        "abc123")]
    public void ParseUrl_WithoutNodeId_ExtractsFileKey(string url, string expectedFileKey)
    {
        var result = FigmaClient.ParseUrl(url);

        Assert.NotNull(result);
        Assert.Equal(expectedFileKey, result.FileKey);
        Assert.Null(result.NodeId);
        Assert.Null(result.ApiNodeId);
    }

    [Theory]
    [InlineData("https://google.com")]
    [InlineData("not-a-url")]
    [InlineData("https://www.figma.com/community/something")]
    [InlineData("")]
    public void ParseUrl_InvalidUrl_ReturnsNull(string url)
    {
        Assert.Null(FigmaClient.ParseUrl(url));
    }

    [Fact]
    public void ParseUrl_DesignUrlWithQueryParams_IgnoresExtraParams()
    {
        var result = FigmaClient.ParseUrl(
            "https://www.figma.com/design/KEY123/Name?node-id=1-2&t=abc&mode=dev");

        Assert.NotNull(result);
        Assert.Equal("KEY123", result.FileKey);
        Assert.Equal("1-2", result.NodeId);
        Assert.Equal("1:2", result.ApiNodeId);
    }
}
