using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Regression coverage for <see cref="ElementFactory{T}"/> + WinUI ItemsRepeater
/// integration. The framework keeps every realized UIElement parented to the
/// ItemsRepeater forever (see <c>microsoft-ui-xaml-lift/controls/dev/Repeater/
/// ViewManager.cpp:865-869</c>) and expects the IElementFactory to cycle them
/// through GetElement / RecycleElement. A factory that allocates fresh on
/// every realize creates one orphan in <c>Repeater.Children</c> per call —
/// the working set grows unbounded and the variable-height demo eventually
/// hit a stowed exception (0xC000027B) when <see cref="ElementFactory{T}.RefreshRealizedItems"/>
/// ran <see cref="Reconciler.Reconcile"/> against a stale Element / foreign
/// realized child pair (structurally divergent rows on the XAML thread).
///
/// These fixtures drive a standalone <see cref="ElementFactory{T}"/> directly
/// through its <see cref="IElementFactory"/> interface. Running against a
/// dedicated factory (not the one bound to a live LazyVStack) keeps the
/// invariants under test isolated from the framework's own realize/recycle
/// activity — every Get/Recycle in the test is one we asked for, so the
/// bookkeeping counts mean exactly what the assertions claim.
/// </summary>
internal static class ElementFactoryRecyclingFixtures
{
    private record Item(string Id, string Label);

    private static ElementFactory<Item> BuildFactory(IReadOnlyList<Item> items, out Reconciler reconciler)
    {
        reconciler = new Reconciler();
        return new ElementFactory<Item>(
            items,
            (i, _) => TextBlock(i.Label),
            reconciler,
            requestRerender: static () => { },
            pool: null);
    }

    private static ElementFactoryGetArgs MakeGetArgs(int index)
        // Factory's int-keyed legacy path: args.Data is the data-source index.
        // No ListState / OC<ReactorRow> needed for this code path.
        => new() { Data = index };

    private static ElementFactoryRecycleArgs MakeRecycleArgs(UIElement element)
        => new() { Element = element };

    // ────────────────────────────────────────────────────────────────────
    //  Regression: distinct-UIElement count stays bounded across N cycles.
    //  This is the exact invariant that, prior to the fix, was violated —
    //  GetElement returned a fresh control on every call, so distinct grew
    //  1:1 with realize count.
    // ────────────────────────────────────────────────────────────────────

    internal class Factory_BoundedDistinctControls_AcrossManyRealizeCycles(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var items = Enumerable.Range(0, 50)
                .Select(i => new Item(i.ToString(), $"Item {i}"))
                .ToArray();
            IElementFactory factory = BuildFactory(items, out _);

            // 100 realize/recycle cycles. Pre-fix: 100 distinct controls.
            // Post-fix: the recycle stack hands the same control back every
            // realize after the first.
            const int Cycles = 100;
            var allReturned = new List<UIElement>(Cycles);
            for (int cycle = 0; cycle < Cycles; cycle++)
            {
                int idx = cycle % items.Length;
                var control = factory.GetElement(MakeGetArgs(idx));
                allReturned.Add(control);
                factory.RecycleElement(MakeRecycleArgs(control));
            }

            int distinct = allReturned.Distinct(ReferenceEqualityComparer.Instance).Count();
            // Tight bound — single-realize-then-recycle should reuse the
            // same control every cycle. Allow small headroom for a future
            // legitimate impl change (e.g., per-height pool) without
            // tripping the regression gate spuriously.
            H.Check($"EFR_BoundedDistinct_DistinctLEq5_actual={distinct}", distinct <= 5);

            // Tighter: once seeded, every subsequent realize reuses.
            H.Check("EFR_BoundedDistinct_FirstAndLastAreSame",
                ReferenceEquals(allReturned[0], allReturned[^1]));

            return Task.CompletedTask;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Positive assertion: the recycled control is the very next control
    //  GetElement returns. Pins reuse-by-identity, not just count.
    // ────────────────────────────────────────────────────────────────────

    internal class Factory_RecycledControlIsReusedOnNextRealize(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var items = new[]
            {
                new Item("a", "A"),
                new Item("b", "B"),
                new Item("c", "C"),
            };
            IElementFactory factory = BuildFactory(items, out _);

            var first = factory.GetElement(MakeGetArgs(0));
            factory.RecycleElement(MakeRecycleArgs(first));

            var second = factory.GetElement(MakeGetArgs(1));
            H.Check("EFR_Reuse_SecondRealizeReusesFirstControl",
                ReferenceEquals(first, second));

            // Pool is now empty (second is outstanding). A third realize
            // must Mint fresh — different control.
            var third = factory.GetElement(MakeGetArgs(2));
            H.Check("EFR_Reuse_ThirdRealizeMintsFreshWhenPoolEmpty",
                !ReferenceEquals(second, third));

            return Task.CompletedTask;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Internal-bookkeeping invariants. These complement the external
    //  distinct-count check above by catching a class of bug where the
    //  control is correctly reused but the bookkeeping dicts leak entries
    //  (which is what was driving the variable-height demo's crash via
    //  stale RefreshRealizedItems entries).
    // ────────────────────────────────────────────────────────────────────

    internal class Factory_BookkeepingBoundedAcrossCycles(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var items = Enumerable.Range(0, 20)
                .Select(i => new Item(i.ToString(), $"Item {i}"))
                .ToArray();
            var typed = BuildFactory(items, out _);
            var ifactory = (IElementFactory)typed;

            const int Cycles = 50;
            for (int cycle = 0; cycle < Cycles; cycle++)
            {
                int idx = cycle % items.Length;
                var control = ifactory.GetElement(MakeGetArgs(idx));
                ifactory.RecycleElement(MakeRecycleArgs(control));
            }

            // After 50 realize-then-recycle pairs on a standalone factory:
            //   - recyclePool: 1   (the single control we cycled)
            //   - lastElementByControl: 1 (the same control's last Element)
            //   - mountedElements: 0 (every realize was paired with a recycle)
            //   - keyByControl: 0  (same reason)
            // Pre-fix this would have been: pool=0, lastElementByControl=0,
            // mountedElements=50 (one per realize, never removed),
            // keyByControl=50.
            int poolCount = typed.DebugRecyclePoolCount;
            int lastElCount = typed.DebugLastElementByControlCount;
            int mountedCount = typed.DebugMountedElementsCount;
            int keyByCtlCount = typed.DebugKeyByControlCount;

            H.Check($"EFR_Bookkeeping_RecyclePoolBounded_actual={poolCount}", poolCount <= 5);
            H.Check($"EFR_Bookkeeping_LastElementByControlBounded_actual={lastElCount}", lastElCount <= 5);
            H.Check($"EFR_Bookkeeping_MountedElementsEmpty_actual={mountedCount}", mountedCount == 0);
            H.Check($"EFR_Bookkeeping_KeyByControlEmpty_actual={keyByCtlCount}", keyByCtlCount == 0);

            return Task.CompletedTask;
        }
    }
}
