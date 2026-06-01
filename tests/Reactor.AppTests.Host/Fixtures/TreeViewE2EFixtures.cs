using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

/// <summary>
/// E2E fixtures for the legacy text-node <c>TreeView</c> expand/collapse path.
///
/// This deliberately mirrors the ReactorGallery "Basic TreeView" card
/// (<c>samples/ReactorGallery/ControlPages/Collections/TreeViewPage.cs</c>):
/// a plain text tree built with <c>TreeView(TreeNode(...))</c>, with
/// <b>no</b> <c>OnExpanding</c>/<c>OnItemInvoked</c> handlers and no component
/// state. That "no handlers, no state" shape is important — clicking a node
/// triggers no Reactor re-render, so it isolates the WinUI-level expand/collapse
/// behavior (see <c>c:\temp\treeview-expand-collapse-investigation.md</c> §5).
///
/// The tree is rendered with the top two levels pre-expanded so that collapse
/// is observable through the UIA tree: a node's children are only realized
/// (and therefore only findable by automation) while that node is expanded.
/// </summary>
internal static class TreeViewE2EFixtures
{
    // The node text values double as the WinAppDriver UIA Names the tests look
    // up. Keep them unique so Name-based lookup is unambiguous.
    internal static Element BasicTextTree(RenderContext ctx)
    {
        static TreeViewNodeData Expanded(TreeViewNodeData n) => n with { IsExpanded = true };

        return VStack(8,
            TextBlock("Basic text TreeView — mirrors ReactorGallery Collections → TreeView (no handlers, no state).")
                .AutomationId("TreeViewCaption"),

            TreeView(
                // Documents / Work pre-expanded → Report.docx + Slides.pptx are
                // initially visible, so a collapse caused by clicking the item
                // body (or a child) is detectable.
                Expanded(TreeNode("Documents",
                    Expanded(TreeNode("Work",
                        TreeNode("Report.docx"),
                        TreeNode("Slides.pptx"))),
                    TreeNode("Personal",
                        TreeNode("Budget.xlsx")))),

                // Pictures stays collapsed → exercises the "click item body to
                // expand, expansion should stick" path.
                TreeNode("Pictures",
                    TreeNode("Vacation",
                        TreeNode("Beach.jpg"),
                        TreeNode("Mountain.jpg")),
                    TreeNode("Family")),

                TreeNode("Music")
            ).Height(300).AutomationId("BasicTreeView")
        );
    }
}
