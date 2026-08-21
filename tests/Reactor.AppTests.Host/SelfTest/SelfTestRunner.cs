using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Reactor;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest;

/// <summary>
/// Runs all self-test fixtures in sequence, mounts each in a ReactorHost,
/// calls RunAsync(), captures TAP output, exits with 0/1.
/// </summary>
internal static class SelfTestRunner
{
    public static string? Filter { get; set; }

    /// <summary>
    /// Process exit code captured by the final-exit path (<see
    /// cref="EndProcessImmediately"/>). The full suite must never run any orderly
    /// process-exit teardown — neither <see cref="Environment.Exit(int)"/> (whose
    /// <c>ExitProcess</c> TLS destructors fault in Microsoft.UI.Xaml's tear-off
    /// teardown, 0xC0000005) nor <see cref="Application.Exit"/> (whose
    /// <c>CTitleBar::Uninitialize</c> double-releases a caption-buttons UI
    /// Automation provider, 0xC0000409) survives the harness's accumulated live
    /// XAML graph. We capture the code here and hard-terminate instead (issue #680).
    /// <para>
    /// Defaults to <c>1</c> (failure), not <c>0</c>: the only reader of this
    /// property is the last-resort <c>Environment.Exit(ExitCode)</c> fallback in
    /// <c>Program.cs</c>, reached only if the dispatcher loop ever unwinds without
    /// <see cref="EndProcessImmediately"/> running. Defaulting to failure means
    /// such an abnormal exit can never masquerade as a clean pass.
    /// </para>
    /// </summary>
    internal static int ExitCode { get; private set; } = 1;

    // Defensive idempotency latch for the final exit. EndProcessImmediately has a
    // single caller today — the run's finally — so this normally just passes
    // through; it ensures the process still terminates exactly once should a future
    // change ever add a second exit path. (The off-dispatcher hang watchdog is a
    // separate, independent FailFast path and never calls EndProcessImmediately.)
    private static int _shutdownStarted;

    /// <summary>
    /// When true (the default), <see cref="DefaultAotSkipPatterns"/> is honoured
    /// under NativeAOT — matching fixtures are skipped. Set to false (via
    /// <c>--no-aot-skip</c>) to run every fixture even under NativeAOT, for
    /// targeted repro of a hanging/crashing fixture together with
    /// <c>--filter &lt;name&gt;</c>. The off-dispatcher watchdog (see
    /// <see cref="HangTimeout"/>) still fires regardless.
    /// </summary>
    public static bool SkipAotPatterns { get; set; } = true;

    // Per-fixture watchdog. A managed hang used to lock up the whole run; now
    // we time out, mark it failed, and abort the Host. Continuing in-process is
    // unsafe because the timed-out fixture task can keep mutating UI and
    // emitting TAP while later fixtures run. Selftest fixtures normally
    // complete in milliseconds; long-running reliability fixtures can override
    // SelfTestFixtureBase.FixtureTimeout explicitly.

    // Off-dispatcher hang watchdog. The in-band fixture timeout relies on the
    // dispatcher processing a Task.Delay continuation, so it cannot fire when a
    // fixture synchronously blocks the UI thread. This second watchdog runs on
    // a background Thread (immune to dispatcher starvation) and declares a hang
    // after HangTimeout of no progress in the fixture loop.
    // Threshold is well past the per-fixture timeout so it only catches the
    // dispatcher-starvation case. Override via REACTOR_SELFTEST_HANG_TIMEOUT_SECONDS;
    // set to 0 or a negative value to disable entirely (useful when attaching
    // a debugger). Also auto-disabled when Debugger.IsAttached.
    private static readonly TimeSpan HangTimeout = ResolveHangTimeout();

    /// <summary>
    /// TAP comment prefix for a fixture's wall-clock time: <c># Fixture time: &lt;name&gt; &lt;ms&gt;</c>.
    /// Consumed by humans and by the ranking snippet in <c>TESTING.md</c>, not by the MSTest wrapper.
    /// </summary>
    internal const string FixtureTimeMarker = "# Fixture time: ";

    /// <summary>
    /// TAP comment for the whole suite's wall clock: <c># Suite elapsed: &lt;seconds&gt;</c>. This is the
    /// Host's own measurement, so unlike the wrapper's it excludes process start and pipe-drain
    /// overhead.
    /// </summary>
    /// <remarks>
    /// <c>tests/Reactor.SelfTests/SelfTestBatch.cs</c> parses this string but cannot reference this
    /// assembly (its ProjectReference sets <c>ReferenceOutputAssembly=false</c>), so the literal is
    /// duplicated there. Change one, change both — and note both sides use the invariant culture,
    /// because a comma-decimal locale would otherwise emit <c>312,4</c>, fail the parse, and
    /// silently fall back to the wrapper's own timing with no indication anything was lost.
    /// </remarks>
    internal const string SuiteElapsedMarker = "# Suite elapsed: ";

    private static TimeSpan ResolveHangTimeout()
    {
        var env = Environment.GetEnvironmentVariable("REACTOR_SELFTEST_HANG_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(env) && int.TryParse(env, out var s))
            return s <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(s);
        return TimeSpan.FromSeconds(60);
    }

    // Single immutable progress record — published atomically via
    // Volatile.Read/Write so the watchdog can never read a mixed
    // (new-name, old-timestamp) state. HangThreshold is per-fixture so
    // long-budget fixtures (e.g. EventSubscriptionLeakBaseline at 120 s)
    // don't trip a global 60 s ceiling.
    private sealed record FixtureProgress(string Name, long StartTimestamp, TimeSpan HangThreshold);
    private static FixtureProgress? _currentFixture;

    // Minimum slack between a fixture's own timeout and the watchdog.
    // The watchdog's job is to FailFast (dumpable) when the fixture's own
    // timeout couldn't fire because the dispatcher itself is stuck —
    // i.e. only after the graceful timeout had its chance.
    private static readonly TimeSpan HangSlack = TimeSpan.FromSeconds(30);

    // Fixtures known to assert-fail under NativeAOT, captured by running
    // tests/Reactor.AppTests.Host/probe-aot-skips.ps1 against the AOT-published
    // Host. As of WindowsAppSDK#6394 workaround (see Reactor.AppTests.Host.csproj
    // _CopyWinUIResourcesForAot target), all NATIVE_CRASH skips are gone. What is
    // left is the buckets below: the UseObservableTree property walk, the
    // Devtools fixtures whose reflection targets the trimmer removes,
    // PropertyGrid auto-discovery, the Issue142 XAML-metadata-provider edge
    // cases, and hot-reload state migration.
    //
    // Reflection on its own does NOT make a fixture AOT-hostile, so don't use it
    // as the sorting rule. The Devtools fixtures that run here mostly depend on
    // *type-name* reflection (GetType().Name in TreeWalker / SelectorResolver),
    // which trimming always preserves. What breaks is *member-level* reflection
    // whose target the trimmer drops — the Devtools entries below.
    //
    // Each name was verified to fail in isolation; wildcards from earlier
    // skip-list iterations have been replaced with explicit names so that
    // newly-passing siblings re-enter the run automatically. Prefer splitting a
    // fixture over skipping it whole: an entry mutes every check in the fixture,
    // including the ones that do pass under AOT (see the #1109 entry below, which
    // exists precisely so the AOT-safe property-tool checks stay live).
    //
    // Keep this list honest: a stale entry is AOT coverage that is silently
    // switched off. Re-run the probe after framework changes and delete whatever
    // now passes. ValidateDefaultSkipPatterns() aborts the run when an entry stops
    // matching any registered fixture, so a rename cannot quietly turn a skip into
    // a no-op. It does NOT catch the opposite drift — an entry that still matches
    // but has started passing, or a wildcard that has grown to cover more fixtures
    // than intended. Only the probe finds those.
    //
    // When probing, beware the observer effect: diagnostic code that calls
    // typeof(T).GetMembers()/GetProperties() on a constant type roots reflection
    // metadata for T at compile time, so the probe can make the very thing it is
    // measuring start working. Confirm any AOT verdict with the probe removed.
    //
    // Override via REACTOR_AOT_SKIP=Pat1,Pat2 (no rebuild needed). Patterns
    // are exact-match or Prefix* wildcard. See docs/aot-support.md for the full
    // debugging workflow.
    private static readonly string[] DefaultAotSkipPatterns =
    {
        // -- UseObservableTree subscribes to nested INotifyPropertyChanged by
        // walking the model graph with Type.GetProperties (see
        // ObservableTreeTracker.CreateInpcCandidateProperties). The fixture's
        // POCO model is not rooted for PublicProperties under AOT, so the walk
        // finds no nested INPC source and the deep-mutation assertion fails
        // (no native crash; the assertion fails inside the fixture). --
        "CoreCov2_UseObservableTreeHook",

        // -- Devtools / MCP server. The other Devtools fixtures run under AOT;
        // these are the ones whose reflection targets the trimmer removes.
        // `fire` resolves a named handler with GetMethods(DeclaredOnly) over the
        // *user* component, and under AOT that set comes back empty — the tool
        // answers unknown-event with reachableMethods: []. `state` reads hook
        // bookkeeping through Component's non-public Context property.
        // Devtools_FireRejectsLifecycleMethods deliberately still runs: `fire`
        // refuses forbidden names against a static HashSet *before* it reflects
        // (DevtoolsFireTool.FindHandler), so that path is trim-safe and is worth
        // keeping as live AOT coverage. See docs/aot-support.md. --
        "Devtools_FireInvokesNamedHandler",
        "Devtools_StateReadsHooks",

        // DependencyProperty discovery for the `properties` / `setProperty` tools
        // (issue #1109). WinUI's DP statics are CsWinRT-projected static
        // *properties*, and ILCompiler keeps no reflection metadata for them unless
        // something roots PublicProperties on those types, so the lookups find
        // nothing under AOT and every DP assertion fails. Measured, not assumed:
        // adding a probe that called typeof(Button).GetProperties() to the fixture
        // made all 112 DP statics visible again and turned the whole fixture green,
        // because DynamicallyAccessedMemberTypes.PublicProperties covers inherited
        // members too — deleting the probe reproduced the failures exactly. Rooting
        // the WinUI control hierarchy that way in every AOT app is not a trade a
        // diagnostic-only tool should make, so this stays skipped rather than
        // annotated. The AOT-safe rest of the property-tool surface (resources,
        // styles, ancestors, value formatting/parsing) lives in
        // Devtools_PropertyToolsExercise and Devtools_PropertyToolsReflectionExercise,
        // which are deliberately NOT skipped. --
        "Devtools_PropertyToolsDpDiscovery",

        // -- PropertyGrid auto-discovery walks user types via reflection and is
        // not AOT-safe by design. Documented in docs/aot-support.md. --
        "PropertyGrid_Category_ExpandCollapse",
        "PropertyGrid_Custom_Editor",
        "PropertyGrid_DeepNesting_RecordInRecord",
        "PropertyGrid_Immutable_Root",
        "PropertyGrid_Nested_ImmutableRecord",
        "PropertyGrid_Reflection_Categorized",
        "PropertyGrid_Reflection_EnumEditor",
        "PropertyGrid_Reflection_MutableObject",
        "PropertyGrid_Target_Switching",

        // -- Issue142 private-DP rendering: requires an IXamlMetadataProvider
        // for third-party / custom controls that is generated by the XAML
        // compiler only when the project has at least one .xaml file. AOT
        // tree-shaking removes the implicit metadata path even when one is
        // present, so these fixtures need a hand-written provider hooked up
        // before they can be re-enabled under AOT. --
        "Issue142_CustomControlPrivateDp_Renders",
        "Issue142_ThirdPartyControlPrivateDp_Renders",

        // -- Spec 049 Phase 3 component state migration is reflection-based and
        // JIT-only by design: the production entry point is gated on
        // HotReloadService.IsHotReloadLive (MetadataUpdater.IsSupported), which
        // is always false under NativeAOT, so the whole migration subsystem is
        // statically dead and trims away (spec 049 §8). This fixture bypasses
        // that gate to exercise the copier directly, but the reflective
        // field-copy cannot preserve state once the metadata is trimmed, so the
        // migration-success assertions only hold under JIT. The child
        // hook-order recovery fixture needs no reflection and still runs. --
        "HotReload_ComponentMigratesState",
    };

    private static string[] GetAotSkipPatterns()
    {
        var env = Environment.GetEnvironmentVariable("REACTOR_AOT_SKIP");
        if (string.IsNullOrWhiteSpace(env)) return DefaultAotSkipPatterns;
        var extra = env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Env var appends to defaults so callers can add new skips without
        // rebuilding the AOT binary.
        return DefaultAotSkipPatterns.Concat(extra).ToArray();
    }

    /// <summary>
    /// Throws when a committed <see cref="DefaultAotSkipPatterns"/> entry no longer
    /// matches any registered fixture.
    /// </summary>
    /// <remarks>
    /// A skip entry is a silent instrument: when a fixture is renamed or deleted, its
    /// stale entry keeps matching nothing and the run stays green, so the mute looks
    /// like it is still doing its job either way. That is the failure mode this whole
    /// list is meant to avoid, and the probe script only catches it when a human
    /// remembers to run it. Checking the patterns against the registry turns it into
    /// a signal on every selftest run instead.
    ///
    /// Throwing — rather than recording a failure and continuing — is deliberate. A
    /// stale committed skip is a configuration error, not a fixture result, and it has
    /// to fail both consumers of this run. The Host's own catch turns this into a TAP
    /// <c>Bail out!</c> and exit 1, which the AOT CI job surfaces directly; because it
    /// throws before any fixture runs, the MSTest wrapper sees exit 1 with an empty
    /// fixture map and raises an init error (<c>SelfTestBatch.RunSelfTests</c>), so the
    /// JIT job fails too. Merely incrementing the failure counter would leave the
    /// wrapper reporting every fixture as passed.
    ///
    /// Only the committed defaults are validated; ad-hoc REACTOR_AOT_SKIP additions are
    /// a debugging affordance and may legitimately name nothing.
    /// </remarks>
    private static void ValidateDefaultSkipPatterns(string[] allFixtures)
    {
        var stale = DefaultAotSkipPatterns
            .Where(p => !allFixtures.Any(f => MatchesPattern(f, p)))
            .ToArray();
        if (stale.Length == 0) return;

        throw new InvalidOperationException(
            $"STALE_AOT_SKIP: {stale.Length} DefaultAotSkipPatterns entr" +
            $"{(stale.Length == 1 ? "y matches" : "ies match")} no registered fixture: " +
            $"{string.Join(", ", stale)}. A renamed or deleted fixture leaves its skip " +
            $"entry muting nothing, which silently switches off AOT coverage. Delete the " +
            $"entry, or correct it to the fixture's current name.");
    }

    /// <summary>
    /// Matches one fixture name against one pattern: exact (ordinal) or a
    /// <c>Prefix*</c> wildcard. Factored out of <see cref="MatchesAnyPattern"/> so
    /// single-pattern callers don't have to wrap the pattern in a throwaway array.
    /// </summary>
    private static bool MatchesPattern(string name, string pattern) =>
        pattern.EndsWith('*')
            ? name.StartsWith(pattern[..^1], StringComparison.Ordinal)
            : string.Equals(name, pattern, StringComparison.Ordinal);

    private static bool MatchesAnyPattern(string name, string[] patterns)
    {
        foreach (var p in patterns)
        {
            if (MatchesPattern(name, p)) return true;
        }
        return false;
    }

    private static Task YieldLowPriorityAsync(DispatcherQueue dq)
    {
        // RunContinuationsAsynchronously: don't let the awaiting continuation
        // run inline on the dispatcher callback — that defeats the purpose of
        // yielding (we want the dispatcher to process other queued work — like
        // a render pass — between our SetResult and the continuation).
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // If TryEnqueue returns false (queue shut down / disposed), the
        // callback would never fire and the awaiter would hang forever.
        // Resolve the TCS synchronously in that case so the caller proceeds.
        if (!dq.TryEnqueue(DispatcherQueuePriority.Low, () => tcs.TrySetResult()))
            tcs.TrySetResult();
        return tcs.Task;
    }

    public static void RunAll()
    {
        StartHangWatchdog();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new ReactorApplication();
            var dispatcher = DispatcherQueue.GetForCurrentThread();

            var window = new Window { Title = "Reactor Self-Test" };
            window.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(800, 600));
            var harness = new Harness(window);

            dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    var allFixtures = SelfTestFixtureRegistry.AllFixtures;

                    // Before the TAP header and before any fixture runs: a stale
                    // committed skip is a configuration error, so it aborts the run
                    // via the catch below rather than being reported as a result.
                    // Validated against the full registry, not `fixtures`, so a
                    // --filter run doesn't flag every unrelated pattern.
                    ValidateDefaultSkipPatterns(allFixtures);

                    var fixtures = Filter is not null
                        ? allFixtures.Where(f => f.Contains(Filter, StringComparison.OrdinalIgnoreCase)).ToArray()
                        : allFixtures;
                    harness.SetupTitleBar(fixtures.Length);
                    window.Activate();
                    await Harness.Render(); // wait for initial layout

                    Console.WriteLine($"TAP version 14");
                    Console.WriteLine($"1..{fixtures.Length}");

                    // Suite clock. The whole run shares one process budget in the
                    // MSTest wrapper (SelfTestBatch.SelfTestTimeoutMs), and when it
                    // expires the wrapper kills the Host and blames whichever fixture
                    // was in flight — a POSITIONAL attribution that has been misread
                    // as a fixture bug across several PRs (issue #988). Emitting the
                    // elapsed time here gives the wrapper a duration it can gate on,
                    // and gives a triager the one number that separates "the suite ran
                    // out of budget" from "this fixture broke".
                    var suiteStart = Stopwatch.GetTimestamp();

                    int testIndex = 0;
                    bool isAot = !RuntimeFeature.IsDynamicCodeSupported;
                    var aotSkipPatterns = GetAotSkipPatterns();

                    // Fixtures that finished having run ZERO assertions. Two ways in: skipped
                    // wholesale by the AOT pattern list, or ran to completion emitting nothing but
                    // H.Skip directives. Both are reported PASSED by a consumer that only counts
                    // failures, which is issue #1061 — so the count goes in the trailer next to
                    // "# Total failures:", where a raw-TAP reader (the AOT CI job pipes straight to
                    // a .tap artifact and never goes through SelfTestBatch) can see it.
                    var skippedFixtures = new List<string>();

                    foreach (var fixtureName in fixtures)
                    {
                        testIndex++;
                        harness.UpdateProgress(testIndex, fixtureName);

                        // Force a low-priority dispatcher cycle so the title
                        // bar / segment bar repaint *before* the fixture runs.
                        // Otherwise a fixture that crashes the process leaves
                        // the title showing the previous fixture's name, which
                        // looks like a hang on the prior fixture.
                        await YieldLowPriorityAsync(dispatcher);

                        if (isAot && SkipAotPatterns && MatchesAnyPattern(fixtureName, aotSkipPatterns))
                        {
                            Console.WriteLine($"ok {testIndex} {fixtureName} # SKIP crashes/hangs under NativeAOT");
                            skippedFixtures.Add(fixtureName);
                            harness.MarkFixtureSkipped(testIndex - 1);
                            // Clear progress so the hang watchdog doesn't trip
                            // while we yield between skips.
                            Volatile.Write(ref _currentFixture, null);
                            // Yield at Low priority so WinUI layout / render
                            // / compositor work can actually run before the
                            // next iteration — Task.Yield runs at Normal,
                            // which lets a run of skips outpace rendering and
                            // makes the title bar look frozen.
                            await YieldLowPriorityAsync(dispatcher);
                            continue;
                        }

                        // Publish a baseline progress record *before* calling
                        // Create() so the watchdog can attribute a hang even
                        // if construction itself blocks. We'll upgrade the
                        // threshold once we know the fixture's own timeout.
                        var fixtureStart = Stopwatch.GetTimestamp();
                        Volatile.Write(ref _currentFixture,
                            new FixtureProgress(fixtureName, fixtureStart, HangTimeout));

                        int failuresBefore = harness.Failures;
                        int checksBefore = harness.Checks;
                        int skipsBefore = harness.Skips;
                        bool crashed = false;
                        try
                        {
                            var fixture = SelfTestFixtureRegistry.Create(fixtureName, harness);
                            if (fixture is null)
                            {
                                Console.WriteLine($"not ok {testIndex} {fixtureName} - fixture not found");
                                harness.RecordFailure();
                                crashed = true;
                            }
                            else
                            {
                                var timeout = fixture.FixtureTimeout;
                                // Per-fixture hang threshold: at least the
                                // global floor, and always strictly past the
                                // fixture's own graceful timeout so the
                                // watchdog only fires when that timeout
                                // couldn't (i.e. dispatcher truly stuck).
                                var perFixtureHang = timeout + HangSlack;
                                if (perFixtureHang < HangTimeout) perFixtureHang = HangTimeout;
                                Volatile.Write(ref _currentFixture,
                                    new FixtureProgress(fixtureName, fixtureStart, perFixtureHang));

                                Console.WriteLine($"# Running: {fixtureName}");
                                // Flush so the parent harness can attribute a
                                // hang to this fixture by name even if the
                                // child terminates abruptly afterward.
                                Console.Out.Flush();
                                var runTask = fixture.RunAsync();
                                var timeoutTask = Task.Delay(timeout);
                                var completed = await Task.WhenAny(runTask, timeoutTask);
                                if (completed == timeoutTask && !runTask.IsCompleted)
                                {
                                    completed = await Task.WhenAny(runTask, Task.Delay(100));
                                }

                                if (completed != runTask)
                                {
                                    crashed = true;
                                    Console.WriteLine($"not ok {testIndex} {fixtureName}_TIMEOUT - exceeded {timeout.TotalSeconds:0}s");
                                    Console.Out.Flush();
                                    harness.RecordFailure();
                                    // Issue #680: do NOT Environment.Exit(1) here. Mark this
                                    // fixture failed and break so the shared finally drives the
                                    // single teardown-free EndProcessImmediately() exit (ExitCode
                                    // = 1 via Failures > 0). Any orderly process-exit from inside
                                    // the live dispatcher loop, with the suite's accumulated XAML
                                    // graph still mounted, faults in framework teardown.
                                    Volatile.Write(ref _currentFixture, null);
                                    harness.MarkFixtureResult(testIndex - 1, false);
                                    break;
                                }
                                else
                                {
                                    await runTask; // surface any exception
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            crashed = true;
                            Console.WriteLine($"not ok {testIndex} {fixtureName}_CRASH - {ex.GetType().Name}: {ex.Message}");
                            Console.Error.WriteLine(ex.ToString());
                            harness.RecordFailure();
                        }
                        // Clear progress now that the fixture finished (or
                        // its dispatcher-bound timeout fired) so the watchdog
                        // doesn't blame this fixture for an inter-fixture gap.
                        Volatile.Write(ref _currentFixture, null);

                        // Three outcomes, not two (issue #1061). A fixture that ran to completion
                        // having emitted only H.Skip directives asserted NOTHING, yet it produces
                        // no `not ok` and so is indistinguishable from a real pass to anything that
                        // counts failures. Paint it amber and name it, so the healthy and the
                        // fully-degraded case are not the same green square.
                        bool failed = crashed || harness.Failures != failuresBefore;
                        bool assertedNothing = harness.Checks == checksBefore;
                        bool skippedSomething = harness.Skips > skipsBefore;
                        bool onlySkipped = !failed && assertedNothing && skippedSomething;

                        // Nothing at all — no check, no skip, no crash. Same defect as above with
                        // the one mitigating detail removed: a skip at least states a reason, so
                        // it is reported rather than failed. Silence states nothing, so there is
                        // no verdict to be generous about, and the wrapper has always called this
                        // a failure ("fixture emitted no TAP checks"). The Host called it a PASS,
                        // which meant the two disagreed and the raw-TAP consumers believed the
                        // Host: the AOT job greps `^not ok `, and a silent fixture emitted no such
                        // line, so it was invisible exactly where there is no wrapper to correct
                        // it. Emit the failure here so both sides — and the title bar — agree.
                        if (!failed && assertedNothing && !skippedSomething)
                        {
                            // Deliberately no `_CRASH`/`_TIMEOUT`-style suffix: the wrapper strips
                            // only those two, so any other decoration would attribute this to a
                            // fixture name that does not exist instead of to this one.
                            Console.WriteLine(
                                $"not ok {testIndex} {fixtureName} - fixture ran to completion " +
                                $"without emitting a single check or skip");
                            harness.RecordFailure();
                            failed = true;
                        }

                        if (onlySkipped)
                        {
                            int skipped = harness.Skips - skipsBefore;
                            Console.WriteLine(
                                $"# Fully skipped fixture: {fixtureName} - {skipped} check(s) " +
                                $"skipped, 0 assertions ran");
                            skippedFixtures.Add(fixtureName);
                            harness.MarkFixtureSkipped(testIndex - 1);
                        }
                        else
                        {
                            harness.MarkFixtureResult(testIndex - 1, !failed);
                        }

                        // Per-fixture wall clock, as a TAP comment. Comments are
                        // inert to every consumer (SelfTestBatch.ParseTap keys only
                        // on "# Running: " / "# Total failures:"; CI greps "^not ok ")
                        // so this is purely additive. It turns "the suite is slow"
                        // into a ranked list of who made it slow, which is what makes
                        // trimming the suite targeted instead of a blind sweep.
                        Console.WriteLine(FixtureTimeMarker + fixtureName + " " +
                            Stopwatch.GetElapsedTime(fixtureStart).TotalMilliseconds
                                .ToString("F0", global::System.Globalization.CultureInfo.InvariantCulture));
                    }

                    Console.WriteLine($"# Total failures: {harness.Failures}");

                    // Deliberately AFTER the failures trailer: `# Total failures:` is the
                    // documented discriminator for "the Host reached the end of its run"
                    // (TESTING.md), and SelfTestBatch keys `sawTotalFailures` on it, so nothing may
                    // come between it and the end of a healthy run's fixture output. This line is
                    // the answer to "`# Total failures: 0` — but did anything actually assert?".
                    Console.WriteLine($"# Total skipped fixtures: {skippedFixtures.Count}");
                    if (skippedFixtures.Count > 0)
                        Console.WriteLine($"# Skipped fixture list: {string.Join(", ", skippedFixtures)}");
                    Console.WriteLine(SuiteElapsedMarker +
                        Stopwatch.GetElapsedTime(suiteStart).TotalSeconds
                            .ToString("F1", global::System.Globalization.CultureInfo.InvariantCulture));
                    harness.FinalizeTaskbarProgress();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Bail out! {ex.GetType().Name}: {ex.Message}");
                    Console.Error.WriteLine(ex.ToString());
                    harness.RecordFailure();
                }
                finally
                {
                    EndProcessImmediately(harness.Failures > 0 ? 1 : 0);
                }
            });
        });
    }

    /// <summary>
    /// Ends the self-test process **without running any WinUI or CLR teardown**.
    /// <para>
    /// The harness reuses a single <see cref="Window"/> across ~600 fixtures and
    /// never disposes the <c>ReactorHost</c>s it creates, and the windowing
    /// fixtures open real <c>ReactorWindow</c>s with custom title bars. Every
    /// orderly process-exit path walks that accumulated, still-live XAML object
    /// graph and trips a Microsoft.UI.Xaml / Microsoft.UI.Windowing framework
    /// use-after-free during teardown (issue #680):
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="Environment.Exit(int)"/> → <c>ExitProcess</c> runs the
    /// loader's TLS destructors, which destroy live <c>DependencyObject</c>s and
    /// dereference the XAML core's already-freed tear-off map
    /// (<c>TearoffMemoryInfoPrivate::Discard</c>) → 0xC0000005.</item>
    /// <item><see cref="Application.Exit"/> tears the windows down in order, but
    /// <c>CTitleBar::Uninitialize</c> double-releases the caption-buttons UI
    /// Automation provider (<c>CTitleBarCaptionButtonsFragmentProvider</c>),
    /// which the suite's windowing fixtures created via
    /// <c>OverlappedPresenter.SetBorderAndTitleBar</c> → 0xC0000005 escalated to
    /// STATUS_FATAL_USER_CALLBACK_EXCEPTION → fast-fail 0xC0000409.</item>
    /// </list>
    /// <para>
    /// Both faults live in framework teardown the harness cannot make safe from
    /// managed code, so — once the TAP stream is flushed — we
    /// <c>TerminateProcess</c> ourselves with the captured exit code. That kills
    /// every thread immediately, running neither the loader's TLS destructors nor
    /// WinUI's window-close cascade, so neither teardown bug can fire. A real
    /// Reactor app never accumulates this state and keeps exiting via the orderly
    /// <c>ReactorApp.SafeExit</c> / <see cref="Application.Exit"/> path.
    /// </para>
    /// </summary>
    private static void EndProcessImmediately(int exitCode)
    {
        // Idempotency latch. EndProcessImmediately is invoked once, from the run's
        // finally; the per-fixture timeout path doesn't call it directly — it marks
        // the fixture failed and breaks into that same finally. The latch is
        // defensive so any future second exit path still terminates exactly once.
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
            return;
        // Set after the latch so that if a second exit path is ever added, the
        // first caller's code is the one the process terminates with.
        ExitCode = exitCode;

        // Flush TAP/stderr to the OS pipe before the hard kill — TerminateProcess
        // discards anything still sitting in the managed stream buffers. Flushing a
        // closed/redirected stdio pipe throws IOException, and a stream already torn
        // down by an in-flight exit throws ObjectDisposedException; both are expected
        // on this emergency path, so we trace and continue rather than rethrow.
        try { Console.Out.Flush(); }
        catch (IOException ex) { Debug.WriteLine($"stdout flush failed during exit, ignored: {ex.Message}"); }
        catch (ObjectDisposedException ex) { Debug.WriteLine($"stdout flush failed during exit, ignored: {ex.Message}"); }
        try { Console.Error.Flush(); }
        catch (IOException ex) { Debug.WriteLine($"stderr flush failed during exit, ignored: {ex.Message}"); }
        catch (ObjectDisposedException ex) { Debug.WriteLine($"stderr flush failed during exit, ignored: {ex.Message}"); }

        // Immediate, teardown-free termination (see the remarks above). -1 is the
        // Win32 current-process pseudo-handle, so no separate GetCurrentProcess
        // interop is needed. On success this never returns — the OS tears the
        // process down abruptly, running no TLS destructors or window-close cascade
        // (the whole point). Reaching the lines below therefore means the syscall
        // itself failed (e.g. blocked by policy); record why before falling back.
        if (!TerminateProcess(CurrentProcessPseudoHandle, unchecked((uint)exitCode)))
            Debug.WriteLine($"TerminateProcess failed: 0x{Marshal.GetLastWin32Error():X8}");

        // Last resort: force a managed exit so a Host that somehow survived the
        // kill still leaves with the captured code rather than running on. This
        // path may itself trip the #680 teardown fault, but an abrupt exit beats a
        // hung Host.
        Environment.Exit(exitCode);
    }

    // The Win32 current-process pseudo-handle, (HANDLE)-1. Passing it directly
    // avoids a second P/Invoke just to fetch the current process handle.
    private static readonly nint CurrentProcessPseudoHandle = -1;

    // The single retained Win32 import. No managed API gives a teardown-free exit
    // that ALSO carries a specific 0/1 exit code: Environment.Exit runs the loader's
    // TLS destructors (the issue #680 fault we are dodging), Environment.FailFast
    // leaves a fail-fast exit code (0xC0000409) plus a WER dump on every run, and
    // Process.Kill forces exit code -1 — each would defeat the fix or trip the
    // HostProcessExitsCleanly_NoTeardownCrash regression guard. TerminateProcess is
    // the only mechanism that terminates immediately with the exact captured code.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(nint hProcess, uint uExitCode);

    private static void StartHangWatchdog()
    {
        if (HangTimeout <= TimeSpan.Zero) return;
        var thread = new Thread(HangWatchdogLoop)
        {
            IsBackground = true,
            Name = "Reactor.SelfTest.HangWatchdog",
        };
        thread.Start();
    }

    private static void HangWatchdogLoop()
    {
        // Sleep small slices so disabling-via-debugger-attach takes effect
        // quickly. Polling 1Hz is plenty: HangTimeout is measured in seconds.
        var pollMs = 1000;
        while (true)
        {
            try { Thread.Sleep(pollMs); }
            catch (ThreadInterruptedException) { return; }

            // Auto-disable when a debugger is attached: developers stepping
            // through a fixture would otherwise trip the watchdog.
            if (Debugger.IsAttached) continue;

            var progress = Volatile.Read(ref _currentFixture);
            if (progress is null) continue;

            var elapsed = Stopwatch.GetElapsedTime(progress.StartTimestamp);
            if (elapsed < progress.HangThreshold) continue;

            // We are past the per-fixture hang threshold and the dispatcher
            // hasn't moved on. Emit a structured signal, flush, and FailFast
            // so a Watson/.NET minidump is produced (when DOTNET_DbgEnableMiniDump=1).
            var elapsedSec = (int)elapsed.TotalSeconds;
            var message =
                $"Bail out! HANG_DETECTED: {progress.Name} ran {elapsedSec}s " +
                $"without progress — UI dispatcher unresponsive. " +
                $"Rerun with --no-aot-skip --filter {progress.Name} and " +
                $"DOTNET_DbgEnableMiniDump=1 to capture a dump for analysis.";
            try
            {
                Console.WriteLine(message);
                Console.Out.Flush();
                Console.Error.WriteLine(message);
                Console.Error.Flush();
            }
            catch { /* swallow IO errors — we're about to FailFast anyway */ }

            // FailFast: synchronous, dumpable termination. Preferred over
            // Environment.Exit (no dump) and Process.Kill (no chance to flush).
            Environment.FailFast(message);
        }
    }
}
