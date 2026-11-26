namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.ModifierMatrix;

/// <summary>What <c>ElementPool.CleanElement</c> leaves on a control returned to the pool, for one (slot, receiver).</summary>
internal enum MatrixPool
{
    /// <summary>The carrier type is not in <c>ElementPool.PoolableTypes</c>.</summary>
    NotPoolable,
    /// <summary>The previous renter's local value is released.</summary>
    ClearValue,
    /// <summary>A default is written back as a local value (<c>TextBlock.FontSize = 14</c>).</summary>
    WriteDefault,
    /// <summary>The previous renter's value survives. A known gap pinned on purpose (#1717), not endorsed.</summary>
    NotReset,
}

/// <summary>One (modifier slot, receiver) the matrix selftest mounts and checks.</summary>
/// <param name="Group">The <c>ModifierMatrix_&lt;Group&gt;</c> fixture that runs it.</param>
/// <param name="Slot">The modifier field.</param>
/// <param name="Receiver">The receiver type the reconciler writes it to.</param>
/// <param name="Carrier">The concrete control the fixture mounts for that receiver.</param>
/// <param name="Dp">The DP the reconciler writes, as <c>Owner.NameProperty</c>.</param>
/// <param name="Pool">What the pool leaves on the carrier once it is returned.</param>
internal sealed record MatrixCase(string Group, string Slot, string Receiver, string Carrier, string Dp, MatrixPool Pool);

/// <summary>A control that declares a DP of the slot's name the modifier deliberately does not reach.</summary>
internal sealed record MatrixNegative(string Slot, string Carrier, string Dp);

/// <summary>
/// Spec 067 §10.2: the expected behaviour of every row A–D modifier, frozen as literal data from
/// the hand-written <c>ApplyModifiers</c> and <c>ElementPool.CleanElement</c>. It deliberately does
/// <b>not</b> read <c>ModifierSchema</c>: a fixture that took both its inputs and its expectations
/// from the schema could not detect a wrong schema. The schema only supplies the typed probes, and
/// the fixture checks each probe's DP against <see cref="MatrixCase.Dp"/> before using it.
/// Linked into Reactor.Tests, where <c>ModifierMatrixSchemaAgreementTests</c> checks schema and
/// matrix agree, so a deliberate behaviour change edits both and shows in review.
/// </summary>
internal static class ModifierMatrixExpectations
{
    internal static readonly MatrixCase[] Cases =
    [
        // ── Layout: FrameworkElement receivers, carried by a TextBlock ──
        new("Layout", "RequestedTheme", "FrameworkElement", "TextBlock", "FrameworkElement.RequestedThemeProperty", MatrixPool.NotReset),
        new("Layout", "Width", "FrameworkElement", "TextBlock", "FrameworkElement.WidthProperty", MatrixPool.ClearValue),
        new("Layout", "Height", "FrameworkElement", "TextBlock", "FrameworkElement.HeightProperty", MatrixPool.ClearValue),
        new("Layout", "MinWidth", "FrameworkElement", "TextBlock", "FrameworkElement.MinWidthProperty", MatrixPool.ClearValue),
        new("Layout", "MinHeight", "FrameworkElement", "TextBlock", "FrameworkElement.MinHeightProperty", MatrixPool.ClearValue),
        new("Layout", "MaxWidth", "FrameworkElement", "TextBlock", "FrameworkElement.MaxWidthProperty", MatrixPool.ClearValue),
        new("Layout", "MaxHeight", "FrameworkElement", "TextBlock", "FrameworkElement.MaxHeightProperty", MatrixPool.ClearValue),
        new("Layout", "HorizontalAlignment", "FrameworkElement", "TextBlock", "FrameworkElement.HorizontalAlignmentProperty", MatrixPool.ClearValue),
        new("Layout", "VerticalAlignment", "FrameworkElement", "TextBlock", "FrameworkElement.VerticalAlignmentProperty", MatrixPool.ClearValue),
        new("Layout", "IsVisible", "FrameworkElement", "TextBlock", "UIElement.VisibilityProperty", MatrixPool.ClearValue),

        // ── Control receivers, carried by a Button ──
        new("Control", "HorizontalContentAlignment", "Control", "Button", "Control.HorizontalContentAlignmentProperty", MatrixPool.ClearValue),
        new("Control", "VerticalContentAlignment", "Control", "Button", "Control.VerticalContentAlignmentProperty", MatrixPool.ClearValue),
        new("Control", "IsEnabled", "Control", "Button", "Control.IsEnabledProperty", MatrixPool.ClearValue),
        new("Control", "CornerRadius", "Control", "Button", "Control.CornerRadiusProperty", MatrixPool.ClearValue),
        new("Control", "ElementSoundMode", "Control", "Button", "Control.ElementSoundModeProperty", MatrixPool.ClearValue),
        new("Control", "TabIndex", "Control", "Button", "UIElement.TabIndexProperty", MatrixPool.ClearValue),
        new("Control", "FontFamily", "Control", "Button", "Control.FontFamilyProperty", MatrixPool.ClearValue),
        new("Control", "FontSize", "Control", "Button", "Control.FontSizeProperty", MatrixPool.ClearValue),
        new("Control", "FontWeight", "Control", "Button", "Control.FontWeightProperty", MatrixPool.ClearValue),

        // ── Panels, Border and TextBlock receivers ──
        new("Panels", "CornerRadius", "Border", "Border", "Border.CornerRadiusProperty", MatrixPool.ClearValue),
        new("Panels", "CornerRadius", "Grid", "Grid", "Grid.CornerRadiusProperty", MatrixPool.ClearValue),
        new("Panels", "CornerRadius", "StackPanel", "StackPanel", "StackPanel.CornerRadiusProperty", MatrixPool.ClearValue),
        new("Panels", "CornerRadius", "RelativePanel", "RelativePanel", "RelativePanel.CornerRadiusProperty", MatrixPool.NotPoolable),
        new("Panels", "FontFamily", "TextBlock", "TextBlock", "TextBlock.FontFamilyProperty", MatrixPool.ClearValue),
        // TextBlock.FontSize inherits, so the pool writes the WinUI default back (ElementPool.CleanElement).
        new("Panels", "FontSize", "TextBlock", "TextBlock", "TextBlock.FontSizeProperty", MatrixPool.WriteDefault),
        new("Panels", "FontWeight", "TextBlock", "TextBlock", "TextBlock.FontWeightProperty", MatrixPool.ClearValue),

        // ── Automation and focus: FrameworkElement receivers ──
        new("Automation", "ToolTipPlacement", "FrameworkElement", "TextBlock", "ToolTipService.PlacementProperty", MatrixPool.ClearValue),
        new("Automation", "AutomationName", "FrameworkElement", "TextBlock", "AutomationProperties.NameProperty", MatrixPool.ClearValue),
        new("Automation", "AutomationId", "FrameworkElement", "TextBlock", "AutomationProperties.AutomationIdProperty", MatrixPool.ClearValue),
        new("Automation", "IsDragRegion", "FrameworkElement", "TextBlock", "TitleBar.IsDragRegionProperty", MatrixPool.ClearValue),
        new("Automation", "HeadingLevel", "FrameworkElement", "TextBlock", "AutomationProperties.HeadingLevelProperty", MatrixPool.ClearValue),
        new("Automation", "IsTabStop", "FrameworkElement", "TextBlock", "UIElement.IsTabStopProperty", MatrixPool.ClearValue),
        new("Automation", "IsHitTestVisible", "FrameworkElement", "TextBlock", "UIElement.IsHitTestVisibleProperty", MatrixPool.ClearValue),
        new("Automation", "AccessKey", "FrameworkElement", "TextBlock", "UIElement.AccessKeyProperty", MatrixPool.ClearValue),
        new("Automation", "XYFocusKeyboardNavigation", "FrameworkElement", "TextBlock", "UIElement.XYFocusKeyboardNavigationProperty", MatrixPool.ClearValue),

        // ── Accessibility tier 2/3 ──
        new("Accessibility", "HelpText", "FrameworkElement", "TextBlock", "AutomationProperties.HelpTextProperty", MatrixPool.ClearValue),
        new("Accessibility", "FullDescription", "FrameworkElement", "TextBlock", "AutomationProperties.FullDescriptionProperty", MatrixPool.ClearValue),
        new("Accessibility", "LandmarkType", "FrameworkElement", "TextBlock", "AutomationProperties.LandmarkTypeProperty", MatrixPool.ClearValue),
        new("Accessibility", "AccessibilityView", "FrameworkElement", "TextBlock", "AutomationProperties.AccessibilityViewProperty", MatrixPool.ClearValue),
        new("Accessibility", "IsRequiredForForm", "FrameworkElement", "TextBlock", "AutomationProperties.IsRequiredForFormProperty", MatrixPool.ClearValue),
        new("Accessibility", "LiveSetting", "FrameworkElement", "TextBlock", "AutomationProperties.LiveSettingProperty", MatrixPool.ClearValue),
        new("Accessibility", "PositionInSet", "FrameworkElement", "TextBlock", "AutomationProperties.PositionInSetProperty", MatrixPool.ClearValue),
        new("Accessibility", "SizeOfSet", "FrameworkElement", "TextBlock", "AutomationProperties.SizeOfSetProperty", MatrixPool.ClearValue),
        new("Accessibility", "Level", "FrameworkElement", "TextBlock", "AutomationProperties.LevelProperty", MatrixPool.ClearValue),
        new("Accessibility", "ItemStatus", "FrameworkElement", "TextBlock", "AutomationProperties.ItemStatusProperty", MatrixPool.ClearValue),
        new("Accessibility", "TabFocusNavigation", "FrameworkElement", "TextBlock", "UIElement.TabFocusNavigationProperty", MatrixPool.ClearValue),
    ];

    /// <summary>Gated slots, on a control that declares the DP but is not a receiver (§10.2 step 6).</summary>
    internal static readonly MatrixNegative[] Negatives =
    [
        new("FontFamily", "RichTextBlock", "RichTextBlock.FontFamilyProperty"),
        new("FontSize", "RichTextBlock", "RichTextBlock.FontSizeProperty"),
        new("FontWeight", "RichTextBlock", "RichTextBlock.FontWeightProperty"),
        // WinUI declares TabIndex on UIElement, but the write is Control-gated.
        new("TabIndex", "TextBlock", "UIElement.TabIndexProperty"),
    ];

    /// <summary>Gated slots with no qualifying non-receiver control, and why.</summary>
    internal static readonly (string Slot, string Reason)[] NoNegative =
    [
        ("HorizontalContentAlignment", "Only Control and ContentPresenter declare it, and Reactor mounts no bare ContentPresenter."),
        ("VerticalContentAlignment", "Only Control and ContentPresenter declare it, and Reactor mounts no bare ContentPresenter."),
        ("IsEnabled", "WinUI declares IsEnabled on Control only."),
        ("ElementSoundMode", "WinUI declares ElementSoundMode on Control only."),
        ("CornerRadius", "Every other declarer Reactor mounts is a receiver; ContentPresenter is not mounted bare."),
    ];
}
