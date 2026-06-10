using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 057 §9.2 topology matrix for scalar and list-valued ElementRef reference edges.
/// </summary>
internal static class RefNodeTopologyFixtures
{
    private static RefNodeElement Keyed(RefNodeElement e, string key) => e with { Key = key };

    private static RefNode? Node(Harness h, string id) => h.FindControl<RefNode>(n => n.NodeId == id);

    private static bool Link(Harness h, string from, Func<RefNode, FrameworkElement?> slot, string to)
    {
        var source = Node(h, from);
        var target = Node(h, to);
        return source is not null && target is not null && ReferenceEquals(slot(source), target);
    }

    private static bool NullSlot(Harness h, string from, Func<RefNode, FrameworkElement?> slot)
    {
        var source = Node(h, from);
        return source is not null && slot(source) is null;
    }

    private static bool Missing(Harness h, string id) => Node(h, id) is null;

    private static bool Related(Harness h, string from, params string[] targetIds)
    {
        var source = Node(h, from);
        if (source is null || source.Related.Count != targetIds.Length) return false;

        for (int i = 0; i < targetIds.Length; i++)
        {
            var target = Node(h, targetIds[i]);
            if (target is null || !ReferenceEquals(source.Related[i], target))
                return false;
        }

        return true;
    }

    private static async Task StableRerender(
        Harness h,
        string button,
        string checkName,
        Func<bool> predicate)
    {
        h.ClickButton(button);
        await Harness.Render();
        h.Check(checkName, await Harness.WaitFor(predicate));
    }

    internal sealed class Row01_LinearChain(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<RefNode>? bRef = null, cRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                bRef = ctx.UseElementRef<RefNode>();
                cRef = ctx.UseElementRef<RefNode>();
                var (showC, setShowC) = ctx.UseState(true);
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                    TextBlock($"r01 {tick}"),
                    RefNodeFactory.Of("R01_A", right: bRef),
                    RefNodeFactory.Of("R01_B", right: cRef).Ref(bRef),
                    showC ? RefNodeFactory.Of("R01_C").Ref(cRef) : Empty(),
                    Button("R01_Rerender", () => setTick(tick + 1)),
                    Button("R01_ToggleC", () => setShowC(!showC)));
            });

            await Harness.Render();
            H.Check("RefNode_Row01_LinearChain_Commit", await Harness.WaitFor(() =>
                Link(H, "R01_A", n => n.Right, "R01_B") &&
                Link(H, "R01_B", n => n.Right, "R01_C") &&
                bRef?.Inner.CurrentChangedSubscriberCount == 1 &&
                cRef?.Inner.CurrentChangedSubscriberCount == 1));

            await StableRerender(H, "R01_Rerender", "RefNode_Row01_LinearChain_StableRerender", () =>
                Link(H, "R01_A", n => n.Right, "R01_B") && Link(H, "R01_B", n => n.Right, "R01_C"));

            H.ClickButton("R01_ToggleC");
            await Harness.Render();
            H.Check("RefNode_Row01_LinearChain_TeardownClears", await Harness.WaitFor(() =>
                Missing(H, "R01_C") && NullSlot(H, "R01_B", n => n.Right) &&
                bRef?.Inner.CurrentChangedSubscriberCount == 1 &&
                cRef?.Inner.CurrentChangedSubscriberCount == 1));
        }
    }

    internal sealed class Row02_FanOut(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<RefNode>? sRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                sRef = ctx.UseElementRef<RefNode>();
                var (showR3, setShowR3) = ctx.UseState(true);
                var (showS, setShowS) = ctx.UseState(true);
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                    TextBlock($"r02 {tick}"),
                    Keyed(RefNodeFactory.Of("R02_R1", right: sRef), "R02_R1"),
                    Keyed(RefNodeFactory.Of("R02_R2", right: sRef), "R02_R2"),
                    showR3 ? Keyed(RefNodeFactory.Of("R02_R3", right: sRef), "R02_R3") : Empty(),
                    showS ? Keyed(RefNodeFactory.Of("R02_S").Ref(sRef), "R02_S") : Empty(),
                    Button("R02_Rerender", () => setTick(tick + 1)),
                    Button("R02_ToggleR3", () => setShowR3(!showR3)),
                    Button("R02_ToggleS", () => setShowS(!showS)));
            });

            await Harness.Render();
            H.Check("RefNode_Row02_FanOut_Commit", await Harness.WaitFor(() =>
                Link(H, "R02_R1", n => n.Right, "R02_S") &&
                Link(H, "R02_R2", n => n.Right, "R02_S") &&
                Link(H, "R02_R3", n => n.Right, "R02_S") &&
                sRef?.Inner.CurrentChangedSubscriberCount == 3));

            await StableRerender(H, "R02_Rerender", "RefNode_Row02_FanOut_StableRerender", () =>
                Link(H, "R02_R1", n => n.Right, "R02_S") &&
                Link(H, "R02_R2", n => n.Right, "R02_S") &&
                Link(H, "R02_R3", n => n.Right, "R02_S") &&
                sRef?.Inner.CurrentChangedSubscriberCount == 3);

            H.ClickButton("R02_ToggleR3");
            await Harness.Render();
            H.Check("RefNode_Row02_FanOut_ReferrerUnmountDropsCount", await Harness.WaitFor(() =>
                Missing(H, "R02_R3") &&
                Link(H, "R02_R1", n => n.Right, "R02_S") &&
                Link(H, "R02_R2", n => n.Right, "R02_S") &&
                sRef?.Inner.CurrentChangedSubscriberCount == 2));

            H.ClickButton("R02_ToggleS");
            await Harness.Render();
            H.Check("RefNode_Row02_FanOut_SourceUnmountClearsAll", await Harness.WaitFor(() =>
                Missing(H, "R02_S") &&
                NullSlot(H, "R02_R1", n => n.Right) &&
                NullSlot(H, "R02_R2", n => n.Right) &&
                sRef?.Inner.CurrentChangedSubscriberCount == 2));
        }
    }

    internal sealed class Row03_FanIn(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<RefNode>? s1Ref = null, s2Ref = null, s3Ref = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                s1Ref = ctx.UseElementRef<RefNode>();
                s2Ref = ctx.UseElementRef<RefNode>();
                s3Ref = ctx.UseElementRef<RefNode>();
                var (showR, setShowR) = ctx.UseState(true);
                var (showS1, setShowS1) = ctx.UseState(true);
                var (showS2, setShowS2) = ctx.UseState(true);
                var (showS3, setShowS3) = ctx.UseState(true);
                var (tick, setTick) = ctx.UseState(0);
                var related = new[] { s1Ref, s2Ref, s3Ref };
                return VStack(
                    TextBlock($"r03 {tick}"),
                    showR ? Keyed(RefNodeFactory.Of("R03_R", related: related), "R03_R") : Empty(),
                    showS1 ? Keyed(RefNodeFactory.Of("R03_S1").Ref(s1Ref), "R03_S1") : Empty(),
                    showS2 ? Keyed(RefNodeFactory.Of("R03_S2").Ref(s2Ref), "R03_S2") : Empty(),
                    showS3 ? Keyed(RefNodeFactory.Of("R03_S3").Ref(s3Ref), "R03_S3") : Empty(),
                    Button("R03_Rerender", () => setTick(tick + 1)),
                    Button("R03_ToggleS1", () => setShowS1(!showS1)),
                    Button("R03_ToggleS2", () => setShowS2(!showS2)),
                    Button("R03_ToggleS3", () => setShowS3(!showS3)),
                    Button("R03_ToggleR", () => setShowR(!showR)));
            });

            await Harness.Render();
            H.Check("RefNode_Row03_FanIn_CommitOrder", await Harness.WaitFor(() =>
                Related(H, "R03_R", "R03_S1", "R03_S2", "R03_S3") &&
                s1Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                s2Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                s3Ref?.Inner.CurrentChangedSubscriberCount == 1));

            await StableRerender(H, "R03_Rerender", "RefNode_Row03_FanIn_StableRerender", () =>
                Related(H, "R03_R", "R03_S1", "R03_S2", "R03_S3") &&
                s1Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                s2Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                s3Ref?.Inner.CurrentChangedSubscriberCount == 1);

            H.ClickButton("R03_ToggleS2");
            await Harness.Render();
            H.Check("RefNode_Row03_FanIn_SourceUnmountDropsOneAndPreservesOrder", await Harness.WaitFor(() =>
                Missing(H, "R03_S2") &&
                Related(H, "R03_R", "R03_S1", "R03_S3") &&
                s1Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                s2Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                s3Ref?.Inner.CurrentChangedSubscriberCount == 1));

            H.ClickButton("R03_ToggleS1");
            H.ClickButton("R03_ToggleS3");
            await Harness.Render();
            H.Check("RefNode_Row03_FanIn_AllSourcesUnmountClearList", await Harness.WaitFor(() =>
            {
                var r = Node(H, "R03_R");
                return r is not null &&
                    r.Related.Count == 0 &&
                    s1Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                    s2Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                    s3Ref?.Inner.CurrentChangedSubscriberCount == 1;
            }));

            H.ClickButton("R03_ToggleR");
            await Harness.Render();
            H.Check("RefNode_Row03_FanIn_ReferrerUnmountDropsAllSubscriptions", await Harness.WaitFor(() =>
                Missing(H, "R03_R") &&
                s1Ref?.Inner.CurrentChangedSubscriberCount == 0 &&
                s2Ref?.Inner.CurrentChangedSubscriberCount == 0 &&
                s3Ref?.Inner.CurrentChangedSubscriberCount == 0));
        }
    }

    internal sealed class Row04_Bidirectional(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<RefNode>? aRef = null, bRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                aRef = ctx.UseElementRef<RefNode>();
                bRef = ctx.UseElementRef<RefNode>();
                var (showB, setShowB) = ctx.UseState(true);
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                    TextBlock($"r04 {tick}"),
                    RefNodeFactory.Of("R04_A", right: bRef).Ref(aRef),
                    showB ? RefNodeFactory.Of("R04_B", left: aRef).Ref(bRef) : Empty(),
                    Button("R04_Rerender", () => setTick(tick + 1)),
                    Button("R04_ToggleB", () => setShowB(!showB)));
            });

            await Harness.Render();
            H.Check("RefNode_Row04_Bidirectional_Commit", await Harness.WaitFor(() =>
                Link(H, "R04_A", n => n.Right, "R04_B") &&
                Link(H, "R04_B", n => n.Left, "R04_A") &&
                aRef?.Inner.CurrentChangedSubscriberCount == 1 &&
                bRef?.Inner.CurrentChangedSubscriberCount == 1));

            await StableRerender(H, "R04_Rerender", "RefNode_Row04_Bidirectional_StableRerender", () =>
                Link(H, "R04_A", n => n.Right, "R04_B") && Link(H, "R04_B", n => n.Left, "R04_A"));

            H.ClickButton("R04_ToggleB");
            await Harness.Render();
            H.Check("RefNode_Row04_Bidirectional_TeardownClears", await Harness.WaitFor(() =>
                Missing(H, "R04_B") &&
                NullSlot(H, "R04_A", n => n.Right) &&
                aRef?.Inner.CurrentChangedSubscriberCount == 0 &&
                bRef?.Inner.CurrentChangedSubscriberCount == 1));
        }
    }

    internal sealed class Row05_ThreeCycle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<RefNode>? aRef = null, bRef = null, cRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                aRef = ctx.UseElementRef<RefNode>();
                bRef = ctx.UseElementRef<RefNode>();
                cRef = ctx.UseElementRef<RefNode>();
                var (showC, setShowC) = ctx.UseState(true);
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                    TextBlock($"r05 {tick}"),
                    RefNodeFactory.Of("R05_A", right: bRef).Ref(aRef),
                    RefNodeFactory.Of("R05_B", right: cRef).Ref(bRef),
                    showC ? RefNodeFactory.Of("R05_C", right: aRef).Ref(cRef) : Empty(),
                    Button("R05_Rerender", () => setTick(tick + 1)),
                    Button("R05_ToggleC", () => setShowC(!showC)));
            });

            await Harness.Render();
            H.Check("RefNode_Row05_ThreeCycle_Commit", await Harness.WaitFor(() =>
                Link(H, "R05_A", n => n.Right, "R05_B") &&
                Link(H, "R05_B", n => n.Right, "R05_C") &&
                Link(H, "R05_C", n => n.Right, "R05_A") &&
                aRef?.Inner.CurrentChangedSubscriberCount == 1 &&
                bRef?.Inner.CurrentChangedSubscriberCount == 1 &&
                cRef?.Inner.CurrentChangedSubscriberCount == 1));

            await StableRerender(H, "R05_Rerender", "RefNode_Row05_ThreeCycle_StableRerender", () =>
                Link(H, "R05_A", n => n.Right, "R05_B") &&
                Link(H, "R05_B", n => n.Right, "R05_C") &&
                Link(H, "R05_C", n => n.Right, "R05_A"));

            H.ClickButton("R05_ToggleC");
            await Harness.Render();
            H.Check("RefNode_Row05_ThreeCycle_TeardownClears", await Harness.WaitFor(() =>
                Missing(H, "R05_C") &&
                NullSlot(H, "R05_B", n => n.Right) &&
                aRef?.Inner.CurrentChangedSubscriberCount == 0 &&
                bRef?.Inner.CurrentChangedSubscriberCount == 1 &&
                cRef?.Inner.CurrentChangedSubscriberCount == 1));
        }
    }

    internal sealed class Row06_SelfReference(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<RefNode>? aRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                aRef = ctx.UseElementRef<RefNode>();
                var (showA, setShowA) = ctx.UseState(true);
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                    TextBlock($"r06 {tick}"),
                    showA ? RefNodeFactory.Of("R06_A", peer: aRef).Ref(aRef) : Empty(),
                    Button("R06_Rerender", () => setTick(tick + 1)),
                    Button("R06_ToggleA", () => setShowA(!showA)));
            });

            await Harness.Render();
            H.Check("RefNode_Row06_SelfReference_Commit", await Harness.WaitFor(() =>
            {
                var a = Node(H, "R06_A");
                return a is not null && ReferenceEquals(a.Peer, a) && aRef?.Inner.CurrentChangedSubscriberCount == 1;
            }));

            await StableRerender(H, "R06_Rerender", "RefNode_Row06_SelfReference_StableRerender", () =>
            {
                var a = Node(H, "R06_A");
                return a is not null && ReferenceEquals(a.Peer, a) && aRef?.Inner.CurrentChangedSubscriberCount == 1;
            });

            H.ClickButton("R06_ToggleA");
            await Harness.Render();
            H.Check("RefNode_Row06_SelfReference_TeardownNoLeak", await Harness.WaitFor(() =>
                Missing(H, "R06_A") && aRef?.Current is null && aRef?.Inner.CurrentChangedSubscriberCount == 0));
        }
    }

    internal sealed class Row07_ParentChildBothWays(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<RefNode>? parentRef = null, childRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                parentRef = ctx.UseElementRef<RefNode>();
                childRef = ctx.UseElementRef<RefNode>();
                var (showChild, setShowChild) = ctx.UseState(true);
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                    TextBlock($"r07 {tick}"),
                    RefNodeFactory.Of("R07_Parent", down: childRef).Ref(parentRef),
                    showChild ? Border(RefNodeFactory.Of("R07_Child", parent: parentRef).Ref(childRef)) : Empty(),
                    Button("R07_Rerender", () => setTick(tick + 1)),
                    Button("R07_ToggleChild", () => setShowChild(!showChild)));
            });

            await Harness.Render();
            H.Check("RefNode_Row07_ParentChildBothWays_Commit", await Harness.WaitFor(() =>
                Link(H, "R07_Parent", n => n.Down, "R07_Child") &&
                Link(H, "R07_Child", n => n.Parent, "R07_Parent") &&
                parentRef?.Inner.CurrentChangedSubscriberCount == 1 &&
                childRef?.Inner.CurrentChangedSubscriberCount == 1));

            await StableRerender(H, "R07_Rerender", "RefNode_Row07_ParentChildBothWays_StableRerender", () =>
                Link(H, "R07_Parent", n => n.Down, "R07_Child") &&
                Link(H, "R07_Child", n => n.Parent, "R07_Parent"));

            H.ClickButton("R07_ToggleChild");
            await Harness.Render();
            H.Check("RefNode_Row07_ParentChildBothWays_TeardownClears", await Harness.WaitFor(() =>
                Missing(H, "R07_Child") &&
                NullSlot(H, "R07_Parent", n => n.Down) &&
                parentRef?.Inner.CurrentChangedSubscriberCount == 0 &&
                childRef?.Inner.CurrentChangedSubscriberCount == 1));
        }
    }

    internal sealed class Row08_Diamond(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<RefNode>? bRef = null, cRef = null, dRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                bRef = ctx.UseElementRef<RefNode>();
                cRef = ctx.UseElementRef<RefNode>();
                dRef = ctx.UseElementRef<RefNode>();
                var (showD, setShowD) = ctx.UseState(true);
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                    TextBlock($"r08 {tick}"),
                    RefNodeFactory.Of("R08_A", right: bRef, down: cRef),
                    RefNodeFactory.Of("R08_B", right: dRef).Ref(bRef),
                    RefNodeFactory.Of("R08_C", right: dRef).Ref(cRef),
                    showD ? RefNodeFactory.Of("R08_D").Ref(dRef) : Empty(),
                    Button("R08_Rerender", () => setTick(tick + 1)),
                    Button("R08_ToggleD", () => setShowD(!showD)));
            });

            await Harness.Render();
            H.Check("RefNode_Row08_Diamond_Commit", await Harness.WaitFor(() =>
                Link(H, "R08_A", n => n.Right, "R08_B") &&
                Link(H, "R08_A", n => n.Down, "R08_C") &&
                Link(H, "R08_B", n => n.Right, "R08_D") &&
                Link(H, "R08_C", n => n.Right, "R08_D") &&
                bRef?.Inner.CurrentChangedSubscriberCount == 1 &&
                cRef?.Inner.CurrentChangedSubscriberCount == 1 &&
                dRef?.Inner.CurrentChangedSubscriberCount == 2));

            await StableRerender(H, "R08_Rerender", "RefNode_Row08_Diamond_StableRerender", () =>
                Link(H, "R08_B", n => n.Right, "R08_D") && Link(H, "R08_C", n => n.Right, "R08_D"));

            H.ClickButton("R08_ToggleD");
            await Harness.Render();
            H.Check("RefNode_Row08_Diamond_TeardownClearsSharedTarget", await Harness.WaitFor(() =>
                Missing(H, "R08_D") &&
                NullSlot(H, "R08_B", n => n.Right) &&
                NullSlot(H, "R08_C", n => n.Right) &&
                dRef?.Inner.CurrentChangedSubscriberCount == 2));
        }
    }

    internal sealed class Row09_LateMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<RefNode>? bRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                bRef = ctx.UseElementRef<RefNode>();
                // Start the target hidden so the reference edge is declared but
                // unresolved — this is the actual "late mount" scenario.
                var (showB, setShowB) = ctx.UseState(false);
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                    TextBlock($"r09 {tick}"),
                    RefNodeFactory.Of("R09_A", right: bRef),
                    showB ? RefNodeFactory.Of("R09_B").Ref(bRef) : Empty(),
                    Button("R09_Rerender", () => setTick(tick + 1)),
                    Button("R09_ToggleB", () => setShowB(!showB)));
            });

            await Harness.Render();
            // Target not yet mounted: A subscribes to the cell, but the slot is null.
            H.Check("RefNode_Row09_LateMount_InitiallyUnresolved", await Harness.WaitFor(() =>
                Missing(H, "R09_B") && NullSlot(H, "R09_A", n => n.Right) &&
                bRef?.Inner.CurrentChangedSubscriberCount == 1));

            // Mount the target late — the reference edge fills reactively, no rewire.
            H.ClickButton("R09_ToggleB");
            await Harness.Render();
            H.Check("RefNode_Row09_LateMount_ResolvesOnLateMount", await Harness.WaitFor(() =>
                Link(H, "R09_A", n => n.Right, "R09_B") && bRef?.Inner.CurrentChangedSubscriberCount == 1));

            await StableRerender(H, "R09_Rerender", "RefNode_Row09_LateMount_StableRerender", () =>
                Link(H, "R09_A", n => n.Right, "R09_B"));

            H.ClickButton("R09_ToggleB");
            await Harness.Render();
            H.Check("RefNode_Row09_LateMount_TeardownClears", await Harness.WaitFor(() =>
                Missing(H, "R09_B") && NullSlot(H, "R09_A", n => n.Right) &&
                bRef?.Inner.CurrentChangedSubscriberCount == 1));
        }
    }

    internal sealed class Row10_Conditional(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<RefNode>? bRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                bRef = ctx.UseElementRef<RefNode>();
                var (showB, setShowB) = ctx.UseState(true);
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                    TextBlock($"r10 {tick}"),
                    RefNodeFactory.Of("R10_A", right: bRef),
                    showB ? RefNodeFactory.Of("R10_B").Ref(bRef) : Empty(),
                    Button("R10_Rerender", () => setTick(tick + 1)),
                    Button("R10_ToggleB", () => setShowB(!showB)));
            });

            await Harness.Render();
            H.Check("RefNode_Row10_Conditional_Commit", await Harness.WaitFor(() =>
            {
                return Link(H, "R10_A", n => n.Right, "R10_B") && bRef?.Inner.CurrentChangedSubscriberCount == 1;
            }));

            await StableRerender(H, "R10_Rerender", "RefNode_Row10_Conditional_StableRerender", () =>
                Link(H, "R10_A", n => n.Right, "R10_B"));

            H.ClickButton("R10_ToggleB");
            await Harness.Render();
            H.Check("RefNode_Row10_Conditional_ToggleOutClears", await Harness.WaitFor(() =>
                Missing(H, "R10_B") && NullSlot(H, "R10_A", n => n.Right)));

            H.ClickButton("R10_ToggleB");
            await Harness.Render();
            H.Check("RefNode_Row10_Conditional_ToggleInRelinks", await Harness.WaitFor(() =>
            {
                var b = Node(H, "R10_B");
                var a = Node(H, "R10_A");
                return a is not null && b is not null &&
                    ReferenceEquals(a.Right, b) &&
                    bRef?.Inner.CurrentChangedSubscriberCount == 1;
            }));
        }
    }

    internal sealed class Row11_Reorder(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<RefNode>? bRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                bRef = ctx.UseElementRef<RefNode>();
                var (reversed, setReversed) = ctx.UseState(false);
                var (showB, setShowB) = ctx.UseState(true);
                var (tick, setTick) = ctx.UseState(0);
                var b = showB ? Keyed(RefNodeFactory.Of("R11_B").Ref(bRef), "R11_B") : Empty();
                var c = Keyed(RefNodeFactory.Of("R11_C"), "R11_C");
                return VStack(
                    TextBlock($"r11 {tick}"),
                    RefNodeFactory.Of("R11_A", right: bRef),
                    reversed ? c : b,
                    reversed ? b : c,
                    Button("R11_Rerender", () => setTick(tick + 1)),
                    Button("R11_Shuffle", () => setReversed(!reversed)),
                    Button("R11_ToggleB", () => setShowB(!showB)));
            });

            await Harness.Render();
            H.Check("RefNode_Row11_Reorder_Commit", await Harness.WaitFor(() =>
                Link(H, "R11_A", n => n.Right, "R11_B") && bRef?.Inner.CurrentChangedSubscriberCount == 1));

            H.ClickButton("R11_Shuffle");
            await Harness.Render();
            H.Check("RefNode_Row11_Reorder_AfterShuffle", await Harness.WaitFor(() =>
                Link(H, "R11_A", n => n.Right, "R11_B") && bRef?.Inner.CurrentChangedSubscriberCount == 1));

            await StableRerender(H, "R11_Rerender", "RefNode_Row11_Reorder_StableRerender", () =>
                Link(H, "R11_A", n => n.Right, "R11_B") && bRef?.Inner.CurrentChangedSubscriberCount == 1);

            H.ClickButton("R11_ToggleB");
            await Harness.Render();
            H.Check("RefNode_Row11_Reorder_TeardownClears", await Harness.WaitFor(() =>
                Missing(H, "R11_B") && NullSlot(H, "R11_A", n => n.Right) &&
                bRef?.Inner.CurrentChangedSubscriberCount == 1));
        }
    }

    internal sealed class Row12_PoolRecycle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<RefNode>? sourceRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                sourceRef = ctx.UseElementRef<RefNode>();
                var (cycle, setCycle) = ctx.UseState(0);
                var showExtraReferrers = cycle % 2 == 0;
                return VStack(
                    TextBlock($"r12 {cycle}"),
                    RefNodeFactory.Of("R12_Root", right: sourceRef),
                    showExtraReferrers ? Keyed(RefNodeFactory.Of("R12_R1", right: sourceRef), "R12_R1") : Empty(),
                    showExtraReferrers ? Keyed(RefNodeFactory.Of("R12_R2", right: sourceRef), "R12_R2") : Empty(),
                    RefNodeFactory.Of("R12_Source").Ref(sourceRef),
                    Button("R12_Cycle", () => setCycle(cycle + 1)));
            });

            await Harness.Render();
            H.Check("RefNode_Row12_PoolRecycle_Commit", await Harness.WaitFor(() =>
                Link(H, "R12_Root", n => n.Right, "R12_Source") &&
                Link(H, "R12_R1", n => n.Right, "R12_Source") &&
                Link(H, "R12_R2", n => n.Right, "R12_Source") &&
                sourceRef?.Inner.CurrentChangedSubscriberCount == 3));

            H.ClickButton("R12_Cycle");
            await Harness.Render();
            H.Check("RefNode_Row12_PoolRecycle_CycleRemovesReferrers", await Harness.WaitFor(() =>
                Link(H, "R12_Root", n => n.Right, "R12_Source") &&
                Missing(H, "R12_R1") &&
                Missing(H, "R12_R2") &&
                sourceRef?.Inner.CurrentChangedSubscriberCount == 1));

            H.ClickButton("R12_Cycle");
            await Harness.Render();
            H.Check("RefNode_Row12_PoolRecycle_CycleRestoresWithoutDoubleSubscribe", await Harness.WaitFor(() =>
                Link(H, "R12_Root", n => n.Right, "R12_Source") &&
                Link(H, "R12_R1", n => n.Right, "R12_Source") &&
                Link(H, "R12_R2", n => n.Right, "R12_Source") &&
                sourceRef?.Inner.CurrentChangedSubscriberCount == 3));
        }
    }

    internal sealed class Row13_ReferrerUnmount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<RefNode>? bRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                bRef = ctx.UseElementRef<RefNode>();
                var (showA, setShowA) = ctx.UseState(true);
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                    TextBlock($"r13 {tick}"),
                    showA ? Keyed(RefNodeFactory.Of("R13_A", right: bRef), "R13_A") : Empty(),
                    Keyed(RefNodeFactory.Of("R13_B").Ref(bRef), "R13_B"),
                    Button("R13_Rerender", () => setTick(tick + 1)),
                    Button("R13_ToggleA", () => setShowA(!showA)));
            });

            await Harness.Render();
            H.Check("RefNode_Row13_ReferrerUnmount_Commit", await Harness.WaitFor(() =>
                Link(H, "R13_A", n => n.Right, "R13_B") && bRef?.Inner.CurrentChangedSubscriberCount == 1));

            await StableRerender(H, "R13_Rerender", "RefNode_Row13_ReferrerUnmount_StableRerender", () =>
                Link(H, "R13_A", n => n.Right, "R13_B") && bRef?.Inner.CurrentChangedSubscriberCount == 1);

            H.ClickButton("R13_ToggleA");
            await Harness.Render();
            H.Check("RefNode_Row13_ReferrerUnmount_NoLeakAndSourceAlive", await Harness.WaitFor(() =>
                Missing(H, "R13_A") &&
                Node(H, "R13_B") is not null &&
                ReferenceEquals(bRef?.Current, Node(H, "R13_B")) &&
                bRef?.Inner.CurrentChangedSubscriberCount == 0));
        }
    }

    internal sealed class Row14_SourceSwap(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<RefNode>? bRef = null, cRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                bRef = ctx.UseElementRef<RefNode>();
                cRef = ctx.UseElementRef<RefNode>();
                var (useC, setUseC) = ctx.UseState(false);
                var (showC, setShowC) = ctx.UseState(true);
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                    TextBlock($"r14 {tick}"),
                    RefNodeFactory.Of("R14_A", right: useC ? cRef : bRef),
                    RefNodeFactory.Of("R14_B").Ref(bRef),
                    showC ? RefNodeFactory.Of("R14_C").Ref(cRef) : Empty(),
                    Button("R14_Rerender", () => setTick(tick + 1)),
                    Button("R14_Swap", () => setUseC(!useC)),
                    Button("R14_ToggleC", () => setShowC(!showC)));
            });

            await Harness.Render();
            H.Check("RefNode_Row14_SourceSwap_Initial", await Harness.WaitFor(() =>
                Link(H, "R14_A", n => n.Right, "R14_B") &&
                bRef?.Inner.CurrentChangedSubscriberCount == 1 &&
                cRef?.Inner.CurrentChangedSubscriberCount == 0));

            H.ClickButton("R14_Swap");
            await Harness.Render();
            H.Check("RefNode_Row14_SourceSwap_Swapped", await Harness.WaitFor(() =>
                Link(H, "R14_A", n => n.Right, "R14_C") &&
                bRef?.Inner.CurrentChangedSubscriberCount == 0 &&
                cRef?.Inner.CurrentChangedSubscriberCount == 1));

            await StableRerender(H, "R14_Rerender", "RefNode_Row14_SourceSwap_StableRerender", () =>
                Link(H, "R14_A", n => n.Right, "R14_C") &&
                bRef?.Inner.CurrentChangedSubscriberCount == 0 &&
                cRef?.Inner.CurrentChangedSubscriberCount == 1);

            H.ClickButton("R14_ToggleC");
            await Harness.Render();
            H.Check("RefNode_Row14_SourceSwap_TeardownClearsCurrentSource", await Harness.WaitFor(() =>
                Missing(H, "R14_C") &&
                NullSlot(H, "R14_A", n => n.Right) &&
                bRef?.Inner.CurrentChangedSubscriberCount == 0 &&
                cRef?.Inner.CurrentChangedSubscriberCount == 1));
        }
    }
}

internal static class RefNodeSurfaceParityFixtures
{
    internal sealed class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await AssertRefNodeParity();
            await AssertTeachingTipParity();
        }

        private async Task AssertRefNodeParity()
        {
            ElementRef<RefNode>? targetRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                targetRef = ctx.UseElementRef<RefNode>();
                var (showTarget, setShowTarget) = ctx.UseState(true);
                return VStack(
                    new RefNodeElement("SP_Record") { Right = targetRef },
                    RefNodeFactory.Of("SP_Fluent").Right(targetRef),
                    RefNodeFactory.Of("SP_Factory", right: targetRef),
                    showTarget ? RefNodeFactory.Of("SP_Target").Ref(targetRef) : Empty(),
                    Button("SP_RefNode_ToggleTarget", () => setShowTarget(!showTarget)));
            });

            await Harness.Render();
            H.Check("RefNode_SurfaceParity_Slot_Commit", await Harness.WaitFor(() =>
                Link(H, "SP_Record", n => n.Right, "SP_Target") &&
                Link(H, "SP_Fluent", n => n.Right, "SP_Target") &&
                Link(H, "SP_Factory", n => n.Right, "SP_Target") &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 3));

            H.ClickButton("SP_RefNode_ToggleTarget");
            await Harness.Render();
            H.Check("RefNode_SurfaceParity_Slot_Clear", await Harness.WaitFor(() =>
                NullSlot(H, "SP_Record", n => n.Right) &&
                NullSlot(H, "SP_Fluent", n => n.Right) &&
                NullSlot(H, "SP_Factory", n => n.Right) &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 3));
        }

        private async Task AssertTeachingTipParity()
        {
            ElementRef<FrameworkElement>? targetRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                targetRef = ctx.UseElementRef<FrameworkElement>();
                var (showTarget, setShowTarget) = ctx.UseState(true);
                return VStack(
                    showTarget ? Button("SP_TipTarget", () => { }).Ref(targetRef) : Empty(),
                    new TeachingTipElement("SP Tip Record") { Target = targetRef }.Set(t => t.Name = "SP_Tip_Record"),
                    TeachingTip("SP Tip Fluent").Target(targetRef).Set(t => t.Name = "SP_Tip_Fluent"),
                    TeachingTip("SP Tip Factory", target: targetRef).Set(t => t.Name = "SP_Tip_Factory"),
                    Button("SP_TeachingTip_ToggleTarget", () => setShowTarget(!showTarget)));
            });

            await Harness.Render();
            H.Check("RefNode_SurfaceParity_TeachingTip_Commit", await Harness.WaitFor(() =>
            {
                var target = H.FindControl<WinUI.Button>(b => b.Content is string s && s == "SP_TipTarget");
                return target is not null &&
                    H.FindControl<WinUI.TeachingTip>(t => t.Name == "SP_Tip_Record" && ReferenceEquals(t.Target, target)) is not null &&
                    H.FindControl<WinUI.TeachingTip>(t => t.Name == "SP_Tip_Fluent" && ReferenceEquals(t.Target, target)) is not null &&
                    H.FindControl<WinUI.TeachingTip>(t => t.Name == "SP_Tip_Factory" && ReferenceEquals(t.Target, target)) is not null &&
                    targetRef?.Inner.CurrentChangedSubscriberCount == 3;
            }));

            H.ClickButton("SP_TeachingTip_ToggleTarget");
            await Harness.Render();
            H.Check("RefNode_SurfaceParity_TeachingTip_Clear", await Harness.WaitFor(() =>
                H.FindControl<WinUI.Button>(b => b.Content is string s && s == "SP_TipTarget") is null &&
                H.FindControl<WinUI.TeachingTip>(t => t.Name == "SP_Tip_Record" && t.Target is null) is not null &&
                H.FindControl<WinUI.TeachingTip>(t => t.Name == "SP_Tip_Fluent" && t.Target is null) is not null &&
                H.FindControl<WinUI.TeachingTip>(t => t.Name == "SP_Tip_Factory" && t.Target is null) is not null &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 3));
        }

        private static bool Link(Harness h, string from, Func<RefNode, FrameworkElement?> slot, string to)
        {
            var source = h.FindControl<RefNode>(n => n.NodeId == from);
            var target = h.FindControl<RefNode>(n => n.NodeId == to);
            return source is not null && target is not null && ReferenceEquals(slot(source), target);
        }

        private static bool NullSlot(Harness h, string from, Func<RefNode, FrameworkElement?> slot)
        {
            var source = h.FindControl<RefNode>(n => n.NodeId == from);
            return source is not null && slot(source) is null;
        }
    }
}
