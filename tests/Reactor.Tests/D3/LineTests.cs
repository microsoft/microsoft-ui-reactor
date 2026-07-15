// Port of d3-shape/test/line-test.js

using Microsoft.UI.Reactor.Charting.D3;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.D3;

public class LineTests
{
    private readonly record struct Pt(double X, double Y);

    [Fact]
    public void Line_Default_GeneratesPath()
    {
        var l = LineGenerator.FromArrays();
        var data = new[] { new[] { 0.0, 1.0 }, new[] { 2.0, 3.0 }, new[] { 4.0, 5.0 } };
        Assert.Equal("M0,1L2,3L4,5", l.Generate(data));
    }

    [Fact]
    public void Line_Digits_Default3()
    {
        var l = LineGenerator.FromArrays();
        Assert.Equal(3, l.Digits);
    }

    [Fact]
    public void Line_Digits_ControlsPrecision()
    {
        var points = new[] { new[] { 0.0, Math.PI }, new[] { Math.E, 4.0 } };
        var l = LineGenerator.FromArrays();
        Assert.Equal("M0,3.142L2.718,4", l.Generate(points));

        l.SetDigits(6);
        Assert.Equal("M0,3.141593L2.718282,4", l.Generate(points));

        l.SetDigits(null);
        string? result = l.Generate(points);
        Assert.Contains("3.141592653589793", result);
        Assert.Contains("2.718281828459045", result);
    }

    [Fact]
    public void Line_CustomAccessors()
    {
        var data = new[]
        {
            new Pt(0.0, 1.0),
            new Pt(2.0, 3.0),
            new Pt(4.0, 5.0),
        };
        var l = LineGenerator.Create<Pt>(d => d.X, d => d.Y);
        Assert.Equal("M0,1L2,3L4,5", l.Generate(data));
    }

    [Fact]
    public void Line_ConstantX()
    {
        var l = LineGenerator.FromArrays().SetX((_, _) => 0);
        var data = new[] { new[] { 99.0, 1.0 }, new[] { 99.0, 3.0 }, new[] { 99.0, 5.0 } };
        Assert.Equal("M0,1L0,3L0,5", l.Generate(data));
    }

    [Fact]
    public void Line_EmptyData_ReturnsNull()
    {
        var l = LineGenerator.FromArrays();
        Assert.Null(l.Generate(Array.Empty<double[]>()));
    }

    [Fact]
    public void Line_Tuples()
    {
        var l = LineGenerator.Create();
        var data = new[] { (0.0, 1.0), (2.0, 3.0), (4.0, 5.0) };
        Assert.Equal("M0,1L2,3L4,5", l.Generate(data));
    }
}
