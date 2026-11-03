using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

public class RenderContextDependencyTests
{
    public static TheoryData<object?[]?, object?[], bool> DependencyPairs => new()
    {
        { null, [], false },
        { [], [], true },
        { [1], [1, 2], false },
        { [1, 32], [2, 1], false },
        { [1, "value", null], [1, "value", null], true },
        { [null], ["value"], false },
        { [1], [1L], false },
    };

    [Theory]
    [MemberData(nameof(DependencyPairs))]
    public void Compares_Dependency_Arrays_Element_Wise(
        object?[]? previous, object?[] next, bool expected)
    {
        Assert.Equal(expected, RenderContext.DepsEqual(previous, next));
    }

    [Fact]
    public void Reference_Dependencies_Use_Object_Equals()
    {
        var first = new EquatableReference(1);
        var second = new EquatableReference(1);
        Assert.True(first.Equals(second));
        Assert.True(RenderContext.DepsEqual([first], [first]));
        Assert.False(RenderContext.DepsEqual([first], [second]));
        Assert.True(RenderContext.DepsEqual([new ValueDependency(1)], [new ValueDependency(1)]));
    }

    private sealed class EquatableReference(int value) : IEquatable<EquatableReference>
    {
        public bool Equals(EquatableReference? other) => other is not null && value == other.Value;
        private int Value => value;
    }

    private sealed record ValueDependency(int Value);
}
