using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class OptionalTests
{
    [Fact]
    public void DefaultValue_IsUnset()
    {
        Optional<int> optional = default;

        Assert.False(optional.HasValue);
        Assert.False(Optional<int>.Unset.HasValue);
        Assert.Equal(default, optional);
        Assert.Equal(Optional<int>.Unset, optional);
    }

    [Fact]
    public void OfValue_HasValue_AndValueReturnsValue()
    {
        var optional = Optional<int>.Of(42);

        Assert.True(optional.HasValue);
        Assert.Equal(42, optional.Value);
    }

    [Fact]
    public void UnsetValue_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Optional<int>.Unset.Value);

        Assert.Equal("Optional<T> is Unset", ex.Message);
    }

    [Fact]
    public void GetValueOrDefault_NoFallback_ReturnsDefaultWhenUnset()
    {
        Assert.Equal(0, Optional<int>.Unset.GetValueOrDefault());
        Assert.Null(Optional<string>.Unset.GetValueOrDefault());
    }

    [Fact]
    public void GetValueOrDefault_WithFallback_ReturnsFallbackWhenUnset_AndValueWhenSet()
    {
        Assert.Equal(17, Optional<int>.Unset.GetValueOrDefault(17));
        Assert.Equal(42, Optional<int>.Of(42).GetValueOrDefault(17));
    }

    [Fact]
    public void ImplicitConversion_ProducesHasValue_IncludingReferenceNull()
    {
        Optional<int> number = 42;
        Optional<string?> text = null;

        Assert.True(number.HasValue);
        Assert.Equal(42, number.Value);
        Assert.True(text.HasValue);
        Assert.Null(text.Value);
    }

    [Fact]
    public void Equality_DistinguishesUnsetFromExplicitDefaultAndNull()
    {
        Assert.True(Optional<int>.Of(1) == Optional<int>.Of(1));
        Assert.True(Optional<int>.Of(1) != Optional<int>.Of(2));
        Assert.True(Optional<int>.Unset != Optional<int>.Of(default));
        Assert.True(Optional<int>.Unset == Optional<int>.Unset);
        Assert.True(Optional<string?>.Unset != Optional<string?>.Of(null));
    }

    [Fact]
    public void GetHashCode_DistinguishesUnsetFromExplicitDefault()
    {
        Assert.NotEqual(Optional<int>.Unset.GetHashCode(), Optional<int>.Of(default).GetHashCode());
        Assert.NotEqual(Optional<string?>.Unset.GetHashCode(), Optional<string?>.Of(null).GetHashCode());
    }

    [Fact]
    public void StructSize_MatchesExpectedX64Layout()
    {
        Assert.Equal(8, Unsafe.SizeOf<Optional<int>>());
        Assert.Equal(2, Unsafe.SizeOf<Optional<bool>>());
        Assert.Equal(16, Unsafe.SizeOf<Optional<double>>());
        Assert.Equal(16, Unsafe.SizeOf<Optional<string>>());
    }
}
