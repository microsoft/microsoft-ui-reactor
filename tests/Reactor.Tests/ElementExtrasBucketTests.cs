using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// PR #455 CR item #2 — a bucketed <see cref="Element"/> field written to
/// <c>null</c> must not materialize (or keep) a non-null empty
/// <see cref="ElementExtras"/>. An empty bucket is not <c>Equals</c> to
/// <c>null</c>, so without normalization the synthesized record equality would
/// diverge between an extras-free element and one whose only "extra" is a
/// field explicitly set to null.
/// </summary>
public class ElementExtrasBucketTests
{
    private static readonly TextBlockElement Base = new("hi");

    [Fact]
    public void Setting_Bucketed_Field_To_Null_On_Extras_Free_Element_Stays_Equal()
    {
        var cleared = Base with { Attached = null };

        Assert.Null(cleared.Extensions);
        Assert.Equal(Base, cleared);
        Assert.Equal(Base.GetHashCode(), cleared.GetHashCode());
    }

    [Fact]
    public void Clearing_The_Only_Bucketed_Field_Collapses_Bucket_To_Null()
    {
        var dict = new Dictionary<Type, object> { [typeof(int)] = 1 };
        var withAttached = Base with { Attached = dict };
        Assert.NotNull(withAttached.Extensions);

        var cleared = withAttached with { Attached = null };

        Assert.Null(cleared.Extensions);
        Assert.Equal(Base, cleared);
    }

    [Fact]
    public void Setting_A_Real_Bucketed_Value_Differs_From_Extras_Free()
    {
        var dict = new Dictionary<Type, object> { [typeof(int)] = 1 };
        var withAttached = Base with { Attached = dict };

        Assert.NotEqual(Base, withAttached);
        Assert.NotNull(withAttached.Extensions);
        Assert.Same(dict, withAttached.Attached);
    }

    [Fact]
    public void Clearing_One_Of_Several_Fields_Keeps_The_Non_Empty_Bucket()
    {
        var dict = new Dictionary<Type, object> { [typeof(int)] = 1 };
        var both = Base with { Attached = dict, ConnectedAnimationKey = "hero" };

        var clearedAttached = both with { Attached = null };

        Assert.NotNull(clearedAttached.Extensions);
        Assert.Equal("hero", clearedAttached.ConnectedAnimationKey);
        Assert.Null(clearedAttached.Attached);
    }
}
