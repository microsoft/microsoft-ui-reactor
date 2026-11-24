using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// An enter fade starts at mount, before the element renders. XAML writes a changed
/// <c>UIElement.Opacity</c> (a local value or a style's) to the visual on the element's first
/// rendered frame, which stops the fade, so Reactor restarts it after that frame. The fade rests at
/// the element's own opacity (#1680).
/// </summary>
internal sealed class EnterFadeOpacityFixture(Harness h) : SelfTestFixtureBase(h)
{
    private static Task AfterNextCommit(Microsoft.UI.Composition.Visual visual)
        => TransformModifierTransitionsFixture.AfterNextCommit(visual);

    public override async Task RunAsync()
    {
        // Every case below depends on XAML rendering frames, which a just-activated window may not
        // do yet. Wait for XAML to push a probe's opacity. The same probe shows XAML leaves the
        // visual's IsVisible alone, which the fade relies on to hide the frame that pushes.
        {
            var probe = new Border { Child = new TextBlock { Text = "probe" }, Opacity = 0.4 };
            var visual = ElementCompositionPreview.GetElementVisual(probe);
            visual.IsVisible = false;
            H.SetContent(probe);
            int frames = 0;
            for (; frames < 300 && Math.Abs(visual.Opacity - 0.4f) > 0.001f; frames++)
            {
                await Harness.Render(16);
                await AfterNextCommit(visual);
            }
            H.Check("EnterFade_XamlPushLeavesIsVisible", !visual.IsVisible && Math.Abs(visual.Opacity - 0.4f) < 0.001f,
                $"isVisible={visual.IsVisible}, opacity={visual.Opacity}, frames={frames}");
            H.SetContent(null);
        }

        // Mounted with .Opacity(0.4), the fade starts before XAML pushes 0.4. A commit before the
        // element is attached makes sure the push lands after the fade started.
        {
            using var reconciler = new Reconciler();
            var control = (Border)reconciler.Mount(Faded(0.4, 60000), () => { })!;
            var visual = ElementCompositionPreview.GetElementVisual(control);
            bool concealed = !visual.IsVisible && Reconciler.OpacityRestartPendingForTests(control);
            await AfterNextCommit(visual);
            H.SetContent(control);
            await Harness.Render();
            await AfterNextCommit(visual);
            var running = visual.TryGetAnimationController("Opacity");
            int restarts = Reconciler.OpacityRestartsForTests(control);
            // The restart count shows the fade survived because Reactor restarted it, not because of
            // timing, and the base value reads 0 only when the restart wrote the fade's start again.
            H.Check("EnterFade_LocalOpacitySurvivesXamlPush", concealed && running is not null && restarts == 1
                && visual.IsVisible && visual.Opacity == 0f && !Reconciler.OpacityRestartPendingForTests(control),
                $"concealed={concealed}, running={running is not null}, restarts={restarts}, visible={visual.IsVisible}, opacity={visual.Opacity}");
            H.SetContent(null);
        }

        // A style's opacity, set after mount, is the target the restarted fade ends at. The default
        // opacity is the control: no push, so no restart, and the fade ends at 1.
        foreach (var (scenario, styled) in new[] { ("Styled", true), ("Default", false) })
        {
            using var reconciler = new Reconciler();
            var control = (Border)reconciler.Mount(Faded(null, 150), () => { })!;
            if (styled) control.Style = new Style(typeof(Border)) { Setters = { new Setter(UIElement.OpacityProperty, 0.65d) } };
            var visual = ElementCompositionPreview.GetElementVisual(control);
            await AfterNextCommit(visual);
            H.SetContent(control);
            await Harness.Render();
            await AfterNextCommit(visual);
            int restarts = Reconciler.OpacityRestartsForTests(control);
            await Settle(visual);
            float expected = styled ? 0.65f : 1f;
            // The base value reads the fade's end only once it finished: it started from 0.
            H.Check($"EnterFade_{scenario}EndsAtOwnOpacity", restarts == (styled ? 1 : 0) && visual.IsVisible
                && Math.Abs(visual.Opacity - expected) < 0.001f,
                $"restarts={restarts}, visible={visual.IsVisible}, opacity={visual.Opacity}, expected={expected}");
            H.SetContent(null);
        }

        // A pooled renter's fade ends at its own opacity whether XAML pushes it again or not.
        {
            using var reconciler = new Reconciler();
            var with = VStack(Faded(0.4, 150));
            var without = VStack();
            var panel = (Panel)reconciler.Mount(without, () => { })!;
            H.SetContent(panel);
            await Harness.Render();
            Border? first = null;
            for (int round = 0; round < 2; round++)
            {
                reconciler.Update(without, with, panel, () => { });
                var control = (Border)panel.Children[0];
                first ??= control;
                var visual = ElementCompositionPreview.GetElementVisual(control);
                int restartsBefore = Reconciler.OpacityRestartsForTests(control);
                await Harness.Render();
                await AfterNextCommit(visual);
                int restarts = Reconciler.OpacityRestartsForTests(control) - restartsBefore;
                await Settle(visual);
                H.Check($"EnterFade_RenterEndsAtOwnOpacity_Round{round}", (round == 0 || ReferenceEquals(control, first))
                    && visual.IsVisible && Math.Abs(visual.Opacity - 0.4f) < 0.001f,
                    $"recycled={ReferenceEquals(control, first)}, restarts={restarts}, visible={visual.IsVisible}, opacity={visual.Opacity}");
                reconciler.Update(with, without, panel, () => { });
                // The exit fade returns the control to the pool once it finishes.
                await Settle(visual);
            }
            H.SetContent(null);
        }

        // Anything that takes the opacity before the first frame cancels the check and shows the
        // visual again: a modifier write, an unmount, the reconciler's disposal.
        foreach (var scenario in new[] { "ModifierWrite", "Unmount", "Dispose" })
        {
            var reconciler = new Reconciler();
            var faded = Faded(0.4, 60000);
            var control = (Border)reconciler.Mount(faded, () => { })!;
            var visual = ElementCompositionPreview.GetElementVisual(control);
            bool armed = !visual.IsVisible && Reconciler.OpacityRestartPendingForTests(control);
            switch (scenario)
            {
                case "ModifierWrite": reconciler.Update(faded, faded.Opacity(0.7), control, () => { }); break;
                case "Unmount": reconciler.UnmountChild(control); break;
                case "Dispose": reconciler.Dispose(); break;
            }
            bool cancelled = !Reconciler.OpacityRestartPendingForTests(control) && visual.IsVisible;
            if (scenario == "ModifierWrite")
            {
                H.SetContent(control);
                await Harness.Render();
                await AfterNextCommit(visual);
                cancelled &= Reconciler.OpacityRestartsForTests(control) == 0;
                H.SetContent(null);
            }
            H.Check("EnterFade_" + scenario + "CancelsAndReveals", armed && cancelled,
                $"armed={armed}, pending={Reconciler.OpacityRestartPendingForTests(control)}, visible={visual.IsVisible}, restarts={Reconciler.OpacityRestartsForTests(control)}");
            if (scenario != "Dispose") reconciler.Dispose();
        }

        // Under a collapsed parent nothing renders, so the visual stays hidden and the check waits
        // for the frame that shows it.
        {
            using var reconciler = new Reconciler();
            var control = (Border)reconciler.Mount(Faded(0.4, 60000), () => { })!;
            var visual = ElementCompositionPreview.GetElementVisual(control);
            var host = new Border { Child = control, Visibility = Visibility.Collapsed };
            H.SetContent(host);
            await Harness.Render();
            await AfterNextCommit(visual);
            bool waiting = !visual.IsVisible && Reconciler.OpacityRestartPendingForTests(control);
            host.Visibility = Visibility.Visible;
            await Harness.Render();
            await AfterNextCommit(visual);
            var running = visual.TryGetAnimationController("Opacity");
            int restarts = Reconciler.OpacityRestartsForTests(control);
            H.Check("EnterFade_CollapsedWaitsThenRestarts", waiting && running is not null && restarts == 1 && visual.IsVisible,
                $"waiting={waiting}, running={running is not null}, restarts={restarts}, visible={visual.IsVisible}");
            host.Child = null;
            H.SetContent(null);
        }
    }

    private static Element Faded(double? opacity, int milliseconds)
    {
        Element plain = Border(TextBlock("Enter Fade"));
        if (opacity is { } o) plain = plain.Opacity(o);
        return plain.Transition(Animation.Transition.Fade, Curve.Linear(milliseconds));
    }

    // Lets a short fade finish and its end value land in the visual's base value.
    private static async Task Settle(Microsoft.UI.Composition.Visual visual)
    {
        await Harness.Render(400);
        await AfterNextCommit(visual);
    }
}
