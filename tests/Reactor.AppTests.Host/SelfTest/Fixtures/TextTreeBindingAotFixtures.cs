using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Isolates the <b>legacy text-node</b> TreeView path — the only place Reactor
/// renders item content through a classic <c>{Binding}</c> rather than setting
/// it imperatively. <c>Reconciler.TreeViewTextItemTemplate</c> resolves
/// <c>Content.Content</c>, hopping <c>TreeViewNode.Content</c> (a native WinRT
/// property) → <c>TreeViewNodeData.Content</c> (a <b>managed</b> record property).
///
/// Nodes here carry no <c>ContentElement</c>, so <c>HasAnyContentElement</c> is
/// false and the tree uses the text template rather than the
/// <c>SharedContentControlTemplate</c> shell — the distinction that makes the
/// existing TTV_* fixtures (which all pass a <c>viewBuilder</c>) unable to cover
/// this path.
///
/// This exists because the managed property hop is resolved by string through
/// CsWinRT's <c>ICustomPropertyProvider</c>, which is reflection-based: under
/// NativeAOT it is trimmed away and the TextBlock silently renders empty, with
/// no build-time warning.
///
/// <para>The assertions use <b>exact</b> text matching, not a substring probe.
/// <c>TreeViewNodeData</c> is a positional record, so its synthesized
/// <c>ToString()</c> embeds <c>Content = BindingRootZulu</c>. If the template
/// selection ever regressed to <c>SharedContentControlTemplate</c>, that bare
/// <c>ContentControl</c> would render the record's <c>ToString()</c> into an
/// implicit TextBlock — which <i>contains</i> the expected substring. A
/// <c>Contains</c> probe would go green there while the text-template binding
/// path was dead; an equality probe cannot.</para>
/// </summary>
internal static class TextTreeBindingAotFixtures
{
    internal class TextTree_NodeTextRendersUnderAot(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            static TreeViewNodeData Expanded(TreeViewNodeData n) => n with { IsExpanded = true };

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                string tag = phase == 0 ? "Zulu" : "Quebec";
                return VStack(
                    Button("Rebind", () => set(1)),
                    TreeView(
                        Expanded(TreeNode($"BindingRoot{tag}",
                            TreeNode($"BindingChild{tag}"))),
                        TreeNode($"BindingSibling{tag}")
                    ).Height(300));
            });

            await Harness.Render();

            H.Check("TXB_RootTextResolved",
                await Harness.WaitFor(() => H.FindText("BindingRootZulu") is not null));
            H.Check("TXB_ChildTextResolved",
                await Harness.WaitFor(() => H.FindText("BindingChildZulu") is not null));
            H.Check("TXB_SiblingTextResolved",
                await Harness.WaitFor(() => H.FindText("BindingSiblingZulu") is not null));

            // Update arm: TreeChildren.Bind(isMount: false) clears RootNodes and
            // rebuilds every TreeViewNode, so the binding has to resolve again on
            // freshly-created nodes — a different code path from mount.
            H.ClickButton("Rebind");
            await Harness.Render();

            H.Check("TXB_RootTextResolvedAfterUpdate",
                await Harness.WaitFor(() => H.FindText("BindingRootQuebec") is not null));
            H.Check("TXB_ChildTextResolvedAfterUpdate",
                await Harness.WaitFor(() => H.FindText("BindingChildQuebec") is not null));
            // Guards node duplication specifically: if the update arm re-added
            // nodes without clearing RootNodes, the two checks above would still
            // find their text, so only the absence of the old text catches it.
            H.Check("TXB_StaleNodeTextCleared",
                await Harness.WaitFor(() => H.FindText("BindingRootZulu") is null));
        }
    }
}
