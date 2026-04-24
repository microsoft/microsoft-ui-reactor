using Microsoft.UI.Reactor.Hosting.LayoutCost;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Hosting.LayoutCost;

public class ColorRampsTests
{
    [Theory]
    [InlineData(-1.0)]        // negative clamps to green
    [InlineData(double.NaN)]  // NaN clamps to green
    [InlineData(0.0)]
    [InlineData(1.9)]
    [InlineData(2.0)]         // exact boundary → lower bucket
    public void MsRamp_Green(double ms)
        => Assert.Equal(ColorRamps.MsRampGreen, ColorRamps.MsRamp(ms));

    [Theory]
    [InlineData(2.01)]
    [InlineData(8.0)]         // exact boundary → yellow
    public void MsRamp_Yellow(double ms)
        => Assert.Equal(ColorRamps.MsRampYellow, ColorRamps.MsRamp(ms));

    [Theory]
    [InlineData(8.01)]
    [InlineData(16.0)]        // exact boundary → orange
    public void MsRamp_Orange(double ms)
        => Assert.Equal(ColorRamps.MsRampOrange, ColorRamps.MsRamp(ms));

    [Theory]
    [InlineData(16.01)]
    [InlineData(100.0)]
    public void MsRamp_Red(double ms)
        => Assert.Equal(ColorRamps.MsRampRed, ColorRamps.MsRamp(ms));

    [Theory]
    [InlineData(0.0)]
    [InlineData(3.0)]
    public void InflationRamp_Green(double ratio)
        => Assert.Equal(ColorRamps.InflationGreen, ColorRamps.InflationRamp(ratio));

    [Theory]
    [InlineData(3.01)]
    [InlineData(8.0)]
    public void InflationRamp_Yellow(double ratio)
        => Assert.Equal(ColorRamps.InflationYellow, ColorRamps.InflationRamp(ratio));

    [Theory]
    [InlineData(8.01)]
    [InlineData(20.0)]
    public void InflationRamp_Orange(double ratio)
        => Assert.Equal(ColorRamps.InflationOrange, ColorRamps.InflationRamp(ratio));

    [Theory]
    [InlineData(20.01)]
    [InlineData(200.0)]
    public void InflationRamp_Red(double ratio)
        => Assert.Equal(ColorRamps.InflationRed, ColorRamps.InflationRamp(ratio));
}
