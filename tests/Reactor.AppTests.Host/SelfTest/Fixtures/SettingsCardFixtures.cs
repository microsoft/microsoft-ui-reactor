using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// Spec 065 — SettingsCard / SettingsExpander against live WinUI controls: the ported template
// and slots, the clickable button behavior and its UI Automation, the width-driven layout
// states, Command cards, theme brushes and theme bindings, and the expander.

internal static class SettingsCardTestKit
{
    internal static T? Part<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is T match && match.Name == name) return match;
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++) queue.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
        return null;
    }

    internal static string? CurrentState(SettingsCardControl card, string group)
    {
        if (Part<FrameworkElement>(card, "PART_RootGrid") is not { } root) return null;
        foreach (var g in VisualStateManager.GetVisualStateGroups(root))
            if (g.Name == group) return g.CurrentState?.Name;
        return null;
    }

    internal static global::Windows.UI.Color? ColorOf(Brush? brush) => (brush as SolidColorBrush)?.Color;

    /// <summary>
    /// The color <c>{ThemeResource key}</c> takes on an element in <paramref name="host"/>: a
    /// probe border rendered next to the card, so it shares the card's theme. Never read the
    /// theme dictionaries from code for this: in a Light app that fixes WinUI's Dark entry to
    /// the Light value for the whole process (spec 065).
    /// </summary>
    internal static async Task<global::Windows.UI.Color?> ProbeAsync(WinUI.Panel host, string key)
    {
        var probe = new WinUI.Border
        {
            Width = 4,
            Height = 4,
            Style = (Style)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                $"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Border'><Setter Property='Background' Value='{{ThemeResource {key}}}'/></Style>"),
        };
        host.Children.Add(probe);
        await Harness.Render();
        var color = ColorOf(probe.Background);
        host.Children.Remove(probe);
        return color;
    }

    internal static AutomationPeer Peer(UIElement element) => FrameworkElementAutomationPeer.CreatePeerForElement(element);

    internal static void Noop() { }
}

/// <summary>Spec 065 — a card mounts the ported template, fills its parts, names its content
/// after the header, and reconciles header, description and content updates.</summary>
internal sealed class SettingsCardMountFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var reconciler = new Reconciler();
        int toggleCleanups = 0, comboCleanups = 0;
        var toggleElement = ToggleSwitch(true).OnUnmount(_ => toggleCleanups++);
        var current = SettingsCard("Notifications", description: "Alerts from apps and the system",
            icon: FontIcon("\uE7E7"), content: toggleElement);
        var card = (SettingsCardControl)reconciler.Mount(current, SettingsCardTestKit.Noop)!;
        var host = new WinUI.Grid { Width = 640 };
        host.Children.Add(card);
        H.SetContent(host);
        try
        {
            await Harness.Render();
            var header = SettingsCardTestKit.Part<WinUI.ContentPresenter>(card, "PART_HeaderPresenter");
            var description = SettingsCardTestKit.Part<WinUI.ContentPresenter>(card, "PART_DescriptionPresenter");
            var iconHolder = SettingsCardTestKit.Part<FrameworkElement>(card, "PART_HeaderIconPresenterHolder");
            var iconPresenter = SettingsCardTestKit.Part<WinUI.ContentPresenter>(card, "PART_HeaderIconPresenter");
            var actionHolder = SettingsCardTestKit.Part<FrameworkElement>(card, "PART_ActionIconPresenterHolder");
            var contentPresenter = SettingsCardTestKit.Part<WinUI.ContentPresenter>(card, "PART_ContentPresenter");
            var toggle = card.Content as WinUI.ToggleSwitch;

            H.Check("SettingsCard_Mount_TemplateParts",
                header is { Visibility: Visibility.Visible } && header.Content as string == "Notifications"
                && description is { Visibility: Visibility.Visible } && description.Content as string == "Alerts from apps and the system"
                && iconHolder is { Visibility: Visibility.Visible }
                && actionHolder is { Visibility: Visibility.Collapsed },
                $"header={header?.Content} description={description?.Content} icon={iconHolder?.Visibility} action={actionHolder?.Visibility}");
            H.Check("SettingsCard_Mount_IconResolved", iconPresenter?.Content is WinUI.FontIcon { Glyph: "\uE7E7" });
            H.Check("SettingsCard_Mount_ContentRightOfHeader",
                toggle is { ActualWidth: > 0 } && contentPresenter is not null
                && WinUI.Grid.GetColumn(contentPresenter) == 2 && WinUI.Grid.GetRow(contentPresenter) == 0
                && SettingsCardTestKit.CurrentState(card, "ContentAlignmentStates") == "Right"
                && SettingsCardTestKit.CurrentState(card, "ContentSpacingStates") == "NoContentSpacing",
                $"state={SettingsCardTestKit.CurrentState(card, "ContentAlignmentStates")}");
            H.Check("SettingsCard_Mount_ToolkitMetrics",
                card.Padding == new Thickness(16) && card.MinHeight == 68 && card.MinWidth == 148
                && card.BorderThickness == new Thickness(1) && card.ActualHeight >= 68
                && card.FocusVisualMargin == new Thickness(-3),
                $"padding={card.Padding} minHeight={card.MinHeight} height={card.ActualHeight}");
            H.Check("SettingsCard_Mount_CompactToggleSwitchStyle",
                toggle is { Height: 36, MinWidth: 0 } && toggle.HorizontalContentAlignment == HorizontalAlignment.Right,
                $"height={toggle?.Height} minWidth={toggle?.MinWidth}");
            var expectedBackground = await SettingsCardTestKit.ProbeAsync(host, "CardBackgroundFillColorDefaultBrush");
            var expectedBorder = await SettingsCardTestKit.ProbeAsync(host, "CardStrokeColorDefaultBrush");
            var expectedForeground = await SettingsCardTestKit.ProbeAsync(host, "TextFillColorPrimaryBrush");
            H.Check("SettingsCard_Mount_ThemeBrushes",
                expectedBackground is not null
                && SettingsCardTestKit.ColorOf(card.Background) == expectedBackground
                && SettingsCardTestKit.ColorOf(card.BorderBrush) == expectedBorder
                && SettingsCardTestKit.ColorOf(card.Foreground) == expectedForeground,
                $"background={SettingsCardTestKit.ColorOf(card.Background)} expected={expectedBackground} theme={card.ActualTheme}");

            var peer = SettingsCardTestKit.Peer(card);
            H.Check("SettingsCard_Mount_PeerIsUnnamedGroup",
                peer.GetAutomationControlType() == AutomationControlType.Group && string.IsNullOrEmpty(peer.GetName())
                && peer.GetClassName() == "SettingsCard" && peer.GetPattern(PatternInterface.Invoke) is null,
                $"type={peer.GetAutomationControlType()} name='{peer.GetName()}'");
            H.Check("SettingsCard_Mount_NotATabStop", !card.IsTabStop);
            H.Check("SettingsCard_Mount_ContentNamedAfterHeader", toggle is not null && AutomationProperties.GetName(toggle) == "Notifications");

            await Update(current with { Header = "Alerts" });
            H.Check("SettingsCard_Update_HeaderAndContentNameFollow",
                header?.Content as string == "Alerts" && ReferenceEquals(card.Content, toggle)
                && AutomationProperties.GetName(toggle!) == "Alerts" && toggleCleanups == 0);

            await Update(current with { Header = "Alerts", Content = toggleElement.AutomationName("Custom name") });
            H.Check("SettingsCard_Update_AuthoredContentNameWins",
                ReferenceEquals(card.Content, toggle) && AutomationProperties.GetName(toggle!) == "Custom name");

            await Update(current with { Header = "Alerts", Description = null });
            H.Check("SettingsCard_Update_DescriptionCollapses", description?.Visibility == Visibility.Collapsed);

            await Update(current with
            {
                Header = "Alerts",
                Description = null,
                Content = ComboBox(["Off", "On"], 1).OnUnmount(_ => comboCleanups++),
            });
            var combo = card.Content as WinUI.ComboBox;
            H.Check("SettingsCard_Update_ContentReplaced",
                combo is not null && toggleCleanups == 1 && AutomationProperties.GetName(combo) == "Alerts"
                && combo.MinWidth == 120,
                $"content={card.Content?.GetType().Name} toggleCleanups={toggleCleanups} minWidth={combo?.MinWidth}");

            await Update(current with { Header = "Alerts", Description = null, Content = null });
            H.Check("SettingsCard_Update_NoContentCollapsesPresenter",
                card.Content is null && comboCleanups == 1
                && SettingsCardTestKit.CurrentState(card, "ContentVisibilityStates") == "Collapsed");

            await Update(current with { Header = "Alerts", Content = ComboBox(["Off", "On"], 0).OnUnmount(_ => comboCleanups++) });
        }
        finally
        {
            reconciler.UnmountChild(card);
            H.SetContent(null);
        }
        H.Check("SettingsCard_Unmount_TearsDownContent", comboCleanups == 2);

        // An element without callbacks or a stamped call site is not tagged by the engine; the
        // handler tags it anyway, or unmount would never reach the slots the generic walk
        // cannot see (an element header lives inside a template part).
        int headerCleanups = 0;
        var bare = new SettingsCardElement(null) { HeaderContent = TextBlock("Rich header").Bold().OnUnmount(_ => headerCleanups++) };
        var bareCard = (SettingsCardControl)reconciler.Mount(bare, SettingsCardTestKit.Noop)!;
        var bareHost = new WinUI.Grid { Width = 640 };
        bareHost.Children.Add(bareCard);
        H.SetContent(bareHost);
        await Harness.Render();
        var mounted = bareCard.HeaderChild is WinUI.TextBlock { ActualWidth: > 0 };
        var taggedBefore = Reconciler.GetElementTag(bareCard) is SettingsCardElement;
        var headerChild = bareCard.HeaderChild;
        reconciler.UnmountChild(bareCard);
        H.SetContent(null);
        H.Check("SettingsCard_Unmount_UntaggedElementSlots", bare.Extensions is null && !bare.HasCallbacks && mounted && headerCleanups == 1,
            $"extensions={bare.Extensions is not null} mounted={mounted} cleanups={headerCleanups} tagged={taggedBefore} childTag={(headerChild as FrameworkElement) is { } hf && Reconciler.GetElementTag(hf) is not null}");

        async Task Update(SettingsCardElement next)
        {
            reconciler.Reconcile(current, next, card, SettingsCardTestKit.Noop);
            current = next;
            await Harness.Render();
        }
    }
}

/// <summary>Spec 065 — a card with <c>onClick</c> is a focusable button with a chevron that
/// UI Automation can invoke; dropping <c>onClick</c> turns it back into a group.</summary>
internal sealed class SettingsCardClickableFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var reconciler = new Reconciler();
        int clicks = 0, laterClicks = 0;
        var current = SettingsCard("Storage", description: "Free up space and manage drives",
            icon: FontIcon("\uEDA2"), onClick: () => clicks++);
        var card = (SettingsCardControl)reconciler.Mount(current, SettingsCardTestKit.Noop)!;
        var host = new WinUI.Grid { Width = 640 };
        host.Children.Add(card);
        H.SetContent(host);
        try
        {
            await Harness.Render();
            var actionHolder = SettingsCardTestKit.Part<FrameworkElement>(card, "PART_ActionIconPresenterHolder");
            var actionPresenter = SettingsCardTestKit.Part<WinUI.ContentPresenter>(card, "PART_ActionIconPresenter");
            H.Check("SettingsCard_Clickable_ChevronAndTabStop",
                card.IsTabStop && actionHolder is { Visibility: Visibility.Visible, ActualWidth: > 0 }
                && actionPresenter?.Content is WinUI.FontIcon { Glyph: "\uE974", MirroredWhenRightToLeft: true });

            var peer = SettingsCardTestKit.Peer(card);
            var invoke = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
            H.Check("SettingsCard_Clickable_PeerIsButtonNamedFromHeader",
                peer.GetAutomationControlType() == AutomationControlType.Button && peer.GetName() == "Storage"
                && peer.IsKeyboardFocusable() && invoke is not null,
                $"type={peer.GetAutomationControlType()} name='{peer.GetName()}' focusable={peer.IsKeyboardFocusable()}");
            invoke?.Invoke();
            H.Check("SettingsCard_Clickable_InvokeRunsOnClick", clicks == 1);

            await Update(current with { OnClick = () => laterClicks++ });
            (SettingsCardTestKit.Peer(card).GetPattern(PatternInterface.Invoke) as IInvokeProvider)?.Invoke();
            H.Check("SettingsCard_Clickable_InvokeUsesLatestCallback", clicks == 1 && laterClicks == 1);

            await Update(current.ActionIcon(FontIcon("\uE8A7")).ActionIconToolTip("Open"));
            H.Check("SettingsCard_Clickable_CustomActionIcon",
                actionPresenter?.Content is WinUI.FontIcon { Glyph: "\uE8A7" }
                && WinUI.ToolTipService.GetToolTip(actionPresenter) as string == "Open");

            await Update(current.ActionIcon(null).ActionIconVisible(false));
            H.Check("SettingsCard_Clickable_ActionIconHidden",
                actionHolder?.Visibility == Visibility.Collapsed && card.IsTabStop
                && actionPresenter?.Content is WinUI.FontIcon { Glyph: "\uE974" });

            await Update(current.Disabled());
            var disabledInvoke = SettingsCardTestKit.Peer(card).GetPattern(PatternInterface.Invoke) as IInvokeProvider;
            var threw = false;
            try { disabledInvoke?.Invoke(); }
            catch (Exception) { threw = true; }
            H.Check("SettingsCard_Clickable_DisabledInvokeRefused",
                !card.IsEnabled && threw && laterClicks == 1
                && SettingsCardTestKit.CurrentState(card, "CommonStates") == "Disabled");
            H.Check("SettingsCard_Clickable_DisabledKeepsBackground",
                SettingsCardTestKit.ColorOf(card.Background) == await SettingsCardTestKit.ProbeAsync(host, "CardBackgroundFillColorDefaultBrush"));

            await Update(current with { OnClick = null });
            var groupPeer = SettingsCardTestKit.Peer(card);
            H.Check("SettingsCard_Clickable_RemovingOnClickMakesAGroup",
                !card.IsTabStop && actionHolder?.Visibility == Visibility.Collapsed
                && groupPeer.GetAutomationControlType() == AutomationControlType.Group
                && groupPeer.GetPattern(PatternInterface.Invoke) is null && string.IsNullOrEmpty(groupPeer.GetName()));
        }
        finally
        {
            reconciler.UnmountChild(card);
            H.SetContent(null);
        }

        async Task Update(SettingsCardElement next)
        {
            reconciler.Reconcile(current, next, card, SettingsCardTestKit.Noop);
            current = next;
            await Harness.Render();
        }
    }
}

/// <summary>Spec 065 — the Toolkit's wrap thresholds (476 and 286 pixels) and the
/// <c>Vertical</c> and <c>Left</c> alignments move the content and hide parts.</summary>
internal sealed class SettingsCardLayoutFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var reconciler = new Reconciler();
        var current = SettingsCard("Theme", description: "Select which app theme to display",
            icon: FontIcon("\uE790"), content: ComboBox(["Light", "Dark", "Use system setting"], 2));
        var card = (SettingsCardControl)reconciler.Mount(current, SettingsCardTestKit.Noop)!;
        var host = new WinUI.Grid { Width = 640, HorizontalAlignment = HorizontalAlignment.Left };
        host.Children.Add(card);
        H.SetContent(host);
        try
        {
            await Harness.Render();
            var content = SettingsCardTestKit.Part<WinUI.ContentPresenter>(card, "PART_ContentPresenter")!;
            var iconHolder = SettingsCardTestKit.Part<FrameworkElement>(card, "PART_HeaderIconPresenterHolder")!;
            var header = SettingsCardTestKit.Part<FrameworkElement>(card, "PART_HeaderPresenter")!;
            var root = SettingsCardTestKit.Part<WinUI.Grid>(card, "PART_RootGrid")!;
            H.Check("SettingsCard_Layout_WideIsRight",
                SettingsCardTestKit.CurrentState(card, "ContentAlignmentStates") == "Right"
                && WinUI.Grid.GetRow(content) == 0 && root.RowSpacing == 0);

            host.Width = 400;
            await Harness.Render();
            H.Check("SettingsCard_Layout_NarrowWraps",
                SettingsCardTestKit.CurrentState(card, "ContentAlignmentStates") == "RightWrapped"
                && WinUI.Grid.GetRow(content) == 1 && WinUI.Grid.GetColumn(content) == 1
                && content.HorizontalContentAlignment == HorizontalAlignment.Left
                && iconHolder.Visibility == Visibility.Visible && root.RowSpacing == 8,
                $"state={SettingsCardTestKit.CurrentState(card, "ContentAlignmentStates")} row={WinUI.Grid.GetRow(content)} spacing={root.RowSpacing}");

            host.Width = 260;
            await Harness.Render();
            H.Check("SettingsCard_Layout_ExtraNarrowHidesIcon",
                SettingsCardTestKit.CurrentState(card, "ContentAlignmentStates") == "RightWrappedNoIcon"
                && WinUI.Grid.GetRow(content) == 1 && iconHolder.Visibility == Visibility.Collapsed);

            host.Width = 640;
            await Update(current.ContentAlignment(SettingsCardContentAlignment.Vertical));
            H.Check("SettingsCard_Layout_Vertical",
                SettingsCardTestKit.CurrentState(card, "ContentAlignmentStates") == "Vertical"
                && WinUI.Grid.GetRow(content) == 1 && content.HorizontalContentAlignment == HorizontalAlignment.Right
                && root.RowSpacing == 8 && iconHolder.Visibility == Visibility.Visible);

            host.Width = 400;
            await Harness.Render();
            H.Check("SettingsCard_Layout_NarrowWinsOverVertical",
                SettingsCardTestKit.CurrentState(card, "ContentAlignmentStates") == "RightWrapped"
                && content.HorizontalContentAlignment == HorizontalAlignment.Left);

            host.Width = 640;
            await Update(current.ContentAlignment(SettingsCardContentAlignment.Left));
            H.Check("SettingsCard_Layout_LeftHidesHeader",
                SettingsCardTestKit.CurrentState(card, "ContentAlignmentStates") == "Left"
                && header.Visibility == Visibility.Collapsed && iconHolder.Visibility == Visibility.Collapsed
                && WinUI.Grid.GetRow(content) == 1 && content.HorizontalAlignment == HorizontalAlignment.Left);

            await Update(current with { Description = null, Header = null, ContentAlignment = SettingsCardContentAlignment.Vertical });
            H.Check("SettingsCard_Layout_NoHeaderNoSpacing",
                SettingsCardTestKit.CurrentState(card, "ContentSpacingStates") == "NoContentSpacing" && root.RowSpacing == 0);

            card.FlowDirection = FlowDirection.RightToLeft;
            await Update(SettingsCard("Storage", onClick: () => { }));
            var chevron = SettingsCardTestKit.Part<WinUI.ContentPresenter>(card, "PART_ActionIconPresenter")?.Content as WinUI.FontIcon;
            H.Check("SettingsCard_Layout_RightToLeftMirrorsChevron",
                chevron is { MirroredWhenRightToLeft: true } && card.FlowDirection == FlowDirection.RightToLeft);
        }
        finally
        {
            reconciler.UnmountChild(card);
            H.SetContent(null);
        }

        async Task Update(SettingsCardElement next)
        {
            reconciler.Reconcile(current, next, card, SettingsCardTestKit.Noop);
            current = next;
            await Harness.Render();
        }
    }
}

/// <summary>Spec 065 — <c>SettingsCard(Command)</c> maps the label, description, icon, action
/// and enabled state; rich element headers render; a plain text header renders as text.</summary>
internal sealed class SettingsCardCommandFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var reconciler = new Reconciler();
        var runs = 0;
        var command = new Command
        {
            Label = "Sync now",
            Description = "Last synced 5 minutes ago",
            Icon = FontIcon("\uE895"),
            Execute = () => runs++,
        };
        var current = SettingsCard(command);
        var card = (SettingsCardControl)reconciler.Mount(current, SettingsCardTestKit.Noop)!;
        var host = new WinUI.Grid { Width = 640 };
        host.Children.Add(card);
        H.SetContent(host);
        try
        {
            await Harness.Render();
            var header = SettingsCardTestKit.Part<WinUI.ContentPresenter>(card, "PART_HeaderPresenter");
            var description = SettingsCardTestKit.Part<WinUI.ContentPresenter>(card, "PART_DescriptionPresenter");
            var icon = SettingsCardTestKit.Part<WinUI.ContentPresenter>(card, "PART_HeaderIconPresenter");
            H.Check("SettingsCard_Command_MapsLabelDescriptionIcon",
                header?.Content as string == "Sync now" && description?.Content as string == "Last synced 5 minutes ago"
                && icon?.Content is WinUI.FontIcon { Glyph: "\uE895" } && card.IsTabStop);
            (SettingsCardTestKit.Peer(card).GetPattern(PatternInterface.Invoke) as IInvokeProvider)?.Invoke();
            H.Check("SettingsCard_Command_InvokeRunsCommand", runs == 1);

            await Update(SettingsCard(command with { CanExecute = false }));
            H.Check("SettingsCard_Command_CannotRunDisables", !card.IsEnabled);
            await Update(SettingsCard(command));
            H.Check("SettingsCard_Command_CanRunAgainEnables", card.IsEnabled);
            await Update(SettingsCard(command with { Execute = null }));
            H.Check("SettingsCard_Command_NoActionIsNotClickable",
                !card.IsTabStop && SettingsCardTestKit.Peer(card).GetAutomationControlType() == AutomationControlType.Group);
            await Update(SettingsCard("Plain", content: ToggleSwitch(false)));
            H.Check("SettingsCard_Command_DroppingCommandRestoresEnabled", card.IsEnabled);

            await Update(SettingsCard(TextBlock("Plain text header"),
                description: HyperlinkButton("Learn more", new Uri("https://learn.microsoft.com/windows/apps/")),
                content: ToggleSwitch(false)));
            H.Check("SettingsCard_Element_PlainTextHeaderRendersAsText",
                header?.Content as string == "Plain text header"
                && description?.Content is WinUI.HyperlinkButton { ActualWidth: > 0 }
                && AutomationProperties.GetName((UIElement)card.Content) == "Plain text header");
            await Update(SettingsCard(TextBlock("Bold header").Bold(), content: ToggleSwitch(false)));
            H.Check("SettingsCard_Element_StyledHeaderMountsAsElement",
                header?.Content is WinUI.TextBlock { Text: "Bold header" }
                && string.IsNullOrEmpty(AutomationProperties.GetName((UIElement)card.Content)));
        }
        finally
        {
            reconciler.UnmountChild(card);
            H.SetContent(null);
        }

        async Task Update(SettingsCardElement next)
        {
            reconciler.Reconcile(current, next, card, SettingsCardTestKit.Noop);
            current = next;
            await Harness.Render();
        }
    }
}

/// <summary>Spec 065 — the card follows an ancestor's theme, and a theme-token modifier
/// restyles it without losing the card's look: the themed style is built on the base style.</summary>
internal sealed class SettingsCardThemeFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var reconciler = new Reconciler();
        var plain = SettingsCard("Notifications", content: ToggleSwitch(true));
        var current = plain;
        var card = (SettingsCardControl)reconciler.Mount(current, SettingsCardTestKit.Noop)!;
        var host = new WinUI.Grid { Width = 640, RequestedTheme = ElementTheme.Light };
        host.Children.Add(card);
        H.SetContent(host);
        try
        {
            await Harness.Render();
            var light = SettingsCardTestKit.ColorOf(card.Background);
            H.Check("SettingsCard_Theme_LightBrush", light is not null
                && light == await SettingsCardTestKit.ProbeAsync(host, "CardBackgroundFillColorDefaultBrush"));
            host.RequestedTheme = ElementTheme.Dark;
            await Harness.Render();
            var dark = SettingsCardTestKit.ColorOf(card.Background);
            var darkExpected = await SettingsCardTestKit.ProbeAsync(host, "CardBackgroundFillColorDefaultBrush");
            H.Check("SettingsCard_Theme_DarkBrushFollowsAncestor", dark is not null && dark != light && dark == darkExpected,
                $"light={light} dark={dark} expected={darkExpected}");

            await Update(current.Background(Theme.SolidBackground));
            var solidExpected = await SettingsCardTestKit.ProbeAsync(host, "SolidBackgroundFillColorBaseBrush");
            H.Check("SettingsCard_Theme_TokenModifierKeepsCardLook",
                card.Style?.BasedOn == SettingsCardResources.Standard.CardStyle
                && ReferenceEquals(card.Template, SettingsCardResources.Standard.CardTemplate)
                && SettingsCardTestKit.Part<FrameworkElement>(card, "PART_RootGrid") is not null
                && card.Padding == new Thickness(16) && card.MinHeight == 68
                && solidExpected is not null && SettingsCardTestKit.ColorOf(card.Background) == solidExpected,
                $"basedOn={card.Style?.BasedOn is not null} padding={card.Padding} background={SettingsCardTestKit.ColorOf(card.Background)} expected={solidExpected}");

            await Update(plain);
            H.Check("SettingsCard_Theme_DroppingTokenRestoresBaseStyle",
                ReferenceEquals(card.Style, SettingsCardResources.Standard.CardStyle) && dark == SettingsCardTestKit.ColorOf(card.Background));

            // A contrast theme switches the card to the contrast markup on the next reconcile
            // (a contrast change triggers a full one), with the Toolkit's contrast brushes.
            HighContrastState.ReaderForTest = static () => true;
            HighContrastState.Invalidate();
            await Update(plain with { Description = "In a contrast theme" });
            var contrastBackground = await SettingsCardTestKit.ProbeAsync(host, "SystemColorButtonFaceColorBrush");
            var contrastBorder = await SettingsCardTestKit.ProbeAsync(host, "SystemColorButtonTextColorBrush");
            H.Check("SettingsCard_Theme_ContrastSwitchesMarkup",
                card.IsHighContrast
                && ReferenceEquals(card.Template, SettingsCardResources.Contrast.CardTemplate)
                && ReferenceEquals(card.Style, SettingsCardResources.Contrast.CardStyle)
                && SettingsCardTestKit.ColorOf(card.Background) == contrastBackground
                && SettingsCardTestKit.ColorOf(card.BorderBrush) == contrastBorder
                && SettingsCardTestKit.Part<WinUI.ContentPresenter>(card, "PART_DescriptionPresenter")?.Content as string == "In a contrast theme"
                && SettingsCardTestKit.Part<WinUI.ContentPresenter>(card, "PART_HeaderPresenter")?.Content as string == "Notifications"
                && card.Content is WinUI.ToggleSwitch { ActualWidth: > 0 },
                $"contrast={card.IsHighContrast} background={SettingsCardTestKit.ColorOf(card.Background)} expected={contrastBackground}");

            HighContrastState.ReaderForTest = null;
            HighContrastState.Invalidate();
            await Update(plain);
            H.Check("SettingsCard_Theme_ContrastOffRestoresMarkup",
                !card.IsHighContrast && ReferenceEquals(card.Template, SettingsCardResources.Standard.CardTemplate)
                && SettingsCardTestKit.ColorOf(card.Background) == dark);
        }
        finally
        {
            HighContrastState.ReaderForTest = null;
            HighContrastState.ResetForTest();
            reconciler.UnmountChild(card);
            H.SetContent(null);
        }
        async Task Update(SettingsCardElement next)
        {
            reconciler.Reconcile(current, next, card, SettingsCardTestKit.Noop);
            current = next;
            await Harness.Render();
        }
    }
}

/// <summary>Spec 065 — the expander hosts a header card, controls its expanded state, styles
/// card items as expander items, reconciles keyed items, and is a group named after its header.</summary>
internal sealed class SettingsExpanderFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var reconciler = new Reconciler();
        var changes = new List<bool>();
        int headerContentCleanups = 0, itemCleanups = 0;
        Element[] Items(params string[] keys) => [.. keys.Select(k =>
            SettingsCard(k, content: ToggleSwitch(false)).WithKey(k).OnUnmount(_ => itemCleanups++))];
        var current = SettingsExpander("Display", description: "Brightness, night light and scale",
            icon: FontIcon("\uE7F4"),
            content: ComboBox(["100%", "125%", "150%"], 1).OnUnmount(_ => headerContentCleanups++),
            isExpanded: false, onIsExpandedChanged: changes.Add,
            items: Items("Brightness", "Night light"));
        var expander = (SettingsExpanderControl)reconciler.Mount(current, SettingsCardTestKit.Noop)!;
        var host = new WinUI.Grid { Width = 640 };
        host.Children.Add(expander);
        H.SetContent(host);
        try
        {
            await Harness.Render();
            var inner = expander.Content as WinUI.Expander;
            var headerCard = expander.HeaderCard;
            var combo = headerCard.Content as WinUI.ComboBox;
            H.Check("SettingsExpander_Mount_HeaderCard",
                inner is not null && ReferenceEquals(inner.Header, headerCard) && !inner.IsExpanded
                && headerCard.HeaderText == "Display" && headerCard.DescriptionText == "Brightness, night light and scale"
                && headerCard.HeaderIcon is WinUI.FontIcon && !headerCard.IsClickEnabled && !headerCard.IsTabStop
                && combo is { ActualWidth: > 0 } && AutomationProperties.GetName(combo) == "Display");
            H.Check("SettingsExpander_Mount_SizesMirrored",
                inner is not null && inner.MinWidth == 148 && inner.MinHeight == 68 && headerCard.Padding == new Thickness(16, 16, 4, 16)
                && inner.CornerRadius == expander.CornerRadius && expander.ActualWidth == 640,
                $"minWidth={inner?.MinWidth} width={expander.ActualWidth}");

            var peer = SettingsCardTestKit.Peer(expander);
            H.Check("SettingsExpander_Mount_PeerIsGroupNamedFromHeader",
                peer.GetAutomationControlType() == AutomationControlType.Group && peer.GetName() == "Display"
                && peer.GetClassName() == "SettingsExpander"
                && inner is not null && AutomationProperties.GetName(inner) == "Display");
            var toggle = inner is null ? null : SettingsCardTestKit.Peer(inner).GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider;
            H.Check("SettingsExpander_Mount_InnerExpanderExpandCollapse", toggle?.ExpandCollapseState == ExpandCollapseState.Collapsed);

            await Update(current with { IsExpanded = true });
            var items = H.FindAllControls<SettingsCardControl>(c => !ReferenceEquals(c, headerCard));
            H.Check("SettingsExpander_Controlled_ExpandFromState",
                inner?.IsExpanded == true && changes.Count == 0 && items.Count == 2,
                $"expanded={inner?.IsExpanded} changes={changes.Count} items={items.Count}");
            H.Check("SettingsExpander_Items_TakeItemStyle",
                items.Count == 2 && items.All(c => c.IsExpanderItem
                    && ReferenceEquals(c.Style, SettingsCardResources.Standard.ExpanderItemStyle)
                    && c.Padding == new Thickness(58, 8, 44, 8) && c.BorderThickness == new Thickness(0, 1, 0, 0)
                    && c.CornerRadius == new CornerRadius(0) && c.MinHeight == 52),
                string.Join(";", items.Select(c => $"{c.HeaderText}:{c.Padding}")));

            if (inner is not null) inner.IsExpanded = false;
            await Harness.Render();
            H.Check("SettingsExpander_Controlled_UserCollapseReported", changes.SequenceEqual([false]), string.Join(",", changes));

            var firstRow = items.FirstOrDefault(c => c.HeaderText == "Brightness");
            await Update(current with
            {
                IsExpanded = true,
                Items = [.. Items("Night light", "Brightness"),
                    SettingsCard("Advanced display", onClick: () => { }).WithKey("Advanced")],
            });
            var reordered = H.FindAllControls<SettingsCardControl>(c => !ReferenceEquals(c, headerCard));
            var advanced = reordered.FirstOrDefault(c => c.HeaderText == "Advanced display");
            H.Check("SettingsExpander_Items_KeyedReorderPreservesControls",
                inner?.IsExpanded == true && changes.Count == 1 && itemCleanups == 0 && reordered.Count == 3
                && ReferenceEquals(reordered.FirstOrDefault(c => c.HeaderText == "Brightness"), firstRow),
                $"count={reordered.Count} cleanups={itemCleanups}");
            H.Check("SettingsExpander_Items_ClickableItemStyle",
                advanced is not null && ReferenceEquals(advanced.Style, SettingsCardResources.Standard.ExpanderClickableItemStyle)
                && advanced.Padding == new Thickness(58, 8, 16, 8) && advanced.IsTabStop);

            await Update((current with { IsExpanded = true, Items = Items("Brightness") })
                .ItemsHeader(TextBlock("Items header")).ItemsFooter(TextBlock("Items footer")));
            H.Check("SettingsExpander_Items_HeaderFooterAndRemoval",
                H.FindText("Items header") is { ActualHeight: > 0 } && H.FindText("Items footer") is { ActualHeight: > 0 }
                && itemCleanups == 1,
                $"cleanups={itemCleanups}");

            HighContrastState.ReaderForTest = static () => true;
            HighContrastState.Invalidate();
            await Update(current with { IsExpanded = true });
            H.Check("SettingsExpander_Contrast_SwitchesMarkup",
                inner is not null && ReferenceEquals(inner.Style, SettingsCardResources.Contrast.ExpanderExpanderStyle)
                && expander.HeaderCard.IsHighContrast && inner.IsExpanded
                && H.FindAllControls<SettingsCardControl>(c => !ReferenceEquals(c, headerCard)).All(c => c.IsHighContrast
                    && ReferenceEquals(c.Style, SettingsCardResources.Contrast.ExpanderItemStyle)));
            HighContrastState.ReaderForTest = null;
            HighContrastState.Invalidate();
            // A fresh element: the contrast refresh pass bypasses the identical-element skip,
            // a plain Reconcile does not.
            await Update(current with { });
            H.Check("SettingsExpander_Contrast_OffRestoresMarkup",
                inner is not null && ReferenceEquals(inner.Style, SettingsCardResources.Standard.ExpanderExpanderStyle)
                && !expander.HeaderCard.IsHighContrast);
            await Update(current with
            {
                Header = "Screen",
                IsExpanded = true,
                Items = Items("Brightness"),
                ItemsHeader = null,
                ItemsFooter = null,
            });
            H.Check("SettingsExpander_Update_HeaderRenamesGroupAndContent",
                SettingsCardTestKit.Peer(expander).GetName() == "Screen" && inner is not null
                && AutomationProperties.GetName(inner) == "Screen"
                && combo is not null && AutomationProperties.GetName(combo) == "Screen"
                && H.FindText("Items header") is null);
        }
        finally
        {
            HighContrastState.ReaderForTest = null;
            HighContrastState.ResetForTest();
            reconciler.UnmountChild(expander);
            H.SetContent(null);
        }
        H.Check("SettingsExpander_Unmount_TearsDownSlots", headerContentCleanups == 1 && itemCleanups == 2,
            $"header={headerContentCleanups} items={itemCleanups}");

        // As for the card: an untagged element's slots (here the items footer, inside the
        // expander's own body grid) are torn down only because the handler tags the control.
        int footerCleanups = 0;
        var bare = new SettingsExpanderElement("Bare") { ItemsFooter = TextBlock("Footer").Bold().OnUnmount(_ => footerCleanups++) };
        var bareExpander = (SettingsExpanderControl)reconciler.Mount(bare, SettingsCardTestKit.Noop)!;
        var bareHost = new WinUI.Grid { Width = 640 };
        bareHost.Children.Add(bareExpander);
        H.SetContent(bareHost);
        await Harness.Render();
        var footerMounted = bareExpander.ItemsFooterChild is WinUI.TextBlock;
        reconciler.UnmountChild(bareExpander);
        H.SetContent(null);
        H.Check("SettingsExpander_Unmount_UntaggedElementSlots", bare.Extensions is null && !bare.HasCallbacks && footerMounted && footerCleanups == 1,
            $"extensions={bare.Extensions is not null} mounted={footerMounted} cleanups={footerCleanups}");

        async Task Update(SettingsExpanderElement next)
        {
            reconciler.Reconcile(current, next, expander, SettingsCardTestKit.Noop);
            current = next;
            await Harness.Render();
        }
    }
}
