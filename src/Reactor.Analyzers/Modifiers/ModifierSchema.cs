using System.Collections.Generic;

namespace Microsoft.UI.Reactor.Analyzers.Modifiers;

/// <summary>
/// Spec 067 §4.2: every common-modifier field, declared once. The array order is the order the
/// reconciler applies the arms (spec 067 §5): property slots are pinned against
/// <c>ApplyModifiers</c> / <c>ApplyAccessibilityModifiers</c> and reference edges against
/// <c>ApplyModifierReferenceEdges</c> by <c>ModifierSchemaTests</c> in Reactor.Tests. Behaviour
/// slots (handlers, gestures, lifecycle) sit at the handoff that wires them.
/// </summary>
/// <remarks>
/// <para>
/// The analyzer's <c>ModifierTable</c> takes its gates from here, and the tests check the facts
/// below against the hand-written reconciler. Layer 2 generates the skip equality
/// (<c>ModifierEquality</c>), the four <c>Merge</c> bodies, <c>AppliedModifierMap.Entries</c> and the
/// ThemeRef rows of <c>GetDependencyPropertyName</c> from it; the apply arms are still hand-written.
/// </para>
/// <para>
/// The analyzer facts (<see cref="ModifierSlot.AnalyzerControlGate"/>,
/// <see cref="ModifierSlot.GateOnlyInReconcilerReason"/>, <see cref="ModifierSlot.AnalyzerPoolResetGate"/>)
/// are editorial, copied from the table this replaced, and are <b>not</b> a function of the apply
/// receivers (spec 067 §4.5): <c>IsEnabled</c> is Control-gated in the reconciler but has no
/// analyzer gate, and the content-alignment pair is gated with neither a gate nor a
/// GateOnlyInReconciler row. <c>ModifierTableParitySnapshotTests</c> proves the move changed no
/// diagnostic.
/// </para>
/// </remarks>
internal static class ModifierSchema
{
    internal const string FrameworkElement = "Microsoft.UI.Xaml.FrameworkElement";
    private const string UIElement = "Microsoft.UI.Xaml.UIElement";
    private const string Control = "Microsoft.UI.Xaml.Controls.Control";
    private const string Border = "Microsoft.UI.Xaml.Controls.Border";
    private const string Grid = "Microsoft.UI.Xaml.Controls.Grid";
    private const string StackPanel = "Microsoft.UI.Xaml.Controls.StackPanel";
    private const string RelativePanel = "Microsoft.UI.Xaml.Controls.RelativePanel";
    private const string Panel = "Microsoft.UI.Xaml.Controls.Panel";
    private const string TextBlock = "Microsoft.UI.Xaml.Controls.TextBlock";
    private const string RichTextBlock = "Microsoft.UI.Xaml.Controls.RichTextBlock";
    private const string IconElement = "Microsoft.UI.Xaml.Controls.IconElement";
    private const string AutomationProperties = "Microsoft.UI.Xaml.Automation.AutomationProperties";
    private const string ToolTipService = "Microsoft.UI.Xaml.Controls.ToolTipService";
    private const string TitleBar = "Microsoft.UI.Xaml.Controls.TitleBar";

    private const string Thickness = "Microsoft.UI.Xaml.Thickness";
    private const string CornerRadius = "Microsoft.UI.Xaml.CornerRadius";
    private const string Brush = "Microsoft.UI.Xaml.Media.Brush";
    private const string ElementRef = "Microsoft.UI.Reactor.Input.ElementRef";
    private const string ElementType = "Microsoft.UI.Reactor.Core.Element";
    private const string FeAction = "System.Action<Microsoft.UI.Xaml.FrameworkElement>";

    // ── Analyzer gate groups (ModifierTable's, moved here) ──────────────────
    // Each name is its receiver list concatenated, so set inclusion is string inclusion: compare
    // what a gate CONTAINS, not what it is called (issue #1062, ModifierGateSource).
    internal static readonly string[] ControlBorderGridStackRelativeText = { "Control", "Border", "Grid", "StackPanel", "RelativePanel", "TextBlock" };
    internal static readonly string[] ControlBorderGridStackRelative = { "Control", "Border", "Grid", "StackPanel", "RelativePanel" };
    internal static readonly string[] PanelControlBorder = { "Panel", "Control", "Border" };
    internal static readonly string[] ControlOrTextBlock = { "Control", "TextBlock" };
    internal static readonly string[] ForegroundControlGate = { "Control", "TextBlock", "RichTextBlock", "IconElement" };

    // The two groups above minus RelativePanel, which ApplyModifiers writes to but ElementPool
    // never recycles. Used as poolResetGate, never as controlGate.
    internal static readonly string[] ControlBorderGridStackText = { "Control", "Border", "Grid", "StackPanel", "TextBlock" };
    internal static readonly string[] ControlBorderGridStack = { "Control", "Border", "Grid", "StackPanel" };

    // Single-receiver poolResetGate: CleanElement clears IsEnabled under `if (fe is Control …)`.
    internal static readonly string[] ControlOnly = { "Control" };

    /// <summary>
    /// The exact WinUI control type names <c>ElementPool.PoolableTypes</c> recycles. Mirrored here
    /// because the analyzer cannot reference Reactor; <c>Analyzer_Poolable_Type_Mirror_Matches_ElementPool</c>
    /// keeps it honest until layer 5 generates both from this list.
    /// </summary>
    internal static readonly string[] PoolableTypeNames =
    {
        "TextBlock", "RichTextBlock", "StackPanel", "Grid", "Border", "ScrollViewer",
        "Canvas", "Viewbox", "ProgressBar", "ProgressRing", "Image", "InfoBadge",
        "Button", "TextBox", "ToggleSwitch",
    };

    private const string GestureReason =
        "Dispatched through per-element gesture / drag state that the skip path refreshes without "
        + "re-subscribing (GestureDispatch.RefreshGestureDragStateOnSkip, #721). Comparing it would "
        + "re-arm an in-flight gesture (see ModifierCallbacksEqual).";

    private static ModifierSlot S(string name, ModifierBucket bucket, string clrType, ModifierFamily family, ApplyShape shape = ApplyShape.Simple)
        => new ModifierSlot(name, bucket, clrType, family, shape);

    private static ModifierSlot Event(string name, string args) =>
        S(name, ModifierBucket.Root, "System.Action<object, " + args + ">", ModifierFamily.Behavior, ApplyShape.Behavior).Callback();

    // The box family's receivers, in the order the apply chain tests them. Padding also reaches
    // TextBlock and tests StackPanel before Grid; the border family is Control, Border, Grid,
    // StackPanel, RelativePanel. Every slot that folds into the same DP shares one list.
    private static ModifierSlot BoxReceivers(ModifierSlot slot, string property, bool text, ReceiverRole roles = ReceiverRole.Apply)
    {
        slot.On(roles, Control, property).On(roles, Border, property);
        return text
            ? slot.On(roles, StackPanel, property).On(roles, Grid, property)
                .On(roles, RelativePanel, property).On(roles, TextBlock, property)
            : slot.On(roles, Grid, property).On(roles, StackPanel, property)
                .On(roles, RelativePanel, property);
    }

    private static ModifierSlot Automation(string name, string property, ModifierBucket bucket, string clrType)
        => S(name, bucket, clrType, bucket == ModifierBucket.Accessibility ? ModifierFamily.Accessibility : ModifierFamily.Automation, ApplyShape.Attached)
            .AttachedTo(AutomationProperties, property);

    /// <summary>Every common-modifier field, in apply order.</summary>
    internal static readonly ModifierSlot[] Slots =
    {
        // ── Theme (first: ThemeRef bindings resolve against it) ──
        S("RequestedTheme", ModifierBucket.Layout, "Microsoft.UI.Xaml.ElementTheme", ModifierFamily.Theme).OnFrameworkElement(),

        // ── Box: Margin and Padding families (row E) ──
        S("Margin", ModifierBucket.Layout, Thickness, ModifierFamily.Box, ApplyShape.Composite)
            .Composite("Margin", "ApplyMarginModifiers").OnFrameworkElement(),
        S("MarginInlineStart", ModifierBucket.Layout, "double", ModifierFamily.Box, ApplyShape.Composite)
            .Composite("Margin", "ApplyMarginModifiers").OnFrameworkElement("Margin"),
        S("MarginInlineEnd", ModifierBucket.Layout, "double", ModifierFamily.Box, ApplyShape.Composite)
            .Composite("Margin", "ApplyMarginModifiers").OnFrameworkElement("Margin"),
        BoxReceivers(S("Padding", ModifierBucket.Layout, Thickness, ModifierFamily.Box, ApplyShape.Composite)
            .Composite("Padding", "ApplyPaddingModifiers"), "Padding", text: true)
            .Analyzer(controlGate: ControlBorderGridStackRelativeText, poolResetGate: ControlBorderGridStackText),
        BoxReceivers(S("PaddingInlineStart", ModifierBucket.Layout, "double", ModifierFamily.Box, ApplyShape.Composite)
            .Composite("Padding", "ApplyPaddingModifiers"), "Padding", text: true)
            .Analyzer(gateOnlyInReconciler: "Reactor-only BiDi logical modifier; folds into the Padding write and inherits its Control/Border/Grid/StackPanel/RelativePanel/TextBlock gate. Not a WinUI property name, so it has no home in Properties (which is keyed on those). REACTOR_MOD_003 coverage needs a modifier-keyed table."),
        BoxReceivers(S("PaddingInlineEnd", ModifierBucket.Layout, "double", ModifierFamily.Box, ApplyShape.Composite)
            .Composite("Padding", "ApplyPaddingModifiers"), "Padding", text: true)
            .Analyzer(gateOnlyInReconciler: "Reactor-only BiDi logical modifier, the mirror of PaddingInlineStart; same guard, same gate, same reasoning."),

        // ── Size and alignment ──
        S("Width", ModifierBucket.Layout, "double", ModifierFamily.Size).OnFrameworkElement(),
        S("Height", ModifierBucket.Layout, "double", ModifierFamily.Size).OnFrameworkElement(),
        S("MinWidth", ModifierBucket.Layout, "double", ModifierFamily.Size).OnFrameworkElement(),
        S("MinHeight", ModifierBucket.Layout, "double", ModifierFamily.Size).OnFrameworkElement(),
        S("MaxWidth", ModifierBucket.Layout, "double", ModifierFamily.Size).OnFrameworkElement(),
        S("MaxHeight", ModifierBucket.Layout, "double", ModifierFamily.Size).OnFrameworkElement(),
        S("HorizontalAlignment", ModifierBucket.Layout, "Microsoft.UI.Xaml.HorizontalAlignment", ModifierFamily.Alignment).OnFrameworkElement(),
        S("VerticalAlignment", ModifierBucket.Layout, "Microsoft.UI.Xaml.VerticalAlignment", ModifierFamily.Alignment).OnFrameworkElement(),
        // Control-gated with a null analyzer gate and deliberately no GateOnlyInReconciler row: the
        // gate reader can't attribute a bare `if (fe is Control …)` to a property (ModifierTable).
        S("HorizontalContentAlignment", ModifierBucket.Layout, "Microsoft.UI.Xaml.HorizontalAlignment", ModifierFamily.Alignment)
            .On(ReceiverRole.Apply, Control),
        S("VerticalContentAlignment", ModifierBucket.Layout, "Microsoft.UI.Xaml.VerticalAlignment", ModifierFamily.Alignment)
            .On(ReceiverRole.Apply, Control),

        // ── Composition (row F, spec 066). The transforms have no public DP. ──
        S("Opacity", ModifierBucket.Visual, "double", ModifierFamily.Composition, ApplyShape.HandCoded)
            .Method("ApplyOpacityTransformAndVisibilityModifiers").On(ReceiverRole.Apply, FrameworkElement, "Opacity", UIElement)
            .Restorable(),
        S("Scale", ModifierBucket.Visual, "System.Numerics.Vector3", ModifierFamily.Composition, ApplyShape.HandCoded)
            .Method("ApplyOpacityTransformAndVisibilityModifiers").Restorable(takesVisualAtMount: true, identity: "Vector3.One")
            .Report(target: "Visual.Scale"),
        S("Rotation", ModifierBucket.Visual, "float", ModifierFamily.Composition, ApplyShape.HandCoded)
            .Method("ApplyOpacityTransformAndVisibilityModifiers").Restorable(takesVisualAtMount: true, identity: "0")
            .Report(target: "Visual.RotationAngle"),
        S("Translation", ModifierBucket.Visual, "System.Numerics.Vector3", ModifierFamily.Composition, ApplyShape.HandCoded)
            .Method("ApplyOpacityTransformAndVisibilityModifiers").Restorable(takesVisualAtMount: true, identity: "Vector3.Zero")
            .Report(target: "Visual.Translation"),
        // Before CenterPoint (spec 066 §8.2).
        S("TransformOrigin", ModifierBucket.Visual, "System.Numerics.Vector2", ModifierFamily.Composition, ApplyShape.HandCoded)
            .Method("ApplyOpacityTransformAndVisibilityModifiers").Restorable(takesVisualAtMount: true, identity: "Vector2.Zero")
            // Spec 066 §8: an expression on Visual.CenterPoint fed by the visual's ReactorOrigin property.
            .Report(target: "Visual.CenterPoint"),
        S("CenterPoint", ModifierBucket.Visual, "System.Numerics.Vector3", ModifierFamily.Composition, ApplyShape.HandCoded)
            .Method("ApplyOpacityTransformAndVisibilityModifiers").Restorable(takesVisualAtMount: true, identity: "Vector3.Zero")
            .Report(target: "Visual.CenterPoint"),

        // ── Visibility (row C) ──
        S("IsVisible", ModifierBucket.Layout, "bool", ModifierFamily.Visibility, ApplyShape.Converted)
            .Converted("ToVisibility")
            .On(ReceiverRole.Apply, FrameworkElement, "Visibility", UIElement).Fluent("IsVisible", "Visible"),

        // ── Tooltips and flyouts (row F: they mount child elements) ──
        S("RichToolTip", ModifierBucket.Root, ElementType, ModifierFamily.ToolTip, ApplyShape.HandCoded)
            .Method("ApplyToolTipAndFlyoutModifiers").On(ReceiverRole.Apply, FrameworkElement, "ToolTip", ToolTipService)
            .Equality(SkipEquality.BlocksSkipWhenSet, WriteEquality.Reference).Fluent("WithToolTip"),
        S("ToolTip", ModifierBucket.Root, "string", ModifierFamily.ToolTip, ApplyShape.HandCoded)
            .Method("ApplyToolTipAndFlyoutModifiers").On(ReceiverRole.Apply, FrameworkElement, "ToolTip", ToolTipService)
            .Report(suppressedBy: "RichToolTip"),
        S("ToolTipPlacement", ModifierBucket.Root, "Microsoft.UI.Xaml.Controls.Primitives.PlacementMode", ModifierFamily.ToolTip, ApplyShape.Attached)
            .AttachedTo(ToolTipService, "Placement"),
        S("AttachedFlyout", ModifierBucket.Root, ElementType, ModifierFamily.Flyout, ApplyShape.HandCoded)
            .Method("ApplyFlyoutAttachment").Equality(SkipEquality.BlocksSkipWhenSet, WriteEquality.Reference).Fluent("WithFlyout")
            .Report(handWritten: true),
        S("ContextFlyout", ModifierBucket.Root, ElementType, ModifierFamily.Flyout, ApplyShape.HandCoded)
            .Method("ApplyToolTipAndFlyoutModifiers").On(ReceiverRole.Apply, FrameworkElement, "ContextFlyout", UIElement)
            .Equality(SkipEquality.BlocksSkipWhenSet, WriteEquality.Reference).Fluent("WithContextFlyout"),

        // ── Control state ──
        S("IsEnabled", ModifierBucket.Root, "bool", ModifierFamily.ControlState).On(ReceiverRole.Apply, Control)
            .Fluent("IsEnabled", "Disabled")
            .Analyzer(
                gateOnlyInReconciler: "Control-gated in ApplyModifiers. Mapped with a null ControlGate because WinUI declares IsEnabled on Control only, so a .Set lambda that compiles already qualifies. REACTOR_MOD_003 therefore does not report .IsEnabled(...) — declaring the gate here would be the way to turn that on.",
                poolResetGate: ControlOnly),

        // ── Border ──
        BoxReceivers(S("CornerRadius", ModifierBucket.Visual, CornerRadius, ModifierFamily.Border), "CornerRadius", text: false)
            .Analyzer(controlGate: ControlBorderGridStackRelative, poolResetGate: ControlBorderGridStack),
        // Hand-coded: a hovered control keeps its interaction brush (spec 067 §6.5).
        BoxReceivers(S("BorderBrush", ModifierBucket.Visual, Brush, ModifierFamily.Brush, ApplyShape.HandCoded)
                .Method("ApplyBorderModifiers"), "BorderBrush", text: false, ReceiverRole.Apply | ReceiverRole.Theme)
            .Equality(SkipEquality.BrushValue, WriteEquality.Reference)
            .Analyzer(controlGate: ControlBorderGridStackRelative, poolResetGate: ControlBorderGridStack),
        BoxReceivers(S("BorderThickness", ModifierBucket.Visual, Thickness, ModifierFamily.Border, ApplyShape.Composite)
                .Composite("Border", "ApplyBorderModifiers"), "BorderThickness", text: false)
            .Analyzer(controlGate: ControlBorderGridStackRelative, poolResetGate: ControlBorderGridStack),
        BoxReceivers(S("BorderInlineStart", ModifierBucket.Layout, Thickness, ModifierFamily.Border, ApplyShape.Composite)
                .Composite("Border", "ApplyBorderModifiers"), "BorderThickness", text: false)
            .Analyzer(gateOnlyInReconciler: "Reactor-only BiDi logical modifier; folds into the BorderThickness write and inherits its Control/Border/Grid/StackPanel/RelativePanel gate. Not a WinUI property name — same reasoning as PaddingInlineStart. (There is no BorderInlineEnd modifier.)"),

        // ── Brushes ──
        // Hand-coded: the unset arm honours keepRemovedBackground (BorderElement's descriptor).
        S("Background", ModifierBucket.Visual, Brush, ModifierFamily.Brush, ApplyShape.HandCoded)
            .Method("ApplyBackgroundAndForegroundModifiers")
            .On(ReceiverRole.Apply | ReceiverRole.Theme, Panel).On(ReceiverRole.Apply | ReceiverRole.Theme, Control)
            .On(ReceiverRole.Apply | ReceiverRole.Theme, Border)
            .Equality(SkipEquality.BrushValue, WriteEquality.Reference)
            .Analyzer(controlGate: PanelControlBorder),
        // Hand-coded in ForegroundModifier (#1687): one receiver lookup for the plain write and the
        // ThemeRef path, and a RichTextBlock's descriptor-written Foreground is coordinated there.
        S("Foreground", ModifierBucket.Visual, Brush, ModifierFamily.Brush, ApplyShape.HandCoded)
            .Method("ForegroundModifier.Apply")
            .On(ReceiverRole.Apply | ReceiverRole.Theme, Control).On(ReceiverRole.Apply | ReceiverRole.Theme, TextBlock)
            .On(ReceiverRole.Apply | ReceiverRole.Theme, RichTextBlock).On(ReceiverRole.Apply | ReceiverRole.Theme, IconElement)
            .Equality(SkipEquality.BrushValue, WriteEquality.Reference)
            .Analyzer(controlGate: ForegroundControlGate),

        // ── Automation and focus ──
        // On a captioned control an unset AutomationName is replaced by a caption-derived local
        // name right after ApplyModifiers (Reconciler.Update.cs, UpdateDefaultAutomationName).
        Automation("AutomationName", "Name", ModifierBucket.Root, "string"),
        Automation("AutomationId", "AutomationId", ModifierBucket.Root, "string"),
        S("IsDragRegion", ModifierBucket.Root, "bool", ModifierFamily.Automation, ApplyShape.Attached).AttachedTo(TitleBar, "IsDragRegion"),
        S("ElementSoundMode", ModifierBucket.Root, "Microsoft.UI.Xaml.ElementSoundMode", ModifierFamily.ControlState)
            .On(ReceiverRole.Apply, Control).Fluent("SoundMode")
            .Analyzer(gateOnlyInReconciler: "Control-gated in ApplyModifiers and unmapped in Properties: there is no .ElementSoundMode modifier to suggest, and the generic .ElementSoundMode(...) path has not been audited for the reverse direction."),
        Automation("HeadingLevel", "HeadingLevel", ModifierBucket.Root, "Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel"),
        S("IsTabStop", ModifierBucket.Root, "bool", ModifierFamily.Focus).OnFrameworkElement(dpOwner: UIElement),
        S("IsHitTestVisible", ModifierBucket.Root, "bool", ModifierFamily.Focus).OnFrameworkElement(dpOwner: UIElement),
        // WinUI declares TabIndexProperty on UIElement; the reconciler's Control.TabIndexProperty
        // resolves to it through inheritance. The write itself is Control-gated.
        S("TabIndex", ModifierBucket.Root, "int", ModifierFamily.Focus).On(ReceiverRole.Apply, Control, dpOwner: UIElement)
            .Analyzer(gateOnlyInReconciler: "Control-gated in ApplyModifiers but unmapped in Properties (see DeliberatelyExcluded) — WinUI also declares TabIndex on UIElement, so the gate needs verifying before either direction uses it."),
        S("AccessKey", ModifierBucket.Root, "string", ModifierFamily.Focus).OnFrameworkElement(dpOwner: UIElement),
        S("XYFocusKeyboardNavigation", ModifierBucket.Root, "Microsoft.UI.Xaml.Input.XYFocusKeyboardNavigationMode", ModifierFamily.Focus)
            .OnFrameworkElement(dpOwner: UIElement),

        // ── Accessibility tier 2/3 (AccessibilityApplier.ApplyAccessibilityModifiers, at this point) ──
        Automation("HelpText", "HelpText", ModifierBucket.Accessibility, "string"),
        Automation("FullDescription", "FullDescription", ModifierBucket.Accessibility, "string"),
        Automation("LandmarkType", "LandmarkType", ModifierBucket.Accessibility, "Microsoft.UI.Xaml.Automation.Peers.AutomationLandmarkType").Fluent("Landmark"),
        Automation("AccessibilityView", "AccessibilityView", ModifierBucket.Accessibility, "Microsoft.UI.Xaml.Automation.Peers.AccessibilityView")
            .Fluent("AccessibilityView", "AccessibilityHidden"),
        Automation("IsRequiredForForm", "IsRequiredForForm", ModifierBucket.Accessibility, "bool").Fluent("Required"),
        Automation("LiveSetting", "LiveSetting", ModifierBucket.Accessibility, "Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting").Fluent("LiveRegion"),
        Automation("PositionInSet", "PositionInSet", ModifierBucket.Accessibility, "int"),
        Automation("SizeOfSet", "SizeOfSet", ModifierBucket.Accessibility, "int").Fluent("PositionInSet"),
        Automation("Level", "Level", ModifierBucket.Accessibility, "int").Fluent("HierarchyLevel"),
        Automation("ItemStatus", "ItemStatus", ModifierBucket.Accessibility, "string"),
        S("TabFocusNavigation", ModifierBucket.Accessibility, "Microsoft.UI.Xaml.Input.KeyboardNavigationMode", ModifierFamily.Accessibility)
            .OnFrameworkElement(dpOwner: UIElement).Fluent("TabNavigation"),
        // Row F: deferred FindByAutomationId plus Loaded resolution (PendingLabeledBy).
        S("LabeledBy", ModifierBucket.Accessibility, "string", ModifierFamily.Accessibility, ApplyShape.HandCoded)
            .Method("ApplyAccessibilityModifiers").AttachedTo(AutomationProperties, "LabeledBy"),

        // ── Typography ──
        S("FontFamily", ModifierBucket.Root, "Microsoft.UI.Xaml.Media.FontFamily", ModifierFamily.Typography)
            .On(ReceiverRole.Apply, Control).On(ReceiverRole.Apply, TextBlock)
            .Equality(SkipEquality.FontFamilyValue, WriteEquality.Reference)
            .Analyzer(controlGate: ControlOrTextBlock),
        S("FontSize", ModifierBucket.Root, "double", ModifierFamily.Typography)
            .On(ReceiverRole.Apply, Control).On(ReceiverRole.Apply, TextBlock).Analyzer(controlGate: ControlOrTextBlock),
        S("FontWeight", ModifierBucket.Root, "Windows.UI.Text.FontWeight", ModifierFamily.Typography)
            .On(ReceiverRole.Apply, Control).On(ReceiverRole.Apply, TextBlock).Analyzer(controlGate: ControlOrTextBlock),

        // ── Routed-event handlers (ModifierEvents.ApplyEventHandlers) ──
        Event("OnSizeChanged", "Microsoft.UI.Xaml.SizeChangedEventArgs"),
        Event("OnPointerPressed", "Microsoft.UI.Xaml.Input.PointerRoutedEventArgs"),
        Event("OnPointerMoved", "Microsoft.UI.Xaml.Input.PointerRoutedEventArgs"),
        Event("OnPointerReleased", "Microsoft.UI.Xaml.Input.PointerRoutedEventArgs"),
        Event("OnPointerEntered", "Microsoft.UI.Xaml.Input.PointerRoutedEventArgs"),
        Event("OnPointerExited", "Microsoft.UI.Xaml.Input.PointerRoutedEventArgs"),
        Event("OnPointerCanceled", "Microsoft.UI.Xaml.Input.PointerRoutedEventArgs"),
        Event("OnPointerCaptureLost", "Microsoft.UI.Xaml.Input.PointerRoutedEventArgs"),
        Event("OnPointerWheelChanged", "Microsoft.UI.Xaml.Input.PointerRoutedEventArgs"),
        Event("OnTapped", "Microsoft.UI.Xaml.Input.TappedRoutedEventArgs"),
        Event("OnDoubleTapped", "Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs"),
        Event("OnRightTapped", "Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs"),
        Event("OnHolding", "Microsoft.UI.Xaml.Input.HoldingRoutedEventArgs"),
        Event("OnKeyDown", "Microsoft.UI.Xaml.Input.KeyRoutedEventArgs"),
        Event("OnKeyUp", "Microsoft.UI.Xaml.Input.KeyRoutedEventArgs"),
        Event("OnPreviewKeyDown", "Microsoft.UI.Xaml.Input.KeyRoutedEventArgs"),
        Event("OnPreviewKeyUp", "Microsoft.UI.Xaml.Input.KeyRoutedEventArgs"),
        S("OnCharacterReceived", ModifierBucket.Root,
            "System.Action<Microsoft.UI.Xaml.UIElement, Microsoft.UI.Xaml.Input.CharacterReceivedRoutedEventArgs>",
            ModifierFamily.Behavior, ApplyShape.Behavior).Callback(),
        Event("OnGotFocus", "Microsoft.UI.Xaml.RoutedEventArgs"),
        Event("OnLostFocus", "Microsoft.UI.Xaml.RoutedEventArgs"),
        S("OnAccessKeyDisplayRequested", ModifierBucket.Root,
            "System.Action<Microsoft.UI.Xaml.UIElement, Microsoft.UI.Xaml.Input.AccessKeyDisplayRequestedEventArgs>",
            ModifierFamily.Behavior, ApplyShape.Behavior).Callback(),

        // ── Gestures and drag-drop (GestureDispatch / DragDropDispatch) ──
        S("Pan", ModifierBucket.Root, "Microsoft.UI.Reactor.Input.PanGestureConfig", ModifierFamily.Behavior, ApplyShape.Behavior)
            .NotCompared(GestureReason).Fluent("OnPan"),
        S("Pinch", ModifierBucket.Root, "Microsoft.UI.Reactor.Input.PinchGestureConfig", ModifierFamily.Behavior, ApplyShape.Behavior)
            .NotCompared(GestureReason).Fluent("OnPinch"),
        S("Rotate", ModifierBucket.Root, "Microsoft.UI.Reactor.Input.RotateGestureConfig", ModifierFamily.Behavior, ApplyShape.Behavior)
            .NotCompared(GestureReason).Fluent("OnRotate"),
        S("LongPress", ModifierBucket.Root, "Microsoft.UI.Reactor.Input.LongPressGestureConfig", ModifierFamily.Behavior, ApplyShape.Behavior)
            .NotCompared(GestureReason).Fluent("OnLongPress"),
        S("DragSource", ModifierBucket.Root, "Microsoft.UI.Reactor.Input.DragSourceConfig", ModifierFamily.Behavior, ApplyShape.Behavior)
            .NotCompared(GestureReason).Fluent("OnDragStart", "DraggableWhen"),
        S("DropTarget", ModifierBucket.Root, "Microsoft.UI.Reactor.Input.DropTargetConfig", ModifierFamily.Behavior, ApplyShape.Behavior)
            .NotCompared(GestureReason).Fluent("OnDrop", "OnDragEnter", "OnDragOver", "OnDragLeave"),

        // ── Lifecycle and ref (after every property write, spec 067 §5) ──
        S("OnMountAction", ModifierBucket.Root, FeAction, ModifierFamily.Lifecycle, ApplyShape.Behavior)
            .NotCompared("Fires once at mount (oldM is null), so a later change is inert.").Fluent("OnMount", "OnMountAdd"),
        // Internal: no public fluent modifier (chart label a11y, issue #162).
        S("OnUpdateAction", ModifierBucket.Root, FeAction, ModifierFamily.Lifecycle, ApplyShape.Behavior).Callback().Fluent(),
        S("OnUnmountAction", ModifierBucket.Root, FeAction, ModifierFamily.Lifecycle, ApplyShape.Behavior)
            .Callback().Fluent("OnUnmount", "OnUnmountAdd"),
        S("Ref", ModifierBucket.Root, ElementRef, ModifierFamily.Lifecycle, ApplyShape.Behavior).Callback(),

        // ── Reference edges (row G′, ReferenceEdgeWiring.ApplyModifierReferenceEdges, last) ──
        S("LabeledByRef", ModifierBucket.Accessibility, ElementRef, ModifierFamily.Accessibility, ApplyShape.ReferenceEdge)
            .Method("ApplyModifierReferenceEdges").AttachedTo(AutomationProperties, "LabeledBy")
            .Equality(SkipEquality.Reference, WriteEquality.Reference).Fluent("LabeledBy"),
        S("DescribedByRefs", ModifierBucket.Accessibility, "System.Collections.Generic.IReadOnlyList<" + ElementRef + ">",
                ModifierFamily.Accessibility, ApplyShape.ReferenceEdge)
            .Method("ApplyModifierReferenceEdges").AttachedTo(AutomationProperties, "DescribedBy")
            .Equality(SkipEquality.Reference, WriteEquality.Reference).Fluent("DescribedBy"),
        S("FlowsToRefs", ModifierBucket.Accessibility, "System.Collections.Generic.IReadOnlyList<" + ElementRef + ">",
                ModifierFamily.Accessibility, ApplyShape.ReferenceEdge)
            .Method("ApplyModifierReferenceEdges").AttachedTo(AutomationProperties, "FlowsTo")
            .Equality(SkipEquality.Reference, WriteEquality.Reference).Fluent("FlowsTo"),
        S("FlowsFromRefs", ModifierBucket.Accessibility, "System.Collections.Generic.IReadOnlyList<" + ElementRef + ">",
                ModifierFamily.Accessibility, ApplyShape.ReferenceEdge)
            .Method("ApplyModifierReferenceEdges").AttachedTo(AutomationProperties, "FlowsFrom")
            .Equality(SkipEquality.Reference, WriteEquality.Reference).Fluent("FlowsFrom"),
        S("XYFocusUpRef", ModifierBucket.Root, ElementRef, ModifierFamily.Focus, ApplyShape.ReferenceEdge)
            .Method("ApplyModifierReferenceEdges").OnFrameworkElement("XYFocusUp", UIElement)
            .Equality(SkipEquality.Reference, WriteEquality.Reference).Fluent("XYFocusUp"),
        S("XYFocusDownRef", ModifierBucket.Root, ElementRef, ModifierFamily.Focus, ApplyShape.ReferenceEdge)
            .Method("ApplyModifierReferenceEdges").OnFrameworkElement("XYFocusDown", UIElement)
            .Equality(SkipEquality.Reference, WriteEquality.Reference).Fluent("XYFocusDown"),
        S("XYFocusLeftRef", ModifierBucket.Root, ElementRef, ModifierFamily.Focus, ApplyShape.ReferenceEdge)
            .Method("ApplyModifierReferenceEdges").OnFrameworkElement("XYFocusLeft", UIElement)
            .Equality(SkipEquality.Reference, WriteEquality.Reference).Fluent("XYFocusLeft"),
        S("XYFocusRightRef", ModifierBucket.Root, ElementRef, ModifierFamily.Focus, ApplyShape.ReferenceEdge)
            .Method("ApplyModifierReferenceEdges").OnFrameworkElement("XYFocusRight", UIElement)
            .Equality(SkipEquality.Reference, WriteEquality.Reference).Fluent("XYFocusRight"),
        S("ToolTipPlacementTargetRef", ModifierBucket.Root, ElementRef, ModifierFamily.ToolTip, ApplyShape.ReferenceEdge)
            .Method("ApplyModifierReferenceEdges").AttachedTo(ToolTipService, "PlacementTarget")
            .Equality(SkipEquality.Reference, WriteEquality.Reference).Fluent("ToolTipPlacementTarget"),

        // ── Host-read state ──
        S("Backdrop", ModifierBucket.Root, "Microsoft.UI.Reactor.Core.BackdropChoice", ModifierFamily.Behavior, ApplyShape.Behavior)
            .NotCompared("Not applied by ApplyModifiers. The host reads the root tree's Backdrop on every render (ReactorHost, ReactorHostControl)."),
    };

    private static readonly Dictionary<string, ModifierSlot> s_byName = BuildIndex();

    /// <summary>
    /// The order <c>ReactorDiagnostics.GetAppliedProperties</c> lists modifiers in, pinned from the
    /// hand-written <c>AppliedModifierMap</c> this replaced (spec 067 layer 2), so generating the map
    /// changed no output. It differs from <see cref="Slots"/> (apply order) only in presentation.
    /// Every reported slot must appear exactly once; the generator throws otherwise.
    /// </summary>
    internal static readonly string[] AppliedReportOrder =
    {
        "RequestedTheme", "Margin", "MarginInlineStart", "MarginInlineEnd", "Padding", "PaddingInlineStart",
        "PaddingInlineEnd", "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight",
        "HorizontalAlignment", "VerticalAlignment", "HorizontalContentAlignment", "VerticalContentAlignment",
        "Opacity", "Scale", "Rotation", "Translation", "CenterPoint", "TransformOrigin", "IsVisible",
        "RichToolTip", "ToolTip", "ToolTipPlacement", "ToolTipPlacementTargetRef", "AttachedFlyout", "ContextFlyout",
        "IsEnabled", "CornerRadius", "BorderBrush", "BorderThickness", "BorderInlineStart", "Background", "Foreground",
        "AutomationName", "AutomationId", "IsDragRegion", "ElementSoundMode", "HeadingLevel", "IsTabStop",
        "IsHitTestVisible", "TabIndex", "AccessKey", "XYFocusKeyboardNavigation",
        "XYFocusUpRef", "XYFocusDownRef", "XYFocusLeftRef", "XYFocusRightRef",
        "FontFamily", "FontSize", "FontWeight",
        "HelpText", "FullDescription", "LandmarkType", "AccessibilityView", "IsRequiredForForm", "LiveSetting",
        "PositionInSet", "SizeOfSet", "Level", "ItemStatus", "LabeledBy", "LabeledByRef", "DescribedByRefs",
        "FlowsToRefs", "FlowsFromRefs", "TabFocusNavigation",
    };

    private static Dictionary<string, ModifierSlot> BuildIndex()
    {
        var map = new Dictionary<string, ModifierSlot>(System.StringComparer.Ordinal);
        foreach (var slot in Slots)
            map[slot.Name] = slot;
        return map;
    }

    /// <summary>The slot named <paramref name="name"/> on any bucket, or null.</summary>
    /// <remarks>Names are unique across buckets; <c>ModifierSchemaTests</c> pins that.</remarks>
    internal static ModifierSlot? Find(string name) => s_byName.TryGetValue(name, out var found) ? found : null;
}
