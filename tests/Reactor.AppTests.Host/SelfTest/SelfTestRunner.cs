using System.Diagnostics;
using System.Runtime.CompilerServices;
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
    /// When true (the default), <see cref="DefaultAotSkipPatterns"/> is honoured
    /// under NativeAOT — matching fixtures are skipped. Set to false (via
    /// <c>--no-aot-skip</c>) to run every fixture even under NativeAOT, for
    /// targeted repro of a hanging/crashing fixture together with
    /// <c>--filter &lt;name&gt;</c>. The off-dispatcher watchdog (see
    /// <see cref="HangTimeout"/>) still fires regardless.
    /// </summary>
    public static bool SkipAotPatterns { get; set; } = true;

    // Per-fixture watchdog. A managed hang used to lock up the whole run; now
    // we time out, mark it failed, and continue. (Note: native crashes under
    // AOT terminate the process before this can fire — use AotSkip patterns
    // to skip known-crashing fixtures.) Selftest fixtures normally complete
    // in milliseconds — 15s is generous.
    private static readonly TimeSpan FixtureTimeout = TimeSpan.FromSeconds(15);

    // Off-dispatcher hang watchdog. The in-band FixtureTimeout above relies on
    // the dispatcher processing a Task.Delay continuation, so it cannot fire
    // when a fixture synchronously blocks the UI thread. This second watchdog
    // runs on a background Thread (immune to dispatcher starvation) and
    // declares a hang after HangTimeout of no progress in the fixture loop.
    // Threshold is well past FixtureTimeout so it only catches the
    // dispatcher-starvation case. Override via REACTOR_SELFTEST_HANG_TIMEOUT_SECONDS;
    // set to 0 or a negative value to disable entirely (useful when attaching
    // a debugger). Also auto-disabled when Debugger.IsAttached.
    private static readonly TimeSpan HangTimeout = ResolveHangTimeout();

    private static TimeSpan ResolveHangTimeout()
    {
        var env = Environment.GetEnvironmentVariable("REACTOR_SELFTEST_HANG_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(env) && int.TryParse(env, out var s))
            return s <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(s);
        return TimeSpan.FromSeconds(60);
    }

    // Single immutable progress record — published atomically via
    // Volatile.Read/Write so the watchdog can never read a mixed
    // (new-name, old-timestamp) state.
    private sealed record FixtureProgress(string Name, long StartTimestamp);
    private static FixtureProgress? _currentFixture;

    // Fixtures known to assert-fail under NativeAOT, captured by running
    // tests/Reactor.AppTests.Host/probe-aot-skips.ps1 against the AOT-published
    // Host. As of WindowsAppSDK#6394 workaround (see Reactor.AppTests.Host.csproj
    // _CopyWinUIResourcesForAot target), all NATIVE_CRASH skips are gone — the
    // remaining failures map to reflection-heavy subsystems (Devtools/MCP,
    // PropertyGrid auto-discovery) plus two control-collection assertions and
    // the Issue142 XAML-metadata-provider edge cases.
    //
    // Each name was verified to fail in isolation; wildcards from earlier
    // skip-list iterations have been replaced with explicit names so that
    // newly-passing siblings re-enter the run automatically.
    //
    // Override via REACTOR_AOT_SKIP=Pat1,Pat2 (no rebuild needed). Patterns
    // are exact-match or Prefix* wildcard. Re-run the probe after framework
    // changes to find new stale skips. See docs/aot-support.md for the full
    // debugging workflow.
    private static readonly string[] DefaultAotSkipPatterns =
    {
        // -- Reactor framework, control-collection assertions still under
        // investigation (no native crash; assertion fails inside the fixture). --
        "ControlUpdate_Collections",
        "CoreCov2_UseObservableTreeHook",

        // -- Devtools / MCP server — JSON-RPC server uses reflection-heavy
        // tool discovery that is not AOT-safe. Documented in
        // docs/aot-support.md as a not-yet-AOT-clean subsystem. --
        "Devtools_ClickInvokesButton",
        "Devtools_ComponentsTool",
        "Devtools_FireInvokesNamedHandler",
        "Devtools_FireRejectsLifecycleMethods",
        "Devtools_FocusElement",
        "Devtools_InitializeHandshake",
        "Devtools_InvokeDirectPattern",
        "Devtools_LoggerWritesOneLinePerCall",
        "Devtools_McpServerProtocolEdges",
        "Devtools_NameSelectorMatchesButtonContent",
        "Devtools_PropertyToolsExercise",
        "Devtools_ScrollByAndInto",
        "Devtools_SelectListItem",
        "Devtools_StateReadsHooks",
        "Devtools_SwitchComponentInvalidatesIds",
        "Devtools_ToggleFlipsCheckBox",
        "Devtools_TreeFullView",
        "Devtools_TreeIdsUniqueAcrossSiblingsWithDifferentParents",
        "Devtools_TreeSelectorScope",
        "Devtools_TreeSummary",
        "Devtools_TypeSetsTextBox",
        "Devtools_UnknownSelectorStructuredError",
        "Devtools_VersionTool",
        "Devtools_WaitForTextChange",
        "Devtools_WaitForTimeout",
        "Devtools_WaitForTimeoutLoggedAsErr",
        "Devtools_WindowsTool",

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

    private static bool MatchesAnyPattern(string name, string[] patterns)
    {
        foreach (var p in patterns)
        {
            if (p.EndsWith('*'))
            {
                if (name.StartsWith(p[..^1], StringComparison.Ordinal)) return true;
            }
            else if (string.Equals(name, p, StringComparison.Ordinal))
            {
                return true;
            }
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
                    var fixtures = Filter is not null
                        ? allFixtures.Where(f => f.Contains(Filter, StringComparison.OrdinalIgnoreCase)).ToArray()
                        : allFixtures;
                    harness.SetupTitleBar(fixtures.Length);
                    window.Activate();
                    await Harness.Render(); // wait for initial layout

                    Console.WriteLine($"TAP version 14");
                    Console.WriteLine($"1..{fixtures.Length}");

                    int testIndex = 0;
                    bool isAot = !RuntimeFeature.IsDynamicCodeSupported;
                    var aotSkipPatterns = GetAotSkipPatterns();
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

                        // Publish progress to the off-dispatcher watchdog so
                        // it can identify the in-flight fixture if the
                        // dispatcher gets blocked.
                        Volatile.Write(ref _currentFixture,
                            new FixtureProgress(fixtureName, Stopwatch.GetTimestamp()));

                        int failuresBefore = harness.Failures;
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
                                Console.WriteLine($"# Running: {fixtureName}");
                                // Flush so the parent harness can attribute a
                                // hang to this fixture by name even if the
                                // child terminates abruptly afterward.
                                Console.Out.Flush();
                                var runTask = fixture.RunAsync();
                                var timeoutTask = Task.Delay(FixtureTimeout);
                                var completed = await Task.WhenAny(runTask, timeoutTask);
                                if (completed == timeoutTask)
                                {
                                    crashed = true;
                                    Console.WriteLine($"not ok {testIndex} {fixtureName}_TIMEOUT - exceeded {FixtureTimeout.TotalSeconds:0}s");
                                    harness.RecordFailure();
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
                        harness.MarkFixtureResult(testIndex - 1,
                            !crashed && harness.Failures == failuresBefore);
                    }

                    Console.WriteLine($"# Total failures: {harness.Failures}");
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
                    Console.Out.Flush();
                    Environment.Exit(harness.Failures > 0 ? 1 : 0);
                }
            });
        });
    }

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
            if (elapsed < HangTimeout) continue;

            // We are >= HangTimeout into a fixture and the dispatcher hasn't
            // moved on. Emit a structured signal, flush, and FailFast so a
            // Watson/.NET minidump is produced (when DOTNET_DbgEnableMiniDump=1).
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
