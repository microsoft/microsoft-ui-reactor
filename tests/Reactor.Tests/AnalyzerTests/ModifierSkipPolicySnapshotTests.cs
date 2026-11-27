using System;
using System.Linq;
using Microsoft.UI.Reactor.Analyzers.Modifiers;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Spec 067 layer 2: <c>ModifierEquality</c> and the <c>Merge</c> bodies are generated from
/// <c>ModifierSchema</c>, and <c>ModifierFieldContractTests</c> reads each field's skip policy from
/// the same schema, so a wrong <c>SkipEquality</c> would generate matching code and a matching
/// test. This literal is the independent oracle: it was captured from the schema while
/// <c>ModifierFieldContractTests</c> still checked it against the hand-written
/// <c>ModifiersEqualCore</c>. A deliberate skip-policy change edits the schema and this list
/// together, so it shows in review.
/// </summary>
public class ModifierSkipPolicySnapshotTests
{
    private static readonly string[] Frozen =
    [
        "Accessibility.AccessibilityView: Value",
        "Accessibility.DescribedByRefs: Reference",
        "Accessibility.FlowsFromRefs: Reference",
        "Accessibility.FlowsToRefs: Reference",
        "Accessibility.FullDescription: Value",
        "Accessibility.HelpText: Value",
        "Accessibility.IsRequiredForForm: Value",
        "Accessibility.ItemStatus: Value",
        "Accessibility.LabeledBy: Value",
        "Accessibility.LabeledByRef: Reference",
        "Accessibility.LandmarkType: Value",
        "Accessibility.Level: Value",
        "Accessibility.LiveSetting: Value",
        "Accessibility.PositionInSet: Value",
        "Accessibility.SizeOfSet: Value",
        "Accessibility.TabFocusNavigation: Value",
        "Layout.BorderInlineStart: Value",
        "Layout.Height: Value",
        "Layout.HorizontalAlignment: Value",
        "Layout.HorizontalContentAlignment: Value",
        "Layout.IsVisible: Value",
        "Layout.Margin: Value",
        "Layout.MarginInlineEnd: Value",
        "Layout.MarginInlineStart: Value",
        "Layout.MaxHeight: Value",
        "Layout.MaxWidth: Value",
        "Layout.MinHeight: Value",
        "Layout.MinWidth: Value",
        "Layout.Padding: Value",
        "Layout.PaddingInlineEnd: Value",
        "Layout.PaddingInlineStart: Value",
        "Layout.RequestedTheme: Value",
        "Layout.VerticalAlignment: Value",
        "Layout.VerticalContentAlignment: Value",
        "Layout.Width: Value",
        "Root.AccessKey: Value",
        "Root.AttachedFlyout: BlocksSkipWhenSet",
        "Root.AutomationId: Value",
        "Root.AutomationName: Value",
        "Root.Backdrop: NotCompared",
        "Root.ContextFlyout: BlocksSkipWhenSet",
        "Root.DragSource: NotCompared",
        "Root.DropTarget: NotCompared",
        "Root.ElementSoundMode: Value",
        "Root.FontFamily: FontFamilyValue",
        "Root.FontSize: Value",
        "Root.FontWeight: Value",
        "Root.HeadingLevel: Value",
        "Root.IsDragRegion: Value",
        "Root.IsEnabled: Value",
        "Root.IsHitTestVisible: Value",
        "Root.IsTabStop: Value",
        "Root.LongPress: NotCompared",
        "Root.OnAccessKeyDisplayRequested: Reference, callback",
        "Root.OnCharacterReceived: Reference, callback",
        "Root.OnDoubleTapped: Reference, callback",
        "Root.OnGotFocus: Reference, callback",
        "Root.OnHolding: Reference, callback",
        "Root.OnKeyDown: Reference, callback",
        "Root.OnKeyUp: Reference, callback",
        "Root.OnLostFocus: Reference, callback",
        "Root.OnMountAction: NotCompared",
        "Root.OnPointerCanceled: Reference, callback",
        "Root.OnPointerCaptureLost: Reference, callback",
        "Root.OnPointerEntered: Reference, callback",
        "Root.OnPointerExited: Reference, callback",
        "Root.OnPointerMoved: Reference, callback",
        "Root.OnPointerPressed: Reference, callback",
        "Root.OnPointerReleased: Reference, callback",
        "Root.OnPointerWheelChanged: Reference, callback",
        "Root.OnPreviewKeyDown: Reference, callback",
        "Root.OnPreviewKeyUp: Reference, callback",
        "Root.OnRightTapped: Reference, callback",
        "Root.OnSizeChanged: Reference, callback",
        "Root.OnTapped: Reference, callback",
        "Root.OnUnmountAction: Reference, callback",
        "Root.OnUpdateAction: Reference, callback",
        "Root.Pan: NotCompared",
        "Root.Pinch: NotCompared",
        "Root.Ref: Reference, callback",
        "Root.RichToolTip: BlocksSkipWhenSet",
        "Root.Rotate: NotCompared",
        "Root.TabIndex: Value",
        "Root.ToolTip: Value",
        "Root.ToolTipPlacement: Value",
        "Root.ToolTipPlacementTargetRef: Reference",
        "Root.XYFocusDownRef: Reference",
        "Root.XYFocusKeyboardNavigation: Value",
        "Root.XYFocusLeftRef: Reference",
        "Root.XYFocusRightRef: Reference",
        "Root.XYFocusUpRef: Reference",
        "Visual.Background: BrushValue",
        "Visual.BorderBrush: BrushValue",
        "Visual.BorderThickness: Value",
        "Visual.CenterPoint: Value",
        "Visual.CornerRadius: Value",
        "Visual.Foreground: BrushValue",
        "Visual.Opacity: Value",
        "Visual.Rotation: Value",
        "Visual.Scale: Value",
        "Visual.TransformOrigin: Value",
        "Visual.Translation: Value",
    ];

    [Fact]
    public void Skip_Policy_Matches_The_Frozen_Snapshot()
    {
        var current = ModifierSchema.Slots
            .Select(s => $"{s.Bucket}.{s.Name}: {s.Skip}{(s.CallbackSlot ? ", callback" : "")}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        var frozen = Frozen.OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.True(current.SequenceEqual(frozen, StringComparer.Ordinal),
            "A slot's skip policy changed. If the change is deliberate, update Frozen in the same PR."
            + $"\n  schema, not frozen: {string.Join("; ", current.Except(frozen))}"
            + $"\n  frozen, not schema: {string.Join("; ", frozen.Except(current))}");
    }
}