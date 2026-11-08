using ReactorRegedit.Core;
using Xunit;

namespace ReactorRegedit.Tests;

public class CommandLineTests
{
    [Fact]
    public void KeyArgument_IsNormalized() =>
        Assert.Equal(@"HKEY_CURRENT_USER\Software", CommandLine.KeyArgument(["app.exe", "--key", @"Computer\HKCU\Software\"]));

    [Fact]
    public void KeyArgument_MissingOrEmpty_IsNull()
    {
        Assert.Null(CommandLine.KeyArgument(["app.exe"]));
        Assert.Null(CommandLine.KeyArgument(["app.exe", "--key"]));
        Assert.Null(CommandLine.KeyArgument(["app.exe", "--key", "Computer"]));
        Assert.Null(CommandLine.KeyArgument(["app.exe", "--key", "NotAHive\\x"]));
    }
}
