using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using WinUI = Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

// Perf: devirtualize per-prop diff dispatch in V1 descriptor (issues #114/#115/#117/#119/#120).
// These are headless tests — they never instantiate a live WinUI control (which throws
// COMException in the test host). Real control *types* are used only as generic arguments;
// the "control" argument passed to entries is null and the recording set/get/vector lambdas
// ignore it. This mirrors the existing RichTextBlockDescriptorTests pattern.
public class PropEntryDiffDispatchTests
{
    private sealed record IntElement(int Value) : Element;

    private sealed record ItemsElement(IReadOnlyList<int> Items) : Element;

    private sealed class DummyPayload { }

    // Treats two ints as equal iff they have the same parity. Lets us prove a custom
    // comparer's behavior is honored (and not silently replaced by the default).
    private sealed class ParityComparer : IEqualityComparer<int>
    {
        public static readonly ParityComparer Instance = new();
        public bool Equals(int x, int y) => (x & 1) == (y & 1);
        public int GetHashCode(int obj) => obj & 1;
    }

    // ── #115: comparer devirtualization helper ──────────────────────────────

    [Fact]
    public void ValuesEqual_NullComparer_MatchesDefaultForValueTypes()
    {
        Assert.True(PropValueComparison.ValuesEqual<int>(null, 5, 5));
        Assert.False(PropValueComparison.ValuesEqual<int>(null, 5, 6));
        Assert.Equal(EqualityComparer<int>.Default.Equals(5, 5), PropValueComparison.ValuesEqual<int>(null, 5, 5));
        Assert.Equal(EqualityComparer<int>.Default.Equals(5, 6), PropValueComparison.ValuesEqual<int>(null, 5, 6));

        var a = new DateTime(2024, 1, 1);
        var b = new DateTime(2024, 1, 1);
        var c = new DateTime(2025, 6, 30);
        Assert.True(PropValueComparison.ValuesEqual<DateTime>(null, a, b));
        Assert.False(PropValueComparison.ValuesEqual<DateTime>(null, a, c));
    }

    [Fact]
    public void ValuesEqual_NullComparer_MatchesDefaultForReferenceTypes()
    {
        Assert.True(PropValueComparison.ValuesEqual<string>(null, "abc", "abc"));
        Assert.False(PropValueComparison.ValuesEqual<string>(null, "abc", "xyz"));
        Assert.True(PropValueComparison.ValuesEqual<string?>(null, null, null));
        Assert.False(PropValueComparison.ValuesEqual<string?>(null, "abc", null));
    }

    [Fact]
    public void ValuesEqual_CustomComparer_IsHonored()
    {
        // Parity comparer: 2 and 4 are "equal", 2 and 3 are not.
        Assert.True(PropValueComparison.ValuesEqual(ParityComparer.Instance, 2, 4));
        Assert.False(PropValueComparison.ValuesEqual(ParityComparer.Instance, 2, 3));
        // The default comparer would disagree on 2 vs 4 — proves we didn't fall back to it.
        Assert.NotEqual(EqualityComparer<int>.Default.Equals(2, 4), PropValueComparison.ValuesEqual(ParityComparer.Instance, 2, 4));
    }

    // ── #115/#117: OneWay write-vs-skip still correct after devirtualization ──

    [Fact]
    public void OneWay_DefaultComparer_WritesOnChange_SkipsWhenEqual()
    {
        int writes = 0;
        int last = -1;
        var entry = new OneWayPropEntry<IntElement, WinUI.TextBlock, int>(
            e => e.Value,
            (c, v) => { writes++; last = v; });

        // changed → write
        entry.Update(null!, new IntElement(1), new IntElement(2));
        Assert.Equal(1, writes);
        Assert.Equal(2, last);

        // unchanged → skip
        entry.Update(null!, new IntElement(2), new IntElement(2));
        Assert.Equal(1, writes);
    }

    [Fact]
    public void OneWay_Mount_AlwaysWrites()
    {
        int writes = 0;
        int last = -1;
        var entry = new OneWayPropEntry<IntElement, WinUI.TextBlock, int>(
            e => e.Value,
            (c, v) => { writes++; last = v; });

        entry.Mount(null!, new IntElement(7));
        Assert.Equal(1, writes);
        Assert.Equal(7, last);
    }

    [Fact]
    public void OneWay_CustomComparer_IsHonoredForWriteDecision()
    {
        int writes = 0;
        var entry = new OneWayPropEntry<IntElement, WinUI.TextBlock, int>(
            e => e.Value,
            (c, v) => writes++,
            ParityComparer.Instance);

        // 2 -> 4: parity-equal, so NO write (default comparer would have written).
        entry.Update(null!, new IntElement(2), new IntElement(4));
        Assert.Equal(0, writes);

        // 2 -> 3: parity differs, so write.
        entry.Update(null!, new IntElement(2), new IntElement(3));
        Assert.Equal(1, writes);
    }

    // ── #114: Subscribes discriminator ──────────────────────────────────────

    [Fact]
    public void Subscribes_IsFalse_ForNonSubscribingEntry()
    {
        var entry = new OneWayPropEntry<IntElement, WinUI.TextBlock, int>(e => e.Value, (c, v) => { });
        Assert.False(entry.Subscribes);
    }

    [Fact]
    public void Subscribes_IsTrue_ForSubscribingEntry()
    {
        var entry = new ReferencePropEntry<IntElement, WinUI.ToggleSwitch, WinUI.ToggleSwitch>(
            e => null, (c, t) => { }, slot: 0);
        Assert.True(entry.Subscribes);
    }

    // Robustness guard: every PropEntry subtype must override Subscribes IFF it
    // overrides EnsureSubscribed. A future entry that wires a subscription but
    // forgets to set Subscribes=true would be silently dropped from the subscribe
    // partition; this test fails loudly in that case.
    [Fact]
    public void Subscribes_OverrideMatches_EnsureSubscribedOverride_ForEveryEntry()
    {
        var baseOpen = typeof(PropEntry<,>);
        var entryTypes = baseOpen.Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && IsSubclassOfRawGeneric(baseOpen, t))
            .ToList();

        Assert.NotEmpty(entryTypes);

        foreach (var t in entryTypes)
        {
            var ensure = t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(m => m.Name == "EnsureSubscribed");
            bool overridesEnsure = ensure.DeclaringType == t;

            var subscribesGetter = t.GetProperty("Subscribes")!.GetGetMethod()!;
            bool overridesSubscribes = subscribesGetter.DeclaringType == t;

            Assert.True(
                overridesEnsure == overridesSubscribes,
                $"{t.Name}: overrides EnsureSubscribed={overridesEnsure} but overrides Subscribes={overridesSubscribes}. " +
                "Any entry that overrides EnsureSubscribed must also override Subscribes => true.");
        }
    }

    // ── #114/#117: DescriptorHandler partitions entries correctly ────────────

    [Fact]
    public void DescriptorHandler_PartitionsUpdateAndSubscribeEntries()
    {
        var descriptor = new ControlDescriptor<IntElement, WinUI.ToggleSwitch>()
            .OneWay(e => e.Value, (c, v) => { })
            .Initial(e => e.Value, (c, v) => { })
            .Reference<WinUI.ToggleSwitch>(e => null, (c, t) => { })
            .OneWay(e => e.Value, (c, v) => { });

        var handler = new DescriptorHandler<IntElement, WinUI.ToggleSwitch>(descriptor);

        var update = GetPrivateArray(handler, "_updateEntries");
        var subscribe = GetPrivateArray(handler, "_subscribeEntries");

        // _updateEntries: all four entries, in declaration order, same instances as Properties.
        Assert.Equal(4, update.Length);
        Assert.True(descriptor.Properties.SequenceEqual(update.Cast<PropEntry<IntElement, WinUI.ToggleSwitch>>()));

        // _subscribeEntries: only the single reference entry (the one that overrides EnsureSubscribed).
        var single = Assert.Single(subscribe)!;
        Assert.StartsWith("ReferencePropEntry", single.GetType().Name, StringComparison.Ordinal);
        Assert.True(((PropEntry<IntElement, WinUI.ToggleSwitch>)single).Subscribes);
    }

    [Fact]
    public void DescriptorHandler_EmptySubscribeSubset_WhenNoSubscribingEntries()
    {
        var descriptor = new ControlDescriptor<IntElement, WinUI.ToggleSwitch>()
            .OneWay(e => e.Value, (c, v) => { })
            .OneWay(e => e.Value, (c, v) => { });

        var handler = new DescriptorHandler<IntElement, WinUI.ToggleSwitch>(descriptor);

        Assert.Equal(2, GetPrivateArray(handler, "_updateEntries").Length);
        Assert.Empty(GetPrivateArray(handler, "_subscribeEntries"));
    }

    // ── #119: CollectionDiff scratch reuse stays correct across updates ──────

    [Fact]
    public void CollectionDiff_ReusedScratch_ProducesCorrectDiffAcrossUpdates()
    {
        var backing = new List<int> { 1, 2, 3 };
        var entry = MakeCollectionDiffEntry(_ => backing);

        // [1,2,3] -> [2,3,4]: drop 1, keep 2/3, add 4.
        entry.Update(null!, new ItemsElement(new[] { 1, 2, 3 }), new ItemsElement(new[] { 2, 3, 4 }));
        Assert.Equal(new[] { 2, 3, 4 }, backing);

        // [2,3,4] -> [5]: if the scratch key sets weren't cleared between updates,
        // stale keys {2,3,4} would survive and suppress the removals — proving reuse is clean.
        entry.Update(null!, new ItemsElement(new[] { 2, 3, 4 }), new ItemsElement(new[] { 5 }));
        Assert.Equal(new[] { 5 }, backing);

        // [5] -> []: clears everything.
        entry.Update(null!, new ItemsElement(new[] { 5 }), new ItemsElement(Array.Empty<int>()));
        Assert.Empty(backing);
    }

    [Fact]
    public void CollectionDiff_ReferenceEqualItems_IsNoOp()
    {
        var backing = new List<int> { 1, 2, 3 };
        var entry = MakeCollectionDiffEntry(_ => backing);

        var items = (IReadOnlyList<int>)new[] { 9, 9, 9 };
        var el = new ItemsElement(items);
        entry.Update(null!, el, el);

        // Same item-list reference on both sides → fast path, backing untouched.
        Assert.Equal(new[] { 1, 2, 3 }, backing);
    }

    private static CollectionDiffControlledPropEntry<ItemsElement, WinUI.CalendarView, DummyPayload, int, int, Action>
        MakeCollectionDiffEntry(Func<WinUI.CalendarView, IList<int>> getVector)
        => new(
            get: e => e.Items,
            getVector: getVector,
            key: i => i,
            subscribe: (c, d) => { },
            callbackPresent: e => null,
            trampoline: () => { },
            slotIsNull: p => true,
            setSlot: (p, d) => { });

    private static Array GetPrivateArray(object owner, string field)
    {
        var f = owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(f);
        return Assert.IsAssignableFrom<Array>(f!.GetValue(owner));
    }

    private static bool IsSubclassOfRawGeneric(Type generic, Type? toCheck)
    {
        while (toCheck is not null && toCheck != typeof(object))
        {
            var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
            if (generic == cur) return true;
            toCheck = toCheck.BaseType;
        }
        return false;
    }
}
