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
/// no build-time warning. The assertions are deliberately non-vacuous — they
/// search the live visual tree for the bound strings, so they fail if that
/// resolution regresses.
/// </summary>
internal static class TextTreeBindingAotFixtures
{
    internal class TextTree_NodeTextRendersUnderAot(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            static TreeViewNodeData Expanded(TreeViewNodeData n) => n with { IsExpanded = true };

            var host = H.CreateHost();
            host.Mount(_ =>
                TreeView(
                    Expanded(TreeNode("BindingRootZulu",
                        TreeNode("BindingChildYankee"))),
                    TreeNode("BindingSiblingXray")
                ).Height(300));

            await Harness.Render();

            H.Check("TXB_RootTextResolved",
                await Harness.WaitFor(() => H.FindTextContaining("BindingRootZulu") is not null));
            H.Check("TXB_ChildTextResolved",
                await Harness.WaitFor(() => H.FindTextContaining("BindingChildYankee") is not null));
            H.Check("TXB_SiblingTextResolved",
                await Harness.WaitFor(() => H.FindTextContaining("BindingSiblingXray") is not null));
        }
    }
}


