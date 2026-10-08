using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// The live half of <see cref="ReactorDiagnostics.GetReferenceEdges"/>: a reference whose
/// target is not mounted is reported pending (while the WinUI property reads null), and the
/// same edge flips to resolved — pointing at the real control — once the target mounts.
/// The projection itself is unit-tested in <c>ReactorDiagnosticsReferenceEdgesTests</c>.
/// </summary>
internal static class ReferenceEdgeFixtures
{
    internal class PendingThenResolved(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            Action<bool>? setShowTarget = null;
            host.Mount(ctx =>
            {
                var (showTarget, set) = ctx.UseState(false);
                setShowTarget = set;
                var target = ctx.UseElementRef<Button>();
                return VStack(
                    Button("refedge-source").XYFocusRight(target),
                    showTarget ? Button("refedge-target").Ref(target) : TextBlock("refedge-placeholder"));
            });
            await Harness.Render();

            var source = H.FindControl<Button>(b => b.Content as string == "refedge-source");
            H.Check("RefEdges_SourceMounted", source is not null);
            if (source is null) return;

            var pending = ReactorDiagnostics.GetReferenceEdges(source);
            var edge = pending.SingleOrDefault(e => e.Property == "XYFocusRight");
            H.Check("RefEdges_PendingReported",
                edge is { IsResolved: false, Target: null, ExpectedTargetTypeName: "Button", IsList: false });
            // The point of the API: WinUI itself has nothing to show for a pending edge.
            H.Check("RefEdges_WinUIPropertyIsNullWhilePending", source.XYFocusRight is null);

            setShowTarget!(true);
            await Harness.Render();

            var target = H.FindControl<Button>(b => b.Content as string == "refedge-target");
            var resolved = ReactorDiagnostics.GetReferenceEdges(source).SingleOrDefault(e => e.Property == "XYFocusRight");
            H.Check("RefEdges_ResolvedToTheMountedTarget",
                target is not null && resolved is { IsResolved: true, ExpectedTargetTypeName: "Button" } && ReferenceEquals(resolved.Target, target));

            var untouched = H.FindControl<TextBlock>(t => t.Text == "refedge-placeholder");
            H.Check("RefEdges_PlaceholderGone", untouched is null);
            H.Check("RefEdges_NoEdgesOnPlainControl", target is not null && ReactorDiagnostics.GetReferenceEdges(target).Count == 0);
        }
    }

    /// <summary>
    /// An AutomationId <c>.LabeledBy("id")</c> whose target never appears stays reported as a
    /// pending edge after the control loads — even with source mapping off, where the control is
    /// not tagged and the deferred request itself is the only record of what the author wrote.
    /// </summary>
    internal class PendingAutomationIdSurvivesLoaded(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled;
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = false;
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx => VStack(TextBox(placeholderText: "refedge-labelled").LabeledBy("refedge-missing-label")));
                await Harness.Render();
                await Harness.Render(50);

                var box = H.FindControl<TextBox>(t => t.PlaceholderText == "refedge-labelled");
                H.Check("RefEdges_LabelledMounted", box is not null && box.IsLoaded);
                if (box is null) return;

                var edge = ReactorDiagnostics.GetReferenceEdges(box).SingleOrDefault(e => e.Property == "LabeledBy");
                H.Check("RefEdges_PendingAutomationIdAfterLoaded",
                    edge is { TargetAutomationId: "refedge-missing-label", IsResolved: false, Target: null });

                // Unmount drops the pending request: a retained, unpooled control (a CheckBox, so
                // the pool's own reset is not what clears it) reports no edge afterwards.
                host.Mount(ctx => VStack(CheckBox(label: "refedge-unmount-check").LabeledBy("refedge-missing-label2")));
                await Harness.Render();
                await Harness.Render(50);
                var check = H.FindControl<CheckBox>(c => c.Content as string == "refedge-unmount-check");
                H.Check("RefEdges_PendingBeforeUnmount",
                    check is not null && ReactorDiagnostics.GetReferenceEdges(check).Any(e => e.TargetAutomationId == "refedge-missing-label2"));
                if (check is null) return;
                host.Mount(ctx => TextBlock("refedge-replaced"));
                await Harness.WaitFor(() => H.FindControl<TextBlock>(t => t.Text == "refedge-replaced") is not null, maxPasses: 16, perPassMs: 10);
                H.Check("RefEdges_NoPendingAfterUnmount", ReactorDiagnostics.GetReferenceEdges(check).Count == 0);
            }
            finally
            {
                Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = previous;
            }
        }
    }

    /// <summary>
    /// A resolved AutomationId <c>.LabeledBy("id")</c> whose label then unmounts: the live
    /// <c>LabeledBy</c> property still holds the detached label, but the edge must no longer be
    /// reported as resolved to it.
    /// </summary>
    internal class ResolvedAutomationIdThenUnmounted(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
#if REACTOR_SOURCEMAP
            var previous = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled;
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = true;
            try
            {
                Action<bool>? setShow = null;
                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (show, set) = ctx.UseState(true);
                    setShow = set;
                    return VStack(
                        show ? TextBlock("refedge-label-text").AutomationId("refedge-live-label") : Button("refedge-gone"),
                        TextBox(placeholderText: "refedge-labelled-live").LabeledBy("refedge-live-label"));
                });
                await Harness.Render();
                await Harness.Render(50);

                var box = H.FindControl<TextBox>(t => t.PlaceholderText == "refedge-labelled-live");
                var label = H.FindControl<TextBlock>(t => t.Text == "refedge-label-text");
                H.Check("RefEdges_LiveLabelMounted", box is not null && label is not null);
                if (box is null || label is null || setShow is null) return;

                var before = ReactorDiagnostics.GetReferenceEdges(box).SingleOrDefault(e => e.Property == "LabeledBy");
                H.Check("RefEdges_AutomationIdResolved",
                    before is { TargetAutomationId: "refedge-live-label", IsResolved: true } && ReferenceEquals(before.Target, label));

                setShow(false);
                await Harness.WaitFor(() => !label.IsLoaded, maxPasses: 16, perPassMs: 10);
                H.Check("RefEdges_LabelUnmounted", !label.IsLoaded);

                var after = ReactorDiagnostics.GetReferenceEdges(box).SingleOrDefault(e => e.Property == "LabeledBy");
                H.Check("RefEdges_UnmountedLabelNotResolved",
                    after is { TargetAutomationId: "refedge-live-label", IsResolved: false, Target: null });

                // The source itself unmounts after resolving: a retained, unpooled control keeps
                // its tag, but no edge is rebuilt from it.
                host.Mount(ctx => VStack(
                    TextBlock("refedge-src-label").AutomationId("refedge-src-label-id"),
                    CheckBox(label: "refedge-src-check").LabeledBy("refedge-src-label-id")));
                await Harness.Render();
                await Harness.Render(50);
                var srcCheck = H.FindControl<CheckBox>(c => c.Content as string == "refedge-src-check");
                H.Check("RefEdges_SourceResolvedBeforeUnmount",
                    srcCheck is not null && ReactorDiagnostics.GetReferenceEdges(srcCheck) is [{ IsResolved: true, TargetAutomationId: "refedge-src-label-id" }]);
                if (srcCheck is null) return;
                host.Mount(ctx => TextBlock("refedge-src-replaced"));
                await Harness.WaitFor(() => !srcCheck.IsLoaded, maxPasses: 16, perPassMs: 10);
                H.Check("RefEdges_UnmountedSourceReportsNoEdge", ReactorDiagnostics.GetReferenceEdges(srcCheck).Count == 0);
            }
            finally
            {
                Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = previous;
            }
#else
            H.Skip("RefEdges_UnmountedLabelNotResolved",
                "assembly built without REACTOR_SOURCEMAP (Release) - the control is not tagged, so the resolved AutomationId form is not recoverable");
            await Task.CompletedTask;
#endif
        }
    }
}