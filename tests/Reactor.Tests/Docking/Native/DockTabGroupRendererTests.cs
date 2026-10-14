using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
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

    // ── §2.8: default tab styling derived from content type ────────────
    //
    // A compact group must never render an unselected tab as a blank stub.
    // WinUI's TabViewWidthMode.Compact collapses every unselected tab to its
    // icon, and docking panes carry no icon, so compact tool groups are
    // realized as SizeToContent (dense, but every tab shows its title).

    [Fact]
    public void Render_AllToolWindow_DefaultsToCompactTabs_SizedToTitles()
    {
        var docs = new DockableContent[]
        {
            new ToolWindow { Title = "Solution Explorer", Key = "se" },
            new ToolWindow { Title = "Properties",        Key = "pr" },
        };
        var group = new DockTabGroup(docs); // record defaults: Top, !Compact

        var tab = (TabViewElement)DockTabGroupRenderer.Render(
            group,
            d => d.Content,
            onSelectedIndexChanged: null,
            onTabClosing: null);

        Assert.True(DockTabGroupRenderer.ResolveCompactTabs(group));
        Assert.Equal(TabViewWidthMode.SizeToContent, tab.TabWidthMode);
    }

    [Fact]
    public void Render_AllDocument_StaysEqualWidth()
    {
        var docs = new DockableContent[]
        {
            new Document { Title = "Main.cs",    Key = "m" },
            new Document { Title = "Program.cs", Key = "p" },
        };
        var group = new DockTabGroup(docs);

        var tab = (TabViewElement)DockTabGroupRenderer.Render(
            group,
            d => d.Content,
            onSelectedIndexChanged: null,
            onTabClosing: null);

        Assert.Equal(TabViewWidthMode.Equal, tab.TabWidthMode);
    }

    [Fact]
    public void Render_MixedDocumentAndToolWindow_StaysEqualWidth()
    {
        // A mixed group (e.g. tool window dragged into editor strip)
        // doesn't auto-switch to tool-pane styling — the document
        // shape wins so the editor's tab strip stays consistent.
        var docs = new DockableContent[]
        {
            new Document { Title = "Editor", Key = "e" },
            new ToolWindow { Title = "Tool", Key = "t" },
        };
        var group = new DockTabGroup(docs);

        var tab = (TabViewElement)DockTabGroupRenderer.Render(
            group,
            d => d.Content,
            onSelectedIndexChanged: null,
            onTabClosing: null);

        Assert.Equal(TabViewWidthMode.Equal, tab.TabWidthMode);
    }

    [Fact]
    public void Render_AllToolWindow_ExplicitNonCompact_HonorsHeuristic()
    {
        // Documenting the heuristic's behavior: a ToolWindow group that
        // passes the *literal* default values still gets the auto-flip.
        // Apps wanting a wide-tabs ToolWindow group must distinguish via
        // some other dimension (e.g. CompactTabs=true is fine and is
        // honored; an "explicit Top/non-compact" group with only tools
        // is interpreted as "use the typed default", flipping to
        // compact). This locks down the contract.
        var docs = new DockableContent[]
        {
            new ToolWindow { Title = "T1", Key = "1" },
        };
        var explicitDefaults = new DockTabGroup(docs, TabPosition.Top, CompactTabs: false);

        var tab = (TabViewElement)DockTabGroupRenderer.Render(
            explicitDefaults,
            d => d.Content,
            onSelectedIndexChanged: null,
            onTabClosing: null);

        Assert.True(DockTabGroupRenderer.ResolveCompactTabs(explicitDefaults));
        Assert.Equal(TabViewWidthMode.SizeToContent, tab.TabWidthMode);
    }

    [Fact]
    public void Render_AllToolWindow_ExplicitCompact_SizesTabsToTitles()
    {
        var docs = new DockableContent[]
        {
            new ToolWindow { Title = "T1", Key = "1" },
        };
        var group = new DockTabGroup(docs, TabPosition.Bottom, CompactTabs: true);

        var tab = (TabViewElement)DockTabGroupRenderer.Render(
            group,
            d => d.Content,
            onSelectedIndexChanged: null,
            onTabClosing: null);

        Assert.True(DockTabGroupRenderer.ResolveCompactTabs(group));
        Assert.Equal(TabViewWidthMode.SizeToContent, tab.TabWidthMode);
    }

    [Fact]
    public void Render_CompactToolStrip_NeverUsesIconOnlyCompactMode()
    {
        // The Reactor IDE sample's bottom dock: an explicitly compact tool
        // strip whose unselected tabs rendered as blank ~34 DIP stubs with
        // no title and no icon, because WinUI's Compact mode shows only the
        // icon of an unselected tab and docking panes have none.
        var docs = new DockableContent[]
        {
            new ToolWindow { Title = "Output",     Key = "tool:output" },
            new ToolWindow { Title = "Terminal",   Key = "tool:terminal" },
            new ToolWindow { Title = "Error List", Key = "tool:errors" },
        };
        var group = new DockTabGroup(
            docs,
            TabPosition.Bottom,
            CompactTabs: true,
            Role: DockGroupRole.ToolWindowStrip);

        var tab = (TabViewElement)DockTabGroupRenderer.Render(
            group,
            d => d.Content,
            onSelectedIndexChanged: null,
            onTabClosing: null,
            onPinRequested: _ => { });

        Assert.NotEqual(TabViewWidthMode.Compact, tab.TabWidthMode);
        Assert.Equal(new[] { "Output", "Terminal", "Error List" }, tab.Tabs.Select(t => t.Header));
        Assert.All(tab.Tabs, t => Assert.True(string.IsNullOrEmpty(t.Icon)));
    }

    // ── §2.8: pure resolution — compact request ────────────────────────

    public enum PaneKind { Tool, Document, Mixed, Untyped }

    [Theory]
    [InlineData(PaneKind.Tool, TabPosition.Top, false, true)]        // record defaults → flip
    [InlineData(PaneKind.Tool, TabPosition.Bottom, true, true)]      // explicit compact
    [InlineData(PaneKind.Tool, TabPosition.Bottom, false, false)]    // customized, not compact
    [InlineData(PaneKind.Document, TabPosition.Top, false, false)]
    [InlineData(PaneKind.Document, TabPosition.Top, true, true)]     // explicit compact honored
    [InlineData(PaneKind.Mixed, TabPosition.Top, false, false)]
    [InlineData(PaneKind.Untyped, TabPosition.Top, false, false)]
    public void ResolveCompactTabs_FollowsContentTypeDefaults(
        PaneKind kind, TabPosition position, bool compactTabs, bool expected)
    {
        DockableContent[] docs = kind switch
        {
            PaneKind.Tool     => new DockableContent[] { new ToolWindow { Title = "T", Key = "t" } },
            PaneKind.Document => new DockableContent[] { new Document { Title = "D", Key = "d" } },
            PaneKind.Mixed    => new DockableContent[]
            {
                new Document { Title = "D", Key = "d" },
                new ToolWindow { Title = "T", Key = "t" },
            },
            _                 => new DockableContent[] { new("P") },
        };
        var group = new DockTabGroup(docs, position, compactTabs);

        Assert.Equal(expected, DockTabGroupRenderer.ResolveCompactTabs(group));
    }

    // ── Pure resolution — compact request → WinUI width mode ───────────

    private static TabViewItemData TabWithIcon(string header, string? icon) =>
        new(header, new TextBlockElement(header)) { Icon = icon };

    [Fact]
    public void ResolveTabWidthMode_NotCompact_IsEqual()
    {
        var tabs = new[] { TabWithIcon("A", "Home"), TabWithIcon("B", null) };
        Assert.Equal(TabViewWidthMode.Equal, DockTabGroupRenderer.ResolveTabWidthMode(false, tabs));
    }

    [Fact]
    public void ResolveTabWidthMode_CompactWithoutIcons_SizesToContent()
    {
        var tabs = new[] { TabWithIcon("A", null), TabWithIcon("B", string.Empty) };
        Assert.Equal(TabViewWidthMode.SizeToContent, DockTabGroupRenderer.ResolveTabWidthMode(true, tabs));
    }

    [Fact]
    public void ResolveTabWidthMode_CompactWithOneIconlessTab_SizesToContent()
    {
        // A single icon-less tab would be the blank stub whenever it is not
        // selected, so one missing icon is enough to rule Compact out.
        var tabs = new[] { TabWithIcon("A", "Home"), TabWithIcon("B", null), TabWithIcon("C", "Setting") };
        Assert.Equal(TabViewWidthMode.SizeToContent, DockTabGroupRenderer.ResolveTabWidthMode(true, tabs));
    }

    [Fact]
    public void ResolveTabWidthMode_CompactWithAllIcons_IsIconOnlyCompact()
    {
        var tabs = new[] { TabWithIcon("A", "Home"), TabWithIcon("B", "Setting") };
        Assert.Equal(TabViewWidthMode.Compact, DockTabGroupRenderer.ResolveTabWidthMode(true, tabs));
    }

    // ── §2.2: per-tab pin button on ToolWindow ─────────────────────────

    [Fact]
    public void Render_ToolWindowWithAutoHide_GetsPinButton_WhenCallbackProvided()
    {
        var docs = new DockableContent[]
        {
            new Document   { Title = "Main.cs", Key = "m" },
            new ToolWindow { Title = "Output",  Key = "o" }, // CanAutoHide default true
        };
        var group = new DockTabGroup(docs);

        ToolWindow? pinned = null;
        var tab = (TabViewElement)DockTabGroupRenderer.Render(
            group,
            d => d.Content,
            onSelectedIndexChanged: null,
            onTabClosing: null,
            onPinRequested: tw => pinned = tw);

        Assert.False(tab.Tabs[0].IsPinnable, "Document tab should not get pin button");
        Assert.True(tab.Tabs[1].IsPinnable, "ToolWindow tab should get pin button");
        Assert.Equal("pin:o", tab.Tabs[1].PinAutomationId);
        Assert.NotNull(tab.Tabs[1].OnPinRequested);
        tab.Tabs[1].OnPinRequested!();
        Assert.NotNull(pinned);
        Assert.Equal("o", pinned!.Key);
    }

    [Fact]
    public void Render_NoPinCallback_NoPinButton()
    {
        var docs = new DockableContent[]
        {
            new ToolWindow { Title = "Output", Key = "o" },
        };
        var group = new DockTabGroup(docs);

        var tab = (TabViewElement)DockTabGroupRenderer.Render(
            group,
            d => d.Content,
            onSelectedIndexChanged: null,
            onTabClosing: null,
            onPinRequested: null);

        Assert.False(tab.Tabs[0].IsPinnable);
    }

    [Fact]
    public void Render_ToolWindowWithoutAutoHide_NoPinButton()
    {
        var docs = new DockableContent[]
        {
            new ToolWindow { Title = "Pinned-Only", Key = "p", CanAutoHide = false },
        };
        var group = new DockTabGroup(docs);

        var tab = (TabViewElement)DockTabGroupRenderer.Render(
            group,
            d => d.Content,
            onSelectedIndexChanged: null,
            onTabClosing: null,
            onPinRequested: _ => { });

        Assert.False(tab.Tabs[0].IsPinnable);
    }

    // ── §4.6 TabChrome ─────────────────────────────────────────────────

    [Fact]
    public void Render_DefaultTabChrome_IsWin11()
    {
        var group = new DockTabGroup(new DockableContent[] { new("A") });
        Assert.Equal(TabChrome.Win11, group.TabChrome);
    }

    [Theory]
    [InlineData(TabChrome.Win11,    1)] // blanker only
    [InlineData(TabChrome.Flat,     2)] // blanker + apply
    [InlineData(TabChrome.TitleBar, 2)] // blanker + apply
    public void BuildSetters_HasBlankerPlusOptionalApply(TabChrome chrome, int expectedCount)
    {
        // Pool-safety contract per §4.6 — every preset starts with the
        // shared "clear managed keys" setter so a TabView reused across
        // chrome flips doesn't leak prior overrides.
        var group = new DockTabGroup(new DockableContent[] { new("A") }, TabChrome: chrome);
        var setters = DockTabGroupRenderer.BuildSetters(group);
        Assert.Equal(expectedCount, setters.Length);
    }

    [Fact]
    public void ManagedResourceKeys_AreLocked()
    {
        // The exact key set is part of the §4.6 chrome contract: it
        // defines what gets blanked between chrome flips. Lock the list
        // so a refactor that drops a key without adjusting consumers
        // surfaces here.
        Assert.Equal(new[]
        {
            "TabViewItemHeaderCornerRadius",
            "TabViewItemHeaderPadding",
            "TabViewItemHeaderBackgroundSelected",
            "TabViewItemHeaderBackgroundPointerOver",
            "TabViewBackground",
        }, DockTabGroupRenderer.ManagedResourceKeys);
    }
}
