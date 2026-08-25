using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Regression guard for the <c>_reconcileTraceDepth</c> bookkeeping in
/// <c>Reconciler.Reconcile</c>.
///
/// The counter is incremented by every tracing-enabled call but only the
/// outermost call emits. When the decrement was also gated on <c>emitTrace</c>,
/// a pass containing a nested <c>Reconcile()</c> left the counter above zero
/// forever, so every later top-level pass saw a non-zero pre-increment and
/// silently stopped emitting ReconcileStart/ReconcileStop.
///
/// This has to run as a selftest rather than a unit test: it must drive the
/// real <c>Reconciler.Reconcile</c> entry point over a live control tree, and
/// the existing <c>ReactorEventSourceCoverageTests</c> only call
/// <c>ReconcileStart</c>/<c>ReconcileStop</c> directly, which cannot observe
/// this defect.
/// </summary>
internal class ReconcileTraceDepth_TopLevelSpansSurviveNestedPasses(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        using var listener = new ReconcileSpanListener();
        // Informational + the Reconcile keyword is exactly the gate
        // Reconciler.Reconcile tests with IsEnabled.
        listener.EnableEvents(
            ReactorEventSource.Log,
            EventLevel.Informational,
            ReactorEventSource.Keywords.Reconcile);

        var host = H.CreateHost();
        host.Mount(ctx =>
        {
            var (n, setN) = ctx.UseState(0);
            // A child Component is what makes Reconcile() re-enter itself:
            // UpdateComponent dereferences the Border identity anchor and
            // calls Reconcile again for the rendered child
            // (Reconciler.cs:1847). Plain elements recurse through the child
            // reconciler instead and never re-enter this counter, so a tree
            // of bare VStack/TextBlock cannot reproduce the leak.
            return VStack(4,
                TextBlock($"count {n}"),
                // Props must change each pass: a propless child is skipped by
                // the shallow-equality short-circuit and never reaches the
                // nested Reconcile call, which would make this fixture vacuous.
                Component<TraceDepthChild, int>(n),
                Button("bump", () => setN(n + 1)));
        });

        await Harness.Render();

        // The mount pass must have produced a span at all; if this is zero the
        // listener/keyword wiring is wrong and the rest of the fixture would
        // pass vacuously.
        int afterMount = listener.StartCount;
        H.Check("ReconcileTrace_MountEmittedSpan", afterMount > 0);
        Console.WriteLine($"# trace diag: afterMount starts={afterMount} stops={listener.StopCount}");

        // Second and third top-level passes. Before the fix these emitted
        // nothing, because the mount pass left the depth counter above zero.
        H.ClickButton("bump");
        await Harness.Render();
        int afterFirstUpdate = listener.StartCount;
        var text1 = H.FindControl<Microsoft.UI.Xaml.Controls.TextBlock>(
            t => t.Text.StartsWith("count", StringComparison.Ordinal))?.Text;

        H.ClickButton("bump");
        await Harness.Render();
        int afterSecondUpdate = listener.StartCount;
        var text2 = H.FindControl<Microsoft.UI.Xaml.Controls.TextBlock>(
            t => t.Text.StartsWith("count", StringComparison.Ordinal))?.Text;

        // Guard: if the tree did not actually advance, the span counts below
        // would be comparing passes that never happened.
        H.Check("ReconcileTrace_TreeAdvancedPass2", text1 == "count 1");
        H.Check("ReconcileTrace_TreeAdvancedPass3", text2 == "count 2");

        // The direct invariant: the depth counter must be back at zero between
        // passes. With the decrement gated on `emitTrace` this reads 3 here,
        // because each pass's nested component Reconcile incremented without
        // a matching decrement.
        int depth = host.Reconciler.ReconcileTraceDepthForTests;
        Console.WriteLine($"# trace diag: depth={depth}");
        H.Check("ReconcileTrace_DepthReturnedToZero", depth == 0);

        H.Check("ReconcileTrace_SecondTopLevelPassEmitted", afterFirstUpdate > afterMount);
        H.Check("ReconcileTrace_ThirdTopLevelPassEmitted", afterSecondUpdate > afterFirstUpdate);
        Console.WriteLine($"# trace diag: afterFirstUpdate={afterFirstUpdate} afterSecondUpdate={afterSecondUpdate} stops={listener.StopCount}");

        // Start/Stop must stay paired — a decrement that ran on a call which
        // never emitted Start would show up here as an imbalance.
        H.Check("ReconcileTrace_StartStopBalanced",
            listener.StopCount == listener.StartCount);
    }

    private sealed class ReconcileSpanListener : EventListener
    {
        private int _starts;
        private int _stops;
        public int StartCount => Volatile.Read(ref _starts);
        public int StopCount => Volatile.Read(ref _stops);

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e.EventName == nameof(ReactorEventSource.ReconcileStart))
                Interlocked.Increment(ref _starts);
            else if (e.EventName == nameof(ReactorEventSource.ReconcileStop))
                Interlocked.Increment(ref _stops);
        }
    }
}

/// <summary>
/// Child component for
/// <see cref="ReconcileTraceDepth_TopLevelSpansSurviveNestedPasses"/>. Its only
/// job is to exist as a component node so the parent's pass re-enters
/// <c>Reconcile</c>.
/// </summary>
internal sealed class TraceDepthChild : Component<int>
{
    public override Element Render() => TextBlock($"nested component {Props}");
}
