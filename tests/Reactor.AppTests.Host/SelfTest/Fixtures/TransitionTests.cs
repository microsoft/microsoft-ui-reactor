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

    /// <summary>Counts the per-child reads (a WinRT call apiece) and exit probes a pass makes.</summary>
    private sealed class CountingChildCollection(IChildCollection inner) : IChildCollection
    {
        public int Reads;
        public int Probes;
        public int Count => inner.Count;
        public Microsoft.UI.Xaml.UIElement Get(int index) { Reads++; return inner.Get(index); }
        public int IndexOf(Microsoft.UI.Xaml.UIElement element) { Probes++; return inner.IndexOf(element); }
        public void Insert(int index, Microsoft.UI.Xaml.UIElement element) => inner.Insert(index, element);
        public void RemoveAt(int index) => inner.RemoveAt(index);
        public void Move(int oldIndex, int newIndex) => inner.Move(oldIndex, newIndex);
        public void Replace(int index, Microsoft.UI.Xaml.UIElement element) => inner.Replace(index, element);
    }

    // While an exit plays (here or in another panel) or a child mounts nothing, a keyed pass
    // must read no more controls than when nothing is retained: none for a skipped list, one
    // per realized child for an updated one. Each read is a WinRT call that allocates, paid
    // per child per render.
    internal class KeyedPassReadsEachControlAtMostOnce(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            foreach (var (localExit, unrelatedExit, emptyMemo) in new[]
            {
                (false, false, false), (false, true, false), (true, false, false), (false, false, true), (true, true, true),
            })
                await RunCase(localExit, unrelatedExit, emptyMemo);
        }

        private async Task RunCase(bool localExit, bool unrelatedExit, bool emptyMemo)
        {
            using var reconciler = new Reconciler();
            var scenario = $"KeyedReads_Local{localExit}_Unrelated{unrelatedExit}_EmptyMemo{emptyMemo}";
            var empty = Memo("empty", () => Empty()).WithKey("empty");
            Element[] Rows(string b, Element? trailing = null, bool lead = false)
            {
                var rows = new List<Element>();
                if (emptyMemo) rows.Add(empty);
                if (lead) rows.Add(Border(TextBlock("x")).WithKey("x"));
                rows.Add(Border(TextBlock("a")).WithKey("a"));
                rows.Add(Border(TextBlock(b)).WithKey("b"));
                rows.Add(Border(TextBlock("c")).WithKey("c"));
                if (trailing is not null) rows.Add(trailing);
                return [.. rows];
            }

            var initial = VStack(Rows("b", localExit ? Leaving("local-exit") : null));
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
                var current = Rows("b");
                reconciler.Update(initial, VStack(current), panel, () => { });
                reconciler.Update(otherInitial, VStack(), otherPanel, () => { });
                var a = panel.Children[0];
                var b = panel.Children[1];
                var c = panel.Children[2];
                H.Check($"{scenario}_PositiveControl",
                    panel.Children.Count == (localExit ? 4 : 3)
                    && current.Length == (emptyMemo ? 4 : 3)
                    && reconciler.HasExitingControls == (localExit || unrelatedExit));

                // Same records: every child takes the structural skip, so the pass neither reads
                // a control nor probes for exits.
                var counting = new CountingChildCollection(new PanelChildCollection(panel));
                ChildReconciler.Reconcile(current, [.. current], counting, reconciler, () => { }, panel);
                H.Check($"{scenario}_SkippedListReadsNoChild",
                    counting.Reads == 0 && counting.Probes == 0,
                    $"reads={counting.Reads} probes={counting.Probes}");

                // Fresh records (a Border's child is compared by reference) update every child,
                // reading each realized control exactly once, as when nothing is retained.
                var unchanged = Rows("b");
                counting = new CountingChildCollection(new PanelChildCollection(panel));
                ChildReconciler.Reconcile(current, unchanged, counting, reconciler, () => { }, panel);
                H.Check($"{scenario}_UpdatedListReadsEachControlOnce",
                    counting.Reads == 3
                    && panel.Children.Where(x => !reconciler.IsExiting(x)).SequenceEqual([a, b, c]),
                    $"reads={counting.Reads}");

                // A changed child patches its own control.
                var changed = Rows("b changed");
                ChildReconciler.Reconcile(unchanged, changed, new PanelChildCollection(panel), reconciler, () => { }, panel);
                H.Check($"{scenario}_ChangedChildPatchesItsControl",
                    a is Border { Child: TextBlock { Text: "a" } }
                    && b is Border { Child: TextBlock { Text: "b changed" } }
                    && c is Border { Child: TextBlock { Text: "c" } }
                    && panel.Children.Where(x => !reconciler.IsExiting(x)).SequenceEqual([a, b, c]));

                // A leading insertion breaks the prefix, so the fresh rows update from the suffix
                // walk, which resolves the map from the front: still one read per realized child.
                var inserted = Rows("b changed", lead: true);
                counting = new CountingChildCollection(new PanelChildCollection(panel));
                ChildReconciler.Reconcile(changed, inserted, counting, reconciler, () => { }, panel);
                var live = panel.Children.Where(x => !reconciler.IsExiting(x)).ToList();
                H.Check($"{scenario}_SuffixReadsEachControlOnce",
                    counting.Reads == 3
                    && live.Count == 4 && live[0] is Border { Child: TextBlock { Text: "x" } }
                    && live.Skip(1).SequenceEqual([a, b, c])
                    && b is Border { Child: TextBlock { Text: "b changed" } },
                    $"reads={counting.Reads} live={live.Count}");

                var completed = await Harness.WaitFor(() => !reconciler.HasExitingControls, maxPasses: 40, perPassMs: 50);
                H.Check($"{scenario}_ExitsComplete", completed);
            }
            finally
            {
                H.SetContent(null);
                reconciler.UnmountChild(panel);
                reconciler.UnmountChild(otherPanel);
            }
        }
    }

    // Exit detection probes the collection once per in-flight exit up to
    // Reconciler.ExitProbeLimit and scans it past that. Either arm must find a retained exit
    // in a panel whose counts match only because a keyed Memo mounts nothing; reading it as
    // the identity would pair every child with its neighbour's control.
    internal class KeyedExitProbeLimitBoundary(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await RunCase(Reconciler.ExitProbeLimit);
            await RunCase(Reconciler.ExitProbeLimit + 1);
        }

        private async Task RunCase(int exits)
        {
            using var reconciler = new Reconciler();
            bool probes = exits <= Reconciler.ExitProbeLimit;
            var scenario = $"KeyedExitProbe_{exits}Exits";
            var empty = Memo("empty", () => Empty()).WithKey("empty");
            Element[] Rows(string suffix) =>
            [
                empty,
                Border(TextBlock("a" + suffix)).WithKey("a"),
                Border(TextBlock("b" + suffix)).WithKey("b"),
                Border(TextBlock("c" + suffix)).WithKey("c"),
            ];
            var others = new Element[exits - 1];
            for (int i = 0; i < others.Length; i++) others[i] = Leaving("other-" + i);
            var otherInitial = VStack(others);
            var initial = VStack([.. Rows(""), Leaving("local")]);
            var otherPanel = (StackPanel)reconciler.Mount(otherInitial, () => { })!;
            var panel = (StackPanel)reconciler.Mount(initial, () => { })!;
            var root = new StackPanel();
            root.Children.Add(panel);
            root.Children.Add(otherPanel);
            H.SetContent(root);
            try
            {
                await Harness.Render();
                var current = Rows("");
                reconciler.Update(otherInitial, VStack(), otherPanel, () => { });
                reconciler.Update(initial, VStack(current), panel, () => { });
                var a = panel.Children[0];
                var b = panel.Children[1];
                var c = panel.Children[2];
                H.Check($"{scenario}_PositiveControl",
                    panel.Children.Count == current.Length && reconciler.IsExiting(panel.Children[3])
                    && otherPanel.Children.Count == exits - 1 && otherPanel.Children.All(reconciler.IsExiting));

                var counting = new CountingChildCollection(new PanelChildCollection(panel));
                ChildReconciler.Reconcile(current, Rows(" 2"), counting, reconciler, () => { }, panel);
                H.Check($"{scenario}_EachChildPatchesItsControl",
                    a is Border { Child: TextBlock { Text: "a 2" } }
                    && b is Border { Child: TextBlock { Text: "b 2" } }
                    && c is Border { Child: TextBlock { Text: "c 2" } }
                    && panel.Children.Where(x => !reconciler.IsExiting(x)).SequenceEqual([a, b, c]));
                // Probing reads only the three mapped controls; scanning first reads all four.
                H.Check($"{scenario}_{(probes ? "Probes" : "Scans")}",
                    probes ? counting.Probes >= 1 && counting.Reads == 3 : counting.Probes == 0 && counting.Reads == 4 + 3,
                    $"probes={counting.Probes} reads={counting.Reads}");

                var completed = await Harness.WaitFor(() => !reconciler.HasExitingControls, maxPasses: 60, perPassMs: 50);
                H.Check($"{scenario}_ExitsComplete", completed && panel.Children.Count == 3 && otherPanel.Children.Count == 0);
            }
            finally
            {
                H.SetContent(null);
                reconciler.UnmountChild(panel);
                reconciler.UnmountChild(otherPanel);
            }
        }
    }

    private static readonly Context<string> SkipArmContext = new("ctx-0");

    private sealed record SkipArmConsumerProps(string Name);

    private sealed class SkipArmConsumer : Component<SkipArmConsumerProps>
    {
        public override Element Render() => TextBlock($"{Props.Name}:{UseContext(SkipArmContext)}");
    }

    // The skip arms that read a child's control (context decline, callback refresh) must
    // reach that child's own control through the keyed map, in the prefix and the suffix,
    // while a keyed Memo that mounts nothing shifts every later physical index.
    internal class KeyedMapSkipArmsUseOwnControls(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            string? clicked = null;
            Action? bump = null;
            var empty = Memo("skip-empty", () => Empty()).WithKey("skip-empty");
            var consumerA = Component<SkipArmConsumer, SkipArmConsumerProps>(new("ctx-a")).WithKey("ctx-a");
            var consumerB = Component<SkipArmConsumer, SkipArmConsumerProps>(new("ctx-b")).WithKey("ctx-b");
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (n, setN) = ctx.UseState(0);
                bump = () => setN(n + 1);
                // The lead key changes every render, so the prefix ends before it and the
                // children after it are reconciled by the suffix walk.
                return VStack(
                        empty,
                        consumerA,
                        Button("skip-arm-a", () => clicked = $"a{n}").WithKey("cb-a"),
                        TextBlock($"lead-{n}").WithKey($"lead-{n}"),
                        Button("skip-arm-b", () => clicked = $"b{n}").WithKey("cb-b"),
                        consumerB)
                    .Provide(SkipArmContext, $"ctx-{n}");
            });
            await Harness.Render();
            var labelA = H.FindText("ctx-a:ctx-0");
            var labelB = H.FindText("ctx-b:ctx-0");
            H.Check("KeyedMapSkipArms_Mounted", labelA is not null && labelB is not null && bump is not null);

            bump!();
            await Harness.Render();
            H.Check("KeyedMapSkipArms_PrefixContextConsumerRerendered",
                H.FindText("ctx-a:ctx-1") is { } a1 && ReferenceEquals(a1, labelA));
            H.Check("KeyedMapSkipArms_SuffixContextConsumerRerendered",
                H.FindText("ctx-b:ctx-1") is { } b1 && ReferenceEquals(b1, labelB));

            H.ClickButton("skip-arm-a");
            await Harness.Render();
            H.Check("KeyedMapSkipArms_PrefixCallbackRefreshed", clicked == "a1", clicked ?? "<none>");
            H.ClickButton("skip-arm-b");
            await Harness.Render();
            H.Check("KeyedMapSkipArms_SuffixCallbackRefreshed", clicked == "b1", clicked ?? "<none>");
        }
    }
}