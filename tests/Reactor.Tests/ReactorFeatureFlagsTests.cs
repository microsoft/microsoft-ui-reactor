using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class ReactorFeatureFlagsTests
{
    [Fact]
    public void ShowLayoutCost_GetterReadsAssignedValue()
    {
        // The flag is static and process-wide — testing the literal default
        // is unreliable in xUnit because earlier tests in the assembly may
        // have toggled it. Instead verify the getter / setter contract
        // round-trips around an explicitly-set value, with save/restore so
        // the test is order-independent.
        var saved = ReactorFeatureFlags.ShowLayoutCost;
        try
        {
            ReactorFeatureFlags.ShowLayoutCost = false;
            Assert.False(ReactorFeatureFlags.ShowLayoutCost);
            ReactorFeatureFlags.ShowLayoutCost = true;
            Assert.True(ReactorFeatureFlags.ShowLayoutCost);
        }
        finally
        {
            ReactorFeatureFlags.ShowLayoutCost = saved;
        }
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
