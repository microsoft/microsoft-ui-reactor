using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Stress tests targeting uncovered branches in the reconciler diff system:
///   - Keyed suffix matching (ChildReconciler lines 126-144)
///   - Keyed middle: removal of unmatched items (lines 232-244)
///   - Keyed middle: LIS reorder + move (lines 260-313)
///   - Keyed middle: new item insertion (lines 265-269)
///   - Positional type mismatch replacement (lines 66-72)
///   - Component unmount cleanup (Reconciler lines 286-324)
///   - FuncElement render path (Reconciler lines 209-214)
///   - Error boundary during render (Reconciler lines 217-221)
///   - Element-to-null transition (Reconciler lines 145-149)
/// </summary>
internal static class ReconcilerStressFixtures
{
    // ════════════════════════════════════════════════════════════════════
    //  Keyed suffix reconciliation
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tests the keyed suffix matching phase: old=[A,B,C,D], new=[X,Y,C,D].
    /// C and D match as suffix, X and Y are new in the middle, A and B removed.
    /// Exercises ChildReconciler lines 126-144 (suffix update-in-place).
    /// </summary>
    internal class KeyedSuffixMatch(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                Element[] items = phase == 0
                    ? [TextBlock("A").WithKey("a"), TextBlock("B").WithKey("b"), TextBlock("C").WithKey("c"), TextBlock("D").WithKey("d")]
                    : [TextBlock("X").WithKey("x"), TextBlock("Y").WithKey("y"), TextBlock("C").WithKey("c"), TextBlock("D").WithKey("d")];

                return VStack(
                    Button("Switch", () => setPhase(1)),
                    VStack(items)
                );
            });

            await Harness.Render();

            var cBefore = H.FindText("C");
            var dBefore = H.FindText("D");
            H.Check("KeyedSuffix_InitialPresent",
                H.FindText("A") is not null && cBefore is not null && dBefore is not null);

            H.ClickButton("Switch");
            await Harness.Render();

            H.Check("KeyedSuffix_OldRemoved",
                H.FindText("A") is null && H.FindText("B") is null);
            H.Check("KeyedSuffix_NewInserted",
                H.FindText("X") is not null && H.FindText("Y") is not null);
            H.Check("KeyedSuffix_SuffixPreserved",
                H.FindText("C") is not null && H.FindText("D") is not null);
            // Suffix controls should be reused (same TextBlock instance)
            H.Check("KeyedSuffix_SuffixReused",
                ReferenceEquals(cBefore, H.FindText("C")) && ReferenceEquals(dBefore, H.FindText("D")));
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Keyed middle: removal of unmatched items
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tests removal of items from a keyed middle section.
    /// Old=[A,B,C,D,E], New=[A,C,E]. B and D are removed from the middle.
    /// Exercises ChildReconciler lines 232-244 (unmatched old item removal).
    /// </summary>
    internal class KeyedMiddleRemoval(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                Element[] items = phase == 0
                    ? [TextBlock("A").WithKey("a"), TextBlock("B").WithKey("b"), TextBlock("C").WithKey("c"), TextBlock("D").WithKey("d"), TextBlock("E").WithKey("e")]
                    : [TextBlock("A").WithKey("a"), TextBlock("C").WithKey("c"), TextBlock("E").WithKey("e")];

                return VStack(
                    Button("Remove", () => setPhase(1)),
                    VStack(items)
                );
            });

            await Harness.Render();
            H.Check("KeyedRemoval_Initial", H.FindText("B") is not null && H.FindText("D") is not null);

            H.ClickButton("Remove");
            await Harness.Render();

            H.Check("KeyedRemoval_BRemoved", H.FindText("B") is null);
            H.Check("KeyedRemoval_DRemoved", H.FindText("D") is null);
            H.Check("KeyedRemoval_ACEPresent",
                H.FindText("A") is not null && H.FindText("C") is not null && H.FindText("E") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Keyed middle: complex LIS reorder with moves
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tests complex reorder requiring LIS computation and move operations.
    /// Old=[A,B,C,D,E], New=[E,C,A,D,B]. Only one element stays in LIS position;
    /// the rest must be moved.
    /// Exercises ChildReconciler lines 260-313 (LIS-based move logic).
    /// </summary>
    internal class KeyedComplexReorder(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                Element[] items = phase == 0
                    ? [TextBlock("A").WithKey("a"), TextBlock("B").WithKey("b"), TextBlock("C").WithKey("c"), TextBlock("D").WithKey("d"), TextBlock("E").WithKey("e")]
                    : [TextBlock("E").WithKey("e"), TextBlock("C").WithKey("c"), TextBlock("A").WithKey("a"), TextBlock("D").WithKey("d"), TextBlock("B").WithKey("b")];

                return VStack(
                    Button("Reorder", () => setPhase(1)),
                    VStack(items)
                );
            });

            await Harness.Render();
            var aBefore = H.FindText("A");
            H.Check("KeyedReorder_AllInitial",
                aBefore is not null && H.FindText("E") is not null);

            H.ClickButton("Reorder");
            await Harness.Render();

            // All items still present
            H.Check("KeyedReorder_AllPresent",
                H.FindText("A") is not null && H.FindText("B") is not null &&
                H.FindText("C") is not null && H.FindText("D") is not null &&
                H.FindText("E") is not null);

            // Controls reused (keyed reconciliation preserves instances)
            H.Check("KeyedReorder_Reused", ReferenceEquals(aBefore, H.FindText("A")));

            // Verify all 5 items are still in the tree (no duplicates or losses)
            var allText = H.FindAllControls<TextBlock>(tb =>
                tb.Text is "A" or "B" or "C" or "D" or "E");
            H.Check("KeyedReorder_NoLostItems", allText.Count == 5);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Keyed middle: interleaved insert + remove + reorder
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tests simultaneous insertion of new items, removal of old items, and reordering
    /// of surviving items in a single reconciliation pass.
    /// Old=[A,B,C,D], New=[D,X,B,Y]. A and C removed, X and Y inserted, D and B reordered.
    /// Exercises all three keyed middle paths: removal (232-244), insertion (265-269), move (287-313).
    /// </summary>
    internal class KeyedInsertRemoveReorder(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                Element[] items = phase == 0
                    ? [TextBlock("A").WithKey("a"), TextBlock("B").WithKey("b"), TextBlock("C").WithKey("c"), TextBlock("D").WithKey("d")]
                    : [TextBlock("D").WithKey("d"), TextBlock("X").WithKey("x"), TextBlock("B").WithKey("b"), TextBlock("Y").WithKey("y")];

                return VStack(
                    Button("Change", () => setPhase(1)),
                    VStack(items)
                );
            });

            await Harness.Render();
            var bBefore = H.FindText("B");
            var dBefore = H.FindText("D");

            H.ClickButton("Change");
            await Harness.Render();

            H.Check("KeyedMixed_ACRemoved",
                H.FindText("A") is null && H.FindText("C") is null);
            H.Check("KeyedMixed_XYInserted",
                H.FindText("X") is not null && H.FindText("Y") is not null);
            H.Check("KeyedMixed_BDReused",
                ReferenceEquals(bBefore, H.FindText("B")) && ReferenceEquals(dBefore, H.FindText("D")));

            var allText = H.FindAllControls<TextBlock>(tb =>
                tb.Text is "D" or "X" or "B" or "Y");
            H.Check("KeyedMixed_AllPresent", allText.Count == 4);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Positional type mismatch replacement
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tests positional reconciliation when the element type changes at the same index.
    /// Old=[Text, Button], New=[Button, Text]. Neither has keys, so positional reconciliation
    /// detects type mismatch at both indices and replaces each control.
    /// Exercises ChildReconciler lines 66-72 (positional type mismatch → unmount + mount).
    /// </summary>
    internal class PositionalTypeMismatch(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                Element[] items = phase == 0
                    ? [TextBlock("TypeA"), Button("TypeB")]
                    : [Button("TypeC"), TextBlock("TypeD")];

                return VStack(
                    Button("Swap", () => setPhase(1)),
                    VStack(items)
                );
            });

            await Harness.Render();
            H.Check("PositionalMismatch_Initial",
                H.FindText("TypeA") is not null && H.FindButton("TypeB") is not null);

            H.ClickButton("Swap");
            await Harness.Render();

            H.Check("PositionalMismatch_TextGone", H.FindText("TypeA") is null);
            H.Check("PositionalMismatch_NewButton", H.FindButton("TypeC") is not null);
            H.Check("PositionalMismatch_NewText", H.FindText("TypeD") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  FuncElement render path
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tests the FuncElement component rendering and re-rendering path.
    /// FuncElement uses RenderContext directly (not a Component subclass).
    /// Exercises Reconciler ReconcileComponent lines 209-214 (FuncElement path).
    /// </summary>
    internal class FuncElementRerender(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                // Outer func wraps an inner func — tests nested FuncElement reconciliation.
                // Intentionally exercises the legacy Func() factory so this stress
                // fixture still covers it until the obsolete overload is removed.
#pragma warning disable CS0618
                return Func(innerCtx =>
                {
                    var (count, setCount) = innerCtx.UseState(0);
                    return VStack(
                        TextBlock($"FuncCount: {count}"),
                        Button("FuncInc", () => setCount(count + 1))
                    );
                });
#pragma warning restore CS0618
            });

            await Harness.Render();
            H.Check("FuncElement_InitialRender", H.FindText("FuncCount: 0") is not null);

            H.ClickButton("FuncInc");
            await Harness.Render();
            H.Check("FuncElement_AfterRerender", H.FindText("FuncCount: 1") is not null);

            H.ClickButton("FuncInc");
            await Harness.Render();
            H.Check("FuncElement_SecondRerender", H.FindText("FuncCount: 2") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Element-to-null transition (unmount existing)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tests the transition from a rendered element to null/EmptyElement.
    /// Exercises Reconciler.Reconcile lines 145-149 (unmount existing control).
    /// </summary>
    internal class ElementToNullTransition(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                return VStack(
                    Button("Toggle", () => setShow(!show)),
                    show ? Border(TextBlock("Visible")) : null
                );
            });

            await Harness.Render();
            H.Check("NullTransition_InitiallyVisible", H.FindText("Visible") is not null);

            H.ClickButton("Toggle");
            await Harness.Render();
            H.Check("NullTransition_RemovedToNull", H.FindText("Visible") is null);

            H.ClickButton("Toggle");
            await Harness.Render();
            H.Check("NullTransition_RestoredFromNull", H.FindText("Visible") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Component unmount with cleanup
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tests that components with UseEffect cleanup are properly unmounted
    /// when removed from the tree. Exercises Reconciler UnmountRecursive
    /// lines 286-324 (component node cleanup, container children traversal).
    /// </summary>
    internal class ComponentUnmountCleanup(Harness h) : SelfTestFixtureBase(h)
    {
        private class CleanupTrackingComponent : Component
        {
            public static int CleanupCount;
            public override Element Render()
            {
                UseEffect(() =>
                {
                    // Setup
                    return () => { CleanupCount++; }; // Cleanup
                });
                return TextBlock("Cleanup Tracked");
            }
        }

        public override async Task RunAsync()
        {
            CleanupTrackingComponent.CleanupCount = 0;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                return VStack(
                    Button("RemoveComp", () => setShow(false)),
                    show ? Component<CleanupTrackingComponent>() : null
                );
            });

            await Harness.Render();
            H.Check("CompCleanup_Mounted", H.FindText("Cleanup Tracked") is not null);
            H.Check("CompCleanup_NoCleanupYet", CleanupTrackingComponent.CleanupCount == 0);

            H.ClickButton("RemoveComp");
            await Harness.Render();

            H.Check("CompCleanup_Unmounted", H.FindText("Cleanup Tracked") is null);
            H.Check("CompCleanup_CleanupRan", CleanupTrackingComponent.CleanupCount == 1);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Nested container unmount (Border → ScrollView → children)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tests unmounting of nested containers (Border wrapping ScrollView wrapping children).
    /// Exercises UnmountRecursive lines 308-319 (Border.Child, ScrollViewer.Content traversal).
    /// </summary>
    internal class NestedContainerUnmount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, setShow) = ctx.UseState(true);
                return VStack(
                    Button("RemoveNested", () => setShow(false)),
                    show
                        ? Border(ScrollView(VStack(
                            TextBlock("Nested1"),
                            TextBlock("Nested2"),
                            TextBlock("Nested3"))))
                        : null
                );
            });

            await Harness.Render();
            H.Check("NestedUnmount_AllPresent",
                H.FindText("Nested1") is not null &&
                H.FindText("Nested2") is not null &&
                H.FindText("Nested3") is not null);

            H.ClickButton("RemoveNested");
            await Harness.Render();

            H.Check("NestedUnmount_AllRemoved",
                H.FindText("Nested1") is null &&
                H.FindText("Nested2") is null &&
                H.FindText("Nested3") is null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Large keyed list stress test
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Stress test: mount 50 keyed items, reverse them, then shuffle.
    /// Exercises the LIS algorithm and move logic at scale.
    /// </summary>
    internal class KeyedLargeListStress(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            const int count = 50;

            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);

                int[] indices;
                if (phase == 0)
                    indices = Enumerable.Range(0, count).ToArray();
                else if (phase == 1)
                    indices = Enumerable.Range(0, count).Reverse().ToArray();
                else
                {
                    // Deterministic shuffle
                    indices = Enumerable.Range(0, count).ToArray();
                    var rng = new Random(42);
                    for (int i = indices.Length - 1; i > 0; i--)
                    {
                        int j = rng.Next(i + 1);
                        (indices[i], indices[j]) = (indices[j], indices[i]);
                    }
                }

                return VStack(
                    HStack(
                        Button("Reverse", () => setPhase(1)),
                        Button("Shuffle", () => setPhase(2))
                    ),
                    VStack(indices.Select(i => TextBlock($"K{i}").WithKey($"k{i}")).ToArray())
                );
            });

            await Harness.Render();
            H.Check("LargeKeyed_InitialFirst", H.FindText("K0") is not null);
            H.Check("LargeKeyed_InitialLast", H.FindText($"K{count - 1}") is not null);

            var k0Before = H.FindText("K0");

            H.ClickButton("Reverse");
            await Harness.Render();

            // All items still present after reversal
            var allK = H.FindAllControls<TextBlock>(tb => tb.Text?.StartsWith("K") == true);
            H.Check("LargeKeyed_ReversedCount", allK.Count == count);
            H.Check("LargeKeyed_ReversedReuse", ReferenceEquals(k0Before, H.FindText("K0")));

            H.ClickButton("Shuffle");
            await Harness.Render();

            var allKShuffled = H.FindAllControls<TextBlock>(tb => tb.Text?.StartsWith("K") == true);
            H.Check("LargeKeyed_ShuffledCount", allKShuffled.Count == count);
            H.Check("LargeKeyed_ShuffledReuse", ReferenceEquals(k0Before, H.FindText("K0")));
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Multiple reconcile cycles (grow → reorder → shrink → grow)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Exercises multiple reconciliation cycles: grow keyed list, then shrink it.
    /// Uses unique text markers to avoid visual tree ambiguity.
    /// </summary>
    internal class MultiCycleReconcile(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);

                Element[] items = phase switch
                {
                    0 => [TextBlock("MC_A").WithKey("a"), TextBlock("MC_B").WithKey("b")],
                    1 => [TextBlock("MC_A").WithKey("a"), TextBlock("MC_B").WithKey("b"), TextBlock("MC_C").WithKey("c"), TextBlock("MC_D").WithKey("d")],
                    _ => [TextBlock("MC_B").WithKey("b"), TextBlock("MC_D").WithKey("d")],
                };

                return VStack(
                    Button("MCNext", () => setPhase(phase + 1)),
                    VStack(items)
                );
            });

            // Phase 0: [A, B]
            await Harness.Render();
            H.Check("MultiCycle_Phase0",
                H.FindText("MC_A") is not null && H.FindText("MC_B") is not null);

            // Phase 1: grow → [A, B, C, D]
            H.ClickButton("MCNext");
            await Harness.Render();
            H.Check("MultiCycle_Phase1_Grew",
                H.FindText("MC_C") is not null && H.FindText("MC_D") is not null);

            var bRef = H.FindText("MC_B");

            // Phase 2: shrink → [B, D] (remove A and C)
            H.ClickButton("MCNext");
            await Harness.Render();
            H.Check("MultiCycle_Phase2_BPresent", H.FindText("MC_B") is not null);
            H.Check("MultiCycle_Phase2_DPresent", H.FindText("MC_D") is not null);
            H.Check("MultiCycle_Phase2_AGone", H.FindText("MC_A") is null);
            H.Check("MultiCycle_Phase2_CGone", H.FindText("MC_C") is null);
            H.Check("MultiCycle_Phase2_BReused", ReferenceEquals(bRef, H.FindText("MC_B")));
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Keyed prefix with type change (replacement during prefix)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tests what happens when a keyed prefix element changes type at the same key.
    /// The prefix phase should break because KeyMatch requires same type.
    /// Old=[Button:k1, Text:k2], New=[Text:k1, Text:k2]. k1 changes type, so
    /// prefix breaks at index 0 and falls through to the middle section.
    /// Exercises the prefix break + middle section fallback path.
    /// </summary>
    internal class KeyedPrefixTypeBreak(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                Element[] items = phase == 0
                    ? [Button("Btn1").WithKey("k1"), TextBlock("Txt2").WithKey("k2")]
                    : [TextBlock("Txt1").WithKey("k1"), TextBlock("Txt2").WithKey("k2")];

                return VStack(
                    Button("TypeBreak", () => setPhase(1)),
                    VStack(items)
                );
            });

            await Harness.Render();
            H.Check("PrefixBreak_Initial", H.FindButton("Btn1") is not null && H.FindText("Txt2") is not null);

            H.ClickButton("TypeBreak");
            await Harness.Render();

            H.Check("PrefixBreak_ButtonGone", H.FindButton("Btn1") is null);
            H.Check("PrefixBreak_TextReplaced", H.FindText("Txt1") is not null);
            H.Check("PrefixBreak_SuffixPreserved", H.FindText("Txt2") is not null);
        }
    }
}
