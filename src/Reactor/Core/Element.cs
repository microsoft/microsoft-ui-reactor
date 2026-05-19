using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.UI.Text;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;

namespace Microsoft.UI.Reactor.Core;

// ════════════════════════════════════════════════════════════════════════
//  Base types
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// A lightweight, immutable description of a UI node (the "virtual DOM").
/// Elements are cheap to create and diff — they never touch real controls directly.
/// </summary>
// <snippet:element-record>
public abstract record Element
{
    /// <summary>
    /// Optional key for stable identity across re-renders (like React's key prop).
    /// When set, the reconciler uses it to match elements across list reorderings.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>
    /// Layout modifiers (margin, padding, size, alignment, etc.) applied to this element.
    /// Set via fluent extension methods: Text("hi").Margin(10).Width(200)
    /// Modifiers are stored inline so the concrete element type is preserved through chaining.
    /// </summary>
    public ElementModifiers? Modifiers { get; init; }
// </snippet:element-record>

    /// <summary>
    /// Outer margin shim that routes to <see cref="Modifiers"/>. Lets
    /// <c>el with { Margin = new Thickness(8) }</c> work directly on a record
    /// initializer (where extension methods are not visible). Identical
    /// semantics to <c>.Margin(...)</c>.
    /// </summary>
    public Thickness? Margin
    {
        get => Modifiers?.Margin;
        init => Modifiers = Modifiers is null
            ? new ElementModifiers { Margin = value }
            : Modifiers with { Margin = value };
    }

    /// <summary>
    /// Inner padding shim that routes to <see cref="Modifiers"/>. Lets
    /// <c>el with { Padding = new Thickness(8) }</c> work directly on a record
    /// initializer (where extension methods are not visible). Identical
    /// semantics to <c>.Padding(...)</c>.
    /// </summary>
    public Thickness? Padding
    {
        get => Modifiers?.Padding;
        init => Modifiers = Modifiers is null
            ? new ElementModifiers { Padding = value }
            : Modifiers with { Padding = value };
    }

    /// <summary>
    /// Attached properties from parent containers (Grid.Row, Canvas.Left, etc.).
    /// Set via fluent extension methods: Text("hi").Grid(row: 1, column: 2)
    /// Stored as a type-keyed dictionary so each provider defines its own data record.
    /// </summary>
    public IReadOnlyDictionary<Type, object>? Attached { get; init; }

    /// <summary>
    /// Implicit transitions (opacity, scale, rotation, translation, background).
    /// Set via fluent extension methods: Rectangle().WithOpacityTransition()
    /// Applied by the reconciler after mount/update, so they are always present when
    /// property values are set via .Set() callbacks.
    /// </summary>
    public ImplicitTransitions? ImplicitTransitions { get; init; }

    /// <summary>
    /// Theme transitions (children, item container).
    /// Set via fluent extension methods: VStack(children).WithThemeTransitions(...)
    /// </summary>
    public ThemeTransitions? ThemeTransitions { get; init; }

    /// <summary>
    /// Theme-resource bindings for brush properties (Background, Foreground, BorderBrush).
    /// When set, the reconciler resolves from WinUI theme resources instead of using local values.
    /// Set via fluent extension methods: Text("hi").Background(Theme.Accent)
    /// </summary>
    public IReadOnlyDictionary<string, ThemeRef>? ThemeBindings { get; init; }

    /// <summary>
    /// Composition-layer layout animation configuration.
    /// When set, the reconciler attaches implicit animations to the element's Visual
    /// so that layout-driven position (and optionally size) changes animate smoothly.
    /// Set via fluent extension methods: Border(child).LayoutAnimation()
    /// </summary>
    public LayoutAnimationConfig? LayoutAnimation { get; init; }

    /// <summary>
    /// Compositor property animation configuration (.Animate() modifier).
    /// When set, the reconciler creates ImplicitAnimationCollection entries on the
    /// element's Visual for Opacity/Scale/Rotation/Offset/CenterPoint.
    /// </summary>
    public Microsoft.UI.Reactor.Animation.AnimationConfig? AnimationConfig { get; init; }

    /// <summary>
    /// Element enter/exit transition configuration (.Transition() modifier).
    /// When set, the reconciler animates mount (enter) and unmount (exit) with
    /// compositor animations, deferring removal until exit animation completes.
    /// </summary>
    public Microsoft.UI.Reactor.Animation.ElementTransition? ElementTransition { get; init; }

    /// <summary>
    /// Interaction states configuration (.InteractionStates() modifier).
    /// When set, the reconciler registers pointer event handlers that drive
    /// zero-reconcile visual state transitions (hover, pressed, focused).
    /// </summary>
    public Microsoft.UI.Reactor.Animation.InteractionStatesConfig? InteractionStates { get; init; }

    /// <summary>
    /// Stagger configuration for container children (.Stagger() modifier).
    /// When set, child animations (enter, layout, property) have incrementing
    /// DelayTime = childIndex * staggerDelay.
    /// </summary>
    public Microsoft.UI.Reactor.Animation.StaggerConfig? StaggerConfig { get; init; }

    /// <summary>
    /// Keyframe animation definitions (.Keyframes() modifier).
    /// Trigger-based: plays when the trigger value changes between renders.
    /// </summary>
    public Microsoft.UI.Reactor.Animation.KeyframeEntry[]? KeyframeAnimations { get; init; }

    /// <summary>
    /// Scroll-linked expression animation configuration (.ScrollLinked() modifier).
    /// Expression animations run on the compositor, driven by ScrollViewer position.
    /// </summary>
    public Microsoft.UI.Reactor.Animation.ScrollAnimationConfig? ScrollAnimation { get; init; }

    /// <summary>
    /// Connected animation key for cross-container transitions.
    /// When set, the reconciler automatically captures a visual snapshot on unmount
    /// (via ConnectedAnimationService.PrepareToAnimate) and starts the animation on
    /// mount if a prepared animation with the same key exists.
    /// Set via fluent extension method: Border(child).ConnectedAnimation("hero")
    /// </summary>
    public string? ConnectedAnimationKey { get; init; }

    /// <summary>
    /// Per-control resource overrides (lightweight styling). When set, the reconciler
    /// injects these into <see cref="FrameworkElement.Resources"/> so that the control's
    /// VisualStateManager picks them up for hover/pressed/disabled states.
    /// Set via fluent extension: <c>Button("Go").Resources(r => r.Set("ButtonBackground", "#0078D4"))</c>
    /// </summary>
    public Microsoft.UI.Reactor.Elements.ResourceOverrides? ResourceOverrides { get; init; }

    /// <summary>
    /// Context values provided to this element's subtree via .Provide().
    /// The reconciler pushes these onto the context scope when entering
    /// this element's subtree and pops them when leaving.
    /// </summary>
    public IReadOnlyDictionary<ContextBase, object?>? ContextValues { get; init; }

    /// <summary>
    /// Gets the attached property data of the specified type, or null if not set.
    /// </summary>
    internal T? GetAttached<T>() where T : class =>
        Attached is not null && Attached.TryGetValue(typeof(T), out var val) ? (T)val : null;

    /// <summary>
    /// Returns a copy of this element with the given attached property data set.
    /// Used by Grid/Canvas/RelativePanel extension methods.
    /// </summary>
    internal Element SetAttached(object data)
    {
        var dict = Attached is not null
            ? new Dictionary<Type, object>(Attached)
            : new Dictionary<Type, object>();
        dict[data.GetType()] = data;
        return this with { Attached = dict };
    }

    /// <summary>
    /// Convenience: implicitly convert a string to a TextBlockElement.
    /// Allows writing: VStack("Hello", "World") instead of VStack(Text("Hello"), Text("World"))
    /// </summary>
    public static implicit operator Element(string text) => new TextBlockElement(text);

    // ════════════════════════════════════════════════════════════════════════
    //  Fast structural comparison for reconciler short-circuit
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// True if this element exposes any non-null event-handler delegate (OnClick,
    /// OnChanged, etc.). Two roles:
    ///
    /// 1. When the reconciler takes a skip fast-path, it must still refresh the
    ///    control's Tag so the event trampoline dispatches into the current
    ///    render's closure rather than a stale one. Handler-free elements don't
    ///    need the Tag refresh — their controls never fire into Reactor code.
    ///
    /// 2. Callback *presence* is part of the skip invariant: when
    ///    <c>oldEl.HasCallbacks != newEl.HasCallbacks</c>, skipping is unsafe
    ///    because <see cref="ShallowEquals"/> intentionally ignores delegate
    ///    identity — a null→non-null transition wouldn't trigger the lazy-wire
    ///    path in UpdateXxx, so the WinRT event would never be subscribed.
    ///    The skip fast-paths therefore guard on this equality.
    ///
    /// Override on each callback-bearing leaf.
    /// </summary>
    internal virtual bool HasCallbacks => false;

    /// <summary>
    /// Returns true if two elements are structurally identical AND the child can be
    /// completely skipped during reconciliation (no need to call Update at all).
    /// This is stricter than ShallowEquals: elements with ThemeBindings must still
    /// go through Update so bindings can be re-evaluated against the current theme,
    /// and a change in callback *presence* must run Update so the lazy-wire path
    /// can subscribe to the WinRT event on a null→non-null transition.
    /// IMPORTANT: keep in sync with the ShallowEquals fast-path in Reconciler.Update().
    /// </summary>
    internal static bool CanSkipUpdate(Element oldEl, Element newEl)
        => ShallowEquals(oldEl, newEl)
            && newEl.ThemeBindings is null
            && oldEl.HasCallbacks == newEl.HasCallbacks;

    /// <summary>
    /// Fast structural comparison that avoids the pitfalls of record Equals
    /// (Dictionary reference equality, Action[] reference equality, delegate equality).
    /// Returns true only when the two elements are provably identical for rendering purposes.
    /// Conservative: returns false for unknown element types.
    /// </summary>
    internal static bool ShallowEquals(Element a, Element b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.GetType() != b.GetType()) return false;
        if (!ModifiersEqual(a.Modifiers, b.Modifiers)) return false;
        if (!AttachedEqual(a.Attached, b.Attached)) return false;
        if (!ThemeBindingsEqual(a.ThemeBindings, b.ThemeBindings)) return false;
        if (!ContextValuesEqual(a.ContextValues, b.ContextValues)) return false;

        return (a, b) switch
        {
            (TextBlockElement ta, TextBlockElement tb) =>
                ta.Content == tb.Content
                && ta.FontSize == tb.FontSize
                && ta.Weight == tb.Weight
                && ta.FontStyle == tb.FontStyle
                && ta.HorizontalAlignment == tb.HorizontalAlignment
                && ReferenceEquals(ta.Setters, tb.Setters),

            // Callbacks (OnClick, OnChanged, etc.) are intentionally not compared:
            // dispatch goes through the Tag trampoline, and the Update.cs skip path
            // refreshes Tag when HasCallbacks is true. So identity of the delegate
            // on the element is irrelevant to dispatch correctness — only presence
            // mattered historically (and presence is still captured by HasCallbacks).
            (ButtonElement ba, ButtonElement bb) =>
                ba.Label == bb.Label
                && ba.IsEnabled == bb.IsEnabled
                && ba.ContentElement is null && bb.ContentElement is null
                && ReferenceEquals(ba.Setters, bb.Setters),

            (HyperlinkButtonElement ha, HyperlinkButtonElement hb) =>
                ha.Content == hb.Content
                && ha.NavigateUri == hb.NavigateUri
                && ReferenceEquals(ha.Setters, hb.Setters),

            (RepeatButtonElement ra, RepeatButtonElement rb) =>
                ra.Label == rb.Label
                && ra.Delay == rb.Delay
                && ra.Interval == rb.Interval
                && ReferenceEquals(ra.Setters, rb.Setters),

            (ToggleButtonElement ta, ToggleButtonElement tb) =>
                ta.Label == tb.Label
                && ta.IsChecked == tb.IsChecked
                && ReferenceEquals(ta.Setters, tb.Setters),

            (SliderElement sa, SliderElement sb) =>
                sa.Value == sb.Value
                && sa.Min == sb.Min
                && sa.Max == sb.Max
                && sa.StepFrequency == sb.StepFrequency
                && sa.Header == sb.Header
                && ReferenceEquals(sa.Setters, sb.Setters),

            (ToggleSwitchElement ta, ToggleSwitchElement tb) =>
                ta.IsOn == tb.IsOn
                && ta.OnContent == tb.OnContent
                && ta.OffContent == tb.OffContent
                && ta.Header == tb.Header
                && ReferenceEquals(ta.Setters, tb.Setters),

            (CheckBoxElement ca, CheckBoxElement cb) =>
                ca.IsChecked == cb.IsChecked
                && ca.Label == cb.Label
                && ca.IsThreeState == cb.IsThreeState
                && ca.CheckedState == cb.CheckedState
                && ReferenceEquals(ca.Setters, cb.Setters),

            (RadioButtonElement ra, RadioButtonElement rb) =>
                ra.Label == rb.Label
                && ra.IsChecked == rb.IsChecked
                && ra.GroupName == rb.GroupName
                && ReferenceEquals(ra.Setters, rb.Setters),

            (ComboBoxElement ca, ComboBoxElement cb) =>
                ReferenceEquals(ca.Items, cb.Items)
                && ca.SelectedIndex == cb.SelectedIndex
                && ca.PlaceholderText == cb.PlaceholderText
                && ca.Header == cb.Header
                && ca.IsEditable == cb.IsEditable
                && ReferenceEquals(ca.ItemElements, cb.ItemElements)
                && ReferenceEquals(ca.Setters, cb.Setters),

            (TextFieldElement ta, TextFieldElement tb) =>
                ta.Value == tb.Value
                && ta.Placeholder == tb.Placeholder
                && ta.Header == tb.Header
                && ta.IsReadOnly == tb.IsReadOnly
                && ta.AcceptsReturn == tb.AcceptsReturn
                && ta.TextWrapping == tb.TextWrapping
                && ta.SelectionStart == tb.SelectionStart
                && ta.SelectionLength == tb.SelectionLength
                && ReferenceEquals(ta.Setters, tb.Setters),

            (NumberBoxElement na, NumberBoxElement nb) =>
                na.Value == nb.Value
                && na.Minimum == nb.Minimum
                && na.Maximum == nb.Maximum
                && na.SmallChange == nb.SmallChange
                && na.LargeChange == nb.LargeChange
                && na.Header == nb.Header
                && na.PlaceholderText == nb.PlaceholderText
                && na.SpinButtonPlacement == nb.SpinButtonPlacement
                && ReferenceEquals(na.Setters, nb.Setters),

            (PasswordBoxElement pa, PasswordBoxElement pb) =>
                pa.Password == pb.Password
                && pa.PlaceholderText == pb.PlaceholderText
                && ReferenceEquals(pa.Setters, pb.Setters),

            (ProgressElement pa, ProgressElement pb) =>
                pa.Value == pb.Value
                && pa.Minimum == pb.Minimum
                && pa.Maximum == pb.Maximum
                && pa.ShowError == pb.ShowError
                && pa.ShowPaused == pb.ShowPaused
                && ReferenceEquals(pa.Setters, pb.Setters),

            (ProgressRingElement pa, ProgressRingElement pb) =>
                pa.Value == pb.Value
                && pa.Minimum == pb.Minimum
                && pa.Maximum == pb.Maximum
                && pa.IsActive == pb.IsActive
                && ReferenceEquals(pa.Setters, pb.Setters),

            (ImageElement ia, ImageElement ib) =>
                ia.Source == ib.Source
                && ReferenceEquals(ia.Setters, ib.Setters),

            (RectangleElement ra, RectangleElement rb) =>
                ReferenceEquals(ra.Setters, rb.Setters),

            (EllipseElement ea, EllipseElement eb) =>
                ReferenceEquals(ea.Setters, eb.Setters),

            // Chart primitives — emitted in bulk by D3Charts. Without these arms,
            // every Path/Line in a chart falls through to UpdatePath/UpdateLine on
            // every parent render even when chart data is unchanged, so D3Charts.Brush
            // re-allocations cause every WinUI Path/Line property to be reassigned.
            (PathElement pa, PathElement pb) =>
                string.Equals(pa.PathDataString, pb.PathDataString, StringComparison.Ordinal)
                && (pa.PathDataString is not null || ReferenceEquals(pa.Data, pb.Data))
                && BrushesEqual(pa.Fill, pb.Fill)
                && BrushesEqual(pa.Stroke, pb.Stroke)
                && pa.StrokeThickness == pb.StrokeThickness
                && ReferenceEquals(pa.StrokeDashArray, pb.StrokeDashArray)
                && TransformsEqual(pa.RenderTransform, pb.RenderTransform)
                && pa.Setters.Length == 0 && pb.Setters.Length == 0,

            (LineElement la, LineElement lb) =>
                la.X1 == lb.X1 && la.Y1 == lb.Y1 && la.X2 == lb.X2 && la.Y2 == lb.Y2
                && BrushesEqual(la.Stroke, lb.Stroke)
                && la.StrokeThickness == lb.StrokeThickness
                && la.Setters.Length == 0 && lb.Setters.Length == 0,

            (RichTextBlockElement ra, RichTextBlockElement rb) =>
                ra.Text == rb.Text
                && ra.FontSize == rb.FontSize
                && ra.IsTextSelectionEnabled == rb.IsTextSelectionEnabled
                && ra.TextWrapping == rb.TextWrapping
                && ParagraphsEqual(ra.Paragraphs, rb.Paragraphs)
                && ReferenceEquals(ra.Setters, rb.Setters),

            // Container elements: compare own props + children by reference.
            // Same children reference = truly unchanged subtree = safe to skip entirely.
            // Different children reference = fall through to UpdateXxx which recurses.
            (StackElement sa, StackElement sb) =>
                sa.Orientation == sb.Orientation
                && sa.Spacing == sb.Spacing
                && sa.HorizontalAlignment == sb.HorizontalAlignment
                && sa.VerticalAlignment == sb.VerticalAlignment
                && ReferenceEquals(sa.Children, sb.Children)
                && ReferenceEquals(sa.Setters, sb.Setters),

            (BorderElement ba, BorderElement bb) =>
                BrushesEqual(ba.Background, bb.Background)
                && BrushesEqual(ba.BorderBrush, bb.BorderBrush)
                && ba.CornerRadius == bb.CornerRadius
                && ba.BorderThickness == bb.BorderThickness
                && ReferenceEquals(ba.Child, bb.Child)
                && ReferenceEquals(ba.Setters, bb.Setters),

            (GridElement ga, GridElement gb) =>
                ga.RowSpacing == gb.RowSpacing
                && ga.ColumnSpacing == gb.ColumnSpacing
                && ReferenceEquals(ga.Definition, gb.Definition)
                && ReferenceEquals(ga.Children, gb.Children)
                && ReferenceEquals(ga.Setters, gb.Setters),

            (ScrollViewElement sva, ScrollViewElement svb) =>
                sva.Orientation == svb.Orientation
                && sva.HorizontalScrollBarVisibility == svb.HorizontalScrollBarVisibility
                && sva.VerticalScrollBarVisibility == svb.VerticalScrollBarVisibility
                && sva.HorizontalScrollMode == svb.HorizontalScrollMode
                && sva.VerticalScrollMode == svb.VerticalScrollMode
                && sva.ZoomMode == svb.ZoomMode
                && ReferenceEquals(sva.Child, svb.Child)
                && ReferenceEquals(sva.Setters, svb.Setters),

            (FlexElement fa, FlexElement fb) =>
                fa.Direction == fb.Direction
                && fa.JustifyContent == fb.JustifyContent
                && fa.AlignItems == fb.AlignItems
                && fa.AlignContent == fb.AlignContent
                && fa.Wrap == fb.Wrap
                && fa.ColumnGap == fb.ColumnGap
                && fa.RowGap == fb.RowGap
                && fa.FlexPadding == fb.FlexPadding
                && ReferenceEquals(fa.Children, fb.Children)
                && ReferenceEquals(fa.Setters, fb.Setters),

            (CanvasElement ca, CanvasElement cb) =>
                ca.Width == cb.Width
                && ca.Height == cb.Height
                && BrushesEqual(ca.Background, cb.Background)
                && ReferenceEquals(ca.Children, cb.Children)
                && ReferenceEquals(ca.ChartData, cb.ChartData)
                && ReferenceEquals(ca.CustomPalette, cb.CustomPalette)
                && ca.IsColorOnly == cb.IsColorOnly
                && ca.IsRawColors == cb.IsRawColors
                && ca.IsInteractive == cb.IsInteractive
                && ca.IsKeyboardDisabled == cb.IsKeyboardDisabled
                && ca.IsTightHitTest == cb.IsTightHitTest
                && ca.IsAnnounceEveryFrame == cb.IsAnnounceEveryFrame
                && ca.CustomFocusColor == cb.CustomFocusColor
                && ca.Setters.Length == 0 && cb.Setters.Length == 0,

            (EmptyElement, EmptyElement) => true,

            // ErrorBoundary contains delegates — always update
            (ErrorBoundaryElement, ErrorBoundaryElement) => false,

            // Conservative: unknown element types always update
            _ => false,
        };
    }

    /// <summary>
    /// Like ShallowEquals but for container types, ignores child/children references.
    /// Returns true when the element's own WinUI-mapped properties are unchanged,
    /// meaning the only reason Update was entered is to recurse into children.
    /// Used by the highlight overlay to avoid marking containers yellow when only
    /// their children changed (the children themselves will be individually captured).
    /// Conservative: returns false for unknown/non-container types (assume props changed).
    /// </summary>
    internal static bool OwnPropsEqual(Element a, Element b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.GetType() != b.GetType()) return false;

        return (a, b) switch
        {
            // Container types: same checks as ShallowEquals minus Children/Child refs
            (StackElement sa, StackElement sb) =>
                sa.Orientation == sb.Orientation
                && sa.Spacing == sb.Spacing
                && sa.HorizontalAlignment == sb.HorizontalAlignment
                && sa.VerticalAlignment == sb.VerticalAlignment
                && ReferenceEquals(sa.Setters, sb.Setters),

            (Core.GridElement ga, Core.GridElement gb) =>
                ga.RowSpacing == gb.RowSpacing
                && ga.ColumnSpacing == gb.ColumnSpacing
                && ReferenceEquals(ga.Definition, gb.Definition)
                && ReferenceEquals(ga.Setters, gb.Setters),

            (BorderElement ba, BorderElement bb) =>
                BrushesEqual(ba.Background, bb.Background)
                && BrushesEqual(ba.BorderBrush, bb.BorderBrush)
                && ba.CornerRadius == bb.CornerRadius
                && ba.Padding == bb.Padding
                && ba.BorderThickness == bb.BorderThickness
                && ReferenceEquals(ba.Setters, bb.Setters),

            (ScrollViewElement sva, ScrollViewElement svb) =>
                sva.Orientation == svb.Orientation
                && sva.HorizontalScrollBarVisibility == svb.HorizontalScrollBarVisibility
                && sva.VerticalScrollBarVisibility == svb.VerticalScrollBarVisibility
                && sva.HorizontalScrollMode == svb.HorizontalScrollMode
                && sva.VerticalScrollMode == svb.VerticalScrollMode
                && sva.ZoomMode == svb.ZoomMode
                && ReferenceEquals(sva.Setters, svb.Setters),

            (FlexElement fa, FlexElement fb) =>
                fa.Direction == fb.Direction
                && fa.JustifyContent == fb.JustifyContent
                && fa.AlignItems == fb.AlignItems
                && fa.AlignContent == fb.AlignContent
                && fa.Wrap == fb.Wrap
                && fa.ColumnGap == fb.ColumnGap
                && fa.RowGap == fb.RowGap
                && fa.FlexPadding == fb.FlexPadding
                && ReferenceEquals(fa.Setters, fb.Setters),

            (CanvasElement ca, CanvasElement cb) =>
                ReferenceEquals(ca.Setters, cb.Setters),

            (WrapGridElement wa, WrapGridElement wb) =>
                wa.Orientation == wb.Orientation
                && wa.ItemWidth == wb.ItemWidth
                && wa.ItemHeight == wb.ItemHeight
                && wa.MaximumRowsOrColumns == wb.MaximumRowsOrColumns
                && ReferenceEquals(wa.Setters, wb.Setters),

            (RelativePanelElement ra, RelativePanelElement rb) =>
                ReferenceEquals(ra.Setters, rb.Setters),

            (ViewboxElement va, ViewboxElement vb) =>
                ReferenceEquals(va.Setters, vb.Setters),

            // Structural wrappers that only contain children
            (NavigationHostElement, NavigationHostElement) => true,
            (CommandHostElement, CommandHostElement) => true,
            (PopupElement pa, PopupElement pb) =>
                pa.IsOpen == pb.IsOpen
                && pa.IsLightDismissEnabled == pb.IsLightDismissEnabled,

            // TitleBar: own-props check (ignore Content/RightHeader slots which
            // recurse as children). Without this, TitleBar flashes yellow on
            // every reconcile even when only descendants changed.
            (TitleBarElement ta, TitleBarElement tb) =>
                ta.Title == tb.Title
                && ta.Subtitle == tb.Subtitle
                && ta.IsBackButtonVisible == tb.IsBackButtonVisible
                && ta.IsBackButtonEnabled == tb.IsBackButtonEnabled
                && ta.IsPaneToggleButtonVisible == tb.IsPaneToggleButtonVisible
                && ReferenceEquals(ta.Setters, tb.Setters),

            // Pure composition wrappers — they never write their own WinUI
            // properties; their rendered output is diffed separately. Returning
            // true here prevents the overlay from flashing the entire content
            // block every time the component re-renders.
            (ComponentElement, ComponentElement) => true,
            (FuncElement, FuncElement) => true,
            (MemoElement, MemoElement) => true,
            (ModifiedElement, ModifiedElement) => true,
            (GroupElement, GroupElement) => true,
            (ErrorBoundaryElement, ErrorBoundaryElement) => true,

            // MenuFlyout attaches a flyout to its Target but doesn't have its
            // own WinUI props that change across renders.
            (MenuFlyoutElement, MenuFlyoutElement) => true,
            (ContentFlyoutElement, ContentFlyoutElement) => true,
            (MenuFlyoutContentElement, MenuFlyoutContentElement) => true,
            (FlyoutElement, FlyoutElement) => true,

            // Collection-style elements: compare own props only (SelectedIndex,
            // mode flags, header). Item/children arrays are compared separately
            // in ShallowEquals via ReferenceEquals — a fresh items array does
            // NOT mean own props changed, so the highlight overlay should not
            // light up the ComboBox/ListView/etc. when only the authored items
            // projection allocated a new array.
            (ComboBoxElement ca, ComboBoxElement cb) =>
                ca.SelectedIndex == cb.SelectedIndex
                && ca.PlaceholderText == cb.PlaceholderText
                && ca.Header == cb.Header
                && ca.IsEditable == cb.IsEditable
                && ReferenceEquals(ca.Setters, cb.Setters),

            (ListViewElement la, ListViewElement lb) =>
                la.SelectedIndex == lb.SelectedIndex
                && la.SelectionMode == lb.SelectionMode
                && la.Header == lb.Header
                && ReferenceEquals(la.Setters, lb.Setters),

            (GridViewElement ga, GridViewElement gb) =>
                ga.SelectedIndex == gb.SelectedIndex
                && ga.SelectionMode == gb.SelectionMode
                && ga.Header == gb.Header
                && ReferenceEquals(ga.Setters, gb.Setters),

            (FlipViewElement fa, FlipViewElement fb) =>
                fa.SelectedIndex == fb.SelectedIndex
                && ReferenceEquals(fa.Setters, fb.Setters),

            (PivotElement pa, PivotElement pb) =>
                pa.SelectedIndex == pb.SelectedIndex
                && pa.Title == pb.Title
                && ReferenceEquals(pa.Setters, pb.Setters),

            (TabViewElement ta, TabViewElement tb) =>
                ta.SelectedIndex == tb.SelectedIndex
                && ta.IsAddTabButtonVisible == tb.IsAddTabButtonVisible
                && ReferenceEquals(ta.Setters, tb.Setters),

            (TreeViewElement ta, TreeViewElement tb) =>
                ta.SelectionMode == tb.SelectionMode
                && ta.CanDragItems == tb.CanDragItems
                && ta.AllowDrop == tb.AllowDrop
                && ta.CanReorderItems == tb.CanReorderItems
                && ReferenceEquals(ta.Setters, tb.Setters),

            (SelectorBarElement sa, SelectorBarElement sb) =>
                sa.SelectedIndex == sb.SelectedIndex
                && ReferenceEquals(sa.Setters, sb.Setters),

            (ListBoxElement la, ListBoxElement lb) =>
                la.SelectedIndex == lb.SelectedIndex
                && ReferenceEquals(la.Setters, lb.Setters),

            (RadioButtonsElement ra, RadioButtonsElement rb) =>
                ra.SelectedIndex == rb.SelectedIndex
                && ra.Header == rb.Header
                && ReferenceEquals(ra.Setters, rb.Setters),

            (BreadcrumbBarElement ba, BreadcrumbBarElement bb) =>
                ReferenceEquals(ba.Setters, bb.Setters),

            // Templated (data-driven) collections: own props are the WinUI
            // properties UpdateTemplatedXxx writes back. Items + ViewBuilder
            // are not own props — they drive child reconcile but don't write
            // properties on the parent control. Without this case, the typed
            // ListView<T>/GridView<T>/FlipView<T> falls through to false and
            // the highlight overlay flashes the whole list on every parent
            // re-render (because OwnPropsEqual returning false is the gate
            // for ReconcileHighlightOverlay's "modified" tag).
            (TemplatedListElementBase ta, TemplatedListElementBase tb) =>
                ta.GetSelectedIndex() == tb.GetSelectedIndex()
                && ta.GetSelectionMode() == tb.GetSelectionMode()
                && ta.GetHeader() == tb.GetHeader()
                && ta.GetIsItemClickEnabled() == tb.GetIsItemClickEnabled()
                && !ta.HasSetters && !tb.HasSetters,

            // Lazy (virtualized) stacks: same rationale — Items/ViewBuilder
            // are factory inputs, not control properties.
            (LazyStackElementBase la, LazyStackElementBase lb) =>
                la.Orientation == lb.Orientation
                && la.Spacing == lb.Spacing
                && la.EstimatedItemSize == lb.EstimatedItemSize
                && ReferenceEquals(la.ScrollViewerSetters, lb.ScrollViewerSetters)
                && ReferenceEquals(la.RepeaterSetters, lb.RepeaterSetters),

            // Non-container / leaf types: return false → always captured
            _ => false,
        };
    }

    /// <summary>
    /// Structural comparison of RichTextParagraph arrays.
    /// Compares each paragraph's inlines using record equality.
    /// </summary>
    private static bool ParagraphsEqual(RichTextParagraph[]? a, RichTextParagraph[]? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!ParagraphEqual(a[i], b[i])) return false;
        }
        return true;
    }

    /// <summary>
    /// Structural comparison of a single RichTextParagraph (inline-by-inline record equality).
    /// </summary>
    internal static bool ParagraphEqual(RichTextParagraph a, RichTextParagraph b)
    {
        if (ReferenceEquals(a, b)) return true;
        var ai = a.Inlines;
        var bi = b.Inlines;
        if (ai.Length != bi.Length) return false;
        for (int j = 0; j < ai.Length; j++)
        {
            if (!ai[j].Equals(bi[j])) return false;
        }
        return true;
    }

    /// <summary>
    /// Structural brush comparison. BrushHelper.Parse caches the parsed Color
    /// but returns a fresh SolidColorBrush instance on every call (Brushes have
    /// thread affinity), so ReferenceEquals always fails for ".Background("#x")"
    /// style fluent chains. Unwrap the underlying Color for the common
    /// SolidColorBrush case and fall back to ReferenceEquals for everything else.
    /// </summary>
    private static bool BrushesEqual(Brush? a, Brush? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a is SolidColorBrush sa && b is SolidColorBrush sb)
            return sa.Color == sb.Color && sa.Opacity == sb.Opacity;
        return false;
    }

    /// <summary>
    /// Structural transform comparison. D3PathTranslated allocates a fresh
    /// TranslateTransform on every render even when X/Y match, so reference
    /// equality always fails for the common chart case. Unwrap TranslateTransform
    /// and fall back to ReferenceEquals for everything else.
    /// </summary>
    private static bool TransformsEqual(Transform? a, Transform? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a is TranslateTransform ta && b is TranslateTransform tb)
            return ta.X == tb.X && ta.Y == tb.Y;
        return false;
    }

    private static bool FontFamiliesEqual(Microsoft.UI.Xaml.Media.FontFamily? a, Microsoft.UI.Xaml.Media.FontFamily? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return a.Source == b.Source;
    }

    /// <summary>
    /// Compare two ElementModifiers for rendering equivalence.
    /// Brushes and FontFamily are compared structurally because fluent helpers
    /// (<c>.Background("#color")</c>, <c>.FontFamily("Segoe UI")</c>) allocate
    /// fresh instances on every render even when the underlying values match.
    /// Ignores OnMountAction (only runs at mount time, not during update).
    /// </summary>
    internal static bool ModifiersEqual(ElementModifiers? a, ElementModifiers? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        return a.Margin == b.Margin
            && a.Padding == b.Padding
            && a.Width == b.Width
            && a.Height == b.Height
            && a.MinWidth == b.MinWidth
            && a.MinHeight == b.MinHeight
            && a.MaxWidth == b.MaxWidth
            && a.MaxHeight == b.MaxHeight
            && a.HorizontalAlignment == b.HorizontalAlignment
            && a.VerticalAlignment == b.VerticalAlignment
            && a.Opacity == b.Opacity
            && a.IsVisible == b.IsVisible
            && a.IsEnabled == b.IsEnabled
            && a.CornerRadius == b.CornerRadius
            && a.BorderThickness == b.BorderThickness
            && a.ElementSoundMode == b.ElementSoundMode
            && a.ToolTip == b.ToolTip
            && a.AutomationName == b.AutomationName
            && a.AutomationId == b.AutomationId
            && BrushesEqual(a.Background, b.Background)
            && BrushesEqual(a.Foreground, b.Foreground)
            && BrushesEqual(a.BorderBrush, b.BorderBrush)
            && a.FontSize == b.FontSize
            && a.FontWeight == b.FontWeight
            && FontFamiliesEqual(a.FontFamily, b.FontFamily)
            // Skip OnMountAction — only runs at mount time
            // Skip event handlers — delegate comparison is unreliable, conservative false
            && a.OnSizeChanged is null && b.OnSizeChanged is null
            && a.OnPointerPressed is null && b.OnPointerPressed is null
            && a.OnPointerMoved is null && b.OnPointerMoved is null
            && a.OnPointerReleased is null && b.OnPointerReleased is null
            && a.OnTapped is null && b.OnTapped is null
            && a.OnKeyDown is null && b.OnKeyDown is null
            // Skip RichToolTip, AttachedFlyout, ContextFlyout — rare, conservative false
            && a.RichToolTip is null && b.RichToolTip is null
            && a.AttachedFlyout is null && b.AttachedFlyout is null
            && a.ContextFlyout is null && b.ContextFlyout is null
            // Accessibility Tier 1
            && a.HeadingLevel == b.HeadingLevel
            && a.IsTabStop == b.IsTabStop
            && a.TabIndex == b.TabIndex
            && a.AccessKey == b.AccessKey
            // Accessibility Tier 2/3. AccessibilityModifiers is a record of
            // scalar/string fields, but every fluent helper (.AccessibilityView,
            // .LiveRegion, .ItemStatus, …) allocates a fresh instance per render
            // — so reference equality always fails for elements that set any
            // accessibility modifier, even when the values are unchanged.
            // Falsely missing this match cascades into the reconcile-highlight
            // overlay, which paints those elements as "modified" every render.
            // Use record value-equality instead.
            && AccessibilityEqual(a.Accessibility, b.Accessibility);
    }

    private static bool AccessibilityEqual(AccessibilityModifiers? a, AccessibilityModifiers? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return a.Equals(b);
    }

    /// <summary>
    /// Compare two Attached property dictionaries by content.
    /// Common case: both have a single GridAttached entry (a record with structural equality).
    /// </summary>
    internal static bool AttachedEqual(IReadOnlyDictionary<Type, object>? a, IReadOnlyDictionary<Type, object>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;

        foreach (var (key, valA) in a)
        {
            if (!b.TryGetValue(key, out var valB)) return false;
            if (!Equals(valA, valB)) return false; // GridAttached is a record — Equals works
        }
        return true;
    }

    internal static bool ThemeBindingsEqual(IReadOnlyDictionary<string, ThemeRef>? a, IReadOnlyDictionary<string, ThemeRef>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;

        foreach (var (key, valA) in a)
        {
            if (!b.TryGetValue(key, out var valB)) return false;
            if (valA.ResourceKey != valB.ResourceKey) return false;
        }
        return true;
    }

    internal static bool ContextValuesEqual(IReadOnlyDictionary<ContextBase, object?>? a, IReadOnlyDictionary<ContextBase, object?>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;

        foreach (var (key, valA) in a)
        {
            if (!b.TryGetValue(key, out var valB)) return false;
            if (!Equals(valA, valB)) return false;
        }
        return true;
    }
}

/// <summary>
/// An element that renders nothing (used for conditional rendering).
/// </summary>
public record EmptyElement : Element
{
    public static readonly EmptyElement Instance = new();
}

/// <summary>
/// A transparent grouping element (like React's Fragment). Does not introduce
/// any layout container — its children are flattened into the parent.
/// Produced by <c>ForEach</c> and <c>Group()</c> in the DSL.
/// </summary>
public record GroupElement(Element[] Children) : Element;

/// <summary>
/// Catches render errors in its child subtree and displays fallback UI.
/// Like React's ErrorBoundary — catches errors during rendering, not event handlers.
/// When the ErrorBoundary re-renders, it retries the child (error recovery).
/// </summary>
public record ErrorBoundaryElement(Element Child, Func<Exception, Element> Fallback) : Element;

/// <summary>
/// Wraps any element with layout modifiers (margin, alignment, size, etc.).
/// Kept for backward compatibility. New code stores modifiers inline on Element.Modifiers.
/// </summary>
public record ModifiedElement(Element Inner, ElementModifiers WrappedModifiers) : Element;

/// <summary>
/// Wraps a Component class so it can participate in the element tree.
/// Created automatically by Component&lt;T&gt;() factory method.
/// </summary>
public record ComponentElement(
    [property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    Type ComponentType,
    object? Props = null) : Element
{
    // Factory creates the component instance without reflection. Stored as a field
    // so it does not participate in record equality (two ComponentElements for the
    // same Type/Props are equal regardless of factory identity).
    internal Func<Component>? _factory;

    internal Component CreateInstance() =>
        _factory is not null ? _factory() : (Component)Activator.CreateInstance(ComponentType)!;
}

/// <summary>
/// A component defined inline via a render function (like a React function component).
/// </summary>
public record FuncElement(Func<RenderContext, Element> RenderFunc) : Element;

/// <summary>
/// A memoized function component. Skips re-render when Dependencies haven't changed.
/// null Dependencies = render once on mount + self-triggered state changes only.
/// </summary>
public record MemoElement(Func<RenderContext, Element> RenderFunc, object?[]? Dependencies = null) : Element;

// ════════════════════════════════════════════════════════════════════════
//  Semantic wrapper for composite accessibility
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Describes the semantic role, value, and range of a composite component
/// for assistive technology. Used with the .Semantics() modifier.
/// </summary>
public record SemanticDescription(
    string? Role = null,
    string? Value = null,
    double? RangeMin = null,
    double? RangeMax = null,
    double? RangeValue = null,
    bool IsReadOnly = true);

/// <summary>
/// Wraps a child element in a SemanticPanel that provides custom automation
/// semantics to screen readers. This solves the problem where Reactor components
/// can't override OnCreateAutomationPeer().
/// </summary>
public record SemanticElement(Element Child, SemanticDescription Semantics) : Element;

public record ElementModifiers
{
    // ── Bucketed sub-records (spec 034 §A) ──────────────────────────
    // Layout / Visual fields are stored in dedicated sub-records so that the
    // common case (a cell that sets only Foreground + Padding) allocates two
    // small bucket records instead of bloating the parent ElementModifiers.
    // Public properties for moved fields (Padding, Margin, Width, …,
    // Foreground, Background, …) stay on ElementModifiers as get/init shims
    // that read from / write into the appropriate bucket — call sites see no
    // API change.
    /// <summary>
    /// Layout-bucket sub-record. Set directly only in perf-critical inner
    /// loops; ordinary code uses the field shim properties (Padding, Margin,
    /// Width, …) and never observes this slot.
    /// </summary>
    /// <remarks>Spec 034 §A.</remarks>
    public LayoutModifiers? Layout { get; init; }
    /// <summary>
    /// Visual-bucket sub-record. Set directly only in perf-critical inner
    /// loops; ordinary code uses the field shim properties (Foreground,
    /// Background, BorderBrush, …) and never observes this slot.
    /// </summary>
    /// <remarks>Spec 034 §A.</remarks>
    public VisualModifiers? Visual { get; init; }

    public Thickness? Margin
    {
        get => Layout?.Margin;
        init => Layout = Layout is null ? new LayoutModifiers { Margin = value } : Layout with { Margin = value };
    }
    public Thickness? Padding
    {
        get => Layout?.Padding;
        init => Layout = Layout is null ? new LayoutModifiers { Padding = value } : Layout with { Padding = value };
    }
    public double? Width
    {
        get => Layout?.Width;
        init => Layout = Layout is null ? new LayoutModifiers { Width = value } : Layout with { Width = value };
    }
    public double? Height
    {
        get => Layout?.Height;
        init => Layout = Layout is null ? new LayoutModifiers { Height = value } : Layout with { Height = value };
    }
    public double? MinWidth
    {
        get => Layout?.MinWidth;
        init => Layout = Layout is null ? new LayoutModifiers { MinWidth = value } : Layout with { MinWidth = value };
    }
    public double? MinHeight
    {
        get => Layout?.MinHeight;
        init => Layout = Layout is null ? new LayoutModifiers { MinHeight = value } : Layout with { MinHeight = value };
    }
    public double? MaxWidth
    {
        get => Layout?.MaxWidth;
        init => Layout = Layout is null ? new LayoutModifiers { MaxWidth = value } : Layout with { MaxWidth = value };
    }
    public double? MaxHeight
    {
        get => Layout?.MaxHeight;
        init => Layout = Layout is null ? new LayoutModifiers { MaxHeight = value } : Layout with { MaxHeight = value };
    }
    public HorizontalAlignment? HorizontalAlignment
    {
        get => Layout?.HorizontalAlignment;
        init => Layout = Layout is null ? new LayoutModifiers { HorizontalAlignment = value } : Layout with { HorizontalAlignment = value };
    }
    public VerticalAlignment? VerticalAlignment
    {
        get => Layout?.VerticalAlignment;
        init => Layout = Layout is null ? new LayoutModifiers { VerticalAlignment = value } : Layout with { VerticalAlignment = value };
    }
    public double? Opacity
    {
        get => Visual?.Opacity;
        init => Visual = Visual is null ? new VisualModifiers { Opacity = value } : Visual with { Opacity = value };
    }
    public global::System.Numerics.Vector3? Scale
    {
        get => Visual?.Scale;
        init => Visual = Visual is null ? new VisualModifiers { Scale = value } : Visual with { Scale = value };
    }
    public float? Rotation
    {
        get => Visual?.Rotation;
        init => Visual = Visual is null ? new VisualModifiers { Rotation = value } : Visual with { Rotation = value };
    }
    public global::System.Numerics.Vector3? Translation
    {
        get => Visual?.Translation;
        init => Visual = Visual is null ? new VisualModifiers { Translation = value } : Visual with { Translation = value };
    }
    public global::System.Numerics.Vector3? CenterPoint
    {
        get => Visual?.CenterPoint;
        init => Visual = Visual is null ? new VisualModifiers { CenterPoint = value } : Visual with { CenterPoint = value };
    }
    public bool? IsVisible
    {
        get => Layout?.IsVisible;
        init => Layout = Layout is null ? new LayoutModifiers { IsVisible = value } : Layout with { IsVisible = value };
    }
    public string? ToolTip { get; init; }
    public Element? RichToolTip { get; init; }
    public Element? AttachedFlyout { get; init; }
    public Element? ContextFlyout { get; init; }
    public Brush? Background
    {
        get => Visual?.Background;
        init => Visual = Visual is null ? new VisualModifiers { Background = value } : Visual with { Background = value };
    }
    public Brush? Foreground
    {
        get => Visual?.Foreground;
        init => Visual = Visual is null ? new VisualModifiers { Foreground = value } : Visual with { Foreground = value };
    }
    public bool? IsEnabled { get; init; }
    public Microsoft.UI.Xaml.CornerRadius? CornerRadius
    {
        get => Visual?.CornerRadius;
        init => Visual = Visual is null ? new VisualModifiers { CornerRadius = value } : Visual with { CornerRadius = value };
    }
    public Brush? BorderBrush
    {
        get => Visual?.BorderBrush;
        init => Visual = Visual is null ? new VisualModifiers { BorderBrush = value } : Visual with { BorderBrush = value };
    }
    public Thickness? BorderThickness
    {
        get => Visual?.BorderThickness;
        init => Visual = Visual is null ? new VisualModifiers { BorderThickness = value } : Visual with { BorderThickness = value };
    }
    public string? AutomationName { get; init; }
    public string? AutomationId { get; init; }
    public ElementSoundMode? ElementSoundMode { get; init; }
    public Action<FrameworkElement>? OnMountAction { get; init; }

    // ── Typography (applies to any Control or TextBlock) ────────────
    public FontFamily? FontFamily { get; init; }
    public double? FontSize { get; init; }
    public FontWeight? FontWeight { get; init; }

    // ── Declarative event handlers (re-attached on every update) ────
    public Action<object, SizeChangedEventArgs>? OnSizeChanged { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? OnPointerPressed { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? OnPointerMoved { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? OnPointerReleased { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? OnPointerEntered { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? OnPointerExited { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? OnPointerCanceled { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? OnPointerCaptureLost { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? OnPointerWheelChanged { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs>? OnTapped { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs>? OnDoubleTapped { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs>? OnRightTapped { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.HoldingRoutedEventArgs>? OnHolding { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? OnKeyDown { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? OnKeyUp { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? OnPreviewKeyDown { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? OnPreviewKeyUp { get; init; }
    public Action<UIElement, Microsoft.UI.Xaml.Input.CharacterReceivedRoutedEventArgs>? OnCharacterReceived { get; init; }
    public Action<object, RoutedEventArgs>? OnGotFocus { get; init; }
    public Action<object, RoutedEventArgs>? OnLostFocus { get; init; }

    // ── Declarative gesture recognizers (spec 027 Tier 3) ───────────
    // Drive a single ManipulationStarted/Delta/Completed subscription per element.
    public Microsoft.UI.Reactor.Input.PanGestureConfig? Pan { get; init; }
    public Microsoft.UI.Reactor.Input.PinchGestureConfig? Pinch { get; init; }
    public Microsoft.UI.Reactor.Input.RotateGestureConfig? Rotate { get; init; }
    public Microsoft.UI.Reactor.Input.LongPressGestureConfig? LongPress { get; init; }

    // ── Drag-and-drop (spec 027 Tier 6 — Phase 6a typed in-process) ─
    public Microsoft.UI.Reactor.Input.DragSourceConfig? DragSource { get; init; }
    public Microsoft.UI.Reactor.Input.DropTargetConfig? DropTarget { get; init; }

    // ── Logical (BiDi-aware) layout properties ──────────────────────
    // These resolve to physical left/right based on FlowDirection at mount/update time.
    // InlineStart = left in LTR, right in RTL. InlineEnd = right in LTR, left in RTL.
    public double? MarginInlineStart
    {
        get => Layout?.MarginInlineStart;
        init => Layout = Layout is null ? new LayoutModifiers { MarginInlineStart = value } : Layout with { MarginInlineStart = value };
    }
    public double? MarginInlineEnd
    {
        get => Layout?.MarginInlineEnd;
        init => Layout = Layout is null ? new LayoutModifiers { MarginInlineEnd = value } : Layout with { MarginInlineEnd = value };
    }
    public double? PaddingInlineStart
    {
        get => Layout?.PaddingInlineStart;
        init => Layout = Layout is null ? new LayoutModifiers { PaddingInlineStart = value } : Layout with { PaddingInlineStart = value };
    }
    public double? PaddingInlineEnd
    {
        get => Layout?.PaddingInlineEnd;
        init => Layout = Layout is null ? new LayoutModifiers { PaddingInlineEnd = value } : Layout with { PaddingInlineEnd = value };
    }
    public Thickness? BorderInlineStart
    {
        get => Layout?.BorderInlineStart;
        init => Layout = Layout is null ? new LayoutModifiers { BorderInlineStart = value } : Layout with { BorderInlineStart = value };
    }

    // ── Theme override ───────────────────────────────────────────────
    /// <summary>
    /// Sets <see cref="FrameworkElement.RequestedTheme"/> on the control,
    /// forcing a subtree to render in a specific theme variant (e.g., dark
    /// sidebar in a light app). Applied before ThemeRef bindings resolve so
    /// that theme resources pick up the correct variant.
    /// </summary>
    public ElementTheme? RequestedTheme
    {
        get => Layout?.RequestedTheme;
        init => Layout = Layout is null ? new LayoutModifiers { RequestedTheme = value } : Layout with { RequestedTheme = value };
    }

    // ── Accessibility — Tier 1 (inline, commonly needed for WCAG AA) ─
    public Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel? HeadingLevel { get; init; }
    public bool? IsTabStop { get; init; }
    public int? TabIndex { get; init; }
    public string? AccessKey { get; init; }
    public Microsoft.UI.Xaml.Input.XYFocusKeyboardNavigationMode? XYFocusKeyboardNavigation { get; init; }
    public Action<UIElement, Microsoft.UI.Xaml.Input.AccessKeyDisplayRequestedEventArgs>? OnAccessKeyDisplayRequested { get; init; }

    /// <summary>
    /// Imperative ref slot (spec 027 Tier 5). The reconciler writes the mounted
    /// <see cref="FrameworkElement"/> into <see cref="Microsoft.UI.Reactor.Input.ElementRef._current"/>
    /// so <c>FocusManager.Focus(ref)</c> (and future ref-based imperative APIs) can target it.
    /// </summary>
    public Microsoft.UI.Reactor.Input.ElementRef? Ref { get; init; }

    // ── SystemBackdrop (spec 033 §6) ────────────────────────────────
    /// <summary>
    /// Declarative system backdrop (Mica / Acrylic) for the host window.
    /// Read by <c>ReactorHost</c> from the root tree's modifiers and applied to
    /// the owning <c>Window.SystemBackdrop</c>. Ignored by <c>ReactorHostControl</c>
    /// that does not own its window.
    /// </summary>
    public BackdropChoice? Backdrop { get; init; }

    // ── Accessibility — Tier 2/3 (lazy sub-record, zero allocation unless used) ─
    public AccessibilityModifiers? Accessibility { get; init; }

    public ElementModifiers Merge(ElementModifiers other)
    {
        // Merge buckets at sub-record level. Naming a shim'd property inside
        // the with { } here would clone the bucket once per moved field
        // (each shim's init re-runs); write Layout / Visual once instead.
        var mergedLayout = other.Layout is not null
            ? (Layout is not null ? Layout.Merge(other.Layout) : other.Layout)
            : Layout;
        var mergedVisual = other.Visual is not null
            ? (Visual is not null ? Visual.Merge(other.Visual) : other.Visual)
            : Visual;

        return this with
        {
            Layout = mergedLayout,
            Visual = mergedVisual,
            ToolTip = other.ToolTip ?? ToolTip,
            RichToolTip = other.RichToolTip ?? RichToolTip,
            AttachedFlyout = other.AttachedFlyout ?? AttachedFlyout,
            ContextFlyout = other.ContextFlyout ?? ContextFlyout,
            IsEnabled = other.IsEnabled ?? IsEnabled,
            AutomationName = other.AutomationName ?? AutomationName,
            AutomationId = other.AutomationId ?? AutomationId,
            ElementSoundMode = other.ElementSoundMode ?? ElementSoundMode,
            OnMountAction = other.OnMountAction ?? OnMountAction,
            FontFamily = other.FontFamily ?? FontFamily,
            FontSize = other.FontSize ?? FontSize,
            FontWeight = other.FontWeight ?? FontWeight,
            OnSizeChanged = other.OnSizeChanged ?? OnSizeChanged,
            OnPointerPressed = other.OnPointerPressed ?? OnPointerPressed,
            OnPointerMoved = other.OnPointerMoved ?? OnPointerMoved,
            OnPointerReleased = other.OnPointerReleased ?? OnPointerReleased,
            OnPointerEntered = other.OnPointerEntered ?? OnPointerEntered,
            OnPointerExited = other.OnPointerExited ?? OnPointerExited,
            OnPointerCanceled = other.OnPointerCanceled ?? OnPointerCanceled,
            OnPointerCaptureLost = other.OnPointerCaptureLost ?? OnPointerCaptureLost,
            OnPointerWheelChanged = other.OnPointerWheelChanged ?? OnPointerWheelChanged,
            OnTapped = other.OnTapped ?? OnTapped,
            OnDoubleTapped = other.OnDoubleTapped ?? OnDoubleTapped,
            OnRightTapped = other.OnRightTapped ?? OnRightTapped,
            OnHolding = other.OnHolding ?? OnHolding,
            OnKeyDown = other.OnKeyDown ?? OnKeyDown,
            OnKeyUp = other.OnKeyUp ?? OnKeyUp,
            OnPreviewKeyDown = other.OnPreviewKeyDown ?? OnPreviewKeyDown,
            OnPreviewKeyUp = other.OnPreviewKeyUp ?? OnPreviewKeyUp,
            OnCharacterReceived = other.OnCharacterReceived ?? OnCharacterReceived,
            OnGotFocus = other.OnGotFocus ?? OnGotFocus,
            OnLostFocus = other.OnLostFocus ?? OnLostFocus,
            Pan = other.Pan ?? Pan,
            Pinch = other.Pinch ?? Pinch,
            Rotate = other.Rotate ?? Rotate,
            LongPress = other.LongPress ?? LongPress,
            DragSource = other.DragSource ?? DragSource,
            DropTarget = other.DropTarget ?? DropTarget,
            HeadingLevel = other.HeadingLevel ?? HeadingLevel,
            IsTabStop = other.IsTabStop ?? IsTabStop,
            TabIndex = other.TabIndex ?? TabIndex,
            AccessKey = other.AccessKey ?? AccessKey,
            XYFocusKeyboardNavigation = other.XYFocusKeyboardNavigation ?? XYFocusKeyboardNavigation,
            OnAccessKeyDisplayRequested = other.OnAccessKeyDisplayRequested ?? OnAccessKeyDisplayRequested,
            Ref = other.Ref ?? Ref,
            Backdrop = other.Backdrop ?? Backdrop,
            Accessibility = other.Accessibility is not null
                ? (Accessibility is not null ? Accessibility.Merge(other.Accessibility) : other.Accessibility)
                : Accessibility,
        };
    }
}

/// <summary>
/// Layout-related modifiers (sizing, alignment, spacing, visibility, theme,
/// logical-direction insets). Stored as a lazy sub-record on
/// <see cref="ElementModifiers"/> so that the common case of a few fields
/// set allocates a small bucket rather than bloating the parent record.
/// Public properties on <see cref="ElementModifiers"/> (Padding, Margin,
/// Width, …) read from / write into this sub-record transparently — most
/// callers never see this type.
/// </summary>
/// <remarks>
/// Spec 034 §A. The field set may grow but won't shrink — additions are
/// always backwards-compatible.
/// </remarks>
public record LayoutModifiers
{
    public Thickness? Margin { get; init; }
    public Thickness? Padding { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public double? MinWidth { get; init; }
    public double? MinHeight { get; init; }
    public double? MaxWidth { get; init; }
    public double? MaxHeight { get; init; }
    public HorizontalAlignment? HorizontalAlignment { get; init; }
    public VerticalAlignment? VerticalAlignment { get; init; }
    public bool? IsVisible { get; init; }
    public double? MarginInlineStart { get; init; }
    public double? MarginInlineEnd { get; init; }
    public double? PaddingInlineStart { get; init; }
    public double? PaddingInlineEnd { get; init; }
    public Thickness? BorderInlineStart { get; init; }
    public ElementTheme? RequestedTheme { get; init; }

    /// <summary>
    /// Merge <paramref name="other"/> into this record, preferring
    /// <paramref name="other"/>'s set fields and falling back to ours.
    /// Mirrors <see cref="ElementModifiers.Merge"/>.
    /// </summary>
    /// <remarks>Spec 034 §A.</remarks>
    public LayoutModifiers Merge(LayoutModifiers other) => this with
    {
        Margin = other.Margin ?? Margin,
        Padding = other.Padding ?? Padding,
        Width = other.Width ?? Width,
        Height = other.Height ?? Height,
        MinWidth = other.MinWidth ?? MinWidth,
        MinHeight = other.MinHeight ?? MinHeight,
        MaxWidth = other.MaxWidth ?? MaxWidth,
        MaxHeight = other.MaxHeight ?? MaxHeight,
        HorizontalAlignment = other.HorizontalAlignment ?? HorizontalAlignment,
        VerticalAlignment = other.VerticalAlignment ?? VerticalAlignment,
        IsVisible = other.IsVisible ?? IsVisible,
        MarginInlineStart = other.MarginInlineStart ?? MarginInlineStart,
        MarginInlineEnd = other.MarginInlineEnd ?? MarginInlineEnd,
        PaddingInlineStart = other.PaddingInlineStart ?? PaddingInlineStart,
        PaddingInlineEnd = other.PaddingInlineEnd ?? PaddingInlineEnd,
        BorderInlineStart = other.BorderInlineStart ?? BorderInlineStart,
        RequestedTheme = other.RequestedTheme ?? RequestedTheme,
    };
}

/// <summary>
/// Visual-related modifiers (brushes, borders, transforms, opacity).
/// Stored as a lazy sub-record on <see cref="ElementModifiers"/> in the
/// same pattern as <see cref="LayoutModifiers"/>. Public properties on
/// <see cref="ElementModifiers"/> (Foreground, Background, BorderBrush, …)
/// shim through.
/// </summary>
/// <remarks>
/// Spec 034 §A. The field set may grow but won't shrink — additions are
/// always backwards-compatible.
/// </remarks>
public record VisualModifiers
{
    public Brush? Background { get; init; }
    public Brush? Foreground { get; init; }
    public Brush? BorderBrush { get; init; }
    public Thickness? BorderThickness { get; init; }
    public Microsoft.UI.Xaml.CornerRadius? CornerRadius { get; init; }
    public double? Opacity { get; init; }
    public global::System.Numerics.Vector3? Scale { get; init; }
    public float? Rotation { get; init; }
    public global::System.Numerics.Vector3? Translation { get; init; }
    public global::System.Numerics.Vector3? CenterPoint { get; init; }

    /// <summary>
    /// Merge <paramref name="other"/> into this record, preferring
    /// <paramref name="other"/>'s set fields and falling back to ours.
    /// </summary>
    /// <remarks>Spec 034 §A.</remarks>
    public VisualModifiers Merge(VisualModifiers other) => this with
    {
        Background = other.Background ?? Background,
        Foreground = other.Foreground ?? Foreground,
        BorderBrush = other.BorderBrush ?? BorderBrush,
        BorderThickness = other.BorderThickness ?? BorderThickness,
        CornerRadius = other.CornerRadius ?? CornerRadius,
        Opacity = other.Opacity ?? Opacity,
        Scale = other.Scale ?? Scale,
        Rotation = other.Rotation ?? Rotation,
        Translation = other.Translation ?? Translation,
        CenterPoint = other.CenterPoint ?? CenterPoint,
    };
}

/// <summary>
/// Advanced accessibility properties (WCAG Tier 2/3). Stored as a lazy sub-record
/// on ElementModifiers to avoid allocating storage for elements that don't need
/// advanced accessibility annotations. All fluent extension methods create/merge
/// this record automatically — developers never need to construct it directly.
/// </summary>
public record AccessibilityModifiers
{
    /// <summary>AutomationProperties.HelpText — supplemental description read after the Name.</summary>
    public string? HelpText { get; init; }

    /// <summary>AutomationProperties.FullDescription — extended description for complex elements.</summary>
    public string? FullDescription { get; init; }

    /// <summary>AutomationProperties.LandmarkType — landmark region (Main, Navigation, Search, Form).</summary>
    public Microsoft.UI.Xaml.Automation.Peers.AutomationLandmarkType? LandmarkType { get; init; }

    /// <summary>AutomationProperties.AccessibilityView — UIA tree visibility (Content, Control, Raw).</summary>
    public Microsoft.UI.Xaml.Automation.Peers.AccessibilityView? AccessibilityView { get; init; }

    /// <summary>AutomationProperties.IsRequiredForForm — screen readers announce "required".</summary>
    public bool? IsRequiredForForm { get; init; }

    /// <summary>AutomationProperties.LiveSetting — live region announcement mode (Polite, Assertive).</summary>
    public Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting? LiveSetting { get; init; }

    /// <summary>AutomationProperties.PositionInSet — ordinal position (1-based) in a group.</summary>
    public int? PositionInSet { get; init; }

    /// <summary>AutomationProperties.SizeOfSet — total count in the group.</summary>
    public int? SizeOfSet { get; init; }

    /// <summary>AutomationProperties.Level — hierarchical depth (e.g., tree node level).</summary>
    public int? Level { get; init; }

    /// <summary>AutomationProperties.ItemStatus — status string (e.g., "3 unread").</summary>
    public string? ItemStatus { get; init; }

    /// <summary>AutomationProperties.LabeledBy target AutomationId — resolved by the reconciler.</summary>
    public string? LabeledBy { get; init; }

    /// <summary>UIElement.TabFocusNavigation — Tab behavior within a container (Local, Once, Cycle).</summary>
    public Microsoft.UI.Xaml.Input.KeyboardNavigationMode? TabFocusNavigation { get; init; }

    public AccessibilityModifiers Merge(AccessibilityModifiers other)
    {
        return this with
        {
            HelpText = other.HelpText ?? HelpText,
            FullDescription = other.FullDescription ?? FullDescription,
            LandmarkType = other.LandmarkType ?? LandmarkType,
            AccessibilityView = other.AccessibilityView ?? AccessibilityView,
            IsRequiredForForm = other.IsRequiredForForm ?? IsRequiredForForm,
            LiveSetting = other.LiveSetting ?? LiveSetting,
            PositionInSet = other.PositionInSet ?? PositionInSet,
            SizeOfSet = other.SizeOfSet ?? SizeOfSet,
            Level = other.Level ?? Level,
            ItemStatus = other.ItemStatus ?? ItemStatus,
            LabeledBy = other.LabeledBy ?? LabeledBy,
            TabFocusNavigation = other.TabFocusNavigation ?? TabFocusNavigation,
        };
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Transition data records (stored on Element base, applied by Reconciler)
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Declarative implicit transition configuration for a UIElement.
/// Each property maps to a WinUI implicit transition property on UIElement/Panel.
/// Null means "don't set this transition".
/// </summary>
public record ImplicitTransitions
{
    public ScalarTransition? Opacity { get; init; }
    public ScalarTransition? Rotation { get; init; }
    public Vector3Transition? Scale { get; init; }
    public Vector3Transition? Translation { get; init; }
    public BrushTransition? Background { get; init; }
}

/// <summary>
/// Declarative theme transition configuration.
/// Children applies to Panel.ChildrenTransitions / Border.ChildTransitions / ContentControl.ContentTransitions.
/// ItemContainer applies to ItemsControl.ItemContainerTransitions.
/// The reconciler picks the correct property based on control type.
/// </summary>
public record ThemeTransitions
{
    public Microsoft.UI.Xaml.Media.Animation.Transition[]? Children { get; init; }
    public Microsoft.UI.Xaml.Media.Animation.Transition[]? ItemContainer { get; init; }
}
// Note: Transition is in Microsoft.UI.Xaml.Media.Animation (not imported by default in Element.cs)

/// <summary>
/// Configuration for Composition-layer layout animations.
/// When applied to an element, the reconciler sets up implicit animations on the element's
/// Visual so that layout-driven Offset (position) and optionally Size changes animate smoothly.
/// Runs entirely on the Composition thread — zero managed-code involvement during animation.
///
/// Limitations:
/// - Hit-testing uses the final layout position, not the animated visual position.
/// - Elements must have stable keys (.WithKey()) for the reconciler to match them across reorders.
/// - Size animation is cosmetic: content does not re-layout during the Size animation.
/// - Only handles position changes for persistent elements; use theme transitions for enter/exit.
/// </summary>
public record LayoutAnimationConfig
{
    /// <summary>Duration of the layout animation. Default: 300ms.</summary>
    public TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>When true, use a spring natural motion animation instead of linear keyframes.</summary>
    public bool UseSpring { get; init; }

    /// <summary>Spring damping ratio (0..1). Only used when UseSpring is true. Default: 0.6.</summary>
    public float DampingRatio { get; init; } = 0.6f;

    /// <summary>Spring period in seconds. Only used when UseSpring is true. Default: 0.08.</summary>
    public float Period { get; init; } = 0.08f;

    /// <summary>Animate Offset (position) changes. Default: true.</summary>
    public bool AnimateOffset { get; init; } = true;

    /// <summary>Animate Size changes. Default: false (content won't re-layout during animation).</summary>
    public bool AnimateSize { get; init; }
}

// Reactor reuses WinUI types directly — no shadow enums.
// See: Microsoft.UI.Xaml (Thickness, HorizontalAlignment, VerticalAlignment)
//      Microsoft.UI.Xaml.Controls (Orientation, InfoBarSeverity, ExpandDirection, etc.)
//      Microsoft.UI.Xaml.Controls.Primitives (FlyoutPlacementMode)
//      global::Windows.UI.Text (FontWeight, FontWeights)

// ════════════════════════════════════════════════════════════════════════
//  Supporting data records (non-Element, used as structured params)
// ════════════════════════════════════════════════════════════════════════

public record GridDefinition(string[] Columns, string[] Rows)
{
    /// <summary>
    /// Construct a <see cref="GridDefinition"/> from the strongly-typed
    /// <see cref="GridSize"/> form. Track strings are produced via
    /// <see cref="GridSize.ToString"/> using <c>CultureInfo.InvariantCulture</c>.
    /// Spec 033 §1.
    /// </summary>
    /// <exception cref="global::System.ArgumentNullException">Thrown when either array is null.</exception>
    public GridDefinition(GridSize[] columns, GridSize[] rows)
        : this(ToStrings(columns), ToStrings(rows))
    {
    }

    private static string[] ToStrings(GridSize[] sizes)
    {
        if (sizes is null) throw new global::System.ArgumentNullException(nameof(sizes));
        var result = new string[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
            result[i] = sizes[i].ToString();
        return result;
    }
}

/// <summary>Attached property data for Grid children. Set via .Grid(row:, column:) extension.</summary>
public record GridAttached(int Row = 0, int Column = 0, int RowSpan = 1, int ColumnSpan = 1);

/// <summary>
/// Attached property data for VariableSizedWrapGrid children. Set via
/// <c>.WrapGridColumnSpan(int)</c> / <c>.WrapGridRowSpan(int)</c> extensions.
/// </summary>
public record WrapGridAttached(int RowSpan = 1, int ColumnSpan = 1);

/// <summary>Attached property data for Canvas children. Set via .Canvas(left:, top:) extension.</summary>
/// <remarks>
/// <see cref="AnchorX"/> / <see cref="AnchorY"/> are 0..1 fractions of the element's
/// rendered size that are subtracted from <see cref="Left"/>/<see cref="Top"/> after
/// layout. Anchor 0,0 (default) keeps the legacy top-left positioning. Anchor 0.5,0.5
/// centers the element on (Left, Top); 1,1 anchors at bottom-right. Useful for
/// chart labels and other elements that need to align around a logical point
/// rather than their top-left corner.
/// </remarks>
public record CanvasAttached(double Left = 0, double Top = 0)
{
    /// <summary>Horizontal anchor as a 0..1 fraction of the element's rendered width.</summary>
    public double AnchorX { get; init; }

    /// <summary>Vertical anchor as a 0..1 fraction of the element's rendered height.</summary>
    public double AnchorY { get; init; }
}

/// <summary>Attached property data for Flex children. Set via .Flex(grow:, shrink:, ...) extension.</summary>
public record FlexAttached(
    double Grow = 0,
    double Shrink = 1,
    double? Basis = null,
    Layout.FlexAlign? AlignSelf = null,
    Layout.FlexPositionType Position = Layout.FlexPositionType.Relative,
    double? Left = null,
    double? Top = null,
    double? Right = null,
    double? Bottom = null
);

/// <summary>Attached property data for RelativePanel children. Set via .RelativePanel(...) extension.</summary>
public record RelativePanelAttached(string Name)
{
    public string? RightOf { get; init; }
    public string? Below { get; init; }
    public string? LeftOf { get; init; }
    public string? Above { get; init; }
    public string? AlignLeftWith { get; init; }
    public string? AlignRightWith { get; init; }
    public string? AlignTopWith { get; init; }
    public string? AlignBottomWith { get; init; }
    public string? AlignHorizontalCenterWith { get; init; }
    public string? AlignVerticalCenterWith { get; init; }
    public bool AlignLeftWithPanel { get; init; }
    public bool AlignRightWithPanel { get; init; }
    public bool AlignTopWithPanel { get; init; }
    public bool AlignBottomWithPanel { get; init; }
    public bool AlignHorizontalCenterWithPanel { get; init; }
    public bool AlignVerticalCenterWithPanel { get; init; }
}

public record NavigationViewItemData(string Content, string? Icon = null, string? Tag = null)
{
    public NavigationViewItemData[]? Children { get; init; }
    public bool IsHeader { get; init; }
    public IconData? IconElement { get; init; }
}

public record TabViewItemData(string Header, Element Content)
{
    public string? Icon { get; init; }
    public bool IsClosable { get; init; } = true;
}

public record PivotItemData(string Header, Element Content);

public record BreadcrumbBarItemData(string Label, object? Tag = null);

public record TreeViewNodeData(string Content, TreeViewNodeData[]? Children = null)
{
    public bool IsExpanded { get; init; }

    /// <summary>
    /// Optional Reactor element to render as the node's visual content.
    /// When null, a TextBlock showing Content is rendered.
    /// </summary>
    public Element? ContentElement { get; init; }
}

public record MenuBarItemData(string Title, MenuFlyoutItemBase[] Items);

public abstract record MenuFlyoutItemBase;
public record MenuFlyoutItemData(string Text, Action? OnClick = null, string? Icon = null) : MenuFlyoutItemBase
{
    public bool IsEnabled { get; init; } = true;
    public IconData? IconElement { get; init; }
    public KeyboardAcceleratorData[]? KeyboardAccelerators { get; init; }
    public string? AccessKey { get; init; }
    public string? Description { get; init; }
}
public record MenuFlyoutSeparatorData() : MenuFlyoutItemBase;
public record MenuFlyoutSubItemData(string Text, MenuFlyoutItemBase[] Items, string? Icon = null) : MenuFlyoutItemBase
{
    public IconData? IconElement { get; init; }
}
public record ToggleMenuFlyoutItemData(string Text, bool IsChecked = false, Action<bool>? OnIsCheckedChanged = null, string? Icon = null) : MenuFlyoutItemBase
{
    public IconData? IconElement { get; init; }
}
public record RadioMenuFlyoutItemData(string Text, string GroupName, bool IsChecked = false, Action? OnClick = null, string? Icon = null) : MenuFlyoutItemBase
{
    public IconData? IconElement { get; init; }
}

// Keyboard accelerator data
public record KeyboardAcceleratorData(global::Windows.System.VirtualKey Key, global::Windows.System.VirtualKeyModifiers Modifiers = global::Windows.System.VirtualKeyModifiers.None);

// Icon data hierarchy — used to set icons on menu items, app bar buttons, etc.
public abstract record IconData;
public record SymbolIconData(string Symbol) : IconData;
public record FontIconData(string Glyph, string? FontFamily = null, double? FontSize = null) : IconData;
public record BitmapIconData(global::System.Uri Source, bool ShowAsMonochrome = true) : IconData;
public record PathIconData(string Data) : IconData;
public record ImageIconData(global::System.Uri Source) : IconData;

public abstract record AppBarItemBase;
public record AppBarButtonData(string Label, Action? OnClick = null, string? Icon = null) : AppBarItemBase
{
    public bool IsEnabled { get; init; } = true;
    public IconData? IconElement { get; init; }
    public KeyboardAcceleratorData[]? KeyboardAccelerators { get; init; }
    public string? AccessKey { get; init; }
    public string? Description { get; init; }
}
public record AppBarToggleButtonData(string Label, bool IsChecked = false, Action<bool>? OnIsCheckedChanged = null, string? Icon = null) : AppBarItemBase
{
    public IconData? IconElement { get; init; }
}
public record AppBarSeparatorData() : AppBarItemBase;

/// <summary>
/// Scopes keyboard accelerators from a set of commands to a subtree.
/// Accelerators are only active when the host or its descendants have focus.
/// </summary>
public record CommandHostElement(Command[] Commands, Element Child) : Element;

// ════════════════════════════════════════════════════════════════════════
//  Text elements
// ════════════════════════════════════════════════════════════════════════

public record TextBlockElement(string Content) : Element
{
    public double? FontSize { get; init; }
    public FontWeight? Weight { get; init; }
    public global::Windows.UI.Text.FontStyle? FontStyle { get; init; }
    public HorizontalAlignment? HorizontalAlignment { get; init; }
    public TextWrapping? TextWrapping { get; init; }
    public TextAlignment? TextAlignment { get; init; }
    public TextTrimming? TextTrimming { get; init; }
    public bool? IsTextSelectionEnabled { get; init; }
    public Microsoft.UI.Xaml.Media.FontFamily? FontFamily { get; init; }
    /// <summary>Line height in pixels. <c>null</c> uses the WinUI default (proportional to FontSize).</summary>
    public double? LineHeight { get; init; }
    /// <summary>Maximum number of lines to render before truncating per <see cref="TextTrimming"/>. <c>0</c> (default) = no limit.</summary>
    public int MaxLines { get; init; }
    /// <summary>Extra spacing between characters, in units of 1/1000em. Defaults to <c>0</c> (no extra spacing).</summary>
    public int CharacterSpacing { get; init; }
    /// <summary>Bitmask of underline / strikethrough decorations. Default <c>None</c>.</summary>
    public global::Windows.UI.Text.TextDecorations TextDecorations { get; init; } = global::Windows.UI.Text.TextDecorations.None;
    internal Action<WinUI.TextBlock>[] Setters { get; init; } = [];

    /// <summary>
    /// EXP-2: Bitmask diff — compare two TextBlockElement instances (pure C#, no COM interop)
    /// and return which properties actually changed. Callers only touch WinUI for set bits.
    /// </summary>
    internal static TextPropChanged DiffProps(TextBlockElement old, TextBlockElement cur)
    {
        var diff = TextPropChanged.None;
        if (old.Content != cur.Content) diff |= TextPropChanged.Content;
        if (old.FontSize != cur.FontSize) diff |= TextPropChanged.FontSize;
        if (old.Weight != cur.Weight) diff |= TextPropChanged.Weight;
        if (old.FontStyle != cur.FontStyle) diff |= TextPropChanged.FontStyle;
        if (old.HorizontalAlignment != cur.HorizontalAlignment) diff |= TextPropChanged.HorizontalAlignment;
        if (old.TextWrapping != cur.TextWrapping) diff |= TextPropChanged.TextWrapping;
        if (old.TextAlignment != cur.TextAlignment) diff |= TextPropChanged.TextAlignment;
        if (old.TextTrimming != cur.TextTrimming) diff |= TextPropChanged.TextTrimming;
        if (old.IsTextSelectionEnabled != cur.IsTextSelectionEnabled) diff |= TextPropChanged.IsTextSelectionEnabled;
        if (old.FontFamily != cur.FontFamily) diff |= TextPropChanged.FontFamily;
        if (old.LineHeight != cur.LineHeight) diff |= TextPropChanged.LineHeight;
        if (old.MaxLines != cur.MaxLines) diff |= TextPropChanged.MaxLines;
        if (old.CharacterSpacing != cur.CharacterSpacing) diff |= TextPropChanged.CharacterSpacing;
        if (old.TextDecorations != cur.TextDecorations) diff |= TextPropChanged.TextDecorations;
        if (old.Setters.Length != cur.Setters.Length) diff |= TextPropChanged.Setters;
        else if (cur.Setters.Length > 0) diff |= TextPropChanged.Setters; // can't compare delegates
        return diff;
    }
}

[Flags]
internal enum TextPropChanged : ushort
{
    None                = 0,
    Content             = 1 << 0,
    FontSize            = 1 << 1,
    Weight              = 1 << 2,
    FontStyle           = 1 << 3,
    HorizontalAlignment = 1 << 4,
    TextWrapping        = 1 << 5,
    TextAlignment       = 1 << 6,
    TextTrimming        = 1 << 7,
    IsTextSelectionEnabled = 1 << 8,
    FontFamily          = 1 << 9,
    Setters             = 1 << 10,
    LineHeight          = 1 << 11,
    MaxLines            = 1 << 12,
    CharacterSpacing    = 1 << 13,
    TextDecorations     = 1 << 14,
}

public record RichTextBlockElement(string Text) : Element
{
    public double? FontSize { get; init; }
    public RichTextParagraph[]? Paragraphs { get; init; }
    public bool IsTextSelectionEnabled { get; init; }
    public TextWrapping? TextWrapping { get; init; }
    /// <summary>Maximum number of lines before trimming per <see cref="TextTrimming"/>. <c>0</c> (default) = no limit.</summary>
    public int MaxLines { get; init; }
    /// <summary>Line height in pixels. <c>null</c> uses the WinUI default (proportional to FontSize).</summary>
    public double? LineHeight { get; init; }
    /// <summary>Horizontal alignment of text within the block. <c>null</c> uses the WinUI default (Left).</summary>
    public TextAlignment? TextAlignment { get; init; }
    /// <summary>How overflowing text is truncated. <c>null</c> uses the WinUI default (None).</summary>
    public TextTrimming? TextTrimming { get; init; }
    /// <summary>Extra spacing between characters, in units of 1/1000em. Defaults to <c>0</c>.</summary>
    public int CharacterSpacing { get; init; }
    internal Action<WinUI.RichTextBlock>[] Setters { get; init; } = [];
}

// Rich text inline content types
public record RichTextParagraph(RichTextInline[] Inlines);

public abstract record RichTextInline;

public record RichTextRun(string Text) : RichTextInline
{
    public bool IsBold { get; init; }
    public bool IsItalic { get; init; }
    public bool IsStrikethrough { get; init; }
    public double? FontSize { get; init; }
    public string? FontFamily { get; init; }
    public Brush? Foreground { get; init; }
}

public record RichTextHyperlink(string Text, Uri NavigateUri) : RichTextInline;

public record RichTextLineBreak() : RichTextInline;

// ════════════════════════════════════════════════════════════════════════
//  Button elements
// ════════════════════════════════════════════════════════════════════════

public record ButtonElement(string Label, Action? OnClick = null) : Element
{
    public bool IsEnabled { get; init; } = true;
    /// <summary>
    /// When true, the button is visually dimmed and its <c>OnClick</c> handler
    /// is suppressed, but it stays keyboard-focusable and reachable via Tab.
    /// Use for submit buttons gated on validation so users can discover them
    /// and the disable state doesn't trap keyboard navigation through commit-
    /// on-blur inputs. Conceptually equivalent to Fluent UI React's
    /// <c>disabledFocusable</c> / ARIA's <c>aria-disabled</c>. UIA still
    /// reports the button as enabled — full assistive-tech "unavailable"
    /// reporting requires a custom <c>ButtonAutomationPeer</c> and is tracked
    /// as a follow-up.
    /// </summary>
    public bool IsDisabledFocusable { get; init; }
    public Element? ContentElement { get; init; }
    internal Action<WinUI.Button>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnClick is not null;
}

public record HyperlinkButtonElement(string Content, Uri? NavigateUri = null, Action? OnClick = null) : Element
{
    internal Action<WinUI.HyperlinkButton>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnClick is not null;
}

public record RepeatButtonElement(string Label, Action? OnClick = null) : Element
{
    public int Delay { get; init; } = 250;
    public int Interval { get; init; } = 50;
    internal Action<WinPrim.RepeatButton>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnClick is not null;
}

public record ToggleButtonElement(string Label, bool IsChecked = false, Action<bool>? OnIsCheckedChanged = null) : Element
{
    /// <summary>
    /// Enable the three-state cycle (true → false → null → true). Pair with
    /// <see cref="CheckedState"/> and <see cref="OnCheckedStateChanged"/>; the
    /// non-nullable <see cref="IsChecked"/> primary is ignored in this mode.
    /// Mirrors the established <c>CheckBoxElement</c> precedent.
    /// </summary>
    public bool IsThreeState { get; init; }
    /// <summary>Three-state value (<c>null</c> = indeterminate). Active only when <see cref="IsThreeState"/> is true.</summary>
    public bool? CheckedState { get; init; }
    /// <summary>Three-state change handler. Receives <c>null</c> for indeterminate.</summary>
    public Action<bool?>? OnCheckedStateChanged { get; init; }
    internal Action<WinPrim.ToggleButton>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnIsCheckedChanged is not null || OnCheckedStateChanged is not null;
}

public record DropDownButtonElement(string Label, Element? Flyout = null) : Element
{
    internal Action<WinUI.DropDownButton>[] Setters { get; init; } = [];
}

public record SplitButtonElement(string Label, Action? OnClick = null, Element? Flyout = null) : Element
{
    internal Action<WinUI.SplitButton>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnClick is not null;
}

public record ToggleSplitButtonElement(string Label, bool IsChecked = false, Action<bool>? OnIsCheckedChanged = null, Element? Flyout = null) : Element
{
    internal Action<WinUI.ToggleSplitButton>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnIsCheckedChanged is not null;
}

// ════════════════════════════════════════════════════════════════════════
//  Input elements
// ════════════════════════════════════════════════════════════════════════

public record TextFieldElement(
    string Value,
    Action<string>? OnChanged = null,
    string? Placeholder = null
) : Element
{
    public string? Header { get; init; }
    public bool? IsReadOnly { get; init; }
    public bool? AcceptsReturn { get; init; }
    public TextWrapping? TextWrapping { get; init; }
    /// <summary>Fires when the text selection changes. Receives (selectedText, selectionStart, selectionLength).</summary>
    public Action<string, int, int>? OnSelectionChanged { get; init; }
    /// <summary>Caret / selection start position. Set this to control where the caret sits after a text update.</summary>
    public int? SelectionStart { get; init; }
    /// <summary>Selection length. Set alongside SelectionStart to control the selection range.</summary>
    public int? SelectionLength { get; init; }
    /// <summary>Maximum number of characters allowed. <c>0</c> (default) means no limit.</summary>
    public int MaxLength { get; init; }
    /// <summary>Whether built-in spell-check is enabled. Defaults to the WinUI default (true).</summary>
    public bool? IsSpellCheckEnabled { get; init; }
    /// <summary>Forces input to upper/lower-case as the user types. Defaults to <c>Normal</c> (no transform).</summary>
    public CharacterCasing CharacterCasing { get; init; } = CharacterCasing.Normal;
    /// <summary>Horizontal text alignment within the box. Defaults to <c>Left</c>.</summary>
    public TextAlignment TextAlignment { get; init; } = TextAlignment.Left;
    /// <summary>Help text rendered below the box. WinUI 3 1.2+ feature.</summary>
    public string? Description { get; init; }
    internal Action<WinUI.TextBox>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnChanged is not null || OnSelectionChanged is not null;
}

public record PasswordBoxElement(
    string Password,
    Action<string>? OnPasswordChanged = null,
    string? PlaceholderText = null
) : Element
{
    /// <summary>Maximum number of characters allowed. <c>0</c> (default) = no limit.</summary>
    public int MaxLength { get; init; }
    /// <summary>Optional label rendered above the box.</summary>
    public string? Header { get; init; }
    /// <summary>How the reveal button behaves. Defaults to <c>Peek</c> (matches WinUI).</summary>
    public PasswordRevealMode PasswordRevealMode { get; init; } = PasswordRevealMode.Peek;
    /// <summary>Character displayed in place of the entered password (default '●' bullet).</summary>
    public string? PasswordChar { get; init; }
    internal Action<WinUI.PasswordBox>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnPasswordChanged is not null;
}

public record NumberBoxElement(
    double Value,
    Action<double>? OnValueChanged = null,
    string? Header = null
) : Element
{
    public double Minimum { get; init; } = double.MinValue;
    public double Maximum { get; init; } = double.MaxValue;
    public string? PlaceholderText { get; init; }
    public NumberBoxSpinButtonPlacementMode SpinButtonPlacement { get; init; } = NumberBoxSpinButtonPlacementMode.Hidden;
    public double SmallChange { get; init; } = 1;
    public double LargeChange { get; init; } = 10;
    /// <summary>Custom number formatter (e.g. currency, percent). Null uses WinUI's default DecimalFormatter.</summary>
    public global::Windows.Globalization.NumberFormatting.INumberFormatter2? NumberFormatter { get; init; }
    /// <summary>Whether the user can type arithmetic expressions (e.g. <c>2*3+1</c>) that resolve on commit.</summary>
    public bool AcceptsExpression { get; init; }
    /// <summary>How invalid input is treated. Defaults to <c>InvalidInputOverwritten</c> (matches WinUI default).</summary>
    public NumberBoxValidationMode ValidationMode { get; init; } = NumberBoxValidationMode.InvalidInputOverwritten;
    /// <summary>Help text rendered below the box. WinUI 3 1.2+ feature.</summary>
    public string? Description { get; init; }
    internal Action<WinUI.NumberBox>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnValueChanged is not null;
}

public record AutoSuggestBoxElement(
    string Text,
    Action<string>? OnTextChanged = null,
    Action<string>? OnQuerySubmitted = null,
    Action<string>? OnSuggestionChosen = null
) : Element
{
    public string[] Suggestions { get; init; } = [];
    public string? PlaceholderText { get; init; }
    /// <summary>Optional label rendered above the box.</summary>
    public string? Header { get; init; }
    /// <summary>Icon rendered in the trailing query slot (e.g. a Search symbol).</summary>
    public IconData? QueryIcon { get; init; }
    /// <summary>Programmatically open or close the suggestion list. Defaults to <c>false</c>.</summary>
    public bool IsSuggestionListOpen { get; init; }
    internal Action<WinUI.AutoSuggestBox>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnTextChanged is not null || OnQuerySubmitted is not null || OnSuggestionChosen is not null;
}

public record CheckBoxElement(
    bool IsChecked,
    Action<bool>? OnIsCheckedChanged = null,
    string? Label = null
) : Element
{
    public bool IsThreeState { get; init; }
    public bool? CheckedState { get; init; }
    public Action<bool?>? OnCheckedStateChanged { get; init; }
    internal Action<WinUI.CheckBox>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnIsCheckedChanged is not null || OnCheckedStateChanged is not null;
}

public record RadioButtonElement(
    string Label,
    bool IsChecked = false,
    Action<bool>? OnIsCheckedChanged = null,
    string? GroupName = null
) : Element
{
    internal Action<WinUI.RadioButton>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnIsCheckedChanged is not null;
}

public record RadioButtonsElement(
    string[] Items,
    int SelectedIndex = -1,
    Action<int>? OnSelectedIndexChanged = null
) : Element
{
    public string? Header { get; init; }
    internal Action<WinUI.RadioButtons>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnSelectedIndexChanged is not null;
}

public record ComboBoxElement(
    string[] Items,
    int SelectedIndex = -1,
    Action<int>? OnSelectedIndexChanged = null
) : Element
{
    public string? PlaceholderText { get; init; }
    public string? Header { get; init; }
    public bool IsEditable { get; init; }
    public Element[]? ItemElements { get; init; }
    /// <summary>Maximum pixel height of the open drop-down. <c>NaN</c> (default) uses the WinUI default.</summary>
    public double MaxDropDownHeight { get; init; } = double.NaN;
    /// <summary>Help text rendered below the box. WinUI 3 1.2+ feature.</summary>
    public string? Description { get; init; }
    /// <summary>Raised when the user opens the drop-down list.</summary>
    public Action? OnDropDownOpened { get; init; }
    /// <summary>Raised when the drop-down list closes (either by selection or dismissal).</summary>
    public Action? OnDropDownClosed { get; init; }
    internal Action<WinUI.ComboBox>[] Setters { get; init; } = [];
    internal override bool HasCallbacks =>
        OnSelectedIndexChanged is not null
        || OnDropDownOpened is not null
        || OnDropDownClosed is not null;
}

public record SliderElement(
    double Value,
    double Min = 0,
    double Max = 100,
    Action<double>? OnValueChanged = null
) : Element
{
    public double StepFrequency { get; init; } = 1;
    public string? Header { get; init; }
    /// <summary>Slider orientation. Defaults to <c>Orientation.Horizontal</c>.</summary>
    public Orientation Orientation { get; init; } = Orientation.Horizontal;
    /// <summary>Interval between tick marks on the slider's track. Defaults to <c>0</c> (no ticks).</summary>
    public double TickFrequency { get; init; }
    /// <summary>Where tick marks render relative to the track. Defaults to <c>TickPlacement.Inline</c>.</summary>
    public TickPlacement TickPlacement { get; init; } = TickPlacement.Inline;
    /// <summary>Whether the thumb snaps to ticks or step values during drag.</summary>
    public SliderSnapsTo SnapsTo { get; init; } = SliderSnapsTo.StepValues;
    /// <summary>Whether the floating value tooltip appears while dragging the thumb. Defaults to <c>true</c>.</summary>
    public bool IsThumbToolTipEnabled { get; init; } = true;
    internal Action<WinUI.Slider>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnValueChanged is not null;
}

public record ToggleSwitchElement(
    bool IsOn,
    Action<bool>? OnIsOnChanged = null,
    string? OnContent = null,
    string? OffContent = null
) : Element
{
    public string? Header { get; init; }
    internal Action<WinUI.ToggleSwitch>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnIsOnChanged is not null;
}

public record RatingControlElement(
    double Value = 0,
    Action<double>? OnValueChanged = null
) : Element
{
    public int MaxRating { get; init; } = 5;
    public bool IsReadOnly { get; init; }
    public string? Caption { get; init; }
    /// <summary>Star value shown when the rating is unset. Defaults to -1 (no placeholder).</summary>
    public double PlaceholderValue { get; init; } = -1;
    /// <summary>Integer rating to assume when the user first interacts. Defaults to 1. (WinUI's <c>InitialSetValue</c> is int.)</summary>
    public int InitialSetValue { get; init; } = 1;
    internal Action<WinUI.RatingControl>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnValueChanged is not null;
}

public record ColorPickerElement(
    global::Windows.UI.Color Color,
    Action<global::Windows.UI.Color>? OnColorChanged = null
) : Element
{
    public bool IsAlphaEnabled { get; init; }
    public bool IsMoreButtonVisible { get; init; }
    public bool IsColorSpectrumVisible { get; init; } = true;
    public bool IsColorSliderVisible { get; init; } = true;
    public bool IsColorChannelTextInputVisible { get; init; } = true;
    public bool IsHexInputVisible { get; init; } = true;
    /// <summary>Shape of the 2D color spectrum (Box or Ring). Defaults to <c>Box</c>.</summary>
    public ColorSpectrumShape ColorSpectrumShape { get; init; } = ColorSpectrumShape.Box;
    /// <summary>Minimum hue (0–359). Defaults to 0.</summary>
    public int MinHue { get; init; }
    /// <summary>Maximum hue (0–359). Defaults to 359.</summary>
    public int MaxHue { get; init; } = 359;
    /// <summary>Minimum saturation (0–100). Defaults to 0.</summary>
    public int MinSaturation { get; init; }
    /// <summary>Maximum saturation (0–100). Defaults to 100.</summary>
    public int MaxSaturation { get; init; } = 100;
    /// <summary>Minimum value/brightness (0–100). Defaults to 0.</summary>
    public int MinValue { get; init; }
    /// <summary>Maximum value/brightness (0–100). Defaults to 100.</summary>
    public int MaxValue { get; init; } = 100;
    internal Action<WinUI.ColorPicker>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnColorChanged is not null;
}

// ════════════════════════════════════════════════════════════════════════
//  Date / Time elements
// ════════════════════════════════════════════════════════════════════════

public record CalendarDatePickerElement(
    DateTimeOffset? Date = null,
    Action<DateTimeOffset?>? OnDateChanged = null
) : Element
{
    public string? PlaceholderText { get; init; }
    public string? Header { get; init; }
    public DateTimeOffset? MinDate { get; init; }
    public DateTimeOffset? MaxDate { get; init; }
    /// <summary>Display format string for the picker's text (see WinUI <c>DateFormat</c> reference).</summary>
    public string? DateFormat { get; init; }
    /// <summary>Highlight today's date in the popup. Defaults to <c>true</c> (matches WinUI).</summary>
    public bool IsTodayHighlighted { get; init; } = true;
    /// <summary>Programmatically open or close the calendar popup.</summary>
    public bool IsCalendarOpen { get; init; }
    /// <summary>Show month/year group label headers in the popup. Defaults to <c>true</c>.</summary>
    public bool IsGroupLabelVisible { get; init; } = true;
    internal Action<WinUI.CalendarDatePicker>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnDateChanged is not null;
}

public record DatePickerElement(
    DateTimeOffset Date,
    Action<DateTimeOffset>? OnDateChanged = null
) : Element
{
    public string? Header { get; init; }
    public DateTimeOffset? MinYear { get; init; }
    public DateTimeOffset? MaxYear { get; init; }
    public bool DayVisible { get; init; } = true;
    public bool MonthVisible { get; init; } = true;
    public bool YearVisible { get; init; } = true;
    /// <summary>Display format string for the day column. <c>null</c> uses the WinUI default.</summary>
    public string? DayFormat { get; init; }
    /// <summary>Display format string for the month column. <c>null</c> uses the WinUI default.</summary>
    public string? MonthFormat { get; init; }
    /// <summary>Display format string for the year column. <c>null</c> uses the WinUI default.</summary>
    public string? YearFormat { get; init; }
    /// <summary>Layout direction of the picker. Defaults to <c>Horizontal</c>.</summary>
    public Orientation Orientation { get; init; } = Orientation.Horizontal;
    internal Action<WinUI.DatePicker>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnDateChanged is not null;
}

public record TimePickerElement(
    TimeSpan Time,
    Action<TimeSpan>? OnTimeChanged = null
) : Element
{
    public string? Header { get; init; }
    public int MinuteIncrement { get; init; } = 1;
    public int ClockIdentifier { get; init; } = 12;
    internal Action<WinUI.TimePicker>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnTimeChanged is not null;
}

// ════════════════════════════════════════════════════════════════════════
//  Progress elements
// ════════════════════════════════════════════════════════════════════════

public record ProgressElement(double? Value = null) : Element  // null = indeterminate
{
    public bool IsIndeterminate => Value is null;
    public double Minimum { get; init; } = 0;
    public double Maximum { get; init; } = 100;
    public bool ShowError { get; init; }
    public bool ShowPaused { get; init; }
    internal Action<WinUI.ProgressBar>[] Setters { get; init; } = [];
}

public record ProgressRingElement(double? Value = null) : Element
{
    public bool IsIndeterminate => Value is null;
    public double Minimum { get; init; } = 0;
    public double Maximum { get; init; } = 100;
    public bool IsActive { get; init; } = true;
    internal Action<WinUI.ProgressRing>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  Media elements
// ════════════════════════════════════════════════════════════════════════

public record ImageElement(string Source) : Element
{
    public double? Width { get; init; }
    public double? Height { get; init; }
    public string? Stretch { get; init; }
    /// <summary>Raised after the image source loads successfully (marshalled to UI thread).</summary>
    public Action? OnImageOpened { get; init; }
    /// <summary>Raised when the image fails to load. Receives the failure message.</summary>
    public Action<string>? OnImageFailed { get; init; }
    /// <summary>Nine-grid (slice) values for resolution-independent corner stretching.</summary>
    public Thickness? NineGrid { get; init; }
    internal Action<WinUI.Image>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnImageOpened is not null || OnImageFailed is not null;
}

public record PersonPictureElement() : Element
{
    public string? DisplayName { get; init; }
    public string? Initials { get; init; }
    public string? ProfilePicture { get; init; }
    public bool IsGroup { get; init; }
    public int BadgeNumber { get; init; }
    internal Action<WinUI.PersonPicture>[] Setters { get; init; } = [];
}

public record WebView2Element(Uri? Source = null) : Element
{
    public Action<Uri>? OnNavigationStarting { get; init; }
    public Action<Uri>? OnNavigationCompleted { get; init; }

    /// <summary>
    /// Raised when the hosted page posts a message via
    /// <c>window.chrome.webview.postMessage(...)</c>. The callback receives the
    /// JSON payload as a string.
    ///
    /// Threading: messages dispatch on the UI thread (the WinUI WebView2 raises
    /// <c>WebMessageReceived</c> via the control's dispatcher), so the handler
    /// is safe to mutate component state from directly.
    /// </summary>
    public Action<string>? OnWebMessageReceived { get; init; }

    /// <summary>
    /// Raised once <c>CoreWebView2</c> initialization completes — the earliest
    /// point at which features like <c>AddScriptToExecuteOnDocumentCreatedAsync</c>
    /// or <c>AddHostObjectToScript</c> become available. Fires on the UI thread.
    /// </summary>
    public Action? OnCoreWebView2Initialized { get; init; }

    internal Action<WinUI.WebView2>[] Setters { get; init; } = [];
    internal override bool HasCallbacks =>
        OnNavigationStarting is not null
        || OnNavigationCompleted is not null
        || OnWebMessageReceived is not null
        || OnCoreWebView2Initialized is not null;
}

// ════════════════════════════════════════════════════════════════════════
//  Rich text elements
// ════════════════════════════════════════════════════════════════════════

public record RichEditBoxElement(
    string Text = ""
) : Element
{
    public bool IsReadOnly { get; init; }
    public string? Header { get; init; }
    public string? PlaceholderText { get; init; }
    public Action<string>? OnTextChanged { get; init; }
    /// <summary>Whether built-in spell-check is enabled. Defaults to the WinUI default (true).</summary>
    public bool? IsSpellCheckEnabled { get; init; }
    /// <summary>Maximum number of characters allowed. <c>0</c> (default) = no limit.</summary>
    public int MaxLength { get; init; }
    /// <summary>How text wraps within the box. Defaults to <c>Wrap</c>.</summary>
    public TextWrapping TextWrapping { get; init; } = TextWrapping.Wrap;
    /// <summary>Whether Enter inserts a newline (vs committing). Defaults to <c>true</c>.</summary>
    public bool AcceptsReturn { get; init; } = true;
    /// <summary>Brush used to render the selection highlight. <c>null</c> = WinUI default (accent).</summary>
    public Microsoft.UI.Xaml.Media.SolidColorBrush? SelectionHighlightColor { get; init; }
    internal Action<WinUI.RichEditBox>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnTextChanged is not null;
}

// ════════════════════════════════════════════════════════════════════════
//  Layout / Container elements
// ════════════════════════════════════════════════════════════════════════

public record WrapGridElement(
    Element[] Children
) : Element
{
    public int MaximumRowsOrColumns { get; init; } = -1;
    public Orientation Orientation { get; init; } = Orientation.Horizontal;
    public double ItemWidth { get; init; } = double.NaN;
    public double ItemHeight { get; init; } = double.NaN;
    internal Action<WinUI.VariableSizedWrapGrid>[] Setters { get; init; } = [];
}

public record StackElement(
    Orientation Orientation,
    Element[] Children
) : Element
{
    /// <summary>
    /// Spacing between children, in DIPs.
    /// </summary>
    /// <remarks>
    /// Reactor default is <c>8</c> — a deliberate deviation from WinUI's
    /// <c>StackPanel.Spacing</c> default of <c>0</c>. Reactor's call shape
    /// (<c>VStack(a, b, c)</c>) almost always wants whitespace between siblings;
    /// the 8 DIP default produces visually correct output for the
    /// zero-argument call. Set to <c>0</c> explicitly for legacy WinUI
    /// behavior. (spec 039 §0.4 / §16)
    /// </remarks>
    public double Spacing { get; init; } = 8;
    public HorizontalAlignment? HorizontalAlignment { get; init; }
    public VerticalAlignment? VerticalAlignment { get; init; }
    internal Action<WinUI.StackPanel>[] Setters { get; init; } = [];
}

public record GridElement(
    GridDefinition Definition,
    Element[] Children
) : Element
{
    public double RowSpacing { get; init; }
    public double ColumnSpacing { get; init; }
    internal Action<WinUI.Grid>[] Setters { get; init; } = [];
}

public record FlexElement(Element[] Children) : Element
{
    public Layout.FlexDirection Direction { get; init; } = Layout.FlexDirection.Row;
    public Layout.FlexJustify JustifyContent { get; init; } = Layout.FlexJustify.FlexStart;
    public Layout.FlexAlign AlignItems { get; init; } = Layout.FlexAlign.Stretch;
    public Layout.FlexAlign AlignContent { get; init; } = Layout.FlexAlign.FlexStart;
    public Layout.FlexWrap Wrap { get; init; } = Layout.FlexWrap.NoWrap;
    public double ColumnGap { get; init; }
    public double RowGap { get; init; }
    public Thickness FlexPadding { get; init; }
    internal Action<Layout.FlexPanel>[] Setters { get; init; } = [];
}

public record ScrollViewElement(Element Child) : Element
{
    public Orientation Orientation { get; init; } = Orientation.Vertical;
    public ScrollBarVisibility HorizontalScrollBarVisibility { get; init; } = ScrollBarVisibility.Auto;
    public ScrollBarVisibility VerticalScrollBarVisibility { get; init; } = ScrollBarVisibility.Auto;
    public WinUI.ScrollMode HorizontalScrollMode { get; init; } = WinUI.ScrollMode.Auto;
    public WinUI.ScrollMode VerticalScrollMode { get; init; } = WinUI.ScrollMode.Auto;
    public WinUI.ZoomMode ZoomMode { get; init; } = WinUI.ZoomMode.Disabled;

    /// <summary>
    /// Raised when the scroll view's offset or zoom factor changes. The args
    /// expose <c>IsIntermediate</c> for callers who want to debounce until the
    /// scroll settles.
    /// </summary>
    public Action<WinUI.ScrollViewerViewChangedEventArgs>? OnViewChanged { get; init; }

    internal Action<WinUI.ScrollViewer>[] Setters { get; init; } = [];
}

public record BorderElement(Element? Child) : Element
{
    public double? CornerRadius { get; init; }
    public Brush? Background { get; init; }
    public Brush? BorderBrush { get; init; }
    public double? BorderThickness { get; init; }
    internal Action<WinUI.Border>[] Setters { get; init; } = [];
}

public record ExpanderElement(
    string Header,
    Element Content,
    bool IsExpanded = false,
    Action<bool>? OnIsExpandedChanged = null
) : Element
{
    public ExpandDirection ExpandDirection { get; init; } = ExpandDirection.Down;
    /// <summary>Custom Element header (overrides the string <see cref="Header"/>).</summary>
    public Element? HeaderTemplate { get; init; }
    /// <summary>Custom <c>TransitionCollection</c> applied to the expanding content area.</summary>
    public Microsoft.UI.Xaml.Media.Animation.TransitionCollection? ContentTransitions { get; init; }
    internal Action<WinUI.Expander>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnIsExpandedChanged is not null;
}

public record SplitViewElement(
    Element? Pane = null,
    Element? Content = null
) : Element
{
    public bool IsPaneOpen { get; init; } = true;
    public double OpenPaneLength { get; init; } = 320;
    public double CompactPaneLength { get; init; } = 48;
    public SplitViewDisplayMode DisplayMode { get; init; } = SplitViewDisplayMode.Overlay;
    public Action<bool>? OnPaneOpenChanged { get; init; }
    /// <summary>Brush behind the pane. Pair with the <c>.PaneBackground(ThemeRef)</c> overload for theme-aware backgrounds.</summary>
    public Brush? PaneBackground { get; init; }
    /// <summary>How the light-dismiss overlay reacts to taps in Overlay mode. Defaults to <c>Auto</c>.</summary>
    public LightDismissOverlayMode LightDismissOverlayMode { get; init; } = LightDismissOverlayMode.Auto;
    internal Action<WinUI.SplitView>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnPaneOpenChanged is not null;
}

public record ViewboxElement(Element Child) : Element
{
    public Stretch? Stretch { get; init; }
    public StretchDirection? StretchDirection { get; init; }
    internal Action<WinUI.Viewbox>[] Setters { get; init; } = [];
}

public record CanvasElement(Element[] Children) : Element
{
    public double? Width { get; init; }
    public double? Height { get; init; }
    public Brush? Background { get; init; }
    internal Action<WinUI.Canvas>[] Setters { get; init; } = [];

    /// <summary>
    /// When this Canvas was created by a chart element, carries the chart's
    /// accessibility data so the scanner can inspect chart-specific properties.
    /// Null for non-chart canvases.
    /// </summary>
    internal Charting.Accessibility.IChartAccessibilityData? ChartData { get; init; }

    /// <summary>
    /// When set, indicates this chart used <c>.ColorOnly()</c> — scanner flags as A11Y_CHART_004.
    /// </summary>
    internal bool IsColorOnly { get; init; }

    /// <summary>
    /// When set, indicates this chart used <c>.RawColors()</c> — scanner flags as A11Y_CHART_012.
    /// </summary>
    internal bool IsRawColors { get; init; }

    /// <summary>Custom palette set on the chart, if any — scanner validates for contrast.</summary>
    internal Charting.Accessibility.ChartPalette? CustomPalette { get; init; }

    /// <summary>When true, chart is interactive with keyboard navigation enabled.</summary>
    internal bool IsInteractive { get; init; }

    /// <summary>When true, keyboard navigation is explicitly disabled. Scanner flags as A11Y_CHART_003.</summary>
    internal bool IsKeyboardDisabled { get; init; }

    /// <summary>When true, hit targets are not expanded to 24×24. Scanner flags as A11Y_CHART_005.</summary>
    internal bool IsTightHitTest { get; init; }

    /// <summary>
    /// When set, a custom focus indicator color is used instead of the default double-ring.
    /// Scanner validates it meets 3:1 contrast (A11Y_CHART_006).
    /// </summary>
    internal global::Windows.UI.Color? CustomFocusColor { get; init; }

    /// <summary>
    /// When true, the chart announces every animation frame via the live region,
    /// which floods assistive technology. Scanner flags as A11Y_CHART_007.
    /// </summary>
    internal bool IsAnnounceEveryFrame { get; init; }
}

// ════════════════════════════════════════════════════════════════════════
//  Navigation elements
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Renders the content for the current route of a <see cref="Navigation.NavigationHandle{TRoute}"/>.
/// Created via <c>NavigationHost&lt;TRoute&gt;(nav, routeMap)</c> in the DSL.
/// The reconciler uses a Grid container so outgoing/incoming pages can overlap during transitions (Phase 4).
/// </summary>
public record NavigationHostElement(
    object NavigationHandle,
    Func<object, Element> RouteMap
) : Element
{
    public Navigation.NavigationTransition Transition { get; init; } = Navigation.NavigationTransition.Default;
    public Navigation.NavigationCacheMode CacheMode { get; init; } = Navigation.NavigationCacheMode.Disabled;
    public int CacheSize { get; init; } = 10;
}

public record NavigationViewElement(
    NavigationViewItemData[] MenuItems,
    Element? Content = null
) : Element
{
    public string? SelectedTag { get; init; }
    public Action<string?>? OnSelectedTagChanged { get; init; }
    public bool IsPaneOpen { get; init; } = true;
    public NavigationViewPaneDisplayMode PaneDisplayMode { get; init; } = NavigationViewPaneDisplayMode.Auto;
    public bool IsBackEnabled { get; init; }
    public Action? OnBackRequested { get; init; }
    public Element? Header { get; init; }
    public bool IsSettingsVisible { get; init; } = true;
    public string? PaneTitle { get; init; }
    /// <summary>AutoSuggestBox rendered at the top of the pane. Mirrors <c>NavigationView.AutoSuggestBox</c>.</summary>
    public AutoSuggestBoxElement? AutoSuggestBox { get; init; }
    /// <summary>Element rendered at the bottom of the pane, below all menu items.</summary>
    public Element? PaneFooter { get; init; }
    /// <summary>Custom element rendered between the AutoSuggestBox and the menu items.</summary>
    public Element? PaneCustomContent { get; init; }
    /// <summary>Width of the pane when expanded. <c>NaN</c> uses the WinUI default (320).</summary>
    public double OpenPaneLength { get; init; } = double.NaN;
    /// <summary>Window width below which the pane collapses to compact mode. <c>NaN</c> uses the WinUI default (640).</summary>
    public double CompactModeThresholdWidth { get; init; } = double.NaN;
    /// <summary>Window width at which the pane auto-expands. <c>NaN</c> uses the WinUI default (1008).</summary>
    public double ExpandedModeThresholdWidth { get; init; } = double.NaN;
    internal Action<WinUI.NavigationView>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnSelectedTagChanged is not null || OnBackRequested is not null;
}

public record TitleBarElement(
    string Title
) : Element
{
    public string? Subtitle { get; init; }
    public bool IsBackButtonVisible { get; init; }
    public bool IsBackButtonEnabled { get; init; }
    public Action? OnBackRequested { get; init; }
    public bool IsPaneToggleButtonVisible { get; init; }
    public Action? OnPaneToggleRequested { get; init; }
    public Element? Content { get; init; }
    public Element? RightHeader { get; init; }
    /// <summary>
    /// Icon shown in the title bar's leading slot. Mirrors WinUI 3
    /// <c>TitleBar.IconSource</c>. Pass a <see cref="SymbolIconData"/> /
    /// <see cref="FontIconData"/> for built-in glyphs, or
    /// <see cref="ImageIconData"/> / <see cref="BitmapIconData"/> for a
    /// bundled <c>.ico</c> / image (e.g. <c>new ImageIconData(new
    /// Uri("ms-appx:///Assets/AppIcon.ico"))</c>).
    /// </summary>
    public IconData? Icon { get; init; }
    internal Action<WinUI.TitleBar>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnBackRequested is not null || OnPaneToggleRequested is not null;
}

public record TabViewElement(
    TabViewItemData[] Tabs
) : Element
{
    public int SelectedIndex { get; init; } = 0;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    public Action<int>? OnTabCloseRequested { get; init; }
    public Action? OnAddTabButtonClick { get; init; }
    public bool IsAddTabButtonVisible { get; init; }
    /// <summary>How tab widths are sized. Defaults to <c>Equal</c> (matches WinUI default).</summary>
    public TabViewWidthMode TabWidthMode { get; init; } = TabViewWidthMode.Equal;
    /// <summary>Controls when the per-tab close button is visible. Defaults to <c>Auto</c>.</summary>
    public TabViewCloseButtonOverlayMode CloseButtonOverlayMode { get; init; } = TabViewCloseButtonOverlayMode.Auto;
    /// <summary>Whether tabs can be dragged out (to a window).</summary>
    public bool CanDragTabs { get; init; }
    /// <summary>Whether tabs can be reordered within the strip.</summary>
    public bool CanReorderTabs { get; init; }
    /// <summary>Whether tabs from another TabView can be dropped onto this one.</summary>
    public bool AllowDropTabs { get; init; }
    /// <summary>Element rendered at the leading edge of the tab strip.</summary>
    public Element? TabStripHeader { get; init; }
    /// <summary>Element rendered at the trailing edge of the tab strip.</summary>
    public Element? TabStripFooter { get; init; }
    internal Action<WinUI.TabView>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnSelectedIndexChanged is not null || OnTabCloseRequested is not null || OnAddTabButtonClick is not null;
}

public record BreadcrumbBarElement(
    BreadcrumbBarItemData[] Items,
    Action<BreadcrumbBarItemData>? OnItemClicked = null
) : Element
{
    internal Action<WinUI.BreadcrumbBar>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnItemClicked is not null;
}

public record PivotElement(
    PivotItemData[] Items
) : Element
{
    public int SelectedIndex { get; init; } = 0;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    public string? Title { get; init; }
    internal Action<WinUI.Pivot>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnSelectedIndexChanged is not null;
}

// ════════════════════════════════════════════════════════════════════════
//  Collection elements (simple, no item templating)
// ════════════════════════════════════════════════════════════════════════

public record ListViewElement(
    Element[] Items
) : Element
{
    public int SelectedIndex { get; init; } = -1;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    public Action<int>? OnItemClick { get; init; }
    public ListViewSelectionMode SelectionMode { get; init; } = ListViewSelectionMode.Single;
    public string? Header { get; init; }
    /// <summary>Style applied to each generated <c>ListViewItem</c> container (e.g. for padding, hover background).</summary>
    public Style? ItemContainerStyle { get; init; }
    /// <summary>Controls when incremental data sources fetch the next page. Defaults to <c>Edge</c>.</summary>
    public IncrementalLoadingTrigger IncrementalLoadingTrigger { get; init; } = IncrementalLoadingTrigger.Edge;
    /// <summary>
    /// Multi-select snapshot callback. Receives the FULL list of currently
    /// selected indices (snapshot semantics, matching <see cref="CalendarViewElement.OnSelectedDatesChanged"/>).
    /// Use this in Multiple / Extended selection modes — <see cref="OnSelectedIndexChanged"/>
    /// only carries the focused single index. Not raised on initial mount.
    /// </summary>
    public Action<IReadOnlyList<int>>? OnSelectionChanged { get; init; }
    internal Action<WinUI.ListView>[] Setters { get; init; } = [];
    internal override bool HasCallbacks =>
        OnSelectedIndexChanged is not null
        || OnItemClick is not null
        || OnSelectionChanged is not null;
}

public record GridViewElement(
    Element[] Items
) : Element
{
    public int SelectedIndex { get; init; } = -1;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    public Action<int>? OnItemClick { get; init; }
    public ListViewSelectionMode SelectionMode { get; init; } = ListViewSelectionMode.Single;
    public string? Header { get; init; }
    /// <summary>Style applied to each generated <c>GridViewItem</c> container.</summary>
    public Style? ItemContainerStyle { get; init; }
    /// <summary>Controls when incremental data sources fetch the next page. Defaults to <c>Edge</c>.</summary>
    public IncrementalLoadingTrigger IncrementalLoadingTrigger { get; init; } = IncrementalLoadingTrigger.Edge;
    /// <summary>
    /// Multi-select snapshot callback. See <see cref="ListViewElement.OnSelectionChanged"/>.
    /// </summary>
    public Action<IReadOnlyList<int>>? OnSelectionChanged { get; init; }
    internal Action<WinUI.GridView>[] Setters { get; init; } = [];
    internal override bool HasCallbacks =>
        OnSelectedIndexChanged is not null
        || OnItemClick is not null
        || OnSelectionChanged is not null;
}

public record TreeViewElement(
    TreeViewNodeData[] Nodes
) : Element
{
    public Action<TreeViewNodeData>? OnItemInvoked { get; init; }
    public Action<TreeViewNodeData>? OnExpanding { get; init; }
    public TreeViewSelectionMode SelectionMode { get; init; } = TreeViewSelectionMode.Single;
    public bool CanDragItems { get; init; }
    public bool AllowDrop { get; init; }
    public bool CanReorderItems { get; init; }
    internal Action<WinUI.TreeView>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnItemInvoked is not null || OnExpanding is not null;
}

public record FlipViewElement(
    Element[] Items
) : Element
{
    public int SelectedIndex { get; init; } = 0;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    internal Action<WinUI.FlipView>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnSelectedIndexChanged is not null;
}

// ════════════════════════════════════════════════════════════════════════
//  Dialog / Overlay elements
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Declarative content dialog. Set IsOpen to true to show.
/// OnClosed fires with the result when the user dismisses the dialog.
/// </summary>
public record ContentDialogElement(
    string Title,
    Element Content,
    string PrimaryButtonText = "OK"
) : Element
{
    public bool IsOpen { get; init; }
    public string? SecondaryButtonText { get; init; }
    public string? CloseButtonText { get; init; }
    public ContentDialogButton DefaultButton { get; init; } = ContentDialogButton.Primary;
    public Action<ContentDialogResult>? OnClosed { get; init; }
    /// <summary>Enables/disables the primary button while the dialog is open. Defaults to <c>true</c>.</summary>
    public bool IsPrimaryButtonEnabled { get; init; } = true;
    /// <summary>Enables/disables the secondary button while the dialog is open. Defaults to <c>true</c>.</summary>
    public bool IsSecondaryButtonEnabled { get; init; } = true;
    /// <summary>Raised after the dialog finishes opening.</summary>
    public Action? OnOpened { get; init; }
    internal Action<WinUI.ContentDialog>[] Setters { get; init; } = [];
    internal override bool HasCallbacks =>
        OnClosed is not null || OnOpened is not null;
}

/// <summary>
/// A flyout attached to another element. Wrap the target element.
/// </summary>
public record FlyoutElement(
    Element Target,
    Element FlyoutContent
) : Element
{
    public bool IsOpen { get; init; }
    public FlyoutPlacementMode Placement { get; init; } = FlyoutPlacementMode.Auto;
    public Action? OnOpened { get; init; }
    public Action? OnClosed { get; init; }
    /// <summary>How the flyout reacts to clicks outside its bounds (Auto / Standard / Transient / TransientWithDismissOnPointerMoveAway).</summary>
    public FlyoutShowMode ShowMode { get; init; } = FlyoutShowMode.Auto;
    /// <summary>Whether the flyout animates on open/close. Defaults to <c>true</c>.</summary>
    public bool AreOpenCloseAnimationsEnabled { get; init; } = true;
    /// <summary>Element whose input is passed through the light-dismiss overlay (lets the user interact with one element behind the flyout).</summary>
    public Element? OverlayInputPassThroughElement { get; init; }
    internal Action<WinUI.Flyout>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnOpened is not null || OnClosed is not null;
}

/// <summary>
/// Describes a content flyout (used as a slot value on buttons or as a modifier attachment).
/// NOT independently mountable — the reconciler recognizes it in flyout slots.
/// </summary>
public record ContentFlyoutElement(Element Content) : Element
{
    public FlyoutPlacementMode Placement { get; init; } = FlyoutPlacementMode.Auto;
}

/// <summary>
/// Describes a menu flyout (used as a slot value on buttons or as a modifier attachment).
/// NOT independently mountable — the reconciler recognizes it in flyout slots.
/// </summary>
public record MenuFlyoutContentElement(MenuFlyoutItemBase[] Items) : Element
{
    public FlyoutPlacementMode Placement { get; init; } = FlyoutPlacementMode.Auto;
}

public record TeachingTipElement(
    string Title,
    string? Subtitle = null
) : Element
{
    public bool IsOpen { get; init; }
    public Element? Content { get; init; }
    public string? ActionButtonContent { get; init; }
    public Action? OnActionButtonClick { get; init; }
    public string? CloseButtonContent { get; init; }
    public Action? OnClosed { get; init; }
    /// <summary>Custom icon source rendered in the tip's leading slot.</summary>
    public IconData? IconSource { get; init; }
    /// <summary>Optional "hero" Element (image / banner) rendered above the title.</summary>
    public Element? HeroContent { get; init; }
    /// <summary>Extra margin around the tip when placed relative to its target.</summary>
    public Thickness PlacementMargin { get; init; }
    /// <summary>Preferred placement edge. Defaults to <c>Auto</c>.</summary>
    public TeachingTipPlacementMode PreferredPlacement { get; init; } = TeachingTipPlacementMode.Auto;
    internal Action<WinUI.TeachingTip>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnActionButtonClick is not null || OnClosed is not null;
}

// ════════════════════════════════════════════════════════════════════════
//  Status / Info elements
// ════════════════════════════════════════════════════════════════════════

public record InfoBarElement(
    string? Title = null,
    string? Message = null
) : Element
{
    public InfoBarSeverity Severity { get; init; } = InfoBarSeverity.Informational;
    public bool IsOpen { get; init; } = true;
    public bool IsClosable { get; init; } = true;
    public string? ActionButtonContent { get; init; }
    public Action? OnActionButtonClick { get; init; }
    public Action? OnClosed { get; init; }
    /// <summary>Custom icon source. When set, overrides the severity-based icon.</summary>
    public IconData? IconSource { get; init; }
    /// <summary>Custom rich content rendered below the message (e.g. links, buttons, an embedded form).</summary>
    public Element? Content { get; init; }
    internal override bool HasCallbacks => OnActionButtonClick is not null || OnClosed is not null;
    internal Action<WinUI.InfoBar>[] Setters { get; init; } = [];
}

public record InfoBadgeElement() : Element
{
    public int? Value { get; init; }
    public string? Icon { get; init; }
    internal Action<WinUI.InfoBadge>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  Menu elements
// ════════════════════════════════════════════════════════════════════════

public record MenuBarElement(MenuBarItemData[] Items) : Element
{
    internal Action<WinUI.MenuBar>[] Setters { get; init; } = [];
}

public record CommandBarElement(
    AppBarItemBase[]? PrimaryCommands = null,
    AppBarItemBase[]? SecondaryCommands = null
) : Element
{
    public CommandBarDefaultLabelPosition DefaultLabelPosition { get; init; } = CommandBarDefaultLabelPosition.Bottom;
    public bool IsOpen { get; init; }
    public Element? Content { get; init; }
    internal Action<WinUI.CommandBar>[] Setters { get; init; } = [];
}

public record MenuFlyoutElement(
    Element Target,
    MenuFlyoutItemBase[] Items
) : Element
{
    internal Action<WinUI.MenuFlyout>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  Templated collection elements (data-driven ListView/GridView/FlipView)
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Which WinUI control type a templated list element targets.
/// </summary>
public enum TemplatedControlKind { ListView, GridView, FlipView }

/// <summary>
/// Abstract base for data-driven items controls. Non-generic so the reconciler
/// can match on a single type in its switch expression (same pattern as LazyStackElementBase).
/// </summary>
public abstract record TemplatedListElementBase : Element
{
    public abstract TemplatedControlKind ControlKind { get; }
    public abstract int ItemCount { get; }
    public abstract int GetSelectedIndex();
    public abstract ListViewSelectionMode GetSelectionMode();
    public abstract string? GetHeader();
    public abstract bool GetIsItemClickEnabled();
    public abstract Element BuildItemView(int index);
    /// <summary>
    /// Projects the user's data item at <paramref name="index"/> through
    /// the typed peer's <c>KeySelector</c> to produce the stable identity
    /// string consumed by spec 042's keyed-list reconciliation pipeline.
    /// Phase 1: ListView/GridView/LazyVStack/LazyHStack only — FlipView
    /// pre-mounts so it does not participate.
    /// </summary>
    internal abstract string GetKeyAt(int index);
    public abstract void InvokeSelectionChanged(int index);
    public abstract void InvokeItemClick(int index);
    public abstract void ApplyControlSetters(object control);
    /// <summary>
    /// True when programmatic setter actions (.Set(...)) have been attached.
    /// Used by <see cref="Element.OwnPropsEqual"/> to suppress the reconcile-highlight
    /// short-circuit so the overlay correctly tags the control as modified
    /// (and ApplyControlSetters keeps running on every reconcile pass).
    /// Virtual + default-false so external types deriving from this public
    /// abstract record don't break — only Reactor's own derived records that
    /// expose Setters need to override.
    /// </summary>
    internal virtual bool HasSetters => false;

    /// <summary>
    /// Snapshot-style multi-select callback. Default no-op; typed peers
    /// (TemplatedListView/TemplatedGridView) override to materialize and
    /// invoke <c>OnSelectionChanged</c> with the typed items.
    /// </summary>
    internal virtual void InvokeMultiSelectionChanged(IReadOnlyList<int> indices) { }

    /// <summary>True when the derived peer has wired a typed multi-select callback.</summary>
    internal virtual bool HasMultiSelectionCallback => false;
}

public record TemplatedListViewElement<T>(
    IReadOnlyList<T> Items,
    Func<T, string> KeySelector,
    Func<T, int, Element> ViewBuilder
) : TemplatedListElementBase
{
    public int SelectedIndex { get; init; } = -1;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    public Action<T>? OnItemClick { get; init; }
    public ListViewSelectionMode SelectionMode { get; init; } = ListViewSelectionMode.Single;
    public string? Header { get; init; }
    /// <summary>
    /// Multi-select snapshot callback for the typed peer. Receives the full list
    /// of currently selected items (not just indices). Snapshot semantics — not
    /// raised on initial mount.
    /// </summary>
    public Action<IReadOnlyList<T>>? OnSelectionChanged { get; init; }
    internal Action<WinUI.ListView>[] Setters { get; init; } = [];

    public override TemplatedControlKind ControlKind => TemplatedControlKind.ListView;
    public override int ItemCount => Items.Count;
    public override int GetSelectedIndex() => SelectedIndex;
    public override ListViewSelectionMode GetSelectionMode() => SelectionMode;
    public override string? GetHeader() => Header;
    public override bool GetIsItemClickEnabled() => OnItemClick is not null;
    public override Element BuildItemView(int index) => ViewBuilder(Items[index], index);
    internal override string GetKeyAt(int index) => KeySelector(Items[index]);
    public override void InvokeSelectionChanged(int index) => OnSelectedIndexChanged?.Invoke(index);
    public override void InvokeItemClick(int index) =>
        OnItemClick?.Invoke(index >= 0 && index < Items.Count ? Items[index] : default!);
    public override void ApplyControlSetters(object control) =>
        Reconciler.ApplySetters(Setters, (WinUI.ListView)control);
    /// <summary>Snapshot-style multi-select callback. Materializes the typed items from the given indices.</summary>
    internal override void InvokeMultiSelectionChanged(IReadOnlyList<int> indices)
    {
        if (OnSelectionChanged is null) return;
        var selected = new List<T>(indices.Count);
        foreach (var i in indices)
            if (i >= 0 && i < Items.Count) selected.Add(Items[i]);
        OnSelectionChanged(selected);
    }
    internal override bool HasMultiSelectionCallback => OnSelectionChanged is not null;
    internal override bool HasCallbacks =>
        OnSelectedIndexChanged is not null || OnItemClick is not null || OnSelectionChanged is not null;
    internal override bool HasSetters => Setters.Length > 0;
}

public record TemplatedGridViewElement<T>(
    IReadOnlyList<T> Items,
    Func<T, string> KeySelector,
    Func<T, int, Element> ViewBuilder
) : TemplatedListElementBase
{
    public int SelectedIndex { get; init; } = -1;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    public Action<T>? OnItemClick { get; init; }
    public ListViewSelectionMode SelectionMode { get; init; } = ListViewSelectionMode.Single;
    public string? Header { get; init; }
    /// <summary>
    /// Multi-select snapshot callback for the typed peer (see
    /// <see cref="TemplatedListViewElement{T}.OnSelectionChanged"/>).
    /// </summary>
    public Action<IReadOnlyList<T>>? OnSelectionChanged { get; init; }
    internal Action<WinUI.GridView>[] Setters { get; init; } = [];

    public override TemplatedControlKind ControlKind => TemplatedControlKind.GridView;
    public override int ItemCount => Items.Count;
    public override int GetSelectedIndex() => SelectedIndex;
    public override ListViewSelectionMode GetSelectionMode() => SelectionMode;
    public override string? GetHeader() => Header;
    public override bool GetIsItemClickEnabled() => OnItemClick is not null;
    public override Element BuildItemView(int index) => ViewBuilder(Items[index], index);
    internal override string GetKeyAt(int index) => KeySelector(Items[index]);
    public override void InvokeSelectionChanged(int index) => OnSelectedIndexChanged?.Invoke(index);
    public override void InvokeItemClick(int index) =>
        OnItemClick?.Invoke(index >= 0 && index < Items.Count ? Items[index] : default!);
    public override void ApplyControlSetters(object control) =>
        Reconciler.ApplySetters(Setters, (WinUI.GridView)control);
    /// <summary>Snapshot-style multi-select callback. Materializes the typed items from the given indices.</summary>
    internal override void InvokeMultiSelectionChanged(IReadOnlyList<int> indices)
    {
        if (OnSelectionChanged is null) return;
        var selected = new List<T>(indices.Count);
        foreach (var i in indices)
            if (i >= 0 && i < Items.Count) selected.Add(Items[i]);
        OnSelectionChanged(selected);
    }
    internal override bool HasMultiSelectionCallback => OnSelectionChanged is not null;
    internal override bool HasCallbacks =>
        OnSelectedIndexChanged is not null || OnItemClick is not null || OnSelectionChanged is not null;
    internal override bool HasSetters => Setters.Length > 0;
}

public record TemplatedFlipViewElement<T>(
    IReadOnlyList<T> Items,
    Func<T, string> KeySelector,
    Func<T, int, Element> ViewBuilder
) : TemplatedListElementBase
{
    public int SelectedIndex { get; init; } = 0;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    internal Action<WinUI.FlipView>[] Setters { get; init; } = [];

    public override TemplatedControlKind ControlKind => TemplatedControlKind.FlipView;
    public override int ItemCount => Items.Count;
    public override int GetSelectedIndex() => SelectedIndex;
    public override ListViewSelectionMode GetSelectionMode() => ListViewSelectionMode.Single;
    public override string? GetHeader() => null;
    public override bool GetIsItemClickEnabled() => false;
    public override Element BuildItemView(int index) => ViewBuilder(Items[index], index);
    // FlipView pre-mounts all items so it does not participate in the
    // keyed-list ObservableCollection delta channel; return a positional
    // synthetic key so any external consumer that asks still gets a value.
    internal override string GetKeyAt(int index) =>
        KeySelector is not null && (uint)index < (uint)Items.Count
            ? KeySelector(Items[index])
            : $"__flip_{index}";
    public override void InvokeSelectionChanged(int index) => OnSelectedIndexChanged?.Invoke(index);
    public override void InvokeItemClick(int index) { }
    public override void ApplyControlSetters(object control) =>
        Reconciler.ApplySetters(Setters, (WinUI.FlipView)control);
    internal override bool HasCallbacks => OnSelectedIndexChanged is not null;
    internal override bool HasSetters => Setters.Length > 0;
}

// ════════════════════════════════════════════════════════════════════════
//  Virtualized collection elements (backed by ItemsRepeater)
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Abstract base for virtualized lazy stacks. Non-generic so the reconciler
/// can match on a single type in its switch expression.
/// </summary>
public abstract record LazyStackElementBase : Element
{
    public abstract Orientation Orientation { get; }
    public abstract double Spacing { get; init; }
    public abstract double EstimatedItemSize { get; init; }
    public abstract object GetItemsSource();
    /// <summary>Total number of items in the source list.</summary>
    public abstract int ItemCount { get; }
    /// <summary>
    /// Projects the user's data item at <paramref name="index"/> through
    /// the typed peer's <c>KeySelector</c> to produce the stable identity
    /// string consumed by spec 042's keyed-list reconciliation pipeline.
    /// </summary>
    internal abstract string GetKeyAt(int index);
    public abstract IElementFactory CreateFactory(Reconciler reconciler, Action requestRerender, ElementPool? pool);
    /// <summary>
    /// Update an existing factory's items and viewBuilder in place, avoiding
    /// ItemsRepeater re-realization. Returns true if the factory was updated.
    /// </summary>
    public abstract bool TryUpdateFactory(IElementFactory existingFactory);
    /// <summary>
    /// Spec 042 Phase 1: hand the factory the host's <see cref="Internal.ReactorListState"/>
    /// so its element-tracking dictionary can be keyed by stable
    /// <see cref="Internal.ReactorRow.Key"/> instead of by realized index.
    /// Insertions at non-tail positions used to shift every entry's effective
    /// index — keying by string makes the mapping reorder-stable.
    /// </summary>
    internal abstract void AttachListStateToFactory(IElementFactory factory, Internal.ReactorListState listState);
    /// <summary>
    /// After updating the factory in place, reconcile all realized items
    /// with the new viewBuilder output (property diffs only, no collection changes).
    /// </summary>
    public abstract void RefreshRealizedItems(IElementFactory factory, WinUI.ItemsRepeater repeater);
    internal Action<WinUI.ScrollViewer>[] ScrollViewerSetters { get; init; } = [];
    internal Action<WinUI.ItemsRepeater>[] RepeaterSetters { get; init; } = [];
}

public record LazyVStackElement<T>(
    IReadOnlyList<T> Items,
    Func<T, string> KeySelector,
    Func<T, int, Element> ViewBuilder
) : LazyStackElementBase
{
    public override Orientation Orientation => Orientation.Vertical;
    public override double Spacing { get; init; } = 8;
    public override double EstimatedItemSize { get; init; } = 40;
    public override int ItemCount => Items.Count;
    internal override string GetKeyAt(int index) => KeySelector(Items[index]);

    public override object GetItemsSource() =>
        Enumerable.Range(0, Items.Count).ToList();

    public override IElementFactory CreateFactory(Reconciler reconciler, Action requestRerender, ElementPool? pool) =>
        new ElementFactory<T>(Items, ViewBuilder, reconciler, requestRerender, pool);

    public override bool TryUpdateFactory(IElementFactory existingFactory)
    {
        if (existingFactory is ElementFactory<T> f) { f.UpdateInPlace(Items, ViewBuilder); return true; }
        return false;
    }

    public override void RefreshRealizedItems(IElementFactory factory, WinUI.ItemsRepeater repeater)
    {
        if (factory is ElementFactory<T> f) f.RefreshRealizedItems(repeater);
    }

    internal override void AttachListStateToFactory(IElementFactory factory, Internal.ReactorListState listState)
    {
        if (factory is ElementFactory<T> f) f.AttachListState(listState);
    }
}

public record LazyHStackElement<T>(
    IReadOnlyList<T> Items,
    Func<T, string> KeySelector,
    Func<T, int, Element> ViewBuilder
) : LazyStackElementBase
{
    public override Orientation Orientation => Orientation.Horizontal;
    public override double Spacing { get; init; } = 8;
    public override double EstimatedItemSize { get; init; } = 100;
    public override int ItemCount => Items.Count;
    internal override string GetKeyAt(int index) => KeySelector(Items[index]);

    public override object GetItemsSource() =>
        Enumerable.Range(0, Items.Count).ToList();

    public override IElementFactory CreateFactory(Reconciler reconciler, Action requestRerender, ElementPool? pool) =>
        new ElementFactory<T>(Items, ViewBuilder, reconciler, requestRerender, pool);

    public override bool TryUpdateFactory(IElementFactory existingFactory)
    {
        if (existingFactory is ElementFactory<T> f) { f.UpdateInPlace(Items, ViewBuilder); return true; }
        return false;
    }

    public override void RefreshRealizedItems(IElementFactory factory, WinUI.ItemsRepeater repeater)
    {
        if (factory is ElementFactory<T> f) f.RefreshRealizedItems(repeater);
    }

    internal override void AttachListStateToFactory(IElementFactory factory, Internal.ReactorListState listState)
    {
        if (factory is ElementFactory<T> f) f.AttachListState(listState);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Shape elements
// ════════════════════════════════════════════════════════════════════════

public record RectangleElement() : Element
{
    public Brush? Fill { get; init; }
    public Brush? Stroke { get; init; }
    public double StrokeThickness { get; init; }
    public double RadiusX { get; init; }
    public double RadiusY { get; init; }
    internal Action<WinShapes.Rectangle>[] Setters { get; init; } = [];
}

public record EllipseElement() : Element
{
    public Brush? Fill { get; init; }
    public Brush? Stroke { get; init; }
    public double StrokeThickness { get; init; }
    internal Action<WinShapes.Ellipse>[] Setters { get; init; } = [];
}

public record LineElement() : Element
{
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
    public Brush? Stroke { get; init; }
    public double StrokeThickness { get; init; } = 1;
    internal Action<WinShapes.Line>[] Setters { get; init; } = [];
}

public record PathElement() : Element
{
    /// <summary>
    /// Pre-parsed WinUI Geometry. When null, the reconciler resolves from <see cref="PathDataString"/>.
    /// Callers that construct PathElement directly (not via D3Path) can set this for non-SVG geometries.
    /// </summary>
    public Geometry? Data { get; init; }
    /// <summary>
    /// The original SVG path data string. When set, geometry is parsed lazily by the reconciler —
    /// only when mounting or when the string changes between renders. This avoids expensive
    /// PathDataParser.Parse + COM Geometry creation on every tree build.
    /// </summary>
    public string? PathDataString { get; init; }
    public Brush? Fill { get; init; }
    public Brush? Stroke { get; init; }
    public double StrokeThickness { get; init; } = 1;
    public Microsoft.UI.Xaml.Media.DoubleCollection? StrokeDashArray { get; init; }
    public Transform? RenderTransform { get; init; }
    /// <summary>Cap rendered at the start of an open stroke. Defaults to <c>Flat</c>.</summary>
    public PenLineCap StrokeStartLineCap { get; init; } = PenLineCap.Flat;
    /// <summary>Cap rendered at the end of an open stroke. Defaults to <c>Flat</c>.</summary>
    public PenLineCap StrokeEndLineCap { get; init; } = PenLineCap.Flat;
    /// <summary>Join style between two connected stroke segments. Defaults to <c>Miter</c>.</summary>
    public PenLineJoin StrokeLineJoin { get; init; } = PenLineJoin.Miter;
    /// <summary>Maximum extent of a miter join relative to half the stroke thickness. Defaults to 10.</summary>
    public double StrokeMiterLimit { get; init; } = 10;
    /// <summary>Cap rendered on dashes when <see cref="StrokeDashArray"/> is set. Defaults to <c>Flat</c>.</summary>
    public PenLineCap StrokeDashCap { get; init; } = PenLineCap.Flat;
    /// <summary>Distance into the dash pattern at which to begin drawing. Defaults to 0.</summary>
    public double StrokeDashOffset { get; init; }
    /// <summary>How interior regions are determined for fills. Defaults to <c>EvenOdd</c>.</summary>
    public FillRule FillRule { get; init; } = FillRule.EvenOdd;
    internal Action<WinShapes.Path>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  Additional layout elements
// ════════════════════════════════════════════════════════════════════════

public record RelativePanelElement(Element[] Children) : Element
{
    internal Action<WinUI.RelativePanel>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  Additional media elements
// ════════════════════════════════════════════════════════════════════════

public record MediaPlayerElementElement(string? Source = null) : Element
{
    public bool AreTransportControlsEnabled { get; init; } = true;
    public bool AutoPlay { get; init; }

    /// <summary>
    /// Raised when the underlying <c>MediaPlayer</c> finishes opening the
    /// source. Marshalled to the element's UI thread; may fire after the
    /// element has unmounted (the handler is safe to ignore in that case).
    /// </summary>
    public Action? OnMediaOpened { get; init; }

    /// <summary>
    /// Raised when playback reaches the end of the source. Marshalled to the
    /// UI thread.
    /// </summary>
    public Action? OnMediaEnded { get; init; }

    /// <summary>
    /// Raised when the underlying <c>MediaPlayer</c> fails to open or play.
    /// Receives the failure error message as a string. Marshalled to the UI
    /// thread.
    /// </summary>
    public Action<string>? OnMediaFailed { get; init; }

    internal Action<WinUI.MediaPlayerElement>[] Setters { get; init; } = [];
}

public record AnimatedVisualPlayerElement() : Element
{
    public bool AutoPlay { get; init; }
    internal Action<WinUI.AnimatedVisualPlayer>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  Additional collection elements
// ════════════════════════════════════════════════════════════════════════

public record SemanticZoomElement(Element ZoomedInView, Element ZoomedOutView) : Element
{
    internal Action<WinUI.SemanticZoom>[] Setters { get; init; } = [];
}

public record ListBoxElement(string[] Items) : Element
{
    public int SelectedIndex { get; init; } = -1;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    /// <summary>
    /// Multi-select snapshot callback. Receives the FULL list of currently
    /// selected indices. Use this in multi-select selection modes.
    /// </summary>
    public Action<IReadOnlyList<int>>? OnSelectionChanged { get; init; }
    internal Action<WinUI.ListBox>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnSelectedIndexChanged is not null || OnSelectionChanged is not null;
}

// ════════════════════════════════════════════════════════════════════════
//  Additional navigation elements
// ════════════════════════════════════════════════════════════════════════

public record SelectorBarElement(SelectorBarItemData[] Items) : Element
{
    public int SelectedIndex { get; init; } = 0;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    internal Action<WinUI.SelectorBar>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnSelectedIndexChanged is not null;
}

public record SelectorBarItemData(string Text, string? Icon = null);

public record PipsPagerElement(int NumberOfPages) : Element
{
    public int SelectedPageIndex { get; init; }
    public Action<int>? OnSelectedPageIndexChanged { get; init; }
    /// <summary>Whether the selected index wraps around the ends. Defaults to <c>None</c>.</summary>
    public PipsPagerWrapMode WrapMode { get; init; } = PipsPagerWrapMode.None;
    /// <summary>Maximum number of visible pips. Defaults to 5 (matches WinUI).</summary>
    public int MaxVisiblePips { get; init; } = 5;
    /// <summary>When the previous button shows. Defaults to <c>Collapsed</c>.</summary>
    public PipsPagerButtonVisibility PreviousButtonVisibility { get; init; } = PipsPagerButtonVisibility.Collapsed;
    /// <summary>When the next button shows. Defaults to <c>Collapsed</c>.</summary>
    public PipsPagerButtonVisibility NextButtonVisibility { get; init; } = PipsPagerButtonVisibility.Collapsed;
    internal Action<WinUI.PipsPager>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnSelectedPageIndexChanged is not null;
}

public record AnnotatedScrollBarElement() : Element
{
    internal Action<WinUI.AnnotatedScrollBar>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  Additional overlay / container elements
// ════════════════════════════════════════════════════════════════════════

public record PopupElement(Element Child) : Element
{
    public bool IsOpen { get; init; }
    public bool IsLightDismissEnabled { get; init; } = true;
    public double HorizontalOffset { get; init; }
    public double VerticalOffset { get; init; }
    public Action? OnOpened { get; init; }
    public Action? OnClosed { get; init; }
    internal Action<WinPrim.Popup>[] Setters { get; init; } = [];
}

public record RefreshContainerElement(Element Content) : Element
{
    public Action? OnRefreshRequested { get; init; }
    /// <summary>Direction the user pulls to trigger refresh. Defaults to <c>TopToBottom</c>.</summary>
    public RefreshPullDirection PullDirection { get; init; } = RefreshPullDirection.TopToBottom;
    internal Action<WinUI.RefreshContainer>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnRefreshRequested is not null;
}

public record CommandBarFlyoutElement(
    Element Target,
    AppBarItemBase[]? PrimaryCommands = null,
    AppBarItemBase[]? SecondaryCommands = null
) : Element
{
    public FlyoutPlacementMode Placement { get; init; } = FlyoutPlacementMode.Auto;
    internal Action<WinUI.CommandBarFlyout>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  Additional date/time elements
// ════════════════════════════════════════════════════════════════════════

public record CalendarViewElement() : Element
{
    public CalendarViewSelectionMode SelectionMode { get; init; } = CalendarViewSelectionMode.Single;
    public bool IsGroupLabelVisible { get; init; } = true;
    public bool IsOutOfScopeEnabled { get; init; } = true;
    public string? CalendarIdentifier { get; init; }
    public string? Language { get; init; }
    /// <summary>Earliest selectable date. <c>null</c> = WinUI default (~100 years back).</summary>
    public DateTimeOffset? MinDate { get; init; }
    /// <summary>Latest selectable date. <c>null</c> = WinUI default (~100 years ahead).</summary>
    public DateTimeOffset? MaxDate { get; init; }
    /// <summary>Day of the week that starts each row. <c>null</c> = locale default.</summary>
    public global::Windows.Globalization.DayOfWeek? FirstDayOfWeek { get; init; }
    /// <summary>How many week rows to display in month mode (2–8). Defaults to 6.</summary>
    public int NumberOfWeeksInView { get; init; } = 6;
    /// <summary>Initial display mode (Month / Year / Decade). Defaults to <c>Month</c>.</summary>
    public CalendarViewDisplayMode DisplayMode { get; init; } = CalendarViewDisplayMode.Month;

    /// <summary>
    /// Initial selection. Bind for declarative selection on mount; subsequent
    /// programmatic updates re-apply only when the list reference differs.
    /// Combine with <see cref="OnSelectedDatesChanged"/> for two-way binding
    /// in multi-select mode.
    /// </summary>
    public IReadOnlyList<DateTimeOffset>? SelectedDates { get; init; }

    /// <summary>
    /// Raised when the user changes the selection. Receives a snapshot of the
    /// full selection (not just added/removed dates) — easier to bind into
    /// component state without diffing. Not raised on the initial declarative
    /// selection applied at mount.
    /// </summary>
    public Action<IReadOnlyList<DateTimeOffset>>? OnSelectedDatesChanged { get; init; }

    internal Action<WinUI.CalendarView>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  SwipeControl
// ════════════════════════════════════════════════════════════════════════

public record SwipeItemData(
    string Text,
    Action? OnInvoked = null,
    Microsoft.UI.Xaml.Controls.IconSource? IconSource = null,
    Microsoft.UI.Xaml.Media.Brush? Background = null,
    Microsoft.UI.Xaml.Media.Brush? Foreground = null,
    Microsoft.UI.Xaml.Controls.SwipeBehaviorOnInvoked BehaviorOnInvoked = Microsoft.UI.Xaml.Controls.SwipeBehaviorOnInvoked.Auto);

public record SwipeControlElement(Element Content) : Element
{
    public SwipeItemData[]? LeftItems { get; init; }
    public SwipeItemData[]? RightItems { get; init; }
    public Microsoft.UI.Xaml.Controls.SwipeMode LeftItemsMode { get; init; } = Microsoft.UI.Xaml.Controls.SwipeMode.Reveal;
    public Microsoft.UI.Xaml.Controls.SwipeMode RightItemsMode { get; init; } = Microsoft.UI.Xaml.Controls.SwipeMode.Reveal;
    internal Action<WinUI.SwipeControl>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  AnimatedIcon
// ════════════════════════════════════════════════════════════════════════

public record AnimatedIconElement() : Element
{
    public object? Source { get; init; }
    public IconSource? FallbackIconSource { get; init; }
    internal Action<WinUI.AnimatedIcon>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  ParallaxView
// ════════════════════════════════════════════════════════════════════════

public record ParallaxViewElement(Element Child) : Element
{
    public double VerticalShift { get; init; }
    public double HorizontalShift { get; init; }
    /// <summary>Source UIElement that drives the parallax (typically a ScrollViewer / ListView). <c>null</c> uses the nearest scroller.</summary>
    public UIElement? Source { get; init; }
    /// <summary>Vertical-axis source offset (in pixels) at which parallax begins. Defaults to 0.</summary>
    public double VerticalSourceStartOffset { get; init; }
    /// <summary>Vertical-axis source offset (in pixels) at which parallax ends. Defaults to 0 (auto).</summary>
    public double VerticalSourceEndOffset { get; init; }
    internal Action<WinUI.ParallaxView>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  MapControl
// ════════════════════════════════════════════════════════════════════════

public record MapControlElement() : Element
{
    public string? MapServiceToken { get; init; }
    public double ZoomLevel { get; init; } = 1;
    internal Action<WinUI.MapControl>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  Frame
// ════════════════════════════════════════════════════════════════════════

public record FrameElement() : Element
{
    public Type? SourcePageType { get; init; }
    public object? NavigationParameter { get; init; }

    /// <summary>Raised after a successful navigation. Receives the new <c>SourcePageType</c>.</summary>
    public Action<Type>? OnNavigated { get; init; }

    /// <summary>Raised before navigation begins. Receives the target <c>SourcePageType</c>. Cancellation is not supported via this fluent — use <c>.Set(...)</c> to wire the raw <c>Navigating</c> event for that.</summary>
    public Action<Type>? OnNavigating { get; init; }

    /// <summary>Raised when a navigation fails. Receives the target <c>SourcePageType</c> and the failure exception.</summary>
    public Action<Type, Exception>? OnNavigationFailed { get; init; }

    internal Action<WinUI.Frame>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  ItemsView
// ════════════════════════════════════════════════════════════════════════

public enum ItemsViewLayoutKind
{
    StackLayout,
    LinedFlowLayout,
    UniformGridLayout,
}

public record ItemsViewElement<T>(
    IReadOnlyList<T> Items,
    Func<T, string> KeySelector,
    Func<T, int, Element> ViewBuilder
) : Element
{
    public ItemsViewLayoutKind LayoutKind { get; init; } = ItemsViewLayoutKind.StackLayout;
    public ItemsViewSelectionMode SelectionMode { get; init; } = ItemsViewSelectionMode.Single;
    public bool IsItemInvokedEnabled { get; init; }
    public Action<T>? OnItemInvoked { get; init; }
    /// <summary>
    /// Multi-select snapshot callback. Receives the full list of currently
    /// selected items. Use this when <see cref="SelectionMode"/> is Multiple
    /// or Extended.
    /// </summary>
    public Action<IReadOnlyList<T>>? OnSelectionChanged { get; init; }
    internal Action<WinUI.ItemsView>[] Setters { get; init; } = [];
    internal override bool HasCallbacks =>
        OnItemInvoked is not null || OnSelectionChanged is not null;
}
