using System.Numerics;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class RemainingModifierOwnerTests
{
    private static readonly Func<ElementModifiers, bool> HasScale = static m => m.Scale.HasValue;
    private delegate (Element Source, ElementModifiers Modifiers)? Lookup(Func<ElementModifiers, bool> declares);
    private sealed record Leaf(int Id) : Element;

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    [InlineData(12)] [InlineData(13)] [InlineData(14)] [InlineData(15)]
    public void Legacy_Overrides_Select_The_Last_Declaration(int mask)
    {
        var bags = Enumerable.Range(0, 4).Select(i => (mask & (1 << i)) != 0 ? Mods(i + 1) : null).ToArray();
        var leaf = new Leaf(1) { Modifiers = bags[3] };
        var root = new ModifiedElement(new ModifiedElement(leaf, bags[2] ?? new()), bags[1] ?? new())
        {
            Modifiers = bags[0],
        };
        var found = Bind(root)(HasScale);
        var expected = bags.LastOrDefault(b => b is not null);
        if (expected is null) Assert.Null(found);
        else
        {
            Assert.True(found.HasValue);
            Assert.Same(leaf, found.Value.Source);
            Assert.Same(expected, found.Value.Modifiers);
        }
    }

    [Fact]
    public void Unrelated_Inner_Properties_Do_Not_Hide_A_Wrapped_Declaration()
    {
        var outer = new ElementModifiers { Opacity = 0.2 };
        var wrapped = new ElementModifiers { Scale = new Vector3(2) };
        var inner = new ElementModifiers { Rotation = 30 };
        var leaf = new Leaf(1) { Modifiers = inner };
        var lookup = Bind(new ModifiedElement(leaf, wrapped) { Modifiers = outer });
        Assert.Same(wrapped, lookup(HasScale)!.Value.Modifiers);
        Assert.Same(outer, lookup(static m => m.Opacity.HasValue)!.Value.Modifiers);
        Assert.Same(inner, lookup(static m => m.Rotation.HasValue)!.Value.Modifiers);
        Assert.Null(lookup(static m => m.Translation.HasValue));
    }

    [Fact]
    public void Decorator_Declaration_Wins_Before_Its_Target()
    {
        var inner = new Leaf(1) { Modifiers = Mods(2) };
        var outer = new FlyoutElement(inner, new Leaf(2)) { Modifiers = Mods(3) };
        var found = Bind(outer)(HasScale)!.Value;
        Assert.Same(outer, found.Source);
        Assert.Same(outer.Modifiers, found.Modifiers);
        Assert.Same(inner, Bind(outer with { Modifiers = null })(HasScale)!.Value.Source);
    }

    [Fact]
    public void Wrapper_Depth_Remains_Bounded()
    {
        Element root = new Leaf(1) { Modifiers = Mods(2) };
        for (int i = 0; i < 100; i++) root = new ModifiedElement(root, new());
        Assert.Null(Bind(root)(HasScale));
    }

    private static ElementModifiers Mods(float? scale) => new()
    {
        Width = 120,
        Opacity = 0.4,
        Scale = scale.HasValue ? new Vector3(scale.Value) : null,
    };

    private static Reconciler Prepare(Element root) => new() { ModifierUpdate = (null, root, root.Modifiers) };

    private static Lookup Bind(Element root) => Prepare(root).FindRemainingModifierOwner;

    [Fact]
    public void Merged_Bag_Does_Not_Require_Cloning_The_Element()
    {
        var root = new Leaf(1) { Modifiers = new() { Rotation = 30 } };
        var merged = root.Modifiers.Merge(Mods(2));
        using var reconciler = new Reconciler { ModifierUpdate = (null, root, merged) };
        var owner = reconciler.FindRemainingModifierOwner(HasScale)!.Value;
        Assert.Same(root, owner.Source);
        Assert.Same(merged, owner.Modifiers);
        Assert.Same(root, reconciler.ModifierUpdate.Root);
        Assert.False(root.Modifiers.Scale.HasValue);
    }
}
