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
    // Heterogeneous node model: folders, documents and images are distinct C#
    // shapes. TreeView<T> reads hierarchy off the shape (childrenSelector) and
    // picks a per-node template by pattern-matching the shape (viewBuilder) —
    // the WinUI ItemTemplateSelector pattern, expressed in C#.
    abstract record FsEntry(string Name) : IReactorKeyed { public string Key => Name; }
    sealed record FsFolder(string Name, FsEntry[] Items) : FsEntry(Name);
    sealed record FsDoc(string Name, string Size) : FsEntry(Name);
    sealed record FsImage(string Name, string Dimensions) : FsEntry(Name);

    static readonly FsEntry[] SampleTree =
    [
        new FsFolder("Documents",
        [
            new FsFolder("Work",
            [
                new FsDoc("Report.docx", "18 KB"),
                new FsDoc("Slides.pptx", "2.1 MB"),
            ]),
            new FsDoc("Budget.xlsx", "44 KB"),
        ]),
        new FsFolder("Pictures",
        [
            new FsImage("Beach.jpg", "4032 x 3024"),
            new FsImage("Mountain.png", "1920 x 1080"),
        ]),
        new FsDoc("readme.txt", "1 KB"),
    ];

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
                        TreeNode("Pictures", ...),
                        TreeNode("Music"))
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
                                    TreeNode("Level 3A")))))
                    """),

                SampleCard("Heterogeneous nodes & custom templates (TreeView<T>)",
                    (TreeView<FsEntry>(
                        items: SampleTree,
                        childrenSelector: e => e is FsFolder f ? f.Items : null,
                        viewBuilder: e => e switch
                        {
                            FsFolder fo => HStack(8,
                                TextBlock("\U0001F4C1"),
                                TextBlock(fo.Name).SemiBold(),
                                Caption($"{fo.Items.Length} items")),
                            FsImage im => HStack(8,
                                TextBlock("\U0001F5BC"),
                                TextBlock(im.Name),
                                Caption(im.Dimensions)),
                            FsDoc d => HStack(8,
                                TextBlock("\U0001F4C4"),
                                TextBlock(d.Name),
                                Caption(d.Size)),
                            _ => TextBlock(e.Name),
                        }) with { IsExpanded = e => e is FsFolder })
                        .Height(320),
                    """
                    TreeView<FsEntry>(
                        items: tree,
                        childrenSelector: e => e is FsFolder f ? f.Items : null,
                        viewBuilder: e => e switch
                        {
                            FsFolder fo => HStack(Text("📁"), Text(fo.Name).SemiBold(), Caption($"{fo.Items.Length} items")),
                            FsImage  im => HStack(Text("🖼"), Text(im.Name), Caption(im.Dimensions)),
                            FsDoc    d  => HStack(Text("📄"), Text(d.Name), Caption(d.Size)),
                        }) with { IsExpanded = e => e is FsFolder }
                    """)
            ).Margin(36, 24, 36, 36)
        );
    }
}
