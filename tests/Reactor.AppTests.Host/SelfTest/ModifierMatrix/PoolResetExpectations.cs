namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.ModifierMatrix;

/// <summary>
/// Spec 067 layer 5: what <c>ElementPool.CleanElement</c> leaves on every DP the modifier schema
/// declares a receiver for, on every pooled type, frozen as literal data from the hand-written <c>CleanElement</c>. Like
/// <see cref="ModifierMatrixExpectations"/> it does not read <c>ModifierSchema</c>, so it can
/// catch a wrong schema and a wrong generated reset alike. Linked into Reactor.Tests, where
/// <c>ModifierPoolResetTests</c> checks that it names exactly the resets the schema declares, so
/// the <c>ModifierPoolReset</c> selftest exercises every one of them.
/// </summary>
internal static class PoolResetExpectations
{
    /// <summary>
    /// Each type <c>ElementPool</c> recycles, and the receiver types it is an instance of among those
    /// the resets below name. The selftest checks the hierarchy before trusting it.
    /// </summary>
    internal static readonly (string Type, string[] Receivers)[] PooledTypes =
    [
        ("TextBlock", ["FrameworkElement", "TextBlock"]),
        ("RichTextBlock", ["FrameworkElement", "RichTextBlock"]),
        ("StackPanel", ["FrameworkElement", "Panel", "StackPanel"]),
        ("Grid", ["FrameworkElement", "Panel", "Grid"]),
        ("Border", ["FrameworkElement", "Border"]),
        ("ScrollViewer", ["FrameworkElement", "Control"]),
        ("Canvas", ["FrameworkElement", "Panel"]),
        ("Viewbox", ["FrameworkElement"]),
        ("ProgressBar", ["FrameworkElement", "Control"]),
        ("ProgressRing", ["FrameworkElement", "Control"]),
        ("Image", ["FrameworkElement"]),
        ("InfoBadge", ["FrameworkElement", "Control"]),
        ("Button", ["FrameworkElement", "Control"]),
        ("TextBox", ["FrameworkElement", "Control"]),
        ("ToggleSwitch", ["FrameworkElement", "Control"]),
    ];

    /// <summary>The receiver types the resets name, so a pooled type's membership is checked both ways.</summary>
    internal static readonly string[] ReceiverTypes =
        ["FrameworkElement", "Control", "Border", "Panel", "Grid", "StackPanel", "TextBlock", "RichTextBlock"];

    /// <summary>
    /// Every modifier-backed reset, as <c>Receiver.Property</c> (an attached DP names its owner:
    /// <c>FrameworkElement.AutomationProperties.Name</c>), and what the pool leaves: <c>"Unset"</c>
    /// for a <c>ClearValue</c>, otherwise the literal it writes back.
    /// </summary>
    internal static readonly (string Reset, string Leaves)[] Resets =
    [
        ("FrameworkElement.Margin", "Unset"),
        ("FrameworkElement.Width", "Unset"),
        ("FrameworkElement.Height", "Unset"),
        ("FrameworkElement.MinWidth", "Unset"),
        ("FrameworkElement.MinHeight", "Unset"),
        ("FrameworkElement.MaxWidth", "Unset"),
        ("FrameworkElement.MaxHeight", "Unset"),
        ("FrameworkElement.HorizontalAlignment", "Unset"),
        ("FrameworkElement.VerticalAlignment", "Unset"),
        ("FrameworkElement.Opacity", "Unset"),
        ("FrameworkElement.Visibility", "Unset"),
        ("FrameworkElement.RequestedTheme", "Unset"),
        ("FrameworkElement.ContextFlyout", "Unset"),
        ("FrameworkElement.AutomationProperties.Name", "Unset"),
        ("FrameworkElement.AutomationProperties.AutomationId", "Unset"),
        ("FrameworkElement.AutomationProperties.HelpText", "Unset"),
        ("FrameworkElement.AutomationProperties.FullDescription", "Unset"),
        ("FrameworkElement.AutomationProperties.LandmarkType", "Unset"),
        ("FrameworkElement.AutomationProperties.AccessibilityView", "Unset"),
        ("FrameworkElement.IsHitTestVisible", "Unset"),
        ("FrameworkElement.IsTabStop", "Unset"),
        ("FrameworkElement.XYFocusKeyboardNavigation", "Unset"),
        ("FrameworkElement.TabFocusNavigation", "Unset"),
        ("Control.Padding", "Unset"),
        ("Control.CornerRadius", "Unset"),
        ("Control.BorderThickness", "Unset"),
        ("Control.BorderBrush", "Unset"),
        ("Control.Background", "Unset"),
        ("Control.IsEnabled", "Unset"),
        ("Control.HorizontalContentAlignment", "Unset"),
        ("Control.VerticalContentAlignment", "Unset"),
        ("Control.Foreground", "Unset"),
        ("Control.FontFamily", "Unset"),
        ("Control.FontSize", "Unset"),
        ("Control.FontWeight", "Unset"),
        ("Control.TabIndex", "Unset"),
        ("Control.ElementSoundMode", "Unset"),
        ("Border.Padding", "Unset"),
        ("Border.CornerRadius", "Unset"),
        ("Border.BorderThickness", "Unset"),
        ("Border.BorderBrush", "Unset"),
        ("Border.Background", "Unset"),
        ("Panel.Background", "Unset"),
        ("Grid.Padding", "Unset"),
        ("Grid.CornerRadius", "Unset"),
        ("Grid.BorderThickness", "Unset"),
        ("Grid.BorderBrush", "Unset"),
        ("StackPanel.Padding", "Unset"),
        ("StackPanel.CornerRadius", "Unset"),
        ("StackPanel.BorderThickness", "Unset"),
        ("StackPanel.BorderBrush", "Unset"),
        ("TextBlock.Padding", "Unset"),
        ("TextBlock.Foreground", "Unset"),
        ("FrameworkElement.TitleBar.IsDragRegion", "Unset"),
        ("FrameworkElement.AutomationProperties.IsRequiredForForm", "Unset"),
        ("FrameworkElement.AutomationProperties.LiveSetting", "Unset"),
        ("FrameworkElement.AutomationProperties.PositionInSet", "Unset"),
        ("FrameworkElement.AutomationProperties.SizeOfSet", "Unset"),
        ("FrameworkElement.AutomationProperties.Level", "Unset"),
        ("FrameworkElement.AutomationProperties.ItemStatus", "Unset"),
        ("FrameworkElement.AutomationProperties.LabeledBy", "Unset"),
        ("FrameworkElement.AutomationProperties.DescribedBy", "Unset"),
        ("FrameworkElement.AutomationProperties.FlowsTo", "Unset"),
        ("FrameworkElement.AutomationProperties.FlowsFrom", "Unset"),
        ("FrameworkElement.XYFocusUp", "Unset"),
        ("FrameworkElement.XYFocusDown", "Unset"),
        ("FrameworkElement.XYFocusLeft", "Unset"),
        ("FrameworkElement.XYFocusRight", "Unset"),
        ("FrameworkElement.ToolTipService.ToolTip", "Unset"),
        ("FrameworkElement.ToolTipService.Placement", "Unset"),
        ("FrameworkElement.ToolTipService.PlacementTarget", "Unset"),
        ("FrameworkElement.AutomationProperties.HeadingLevel", "Unset"),
        ("FrameworkElement.AccessKey", "Unset"),
        // TextBlock.FontSize inherits, so the pool writes the WinUI default instead of clearing.
        ("TextBlock.FontSize", "14"),
        ("TextBlock.FontWeight", "Unset"),
        ("TextBlock.FontFamily", "Unset"),
        ("RichTextBlock.Foreground", "Unset"),
    ];

    /// <summary>
    /// Receivers the pool never resets, because no pooled type is one: the schema declares them
    /// <c>NotReset</c>. Listed so a deliberate change to the pool set shows here too.
    /// </summary>
    internal static readonly string[] NotReset =
    [
        "IconElement.Foreground",
        "RelativePanel.BorderBrush",
        "RelativePanel.BorderThickness",
        "RelativePanel.CornerRadius",
        "RelativePanel.Padding",
    ];
}
