using Microsoft.UI.Reactor.Animation;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Animation;

/// <summary>
/// Spec 066 §6.1: who owns each implicit animation key. The claims are a pure state machine, so
/// a recording sink stands in for <c>Visual.ImplicitAnimations</c>.
/// </summary>
public class ImplicitAnimationClaimsTests
{
    private sealed class RecordingSink : IImplicitAnimationSink
    {
        public readonly Dictionary<string, object> Keys = [];
        public readonly List<string> Log = [];
        public void Insert(string key, object value) { Keys[key] = value; Log.Add($"+{key}={value}"); }
        public void Remove(string key) { Keys.Remove(key); Log.Add($"-{key}"); }
        public void Clear() { Keys.Clear(); Log.Add("clear"); }
    }

    [Fact]
    public void LowerFeature_IsHidden_ThenRestored_WhenTheWinnerReleases()
    {
        var sink = new RecordingSink();
        var claims = new ImplicitAnimationClaims();

        claims.Add(sink, "Offset", ImplicitFeature.Layout, 0, "layout");
        claims.Add(sink, "Offset", ImplicitFeature.Animate, 0, "animate");
        Assert.Equal("layout", sink.Keys["Offset"]);
        Assert.Equal(["+Offset=layout"], sink.Log);

        claims.Release(sink, "Offset", ImplicitFeature.Layout, 0);
        Assert.Equal("animate", sink.Keys["Offset"]);
    }

    [Fact]
    public void HigherFeature_ClaimedLater_Wins()
    {
        var sink = new RecordingSink();
        var claims = new ImplicitAnimationClaims();

        claims.Add(sink, "Opacity", ImplicitFeature.Transition, 0, "transition");
        claims.Add(sink, "Opacity", ImplicitFeature.Animate, 0, "animate");
        Assert.Equal("animate", sink.Keys["Opacity"]);

        claims.Release(sink, "Opacity", ImplicitFeature.Animate, 0);
        Assert.Equal("transition", sink.Keys["Opacity"]);
    }

    [Fact]
    public void ReleasingOneKey_LeavesTheOtherFeaturesKeys()
    {
        // #1625: dropping the layout animation must not drop a transition's key.
        var sink = new RecordingSink();
        var claims = new ImplicitAnimationClaims();

        claims.Add(sink, "Opacity", ImplicitFeature.Transition, 0, "fade");
        claims.Add(sink, "Offset", ImplicitFeature.Layout, 0, "layout");
        claims.Release(sink, "Offset", ImplicitFeature.Layout, 0);

        Assert.Equal(["Opacity"], sink.Keys.Keys);
    }

    [Fact]
    public void ReleaseOfANonOwner_IsANoOp()
    {
        var sink = new RecordingSink();
        var claims = new ImplicitAnimationClaims();
        claims.Add(sink, "Scale", ImplicitFeature.Animate, 0, "animate");
        sink.Log.Clear();

        claims.Release(sink, "Scale", ImplicitFeature.Transition, 0);
        claims.Release(sink, "Scale", ImplicitFeature.Animate, 1);
        claims.Release(sink, "Opacity", ImplicitFeature.Animate, 0);

        Assert.Empty(sink.Log);
        Assert.Equal("animate", sink.Keys["Scale"]);
    }

    [Fact]
    public void LastRelease_RemovesTheKey_AndLeavesNoClaim()
    {
        var sink = new RecordingSink();
        var claims = new ImplicitAnimationClaims();
        claims.Add(sink, "Scale", ImplicitFeature.Transition, 0, "a");
        claims.Add(sink, "Scale", ImplicitFeature.Animate, 0, "b");

        claims.Release(sink, "Scale", ImplicitFeature.Animate, 0);
        claims.Release(sink, "Scale", ImplicitFeature.Transition, 0);

        Assert.Empty(sink.Keys);
        Assert.Equal(0, claims.Count);
        Assert.Equal("-Scale", sink.Log[^1]);
    }

    [Fact]
    public void Reclaiming_TheSameSlot_ReplacesTheValueInPlace()
    {
        // Value-only updates re-claim under the same slot; the old value must not come back.
        var sink = new RecordingSink();
        var claims = new ImplicitAnimationClaims();
        claims.Add(sink, "Opacity", ImplicitFeature.Animate, 0, "v1");
        claims.Add(sink, "Opacity", ImplicitFeature.Animate, 0, "v2");
        Assert.Equal(1, claims.Count);
        Assert.Equal("v2", sink.Keys["Opacity"]);

        claims.Release(sink, "Opacity", ImplicitFeature.Animate, 0);
        Assert.Empty(sink.Keys);
    }

    [Fact]
    public void OutermostSlot_WinsWithinAFeature_AndReleasingItRestoresTheInnerOne()
    {
        var sink = new RecordingSink();
        var claims = new ImplicitAnimationClaims();

        // Mount order: the inner layer claims first, the outer layer shifts it inward.
        claims.Add(sink, "Opacity", ImplicitFeature.Transition, 0, "inner");
        claims.ShiftInward();
        claims.Add(sink, "Opacity", ImplicitFeature.Transition, 0, "outer");
        Assert.Equal("outer", sink.Keys["Opacity"]);

        // An inner value-only update re-claims its own slot and stays hidden.
        sink.Log.Clear();
        claims.Add(sink, "Opacity", ImplicitFeature.Transition, 1, "inner2");
        Assert.Empty(sink.Log);

        claims.Release(sink, "Opacity", ImplicitFeature.Transition, 0);
        Assert.Equal("inner2", sink.Keys["Opacity"]);
    }

    [Fact]
    public void ReleaseAll_ClearsOnce_AndForgetsEveryClaim()
    {
        var sink = new RecordingSink();
        var claims = new ImplicitAnimationClaims();
        claims.Add(sink, "Opacity", ImplicitFeature.Transition, 0, "a");
        claims.Add(sink, "Offset", ImplicitFeature.Layout, 0, "b");
        sink.Log.Clear();

        claims.ReleaseAll(sink);

        Assert.Equal(["clear"], sink.Log);
        Assert.Equal(0, claims.Count);
        Assert.Null(claims.Effective("Opacity"));
    }
}
