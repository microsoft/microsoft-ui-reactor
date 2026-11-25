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
        // element is attached makes sure the push lands after the fade started. Combined with a
        // Scale enter the fade restarts the same way. With .Animate() on Opacity a push would fire
        // an implicit animation that replaces the fade, whatever its curve, so the fade must still
        // be the one playing: its base value reads 0, where a replacement started by the push
        // would read the pushed opacity.
        foreach (var (scenario, element, styled, restartExpected) in new (string, Element, bool, bool?)[]
        {
            ("Local", Faded(0.4, 60000), false, true),
            ("CombinedScale", Border(TextBlock("Enter Fade")).Opacity(0.4)
                .Transition(Animation.Transition.Fade + Animation.Transition.Scale(0.8f), Curve.Linear(60000)), false, true),
            ("Animate", Border(TextBlock("Enter Fade")).Opacity(0.4).Animate(Curve.Linear(60000), AnimateProperty.Opacity)
                .Transition(Animation.Transition.Fade, Curve.Linear(60000)), false, null),
            ("AnimateShort", Border(TextBlock("Enter Fade")).Opacity(0.4).Animate(Curve.Linear(1), AnimateProperty.Opacity)
                .Transition(Animation.Transition.Fade, Curve.Linear(60000)), false, null),
            ("AnimateShortStyled", Border(TextBlock("Enter Fade")).Animate(Curve.Linear(1), AnimateProperty.Opacity)
                .Transition(Animation.Transition.Fade, Curve.Linear(60000)), true, null),
        })
        {
            using var reconciler = new Reconciler();
            var control = (Border)reconciler.Mount(element, () => { })!;
            if (styled) control.Style = new Style(typeof(Border)) { Setters = { new Setter(UIElement.OpacityProperty, 0.4d) } };
            var visual = ElementCompositionPreview.GetElementVisual(control);
            bool concealed = !visual.IsVisible && Reconciler.OpacityRestartPendingForTests(control);
            await AfterNextCommit(visual);
            H.SetContent(control);
            await Harness.Render();
            await AfterNextCommit(visual);
            var running = visual.TryGetAnimationController("Opacity");
            int restarts = Reconciler.OpacityRestartsForTests(control);
            // The restart count shows the fade survived because Reactor restarted it, not because of
            // timing, and the base value reads 0 only while the fade's own start value stands.
            bool restarted = restartExpected is not true || restarts == 1;
            H.Check($"EnterFade_{scenario}OpacitySurvivesXamlPush", concealed && running is not null && restarted
                && visual.Opacity == 0f && visual.IsVisible && !Reconciler.OpacityRestartPendingForTests(control),
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
            float expected = styled ? 0.65f : 1f;
            int frames = await Settle(visual, expected);
            // The base value reads the fade's end only once it finished: it started from 0.
            H.Check($"EnterFade_{scenario}EndsAtOwnOpacity", restarts == (styled ? 1 : 0) && visual.IsVisible
                && Math.Abs(visual.Opacity - expected) < 0.001f,
                $"restarts={restarts}, visible={visual.IsVisible}, opacity={visual.Opacity}, expected={expected}, frames={frames}");
            H.SetContent(null);
        }

        // A pooled renter's fade ends at its own opacity whether XAML pushes it again or not. The
        // first mount gets a push and a restart. The recycled renter, whose opacity equals XAML's
        // last push, gets none, so only the fade's own target puts it at 0.4.
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
                int frames = await Settle(visual, 0.4f);
                H.Check($"EnterFade_RenterEndsAtOwnOpacity_Round{round}", (round == 0 || ReferenceEquals(control, first))
                    && restarts == (round == 0 ? 1 : 0) && visual.IsVisible && Math.Abs(visual.Opacity - 0.4f) < 0.001f,
                    $"recycled={ReferenceEquals(control, first)}, restarts={restarts}, visible={visual.IsVisible}, opacity={visual.Opacity}, frames={frames}");
                reconciler.Update(with, without, panel, () => { });
                // The exit fade returns the control to the pool once it finishes.
                for (int i = 0; i < 300 && panel.Children.Count > 0; i++)
                    await Harness.Render(16);
            }
            H.SetContent(null);
        }

        // Removed before its first frame, the element plays its exit (here a slide, which doesn't
        // take the opacity): the exit cancels the check, so the element isn't hidden through it and
        // its enter fade isn't restarted.
        {
            using var reconciler = new Reconciler();
            var child = Border(TextBlock("Enter Fade")).Opacity(0.4)
                .Transition(Animation.Transition.Fade | Animation.Transition.Slide(Edge.Bottom), Curve.Linear(600));
            var with = VStack(child);
            var without = VStack();
            var panel = (Panel)reconciler.Mount(without, () => { })!;
            H.SetContent(panel);
            await Harness.Render();
            reconciler.Update(without, with, panel, () => { });
            var control = (Border)panel.Children[0];
            var visual = ElementCompositionPreview.GetElementVisual(control);
            bool armed = !visual.IsVisible && Reconciler.OpacityRestartPendingForTests(control);
            reconciler.Update(with, without, panel, () => { });
            bool exiting = panel.Children.Count == 1;
            bool cancelled = !Reconciler.OpacityRestartPendingForTests(control) && visual.IsVisible;
            await Harness.Render();
            await AfterNextCommit(visual);
            int restarts = Reconciler.OpacityRestartsForTests(control);
            H.Check("EnterFade_ExitBeforeFirstFrameCancels", armed && exiting && cancelled && restarts == 0,
                $"armed={armed}, exiting={exiting}, cancelled={cancelled}, restarts={restarts}");
            H.SetContent(null);
        }

        // Popup content never reaches the window's root, so the check has to see it render without
        // a layout pass that finds it in the live tree (IsLoaded turns true when it enters the popup).
        {
            using var reconciler = new Reconciler();
            var anchor = new Border();
            H.SetContent(anchor);
            await Harness.Render();
            var control = (Border)reconciler.Mount(Faded(0.4, 60000), () => { })!;
            var visual = ElementCompositionPreview.GetElementVisual(control);
            await AfterNextCommit(visual);
            var popup = new Microsoft.UI.Xaml.Controls.Primitives.Popup { Child = control, XamlRoot = anchor.XamlRoot };
            popup.IsOpen = true;
            for (int i = 0; i < 60 && Reconciler.OpacityRestartPendingForTests(control); i++)
            {
                await Harness.Render(16);
                await AfterNextCommit(visual);
            }
            var running = visual.TryGetAnimationController("Opacity");
            int restarts = Reconciler.OpacityRestartsForTests(control);
            string chain = "";
            for (DependencyObject? node = control; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
                chain += node.GetType().Name + (node is UIElement { Visibility: Visibility.Collapsed } ? "(C)" : "") + ">";
            H.Check("EnterFade_PopupContentRevealed", visual.IsVisible && !Reconciler.OpacityRestartPendingForTests(control)
                && running is not null && restarts == 1,
                $"visible={visual.IsVisible}, pending={Reconciler.OpacityRestartPendingForTests(control)}, running={running is not null}, restarts={restarts}, "
                + $"loaded={control.IsLoaded}, xamlRoot={control.XamlRoot is not null}, onLayout={Reconciler.OpacityRestartWaitsOnLayoutForTests(control)}, chain={chain}");
            popup.IsOpen = false;
            popup.Child = null;
            H.SetContent(null);
        }

        // The default enter under Animations.Animate (no .Transition) fades the same way.
        foreach (bool keyed in new[] { false, true })
        {
            using var reconciler = new Reconciler();
            Element child = Border(TextBlock("Ambient Enter")).Opacity(0.4);
            if (keyed) child = child.WithKey("a");
            var with = VStack(child);
            var without = VStack();
            var panel = (Panel)reconciler.Mount(without, () => { })!;
            H.SetContent(panel);
            await Harness.Render();
            Animations.Animate(AnimationKind.Spring, () => reconciler.Update(without, with, panel, () => { }));
            var control = (Border)panel.Children[0];
            var visual = ElementCompositionPreview.GetElementVisual(control);
            bool concealed = !visual.IsVisible && Reconciler.OpacityRestartPendingForTests(control);
            string atMount = $"visibleAtMount={visual.IsVisible}, pendingAtMount={Reconciler.OpacityRestartPendingForTests(control)}, "
                + $"fadeAtMount={visual.TryGetAnimationController("Opacity") is not null}, loaded={control.IsLoaded}, "
                + $"baseAtMount={visual.Opacity}, offsetAtMount={visual.Offset.Y}, children={panel.Children.Count}";
            await Harness.Render();
            await AfterNextCommit(visual);
            int restarts = Reconciler.OpacityRestartsForTests(control);
            int frames = await Settle(visual, 0.4f);
            H.Check($"EnterFade_AmbientEnterEndsAtOwnOpacity_{(keyed ? "Keyed" : "Positional")}", concealed && restarts == 1 && visual.IsVisible
                && Math.Abs(visual.Opacity - 0.4f) < 0.001f,
                $"{atMount}, restarts={restarts}, visible={visual.IsVisible}, opacity={visual.Opacity}, frames={frames}");
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

        // An ambient enter waiting under a collapsed parent is the reconciler's too: disposing it
        // cancels the check and shows the visual again.
        {
            var reconciler = new Reconciler();
            var with = VStack(Border(TextBlock("Ambient Enter")).Opacity(0.4));
            var without = VStack();
            var panel = (Panel)reconciler.Mount(without, () => { })!;
            var host = new Border { Child = panel, Visibility = Visibility.Collapsed };
            H.SetContent(host);
            await Harness.Render();
            Animations.Animate(AnimationKind.Spring, () => reconciler.Update(without, with, panel, () => { }));
            var control = (Border)panel.Children[0];
            var visual = ElementCompositionPreview.GetElementVisual(control);
            await Harness.Render();
            await AfterNextCommit(visual);
            bool waiting = !visual.IsVisible && Reconciler.OpacityRestartPendingForTests(control);
            reconciler.Dispose();
            bool cancelled = !Reconciler.OpacityRestartPendingForTests(control) && visual.IsVisible;
            H.Check("EnterFade_AmbientEnterDisposeCancelsAndReveals", waiting && cancelled,
                $"waiting={waiting}, pending={Reconciler.OpacityRestartPendingForTests(control)}, visible={visual.IsVisible}");
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

    // Lets a short fade finish and its end value land in the visual's base value. Polls for the
    // value, so a slow frame can't fail the check; a fade that never gets there runs out the cap.
    private static async Task<int> Settle(Microsoft.UI.Composition.Visual visual, float expected)
    {
        int frames = 0;
        for (; frames < 300 && Math.Abs(visual.Opacity - expected) > 0.001f; frames++)
        {
            await Harness.Render(16);
            await AfterNextCommit(visual);
        }
        // A little longer, so a fade that only passes the value on its way elsewhere is caught.
        await Harness.Render(200);
        await AfterNextCommit(visual);
        return frames;
    }
}
