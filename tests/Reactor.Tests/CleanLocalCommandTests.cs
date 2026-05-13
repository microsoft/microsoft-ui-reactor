using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class CleanLocalCommandTests
{
    [Theory]
    [InlineData("0.0.0-local", true)]
    [InlineData("1.0.0-local.42", true)]
    [InlineData("2.0.0-LOCAL", true)]
    [InlineData("3.0.0-preview.1", false)]
    [InlineData("1.0.0", false)]
    public void IsLocalVersion_ClassifiesCorrectly(string version, bool expected)
    {
        Assert.Equal(expected, CleanLocalCommand.IsLocalVersion(version));
    }

    [Fact]
    public void PackageIds_ContainsExpectedEntries()
    {
        Assert.Contains("microsoft.ui.reactor", CleanLocalCommand.PackageIds);
        Assert.Contains("microsoft.ui.reactor.projecttemplates", CleanLocalCommand.PackageIds);
    }
}
