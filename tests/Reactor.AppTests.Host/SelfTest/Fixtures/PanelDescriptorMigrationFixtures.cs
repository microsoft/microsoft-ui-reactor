using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using WinXC = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 047 §14 — panel descriptor migration. These fixtures pin the behaviour
/// that the migration of all six built-in panels (Stack, Grid, Canvas, Flex,
/// WrapGrid, RelativePanel) from the legacy decorator handlers onto the V1
/// descriptor API must preserve or fix:
///
///   • keyed reconcile now preserves WinUI control identity across reorders for
///     EVERY panel (the legacy Panel&lt;&gt; Update arm reconciled by index and
///     lost identity on keyed moves);
///   • per-child attached properties (Grid.Row/Column, Canvas.Left/Top) are
///     re-applied in lockstep AFTER the keyed reconcile, so they follow the
///     moved child rather than sticking to a stale slot;
///   • a reused control drops stale RelativePanel sibling refs and stale Canvas
///     anchor state (the two state-clear bug fixes);
///   • unmounting a panel runs the UseEffect cleanup of Component children
///     (the Unmount → ContinueDefaultTraversal fix).
///
/// They complement the FlexColumn-only coverage in
/// <see cref="KeyedListReconciliationFixtures"/> by exercising the five panels
/// that fixture does not.
/// </summary>
internal static class PanelDescriptorMigrationFixtures
{
    // Shared static cleanup counter for the unmount fixture. Component&lt;T&gt;
    // has no per-instance props API in this fixture context, so a static is the
    // path of least resistance (mirrors ElementFactoryRecyclingFixtures).
    private static int s_cleanupCount;

    private sealed class CleanupChild : Component
    {
        public override Element Render()
        {
            UseEffect(() => () => global::System.Threading.Interlocked.Increment(ref s_cleanupCount));
            return TextBlock("c");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Stack — keyed swap preserves identity (was index-based pre-migration).
    // ────────────────────────────────────────────────────────────────────
    internal class Stack_KeyedSwap_PreservesIdentity(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { "a", "b", "c", "d" }
                    : new[] { "a", "c", "b", "d" }; // swap b and c
                return VStack(
                    Button("Swap", () => setPhase(1)),
                    VStack(items.Select(item =>
                        (Element)Border(TextBlock(item).AutomationId($"pdm_st_{item}"))
                            .WithKey(item)).ToArray())
                );
            });

            await Harness.Render();

            int? Hash(string key) =>
                H.FindControl<WinXC.TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == $"pdm_st_{key}") is { Parent: WinXC.Border br }
                    ? global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(br)
                    : null;

            var keys = new[] { "a", "b", "c", "d" };
            var before = keys.ToDictionary(k => k, Hash);
            H.Check("PDM_Stack_Swap_AllInitial", before.Values.All(v => v is not null));

            H.ClickButton("Swap");
            await Harness.Render();

            H.Check("PDM_Stack_Swap_AllSurvivorsKeepIdentity",
                keys.All(k => before[k] == Hash(k)));
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Grid — keyed swap preserves identity AND Grid.Row follows the moved
    //  child (lockstep attached-prop reapply). Grid is now ALWAYS keyed —
    //  the legacy same-count positional fast path is gone.
    // ────────────────────────────────────────────────────────────────────
    internal class Grid_KeyedSwap_PreservesIdentity_And_RowFollows(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var order = phase == 0
                    ? new[] { "a", "b", "c", "d" }
                    : new[] { "a", "c", "b", "d" }; // swap b and c
                // Row is assigned by POSITION, so after the swap the moved
                // children must pick up their new row.
                var rows = new[]
                {
                    GridSize.Star(), GridSize.Star(), GridSize.Star(), GridSize.Star()
                };
                return VStack(
                    Button("Swap", () => setPhase(1)),
                    Grid(new[] { GridSize.Star() }, rows,
                        order.Select((item, idx) =>
                            (Element)Border(TextBlock(item).AutomationId($"pdm_gr_{item}"))
                                .Grid(row: idx)
                                .WithKey(item)).ToArray())
                );
            });

            await Harness.Render();

            WinXC.Border? Ctrl(string key) =>
                H.FindControl<WinXC.TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == $"pdm_gr_{key}") is { Parent: WinXC.Border br }
                    ? br
                    : null;

            var keys = new[] { "a", "b", "c", "d" };
            var beforeCtrl = keys.ToDictionary(k => k, k => Ctrl(k));
            H.Check("PDM_Grid_Swap_AllInitial", beforeCtrl.Values.All(v => v is not null));
            H.Check("PDM_Grid_Swap_InitialRows",
                beforeCtrl["a"] is { } ca && WinXC.Grid.GetRow(ca) == 0 &&
                beforeCtrl["b"] is { } cb && WinXC.Grid.GetRow(cb) == 1 &&
                beforeCtrl["c"] is { } cc && WinXC.Grid.GetRow(cc) == 2 &&
                beforeCtrl["d"] is { } cd && WinXC.Grid.GetRow(cd) == 3);

            H.ClickButton("Swap");
            await Harness.Render();

            // Identity preserved across the keyed swap…
            H.Check("PDM_Grid_Swap_IdentityPreserved",
                keys.All(k => ReferenceEquals(beforeCtrl[k], Ctrl(k))));

            // …and Grid.Row followed the moved children to their new position.
            H.Check("PDM_Grid_Swap_RowFollowsMovedChild",
                Ctrl("a") is { } a2 && WinXC.Grid.GetRow(a2) == 0 &&
                Ctrl("c") is { } c2 && WinXC.Grid.GetRow(c2) == 1 &&
                Ctrl("b") is { } b2 && WinXC.Grid.GetRow(b2) == 2 &&
                Ctrl("d") is { } d2 && WinXC.Grid.GetRow(d2) == 3);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Canvas — keyed reorder preserves identity AND Canvas.Left follows the
    //  moved child (lockstep attached-prop reapply).
    // ────────────────────────────────────────────────────────────────────
    internal class Canvas_KeyedReorder_PreservesIdentity_And_PositionFollows(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var order = phase == 0
                    ? new[] { "a", "b", "c" }
                    : new[] { "c", "a", "b" }; // rotate
                return VStack(
                    Button("Rotate", () => setPhase(1)),
                    Canvas(order.Select((item, idx) =>
                        (Element)Border(TextBlock(item).AutomationId($"pdm_cv_{item}"))
                            .Canvas(left: idx * 100.0, top: 0)
                            .WithKey(item)).ToArray())
                );
            });

            await Harness.Render();

            WinXC.Border? Ctrl(string key) =>
                H.FindControl<WinXC.TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == $"pdm_cv_{key}") is { Parent: WinXC.Border br }
                    ? br
                    : null;

            var keys = new[] { "a", "b", "c" };
            var beforeCtrl = keys.ToDictionary(k => k, k => Ctrl(k));
            H.Check("PDM_Canvas_Reorder_AllInitial", beforeCtrl.Values.All(v => v is not null));
            H.Check("PDM_Canvas_Reorder_InitialPositions",
                beforeCtrl["a"] is { } ca && Math.Abs(WinXC.Canvas.GetLeft(ca) - 0) < 0.001 &&
                beforeCtrl["b"] is { } cb && Math.Abs(WinXC.Canvas.GetLeft(cb) - 100) < 0.001 &&
                beforeCtrl["c"] is { } cc && Math.Abs(WinXC.Canvas.GetLeft(cc) - 200) < 0.001);

            H.ClickButton("Rotate");
            await Harness.Render();

            H.Check("PDM_Canvas_Reorder_IdentityPreserved",
                keys.All(k => ReferenceEquals(beforeCtrl[k], Ctrl(k))));

            // New order is c,a,b → positions 0,100,200 respectively.
            H.Check("PDM_Canvas_Reorder_PositionFollowsMovedChild",
                Ctrl("c") is { } c2 && Math.Abs(WinXC.Canvas.GetLeft(c2) - 0) < 0.001 &&
                Ctrl("a") is { } a2 && Math.Abs(WinXC.Canvas.GetLeft(a2) - 100) < 0.001 &&
                Ctrl("b") is { } b2 && Math.Abs(WinXC.Canvas.GetLeft(b2) - 200) < 0.001);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Canvas — anchor state is cleared when a reused control loses its
    //  CanvasAttached. Without ClearCanvasPosition the retained anchor would
    //  be re-applied on the next layout pass.
    // ────────────────────────────────────────────────────────────────────
    internal class Canvas_AnchorClearedOnReuse(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                // Phase 0: anchored (centered on 300,200). Phase 1: same keyed
                // control but NO Canvas modifier at all → ca is null branch.
                Element child = phase == 0
                    ? Border(TextBlock("x").AutomationId("pdm_anchor")).CenterAt(300, 200).WithKey("x")
                    : Border(TextBlock("x").AutomationId("pdm_anchor")).WithKey("x");
                return VStack(
                    Button("Drop", () => setPhase(1)),
                    Canvas(child)
                );
            });

            await Harness.Render();

            WinXC.Border? Ctrl() =>
                H.FindControl<WinXC.TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "pdm_anchor") is { Parent: WinXC.Border br }
                    ? br
                    : null;

            var before = Ctrl();
            H.Check("PDM_CanvasAnchor_Initial", before is not null);
            // Anchored position was applied (non-zero Left). The exact value is
            // 300 - 0.5*ActualWidth; we only assert it was positioned so the
            // subsequent clear is meaningful.
            double leftBefore = before is null ? double.NaN : WinXC.Canvas.GetLeft(before);
            H.Check("PDM_CanvasAnchor_InitiallyPositioned", leftBefore > 0);

            H.ClickButton("Drop");
            await Harness.Render();

            var after = Ctrl();
            H.Check("PDM_CanvasAnchor_ControlReused", ReferenceEquals(before, after));
            // ClearCanvasPosition cleared Left/Top back to the DP default (0),
            // and reset the retained anchor state so the next layout pass is a
            // no-op rather than re-applying the stale centered position.
            H.Check("PDM_CanvasAnchor_ClearedToZero",
                after is not null &&
                Math.Abs(WinXC.Canvas.GetLeft(after)) < 0.001 &&
                Math.Abs(WinXC.Canvas.GetTop(after)) < 0.001);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  WrapGrid — keyed reverse preserves identity for every survivor.
    // ────────────────────────────────────────────────────────────────────
    internal class WrapGrid_KeyedReverse_PreservesIdentity(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { "a", "b", "c", "d", "e" }
                    : new[] { "e", "d", "c", "b", "a" };
                return VStack(
                    Button("Reverse", () => setPhase(1)),
                    WrapGrid(items.Select(item =>
                        (Element)Border(TextBlock(item).AutomationId($"pdm_wg_{item}"))
                            .WithKey(item)).ToArray())
                );
            });

            await Harness.Render();

            int? Hash(string key) =>
                H.FindControl<WinXC.TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == $"pdm_wg_{key}") is { Parent: WinXC.Border br }
                    ? global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(br)
                    : null;

            var keys = new[] { "a", "b", "c", "d", "e" };
            var before = keys.ToDictionary(k => k, Hash);
            H.Check("PDM_WrapGrid_Reverse_AllInitial", before.Values.All(v => v is not null));

            H.ClickButton("Reverse");
            await Harness.Render();

            H.Check("PDM_WrapGrid_Reverse_AllSurvivorsKeepIdentity",
                keys.All(k => before[k] == Hash(k)));
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  RelativePanel — keyed reorder preserves identity AND a reused control
    //  drops a stale sibling ref (Phase 1b stale-clear fix).
    // ────────────────────────────────────────────────────────────────────
    internal class RelativePanel_KeyedReorder_PreservesIdentity_And_StaleClear(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                // Phase 0: B is positioned rightOf A. Phase 1: keep both keyed
                // controls but drop B's rightOf entirely → the descriptor must
                // clear the stale RightOf on the reused control.
                Element a = Border(TextBlock("a").AutomationId("pdm_rp_a"))
                    .RelativePanel(name: "A").WithKey("a");
                Element b = phase == 0
                    ? Border(TextBlock("b").AutomationId("pdm_rp_b"))
                        .RelativePanel(name: "B", rightOf: "A").WithKey("b")
                    : Border(TextBlock("b").AutomationId("pdm_rp_b"))
                        .RelativePanel(name: "B").WithKey("b");
                return VStack(
                    Button("Drop", () => setPhase(1)),
                    RelativePanel(a, b)
                );
            });

            await Harness.Render();

            WinXC.Border? Ctrl(string key) =>
                H.FindControl<WinXC.TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == $"pdm_rp_{key}") is { Parent: WinXC.Border br }
                    ? br
                    : null;

            var beforeA = Ctrl("a");
            var beforeB = Ctrl("b");
            H.Check("PDM_RelPanel_Initial", beforeA is not null && beforeB is not null);
            H.Check("PDM_RelPanel_InitialRightOfSet",
                beforeB is not null && WinXC.RelativePanel.GetRightOf(beforeB) is not null);

            H.ClickButton("Drop");
            await Harness.Render();

            H.Check("PDM_RelPanel_IdentityPreserved",
                ReferenceEquals(beforeA, Ctrl("a")) && ReferenceEquals(beforeB, Ctrl("b")));
            H.Check("PDM_RelPanel_StaleRightOfCleared",
                Ctrl("b") is { } b2 && WinXC.RelativePanel.GetRightOf(b2) is null);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Unmount — a Component child's UseEffect cleanup runs when the panel is
    //  removed from the render tree, for every panel type (Phase 0 fix:
    //  Unmount returns ContinueDefaultTraversal for Panel<> strategies).
    // ────────────────────────────────────────────────────────────────────
    internal class Panel_Unmount_RunsChildEffectCleanup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await TestPanel("VStack", () => VStack(Component<CleanupChild>()));
            await TestPanel("Grid", () => Grid(new[] { GridSize.Star() }, new[] { GridSize.Star() }, Component<CleanupChild>()));
            await TestPanel("Canvas", () => Canvas(Component<CleanupChild>()));
            await TestPanel("FlexColumn", () => FlexColumn(Component<CleanupChild>()));
            await TestPanel("WrapGrid", () => WrapGrid(Component<CleanupChild>()));
            await TestPanel("RelativePanel", () => RelativePanel(Component<CleanupChild>()));
        }

        private async Task TestPanel(string label, Func<Element> panel)
        {
            s_cleanupCount = 0;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                return VStack(
                    Button("Toggle", () => setShow(!show)),
                    show ? panel() : (Element)TextBlock("(hidden)")
                );
            });
            await Harness.Render();

            H.Check($"PDM_Unmount_{label}_NoCleanupBeforeUnmount", s_cleanupCount == 0);

            H.ClickButton("Toggle");
            await Harness.Render();

            H.Check($"PDM_Unmount_{label}_CleanupRan", s_cleanupCount == 1);
        }
    }
}
