// Adapted from openclaw/openclaw-windows-node at 0d74aee5f7235472c85298d51af974dc08e29297.
// ChatVisuals.cs and Themes/ChatResources.xaml. See OpenClaw-LICENSE.txt.
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Elements;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using static Microsoft.UI.Reactor.Factories;

namespace WinUIGalleryReactor.ControlPages.Collections;

internal static class OpenClawChatStyles
{
    internal const double ReadingWidth = 768;
    internal const double ProseInset = 16;
    internal const double TranscriptBottomInset = 38;
    internal static double Gutter(double width) => width < 640 ? 12 : 40;
    internal static double RailWidth(double width) => width < 640 ? 24 : 40;
    private static ResourceDictionary? _resources;

    internal static void EnsureResources()
    {
        if (_resources is not null) return;
        _resources = (ResourceDictionary)XamlReader.Load(Resources);
        Application.Current.Resources.MergedDictionaries.Add(_resources);
    }

    internal static TextBlockElement Prose(string text, bool user = false) => TextBlock(text)
        .FontFamily("Segoe UI Variable Text").FontSize(14).LineHeight(21)
        .TextWrapping(TextWrapping.Wrap).IsTextSelectionEnabled()
        .Set(control =>
        {
            EnsureResources();
            control.Style = (Style)_resources![user ? "ChatUserTextStyle" : "ChatPrimaryTextStyle"];
            control.ClearValue(Microsoft.UI.Xaml.Controls.TextBlock.ForegroundProperty);
        });

    internal static void UserBubble(Border control)
    {
        EnsureResources();
        control.Style = (Style)_resources!["ChatUserBubbleStyle"];
        control.ClearValue(Microsoft.UI.Xaml.Controls.Border.BackgroundProperty);
    }

    internal static void HistoryBar(AnnotatedScrollBar control)
    {
        EnsureResources();
        control.Style = (Style)_resources!["ChatHistoryScrollBarStyle"];
    }

    internal static void ComposerInput(TextBox control)
    {
        EnsureResources();
        control.Style = (Style)_resources!["ChatComposerTextBoxStyle"];
        control.ClearValue(Control.BackgroundProperty);
        control.ClearValue(Control.BorderBrushProperty);
        control.ClearValue(Control.ForegroundProperty);
        control.ClearValue(Microsoft.UI.Xaml.Controls.TextBox.PlaceholderForegroundProperty);
    }

    internal static BorderElement Composer(TextBoxElement input, Element toolbar, double viewportWidth, Element? attachment = null) =>
        Border(VStack(8,
            attachment ?? Empty(),
            input.AcceptsReturn().TextWrapping(TextWrapping.Wrap)
                .FontFamily("Segoe UI Variable Text").FontSize(14)
                .MinHeight(44).MaxHeight(200).Padding(0).BorderThickness(0)
                .Resources(ComposerResources)
                .Set(ComposerInput),
            toolbar).Padding(16, 12, 16, 8))
            .CornerRadius(20).MinHeight(96).Background(Theme.CardBackground).WithBorder(Theme.ControlStroke)
            .AutomationId("ChatWritingSurface")
            .MaxWidth(ReadingWidth).Margin(Gutter(viewportWidth), 4, Gutter(viewportWidth), 12);

    internal static void Feed(ItemsView control)
    {
        control.Loaded -= OnFeedLoaded;
        control.Loaded += OnFeedLoaded;
        if (control.ScrollView is { } scroll)
            scroll.VerticalScrollBarVisibility = ScrollingScrollBarVisibility.Hidden;
    }

    private static void OnFeedLoaded(object sender, RoutedEventArgs args) => Feed((ItemsView)sender);
    internal static void ReleaseFeed(FrameworkElement control) => ((ItemsView)control).Loaded -= OnFeedLoaded;

    internal static void ItemResources(ResourceBuilder resources)
    {
        foreach (var key in new[] { "ItemContainerPointerOverBackground", "ItemContainerPointerOverBorderBrush",
            "ItemContainerPressedBackground", "ItemContainerPressedBorderBrush", "ItemContainerSelectionVisualBackground",
            "ItemContainerSelectedBackground", "ItemContainerSelectionVisualPointerOverBackground",
            "ItemContainerSelectionVisualPressedBackground", "ItemContainerSelectedPointerOverBackground",
            "ItemContainerSelectedPressedBackground", "ItemContainerSelectedInnerBorderBrush" })
            resources.Set(key, Theme.Ref("SubtleFillColorTransparentBrush"));
    }

    internal static void ComposerResources(ResourceBuilder resources)
    {
        resources.Set("TextControlBorderThemeThickness", new Thickness(0))
            .Set("TextControlBorderThemeThicknessFocused", new Thickness(0));
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundFocused",
            "TextControlBackgroundPointerOver", "TextControlBorderBrush",
            "TextControlBorderBrushFocused", "TextControlBorderBrushPointerOver" })
            resources.Set(key, Theme.Ref("SubtleFillColorTransparentBrush"));
    }

    internal static void ToolbarResources(ResourceBuilder resources)
    {
        foreach (var key in new[] { "ButtonBackground", "ButtonBackgroundDisabled", "ButtonBorderBrush",
            "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed", "ButtonBorderBrushDisabled" })
            resources.Set(key, Theme.Ref("SubtleFillColorTransparentBrush"));
        resources.Set("ButtonBackgroundPointerOver", Theme.SubtleFill)
            .Set("ButtonBackgroundPressed", Theme.Ref("SubtleFillColorTertiaryBrush"));
    }

    internal static ButtonElement ToolbarButton(string glyph, string name, Action action) =>
        Button(TextBlock(glyph).FontFamily("Segoe Fluent Icons").FontSize(14)
                .Set(text => text.IsTextScaleFactorEnabled = false)
                .AccessibilityView(Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw), action)
            .Size(32, 32).MinWidth(0).MinHeight(0).Padding(0).BorderThickness(0)
            .Resources(ToolbarResources).AutomationName(name).ToolTip(name);

    internal static void ResizePane(SplitView control)
    {
        if (control.Pane is not Microsoft.UI.Xaml.Controls.Grid pane) return;
        var handle = pane.Children.OfType<Button>().SingleOrDefault();
        if (handle is null) return;
        EnsureResources();
        handle.Width = 12;
        handle.BorderThickness = new Thickness(0);
        if (handle.Content is Border) return;
        handle.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(ResizeResources));
        var grip = new Border { Width = 4, Height = 40, CornerRadius = new CornerRadius(2) };
        Reconciler.ApplyThemeBindings(grip, new Dictionary<string, ThemeRef> { ["Background"] = Theme.ControlStroke });
        handle.Content = grip;
    }

    // Native templates remain inline C# resources, matching Reactor's theme-binding mechanism.
    private const string ResizeResources = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <SolidColorBrush x:Key="ButtonBackground" Color="Transparent"/>
          <SolidColorBrush x:Key="ButtonBackgroundPointerOver" Color="{ThemeResource SubtleFillColorSecondary}"/>
          <SolidColorBrush x:Key="ButtonBackgroundPressed" Color="{ThemeResource SubtleFillColorTertiary}"/>
          <SolidColorBrush x:Key="ButtonBorderBrush" Color="Transparent"/>
          <SolidColorBrush x:Key="ButtonBorderBrushPointerOver" Color="Transparent"/>
          <SolidColorBrush x:Key="ButtonBorderBrushPressed" Color="Transparent"/>
        </ResourceDictionary>
        """;

    private const string Resources = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:controls="using:Microsoft.UI.Xaml.Controls">
          <ResourceDictionary.ThemeDictionaries>
            <ResourceDictionary x:Key="Default">
              <SolidColorBrush x:Key="ChatUserBrush" Color="{ThemeResource SystemAccentColorLight2}" Opacity="0.12"/>
              <SolidColorBrush x:Key="ChatUserTextBrush" Color="{ThemeResource TextFillColorPrimary}"/>
              <SolidColorBrush x:Key="ChatTextBrush" Color="{ThemeResource TextFillColorPrimary}"/>
              <SolidColorBrush x:Key="ChatSecondaryTextBrush" Color="{ThemeResource TextFillColorSecondary}"/>
              <SolidColorBrush x:Key="ChatHistoryTickBrush" Color="{ThemeResource TextFillColorSecondary}" Opacity="0.4"/>
              <SolidColorBrush x:Key="ChatPlaceholderBrush" Color="{ThemeResource TextFillColorTertiary}"/>
            </ResourceDictionary>
            <ResourceDictionary x:Key="Light">
              <SolidColorBrush x:Key="ChatUserBrush" Color="{ThemeResource SystemAccentColorDark1}" Opacity="0.08"/>
              <SolidColorBrush x:Key="ChatUserTextBrush" Color="{ThemeResource TextFillColorPrimary}"/>
              <SolidColorBrush x:Key="ChatTextBrush" Color="{ThemeResource TextFillColorPrimary}"/>
              <SolidColorBrush x:Key="ChatSecondaryTextBrush" Color="{ThemeResource TextFillColorSecondary}"/>
              <SolidColorBrush x:Key="ChatHistoryTickBrush" Color="{ThemeResource TextFillColorSecondary}" Opacity="0.4"/>
              <SolidColorBrush x:Key="ChatPlaceholderBrush" Color="{ThemeResource TextFillColorTertiary}"/>
            </ResourceDictionary>
            <ResourceDictionary x:Key="HighContrast">
              <SolidColorBrush x:Key="ChatUserBrush" Color="{ThemeResource SystemColorHighlightColor}"/>
              <SolidColorBrush x:Key="ChatUserTextBrush" Color="{ThemeResource SystemColorHighlightTextColor}"/>
              <SolidColorBrush x:Key="ChatTextBrush" Color="{ThemeResource SystemColorWindowTextColor}"/>
              <SolidColorBrush x:Key="ChatSecondaryTextBrush" Color="{ThemeResource SystemColorWindowTextColor}"/>
              <SolidColorBrush x:Key="ChatHistoryTickBrush" Color="{ThemeResource SystemColorWindowTextColor}"/>
              <StaticResource x:Key="ChatPlaceholderBrush" ResourceKey="TextControlPlaceholderForeground"/>
            </ResourceDictionary>
          </ResourceDictionary.ThemeDictionaries>
          <Style x:Key="ChatUserBubbleStyle" TargetType="Border">
            <Setter Property="Background" Value="{ThemeResource ChatUserBrush}"/>
          </Style>
          <Style x:Key="ChatUserTextStyle" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{ThemeResource ChatUserTextBrush}"/>
          </Style>
          <Style x:Key="ChatPrimaryTextStyle" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{ThemeResource ChatTextBrush}"/>
          </Style>
          <Style x:Key="ChatComposerTextBoxStyle" TargetType="TextBox">
            <Setter Property="Background" Value="{ThemeResource SubtleFillColorTransparentBrush}"/>
            <Setter Property="BorderBrush" Value="{ThemeResource SubtleFillColorTransparentBrush}"/>
            <Setter Property="Foreground" Value="{ThemeResource ChatTextBrush}"/>
            <Setter Property="PlaceholderForeground" Value="{ThemeResource ChatPlaceholderBrush}"/>
          </Style>
          <ControlTemplate x:Key="ChatHistoryTickTemplate" TargetType="ContentControl">
            <Grid>
              <VisualStateManager.VisualStateGroups>
                <VisualStateGroup x:Name="CommonStates">
                  <VisualState x:Name="Normal"/>
                  <VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="Tick.Width" Value="32"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="HoverAdjacent"><VisualState.Setters><Setter Target="Tick.Width" Value="24"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="HoverNear"><VisualState.Setters><Setter Target="Tick.Width" Value="16"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="HoverFar"><VisualState.Setters><Setter Target="Tick.Width" Value="12"/></VisualState.Setters></VisualState>
                </VisualStateGroup>
                <VisualStateGroup x:Name="ToneStates">
                  <VisualState x:Name="Quiet"/>
                  <VisualState x:Name="Visible"><VisualState.Setters><Setter Target="Tick.Background" Value="{ThemeResource ChatSecondaryTextBrush}"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="Highlighted"><VisualState.Setters><Setter Target="Tick.Background" Value="{ThemeResource ChatTextBrush}"/></VisualState.Setters></VisualState>
                </VisualStateGroup>
              </VisualStateManager.VisualStateGroups>
              <Border x:Name="Tick" Width="8" Height="2" CornerRadius="1"
                MaxWidth="{TemplateBinding MaxWidth}" HorizontalAlignment="Right"
                Background="{ThemeResource ChatHistoryTickBrush}"/>
            </Grid>
          </ControlTemplate>
          <Style x:Key="ChatHistoryScrollBarStyle" TargetType="controls:AnnotatedScrollBar">
            <Setter Property="UseSystemFocusVisuals" Value="True"/>
            <Setter Property="LabelTemplate">
              <Setter.Value><DataTemplate>
                <ContentControl Width="32" Height="2" IsTabStop="False" IsHitTestVisible="False"
                  Foreground="{ThemeResource ChatHistoryTickBrush}" Template="{StaticResource ChatHistoryTickTemplate}"
                  VerticalAlignment="Top" HorizontalAlignment="Right" AutomationProperties.AccessibilityView="Raw"/>
              </DataTemplate></Setter.Value>
            </Setter>
            <Setter Property="Template">
              <Setter.Value><ControlTemplate TargetType="controls:AnnotatedScrollBar">
                <Grid x:Name="PART_RootGrid" Padding="4,8" Background="Transparent">
                  <Grid x:Name="PART_VerticalGrid">
                    <Grid x:Name="PART_LabelsGrid" Background="Transparent"/>
                    <Border x:Name="PART_ToolTipRail" HorizontalAlignment="Right" Width="1">
                      <ToolTipService.ToolTip>
                        <ToolTip x:Name="PART_DetailLabelToolTip" Placement="Left" MaxWidth="320" CornerRadius="12">
                          <ContentPresenter x:Name="PART_TooltipContentPresenter"/>
                        </ToolTip>
                      </ToolTipService.ToolTip>
                    </Border>
                    <Border x:Name="PART_VerticalThumb" Width="1" Height="2" VerticalAlignment="Top"
                      HorizontalAlignment="Right" Opacity="0" IsHitTestVisible="False"/>
                    <Border x:Name="PART_VerticalThumbGhost" Width="16" Height="1" Opacity="0"
                      VerticalAlignment="Top" HorizontalAlignment="Right" IsHitTestVisible="False" Visibility="Collapsed"/>
                  </Grid>
                </Grid>
              </ControlTemplate></Setter.Value>
            </Setter>
          </Style>
        </ResourceDictionary>
        """;
}
