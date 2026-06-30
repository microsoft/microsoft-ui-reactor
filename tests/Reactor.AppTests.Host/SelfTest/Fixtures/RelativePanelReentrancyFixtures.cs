using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using static Microsoft.UI.Reactor.Factories;
using WinXC = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #676 — coverage hardening (no behavioural defect). Live-control proof
/// that the RelativePanel attached-DP hook path
/// (<c>RelativePanelElement.ApplyRelativePanelAttachedProps</c> in
/// <c>src/Reactor/Core/PanelAttachedHooks.cs</c>) does NOT trigger a reentrant
/// re-render storm: writing the sibling-referencing + panel-alignment attached
/// dependency properties (<c>RelativePanel.SetRightOf</c>/<c>SetBelow</c>/… via
/// the two-pass <c>PerChildAttachedAfterAll</c> hook) must not re-enter reconcile
/// and schedule further renders in a feedback loop.
///
/// The two-pass hook runs only when the RelativePanel subtree actually reconciles
/// (<c>V1HandlerAdapter.DispatchChildrenUpdate</c>); a memo-skipped subtree never
/// re-runs it. We exploit that to get a purely test-side observable: the panel is
/// wrapped in a child <see cref="Component{TProps}"/> whose props are a value
/// <c>LayoutVersion</c> plus a reference-stable <see cref="RenderTally"/>. Record
/// structural equality means an unchanged version memo-skips, so the tally is a
/// faithful proxy for "the panel subtree reconciled and rebuilt its name-map".
/// No hot reconciler file is touched and no <c>InternalsVisibleTo</c> accessor is
/// needed — the counter is owned by the component under test.
/// </summary>
internal static class RelativePanelReentrancyFixtures
{
    // Reference-stable render tally the panel component owns and increments. Its
    // identity never changes across renders, so it compares reference-equal and
    // never itself drives a re-render — it only lets the test observe how many
    // times the panel subtree actually reconciled, without static mutable state.
    private sealed class RenderTally
    {
        public int Count;
    }

    // Value LayoutVersion + reference-stable Tally. Record structural equality →
    // unchanged version + same Tally instance compares equal → the reconciler
    // memo-skips, so ApplyRelativePanelAttachedProps does NOT re-run.
    private sealed record RelPanelProps(int LayoutVersion, RenderTally Tally);

    private sealed class RelPanelComponent : Component<RelPanelProps>
    {
        public override Element Render()
        {
            Props.Tally.Count++;

            // Three children positioned purely through attached DPs. A is the
            // panel anchor; B references A (rightOf in v0, below in v1 — the
            // "children actually change" case); C is below+aligned-left with A.
            Element a = TextBlock("A")
                .RelativePanel(name: "A", alignLeftWithPanel: true, alignTopWithPanel: true)
                .AutomationId("rpr_a").WithKey("a");

            Element b = Props.LayoutVersion == 0
                ? TextBlock("B").RelativePanel(name: "B", rightOf: "A").AutomationId("rpr_b").WithKey("b")
                : TextBlock("B").RelativePanel(name: "B", below: "A").AutomationId("rpr_b").WithKey("b");

            Element c = TextBlock("C")
                .RelativePanel(name: "C", below: "A", alignLeftWith: "A")
                .AutomationId("rpr_c").WithKey("c");

            return RelativePanel(a, b, c);
        }
    }

    /// <summary>
    /// Mounts a RelativePanel with several attached-DP-positioned children, then
    /// re-renders the tree repeatedly. Under unchanged attached props the panel
    /// subtree memo-skips (the hook does not re-run and applying the DPs never
    /// re-entered reconcile), so the reconcile count stays pinned; an actual
    /// child change re-runs the hook exactly once and rewires the live controls.
    /// </summary>
    internal class AttachedDpDoesNotReenter(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var panelTally = new RenderTally();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, setTick) = ctx.UseState(0);
                var (version, setVersion) = ctx.UseState(0);

                // "Bump" mutates only this unrelated TextBlock + an excluded state
                // slot; the RelPanelProps stay value-equal, so the panel subtree
                // memo-skips on every Bump. "ChangeLayout" bumps LayoutVersion,
                // which is the only thing that should re-run the attached-DP hook.
                return VStack(
                    TextBlock($"tick:{tick}"),
                    Button("Bump", () => setTick(tick + 1)),
                    Button("ChangeLayout", () => setVersion(version + 1)),
                    Component<RelPanelComponent, RelPanelProps>(
                        new RelPanelProps(version, panelTally)));
            });

            await Harness.Render();

            WinXC.TextBlock? Ctrl(string key) =>
                H.FindControl<WinXC.TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == $"rpr_{key}");

            var a0 = Ctrl("a");
            var b0 = Ctrl("b");
            var c0 = Ctrl("c");

            // The attached-DP hook ran exactly once and wired the live controls.
            H.Check("RPReentry_InitialRenderOnce", panelTally.Count == 1);
            H.Check("RPReentry_InitialChildrenMounted",
                a0 is not null && b0 is not null && c0 is not null);
            H.Check("RPReentry_InitialRightOfWired",
                b0 is not null && ReferenceEquals(WinXC.RelativePanel.GetRightOf(b0), a0));
            H.Check("RPReentry_InitialBelowWired",
                c0 is not null && ReferenceEquals(WinXC.RelativePanel.GetBelow(c0), a0));

            // Re-render the tree several times under UNCHANGED panel props. The panel
            // component memo-skips (props value-equal), so the two-pass hook does NOT
            // re-run — and applying the attached DPs never re-entered reconcile to
            // schedule extra renders. The tally must stay pinned at 1 (no storm), and
            // the live wiring must remain intact (identity preserved, not re-applied).
            for (int i = 0; i < 4; i++)
            {
                H.ClickButton("Bump");
                await Harness.Render();
            }
            H.Check("RPReentry_NoReentrantStorm_UnchangedChildren", panelTally.Count == 1);
            H.Check("RPReentry_WiringIntactAfterBumps",
                Ctrl("b") is { } bAfter &&
                ReferenceEquals(WinXC.RelativePanel.GetRightOf(bAfter), Ctrl("a")));

            // Now actually change the children: B moves from rightOf→below A. The panel
            // component re-renders EXACTLY once, the hook rebuilds the name map, the
            // stale RightOf is cleared and the new Below is wired — all bounded, no storm.
            H.ClickButton("ChangeLayout");
            await Harness.Render();
            H.Check("RPReentry_RerenderOnceOnChange", panelTally.Count == 2);

            var aN = Ctrl("a");
            var bN = Ctrl("b");
            H.Check("RPReentry_StaleRightOfCleared",
                bN is not null && WinXC.RelativePanel.GetRightOf(bN) is null);
            H.Check("RPReentry_NewBelowWired",
                bN is not null && ReferenceEquals(WinXC.RelativePanel.GetBelow(bN), aN));

            // Re-render again under the now-unchanged (v1) props: still memo-skips,
            // still no storm — the tally holds at 2.
            for (int i = 0; i < 3; i++)
            {
                H.ClickButton("Bump");
                await Harness.Render();
            }
            H.Check("RPReentry_NoStormAfterChange", panelTally.Count == 2);
        }
    }
}
