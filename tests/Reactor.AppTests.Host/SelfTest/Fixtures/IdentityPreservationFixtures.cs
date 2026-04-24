using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Regression coverage for the "Update falls through to Mount" class of bug
/// where 14 controls (ComboBox, RadioButtons, TabView, Pivot, SplitView,
/// ListBox, SelectorBar, RelativePanel, SemanticZoom, Popup, RefreshContainer,
/// CommandBarFlyout, SwipeControl, ParallaxView) rebuilt their entire WinUI
/// control on every ancestor re-render. The visible symptom was icon flicker
/// on ComboBox and silent loss of focus/selection on the others.
///
/// The load-bearing invariant: the WinUI control instance is the *same* across
/// unrelated sibling re-renders. Each fixture here:
///   1. mounts a driver (a Button that bumps a useState counter) alongside the
///      control under test,
///   2. captures the control's WinUI instance via FindControl,
///   3. clicks the driver to trigger a re-render,
///   4. asserts the same WinUI instance is still present.
///
/// Selection/value state is *not* asserted across re-renders because the
/// element's declared value is authoritative (controlled-prop semantics — the
/// framework overwrites transient user state with the element's value unless
/// OnChanged is wired back through useState). The controlled-prop contract is
/// exercised elsewhere; this suite is specifically about control-instance
/// identity preservation.
/// </summary>
internal static class IdentityPreservationFixtures
{
    private static async Task RunIdentityCheck<T>(
        Harness H,
        string fixtureName,
        string buttonLabel,
        Func<Action, Element> rootFactory,
        Func<T, bool> findPredicate) where T : Microsoft.UI.Xaml.DependencyObject
    {
        var host = H.CreateHost();
        host.Mount(ctx =>
        {
            var (phase, setPhase) = ctx.UseState(0);
            return VStack(
                Button(buttonLabel, () => setPhase(phase + 1)),
                TextBlock($"phase={phase}"),
                rootFactory(() => { /* no-op; state lives inside factory */ })
            );
        });
        await Harness.Render();

        var c1 = H.FindControl<T>(findPredicate);
        H.Check($"{fixtureName}_Mounted", c1 is not null);

        H.ClickButton(buttonLabel);
        await Harness.Render();

        var c2 = H.FindControl<T>(findPredicate);
        H.Check($"{fixtureName}_SameInstance", c1 is not null && ReferenceEquals(c1, c2));
    }

    // ── ComboBox ──────────────────────────────────────────────────────

    internal class ComboBoxSurvivesSiblingUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await RunIdentityCheck<ComboBox>(H, "IdentityPreserve_ComboBox", "ID_CB_Go",
                _ => ComboBox(["A", "B", "C"], 0).Set(c => c.Name = "idCombo"),
                c => c.Name == "idCombo");
        }
    }

    // ── ComboBox with element items — mirrors the TestApp TitleBar pattern.
    // Each item is a composite element (HStack(icon, label)); the previous bug
    // tore these down on every sibling tick, causing icon flicker. ────────

    internal class ComboBoxElementItemsSurvivesSiblingUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Element[] items =
            [
                HStack(4, TextBlock("A-icon"), TextBlock("One")),
                HStack(4, TextBlock("B-icon"), TextBlock("Two")),
                HStack(4, TextBlock("C-icon"), TextBlock("Three")),
            ];

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                return VStack(
                    Button("ID_CBE_Go", () => setPhase(phase + 1)),
                    TextBlock($"phase={phase}"),
                    ComboBox(items, 0, null).Set(c => c.Name = "idCBE")
                );
            });
            await Harness.Render();

            var cb1 = H.FindControl<ComboBox>(c => c.Name == "idCBE");
            H.Check("IdentityPreserve_ComboBoxElements_Mounted", cb1 is not null);
            var firstItem1 = cb1 is { Items.Count: > 0 } ? cb1.Items[0] : null;
            H.Check("IdentityPreserve_ComboBoxElements_FirstItemCaptured", firstItem1 is not null);

            H.ClickButton("ID_CBE_Go");
            await Harness.Render();

            var cb2 = H.FindControl<ComboBox>(c => c.Name == "idCBE");
            var firstItem2 = cb2 is { Items.Count: > 0 } ? cb2.Items[0] : null;
            H.Check("IdentityPreserve_ComboBoxElements_SameInstance",
                ReferenceEquals(cb1, cb2));
            // Child items reconcile positionally — the underlying WinUI elements
            // that house each item should be the same instances, not rebuilt.
            H.Check("IdentityPreserve_ComboBoxElements_ItemsSameInstance",
                firstItem1 is not null && ReferenceEquals(firstItem1, firstItem2));
        }
    }

    // ── RadioButtons ──────────────────────────────────────────────────

    internal class RadioButtonsSurvivesSiblingUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await RunIdentityCheck<RadioButtons>(H, "IdentityPreserve_RadioButtons", "ID_RB_Go",
                _ => RadioButtons(["X", "Y", "Z"], 0).Set(r => r.Name = "idRB"),
                r => r.Name == "idRB");
        }
    }

    // ── TabView ───────────────────────────────────────────────────────

    internal class TabViewSurvivesSiblingUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await RunIdentityCheck<TabView>(H, "IdentityPreserve_TabView", "ID_TV_Go",
                _ => TabView(
                    Tab("One", TextBlock("t1")),
                    Tab("Two", TextBlock("t2")),
                    Tab("Three", TextBlock("t3"))
                ).Set(t => t.Name = "idTV"),
                t => t.Name == "idTV");
        }
    }

    // ── Pivot ─────────────────────────────────────────────────────────

    internal class PivotSurvivesSiblingUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await RunIdentityCheck<Pivot>(H, "IdentityPreserve_Pivot", "ID_PV_Go",
                _ => Pivot(
                    PivotItem("One", TextBlock("p1")),
                    PivotItem("Two", TextBlock("p2")),
                    PivotItem("Three", TextBlock("p3"))
                ).Set(p => p.Name = "idPV"),
                p => p.Name == "idPV");
        }
    }

    // ── ListBox ───────────────────────────────────────────────────────

    internal class ListBoxSurvivesSiblingUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await RunIdentityCheck<ListBox>(H, "IdentityPreserve_ListBox", "ID_LB_Go",
                _ => ListBox(["One", "Two", "Three"], 0).Set(l => l.Name = "idLB"),
                l => l.Name == "idLB");
        }
    }

    // ── SelectorBar ───────────────────────────────────────────────────

    internal class SelectorBarSurvivesSiblingUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await RunIdentityCheck<SelectorBar>(H, "IdentityPreserve_SelectorBar", "ID_SB_Go",
                _ => SelectorBar([
                        new SelectorBarItemData("One"),
                        new SelectorBarItemData("Two"),
                        new SelectorBarItemData("Three"),
                    ], 0).Set(s => s.Name = "idSB"),
                s => s.Name == "idSB");
        }
    }

    // ── SplitView ─────────────────────────────────────────────────────

    internal class SplitViewSurvivesSiblingUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await RunIdentityCheck<SplitView>(H, "IdentityPreserve_SplitView", "ID_SV_Go",
                _ => SplitView(TextBlock("pane"), TextBlock("content"))
                    .Set(s => s.Name = "idSV"),
                s => s.Name == "idSV");
        }
    }
}
