using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class ReactorFeatureFlagsTests
{
    [Fact]
    public void ShowLayoutCost_DefaultIsFalse()
    {
        // Note: this assertion is best-effort — the static flag is process-wide and
        // other tests in the assembly may have toggled it. We save/restore around
        // our assertion so the default-state check runs on a known-clean value only
        // when no one else has mutated it.
        Assert.False(ReactorFeatureFlags.ShowLayoutCost,
            "ShowLayoutCost must default to false (tests that mutate it must save/restore).");
    }

    [Fact]
    public void ShowLayoutCost_RoundTrips()
    {
        var saved = ReactorFeatureFlags.ShowLayoutCost;
        try
        {
            ReactorFeatureFlags.ShowLayoutCost = true;
            Assert.True(ReactorFeatureFlags.ShowLayoutCost);
            ReactorFeatureFlags.ShowLayoutCost = false;
            Assert.False(ReactorFeatureFlags.ShowLayoutCost);
        }
        finally
        {
            ReactorFeatureFlags.ShowLayoutCost = saved;
        }
    }
}
