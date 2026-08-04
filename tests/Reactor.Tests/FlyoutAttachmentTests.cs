using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for the declarative flyout attachment system — ContentFlyout, MenuItems,
/// .WithFlyout(), .WithContextFlyout(), and .WithToolTip(Element).
/// These are pure C# record tests, no WinUI thread needed.
/// </summary>
public class FlyoutAttachmentTests
{
    // ════════════════════════════════════════════════════════════════
    //  DSL factory tests (pure record construction)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ContentFlyout_Creates_ContentFlyoutElement_With_Defaults()
    {
        var el = ContentFlyout(TextBlock("content"));
        Assert.IsType<ContentFlyoutElement>(el);
        Assert.Equal(FlyoutPlacementMode.Auto, el.Placement);
    }

    [Fact]
    public void ContentFlyout_With_Explicit_Placement()
    {
        var el = ContentFlyout(TextBlock("content"), placement: FlyoutPlacementMode.Bottom);
        Assert.Equal(FlyoutPlacementMode.Bottom, el.Placement);
    }

    [Fact]
    public void ContentFlyout_Content_Is_Preserved()
    {
        var inner = TextBlock("inner content");
        var el = ContentFlyout(inner);
        Assert.Same(inner, el.Content);
    }

    [Fact]
    public void MenuItems_Creates_MenuFlyoutContentElement()
    {
        var el = MenuItems(
            MenuItem("Item 1"),
            MenuItem("Item 2")
        );
        Assert.IsType<MenuFlyoutContentElement>(el);
        Assert.Equal(2, el.Items.Length);
    }

    [Fact]
    public void MenuItems_With_Placement()
    {
        var el = MenuItems(FlyoutPlacementMode.Top,
            MenuItem("Item 1")
        );
        Assert.Equal(FlyoutPlacementMode.Top, el.Placement);
    }

    [Fact]
    public void MenuItems_Preserves_Items_Array()
    {
        var item1 = MenuItem("One");
        var item2 = MenuItem("Two");
        var sep = MenuSeparator();
        var el = MenuItems(item1, sep, item2);
        Assert.Equal(3, el.Items.Length);
        Assert.Same(item1, el.Items[0]);
        Assert.Same(sep, el.Items[1]);
        Assert.Same(item2, el.Items[2]);
    }

    [Fact]
    public void ContentFlyoutElement_Record_Equality()
    {
        var a = ContentFlyout(TextBlock("x"), FlyoutPlacementMode.Bottom);
        var b = ContentFlyout(TextBlock("x"), FlyoutPlacementMode.Bottom);
        Assert.Equal(a, b);
    }

    [Fact]
    public void MenuFlyoutContentElement_Record_Equality()
    {
        var item = MenuItem("A");
        var a = new MenuFlyoutContentElement(new[] { (Microsoft.UI.Reactor.Core.MenuFlyoutItemBase)item });
        var b = new MenuFlyoutContentElement(new[] { (Microsoft.UI.Reactor.Core.MenuFlyoutItemBase)item });
        // Arrays are reference types so two different arrays won't be equal by default
        Assert.NotSame(a.Items, b.Items);
    }

    [Fact]
    public void ContentFlyoutElement_Is_Element_Subclass()
    {
        Element el = ContentFlyout(TextBlock("x"));
        Assert.IsAssignableFrom<Element>(el);
    }

    [Fact]
    public void MenuFlyoutContentElement_Is_Element_Subclass()
    {
        Element el = MenuItems(MenuItem("x"));
        Assert.IsAssignableFrom<Element>(el);
    }

    // ════════════════════════════════════════════════════════════════
    //  Modifier tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void WithFlyout_Sets_AttachedFlyout_On_Modifiers()
    {
        var flyout = ContentFlyout(TextBlock("content"));
        var el = Button("Click", null).WithFlyout(flyout);
        Assert.NotNull(el.Modifiers);
        Assert.NotNull(el.Modifiers!.AttachedFlyout);
        Assert.IsType<ContentFlyoutElement>(el.Modifiers.AttachedFlyout);
    }

    [Fact]
    public void WithContextFlyout_Sets_ContextFlyout_On_Modifiers()
    {
        var menu = MenuItems(MenuItem("Copy"), MenuItem("Paste"));
        var el = TextBlock("right-click me").WithContextFlyout(menu);
        Assert.NotNull(el.Modifiers);
        Assert.NotNull(el.Modifiers!.ContextFlyout);
        Assert.IsType<MenuFlyoutContentElement>(el.Modifiers.ContextFlyout);
    }

    [Fact]
    public void WithToolTip_Element_Sets_RichToolTip_On_Modifiers()
    {
        var tip = VStack(TextBlock("Title"), TextBlock("Description"));
        var el = Button("Hover me", null).WithToolTip(tip);
        Assert.NotNull(el.Modifiers);
        Assert.NotNull(el.Modifiers!.RichToolTip);
        Assert.IsType<StackElement>(el.Modifiers.RichToolTip);
    }

    [Fact]
    public void WithFlyout_Works_On_TextBlockElement()
    {
        var el = TextBlock("tap me").WithFlyout(ContentFlyout(TextBlock("popup")));
        Assert.IsType<TextBlockElement>(el);
        Assert.NotNull(el.Modifiers?.AttachedFlyout);
    }

    [Fact]
    public void WithFlyout_Works_On_ButtonElement()
    {
        var el = Button("Go", null).WithFlyout(ContentFlyout(TextBlock("popup")));
        Assert.IsType<ButtonElement>(el);
        Assert.NotNull(el.Modifiers?.AttachedFlyout);
    }

    [Fact]
    public void WithFlyout_Works_On_BorderElement()
    {
        var el = Border(TextBlock("inner")).WithFlyout(ContentFlyout(TextBlock("popup")));
        Assert.IsType<BorderElement>(el);
        Assert.NotNull(el.Modifiers?.AttachedFlyout);
    }

    [Fact]
    public void WithContextFlyout_Works_On_TextBlockElement()
    {
        var el = TextBlock("right-click").WithContextFlyout(MenuItems(MenuItem("Cut")));
        Assert.IsType<TextBlockElement>(el);
        Assert.NotNull(el.Modifiers?.ContextFlyout);
    }

    [Fact]
    public void WithContextFlyout_Works_On_ButtonElement()
    {
        var el = Button("Go", null).WithContextFlyout(MenuItems(MenuItem("Help")));
        Assert.IsType<ButtonElement>(el);
        Assert.NotNull(el.Modifiers?.ContextFlyout);
    }

    [Fact]
    public void WithContextFlyout_Works_On_BorderElement()
    {
        var el = Border(Empty()).WithContextFlyout(MenuItems(MenuItem("Refresh")));
        Assert.IsType<BorderElement>(el);
        Assert.NotNull(el.Modifiers?.ContextFlyout);
    }

    [Fact]
    public void RichToolTip_Does_Not_Interfere_With_String_ToolTip()
    {
        var el = Button("Hover", null)
            .ToolTip("simple string tip")
            .WithToolTip(VStack(TextBlock("rich"), TextBlock("tooltip")));

        Assert.NotNull(el.Modifiers);
        Assert.Equal("simple string tip", el.Modifiers!.ToolTip);
        Assert.NotNull(el.Modifiers.RichToolTip);
        Assert.IsType<StackElement>(el.Modifiers.RichToolTip);
    }

    [Fact]
    public void Modifier_Merge_Preserves_AttachedFlyout()
    {
        var flyout = ContentFlyout(TextBlock("content"));
        var mods1 = new ElementModifiers { AttachedFlyout = flyout };
        var mods2 = new ElementModifiers { Opacity = 0.5 };

        var merged = mods1.Merge(mods2);
        Assert.Same(flyout, merged.AttachedFlyout);
        Assert.Equal(0.5, merged.Opacity);
    }

    [Fact]
    public void Modifier_Merge_Preserves_ContextFlyout()
    {
        var menu = MenuItems(MenuItem("Action"));
        var mods1 = new ElementModifiers { ContextFlyout = menu };
        var mods2 = new ElementModifiers { Width = 100 };

        var merged = mods1.Merge(mods2);
        Assert.Same(menu, merged.ContextFlyout);
        Assert.Equal(100, merged.Width);
    }

    [Fact]
    public void Modifier_Merge_Preserves_RichToolTip()
    {
        var tip = TextBlock("rich tip");
        var mods1 = new ElementModifiers { RichToolTip = tip };
        var mods2 = new ElementModifiers { Height = 50 };

        var merged = mods1.Merge(mods2);
        Assert.Same(tip, merged.RichToolTip);
        Assert.Equal(50, merged.Height);
    }

    [Fact]
    public void Attachments_Compose_With_Other_Modifiers()
    {
        var el = TextBlock("styled")
            .Margin(16)
            .Opacity(0.8)
            .WithFlyout(ContentFlyout(TextBlock("popup")));

        Assert.NotNull(el.Modifiers);
        Assert.Equal(new Thickness(16), el.Modifiers!.Margin);
        Assert.Equal(0.8, el.Modifiers.Opacity);
        Assert.NotNull(el.Modifiers.AttachedFlyout);
    }

    [Fact]
    public void Multiple_Attachments_On_Same_Element()
    {
        var el = Border(TextBlock("full"))
            .WithFlyout(ContentFlyout(TextBlock("flyout")))
            .WithContextFlyout(MenuItems(MenuItem("Copy")))
            .WithToolTip(TextBlock("tip"));

        Assert.NotNull(el.Modifiers?.AttachedFlyout);
        Assert.NotNull(el.Modifiers?.ContextFlyout);
        Assert.NotNull(el.Modifiers?.RichToolTip);
    }

    // ════════════════════════════════════════════════════════════════
    //  Type matching tests (reconciler uses GetType() equality)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Same_ContentFlyoutElement_Types_Match()
    {
        var a = ContentFlyout(TextBlock("old"));
        var b = ContentFlyout(TextBlock("new"));
        Assert.Equal(a.GetType(), b.GetType());
    }

    [Fact]
    public void ContentFlyoutElement_And_MenuFlyoutContentElement_Do_Not_Match()
    {
        Element a = ContentFlyout(TextBlock("content"));
        Element b = MenuItems(MenuItem("item"));
        Assert.NotEqual(a.GetType(), b.GetType());
    }

    [Fact]
    public void Same_MenuFlyoutContentElement_Types_Match()
    {
        var a = MenuItems(MenuItem("old"));
        var b = MenuItems(MenuItem("new"));
        Assert.Equal(a.GetType(), b.GetType());
    }

    // ════════════════════════════════════════════════════════════════
    //  DropDownButton / SplitButton flyout slot tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DropDownButton_Accepts_MenuItems_As_Flyout()
    {
        var el = DropDownButton("Menu", flyout: MenuItems(
            MenuItem("A"),
            MenuItem("B")
        ));
        Assert.IsType<DropDownButtonElement>(el);
        Assert.NotNull(el.Flyout);
        Assert.IsType<MenuFlyoutContentElement>(el.Flyout);
    }

    [Fact]
    public void SplitButton_Accepts_ContentFlyout_As_Flyout()
    {
        var el = SplitButton("Action", () => { }, flyout: ContentFlyout(TextBlock("options")));
        Assert.IsType<SplitButtonElement>(el);
        Assert.NotNull(el.Flyout);
        Assert.IsType<ContentFlyoutElement>(el.Flyout);
    }

    [Fact]
    public void ContentFlyout_Default_Placement_Is_Auto()
    {
        var el = ContentFlyout(Empty());
        Assert.Equal(FlyoutPlacementMode.Auto, el.Placement);
    }

    [Fact]
    public void MenuFlyoutContentElement_Default_Placement_Is_Auto()
    {
        var el = MenuItems(MenuItem("x"));
        Assert.Equal(FlyoutPlacementMode.Auto, el.Placement);
    }

    // ════════════════════════════════════════════════════════════════
    //  Flyout slot resolution (Reconciler.ResolveFlyoutSlot)
    //
    //  Single source of truth shared by SetFlyoutOnControl (write) and
    //  GetFlyoutOnControl (read). A writer and a reader that disagree on the
    //  slot silently lose the flyout — that is exactly how CommandBarFlyout
    //  ended up installing into AttachedFlyout (which nothing ever opens)
    //  while Flyout/MenuFlyout used the Button.Flyout slot.
    //
    //  Type-only: no WinUI object is constructed, so these stay headless.
    // ════════════════════════════════════════════════════════════════

    [Theory]
    // Button family → the control's own Flyout property, which WinUI opens on click.
    [InlineData(typeof(Button), (int)Reconciler.FlyoutSlot.Button)]
    [InlineData(typeof(DropDownButton), (int)Reconciler.FlyoutSlot.Button)]
    [InlineData(typeof(AppBarButton), (int)Reconciler.FlyoutSlot.Button)]
    // SplitButton does NOT derive from Button and has its own Flyout property.
    [InlineData(typeof(SplitButton), (int)Reconciler.FlyoutSlot.SplitButton)]
    [InlineData(typeof(ToggleSplitButton), (int)Reconciler.FlyoutSlot.SplitButton)]
    // Everything else — including ButtonBase-derived types that have no Flyout
    // property — falls back to FlyoutBase.AttachedFlyout metadata.
    [InlineData(typeof(HyperlinkButton), (int)Reconciler.FlyoutSlot.Attached)]
    [InlineData(typeof(AppBarToggleButton), (int)Reconciler.FlyoutSlot.Attached)]
    [InlineData(typeof(TextBlock), (int)Reconciler.FlyoutSlot.Attached)]
    [InlineData(typeof(Border), (int)Reconciler.FlyoutSlot.Attached)]
    [InlineData(typeof(FrameworkElement), (int)Reconciler.FlyoutSlot.Attached)]
    // int, not FlyoutSlot: the enum is internal and an internal parameter type on a
    // public test method is CS0051.
    public void ResolveFlyoutSlot_Maps_TargetType_To_Slot(global::System.Type targetType, int expectedSlot)
    {
        Assert.Equal((Reconciler.FlyoutSlot)expectedSlot, Reconciler.ResolveFlyoutSlot(targetType));
    }

    [Fact]
    public void ResolveFlyoutSlot_Separates_Button_From_SplitButton()
    {
        // Differential: the two button families must NOT collapse onto the same
        // slot, or one of them gets written to a property the reader never checks.
        Assert.NotEqual(
            Reconciler.ResolveFlyoutSlot(typeof(Button)),
            Reconciler.ResolveFlyoutSlot(typeof(SplitButton)));
    }

    [Fact]
    public void ResolveFlyoutSlot_Does_Not_Put_Buttons_In_The_Attached_Slot()
    {
        // The attached slot only opens via an explicit ShowAttachedFlyout call.
        // A Button target landing there is the CommandBarFlyout "dead button" bug.
        Assert.NotEqual(Reconciler.FlyoutSlot.Attached, Reconciler.ResolveFlyoutSlot(typeof(Button)));
        Assert.NotEqual(Reconciler.FlyoutSlot.Attached, Reconciler.ResolveFlyoutSlot(typeof(SplitButton)));
    }

    [Fact]
    public void CommandBarFlyout_Does_Not_Open_Itself_By_Default()
    {
        // IsOpen is an explicit opt-in trigger — defaulting it true would pop every
        // CommandBarFlyout open on mount.
        Assert.False(CommandBarFlyout(Button("target", null)).IsOpen);
    }

    [Fact]
    public void CommandBarFlyout_IsOpen_Is_Settable_Via_With()
    {
        Assert.True((CommandBarFlyout(Button("target", null)) with { IsOpen = true }).IsOpen);
    }

    // ════════════════════════════════════════════════════════════════
    //  Deferred-open guard (OverlayLifecycle.IsStillRequestingOpen)
    //
    //  A mount-time IsOpen=true defers the show to the target's Loaded.
    //  Between those two points the element can be re-rendered with
    //  IsOpen=false, replaced by a different element, or unmounted (the
    //  pool clears the tag). The guard reads the target's *current* tag so
    //  a stale request never pops a flyout.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DeferredOpen_Honoured_While_Element_Still_Wants_Open()
    {
        Assert.True(OverlayLifecycle.IsStillRequestingOpen(
            CommandBarFlyout(Button("t", null)) with { IsOpen = true }));
    }

    [Fact]
    public void DeferredOpen_Dropped_When_Element_Rerendered_Closed()
    {
        Assert.False(OverlayLifecycle.IsStillRequestingOpen(
            CommandBarFlyout(Button("t", null)) with { IsOpen = false }));
    }

    [Fact]
    public void DeferredOpen_Dropped_When_Target_Untagged()
    {
        // Pool return calls ClearElementTag, so a recycled control reports no element.
        Assert.False(OverlayLifecycle.IsStillRequestingOpen(null));
    }

    [Fact]
    public void DeferredOpen_Dropped_When_Target_Now_Hosts_A_Different_Element()
    {
        // FlyoutElement.IsOpen is a different feature on a different element type —
        // it must not satisfy the CommandBarFlyout deferred-open guard.
        Assert.False(OverlayLifecycle.IsStillRequestingOpen(
            Flyout(Button("t", null), TextBlock("content")) with { IsOpen = true }));
    }

    // ════════════════════════════════════════════════════════════════
    //  ContentDialog deferred-open guard
    //  (OverlayLifecycle.ShouldStartDeferredDialog)
    //
    //  Same shape as the flyout guard above, plus an already-showing term:
    //  a placeholder tracks exactly one live dialog, and WinUI permits only
    //  one dialog per XamlRoot, so a second show would throw out of an
    //  async void after overwriting the tracking entry.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DeferredDialog_Honoured_While_Element_Still_Wants_Open()
    {
        Assert.True(OverlayLifecycle.ShouldStartDeferredDialog(
            ContentDialog("t", TextBlock("body")) with { IsOpen = true }, alreadyShowing: false));
    }

    [Fact]
    public void DeferredDialog_Dropped_When_Element_Rerendered_Closed()
    {
        Assert.False(OverlayLifecycle.ShouldStartDeferredDialog(
            ContentDialog("t", TextBlock("body")) with { IsOpen = false }, alreadyShowing: false));
    }

    [Fact]
    public void DeferredDialog_Dropped_When_Anchor_Untagged()
    {
        // Unmount clears the tag, so a pending Loaded reports no element.
        Assert.False(OverlayLifecycle.ShouldStartDeferredDialog(null, alreadyShowing: false));
    }

    [Fact]
    public void DeferredDialog_Dropped_When_Anchor_Now_Hosts_A_Different_Element()
    {
        // PopupElement.IsOpen is a different feature on a different element type.
        Assert.False(OverlayLifecycle.ShouldStartDeferredDialog(
            Popup(TextBlock("content")) with { IsOpen = true }, alreadyShowing: false));
    }

    [Fact]
    public void DeferredDialog_Dropped_When_A_Dialog_Is_Already_Showing()
    {
        // A rising edge that lands after the anchor gains a XamlRoot shows
        // directly while this deferral is still pending; the pending one must
        // not show a second dialog over it.
        Assert.False(OverlayLifecycle.ShouldStartDeferredDialog(
            ContentDialog("t", TextBlock("body")) with { IsOpen = true }, alreadyShowing: true));
    }
}
