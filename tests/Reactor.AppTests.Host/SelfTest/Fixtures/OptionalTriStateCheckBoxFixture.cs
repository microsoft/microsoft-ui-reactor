using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 050 G2 — tri-state CheckBox distinguishes the three Optional&lt;bool?&gt;
/// states:
/// <list type="bullet">
///   <item><c>Optional&lt;bool?&gt;.Unset</c> — Reactor leaves the control
///   alone (user / WinUI owns IsChecked). Sibling re-render must NOT flip
///   the user's last selection.</item>
///   <item><c>Optional&lt;bool?&gt;.Of(null)</c> — explicit "indeterminate".
///   Reactor force-asserts IsChecked = null on every Update.</item>
///   <item><c>Optional&lt;bool?&gt;.Of(true)</c> / <c>.Of(false)</c> —
///   explicit two-state value, force-asserted.</item>
/// </list>
/// </summary>
internal static class OptionalTriStateCheckBoxFixture
{
    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Unset_LetsUserToggleSurviveSiblingRerender();
            await OfNull_ForcesIndeterminate();
            await TransitionsBetweenAllThreeStates();
        }

        private async Task Unset_LetsUserToggleSurviveSiblingRerender()
        {
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (flag, setFlag) = ctx.UseState(false);
                return VStack(
                    Button("TriState_Unset_Toggle", () => setFlag(!flag)),
                    TextBlock(flag ? "flag:on" : "flag:off"),
                    ThreeStateCheckBox(Optional<bool?>.Unset, _ => { }, "tri-unset"));
            });

            await Harness.Render();
            var cb = H.FindControl<WinUI.CheckBox>(c => c.Content as string == "tri-unset");
            H.Check("OptionalTriStateCheckBox_Unset_ControlFound", cb is not null);
            if (cb is null) return;

            cb.IsChecked = true;
            await Harness.Render();

            H.ClickButton("TriState_Unset_Toggle");
            await Harness.Render();

            H.Check(
                "OptionalTriStateCheckBox_Unset_UserToggleSurvivesSiblingRerender",
                cb.IsChecked == true);
        }

        private async Task OfNull_ForcesIndeterminate()
        {
            // Note: Reactor's ShallowEquals fast path (Reconciler.Update.cs:71)
            // skips per-element Update when the new element value-equals the old
            // (modifiers + HasCallbacks also match). So a re-render that produces
            // the SAME `ThreeStateCheckBox(Of(null))` element does NOT re-assert
            // — the entire prop pipeline is bypassed. The spec 050 §6.5 snap-back
            // recipe handles this by flipping `.Margin(tick ? 0 : 1)` so the
            // modifier differs and Update runs. This test exercises that recipe
            // to verify `Of(null)` reasserts indeterminate when Update actually
            // runs.
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, bump) = ctx.UseReducer(false);
                return VStack(
                    Button("TriState_OfNull_Bump", () => bump(t => !t)),
                    TextBlock(tick ? "tick:on" : "tick:off"),
                    ThreeStateCheckBox(Optional<bool?>.Of(null), _ => { }, "tri-null")
                        .Margin(tick ? 0 : 1));
            });

            await Harness.Render();
            var cb = H.FindControl<WinUI.CheckBox>(c => c.Content as string == "tri-null");
            H.Check("OptionalTriStateCheckBox_OfNull_ControlFound", cb is not null);
            if (cb is null) return;

            H.Check(
                "OptionalTriStateCheckBox_OfNull_InitiallyIndeterminate",
                cb.IsChecked is null);

            // User flips to checked.
            cb.IsChecked = true;
            await Harness.Render();

            // Snap-back bump: Margin modifier changes → Update runs →
            // `Of(null)` force-asserts IsChecked = null.
            H.ClickButton("TriState_OfNull_Bump");
            await Harness.Render();

            var reasserted = await Harness.WaitFor(
                () => cb.IsChecked is null,
                maxPasses: 10,
                perPassMs: 20);
            H.Check(
                "OptionalTriStateCheckBox_OfNull_BumpForceAssertsIndeterminate",
                reasserted);
        }

        private async Task TransitionsBetweenAllThreeStates()
        {
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var value = phase switch
                {
                    0 => Optional<bool?>.Of(true),
                    1 => Optional<bool?>.Of(null),
                    2 => Optional<bool?>.Of(false),
                    _ => Optional<bool?>.Unset,
                };
                return VStack(
                    Button("TriState_Trans_Next", () => setPhase(phase + 1)),
                    ThreeStateCheckBox(value, _ => { }, "tri-trans"));
            });

            await Harness.Render();
            var cb = H.FindControl<WinUI.CheckBox>(c => c.Content as string == "tri-trans");
            H.Check("OptionalTriStateCheckBox_Trans_ControlFound", cb is not null);
            if (cb is null) return;

            H.Check("OptionalTriStateCheckBox_Trans_Phase0_True", cb.IsChecked == true);

            H.ClickButton("TriState_Trans_Next");
            await Harness.Render();
            var indet = await Harness.WaitFor(() => cb.IsChecked is null, 10, 20);
            H.Check("OptionalTriStateCheckBox_Trans_Phase1_Indeterminate", indet);

            H.ClickButton("TriState_Trans_Next");
            await Harness.Render();
            var falseAsserted = await Harness.WaitFor(() => cb.IsChecked == false, 10, 20);
            H.Check("OptionalTriStateCheckBox_Trans_Phase2_False", falseAsserted);

            // Phase 3 → Unset. The control must keep its last asserted value
            // (false) — Unset means "stop force-asserting", not "clear".
            H.ClickButton("TriState_Trans_Next");
            await Harness.Render();
            H.Check(
                "OptionalTriStateCheckBox_Trans_Phase3_UnsetKeepsLastValue",
                cb.IsChecked == false);

            // User toggle now sticks because we're Unset.
            cb.IsChecked = true;
            await Harness.Render();
            H.Check(
                "OptionalTriStateCheckBox_Trans_Phase3_UserCanToggleAfterUnset",
                cb.IsChecked == true);
        }
    }
}
