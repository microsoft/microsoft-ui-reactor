using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class TransitionTests
{
    internal class TypeHierarchy(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.CompletedTask;

            // Verify Fade + Slide produces CombinedTransition
            var combined = Animation.Transition.Fade + Animation.Transition.Slide(Edge.Bottom);
            H.Check("Transition_FadePlusSlide_IsCombined", combined is CombinedTransition);

            // Verify CombinedTransition contents
            var ct = (CombinedTransition)combined;
            H.Check("Transition_Combined_FirstIsFade", ct.First is FadeTransition);
            H.Check("Transition_Combined_SecondIsSlide", ct.Second is SlideTransition);

            // Verify Scale preset
            var scale = Animation.Transition.Scale(0.9f);
            H.Check("Transition_Scale_IsScale", scale is ScaleTransition);
            H.Check("Transition_Scale_Value", ((ScaleTransition)scale).From == 0.9f);
        }
    }

    internal class AsymmetricTransition(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.CompletedTask;

            // Enter(Fade) | Exit(Scale) produces AsymmetricTransition
            var asym = Animation.Transition.Enter(Animation.Transition.Fade)
                     | Animation.Transition.Exit(Animation.Transition.Scale());

            // The | operator combines two DirectionalTransitions into AsymmetricTransition
            H.Check("Transition_Asymmetric_IsAsymmetric", asym is Animation.AsymmetricTransition);

            // Verify AsymmetricTransition has enter/exit
            if (asym is Animation.AsymmetricTransition at)
            {
                H.Check("Transition_Asymmetric_HasEnter", at.EnterTransition is not null);
                H.Check("Transition_Asymmetric_HasExit", at.ExitTransition is not null);
            }
        }
    }

    internal class CurveResolutionPriority(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.CompletedTask;

            // Default curve when none specified
            var et1 = new ElementTransition(Animation.Transition.Fade);
            H.Check("Transition_DefaultCurve_Null", et1.Curve is null);

            // Explicit curve parameter
            var et2 = new ElementTransition(Animation.Transition.Fade, Curve.Spring(0.7f));
            H.Check("Transition_ExplicitCurve_IsSpring", et2.Curve is SpringCurve);
        }
    }

    internal class MountUnmountIntegration(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var showElement = true;

            host.Mount(ctx =>
            {
                return VStack(
                    showElement
                        ? Border(TextBlock("Transitioned"))
                            .Transition(Animation.Transition.Fade)
                            .AutomationId("transition-target")
                        : (Element)TextBlock("Hidden")
                );
            });

            await Harness.Render();

            var target = H.FindControl<Border>(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "transition-target");

            H.Check("Transition_Mount_Visible", target is not null);

            // Verify enter animation was applied (element should be visible)
            if (target is not null)
            {
                var visual = ElementCompositionPreview.GetElementVisual(target);
                // Enter transition starts opacity animation — visual exists and is valid
                H.Check("Transition_Mount_VisualExists", visual is not null);
            }
        }
    }

    private static Element Leaving(string key) =>
        Border(TextBlock(key)).WithKey(key)
            .Transition(Animation.Transition.Fade + Animation.Transition.Slide(Edge.Bottom), Curve.Ease(600));

    internal class KeyedRemovalDuringExitPatchesLiveSiblings(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var reconciler = new Reconciler();
            var initial = VStack(24,
                Leaving("loading"),
                InfoBar("initial", "banner").WithKey("banner"),
                Border(TextBlock("initial card")).WithKey("card")).Stagger(TimeSpan.FromMilliseconds(45));
            var panel = (StackPanel)reconciler.Mount(initial, () => { })!;
            H.SetContent(panel);
            await Harness.Render();
            var loading = panel.Children[0];
            var banner = (InfoBar)panel.Children[1];
            var card = (Border)panel.Children[2];
            try
            {
                var next = VStack(24,
                    InfoBar("second", "banner").WithKey("banner"),
                    Border(TextBlock("second card")).WithKey("card")).Stagger(TimeSpan.FromMilliseconds(45));
                reconciler.Update(initial, next, panel, () => { });
                H.Check("KeyedExit_PrefixExitInFlight",
                    panel.Children.Count == 3 && reconciler.IsExiting(loading));

                var final = VStack(24,
                    InfoBar("third", "banner").WithKey("banner"),
                    Border(TextBlock("third card")).WithKey("card")).Stagger(TimeSpan.FromMilliseconds(45));
                reconciler.Update(next, final, panel, () => { });
                H.Check("KeyedExit_PrefixPatchesOwnControls",
                    banner.Title == "third" && card.Child is TextBlock { Text: "third card" }
                    && ReferenceEquals(panel.Children[1], banner) && ReferenceEquals(panel.Children[2], card));
                H.Check("KeyedExit_PrefixLeavesFadingControlAlone",
                    loading is Border { Child: TextBlock { Text: "loading" } } && reconciler.IsExiting(loading));
                var onlyCard = VStack(24, Border(TextBlock("fourth card")).WithKey("card"));
                reconciler.Update(final, onlyCard, panel, () => { });
                H.Check("KeyedExit_PureRemovalTargetsLiveControl",
                    panel.Children.Count == 2 && reconciler.IsExiting(loading)
                    && ReferenceEquals(panel.Children[1], card) && card.Child is TextBlock { Text: "fourth card" });
                var completed = await Harness.WaitFor(() => panel.Children.Count == 1, maxPasses: 40, perPassMs: 50);
                H.Check("KeyedExit_PrefixFinalOrder",
                    completed && ReferenceEquals(panel.Children[0], card));
            }
            finally
            {
                H.SetContent(null);
                reconciler.UnmountChild(panel);
            }
        }
    }

    internal class KeyedSuffixDuringExitPatchesLiveSiblings(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var reconciler = new Reconciler();
            var initial = VStack(
                Border(TextBlock("card")).WithKey("card"),
                InfoBar("initial", "banner").WithKey("banner"),
                Leaving("trailing"));
            var panel = (StackPanel)reconciler.Mount(initial, () => { })!;
            H.SetContent(panel);
            await Harness.Render();
            var banner = (InfoBar)panel.Children[1];
            var trailing = panel.Children[2];
            try
            {
                var next = VStack(
                    Border(TextBlock("card")).WithKey("card"),
                    InfoBar("second", "banner").WithKey("banner"));
                reconciler.Update(initial, next, panel, () => { });
                H.Check("KeyedExit_SuffixExitInFlight", panel.Children.Count == 3 && reconciler.IsExiting(trailing));

                var final = VStack(
                    Border(TextBlock("inserted")).WithKey("inserted"),
                    Border(TextBlock("new card")).WithKey("card"),
                    InfoBar("third", "banner").WithKey("banner"));
                reconciler.Update(next, final, panel, () => { });
                H.Check("KeyedExit_SuffixPatchesOwnControl",
                    banner.Title == "third" && ReferenceEquals(panel.Children[2], banner));
                var completed = await Harness.WaitFor(() => panel.Children.Count == 3, maxPasses: 40, perPassMs: 50);
                H.Check("KeyedExit_SuffixInsertionOrder",
                    completed && panel.Children[0] is Border { Child: TextBlock { Text: "inserted" } }
                    && panel.Children[1] is Border { Child: TextBlock { Text: "new card" } }
                    && ReferenceEquals(panel.Children[2], banner));
            }
            finally
            {
                H.SetContent(null);
                reconciler.UnmountChild(panel);
            }
        }
    }

    internal class KeyedMiddleDuringExitKeepsIdentity(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var reconciler = new Reconciler();
            var initial = VStack(
                Leaving("leading"),
                InfoBar("banner", "initial").WithKey("banner"),
                Border(TextBlock("card")).WithKey("card"),
                InfoBar("tail", "initial").WithKey("tail"),
                Leaving("trailing"));
            var panel = (StackPanel)reconciler.Mount(initial, () => { })!;
            H.SetContent(panel);
            await Harness.Render();
            var leading = panel.Children[0];
            var banner = (InfoBar)panel.Children[1];
            var tail = (InfoBar)panel.Children[3];
            var trailing = panel.Children[4];
            try
            {
                var next = VStack(
                    InfoBar("banner", "second").WithKey("banner"),
                    Border(TextBlock("card")).WithKey("card"),
                    InfoBar("tail", "second").WithKey("tail"));
                reconciler.Update(initial, next, panel, () => { });
                H.Check("KeyedExit_MiddleExitsInFlight",
                    panel.Children.Count == 5 && reconciler.IsExiting(leading) && reconciler.IsExiting(trailing));

                var final = VStack(
                    InfoBar("tail", "third").WithKey("tail"),
                    Border(TextBlock("inserted")).WithKey("inserted"),
                    InfoBar("banner", "third").WithKey("banner"));
                reconciler.Update(next, final, panel, () => { });
                H.Check("KeyedExit_MiddlePatchesSurvivors",
                    banner.Message == "third" && tail.Message == "third"
                    && !reconciler.IsExiting(banner) && !reconciler.IsExiting(tail));
                var completed = await Harness.WaitFor(() => panel.Children.Count == 3, maxPasses: 40, perPassMs: 50);
                H.Check("KeyedExit_MiddleFinalIdentityAndOrder",
                    completed && ReferenceEquals(panel.Children[0], tail)
                    && panel.Children[1] is Border { Child: TextBlock { Text: "inserted" } }
                    && ReferenceEquals(panel.Children[2], banner));
            }
            finally
            {
                H.SetContent(null);
                reconciler.UnmountChild(panel);
            }
        }
    }

    internal class KeyedGridChildReplaceKeepsPlacement(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var reconciler = new Reconciler();
            var initial = Grid([GridSize.Star(), GridSize.Star(), GridSize.Star()], [GridSize.Auto],
                Leaving("shared").Grid(row: 0, column: 0),
                InfoBar("stable", "initial").WithKey("stable").Grid(row: 0, column: 1));
            var panel = (Grid)reconciler.Mount(initial, () => { })!;
            H.SetContent(panel);
            await Harness.Render();
            var leaving = (Border)panel.Children[0];
            var stable = (InfoBar)panel.Children[1];
            try
            {
                var next = Grid([GridSize.Star(), GridSize.Star(), GridSize.Star()], [GridSize.Auto],
                    InfoBar("replacement", "second").WithKey("shared").Grid(row: 0, column: 2),
                    InfoBar("stable", "second").WithKey("stable").Grid(row: 0, column: 0));
                reconciler.Update(initial, next, panel, () => { });
                var replacement = panel.Children.OfType<InfoBar>().Single(bar => bar.Title == "replacement");
                H.Check("KeyedExit_GridReplacementExitInFlight",
                    panel.Children.Count == 3 && reconciler.IsExiting(leaving));
                H.Check("KeyedExit_GridReplacementPlacement",
                    WinUI.Grid.GetColumn(leaving) == 0 && WinUI.Grid.GetColumn(replacement) == 2 && WinUI.Grid.GetColumn(stable) == 0);

                var final = Grid([GridSize.Star(), GridSize.Star(), GridSize.Star()], [GridSize.Auto],
                    InfoBar("replacement", "third").WithKey("shared").Grid(row: 0, column: 1),
                    InfoBar("stable", "third").WithKey("stable").Grid(row: 0, column: 2));
                reconciler.Update(next, final, panel, () => { });
                H.Check("KeyedExit_GridUpdatesLiveControls",
                    replacement.Message == "third" && stable.Message == "third"
                    && WinUI.Grid.GetColumn(replacement) == 1 && WinUI.Grid.GetColumn(stable) == 2);
                var completed = await Harness.WaitFor(() => panel.Children.Count == 2, maxPasses: 40, perPassMs: 50);
                H.Check("KeyedExit_GridFinalIdentityAndPlacement",
                    completed && ReferenceEquals(panel.Children[0], replacement) && ReferenceEquals(panel.Children[1], stable)
                    && WinUI.Grid.GetColumn(replacement) == 1 && WinUI.Grid.GetColumn(stable) == 2);
            }
            finally
            {
                H.SetContent(null);
                reconciler.UnmountChild(panel);
            }
        }

    }

    private sealed class ExitCounter : Component<Action<Action>>
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            Props(() => setCount(count + 1));
            return TextBlock($"exit-counter:{count}");
        }
    }

    internal class KeyedDirtyChildDuringExitRenders(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            Action? hide = null, bump = null;
            var reused = Border(Component<ExitCounter, Action<Action>>(action => bump = action)).WithKey("counter");
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                hide = () => setShow(false);
                return VStack(show ? Leaving("dirty-loading") : Empty(), reused);
            });
            await Harness.Render();
            var label = H.FindText("exit-counter:0");
            H.Check("KeyedDirtyExit_Mounted", label is not null && hide is not null && bump is not null);
            hide!();
            await Harness.Render();
            H.Check("KeyedDirtyExit_ExitInFlight", host.Reconciler.HasExitingControls && H.FindText("dirty-loading") is not null);
            bump!();
            await Harness.Render();
            H.Check("KeyedDirtyExit_SelfUpdateReached", label is not null && ReferenceEquals(label, H.FindText("exit-counter:1")));
            var completed = await Harness.WaitFor(() => !host.Reconciler.HasExitingControls, maxPasses: 40, perPassMs: 50);
            H.Check("KeyedDirtyExit_ControlKeptAfterExit", completed && ReferenceEquals(label, H.FindText("exit-counter:1")));
        }
    }

    internal class KeyedEmptyMemoKeepsSuffixIdentity(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            foreach (var (localExit, unrelatedExit) in new[] { (false, false), (false, true), (true, false), (true, true) })
            {
                await RunCase(localExit, unrelatedExit);
                await RunMixedKeyCase(localExit, unrelatedExit);
            }
        }

        private async Task RunMixedKeyCase(bool localExit, bool unrelatedExit)
        {
            using var reconciler = new Reconciler();
            var scenario = $"MixedKeyEmptyMemo_Local{localExit}_Unrelated{unrelatedExit}";
            var empty = Memo("empty", () => Empty());
            var initial = VStack(
                Border(TextBlock("before a")).WithKey("a"), empty,
                Border(TextBlock("before b")).WithKey("b"),
                localExit ? Leaving("local-exit") : Empty());
            var otherInitial = VStack(unrelatedExit ? Leaving("other-exit") : Empty());
            var panel = (StackPanel)reconciler.Mount(initial, () => { })!;
            var otherPanel = (StackPanel)reconciler.Mount(otherInitial, () => { })!;
            var root = new StackPanel();
            root.Children.Add(panel);
            root.Children.Add(otherPanel);
            H.SetContent(root);
            try
            {
                await Harness.Render();
                var a = panel.Children[0];
                var b = panel.Children[1];
                var leaving = localExit ? panel.Children[2] : null;
                var otherLeaving = unrelatedExit ? otherPanel.Children[0] : null;
                var current = VStack(
                    Border(TextBlock("before a")).WithKey("a"), empty,
                    Border(TextBlock("before b")).WithKey("b"));
                reconciler.Update(initial, current, panel, () => { });
                reconciler.Update(otherInitial, VStack(), otherPanel, () => { });
                H.Check($"{scenario}_NullMountAndExitPositiveControl",
                    empty.Key is null && panel.Children.Count == (localExit ? 3 : 2)
                    && (leaving is null || reconciler.IsExiting(leaving))
                    && (otherLeaving is null || reconciler.IsExiting(otherLeaving))
                    && reconciler.HasExitingControls == (localExit || unrelatedExit));

                var reordered = VStack(
                    Border(TextBlock("after b")).WithKey("b"), empty,
                    Border(TextBlock("after a")).WithKey("a"));
                reconciler.Update(current, reordered, panel, () => { });
                H.Check($"{scenario}_ReorderKeepsIdentityAndContent",
                    panel.Children.Where(x => !reconciler.IsExiting(x)).SequenceEqual([b, a])
                    && a is Border { Child: TextBlock { Text: "after a" } }
                    && b is Border { Child: TextBlock { Text: "after b" } });

                var final = VStack(
                    Border(TextBlock("final b")).WithKey("b"), empty,
                    Border(TextBlock("inserted")).WithKey("inserted"),
                    Border(TextBlock("final a")).WithKey("a"));
                reconciler.Update(reordered, final, panel, () => { });
                var completed = await Harness.WaitFor(() => !reconciler.HasExitingControls, maxPasses: 40, perPassMs: 50);
                H.Check($"{scenario}_InsertionAndFinalOrder",
                    completed && panel.Children.Count == 3 && ReferenceEquals(panel.Children[0], b)
                    && panel.Children[1] is Border { Child: TextBlock { Text: "inserted" } }
                    && ReferenceEquals(panel.Children[2], a)
                    && a is Border { Child: TextBlock { Text: "final a" } }
                    && b is Border { Child: TextBlock { Text: "final b" } }
                    && otherPanel.Children.Count == 0);
            }
            finally
            {
                H.SetContent(null);
                reconciler.UnmountChild(panel);
                reconciler.UnmountChild(otherPanel);
            }
        }

        private async Task RunCase(bool localExit, bool unrelatedExit)
        {
            using var reconciler = new Reconciler();
            var scenario = $"KeyedEmptyMemo_Local{localExit}_Unrelated{unrelatedExit}";
            var empty = Memo("empty", () => Empty()).WithKey("empty");
            var initial = VStack(empty,
                Border(TextBlock("before b")).WithKey("b"),
                Border(TextBlock("before c")).WithKey("c"),
                localExit ? Leaving("local-exit") : Empty());
            var otherInitial = VStack(unrelatedExit ? Leaving("other-exit") : Empty());
            var otherPanel = (StackPanel)reconciler.Mount(otherInitial, () => { })!;
            var panel = (StackPanel)reconciler.Mount(initial, () => { })!;
            var root = new StackPanel();
            root.Children.Add(panel);
            root.Children.Add(otherPanel);
            H.SetContent(root);
            try
            {
                await Harness.Render();
                var b = panel.Children[0];
                var c = panel.Children[1];
                var leaving = localExit ? panel.Children[2] : null;
                var otherLeaving = unrelatedExit ? otherPanel.Children[0] : null;
                var current = VStack(empty,
                    Border(TextBlock("before b")).WithKey("b"),
                    Border(TextBlock("before c")).WithKey("c"));
                reconciler.Update(initial, current, panel, () => { });
                reconciler.Update(otherInitial, VStack(), otherPanel, () => { });
                H.Check($"{scenario}_NullMountAndExitPositiveControl",
                    panel.Children.Count == (localExit ? 3 : 2)
                    && (leaving is null || reconciler.IsExiting(leaving))
                    && (otherLeaving is null || reconciler.IsExiting(otherLeaving))
                    && reconciler.HasExitingControls == (localExit || unrelatedExit));

                var next = VStack(empty, Memo("inserted-empty", () => Empty()).WithKey("inserted-empty"),
                    Border(TextBlock("inserted")).WithKey("inserted"),
                    Border(TextBlock("after b")).WithKey("b"),
                    Border(TextBlock("after c")).WithKey("c"));
                reconciler.Update(current, next, panel, () => { });
                var inserted = panel.Children.OfType<Border>().Single(x => x.Child is TextBlock { Text: "inserted" });
                H.Check($"{scenario}_SuffixUpdated",
                    b is Border { Child: TextBlock { Text: "after b" } }
                    && c is Border { Child: TextBlock { Text: "after c" } });
                H.Check($"{scenario}_SuffixIdentityAndOrder",
                    panel.Children.Where(x => !reconciler.IsExiting(x)).SequenceEqual([inserted, b, c]));

                var middleEmpty = Memo("middle-empty", () => Empty()).WithKey("middle-empty");
                var trailingEmpty = Memo("trailing-empty", () => Empty()).WithKey("trailing-empty");
                var reordered = VStack(
                    Border(TextBlock("reordered c")).WithKey("c"), middleEmpty,
                    empty, Border(TextBlock("reordered b")).WithKey("b"), trailingEmpty);
                reconciler.Update(next, reordered, panel, () => { });
                H.Check($"{scenario}_MiddleIdentityContentAndOrder",
                    b is Border { Child: TextBlock { Text: "reordered b" } }
                    && c is Border { Child: TextBlock { Text: "reordered c" } }
                    && panel.Children.Where(x => !reconciler.IsExiting(x)).SequenceEqual([c, b]));

                var suffix = VStack(
                    Border(TextBlock("final c")).WithKey("c"), middleEmpty,
                    Border(TextBlock("new before b")).WithKey("before-b"),
                    empty, Border(TextBlock("final b")).WithKey("b"), trailingEmpty);
                reconciler.Update(reordered, suffix, panel, () => { });
                var beforeB = panel.Children.OfType<Border>().Single(x => x.Child is TextBlock { Text: "new before b" });
                H.Check($"{scenario}_AbsentSuffixAnchor",
                    b is Border { Child: TextBlock { Text: "final b" } }
                    && c is Border { Child: TextBlock { Text: "final c" } }
                    && panel.Children.Where(x => !reconciler.IsExiting(x)).SequenceEqual([c, beforeB, b]));

                var final = VStack(empty, Border(TextBlock("only b")).WithKey("b"), trailingEmpty);
                reconciler.Update(suffix, final, panel, () => { });
                var completed = await Harness.WaitFor(() => !reconciler.HasExitingControls, maxPasses: 40, perPassMs: 50);
                H.Check($"{scenario}_RemovalAndFinalOrder",
                    completed && panel.Children.Count == 1 && ReferenceEquals(panel.Children[0], b)
                    && b is Border { Child: TextBlock { Text: "only b" } } && otherPanel.Children.Count == 0);
            }
            finally
            {
                H.SetContent(null);
                reconciler.UnmountChild(panel);
                reconciler.UnmountChild(otherPanel);
            }
        }
    }
}
