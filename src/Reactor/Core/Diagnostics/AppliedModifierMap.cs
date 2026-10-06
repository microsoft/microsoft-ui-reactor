using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// Which WinUI property each common modifier is written to — the read-side mirror of
/// <c>Reconciler.ApplyModifiers</c> / <c>ApplyAccessibilityModifiers</c> /
/// <c>ApplyModifierReferenceEdges</c>, for <see cref="ReactorDiagnostics.GetAppliedProperties"/>.
///
/// <para>Owner resolution follows the same type tests the apply path uses (Padding goes to
/// <c>Control</c>, <c>Border</c>, <c>StackPanel</c>, … in that order; IsEnabled only to a
/// <c>Control</c>), so a modifier the reconciler skips for a control type is not reported
/// for it. Type tests only: nothing here touches a live control, so the table is testable
/// headless and needs no reflection.</para>
///
/// <para>Deliberately absent (not dependency-property writes): the <c>Layout</c> /
/// <c>Visual</c> bucket records (their members are listed individually), event handlers,
/// gesture and drag configs, <c>OnMountAction</c>/<c>OnUnmountAction</c>, <c>Ref</c>,
/// <c>Backdrop</c> (a window property). <c>Scale</c>/<c>Rotation</c>/<c>Translation</c>/
/// <c>CenterPoint</c> are composition-backed <c>UIElement</c> facades rather than DPs, but the
/// reconciler writes them, so they are mapped.
/// <c>AppliedModifierMapTests</c> fails when a new modifier is neither mapped nor listed
/// as excluded.</para>
/// </summary>
/// <summary>One modifier-applied property with its live element-side value (internal; the public surface reports it as text).</summary>
internal sealed record AppliedModifier(string Modifier, string Property, object? Value);

internal static class AppliedModifierMap
{
    internal readonly record struct Entry(string Modifier, Func<ElementModifiers, object?> Get, Func<Type, string?> Target);

    private static bool Is<T>(Type t) => typeof(T).IsAssignableFrom(t);

    private static Func<Type, string?> Always(string property) => _ => property;

    private static Func<Type, string?> ControlOnly(string property)
        => t => Is<WinUI.Control>(t) ? "Control." + property : null;

    private static string? PaddingOwner(Type t) =>
        Is<WinUI.Control>(t) ? "Control.Padding"
        : Is<WinUI.Border>(t) ? "Border.Padding"
        : Is<WinUI.StackPanel>(t) ? "StackPanel.Padding"
        : Is<WinUI.Grid>(t) ? "Grid.Padding"
        : Is<WinUI.RelativePanel>(t) ? "RelativePanel.Padding"
        : Is<WinUI.TextBlock>(t) ? "TextBlock.Padding"
        : null;

    private static string? CornerRadiusOwner(Type t) =>
        Is<WinUI.Control>(t) ? "Control.CornerRadius"
        : Is<WinUI.Border>(t) ? "Border.CornerRadius"
        : Is<WinUI.Grid>(t) ? "Grid.CornerRadius"
        : Is<WinUI.StackPanel>(t) ? "StackPanel.CornerRadius"
        : Is<WinUI.RelativePanel>(t) ? "RelativePanel.CornerRadius"
        : null;

    private static Func<Type, string?> ControlOrBorder(string property)
        => t => Is<WinUI.Control>(t) ? "Control." + property
            : Is<WinUI.Border>(t) ? "Border." + property
            : null;

    private static Func<Type, string?> ControlOrTextBlock(string property)
        => t => Is<WinUI.Control>(t) ? "Control." + property
            : Is<WinUI.TextBlock>(t) ? "TextBlock." + property
            : null;

    private static string? BackgroundOwner(Type t) =>
        Is<WinUI.Panel>(t) ? "Panel.Background"
        : Is<WinUI.Control>(t) ? "Control.Background"
        : Is<WinUI.Border>(t) ? "Border.Background"
        : null;

    // Mirrors Reconciler.ResolveFlyoutSlot.
    private static string? AttachedFlyoutOwner(Type t) =>
        Is<WinUI.SplitButton>(t) ? "SplitButton.Flyout"
        : Is<WinUI.Button>(t) ? "Button.Flyout"
        : "FlyoutBase.AttachedFlyout";

    internal static readonly Entry[] Entries =
    [
        new("RequestedTheme", m => m.RequestedTheme, Always("FrameworkElement.RequestedTheme")),
        new("Margin", m => m.Margin, Always("FrameworkElement.Margin")),
        new("MarginInlineStart", m => m.MarginInlineStart, Always("FrameworkElement.Margin")),
        new("MarginInlineEnd", m => m.MarginInlineEnd, Always("FrameworkElement.Margin")),
        new("Padding", m => m.Padding, PaddingOwner),
        new("PaddingInlineStart", m => m.PaddingInlineStart, PaddingOwner),
        new("PaddingInlineEnd", m => m.PaddingInlineEnd, PaddingOwner),
        new("Width", m => m.Width, Always("FrameworkElement.Width")),
        new("Height", m => m.Height, Always("FrameworkElement.Height")),
        new("MinWidth", m => m.MinWidth, Always("FrameworkElement.MinWidth")),
        new("MinHeight", m => m.MinHeight, Always("FrameworkElement.MinHeight")),
        new("MaxWidth", m => m.MaxWidth, Always("FrameworkElement.MaxWidth")),
        new("MaxHeight", m => m.MaxHeight, Always("FrameworkElement.MaxHeight")),
        new("HorizontalAlignment", m => m.HorizontalAlignment, Always("FrameworkElement.HorizontalAlignment")),
        new("VerticalAlignment", m => m.VerticalAlignment, Always("FrameworkElement.VerticalAlignment")),
        new("HorizontalContentAlignment", m => m.HorizontalContentAlignment, ControlOnly("HorizontalContentAlignment")),
        new("VerticalContentAlignment", m => m.VerticalContentAlignment, ControlOnly("VerticalContentAlignment")),
        new("Opacity", m => m.Opacity, Always("UIElement.Opacity")),
        // Composition-backed UIElement facades (not DPs), written directly or animated on the visual.
        new("Scale", m => m.Scale, Always("UIElement.Scale")),
        new("Rotation", m => m.Rotation, Always("UIElement.Rotation")),
        new("Translation", m => m.Translation, Always("UIElement.Translation")),
        new("CenterPoint", m => m.CenterPoint, Always("UIElement.CenterPoint")),
        new("IsVisible", m => m.IsVisible, Always("UIElement.Visibility")),
        new("RichToolTip", m => m.RichToolTip, Always("ToolTipService.ToolTip")),
        // A rich tooltip wins over the string form, so ToolTip is only applied without one.
        new("ToolTip", m => m.RichToolTip is null ? m.ToolTip : null, Always("ToolTipService.ToolTip")),
        new("ToolTipPlacement", m => m.ToolTipPlacement, Always("ToolTipService.Placement")),
        new("ToolTipPlacementTargetRef", m => m.ToolTipPlacementTargetRef, Always("ToolTipService.PlacementTarget")),
        new("AttachedFlyout", m => m.AttachedFlyout, AttachedFlyoutOwner),
        new("ContextFlyout", m => m.ContextFlyout, Always("UIElement.ContextFlyout")),
        new("IsEnabled", m => m.IsEnabled, ControlOnly("IsEnabled")),
        new("CornerRadius", m => m.CornerRadius, CornerRadiusOwner),
        new("BorderBrush", m => m.BorderBrush, ControlOrBorder("BorderBrush")),
        new("BorderThickness", m => m.BorderThickness, ControlOrBorder("BorderThickness")),
        new("BorderInlineStart", m => m.BorderInlineStart, ControlOrBorder("BorderThickness")),
        new("Background", m => m.Background, BackgroundOwner),
        new("Foreground", m => m.Foreground, ControlOrTextBlock("Foreground")),
        new("AutomationName", m => m.AutomationName, Always("AutomationProperties.Name")),
        new("AutomationId", m => m.AutomationId, Always("AutomationProperties.AutomationId")),
        new("IsDragRegion", m => m.IsDragRegion, Always("TitleBar.IsDragRegion")),
        new("ElementSoundMode", m => m.ElementSoundMode, ControlOnly("ElementSoundMode")),
        new("HeadingLevel", m => m.HeadingLevel, Always("AutomationProperties.HeadingLevel")),
        new("IsTabStop", m => m.IsTabStop, Always("UIElement.IsTabStop")),
        new("IsHitTestVisible", m => m.IsHitTestVisible, Always("UIElement.IsHitTestVisible")),
        new("TabIndex", m => m.TabIndex, ControlOnly("TabIndex")),
        new("AccessKey", m => m.AccessKey, Always("UIElement.AccessKey")),
        new("XYFocusKeyboardNavigation", m => m.XYFocusKeyboardNavigation, Always("UIElement.XYFocusKeyboardNavigation")),
        new("XYFocusUpRef", m => m.XYFocusUpRef, Always("UIElement.XYFocusUp")),
        new("XYFocusDownRef", m => m.XYFocusDownRef, Always("UIElement.XYFocusDown")),
        new("XYFocusLeftRef", m => m.XYFocusLeftRef, Always("UIElement.XYFocusLeft")),
        new("XYFocusRightRef", m => m.XYFocusRightRef, Always("UIElement.XYFocusRight")),
        new("FontFamily", m => m.FontFamily, ControlOrTextBlock("FontFamily")),
        new("FontSize", m => m.FontSize, ControlOrTextBlock("FontSize")),
        new("FontWeight", m => m.FontWeight, ControlOrTextBlock("FontWeight")),

        new("Accessibility.HelpText", m => m.Accessibility?.HelpText, Always("AutomationProperties.HelpText")),
        new("Accessibility.FullDescription", m => m.Accessibility?.FullDescription, Always("AutomationProperties.FullDescription")),
        new("Accessibility.LandmarkType", m => m.Accessibility?.LandmarkType, Always("AutomationProperties.LandmarkType")),
        new("Accessibility.AccessibilityView", m => m.Accessibility?.AccessibilityView, Always("AutomationProperties.AccessibilityView")),
        new("Accessibility.IsRequiredForForm", m => m.Accessibility?.IsRequiredForForm, Always("AutomationProperties.IsRequiredForForm")),
        new("Accessibility.LiveSetting", m => m.Accessibility?.LiveSetting, Always("AutomationProperties.LiveSetting")),
        new("Accessibility.PositionInSet", m => m.Accessibility?.PositionInSet, Always("AutomationProperties.PositionInSet")),
        new("Accessibility.SizeOfSet", m => m.Accessibility?.SizeOfSet, Always("AutomationProperties.SizeOfSet")),
        new("Accessibility.Level", m => m.Accessibility?.Level, Always("AutomationProperties.Level")),
        new("Accessibility.ItemStatus", m => m.Accessibility?.ItemStatus, Always("AutomationProperties.ItemStatus")),
        new("Accessibility.LabeledBy", m => m.Accessibility?.LabeledBy, Always("AutomationProperties.LabeledBy")),
        new("Accessibility.LabeledByRef", m => m.Accessibility?.LabeledByRef, Always("AutomationProperties.LabeledBy")),
        new("Accessibility.DescribedByRefs", m => m.Accessibility?.DescribedByRefs, Always("AutomationProperties.DescribedBy")),
        new("Accessibility.FlowsToRefs", m => m.Accessibility?.FlowsToRefs, Always("AutomationProperties.FlowsTo")),
        new("Accessibility.FlowsFromRefs", m => m.Accessibility?.FlowsFromRefs, Always("AutomationProperties.FlowsFrom")),
        new("Accessibility.TabFocusNavigation", m => m.Accessibility?.TabFocusNavigation, Always("UIElement.TabFocusNavigation")),
    ];

    internal static IReadOnlyList<AppliedModifier> Describe(ElementModifiers modifiers, Type controlType)
    {
        var result = Entries
            .Select(entry => new AppliedModifier(entry.Modifier, entry.Target(controlType) ?? "", entry.Get(modifiers)))
            .Where(applied => applied.Value is not null && applied.Property.Length > 0)
            .ToArray();
        return result.Length == 0 ? global::System.Array.Empty<AppliedModifier>() : result;
    }

    /// <summary>Modifier name reported for the caption-derived default name.</summary>
    internal const string DefaultAutomationNameModifier = "DefaultAutomationName";

    /// <summary>
    /// The caption-derived <c>AutomationProperties.Name</c> the reconciler writes when the
    /// author sets none (<c>Reconciler.ApplyDefaultAutomationName</c>), or null when it does
    /// not apply.
    ///
    /// <para>Reported only while <paramref name="liveName"/> still equals the default: the
    /// apply path yields to any existing name, so a different live value means the app (a
    /// <c>.Set(...)</c> setter or code-behind) wrote it. A value the app set to exactly the
    /// caption is indistinguishable from the default and is reported as the default.</para>
    /// </summary>
    internal static AppliedModifier? DescribeDefaultAutomationName(Element source, string? liveName)
    {
        // An explicit, non-empty .AutomationName(...) is reported as itself and suppresses the
        // default; an empty one does not, because the apply path treats an empty name as absent.
        if (source.Modifiers?.AutomationName is { Length: > 0 }) return null;
        if (Reconciler.DefaultAutomationNameFromCaption(Reconciler.ResolveCaptionForElement(source)) is not { } name)
            return null;
        if (!string.Equals(liveName, name, StringComparison.Ordinal)) return null;
        return new AppliedModifier(DefaultAutomationNameModifier, "AutomationProperties.Name", name);
    }
}
