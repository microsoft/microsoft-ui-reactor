using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// The per-test teardown shared by every E2E base class that drives the desktop through
/// <see cref="WinAppUi"/>.
/// </summary>
/// <remarks>
/// This exists as one function rather than a copied <c>[TestCleanup]</c> body because the two
/// halves have to stay together. Recording the invocation count is only reporting, but releasing
/// the UI turn is load-bearing: every <c>winapp</c> child is stamped with this process's workflow
/// id (see <see cref="WinAppUi.WorkflowId"/>), and a named workflow keeps the desktop through a
/// post-command idle grace. That is exactly what we want *inside* a test — it stops a concurrent
/// agent interleaving between our click and the assertion that reads the result — but between
/// tests it would hold the desktop across a much longer gap for no benefit. A base class that
/// records but forgets to yield therefore blocks other workflows silently, which is a
/// throughput bug nothing fails on.
/// </remarks>
internal static class E2ETestCleanup
{
    /// <summary>
    /// Reports how many <c>winapp.exe</c> processes the finished test spawned, then hands the
    /// desktop back if it took a turn at all.
    /// </summary>
    /// <param name="testContext">MSTest context for the finished test, if injected.</param>
    /// <param name="invocationCountAtStart">
    /// <see cref="WinAppUi.InvocationCount"/> sampled in the matching test initialize.
    /// </param>
    /// <param name="stopwatch">Started in the matching test initialize; may be null.</param>
    /// <param name="fallbackTestName">Used when MSTest did not inject a context.</param>
    internal static void RecordAndYield(
        TestContext? testContext,
        long invocationCountAtStart,
        Stopwatch? stopwatch,
        string fallbackTestName)
    {
        var spawned = WinAppUi.InvocationCount - invocationCountAtStart;
        var seconds = stopwatch?.Elapsed.TotalSeconds ?? 0;
        var name = testContext?.TestName ?? fallbackTestName;

        testContext?.WriteLine($"winapp-invocations={spawned}");
        WinAppMetrics.Record(name, spawned, seconds);

        // Skipped when the test never touched winapp, so a headless test in this assembly doesn't
        // pay a process spawn to release a turn it never took.
        if (spawned > 0)
            WinAppUi.ReleaseUiTurn();
    }
}
