using System.Linq;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 057 §11 Phase 3 (3.1) — end-to-end proof that the devtools reference-graph
/// overlay (<see cref="ReferenceOverlay"/>) builds correct edges and diagnostics
/// from the per-control <c>ReferenceEdgeBag</c> state of a <em>live</em> WinUI tree.
/// The headless <c>ReferenceOverlayTests</c> pin the serialization shape and the
/// cycle/unresolved logic on hand-built edge lists; these fixtures close the loop
/// by walking real reconciled controls the way the <c>references</c> MCP tool does.
/// </summary>
internal static class ReferenceOverlaySelfHostFixtures
{
    private static WinUI.Button? FindButton(Harness h, string content) =>
        h.FindControl<WinUI.Button>(b => b.Content is string s && s == content);

    /// <summary>
    /// Walks the live window content exactly like the <c>references</c> MCP tool and
    /// returns both the overlay result and the walker (so callers can map a live
    /// control back to the node id the walker assigned it).
    /// </summary>
    private static (ReferenceGraphResult Graph, TreeWalker Walker) BuildOverlay(Harness h)
    {
        var walker = new TreeWalker("main", new NodeRegistry());
        walker.Walk(h.Window.Content);
        return (ReferenceOverlay.Build(walker, "main"), walker);
    }

    private static string? IdOf(TreeWalker walker, FrameworkElement? fe) =>
        fe is not null && walker.ElementIds.TryGetValue(fe, out var id) ? id : null;

    internal sealed class ResolvedModifierEdge(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<FrameworkElement>? labelRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                labelRef = ctx.UseElementRef<FrameworkElement>();
                return VStack(
                    Button("Ovl_Resolved_Label", () => { }).Ref(labelRef) with { Key = "Ovl_Resolved_Label" },
                    Button("Ovl_Resolved_Input", () => { }).LabeledBy(labelRef) with { Key = "Ovl_Resolved_Input" });
            });

            await Harness.Render();
            H.Check("ReferenceOverlay_ResolvedModifierEdge_BuildsResolvedEdge", await Harness.WaitFor(() =>
            {
                var input = FindButton(H, "Ovl_Resolved_Input");
                var label = FindButton(H, "Ovl_Resolved_Label");
                if (input is null || label is null) return false;

                var (graph, walker) = BuildOverlay(H);
                var edge = graph.Edges.SingleOrDefault(e => e.Label == "LabeledBy");
                return edge is not null
                    && edge.From == IdOf(walker, input)
                    && edge.To == IdOf(walker, label)
                    && edge.Resolved
                    && edge.Kind == "scalar"
                    && graph.Diagnostics.Count == 0;
            }));
        }
    }

    internal sealed class UnresolvedDiagnostic(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<FrameworkElement>? labelRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                labelRef = ctx.UseElementRef<FrameworkElement>();
                // The label target is intentionally never mounted, so the input's
                // LabeledBy reference cell stays null — a perpetually-unresolved ref.
                return VStack(
                    Button("Ovl_Unresolved_Input", () => { }).LabeledBy(labelRef) with { Key = "Ovl_Unresolved_Input" });
            });

            await Harness.Render();
            H.Check("ReferenceOverlay_UnresolvedDiagnostic_FlagsNullTarget", await Harness.WaitFor(() =>
            {
                var input = FindButton(H, "Ovl_Unresolved_Input");
                if (input is null) return false;

                var (graph, walker) = BuildOverlay(H);
                var inputId = IdOf(walker, input);
                var edge = graph.Edges.SingleOrDefault(e => e.Label == "LabeledBy");
                var diag = graph.Diagnostics.SingleOrDefault(d => d.Kind == "unresolved");
                return edge is not null
                    && !edge.Resolved
                    && edge.To is null
                    && inputId is not null
                    && diag is not null
                    && diag.NodeIds.Contains(inputId)
                    && graph.Diagnostics.All(d => d.Kind != "cycle");
            }));
        }
    }

    internal sealed class CycleDiagnostic(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<FrameworkElement>? aRef = null, bRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                aRef = ctx.UseElementRef<FrameworkElement>();
                bRef = ctx.UseElementRef<FrameworkElement>();
                // A → B and B → A via XYFocusRight: a resolved 2-cycle.
                return VStack(
                    Button("Ovl_Cycle_A", () => { }).Ref(aRef).XYFocusRight(bRef) with { Key = "Ovl_Cycle_A" },
                    Button("Ovl_Cycle_B", () => { }).Ref(bRef).XYFocusRight(aRef) with { Key = "Ovl_Cycle_B" });
            });

            await Harness.Render();
            H.Check("ReferenceOverlay_CycleDiagnostic_DetectsResolvedRing", await Harness.WaitFor(() =>
            {
                var a = FindButton(H, "Ovl_Cycle_A");
                var b = FindButton(H, "Ovl_Cycle_B");
                if (a is null || b is null) return false;

                var (graph, walker) = BuildOverlay(H);
                var aId = IdOf(walker, a);
                var bId = IdOf(walker, b);
                var cycle = graph.Diagnostics.SingleOrDefault(d => d.Kind == "cycle");
                return aId is not null && bId is not null
                    && graph.Edges.Count(e => e.Label == "XYFocusRight" && e.Resolved) == 2
                    && cycle is not null
                    && cycle.NodeIds.Count == 2
                    && cycle.NodeIds.Contains(aId)
                    && cycle.NodeIds.Contains(bId);
            }));
        }
    }
}
