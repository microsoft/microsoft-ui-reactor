using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor;

class TreeViewPage : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(16,
                PageHeader("TreeView", "A hierarchical list with expanding and collapsing nodes."),

                SampleCard("Basic TreeView",
                    TreeView(
                        TreeNode("Documents",
                            TreeNode("Work",
                                TreeNode("Report.docx"),
                                TreeNode("Slides.pptx")),
                            TreeNode("Personal",
                                TreeNode("Budget.xlsx"))),
                        TreeNode("Pictures",
                            TreeNode("Vacation",
                                TreeNode("Beach.jpg"),
                                TreeNode("Mountain.jpg")),
                            TreeNode("Family")),
                        TreeNode("Music")
                    ).Height(300),
                    """
                    TreeView(
                        TreeNode("Documents",
                            TreeNode("Work",
                                TreeNode("Report.docx"),
                                TreeNode("Slides.pptx"))),
                        TreeNode("Pictures", ...)
                    )
                    """),

                SampleCard("Deeply Nested TreeView",
                    TreeView(
                        TreeNode("Root",
                            TreeNode("Level 1A",
                                TreeNode("Level 2A",
                                    TreeNode("Level 3A"),
                                    TreeNode("Level 3B")),
                                TreeNode("Level 2B")),
                            TreeNode("Level 1B",
                                TreeNode("Level 2C")))
                    ).Height(250),
                    """
                    TreeView(
                        TreeNode("Root",
                            TreeNode("Level 1A",
                                TreeNode("Level 2A",
                                    TreeNode("Level 3A"))))
                    )
                    """)
            ).Margin(36, 24, 36, 36)
        );
    }
}
