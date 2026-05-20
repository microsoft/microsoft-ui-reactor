// Dock Showcase — exercises every feature in the WinUI.Dock matrix
// surfaced by Reactor's Phase 1 docking wrapper. Drives the human-in-the-
// loop review for spec 045 §4.7 (sit it next to Example.WinUI and run
// down the 8-item script).
//
// Six scenes mirrored from the spec's review script:
//   A — IDE layout: solution / editor / properties / log
//   B — Floating tear-out
//   C — Side pin / auto-hide
//   D — Compact + bottom tabs
//   E — Persistence menu (Save / Load via PersistenceId)
//   F — Programmatic dock (button issues DockTo)
//
// Each scene is its own component so a reviewer can switch between them
// via the side menu without relaunching the app.

using System.Collections.Immutable;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<DockShowcaseRoot>(
    title: "Reactor Docking Showcase",
    width: 1200,
    height: 800,
    configure: host => DockingXamlInterop.Register(host.Reconciler));

// ════════════════════════════════════════════════════════════════════════
//  Root — side menu to switch between scenes
// ════════════════════════════════════════════════════════════════════════

class DockShowcaseRoot : Component
{
    public override Element Render()
    {
        var (scene, setScene) = UseState("ide");

        var menu = VStack(0,
            TextBlock("Reactor Docking Showcase").SemiBold().Margin(8, 8, 8, 12),
            SceneButton("ide",          "Scene A — IDE Layout",        scene, setScene),
            SceneButton("floating",     "Scene B — Floating Tear-Out", scene, setScene),
            SceneButton("sidepin",      "Scene C — Side Pin",          scene, setScene),
            SceneButton("compact",      "Scene D — Compact / Bottom",  scene, setScene),
            SceneButton("persist",      "Scene E — Persistence",       scene, setScene),
            SceneButton("programmatic", "Scene F — Programmatic Dock", scene, setScene)
        ).Width(240).Padding(8);

        Element body = scene switch
        {
            "ide"          => Component<SceneAIde>(),
            "floating"     => Component<SceneBFloating>(),
            "sidepin"      => Component<SceneCSidePin>(),
            "compact"      => Component<SceneDCompact>(),
            "persist"      => Component<SceneEPersistence>(),
            "programmatic" => Component<SceneFProgrammatic>(),
            _              => TextBlock("Unknown scene"),
        };

        return Grid(
            new[] { GridSize.Auto, GridSize.Star(1) },
            new[] { GridSize.Star(1) },
            menu.Grid(column: 0),
            body.Grid(column: 1));
    }

    static Element SceneButton(string id, string label, string current, Action<string> set)
        => Button(label, () => set(id))
            .HAlign(HorizontalAlignment.Stretch)
            .Margin(0, 2, 0, 2);
}

// ════════════════════════════════════════════════════════════════════════
//  Scene A — IDE layout
// ════════════════════════════════════════════════════════════════════════

class SceneAIde : Component
{
    public override Element Render()
    {
        return VStack(8,
            TextBlock("Scene A — IDE layout").FontSize(20).SemiBold(),
            TextBlock(
                "Drag tabs between the groups. Resize the splitters. " +
                "Esc cancels an in-flight drag."
            ).Opacity(0.8),

            new DockManager
            {
                PersistenceId = "dock-showcase:ide",
                Layout = new DockSplit(
                    Orientation.Horizontal,
                    new DockNode[]
                    {
                        new DockableContent(
                            Title: "Solution Explorer",
                            Key: "tool:solution",
                            Content: VStack(2,
                                TextBlock("📁 MyApp.sln").SemiBold(),
                                TextBlock("  📂 src").Margin(8, 0, 0, 0),
                                TextBlock("    📄 main.cs").Margin(16, 0, 0, 0),
                                TextBlock("    📄 App.razor").Margin(16, 0, 0, 0)
                            ).Padding(8),
                            Width: 240),

                        new DockSplit(
                            Orientation.Vertical,
                            new DockNode[]
                            {
                                new DockTabGroup(new[]
                                {
                                    new DockableContent("main.cs",   TextBlock("// main.cs body").Padding(12), Key: "doc:main"),
                                    new DockableContent("App.razor", TextBlock("// app.razor body").Padding(12), Key: "doc:app"),
                                }),
                                new DockableContent(
                                    Title: "Output",
                                    Key: "tool:output",
                                    Content: TextBlock("[12:34:01] Build succeeded.").Padding(8),
                                    Height: 180,
                                    CanPin: true),
                            }),

                        new DockableContent(
                            Title: "Properties",
                            Key: "tool:properties",
                            Content: VStack(4,
                                TextBlock("Name: main.cs"),
                                TextBlock("Size: 4.2 KB"),
                                TextBlock("Modified: 2 min ago")
                            ).Padding(8),
                            Width: 280,
                            CanPin: true),
                    }),
            }.Flex(grow: 1)
        ).Padding(16);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Scene B — Floating tear-out
// ════════════════════════════════════════════════════════════════════════

class SceneBFloating : Component
{
    public override Element Render() => VStack(8,
        TextBlock("Scene B — Floating tear-out").FontSize(20).SemiBold(),
        TextBlock(
            "Drag a tab's title into open space — a floating window appears " +
            "at the pointer with a custom title bar from " +
            "IDockAdapter.GetFloatingWindowTitleBar. Drop back into a tab " +
            "group to re-dock; the floating window auto-closes when its " +
            "last document leaves."
        ).Opacity(0.8),

        new DockManager
        {
            Adapter = new ShowcaseAdapter(),
            Layout = new DockTabGroup(new[]
            {
                new DockableContent("Tab A", TextBlock("body-a").Padding(16), Key: "b:a"),
                new DockableContent("Tab B", TextBlock("body-b").Padding(16), Key: "b:b"),
                new DockableContent("Tab C", TextBlock("body-c").Padding(16), Key: "b:c"),
            }),
        }.Flex(grow: 1)
    ).Padding(16);

    sealed class ShowcaseAdapter : IDockAdapter
    {
        public Element? OnContentCreated(DockableContent content) => null;
        public void OnGroupCreated(DockTabGroupContext g, DockableContent? src) { }
        public Element? GetFloatingWindowTitleBar(DockableContent? draggedSource) =>
            HStack(8,
                TextBlock("📌").Opacity(0.7),
                TextBlock(draggedSource?.Title ?? "Floating Window").SemiBold(),
                TextBlock(" — Reactor Docking Showcase").Opacity(0.5)
            ).Padding(12, 6, 12, 6);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Scene C — Side pin
// ════════════════════════════════════════════════════════════════════════

class SceneCSidePin : Component
{
    public override Element Render()
    {
        var tool = new DockableContent(
            Title: "Pinned Tool",
            Key: "c:tool",
            Content: VStack(4,
                TextBlock("This panel is pinned to the right side.").SemiBold(),
                TextBlock("Click the side icon to expand it as a popup."),
                TextBlock("Drag the right edge of the popup to resize.")
            ).Padding(12),
            CanPin: true);

        return VStack(8,
            TextBlock("Scene C — Side pin / auto-hide").FontSize(20).SemiBold(),
            TextBlock(
                "Pin a tab via its pin button — the tab collapses onto the right edge. " +
                "Click the side icon to expand the popup. Re-pin from the popup " +
                "(thumbtack icon) to restore it to its tab group."
            ).Opacity(0.8),

            new DockManager
            {
                Layout = new DockableContent(
                    Title: "Document",
                    Key: "c:doc",
                    Content: TextBlock("Main document area — try pinning the right-side tool.")
                        .Padding(16)),
                RightSide = new[] { tool },
            }.Flex(grow: 1)
        ).Padding(16);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Scene D — Compact + bottom tabs
// ════════════════════════════════════════════════════════════════════════

class SceneDCompact : Component
{
    public override Element Render() => VStack(8,
        TextBlock("Scene D — Compact + bottom tabs").FontSize(20).SemiBold(),
        TextBlock(
            "TabPosition.Bottom + CompactTabs together. Matches Office's tool-pane shape."
        ).Opacity(0.8),

        new DockManager
        {
            Layout = new DockTabGroup(
                Documents: new[]
                {
                    new DockableContent("Errors",        TextBlock("errors").Padding(12), Key: "d:err"),
                    new DockableContent("Warnings",      TextBlock("warnings").Padding(12), Key: "d:warn"),
                    new DockableContent("Build Output",  TextBlock("build").Padding(12), Key: "d:build"),
                },
                TabPosition: TabPosition.Bottom,
                CompactTabs: true),
        }.Flex(grow: 1).Height(200),

        TextBlock("(Compare with TabPosition.Top below)").Opacity(0.6).Margin(0, 24, 0, 0),

        new DockManager
        {
            Layout = new DockTabGroup(
                Documents: new[]
                {
                    new DockableContent("Errors",        TextBlock("errors 2").Padding(12), Key: "d2:err"),
                    new DockableContent("Warnings",      TextBlock("warnings 2").Padding(12), Key: "d2:warn"),
                    new DockableContent("Build Output",  TextBlock("build 2").Padding(12), Key: "d2:build"),
                },
                TabPosition: TabPosition.Top,
                CompactTabs: false),
        }.Flex(grow: 1).Height(200)
    ).Padding(16);
}

// ════════════════════════════════════════════════════════════════════════
//  Scene E — Persistence
// ════════════════════════════════════════════════════════════════════════

class SceneEPersistence : Component
{
    public override Element Render()
    {
        var (status, setStatus) = UseState("");

        return VStack(8,
            TextBlock("Scene E — Persistence").FontSize(20).SemiBold(),
            TextBlock(
                "DockManager.PersistenceId routes the JSON through " +
                "WindowPersistedScope. Rearrange the panes, quit the app, " +
                "restart — the saved layout restores."
            ).Opacity(0.8),

            HStack(8,
                Button("Note layout-restore status", () =>
                    setStatus("Layout is auto-saved on unmount; reload by relaunching."))
            ),

            TextBlock(status).Opacity(0.7),

            new DockManager
            {
                PersistenceId = "dock-showcase:persistence-demo",
                Layout = new DockSplit(
                    Orientation.Horizontal,
                    new DockNode[]
                    {
                        new DockableContent("Pane 1", TextBlock("p1").Padding(16), Key: "e:1"),
                        new DockableContent("Pane 2", TextBlock("p2").Padding(16), Key: "e:2"),
                        new DockableContent("Pane 3", TextBlock("p3").Padding(16), Key: "e:3"),
                    }),
            }.Flex(grow: 1)
        ).Padding(16);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Scene F — Programmatic dock
// ════════════════════════════════════════════════════════════════════════

class SceneFProgrammatic : Component
{
    public override Element Render()
    {
        var (visibleTools, setVisibleTools) = UseState(ImmutableHashSet<string>.Empty);
        var allTools = new[] { "Properties", "Output", "Console", "Watch" };

        var toolButtons = allTools.Select(t =>
            (Element)Button(
                visibleTools.Contains(t) ? $"Close {t}" : $"Open {t}",
                () => setVisibleTools(visibleTools.Contains(t)
                    ? visibleTools.Remove(t)
                    : visibleTools.Add(t)))).ToArray();

        var dockChildren = new List<DockNode>
        {
            new DockableContent(
                "Editor",
                TextBlock("Main editor — open tools from the toolbar above.").Padding(16),
                Key: "f:editor"),
        };
        foreach (var t in visibleTools.OrderBy(t => t))
        {
            dockChildren.Add(new DockableContent(
                Title: t,
                Key: $"f:tool:{t}",
                Content: TextBlock($"{t} pane body").Padding(16),
                Width: 220,
                CanClose: true));
        }

        return VStack(8,
            TextBlock("Scene F — Programmatic dock").FontSize(20).SemiBold(),
            TextBlock(
                "Click a tool button to open the pane. The pane joins the " +
                "split as a new sibling. Reactor's functional composition " +
                "(state + .Select) replaces upstream's DocumentsSource binding."
            ).Opacity(0.8),

            HStack(8, toolButtons),

            new DockManager
            {
                Layout = new DockSplit(Orientation.Horizontal, dockChildren),
            }.Flex(grow: 1)
        ).Padding(16);
    }
}
