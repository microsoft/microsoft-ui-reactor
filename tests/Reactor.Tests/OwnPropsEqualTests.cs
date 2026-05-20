using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Xunit;
using ReactorCore = Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for <see cref="Element.OwnPropsEqual"/>. This method gates the
/// reconciler's "did this element's own properties change?" decision:
/// returning <c>true</c> when a property changed means the WinUI control
/// keeps a stale value, returning <c>false</c> when nothing changed makes
/// the reconcile-highlight overlay flash on every render.
///
/// Each test pairs (a) a "same own-props" case → expected true, with
/// (b) one or more "differ by exactly one property" cases → expected
/// false. That pairing is what catches a real regression: if a future
/// edit adds a property without adding it to the switch arm, only the
/// "differ" case starts failing while the "same" case keeps passing.
/// </summary>
public class OwnPropsEqualTests
{
    // Helper: empty Element[] reused across container constructors.
    private static readonly Element[] NoChildren = global::System.Array.Empty<Element>();

    // ────────────────────────────────────────────────────────────────────
    // Reference + type-tag short circuits
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReferenceEquals_ReturnsTrue_WithoutFieldInspection()
    {
        // Bug this catches: regression that removed the ReferenceEquals fast
        // path — a memoized element compared to itself would walk the whole
        // switch and inflate reconcile cost on every render.
        var e = new StackElement(Orientation.Vertical, NoChildren);
        Assert.True(Element.OwnPropsEqual(e, e));
    }

    [Fact]
    public void DifferentTypes_ReturnFalse()
    {
        // Bug this catches: the type-tag guard at the top dropping out —
        // a Stack swapped to a Grid in the tree would compare prop-by-prop
        // (matching nothing) and report "equal", suppressing the rebuild.
        Element a = new StackElement(Orientation.Vertical, NoChildren);
        Element b = new ReactorCore.GridElement(new GridDefinition(["*"], ["*"]), NoChildren);
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    // ────────────────────────────────────────────────────────────────────
    // Container layouts — each property is observable in WinUI render
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void StackElement_SameOwnProps_AreEqual()
    {
        var a = new StackElement(Orientation.Vertical, NoChildren) { Spacing = 8 };
        var b = new StackElement(Orientation.Vertical, NoChildren) { Spacing = 8 };
        // Bug this catches: a future Spacing default change that only updates
        // one construction path would fail this test.
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Theory]
    // Orientation flips StackPanel rendering axis — must trigger rebuild.
    [InlineData("orientation")]
    // Spacing change must apply or VStack(a, b) renders without the new gap.
    [InlineData("spacing")]
    // HorizontalAlignment/VerticalAlignment changes change positioning.
    [InlineData("horizontalAlignment")]
    [InlineData("verticalAlignment")]
    public void StackElement_DifferingProp_NotEqual(string which)
    {
        var a = new StackElement(Orientation.Vertical, NoChildren)
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var b = which switch
        {
            "orientation" => a with { Orientation = Orientation.Horizontal },
            "spacing" => a with { Spacing = 12 },
            "horizontalAlignment" => a with { HorizontalAlignment = HorizontalAlignment.Right },
            "verticalAlignment" => a with { VerticalAlignment = VerticalAlignment.Bottom },
            _ => throw new global::System.InvalidOperationException(),
        };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void GridElement_SameOwnProps_AreEqual()
    {
        var def = new GridDefinition(["*"], ["*"]);
        var a = new ReactorCore.GridElement(def, NoChildren) { RowSpacing = 4, ColumnSpacing = 6 };
        var b = new ReactorCore.GridElement(def, NoChildren) { RowSpacing = 4, ColumnSpacing = 6 };
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void GridElement_DifferentDefinitionInstance_NotEqual()
    {
        // GridDefinition is compared by reference — two structurally equal
        // definitions are intentionally treated as different so the reconciler
        // rebuilds the Grid.RowDefinitions / ColumnDefinitions tracks. Bug
        // this catches: a regression to value equality would skip rebuilding
        // tracks even though the author re-allocated the definition.
        var a = new ReactorCore.GridElement(new GridDefinition(["*"], ["*"]), NoChildren);
        var b = new ReactorCore.GridElement(new GridDefinition(["*"], ["*"]), NoChildren);
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Theory]
    [InlineData("row")]
    [InlineData("column")]
    public void GridElement_SpacingChange_NotEqual(string which)
    {
        var def = new GridDefinition(["*"], ["*"]);
        var a = new ReactorCore.GridElement(def, NoChildren) { RowSpacing = 0, ColumnSpacing = 0 };
        var b = which == "row" ? a with { RowSpacing = 4 } : a with { ColumnSpacing = 4 };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void BorderElement_SameOwnProps_AreEqual()
    {
        // Null brushes are intentional — BrushesEqual(null, null) → true.
        // Avoids host-bound SolidColorBrush construction while still
        // exercising the BorderElement switch arm + nested null path.
        var a = new BorderElement(null) { CornerRadius = 4, Padding = new Thickness(8), BorderThickness = 1 };
        var b = new BorderElement(null) { CornerRadius = 4, Padding = new Thickness(8), BorderThickness = 1 };
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Theory]
    [InlineData("cornerRadius")]
    [InlineData("padding")]
    [InlineData("borderThickness")]
    public void BorderElement_DifferingProp_NotEqual(string which)
    {
        var a = new BorderElement(null) { CornerRadius = 4, Padding = new Thickness(8), BorderThickness = 1 };
        var b = which switch
        {
            "cornerRadius" => a with { CornerRadius = 8 },
            "padding" => a with { Padding = new Thickness(12) },
            "borderThickness" => a with { BorderThickness = 2 },
            _ => throw new global::System.InvalidOperationException(),
        };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Theory]
    [InlineData("orientation")]
    [InlineData("hVis")]
    [InlineData("vVis")]
    [InlineData("hMode")]
    [InlineData("vMode")]
    [InlineData("zoom")]
    public void ScrollViewerElement_DifferingProp_NotEqual(string which)
    {
        // Every ScrollViewer DP here is observable to the user — collapsing
        // a scrollbar, switching axis, enabling pinch-zoom. The switch arm
        // is the only thing protecting against a silent stale render.
        var a = new ScrollViewerElement(new EmptyElement());
        var b = which switch
        {
            "orientation" => a with { Orientation = Orientation.Horizontal },
            "hVis" => a with { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            "vVis" => a with { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden },
            "hMode" => a with { HorizontalScrollMode = ScrollMode.Disabled },
            "vMode" => a with { VerticalScrollMode = ScrollMode.Disabled },
            "zoom" => a with { ZoomMode = ZoomMode.Enabled },
            _ => throw new global::System.InvalidOperationException(),
        };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void ScrollViewerElement_SameOwnProps_AreEqual()
    {
        var a = new ScrollViewerElement(new EmptyElement());
        var b = new ScrollViewerElement(new EmptyElement());
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Theory]
    [InlineData("contentOrientation")]
    [InlineData("hVis")]
    [InlineData("vVis")]
    [InlineData("hMode")]
    [InlineData("vMode")]
    [InlineData("zoom")]
    [InlineData("minZoom")]
    [InlineData("maxZoom")]
    [InlineData("hAnchor")]
    [InlineData("vAnchor")]
    public void ScrollViewElement_DifferingProp_NotEqual(string which)
    {
        var a = new ScrollViewElement(new EmptyElement());
        var b = which switch
        {
            "contentOrientation" => a with { ContentOrientation = Microsoft.UI.Xaml.Controls.ScrollingContentOrientation.Horizontal },
            "hVis" => a with { HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollingScrollBarVisibility.Hidden },
            "vVis" => a with { VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollingScrollBarVisibility.Hidden },
            "hMode" => a with { HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollingScrollMode.Disabled },
            "vMode" => a with { VerticalScrollMode = Microsoft.UI.Xaml.Controls.ScrollingScrollMode.Disabled },
            "zoom" => a with { ZoomMode = Microsoft.UI.Xaml.Controls.ScrollingZoomMode.Enabled },
            "minZoom" => a with { MinZoomFactor = 0.5 },
            "maxZoom" => a with { MaxZoomFactor = 4.0 },
            "hAnchor" => a with { HorizontalAnchorRatio = 0.5 },
            "vAnchor" => a with { VerticalAnchorRatio = 1.0 },
            _ => throw new global::System.InvalidOperationException(),
        };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void ScrollViewElement_SameOwnProps_AreEqual()
    {
        var a = new ScrollViewElement(new EmptyElement());
        var b = new ScrollViewElement(new EmptyElement());
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Theory]
    [InlineData("direction")]
    [InlineData("justify")]
    [InlineData("alignItems")]
    [InlineData("alignContent")]
    [InlineData("wrap")]
    [InlineData("colGap")]
    [InlineData("rowGap")]
    [InlineData("padding")]
    public void FlexElement_DifferingProp_NotEqual(string which)
    {
        var a = new FlexElement(NoChildren);
        var b = which switch
        {
            "direction" => a with { Direction = Layout.FlexDirection.Column },
            "justify" => a with { JustifyContent = Layout.FlexJustify.Center },
            "alignItems" => a with { AlignItems = Layout.FlexAlign.Center },
            "alignContent" => a with { AlignContent = Layout.FlexAlign.Center },
            "wrap" => a with { Wrap = Layout.FlexWrap.Wrap },
            "colGap" => a with { ColumnGap = 4 },
            "rowGap" => a with { RowGap = 4 },
            "padding" => a with { FlexPadding = new Thickness(8) },
            _ => throw new global::System.InvalidOperationException(),
        };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void FlexElement_SameOwnProps_AreEqual()
    {
        var a = new FlexElement(NoChildren);
        var b = new FlexElement(NoChildren);
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void CanvasElement_OnlySettersMatter_DifferentChildrenAreEqual()
    {
        // CanvasElement's OwnPropsEqual ignores Children (they're diffed
        // separately) and only checks Setters. Bug this catches: a regression
        // that started inspecting Children would mark Canvas as changed on
        // every reconcile and re-apply WinUI Setters needlessly.
        var a = new CanvasElement(NoChildren);
        var b = new CanvasElement(new Element[] { new EmptyElement() });
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Theory]
    [InlineData("orientation")]
    [InlineData("itemWidth")]
    [InlineData("itemHeight")]
    [InlineData("max")]
    public void WrapGridElement_DifferingProp_NotEqual(string which)
    {
        var a = new WrapGridElement(NoChildren) { ItemWidth = 80, ItemHeight = 80, MaximumRowsOrColumns = 4 };
        var b = which switch
        {
            "orientation" => a with { Orientation = Orientation.Vertical },
            "itemWidth" => a with { ItemWidth = 100 },
            "itemHeight" => a with { ItemHeight = 100 },
            "max" => a with { MaximumRowsOrColumns = 6 },
            _ => throw new global::System.InvalidOperationException(),
        };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void RelativePanelElement_OnlySettersMatter()
    {
        var a = new RelativePanelElement(NoChildren);
        var b = new RelativePanelElement(new Element[] { new EmptyElement() });
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void ViewboxElement_OnlySettersMatter()
    {
        // Both have an empty children array → setter arrays are
        // reference-equal-by-default-init pattern. Pin: own-props equal.
        var a = new ViewboxElement(new EmptyElement());
        var b = new ViewboxElement(new EmptyElement());
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    // ────────────────────────────────────────────────────────────────────
    // Structural wrappers — should always be "equal" so reconcile-highlight
    // doesn't strobe whenever a descendant changes
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void NavigationHostElement_AlwaysEqual_ProtectsAgainstOverlayStrobe()
    {
        // Bug this catches: removing the (NavigationHostElement, _) arm
        // would fall through to `_ => false` and the reconcile overlay
        // would highlight the entire nav-host every time the active route
        // re-renders.
        static Element Map(object _) => new EmptyElement();
        var a = new NavigationHostElement("r1", Map);
        var b = new NavigationHostElement("r2", Map);
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void CommandHostElement_AlwaysEqual()
    {
        var a = new CommandHostElement(global::System.Array.Empty<Command>(), new EmptyElement());
        var b = new CommandHostElement(global::System.Array.Empty<Command>(), new EmptyElement());
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void GroupElement_AlwaysEqual()
    {
        var a = new GroupElement(NoChildren);
        var b = new GroupElement(new Element[] { new EmptyElement() });
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void ErrorBoundaryElement_AlwaysEqual()
    {
        static Element Fb(global::System.Exception _) => new EmptyElement();
        var a = new ErrorBoundaryElement(new EmptyElement(), Fb);
        var b = new ErrorBoundaryElement(new EmptyElement(), Fb);
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void ComponentElement_AlwaysEqual()
    {
        // ComponentElement.OwnPropsEqual returns true so the reconcile
        // overlay highlights only the actual changed leaf inside the
        // component, not the wrapper. Pass distinct types/props so a
        // future regression that started comparing ComponentType or
        // Props would fail this test.
        var a = new ComponentElement(typeof(global::System.Object), Props: 1);
        var b = new ComponentElement(typeof(global::System.String), Props: 2);
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void FuncElement_AlwaysEqual()
    {
        static Element R1(RenderContext _) => new EmptyElement();
        static Element R2(RenderContext _) => new GroupElement(global::System.Array.Empty<Element>());
        var a = new FuncElement(R1);
        var b = new FuncElement(R2);
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void MemoElement_AlwaysEqual()
    {
        static Element R(RenderContext _) => new EmptyElement();
        var a = new MemoElement(R, new object?[] { 1 });
        var b = new MemoElement(R, new object?[] { 2 });
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void ModifiedElement_AlwaysEqual()
    {
        var a = new ModifiedElement(new EmptyElement(), new ElementModifiers());
        var b = new ModifiedElement(new EmptyElement(), new ElementModifiers { Margin = new Thickness(4) });
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    // ────────────────────────────────────────────────────────────────────
    // TitleBar / Popup — explicit own-props (Title, Subtitle, button flags,
    // IsOpen) drive visible chrome state, so this arm matters
    // ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("title")]
    [InlineData("subtitle")]
    [InlineData("backVis")]
    [InlineData("backEnabled")]
    [InlineData("paneVis")]
    public void TitleBarElement_DifferingProp_NotEqual(string which)
    {
        // The doc on this case notes: "Without this, TitleBar flashes yellow
        // on every reconcile even when only descendants changed." So the
        // *positive* test that this arm exists is what guards against the
        // flash; the *negative* tests below ensure observable chrome state
        // changes still trigger an update.
        var a = new TitleBarElement("App")
        {
            Subtitle = "v1",
            IsBackButtonVisible = false,
            IsBackButtonEnabled = false,
            IsPaneToggleButtonVisible = false,
        };
        var b = which switch
        {
            "title" => a with { Title = "Other" },
            "subtitle" => a with { Subtitle = "v2" },
            "backVis" => a with { IsBackButtonVisible = true },
            "backEnabled" => a with { IsBackButtonEnabled = true },
            "paneVis" => a with { IsPaneToggleButtonVisible = true },
            _ => throw new global::System.InvalidOperationException(),
        };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void TitleBarElement_DescendantChange_DoesNotFlashChrome()
    {
        // Pinning the comment in the source: Content / RightHeader are
        // *not* own-props and changing them must leave OwnPropsEqual=true,
        // because they recurse as children. If a future change added Content
        // to the comparison, every descendant edit would flash the chrome.
        var a = new TitleBarElement("App") { Content = new EmptyElement() };
        var b = new TitleBarElement("App") { Content = new GroupElement(NoChildren), RightHeader = new EmptyElement() };
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Theory]
    [InlineData("isOpen")]
    [InlineData("lightDismiss")]
    public void PopupElement_DifferingProp_NotEqual(string which)
    {
        var a = new PopupElement(new EmptyElement()) { IsOpen = false, IsLightDismissEnabled = true };
        var b = which switch
        {
            "isOpen" => a with { IsOpen = true },
            "lightDismiss" => a with { IsLightDismissEnabled = false },
            _ => throw new global::System.InvalidOperationException(),
        };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    // ────────────────────────────────────────────────────────────────────
    // Flyout family — return true so they don't fight the diff overlay
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void MenuFlyoutElement_AlwaysEqual()
    {
        var items = global::System.Array.Empty<ReactorCore.MenuFlyoutItemBase>();
        var a = new MenuFlyoutElement(new EmptyElement(), items);
        var b = new MenuFlyoutElement(new GroupElement(NoChildren), items);
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void ContentFlyoutElement_AlwaysEqual()
    {
        var a = new ContentFlyoutElement(new EmptyElement());
        var b = new ContentFlyoutElement(new GroupElement(NoChildren));
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void MenuFlyoutContentElement_AlwaysEqual()
    {
        var a = new MenuFlyoutContentElement(global::System.Array.Empty<ReactorCore.MenuFlyoutItemBase>());
        var b = new MenuFlyoutContentElement(global::System.Array.Empty<ReactorCore.MenuFlyoutItemBase>());
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void FlyoutElement_AlwaysEqual()
    {
        var a = new FlyoutElement(new EmptyElement(), new EmptyElement());
        var b = new FlyoutElement(new GroupElement(NoChildren), new GroupElement(NoChildren));
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    // ────────────────────────────────────────────────────────────────────
    // Selection-driven collections — each arm protects against a real
    // observable bug (selection visually pinned to stale index, header
    // not redrawing, single-vs-multi select stuck)
    // ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("selectedIndex")]
    [InlineData("placeholder")]
    [InlineData("header")]
    [InlineData("editable")]
    public void ComboBoxElement_DifferingProp_NotEqual(string which)
    {
        var items = new[] { "a", "b" };
        var a = new ComboBoxElement(items, SelectedIndex: 0)
        {
            PlaceholderText = "pick",
            Header = "h",
            IsEditable = false,
        };
        var b = which switch
        {
            "selectedIndex" => a with { SelectedIndex = 1 },
            "placeholder" => a with { PlaceholderText = "choose" },
            "header" => a with { Header = "h2" },
            "editable" => a with { IsEditable = true },
            _ => throw new global::System.InvalidOperationException(),
        };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void ComboBoxElement_DifferentItemsArray_StillEqual_ButOwnPropsUnchanged()
    {
        // Items is intentionally not in OwnPropsEqual — a fresh items array
        // every render (a common authoring mistake) must NOT make the combo
        // box flash. Items are diffed separately as children.
        var a = new ComboBoxElement(new[] { "a", "b" });
        var b = new ComboBoxElement(new[] { "a", "b" });
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Theory]
    [InlineData("selectedIndex")]
    [InlineData("selectionMode")]
    [InlineData("header")]
    public void ListViewElement_DifferingProp_NotEqual(string which)
    {
        var items = new Element[] { new EmptyElement() };
        var a = new ListViewElement(items) { SelectedIndex = 0, SelectionMode = ListViewSelectionMode.Single, Header = "h" };
        var b = which switch
        {
            "selectedIndex" => a with { SelectedIndex = 1 },
            "selectionMode" => a with { SelectionMode = ListViewSelectionMode.Multiple },
            "header" => a with { Header = "h2" },
            _ => throw new global::System.InvalidOperationException(),
        };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void ListViewElement_SameOwnProps_AreEqual()
    {
        var a = new ListViewElement(new Element[] { new EmptyElement() });
        var b = new ListViewElement(new Element[] { new EmptyElement() });
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    [Theory]
    [InlineData("selectedIndex")]
    [InlineData("selectionMode")]
    [InlineData("header")]
    public void GridViewElement_DifferingProp_NotEqual(string which)
    {
        var items = new Element[] { new EmptyElement() };
        var a = new GridViewElement(items) { SelectedIndex = 0, SelectionMode = ListViewSelectionMode.Single, Header = "h" };
        var b = which switch
        {
            "selectedIndex" => a with { SelectedIndex = 1 },
            "selectionMode" => a with { SelectionMode = ListViewSelectionMode.Multiple },
            "header" => a with { Header = "h2" },
            _ => throw new global::System.InvalidOperationException(),
        };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void FlipViewElement_DifferentSelectedIndex_NotEqual()
    {
        var items = new Element[] { new EmptyElement(), new EmptyElement() };
        var a = new FlipViewElement(items) { SelectedIndex = 0 };
        var b = a with { SelectedIndex = 1 };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Theory]
    [InlineData("selectedIndex")]
    [InlineData("title")]
    public void PivotElement_DifferingProp_NotEqual(string which)
    {
        var items = new[] { new PivotItemData("h", new EmptyElement()) };
        var a = new PivotElement(items) { SelectedIndex = 0, Title = "t" };
        var b = which switch
        {
            "selectedIndex" => a with { SelectedIndex = 1 },
            "title" => a with { Title = "t2" },
            _ => throw new global::System.InvalidOperationException(),
        };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Theory]
    [InlineData("selectedIndex")]
    [InlineData("addBtn")]
    public void TabViewElement_DifferingProp_NotEqual(string which)
    {
        var tabs = new[] { new TabViewItemData("h", new EmptyElement()) };
        var a = new TabViewElement(tabs) { SelectedIndex = 0, IsAddTabButtonVisible = false };
        var b = which switch
        {
            "selectedIndex" => a with { SelectedIndex = 1 },
            "addBtn" => a with { IsAddTabButtonVisible = true },
            _ => throw new global::System.InvalidOperationException(),
        };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Theory]
    [InlineData("selectionMode")]
    [InlineData("canDrag")]
    [InlineData("allowDrop")]
    [InlineData("canReorder")]
    public void TreeViewElement_DifferingProp_NotEqual(string which)
    {
        var nodes = new[] { new TreeViewNodeData("x") };
        var a = new TreeViewElement(nodes)
        {
            SelectionMode = TreeViewSelectionMode.Single,
            CanDragItems = false,
            AllowDrop = false,
            CanReorderItems = false,
        };
        var b = which switch
        {
            "selectionMode" => a with { SelectionMode = TreeViewSelectionMode.Multiple },
            "canDrag" => a with { CanDragItems = true },
            "allowDrop" => a with { AllowDrop = true },
            "canReorder" => a with { CanReorderItems = true },
            _ => throw new global::System.InvalidOperationException(),
        };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void SelectorBarElement_DifferentSelectedIndex_NotEqual()
    {
        var items = new[] { new SelectorBarItemData("a"), new SelectorBarItemData("b") };
        var a = new SelectorBarElement(items) { SelectedIndex = 0 };
        var b = a with { SelectedIndex = 1 };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void ListBoxElement_DifferentSelectedIndex_NotEqual()
    {
        var a = new ListBoxElement(["a", "b"]) { SelectedIndex = 0 };
        var b = a with { SelectedIndex = 1 };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Theory]
    [InlineData("selectedIndex")]
    [InlineData("header")]
    public void RadioButtonsElement_DifferingProp_NotEqual(string which)
    {
        var a = new RadioButtonsElement(["a", "b"], SelectedIndex: 0) { Header = "h" };
        var b = which switch
        {
            "selectedIndex" => a with { SelectedIndex = 1 },
            "header" => a with { Header = "h2" },
            _ => throw new global::System.InvalidOperationException(),
        };
        Assert.False(Element.OwnPropsEqual(a, b));
    }

    [Fact]
    public void BreadcrumbBarElement_OnlySettersMatter()
    {
        var a = new BreadcrumbBarElement(global::System.Array.Empty<BreadcrumbBarItemData>());
        var b = new BreadcrumbBarElement(new[] { new BreadcrumbBarItemData("a") });
        Assert.True(Element.OwnPropsEqual(a, b));
    }

    // ────────────────────────────────────────────────────────────────────
    // Fallback — leaf types are intentionally captured as "changed" so the
    // reconciler always re-applies their props (cheap path, safer default)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void LeafElement_FallsThroughToFalse()
    {
        // Bug this catches: a future edit accidentally adding ButtonElement
        // (or any leaf) to the switch arm — that's a real regression because
        // leaf updates need to push the label/IsChecked/SelectedDate to the
        // WinUI control on every render. The default-false fallback is
        // load-bearing for correctness.
        var a = new ButtonElement("Click");
        var b = new ButtonElement("Click");
        Assert.False(Element.OwnPropsEqual(a, b));
    }
}
