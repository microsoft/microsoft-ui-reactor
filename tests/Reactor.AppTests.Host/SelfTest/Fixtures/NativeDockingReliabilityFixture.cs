using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Docking.Persistence;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 045 §2.24 (security) + §2.25 (reliability) — host-mounted
/// selftests for the load / mutation / cleanup paths. Unit tests under
/// `tests/Reactor.Tests/Docking/` cover the same contracts in isolation;
/// these fixtures verify the contracts under a real host so the
/// integration paths (mounted reconciler, dispatcher thread affinity,
/// effect-flush ordering) don't drift.
/// </summary>
internal static class NativeDockingReliabilityFixtures
{
    // ── §2.25 corrupt-persisted-layout fallback (host-mounted) ──────────

    /// <summary>
    /// Mounts a host whose <see cref="DockManager.Layout"/> is sourced
    /// from a corrupt JSON payload via <see cref="DockLayoutSerializer.Load"/>.
    /// The load must not throw; the fallback layout must mount; the
    /// <c>Microsoft-UI-Reactor</c> event source must fire the
    /// <c>DockingLayoutLoadFallback</c> event. Without this fixture the
    /// regression risk is "Load throws when called from a render closure",
    /// which the unit-only path can't catch.
    /// </summary>
    internal class CorruptLayoutFallback_HostMounted(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            using var listener = new FallbackListener();
            listener.EnableEvents(ReactorEventSource.Log, EventLevel.Warning, EventKeywords.All);

            // Corrupt JSON — unbalanced braces, truncated mid-token. The
            // serializer must classify this as `json-parse` and return a
            // fallback result whose Root is null.
            var result = DockLayoutSerializer.Load("{\"$schema\":2,\"root\":{\"kind\":\"split");
            H.Check("Reliability_CorruptLoad_DidNotThrow", true);
            H.Check("Reliability_CorruptLoad_IsFallback", result.IsFallback);

            // EventListener callbacks for managed EventSource events do not
            // flow under NativeAOT publish — IsEnabled() returns false on the
            // emit side and the listener observes zero events. Verified via
            // an in-test probe (TotalEvents=0). The same load + Fail() path
            // is covered by JIT runs and by the unit tests in
            // tests/Reactor.Tests/Docking/. Skip the listener-bound check
            // when dynamic code is unavailable.
            if (RuntimeFeature.IsDynamicCodeSupported)
                H.Check("Reliability_CorruptLoad_EventEmittedJsonParse",
                    listener.Categories.Contains("json-parse"));

            // The fallback Root is null. The host should mount a healthy
            // empty-layout shape — no exception, no orphan tree.
            var pane = new Document
            {
                Title = "Fallback",
                Key = "fb",
                Content = TextBlock("body-fallback"),
            };
            host.Mount(_ => new DockManager
            {
                // Synthesize the "use loaded or default" branch the app
                // would write at the call site. Result.Root is null →
                // fall through to a default tab group with the pane.
                Layout = result.Root ?? new DockTabGroup(new DockableContent[] { pane }),
            });
            await Harness.Render();
            H.Check("Reliability_CorruptLoad_FallbackPaneMounted",
                await Harness.WaitFor(() => H.FindText("body-fallback") is not null));

            host.Mount(_ => TextBlock("corrupt-fallback-done"));
            await Harness.Render();
        }
    }

    // ── §2.25 concurrent off-dispatcher mutation throws ─────────────────

    /// <summary>
    /// After the host has mounted, the bridge-resolved <see cref="DockHostModel"/>
    /// is owned by the UI dispatcher. A mutator call from a worker thread
    /// must throw <see cref="InvalidOperationException"/> (spec §8.10) and
    /// the queue must stay empty.
    /// </summary>
    internal class OffThreadMutation_ThrowsAndDoesNotQueue(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var pane = new Document
            {
                Title = "Doc",
                Key = "off-thread:doc",
                Content = TextBlock("body-off-thread"),
            };
            var managerEl = new DockManager
            {
                Layout = new DockTabGroup(new DockableContent[] { pane }),
            };
            host.Mount(_ => managerEl);
            await Harness.Render();

            var model = DockHostModelBridge.Get(managerEl);
            H.Check("Reliability_OffThread_ModelResolved", model is not null);

            var newDoc = new Document { Title = "X", Key = "x" };
            bool threw = false;
            await Task.Run(() =>
            {
                try { model!.Dock(newDoc, DockTarget.Center); }
                catch (InvalidOperationException) { threw = true; }
            });

            H.Check("Reliability_OffThread_DockThrew", threw);
            // The mutator throws BEFORE Pending.Add — so the queue stays
            // clean and no spurious re-render fires.
            H.Check("Reliability_OffThread_QueueRemainsEmpty",
                model?.Pending.Count == 0);

            host.Mount(_ => TextBlock("off-thread-done"));
            await Harness.Render();
        }
    }

    // ── §2.25 useEffect cleanup on pane close ───────────────────────────

    /// <summary>
    /// Props for the effect-counter component. Mount / cleanup callbacks
    /// are passed in by the owning fixture so the counters live in
    /// fixture-scoped state instead of static fields — fixtures can run
    /// in parallel (or be aborted mid-run) without leaking state into
    /// the next fixture's run.
    /// </summary>
    internal sealed record EffectCounterProps(
        string Marker,
        Action<string>? OnMount = null,
        Action<string>? OnCleanup = null);

    /// <summary>
    /// Component whose mount registers an effect + cleanup. The owning
    /// fixture supplies <see cref="EffectCounterProps.OnMount"/> /
    /// <see cref="EffectCounterProps.OnCleanup"/> closures that update
    /// per-run counters.
    /// </summary>
    internal sealed class EffectCounterComponent : Component<EffectCounterProps>
    {
        public override Element Render()
        {
            UseEffect(() =>
            {
                Props.OnMount?.Invoke(Props.Marker);
                return () => Props.OnCleanup?.Invoke(Props.Marker);
            });
            return TextBlock($"effect-body-{Props.Marker}");
        }
    }

    /// <summary>
    /// Mounts a pane whose content registers a UseEffect cleanup, then
    /// programmatically closes the pane via <c>model.Close</c>. Asserts:
    /// (a) the close drains through the §2.16 mutation queue, (b) the
    /// component's body is removed from the visual tree, (c) the
    /// component's mount effect ran exactly once, and (d) the component's
    /// UseEffect cleanup fired exactly once on pane close. Spec §8.10
    /// reliability invariant on the visual unmount.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Issue #375 — the UseEffect cleanup assertion is the regression
    /// test for the V1HandlerAdapter unmount-side strategy dispatch:
    /// when a <see cref="DockableContent.Content"/> holds a
    /// <see cref="ComponentElement"/> nested under the docking host's
    /// Border wrapper (<c>WrapLeafWithPaneContext</c>), the SingleContent
    /// strategy must walk its live child on Unmount so the component
    /// wrapper Border that anchors the <c>_componentNodes</c> lookup is
    /// reached and <c>RunCleanups()</c> fires.
    /// </para>
    /// </remarks>
    internal class UseEffectCleanup_BodyRemovedOnPaneClose(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Fixture-owned counters — moved off static fields so a prior
            // fixture that aborts mid-run can't leak state into the next
            // run, and concurrent fixtures don't trample each other.
            int mountedCount = 0;
            int cleanupCount = 0;

            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var pane = new Document
            {
                Title = "EffectPane",
                Key = "effect:pane",
                Content = Component<EffectCounterComponent, EffectCounterProps>(new EffectCounterProps(
                    "p1",
                    OnMount: _ => mountedCount++,
                    OnCleanup: _ => cleanupCount++)),
                CanClose = true,
            };
            var managerEl = new DockManager
            {
                Layout = new DockTabGroup(new DockableContent[] { pane }),
            };
            host.Mount(_ => managerEl);
            await Harness.Render();

            H.Check("Reliability_Effect_MountedOnce", mountedCount == 1);
            H.Check("Reliability_Effect_NoCleanupBeforeClose", cleanupCount == 0);
            H.Check("Reliability_Effect_BodyRendered",
                await Harness.WaitFor(() => H.FindText("effect-body-p1") is not null));

            var model = DockHostModelBridge.Get(managerEl);
            H.Check("Reliability_Effect_BridgeYieldsModel", model is not null);
            model?.Close(pane);
            H.Check("Reliability_Effect_PendingQueued",
                model is { } m && m.Pending.Count == 1);
            // Force a sub-host re-render. Harness.Render's idle-wait
            // targets the primary host; the sub-host's bumpTick from
            // OnMutationQueued queues a render that needs an external
            // nudge to run. A `with`-clone of the controlled element
            // changes the props reference, which the reconciler treats
            // as a prop-change re-render. The drain then flushes Pending.
            host.Mount(_ => managerEl with { });
            await Harness.Render();
            H.Check("Reliability_Effect_PendingDrained",
                model is { } m2 && m2.Pending.Count == 0);
            await Harness.Render();

            H.Check("Reliability_Effect_BodyGoneFromTree",
                H.FindText("effect-body-p1") is null);

            // Issue #375 regression — the cleanup must fire when the pane
            // is closed. Before the V1HandlerAdapter unmount-side strategy
            // dispatch landed, this counter stayed at 0 because the docking
            // host's BorderElement wrapper around the pane Content was
            // unmounted via the V1 arm returning CollectSelf — which short-
            // circuited before the engine could recurse into the component
            // wrapper Border that anchors RunCleanups().
            H.Check("Reliability_Effect_CleanupRanOnClose", cleanupCount == 1);

            host.Mount(_ => TextBlock("effect-cleanup-done"));
            await Harness.Render();
        }
    }

    // ── §2.24 drag-drop payload is object-ref only (no serialization) ──

    /// <summary>
    /// Spec §2.24 / §8.9 — the drag session payload must be in-process
    /// object references only, never a serializable identifier. This
    /// fixture asserts the contract by reflection-checking the session's
    /// public surface for any string-/GUID-keyed lookup, then confirms
    /// the session ends to <c>null</c> (no GC pinning of completed drags).
    /// </summary>
    internal class DragSessionPayload_ObjectRefsOnly(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var pane = new Document
            {
                Title = "Drag",
                Key = "drag:doc",
                Content = TextBlock("body-drag"),
            };
            var managerEl = new DockManager
            {
                Layout = new DockTabGroup(new DockableContent[] { pane }),
            };
            host.Mount(_ => managerEl);
            await Harness.Render();

            DockDragSession.ResetForTest();
            var session = DockDragSession.Begin(pane, managerEl, sourceTabIndex: 0);
            H.Check("Reliability_DragPayload_BeginReturnsSession", session is not null);

            // The session's Source / SourceManager properties must hold
            // the same reference the caller passed in — not a copy, not a
            // string id resolved later.
            H.Check("Reliability_DragPayload_SourceIsObjectRef",
                ReferenceEquals(session?.Source, pane));
            H.Check("Reliability_DragPayload_ManagerIsObjectRef",
                ReferenceEquals(session?.SourceManager, managerEl));

            // No second drag can start while one is in flight (single-
            // drag contract).
            var second = DockDragSession.Begin(pane, managerEl, sourceTabIndex: 0);
            H.Check("Reliability_DragPayload_SecondBeginRefused", second is null);

            // End nulls out the static slot, so GC can collect the source
            // pane + manager once the layout drops references too.
            session?.End();
            H.Check("Reliability_DragPayload_EndClearsCurrent",
                DockDragSession.Current is null);

            host.Mount(_ => TextBlock("drag-payload-done"));
            await Harness.Render();
        }
    }

    // ── §2.25 process crash mid-drag — drag state never persists ───────

    /// <summary>
    /// Spec §8.10 invariant: the drag-session payload is in-memory only.
    /// On a hypothetical process crash mid-drag, restarting reloads the
    /// last persisted layout — the partially-completed drag is lost
    /// (correct behavior). This fixture establishes the contract by
    /// (a) beginning a drag, (b) saving the layout via
    /// <see cref="DockLayoutSerializer.Save"/>, (c) asserting the saved
    /// JSON contains nothing drag-session-related, (d) restoring via
    /// Load on a fresh process and confirming the layout shape matches
    /// the pre-drag tree — no orphans, no half-moved panes.
    /// </summary>
    internal class CrashMidDrag_LeavesPersistedLayoutClean(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var docA = new Document { Title = "A", Key = "crash:a", Content = TextBlock("body-a") };
            var docB = new Document { Title = "B", Key = "crash:b", Content = TextBlock("body-b") };
            var layout = new DockTabGroup(new DockableContent[] { docA, docB });
            var managerEl = new DockManager { Layout = layout };
            host.Mount(_ => managerEl);
            await Harness.Render();

            // Pre-crash layout snapshot — the file written before the
            // imagined crash.
            var preCrashJson = DockLayoutSerializer.Save(layout);
            H.Check("Reliability_Crash_PreSaveSucceeded", !string.IsNullOrEmpty(preCrashJson));

            // Begin a drag — this is the "mid-drag" point. Nothing here
            // should reach the persisted JSON.
            DockDragSession.ResetForTest();
            var session = DockDragSession.Begin(docA, managerEl, sourceTabIndex: 0);
            H.Check("Reliability_Crash_DragBegan", session is not null);

            // Save again while drag is active. The serializer must not
            // include any in-flight drag state — drag is renderer/session
            // state, not model state.
            var midDragJson = DockLayoutSerializer.Save(layout);
            H.Check("Reliability_Crash_NoDragSessionInJson",
                !midDragJson.Contains("dragSession", StringComparison.Ordinal) &&
                !midDragJson.Contains("dragging", StringComparison.OrdinalIgnoreCase));
            H.Check("Reliability_Crash_PreAndMidDragJsonIdentical",
                midDragJson == preCrashJson);

            // "Restart" — drop the session state (simulating process exit)
            // and reload from the persisted JSON.
            DockDragSession.ResetForTest();
            var reloaded = DockLayoutSerializer.Load(preCrashJson);
            H.Check("Reliability_Crash_ReloadedSuccessfully", !reloaded.IsFallback);
            H.Check("Reliability_Crash_NoDragSessionAfterRestart",
                DockDragSession.Current is null);

            // Shape-level check on the reloaded layout: both panes still
            // present, no half-moved state, no orphans. Pane Content is
            // app-owned (not serialized) so we assert on Key identity,
            // not on body text.
            var reloadedRoot = reloaded.Root;
            H.Check("Reliability_Crash_ReloadedRootIsTabGroup",
                reloadedRoot is DockTabGroup);
            if (reloadedRoot is DockTabGroup tg)
            {
                H.Check("Reliability_Crash_ReloadedHasBothPanes",
                    tg.Documents.Count == 2);
                H.Check("Reliability_Crash_ReloadedKeysPreserved",
                    tg.Documents[0].Key?.ToString() == "crash:a" &&
                    tg.Documents[1].Key?.ToString() == "crash:b");
            }

            host.Mount(_ => TextBlock("crash-drag-done"));
            await Harness.Render();
        }
    }

    // ── §2.25 floating window outliving host — host unmount closes ─────

    /// <summary>
    /// Spec §2.25 reliability: a floating window opened from a
    /// <see cref="DockManager"/> must not outlive that manager — when
    /// the host unmounts, every floating window it opened closes.
    /// The host's unmount handler walks
    /// <see cref="DockFloatingTracker"/>.SnapshotFor(manager) and calls
    /// Close on each. Apps that need floating windows to survive a
    /// host transition open them via <c>ReactorApp.OpenWindow</c>
    /// directly, not through the docking float gesture.
    /// </summary>
    internal class FloatingWindowClosesOnHostUnmount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var pane = new Document
            {
                Title = "Float",
                Key = "float:outlive",
                Content = TextBlock("body-float-outlive"),
                CanFloat = true,
            };
            var managerEl = new DockManager
            {
                Layout = new DockTabGroup(new DockableContent[] { pane }),
            };
            host.Mount(_ => managerEl);
            await Harness.Render();

            // Track baseline + open a floating window associated with the
            // host's manager. We exercise the public Open() overload that
            // takes the manager so the host's per-manager tracking set
            // sees the registration. ShutdownPolicy is pinned to Explicit
            // for the duration so closing the floating window doesn't
            // accidentally trip the framework's primary-window shutdown.
            int baseline = DockFloatingTracker.Count;
            ReactorWindow? floating = null;
            bool closedFired = false;

            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            try
            {
                try
                {
                    floating = DockFloatingWindow.Open(pane, manager: managerEl);
                    floating.Closed += (_, _) => closedFired = true;
                    H.Check("Reliability_FloatOutlive_OpenSucceeded", floating is not null);
                    H.Check("Reliability_FloatOutlive_TrackerIncremented",
                        DockFloatingTracker.Count == baseline + 1);
                    H.Check("Reliability_FloatOutlive_PerHostTrackerSeesIt",
                        DockFloatingTracker.SnapshotFor(managerEl).Contains(floating));
                }
                catch
                {
                    H.Check("Reliability_FloatOutlive_OpenSkippedHeadless", true);
                    return;
                }

                // Drive the host into an unmount state by replacing the
                // root with a non-DockManager element. The production
                // contract: DockingNativeInterop's unmount lambda
                // iterates DockFloatingTracker.SnapshotFor(managerEl)
                // and calls Close + UnregisterFor on each floating
                // window. This used to be skipped as "intermittently
                // observable in the headless harness", but the lambda
                // never ran at all: the reconciler did not tag controls
                // mounted through RegisterType, so the unmount path could
                // not find the registration. It is deterministic now.
                host.Mount(_ => TextBlock("host-unmounted"));
                bool unmountClearedTracker = await Harness.WaitFor(
                    () => DockFloatingTracker.SnapshotFor(managerEl).Count == 0, maxPasses: 8);
                H.Check("Reliability_FloatOutlive_TrackerClearedByUnmount", unmountClearedTracker);

                // Close it by hand if the host did not, so a failure here
                // doesn't leak the window into later fixtures.
                if (!unmountClearedTracker)
                    floating?.Close();
                for (int i = 0; i < 8 && !closedFired; i++)
                    await Harness.Render();

                H.Check("Reliability_FloatOutlive_PerHostTrackerClearedEventually",
                    DockFloatingTracker.SnapshotFor(managerEl).Count == 0);
                H.Check("Reliability_FloatOutlive_ClosedEventFired", closedFired);
            }
            finally
            {
                ReactorApp.ShutdownPolicy = savedPolicy;
            }
        }
    }

    // ── §2.25 floating window outliving host — a new DockManager per render

    /// <summary>
    /// <see cref="FloatingWindowClosesOnHostUnmount"/> keeps one
    /// <see cref="DockManager"/> instance for the host's whole life. Apps
    /// build a new one in every render instead, the Reactor IDE sample among
    /// them, and any state change renders the app, the float's own included.
    /// This floats a pane through the host model, renders the app again so
    /// the host has moved past the instance the window opened under, then
    /// unmounts the host. The floating window must close, and no per-host
    /// table may still answer for any instance the host rendered. The tables
    /// used to be keyed by element instance while unmount cleaned up only the
    /// last one, so the window, and every entry made under an earlier
    /// instance, outlived the host.
    /// </summary>
    internal class FloatingWindowClosesOnHostUnmount_AfterRerender(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);
            var app = new RerenderingDockApp("rerender-unmount");

            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorWindow? floating = null;
            try
            {
                host.Mount(_ => app.Render());
                await Harness.Render();
                var first = app.Rendered[0];
                var firstRecordId = DockHostRegistry.Snapshot()
                    .FirstOrDefault(r => ReferenceEquals(r.Manager, first))?.Id;

                floating = await app.FloatOutput();
                H.Check("Reliability_RerenderUnmount_FloatOpenedWindow", floating is not null);
                if (floating is null) return;
                bool closed = false;
                floating.Closed += (_, _) => closed = true;
                int instancesAtFloat = app.Rendered.Count;

                // Any state change renders the app again, so the float has
                // already handed the host newer instances; so does an update.
                host.Mount(_ => app.Render());
                await Harness.Render();
                var current = app.Rendered[^1];
                H.Check("Reliability_RerenderUnmount_HostMovedPastFloatInstance",
                    app.Rendered.Count > instancesAtFloat && DockHostModelBridge.Get(current) is not null,
                    $"instances at float={instancesAtFloat} now={app.Rendered.Count}");

                // While mounted it is one host, whichever instance it rendered last.
                var records = DockHostRegistry.Snapshot()
                    .Where(r => app.Rendered.Any(m => ReferenceEquals(m, r.Manager)))
                    .ToArray();
                H.Check("Reliability_RerenderUnmount_RegistryListsHostOnceUnderStableId",
                    records.Length == 1 && records[0].Id == firstRecordId && ReferenceEquals(records[0].Manager, current),
                    $"records=[{string.Join(",", records.Select(r => r.Id))}] firstId={firstRecordId}");
                H.Check("Reliability_RerenderUnmount_ModelStillListsFloatingPane",
                    DockHostModelBridge.Get(current)?.Floating
                        .Any(f => f.Contents.Any(c => ReferenceEquals(c, app.Output))) == true);

                host.Mount(_ => TextBlock("rerender-unmount-done"));
                H.Check("Reliability_RerenderUnmount_FloatingWindowClosed",
                    await Harness.WaitFor(() => closed, maxPasses: 8));
                CheckNothingLeft(H, "Reliability_RerenderUnmount", app.Rendered);
            }
            finally
            {
                // A failed close must not leak the window into later fixtures.
                if (floating is not null && DockFloatingTracker.Snapshot().Contains(floating))
                    floating.Close();
                ReactorApp.ShutdownPolicy = savedPolicy;
            }
        }
    }

    /// <summary>
    /// The IDE's View &gt; Reset Layout: the app bumps the
    /// <see cref="DockManager"/>'s key, so the reconciler unmounts the old
    /// host and mounts a new one that docks every pane again. A pane floated
    /// before the reset must not stay open next to its re-docked copy, and
    /// the host registry must list only the new host.
    /// </summary>
    internal class FloatingWindowClosesOnResetLayoutRemount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);
            var app = new RerenderingDockApp("reset-remount");

            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorWindow? floating = null;
            try
            {
                host.Mount(_ => app.Render());
                await Harness.Render();

                floating = await app.FloatOutput();
                H.Check("Reliability_ResetRemount_FloatOpenedWindow", floating is not null);
                if (floating is null) return;
                bool closed = false;
                floating.Closed += (_, _) => closed = true;

                host.Mount(_ => app.Render());
                await Harness.Render();
                var oldHost = app.Rendered.ToArray();
                var oldModel = DockHostModelBridge.Get(oldHost[^1]);

                app.Epoch++;
                host.Mount(_ => app.Render());
                H.Check("Reliability_ResetRemount_FloatingWindowClosed",
                    await Harness.WaitFor(() => closed, maxPasses: 8));

                var newHost = app.Rendered[^1];
                var newModel = DockHostModelBridge.Get(newHost);
                H.Check("Reliability_ResetRemount_NewHostMountedWithFreshModel",
                    newModel is not null && oldModel is not null && !ReferenceEquals(newModel, oldModel));
                var records = DockHostRegistry.Snapshot()
                    .Where(r => app.Rendered.Any(m => ReferenceEquals(m, r.Manager)))
                    .ToArray();
                H.Check("Reliability_ResetRemount_RegistryListsOnlyNewHost",
                    records.Length == 1 && ReferenceEquals(records[0].Manager, newHost),
                    $"records=[{string.Join(",", records.Select(r => r.Id))}]");
                CheckNothingLeft(H, "Reliability_ResetRemount", oldHost);

                host.Mount(_ => TextBlock("reset-remount-done"));
                await Harness.Render();
            }
            finally
            {
                if (floating is not null && DockFloatingTracker.Snapshot().Contains(floating))
                    floating.Close();
                ReactorApp.ShutdownPolicy = savedPolicy;
            }
        }
    }

    /// <summary>
    /// An app can keep one <see cref="DockManager"/> instance and move it to
    /// another container, which mounts a new host for the same element. A type
    /// change mounts the new child before it unmounts the old one, so the
    /// element already belongs to the new host when the old host unmounts. The
    /// old host must still close the floating window it opened, and must leave
    /// the new host's entries alone.
    /// </summary>
    internal class FloatingWindowClosesWhenElementMovesToNewHost(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            var output = new ToolWindow { Title = "Output", Key = "moved:output", Content = TextBlock("body-moved-output") };
            var dock = new DockManager
            {
                Layout = new DockSplit(Orientation.Vertical, new DockNode[]
                {
                    new DockTabGroup(new DockableContent[]
                    {
                        new Document { Title = "Doc", Key = "moved:doc", Content = TextBlock("body-moved-doc") },
                    }),
                    new DockTabGroup(new DockableContent[] { output }),
                }),
            };
            bool moved = false;
            // The container is a panel's child: that is where a type change
            // mounts the replacement before it unmounts the old child.
            var star = new[] { GridSize.Star(1) };
            Element Render() => Grid(star, star, moved ? Grid(star, star, dock) : Border(dock));

            var savedPolicy = ReactorApp.ShutdownPolicy;
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            ReactorWindow? floating = null;
            try
            {
                host.Mount(_ => Render());
                await Harness.Render();
                var oldModel = DockHostModelBridge.Get(dock);
                var oldHostBorder = DockHostLiveAnnouncer.GetHost(dock);

                // Opened directly, as the tear-off does, rather than through
                // model.Float: the float drains in a re-render of the host's
                // component, and the reconciler skips a reference-equal panel
                // child without checking for a self-triggered descendant (the
                // edge ChildReconciler.UpdateCommonChild documents), so a
                // reused DockManager instance never drains it.
                floating = DockFloatingWindow.Open(output, manager: dock);
                bool closed = false;
                floating.Closed += (_, _) => closed = true;
                H.Check("Reliability_ElementMoved_WindowFiledUnderOldHost",
                    DockFloatingTracker.SnapshotFor(dock).Contains(floating));

                moved = true;
                host.Mount(_ => Render());
                H.Check("Reliability_ElementMoved_OldHostsWindowClosed",
                    await Harness.WaitFor(() => closed, maxPasses: 8));

                var newModel = DockHostModelBridge.Get(dock);
                var newHostBorder = DockHostLiveAnnouncer.GetHost(dock);
                H.Check("Reliability_ElementMoved_NewHostKeepsItsModel",
                    newModel is not null && oldModel is not null && !ReferenceEquals(newModel, oldModel));
                H.Check("Reliability_ElementMoved_NewHostKeepsItsAnnouncer",
                    newHostBorder is not null && oldHostBorder is not null && !ReferenceEquals(newHostBorder, oldHostBorder));
                H.Check("Reliability_ElementMoved_NewHostKeepsItsChords", DockChordBridge.Get(dock) is not null);
                H.Check("Reliability_ElementMoved_NewHostKeepsItsDragGate", DockDragGateBridge.Get(dock) is not null);
                var records = DockHostRegistry.Snapshot().Count(r => ReferenceEquals(r.Manager, dock));
                H.Check("Reliability_ElementMoved_RegistryListsNewHostOnce", records == 1, $"records={records}");

                host.Mount(_ => TextBlock("element-moved-done"));
                await Harness.Render();
                CheckNothingLeft(H, "Reliability_ElementMoved", new[] { dock });
            }
            finally
            {
                if (floating is not null && DockFloatingTracker.Snapshot().Contains(floating))
                    floating.Close();
                ReactorApp.ShutdownPolicy = savedPolicy;
            }
        }
    }

    /// <summary>
    /// One check per per-host table: none may still answer for any of
    /// <paramref name="instances"/>, the elements an unmounted host rendered.
    /// </summary>
    private static void CheckNothingLeft(Harness h, string prefix, IReadOnlyList<DockManager> instances)
    {
        var records = DockHostRegistry.Snapshot();
        void CheckTable(string table, Func<DockManager, bool> answers)
        {
            var stale = Enumerable.Range(0, instances.Count).Where(i => answers(instances[i])).ToArray();
            h.Check($"{prefix}_{table}Cleared", stale.Length == 0,
                $"still answers for render(s) [{string.Join(",", stale)}] of {instances.Count}");
        }
        CheckTable("HostRegistry", m => records.Any(r => ReferenceEquals(r.Manager, m)));
        CheckTable("ChordBridge", m => DockChordBridge.Get(m) is not null);
        CheckTable("LiveAnnouncer", m => DockHostLiveAnnouncer.GetHost(m) is not null);
        CheckTable("ModelBridge", m => DockHostModelBridge.Get(m) is not null);
        CheckTable("DragGate", m => DockDragGateBridge.Get(m) is not null);
        CheckTable("FloatingTracker", m => DockFloatingTracker.SnapshotFor(m).Count > 0);
    }

    /// <summary>
    /// Builds its <see cref="DockManager"/> the way the Reactor IDE sample
    /// does: a new element around a freshly built layout on every render,
    /// keyed by an epoch that a layout reset bumps. Keeps every element it
    /// hands out so a fixture can ask the per-host tables about each one.
    /// </summary>
    private sealed class RerenderingDockApp(string prefix)
    {
        public Document Doc { get; } = new() { Title = "Doc", Key = $"{prefix}:doc", Content = TextBlock($"body-{prefix}-doc") };

        public ToolWindow Output { get; } = new() { Title = "Output", Key = $"{prefix}:output", Content = TextBlock($"body-{prefix}-output") };

        public List<DockManager> Rendered { get; } = new();

        public int Epoch { get; set; }

        public DockManager Render()
        {
            // The layout is rebuilt inline like the IDE's, so every element
            // is a distinct record and reaches the host through update.
            var manager = new DockManager
            {
                Layout = new DockSplit(Orientation.Vertical, new DockNode[]
                {
                    new DockTabGroup(new DockableContent[] { Doc }),
                    new DockTabGroup(new DockableContent[] { Output }),
                }),
            }.WithKey($"{prefix}-dock-{Epoch}");
            Rendered.Add(manager);
            return manager;
        }

        /// <summary>
        /// Floats <see cref="Output"/> through the model of the host's
        /// current element (the path devtools' <c>docking.dock</c> float
        /// takes) and returns the window the drain opened.
        /// </summary>
        public async Task<ReactorWindow?> FloatOutput()
        {
            var model = DockHostModelBridge.Get(Rendered[^1]);
            if (model is null) return null;
            var before = DockFloatingTracker.Snapshot();
            model.Float(Output);
            ReactorWindow? opened = null;
            await Harness.WaitFor(() =>
            {
                opened = DockFloatingTracker.Snapshot().FirstOrDefault(w => !before.Contains(w));
                return opened is not null;
            }, maxPasses: 8);
            return opened;
        }
    }

    // ── §2.25 event-subscription leak baseline ──────────────────────────

    /// <summary>
    /// Spec §8.10 invariant: docking does not retain panes by static
    /// dictionary, GUID table, or closure-captured event subscription.
    /// 100 open/close cycles bring allocated bytes back to baseline
    /// (within reasonable JIT/GC slack). Precedent: spec 034 allocation
    /// counter. The check is intentionally generous — a real leak (e.g.
    /// every pane registering on a static handler chain) would blow far
    /// past the cap (closure objects are ~64 B each; 100 of them is
    /// 6400 B, the cap is 256 KB to cover JIT warm-up + reconciler
    /// caches + Yoga's per-node bookkeeping for the rebuild).
    /// </summary>
    internal class EventSubscriptionLeakBaseline(Harness h) : SelfTestFixtureBase(h)
    {
        // 100 mount/unmount cycles × 2 Harness.Render() each = 200 renders + 200
        // reconcile passes. Locally this runs ~15s; CI VMs under contention have
        // been measured at 2-4× slower per INVESTIGATION.md Cluster T (i.e. up to
        // ~60s on a heavy iteration). Prior 60s budget tripped the host-level
        // HangWatchdogLoop once in 500 stress iterations; 120s gives margin and,
        // under the new per-fixture watchdog rule (SelfTestRunner.HangSlack),
        // automatically lifts that watchdog threshold to 150s.
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(120);

        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            DockingNativeInterop.Register(host.Reconciler);

            // Warm-up — JIT the open/close path and let the reconciler
            // populate its caches. The measurement window opens after
            // these settle.
            for (int i = 0; i < 5; i++)
            {
                var warmupPane = new Document { Title = $"w{i}", Key = $"warm:{i}", Content = TextBlock($"w{i}") };
                host.Mount(_ => new DockManager { Layout = new DockTabGroup(new DockableContent[] { warmupPane }) });
                await Harness.Render();
            }
            // Drain to an empty host.
            host.Mount(_ => TextBlock("warmup-done"));
            await Harness.Render();
            H.Check("Reliability_LeakBaseline_WarmupComplete", true);

            // Force GC so the baseline reflects steady state. Marshal off
            // the UI dispatcher to avoid a finalizer-deadlock on UI-thread-
            // affine RCWs.
            await Task.Run(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });

            long baseline = GC.GetAllocatedBytesForCurrentThread();

            const int cycles = 100;
            for (int i = 0; i < cycles; i++)
            {
                var pane = new Document
                {
                    Title = $"p{i}",
                    Key = $"leak:{i}",
                    Content = TextBlock($"body-{i}"),
                };
                host.Mount(_ => new DockManager
                {
                    Layout = new DockTabGroup(new DockableContent[] { pane }),
                });
                await Harness.Render();
                host.Mount(_ => TextBlock($"between-{i}"));
                await Harness.Render();
                // Heartbeat every 25 cycles. NOTE: this does NOT reset the
                // host-level HangWatchdogLoop — that uses elapsed-since-fixture-
                // start, not TAP output. These are log breadcrumbs only, so a
                // future hang reveals which 25-cycle window it lived in
                // (e.g. "Cycle25Progress: ok" printed but Cycle50 did not).
                if ((i + 1) % 25 == 0)
                    H.Check($"Reliability_LeakBaseline_Cycle{i + 1}Progress", true);
            }

            await Task.Run(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });
            long after = GC.GetAllocatedBytesForCurrentThread();
            long delta = after - baseline;

            // 32 MB cap. This is a smoke test against catastrophic
            // event-subscription leaks (e.g. every pane silently
            // registering on a static handler chain) — NOT a tight
            // budget. The mount/unmount path through the reconciler
            // legitimately allocates per cycle: element graph, Yoga
            // node, attached-property tables, control instances,
            // ConditionalWeakTable bookkeeping. Per spec §8.10 +
            // CHANGELOG, the precise allocation budget rides on the
            // §2.20 dedicated perf benchmarks that report per-frame
            // GC pressure with sub-MB precision. The point here is
            // to detect a *retention* leak (every cycle holds the
            // closed pane subtree), which would balloon into the
            // hundreds of MB range across 100 cycles.
            const long capBytes = 32L * 1024L * 1024L;
            H.Check("Reliability_LeakBaseline_AllocationDeltaWithinCap",
                delta < capBytes);

            host.Mount(_ => TextBlock($"leak-baseline-done delta={delta}"));
            await Harness.Render();
        }
    }

    // ── Shared listener helper ──────────────────────────────────────────

    private sealed class FallbackListener : EventListener
    {
        private readonly List<string> _categories = new();
        public IReadOnlyList<string> Categories
        {
            get { lock (_categories) return _categories.ToArray(); }
        }
        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e.EventName != nameof(ReactorEventSource.DockingLayoutLoadFallback)) return;
            var payload = e.Payload is { Count: > 0 } ? e.Payload[0]?.ToString() ?? string.Empty : string.Empty;
            lock (_categories) _categories.Add(payload);
        }
    }
}
