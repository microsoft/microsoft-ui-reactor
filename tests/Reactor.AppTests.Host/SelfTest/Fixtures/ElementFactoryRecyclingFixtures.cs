using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

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

    // ────────────────────────────────────────────────────────────────────
    //  PR #324 review fix #1 — heterogeneous-row replacement.
    //  When the row's root element type changes between cycles, Reconcile
    //  returns a fresh control. The old reused control must be untracked
    //  from _lastElementByControl (and detached from any parent, but the
    //  standalone factory has no parent so we only assert tracking).
    // ────────────────────────────────────────────────────────────────────

    internal class Factory_ReplacementOnRootTypeChange_DropsOldControlTracking(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            // Two heterogeneous items: even index → FlexRow, odd index → TextBlock.
            var items = new[]
            {
                new Item("0", "row0"),
                new Item("1", "row1"),
            };
            var reconciler = new Reconciler();
            var factory = new ElementFactory<Item>(
                items,
                (item, idx) => idx % 2 == 0
                    ? (Microsoft.UI.Reactor.Core.Element)FlexRow(TextBlock(item.Label))
                    : TextBlock(item.Label),
                reconciler,
                requestRerender: static () => { },
                pool: null);
            var ifactory = (IElementFactory)factory;

            // Realize index 0 → FlexRow root. Recycle so it's pool-available.
            var rowCtl = ifactory.GetElement(MakeGetArgs(0));
            ifactory.RecycleElement(MakeRecycleArgs(rowCtl));

            int beforeLastEl = factory.DebugLastElementByControlCount;

            // Realize index 1 → TextBlock root. Reconcile returns a fresh
            // control because the root type changed; old reused FlexRow
            // becomes orphaned in any real parent (no parent here in the
            // standalone test, but tracking-removal should still happen).
            var txtCtl = ifactory.GetElement(MakeGetArgs(1));

            H.Check("EFR_Heterogeneous_ReturnsReplacementNotReused",
                !ReferenceEquals(txtCtl, rowCtl));
            H.Check("EFR_Heterogeneous_ReturnedControlIsTextBlock",
                txtCtl is TextBlock);

            // The factory must have dropped its _lastElementByControl entry
            // for the orphaned old FlexRow. Otherwise the bookkeeping leaks
            // one entry per heterogeneous-cycle pair.
            int afterLastEl = factory.DebugLastElementByControlCount;
            // beforeLastEl had 1 (the FlexRow). afterLastEl should be 1 (the new TextBlock).
            // The FlexRow's entry was dropped, TextBlock's entry was added → net 0 delta.
            H.Check($"EFR_Heterogeneous_LastElementByControlSwapped_before={beforeLastEl}_after={afterLastEl}",
                afterLastEl == beforeLastEl);

            return Task.CompletedTask;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  PR #324 review fix #2 — RefreshRealizedItems must keep
    //  _lastElementByControl in sync with _mountedElements.
    //  If a row content changes via re-render (RefreshRealizedItems path)
    //  and the row is later recycled then reused, the next Reconcile would
    //  diff against the pre-refresh Element if _lastElementByControl is
    //  stale, walking the wrong tree shape.
    // ────────────────────────────────────────────────────────────────────

    internal class Factory_RefreshRealizedItems_SyncsLastElementByControl(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { new Item("a", "first") }
                    : new[] { new Item("a", "second") };
                return VStack(
                    Button("Update", () => setPhase(1)),
                    LazyVStack<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(200)
                );
            });
            await Harness.Render();

            var repeater = H.FindControl<WinUI.ItemsRepeater>(_ => true);
            var factory = repeater?.ItemTemplate as ElementFactory<Item>;
            H.Check("EFR_RefreshSync_FactoryFound", factory is not null);
            if (factory is null || repeater is null) return;

            var realized = repeater.TryGetElement(0);
            H.Check("EFR_RefreshSync_RowRealized", realized is not null);
            if (realized is null) return;

            // Pre-update: _lastElementByControl[realized] should reference
            // the Element produced by the "first" view.
            bool hadBefore = factory.DebugTryGetLastElementByControl(realized, out var beforeEl);
            H.Check("EFR_RefreshSync_BeforeUpdate_HasEntry", hadBefore);

            // Trigger state change → re-render → RefreshRealizedItems runs.
            H.ClickButton("Update");
            await Harness.Render();

            bool hadAfter = factory.DebugTryGetLastElementByControl(realized, out var afterEl);
            H.Check("EFR_RefreshSync_AfterUpdate_HasEntry", hadAfter);
            H.Check("EFR_RefreshSync_LastElementChanged",
                !ReferenceEquals(beforeEl, afterEl));
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  PR #324 review fix #4 — Reconciler.UnmountRecursive must descend
    //  into ItemsRepeater children, otherwise row Components never get
    //  their UseEffect cleanups when the LazyStack itself is unmounted
    //  (navigation, conditional render, etc.). Verified by counting how
    //  many "cleanup" callbacks fire after the LazyStack is replaced.
    // ────────────────────────────────────────────────────────────────────

    // Static counter used by CleanupRowComponent. Reset at the top of the
    // fixture run. Component<T> doesn't have a per-instance init/props API
    // available in this fixture context, so the static is the path of
    // least resistance for the test.
    private static int s_cleanupCount;

    private class CleanupRowComponent : Microsoft.UI.Reactor.Core.Component
    {
        public override Microsoft.UI.Reactor.Core.Element Render()
        {
            UseEffect(() => () => global::System.Threading.Interlocked.Increment(ref s_cleanupCount));
            return TextBlock("row");
        }
    }

    internal class LazyStack_Unmount_CleansUpAllRecycledRowComponents(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            s_cleanupCount = 0;

            // Five rows, each wrapping a Component with a UseEffect cleanup.
            var items = Enumerable.Range(0, 5)
                .Select(i => new Item(i.ToString(), $"Item {i}"))
                .ToArray();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                return VStack(
                    Button("Toggle", () => setShow(!show)),
                    show
                        ? LazyVStack<Item>(items, i => i.Id, (i, _) =>
                            Component<CleanupRowComponent>()).Height(300)
                        : (Microsoft.UI.Reactor.Core.Element)TextBlock("(hidden)")
                );
            });
            await Harness.Render();

            // Sanity: at least one row Component should have been mounted.
            var repeater = H.FindControl<WinUI.ItemsRepeater>(_ => true);
            H.Check("EFR_LazyStackUnmount_LazyStackRealized", repeater is not null);

            int before = s_cleanupCount;
            // No cleanup should have run yet — rows are still mounted.
            H.Check($"EFR_LazyStackUnmount_NoCleanupsBeforeUnmount_before={before}", before == 0);

            // Toggle → LazyStack disappears from the render tree → Unmount.
            // Pre-fix #4 (the ItemsRepeater branch in UnmountRecursive),
            // Reconciler.Unmount stopped at ScrollViewer.Content (the
            // ItemsRepeater) because ItemsRepeater isn't a Panel in C#.
            // Row Components' UseEffect cleanups would never fire.
            H.ClickButton("Toggle");
            await Harness.Render();

            int after = s_cleanupCount;
            // We had ~5 rows but only a subset is realized at any time. We
            // want at least one cleanup to fire — pre-fix the count was 0.
            // Use ≥1 as the regression gate rather than ==5 to tolerate
            // the realization window not covering all 5 in a 300px host.
            H.Check($"EFR_LazyStackUnmount_AtLeastOneCleanup_after={after}", after >= 1);
        }
    }
}
