using ReactorRegedit.Core;
using Xunit;

namespace ReactorRegedit.Tests;

public class RegPathTests
{
    [Theory]
    [InlineData("HKLM", "HKEY_LOCAL_MACHINE")]
    [InlineData("hkcu\\Software", "HKEY_CURRENT_USER\\Software")]
    [InlineData("HKCR\\.txt", "HKEY_CLASSES_ROOT\\.txt")]
    [InlineData("HKU\\.DEFAULT", "HKEY_USERS\\.DEFAULT")]
    [InlineData("HKCC\\System", "HKEY_CURRENT_CONFIG\\System")]
    [InlineData("Computer\\HKEY_LOCAL_MACHINE\\SOFTWARE", "HKEY_LOCAL_MACHINE\\SOFTWARE")]
    [InlineData("computer\\hklm\\software\\", "HKEY_LOCAL_MACHINE\\software")]
    [InlineData("  \"HKCU\\Software\\\\X\\\\\"  ", "HKEY_CURRENT_USER\\Software\\X")]
    [InlineData("hkey_current_user", "HKEY_CURRENT_USER")]
    [InlineData("Computer", "")]
    [InlineData("Computer\\", "")]
    [InlineData("", "")]
    public void Normalize_AcceptsAbbreviationsPrefixAndCasing(string input, string expected)
    {
        Assert.Equal(expected, RegPath.Normalize(input));
    }

    [Theory]
    [InlineData("HKXX\\Software")]
    [InlineData("Software\\Microsoft")]
    [InlineData("Computer\\Nope")]
    public void Normalize_RejectsUnknownHive(string input)
    {
        Assert.Null(RegPath.Normalize(input));
    }

    [Fact]
    public void Normalize_PreservesSlashesInsideKeyNames()
    {
        // '/' is legal in key names, so it must not be treated as a separator.
        Assert.Equal("HKEY_CLASSES_ROOT\\MIME/Database", RegPath.Normalize("HKCR\\MIME/Database"));
    }

    [Fact]
    public void Normalize_RemotePaths()
    {
        Assert.Equal(@"\\SERVER\HKEY_LOCAL_MACHINE\Software", RegPath.Normalize(@"\\SERVER\hklm\Software\"));
        Assert.Equal(@"\\SERVER", RegPath.Normalize(@"\\SERVER"));
    }

    [Fact]
    public void ToDisplay_AddsComputerPrefix()
    {
        Assert.Equal("Computer", RegPath.ToDisplay(""));
        Assert.Equal("Computer\\HKEY_USERS\\S-1", RegPath.ToDisplay("HKEY_USERS\\S-1"));
    }

    [Fact]
    public void ParentLeafAndHive()
    {
        Assert.Equal("HKEY_CURRENT_USER\\Software", RegPath.Parent("HKEY_CURRENT_USER\\Software\\X"));
        Assert.Equal("", RegPath.Parent("HKEY_CURRENT_USER"));
        Assert.Equal("X", RegPath.Leaf("HKEY_CURRENT_USER\\Software\\X"));
        Assert.True(RegPath.IsHive("HKEY_CURRENT_USER"));
        Assert.False(RegPath.IsHive("HKEY_CURRENT_USER\\Software"));
        Assert.False(RegPath.IsHive(""));
        Assert.Equal("HKEY_CURRENT_USER", RegPath.Hive("HKEY_CURRENT_USER\\Software\\X"));
        Assert.Equal("Software\\X", RegPath.SubPath("HKEY_CURRENT_USER\\Software\\X"));
        Assert.Equal("", RegPath.SubPath("HKEY_CURRENT_USER"));
    }

    [Fact]
    public void IsSameOrDescendant_RespectsSegmentBoundaries()
    {
        Assert.True(RegPath.IsSameOrDescendant("HKEY_CURRENT_USER\\A\\B", "HKEY_CURRENT_USER\\a"));
        Assert.True(RegPath.IsSameOrDescendant("HKEY_CURRENT_USER\\A", "HKEY_CURRENT_USER\\A"));
        Assert.False(RegPath.IsSameOrDescendant("HKEY_CURRENT_USER\\AB", "HKEY_CURRENT_USER\\A"));
    }

    [Fact]
    public void AncestorsAndSelf_FromHiveDown()
    {
        Assert.Equal(
            ["HKEY_CURRENT_USER", "HKEY_CURRENT_USER\\A", "HKEY_CURRENT_USER\\A\\B"],
            RegPath.AncestorsAndSelf("HKEY_CURRENT_USER\\A\\B"));
        Assert.Empty(RegPath.AncestorsAndSelf(""));
    }
}
