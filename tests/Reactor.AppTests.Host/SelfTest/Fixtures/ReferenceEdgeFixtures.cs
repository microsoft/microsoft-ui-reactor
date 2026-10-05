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
}
