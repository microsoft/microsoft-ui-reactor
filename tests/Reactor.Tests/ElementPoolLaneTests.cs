using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
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

    // Only presence matters to ForMount, so an uninitialized wrapper stands in for the WinUI object.
    private static T Sentinel<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>()
        where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    // Every ModifierTable.VisualOwningModifiers row, applied to an element. ScrollLinked needs a
    // live ScrollViewer, which this headless suite can't construct, so it's checked by shape only.
    private static Element? WithFeature(Element e, string feature) => feature switch
    {
        "Animate" => e.Animate(Curve.Linear(100)),
        "Transition" => e.Transition(Transition.Fade),
        "Keyframes" => e.Keyframes("k", 1, static k => k.Duration(30).At(0f, opacity: 0.5f).At(1f, opacity: 1f)),
        "LayoutAnimation" => e.LayoutAnimation(),
        "SpringLayoutAnimation" => e.SpringLayoutAnimation(),
        // The transition modifiers build WinUI objects, which this suite can't construct.
        "OpacityTransition" => e with { ImplicitTransitions = new() { Opacity = Sentinel<ScalarTransition>() } },
        "ScaleTransition" => e with { ImplicitTransitions = new() { Scale = Sentinel<Vector3Transition>() } },
        "RotationTransition" => e with { ImplicitTransitions = new() { Rotation = Sentinel<ScalarTransition>() } },
        "TranslationTransition" => e with { ImplicitTransitions = new() { Translation = Sentinel<Vector3Transition>() } },
        // Spec 066 layer 4: a non-identity transform takes the visual at mount.
        "Scale" => e.Scale(2f),
        "Rotation" => e.Rotation(30),
        "Translation" => e.Translation(10, 0, 0),
        "CenterPoint" => e.CenterPoint(new global::System.Numerics.Vector3(10, 5, 0)),
        // Spec 066 layer 5: a non-(0, 0) origin starts its pivot expression at mount.
        "TransformOrigin" => e.TransformOrigin(0.5f, 0.5f),
        // Takes the visual on the first state change, or needs a live ScrollViewer: see below.
        "InteractionStates" or "ScrollLinked" => null,
        _ => throw new Xunit.Sdk.XunitException(
            $"ModifierTable.VisualOwningModifiers gained '{feature}'. Make Reconciler.ForMount count it as demand, then map it here."),
    };

    [Fact]
    public void Every_visual_owning_modifier_is_demand()
    {
        foreach (var feature in Microsoft.UI.Reactor.Analyzers.ModifierTable.VisualOwningModifiers.Keys)
        {
            if (WithFeature(Target(), feature) is { } declared)
                Assert.True(For(declared).VisualLane, feature);
        }
    }

    [Fact]
    public void Features_that_take_no_visual_at_mount_are_not_demand()
    {
        // A fresh control keeps writable facades until these act, so a visual-lane control
        // would refuse a write that works fresh.
        Assert.False(For(Target().Transition(Transition.Exit(Transition.Fade))).VisualLane);
        Assert.False(For(Target() with { ScrollAnimation = new ScrollAnimationConfig(null!, []) }).VisualLane);
        Assert.False(For(Target().InteractionStates(static b => b.PointerOver(scale: 1.1f))).VisualLane);
        // BackgroundTransition stays a XAML transition (spec 066 §5.6).
        Assert.False(For(Target() with { ImplicitTransitions = new() { Background = Sentinel<BrushTransition>() } }).VisualLane);
    }
    [Fact]
    public void No_feature_means_no_demand()
    {
        Assert.False(For(Target()).VisualLane);
    }

    [Fact]
    public void Identity_transforms_are_not_demand()
    {
        // An identity value never takes the visual (spec 066 §3 invariant 2), so a facade write
        // through a ref must keep working on the rented control.
        Assert.False(For(Target().Scale(1f).Rotation(0).Translation(0, 0, 0)
            .CenterPoint(global::System.Numerics.Vector3.Zero).TransformOrigin(0, 0)).VisualLane);
        Assert.True(For(Target().Scale(1f).Rotation(0.5f)).VisualLane);
    }

    [Fact]
    public void Setters_and_mount_callbacks_block_the_visual_lane()
    {
        Assert.False(For(Target().Set(static b => b.Rotation = 10).Animate(Curve.Linear(100))).VisualLane);
        Assert.False(For(Target().Animate(Curve.Linear(100)).OnMount(static fe => fe.Rotation = 10)).VisualLane);
    }

    private sealed record DerivedBorder() : BorderElement((Element?)null);

    [Fact]
    public void Only_built_in_poolable_elements_are_candidates()
    {
        Assert.False(For(Expander("h", TextBlock("x")).Animate(Curve.Linear(100))).VisualLane);
        // A record derived from a built-in may come with its own handler.
        Assert.False(For(new DerivedBorder().Animate(Curve.Linear(100))).VisualLane);
    }

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