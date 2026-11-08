using System.Globalization;
using ReactorRegedit.Core;
using Xunit;

namespace ReactorRegedit.Tests;

public class KeyDetailsTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void Format_PluralizesAndGroups() =>
        Assert.Equal("1,234 subkeys, 0 values, modified 11/8/2026 2:05 PM",
            KeyDetails.Format(1234, 0, new DateTime(2026, 11, 8, 14, 5, 0), En));

    [Fact]
    public void Format_Singular() =>
        Assert.Equal("1 subkey, 1 value, modified 1/2/2020 3:04 AM",
            KeyDetails.Format(1, 1, new DateTime(2020, 1, 2, 3, 4, 0), En));
}
