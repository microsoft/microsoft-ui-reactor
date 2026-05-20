using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking.Native;

/// <summary>
/// Structural coverage for the §2.2 tab-group renderer. The renderer
/// returns a <see cref="TabViewElement"/> shape — assert that mapping
/// directly without booting a UI thread.
/// </summary>
public class DockTabGroupRendererTests
{
    [Fact]
    public void Render_MapsDocumentsToTabs_PreservingTitlesAndContent()
    {
        var docs = new DockableContent[]
        {
            new("Alpha", Content: new TextBlockElement("A body"), CanClose: true),
            new("Beta",  Content: new TextBlockElement("B body"), CanClose: false),
        };
        var group = new DockTabGroup(docs);

        var el = DockTabGroupRenderer.Render(
            group,
            renderLeafContent: d => d.Content,
            onSelectedIndexChanged: null,
            onTabClosing: null);

        var tab = Assert.IsType<TabViewElement>(el);
        Assert.Equal(2, tab.Tabs.Length);
        Assert.Equal("Alpha", tab.Tabs[0].Header);
        Assert.Equal("Beta", tab.Tabs[1].Header);
        Assert.IsType<TextBlockElement>(tab.Tabs[0].Content);
        Assert.True(tab.Tabs[0].IsClosable);
        Assert.False(tab.Tabs[1].IsClosable);
    }

    [Fact]
    public void Render_SelectedIndex_ClampsOutOfRange()
    {
        var docs = new DockableContent[] { new("Only") };
        var group = new DockTabGroup(docs, SelectedIndex: 99);

        var tab = (TabViewElement)DockTabGroupRenderer.Render(
            group,
            d => d.Content,
            onSelectedIndexChanged: null,
            onTabClosing: null);

        Assert.Equal(0, tab.SelectedIndex);
    }

    [Fact]
    public void Render_NegativeSelectedIndex_DefaultsToZero()
    {
        var docs = new DockableContent[] { new("Only"), new("Two") };
        var group = new DockTabGroup(docs, SelectedIndex: -1);

        var tab = (TabViewElement)DockTabGroupRenderer.Render(
            group,
            d => d.Content,
            onSelectedIndexChanged: null,
            onTabClosing: null);

        Assert.Equal(0, tab.SelectedIndex);
    }

    [Fact]
    public void Render_TabCloseCallback_PassesThroughDockableContent()
    {
        DockableContent? captured = null;
        var docs = new DockableContent[]
        {
            new("A", CanClose: true),
            new("B", CanClose: true),
        };
        var group = new DockTabGroup(docs);

        var tab = (TabViewElement)DockTabGroupRenderer.Render(
            group,
            d => d.Content,
            onSelectedIndexChanged: null,
            onTabClosing: d => captured = d);

        Assert.NotNull(tab.OnTabCloseRequested);
        tab.OnTabCloseRequested!(1);
        Assert.NotNull(captured);
        Assert.Equal("B", captured!.Title);
    }

    [Fact]
    public void Render_EmptyDocuments_ReturnsBorderPlaceholder()
    {
        var group = new DockTabGroup(Array.Empty<DockableContent>());
        var el = DockTabGroupRenderer.Render(
            group,
            d => d.Content,
            onSelectedIndexChanged: null,
            onTabClosing: null);
        Assert.IsType<BorderElement>(el);
    }

    [Fact]
    public void Render_NullLeafContent_FallsBackToBorder()
    {
        var docs = new DockableContent[] { new("A", Content: null) };
        var group = new DockTabGroup(docs);
        var tab = (TabViewElement)DockTabGroupRenderer.Render(
            group,
            d => d.Content,            // returns null
            onSelectedIndexChanged: null,
            onTabClosing: null);
        Assert.IsType<BorderElement>(tab.Tabs[0].Content);
    }
}
