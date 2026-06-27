using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.6 — VS-style tab tear-off matrix.
//
//  Each fixture mounts a DockManager, locates the TabView, and drives the
//  tear-off state machine via the *ForTest hooks on DockTabTearOff /
//  DockTabTearOffTracker. The hooks call the same Core methods the
//  production PointerPressed/Moved handlers wrap, so a regression caught
//  here is a regression in production code.
//
//  The cursor-poll timer is disabled (AutoStartTimerForTest=false) so
//  fixtures can advance the pipeline deterministically without depending
//  on real-time ticks. The threshold is shrunk to 1 DIP so small
//  synthesized moves are unambiguous.
//
//  Every fixture wraps its body in a try/finally that resets:
//    • DockTabTearOff candidate state (all hooks)
//    • DockTabTearOffTracker (active drag + timer)
//    • DockDragSession (process-wide drag slot)
//    • ThresholdDipForTest + AutoStartTimerForTest (process-wide statics)
//  Without that reset a leaked session would poison the next fixture.
// ════════════════════════════════════════════════════════════════════════

internal static class NativeDockingTearOffFixtures
{
    private static DockableContent MakePane(string key, string body, bool canMove = true, bool canFloat = true) =>
        new(Title: key, Key: key, Content: TextBlock(body), CanClose: true)
        {
            CanMove = canMove,
            CanFloat = canFloat,
        };

    /// <summary>Restore every process-wide static the tear-off pipeline
    /// touches. Symmetric: every fixture body opens with these resets in
    /// a try/finally tail so a partial run can't leak into the next.</summary>
    private static void ResetAll()
    {
        DockTabTearOff.ResetAllCandidatesForTest();
        DockTabTearOffTracker.ResetForTest();
        DockDragSession.ResetForTest();
        DockTabTearOff.ThresholdDipForTest = null;
    }

    /// <summary>Prep statics for a deterministic test run: skip the
    /// cursor-poll timer (tests advance the pipeline manually), shrink
    /// the move threshold so a 2-DIP simulated move trips it.</summary>
    private static void PrepDeterministic()
    {
        DockTabTearOffTracker.AutoStartTimerForTest = false;
        DockTabTearOff.ThresholdDipForTest = 1.0;
    }

    private static TabView? FirstTabView(Harness h) =>
        h.FindAllControls<TabView>(_ => true).FirstOrDefault();

    // ─── #1 PressHookAttached ──────────────────────────────────────────────

    /// <summary>Sanity: the host's render path always attaches the
    /// DockTabTearOff press hook to every host TabView. Without this we
    /// wouldn't observe any of the other behaviors.</summary>
    internal class T01_HostMounts_AttachesPressHook(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ResetAll(); PrepDeterministic();
            try
            {
                var host = H.CreateHost();
                DockingNativeInterop.Register(host.Reconciler);
                var a = MakePane("a", "body-a");
                var b = MakePane("b", "body-b");
                host.Mount(_ => new DockManager
                {
                    Layout = new DockTabGroup(new[] { a, b }),
                });
                await Harness.Render();

                var tv = FirstTabView(H);
                H.Check("T01_TabViewFound", tv is not null);
                H.Check("T01_PressHookAttached",
                    tv is not null && DockTabTearOff.IsHookAttachedForTest(tv));

                host.Mount(_ => TextBlock("done"));
                await Harness.Render();
            }
            finally { ResetAll(); }
        }
    }

    // ─── #2 PressRecordsCandidate ──────────────────────────────────────────

    /// <summary>A press on a tab records a candidate; the candidate
    /// carries the pane reference + tab index + start cursor pos.</summary>
    internal class T02_PressOnTab_RecordsCandidate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ResetAll(); PrepDeterministic();
            try
            {
                var host = H.CreateHost();
                DockingNativeInterop.Register(host.Reconciler);
                var a = MakePane("a", "body-a");
                var b = MakePane("b", "body-b");
                host.Mount(_ => new DockManager
                {
                    Layout = new DockTabGroup(new[] { a, b }),
                });
                await Harness.Render();
                var tv = FirstTabView(H);
                H.Check("T02_TabViewFound", tv is not null);
                if (tv is null) return;

                var item0 = (TabViewItem)tv.TabItems[0];
                DockTabTearOff.SimulatePressForTest(tv, item0, a, tabIndex: 0, localX: 0, localY: 0);
                var (candidate, idx, start) = DockTabTearOff.InspectCandidateForTest(tv);
                H.Check("T02_CandidatePane", ReferenceEquals(candidate, a));
                H.Check("T02_CandidateIndex", idx == 0);
                H.Check("T02_CandidateStart", start is { X: 0, Y: 0 });

                host.Mount(_ => TextBlock("done"));
                await Harness.Render();
            }
            finally { ResetAll(); }
        }
    }

    // ─── #3 BelowThreshold_NoTearOff ───────────────────────────────────────

    /// <summary>Press + move below threshold — the candidate stays
    /// recorded but no tear-off fires (no tracker active, no session).</summary>
    internal class T03_PressBelowThreshold_NoTearOff(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ResetAll(); PrepDeterministic();
            // Threshold=1 — move (0.5,0) is below sqrt(1^2)=1.
            DockTabTearOff.ThresholdDipForTest = 1.0;
            try
            {
                var host = H.CreateHost();
                DockingNativeInterop.Register(host.Reconciler);
                var a = MakePane("a", "body-a");
                host.Mount(_ => new DockManager { Layout = new DockTabGroup(new[] { a }) });
                await Harness.Render();
                var tv = FirstTabView(H);
                if (tv is null) { H.Check("T03_TabViewFound", false); return; }

                var item0 = (TabViewItem)tv.TabItems[0];
                DockTabTearOff.SimulatePressForTest(tv, item0, a, 0);
                DockTabTearOff.SimulateMoveForTest(tv, 0.5, 0.0);

                H.Check("T03_NoTrackerActive", !DockTabTearOffTracker.IsActiveForTest);
                H.Check("T03_NoSession", DockDragSession.Current is null or { IsActive: false });
                var (cand, _, _) = DockTabTearOff.InspectCandidateForTest(tv);
                H.Check("T03_CandidateStillRecorded", ReferenceEquals(cand, a));

                host.Mount(_ => TextBlock("done"));
                await Harness.Render();
            }
            finally { ResetAll(); }
        }
    }

    // ─── #4 AboveThreshold_TearsOff ────────────────────────────────────────

    /// <summary>Press + move above threshold triggers BeginTearOff: the
    /// pane is removed from the source layout, the tracker becomes
    /// active, a DockDragSession is started, and the candidate is
    /// cleared (so a subsequent press on a different tab is fresh).</summary>
    internal class T04_PressAboveThreshold_TearsOff(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ResetAll(); PrepDeterministic();
            ReactorWindow? previewWindow = null;
            try
            {
                var host = H.CreateHost();
                DockingNativeInterop.Register(host.Reconciler);
                var a = MakePane("a", "body-a");
                var b = MakePane("b", "body-b");
                host.Mount(_ => new DockManager { Layout = new DockTabGroup(new[] { a, b }) });
                await Harness.Render();
                var tv = FirstTabView(H);
                if (tv is null) { H.Check("T04_TabViewFound", false); return; }

                var item0 = (TabViewItem)tv.TabItems[0];
                DockTabTearOff.SimulatePressForTest(tv, item0, a, 0);
                DockTabTearOff.SimulateMoveForTest(tv, 5.0, 5.0);
                await Harness.Render();
                previewWindow = DockTabTearOffTracker.ActiveForTest?.FloatingWindow;

                H.Check("T04_TrackerActive", DockTabTearOffTracker.IsActiveForTest);
                H.Check("T04_SessionActive",
                    DockDragSession.Current is { IsActive: true } s
                    && ReferenceEquals(s.Source, a));
                var (cand, _, _) = DockTabTearOff.InspectCandidateForTest(tv);
                H.Check("T04_CandidateCleared", cand is null);

                // The source layout should now have only 'b' — 'a' was
                // torn out into a floating window.
                var tvAfter = FirstTabView(H);
                var headersAfter = tvAfter is null
                    ? new List<string?>()
                    : tvAfter.TabItems.OfType<TabViewItem>().Select(t => t.Header as string).ToList();
                H.Check("T04_SourceTabsAreB_only",
                    headersAfter.Count == 1 && headersAfter[0] == "b");

                // commit:false (Esc) ends the session but leaves the
                // preview floating window open with drag styles stripped
                // (spec §2.6). The fixture's finally closes it explicitly.
                DockTabTearOffTracker.SimulateReleaseForTest(commit: false);
                await Harness.Render();
                host.Mount(_ => TextBlock("done"));
                await Harness.Render();
            }
            finally
            {
                if (previewWindow is not null) DockFloatingPaneRouter.Unregister(previewWindow);
                previewWindow?.Close();
                ResetAll();
            }
        }
    }

    // ─── #5 CanMoveFalse_Refused ───────────────────────────────────────────

    /// <summary>A pane with CanMove=false refuses the tear-off: the
    /// MoveCore call returns null from BeginTearOff, no tracker starts,
    /// no session begins, and the source layout is unchanged.</summary>
    internal class T05_CanMoveFalse_RefusedAtBeginTearOff(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ResetAll(); PrepDeterministic();
            try
            {
                var host = H.CreateHost();
                DockingNativeInterop.Register(host.Reconciler);
                var pinned = MakePane("p", "body-p", canMove: false);
                var movable = MakePane("m", "body-m");
                host.Mount(_ => new DockManager { Layout = new DockTabGroup(new[] { pinned, movable }) });
                await Harness.Render();
                var tv = FirstTabView(H);
                if (tv is null) { H.Check("T05_TabViewFound", false); return; }

                var item0 = (TabViewItem)tv.TabItems[0];
                DockTabTearOff.SimulatePressForTest(tv, item0, pinned, 0);
                DockTabTearOff.SimulateMoveForTest(tv, 5.0, 5.0);
                await Harness.Render();

                H.Check("T05_NoTracker", !DockTabTearOffTracker.IsActiveForTest);
                H.Check("T05_NoSession", DockDragSession.Current is null or { IsActive: false });
                var tvAfter = FirstTabView(H);
                H.Check("T05_BothTabsStillPresent",
                    tvAfter is not null && tvAfter.TabItems.Count == 2);

                host.Mount(_ => TextBlock("done"));
                await Harness.Render();
            }
            finally { ResetAll(); }
        }
    }

    // ─── #6 CanFloatFalse_Refused ──────────────────────────────────────────

    /// <summary>A pane with CanFloat=false also refuses tear-off (the
    /// new pipeline checks CanFloat in BeginImmediateTearOff because
    /// the tear-off opens a floating window unconditionally — unlike
    /// the old wasOutside-tear-out path).</summary>
    internal class T06_CanFloatFalse_RefusedAtBeginTearOff(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ResetAll(); PrepDeterministic();
            try
            {
                var host = H.CreateHost();
                DockingNativeInterop.Register(host.Reconciler);
                var stuck = MakePane("s", "body-s", canFloat: false);
                var movable = MakePane("m", "body-m");
                host.Mount(_ => new DockManager { Layout = new DockTabGroup(new[] { stuck, movable }) });
                await Harness.Render();
                var tv = FirstTabView(H);
                if (tv is null) { H.Check("T06_TabViewFound", false); return; }

                var item0 = (TabViewItem)tv.TabItems[0];
                DockTabTearOff.SimulatePressForTest(tv, item0, stuck, 0);
                DockTabTearOff.SimulateMoveForTest(tv, 5.0, 5.0);
                await Harness.Render();

                H.Check("T06_NoTracker", !DockTabTearOffTracker.IsActiveForTest);
                H.Check("T06_NoSession", DockDragSession.Current is null or { IsActive: false });
                var tvAfter = FirstTabView(H);
                H.Check("T06_BothTabsStillPresent",
                    tvAfter is not null && tvAfter.TabItems.Count == 2);

                host.Mount(_ => TextBlock("done"));
                await Harness.Render();
            }
            finally { ResetAll(); }
        }
    }

    // ─── #7 StaleTrackerForceCleaned_NextDragSucceeds ──────────────────────

    /// <summary>If a previous drag left the tracker in a non-Stop'd state
    /// (e.g. a missed release, a crashed callback), the NEXT press+move
    /// must not get permanently stuck. BeginImmediateTearOff's defensive
    /// path force-cancels the stale tracker (which strips drag styles
    /// from its floating window) and then proceeds with the new drag.
    /// This contract is what guards the §2.6 stuck-state issue.</summary>
    internal class T07_StaleTrackerForceCleaned_NextDragSucceeds(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ResetAll(); PrepDeterministic();
            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorWindow? firstFloating = null;
            ReactorWindow? secondFloating = null;
            try
            {
                var host = H.CreateHost();
                DockingNativeInterop.Register(host.Reconciler);
                var a = MakePane("a", "body-a");
                var b = MakePane("b", "body-b");
                var c = MakePane("c", "body-c");
                host.Mount(_ => new DockManager { Layout = new DockTabGroup(new[] { a, b, c }) });
                await Harness.Render();
                var tv = FirstTabView(H);
                if (tv is null) { H.Check("T07_TabViewFound", false); return; }

                // First drag — succeeds, leaves tracker active.
                var item0 = (TabViewItem)tv.TabItems[0];
                DockTabTearOff.SimulatePressForTest(tv, item0, a, 0);
                DockTabTearOff.SimulateMoveForTest(tv, 5.0, 5.0);
                await Harness.Render();
                H.Check("T07_FirstDragStarted", DockTabTearOffTracker.IsActive);
                var firstActive = DockTabTearOffTracker.ActiveForTest;
                firstFloating = firstActive?.FloatingWindow;

                // Second drag on the post-tear-off TabView. The stale
                // first tracker should be force-cleaned; the second drag
                // proceeds.
                var tvNow = FirstTabView(H);
                if (tvNow is not null && tvNow.TabItems.Count >= 1)
                {
                    var itemNext = (TabViewItem)tvNow.TabItems[0];
                    // The first remaining tab in the post-tear-off
                    // group is 'b'.
                    DockTabTearOff.SimulatePressForTest(tvNow, itemNext, b, 0);
                    DockTabTearOff.SimulateMoveForTest(tvNow, 5.0, 5.0);
                    await Harness.Render();
                }
                H.Check("T07_SecondDragStarted_TrackerDifferent",
                    DockTabTearOffTracker.IsActive
                    && !ReferenceEquals(DockTabTearOffTracker.ActiveForTest, firstActive));
                H.Check("T07_FirstFloatingDragStylesStripped",
                    firstFloating is not null && firstFloating.Spec.Opacity >= 0.99);

                secondFloating = DockTabTearOffTracker.ActiveForTest?.FloatingWindow;
                DockTabTearOffTracker.SimulateReleaseForTest(commit: false);
                await Harness.Render();
                host.Mount(_ => TextBlock("done"));
                await Harness.Render();
            }
            finally
            {
                if (firstFloating is not null) DockFloatingPaneRouter.Unregister(firstFloating);
                if (secondFloating is not null) DockFloatingPaneRouter.Unregister(secondFloating);
                firstFloating?.Close();
                secondFloating?.Close();
                ReactorApp.ShutdownPolicy = savedPolicy;
                ResetAll();
            }
        }
    }

    // ─── #8 DropOutside_RetainsFloatingWindow ──────────────────────────────

    /// <summary>After a tear-off, a "release outside any target" (no
    /// overlay had a latched hover) leaves the floating window in place
    /// with its drag styles stripped (opacity → 1.0). The session ends.
    /// </summary>
    internal class T08_DropOutside_RetainsFloating_EndsSession(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ResetAll(); PrepDeterministic();
            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorWindow? floatingWindow = null;
            try
            {
                var host = H.CreateHost();
                DockingNativeInterop.Register(host.Reconciler);
                var a = MakePane("a", "body-a");
                var b = MakePane("b", "body-b");
                host.Mount(_ => new DockManager { Layout = new DockTabGroup(new[] { a, b }) });
                await Harness.Render();
                var tv = FirstTabView(H);
                if (tv is null) { H.Check("T08_TabViewFound", false); return; }

                var item0 = (TabViewItem)tv.TabItems[0];
                DockTabTearOff.SimulatePressForTest(tv, item0, a, 0);
                DockTabTearOff.SimulateMoveForTest(tv, 5.0, 5.0);
                await Harness.Render();
                var active = DockTabTearOffTracker.ActiveForTest;
                floatingWindow = active?.FloatingWindow;
                H.Check("T08_TrackerHasActive", active is not null);

                // No overlay has a hovered target — release-without-target
                // → drop-outside semantics. Window stays at opacity 1.0.
                DockTabTearOffTracker.SimulateReleaseForTest(commit: true);
                await Harness.Render();

                H.Check("T08_TrackerCleared", !DockTabTearOffTracker.IsActiveForTest);
                H.Check("T08_SessionEnded",
                    DockDragSession.Current is null or { IsActive: false });
                H.Check("T08_FloatingWindowStillOpen",
                    floatingWindow is not null && !IsClosed(floatingWindow));
                H.Check("T08_FloatingWindowOpacityRestored",
                    floatingWindow is not null && floatingWindow.Spec.Opacity >= 0.99);

                host.Mount(_ => TextBlock("done"));
                await Harness.Render();
            }
            finally
            {
                if (floatingWindow is not null) DockFloatingPaneRouter.Unregister(floatingWindow);
                floatingWindow?.Close();
                ReactorApp.ShutdownPolicy = savedPolicy;
                ResetAll();
            }
        }
    }

    // ─── #8b ImmediateTearOff_DropOnHostTarget_ReportsMigratedToHost ───────

    /// <summary>Producer-path coverage for issue #417 (review H1), the
    /// DOCKED-source immediate tear-off variant. A docked tab is torn into a
    /// preview window then dropped back on a host target — the seam is
    /// <see cref="DockHostNativeComponent"/>'s <c>FinalizeImmediateDrop</c>
    /// confirmedTarget branch, which used to bare-<c>Close()</c> the preview
    /// with no stash. After the fix the auto-close must report
    /// <see cref="DockFloatingCloseReason.MigratedToHost"/> + the pane,
    /// because the pane is alive again in the docked tree. Symmetric with
    /// T08 (drop-outside) but latching a host hover so the release confirms a
    /// target instead of falling through to drop-outside.</summary>
    internal class T08b_ImmediateTearOff_DropOnHostTarget_ReportsMigratedToHost(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ResetAll(); PrepDeterministic();
            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorWindow? floatingWindow = null;
            // The migration path (StashReasonAndClose) auto-closes the preview
            // window. Guard the finally so we don't Close() it a second time —
            // double-closing a WinUI window risks the multi-window teardown AV.
            var floatingWindowClosed = false;
            var closed = new List<(DockFloatingCloseReason Reason, DockableContent? Content)>();
            try
            {
                var host = H.CreateHost();
                DockingNativeInterop.Register(host.Reconciler);
                var a = MakePane("a", "body-a");
                var b = MakePane("b", "body-b");
                host.Mount(_ => new DockManager
                {
                    Layout = new DockTabGroup(new[] { a, b }),
                    OnFloatingWindowClosed = args => closed.Add((args.Reason, args.Content)),
                });
                await Harness.Render();
                var tv = FirstTabView(H);
                if (tv is null) { H.Check("T08b_TabViewFound", false); return; }

                var item0 = (TabViewItem)tv.TabItems[0];
                DockTabTearOff.SimulatePressForTest(tv, item0, a, 0);
                DockTabTearOff.SimulateMoveForTest(tv, 5.0, 5.0);
                await Harness.Render();
                floatingWindow = DockTabTearOffTracker.ActiveForTest?.FloatingWindow;
                if (floatingWindow is not null)
                    floatingWindow.Closed += (_, _) => floatingWindowClosed = true;
                H.Check("T08b_TrackerHasActive", DockTabTearOffTracker.IsActive);

                // Latch a host target as hovered, then release: the single
                // FinalizeImmediateDrop closure docks the pane back AND
                // stash-then-closes the preview (the H1 seam).
                var hostOverlay = H.FindAllControls<DockDropTargetOverlayControl>(
                    o => o.Visibility == Microsoft.UI.Xaml.Visibility.Visible).FirstOrDefault()
                    ?? H.FindAllControls<DockDropTargetOverlayControl>(_ => true).FirstOrDefault();
                H.Check("T08b_HostOverlayFound", hostOverlay is not null);
                hostOverlay?.SetHoveredForTest(DockTarget.SplitRight);

                DockTabTearOffTracker.SimulateReleaseForTest(commit: true);
                await Harness.Render();
                await Harness.Render();

                H.Check("T08b_AtLeastOneCloseFired", closed.Count >= 1);
                H.Check("T08b_ReportsMigratedToHost",
                    closed.Any(c => c.Reason == DockFloatingCloseReason.MigratedToHost));
                H.Check("T08b_NoCloseReportsContentClosed",
                    closed.All(c => c.Reason != DockFloatingCloseReason.ContentClosed));
                H.Check("T08b_MigratedCloseCarriesPaneA",
                    closed.Any(c => c.Reason == DockFloatingCloseReason.MigratedToHost
                                    && ReferenceEquals(c.Content, a)));

                host.Mount(_ => TextBlock("done"));
                await Harness.Render();
            }
            finally
            {
                if (floatingWindow is not null) DockFloatingPaneRouter.Unregister(floatingWindow);
                if (floatingWindow is not null && !floatingWindowClosed) floatingWindow.Close();
                ReactorApp.ShutdownPolicy = savedPolicy;
                ResetAll();
            }
        }
    }

    // ─── #9 EscCancel_BehavesLikeDropOutside ───────────────────────────────

    /// <summary>Esc cancellation (commit=false to SimulateReleaseForTest)
    /// today behaves the same as drop-outside — the floating window
    /// stays at the cursor's last position with 1.0 opacity. Recorded
    /// as the current contract so a future "Esc restores to source"
    /// change becomes a visible diff in this fixture.</summary>
    internal class T09_EscCancel_BehavesLikeDropOutside(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ResetAll(); PrepDeterministic();
            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorWindow? floatingWindow = null;
            try
            {
                var host = H.CreateHost();
                DockingNativeInterop.Register(host.Reconciler);
                var a = MakePane("a", "body-a");
                var b = MakePane("b", "body-b");
                host.Mount(_ => new DockManager { Layout = new DockTabGroup(new[] { a, b }) });
                await Harness.Render();
                var tv = FirstTabView(H);
                if (tv is null) { H.Check("T09_TabViewFound", false); return; }

                var item0 = (TabViewItem)tv.TabItems[0];
                DockTabTearOff.SimulatePressForTest(tv, item0, a, 0);
                DockTabTearOff.SimulateMoveForTest(tv, 5.0, 5.0);
                await Harness.Render();
                var active = DockTabTearOffTracker.ActiveForTest;
                floatingWindow = active?.FloatingWindow;

                DockTabTearOffTracker.SimulateReleaseForTest(commit: false);
                await Harness.Render();

                H.Check("T09_TrackerCleared", !DockTabTearOffTracker.IsActiveForTest);
                H.Check("T09_SessionEnded",
                    DockDragSession.Current is null or { IsActive: false });
                H.Check("T09_FloatingWindowStillOpen",
                    floatingWindow is not null && !IsClosed(floatingWindow));

                host.Mount(_ => TextBlock("done"));
                await Harness.Render();
            }
            finally
            {
                if (floatingWindow is not null) DockFloatingPaneRouter.Unregister(floatingWindow);
                floatingWindow?.Close();
                ReactorApp.ShutdownPolicy = savedPolicy;
                ResetAll();
            }
        }
    }

    // ─── #10 HostUnmount_StopsTracker ──────────────────────────────────────

    /// <summary>If the host unmounts mid-drag (e.g. scene switch), the
    /// session is cancelled AND the tear-off tracker is stopped so the
    /// orphaned floating window doesn't keep tracking a cursor that no
    /// longer has a source.</summary>
    internal class T10_HostUnmount_StopsTracker(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ResetAll(); PrepDeterministic();
            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorWindow? floatingWindow = null;
            try
            {
                var host = H.CreateHost();
                DockingNativeInterop.Register(host.Reconciler);
                var a = MakePane("a", "body-a");
                var b = MakePane("b", "body-b");
                host.Mount(_ => new DockManager { Layout = new DockTabGroup(new[] { a, b }) });
                await Harness.Render();
                var tv = FirstTabView(H);
                if (tv is null) { H.Check("T10_TabViewFound", false); return; }

                var item0 = (TabViewItem)tv.TabItems[0];
                DockTabTearOff.SimulatePressForTest(tv, item0, a, 0);
                DockTabTearOff.SimulateMoveForTest(tv, 5.0, 5.0);
                await Harness.Render();
                H.Check("T10_TrackerActiveBeforeUnmount",
                    DockTabTearOffTracker.IsActiveForTest);
                var active = DockTabTearOffTracker.ActiveForTest;
                floatingWindow = active?.FloatingWindow;

                // Swap the entire mounted root out — the DockHostNativeComponent
                // unmounts and its UseEffect cleanup should fire.
                host.Mount(_ => TextBlock("after-unmount"));
                await Harness.Render();

                H.Check("T10_TrackerClearedAfterUnmount",
                    !DockTabTearOffTracker.IsActiveForTest);
                H.Check("T10_SessionCancelledAfterUnmount",
                    DockDragSession.Current is null or { IsActive: false });
            }
            finally
            {
                if (floatingWindow is not null) DockFloatingPaneRouter.Unregister(floatingWindow);
                floatingWindow?.Close();
                ReactorApp.ShutdownPolicy = savedPolicy;
                ResetAll();
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Floating-window source pipeline (BeginFloatingTearOff) coverage.
    //
    //  These fixtures cover the float→host drag-back path that was
    //  broken because (a) the floating window's tab strip wasn't
    //  routed through the tear-off pipeline at all, and (b) the
    //  source floating window stayed at the top of the OS Z-order
    //  intercepting pointer events while its async Close() ran. The
    //  current implementation routes floating-tab drags through
    //  BeginFloatingTearOff, opens a fresh preview window with the
    //  drag styles in its WindowSpec, and Hide()s the source window
    //  immediately so it can't keep absorbing hit-tests.
    //
    //  Findings these fixtures encode:
    //   • Op log proof: dock→host drags fire Overlay.PointerEntered
    //     within ~200 ms; float→host drags fired ZERO Overlay events
    //     across multi-second drags. Root cause: source floating
    //     window in front of the new preview in Z-order.
    //   • SetWindowLong frame-style changes are cached until
    //     SetWindowPos with SWP_FRAMECHANGED runs — covered by the
    //     fact that the new preview opens fresh with styles in spec.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Walks the visual tree under a ReactorWindow's content to
    /// find the first mounted TabView. Floating windows render their
    /// content in their own XAML island; the main host's
    /// <see cref="Harness.FindAllControls{T}"/> only sees the main
    /// window's tree, so we route through the floating's NativeWindow
    /// directly.</summary>
    private static TabView? FindTabViewInWindow(ReactorWindow? window)
    {
        var content = window?.NativeWindow?.Content;
        if (content is null) return null;
        var stack = new Stack<global::Microsoft.UI.Xaml.DependencyObject>();
        stack.Push(content);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (cur is TabView tv) return tv;
            var n = global::Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(cur);
            for (int i = 0; i < n; i++)
                stack.Push(global::Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(cur, i));
        }
        return null;
    }

    // ─── #11 FloatingPress_HidesSource_AndStartsTracker ────────────────────

    /// <summary>The floating-window tear-off pipeline: pressing a tab
    /// on a floating window must (a) open a NEW preview window with
    /// drag styles, (b) <b>hide</b> the source floating window so it
    /// stops absorbing pointer events. Without (b) the source
    /// floating window stays in front of the preview in the OS
    /// Z-order (it had foreground focus), so WS_EX_TRANSPARENT on
    /// the preview can't deliver events to the host's overlays —
    /// the float→host drag goes silent for the entire gesture.</summary>
    internal class T11_FloatingTabPress_HidesSource_AndStartsTracker(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ResetAll(); PrepDeterministic();
            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorWindow? floating = null;
            ReactorWindow? previewWindow = null;
            try
            {
                var host = H.CreateHost();
                DockingNativeInterop.Register(host.Reconciler);
                var a = MakePane("a", "body-a");
                var b = MakePane("b", "body-b");
                var managerEl = new DockManager { Layout = new DockTabGroup(new[] { a, b }) };
                host.Mount(_ => managerEl);
                await Harness.Render();

                floating = DockFloatingWindow.Open(a, manager: managerEl);
                // Give the floating window two renders to fully mount
                // its component tree (the TabView press hook attaches
                // during the renderer pass).
                await Harness.Render();
                await Harness.Render();

                var floatingTabView = FindTabViewInWindow(floating);
                H.Check("T11_FloatingTabViewFound", floatingTabView is not null);
                if (floatingTabView is null) return;
                H.Check("T11_PressHookAttached",
                    DockTabTearOff.IsHookAttachedForTest(floatingTabView));

                var item = floatingTabView.TabItems.OfType<TabViewItem>().FirstOrDefault();
                if (item is null) { H.Check("T11_TabViewItemFound", false); return; }

                DockTabTearOff.SimulatePressForTest(floatingTabView, item, a, tabIndex: 0);
                DockTabTearOff.SimulateMoveForTest(floatingTabView, 5.0, 5.0);
                await Harness.Render();

                var active = DockTabTearOffTracker.ActiveForTest;
                previewWindow = active?.FloatingWindow;
                H.Check("T11_TrackerActive", DockTabTearOffTracker.IsActive);
                H.Check("T11_PreviewIsNewWindow",
                    previewWindow is not null
                    && !ReferenceEquals(previewWindow, floating));
                H.Check("T11_SourceFloatingHidden", !floating.IsVisible);
                H.Check("T11_SessionActiveForPane",
                    DockDragSession.Current is { IsActive: true } s
                    && ReferenceEquals(s.Source, a));

                DockTabTearOffTracker.SimulateReleaseForTest(commit: false);
                await Harness.Render();
                host.Mount(_ => TextBlock("done"));
                await Harness.Render();
            }
            finally
            {
                if (previewWindow is not null) DockFloatingPaneRouter.Unregister(previewWindow);
                if (floating is not null) DockFloatingPaneRouter.Unregister(floating);
                previewWindow?.Close();
                floating?.Close();
                ReactorApp.ShutdownPolicy = savedPolicy;
                ResetAll();
            }
        }
    }

    // ─── #12 FloatingTearOff_DropOnHostTarget_DocksToHostAndClosesPreview ──

    /// <summary>End-to-end float→host: pane in a floating window,
    /// dragged back, dropped on a host's per-group target. Expected:
    /// (a) preview window closes, (b) the source pane is back in the
    /// host's layout at the target slot, (c) session ends. The fixture
    /// drives the overlay confirm directly via the existing
    /// <see cref="DockDropTargetOverlayControl.ConfirmTargetForTest"/>
    /// hook (the same path the real pipeline's
    /// <c>TryConfirmHoveredTargetFor</c> takes once the cursor is
    /// over a button).</summary>
    internal class T12_FloatingTearOff_DropOnHostTarget_DocksToHost(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ResetAll(); PrepDeterministic();
            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorWindow? floating = null;
            ReactorWindow? previewWindow = null;
            try
            {
                var host = H.CreateHost();
                DockingNativeInterop.Register(host.Reconciler);
                var a = MakePane("a", "body-a");
                var b = MakePane("b", "body-b");
                var managerEl = new DockManager { Layout = new DockTabGroup(new[] { b }) };
                host.Mount(_ => managerEl);
                await Harness.Render();

                floating = DockFloatingWindow.Open(a, manager: managerEl);
                await Harness.Render();
                await Harness.Render();

                var floatingTabView = FindTabViewInWindow(floating);
                if (floatingTabView is null) { H.Check("T12_FloatingTabViewFound", false); return; }
                var item = floatingTabView.TabItems.OfType<TabViewItem>().FirstOrDefault();
                if (item is null) { H.Check("T12_TabItemFound", false); return; }

                DockTabTearOff.SimulatePressForTest(floatingTabView, item, a, 0);
                DockTabTearOff.SimulateMoveForTest(floatingTabView, 5.0, 5.0);
                await Harness.Render();

                var active = DockTabTearOffTracker.ActiveForTest;
                previewWindow = active?.FloatingWindow;
                H.Check("T12_TrackerActive", DockTabTearOffTracker.IsActive);

                // Trigger the host's per-group overlay confirm — same
                // event path the production finalize uses.
                var hostOverlay = H.FindAllControls<DockDropTargetOverlayControl>(_ => true).FirstOrDefault();
                H.Check("T12_HostOverlayFound", hostOverlay is not null);
                hostOverlay?.ConfirmTargetForTest(DockTarget.SplitRight);
                await Harness.Render();

                // After the confirm the host's OnConfirm closure ran:
                // cross-window insert (pane not in this layout → falls
                // through InsertPaneRelativeToGroup). Pane 'a' is now
                // in the host's layout to the right of group 'b'.
                var tabs = H.FindAllControls<TabView>(_ => true).ToList();
                var allHeaders = tabs.SelectMany(t => t.TabItems.OfType<TabViewItem>()
                    .Select(ti => ti.Header as string)).ToList();
                H.Check("T12_HostNowContainsA", allHeaders.Contains("a"));
                H.Check("T12_HostStillContainsB", allHeaders.Contains("b"));
                H.Check("T12_SessionEnded",
                    DockDragSession.Current is null or { IsActive: false });

                // Drive the finalize path so the preview window closes.
                // (In production this fires from the cursor-poll
                // detecting LBUTTON-up; the test bypasses the timer.)
                DockTabTearOffTracker.SimulateReleaseForTest(commit: true);
                await Harness.Render();
                await Harness.Render();

                host.Mount(_ => TextBlock("done"));
                await Harness.Render();
            }
            finally
            {
                if (previewWindow is not null) DockFloatingPaneRouter.Unregister(previewWindow);
                if (floating is not null) DockFloatingPaneRouter.Unregister(floating);
                previewWindow?.Close();
                floating?.Close();
                ReactorApp.ShutdownPolicy = savedPolicy;
                ResetAll();
            }
        }
    }

    // ─── #12b FloatingTearOff_DropOnHostTarget_ReportsMigratedToHost ───────

    /// <summary>Producer-path coverage for issue #417 (review H1): the
    /// SAME float→host dock-back T12 drives, but asserting the
    /// <see cref="DockManager.OnFloatingWindowClosed"/> REASON. When the
    /// torn-off preview window auto-closes after its pane was reinserted
    /// into the host, the event must report
    /// <see cref="DockFloatingCloseReason.MigratedToHost"/> — NOT the
    /// default <see cref="DockFloatingCloseReason.ContentClosed"/>, which
    /// would tell a consumer to release resources for a pane that is still
    /// alive in the docked tree. The genuine bug this guards: the immediate
    /// tear-off finalize used to call a bare <c>Close()</c> with no stash.
    /// Every close that fires in this sequence is a migration (source
    /// emptied into the preview = MigratedToFloat; preview docked back =
    /// MigratedToHost), so NONE may report ContentClosed.</summary>
    internal class T12b_FloatingTearOff_DropOnHostTarget_ReportsMigratedToHost(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ResetAll(); PrepDeterministic();
            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorWindow? floating = null;
            ReactorWindow? previewWindow = null;
            // Both the source float (emptied by the migration) and the preview
            // window auto-close via the migration path. Guard the finally so we
            // don't Close() either a second time — double-closing a WinUI window
            // risks the multi-window teardown AV.
            var floatingClosed = false;
            var previewClosed = false;
            var closed = new List<(DockFloatingCloseReason Reason, DockableContent? Content)>();
            try
            {
                var host = H.CreateHost();
                DockingNativeInterop.Register(host.Reconciler);
                var a = MakePane("a", "body-a");
                var b = MakePane("b", "body-b");
                var managerEl = new DockManager
                {
                    Layout = new DockTabGroup(new[] { b }),
                    OnFloatingWindowClosed = args => closed.Add((args.Reason, args.Content)),
                };
                host.Mount(_ => managerEl);
                await Harness.Render();

                floating = DockFloatingWindow.Open(a, manager: managerEl);
                floating.Closed += (_, _) => floatingClosed = true;
                await Harness.Render();
                await Harness.Render();

                var floatingTabView = FindTabViewInWindow(floating);
                if (floatingTabView is null) { H.Check("T12b_FloatingTabViewFound", false); return; }
                var item = floatingTabView.TabItems.OfType<TabViewItem>().FirstOrDefault();
                if (item is null) { H.Check("T12b_TabItemFound", false); return; }

                DockTabTearOff.SimulatePressForTest(floatingTabView, item, a, 0);
                DockTabTearOff.SimulateMoveForTest(floatingTabView, 5.0, 5.0);
                await Harness.Render();

                previewWindow = DockTabTearOffTracker.ActiveForTest?.FloatingWindow;
                if (previewWindow is not null)
                    previewWindow.Closed += (_, _) => previewClosed = true;
                H.Check("T12b_TrackerActive", DockTabTearOffTracker.IsActive);

                // Latch a host target as hovered (the production cursor would
                // be over its button), then drive the release. Unlike T12,
                // we do NOT pre-confirm via ConfirmTargetForTest — we want the
                // SINGLE finalize/confirm closure (the seam H1 patched) to do
                // BOTH the host re-insert AND the stash-then-close, exactly as
                // production does on mouse-up.
                var hostOverlay = H.FindAllControls<DockDropTargetOverlayControl>(
                    o => o.Visibility == Microsoft.UI.Xaml.Visibility.Visible).FirstOrDefault()
                    ?? H.FindAllControls<DockDropTargetOverlayControl>(_ => true).FirstOrDefault();
                H.Check("T12b_HostOverlayFound", hostOverlay is not null);
                hostOverlay?.SetHoveredForTest(DockTarget.SplitRight);

                // Drive the finalize path → confirm closure docks the pane
                // back into the host AND auto-closes the preview window. This
                // is the seam that must stash MigratedToHost.
                DockTabTearOffTracker.SimulateReleaseForTest(commit: true);
                await Harness.Render();
                await Harness.Render();

                // The pane is back in the host (sanity).
                var allHeaders = H.FindAllControls<TabView>(_ => true)
                    .SelectMany(t => t.TabItems.OfType<TabViewItem>().Select(ti => ti.Header as string))
                    .ToList();
                H.Check("T12b_HostNowContainsA", allHeaders.Contains("a"));

                // The key #417 assertions: a MigratedToHost close fired, and
                // NOTHING in this all-migration sequence reported ContentClosed.
                H.Check("T12b_AtLeastOneCloseFired", closed.Count >= 1);
                H.Check("T12b_ReportsMigratedToHost",
                    closed.Any(c => c.Reason == DockFloatingCloseReason.MigratedToHost));
                H.Check("T12b_NoCloseReportsContentClosed",
                    closed.All(c => c.Reason != DockFloatingCloseReason.ContentClosed));
                H.Check("T12b_MigratedCloseCarriesPaneA",
                    closed.Any(c => c.Reason == DockFloatingCloseReason.MigratedToHost
                                    && ReferenceEquals(c.Content, a)));

                // M2 — the source float (its only tab 'a' torn into the
                // preview) empties via RemoveLocal(MigratedToFloat). Assert
                // that leg explicitly so BOTH migration reasons are pinned,
                // not just the preview→host one.
                H.Check("T12b_ReportsMigratedToFloat",
                    closed.Any(c => c.Reason == DockFloatingCloseReason.MigratedToFloat));
                H.Check("T12b_MigratedToFloatCloseCarriesPaneA",
                    closed.Any(c => c.Reason == DockFloatingCloseReason.MigratedToFloat
                                    && ReferenceEquals(c.Content, a)));
                // Exactly the two migration closes (source emptied into the
                // preview, then preview docked back) — nothing spurious, and
                // in particular no ContentClosed.
                H.Check("T12b_ExactlyTwoMigrationCloses", closed.Count == 2);

                host.Mount(_ => TextBlock("done"));
                await Harness.Render();
            }
            finally
            {
                if (previewWindow is not null) DockFloatingPaneRouter.Unregister(previewWindow);
                if (floating is not null) DockFloatingPaneRouter.Unregister(floating);
                if (previewWindow is not null && !previewClosed) previewWindow.Close();
                if (floating is not null && !floatingClosed) floating.Close();
                ReactorApp.ShutdownPolicy = savedPolicy;
                ResetAll();
            }
        }
    }

    // ─── #12c FloatingMultiPane_CloseLastTab_AfterSiblingLeft_ReportsClosingPane ──

    /// <summary>Producer-path coverage for issue #417 (review H1): the
    /// multi-pane "stale Content" hazard. A float is opened with pane A — so
    /// A becomes the Open()-time fallback baked into the <c>window.Closed</c>
    /// closure. A second pane B is docked in (→ <c>[A,B]</c>); then A leaves
    /// the float (a sibling close), leaving <c>[B]</c>; finally B — the
    /// genuine last tab — is closed. <see cref="DockManager.OnFloatingWindowClosed"/>
    /// MUST report <c>Content = B</c> (the real closing pane) with
    /// <see cref="DockFloatingCloseReason.ContentClosed"/> — NOT the stale
    /// Open()-time pane A. Before the H1 fix the willEmpty branch skipped the
    /// stash for ContentClosed, so the handler fell back to A (still alive
    /// elsewhere) and a consumer doing
    /// <c>if (Reason == ContentClosed) Release(Content)</c> would release the
    /// wrong, still-live pane.
    ///
    /// Single real window, no tear-off (so no preview-window teardown): B is
    /// appended via the router's test seam (deterministic, no OS-cursor
    /// dependency) and the tab closes are driven through the realized
    /// TabView's wired <c>OnTabCloseRequested</c> — the SAME element action
    /// the production close-button trampoline invokes — so this exercises the
    /// real onTabClosing → RemoveLocal → willEmpty path end-to-end.</summary>
    internal class T12c_FloatingMultiPane_CloseLastTab_AfterSiblingLeft_ReportsClosingPane(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ResetAll(); PrepDeterministic();
            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorWindow? floating = null;
            // The genuine last-tab close auto-closes the float. Guard the
            // finally so we don't Close() it a second time — double-closing a
            // WinUI window risks the multi-window teardown AV.
            var floatingClosed = false;
            var closed = new List<(DockFloatingCloseReason Reason, DockableContent? Content)>();
            try
            {
                var host = H.CreateHost();
                DockingNativeInterop.Register(host.Reconciler);
                var a = MakePane("a", "body-a");
                var b = MakePane("b", "body-b");
                var managerEl = new DockManager
                {
                    Layout = new DockTabGroup(new[] { MakePane("host", "body-host") }),
                    OnFloatingWindowClosed = args => closed.Add((args.Reason, args.Content)),
                };
                host.Mount(_ => managerEl);
                await Harness.Render();

                // Open a float with A — A is the Open()-time fallback captured
                // by the window.Closed closure.
                floating = DockFloatingWindow.Open(a, manager: managerEl);
                floating.Closed += (_, _) => floatingClosed = true;
                // Two renders so the component mounts and registers its
                // append-as-tab callback with the router.
                await Harness.Render();
                await Harness.Render();

                // Dock B in via the router seam → a REAL [A,B] multi-pane float.
                var appended = DockFloatingPaneRouter.AppendToWindowForTest(floating, b);
                H.Check("T12c_AppendedB", appended);
                await Harness.Render();

                var tabView = FindTabViewInWindow(floating);
                if (tabView is null) { H.Check("T12c_TabViewFound", false); return; }
                H.Check("T12c_FloatHasTwoTabs", tabView.TabItems.Count == 2);

                // Close A (the Open()-time pane) — a non-last close, so the
                // window stays open with [B]. Drives the real onTabClosing via
                // the element's wired close action.
                var elBeforeRemove = Reconciler.GetElementTag(tabView) as TabViewElement;
                int idxA = IndexOfHeader(tabView, "a");
                H.Check("T12c_FoundAIndex", idxA >= 0);
                elBeforeRemove?.OnTabCloseRequested?.Invoke(idxA);
                await Harness.Render();
                await Harness.Render();

                var tabViewAfter = FindTabViewInWindow(floating);
                H.Check("T12c_FloatStillOpenWithOneTab",
                    tabViewAfter is not null && tabViewAfter.TabItems.Count == 1);
                H.Check("T12c_NoCloseFiredYet", closed.Count == 0);

                // Close B — now the genuine last tab → willEmpty → ContentClosed.
                // The event must carry B (the real closing pane), not stale A.
                var elNow = Reconciler.GetElementTag(tabViewAfter!) as TabViewElement;
                int idxB = IndexOfHeader(tabViewAfter!, "b");
                H.Check("T12c_FoundBIndex", idxB >= 0);
                elNow?.OnTabCloseRequested?.Invoke(idxB);
                await Harness.Render();
                await Harness.Render();

                H.Check("T12c_ExactlyOneCloseFired", closed.Count == 1);
                H.Check("T12c_ReasonIsContentClosed",
                    closed.Count == 1 && closed[0].Reason == DockFloatingCloseReason.ContentClosed);
                H.Check("T12c_ContentIsClosingPaneB",
                    closed.Count == 1 && ReferenceEquals(closed[0].Content, b));
                H.Check("T12c_ContentIsNotStaleOpenTimePaneA",
                    closed.Count == 1 && !ReferenceEquals(closed[0].Content, a));

                host.Mount(_ => TextBlock("done"));
                await Harness.Render();
            }
            finally
            {
                if (floating is not null) DockFloatingPaneRouter.Unregister(floating);
                if (floating is not null && !floatingClosed) floating.Close();
                ReactorApp.ShutdownPolicy = savedPolicy;
                ResetAll();
            }
        }

        // Headers are the pane Titles (DockTabGroupRenderer maps doc.Title →
        // tab Header, and tab order tracks the pane/documents order one-to-one,
        // so the returned index is the documents[] index OnTabCloseRequested
        // expects).
        private static int IndexOfHeader(TabView tv, string header)
        {
            for (int i = 0; i < tv.TabItems.Count; i++)
                if (tv.TabItems[i] is TabViewItem it && (it.Header as string) == header)
                    return i;
            return -1;
        }
    }

    // ─── #13 FloatingTearOff_DropOutside_RetainsPreview ────────────────────

    /// <summary>Float→outside: pane in a floating window, dragged, no
    /// target hovered. The PREVIEW window stays open at the cursor's
    /// release position with its drag styles stripped (Opacity=1, no
    /// click-through, no NoActivate). Symmetric with T08 but starting
    /// from a floating source instead of a docked one.</summary>
    internal class T13_FloatingTearOff_DropOutside_RetainsPreview(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ResetAll(); PrepDeterministic();
            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorWindow? floating = null;
            ReactorWindow? previewWindow = null;
            try
            {
                var host = H.CreateHost();
                DockingNativeInterop.Register(host.Reconciler);
                var a = MakePane("a", "body-a");
                var managerEl = new DockManager { Layout = new DockTabGroup(new DockableContent[] { }) };
                host.Mount(_ => managerEl);
                await Harness.Render();

                floating = DockFloatingWindow.Open(a, manager: managerEl);
                await Harness.Render();
                await Harness.Render();

                var floatingTabView = FindTabViewInWindow(floating);
                if (floatingTabView is null) { H.Check("T13_FloatingTabViewFound", false); return; }
                var item = floatingTabView.TabItems.OfType<TabViewItem>().FirstOrDefault();
                if (item is null) { H.Check("T13_TabItemFound", false); return; }

                DockTabTearOff.SimulatePressForTest(floatingTabView, item, a, 0);
                DockTabTearOff.SimulateMoveForTest(floatingTabView, 5.0, 5.0);
                await Harness.Render();

                var active = DockTabTearOffTracker.ActiveForTest;
                previewWindow = active?.FloatingWindow;
                H.Check("T13_TrackerActiveBeforeRelease", DockTabTearOffTracker.IsActive);

                // No host overlay confirmed → drop-outside.
                DockTabTearOffTracker.SimulateReleaseForTest(commit: true);
                await Harness.Render();

                H.Check("T13_TrackerCleared", !DockTabTearOffTracker.IsActive);
                H.Check("T13_SessionEnded",
                    DockDragSession.Current is null or { IsActive: false });
                H.Check("T13_PreviewWindowStillOpen",
                    previewWindow is not null && !IsClosed(previewWindow));
                H.Check("T13_PreviewOpacityRestored",
                    previewWindow is not null && previewWindow.Spec.Opacity >= 0.99);
                H.Check("T13_PreviewNotIgnoringPointer",
                    previewWindow is not null && !previewWindow.Spec.IgnorePointerInput);
                H.Check("T13_PreviewNoActivateCleared",
                    previewWindow is not null && !previewWindow.Spec.NoActivate);

                host.Mount(_ => TextBlock("done"));
                await Harness.Render();
            }
            finally
            {
                if (previewWindow is not null) DockFloatingPaneRouter.Unregister(previewWindow);
                if (floating is not null) DockFloatingPaneRouter.Unregister(floating);
                previewWindow?.Close();
                floating?.Close();
                ReactorApp.ShutdownPolicy = savedPolicy;
                ResetAll();
            }
        }
    }

    // ─── #14 FloatingTearOff_DefensiveTrackerCleanup ───────────────────────

    /// <summary>If a previous drag left the tracker in a non-Stop'd
    /// state, the floating-window tear-off's defensive ForceCancel +
    /// session cancel must clear it BEFORE the new tear-off runs.
    /// Otherwise the new BeginFloatingTearOff would refuse (because
    /// DockDragSession.Current is still IsActive). Mirrors T07 for
    /// the host path but exercised from the floating-source code.</summary>
    internal class T14_FloatingTearOff_StaleTrackerForceCleaned(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ResetAll(); PrepDeterministic();
            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorWindow? floating = null;
            ReactorWindow? firstPreview = null;
            ReactorWindow? secondPreview = null;
            try
            {
                var host = H.CreateHost();
                DockingNativeInterop.Register(host.Reconciler);
                var a = MakePane("a", "body-a");
                var managerEl = new DockManager { Layout = new DockTabGroup(new[] { a }) };
                host.Mount(_ => managerEl);
                await Harness.Render();

                floating = DockFloatingWindow.Open(a, manager: managerEl);
                await Harness.Render();
                await Harness.Render();

                var floatingTabView = FindTabViewInWindow(floating);
                if (floatingTabView is null) { H.Check("T14_FloatingTabViewFound", false); return; }
                var item = floatingTabView.TabItems.OfType<TabViewItem>().FirstOrDefault();
                if (item is null) { H.Check("T14_TabItemFound", false); return; }

                // First tear-off (leaves tracker active intentionally).
                DockTabTearOff.SimulatePressForTest(floatingTabView, item, a, 0);
                DockTabTearOff.SimulateMoveForTest(floatingTabView, 5.0, 5.0);
                await Harness.Render();
                firstPreview = DockTabTearOffTracker.ActiveForTest?.FloatingWindow;
                H.Check("T14_FirstTearOffActive", DockTabTearOffTracker.IsActive);

                // Now try a SECOND tear-off without releasing first.
                // The new preview attaches to the SAME floating
                // window's TabView (the press hook is still wired —
                // even though Hide() was called, the WinUI element
                // tree is intact). The defensive ForceCancel in
                // BeginFloatingTearOff should clear the stale tracker
                // and the second drag should succeed.
                DockTabTearOff.SimulatePressForTest(floatingTabView, item, a, 0);
                DockTabTearOff.SimulateMoveForTest(floatingTabView, 5.0, 5.0);
                await Harness.Render();

                secondPreview = DockTabTearOffTracker.ActiveForTest?.FloatingWindow;
                H.Check("T14_SecondTearOffActive", DockTabTearOffTracker.IsActive);
                H.Check("T14_TrackerActiveIsNew",
                    secondPreview is not null
                    && !ReferenceEquals(secondPreview, firstPreview));
                H.Check("T14_FirstPreviewStylesRestored",
                    firstPreview is not null && firstPreview.Spec.Opacity >= 0.99);

                DockTabTearOffTracker.SimulateReleaseForTest(commit: false);
                await Harness.Render();
                host.Mount(_ => TextBlock("done"));
                await Harness.Render();
            }
            finally
            {
                if (firstPreview is not null) DockFloatingPaneRouter.Unregister(firstPreview);
                if (secondPreview is not null) DockFloatingPaneRouter.Unregister(secondPreview);
                if (floating is not null) DockFloatingPaneRouter.Unregister(floating);
                firstPreview?.Close();
                secondPreview?.Close();
                floating?.Close();
                ReactorApp.ShutdownPolicy = savedPolicy;
                ResetAll();
            }
        }
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static bool IsClosed(ReactorWindow w)
    {
        try { _ = w.Spec; return false; }
        catch { return true; }
    }
}
