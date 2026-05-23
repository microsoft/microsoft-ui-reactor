using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Hosting;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking.Native;

/// <summary>
/// Spec 045 §4.2 / §4.3 — floating-window root shape: Edge / Files
/// "tabs-in-titlebar" pattern. The root is a <c>TabViewElement</c>
/// (rendered <c>DockTabGroup</c>) directly — no separate WinUI 3
/// <c>TitleBar</c> wrapper. The tab strip sits in the title-bar zone
/// at y=0 (via <c>ExtendsContentIntoTitleBar = true</c> on the
/// WindowSpec); a <c>TabStripFooter</c> drag region is registered via
/// <c>Window.SetTitleBar</c> on mount so the OS knows where dragging
/// is enabled. Asserts element-tree shape without booting a UI thread.
/// </summary>
public class DockFloatingWindowTests
{
    private static TabViewElement BuildRoot(DockableContent pane)
    {
        // Holder + manager are nullable in `BuildFloatingRoot`; the
        // close / drag-completion callbacks use the holder lazily so
        // a null window is safe for tree-shape tests.
        //
        // `Provide(...)` does not wrap the element — it sets the
        // ContextValues bag on the existing element (`with { ... }`)
        // so the returned record is still the underlying TabViewElement
        // (see ContextExtensions.Provide).
        var holder = new ReactorWindow?[] { null };
        var root = DockFloatingWindow.BuildFloatingRoot(pane, holder, manager: null);
        return Assert.IsType<TabViewElement>(root);
    }

    [Fact]
    public void BuildFloatingRoot_RootIsTabView()
    {
        // §4.3 — tabs-in-titlebar (Edge pattern): the TabView is the
        // window root so its strip occupies the title-bar zone.
        var pane = new DockableContent(Title: "FloatPane", Content: new TextBlockElement("body"));
        var tv = BuildRoot(pane);
        Assert.NotNull(tv);
    }

    [Fact]
    public void BuildFloatingRoot_HasOneTabWithPaneTitle()
    {
        var pane = new DockableContent(Title: "MyDoc", Content: new TextBlockElement("body"));
        var tv = BuildRoot(pane);
        Assert.Single(tv.Tabs);
        Assert.Equal("MyDoc", tv.Tabs[0].Header);
    }

    [Fact]
    public void BuildFloatingRoot_UsesTitleBarTabChrome()
    {
        // §4.6 — the tab strip is themed to merge with the OS title bar
        // (TabChrome.TitleBar applies TitleBarBackgroundFillBrush).
        var pane = new DockableContent(Title: "P", Content: new TextBlockElement("body"));
        var tv = BuildRoot(pane);
        Assert.NotEmpty(tv.Setters);
    }

    [Fact]
    public void BuildFloatingRoot_HasDragRegionInTabStripFooter()
    {
        // §4.2 / §4.4 — drag region in TabStripFooter is what gets
        // handed to Window.SetTitleBar on mount so the OS reserves
        // caption-button inset and treats the footer as drag-move
        // surface.
        var pane = new DockableContent(Title: "P", Content: new TextBlockElement("body"));
        var tv = BuildRoot(pane);
        Assert.NotNull(tv.TabStripFooter);
        // The drag region is a BorderElement with an OnMount handler
        // that calls Window.SetTitleBar. We can't invoke OnMount without
        // a real FrameworkElement, but we can verify the element shape.
        Assert.IsType<BorderElement>(tv.TabStripFooter);
    }

    [Fact]
    public void BuildFloatingRoot_ProvidesFloatingPaneState()
    {
        // §2.17 — UseDockState consumers inside the floating window
        // resolve to PaneState=Floating. Lock the context wiring.
        var pane = new DockableContent(Title: "P", Content: new TextBlockElement("body"));
        var holder = new ReactorWindow?[] { null };
        var root = DockFloatingWindow.BuildFloatingRoot(pane, holder, manager: null);
        Assert.NotNull(root.ContextValues);
        Assert.True(root.ContextValues!.ContainsKey(DockContexts.PaneState));
        Assert.Equal(DockPaneState.Floating, root.ContextValues[DockContexts.PaneState]);
    }
}
