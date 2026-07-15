// Port of d3-shape/test/area-test.js

using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.Charting.D3;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.D3;

public class AreaTests
{
    [Fact]
    public void Area_Default_GeneratesPath()
    {
        var a = AreaGenerator.FromArrays();
        var data = new[] { new[] { 0.0, 1.0 }, new[] { 2.0, 3.0 }, new[] { 4.0, 5.0 } };
        Assert.Equal("M0,1L2,3L4,5L4,0L2,0L0,0Z", a.Generate(data));
    }

    [Fact]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: the C# `dynamic` accessor lambdas bind through Microsoft.CSharp's RuntimeBinder (RequiresUnreferencedCode). `dynamic` is inherently reflection-based; this test runs on JIT (standard `dotnet test`), not an AOT publish. Behaviour-neutral.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test-only: the C# `dynamic` accessor lambdas require runtime code generation (Microsoft.CSharp RuntimeBinder), AOT-incompatible by nature. Exercised on JIT only. Behaviour-neutral.")]
    public void Area_CustomAccessors()
    {
        var data = new[]
        {
            new { X = 0.0, Y = 5.0 },
            new { X = 10.0, Y = 15.0 },
            new { X = 20.0, Y = 10.0 },
        };
        var a = AreaGenerator.Create<dynamic>(d => (double)d.X, d => (double)d.Y);
        Assert.Equal("M0,5L10,15L20,10L20,0L10,0L0,0Z", a.Generate(data));
    }

    [Fact]
    public void Area_EmptyData_ReturnsNull()
    {
        var a = AreaGenerator.FromArrays();
        Assert.Null(a.Generate(Array.Empty<double[]>()));
    }
}
