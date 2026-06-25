using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
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

    // #114: when the subscribe subset is empty, handler.Update skips BindFor and the
    // subscribe loop entirely. This guards that the *update* loop still runs every
    // OneWay entry in that path — i.e. the BindFor skip never gates the prop writes.
    [Fact]
    public void DescriptorHandler_Update_StillWritesOneWayEntries_WhenSubscribeSubsetEmpty()
    {
        int aWrites = 0, bWrites = 0;
        int aLast = -1, bLast = -1;
        var descriptor = new ControlDescriptor<IntElement, WinUI.TextBlock>()
            .OneWay(e => e.Value, (c, v) => { aWrites++; aLast = v; })
            .OneWay(e => e.Value + 100, (c, v) => { bWrites++; bLast = v; });

        var handler = new DescriptorHandler<IntElement, WinUI.TextBlock>(descriptor);
        Assert.Empty(GetPrivateArray(handler, "_subscribeEntries"));

        var reconciler = new Reconciler();
        var ctx = new UpdateContext(reconciler, static () => { });

        // Changed value → both OneWay entries write. ctrl is null; the recording
        // setters ignore it (headless — no live control needed for OneWay writes).
        handler.Update(ctx, new IntElement(1), new IntElement(2), null!);
        Assert.Equal(1, aWrites);
        Assert.Equal(2, aLast);
        Assert.Equal(1, bWrites);
        Assert.Equal(102, bLast);

        // Unchanged value → both skip (proves the writes are real diffs, not unconditional).
        handler.Update(ctx, new IntElement(2), new IntElement(2), null!);
        Assert.Equal(1, aWrites);
        Assert.Equal(1, bWrites);
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

    // #119: two CollectionDiff entries of the SAME closed generic share the
    // thread-static scratch sets, but each must diff with its OWN key comparer.
    // Guards the s_scratchComparer sentinel that rebuilds the sets when a
    // differing comparer runs (a leaked comparer would corrupt the diff).
    [Fact]
    public void CollectionDiff_PerEntryKeyComparer_NotLeakedAcrossSharedScratch()
    {
        // Run the parity entry first so the shared scratch is built with ParityComparer.
        // Target [4], new items [2]: under parity 2 ≡ 4, so 4 stays and 2 is not added.
        var parityBacking = new List<int> { 4 };
        var parityEntry = MakeCollectionDiffEntry(_ => parityBacking, ParityComparer.Instance);
        parityEntry.Update(null!, new ItemsElement(new[] { 4 }), new ItemsElement(new[] { 2 }));
        Assert.Equal(new[] { 4 }, parityBacking);

        // Now a DEFAULT-comparer entry. If the scratch leaked ParityComparer, this would
        // wrongly keep [4]; with the per-entry rebuild it diffs with default equality:
        // 2 ≠ 4, so 4 is removed and 2 is added.
        var defaultBacking = new List<int> { 4 };
        var defaultEntry = MakeCollectionDiffEntry(_ => defaultBacking);
        defaultEntry.Update(null!, new ItemsElement(new[] { 4 }), new ItemsElement(new[] { 2 }));
        Assert.Equal(new[] { 2 }, defaultBacking);
    }

    // C2: if a lambda throws mid-diff, the finally (ReturnScratchSets) must still
    // drain the shared scratch so the NEXT diff on this thread starts clean — a
    // leaked newKeys set would let removed items wrongly survive.
    [Fact]
    public void CollectionDiff_ScratchDrainedAfterException_NoStaleKeyLeak()
    {
        var backing = new List<int> { 1, 2, 3 };
        int getVectorCalls = 0;
        var entry = MakeCollectionDiffEntry(_ =>
        {
            getVectorCalls++;
            // Throw on the first diff, AFTER newKeys {1,2} has been populated.
            if (getVectorCalls == 1) throw new InvalidOperationException("boom");
            return backing;
        });

        // Update 1: builds newKeys {1,2}, then getVector throws → finally must drain.
        Assert.Throws<InvalidOperationException>(() =>
            entry.Update(null!, new ItemsElement(new[] { 1, 2 }), new ItemsElement(new[] { 1, 2 })));

        // Update 2: vec [1,2,3] -> [3]. If {1,2} leaked into newKeys, keys 1 and 2 would
        // be treated as still-wanted and never removed (wrong result [1,2,3]); a clean
        // drain removes them, leaving [3].
        entry.Update(null!, new ItemsElement(new[] { 1, 2, 3 }), new ItemsElement(new[] { 3 }));
        Assert.Equal(new[] { 3 }, backing);
    }

    // FINDING A (reentrancy): a CollectionDiff mutation can echo synchronously and
    // re-enter Update on the same thread + closed generic while the outer diff still
    // holds the shared scratch. The in-use guard must hand the nested call FRESH sets
    // so it neither pollutes nor drains the outer's populated newKeys.
    [Fact]
    public void CollectionDiff_NestedReentrantUpdate_DoesNotCorruptOuterDiff()
    {
        var outerVec = new RecordingList(new[] { 1, 2, 3 });
        var innerBacking = new List<int> { 10, 20 };
        var innerEntry = MakeCollectionDiffEntry(_ => innerBacking);

        bool reentered = false;
        var outerEntry = MakeCollectionDiffEntry(_ =>
        {
            if (!reentered)
            {
                reentered = true;
                // Nested diff on a DIFFERENT entry of the SAME closed generic — it shares
                // the thread-static scratch with the outer call that is mid-flight.
                innerEntry.Update(null!, new ItemsElement(new[] { 10, 20 }), new ItemsElement(new[] { 20, 30 }));
            }
            return outerVec;
        });

        // Outer [1,2,3] -> [2,3,4]: a correct diff drops only key 1 and appends key 4
        // (exactly 1 RemoveAt + 1 Add). Without the guard the nested call drains the
        // outer's newKeys in its finally, so the outer instead removes all three and
        // re-adds three — same final state, but 4 extra WinUI mutations (and echo churn).
        outerEntry.Update(null!, new ItemsElement(new[] { 1, 2, 3 }), new ItemsElement(new[] { 2, 3, 4 }));

        Assert.Equal(new[] { 20, 30 }, innerBacking);       // nested diff correct
        Assert.Equal(new[] { 2, 3, 4 }, outerVec.Snapshot); // outer final state correct
        Assert.Equal(1, outerVec.Removes);                  // minimal churn — proves newKeys survived
        Assert.Equal(1, outerVec.Adds);
    }

    private static CollectionDiffControlledPropEntry<ItemsElement, WinUI.CalendarView, DummyPayload, int, int, Action>
        MakeCollectionDiffEntry(Func<WinUI.CalendarView, IList<int>> getVector, IEqualityComparer<int>? keyComparer = null)
        => new(
            get: e => e.Items,
            getVector: getVector,
            key: i => i,
            subscribe: (c, d) => { },
            callbackPresent: e => null,
            trampoline: () => { },
            slotIsNull: p => true,
            setSlot: (p, d) => { },
            keyComparer: keyComparer);

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

    // IList<int> that counts Add/RemoveAt so a test can assert how much a diff churned
    // the target vector (used by the reentrancy test, where the corruption shows up as
    // extra mutations rather than a different final state).
    private sealed class RecordingList : IList<int>
    {
        private readonly List<int> _inner;
        public int Removes;
        public int Adds;

        public RecordingList(IEnumerable<int> items) => _inner = new List<int>(items);

        public IReadOnlyList<int> Snapshot => _inner;

        public int this[int index] { get => _inner[index]; set => _inner[index] = value; }
        public int Count => _inner.Count;
        public bool IsReadOnly => false;
        public void Add(int item) { Adds++; _inner.Add(item); }
        public void RemoveAt(int index) { Removes++; _inner.RemoveAt(index); }
        public void Clear() => _inner.Clear();
        public bool Contains(int item) => _inner.Contains(item);
        public void CopyTo(int[] array, int arrayIndex) => _inner.CopyTo(array, arrayIndex);
        public int IndexOf(int item) => _inner.IndexOf(item);
        public void Insert(int index, int item) => _inner.Insert(index, item);
        public bool Remove(int item) => _inner.Remove(item);
        public IEnumerator<int> GetEnumerator() => _inner.GetEnumerator();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => _inner.GetEnumerator();
    }
}
