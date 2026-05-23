using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

// <snippet:register>
ReactorApp.Run<DockingApp>(
    title: "Docking",
    width: 900,
    height: 600,
    devtools: true,
    configure: host => DockingNativeInterop.Register(host.Reconciler));
// </snippet:register>

class DockingApp : Component
{
    public override Element Render() => Component<TwoPaneDemo>();
}

// <snippet:two-pane>
class TwoPaneDemo : Component
{
    public override Element Render() => new DockManager
    {
        Layout = new DockSplit(
            Orientation.Horizontal,
            new DockNode[]
            {
                new DockableContent(
                    Title: "Solution",
                    Key: "tool:solution",
                    Content: VStack(4,
                        TextBlock("📁 MyApp.sln").SemiBold(),
                        TextBlock("    📄 App.cs"),
                        TextBlock("    📄 MainView.cs")
                    ).Padding(12),
                    Width: 240),

                new DockableContent(
                    Title: "App.cs",
                    Key: "doc:app-cs",
                    Content: TextBlock("// editor body").Padding(12)),
            }),
    };
}
// </snippet:two-pane>

// <snippet:tab-group>
class TabGroupDemo : Component
{
    public override Element Render() => new DockManager
    {
        Layout = new DockTabGroup(
            Documents: new[]
            {
                new DockableContent("App.cs",
                    VStack(4,
                        TextBlock("// App.cs"),
                        TextBlock("public sealed class App : Component"),
                        TextBlock("{"),
                        TextBlock("    public override Element Render() =>"),
                        TextBlock("        Text(\"hello, world\");"),
                        TextBlock("}")
                    ).Padding(16),
                    Key: "doc:app", CanClose: true),
                new DockableContent("MainView.cs",
                    TextBlock("// MainView.cs body").Padding(16),
                    Key: "doc:main", CanClose: true),
                new DockableContent("Readme.md",
                    TextBlock("# Readme").Padding(16),
                    Key: "doc:readme", CanClose: true),
            },
            SelectedIndex: 0),
    };
}
// </snippet:tab-group>

// <snippet:side-pin>
class SidePinDemo : Component
{
    public override Element Render() => new DockManager
    {
        Layout = new DockableContent(
            Title: "Document",
            Key: "doc:main",
            Content: VStack(8,
                TextBlock("Document area").SemiBold(),
                TextBlock("Click the pinned tab on the right to expand it."),
                TextBlock("Pin / unpin from inside the popup to toggle.")
            ).Padding(16)),

        RightSide = new[]
        {
            new DockableContent(
                Title: "Properties",
                Key: "tool:properties",
                Content: VStack(4,
                    TextBlock("Name").SemiBold(),
                    TextBlock("Width: 240"),
                    TextBlock("Height: 120")
                ).Padding(12),
                CanPin: true),
        },
    };
}
// </snippet:side-pin>

// <snippet:persistence>
class PersistenceDemo : Component
{
    public override Element Render() => new DockManager
    {
        // Layout JSON is auto-saved to WindowPersistedScope["docking:my-shell"]
        // on unmount and restored on next mount.
        PersistenceId = "my-shell",
        Layout = new DockSplit(
            Orientation.Horizontal,
            new DockNode[]
            {
                new DockableContent("Pane 1",
                    TextBlock("Rearrange me, then relaunch.").Padding(12),
                    Key: "p1", Width: 220),
                new DockableContent("Pane 2",
                    TextBlock("Layout restores from PersistenceId.").Padding(12),
                    Key: "p2"),
            }),
    };
}
// </snippet:persistence>

// <snippet:floating-adapter>
class FloatingChromeAdapter : IDockAdapter
{
    public Element? OnContentCreated(DockableContent content) => null;
    public void OnGroupCreated(DockTabGroupContext group) { }

    // Custom title bar painted on torn-out floating windows.
    public Element? GetFloatingWindowTitleBar(DockableContent? source) =>
        HStack(8,
            TextBlock("📌").Opacity(0.7),
            TextBlock(source?.Title ?? "Floating").SemiBold(),
            TextBlock(" — My App").Opacity(0.5)
        ).Padding(12, 6, 12, 6);
}
// </snippet:floating-adapter>
