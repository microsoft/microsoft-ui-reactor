using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Coverage for the typed, data-driven <c>TreeView&lt;T&gt;</c>
/// (<see cref="TemplatedTreeViewElement{T}"/>) — the WinUI-aligned replacement
/// for the obsolete per-node <c>ContentElement</c> (issue #447). Each node is
/// rendered by a viewBuilder (the ItemTemplate equivalent) hosted via the
/// <c>{Binding Content}</c> template, so rich elements actually render — unlike
/// the native node-mode default (see <see cref="WinUITreeViewNativeProbe"/>).
/// </summary>
internal static class TemplatedTreeViewFixtures
{
    private sealed record Node(string Key, string Label, Node[]? Kids = null) : IReactorKeyed;

    private static IReadOnlyList<Node>? Children(Node n) => n.Kids;

    // ── 1. Rich per-node content renders, including expanded children ────────
    internal sealed class RendersRichContent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => TreeView<Node>(
                items: new[]
                {
                    new Node("root", "ROOT_BTN", new[] { new Node("c1", "CHILD_BTN") }),
                },
                childrenSelector: Children,
                viewBuilder: n => Button(n.Label)) with
            {
                IsExpanded = _ => true,
            });

            await Harness.Render();

            H.Check("TTV_Render_RootButton", H.FindButton("ROOT_BTN") is not null);
            // Child realizes because the root is expanded.
            H.Check("TTV_Render_ChildButton", H.FindButton("CHILD_BTN") is not null);
        }
    }

    // ── 2. OnItemInvoked / node→T resolution hands the developer's T back ────
    internal sealed class ResolvesItemForEvents(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = new Node("root", "R");
            Node? invokedWith = null;

            var host = H.CreateHost();
            host.Mount(_ => TreeView<Node>(
                items: new[] { root },
                childrenSelector: Children,
                viewBuilder: n => Button(n.Label)) with
            {
                OnItemInvoked = n => invokedWith = n,
            });

            await Harness.Render();

            var tv = H.FindControl<TreeView>(_ => true);
            H.Check("TTV_Evt_Mounted", tv is not null);

            if (tv is not null && tv.RootNodes.Count > 0)
            {
                var node = tv.RootNodes[0];

                // The originating T rides on the node (duplicate-RCW-safe DP),
                // and it is the SAME instance the developer passed in.
                var stored = Reconciler.GetTreeNodeItem(node);
                H.Check("TTV_Evt_NodeCarriesItem", ReferenceEquals(stored, root));

                // The trampoline's resolution path routes that T to the typed
                // callback (TreeViewItemInvokedEventArgs can't be synthesized,
                // so drive the same resolution the trampoline performs).
                if (Reconciler.GetElementTag(tv) is TemplatedTreeViewElementBase el && stored is not null)
                    el.InvokeItemInvoked(stored);

                H.Check("TTV_Evt_CallbackGotSameT", ReferenceEquals(invokedWith, root));
            }
            else
            {
                H.Check("TTV_Evt_NodeCarriesItem", false);
                H.Check("TTV_Evt_CallbackGotSameT", false);
            }
        }
    }

    // ── 3. Keyed update reconciles a node's view in place (state survives) ───
    internal sealed class KeyedUpdateReconciles(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Action<string>? setLabel = null;
            TreeViewNode? firstNodeBefore = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (label, set) = ctx.UseState("LBL_A");
                setLabel = set;
                return TreeView<Node>(
                    items: new[] { new Node("stable", label) },
                    childrenSelector: Children,
                    viewBuilder: n => Button(n.Label));
            });

            await Harness.Render();
            H.Check("TTV_Upd_InitialVisible", H.FindButton("LBL_A") is not null);

            var tv = H.FindControl<TreeView>(_ => true);
            if (tv is not null && tv.RootNodes.Count > 0)
                firstNodeBefore = tv.RootNodes[0];

            // Same key → node reused, view reconciled in place to the new label.
            setLabel?.Invoke("LBL_B");
            await Harness.Render();

            H.Check("TTV_Upd_NewVisible", H.FindButton("LBL_B") is not null);
            H.Check("TTV_Upd_OldGone", H.FindButton("LBL_A") is null);

            var tvAfter = H.FindControl<TreeView>(_ => true);
            bool sameNodeReused = tvAfter is not null && tvAfter.RootNodes.Count > 0
                && ReferenceEquals(tvAfter.RootNodes[0], firstNodeBefore);
            H.Check("TTV_Upd_NodeReusedByKey", sameNodeReused);
        }
    }

    // ── 3b. Reconcile must not clobber user/native expansion ────────────────
    // Regression: forcing node.IsExpanded from the selector on every render
    // yanks a user-collapsed node back open and orphans hosted views (blank
    // gaps). With a stable selector, a re-render must leave expansion alone.
    internal sealed class ExpansionNotClobbered(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Action<int>? bump = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (_, set) = ctx.UseState(0);
                bump = set;
                return TreeView<Node>(
                    items: new[]
                    {
                        new Node("root", "R", new[] { new Node("c1", "C1"), new Node("c2", "C2") }),
                    },
                    childrenSelector: Children,
                    viewBuilder: n => Button(n.Label)) with
                {
                    IsExpanded = _ => true, // stable selector
                };
            });

            await Harness.Render();
            var tv = H.FindControl<TreeView>(_ => true);
            H.Check("TTV_Exp_Mounted", tv is not null && tv.RootNodes.Count > 0);

            if (tv is not null && tv.RootNodes.Count > 0)
            {
                var root = tv.RootNodes[0];
                H.Check("TTV_Exp_InitiallyExpanded", root.IsExpanded);

                // Simulate the user collapsing the node.
                root.IsExpanded = false;

                // Trigger a re-render that does NOT change the data.
                bump?.Invoke(1);
                await Harness.Render();

                // Stable selector ⇒ reconcile must leave the collapse intact.
                H.Check("TTV_Exp_StaysCollapsed", !root.IsExpanded);
            }
            else
            {
                H.Check("TTV_Exp_InitiallyExpanded", false);
                H.Check("TTV_Exp_StaysCollapsed", false);
            }
        }
    }

    // ── 3c. Collapse/expand must re-host every child view each cycle ─────────
    // Regression (#447 follow-up): collapsing a node recycles its child
    // containers and expanding re-realizes them. A shared live view kept a
    // stale visual parent across the cycle, so on alternating expands some
    // rows rendered blank. The ContainerContentChanging host/release must keep
    // every child rendered on every expand.
    internal sealed class CollapseExpandCycle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => TreeView<Node>(
                items: new[]
                {
                    new Node("root", "ROOT", new[]
                    {
                        new Node("a", "ITEM_AA"),
                        new Node("b", "ITEM_BB"),
                        new Node("c", "ITEM_CC"),
                    }),
                },
                childrenSelector: Children,
                viewBuilder: n => Button(n.Label)) with
            {
                IsExpanded = n => n.Key == "root",
            });

            await Harness.Render();
            var tv = H.FindControl<TreeView>(_ => true);
            H.Check("TTV_Cycle_Mounted", tv is not null && tv.RootNodes.Count == 1);

            if (tv is null || tv.RootNodes.Count == 0)
            {
                H.Check("TTV_Cycle_AllChildrenEachExpand", false);
                return;
            }

            var root = tv.RootNodes[0];
            bool allCyclesOk = true;

            for (int cycle = 0; cycle < 3; cycle++)
            {
                root.IsExpanded = false;
                await Harness.Render();
                root.IsExpanded = true;
                await Harness.Render();

                int rendered = H.FindAllControls<Button>(b =>
                    b.Content is string s && (s == "ITEM_AA" || s == "ITEM_BB" || s == "ITEM_CC")).Count;
                if (rendered != 3) allCyclesOk = false;
            }

            H.Check("TTV_Cycle_AllChildrenEachExpand", allCyclesOk);
        }
    }

    // ── 3d. Expanding an initially-collapsed node must stay expanded ─────────
    // Regression: user reports clicking to expand a collapsed node "opens &
    // closes immediately". Expand a collapsed subfolder and assert it stays
    // expanded with its children realized after layout settles.
    internal sealed class ExpandCollapsedNode(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => TreeView<Node>(
                items: new[]
                {
                    new Node("root", "ROOT", new[]
                    {
                        new Node("sub", "SUB", new[] { new Node("x", "XX"), new Node("y", "YY") }),
                    }),
                },
                childrenSelector: Children,
                viewBuilder: n => Button(n.Label)) with
            {
                IsExpanded = n => n.Key == "root", // root open, sub collapsed
            });

            await Harness.Render();
            var tv = H.FindControl<TreeView>(_ => true);
            bool ok = tv is not null && tv.RootNodes.Count == 1 && tv.RootNodes[0].Children.Count == 1;
            H.Check("TTV_ExpandCollapsed_Mounted", ok);
            H.Check("TTV_ExpandCollapsed_SubCollapsedInitially",
                ok && !tv!.RootNodes[0].Children[0].IsExpanded);
            H.Check("TTV_ExpandCollapsed_ChildrenHiddenInitially", H.FindButton("XX") is null);

            if (!ok) { H.Check("TTV_ExpandCollapsed_StaysExpanded", false); return; }

            // Simulate the user expanding the subfolder.
            var sub = tv!.RootNodes[0].Children[0];
            sub.IsExpanded = true;
            await Harness.Render();
            await Harness.Render(); // let WinUI's prepare-container IsExpanded sync settle

            H.Check("TTV_ExpandCollapsed_StaysExpanded", sub.IsExpanded);
            H.Check("TTV_ExpandCollapsed_ChildrenShown",
                H.FindButton("XX") is not null && H.FindButton("YY") is not null);
        }
    }

    // ── 3e. Constrained-height expand must stick (virtualization race) ───────
    // Tiny viewport + many children forces container recycling; reproduces the
    // GUI "expand opens & closes immediately" report.
    internal sealed class ConstrainedExpandSticks(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var kids = new Node[20];
            for (int i = 0; i < kids.Length; i++) kids[i] = new Node($"k{i}", $"KID_{i}");

            var host = H.CreateHost();
            host.Mount(_ => (TreeView<Node>(
                items: new[] { new Node("root", "ROOT", kids) },
                childrenSelector: Children,
                viewBuilder: n => Button(n.Label)) with
            {
                IsExpanded = _ => false, // root starts collapsed
            }).Height(90));

            await Harness.Render();
            var tv = H.FindControl<TreeView>(_ => true);
            H.Check("TTV_Constrained_Mounted", tv is not null && tv.RootNodes.Count == 1);
            if (tv is null || tv.RootNodes.Count == 0)
            {
                H.Check("TTV_Constrained_StaysExpanded", false);
                return;
            }

            var root = tv.RootNodes[0];
            root.IsExpanded = true;
            await Harness.Render();
            await Harness.Render();
            await Harness.Render();

            H.Check("TTV_Constrained_StaysExpanded", root.IsExpanded);
        }
    }

    // ── Legacy TreeView (TreeViewElement): reconcile must not clobber the
    //    user's runtime expansion back to the static data value (the
    //    "expand flashes open then shut" regression — repros on main).
    internal sealed class LegacyExpansionNotClobbered(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Action<int>? bump = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (_, set) = ctx.UseState(0);
                bump = set;
                // Data default IsExpanded = false (collapsed).
                return new TreeViewElement(new[]
                {
                    new TreeViewNodeData("root", new[] { new TreeViewNodeData("child") }),
                });
            });

            await Harness.Render();
            var tv = H.FindControl<TreeView>(_ => true);
            H.Check("LegacyTV_Mounted", tv is not null && tv.RootNodes.Count == 1);
            if (tv is null || tv.RootNodes.Count == 0)
            {
                H.Check("LegacyTV_StaysExpanded", false);
                return;
            }

            var root = tv.RootNodes[0];
            root.IsExpanded = true;   // user expands at runtime
            bump?.Invoke(1);          // unrelated state change → reconcile (data unchanged)
            await Harness.Render();

            // Old code reset IsExpanded to the data value (false) → collapse.
            H.Check("LegacyTV_StaysExpanded", root.IsExpanded);
        }
    }

    // ── 4. Value-type T (exercises the boxing Project path) ──────────────────
    internal sealed class ValueTypeItems(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => TreeView<int>(
                items: new[] { 7, 42 },
                keySelector: i => i.ToString(),
                childrenSelector: _ => null,
                viewBuilder: i => Button($"INT_{i}")));

            await Harness.Render();

            H.Check("TTV_Int_First", H.FindButton("INT_7") is not null);
            H.Check("TTV_Int_Second", H.FindButton("INT_42") is not null);
        }
    }
}
