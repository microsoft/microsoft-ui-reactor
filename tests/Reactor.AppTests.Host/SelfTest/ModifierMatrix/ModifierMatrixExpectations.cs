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
    /// <summary>The previous renter's value survives. A gap to pin on purpose, never endorse; none today (RequestedTheme was one until #1717).</summary>
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
        new("Layout", "RequestedTheme", "FrameworkElement", "TextBlock", "FrameworkElement.RequestedThemeProperty", MatrixPool.ClearValue),
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
        new("Automation", "AutomationName", "FrameworkElement", "Border", "AutomationProperties.NameProperty", MatrixPool.ClearValue),
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
        // ContentPresenter declares all three but is neither a Control nor a CornerRadius box
        // receiver. Reactor mounts none bare, so the fixture registers a test-only carrier.
        new("CornerRadius", "ContentPresenter", "ContentPresenter.CornerRadiusProperty"),
        new("HorizontalContentAlignment", "ContentPresenter", "ContentPresenter.HorizontalContentAlignmentProperty"),
        new("VerticalContentAlignment", "ContentPresenter", "ContentPresenter.VerticalContentAlignmentProperty"),
    ];

    /// <summary>Gated slots with no qualifying non-receiver control, and why.</summary>
    internal static readonly (string Slot, string Reason)[] NoNegative =
    [
        ("IsEnabled", "WinUI declares IsEnabled on Control only."),
        ("ElementSoundMode", "WinUI declares ElementSoundMode on Control only."),
    ];

    /// <summary>
    /// §10.2 step 3's exception: on a captioned control, unset <c>AutomationName</c> falls back to
    /// the caption-derived name instead of releasing the local value. Run with no Style, since a
    /// Style-provided name stops the fallback from writing.
    /// </summary>
    internal static readonly (string Group, string Slot, string Carrier, string Caption)[] CaptionedUnset =
    [
        ("Automation", "AutomationName", "TextBlock", "matrix-carrier"),
    ];

    /// <summary>
    /// Hand-coded property slots the matrix leaves out, and why. Pinned so a slot that changes
    /// shape (into or out of rows A–D) shows in review instead of silently leaving the matrix.
    /// </summary>
    internal static readonly (string Slot, string Reason)[] Excluded =
    [
        ("Opacity", "Composition: owned by the visual handoff and keyframe paths (spec 066), covered by the ModifierTransform_* and keyframe opacity selftests."),
        ("Scale", "Composition facade, covered by the ModifierTransform_* selftests."),
        ("Rotation", "Composition facade, covered by the ModifierTransform_* selftests."),
        ("Translation", "Composition facade, covered by the ModifierTransform_* selftests."),
        ("TransformOrigin", "Composition facade, covered by the ModifierTransform_* selftests."),
        ("CenterPoint", "Composition facade, covered by the ModifierTransform_* selftests."),
        ("RichToolTip", "Element-valued: mounts a subtree, no single DP value to compare."),
        ("ToolTip", "Shares the tooltip path with RichToolTip (ToolTipService.ToolTip), covered by the ModifierEvent_Tooltip* selftests."),
        ("AttachedFlyout", "Element-valued: mounts a flyout subtree."),
        ("ContextFlyout", "Element-valued: mounts a flyout subtree."),
        ("BorderBrush", "Brush family: theme bindings and the Border composite, covered by ModifierFieldContract_BrushAndFont and the ThemeRefStyle_* selftests."),
        ("Background", "Brush family: theme bindings, covered by ModifierFieldContract_BrushAndFont and the ThemeRefStyle_* selftests."),
        ("Foreground", "Brush family, hand-coded in ForegroundModifier.Apply since #1687 (interaction states, RichTextBlock fallback), covered by ModifierFieldContract_BrushAndFont and the ThemeRefStyle_* and Issue522_* selftests."),
        ("LabeledBy", "Resolves a reference to another element, not a value."),
    ];
}
