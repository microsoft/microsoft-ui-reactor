using System.Linq;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting.Etw;
using Microsoft.UI.Reactor.Hosting.LayoutCost;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selftest coverage for the layout-cost overlay's attribution layer
/// reacting to tree changes — the user-facing failure mode is "switch
/// tabs while the overlay is on, new Components don't get outlines".
///
/// We don't exercise the Composition renderer or the ETW path here; the
/// renderer is a thin in-place mutation of the snapshot, and ETW requires
/// privileges the test bot doesn't reliably have. Attribution is the
/// load-bearing piece.
/// </summary>
internal static class LayoutCostOverlayTests
{
    // ── Components used by the fixtures ─────────────────────────────────

    private class LeafA : Component
    {
        public override Element Render() => Border(TextBlock("A")).Width(120).Height(60);
    }

    private class LeafB : Component
    {
        public override Element Render() => Border(TextBlock("B")).Width(140).Height(80);
    }

    private class LeafC : Component
    {
        public override Element Render() => Border(TextBlock("C")).Width(160).Height(100);
    }

    /// <summary>Root component whose render varies between two stable
    /// children (LeafA + LeafB) and three (LeafA + LeafB + LeafC), driven
    /// by a state cell so the fixture can flip it via setState.</summary>
    private class TreeShape : Component
    {
        public static global::System.Action? IncludeC; // wired in Render via setState

        public override Element Render()
        {
            var (includeC, setIncludeC) = UseState(false);
            IncludeC = () => setIncludeC(true);

            return VStack(8,
                Component<LeafA>(),
                Component<LeafB>(),
                includeC ? Component<LeafC>() : (Element?)null
            );
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static IReadOnlyList<ComponentSnapshot> Snapshot(Hosting.ReactorHost host)
    {
        host.FlushLayoutCostNow();
        return host.LayoutCostReporter?.GetSnapshot() ?? global::System.Array.Empty<ComponentSnapshot>();
    }

    private static int CountNonChrome(IReadOnlyList<ComponentSnapshot> snap) =>
        snap.Count(s => !s.Id.IsChrome);

    private static bool HasComponent(IReadOnlyList<ComponentSnapshot> snap, string name) =>
        snap.Any(s => !s.Id.IsChrome && s.DisplayName == name);

    // ── Tree mounted before flag-flip is back-filled ─────────────────────

    /// <summary>Components mounted before <c>ShowLayoutCost</c> flipped on
    /// must still appear in the snapshot once the flag is observed.</summary>
    internal class FlagOn_BackFillsExistingComponents(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.ShowLayoutCost;
            try
            {
                ReactorFeatureFlags.ShowLayoutCost = false;

                var host = H.CreateHost();
                host.Mount(new TreeShape());
                await Harness.Render();

                // Now flip the flag and trigger a render so the host wires
                // up the pipeline + back-fills.
                ReactorFeatureFlags.ShowLayoutCost = true;
                host.Reconciler.GetType(); // touch to keep using
                // Forcing a state-free re-render isn't directly exposed —
                // a state change works the same way for our purposes.
                TreeShape.IncludeC?.Invoke();
                await Harness.Render();

                var snap = Snapshot(host);
                // Note: TreeShape is the *root* Component (mounted via
                // host.Mount(component)). The Reactor reconciler doesn't
                // wrap the root in a ComponentNode — only nested
                // ComponentElements get wrappers and lifecycle events.
                // So back-fill picks up LeafA + LeafB; LeafC was a live
                // mount after the IncludeC state flip.
                H.Check("LayoutCost_BackFill_LeafAAppears", HasComponent(snap, "LeafA"));
                H.Check("LayoutCost_BackFill_LeafBAppears", HasComponent(snap, "LeafB"));
                H.Check("LayoutCost_BackFill_LeafCAppearsAfterMount", HasComponent(snap, "LeafC"));
                H.Check("LayoutCost_BackFill_RootNotTracked",
                    !HasComponent(snap, "TreeShape"));
            }
            finally
            {
                ReactorFeatureFlags.ShowLayoutCost = prev;
            }
        }
    }

    // ── Mount-while-on registers new Components ─────────────────────────

    /// <summary>While <c>ShowLayoutCost</c> is on, mounting a new Component
    /// must register a rollup. This is the path that breaks the user's
    /// "switch tabs and the new tab's Components have no outline" scenario.</summary>
    internal class MountWhileOn_AddsRollup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.ShowLayoutCost;
            try
            {
                ReactorFeatureFlags.ShowLayoutCost = true;

                var host = H.CreateHost();
                host.Mount(new TreeShape());
                await Harness.Render();

                var before = Snapshot(host);
                H.Check("LayoutCost_MountWhileOn_NoLeafCInitially",
                    !HasComponent(before, "LeafC"));
                int beforeCount = CountNonChrome(before);
                // TreeShape is the *root* Component and isn't tracked; the
                // pre-IncludeC tree exposes LeafA + LeafB.
                H.Check("LayoutCost_MountWhileOn_HasInitialTwo",
                    beforeCount == 2);

                TreeShape.IncludeC?.Invoke();
                await Harness.Render();

                var after = Snapshot(host);
                H.Check("LayoutCost_MountWhileOn_LeafCRegistered",
                    HasComponent(after, "LeafC"));
                H.Check("LayoutCost_MountWhileOn_CountIncremented",
                    CountNonChrome(after) == beforeCount + 1);
            }
            finally
            {
                ReactorFeatureFlags.ShowLayoutCost = prev;
            }
        }
    }

    // ── Unmount-while-on removes the rollup ─────────────────────────────

    /// <summary>Unmounting a Component while the flag is on must remove
    /// its rollup so it doesn't stay forever as a dead entry.</summary>
    internal class UnmountWhileOn_DropsRollup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.ShowLayoutCost;
            try
            {
                ReactorFeatureFlags.ShowLayoutCost = true;

                global::System.Action? hide = null;
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (showB, setShowB) = ctx.UseState(true);
                    hide = () => setShowB(false);
                    return VStack(8,
                        Component<LeafA>(),
                        showB ? Component<LeafB>() : (Element?)null
                    );
                });
                await Harness.Render();

                var before = Snapshot(host);
                H.Check("LayoutCost_UnmountWhileOn_LeafBPresent",
                    HasComponent(before, "LeafB"));

                hide?.Invoke();
                await Harness.Render();

                var after = Snapshot(host);
                H.Check("LayoutCost_UnmountWhileOn_LeafBDropped",
                    !HasComponent(after, "LeafB"));
                H.Check("LayoutCost_UnmountWhileOn_LeafAStillPresent",
                    HasComponent(after, "LeafA"));
            }
            finally
            {
                ReactorFeatureFlags.ShowLayoutCost = prev;
            }
        }
    }

    // ── Toggle off → on → mount: the user's reported failure ─────────────

    /// <summary>The user-reported scenario: turn the overlay on, off,
    /// then on; THEN switch tabs (mount new Components). The new
    /// Components must register and appear in the snapshot.</summary>
    internal class ToggleOffOn_NewComponentsStillRegister(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.ShowLayoutCost;
            try
            {
                ReactorFeatureFlags.ShowLayoutCost = true;

                global::System.Action? swapToTreeB = null;
                var host = H.CreateHost();

                // First "tab": just LeafA + LeafB
                // Second "tab": TreeShape (LeafA + LeafB inside, plus LeafC after IncludeC)
                host.Mount(ctx =>
                {
                    var (tab, setTab) = ctx.UseState(0);
                    swapToTreeB = () => setTab(1);
                    return tab switch
                    {
                        0 => VStack(8, Component<LeafA>(), Component<LeafB>()),
                        _ => VStack(8, Component<TreeShape>()),
                    };
                });
                await Harness.Render();

                var initial = Snapshot(host);
                H.Check("LayoutCost_ToggleOffOn_TabAHasComponents",
                    HasComponent(initial, "LeafA") && HasComponent(initial, "LeafB"));

                // Hide → show again. The state we care about: flag flips
                // off mid-session, then back on. Both transitions trigger
                // a render via the menu callback in production; the
                // fixture mirrors that.
                ReactorFeatureFlags.ShowLayoutCost = false;
                await Harness.Render();
                ReactorFeatureFlags.ShowLayoutCost = true;
                await Harness.Render();

                // Now "switch tabs" — mounts a fresh TreeShape Component.
                swapToTreeB?.Invoke();
                await Harness.Render();

                var afterTabSwap = Snapshot(host);
                H.Check("LayoutCost_ToggleOffOn_NewTab_TreeShapeRegistered",
                    HasComponent(afterTabSwap, "TreeShape"));
                H.Check("LayoutCost_ToggleOffOn_NewTab_LeafARegistered",
                    HasComponent(afterTabSwap, "LeafA"));
                H.Check("LayoutCost_ToggleOffOn_NewTab_LeafBRegistered",
                    HasComponent(afterTabSwap, "LeafB"));
                // The previous-tab Components are gone.
                H.Check("LayoutCost_ToggleOffOn_NewTab_OldUnmountsCleared",
                    CountNonChrome(afterTabSwap) <= 4); // TreeShape + LeafA + LeafB (+ optional LeafC)
            }
            finally
            {
                ReactorFeatureFlags.ShowLayoutCost = prev;
            }
        }
    }

    // ── Subtree bounds populate after layout completes ──────────────────

    /// <summary>After a render + layout cycle, every non-chrome rollup
    /// should have non-zero bounds (its content has been laid out).</summary>
    internal class BoundsRefreshFromVisualTree(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.ShowLayoutCost;
            try
            {
                ReactorFeatureFlags.ShowLayoutCost = true;

                var host = H.CreateHost();
                host.Mount(new TreeShape());
                await Harness.Render();
                // Yield a couple more times so layout has settled and our
                // visual-tree walk sees real ActualWidth/Height.
                await Harness.Render();

                var snap = Snapshot(host);
                int withBounds = snap.Count(s => !s.Id.IsChrome && s.SubtreeW > 0 && s.SubtreeH > 0);
                H.Check("LayoutCost_Bounds_AllRollupsHaveBounds",
                    withBounds == CountNonChrome(snap) && withBounds > 0);
            }
            finally
            {
                ReactorFeatureFlags.ShowLayoutCost = prev;
            }
        }
    }
}
