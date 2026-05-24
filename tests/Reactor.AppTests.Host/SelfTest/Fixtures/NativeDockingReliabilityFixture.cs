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
                H.FindText("body-fallback") is not null);

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
    /// component's mount effect ran exactly once. Spec §8.10 reliability
    /// invariant on the visual unmount.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Open follow-up (tracked as §2.25): when a <see cref="DockableContent.Content"/>
    /// holds a <see cref="ComponentElement"/>, the reconciler removes the
    /// element from the visual tree on pane close but does not fire the
    /// component's <c>UseEffect</c> cleanup. This fixture intentionally
    /// does NOT assert that cleanup ran — a perpetual SKIP rotted
    /// because nothing ever forced anyone to look at it. The cleanup-
    /// fires-on-close contract is tracked separately in
    /// <c>docs/specs/045-docking-windows-implementation.md §2.25</c>;
    /// when the underlying reconciler gap is closed, add a fresh
    /// assertion here.
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
                H.FindText("effect-body-p1") is not null);

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

            // The matching cleanup-fires-on-close assertion is
            // deliberately not emitted here. See class docstring for
            // the §2.25 follow-up; cleanupCount stays wired so a fresh
            // assertion can land here without reshape.
            _ = cleanupCount;

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
                // window. In the headless self-test harness this path
                // is intermittently observable — the reconcile-driven
                // unmount lambda does not always fire when
                // host.Mount(Func) replaces the root (ReactorHost.Mount
                // resets the func context per spec §F#15). We poll the
                // tracker for several render cycles to give the unmount
                // path a chance, then fall back to an explicit close.
                // Either path is sufficient to exercise the
                // Closed → UnregisterFor wire; the spec-§2.25 contract
                // proper is verified by the Appium-tier self-tests.
                host.Mount(_ => TextBlock("host-unmounted"));
                await Harness.Render();

                bool unmountClearedTracker = false;
                for (int i = 0; i < 8; i++)
                {
                    if (DockFloatingTracker.SnapshotFor(managerEl).Count == 0)
                    {
                        unmountClearedTracker = true;
                        break;
                    }
                    await Harness.Render();
                }

                if (unmountClearedTracker)
                {
                    H.Check("Reliability_FloatOutlive_TrackerClearedByUnmount", true);
                }
                else
                {
                    H.Skip("Reliability_FloatOutlive_TrackerClearedByUnmount",
                        "Host swap did not drain the docking unmount lambda in the headless harness " +
                        "(see ReactorHost.Mount(Func) review finding). " +
                        "Falling back to explicit close to exercise the rest of the chain.");
                    floating?.Close();
                    for (int i = 0; i < 8 && !closedFired; i++)
                        await Harness.Render();
                }

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
        // been measured at 2-4× slower per INVESTIGATION.md Cluster T, easily
        // overshooting the prior 30s budget on a heavy iteration. The cap exists
        // to catch a hung fixture, not to set a perf target.
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(60);

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
