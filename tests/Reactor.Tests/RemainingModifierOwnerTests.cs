using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class RemainingModifierOwnerTests(ITestOutputHelper output)
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
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

    [Fact]
    public void Lookup_Does_Not_Allocate_Per_Iteration()
    {
        var metrics = new List<(string Name, double Bytes, double Nanoseconds)>();
        foreach (bool legacy in new[] { false, true })
        foreach (bool hit in new[] { false, true })
        {
            Element root = new Leaf(1) { Modifiers = Mods(hit ? 2 : null) };
            if (legacy)
                for (int i = 0; i < 3; i++) root = new ModifiedElement(root, Mods(null));
            var reconciler = Prepare(root);
            // Bind before measuring; the typed delegate does not box the result.
            var measured = Measure(Method().CreateDelegate<Func<Func<ElementModifiers, bool>,
                (Element Source, ElementModifiers Modifiers)?>>(reconciler));
            metrics.Add(($"{(legacy ? "legacy" : "direct")}-{(hit ? "hit" : "miss")}", measured.Bytes, measured.Nanoseconds));
        }
        foreach (var row in metrics)
            output.WriteLine($"{row.Name}: {row.Bytes:F1} B/call, {row.Nanoseconds:F1} ns/call");
        Assert.True(metrics.All(row => row.Bytes == 0), string.Join("; ", metrics.Select(row => $"{row.Name}={row.Bytes:F1} B/call")));
    }

    private static ElementModifiers Mods(float? scale) => new()
    {
        Width = 120,
        Opacity = 0.4,
        Scale = scale.HasValue ? new Vector3(scale.Value) : null,
    };

    [DynamicDependency("FindRemainingModifierOwner", typeof(Reconciler))]
    private static MethodInfo Method() => typeof(Reconciler).GetMethod("FindRemainingModifierOwner", PrivateInstance)!;

    [DynamicDependency("_modifierUpdate", typeof(Reconciler))]
    private static Reconciler Prepare(Element root)
    {
        var reconciler = new Reconciler();
        typeof(Reconciler).GetField("_modifierUpdate", PrivateInstance)!
            .SetValue(reconciler, ((UIElement?)null, root));
        return reconciler;
    }

    private static Lookup Bind(Element root) => Method().CreateDelegate<Lookup>(Prepare(root));

    private static (double Bytes, double Nanoseconds) Measure<T>(Func<Func<ElementModifiers, bool>, T> lookup)
    {
        for (int i = 0; i < 10_000; i++) _ = lookup(HasScale);
        const int iterations = 20_000;
        var times = new double[7];
        double bytes = 0;
        T last = default!;
        for (int sample = 0; sample < times.Length; sample++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) last = lookup(HasScale);
            long elapsed = Stopwatch.GetTimestamp() - started;
            bytes = Math.Max(bytes, (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)iterations);
            times[sample] = elapsed * (1_000_000_000.0 / Stopwatch.Frequency) / iterations;
        }
        GC.KeepAlive(last);
        Array.Sort(times);
        return (bytes, times[times.Length / 2]);
    }
}
