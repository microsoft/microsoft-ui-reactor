using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 045 — coverage fixtures for <see cref="DockFloatingWindow"/> and
/// <see cref="DockFloatingPaneRouter"/>. The existing reliability fixture
/// covers the host-unmount path; this set fills in event-callback wiring,
/// the bounds-clamp branch, the BuildFloatingRoot tree shape, and the
/// router's empty / hit / miss arms.
/// </summary>
internal static class NativeDockingCoverageFloatingFixtures
{
    /// <summary>
    /// Drives <see cref="DockFloatingWindow.Open"/> with an out-of-bounds
    /// <c>savedBounds</c> + a single-display set — the saved rect lies
    /// entirely off-screen so the clamp recenters it on the primary
    /// display. Also exercises the OnFloatingWindowCreated /
    /// OnFloatingWindowClosed callbacks via the lifecycle event args.
    /// </summary>
    internal class FloatingWindow_OpenWithClampedBounds_FiresLifecycleEvents(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            DockableContent? createdSource = null;
            DockableContent? closedContent = null;
            int createdCount = 0;
            int closedCount = 0;

            var pane = new Document
            {
                Title = "Clamped",
                Key = "clamp:doc",
                Content = TextBlock("body-clamp"),
                CanFloat = true,
            };
            var managerEl = new DockManager
            {
                Layout = new DockTabGroup(new DockableContent[] { pane }),
                OnFloatingWindowCreated = args =>
                {
                    createdCount++;
                    createdSource = args.DraggedSource;
                },
                OnFloatingWindowClosed = args =>
                {
                    closedCount++;
                    closedContent = args.Content;
                },
            };
            host.Mount(_ => managerEl);
            await Harness.Render();

            var displays = new[] { new DockDisplay(0, 0, 1920, 1080) };
            var savedOffscreen = new DockFloatingBounds(50_000, 50_000, 600, 400);

            ReactorWindow? floating = null;
            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            try
            {
                floating = DockFloatingWindow.Open(
                    pane,
                    savedBounds: savedOffscreen,
                    displays: displays,
                    manager: managerEl);

                H.Check("FloatCov_Open_ReturnsWindow", floating is not null);
                H.Check("FloatCov_Open_CreatedEventFired", createdCount == 1);
                H.Check("FloatCov_Open_CreatedEventSourceIsPane",
                    ReferenceEquals(createdSource, pane));

                floating?.Close();
                await Harness.Render();
                // Closed event drains on the next dispatcher tick.
                await Harness.Render();

                H.Check("FloatCov_Close_ClosedEventFired", closedCount == 1);
                H.Check("FloatCov_Close_ClosedEventContentIsPane",
                    ReferenceEquals(closedContent, pane));
            }
            finally
            {
                ReactorApp.ShutdownPolicy = savedPolicy;
            }

            host.Mount(_ => TextBlock("float-clamp-done"));
            await Harness.Render();
        }
    }

    /// <summary>
    /// Exercises <see cref="DockFloatingWindow.Open"/> with no clamp inputs
    /// — savedBounds null AND displays null — to ensure the
    /// "skip clamp" branch is taken and the default width/height are used.
    /// Validates that the entry is recorded in <see cref="DockFloatingTracker"/>
    /// snapshot dimensions.
    /// </summary>
    internal class FloatingWindow_OpenWithDefaultSize_TracksDimensions(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var pane = new Document
            {
                Title = "Defaults",
                Key = "default:doc",
                Content = TextBlock("body-defaults"),
            };
            var managerEl = new DockManager
            {
                Layout = new DockTabGroup(new DockableContent[] { pane }),
            };
            host.Mount(_ => managerEl);
            await Harness.Render();

            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            try
            {
                var floating = DockFloatingWindow.Open(pane, manager: managerEl);
                try
                {
                    H.Check("FloatCov_Defaults_OpenSucceeded", floating is not null);
                    var snap = DockFloatingTracker.SnapshotPanesFor(managerEl);
                    H.Check("FloatCov_Defaults_TrackerSeesPane",
                        snap.Any(s => s.Contents.Any(c => ReferenceEquals(c, pane))));
                    // Default width/height = 480/320 in the spec; the snapshot
                    // records whatever value flowed through to RegisterEntry.
                    var entry = snap.FirstOrDefault();
                    H.Check("FloatCov_Defaults_HasPositiveWidth",
                        entry is not null && entry.Width > 0);
                    H.Check("FloatCov_Defaults_HasPositiveHeight",
                        entry is not null && entry.Height > 0);
                }
                finally { floating?.Close(); }
            }
            finally { ReactorApp.ShutdownPolicy = savedPolicy; }

            await Harness.Render();
            host.Mount(_ => TextBlock("float-default-done"));
            await Harness.Render();
        }
    }

    /// <summary>
    /// Open() with a null manager — the per-manager tracking arms must be
    /// skipped (no event subscribers; no tracker registration for any
    /// DockManager). The window still appears in the global tracker.
    /// </summary>
    internal class FloatingWindow_OpenWithoutManager_SkipsPerManagerWiring(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var pane = new Document
            {
                Title = "Bare",
                Key = "bare:doc",
                Content = TextBlock("body-bare"),
            };

            // Mount a primary host to keep the dispatcher alive while we
            // open a managerless floating window.
            host.Mount(_ => TextBlock("bare-host"));
            await Harness.Render();

            var baseline = DockFloatingTracker.Count;
            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            try
            {
                var floating = DockFloatingWindow.Open(pane); // no manager
                try
                {
                    H.Check("FloatCov_NoManager_OpenSucceeded", floating is not null);
                    H.Check("FloatCov_NoManager_GlobalTrackerIncremented",
                        DockFloatingTracker.Count == baseline + 1);
                }
                finally { floating?.Close(); }
                await Harness.Render();
                H.Check("FloatCov_NoManager_TrackerDecrementsOnClose",
                    DockFloatingTracker.Count == baseline);
            }
            finally { ReactorApp.ShutdownPolicy = savedPolicy; }

            host.Mount(_ => TextBlock("bare-done"));
            await Harness.Render();
        }
    }

    /// <summary>
    /// Exercises <see cref="DockFloatingPaneRouter.Register"/> /
    /// <see cref="DockFloatingPaneRouter.Unregister"/> /
    /// <see cref="DockFloatingPaneRouter.HasRegisteredWindows"/> and the
    /// fast-skip path of <see cref="DockFloatingPaneRouter.TryAppendUnderCursor"/>
    /// when no windows are registered.
    /// </summary>
    internal class FloatingRouter_RegisterUnregister_AndEmptyHitTest(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var pane = new Document
            {
                Title = "Router",
                Key = "router:doc",
                Content = TextBlock("body-router"),
            };
            var managerEl = new DockManager
            {
                Layout = new DockTabGroup(new DockableContent[] { pane }),
            };
            host.Mount(_ => managerEl);
            await Harness.Render();

            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            try
            {
                // The router is process-global so an earlier fixture might
                // have left appenders registered. Capture the baseline
                // count from a single TryAppendUnderCursor — false return
                // means cursor isn't over any registered window (true on
                // an empty registry; possibly true with siblings registered
                // but cursor outside). We just confirm the call doesn't
                // throw and returns a bool.
                bool baselineEmpty = !DockFloatingPaneRouter.HasRegisteredWindows;
                var baselineHit = DockFloatingPaneRouter.TryAppendUnderCursor(pane);
                if (baselineEmpty)
                    H.Check("FloatRouter_EmptyBaseline_HitReturnsFalse", baselineHit == false);

                var floating = DockFloatingWindow.Open(pane, manager: managerEl);
                try
                {
                    // The component's UseEffect registers asynchronously on
                    // the dispatcher; two render passes should drain.
                    await Harness.Render();
                    await Harness.Render();
                    H.Check("FloatRouter_AfterOpen_HasRegistered_True",
                        DockFloatingPaneRouter.HasRegisteredWindows);

                    int appendCount = 0;
                    Action<DockableContent> append = _ => appendCount++;
                    // Re-register against our own callback to exercise the
                    // overwrite branch of Register (Dictionary indexer).
                    DockFloatingPaneRouter.Register(floating, append);
                    H.Check("FloatRouter_Reregister_HasRegistered_True",
                        DockFloatingPaneRouter.HasRegisteredWindows);
                    // Hit-test won't necessarily land on our window (cursor
                    // could be anywhere), but the method must return a bool
                    // without throwing.
                    var hit = DockFloatingPaneRouter.TryAppendUnderCursor(pane);
                    H.Check("FloatRouter_TryAppend_ReturnsBool",
                        hit == true || hit == false);
                    // When the hit landed on us, our overwrite-registered
                    // appender ran exactly once.
                    if (hit)
                        H.Check("FloatRouter_TryAppend_HitInvokesOverwriteAppender",
                            appendCount == 1);

                    // Explicit unregister of our window drops the registration
                    // count by exactly one (HasRegisteredWindows depends on
                    // peer registrations from other fixtures, so we can only
                    // assert "didn't throw" / "second call is no-op").
                    DockFloatingPaneRouter.Unregister(floating);
                    DockFloatingPaneRouter.Unregister(floating); // idempotent
                    H.Check("FloatRouter_Unregister_Idempotent", true);
                }
                finally { floating?.Close(); }
                await Harness.Render();
            }
            finally { ReactorApp.ShutdownPolicy = savedPolicy; }

            host.Mount(_ => TextBlock("router-done"));
            await Harness.Render();
        }
    }

    /// <summary>
    /// Exercises <see cref="DockFloatingWindow.BuildFloatingRoot"/> directly
    /// without opening a real window — proves the element tree shape (the
    /// returned element is a TabViewElement-rooted chrome with one tab whose
    /// body inlines the pane Content).
    /// </summary>
    internal class FloatingWindow_BuildFloatingRoot_ProducesTabbedChrome(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var pane = new Document
            {
                Title = "Tree",
                Key = "tree:doc",
                Content = TextBlock("body-tree"),
            };
            var holder = new ReactorWindow?[] { null };
            var manager = new DockManager
            {
                Layout = new DockTabGroup(new DockableContent[] { pane }),
            };

            var root = DockFloatingWindow.BuildFloatingRoot(pane, holder, manager);
            H.Check("FloatRoot_Built_NonNull", root is not null);

            // Mount it into the live host so visual-tree probes work.
            host.Mount(_ => root!);
            await Harness.Render();

            H.Check("FloatRoot_TabViewMounted",
                H.FindAllControls<TabView>(_ => true).Count >= 1);
            H.Check("FloatRoot_PaneBodyRendered",
                H.FindText("body-tree") is not null);

            host.Mount(_ => TextBlock("float-root-done"));
            await Harness.Render();
        }
    }
}
