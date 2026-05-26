using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// ════════════════════════════════════════════════════════════════════════
//  Deterministic splitter matrix — DockSplitterControl + FlexPanel.
//
//  Goal: lock the splitter's pointer-drive math against every shape we
//  have hit (or are likely to hit) without going through DockHostNativeComponent,
//  WinUI's drag-drop pipeline, or any host-level overlays. Each case
//  builds a fresh FlexPanel + N panes interleaved with splitters, drives
//  the splitter's internal BeginSimulatedDrag / ContinueSimulatedDrag /
//  EndSimulatedDrag API directly, and asserts the leading pane's
//  ActualWidth (or ActualHeight) follows the cursor 1:1.
//
//  Categories (40 cases total):
//   A — basic cursor follow (2/3/4 panes, H+V, both directions)
//   B — min-clamp at 60 DIP
//   C — continuous drag invariants (round-trip, additivity, layout shift)
//   D — mixed initial grow weights
//   E — layout-shift mid-drag self-correction
//   F — splitter independence in 4-pane layouts
//   G — keyboard arrow steps
//   H — RTL flow direction
//   I — HiDPI / fractional coords
//   J — capture-loss / abort
// ════════════════════════════════════════════════════════════════════════

internal static class SplitterMatrixFixtures
{
    // ── Rig ─────────────────────────────────────────────────────────────
    //
    //  Builds a FlexPanel of N panes interleaved with (N-1) splitters.
    //  Each pane is a Border with min-size 60 to match the splitter's
    //  clamp. The rig exposes:
    //    .Panes[i]      → FrameworkElement (Border) at pane index i
    //    .Splitters[s]  → DockSplitterControl at splitter index s
    //                     (s = i-1 from the FlexPanel children list)
    //    .Width / Height of pane i along the split axis
    //
    //  After construction, callers must SetContent + Render twice so
    //  ActualWidth / ActualHeight populate.
    private sealed class Rig
    {
        public FlexPanel Panel { get; }
        public IReadOnlyList<Border> Panes { get; }
        public IReadOnlyList<DockSplitterControl> Splitters { get; }
        public bool IsHorizontal { get; }

        public Rig(int paneCount, FlexDirection direction,
            double panelW, double panelH, double[]? grows)
        {
            IsHorizontal = direction == FlexDirection.Row;
            var panes = new List<Border>(paneCount);
            var splitters = new List<DockSplitterControl>(paneCount - 1);

            Panel = new FlexPanel
            {
                Direction = direction,
                AlignItems = FlexAlign.Stretch,
                Wrap = FlexWrap.NoWrap,
                Width = panelW,
                Height = panelH,
            };

            for (int i = 0; i < paneCount; i++)
            {
                var pane = new Border
                {
                    MinWidth = 60,
                    MinHeight = 60,
                };
                var grow = grows is { } g && i < g.Length ? g[i] : 1.0;
                FlexPanel.SetGrow(pane, grow);
                FlexPanel.SetShrink(pane, 1);
                FlexPanel.SetBasis(pane, 0);
                panes.Add(pane);
                Panel.Children.Add(pane);

                if (i < paneCount - 1)
                {
                    var splitter = new DockSplitterControl
                    {
                        Direction = IsHorizontal
                            ? DockSplitterDirection.Columns
                            : DockSplitterDirection.Rows,
                    };
                    FlexPanel.SetGrow(splitter, 0);
                    FlexPanel.SetShrink(splitter, 0);
                    splitters.Add(splitter);
                    Panel.Children.Add(splitter);
                }
            }

            Panes = panes;
            Splitters = splitters;
        }

        public double PaneExtent(int paneIndex) =>
            IsHorizontal ? Panes[paneIndex].ActualWidth : Panes[paneIndex].ActualHeight;

        public double PaneGrow(int paneIndex) => FlexPanel.GetGrow(Panes[paneIndex]);

        public Point Origin => new(0, 0);

        // Translate a 1-D cursor delta along the split axis into a parent-
        // relative Point. The off-axis coordinate is irrelevant to the
        // splitter math, so it stays at 0.
        public Point Delta(double d) => IsHorizontal ? new Point(d, 0) : new Point(0, d);
    }

    // Mount the rig into the harness, render twice (so ActualWidth lands),
    // then return. Always returns with the rig live in the content area.
    private static async Task MountAsync(Harness h, Rig rig)
    {
        h.SetContent(rig.Panel);
        await Harness.Render();
        await Harness.Render();
    }

    private static async Task TeardownAsync(Harness h)
    {
        h.SetContent(null);
        await Harness.Render();
    }

    // Common assertion: pane[i].ActualExtent ≈ expected within a DIP
    // tolerance. WinUI rounds ActualWidth/Height to fractional values
    // dependent on display scale; 0.6 DIP is below visible drift on
    // every test display we care about.
    private static bool ExtentNear(double actual, double expected, double tol = 0.6)
        => Math.Abs(actual - expected) <= tol;

    // ════════════════════════════════════════════════════════════════════
    //  A — Basic cursor follow
    // ════════════════════════════════════════════════════════════════════

    internal class A01_TwoPaneH_DragForward_LeadingTracksCursor(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 600, 200, grows: [1, 1]);
            await MountAsync(H, rig);

            var captured = rig.PaneExtent(0);
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(50));
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(50));
            await Harness.Render();

            H.Check("A01_LeadingGrewBy50",
                ExtentNear(rig.PaneExtent(0), captured + 50));
            H.Check("A01_TrailingShrunkBy50",
                ExtentNear(rig.PaneExtent(1), 600 - DockSplitterControl.HitThicknessDip - (captured + 50)));
            await TeardownAsync(H);
        }
    }

    internal class A02_TwoPaneH_DragBackward_LeadingTracksCursor(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 600, 200, grows: [1, 1]);
            await MountAsync(H, rig);

            var captured = rig.PaneExtent(0);
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(-80));
            await Harness.Render();

            H.Check("A02_LeadingShrunkBy80",
                ExtentNear(rig.PaneExtent(0), captured - 80));
            await TeardownAsync(H);
        }
    }

    internal class A03_TwoPaneV_DragDown_TopTracksCursor(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Column, 200, 600, grows: [1, 1]);
            await MountAsync(H, rig);

            var captured = rig.PaneExtent(0);
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(70));
            await Harness.Render();

            H.Check("A03_TopGrewBy70",
                ExtentNear(rig.PaneExtent(0), captured + 70));
            await TeardownAsync(H);
        }
    }

    internal class A04_TwoPaneV_DragUp_TopTracksCursor(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Column, 200, 600, grows: [1, 1]);
            await MountAsync(H, rig);

            var captured = rig.PaneExtent(0);
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(-70));
            await Harness.Render();

            H.Check("A04_TopShrunkBy70",
                ExtentNear(rig.PaneExtent(0), captured - 70));
            await TeardownAsync(H);
        }
    }

    // 3-pane equal share — the Pix bug repro. Drag splitter 0 by +75; pane 0
    // must grow by 75, pane 1 must shrink by 75, pane 2 must stay put.
    internal class A05_ThreePaneH_DragSplitter0_ThirdPaneUntouched(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(3, FlexDirection.Row, 900, 200, grows: [1, 1, 1]);
            await MountAsync(H, rig);

            var c0 = rig.PaneExtent(0);
            var c1 = rig.PaneExtent(1);
            var c2 = rig.PaneExtent(2);
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(75));
            await Harness.Render();

            H.Check("A05_C0GrewBy75", ExtentNear(rig.PaneExtent(0), c0 + 75));
            H.Check("A05_C1ShrunkBy75", ExtentNear(rig.PaneExtent(1), c1 - 75));
            H.Check("A05_C2Untouched", ExtentNear(rig.PaneExtent(2), c2));
            await TeardownAsync(H);
        }
    }

    internal class A06_ThreePaneH_DragSplitter1_FirstPaneUntouched(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(3, FlexDirection.Row, 900, 200, grows: [1, 1, 1]);
            await MountAsync(H, rig);

            var c0 = rig.PaneExtent(0);
            var c1 = rig.PaneExtent(1);
            var c2 = rig.PaneExtent(2);
            rig.Splitters[1].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[1].EndSimulatedDrag(rig.Delta(60));
            await Harness.Render();

            H.Check("A06_C1GrewBy60", ExtentNear(rig.PaneExtent(1), c1 + 60));
            H.Check("A06_C2ShrunkBy60", ExtentNear(rig.PaneExtent(2), c2 - 60));
            H.Check("A06_C0Untouched", ExtentNear(rig.PaneExtent(0), c0));
            await TeardownAsync(H);
        }
    }

    internal class A07_ThreePaneV_DragSplitter0_BottomUntouched(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(3, FlexDirection.Column, 200, 900, grows: [1, 1, 1]);
            await MountAsync(H, rig);

            var top = rig.PaneExtent(0);
            var mid = rig.PaneExtent(1);
            var bot = rig.PaneExtent(2);
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(-30));
            await Harness.Render();

            H.Check("A07_TopShrunkBy30", ExtentNear(rig.PaneExtent(0), top - 30));
            H.Check("A07_MidGrewBy30", ExtentNear(rig.PaneExtent(1), mid + 30));
            H.Check("A07_BotUntouched", ExtentNear(rig.PaneExtent(2), bot));
            await TeardownAsync(H);
        }
    }

    internal class A08_FourPaneH_DragMiddleSplitter_OuterPanesUntouched(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(4, FlexDirection.Row, 1200, 200, grows: [1, 1, 1, 1]);
            await MountAsync(H, rig);

            var c0 = rig.PaneExtent(0);
            var c3 = rig.PaneExtent(3);
            var c1 = rig.PaneExtent(1);
            var c2 = rig.PaneExtent(2);
            rig.Splitters[1].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[1].EndSimulatedDrag(rig.Delta(40));
            await Harness.Render();

            H.Check("A08_C1GrewBy40", ExtentNear(rig.PaneExtent(1), c1 + 40));
            H.Check("A08_C2ShrunkBy40", ExtentNear(rig.PaneExtent(2), c2 - 40));
            H.Check("A08_C0Untouched", ExtentNear(rig.PaneExtent(0), c0));
            H.Check("A08_C3Untouched", ExtentNear(rig.PaneExtent(3), c3));
            await TeardownAsync(H);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  B — Min-clamp at 60 DIP
    // ════════════════════════════════════════════════════════════════════

    internal class B01_TwoPaneH_DragPastLeadingMin_ClampsAt60(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 600, 200, grows: [1, 1]);
            await MountAsync(H, rig);

            // Drag far enough that leading would go negative without clamp.
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(-500));
            await Harness.Render();

            H.Check("B01_LeadingClampedAt60",
                ExtentNear(rig.PaneExtent(0), 60, tol: 1.0));
            await TeardownAsync(H);
        }
    }

    internal class B02_TwoPaneH_DragPastTrailingMin_ClampsAtPanelMinus60(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 600, 200, grows: [1, 1]);
            await MountAsync(H, rig);

            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(500));
            await Harness.Render();

            // Trailing min = 60 DIP, so leading max = panel - splitter - 60.
            var expectedMax = 600 - DockSplitterControl.HitThicknessDip - 60;
            H.Check("B02_LeadingAtMaxClamp",
                ExtentNear(rig.PaneExtent(0), expectedMax, tol: 1.5));
            H.Check("B02_TrailingClampedAt60",
                ExtentNear(rig.PaneExtent(1), 60, tol: 1.5));
            await TeardownAsync(H);
        }
    }

    internal class B03_ThreePaneH_ShrinkLeadingBelow60_Clamps(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(3, FlexDirection.Row, 900, 200, grows: [1, 1, 1]);
            await MountAsync(H, rig);

            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(-500));
            await Harness.Render();

            H.Check("B03_LeadingClamped",
                rig.PaneExtent(0) >= 59.5 && rig.PaneExtent(0) <= 61.0);
            await TeardownAsync(H);
        }
    }

    internal class B04_ThreePaneH_ShrinkMiddleBelow60_Clamps(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(3, FlexDirection.Row, 900, 200, grows: [1, 1, 1]);
            await MountAsync(H, rig);

            // Splitter 0 grows leading until middle would clamp at 60.
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(500));
            await Harness.Render();

            H.Check("B04_MiddleClamped",
                rig.PaneExtent(1) >= 59.5 && rig.PaneExtent(1) <= 61.0);
            await TeardownAsync(H);
        }
    }

    internal class B05_TwoPaneV_DragPastTopMin_ClampsAt60(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Column, 200, 600, grows: [1, 1]);
            await MountAsync(H, rig);

            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(-500));
            await Harness.Render();

            H.Check("B05_TopClampedAt60",
                ExtentNear(rig.PaneExtent(0), 60, tol: 1.0));
            await TeardownAsync(H);
        }
    }

    internal class B06_TwoPaneV_DragPastBottomMin_ClampsAt60(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Column, 200, 600, grows: [1, 1]);
            await MountAsync(H, rig);

            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(500));
            await Harness.Render();

            H.Check("B06_BottomClampedAt60",
                ExtentNear(rig.PaneExtent(1), 60, tol: 1.5));
            await TeardownAsync(H);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  C — Continuous drag invariants
    // ════════════════════════════════════════════════════════════════════

    internal class C01_DragForwardThenBack_ReturnsToCapture(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(3, FlexDirection.Row, 900, 200, grows: [1, 1, 1]);
            await MountAsync(H, rig);

            var c0 = rig.PaneExtent(0);
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(60));
            rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(0));
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(0));
            await Harness.Render();

            H.Check("C01_RoundTripBack", ExtentNear(rig.PaneExtent(0), c0));
            await TeardownAsync(H);
        }
    }

    internal class C02_ManySmallContinues_EqualsOneBigContinue(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rigA = new Rig(3, FlexDirection.Row, 900, 200, grows: [1, 1, 1]);
            await MountAsync(H, rigA);
            var c0A = rigA.PaneExtent(0);
            rigA.Splitters[0].BeginSimulatedDrag(rigA.Origin);
            for (int step = 5; step <= 100; step += 5)
                rigA.Splitters[0].ContinueSimulatedDrag(rigA.Delta(step));
            rigA.Splitters[0].EndSimulatedDrag(rigA.Delta(100));
            await Harness.Render();
            var manyA = rigA.PaneExtent(0);
            await TeardownAsync(H);

            var rigB = new Rig(3, FlexDirection.Row, 900, 200, grows: [1, 1, 1]);
            await MountAsync(H, rigB);
            var c0B = rigB.PaneExtent(0);
            rigB.Splitters[0].BeginSimulatedDrag(rigB.Origin);
            rigB.Splitters[0].EndSimulatedDrag(rigB.Delta(100));
            await Harness.Render();
            var oneB = rigB.PaneExtent(0);
            await TeardownAsync(H);

            // Both paths should converge to the same final width. Same captured
            // start and same total delta — order of intermediate events must
            // not affect the outcome because each Continue computes against
            // the captured grow + live panel extent.
            H.Check("C02_StartedEqual", ExtentNear(c0A, c0B));
            H.Check("C02_FinalEqual_ManyVsOne", ExtentNear(manyA, oneB));
        }
    }

    internal class C03_ContinueAfterPanelWidthChanges_StillTracksCursor(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(3, FlexDirection.Row, 900, 200, grows: [1, 1, 1]);
            await MountAsync(H, rig);

            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(50));
            await Harness.Render();
            var afterFirstC0 = rig.PaneExtent(0);

            // Shrink the panel mid-drag. The next Continue must use the
            // live panel extent (not a stale snapshot) so the cursor
            // still ends up at the same parent-relative target.
            rig.Panel.Width = 750;
            await Harness.Render();
            await Harness.Render();

            // From the new (smaller) panel, drag back to the origin —
            // the leading pane should return to roughly its grow-share
            // of the new panel (1/3 of 750 - 16 ≈ 244), regardless of
            // what happened during the first half of the drag.
            rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(0));
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(0));
            await Harness.Render();

            var expectedShare = (750 - 2 * DockSplitterControl.HitThicknessDip) / 3.0;
            H.Check("C03_FirstContinueGrewLeading",
                afterFirstC0 > rig.PaneExtent(0));
            H.Check("C03_PostResizeBackToShare",
                ExtentNear(rig.PaneExtent(0), expectedShare, tol: 2.0));
            await TeardownAsync(H);
        }
    }

    internal class C04_EndFiresExactlyOneFinalResizeDelta(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 600, 200, grows: [1, 1]);
            await MountAsync(H, rig);

            int events = 0;
            DockSplitterDeltaEventArgs? last = null;
            rig.Splitters[0].ResizeDelta += (_, e) => { events++; last = e; };

            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(10));
            rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(20));
            rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(30));
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(40));
            await Harness.Render();

            H.Check("C04_OneEventFromOneDrag", events == 1);
            H.Check("C04_EventIsFinal", last is { IsFinal: true });
            H.Check("C04_EventDeltaNegated",
                last is { } a && Math.Abs(a.Delta - (-40)) < 0.001);
            await TeardownAsync(H);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  D — Mixed initial grow weights
    // ════════════════════════════════════════════════════════════════════

    internal class D01_ThreePaneH_Grow121_Splitter0DragPreservesThird(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(3, FlexDirection.Row, 800, 200, grows: [1, 2, 1]);
            await MountAsync(H, rig);

            var c2 = rig.PaneExtent(2);
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(40));
            await Harness.Render();

            H.Check("D01_ThirdPaneUntouched", ExtentNear(rig.PaneExtent(2), c2));
            await TeardownAsync(H);
        }
    }

    internal class D02_ThreePaneH_Grow311_DragInCorrectDIPSpace(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(3, FlexDirection.Row, 800, 200, grows: [3, 1, 1]);
            await MountAsync(H, rig);

            var c0 = rig.PaneExtent(0);
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(-50));
            await Harness.Render();

            H.Check("D02_LeadingShrunkBy50", ExtentNear(rig.PaneExtent(0), c0 - 50));
            await TeardownAsync(H);
        }
    }

    internal class D03_TwoPaneH_Grow13_DragConverges(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 800, 200, grows: [1, 3]);
            await MountAsync(H, rig);

            var c0 = rig.PaneExtent(0);
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(80));
            await Harness.Render();

            H.Check("D03_LeadingGrewBy80", ExtentNear(rig.PaneExtent(0), c0 + 80));
            await TeardownAsync(H);
        }
    }

    internal class D04_FourPaneH_Grow1122_DragPreservesUntouchedPanes(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(4, FlexDirection.Row, 1200, 200, grows: [1, 1, 2, 2]);
            await MountAsync(H, rig);

            var c0 = rig.PaneExtent(0);
            var c3 = rig.PaneExtent(3);
            rig.Splitters[1].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[1].EndSimulatedDrag(rig.Delta(50));
            await Harness.Render();

            H.Check("D04_C0Untouched", ExtentNear(rig.PaneExtent(0), c0));
            H.Check("D04_C3Untouched", ExtentNear(rig.PaneExtent(3), c3));
            await TeardownAsync(H);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  E — Layout shift mid-drag
    // ════════════════════════════════════════════════════════════════════

    internal class E01_SetGrowOnSiblingMidDrag_CursorStillFollows(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(3, FlexDirection.Row, 900, 200, grows: [1, 1, 1]);
            await MountAsync(H, rig);

            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(30));
            await Harness.Render();
            var afterFirstC0 = rig.PaneExtent(0);

            // Mutate the third pane's grow mid-drag (e.g., app code added
            // a new sibling weight). The drag uses the LIVE total panel
            // grow on every subsequent ContinueSimulatedDrag.
            FlexPanel.SetGrow(rig.Panes[2], 2.0);
            await Harness.Render();
            await Harness.Render();

            // Drag forward more — cumulative cursor delta now +60. The
            // exact landing pane[0] depends on captured grow + live
            // totalPanelGrow + live panelExtent (interpretation: grow-
            // locked, not pixel-locked); what we ASSERT is the cursor-
            // follow contract: increasing positive cumDelta must
            // monotonically grow the leading pane's grow weight, and
            // the math must not crash on stale sibling state.
            rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(60));
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(60));
            await Harness.Render();

            H.Check("E01_LeadingGrowIncreased",
                rig.PaneGrow(0) > 1.0);
            H.Check("E01_NoNaN",
                !double.IsNaN(rig.PaneExtent(0)) && !double.IsInfinity(rig.PaneExtent(0)));
            H.Check("E01_LeadingWithinPanelBounds",
                rig.PaneExtent(0) > 60 && rig.PaneExtent(0) < rig.Panel.ActualWidth);
            await TeardownAsync(H);
        }
    }

    internal class E02_PanelWidthShiftMidDrag_ConvergesToCursor(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 800, 200, grows: [1, 1]);
            await MountAsync(H, rig);

            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(100));
            await Harness.Render();

            rig.Panel.Width = 1000; // wider panel mid-drag
            await Harness.Render();
            await Harness.Render();

            // After the resize the cursor delta is still 100 (cumulative).
            // End the drag at +120 — leading width must reflect the new
            // panel's allocation + the +120 delta from capture.
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(120));
            await Harness.Render();

            // The grow values are constrained to [minGrow, pairGrow-minGrow]
            // — for a 2-pane equal split that's [grow_for_60_DIP, ...].
            // We mostly want "no crash, no NaN, no negative widths".
            H.Check("E02_NoCrash_LeadingPositive", rig.PaneExtent(0) > 60);
            H.Check("E02_NoCrash_TrailingPositive", rig.PaneExtent(1) > 60);
            await TeardownAsync(H);
        }
    }

    internal class E03_StackedMutationsBetweenContinues_NoNaN(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(3, FlexDirection.Row, 900, 200, grows: [1, 1, 1]);
            await MountAsync(H, rig);

            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            for (int i = 1; i <= 10; i++)
            {
                rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(i * 5));
                // Wobble third pane's grow each step.
                FlexPanel.SetGrow(rig.Panes[2], 1.0 + (i % 3) * 0.25);
                await Harness.Render();
            }
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(50));
            await Harness.Render();

            H.Check("E03_NoNaN_C0",
                !double.IsNaN(rig.PaneExtent(0)) && !double.IsInfinity(rig.PaneExtent(0)));
            H.Check("E03_NoNaN_C1",
                !double.IsNaN(rig.PaneExtent(1)) && !double.IsInfinity(rig.PaneExtent(1)));
            H.Check("E03_NoNaN_C2",
                !double.IsNaN(rig.PaneExtent(2)) && !double.IsInfinity(rig.PaneExtent(2)));
            await TeardownAsync(H);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  F — Splitter independence in 4-pane layouts
    // ════════════════════════════════════════════════════════════════════

    internal class F01_FourPaneH_DragS0_DoesNotMoveS1Position(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(4, FlexDirection.Row, 1200, 200, grows: [1, 1, 1, 1]);
            await MountAsync(H, rig);

            // Position of splitter 1 = c0 + s0_w + c1 + s1_w start. We track
            // the COMBINED width of c2 + c3 as a proxy for "splitter 1 didn't
            // slide relative to the panel right edge".
            var trailingPair = rig.PaneExtent(2) + rig.PaneExtent(3);
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(50));
            await Harness.Render();

            H.Check("F01_TrailingPairUnchanged",
                ExtentNear(rig.PaneExtent(2) + rig.PaneExtent(3), trailingPair));
            await TeardownAsync(H);
        }
    }

    internal class F02_FourPaneH_DragS2_DoesNotMoveS0Position(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(4, FlexDirection.Row, 1200, 200, grows: [1, 1, 1, 1]);
            await MountAsync(H, rig);

            var leadingPair = rig.PaneExtent(0) + rig.PaneExtent(1);
            rig.Splitters[2].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[2].EndSimulatedDrag(rig.Delta(-40));
            await Harness.Render();

            H.Check("F02_LeadingPairUnchanged",
                ExtentNear(rig.PaneExtent(0) + rig.PaneExtent(1), leadingPair));
            await TeardownAsync(H);
        }
    }

    internal class F03_FourPaneH_DragS0ThenS2_BothLand(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(4, FlexDirection.Row, 1200, 200, grows: [1, 1, 1, 1]);
            await MountAsync(H, rig);

            var c0 = rig.PaneExtent(0);
            var c3 = rig.PaneExtent(3);

            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(50));
            await Harness.Render();

            rig.Splitters[2].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[2].EndSimulatedDrag(rig.Delta(-30));
            await Harness.Render();

            H.Check("F03_S0Landed_C0Grew", ExtentNear(rig.PaneExtent(0), c0 + 50));
            H.Check("F03_S2Landed_C3Grew", ExtentNear(rig.PaneExtent(3), c3 + 30));
            await TeardownAsync(H);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  G — Keyboard arrow steps
    // ════════════════════════════════════════════════════════════════════

    internal class G01_ColumnsForward_LeadingGrowsByKeyboardStep(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 600, 200, grows: [1, 1]);
            await MountAsync(H, rig);

            var c0 = rig.PaneExtent(0);
            var step = DockSplitterControl.DefaultKeyboardStepDip;
            rig.Splitters[0].SimulateKeyboardStep(forward: true);
            await Harness.Render();

            H.Check("G01_LeadingGrewByStep",
                ExtentNear(rig.PaneExtent(0), c0 + step));
            await TeardownAsync(H);
        }
    }

    internal class G02_ColumnsBackward_LeadingShrinksByKeyboardStep(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 600, 200, grows: [1, 1]);
            await MountAsync(H, rig);

            var c0 = rig.PaneExtent(0);
            var step = DockSplitterControl.DefaultKeyboardStepDip;
            rig.Splitters[0].SimulateKeyboardStep(forward: false);
            await Harness.Render();

            H.Check("G02_LeadingShrunkByStep",
                ExtentNear(rig.PaneExtent(0), c0 - step));
            await TeardownAsync(H);
        }
    }

    internal class G03_RowsForward_TopGrowsByKeyboardStep(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Column, 200, 600, grows: [1, 1]);
            await MountAsync(H, rig);

            var top = rig.PaneExtent(0);
            var step = DockSplitterControl.DefaultKeyboardStepDip;
            rig.Splitters[0].SimulateKeyboardStep(forward: true);
            await Harness.Render();

            H.Check("G03_TopGrewByStep",
                ExtentNear(rig.PaneExtent(0), top + step));
            await TeardownAsync(H);
        }
    }

    internal class G04_RowsBackward_TopShrinksByKeyboardStep(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Column, 200, 600, grows: [1, 1]);
            await MountAsync(H, rig);

            var top = rig.PaneExtent(0);
            var step = DockSplitterControl.DefaultKeyboardStepDip;
            rig.Splitters[0].SimulateKeyboardStep(forward: false);
            await Harness.Render();

            H.Check("G04_TopShrunkByStep",
                ExtentNear(rig.PaneExtent(0), top - step));
            await TeardownAsync(H);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  H — RTL flow direction
    // ════════════════════════════════════════════════════════════════════

    internal class H01_RtlColumns_DragRight_LeadingShrinks(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 600, 200, grows: [1, 1]);
            rig.Panel.FlowDirection = FlowDirection.RightToLeft;
            await MountAsync(H, rig);

            var c0 = rig.PaneExtent(0);
            // Under RTL, parent-relative X increases LEFTWARD on screen.
            // A "+50" cumDelta means the visual cursor moved left by 50.
            // The leading pane (visually right under RTL) should still
            // grow by +50 in its allocated grow space — the math is in
            // tree order, not screen order, and is RTL-correct by virtue
            // of WinUI's pointer coordinate mirroring.
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(50));
            await Harness.Render();

            H.Check("H01_RtlLeadingGrowsBy50",
                ExtentNear(rig.PaneExtent(0), c0 + 50));
            await TeardownAsync(H);
        }
    }

    internal class H02_RtlColumns_KeyboardForwardInverts(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 600, 200, grows: [1, 1]);
            rig.Panel.FlowDirection = FlowDirection.RightToLeft;
            await MountAsync(H, rig);
            await Harness.Render(); // let FlowDirection inheritance propagate
            await Harness.Render();

            // Splitter handle convention: "forward=true" (Right arrow)
            // moves the handle to the right visually, regardless of flow
            // direction. Under LTR that grows the tree-leading pane (it's
            // on the screen-left); under RTL, tree-leading is on the
            // screen-RIGHT so the handle moving right SHRINKS pane[0]
            // and the visually-left pane[1] grows. The implementation
            // negates rawDelta under RTL to achieve this.
            var c0 = rig.PaneExtent(0);
            var c1 = rig.PaneExtent(1);
            var step = DockSplitterControl.DefaultKeyboardStepDip;
            rig.Splitters[0].SimulateKeyboardStep(forward: true);
            await Harness.Render();

            // Under RTL forward=true: pane[0] (screen-right) shrinks,
            // pane[1] (screen-left) grows by step DIPs.
            H.Check("H02_RtlForwardShrinksPane0",
                ExtentNear(rig.PaneExtent(0), c0 - step));
            H.Check("H02_RtlForwardGrowsPane1",
                ExtentNear(rig.PaneExtent(1), c1 + step));
            await TeardownAsync(H);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  I — HiDPI / fractional coords
    // ════════════════════════════════════════════════════════════════════

    internal class I01_FractionalCursorDelta_NoNaN(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(3, FlexDirection.Row, 900, 200, grows: [1, 1, 1]);
            await MountAsync(H, rig);

            rig.Splitters[0].BeginSimulatedDrag(new Point(0.5, 0.25));
            rig.Splitters[0].ContinueSimulatedDrag(new Point(50.5, 0.25));
            rig.Splitters[0].EndSimulatedDrag(new Point(50.5, 0.25));
            await Harness.Render();

            H.Check("I01_NoNaN",
                !double.IsNaN(rig.PaneExtent(0)) && !double.IsNaN(rig.PaneExtent(1)));
            H.Check("I01_FractionalDeltaApplied",
                rig.PaneExtent(0) > rig.PaneExtent(1) - 1);
            await TeardownAsync(H);
        }
    }

    internal class I02_TinyDeltaBelow0p01_NoVisibleChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 600, 200, grows: [1, 1]);
            await MountAsync(H, rig);

            var c0 = rig.PaneExtent(0);
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].ContinueSimulatedDrag(new Point(0.005, 0));
            rig.Splitters[0].ContinueSimulatedDrag(new Point(-0.003, 0));
            rig.Splitters[0].EndSimulatedDrag(rig.Origin);
            await Harness.Render();

            H.Check("I02_LeadingUnchanged", ExtentNear(rig.PaneExtent(0), c0));
            await TeardownAsync(H);
        }
    }

    internal class I03_LongDrag_ConvergesToClamp(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(3, FlexDirection.Row, 900, 200, grows: [1, 1, 1]);
            await MountAsync(H, rig);

            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            // 50 incremental continues over a 2000-DIP drag — far past
            // the panel extent, exercises the clamp every step.
            for (int i = 1; i <= 50; i++)
                rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(i * 40));
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(2000));
            await Harness.Render();

            // Leading must be clamped at panel_pair_max - 60.
            H.Check("I03_NoOverflow", rig.PaneExtent(0) < 900);
            H.Check("I03_MiddleClampedAtMin",
                rig.PaneExtent(1) >= 59.5 && rig.PaneExtent(1) <= 61.0);
            await TeardownAsync(H);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  J — Capture-loss / abort
    // ════════════════════════════════════════════════════════════════════

    internal class J01_AbortMidDrag_FiresFinalWithZeroDelta(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 600, 200, grows: [1, 1]);
            await MountAsync(H, rig);

            int events = 0;
            DockSplitterDeltaEventArgs? last = null;
            rig.Splitters[0].ResizeDelta += (_, e) => { events++; last = e; };

            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(40));
            rig.Splitters[0].AbortSimulatedDrag();
            await Harness.Render();

            H.Check("J01_OneFinalEvent", events == 1);
            H.Check("J01_ZeroDeltaOnAbort",
                last is { } a && Math.Abs(a.Delta) < 0.001);
            H.Check("J01_IsFinal", last is { IsFinal: true });
            await TeardownAsync(H);
        }
    }

    internal class J02_AbortImmediately_NoChangeToGrow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 600, 200, grows: [1, 1]);
            await MountAsync(H, rig);

            var g0 = rig.PaneGrow(0);
            var g1 = rig.PaneGrow(1);
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].AbortSimulatedDrag();
            await Harness.Render();

            H.Check("J02_LeadingGrowUnchanged", Math.Abs(rig.PaneGrow(0) - g0) < 1e-9);
            H.Check("J02_TrailingGrowUnchanged", Math.Abs(rig.PaneGrow(1) - g1) < 1e-9);
            await TeardownAsync(H);
        }
    }

    internal class J03_DoubleBegin_ReSnapshots(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 600, 200, grows: [1, 1]);
            await MountAsync(H, rig);

            // First drag, partial.
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(80));
            await Harness.Render();
            var afterFirst = rig.PaneExtent(0);

            // Second begin without explicit end — re-snapshots from the
            // CURRENT state. Subsequent drag of +40 should grow leading
            // by an additional ~40, not from the original capture.
            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(40));
            await Harness.Render();

            H.Check("J03_SecondDragAdditive",
                ExtentNear(rig.PaneExtent(0), afterFirst + 40, tol: 1.5));
            await TeardownAsync(H);
        }
    }

    internal class J04_ContinueWithoutBegin_NoOp(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 600, 200, grows: [1, 1]);
            await MountAsync(H, rig);

            var c0 = rig.PaneExtent(0);
            int events = 0;
            rig.Splitters[0].ResizeDelta += (_, _) => events++;

            // Continue + End without a prior Begin must no-op silently.
            rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(50));
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(50));
            await Harness.Render();

            H.Check("J04_NoEventsFired", events == 0);
            H.Check("J04_LeadingUnchanged", ExtentNear(rig.PaneExtent(0), c0));
            await TeardownAsync(H);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  K — Sync-capture-lost-during-release regression coverage
    //
    //  The Pix snap-back regression: WinUI fires PointerCaptureLost
    //  SYNCHRONOUSLY inside ReleasePointerCapture. If the splitter's
    //  OnPointerReleased handler calls ReleasePointerCapture BEFORE
    //  EndDragCore has flipped `_isCapturing` to false, the synchronous
    //  capture-loss handler sees a live drag and runs AbortDragCore,
    //  which fires the destructive `ResizeDelta(0, IsFinal=true)`. The
    //  host's solver then computes `oldRatio - 0 = oldRatio`, re-renders
    //  with the pre-drag values, and the splitter snaps back. Then
    //  EndDragCore returns to the (now uncapturing) splitter and
    //  early-returns without firing the legitimate drag delta.
    //
    //  These tests model that ordering directly via the
    //  SimulateRealReleaseSequence + IsCapturingForTest hooks so a
    //  regression in OnPointerReleased's order lights up red here
    //  instead of only in manual gallery testing.
    // ════════════════════════════════════════════════════════════════════

    internal class K01_TerminalEvent_FiresAfterIsCapturingFalse(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 600, 200, grows: [1, 1]);
            await MountAsync(H, rig);

            // Read _isCapturing at the moment the terminal ResizeDelta
            // event fires. The splitter MUST already be in the
            // non-capturing state by then; otherwise a synchronous
            // PointerCaptureLost (which WinUI fires inside
            // ReleasePointerCapture) would observe `_isCapturing = true`
            // and run AbortDragCore destructively against the host's
            // captured ratios. This invariant is what makes
            // ReleasePointerCapture safe to call AFTER EndDragCore.
            bool capturingAtEvent = true;
            rig.Splitters[0].ResizeDelta += (s, _) =>
            {
                capturingAtEvent = ((DockSplitterControl)s!).IsCapturingForTest;
            };

            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].EndSimulatedDrag(rig.Delta(50));

            H.Check("K01_IsCapturingFalseWhenTerminalEventFires", !capturingAtEvent);
            await TeardownAsync(H);
        }
    }

    internal class K02_RealReleaseSequence_DragCommitsDespiteSyncCaptureLost(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rig = new Rig(2, FlexDirection.Row, 600, 200, grows: [1, 1]);
            await MountAsync(H, rig);

            var c0Before = rig.PaneExtent(0);
            var events = new List<DockSplitterDeltaEventArgs>();
            rig.Splitters[0].ResizeDelta += (_, e) => events.Add(e);

            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(50));

            // SimulateRealReleaseSequence models the production
            // OnPointerReleased ordering. The hook must order EndDragCore
            // (which flips _isCapturing to false) BEFORE the synchronous
            // capture-loss simulator runs; otherwise the splitter fires
            // a destructive zero-delta event and the legitimate drag is
            // discarded. This is the Pix snap-back regression.
            rig.Splitters[0].SimulateRealReleaseSequence(rig.Delta(50));
            await Harness.Render();

            H.Check("K02_ExactlyOneTerminalEvent", events.Count == 1);
            H.Check("K02_EventReflectsCumDelta_NotZero",
                events.Count == 1 && Math.Abs(events[0].Delta + 50) < 0.01);
            H.Check("K02_LeadingPaneCommittedTo50",
                ExtentNear(rig.PaneExtent(0), c0Before + 50));
            await TeardownAsync(H);
        }
    }

    internal class K03_RealReleaseSequence_ThreePaneEqualShare(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // The original Pix repro: 3-pane equal-share. Drag the left
            // splitter by +75; release; verify the host-observable event
            // and the pane widths reflect the drag, not a snap-back to
            // the equal-share starting position.
            var rig = new Rig(3, FlexDirection.Row, 900, 200, grows: [1, 1, 1]);
            await MountAsync(H, rig);

            var c0Before = rig.PaneExtent(0);
            var c1Before = rig.PaneExtent(1);
            var c2Before = rig.PaneExtent(2);

            var events = new List<DockSplitterDeltaEventArgs>();
            rig.Splitters[0].ResizeDelta += (_, e) => events.Add(e);

            rig.Splitters[0].BeginSimulatedDrag(rig.Origin);
            rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(40));
            rig.Splitters[0].ContinueSimulatedDrag(rig.Delta(75));
            rig.Splitters[0].SimulateRealReleaseSequence(rig.Delta(75));
            await Harness.Render();

            H.Check("K03_OneEvent", events.Count == 1);
            H.Check("K03_EventDeltaNegated75",
                events.Count == 1 && Math.Abs(events[0].Delta + 75) < 0.01);
            H.Check("K03_C0GrewBy75", ExtentNear(rig.PaneExtent(0), c0Before + 75));
            H.Check("K03_C1ShrunkBy75", ExtentNear(rig.PaneExtent(1), c1Before - 75));
            H.Check("K03_C2Untouched", ExtentNear(rig.PaneExtent(2), c2Before));
            await TeardownAsync(H);
        }
    }
}
