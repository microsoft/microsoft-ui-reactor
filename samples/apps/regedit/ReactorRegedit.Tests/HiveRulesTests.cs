using ReactorRegedit.Core;
using Xunit;

namespace ReactorRegedit.Tests;

public class HiveRulesTests
{
    [Theory]
    [InlineData("HKEY_LOCAL_MACHINE", true, null)]
    [InlineData("HKEY_USERS", true, null)]
    [InlineData("HKEY_LOCAL_MACHINE", false, "requires administrator")]
    [InlineData("HKEY_CURRENT_USER", true, "select HKEY_LOCAL_MACHINE or HKEY_USERS")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE", true, "select HKEY_LOCAL_MACHINE or HKEY_USERS")]
    [InlineData("", true, "select HKEY_LOCAL_MACHINE or HKEY_USERS")]
    [InlineData(@"\\PC\HKEY_LOCAL_MACHINE", true, "not available on a remote registry")]
    public void LoadBlockedReason(string path, bool elevated, string? expected) =>
        Assert.Equal(expected, HiveRules.LoadBlockedReason(path, elevated));

    [Fact]
    public void HivelistNames_MapToKeyPaths_AndSkipSystemHives()
    {
        var roots = HiveRules.HiveRootsFromHivelist(
        [
            @"\REGISTRY\MACHINE\HARDWARE",
            @"\REGISTRY\MACHINE\SYSTEM",
            @"\REGISTRY\MACHINE\SOFTWARE",
            @"\REGISTRY\MACHINE\MyHive",
            @"\REGISTRY\USER\S-1-5-21-1-2-3-1001",
            @"\REGISTRY\USER\S-1-5-21-1-2-3-1001_Classes",
            @"\REGISTRY\A\{abc}",
            "garbage",
        ]);
        Assert.Equal(
        [
            @"HKEY_LOCAL_MACHINE\MyHive",
            @"HKEY_USERS\S-1-5-21-1-2-3-1001",
            @"HKEY_USERS\S-1-5-21-1-2-3-1001_Classes",
        ], roots);
    }

    [Theory]
    [InlineData(@"HKEY_LOCAL_MACHINE\MyHive", true, null)]
    [InlineData(@"hkey_local_machine\myhive", true, null)]
    [InlineData(@"HKEY_LOCAL_MACHINE\MyHive", false, "requires administrator")]
    [InlineData(@"HKEY_LOCAL_MACHINE\Other", true, "the selected key is not a loaded hive")]
    [InlineData(@"HKEY_LOCAL_MACHINE\MyHive\Sub", true, "select a loaded hive")]
    [InlineData("HKEY_LOCAL_MACHINE", true, "select a loaded hive")]
    [InlineData(@"HKEY_CURRENT_USER\MyHive", true, "select a loaded hive")]
    public void UnloadBlockedReason(string path, bool elevated, string? expected) =>
        Assert.Equal(expected, HiveRules.UnloadBlockedReason(path, elevated, [@"HKEY_LOCAL_MACHINE\MyHive"]));

    [Theory]
    [InlineData("Offline", null)]
    [InlineData("", "Type a key name for the hive.")]
    [InlineData("  ", "Type a key name for the hive.")]
    [InlineData(@"a\b", "The key name cannot contain a backslash.")]
    public void ValidateHiveName(string name, string? expected) =>
        Assert.Equal(expected, HiveRules.ValidateHiveName(name));
}
