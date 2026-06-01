using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using static Microsoft.UI.Reactor.Factories;
using WinXC = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Fixtures for the typed, data-driven <c>TreeView&lt;T&gt;</c> — the
/// hierarchical peer of <c>ListView&lt;T&gt;</c> that closes issue #447
/// ("TreeViewNodeData.ContentElement renders blank"). The legacy node-mode
/// TreeView stringifies its content and cannot host a pre-built UIElement;
/// the typed peer renders each node from a <c>data → Element</c> viewBuilder
/// (the WinUI ItemTemplate equivalent), hosted via a ContentControl template.
/// </summary>
internal static class TemplatedTreeViewFixtures
{
    // A discriminated file-system model — folders nest, files are leaves.
    private abstract record FsNode(string Id);
    private record FsFolder(string Id, string Name, FsNode[] Children) : FsNode(Id);
    private record FsFile(string Id, string Name, string Ext) : FsNode(Id);

    private static FsNode[] SampleTree() =>
    [
        new FsFolder("docs", "Documents",
        [
            new FsFile("readme", "readme", "md"),
            new FsFile("notes", "notes", "txt"),
        ]),
        new FsFolder("pics", "Pictures",
        [
            new FsFile("logo", "logo", "png"),
        ]),
    ];

    private static IReadOnlyList<FsNode>? ChildrenOf(FsNode n) =>
        n is FsFolder f ? f.Children : null;

    // viewBuilder = ItemTemplateSelector-as-a-switch. Folders and files get
    // visibly distinct visuals (the "[D]" / "[F]" prefix is the tell).
    private static Element BuildNodeView(FsNode n) => n switch
    {
        FsFolder f => HStack(TextBlock("[D]"), TextBlock(f.Name)),
        FsFile file => HStack(TextBlock("[F]"), TextBlock($"{file.Name}.{file.Ext}")),
        _ => TextBlock("?"),
    };

    // Per-node views host into their containers when the TreeView realizes them,
    // which lands on a dispatcher cycle after mount — and the runtime decides how
    // many pump cycles that takes (the NativeAOT host consistently needs one more
    // than JIT). Pump render passes until the condition holds rather than
    // asserting after a single Render(); returns false if it never does.
    // Delegates to the shared Harness.WaitFor so the polling logic lives in one place.
    private static Task<bool> WaitFor(Func<bool> condition, int maxPasses = 15)
        => Harness.WaitFor(condition, maxPasses);

    // ── 1. Rich content actually renders (the core #447 win) ──────────────
    internal class RendersRichContent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ =>
                VStack(
                    TextBlock("File Explorer"),
                    TreeView(SampleTree(),
                        keySelector: n => n.Id,
                        childrenSelector: ChildrenOf,
                        viewBuilder: BuildNodeView)
                        // Expand folders so their child views realize.
                        with { IsExpanded = n => n is FsFolder }
                ).Height(400)
            );

            await Harness.Render();

            H.Check("TTV_RendersRichContent_TreeViewCreated",
                H.FindControl<WinXC.TreeView>(_ => true) is not null);

            // The node's view is a live HStack of TextBlocks — not a stringified
            // blank row. Finding the folder name proves the content hosted.
            H.Check("TTV_RendersRichContent_RootNodeVisible",
                await WaitFor(() => H.FindTextContaining("Documents") is not null));

            // The "[D]" prefix only exists inside the rich per-node template —
            // a stringified node could never produce it.
            H.Check("TTV_RendersRichContent_RichTemplateHosted",
                await WaitFor(() => H.FindText("[D]") is not null));

            // Expanded child leaf renders too.
            H.Check("TTV_RendersRichContent_ChildLeafVisible",
                await WaitFor(() => H.FindTextContaining("readme.md") is not null));
        }
    }

    // ── 2. Heterogeneous nodes → per-shape templates ──────────────────────
    internal class HeterogeneousTemplates(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ =>
                TreeView(SampleTree(),
                    keySelector: n => n.Id,
                    childrenSelector: ChildrenOf,
                    viewBuilder: BuildNodeView)
                    with { IsExpanded = n => n is FsFolder }
            );

            await Harness.Render();

            // Both the folder template ("[D]") and the file template ("[F]")
            // are realized from the single switch-based viewBuilder.
            H.Check("TTV_Heterogeneous_FolderTemplate", await WaitFor(() => H.FindText("[D]") is not null));
            H.Check("TTV_Heterogeneous_FileTemplate", await WaitFor(() => H.FindText("[F]") is not null));
        }
    }

    // ── 3. Keyed update reconcile — in-place rename of a matched node ──────
    // Structure is stable across the flip (same keys, same child count), so the
    // matched node's view reconciles in place and stays hosted. (Structural
    // add/remove that forces TreeView to re-realize containers is the separate
    // §6 hosting tradeoff — see KeyedUpdateAddChild + the handoff doc.)
    internal class KeyedUpdateReconcile(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            WinXC.TreeView? firstInstance = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                FsNode[] tree =
                [
                    new FsFolder("root", "Root",
                    [
                        new FsFile("a", phase == 0 ? "alpha" : "alpha-renamed", "txt"),
                        new FsFile("b", "beta", "txt"),
                    ]),
                ];
                return VStack(
                    Button("Mutate", () => set(1)),
                    TreeView(tree,
                        keySelector: n => n.Id,
                        childrenSelector: ChildrenOf,
                        viewBuilder: BuildNodeView)
                        with { IsExpanded = _ => true }
                );
            });

            await Harness.Render();
            firstInstance = H.FindControl<WinXC.TreeView>(_ => true);
            H.Check("TTV_KeyedUpdate_InitialChild",
                await WaitFor(() => H.FindTextContaining("alpha.txt") is not null));

            H.ClickButton("Mutate");
            await Harness.Render();

            // With per-container hosting the reused node's view reconciles in
            // place and stays rendered in the visual tree.
            H.Check("TTV_KeyedUpdate_RenamedChildReconciled",
                await WaitFor(() => H.FindTextContaining("alpha-renamed.txt") is not null));
            // The rename has rendered, so the old text is gone ("alpha-renamed.txt"
            // does not contain the substring "alpha.txt").
            H.Check("TTV_KeyedUpdate_OldTextGone",
                H.FindTextContaining("alpha.txt") is null);
            // The untouched sibling keeps rendering through the reconcile.
            H.Check("TTV_KeyedUpdate_SiblingPreserved",
                await WaitFor(() => H.FindTextContaining("beta.txt") is not null));

            // The reconcile updated the existing control in place rather than
            // remounting a fresh TreeView.
            var secondInstance = H.FindControl<WinXC.TreeView>(_ => true);
            H.Check("TTV_KeyedUpdate_ControlIdentityPreserved",
                firstInstance is not null && ReferenceEquals(firstInstance, secondInstance));
        }
    }

    // ── 3b. Structural add — a freshly-keyed node appears and renders ─────
    // Verifies the diff inserts a new node (built fresh, so it hosts cleanly)
    // and the live TreeViewNode hierarchy reflects the new child count. The
    // reused siblings' re-hosting under container recycle is the open §6
    // tradeoff, so this asserts the data/structure side, not their pixels.
    internal class KeyedUpdateAddChild(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                FsNode[] children = phase == 0
                    ? [new FsFile("a", "alpha", "txt")]
                    : [new FsFile("a", "alpha", "txt"), new FsFile("b", "beta", "txt")];
                FsNode[] tree = [new FsFolder("root", "Root", children)];
                return VStack(
                    Button("Add", () => set(1)),
                    TreeView(tree,
                        keySelector: n => n.Id,
                        childrenSelector: ChildrenOf,
                        viewBuilder: BuildNodeView)
                        with { IsExpanded = _ => true }
                );
            });

            await Harness.Render();
            var tv = H.FindControl<WinXC.TreeView>(_ => true);
            H.Check("TTV_AddChild_InitialOneChild",
                tv is not null && tv.RootNodes.Count == 1 && tv.RootNodes[0].Children.Count == 1);

            H.ClickButton("Add");
            await Harness.Render();

            H.Check("TTV_AddChild_NodeInserted",
                tv!.RootNodes[0].Children.Count == 2);
            H.Check("TTV_AddChild_NewNodeRenders",
                await WaitFor(() => H.FindTextContaining("beta.txt") is not null));
        }
    }

    // ── 4. Event trampolines hand back the developer's own T (erasure) ────
    internal class EventErasureResolvesT(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var tree = SampleTree();
            FsNode? invoked = null;
            FsNode? expanding = null;

            var el = TreeView(tree,
                keySelector: n => n.Id,
                childrenSelector: ChildrenOf,
                viewBuilder: BuildNodeView)
                with
                {
                    OnItemInvoked = n => invoked = n,
                    OnExpanding = n => expanding = n,
                };

            // The reconciler dispatches through the object-erased base; verify
            // the cast back to T round-trips the original reference.
            var roots = el.GetRoots();
            H.Check("TTV_Erasure_RootCount", roots.Count == 2);
            H.Check("TTV_Erasure_KeyResolves", el.GetKey(roots[0]) == "docs");
            H.Check("TTV_Erasure_ChildrenResolve", el.GetChildren(roots[0])?.Count == 2);
            H.Check("TTV_Erasure_LeafHasNoChildren", el.GetChildren(roots[1]) is { } pics && el.GetChildren(pics[0]) is null);

            el.InvokeItemInvoked(roots[0]);
            H.Check("TTV_Erasure_ItemInvokedResolvesT", ReferenceEquals(invoked, tree[0]));

            el.InvokeExpanding(roots[1]);
            H.Check("TTV_Erasure_ExpandingResolvesT", ReferenceEquals(expanding, tree[1]));

            return Task.CompletedTask;
        }
    }

    // ── 5. Value-type T is boxed/projected correctly ──────────────────────
    internal class ValueTypeT(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int[] items = [1, 2, 3];
            int invoked = -1;

            var el = TreeView(items,
                keySelector: i => $"n{i}",
                childrenSelector: i => i == 1 ? new[] { 10, 11 } : null,
                viewBuilder: i => TextBlock($"#{i}"))
                with { OnItemInvoked = i => invoked = i, IsExpanded = _ => true };

            var roots = el.GetRoots();
            H.Check("TTV_ValueType_RootCount", roots.Count == 3);
            H.Check("TTV_ValueType_KeyResolves", el.GetKey(roots[0]) == "n1");
            H.Check("TTV_ValueType_ChildrenResolve", el.GetChildren(roots[0])?.Count == 2);

            el.InvokeItemInvoked(roots[2]);
            H.Check("TTV_ValueType_InvokeUnboxesT", invoked == 3);

            // And it actually mounts + renders.
            var host = H.CreateHost();
            host.Mount(_ => el);
            await Harness.Render();
            H.Check("TTV_ValueType_Renders", await WaitFor(() => H.FindTextContaining("#1") is not null));
            H.Check("TTV_ValueType_ChildRenders", await WaitFor(() => H.FindTextContaining("#10") is not null));
        }
    }

    // ── 6. IsExpanded selector drives the node's initial expansion ────────
    internal class IsExpandedApplied(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ =>
                TreeView(SampleTree(),
                    keySelector: n => n.Id,
                    childrenSelector: ChildrenOf,
                    viewBuilder: BuildNodeView)
                    with { IsExpanded = n => n.Id == "docs" }
            );

            await Harness.Render();

            var tv = H.FindControl<WinXC.TreeView>(_ => true);
            H.Check("TTV_IsExpanded_TreeFound", tv is not null);
            // First root ("docs") is expanded; the second ("pics") is not.
            H.Check("TTV_IsExpanded_SelectedNodeExpanded",
                tv!.RootNodes.Count == 2 && tv.RootNodes[0].IsExpanded);
            H.Check("TTV_IsExpanded_OtherNodeCollapsed",
                !tv.RootNodes[1].IsExpanded);
        }
    }

    // ── 6b. Expand/collapse cycles keep every child rendered ──────────────
    // Regression for the "every other expand/collapse blanks the first child
    // row(s)" bug: per-container hosting must re-mount a fresh view into
    // whichever pooled container WinUI realizes each node into, so no row goes
    // blank after a collapse→expand cycle recycles containers.
    internal class ExpandCollapseCycle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ =>
                TreeView(SampleTree(),
                    keySelector: n => n.Id,
                    childrenSelector: ChildrenOf,
                    viewBuilder: BuildNodeView)
                    // Start collapsed so we drive the expansions ourselves.
                    with { IsExpanded = _ => false }
            );

            await Harness.Render();
            var tv = H.FindControl<WinXC.TreeView>(_ => true);
            H.Check("TTV_Cycle_TreeFound", tv is not null);

            var docs = tv!.RootNodes[0];   // "Documents" → readme.md, notes.txt
            bool allCyclesOk = true;

            // Several collapse→expand cycles; after each expand both children
            // must be present (the bug blanked the first child every 2nd cycle).
            // WaitFor tolerates the realization landing a pump-cycle later, but a
            // genuinely blank row never appears within the budget → fails.
            for (int cycle = 0; cycle < 4; cycle++)
            {
                docs.IsExpanded = true;
                bool firstChild = await WaitFor(() => H.FindTextContaining("readme.md") is not null);
                bool secondChild = await WaitFor(() => H.FindTextContaining("notes.txt") is not null);
                if (!firstChild || !secondChild) allCyclesOk = false;

                docs.IsExpanded = false;
                await Harness.Render();
            }

            H.Check("TTV_Cycle_NoBlankRowsAcrossCycles", allCyclesOk);

            // Leave it expanded and confirm a final realization renders.
            docs.IsExpanded = true;
            H.Check("TTV_Cycle_FinalExpandRenders",
                await WaitFor(() => H.FindTextContaining("readme.md") is not null)
                && await WaitFor(() => H.FindTextContaining("notes.txt") is not null));
        }
    }

    // ── 7. Unmount tears the tree down without leaking / throwing ─────────
    internal class UnmountTearsDown(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, set) = ctx.UseState(true);
                return VStack(
                    Button("Hide", () => set(false)),
                    show
                        ? TreeView(SampleTree(),
                            keySelector: n => n.Id,
                            childrenSelector: ChildrenOf,
                            viewBuilder: BuildNodeView)
                            with { IsExpanded = _ => true }
                        : TextBlock("gone")
                );
            });

            await Harness.Render();
            H.Check("TTV_Unmount_TreeMounted", H.FindControl<WinXC.TreeView>(_ => true) is not null);

            H.ClickButton("Hide");
            await Harness.Render();

            H.Check("TTV_Unmount_TreeRemoved", H.FindControl<WinXC.TreeView>(_ => true) is null);
            H.Check("TTV_Unmount_ReplacementShown", H.FindText("gone") is not null);
        }
    }

    // ── 8. Unmount runs the hosted view's component cleanup ────────────────
    // The decorator handler owns the typed TreeView container walk, so hosted
    // node views (in ContentControl.Content) run component UseEffect cleanup on
    // both recursive and pooled unmount paths.
    private static int s_cleanupCount;

    private sealed class CleanupLeaf : Component
    {
        public override Element Render()
        {
            UseEffect(() => () => global::System.Threading.Interlocked.Increment(ref s_cleanupCount));
            return TextBlock("leaf-rich");
        }
    }

    internal class UnmountRunsChildCleanup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            global::System.Threading.Interlocked.Exchange(ref s_cleanupCount, 0);

            // Single root whose view is a component with UseEffect cleanup, so it
            // realizes immediately (no expansion needed) and its teardown is
            // observable.
            string[] items = ["root"];
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, set) = ctx.UseState(true);
                return VStack(
                    Button("Hide", () => set(false)),
                    show
                        ? TreeView(items,
                            keySelector: s => s,
                            childrenSelector: _ => null,
                            viewBuilder: _ => Component<CleanupLeaf>())
                        : TextBlock("gone")
                );
            });

            await Harness.Render();
            H.Check("TTV_UnmountCleanup_ViewHosted",
                await WaitFor(() => H.FindText("leaf-rich") is not null));
            H.Check("TTV_UnmountCleanup_NotYetCleaned",
                global::System.Threading.Volatile.Read(ref s_cleanupCount) == 0);

            H.ClickButton("Hide");
            await Harness.Render();

            H.Check("TTV_UnmountCleanup_TreeRemoved", H.FindControl<WinXC.TreeView>(_ => true) is null);
            H.Check("TTV_UnmountCleanup_CleanupRan",
                await WaitFor(() => global::System.Threading.Volatile.Read(ref s_cleanupCount) == 1));
        }
    }

    internal class UnmountAndPoolRunsChildCleanup(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            global::System.Threading.Interlocked.Exchange(ref s_cleanupCount, 0);

            string[] items = ["root"];
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, set) = ctx.UseState(true);
                return VStack(
                    Button("Remove", () => set(false)),
                    show
                        ? TreeView(items,
                            keySelector: s => s,
                            childrenSelector: _ => null,
                            viewBuilder: _ => Component<CleanupLeaf>())
                        : null
                );
            });

            await Harness.Render();
            H.Check("TTV_PooledUnmountCleanup_ViewHosted",
                await WaitFor(() => H.FindText("leaf-rich") is not null));
            H.Check("TTV_PooledUnmountCleanup_NotYetCleaned",
                global::System.Threading.Volatile.Read(ref s_cleanupCount) == 0);

            H.ClickButton("Remove");
            await Harness.Render();

            H.Check("TTV_PooledUnmountCleanup_TreeRemoved", H.FindControl<WinXC.TreeView>(_ => true) is null);
            H.Check("TTV_PooledUnmountCleanup_CleanupRan",
                await WaitFor(() => global::System.Threading.Volatile.Read(ref s_cleanupCount) == 1));
        }
    }

    // ── 9. Structural root removal — a realized root subtree drops ─────────
    // The keyed diff drops unmatched root keys from the live TreeViewNode
    // collection; survivors keep rendering, the removed node's text is gone.
    internal class StructuralRootRemoval(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                FsNode[] tree = phase == 0
                    ?
                    [
                        new FsFolder("docs", "Documents", [new FsFile("readme", "readme", "md")]),
                        new FsFolder("pics", "Pictures", [new FsFile("logo", "logo", "png")]),
                        new FsFolder("vids", "Videos", [new FsFile("clip", "clip", "mp4")]),
                    ]
                    :
                    [
                        new FsFolder("docs", "Documents", [new FsFile("readme", "readme", "md")]),
                        new FsFolder("vids", "Videos", [new FsFile("clip", "clip", "mp4")]),
                    ];
                return VStack(
                    Button("Remove", () => set(1)),
                    TreeView(tree,
                        keySelector: n => n.Id,
                        childrenSelector: ChildrenOf,
                        viewBuilder: BuildNodeView)
                        with { IsExpanded = _ => true }
                );
            });

            await Harness.Render();
            var tv = H.FindControl<WinXC.TreeView>(_ => true);
            H.Check("TTV_RootRemoval_InitialThreeRoots",
                tv is not null && tv.RootNodes.Count == 3);
            if (tv is null) return;
            H.Check("TTV_RootRemoval_MiddleVisible",
                await WaitFor(() => H.FindTextContaining("Pictures") is not null));

            H.ClickButton("Remove");
            await Harness.Render();

            H.Check("TTV_RootRemoval_TwoRootsRemain", tv.RootNodes.Count == 2);
            // The removed subtree's content is gone (both the folder and its leaf).
            H.Check("TTV_RootRemoval_RemovedTextGone",
                await WaitFor(() => H.FindTextContaining("Pictures") is null
                    && H.FindTextContaining("logo.png") is null));
            // Survivors keep rendering.
            H.Check("TTV_RootRemoval_SurvivorsPreserved",
                await WaitFor(() => H.FindTextContaining("Documents") is not null
                    && H.FindTextContaining("Videos") is not null));
        }
    }

    // ── 10. Keyed root reorder preserves TreeViewNode identity ────────────
    // The keyed diff reuses matched nodes and only reorders the live collection,
    // so the exact same TreeViewNode instances survive a reorder (just swapped).
    internal class KeyedRootReorder(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                FsNode a = new FsFolder("docs", "Documents", [new FsFile("readme", "readme", "md")]);
                FsNode b = new FsFolder("pics", "Pictures", [new FsFile("logo", "logo", "png")]);
                FsNode[] tree = phase == 0 ? [a, b] : [b, a];
                return VStack(
                    Button("Reorder", () => set(1)),
                    TreeView(tree,
                        keySelector: n => n.Id,
                        childrenSelector: ChildrenOf,
                        viewBuilder: BuildNodeView)
                );
            });

            await Harness.Render();
            var tv = H.FindControl<WinXC.TreeView>(_ => true);
            H.Check("TTV_Reorder_TwoRoots", tv is not null && tv.RootNodes.Count == 2);
            if (tv is null) return;

            var firstNode = tv.RootNodes[0];   // docs
            var secondNode = tv.RootNodes[1];   // pics

            H.ClickButton("Reorder");
            await Harness.Render();

            H.Check("TTV_Reorder_CountStable", tv.RootNodes.Count == 2);
            // Same instances, swapped order — the keyed diff reused both nodes.
            H.Check("TTV_Reorder_NodeIdentityPreserved",
                ReferenceEquals(tv.RootNodes[0], secondNode)
                && ReferenceEquals(tv.RootNodes[1], firstNode));
        }
    }

    // ── 11. Control props applied on mount and updated on a state flip ─────
    internal class ControlPropsMountAndUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("Flip", () => set(1)),
                    TreeView(SampleTree(),
                        keySelector: n => n.Id,
                        childrenSelector: ChildrenOf,
                        viewBuilder: BuildNodeView)
                        with
                        {
                            SelectionMode = phase == 0
                                ? WinXC.TreeViewSelectionMode.Single
                                : WinXC.TreeViewSelectionMode.Multiple,
                            CanReorderItems = phase != 0,
                            CanDragItems = phase != 0,
                            AllowDrop = phase != 0,
                        }
                );
            });

            await Harness.Render();
            var tv = H.FindControl<WinXC.TreeView>(_ => true);
            H.Check("TTV_Props_MountSelectionSingle",
                tv is not null && tv.SelectionMode == WinXC.TreeViewSelectionMode.Single);
            if (tv is null) return;
            H.Check("TTV_Props_MountFlagsFalse",
                !tv.CanReorderItems && !tv.CanDragItems && !tv.AllowDrop);

            H.ClickButton("Flip");
            await Harness.Render();

            // The update body wrote the new values onto the same control.
            H.Check("TTV_Props_UpdateSelectionMultiple",
                tv.SelectionMode == WinXC.TreeViewSelectionMode.Multiple);
            H.Check("TTV_Props_UpdateFlagsTrue",
                tv.CanReorderItems && tv.CanDragItems && tv.AllowDrop);
        }
    }

    // ── 12. Empty tree → populate via state ───────────────────────────────
    internal class EmptyThenPopulate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                FsNode[] tree = phase == 0
                    ? []
                    : [new FsFolder("docs", "Documents", [new FsFile("readme", "readme", "md")])];
                return VStack(
                    Button("Populate", () => set(1)),
                    TreeView(tree,
                        keySelector: n => n.Id,
                        childrenSelector: ChildrenOf,
                        viewBuilder: BuildNodeView)
                        with { IsExpanded = _ => true }
                );
            });

            await Harness.Render();
            var tv = H.FindControl<WinXC.TreeView>(_ => true);
            H.Check("TTV_Empty_NoRootsInitially", tv is not null && tv.RootNodes.Count == 0);
            if (tv is null) return;

            H.ClickButton("Populate");
            await Harness.Render();

            H.Check("TTV_Empty_RootAddedOnPopulate", tv.RootNodes.Count == 1);
            H.Check("TTV_Empty_PopulatedRootRenders",
                await WaitFor(() => H.FindTextContaining("Documents") is not null));
            H.Check("TTV_Empty_PopulatedChildRenders",
                await WaitFor(() => H.FindTextContaining("readme.md") is not null));
        }
    }

    // ── 13. Deep 3-level nesting renders at every depth ───────────────────
    internal class DeepNestingRenders(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            FsNode[] deep =
            [
                new FsFolder("L1", "Level1",
                [
                    new FsFolder("L2", "Level2",
                    [
                        new FsFile("L3", "Level3", "txt"),
                    ]),
                ]),
            ];

            var host = H.CreateHost();
            host.Mount(_ =>
                VStack(
                    TreeView(deep,
                        keySelector: n => n.Id,
                        childrenSelector: ChildrenOf,
                        viewBuilder: BuildNodeView)
                        with { IsExpanded = _ => true }
                ).Height(400)
            );

            await Harness.Render();
            H.Check("TTV_Deep_Level1", await WaitFor(() => H.FindTextContaining("Level1") is not null));
            H.Check("TTV_Deep_Level2", await WaitFor(() => H.FindTextContaining("Level2") is not null));
            H.Check("TTV_Deep_Level3Leaf", await WaitFor(() => H.FindTextContaining("Level3.txt") is not null));
        }
    }

    // ── 14. Two independent trees stay isolated across an update ───────────
    internal class TwoIndependentTrees(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                FsNode[] left =
                [
                    new FsFolder("la", phase == 0 ? "LeftAlpha" : "LeftAlpha-renamed",
                        [new FsFile("lf", "leftfile", "txt")]),
                ];
                FsNode[] right = [new FsFolder("ra", "RightAlpha", [new FsFile("rf", "rightfile", "txt")])];
                return VStack(
                    Button("MutateLeft", () => set(1)),
                    (TreeView(left, keySelector: n => n.Id, childrenSelector: ChildrenOf, viewBuilder: BuildNodeView)
                        with { IsExpanded = _ => true }),
                    (TreeView(right, keySelector: n => n.Id, childrenSelector: ChildrenOf, viewBuilder: BuildNodeView)
                        with { IsExpanded = _ => true })
                ).Height(600);
            });

            await Harness.Render();
            H.Check("TTV_TwoTrees_BothMounted",
                H.FindControl<WinXC.TreeView>(_ => true) is not null
                && await WaitFor(() => H.FindTextContaining("LeftAlpha") is not null
                    && H.FindTextContaining("RightAlpha") is not null));

            H.ClickButton("MutateLeft");
            await Harness.Render();

            // Left tree updated…
            H.Check("TTV_TwoTrees_LeftUpdated",
                await WaitFor(() => H.FindTextContaining("LeftAlpha-renamed") is not null));
            // …while the right tree is untouched.
            H.Check("TTV_TwoTrees_RightUntouched",
                await WaitFor(() => H.FindTextContaining("RightAlpha") is not null
                    && H.FindTextContaining("rightfile.txt") is not null));
        }
    }
}
