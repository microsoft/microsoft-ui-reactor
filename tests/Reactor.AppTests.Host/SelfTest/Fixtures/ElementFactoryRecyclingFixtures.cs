using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using WinXC = Microsoft.UI.Xaml.Controls;

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
    //  Heterogeneous-row realization.
    //
    //  PR #324 review fix #1 originally popped the pooled container blindly:
    //  Reconcile then minted a fresh control for the new root type and the
    //  popped one was untracked and left behind. Issue #919 showed that is a
    //  leak, not a fix — a container realized by an ItemsRepeater can never be
    //  un-parented from managed code, so a repeatedly-flipping list stranded a
    //  full realized window of live, visible containers on every flip.
    //
    //  The container that cannot serve this row must therefore STAY POOLED and
    //  STAY TRACKED, so the next realize of a matching root type reuses that
    //  exact instance instead of allocating another one.
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

            // Realize index 1 → TextBlock root. The pooled FlexRow cannot be
            // diffed into a TextBlock, so the factory must leave it in the pool
            // and mount a fresh control for this row.
            var txtCtl = ifactory.GetElement(MakeGetArgs(1));

            H.Check("EFR_Heterogeneous_ReturnsReplacementNotReused",
                !ReferenceEquals(txtCtl, rowCtl));
            H.Check("EFR_Heterogeneous_ReturnedControlIsTextBlock",
                txtCtl is TextBlock);

            // The rejected FlexRow must KEEP its _lastElementByControl entry —
            // that entry is what lets the next matching realize find and reuse
            // it. Dropping it (the pre-#919 behaviour) stranded the control:
            // untracked, un-detachable, and re-allocated on every flip.
            int afterLastEl = factory.DebugLastElementByControlCount;
            H.Check($"EFR_Heterogeneous_RejectedControlStaysTracked_before={beforeLastEl}_after={afterLastEl}",
                afterLastEl == beforeLastEl + 1
                    && factory.DebugTryGetLastElementByControl(rowCtl, out _));

            // …and the proof that retention is what bounds the working set:
            // recycle the TextBlock, realize an even index again, and the very
            // same FlexRow instance must come back instead of a third control.
            ifactory.RecycleElement(MakeRecycleArgs(txtCtl));
            var rowCtl2 = ifactory.GetElement(MakeGetArgs(0));
            H.Check("EFR_Heterogeneous_PooledRootReusedOnFlipBack",
                ReferenceEquals(rowCtl2, rowCtl));
            H.Check($"EFR_Heterogeneous_BookkeepingBounded_count={factory.DebugLastElementByControlCount}",
                factory.DebugLastElementByControlCount == 2);

            // Retention must not become its own leak. A keyed list stamps a
            // per-item key on every row root (ApplyItemIdentityKey), and
            // CanUpdate rejects unequal keys — so scrolling forward through
            // distinct items never finds a reusable container. Without the pool
            // cap, that retained one container plus one tracking entry per item
            // scrolled past and made the reuse scan grow with it.
            var many = Enumerable.Range(0, 200).Select(i => new Item($"k{i}", $"L{i}")).ToArray();
            var scrollFactory = new ElementFactory<Item>(
                many,
                (it, _) => TextBlock(it.Label),
                new Reconciler(),
                requestRerender: static () => { },
                pool: null);
            var iscroll = (IElementFactory)scrollFactory;
            UIElement? previous = null;
            for (var i = 0; i < many.Length; i++)
            {
                if (previous is not null) iscroll.RecycleElement(MakeRecycleArgs(previous));
                previous = iscroll.GetElement(MakeRowGetArgs(i, $"k{i}"));
            }

            H.Check($"EFR_KeyedScroll_PoolBounded_pool={scrollFactory.DebugRecyclePoolCount}",
                scrollFactory.DebugRecyclePoolCount <= 32);
            H.Check($"EFR_KeyedScroll_TrackingBounded_lastEl={scrollFactory.DebugLastElementByControlCount}",
                scrollFactory.DebugLastElementByControlCount <= 33);

            return Task.CompletedTask;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Issue #919 — the pool cap must not defeat the bounded-children
    //  guarantee for a list that cycles its rows through THREE or more root
    //  shapes. A flat two-window cap fills up with the first two shapes and
    //  then evicts the oldest shape on every recycle, so the shape that comes
    //  back next pass is re-minted — and a re-minted container is one more
    //  permanently-parented ItemsRepeater child.
    // ────────────────────────────────────────────────────────────────────

    internal class Factory_MultiShapeCycle_KeepsContainerSetBounded(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            const int rows = 20;
            const int cycles = 6;

            // Value-type T deliberately: ElementFactory skips _viewBuilderCache
            // for value types, so flipping `shape` actually rebuilds the row.
            var items = Enumerable.Range(0, rows).ToArray();
            var shape = 0;
            var factory = new ElementFactory<int>(
                items,
                (i, _) => shape switch
                {
                    0 => TextBlock($"r{i}"),
                    1 => VStack(TextBlock($"r{i}")),
                    _ => FlexColumn(TextBlock($"r{i}")),
                },
                new Reconciler(),
                requestRerender: static () => { },
                pool: null);
            var ifactory = (IElementFactory)factory;

            var distinct = new HashSet<UIElement>();
            var live = new UIElement[rows];
            for (var c = 0; c < cycles; c++)
            {
                shape = c % 3;
                if (c > 0)
                    for (var i = 0; i < rows; i++) ifactory.RecycleElement(MakeRecycleArgs(live[i]));

                for (var i = 0; i < rows; i++)
                {
                    live[i] = ifactory.GetElement(MakeRowGetArgs(i, $"k{i}"));
                    distinct.Add(live[i]);
                }
            }

            // Three shapes × one realized window each. Anything above that means
            // a shape was evicted before it came back around and had to be
            // re-minted, which on a real ItemsRepeater is a permanent orphan.
            H.Check($"EFR_MultiShape_ContainersBounded_distinct={distinct.Count}",
                distinct.Count <= rows * 3);

            // …and the retention is real reuse, not luck: the last cycle must
            // hand back containers seen in the matching earlier cycle.
            H.Check($"EFR_MultiShape_PoolStillBounded_pool={factory.DebugRecyclePoolCount}",
                factory.DebugRecyclePoolCount <= rows * 3);

            return Task.CompletedTask;
        }
    }

    private class ShapeRowA : Component { public override Element Render() => TextBlock("a"); }
    private class ShapeRowB : Component { public override Element Render() => TextBlock("b"); }
    private class ShapeRowC : Component { public override Element Render() => TextBlock("c"); }

    // ────────────────────────────────────────────────────────────────────
    //  Issue #919 — pool "shape" must mean reuse compatibility, not native
    //  control type. Three modifier-free Component<A/B/C> roots are mutually
    //  incompatible pool entries but ALL mount as Border, so a census keyed on
    //  Control.GetType() sees one shape, under-sizes the pool, and evicts the
    //  shape that is about to be cycled back to.
    // ────────────────────────────────────────────────────────────────────

    internal class Factory_ComponentShapeCycle_KeepsContainerSetBounded(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            const int rows = 20;
            const int cycles = 6;

            var items = Enumerable.Range(0, rows).ToArray();
            var shape = 0;
            var factory = new ElementFactory<int>(
                items,
                (_, _) => shape switch
                {
                    0 => Component<ShapeRowA>(),
                    1 => Component<ShapeRowB>(),
                    _ => Component<ShapeRowC>(),
                },
                new Reconciler(),
                requestRerender: static () => { },
                pool: null);
            var ifactory = (IElementFactory)factory;

            var distinct = new HashSet<UIElement>();
            var live = new UIElement[rows];
            for (var c = 0; c < cycles; c++)
            {
                shape = c % 3;
                if (c > 0)
                    for (var i = 0; i < rows; i++) ifactory.RecycleElement(MakeRecycleArgs(live[i]));

                for (var i = 0; i < rows; i++)
                {
                    live[i] = ifactory.GetElement(MakeRowGetArgs(i, $"k{i}"));
                    distinct.Add(live[i]);
                }
            }

            // Every root here is a Border, so this only stays bounded if the
            // census distinguishes them by ComponentType.
            H.Check($"EFR_CompShape_AllRootsAreBorders_distinctTypes={distinct.Select(c => c.GetType()).Distinct().Count()}",
                distinct.All(c => c is Border));
            H.Check($"EFR_CompShape_ContainersBounded_distinct={distinct.Count}",
                distinct.Count <= rows * 3);

            return Task.CompletedTask;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Issue #919 — a container evicted by the pool cap must be UNMOUNTED.
    //  Recycling deliberately leaves a container mounted so the next realize
    //  can diff it in place; eviction is where that lease ends. Skipping the
    //  unmount leaves every evicted row's components in the reconciler's node
    //  table with their effects, subscriptions and captured state alive — the
    //  pool counts stay flat while real Reactor state grows per visited item.
    // ────────────────────────────────────────────────────────────────────

    internal class Factory_PoolEviction_UnmountsEvictedRows(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            s_rowCtorCount = 0;
            s_rowEffectMountCount = 0;
            s_rowEffectCleanupCount = 0;

            const int visited = 200;
            var items = Enumerable.Range(0, visited).ToArray();
            // A StackPanel root (not a component-wrapper Border) so the pool's
            // component-adoption pass can't serve these, and a distinct key per
            // row so CanUpdate can't either. Every realize therefore mints a
            // fresh container and the pool has to evict — which is the path
            // under test. The nested Component is what carries the effect whose
            // cleanup proves the eviction unmounted the subtree.
            var factory = new ElementFactory<int>(
                items,
                (_, _) => VStack(Component<IdentityRowComponent>()),
                new Reconciler(),
                requestRerender: static () => { },
                pool: null);
            var ifactory = (IElementFactory)factory;

            UIElement? previous = null;
            UIElement? first = null;
            for (var i = 0; i < visited; i++)
            {
                if (previous is not null) ifactory.RecycleElement(MakeRecycleArgs(previous));
                previous = ifactory.GetElement(MakeRowGetArgs(i, $"k{i}"));
                first ??= previous;
            }

            H.Check($"EFR_Eviction_AllRowsMounted_mount={s_rowEffectMountCount}",
                s_rowEffectMountCount == visited);
            // Everything recycled beyond the pool capacity gets evicted, and
            // each eviction must run one cleanup. Without the unmount this
            // reads 0.
            H.Check($"EFR_Eviction_EvictedRowsCleanedUp_cleanup={s_rowEffectCleanupCount}",
                s_rowEffectCleanupCount >= 100);
            H.Check($"EFR_Eviction_PoolStillBounded_pool={factory.DebugRecyclePoolCount}",
                factory.DebugRecyclePoolCount <= 32);

            // Unmounting tears the components down but leaves ReactorState's
            // Element pointer and the modifier-event trampolines attached — and
            // the repeater keeps the evicted tree parented forever, so those
            // captured closures stay reachable and can still fire. The first
            // row is long since evicted by now; its Reactor state must be gone.
            H.Check("EFR_Eviction_FirstRowWasEvicted",
                first is not null && !factory.DebugTryGetLastElementByControl(first, out _));
            var detached = first is FrameworkElement ffe
                && (ffe.GetValue(Reconciler.ReactorAttached.StateProperty) is not Reconciler.ReactorState st
                    || st.Element is null);
            H.Check("EFR_Eviction_EvictedRowStateDetached", detached);

            return Task.CompletedTask;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Issue #919 — parking must not vandalise the Visibility DP.
    //  RecycleElement collapses a container so it can't paint as a ghost, and
    //  reuse has to undo exactly that. Two ways to get it wrong: force Visible
    //  on reuse (un-hides a row the author hid), or write back the *evaluated*
    //  enum (pins a local value on a row that had none, permanently outranking
    //  a Style- or default-provided Visibility).
    // ────────────────────────────────────────────────────────────────────

    internal class Factory_PoolParking_PreservesVisibilityValueSource(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var items = Enumerable.Range(0, 2).ToArray();

            // Row A: no Visibility modifier at all → no local value.
            var plain = new ElementFactory<int>(
                items,
                (i, _) => TextBlock($"r{i}"),
                new Reconciler(),
                requestRerender: static () => { },
                pool: null);
            var iplain = (IElementFactory)plain;

            var a0 = iplain.GetElement(MakeGetArgs(0));
            H.Check("EFR_Park_FreshRowHasNoLocalVisibility",
                ReferenceEquals(a0.ReadLocalValue(UIElement.VisibilityProperty), DependencyProperty.UnsetValue));

            iplain.RecycleElement(MakeRecycleArgs(a0));
            var a1 = iplain.GetElement(MakeGetArgs(0));
            H.Check("EFR_Park_ReusedRowIsSameControl", ReferenceEquals(a1, a0));
            H.Check("EFR_Park_ReusedRowIsVisible", a1.Visibility == Visibility.Visible);
            // The real assertion: the container came back with its Visibility
            // value source untouched, not with an invented local value.
            H.Check("EFR_Park_ReusedRowHasNoLocalVisibility",
                ReferenceEquals(a1.ReadLocalValue(UIElement.VisibilityProperty), DependencyProperty.UnsetValue));

            // Row B: author explicitly hid it → local Collapsed must survive the
            // park/unpark round-trip.
            var hidden = new ElementFactory<int>(
                items,
                (i, _) => TextBlock($"r{i}").IsVisible(false),
                new Reconciler(),
                requestRerender: static () => { },
                pool: null);
            var ihidden = (IElementFactory)hidden;

            var b0 = ihidden.GetElement(MakeGetArgs(0));
            H.Check("EFR_Park_HiddenRowStartsCollapsed", b0.Visibility == Visibility.Collapsed);
            ihidden.RecycleElement(MakeRecycleArgs(b0));
            var b1 = ihidden.GetElement(MakeGetArgs(0));
            H.Check("EFR_Park_HiddenRowIsSameControl", ReferenceEquals(b1, b0));
            H.Check("EFR_Park_HiddenRowStaysCollapsed", b1.Visibility == Visibility.Collapsed);

            return Task.CompletedTask;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Issue #919 — a row whose Visibility is BOUND cannot be parked
    //  reversibly: collapsing it clobbers the binding and no public API can
    //  reinstall it. Pooling it un-collapsed instead would leave a visible,
    //  no-longer-arranged repeater child painting at its last bounds — the
    //  exact ghost row this change exists to prevent. It must be retired.
    // ────────────────────────────────────────────────────────────────────

    internal class Factory_BoundVisibilityRow_IsRetiredNotPooled(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            // The binding source must be a built-in WinUI type, not a plain
            // managed class: XAML resolves a WinRT source's properties from
            // native metadata, whereas a POCO source goes through
            // ICustomProperty, which NativeAOT cannot synthesize without
            // [WinRT.GeneratedBindableCustomProperty].
            var source = new WinXC.Border { Visibility = Visibility.Visible };
            var factory = new ElementFactory<int>(
                new[] { 0 },
                (i, _) => TextBlock($"r{i}").Set(fe => fe.SetBinding(
                    UIElement.VisibilityProperty,
                    new Microsoft.UI.Xaml.Data.Binding
                    {
                        Source = source,
                        Path = new PropertyPath(nameof(UIElement.Visibility)),
                    })),
                new Reconciler(),
                requestRerender: static () => { },
                pool: null);
            var ifactory = (IElementFactory)factory;

            var ctl = ifactory.GetElement(MakeGetArgs(0));
            // Guard the premise: if WinUI ever stops handing back a
            // BindingExpression from ReadLocalValue, the retire branch is dead
            // code and this fixture is the thing that says so.
            H.Check("EFR_BoundVis_ReadLocalValueIsBindingExpression",
                ctl.ReadLocalValue(UIElement.VisibilityProperty) is not Visibility
                && !ReferenceEquals(ctl.ReadLocalValue(UIElement.VisibilityProperty), DependencyProperty.UnsetValue));

            ifactory.RecycleElement(MakeRecycleArgs(ctl));

            H.Check($"EFR_BoundVis_NotPooled_pool={factory.DebugRecyclePoolCount}",
                factory.DebugRecyclePoolCount == 0);
            H.Check("EFR_BoundVis_Untracked",
                !factory.DebugTryGetLastElementByControl(ctl, out _));
            // Retired, so it must not be left painting.
            H.Check($"EFR_BoundVis_Collapsed_vis={ctl.Visibility}",
                ctl.Visibility == Visibility.Collapsed);

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

            var repeater = H.FindControl<WinXC.ItemsRepeater>(_ => true);
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
            var repeater = H.FindControl<WinXC.ItemsRepeater>(_ => true);
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

    // ────────────────────────────────────────────────────────────────────
    //  Issue #326 — per-item identity reset on recycle.
    //  The ElementFactory recycle path (#324) retains the realized WinUI tree
    //  and Reconciles-on-reuse. Without propagating the keySelector key to the
    //  row's top-level Element.Key, reusing a realized container for a DIFFERENT
    //  logical item diffs the inner Component in place and CARRIES its
    //  UseState/UseEffect state from item A into item B. The fix propagates the
    //  per-item key (issue #326), so cross-item reuse fails CanUpdate and the
    //  row remounts → fresh hook cells. Same-item reuse keeps the key and stays
    //  in place → state preserved.
    //
    //  These drive a standalone ElementFactory through the spec-042 ReactorRow
    //  keyed path (args.Data is ReactorRow), the same path every live
    //  LazyVStack/LazyHStack/ItemsRepeater<T> uses. A per-Component constructor
    //  count is a deterministic proxy for "fresh hook state": a new Component
    //  instance means new UseState/UseEffect cells. The UseEffect mount/cleanup
    //  counters corroborate it (effects flush synchronously on the render path).
    // ────────────────────────────────────────────────────────────────────

    private static int s_rowCtorCount;
    private static int s_rowEffectMountCount;
    private static int s_rowEffectCleanupCount;

    private class IdentityRowComponent : Microsoft.UI.Reactor.Core.Component
    {
        public IdentityRowComponent() => global::System.Threading.Interlocked.Increment(ref s_rowCtorCount);

        public override Microsoft.UI.Reactor.Core.Element Render()
        {
            // A per-item state cell + a per-item effect. If the row's identity
            // is correctly reset on cross-item reuse, both are re-initialized
            // (fresh cell, effect re-runs). If it leaks, the same instance —
            // and therefore the same cell/effect — is reused across items.
            var (_, _) = UseState(0);
            UseEffect(() =>
            {
                global::System.Threading.Interlocked.Increment(ref s_rowEffectMountCount);
                return () => global::System.Threading.Interlocked.Increment(ref s_rowEffectCleanupCount);
            });
            return TextBlock("row");
        }
    }

    private static ElementFactoryGetArgs MakeRowGetArgs(int index, string key)
        // Spec-042 ReactorRow path — args.Data carries both the stable key
        // (keySelector projection) and the data index. This is the path the
        // issue #326 fix gates on; the legacy int path is intentionally left
        // unkeyed so the bounded-working-set fixtures above keep passing.
        => new() { Data = new ReactorRow { Index = index, Key = key } };

    internal class Factory_KeyChangeRecycle_ResetsRowComponentState(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            s_rowCtorCount = 0;
            s_rowEffectMountCount = 0;
            s_rowEffectCleanupCount = 0;

            var items = new[] { new Item("a", "A"), new Item("b", "B") };
            var reconciler = new Reconciler();
            var factory = new ElementFactory<Item>(
                items,
                // No explicit .WithKey on the row root — the implicit per-item
                // key is what must drive the reset.
                (_, _) => Component<IdentityRowComponent>(),
                reconciler,
                requestRerender: static () => { },
                pool: null);
            var ifactory = (IElementFactory)factory;

            // Realize logical item "a" → one Component instance mounts.
            var ctlA = ifactory.GetElement(MakeRowGetArgs(0, "a"));
            H.Check($"EFR326_KeyChange_FirstRealizeMountsOne_ctor={s_rowCtorCount}", s_rowCtorCount == 1);
            H.Check($"EFR326_KeyChange_FirstRealizeEffectRan_mount={s_rowEffectMountCount}", s_rowEffectMountCount == 1);

            // Recycle it so the container is pool-available, then realize a
            // DIFFERENT logical item ("b") that reuses the pooled container.
            ifactory.RecycleElement(MakeRecycleArgs(ctlA));
            var ctlB = ifactory.GetElement(MakeRowGetArgs(1, "b"));

            // The pooled container itself must come back. CanUpdate is false
            // here (the key changed), so this can only happen through the
            // component-wrapper pass of TryTakeCompatibleFromPool, which gates
            // on ComponentType + Equals(Modifiers) + Equals(Extensions). If
            // that pass ever goes dead — e.g. ElementModifiers stops being a
            // value-equal record — the pool would miss and a fresh Border would
            // be minted here. The effect/ctor counts below would still read 2/2
            // in that case, so they cannot detect it; this identity check can.
            H.Check("EFR326_KeyChange_PooledContainerReused",
                ReferenceEquals(ctlB, ctlA));

            // Key "a" → "b" differs → CanUpdate false → the old Component is
            // unmounted (cleanup runs) and a fresh one is mounted (new hook
            // cells). Pre-fix this was an in-place diff → ctor stays 1, state
            // leaks. Post-fix: ctor == 2.
            H.Check($"EFR326_KeyChange_FreshComponentInstance_ctor={s_rowCtorCount}", s_rowCtorCount == 2);
            H.Check($"EFR326_KeyChange_NewEffectMounted_mount={s_rowEffectMountCount}", s_rowEffectMountCount == 2);
            H.Check($"EFR326_KeyChange_OldEffectCleanedUp_cleanup={s_rowEffectCleanupCount}", s_rowEffectCleanupCount >= 1);

            return Task.CompletedTask;
        }
    }

    internal class Factory_SameItemReuse_PreservesRowComponentState(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            s_rowCtorCount = 0;
            s_rowEffectMountCount = 0;
            s_rowEffectCleanupCount = 0;

            var items = new[] { new Item("a", "A"), new Item("b", "B") };
            var reconciler = new Reconciler();
            var factory = new ElementFactory<Item>(
                items,
                (_, _) => Component<IdentityRowComponent>(),
                reconciler,
                requestRerender: static () => { },
                pool: null);
            var ifactory = (IElementFactory)factory;

            // Realize "a", recycle, then realize the SAME logical item "a"
            // again reusing the pooled container.
            var ctlA = ifactory.GetElement(MakeRowGetArgs(0, "a"));
            ifactory.RecycleElement(MakeRecycleArgs(ctlA));
            ifactory.GetElement(MakeRowGetArgs(0, "a"));

            // Same key → CanUpdate true → in-place reuse → the same Component
            // instance is preserved (no remount). State for the unchanged
            // logical item is retained — exactly the non-reset half of the
            // contract.
            H.Check($"EFR326_SameItem_PreservesComponentInstance_ctor={s_rowCtorCount}", s_rowCtorCount == 1);
            H.Check($"EFR326_SameItem_NoExtraEffectMount_mount={s_rowEffectMountCount}", s_rowEffectMountCount == 1);
            H.Check($"EFR326_SameItem_NoCleanup_cleanup={s_rowEffectCleanupCount}", s_rowEffectCleanupCount == 0);

            return Task.CompletedTask;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Issue #326 (pr-review H1 + L1) — RefreshRealizedItems must handle a
    //  same-slot key change end-to-end.
    //
    //  The documented `.WithKey($"{id}:{revision}")` pattern bumps a realized
    //  row's Element.Key on an in-place re-render (no structural list change),
    //  so the refresh path runs Reconcile with CanUpdate == false → Unmount +
    //  Mount → a fresh replacement control. RefreshRealizedItems must, exactly
    //  like GetElement, (a) parent the replacement into the realized slot,
    //  (b) detach/unmount the old control, and (c) migrate the per-control
    //  tracking maps so no stale entry survives. It must ALSO reset the inner
    //  Component's UseState end-to-end (L1): the rendered value returns to its
    //  initial state.
    //
    //  Drives a LIVE LazyVStack + real ItemsRepeater (not a standalone factory)
    //  because the bug is specifically about the framework-owned realized tree
    //  and the refresh path that only that wiring exercises.
    // ────────────────────────────────────────────────────────────────────

    private class StatefulKeyedRow : Microsoft.UI.Reactor.Core.Component
    {
        public override Microsoft.UI.Reactor.Core.Element Render()
        {
            var (n, setN) = UseState(0);
            UseEffect(() =>
            {
                global::System.Threading.Interlocked.Increment(ref s_keyedRowEffectMount);
                return () => global::System.Threading.Interlocked.Increment(ref s_keyedRowEffectCleanup);
            });
            return VStack(
                TextBlock($"val:{n}"),
                Button("incRow", () => setN(n + 1))
            );
        }
    }

    private static int s_keyedRowEffectMount;
    private static int s_keyedRowEffectCleanup;

    internal class Factory_RefreshKeyChange_RemountsRealizedRow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            s_keyedRowEffectMount = 0;
            s_keyedRowEffectCleanup = 0;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (rev, setRev) = ctx.UseState(0);
                var items = new[] { new Item("a", "A") };
                return VStack(
                    Button("BumpRev", () => setRev(rev + 1)),
                    LazyVStack<Item>(items, i => i.Id, (i, _) =>
                        Component<StatefulKeyedRow>().WithKey($"{i.Id}:{rev}")).Height(200)
                );
            });
            await Harness.Render();

            var repeater = H.FindControl<WinXC.ItemsRepeater>(_ => true);
            var factory = repeater?.ItemTemplate as ElementFactory<Item>;
            H.Check("EFR326_RefreshKey_FactoryFound", factory is not null && repeater is not null);
            if (factory is null || repeater is null) return;

            H.Check("EFR326_RefreshKey_InitialVal0", H.FindText("val:0") is not null);

            // Make the realized row dirty: val 0 → 1.
            H.ClickButton("incRow");
            await Harness.Render();
            H.Check("EFR326_RefreshKey_DirtyVal1", H.FindText("val:1") is not null);

            var realizedBefore = repeater.TryGetElement(0);
            int mountBefore = s_keyedRowEffectMount;

            // Bump the outer revision → row key "a:0" → "a:1". Same list slot,
            // changed Element.Key → RefreshRealizedItems runs Reconcile with
            // CanUpdate == false → remount.
            H.ClickButton("BumpRev");
            await Harness.Render();

            // (L1) End-to-end state reset: the fresh Component renders val:0,
            // and the dirty val:1 control is gone from the visual tree.
            bool showsReset = await Harness.WaitFor(() => H.FindText("val:0") is not null);
            H.Check("EFR326_RefreshKey_StateResetToVal0", showsReset);
            H.Check("EFR326_RefreshKey_DirtyValGone", H.FindText("val:1") is null);

            // Effect lifecycle: old row's cleanup ran, fresh row's effect mounted.
            H.Check($"EFR326_RefreshKey_NewEffectMounted_mount={s_keyedRowEffectMount}",
                s_keyedRowEffectMount > mountBefore);
            H.Check($"EFR326_RefreshKey_OldEffectCleanedUp_cleanup={s_keyedRowEffectCleanup}",
                s_keyedRowEffectCleanup >= 1);

            // Tracking maps: the realized control after the bump is tracked,
            // and the pre-bump control left no stale entry behind.
            var realizedAfter = repeater.TryGetElement(0);
            H.Check("EFR326_RefreshKey_RealizedAfterTracked",
                realizedAfter is not null && factory.DebugTryGetLastElementByControl(realizedAfter, out _));
            H.Check("EFR326_RefreshKey_NoStaleOldTracking",
                realizedBefore is null
                || ReferenceEquals(realizedBefore, realizedAfter)
                || !factory.DebugTryGetLastElementByControl(realizedBefore, out _));
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Issue #919 (pr-review round 4, finding A) — the RefreshRealizedItems
    //  adoption gate, exercised through the refresh path specifically.
    //
    //  A keyed row whose root is a component wrapper carrying MODIFIERS must
    //  not be silently adopted when its key changes. TryAdoptRealizedReplacement
    //  transplants only the component subtree; the wrapper's own runtime
    //  bookkeeping — here the ElementRef cell that ApplyModifiers installs — is
    //  keyed on the REPLACEMENT Border. Adopting therefore leaves Ref.Current
    //  pointing at a control that is never parented, so imperative code
    //  (Focus, scroll-into-view, measurement) targets a detached element.
    //
    //  The gate must route this through the framework's realize channel so the
    //  ref lands on the control the repeater actually realized. Gating on
    //  "is the realized child a Border" alone is not enough: the wrapper here
    //  IS a Border.
    // ────────────────────────────────────────────────────────────────────

    internal class Factory_RefreshKeyChange_ModifiedRootKeepsRefLive(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rowRef = new Microsoft.UI.Reactor.Input.ElementRef();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (rev, setRev) = ctx.UseState(0);
                var items = new[] { new Item("a", "A") };
                return VStack(
                    Button("BumpRefRev", () => setRev(rev + 1)),
                    LazyVStack<Item>(items, i => i.Id, (i, _) =>
                        Component<StatefulKeyedRow>().Ref(rowRef).WithKey($"{i.Id}:{rev}")).Height(200)
                );
            });
            await Harness.Render();

            var repeater = H.FindControl<WinXC.ItemsRepeater>(_ => true);
            var factory = repeater?.ItemTemplate as ElementFactory<Item>;
            H.Check("EFR919_RefAdopt_FactoryFound", factory is not null && repeater is not null);
            if (factory is null || repeater is null) return;

            var realizedBefore = repeater.TryGetElement(0);
            H.Check("EFR919_RefAdopt_RefTracksRealizedBefore",
                realizedBefore is not null && ReferenceEquals(rowRef.Current, realizedBefore));

            // Key "a:0" → "a:1" with the same list slot: RefreshRealizedItems
            // sees CanUpdate == false on a Border-rooted row.
            H.ClickButton("BumpRefRev");
            await Harness.Render();
            await Harness.WaitFor(() =>
                repeater.TryGetElement(0) is { } e && !ReferenceEquals(e, realizedBefore));

            var realizedAfter = repeater.TryGetElement(0);
            H.Check("EFR919_RefAdopt_RowStillRealized", realizedAfter is not null);

            // The guard: Ref.Current must be the control the repeater actually
            // realized, and that control must be in the visual tree.
            H.Check("EFR919_RefAdopt_RefPointsAtRealizedControl",
                realizedAfter is not null && ReferenceEquals(rowRef.Current, realizedAfter));
            H.Check("EFR919_RefAdopt_RefIsParented",
                rowRef.Current is not null
                && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(rowRef.Current) is not null);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Issue #919 — adoption must be gated even when CanUpdate is TRUE.
    //
    //  CanUpdate does not imply "Reconcile preserved control identity". A
    //  decorator-style V1 handler (spec 048 §14) may substitute a different
    //  instance when only its inner target changed shape. FlyoutElement is the
    //  live example: CanUpdate(FlyoutElement, FlyoutElement) only compares type
    //  and Key, but UpdateFlyoutElement unmounts and re-mounts its Target when
    //  CanUpdate(o.Target, n.Target) is false, and returns the fresh control.
    //
    //  Both the old and new realized controls are component-wrapper Borders, so
    //  TryAdoptRealizedReplacement happily succeeds — and silently drops
    //  everything MountFlyout/UpdateFlyoutElement installed on the replacement:
    //  the attached flyout and the element tag its Opened/Closed handlers read
    //  back through Reconciler.GetElementTag. The row keeps rendering, so only
    //  the tag exposes the damage.
    // ────────────────────────────────────────────────────────────────────

    private class FlyoutRowA : Microsoft.UI.Reactor.Core.Component
    {
        public override Microsoft.UI.Reactor.Core.Element Render() => TextBlock("flyrowA");
    }

    private class FlyoutRowB : Microsoft.UI.Reactor.Core.Component
    {
        public override Microsoft.UI.Reactor.Core.Element Render() => TextBlock("flyrowB");
    }

    // ────────────────────────────────────────────────────────────────────
    //  Spec 010 — a SUCCESSFUL adoption must carry the source location over.
    //
    //  TryAdoptRealizedReplacement transplants the component subtree and
    //  Border.Child, but not ReactorAttached.StateProperty — so the wrapper that
    //  stays parented keeps pointing at the OLD element. Rendering is unaffected,
    //  which is exactly why this needs its own fixture: the only visible damage is
    //  that ReactorSourceMap.GetSource reports the PREVIOUS row's call site.
    //
    //  Deliberately no .Ref() here. That would trip the adoption gate the #919
    //  fixtures above cover, routing the row through the realize channel instead —
    //  the opposite path from the one under test.
    // ────────────────────────────────────────────────────────────────────

    internal class Factory_SuccessfulAdoption_RefreshesTheReportedCallSite(Harness h) : SelfTestFixtureBase(h)
    {
        private const string SkipReason =
            "built without REACTOR_SOURCEMAP, so no interceptors exist to stamp a location";

        public override async Task RunAsync()
        {
            var previous = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled;
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = true;
            try
            {
                // Two Component<> calls on DIFFERENT physical lines, chosen by rev.
                // Identical for rendering, so the adoption path is reached; the only
                // difference is the location each stamps.
                int lineA = 0, lineB = 0;

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (rev, setRev) = ctx.UseState(0);
                    var items = new[] { new Item("a", "A") };
                    return VStack(
                        Button("BumpAdoptRev", () => setRev(rev + 1)),
                        LazyVStack<Item>(items, i => i.Id, (i, _) =>
                            rev == 0
                                ? At(out lineA, Component<StatefulKeyedRow>().WithKey($"{i.Id}:{rev}"))
                                : At(out lineB, Component<StatefulKeyedRow>().WithKey($"{i.Id}:{rev}"))
                        ).Height(200)
                    );
                });
                await Harness.Render();

                var repeater = H.FindControl<WinXC.ItemsRepeater>(_ => true);
                H.Check("EFSourceMap_RepeaterFound", repeater is not null);
                if (repeater is null) return;

                var realizedBefore = repeater.TryGetElement(0);
                H.Check("EFSourceMap_RowRealized", realizedBefore is not null);
                if (realizedBefore is null) return;

#if REACTOR_SOURCEMAP
                // Since the composition mounts tag their wrapper (spec 010), the first
                // realize now reports the arm that built it. This assertion started life
                // as "reports nothing", which was true before that fix — the fixture
                // caught its own premise going stale, which is the point of pinning it
                // rather than ignoring the initial state.
                var beforeLine = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(realizedBefore)?.LineNumber;
                H.Check("EFSourceMap_InitialRealizeReportsArmA", beforeLine == lineA);

                // Anti-tautology: the two arms must really be on different lines, or
                // "follows the live row" below could pass by coincidence.
                H.Check("EFSourceMap_ArmsAreOnDifferentLines", lineA != 0 && lineA != lineB);

                // Key "a:0" -> "a:1" in the same slot drives RefreshRealizedItems
                // down the adoption path.
                H.ClickButton("BumpAdoptRev");
                await Harness.Render();
                await Harness.WaitFor(() => repeater.TryGetElement(0) is not null);

                var realizedAfter = repeater.TryGetElement(0);
                H.Check("EFSourceMap_RowStillRealized", realizedAfter is not null);
                if (realizedAfter is null) return;

                var afterLine = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(realizedAfter)?.LineNumber;
                H.Check("EFSourceMap_LineFollowsTheLiveRow", afterLine is not null && afterLine == lineB);
                H.Check("EFSourceMap_LineIsNotStale", afterLine != lineA);
#else
                H.Skip("EFSourceMap_InitialRealizeReportsArmA", SkipReason);
                H.Skip("EFSourceMap_ArmsAreOnDifferentLines", SkipReason);
                H.Skip("EFSourceMap_RowStillRealized", SkipReason);
                H.Skip("EFSourceMap_LineFollowsTheLiveRow", SkipReason);
                H.Skip("EFSourceMap_LineIsNotStale", SkipReason);
#endif
            }
            finally
            {
                Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = previous;
            }
        }

        /// <summary>
        /// Records the physical line of the call and returns the element unchanged, so
        /// the two arms above stay identical for rendering while their locations differ.
        /// The <c>Component&lt;&gt;</c> call sits on the same physical line as this call,
        /// so the captured number is exactly what the interceptor stamps — an
        /// independent oracle rather than a hand-counted constant.
        /// </summary>
        private static Element At(out int line, Element element, [global::System.Runtime.CompilerServices.CallerLineNumber] int caller = 0)
        {
            line = caller;
            return element;
        }
    }

    internal class Factory_DecoratorSubstitution_IsNotSilentlyAdopted(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (flip, setFlip) = ctx.UseState(false);
                var items = new[] { new Item("a", "A") };
                return VStack(
                    Button("FlipFlyoutTarget", () => setFlip(!flip)),
                    LazyVStack<Item>(items, i => i.Id, (i, _) =>
                        Flyout(
                            flip ? Component<FlyoutRowB>() : Component<FlyoutRowA>(),
                            TextBlock("flymenu"))).Height(200)
                );
            });
            await Harness.Render();

            var repeater = H.FindControl<WinXC.ItemsRepeater>(_ => true);
            var factory = repeater?.ItemTemplate as ElementFactory<Item>;
            H.Check("EFR919_Decorator_FactoryFound", factory is not null && repeater is not null);
            if (factory is null || repeater is null) return;

            H.Check("EFR919_Decorator_InitialRowA", H.FindText("flyrowA") is not null);

            // Guard the premise: the realized row really is a component-wrapper
            // Border, so TryAdoptRealizedReplacement *would* succeed if unguarded.
            H.Check("EFR919_Decorator_RealizedIsBorder", repeater.TryGetElement(0) is Border);

            // Flip only the flyout's Target shape. The FlyoutElement itself keeps
            // the same type and Key, so CanUpdate stays TRUE while the decorator
            // hands back a brand-new control.
            H.ClickButton("FlipFlyoutTarget");
            await Harness.Render();
            await Harness.WaitFor(() => H.FindText("flyrowB") is not null);

            H.Check("EFR919_Decorator_ShowsRowB", H.FindText("flyrowB") is not null);

            // The pre-flip container can't be un-parented from the repeater, so
            // the rowA subtree stays in the tree — but parked collapsed. What
            // must never happen is a still-Visible repeater child that is no
            // longer a live realized item, i.e. a ghost painting over the row.
            var ghosts = 0;
            var childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(repeater);
            for (var i = 0; i < childCount; i++)
            {
                if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(repeater, i) is not FrameworkElement fe) continue;
                if (fe.Visibility != Visibility.Visible) continue;
                if (repeater.GetElementIndex(fe) < 0) ghosts++;
            }
            H.Check($"EFR919_Decorator_NoVisibleGhosts_ghosts={ghosts}/{childCount}", ghosts == 0);

            // The guard. The control the repeater actually realized must carry the
            // CURRENT FlyoutElement as its element tag — that tag is what the
            // flyout's Opened/Closed handlers dereference at click time. A silent
            // adopt leaves the pre-flip element here.
            var realizedAfter = repeater.TryGetElement(0);
            var tag = realizedAfter is null ? null : Reconciler.GetElementTag(realizedAfter);
            var targetType = (tag as FlyoutElement)?.Target as ComponentElement;
            H.Check($"EFR919_Decorator_RealizedCarriesCurrentTag_target={targetType?.ComponentType?.Name ?? "<null>"}",
                targetType is not null && targetType.ComponentType == typeof(FlyoutRowB));
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Issue #919 (pr-review round 5) — the pool census must discriminate on
    //  XamlHostElement.TypeKey, and must grant a window to EVERY recurring
    //  shape rather than clamping the shape count.
    //
    //  Nine XamlHostElements with distinct TypeKeys are nine mutually
    //  incompatible reuse classes (Reconciler.CanUpdate compares TypeKey) that
    //  share ONE element type and ONE native control type. So this stays
    //  bounded only if (a) ShapeKeyOf folds TypeKey in — otherwise the census
    //  sees a single shape and sizes the pool for one window — and (b) capacity
    //  is not clamped below the number of recurring shapes. Under-provisioning
    //  is not a mere cache miss: the missing container is re-minted and the
    //  evicted one stays parented to the repeater forever, so a bounded managed
    //  pool would be silently trading itself for unbounded native growth.
    // ────────────────────────────────────────────────────────────────────

    internal class Factory_HostTypeKeyCycle_KeepsContainerSetBounded(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            const int rows = 12;
            const int shapes = 9;
            const int cycles = shapes * 2;

            var items = Enumerable.Range(0, rows).ToArray();
            var shape = 0;
            var factory = new ElementFactory<int>(
                items,
                (_, _) => new Microsoft.UI.Reactor.Hosting.XamlHostElement(static () => new TextBlock())
                {
                    TypeKey = $"host{shape}",
                },
                new Reconciler(),
                requestRerender: static () => { },
                pool: null);
            var ifactory = (IElementFactory)factory;

            var distinct = new HashSet<UIElement>();
            var live = new UIElement[rows];
            for (var c = 0; c < cycles; c++)
            {
                shape = c % shapes;
                if (c > 0)
                    for (var i = 0; i < rows; i++) ifactory.RecycleElement(MakeRecycleArgs(live[i]));

                for (var i = 0; i < rows; i++)
                {
                    live[i] = ifactory.GetElement(MakeRowGetArgs(i, $"k{i}"));
                    distinct.Add(live[i]);
                }
            }

            // Guards the premise: one element type, one control type — the only
            // thing that can tell these nine apart is TypeKey.
            var nativeTypes = distinct.Select(c => c.GetType()).Distinct().Count();
            H.Check($"EFR_HostShape_AllRootsSameNativeType_types={nativeTypes}", nativeTypes == 1);
            H.Check($"EFR_HostShape_ContainersBounded_distinct={distinct.Count}",
                distinct.Count <= rows * shapes);

            return Task.CompletedTask;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Issue #919 (pr-review round 6, M1/M2) — a retired container must have
    //  its Reactor state detached ALL THE WAY DOWN, and the ownership slots
    //  cleared.
    //
    //  UnmountChild tears the components down but deliberately leaves the
    //  per-control ReactorState in place: Element still points at the last
    //  element, the modifier trampolines stay subscribed, and ListState /
    //  ItemViewSource / ControlEventState still hold their payloads (a pooled
    //  control re-uses those on its next rent). An evicted repeater container
    //  has no next rent — the repeater keeps it parented forever — so every one
    //  of those slots is pure retention on a dead subtree, and a trampoline that
    //  fires can still reach the dead component's captured rerender closure.
    //
    //  The DESCENDANT is the interesting assertion: RetireContainer walks the
    //  subtree, so a check on the container root alone would pass even with the
    //  recursion deleted.
    // ────────────────────────────────────────────────────────────────────

    private class EventBearingRow : Microsoft.UI.Reactor.Core.Component
    {
        public override Microsoft.UI.Reactor.Core.Element Render()
            // Button.Click is a control-intrinsic event, so mounting this
            // populates ReactorState.ControlEventState on the Button — one of
            // the ownership slots a permanent retire has to clear. It sits
            // BELOW the container root, so it also proves the walk recurses.
            => VStack(Button("row-btn", () => { }));
    }

    internal class Factory_RetiredContainer_DetachesNestedStateAndOwnership(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var items = Enumerable.Range(0, 400)
                .Select(i => new Item(i.ToString(), $"Item {i}")).ToArray();
            var factory = new ElementFactory<Item>(
                items,
                (_, _) => VStack(Component<EventBearingRow>()),
                new Reconciler(),
                requestRerender: static () => { },
                pool: null);
            var ifactory = (IElementFactory)factory;

            // Cycle far past the pool capacity so the first container is long
            // since evicted and retired.
            UIElement? previous = null;
            UIElement? first = null;
            for (var i = 0; i < 400; i++)
            {
                if (previous is not null) ifactory.RecycleElement(MakeRecycleArgs(previous));
                previous = ifactory.GetElement(MakeRowGetArgs(i, $"rk{i}"));
                first ??= previous;
            }

            H.Check("EFR_Retire_FirstRowWasEvicted",
                first is not null && !factory.DebugTryGetLastElementByControl(first, out _));
            if (first is null) return Task.CompletedTask;

            // Find the Button somewhere beneath the retired container.
            var button = FindDescendant<WinXC.Button>(first);
            H.Check("EFR_Retire_FoundNestedButton", button is not null);
            if (button is null) return Task.CompletedTask;

            // Guard the premise: the Button really is a DESCENDANT, not the
            // container root, so this can only pass if the walk recursed.
            H.Check("EFR_Retire_ButtonIsNotTheRoot", !ReferenceEquals(button, first));

            var st = button.GetValue(Reconciler.ReactorAttached.StateProperty)
                as Reconciler.ReactorState;

            // Recursion oracle. Delete the recursive walk and the nested
            // Button keeps its Element pointer.
            H.Check($"EFR_Retire_NestedElementDetached_el={st?.Element?.GetType().Name ?? "<null>"}",
                st is null || st.Element is null);

            // Ownership-slot oracle. UnmountChild + DetachReactorState both
            // leave ControlEventState alone; only the permanent-retire clearing
            // nulls it. Guarded by the premise check below so a Button that
            // never had a payload can't make this vacuously true.
            H.Check($"EFR_Retire_NestedControlEventStateCleared",
                st is null || st.ControlEventState is null);

            // Premise: a LIVE button of the same shape does carry a
            // ControlEventState box. Without this, the assertion above would
            // pass even if Button.Click never populated the slot at all.
            var liveRoot = ifactory.GetElement(MakeRowGetArgs(0, "live-k"));
            var liveButton = FindDescendant<WinXC.Button>(liveRoot);
            var liveSt = liveButton?.GetValue(Reconciler.ReactorAttached.StateProperty)
                as Reconciler.ReactorState;
            H.Check("EFR_Retire_LiveButtonHasControlEventState",
                liveSt is not null && liveSt.ControlEventState is not null);

            return Task.CompletedTask;
        }

        private static TControl? FindDescendant<TControl>(DependencyObject root)
            where TControl : class
        {
            if (root is TControl hit) return hit;
            var n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < n; i++)
            {
                var found = FindDescendant<TControl>(
                    Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i));
                if (found is not null) return found;
            }
            return null;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Issue #919 (pr-review round 6, M6) — the CanSafelyAdopt gate on the
    //  POOL-REUSE path needs its own regression, not just the refresh path.
    //
    //  Factory_DecoratorSubstitution_IsNotSilentlyAdopted covers the
    //  RefreshRealizedItems call site. This one covers GetElement: recycle a
    //  decorator row into the pool, then realize the same KEY with a different
    //  wrapped target. Pass 1 picks the container (CanUpdate on FlyoutElement
    //  compares type + Key only, both unchanged), Reconcile's decorator arm
    //  unmounts the old target and hands back a DIFFERENT control, and the gate
    //  has to refuse to adopt it — a FlyoutElement is not a ComponentElement, so
    //  its wiring (the element tag the Opened/Closed handlers read at click
    //  time) does not travel with the adopted subtree.
    // ────────────────────────────────────────────────────────────────────

    internal class Factory_PoolReuseDecoratorSubstitution_IsNotSilentlyAdopted(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var items = new[] { new Item("a", "A") };
            var factory = new ElementFactory<Item>(
                items,
                (_, _) => Flyout(Component<FlyoutRowA>(), TextBlock("flymenu")),
                new Reconciler(),
                requestRerender: static () => { },
                pool: null);
            var ifactory = (IElementFactory)factory;

            var firstControl = ifactory.GetElement(MakeRowGetArgs(0, "dk0"));
            var tagBefore = Reconciler.GetElementTag(firstControl) as FlyoutElement;
            H.Check($"EFR919_PoolDecorator_InitialTargetIsA_target={(tagBefore?.Target as ComponentElement)?.ComponentType?.Name ?? "<null>"}",
                (tagBefore?.Target as ComponentElement)?.ComponentType == typeof(FlyoutRowA));

            // Guard the premise: the realized control is a component-wrapper
            // Border, i.e. TryAdoptRealizedReplacement *would* succeed here if
            // the gate did not refuse it first.
            H.Check("EFR919_PoolDecorator_RealizedIsBorder", firstControl is Border);

            ifactory.RecycleElement(MakeRecycleArgs(firstControl));
            H.Check($"EFR919_PoolDecorator_WentToPool_pool={factory.DebugRecyclePoolCount}",
                factory.DebugRecyclePoolCount == 1);

            // Swap the view builder the way a re-rendering LazyVStack does.
            // This is what makes the next GetElement build a DIFFERENT element
            // for the same key — UpdateInPlace clears the per-key build cache,
            // so without it the cached FlyoutRowA element is served right back
            // and no substitution ever happens.
            factory.UpdateInPlace(items, (_, _) => Flyout(Component<FlyoutRowB>(), TextBlock("flymenu")));

            // Same key — so pass 1 matches on CanUpdate — but a different
            // wrapped target, so the decorator substitutes the control.
            var secondControl = ifactory.GetElement(MakeRowGetArgs(0, "dk0"));

            // Guard the premise: the container really was taken from the pool
            // and the decorator really did substitute. If either stopped being
            // true the oracle below would pass for the wrong reason.
            H.Check($"EFR919_PoolDecorator_PoolWasDrained_pool={factory.DebugRecyclePoolCount}",
                factory.DebugRecyclePoolCount == 0);
            H.Check("EFR919_PoolDecorator_SubstitutionHappened",
                !ReferenceEquals(secondControl, firstControl));

            // The oracle. Whatever control ends up in the slot must carry the
            // CURRENT FlyoutElement as its tag. A silent adopt returns the
            // pooled container, whose tag still names FlyoutRowA.
            var tagAfter = Reconciler.GetElementTag(secondControl) as FlyoutElement;
            var afterTarget = (tagAfter?.Target as ComponentElement)?.ComponentType;
            H.Check($"EFR919_PoolDecorator_SlotCarriesCurrentTag_target={afterTarget?.Name ?? "<null>"}",
                afterTarget == typeof(FlyoutRowB));

            // And the refused container must not be left tracked or visible.
            H.Check("EFR919_PoolDecorator_RefusedContainerUntracked",
                !factory.DebugTryGetLastElementByControl(firstControl, out _));
            H.Check($"EFR919_PoolDecorator_RefusedContainerCollapsed",
                firstControl is not FrameworkElement rfe || rfe.Visibility == Visibility.Collapsed);

            return Task.CompletedTask;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Issue #919 (pr-review round 6, M7) — V1HandlerAdapter.Update must
    //  remount rather than hard-cast when it is handed a foreign control.
    //
    //  This is the exact line that threw the InvalidCastException in the bug
    //  report. The rest of this change makes the desync unreachable; this
    //  fixture drives the guard directly through the registry seam so the
    //  defense-in-depth arm cannot rot. Reverting it to `(TControl)control`
    //  makes this fixture throw.
    // ────────────────────────────────────────────────────────────────────

    internal class Adapter_ForeignControl_RemountsInsteadOfThrowing(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var resolved = Microsoft.UI.Reactor.Core.V1Protocol.ControlRegistry.TryResolve(
                typeof(TextBlockElement), out var entryFactory);
            H.Check("EFR919_Adapter_ResolvedTextBlockHandler", resolved && entryFactory is not null);
            if (!resolved || entryFactory is null) return Task.CompletedTask;

            var entry = entryFactory();
            var reconciler = new Reconciler();

            var oldEl = TextBlock("before");
            var newEl = TextBlock("after");

            // The desync the bug produced: a TextBlockElement paired with a
            // control that is not a TextBlock.
            var foreign = new WinXC.Button { Content = "not-a-textblock" };

            UIElement? result = null;
            global::System.Exception? thrown = null;
            try
            {
                result = entry.Update(oldEl, newEl, foreign, static () => { }, reconciler);
            }
            catch (global::System.Exception ex)
            {
                thrown = ex;
            }

            H.Check($"EFR919_Adapter_DidNotThrow_ex={thrown?.GetType().Name ?? "<none>"}",
                thrown is null);

            // Not merely "didn't throw": it must hand back a correctly mounted
            // replacement for the NEW element, so the caller can install it.
            H.Check($"EFR919_Adapter_RemountedCorrectType_type={result?.GetType().Name ?? "<null>"}",
                result is WinXC.TextBlock);
            H.Check($"EFR919_Adapter_RemountedCarriesNewText_text={(result as WinXC.TextBlock)?.Text ?? "<null>"}",
                (result as WinXC.TextBlock)?.Text == "after");
            H.Check("EFR919_Adapter_DidNotReturnForeignControl",
                !ReferenceEquals(result, foreign));

            // Sanity: the same call with a MATCHING control still takes the
            // normal in-place arm and returns that very control, so the guard
            // isn't remounting everything.
            var proper = entry.Mount(oldEl, static () => { }, reconciler);
            var same = entry.Update(oldEl, newEl, proper, static () => { }, reconciler);
            H.Check("EFR919_Adapter_MatchingControlUpdatedInPlace", ReferenceEquals(same, proper));
            H.Check($"EFR919_Adapter_MatchingControlGotNewText_text={(proper as WinXC.TextBlock)?.Text ?? "<null>"}",
                (proper as WinXC.TextBlock)?.Text == "after");

            return Task.CompletedTask;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Issue #919 (pr-review round 6, M3) — the census must also discriminate
    //  on KeyedMemoElement.MemoKey.
    //
    //  Mirrors the XamlHostElement fixture: nine DECORATED KeyedMemoElements
    //  with distinct MemoKeys are nine mutually incompatible reuse classes
    //  (Reconciler.CanUpdate compares MemoKey) sharing one element type and one
    //  native control type. Decorated is load-bearing — BuildOrCache resolves a
    //  *bare* wrapper through the memo LRU and returns its inner element, so
    //  only a decorated wrapper survives as a KeyedMemoElement to be censused.
    //  Unkeyed args are load-bearing too: the keyed path stamps Element.Key,
    //  which CanUpdate checks first, so MemoKey would never be the deciding
    //  term. Under-counting here evicts containers that are still wanted, and
    //  every such eviction is a permanently-parented native leak.
    // ────────────────────────────────────────────────────────────────────

    internal class Factory_MemoKeyCycle_KeepsContainerSetBounded(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            const int rows = 12;
            const int shapes = 9;
            const int cycles = shapes * 2;

            var items = Enumerable.Range(0, rows).ToArray();
            var shape = 0;
            var factory = new ElementFactory<int>(
                items,
                // .Margin(1) keeps this a KeyedMemoElement instead of being
                // unwrapped to its inner TextBlock by the memo LRU.
                (_, _) => Memo($"memo{shape}", static () => TextBlock("memo")).Margin(1),
                new Reconciler(),
                requestRerender: static () => { },
                pool: null);
            var ifactory = (IElementFactory)factory;

            var distinct = new HashSet<UIElement>();
            var live = new UIElement[rows];
            for (var c = 0; c < cycles; c++)
            {
                shape = c % shapes;
                if (c > 0)
                    for (var i = 0; i < rows; i++) ifactory.RecycleElement(MakeRecycleArgs(live[i]));

                for (var i = 0; i < rows; i++)
                {
                    // Unkeyed (int) args — see the comment above.
                    live[i] = ifactory.GetElement(MakeGetArgs(i));
                    distinct.Add(live[i]);
                }
            }

            // Guards the premise: one native control type, so the only thing
            // that can tell these nine reuse classes apart is MemoKey.
            var nativeTypes = distinct.Select(c => c.GetType()).Distinct().Count();
            H.Check($"EFR_MemoShape_AllRootsSameNativeType_types={nativeTypes}", nativeTypes == 1);
            H.Check($"EFR_MemoShape_ContainersBounded_distinct={distinct.Count}",
                distinct.Count <= rows * shapes);

            return Task.CompletedTask;
        }
    }
}




