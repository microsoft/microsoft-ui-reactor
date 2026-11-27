using Microsoft.UI.Reactor.AppTests.Host.SelfTest.ModifierMatrix;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 067 layer 5: every DP the modifier schema declares a receiver for, on every pooled type, through
/// <c>ElementPool.CleanElement</c>, against the frozen <see cref="PoolResetExpectations"/>. Each
/// DP is set to a non-default local value on a fresh control, the control is cleaned as the pool
/// does on return, and the DP must hold exactly what the pool leaves: no local value, or the
/// literal it writes back. Dropping any one reset fails its check.
/// </summary>
internal sealed class ModifierPoolResetFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override Task RunAsync()
    {
        foreach (var (typeName, receivers) in PoolResetExpectations.PooledTypes)
        {
            var fe = Create(typeName);
            // Start a ProgressRing in the state CleanElement leaves it in. Its IsIndeterminate
            // default is true, and CleanElement's false write would otherwise make WinUI rewrite
            // AccessibilityView itself, so that check would pass with or without the clear.
            if (fe is WinUI.ProgressRing ring) ring.IsIndeterminate = false;
            var id = "ModifierPoolReset_" + typeName;

            // The frozen hierarchy is what decides which resets apply, so check it both ways first.
            foreach (var receiver in PoolResetExpectations.ReceiverTypes)
                H.Check($"{id}_{(receivers.Contains(receiver) ? "Is" : "IsNot")}{receiver}", IsA(fe, receiver) == receivers.Contains(receiver));

            var resets = PoolResetExpectations.Resets.Where(r => receivers.Contains(r.Reset[..r.Reset.IndexOf('.')])).ToList();
            H.Check(id + "_HasResets", resets.Count >= 20, resets.Count.ToString());
            foreach (var (reset, _) in resets)
            {
                Set(fe, reset);
                H.Check($"{id}_{reset}_Set", Holds(fe, reset), Describe(fe, reset));
            }

            ElementPool.CleanElement(fe);

            foreach (var (reset, leaves) in resets)
                H.Check($"{id}_{reset}_Leaves{leaves}", Leaves(fe, reset, leaves), Describe(fe, reset));
        }
        return Task.CompletedTask;
    }

    private static FrameworkElement Create(string name) => name switch
    {
        "TextBlock" => new WinUI.TextBlock(),
        "RichTextBlock" => new WinUI.RichTextBlock(),
        "StackPanel" => new WinUI.StackPanel(),
        "Grid" => new WinUI.Grid(),
        "Border" => new WinUI.Border(),
        "ScrollViewer" => new WinUI.ScrollViewer(),
        "Canvas" => new WinUI.Canvas(),
        "Viewbox" => new WinUI.Viewbox(),
        "ProgressBar" => new WinUI.ProgressBar(),
        "ProgressRing" => new WinUI.ProgressRing(),
        "Image" => new WinUI.Image(),
        "InfoBadge" => new WinUI.InfoBadge(),
        "Button" => new WinUI.Button(),
        "TextBox" => new WinUI.TextBox(),
        "ToggleSwitch" => new WinUI.ToggleSwitch(),
        _ => throw new InvalidOperationException($"No pooled type {name}."),
    };

    private static bool IsA(FrameworkElement fe, string receiver) => receiver switch
    {
        "FrameworkElement" => true,
        "Control" => fe is WinUI.Control,
        "Border" => fe is WinUI.Border,
        "Panel" => fe is WinUI.Panel,
        "Grid" => fe is WinUI.Grid,
        "StackPanel" => fe is WinUI.StackPanel,
        "TextBlock" => fe is WinUI.TextBlock,
        "RichTextBlock" => fe is WinUI.RichTextBlock,
        _ => throw new InvalidOperationException($"No receiver type {receiver}."),
    };

    // The relationship lists are collections a renter appends to, not values it sets.
    private static bool IsList(string reset) => reset is "FrameworkElement.AutomationProperties.DescribedBy"
        or "FrameworkElement.AutomationProperties.FlowsTo" or "FrameworkElement.AutomationProperties.FlowsFrom";

    private static IList<DependencyObject> List(FrameworkElement fe, string reset) => reset switch
    {
        "FrameworkElement.AutomationProperties.DescribedBy" => AutomationProperties.GetDescribedBy(fe),
        "FrameworkElement.AutomationProperties.FlowsTo" => AutomationProperties.GetFlowsTo(fe),
        _ => AutomationProperties.GetFlowsFrom(fe),
    };

    private static void Set(FrameworkElement fe, string reset)
    {
        if (IsList(reset))
        {
            List(fe, reset).Add(new WinUI.TextBlock());
            return;
        }
        var dp = Dp(reset);
        fe.SetValue(dp, fe.GetValue(dp) is bool b ? !b : Sample(reset));
    }

    private static bool Holds(FrameworkElement fe, string reset) =>
        IsList(reset) ? List(fe, reset).Count == 1 : fe.ReadLocalValue(Dp(reset)) != DependencyProperty.UnsetValue;

    private static bool Leaves(FrameworkElement fe, string reset, string leaves)
    {
        if (IsList(reset)) return leaves == "Unset" && List(fe, reset).Count == 0;
        var local = fe.ReadLocalValue(Dp(reset));
        return leaves == "Unset" ? local == DependencyProperty.UnsetValue : local is double d && d.ToString(global::System.Globalization.CultureInfo.InvariantCulture) == leaves;
    }

    private static string Describe(FrameworkElement fe, string reset)
    {
        if (IsList(reset)) return "count " + List(fe, reset).Count;
        var local = fe.ReadLocalValue(Dp(reset));
        return local == DependencyProperty.UnsetValue ? "UnsetValue" : local?.ToString() ?? "null";
    }

    private static object Sample(string reset) => reset[(reset.LastIndexOf('.') + 1)..] switch
    {
        "Margin" or "Padding" or "BorderThickness" => new Thickness(3),
        "Width" or "Height" or "MinWidth" or "MinHeight" or "MaxWidth" or "MaxHeight" => 57.0,
        "HorizontalAlignment" or "HorizontalContentAlignment" => HorizontalAlignment.Right,
        "VerticalAlignment" or "VerticalContentAlignment" => VerticalAlignment.Bottom,
        "Opacity" => 0.5,
        "Visibility" => Visibility.Collapsed,
        "RequestedTheme" => ElementTheme.Dark,
        "ContextFlyout" => new WinUI.MenuFlyout(),
        "Name" or "AutomationId" or "HelpText" or "FullDescription" or "ItemStatus" or "AccessKey" => "pool-sample",
        "LandmarkType" => AutomationLandmarkType.Main,
        "AccessibilityView" => AccessibilityView.Raw,
        "XYFocusKeyboardNavigation" => XYFocusKeyboardNavigationMode.Enabled,
        "TabFocusNavigation" => KeyboardNavigationMode.Cycle,
        "CornerRadius" => new CornerRadius(4),
        "BorderBrush" or "Background" or "Foreground" => new SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
        "FontFamily" => new FontFamily("Consolas"),
        "FontSize" => 21.0,
        "FontWeight" => new global::Windows.UI.Text.FontWeight(700),
        "TabIndex" => 5,
        "ElementSoundMode" => ElementSoundMode.Off,
        "LiveSetting" => AutomationLiveSetting.Polite,
        "PositionInSet" or "Level" => 2,
        "SizeOfSet" => 3,
        "LabeledBy" => new WinUI.TextBlock(),
        "XYFocusUp" or "XYFocusDown" or "XYFocusLeft" or "XYFocusRight" => new WinUI.Button(),
        "ToolTip" => "pool-tip",
        "Placement" => PlacementMode.Bottom,
        "PlacementTarget" => new WinUI.Border(),
        "HeadingLevel" => AutomationHeadingLevel.Level2,
        // Attached bools whose unset read is not a boxed bool, so the toggle above cannot apply.
        "IsDragRegion" or "IsRequiredForForm" => true,
        _ => throw new InvalidOperationException($"No sample value for {reset}."),
    };

    private static DependencyProperty Dp(string reset) => reset switch
    {
        "FrameworkElement.Margin" => FrameworkElement.MarginProperty,
        "FrameworkElement.Width" => FrameworkElement.WidthProperty,
        "FrameworkElement.Height" => FrameworkElement.HeightProperty,
        "FrameworkElement.MinWidth" => FrameworkElement.MinWidthProperty,
        "FrameworkElement.MinHeight" => FrameworkElement.MinHeightProperty,
        "FrameworkElement.MaxWidth" => FrameworkElement.MaxWidthProperty,
        "FrameworkElement.MaxHeight" => FrameworkElement.MaxHeightProperty,
        "FrameworkElement.HorizontalAlignment" => FrameworkElement.HorizontalAlignmentProperty,
        "FrameworkElement.VerticalAlignment" => FrameworkElement.VerticalAlignmentProperty,
        "FrameworkElement.Opacity" => UIElement.OpacityProperty,
        "FrameworkElement.Visibility" => UIElement.VisibilityProperty,
        "FrameworkElement.RequestedTheme" => FrameworkElement.RequestedThemeProperty,
        "FrameworkElement.ContextFlyout" => UIElement.ContextFlyoutProperty,
        "FrameworkElement.AutomationProperties.Name" => AutomationProperties.NameProperty,
        "FrameworkElement.AutomationProperties.AutomationId" => AutomationProperties.AutomationIdProperty,
        "FrameworkElement.AutomationProperties.HelpText" => AutomationProperties.HelpTextProperty,
        "FrameworkElement.AutomationProperties.FullDescription" => AutomationProperties.FullDescriptionProperty,
        "FrameworkElement.AutomationProperties.LandmarkType" => AutomationProperties.LandmarkTypeProperty,
        "FrameworkElement.AutomationProperties.AccessibilityView" => AutomationProperties.AccessibilityViewProperty,
        "FrameworkElement.IsHitTestVisible" => UIElement.IsHitTestVisibleProperty,
        "FrameworkElement.IsTabStop" => UIElement.IsTabStopProperty,
        "FrameworkElement.XYFocusKeyboardNavigation" => UIElement.XYFocusKeyboardNavigationProperty,
        "FrameworkElement.TabFocusNavigation" => UIElement.TabFocusNavigationProperty,
        "Control.Padding" => WinUI.Control.PaddingProperty,
        "Control.CornerRadius" => WinUI.Control.CornerRadiusProperty,
        "Control.BorderThickness" => WinUI.Control.BorderThicknessProperty,
        "Control.BorderBrush" => WinUI.Control.BorderBrushProperty,
        "Control.Background" => WinUI.Control.BackgroundProperty,
        "Control.IsEnabled" => WinUI.Control.IsEnabledProperty,
        "Control.HorizontalContentAlignment" => WinUI.Control.HorizontalContentAlignmentProperty,
        "Control.VerticalContentAlignment" => WinUI.Control.VerticalContentAlignmentProperty,
        "Control.Foreground" => WinUI.Control.ForegroundProperty,
        "Control.FontFamily" => WinUI.Control.FontFamilyProperty,
        "Control.FontSize" => WinUI.Control.FontSizeProperty,
        "Control.FontWeight" => WinUI.Control.FontWeightProperty,
        "Control.TabIndex" => UIElement.TabIndexProperty,
        "Control.ElementSoundMode" => WinUI.Control.ElementSoundModeProperty,
        "Border.Padding" => WinUI.Border.PaddingProperty,
        "Border.CornerRadius" => WinUI.Border.CornerRadiusProperty,
        "Border.BorderThickness" => WinUI.Border.BorderThicknessProperty,
        "Border.BorderBrush" => WinUI.Border.BorderBrushProperty,
        "Border.Background" => WinUI.Border.BackgroundProperty,
        "Panel.Background" => WinUI.Panel.BackgroundProperty,
        "Grid.Padding" => WinUI.Grid.PaddingProperty,
        "Grid.CornerRadius" => WinUI.Grid.CornerRadiusProperty,
        "Grid.BorderThickness" => WinUI.Grid.BorderThicknessProperty,
        "Grid.BorderBrush" => WinUI.Grid.BorderBrushProperty,
        "StackPanel.Padding" => WinUI.StackPanel.PaddingProperty,
        "StackPanel.CornerRadius" => WinUI.StackPanel.CornerRadiusProperty,
        "StackPanel.BorderThickness" => WinUI.StackPanel.BorderThicknessProperty,
        "StackPanel.BorderBrush" => WinUI.StackPanel.BorderBrushProperty,
        "TextBlock.Padding" => WinUI.TextBlock.PaddingProperty,
        "TextBlock.Foreground" => WinUI.TextBlock.ForegroundProperty,
        "TextBlock.FontSize" => WinUI.TextBlock.FontSizeProperty,
        "TextBlock.FontWeight" => WinUI.TextBlock.FontWeightProperty,
        "TextBlock.FontFamily" => WinUI.TextBlock.FontFamilyProperty,
        "RichTextBlock.Foreground" => WinUI.RichTextBlock.ForegroundProperty,
        "FrameworkElement.TitleBar.IsDragRegion" => WinUI.TitleBar.IsDragRegionProperty,
        "FrameworkElement.AutomationProperties.IsRequiredForForm" => AutomationProperties.IsRequiredForFormProperty,
        "FrameworkElement.AutomationProperties.LiveSetting" => AutomationProperties.LiveSettingProperty,
        "FrameworkElement.AutomationProperties.PositionInSet" => AutomationProperties.PositionInSetProperty,
        "FrameworkElement.AutomationProperties.SizeOfSet" => AutomationProperties.SizeOfSetProperty,
        "FrameworkElement.AutomationProperties.Level" => AutomationProperties.LevelProperty,
        "FrameworkElement.AutomationProperties.ItemStatus" => AutomationProperties.ItemStatusProperty,
        "FrameworkElement.AutomationProperties.LabeledBy" => AutomationProperties.LabeledByProperty,
        "FrameworkElement.XYFocusUp" => UIElement.XYFocusUpProperty,
        "FrameworkElement.XYFocusDown" => UIElement.XYFocusDownProperty,
        "FrameworkElement.XYFocusLeft" => UIElement.XYFocusLeftProperty,
        "FrameworkElement.XYFocusRight" => UIElement.XYFocusRightProperty,
        "FrameworkElement.ToolTipService.ToolTip" => WinUI.ToolTipService.ToolTipProperty,
        "FrameworkElement.ToolTipService.Placement" => WinUI.ToolTipService.PlacementProperty,
        "FrameworkElement.ToolTipService.PlacementTarget" => WinUI.ToolTipService.PlacementTargetProperty,
        "FrameworkElement.AutomationProperties.HeadingLevel" => AutomationProperties.HeadingLevelProperty,
        "FrameworkElement.AccessKey" => UIElement.AccessKeyProperty,
        _ => throw new InvalidOperationException($"No DP mapped for {reset}."),
    };
}
