using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 066 §7.2: which mounts may rent from the pool's visual lane. A visual-lane control
/// refuses facade transforms and XAML transitions, so only a built-in element that takes the
/// visual itself (demand) and has nothing that could write a facade (setters, mount callbacks)
/// may get one.
/// </summary>
public class ElementPoolLaneTests
{
    private static Reconciler.PoolRentContext For(Element e, Reconciler.PoolRentContext outer = default)
        => Reconciler.ForMount(e, e.Modifiers, outer);

    private static BorderElement Target() => Border(TextBlock("x"));

    public static TheoryData<string> Features => new() { "Animate", "Transition", "InteractionStates", "Keyframes", "LayoutAnimation", "ScrollLinked" };

    private static Element WithFeature(Element e, string feature) => feature switch
    {
        "Animate" => e.Animate(Curve.Linear(100)),
        "Transition" => e.Transition(Transition.Fade),
        "InteractionStates" => e.InteractionStates(static b => b.PointerOver(scale: 1.1f)),
        "Keyframes" => e.Keyframes("k", 1, static k => k.Duration(30).At(0f, opacity: 0.5f).At(1f, opacity: 1f)),
        "LayoutAnimation" => e.LayoutAnimation(),
        // ScrollLinked needs a live ScrollViewer, so set the declaration directly.
        _ => e with { ScrollAnimation = new ScrollAnimationConfig(null!, []) },
    };

    [Theory]
    [MemberData(nameof(Features))]
    public void A_feature_that_takes_the_visual_is_demand(string feature)
        => Assert.True(For(WithFeature(Target(), feature)).VisualLane);

    [Fact]
    public void No_feature_means_no_demand()
    {
        Assert.False(For(Target()).VisualLane);
        // Transforms don't take the visual before spec 066 layer 4.
        Assert.False(For(Target().Scale(2f).Rotation(30)).VisualLane);
    }

    [Fact]
    public void Setters_and_mount_callbacks_block_the_visual_lane()
    {
        Assert.False(For(Target().Set(static b => b.Rotation = 10).Animate(Curve.Linear(100))).VisualLane);
        Assert.False(For(Target().Animate(Curve.Linear(100)).OnMount(static fe => fe.Rotation = 10)).VisualLane);
    }

    [Fact]
    public void Only_built_in_poolable_elements_are_candidates()
        => Assert.False(For(Expander("h", TextBlock("x")).Animate(Curve.Linear(100))).VisualLane);

    [Fact]
    public void A_keyed_memo_passes_its_demand_and_setters_inward()
    {
        var memo = Memo("k", static () => Target());
        Assert.True(For(Target(), For(memo.Animate(Curve.Linear(100)))).VisualLane);
        var blocking = For(memo.OnMount(static _ => { }));
        Assert.False(For(Target().Animate(Curve.Linear(100)), blocking).VisualLane);
    }

    [Fact]
    public void A_non_transparent_wrapper_passes_setters_but_not_demand()
    {
        var demanding = new Reconciler.PoolRentContext(Armed: true, Transparent: false, Demand: true, Blocked: false, Candidate: false);
        Assert.False(For(Target(), demanding).VisualLane);
        var blocking = demanding with { Demand = false, Blocked = true };
        Assert.False(For(Target().Animate(Curve.Linear(100)), blocking).VisualLane);
    }

    [Fact]
    public void A_mount_that_already_rented_passes_nothing_inward()
    {
        var consumed = new Reconciler.PoolRentContext(Armed: false, Transparent: true, Demand: true, Blocked: true, Candidate: true);
        Assert.False(For(Target(), consumed).VisualLane);
        Assert.True(For(Target().Animate(Curve.Linear(100)), consumed).VisualLane);
    }
}