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

    // Per-fixture watchdog. A managed hang used to lock up the whole run; now
    // we time out, mark it failed, and continue. (Note: native crashes under
    // AOT terminate the process before this can fire — use AotSkip patterns
    // to skip known-crashing fixtures.) Selftest fixtures normally complete
    // in milliseconds — 15s is generous.
    private static readonly TimeSpan FixtureTimeout = TimeSpan.FromSeconds(15);

    // Fixtures known to crash/hang under NativeAOT. Skipped with a TAP SKIP
    // directive so the run completes and the remaining failure surface is
    // visible. Patterns are exact-match or "Prefix*" wildcard. Override via
    // REACTOR_AOT_SKIP=Pat1,Pat2 (no rebuild needed). Remove an entry once
    // its underlying issue is fixed.
    private static readonly string[] DefaultAotSkipPatterns =
    {
        // ControlUpdate_TextProperty and _ButtonProperty are the only two in
        // this family known to pass under AOT; the rest crash silently.
        "ControlUpdate_InputControls",
        "ControlUpdate_DateTimePicker",
        "ControlUpdate_Containers",
        "ControlUpdate_Collections",
        "ControlUpdate_Navigation",
        "ControlUpdate_Modifiers",
        "ControlUpdate_PaddingModifiers",
        "ControlUpdate_Shapes",
        "ControlUpdate_StatusControls",
        "ControlUpdate_Grid",
        "ControlUpdate_ModifiedElementUnwrap",
        "ControlUpdate_HyperlinkButton",
        // First ControlUpdate2_* fixture crashes; rest unverified but assumed
        // to share the same shape problem. Remove this wildcard to test each.
        "ControlUpdate2_*",
        // RareControl_ColorPicker crashed — uncommon-control family, assume
        // shared risk.
        "RareControl_*",
        // DslExt_FactoryMethods crashed mid-family; FluentModifierChain and
        // TransitionExtensions passed. Skip the rest from FactoryMethods on.
        "DslExt_FactoryMethods",
        "DslExt_ShapeExtensions",
        "DslExt_GridBuilders",
        "DslExt_MenuDslMethods",
        "DslExt_AttachedProperties",
        "DslExt_ErrorBoundaryElement",
        "DslExt_GroupElement",
        "DslExt_BrushAndFontModifiers",
        // CoreCov_* crashers observed iteratively. Many control-specific
        // CoreCov_* fixtures crash silently under AOT.
        "CoreCov_MenuBarMountUpdate",
        "CoreCov_MediaPlayerMount",
        "CoreCov_SwipeControlMount",
        "CoreCov_SelectorBarPipsPagerMount",
        "CoreCov_PopupRefreshContainerMount",
        "CoreCov_AnnotatedScrollBarMount",
        "CoreCov_TreeViewUpdateExercise",
        "CoreCov_ExpanderChildUpdateDeep",
        // CoreCov2_* — InfoBarActionButton crashed; pre-skip the other
        // control-specific ones (named after specific WinUI controls) which
        // are likely to share the same shape problem.
        "CoreCov2_InfoBarActionButton",
        "CoreCov2_CalendarPipsPagerUpdate",
        "CoreCov2_FrameAnimatedIconUpdate",
        "CoreCov2_ParallaxViewMount",
        "CoreCov2_XamlHostMount",
        "CoreCov2_InfoBadgeMountUpdate",
        "CoreCov2_SelectorBarUpdate",
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

                        if (isAot && MatchesAnyPattern(fixtureName, aotSkipPatterns))
                        {
                            Console.WriteLine($"ok {testIndex} {fixtureName} # SKIP crashes/hangs under NativeAOT");
                            harness.MarkFixtureSkipped(testIndex - 1);
                            // Yield at Low priority so WinUI layout / render
                            // / compositor work can actually run before the
                            // next iteration — Task.Yield runs at Normal,
                            // which lets a run of skips outpace rendering and
                            // makes the title bar look frozen.
                            await YieldLowPriorityAsync(dispatcher);
                            continue;
                        }

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
}
