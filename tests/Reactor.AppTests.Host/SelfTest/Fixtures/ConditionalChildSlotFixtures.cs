using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// An unkeyed conditional child (<c>cond ? X : Empty()</c>, <c>cond ? X : null</c>, a
/// <c>Group</c> that changes size) holds its slot among its siblings, as React's
/// <c>null</c> does. The container factories drop the empty arguments, so before
/// <see cref="ChildSlots"/> every later sibling shifted one index on a toggle and was
/// compared with the child that used to sit there: a sibling of another type remounted
/// (its state reset to the initial value) and a sibling of the same type inherited its
/// neighbour's state.
/// </summary>
internal static class ConditionalChildSlotFixtures
{
    private sealed record CounterProps(string Name, int Start);

    private sealed class Counter : Component<CounterProps>
    {
        public override Element Render()
        {
            var (n, set) = UseState(Props.Start);
            return HStack(TextBlock($"{Props.Name}:{n}"), Button($"{Props.Name}+", () => set(n + 1)));
        }
    }

    // A different component type, so the pre-fix shift paired it with a Counter and remounted.
    private sealed class Tally : Component<CounterProps>
    {
        public override Element Render()
        {
            var (n, set) = UseState(Props.Start);
            return HStack(TextBlock($"{Props.Name}:{n}"), Button($"{Props.Name}+", () => set(n + 1)));
        }
    }

    private static Element C(string name, int start = 0) => Component<Counter, CounterProps>(new(name, start));
    private static Element T(string name, int start = 0) => Component<Tally, CounterProps>(new(name, start));

    private static string? Text(Harness h, string name) =>
        h.FindControl<TextBlock>(t => t.Text.StartsWith(name + ":", StringComparison.Ordinal))?.Text;

    private static TextBlock? Label(Harness h, string name) =>
        h.FindControl<TextBlock>(t => t.Text.StartsWith(name + ":", StringComparison.Ordinal));

    /// <summary>The counters' names in visual (panel) order.</summary>
    private static string Order(Harness h, params string[] names) =>
        string.Join(",", h.FindAllControls<TextBlock>(t => names.Any(n => t.Text.StartsWith(n + ":", StringComparison.Ordinal)))
            .Select(t => t.Text[..t.Text.IndexOf(':')]));

    private static async Task Bump(Harness h, string name, int times)
    {
        for (int i = 0; i < times; i++)
        {
            h.ClickButton(name + "+");
            await Harness.Render();
        }
    }

    internal class HideShowMiddle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                return VStack(
                    Button("slot-toggle", () => setShow(!show)),
                    show ? C("t") : Empty(),
                    T("a", 5),
                    C("b", 100));
            });
            await Harness.Render();
            await Bump(H, "a", 2);
            await Bump(H, "b", 1);
            var aLabel = Label(H, "a");
            H.Check("SlotHideShow_Setup", Text(H, "a") == "a:7" && Text(H, "b") == "b:101");

            H.ClickButton("slot-toggle");
            await Harness.Render();
            H.Check("SlotHideShow_HideKeepsTypedSibling", Text(H, "a") == "a:7", $"a={Text(H, "a")}");
            H.Check("SlotHideShow_HideKeepsLaterSibling", Text(H, "b") == "b:101", $"b={Text(H, "b")}");
            H.Check("SlotHideShow_HideKeepsControl", aLabel is not null && ReferenceEquals(aLabel, Label(H, "a")));
            H.Check("SlotHideShow_HideRemovesChild", Text(H, "t") is null && Order(H, "t", "a", "b") == "a,b",
                Order(H, "t", "a", "b"));

            H.ClickButton("slot-toggle");
            await Harness.Render();
            H.Check("SlotHideShow_ShowKeepsSiblings", Text(H, "a") == "a:7" && Text(H, "b") == "b:101",
                $"a={Text(H, "a")} b={Text(H, "b")}");
            H.Check("SlotHideShow_ShowKeepsControl", aLabel is not null && ReferenceEquals(aLabel, Label(H, "a")));
            H.Check("SlotHideShow_ShowInsertsInPlace", Order(H, "t", "a", "b") == "t,a,b", Order(H, "t", "a", "b"));
            H.Check("SlotHideShow_ShownChildIsFresh", Text(H, "t") == "t:0");
            host.Dispose();
        }
    }

    internal class SameTypeSiblingKeepsOwnState(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                return VStack(
                    Button("slot-toggle", () => setShow(!show)),
                    show ? C("x") : Empty(),
                    C("y"));
            });
            await Harness.Render();
            await Bump(H, "y", 3);
            await Bump(H, "x", 1);
            H.Check("SlotSameType_Setup", Text(H, "x") == "x:1" && Text(H, "y") == "y:3");

            H.ClickButton("slot-toggle");
            await Harness.Render();
            // Pre-fix: y's props landed on x's instance, so y showed x's count (1).
            H.Check("SlotSameType_HideKeepsOwnState", Text(H, "y") == "y:3", $"y={Text(H, "y")}");
            H.Check("SlotSameType_HiddenGone", Text(H, "x") is null);

            H.ClickButton("slot-toggle");
            await Harness.Render();
            H.Check("SlotSameType_ShowKeepsOwnState", Text(H, "y") == "y:3" && Text(H, "x") == "x:0",
                $"x={Text(H, "x")} y={Text(H, "y")}");
            H.Check("SlotSameType_Order", Order(H, "x", "y") == "x,y", Order(H, "x", "y"));
            host.Dispose();
        }
    }

    internal class NullConditional(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                return HStack(
                    Button("slot-toggle", () => setShow(!show)),
                    show ? T("t") : null,
                    C("a"));
            });
            await Harness.Render();
            await Bump(H, "a", 4);

            H.ClickButton("slot-toggle");
            await Harness.Render();
            H.Check("SlotNull_HideKeepsSibling", Text(H, "a") == "a:4", $"a={Text(H, "a")}");

            H.ClickButton("slot-toggle");
            await Harness.Render();
            H.Check("SlotNull_ShowKeepsSibling", Text(H, "a") == "a:4" && Order(H, "t", "a") == "t,a",
                $"a={Text(H, "a")} order={Order(H, "t", "a")}");
            host.Dispose();
        }
    }

    internal class EmptyElementEmpty(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(false);
                return VStack(
                    Button("slot-toggle", () => setShow(!show)),
                    C("h"),
                    show ? T("t") : Empty(),
                    C("a"),
                    show ? T("u") : Empty(),
                    C("b"));
            });
            await Harness.Render();
            await Bump(H, "h", 1);
            await Bump(H, "a", 2);
            await Bump(H, "b", 3);
            var bLabel = Label(H, "b");
            const string names = "h,t,a,u,b";
            string Ord() => Order(H, names.Split(','));

            H.ClickButton("slot-toggle");
            await Harness.Render();
            H.Check("SlotEEE_ShowKeepsAll", Text(H, "h") == "h:1" && Text(H, "a") == "a:2" && Text(H, "b") == "b:3",
                $"h={Text(H, "h")} a={Text(H, "a")} b={Text(H, "b")}");
            H.Check("SlotEEE_ShowOrder", Ord() == "h,t,a,u,b", Ord());

            await Bump(H, "t", 1);
            H.ClickButton("slot-toggle");
            await Harness.Render();
            H.Check("SlotEEE_HideKeepsAll", Text(H, "h") == "h:1" && Text(H, "a") == "a:2" && Text(H, "b") == "b:3",
                $"h={Text(H, "h")} a={Text(H, "a")} b={Text(H, "b")}");
            H.Check("SlotEEE_HideOrder", Ord() == "h,a,b", Ord());
            H.Check("SlotEEE_KeepsControl", bLabel is not null && ReferenceEquals(bLabel, Label(H, "b")));
            host.Dispose();
        }
    }

    internal class GroupResize(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (two, setTwo) = ctx.UseState(true);
                return VStack(
                    Button("slot-toggle", () => setTwo(!two)),
                    two ? Group(C("g1"), T("g2")) : Group(C("g1")),
                    T("z"));
            });
            await Harness.Render();
            await Bump(H, "g1", 1);
            await Bump(H, "z", 2);

            H.ClickButton("slot-toggle");
            await Harness.Render();
            H.Check("SlotGroup_ShrinkKeepsLaterSibling", Text(H, "z") == "z:2", $"z={Text(H, "z")}");
            H.Check("SlotGroup_ShrinkKeepsGroupChild", Text(H, "g1") == "g1:1" && Text(H, "g2") is null);

            H.ClickButton("slot-toggle");
            await Harness.Render();
            H.Check("SlotGroup_GrowKeepsAll", Text(H, "z") == "z:2" && Text(H, "g1") == "g1:1" && Text(H, "g2") == "g2:0",
                $"g1={Text(H, "g1")} z={Text(H, "z")}");
            H.Check("SlotGroup_GrowOrder", Order(H, "g1", "g2", "z") == "g1,g2,z", Order(H, "g1", "g2", "z"));
            host.Dispose();
        }
    }

    internal class ExitTransition(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                return VStack(
                    Button("slot-toggle", () => setShow(!show)),
                    show ? Border(TextBlock("e1:fade")).Transition(Animation.Transition.Fade) : Empty(),
                    C("a"),
                    show ? Border(TextBlock("e2:fade")).Transition(Animation.Transition.Fade) : Empty(),
                    T("b"));
            });
            await Harness.Render();
            await Bump(H, "a", 1);
            await Bump(H, "b", 2);
            var aLabel = Label(H, "a");

            // Both conditionals leave in one pass and linger for their exit fade; the walk
            // must step over each lingering control to reach the sibling after it.
            H.ClickButton("slot-toggle");
            await Harness.Render();
            H.Check("SlotExit_SiblingsKeptDuringExit", Text(H, "a") == "a:1" && Text(H, "b") == "b:2",
                $"a={Text(H, "a")} b={Text(H, "b")}");
            H.Check("SlotExit_KeepsControl", aLabel is not null && ReferenceEquals(aLabel, Label(H, "a")));
            var gone = await Harness.WaitFor(() => Text(H, "e1") is null && Text(H, "e2") is null, maxPasses: 40, perPassMs: 50);
            H.Check("SlotExit_ExitsComplete", gone);
            H.Check("SlotExit_OrderAfterExit", Order(H, "e1", "a", "e2", "b") == "a,b", Order(H, "e1", "a", "e2", "b"));

            H.ClickButton("slot-toggle");
            await Harness.Render();
            H.Check("SlotExit_ShowOrder", Order(H, "e1", "a", "e2", "b") == "e1,a,e2,b", Order(H, "e1", "a", "e2", "b"));
            H.Check("SlotExit_ShowKeepsSiblings", Text(H, "a") == "a:1" && Text(H, "b") == "b:2");
            host.Dispose();
        }
    }

    internal class RetoggleDuringExit(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                return VStack(
                    Button("slot-toggle", () => setShow(!show)),
                    show ? Border(TextBlock("r1:fade")).Transition(Animation.Transition.Fade) : Empty(),
                    C("a"),
                    show ? Border(TextBlock("r2:fade")).Transition(Animation.Transition.Fade) : Empty(),
                    T("b"));
            });
            await Harness.Render();
            await Bump(H, "a", 1);
            await Bump(H, "b", 2);
            var aLabel = Label(H, "a");
            var bLabel = Label(H, "b");

            // Hide, then show again while both hidden rows are still fading out: the
            // collection then holds controls that belong to no old child, which the walk
            // must step over to find each sibling's control.
            H.ClickButton("slot-toggle");
            await Harness.Render();
            H.ClickButton("slot-toggle");
            await Harness.Render();
            H.Check("SlotRetoggle_KeepsSiblings", Text(H, "a") == "a:1" && Text(H, "b") == "b:2",
                $"a={Text(H, "a")} b={Text(H, "b")}");
            H.Check("SlotRetoggle_KeepsControls",
                aLabel is not null && ReferenceEquals(aLabel, Label(H, "a"))
                && bLabel is not null && ReferenceEquals(bLabel, Label(H, "b")));

            await Harness.WaitFor(() => Order(H, "r1", "a", "r2", "b") == "r1,a,r2,b", maxPasses: 40, perPassMs: 50);
            H.Check("SlotRetoggle_OrderAfterExits", Order(H, "r1", "a", "r2", "b") == "r1,a,r2,b",
                Order(H, "r1", "a", "r2", "b"));
            H.Check("SlotRetoggle_StillResponds", await BumpAndRead(H, "a") == "a:2");
            host.Dispose();
        }

        private static async Task<string?> BumpAndRead(Harness h, string name)
        {
            await Bump(h, name, 1);
            return Text(h, name);
        }
    }

    internal class CallbackSkipArm(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                var (n, setN) = ctx.UseState(0);
                // Every click toggles the conditional child before this Button, so each pass
                // takes the slot walk with the Button at a live index other than its old one,
                // and the Button's element differs only by its closure — the skip arm, which
                // must refresh the closure on the Button's own control.
                return VStack(
                    show ? C("t") : Empty(),
                    Button("slot-inc", () =>
                    {
                        setN(n + 1);
                        setShow(!show);
                    }),
                    TextBlock($"n:{n}"));
            });
            await Harness.Render();
            for (int i = 0; i < 3; i++)
            {
                H.ClickButton("slot-inc");
                await Harness.Render();
            }
            H.Check("SlotSkipArm_ClosureRefreshed", Text(H, "n") == "n:3", $"n={Text(H, "n")}");
            host.Dispose();
        }
    }

    internal class ReplaceWithExitThenRerender(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (swapped, setSwapped) = ctx.UseState(false);
                var (n, setN) = ctx.UseState(0);
                return VStack(
                    // One click shifts slots (the conditional) and changes the next child's
                    // type, so the slot walk replaces it while the old one fades out after it.
                    Button("slot-swap", () => setSwapped(!swapped)),
                    Button("slot-tick", () => setN(n + 1)),
                    swapped ? Empty() : C("t"),
                    swapped
                        ? Border(TextBlock("x2:fade")).Transition(Animation.Transition.Fade)
                        : TextBlock("x1:fade").Transition(Animation.Transition.Fade),
                    C("a"),
                    HStack(TextBlock($"sib:{n}"), T("s")));
            });
            await Harness.Render();
            await Bump(H, "a", 1);
            await Bump(H, "s", 2);
            var aLabel = Label(H, "a");
            var sLabel = Label(H, "s");

            H.ClickButton("slot-swap");
            await Harness.Render();
            H.Check("SlotReplaceExit_KeepsSiblings", Text(H, "a") == "a:1" && Text(H, "s") == "s:2",
                $"a={Text(H, "a")} s={Text(H, "s")}");
            H.Check("SlotReplaceExit_KeepsControls",
                aLabel is not null && ReferenceEquals(aLabel, Label(H, "a"))
                && sLabel is not null && ReferenceEquals(sLabel, Label(H, "s")));

            // Re-render while x1 still fades: nothing shifts slots, but the walk must still
            // step past the fading control to pair each child with its own control.
            H.ClickButton("slot-tick");
            await Harness.Render();
            H.Check("SlotReplaceExit_RerenderDuringExit",
                Text(H, "a") == "a:1" && Text(H, "s") == "s:2" && Text(H, "sib") == "sib:1"
                && aLabel is not null && ReferenceEquals(aLabel, Label(H, "a")),
                $"a={Text(H, "a")} s={Text(H, "s")} sib={Text(H, "sib")}");

            await Harness.WaitFor(() => Text(H, "x1") is null, maxPasses: 40, perPassMs: 50);
            H.Check("SlotReplaceExit_Order", Order(H, "t", "x1", "x2", "a", "sib") == "x2,a,sib",
                Order(H, "t", "x1", "x2", "a", "sib"));
            host.Dispose();
        }
    }

    internal class ReplaceWithExitPositional(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (swapped, setSwapped) = ctx.UseState(false);
                // No conditional and no shift: the index walk replaces the first child, whose
                // exit transition re-inserts its old control right after the new one.
                return VStack(
                    swapped
                        ? Border(TextBlock("y:fade")).Transition(Animation.Transition.Fade)
                        : TextBlock("x:fade").Transition(Animation.Transition.Fade),
                    C("a"),
                    T("b"),
                    Button("slot-swap", () => setSwapped(!swapped)));
            });
            await Harness.Render();
            await Bump(H, "a", 1);
            await Bump(H, "b", 2);
            var aLabel = Label(H, "a");

            H.ClickButton("slot-swap");
            await Harness.Render();
            H.Check("SlotReplacePositional_KeepsSiblings", Text(H, "a") == "a:1" && Text(H, "b") == "b:2",
                $"a={Text(H, "a")} b={Text(H, "b")}");
            H.Check("SlotReplacePositional_KeepsControl", aLabel is not null && ReferenceEquals(aLabel, Label(H, "a")));
            await Harness.WaitFor(() => Text(H, "x") is null, maxPasses: 40, perPassMs: 50);
            H.Check("SlotReplacePositional_Order", Order(H, "x", "y", "a", "b") == "y,a,b", Order(H, "x", "y", "a", "b"));
            H.Check("SlotReplacePositional_ButtonKept", H.FindButton("slot-swap") is not null);
            host.Dispose();
        }
    }

    internal class NestedGroupToggle(Harness h, int depth = 1) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                // Same-type counters, so a shifted slot would hand one counter another's state.
                // `depth` groups wrap z; past four levels its path is shortened, never its siblings'.
                Element z = C("z");
                for (int d = 0; d < depth; d++) z = Group(z);
                return VStack(
                    Button("slot-toggle", () => setShow(!show)),
                    Group(show ? C("x") : Empty(), C("y"), z));
            });
            await Harness.Render();
            await Bump(H, "x", 1);
            await Bump(H, "y", 2);
            await Bump(H, "z", 3);
            var zLabel = Label(H, "z");

            H.ClickButton("slot-toggle");
            await Harness.Render();
            H.Check($"SlotNestedGroup{depth}_HideKeepsSiblings", Text(H, "y") == "y:2" && Text(H, "z") == "z:3",
                $"y={Text(H, "y")} z={Text(H, "z")}");
            H.Check($"SlotNestedGroup{depth}_HideKeepsControl", zLabel is not null && ReferenceEquals(zLabel, Label(H, "z")));

            H.ClickButton("slot-toggle");
            await Harness.Render();
            H.Check($"SlotNestedGroup{depth}_ShowKeepsSiblings",
                Text(H, "x") == "x:0" && Text(H, "y") == "y:2" && Text(H, "z") == "z:3",
                $"x={Text(H, "x")} y={Text(H, "y")} z={Text(H, "z")}");
            H.Check($"SlotNestedGroup{depth}_Order", Order(H, "x", "y", "z") == "x,y,z", Order(H, "x", "y", "z"));
            host.Dispose();
        }
    }

    internal class UniformGridExitKeepsColumns(Harness h) : SelfTestFixtureBase(h)
    {
        private int Column(string name)
        {
            Microsoft.UI.Xaml.DependencyObject? node = Label(H, name);
            while (node is not null && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node) is not Microsoft.UI.Xaml.Controls.Grid)
                node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
            return node is Microsoft.UI.Xaml.FrameworkElement cell ? Microsoft.UI.Xaml.Controls.Grid.GetColumn(cell) : -1;
        }

        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                return VStack(
                    Button("slot-toggle", () => setShow(!show)),
                    UniformGrid(Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                        show ? Border(TextBlock("u0:fade")).Transition(Animation.Transition.Fade) : Empty(),
                        C("a"),
                        T("b")));
            });
            await Harness.Render();
            H.Check("SlotGridExit_Setup", Column("a") == 1 && Column("b") == 2, $"a={Column("a")} b={Column("b")}");

            // The fading first cell stays in the grid until its exit ends; the survivors'
            // columns must already be their new ones, and stay so once it is gone.
            H.ClickButton("slot-toggle");
            await Harness.Render();
            H.Check("SlotGridExit_ColumnsDuringExit", Column("a") == 0 && Column("b") == 1, $"a={Column("a")} b={Column("b")}");
            await Harness.WaitFor(() => Text(H, "u0") is null, maxPasses: 40, perPassMs: 50);
            H.Check("SlotGridExit_ColumnsAfterExit", Column("a") == 0 && Column("b") == 1, $"a={Column("a")} b={Column("b")}");
            host.Dispose();
        }
    }

    internal class AnimateAmbient(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(false);
                return VStack(
                    Button("slot-animate", () => Animations.Animate(AnimationKind.Spring, () => setShow(!show))),
                    C("a"),
                    show ? T("t") : Empty(),
                    C("b"));
            });
            await Harness.Render();
            await Bump(H, "a", 1);
            await Bump(H, "b", 2);

            H.ClickButton("slot-animate");
            await Harness.Render();
            H.Check("SlotAnimate_InsertInPlace", Order(H, "a", "t", "b") == "a,t,b", Order(H, "a", "t", "b"));
            H.Check("SlotAnimate_InsertKeepsSiblings", Text(H, "a") == "a:1" && Text(H, "b") == "b:2",
                $"a={Text(H, "a")} b={Text(H, "b")}");

            H.ClickButton("slot-animate");
            await Harness.Render();
            H.Check("SlotAnimate_RemoveKeepsSiblings", Text(H, "a") == "a:1" && Text(H, "b") == "b:2",
                $"a={Text(H, "a")} b={Text(H, "b")}");
            // Whether the ambient fade's Composition batch has completed by now varies with
            // the run (it predates slot matching); either way the fading child sits in its own
            // slot, between its siblings, and they keep their state.
            var gone = await Harness.WaitFor(() => Text(H, "t") is null, maxPasses: 40, perPassMs: 50);
            Console.WriteLine($"# ambient exit completed: {gone}");
            var order = Order(H, "a", "t", "b");
            H.Check("SlotAnimate_RemovedChildHoldsItsSlot", order is "a,b" or "a,t,b", order);
            H.Check("SlotAnimate_SiblingsKeptAfterExit", Text(H, "a") == "a:1" && Text(H, "b") == "b:2",
                $"a={Text(H, "a")} b={Text(H, "b")}");
            host.Dispose();
        }
    }

    // ── A sibling that updated its own state in the same pass as the toggle ──

    private static Action? s_bumpStable;

    private sealed class Stable : Component
    {
        public override Element Render()
        {
            var (n, set) = UseState(0);
            s_bumpStable = () => set(n + 1);
            return TextBlock($"s:{n}");
        }
    }

    internal class SelfTriggeredSiblingDuringToggle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                // Reference-equal across renders, so the reconciler skips it unless the
                // dirty-ancestor path names it — by its index before this pass shifted it.
                var stable = ctx.UseMemo(() => Component<Stable>(), Array.Empty<object>());
                return VStack(
                    Button("slot-toggle-and-bump", () =>
                    {
                        setShow(!show);
                        s_bumpStable?.Invoke();
                    }),
                    show ? C("t") : Empty(),
                    C("a"),
                    stable);
            });
            await Harness.Render();
            H.Check("SlotDirty_Setup", Text(H, "s") == "s:0");

            H.ClickButton("slot-toggle-and-bump");
            await Harness.Render();
            H.Check("SlotDirty_HideRendersSelfTriggered", Text(H, "s") == "s:1", $"s={Text(H, "s")}");

            H.ClickButton("slot-toggle-and-bump");
            await Harness.Render();
            H.Check("SlotDirty_ShowRendersSelfTriggered", Text(H, "s") == "s:2", $"s={Text(H, "s")}");
            H.Check("SlotDirty_Order", Order(H, "t", "a", "s") == "t,a,s", Order(H, "t", "a", "s"));
            host.Dispose();
        }
    }
}
