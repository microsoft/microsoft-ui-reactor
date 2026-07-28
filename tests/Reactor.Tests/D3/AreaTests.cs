// Port of d3-shape/test/area-test.js

using Microsoft.UI.Reactor.Charting.D3;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.D3;

public class AreaTests
{
    private readonly record struct Pt(double X, double Y);

    [Fact]
    public void Area_Default_GeneratesPath()
    {
        var a = AreaGenerator.FromArrays();
        var data = new[] { new[] { 0.0, 1.0 }, new[] { 2.0, 3.0 }, new[] { 4.0, 5.0 } };
        Assert.Equal("M0,1L2,3L4,5L4,0L2,0L0,0Z", a.Generate(data));
    }

    [Fact]
    public void Area_CustomAccessors()
    {
        var data = new[]
        {
            new Pt(0.0, 5.0),
            new Pt(10.0, 15.0),
            new Pt(20.0, 10.0),
        };
        var a = AreaGenerator.Create<Pt>(d => d.X, d => d.Y);
        Assert.Equal("M0,5L10,15L20,10L20,0L10,0L0,0Z", a.Generate(data));
    }

    [Fact]
    public void Area_EmptyData_ReturnsNull()
    {
        var a = AreaGenerator.FromArrays();
        Assert.Null(a.Generate(Array.Empty<double[]>()));
    }
}
