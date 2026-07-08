using System.Collections.Generic;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045/046 — supplemental in-process coverage for the docking NATIVE
//  controls. These fixtures drive the internal *ForTest / seam entry points
//  the existing NativeDockingCoverage* + NativeDockingTearOff fixtures do
//  NOT reach:
//
//    • DockDropTargetOverlayControl — SetDisabledTargets guard path,
//      HitTestForTarget + the tear-off pipeline entry points
//      (UpdateHoverAt/TryConfirmAt/ClearHover/TryConfirmCurrentHover),
//      the PointerEntered/Exited reveal toggle, and the automation peer.
//    • DockSplitterControl — the DiagnosticSink-gated MOVE branch inside
//      ContinueDragCore that the sink-less matrix fixtures skip.
//    • DockTabTearOff — MoveCore's XamlRoot==null abort branch and the
//      "hook not attached" guard throws on the Simulate* seams.
//
//  Every check asserts a concrete behaviour/invariant, not merely non-null.
// ════════════════════════════════════════════════════════════════════════

/// <summary>Spec 046 §6.6 + spec 045 §2.6 — coverage for the drop-target
/// overlay seams the base <see cref="NativeDockingCoverageOverlayFixtures"/>
/// set doesn't touch.</summary>
internal static class OverlaySeamCoverageFixtures
{
    private static DockDropTargetOverlayControl MakeHostOverlay(double w = 600, double h = 400) =>
        new() { Width = w, Height = h, Mode = DockDropOverlayMode.Host };

    /// <summary>
    /// Exercises <see cref="DockDropTargetOverlayControl.SetDisabledTargets"/>:
    /// a disabled target refuses hover (SetHovered treats it as null) and
    /// refuses confirm; disabling the currently-hovered target clears the
    /// hover; re-enabling restores normal behaviour. Also proves the visual
    /// dimming (Opacity 0.35) is applied to exactly the disabled buttons.
    /// </summary>
    internal class Overlay_SetDisabledTargets_GuardsHoverAndConfirm(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var overlay = MakeHostOverlay();
            H.SetContent(overlay);
            await Harness.Render();

            int hoverCount = 0, confirmCount = 0;
            DockTarget? lastHover = null;
            overlay.TargetHovered += (_, e) => { hoverCount++; lastHover = e.Target; };
            overlay.TargetConfirmed += (_, _) => confirmCount++;

            // Disable two edge targets.
            overlay.SetDisabledTargets(new[] { DockTarget.DockLeft, DockTarget.DockTop });

            // Exactly two buttons dim to the disabled opacity (0.35). The
            // preview rect is a Border too, but sits at 0.30 — distinct.
            int dimmed = H.FindAllControls<Border>(b => Math.Abs(b.Opacity - 0.35) < 1e-3).Count;
            H.Check("Overlay_Disabled_TwoButtonsDimmed", dimmed == 2);

            // Hovering a disabled target is swallowed → no event, no hover.
            overlay.SetHoveredForTest(DockTarget.DockLeft);
            H.Check("Overlay_Disabled_HoverSwallowed", hoverCount == 0);
            H.Check("Overlay_Disabled_HoveredTargetStaysNull", overlay.HoveredTarget is null);

            // Confirming a disabled target is refused.
            overlay.ConfirmTargetForTest(DockTarget.DockLeft);
            H.Check("Overlay_Disabled_ConfirmRefused", confirmCount == 0);

            // The disabled guard also holds through the GEOMETRY seams (the
            // real tear-off drop path), not just the direct hooks. The button
            // is dimmed, not hidden, so it still hit-tests — but driving
            // UpdateHoverAt / TryConfirmAt over it must neither latch a hover
            // nor fire a confirm.
            await Harness.WaitFor(() => overlay.ActualWidth > 1 && overlay.ActualHeight > 1);
            var atDisabledLeft = new Point(
                DockDropTargetOverlayControl.ButtonSizeDip / 2, overlay.ActualHeight / 2);
            H.Check("Overlay_Disabled_HitTestStillResolvesButton",
                overlay.HitTestForTarget(atDisabledLeft) == DockTarget.DockLeft);
            overlay.UpdateHoverAt(atDisabledLeft);
            H.Check("Overlay_Disabled_UpdateHoverAt_NoLatchNoEvent",
                overlay.CurrentHoveredTarget is null && hoverCount == 0);
            var confirmDisabledViaGeom = overlay.TryConfirmAt(atDisabledLeft);
            H.Check("Overlay_Disabled_TryConfirmAt_RefusedReturnsNull",
                confirmDisabledViaGeom is null && confirmCount == 0);

            // An enabled target still hovers.
            overlay.SetHoveredForTest(DockTarget.DockRight);
            H.Check("Overlay_Enabled_HoverFires", hoverCount == 1 && lastHover == DockTarget.DockRight);

            // Disabling the CURRENTLY-hovered target clears the hover (fires
            // once more with null).
            overlay.SetDisabledTargets(new[] { DockTarget.DockRight });
            H.Check("Overlay_DisableHovered_ClearsHover", overlay.HoveredTarget is null);
            H.Check("Overlay_DisableHovered_FiredNull", hoverCount == 2 && lastHover is null);

            // Re-enable everything (null clears the mask). The dimming lifts.
            overlay.SetDisabledTargets(null);
            int dimmedAfter = H.FindAllControls<Border>(b => Math.Abs(b.Opacity - 0.35) < 1e-3).Count;
            H.Check("Overlay_ReEnabled_NoDimming", dimmedAfter == 0);

            // Differential oracle for the mask-clear: DockRight — disabled at
            // the previous step — must now hover AND confirm again. (Probing an
            // already-enabled target would still pass if the clear was a no-op.)
            int confirmsBeforeReEnable = confirmCount;
            overlay.SetHoveredForTest(DockTarget.DockRight);
            H.Check("Overlay_ReEnabled_PreviouslyDisabledHoversAgain",
                overlay.HoveredTarget == DockTarget.DockRight);
            overlay.ConfirmTargetForTest(DockTarget.DockRight);
            H.Check("Overlay_ReEnabled_PreviouslyDisabledConfirmsAgain",
                confirmCount == confirmsBeforeReEnable + 1);

            // A redundant clear of an already-empty mask exercises the
            // mask-unchanged early-return in SetDisabledTargets. That branch is
            // a non-observable optimization (it only skips re-applying identical
            // state), so no behavioral oracle exists for it — the call is here
            // for line coverage and is deliberately not asserted on.
            overlay.SetDisabledTargets(null);

            H.SetContent(null);
            await Harness.Render();
        }
    }

    /// <summary>
    /// Drives <see cref="DockDropTargetOverlayControl.HitTestForTarget"/> and
    /// the tear-off pipeline entry points (<c>UpdateHoverAt</c>,
    /// <c>TryConfirmAt</c>, <c>ClearHover</c>, <c>TryConfirmCurrentHover</c>,
    /// <c>CurrentHoveredTarget</c>) at real button geometry. Host mode shows
    /// only the 4 edge buttons, so a hit at each edge resolves that target
    /// and a hit at dead-centre (cluster collapsed) resolves null.
    /// </summary>
    internal class Overlay_HitTestPipeline_ResolvesTargetsAndConfirms(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var overlay = MakeHostOverlay();
            H.SetContent(overlay);
            await Harness.Render();
            // Geometry needs a real arrange pass.
            await Harness.WaitFor(() => overlay.ActualWidth > 1 && overlay.ActualHeight > 1);

            double w = overlay.ActualWidth, hgt = overlay.ActualHeight;
            const double half = DockDropTargetOverlayControl.ButtonSizeDip / 2;
            var atLeft = new Point(half, hgt / 2);
            var atRight = new Point(w - half, hgt / 2);
            var atTop = new Point(w / 2, half);
            var atBottom = new Point(w / 2, hgt - half);
            var atCentre = new Point(w / 2, hgt / 2);

            H.Check("Overlay_HitTest_Left", overlay.HitTestForTarget(atLeft) == DockTarget.DockLeft);
            H.Check("Overlay_HitTest_Right", overlay.HitTestForTarget(atRight) == DockTarget.DockRight);
            H.Check("Overlay_HitTest_Top", overlay.HitTestForTarget(atTop) == DockTarget.DockTop);
            H.Check("Overlay_HitTest_Bottom", overlay.HitTestForTarget(atBottom) == DockTarget.DockBottom);
            // Host mode collapses the inner cluster — centre hits nothing.
            H.Check("Overlay_HitTest_CentreMisses", overlay.HitTestForTarget(atCentre) is null);
            // Far outside all buttons → null.
            H.Check("Overlay_HitTest_OutsideMisses",
                overlay.HitTestForTarget(new Point(-50, -50)) is null);

            int hovers = 0, confirms = 0;
            DockTarget? lastConfirm = null;
            overlay.TargetHovered += (_, _) => hovers++;
            overlay.TargetConfirmed += (_, e) => { confirms++; lastConfirm = e.Target; };

            // UpdateHoverAt latches the hovered target and fires TargetHovered.
            var nowHover = overlay.UpdateHoverAt(atLeft);
            H.Check("Overlay_UpdateHoverAt_ReturnsHit", nowHover == DockTarget.DockLeft);
            H.Check("Overlay_UpdateHoverAt_LatchesCurrent",
                overlay.CurrentHoveredTarget == DockTarget.DockLeft && hovers == 1);

            // TryConfirmCurrentHover confirms the latched target.
            var confirmedCurrent = overlay.TryConfirmCurrentHover();
            H.Check("Overlay_TryConfirmCurrent_ConfirmsLatched",
                confirmedCurrent == DockTarget.DockLeft && confirms == 1 && lastConfirm == DockTarget.DockLeft);

            // ClearHover drops the latch.
            overlay.ClearHover();
            H.Check("Overlay_ClearHover_Drops", overlay.CurrentHoveredTarget is null);

            // TryConfirmAt hit-tests + confirms in one shot.
            var confirmedAt = overlay.TryConfirmAt(atRight);
            H.Check("Overlay_TryConfirmAt_HitConfirms",
                confirmedAt == DockTarget.DockRight && confirms == 2 && lastConfirm == DockTarget.DockRight);

            // TryConfirmAt over empty space returns null and fires nothing new.
            var missConfirm = overlay.TryConfirmAt(atCentre);
            H.Check("Overlay_TryConfirmAt_MissReturnsNull", missConfirm is null && confirms == 2);

            H.SetContent(null);
            await Harness.Render();
        }
    }

    /// <summary>
    /// Spec 045 §2.6 — the tear-off pipeline reveals a GroupInner overlay's
    /// inner cluster on <c>PointerEntered</c> and hides it on
    /// <c>PointerExited</c> (the WinUI DragEnter/Leave analogue). Drives the
    /// two seam hooks and asserts the reveal flag + button visibility flip.
    /// </summary>
    internal class Overlay_PointerEnterExit_TogglesGroupReveal(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var overlay = new DockDropTargetOverlayControl
            {
                Width = 400,
                Height = 300,
                Mode = DockDropOverlayMode.GroupInner,
            };
            H.SetContent(overlay);
            await Harness.Render();

            // Before any enter, the inner cluster is masked.
            H.Check("Overlay_GroupInner_StartsHidden", !overlay.IsGroupOverlayRevealedForTest);

            // PointerEntered unmasks the cluster.
            overlay.SimulatePointerEnteredForTest();
            await Harness.Render();
            H.Check("Overlay_PointerEntered_Reveals", overlay.IsGroupOverlayRevealedForTest);
            H.Check("Overlay_Revealed_CentreVisible",
                await Harness.WaitFor(() =>
                {
                    // Center hit-test now resolves (the cluster is visible).
                    return overlay.HitTestForTarget(new Point(overlay.ActualWidth / 2, overlay.ActualHeight / 2))
                        == DockTarget.Center;
                }));

            // PointerExited re-masks it.
            overlay.SimulatePointerExitedForTest();
            await Harness.Render();
            H.Check("Overlay_PointerExited_Hides", !overlay.IsGroupOverlayRevealedForTest);
            H.Check("Overlay_Hidden_CentreMisses",
                overlay.HitTestForTarget(new Point(overlay.ActualWidth / 2, overlay.ActualHeight / 2)) is null);

            H.SetContent(null);
            await Harness.Render();
        }
    }

    /// <summary>
    /// Verifies <see cref="DockDropTargetOverlayControl.OnCreateAutomationPeer"/>
    /// yields a peer that reports the overlay as an a11y Group with the
    /// expected class + localized control-type names.
    /// </summary>
    internal class Overlay_AutomationPeer_ReportsGroup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var overlay = MakeHostOverlay();
            H.SetContent(overlay);
            await Harness.Render();

            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(overlay);
            // The class-name / localized-control-type values come from the
            // overlay's custom OnCreateAutomationPeer override; if that override
            // were removed the inherited Grid peer would report different values,
            // so these are the real oracles (a bare non-null check is not — the
            // base Grid still yields a peer).
            H.Check("Overlay_Peer_ControlTypeGroup",
                peer?.GetAutomationControlType() == AutomationControlType.Group);
            H.Check("Overlay_Peer_ClassName",
                peer?.GetClassName() == "DockDropTargetOverlay");
            H.Check("Overlay_Peer_LocalizedControlType",
                peer?.GetLocalizedControlType() == "dock target group");

            H.SetContent(null);
            await Harness.Render();
        }
    }
}

/// <summary>Spec 045 §2.1 — coverage for the DiagnosticSink-gated drag
/// tracing in <see cref="DockSplitterControl"/> that the sink-less matrix
/// fixtures skip.</summary>
internal static class SplitterSeamCoverageFixtures
{
    /// <summary>
    /// Wires <see cref="DockSplitterControl.DiagnosticSink"/> and drives a
    /// granular Begin → Continue → Continue → End drag inside a real
    /// <see cref="FlexPanel"/>. This lights the diagnostic pre/post
    /// leading-width snapshot branch inside <c>ContinueDragCore</c> (only
    /// taken when a sink is attached) and proves the emitted MOVE/RELEASE
    /// traces carry live cumulative-delta + leading-width data, while the
    /// drag still redistributes flex grow between the two panes.
    /// </summary>
    internal class Splitter_DiagnosticSink_TracesDragLifecycle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var leading = new Border { MinWidth = 40, MinHeight = 40 };
            var trailing = new Border { MinWidth = 40, MinHeight = 40 };
            FlexPanel.SetGrow(leading, 1);
            FlexPanel.SetGrow(trailing, 1);

            var splitter = new DockSplitterControl { Direction = DockSplitterDirection.Columns };
            var panel = new FlexPanel { Direction = FlexDirection.Row, Width = 600, Height = 200 };
            panel.Children.Add(leading);
            panel.Children.Add(splitter);
            panel.Children.Add(trailing);

            H.SetContent(panel);
            await Harness.Render();
            await Harness.Render(); // double-pump so ActualWidth populates

            var traces = new List<string>();
            splitter.DiagnosticSink = msg => traces.Add(msg);

            int finalDeltas = 0;
            DockSplitterDeltaEventArgs? last = null;
            splitter.ResizeDelta += (_, e) => { if (e.IsFinal) { finalDeltas++; last = e; } };

            double leadGrowBefore = FlexPanel.GetGrow(leading);

            // Granular lifecycle: press at origin, two moves, release.
            splitter.BeginSimulatedDrag(new Point(0, 0));
            H.Check("SplitterDiag_Capturing", splitter.IsCapturingForTest);
            splitter.ContinueSimulatedDrag(new Point(60, 0));
            splitter.ContinueSimulatedDrag(new Point(120, 0));
            splitter.EndSimulatedDrag(new Point(120, 0));

            H.Check("SplitterDiag_ReleasedCapture", !splitter.IsCapturingForTest);
            H.Check("SplitterDiag_FiredOneFinalDelta", finalDeltas == 1);
            // Solver convention negates the cursor delta.
            H.Check("SplitterDiag_FinalDeltaNegated",
                last is { } a && Math.Abs(a.Delta - (-120)) < 0.001);
            H.Check("SplitterDiag_HostExtentPositive",
                last is { HostExtentDip: > 0 });

            // The sink saw a PRESS, at least one MOVE (the branch under test),
            // and a RELEASE — carrying real cumulative-delta payloads.
            H.Check("SplitterDiag_SinkSawPress", traces.Any(t => t.StartsWith("PRESS")));
            H.Check("SplitterDiag_SinkSawMoveWithDelta",
                traces.Any(t => t.StartsWith("MOVE") && t.Contains("cumDelta=") && t.Contains("postLeadingActual=")));
            H.Check("SplitterDiag_SinkSawRelease",
                traces.Any(t => t.StartsWith("RELEASE") && t.Contains("cumDelta=")));

            // Non-vacuous oracle for the sink-gated visual-tree snapshot
            // sub-branch: the MOVE trace must carry LIVE leading-pane widths
            // read from the FlexPanel child — NOT the -1 sentinel that the
            // no-FlexPanel / no-op path would log. If the snapshot sub-branch
            // regressed to always log placeholders, this fails.
            var moveTrace = traces.FirstOrDefault(t => t.StartsWith("MOVE"));
            H.Check("SplitterDiag_MoveTrace_LiveLeadingWidths",
                moveTrace is not null
                && !moveTrace.Contains("preLeadingActual=-1")
                && !moveTrace.Contains("postLeadingActual=-1"));

            // The drag actually moved grow off the leading pane.
            double leadGrowAfter = FlexPanel.GetGrow(leading);
            H.Check("SplitterDiag_GrowRedistributed",
                Math.Abs(leadGrowAfter - leadGrowBefore) > 1e-6);

            splitter.DiagnosticSink = null;
            H.SetContent(null);
            await Harness.Render();
        }
    }
}

/// <summary>Spec 045 §2.6 — coverage for the tear-off state machine guards
/// that the mounted T-matrix fixtures don't reach.</summary>
internal static class TearOffSeamCoverageFixtures
{
    /// <summary>
    /// Drives the tear-off press/move state machine on a bare (never-mounted)
    /// <see cref="TabView"/>: crossing the threshold with a null
    /// <c>XamlRoot</c> must abort cleanly (candidate cleared, no tracker
    /// started) rather than proceed into <c>BeginTearOff</c>. Also asserts
    /// the Simulate* seams throw when a hook was never attached.
    /// </summary>
    internal class TearOff_MoveWithoutXamlRoot_AbortsAndGuards(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var savedThreshold = DockTabTearOff.ThresholdDipForTest;
            var savedAuto = DockTabTearOffTracker.AutoStartTimerForTest;
            DockTabTearOffTracker.AutoStartTimerForTest = false;
            DockTabTearOff.ThresholdDipForTest = 1.0;
            try
            {
                var pane = new Document { Title = "seam", Key = "seam:doc", Content = TextBlock("seam-body") };

                // (1) Guard: the Simulate* seams reject a TabView with no hook.
                var bareForGuard = new TabView();
                bool pressThrew = false, moveThrew = false;
                try { DockTabTearOff.SimulatePressForTest(bareForGuard, new TabViewItem(), pane, 0); }
                catch (InvalidOperationException) { pressThrew = true; }
                try { DockTabTearOff.SimulateMoveForTest(bareForGuard, 5, 5); }
                catch (InvalidOperationException) { moveThrew = true; }
                H.Check("TearOff_NoHook_PressThrows", pressThrew);
                H.Check("TearOff_NoHook_MoveThrows", moveThrew);

                // (2) Attach a hook to a bare TabView (never added to a live
                // tree, so XamlRoot stays null), press a tab, then move past
                // the threshold. MoveCore must hit the XamlRoot==null abort.
                var tabView = new TabView();
                bool beginTearOffCalled = false;
                DockTabTearOff.AttachPressHook(
                    tabView,
                    resolveTab: _ => (pane, 0),
                    beginTearOff: _ => { beginTearOffCalled = true; return null; });

                H.Check("TearOff_HookAttached", DockTabTearOff.IsHookAttachedForTest(tabView));
                H.Check("TearOff_BareTabView_NoXamlRoot", tabView.XamlRoot is null);

                DockTabTearOff.SimulatePressForTest(tabView, new TabViewItem(), pane, tabIndex: 0, localX: 0, localY: 0);
                var (candBefore, _, _) = DockTabTearOff.InspectCandidateForTest(tabView);
                H.Check("TearOff_PressRecordedCandidate", ReferenceEquals(candBefore, pane));

                // Threshold-crossing move with null XamlRoot → clean abort
                // BEFORE BeginTearOff runs. The abort is the ONLY thing that
                // stops BeginTearOff here — the normal proceed path ALSO clears
                // the candidate, so a candidate-null check alone is vacuous for
                // this arm. Asserting BeginTearOff was never invoked is the real
                // oracle: it flips to called if the null-XamlRoot guard is removed.
                DockTabTearOff.SimulateMoveForTest(tabView, 25, 25);

                H.Check("TearOff_NullXamlRoot_BeginTearOffNotCalled", !beginTearOffCalled);
                var (candAfter, _, _) = DockTabTearOff.InspectCandidateForTest(tabView);
                // Secondary postcondition: the abort leaves no dangling candidate.
                H.Check("TearOff_NullXamlRoot_ClearsCandidate", candAfter is null);
            }
            finally
            {
                DockTabTearOff.ResetAllCandidatesForTest();
                DockTabTearOffTracker.ResetForTest();
                DockTabTearOff.ThresholdDipForTest = savedThreshold;
                DockTabTearOffTracker.AutoStartTimerForTest = savedAuto;
            }

            await Harness.Render();
        }
    }
}
