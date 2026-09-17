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
                new ToolWindow
                {
                    Title = "Solution Explorer",
                    Key = "tool:solution",
                    Width = 260,
                    Content = VStack(6,
                        TextBlock("MyApp.sln").SemiBold(),
                        TextBlock("  src"),
                        TextBlock("    App.cs"),
                        TextBlock("    MainView.cs"),
                        TextBlock("  tests"),
                        TextBlock("    MainViewTests.cs")
                    ).Padding(12)
                },

                new DockTabGroup(
                    Documents: new DockableContent[]
                    {
                        new Document
                        {
                            Title = "App.cs",
                            Key = "doc:app-cs",
                            Content = VStack(4,
                                TextBlock("// App.cs"),
                                TextBlock("ReactorApp.Run<MainView>(title: \"MyApp\");"),
                                TextBlock(""),
                                TextBlock("class MainView : Component"),
                                TextBlock("{"),
                                TextBlock("    public override Element Render() => TextBlock(\"Hello\");"),
                                TextBlock("}")
                            ).Padding(16)
                        }
                    },
                    SelectedIndex: 0),
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
                new Document
                {
                    Title = "App.cs",
                    Key = "doc:app",
                    Content = VStack(4,
                        TextBlock("// App.cs"),
                        TextBlock("public sealed class App : Component"),
                        TextBlock("{"),
                        TextBlock("    public override Element Render() =>"),
                        TextBlock("        TextBlock(\"hello, world\");"),
                        TextBlock("}")
                    ).Padding(16)
                },
                new Document
                {
                    Title = "MainView.cs",
                    Key = "doc:main",
                    Content = TextBlock("// MainView.cs body").Padding(16)
                },
                new Document
                {
                    Title = "Readme.md",
                    Key = "doc:readme",
                    Content = TextBlock("# Readme").Padding(16)
                },
            },
            SelectedIndex: 0),
    };
}
// </snippet:tab-group>

class DocumentWellDemo : Component
{
    public override Element Render() => TextBlock("Document well demo");

    private static DockNode CreateLayout()
    {
        // <snippet:document-well>
        var galleryItemsToolWindow = new ToolWindow { Title = "Gallery", Key = "tool:gallery" };
        var configurationToolWindow = new ToolWindow { Title = "Configuration", Key = "tool:configuration" };

        return new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(
                new[] { galleryItemsToolWindow },
                Width: 260,
                Role: DockGroupRole.ToolWindowStrip),
            new DockTabGroup(
                Array.Empty<DockableContent>(),
                Role: DockGroupRole.DocumentArea),
            new DockTabGroup(
                new[] { configurationToolWindow },
                Width: 320,
                Role: DockGroupRole.ToolWindowStrip),
        });
        // </snippet:document-well>
    }
}

class ConstrainedToolWindowDemo : Component
{
    public override Element Render() => new DockManager
    {
        Layout = CreateErrorsPane(),
    };

    private static ToolWindow CreateErrorsPane()
    {
        // <snippet:allowed-sides>
        var errors = new ToolWindow
        {
            Title = "Errors",
            Key = "tool:errors",
            AllowedSides = DockSides.Bottom,
        };
        // </snippet:allowed-sides>

        return errors;
    }
}

// <snippet:side-pin>
class SidePinDemo : Component
{
    public override Element Render() => new DockManager
    {
        Layout = new Document
        {
            Title = "Document",
            Key = "doc:main",
            Content = VStack(8,
                TextBlock("Document area").SemiBold(),
                TextBlock("Click the pinned tab on the right to expand it."),
                TextBlock("Pin / unpin from inside the popup to toggle.")
            ).Padding(16)
        },

        RightSide = new[]
        {
            new ToolWindow
            {
                Title = "Properties",
                Key = "tool:properties",
                Content = VStack(4,
                    TextBlock("Name").SemiBold(),
                    TextBlock("Width: 240"),
                    TextBlock("Height: 120")
                ).Padding(12)
            },
        },
    };
}
// </snippet:side-pin>

// <snippet:persistence>
class PersistenceDemo : Component
{
    public override Element Render() => new DockManager
    {
        // Layout JSON is auto-saved to WindowPersistedScope["docking:my-shell"].
        // It is the restore fallback when a later mount leaves Layout null.
        PersistenceId = "my-shell",
        Layout = new DockSplit(
            Orientation.Horizontal,
            new DockNode[]
            {
                new ToolWindow
                {
                    Title = "Outline",
                    Key = "tool:outline",
                    Width = 240,
                    Content = TextBlock("Rearrange me, then relaunch.").Padding(12)
                },
                new Document
                {
                    Title = "Editor",
                    Key = "doc:editor",
                    Content = TextBlock("Layout restores from PersistenceId when no declarative Layout is supplied.").Padding(12)
                },
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
