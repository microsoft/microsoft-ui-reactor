// Standalone FlexPanel for WinUI3, implemented in Microsoft.UI.Reactor.Layout.
// No dependency on Microsoft.UI.Reactor.Core — usable in any WinUI3 app.
//
// AI-HINT: This is a WinUI Panel that delegates layout to Yoga.
// Two-pass measure: Pass 1 = content-size (NaN width/height), Pass 2 = flex distribution
// (definite main axis to enable grow/shrink). Arrange reads cached results from Yoga.
// Each child has a cached YogaNode; attached properties (Grow, Shrink, Basis, etc.)
// are synced to Yoga nodes in SyncYogaTree(). MeasureFunc bridge lets Yoga call
// back into WinUI Measure for leaf children that need intrinsic sizing.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Reactor.Layout;
using Windows.Foundation;

namespace Microsoft.UI.Reactor.Layout;

/// <summary>
/// A WinUI3 Panel that implements CSS Flexbox layout using the Yoga layout engine.
/// Can be used standalone in XAML or through the Reactor framework.
/// </summary>
public partial class FlexPanel : Panel
{
    // ── Yoga node cache: one YogaNode per UIElement child ──
    // Per-instance YogaConfig so PointScaleFactor can track the live
    // XamlRoot.RasterizationScale; default 1.0 rounds to integer DIPs and
    // disagrees with WinUI's physical-pixel layout rounding on non-100%
    // scales, producing ±1 px wobble during resize.
    private readonly YogaConfig _yogaConfig = new();
    private readonly Dictionary<UIElement, YogaNode> _nodeCache = new();
    private readonly YogaNode _rootNode;
    private readonly HashSet<UIElement> _syncCurrentChildren = new();
    private readonly List<UIElement> _syncToRemove = new();

    public FlexPanel()
    {
        _rootNode = new YogaNode(_yogaConfig);
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// Read the current rasterization scale from this panel's XamlRoot and
    /// update <see cref="YogaConfig.PointScaleFactor"/> if it changed. Called
    /// from <see cref="MeasureOverride"/> so the scale tracks the live system
    /// DPI without subscribing to <c>XamlRoot.Changed</c>. The subscription
    /// approach pinned every FlexPanel through XamlRoot's multicast delegate
    /// list — fatal for virtualized lists where ItemsRepeater's recycle path
    /// does not reliably fire Unloaded on every recycled container.
    /// </summary>
    private void SyncPointScaleLazy()
    {
        var scale = (float)(XamlRoot?.RasterizationScale ?? 1.0);
        if (scale <= 0) return;
        if (Math.Abs(_yogaConfig.PointScaleFactor - scale) < 0.0001f) return;
        _yogaConfig.PointScaleFactor = scale;
        _rootNode.MarkDirtyAndPropagate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Clear Yoga node cache when removed from the visual tree to avoid leaking references
        foreach (var node in _nodeCache.Values)
            _rootNode.RemoveChild(node);
        _nodeCache.Clear();
    }

    // ── Container dependency properties ──

    public static readonly DependencyProperty DirectionProperty =
        DependencyProperty.Register(nameof(Direction), typeof(FlexDirection), typeof(FlexPanel),
            new PropertyMetadata(FlexDirection.Row, OnContainerPropertyChanged));

    public static readonly DependencyProperty JustifyContentProperty =
        DependencyProperty.Register(nameof(JustifyContent), typeof(FlexJustify), typeof(FlexPanel),
            new PropertyMetadata(FlexJustify.FlexStart, OnContainerPropertyChanged));

    public static readonly DependencyProperty AlignItemsProperty =
        DependencyProperty.Register(nameof(AlignItems), typeof(FlexAlign), typeof(FlexPanel),
            new PropertyMetadata(FlexAlign.Stretch, OnContainerPropertyChanged));

    public static readonly DependencyProperty AlignContentProperty =
        DependencyProperty.Register(nameof(AlignContent), typeof(FlexAlign), typeof(FlexPanel),
            new PropertyMetadata(FlexAlign.FlexStart, OnContainerPropertyChanged));

    public static readonly DependencyProperty WrapProperty =
        DependencyProperty.Register(nameof(Wrap), typeof(FlexWrap), typeof(FlexPanel),
            new PropertyMetadata(FlexWrap.NoWrap, OnContainerPropertyChanged));

    public static readonly DependencyProperty LayoutDirectionProperty =
        DependencyProperty.Register(nameof(LayoutDirection), typeof(FlexLayoutDirection), typeof(FlexPanel),
            new PropertyMetadata(FlexLayoutDirection.LTR, OnContainerPropertyChanged));

    public static readonly DependencyProperty ColumnGapProperty =
        DependencyProperty.Register(nameof(ColumnGap), typeof(double), typeof(FlexPanel),
            new PropertyMetadata(0.0, OnContainerPropertyChanged));

    public static readonly DependencyProperty RowGapProperty =
        DependencyProperty.Register(nameof(RowGap), typeof(double), typeof(FlexPanel),
            new PropertyMetadata(0.0, OnContainerPropertyChanged));

    public static readonly DependencyProperty FlexPaddingProperty =
        DependencyProperty.Register(nameof(FlexPadding), typeof(Thickness), typeof(FlexPanel),
            new PropertyMetadata(default(Thickness), OnContainerPropertyChanged));

    public FlexDirection Direction
    {
        get => (FlexDirection)GetValue(DirectionProperty);
        set => SetValue(DirectionProperty, value);
    }

    public FlexJustify JustifyContent
    {
        get => (FlexJustify)GetValue(JustifyContentProperty);
        set => SetValue(JustifyContentProperty, value);
    }

    public FlexAlign AlignItems
    {
        get => (FlexAlign)GetValue(AlignItemsProperty);
        set => SetValue(AlignItemsProperty, value);
    }

    public FlexAlign AlignContent
    {
        get => (FlexAlign)GetValue(AlignContentProperty);
        set => SetValue(AlignContentProperty, value);
    }

    public FlexWrap Wrap
    {
        get => (FlexWrap)GetValue(WrapProperty);
        set => SetValue(WrapProperty, value);
    }

    public FlexLayoutDirection LayoutDirection
    {
        get => (FlexLayoutDirection)GetValue(LayoutDirectionProperty);
        set => SetValue(LayoutDirectionProperty, value);
    }

    public double ColumnGap
    {
        get => (double)GetValue(ColumnGapProperty);
        set => SetValue(ColumnGapProperty, value);
    }

    public double RowGap
    {
        get => (double)GetValue(RowGapProperty);
        set => SetValue(RowGapProperty, value);
    }

    public Thickness FlexPadding
    {
        get => (Thickness)GetValue(FlexPaddingProperty);
        set => SetValue(FlexPaddingProperty, value);
    }

    private static void OnContainerPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlexPanel panel)
            panel.InvalidateMeasure();
    }

    // ── Attached properties (for children) ──

    public static readonly DependencyProperty GrowProperty =
        DependencyProperty.RegisterAttached("Grow", typeof(double), typeof(FlexPanel),
            new PropertyMetadata(0.0, OnChildPropertyChanged));

    public static readonly DependencyProperty ShrinkProperty =
        DependencyProperty.RegisterAttached("Shrink", typeof(double), typeof(FlexPanel),
            new PropertyMetadata(1.0, OnChildPropertyChanged));

    public static readonly DependencyProperty BasisProperty =
        DependencyProperty.RegisterAttached("Basis", typeof(double), typeof(FlexPanel),
            new PropertyMetadata(double.NaN, OnChildPropertyChanged));

    // CSS `min-width` / `min-height` on a flex item. Sentinel NaN = `auto`
    // (CSS Flexbox §4.5 automatic minimum size); any non-negative finite value
    // (including 0) is treated as an explicit point min that suppresses the
    // auto/min-content heuristic.
    //
    // These are named `FlexMinWidthProperty`/`FlexMinHeightProperty` rather
    // than `MinWidthProperty`/`MinHeightProperty` to avoid shadowing
    // `FrameworkElement.MinWidthProperty`/`MinHeightProperty` on this public
    // type. The inherited WinUI DPs and these attached Yoga DPs coexist on
    // the same control with different semantics: `FrameworkElement.MinWidth`
    // forces Measure to return ≥X; this attached property tells Yoga to
    // clamp the flex-resolved slot to ≥X. Setting one does not affect the
    // other (see `CssWinUI_NativeMinWidthSeparateFromFlexMinWidth` selftest).
    public static readonly DependencyProperty FlexMinWidthProperty =
        DependencyProperty.RegisterAttached("FlexMinWidth", typeof(double), typeof(FlexPanel),
            new PropertyMetadata(double.NaN, OnChildPropertyChanged));

    public static readonly DependencyProperty FlexMinHeightProperty =
        DependencyProperty.RegisterAttached("FlexMinHeight", typeof(double), typeof(FlexPanel),
            new PropertyMetadata(double.NaN, OnChildPropertyChanged));

    public static readonly DependencyProperty AlignSelfProperty =
        DependencyProperty.RegisterAttached("AlignSelf", typeof(FlexAlign), typeof(FlexPanel),
            new PropertyMetadata(FlexAlign.Auto, OnChildPropertyChanged));

    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.RegisterAttached("Position", typeof(FlexPositionType), typeof(FlexPanel),
            new PropertyMetadata(FlexPositionType.Relative, OnChildPropertyChanged));

    public static readonly DependencyProperty LeftProperty =
        DependencyProperty.RegisterAttached("Left", typeof(double), typeof(FlexPanel),
            new PropertyMetadata(double.NaN, OnChildPropertyChanged));

    public static readonly DependencyProperty TopProperty =
        DependencyProperty.RegisterAttached("Top", typeof(double), typeof(FlexPanel),
            new PropertyMetadata(double.NaN, OnChildPropertyChanged));

    public static readonly DependencyProperty RightProperty =
        DependencyProperty.RegisterAttached("Right", typeof(double), typeof(FlexPanel),
            new PropertyMetadata(double.NaN, OnChildPropertyChanged));

    public static readonly DependencyProperty BottomProperty =
        DependencyProperty.RegisterAttached("Bottom", typeof(double), typeof(FlexPanel),
            new PropertyMetadata(double.NaN, OnChildPropertyChanged));

    // Attached property static accessors
    public static void SetGrow(UIElement el, double value) => el.SetValue(GrowProperty, value);
    public static double GetGrow(UIElement el) => (double)el.GetValue(GrowProperty);

    public static void SetShrink(UIElement el, double value) => el.SetValue(ShrinkProperty, value);
    public static double GetShrink(UIElement el) => (double)el.GetValue(ShrinkProperty);

    public static void SetBasis(UIElement el, double value) => el.SetValue(BasisProperty, value);
    public static double GetBasis(UIElement el) => (double)el.GetValue(BasisProperty);

    public static void SetMinWidth(UIElement el, double value) => el.SetValue(FlexMinWidthProperty, value);
    public static double GetMinWidth(UIElement el) => (double)el.GetValue(FlexMinWidthProperty);

    public static void SetMinHeight(UIElement el, double value) => el.SetValue(FlexMinHeightProperty, value);
    public static double GetMinHeight(UIElement el) => (double)el.GetValue(FlexMinHeightProperty);

    public static void SetAlignSelf(UIElement el, FlexAlign value) => el.SetValue(AlignSelfProperty, value);
    public static FlexAlign GetAlignSelf(UIElement el) => (FlexAlign)el.GetValue(AlignSelfProperty);

    public static void SetPosition(UIElement el, FlexPositionType value) => el.SetValue(PositionProperty, value);
    public static FlexPositionType GetPosition(UIElement el) => (FlexPositionType)el.GetValue(PositionProperty);

    public static void SetLeft(UIElement el, double value) => el.SetValue(LeftProperty, value);
    public static double GetLeft(UIElement el) => (double)el.GetValue(LeftProperty);

    public static void SetTop(UIElement el, double value) => el.SetValue(TopProperty, value);
    public static double GetTop(UIElement el) => (double)el.GetValue(TopProperty);

    public static void SetRight(UIElement el, double value) => el.SetValue(RightProperty, value);
    public static double GetRight(UIElement el) => (double)el.GetValue(RightProperty);

    public static void SetBottom(UIElement el, double value) => el.SetValue(BottomProperty, value);
    public static double GetBottom(UIElement el) => (double)el.GetValue(BottomProperty);

    private static void OnChildPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement el && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(el) is FlexPanel panel)
            panel.InvalidateMeasure();
    }

    // ── Layout ──

    // MeasureOverride — CSS block-level flex container semantics.
    //
    // CSS rule: a flex container is a block-level box. Its INLINE axis (width,
    // for horizontal writing mode) is resolved against the containing block
    // BEFORE flex layout runs — i.e. `width: auto` fills the parent's content
    // width. Its BLOCK axis (height) is `auto` → content-sized from children.
    // This is independent of flex-direction; direction only controls how
    // children flow within the container.
    //
    // Translating to Yoga: the root node is always called with a DEFINITE
    // inline-axis size (availableSize.Width when finite) and NaN on the block
    // axis. Children measured under this rule see their cross-axis constraint
    // naturally (align-items: stretch = fill container width), so text
    // controls (RichTextBlock, TextBlock) wrap correctly in a single pass —
    // no expensive infinite-width measurement followed by reflow.
    //
    // Escape hatch — `HorizontalAlignment != Stretch` on the FlexPanel itself
    // maps to CSS `width: fit-content`. In that case the inline axis is NaN
    // (content-size) capped by availableSize.Width. Slower for text-heavy
    // children, but the user opted in.

    // Cached child layout results from MeasureOverride, reused in ArrangeOverride
    // to avoid re-running Yoga (which calls child.Measure()) during the arrange
    // pass — calling Measure during Arrange can trigger LayoutCycleException.
    private struct ChildLayout { public float X, Y, Width, Height; }
    private readonly List<ChildLayout> _cachedChildLayouts = new();
    private Size _cachedDesiredSize;
    private bool _arranging;

    // Tracks which children already had child.Measure() called via Yoga's
    // MeasureFunction during the current pass. Children measured by Yoga
    // must NOT be measured a second time with Yoga's resolved (rounded)
    // layout size: a finite-height re-measure makes the child re-run its
    // own measure logic and DesiredSize can drift by a sub-pixel — visible
    // as ±1 px height wobble during pure-width window resize. StackPanel
    // measures each child exactly once (Measure(availWidth, INF)); we do
    // the same for children Yoga already measured, and only call Measure
    // ourselves for children Yoga skipped (e.g. fixed-size children where
    // Yoga uses the explicit dimension and bypasses MeasureFunction).
    private readonly HashSet<UIElement> _measuredThisPass = new();

    // Yoga's height-axis MeasureMode for the current MeasureFunction call,
    // threaded down to a nested FlexPanel via [ThreadStatic]. Lets a child
    // FlexPanel disambiguate two semantically-different "AtMost" cases:
    //   - WinUI AtMost from a non-Yoga parent (the standard Measure
    //     contract — VerticalAlignment.Stretch means "fill up to this"),
    //   - Yoga AtMost from an outer FlexPanel doing basis/FitContent
    //     measurement (a soft cap on content size, NOT a fill target —
    //     treating it as fill would make the inner report the cap as its
    //     DesiredSize and defeat the outer's flex-grow distribution).
    // null = not nested under a FlexPanel measurement → use the WinUI
    // contract directly. Yoga Exactly = stretch-fit allocation → fill.
    // Yoga Undefined / AtMost = basis content measurement → do not fill.
    [global::System.ThreadStatic]
    private static YogaMeasureMode? _outerYogaHeightMode;

    protected override Size MeasureOverride(Size availableSize)
    {
        SyncPointScaleLazy();
        _measuredThisPass.Clear();
        SyncYogaTree();
        SetRootConstraints(availableSize);

        bool hasDefiniteWidth = !float.IsInfinity((float)availableSize.Width);
        bool hasDefiniteHeight = !float.IsInfinity((float)availableSize.Height);

        // Inline-axis fill (CSS default) unless the user asked for fit-content
        // via non-Stretch HorizontalAlignment. Width on the panel itself is
        // already clamped by FrameworkElement.Measure before we get here.
        bool fillInlineAxis = HorizontalAlignment == HorizontalAlignment.Stretch;

        float rootWidth;
        if (fillInlineAxis && hasDefiniteWidth)
        {
            // CSS: block-level flex container fills its containing block.
            rootWidth = (float)availableSize.Width;
            _rootNode.MaxWidth = YogaValue.Undefined;
        }
        else
        {
            // CSS fit-content: content-size the inline axis, capped by
            // availableSize. This is the opt-in "shrink-wrap" path.
            rootWidth = float.NaN;
            _rootNode.MaxWidth = hasDefiniteWidth
                ? YogaValue.Point((float)availableSize.Width)
                : YogaValue.Undefined;
        }

        // Block axis: three modes, in priority order.
        //
        // 1. Explicit Height(N): CSS `height: N` — definite container size.
        //    Pass N to Yoga; Yoga falls into StretchFit mode (justify-content
        //    / align-items / align-self all need a definite main axis).
        //
        // 2. VerticalAlignment.Stretch with a definite parent offer: WinUI
        //    Stretch + finite availableSize.Height means "fill my parent's
        //    slot" (analogous to CSS `height: 100%` against a definite-height
        //    parent). Pass availableSize.Height to Yoga as the container
        //    size so flex-grow children have a definite pool to distribute.
        //    This is the symmetric counterpart to inline-axis Stretch above
        //    and is what makes the canonical web flex pattern —
        //    header(auto) / body(flex:1) / footer(auto) filling a viewport —
        //    work without a hard-coded `.Height(N)` on the column.
        //
        //    Wobble safety: when an outer FlexPanel runs Yoga in MaxContent
        //    mode (no explicit height, parent offer infinite — case 3
        //    below), Yoga's MeasureFunction calls children with
        //    hMode=Undefined; the MeasureFunction wrapper translates that
        //    into availableSize.Height = +∞ for the child. With +∞, the
        //    child below sees `hasDefiniteHeight = false` and falls into
        //    case 3 — content-sized — exactly as before. So nested
        //    FlexPanels under an unconstrained outer behave identically to
        //    the pre-fix code (no DesiredSize drift on horizontal resize).
        //
        // 3. Otherwise (no explicit Height, or parent offer is infinite, or
        //    VerticalAlignment != Stretch): CSS `height: auto`. Pass NaN —
        //    MaxContent mode, no shrink. Content overflows a smaller parent.
        // Block axis: three modes, in priority order.
        //
        // 1. Explicit .Height(N): CSS `height: N` — definite. Pass N to
        //    Yoga (StretchFit mode for justify-content / align-items).
        //
        // 2. VerticalAlignment.Stretch with a definite parent offer: WinUI
        //    Stretch + finite availableSize.Height = "fill my parent's
        //    slot" (analogous to CSS `height: 100%` against a definite
        //    parent). Pass availableSize.Height to Yoga as the container
        //    size so flex-grow children have a definite pool to
        //    distribute. This is the symmetric counterpart to the inline
        //    axis above and is what makes the canonical web flex pattern —
        //    header(auto) / body(flex:1) / footer(auto) filling a
        //    viewport — work without forcing a hardcoded `.Height(N)`.
        //
        //    The "is parent's AtMost a fill target?" question is what the
        //    [ThreadStatic] _outerYogaHeightMode resolves: when a parent
        //    FlexPanel's Yoga is in basis/FitContent measurement, we want
        //    to report content size, not the cap. Only Yoga's Exactly
        //    mode (and "no flex parent at all" — the normal WinUI
        //    contract) means fill. Without this discrimination a nested
        //    FlexPanel.Stretch under a flex-grow:1 sibling would report
        //    the cap as its DesiredSize and break the outer's grow
        //    distribution.
        //
        //    Trade-off: `Border` (or any auto-height container with no
        //    flex semantics) wrapping a FlexPanel will inflate to the
        //    parent's offer rather than shrink-wrap content, because
        //    `_outerYogaHeightMode` is null in that case (no flex parent)
        //    so the WinUI Stretch contract applies. Users who want a
        //    content-sized FlexPanel inside an auto-height container
        //    should set `VerticalAlignment.Top` (or any non-Stretch).
        //
        // 3. Otherwise (no explicit Height and either parent offer is
        //    infinite, alignment != Stretch, or outer flex wants content
        //    size): CSS `height: auto`. Pass NaN — MaxContent mode, no
        //    shrink, content overflows a smaller parent.
        bool hasExplicitHeight = !double.IsNaN(Height);
        bool outerFlexWantsContent =
            _outerYogaHeightMode == YogaMeasureMode.Undefined
            || _outerYogaHeightMode == YogaMeasureMode.AtMost;
        bool fillBlockAxis = !hasExplicitHeight
            && !outerFlexWantsContent
            && VerticalAlignment == VerticalAlignment.Stretch
            && hasDefiniteHeight;
        _rootNode.MaxHeight = YogaValue.Undefined;

        float rootHeight = hasExplicitHeight
            ? (float)Height
            : fillBlockAxis ? (float)availableSize.Height
            : float.NaN;

        _rootNode.CalculateLayout(
            rootWidth,
            rootHeight,
            LayoutDirection);

        // Clamp the reported height when the panel has a definite own-height
        // (explicit Height(N) or block-axis fill against a definite parent
        // offer — both cases the box resolves to that size, never more).
        bool hasDefiniteOwnHeight = hasExplicitHeight || fillBlockAxis;
        float reportedHeight = hasDefiniteOwnHeight
            ? Math.Min(_rootNode.LayoutHeight, (float)availableSize.Height)
            : _rootNode.LayoutHeight;
        _cachedDesiredSize = new Size(_rootNode.LayoutWidth, reportedHeight);

        // Cache child positions and measure children at Yoga's resolved sizes.
        // This fulfills the WinUI contract that all children must be Measured
        // during MeasureOverride, and caches positions for ArrangeOverride.
        _cachedChildLayouts.Clear();
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (_nodeCache.TryGetValue(child, out var childNode))
            {
                var layout = new ChildLayout
                {
                    X = childNode.LayoutX,
                    Y = childNode.LayoutY,
                    Width = childNode.LayoutWidth,
                    Height = childNode.LayoutHeight
                };
                _cachedChildLayouts.Add(layout);
                // Only measure here for children Yoga didn't already measure
                // (fixed-dimension children where Yoga uses the explicit value
                // and bypasses MeasureFunction). Re-measuring a child that
                // Yoga already measured — with the now-finite height —
                // perturbs the child's own DesiredSize through internal
                // sub-pixel rounding and is the cause of the resize wobble.
                if (!_measuredThisPass.Contains(child))
                {
                    var m = child is FrameworkElement cfe ? cfe.Margin : default;
                    child.Measure(new Size(
                        layout.Width + m.Left + m.Right,
                        layout.Height + m.Top + m.Bottom));
                }
            }
            else
            {
                _cachedChildLayouts.Add(default);
            }
        }

        return _cachedDesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // If finalSize matches what we measured for, use cached positions directly.
        // This avoids re-running Yoga (which would call child.Measure() via
        // MeasureFunction callbacks), preventing LayoutCycleException.
        bool sizeChanged =
            Math.Abs(finalSize.Width - _cachedDesiredSize.Width) > 0.5 ||
            Math.Abs(finalSize.Height - _cachedDesiredSize.Height) > 0.5;

        if (sizeChanged)
        {
            // Final size differs from measured size — re-run Yoga to redistribute
            // space, but suppress child.Measure() calls during this arrange pass.
            _arranging = true;
            try
            {
                _rootNode.MaxWidth = YogaValue.Undefined;
                _rootNode.MaxHeight = YogaValue.Undefined;
                // Block-axis policy at arrange time:
                //  - explicit Height: pass finalSize.Height (== Height) as
                //    definite so Yoga falls into StretchFit.
                //  - VerticalAlignment.Stretch (the user opted into "fill
                //    my parent's slot" — symmetric to the inline axis, which
                //    has always been treated as definite at finalSize.Width
                //    a few lines above): pass finalSize.Height as definite
                //    so Yoga distributes the slot across grow/shrink
                //    children. This is what gives the canonical web flex
                //    pattern — `header(auto) / body(flex:1) / footer(auto)`
                //    filling a viewport — a definite main-axis pool to
                //    distribute, without forcing a hardcoded `.Height(N)`.
                //    Wobble protection: this is in Arrange, not Measure, so
                //    DesiredSize is unaffected. The _arranging flag makes
                //    the Yoga rerun's MeasureFunction return cached child
                //    DesiredSize without re-measuring — sub-pixel jitter
                //    only shifts Yoga's internal positions, not children's
                //    own DesiredSize.
                //  - VerticalAlignment != Stretch (caller asked for
                //    fit-content vertically): pass NaN so Yoga stays in
                //    MaxContent mode and a horizontal-drag's sub-pixel
                //    finalSize.Height jitter doesn't drive flex-shrink.
                bool hasExplicitHeight = !double.IsNaN(Height);
                bool fillBlockAxisAtArrange = !hasExplicitHeight
                    && VerticalAlignment == VerticalAlignment.Stretch
                    && !double.IsInfinity(finalSize.Height);
                float arrangeHeight = (hasExplicitHeight || fillBlockAxisAtArrange)
                    ? (float)finalSize.Height
                    : float.NaN;
                _rootNode.CalculateLayout(
                    (float)finalSize.Width,
                    arrangeHeight,
                    LayoutDirection);

                // Update cached positions from the new layout
                _cachedChildLayouts.Clear();
                for (int i = 0; i < Children.Count; i++)
                {
                    var child = Children[i];
                    if (_nodeCache.TryGetValue(child, out var childNode))
                    {
                        _cachedChildLayouts.Add(new ChildLayout
                        {
                            X = childNode.LayoutX,
                            Y = childNode.LayoutY,
                            Width = childNode.LayoutWidth,
                            Height = childNode.LayoutHeight
                        });
                    }
                    else
                    {
                        _cachedChildLayouts.Add(default);
                    }
                }
            }
            finally
            {
                _arranging = false;
            }
        }

        for (int i = 0; i < Children.Count && i < _cachedChildLayouts.Count; i++)
        {
            var layout = _cachedChildLayouts[i];
            var child = Children[i];
            // Expand arrange rect by margin: Yoga positions/sizes the content area,
            // but WinUI's Arrange subtracts the child's Margin from the rect.
            var m = child is FrameworkElement fe ? fe.Margin : default;
            child.Arrange(new Rect(
                layout.X - m.Left,
                layout.Y - m.Top,
                layout.Width + m.Left + m.Right,
                layout.Height + m.Top + m.Bottom));
        }

        return finalSize;
    }

    private void SetRootConstraints(Size availableSize)
    {
        // Container properties
        _rootNode.FlexDirection = Direction;
        _rootNode.JustifyContent = JustifyContent;
        _rootNode.AlignItems = AlignItems;
        _rootNode.AlignContent = AlignContent;
        _rootNode.FlexWrap = Wrap;
        _rootNode.SetGap(YogaGutter.Column, (float)ColumnGap);
        _rootNode.SetGap(YogaGutter.Row, (float)RowGap);

        // FlexPadding
        var p = FlexPadding;
        _rootNode.SetPadding(YogaEdge.Left, YogaValue.Point((float)p.Left));
        _rootNode.SetPadding(YogaEdge.Top, YogaValue.Point((float)p.Top));
        _rootNode.SetPadding(YogaEdge.Right, YogaValue.Point((float)p.Right));
        _rootNode.SetPadding(YogaEdge.Bottom, YogaValue.Point((float)p.Bottom));
    }

    private void SyncYogaTree()
    {
        // Remove nodes for children that are no longer present
        _syncCurrentChildren.Clear();
        foreach (UIElement child in Children)
            _syncCurrentChildren.Add(child);

        _syncToRemove.Clear();
        foreach (var kvp in _nodeCache)
        {
            if (!_syncCurrentChildren.Contains(kvp.Key))
                _syncToRemove.Add(kvp.Key);
        }
        foreach (var el in _syncToRemove)
        {
            if (_nodeCache.TryGetValue(el, out var node))
                _rootNode.RemoveChild(node);
            _nodeCache.Remove(el);
        }

        // Ensure each child has a YogaNode at the correct index
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (!_nodeCache.TryGetValue(child, out var childNode))
            {
                childNode = new YogaNode(_yogaConfig);
                _nodeCache[child] = childNode;

                // Set measure function: delegates to WinUI Measure.
                // During ArrangeOverride (_arranging=true), return the last
                // DesiredSize without calling Measure — calling Measure during
                // Arrange can trigger LayoutCycleException.
                //
                // Margin compensation: Yoga handles margins for positioning and
                // spacing between children (synced in ApplyAttachedProperties).
                // WinUI also subtracts Margin during Measure/Arrange. To avoid
                // double-counting, we add the margin back to Yoga's constraints
                // before calling WinUI Measure, and subtract it from DesiredSize
                // before returning to Yoga.
                var capturedChild = child;
                var panel = this;
                childNode.MeasureFunction = (node, w, wMode, h, hMode) =>
                {
                    var m = capturedChild is FrameworkElement cfe ? cfe.Margin : default;
                    double mH = m.Left + m.Right;
                    double mV = m.Top + m.Bottom;

                    if (panel._arranging)
                    {
                        // Cached DesiredSize fallback during arrange — Yoga is not
                        // supposed to run a fresh layout here (Measure during
                        // Arrange triggers LayoutCycleException). Clamp to the
                        // Exactly slot for the same CSS-spec reason as below so a
                        // stale oversized DesiredSize from a previous pass does
                        // not re-expand the layout box.
                        var aOutW = (float)(capturedChild.DesiredSize.Width - mH);
                        var aOutH = (float)(capturedChild.DesiredSize.Height - mV);
                        if (wMode == YogaMeasureMode.Exactly) aOutW = (float)w;
                        if (hMode == YogaMeasureMode.Exactly) aOutH = (float)h;
                        return new YogaSize(Math.Max(0, aOutW), Math.Max(0, aOutH));
                    }

                    // Yoga's constraints are content-area (excluding margin).
                    // Add margin so WinUI's subtraction yields the correct content area.
                    //
                    // Mode → WinUI constraint:
                    //  - Undefined → +∞ (give me your content size).
                    //  - AtMost / Exactly → finite (w + margin). Preserves
                    //    the standard WinUI Measure contract — TextBlock
                    //    wrapping, Image stretch sizing, etc. all depend on
                    //    receiving a real cap rather than infinity.
                    //
                    // The "is this AtMost a fill target?" discrimination is
                    // not done by changing the constraint — that would break
                    // text wrapping. Instead the wrapper publishes Yoga's
                    // hMode via [ThreadStatic] _outerYogaHeightMode so a
                    // nested FlexPanel.MeasureOverride can tell whether it
                    // is being measured for content (Undefined / AtMost) or
                    // for fill (Exactly). See fillBlockAxis above.
                    var constraintW = wMode == YogaMeasureMode.Undefined ? double.PositiveInfinity : w + mH;
                    var constraintH = hMode == YogaMeasureMode.Undefined ? double.PositiveInfinity : h + mV;
                    var prevYogaH = _outerYogaHeightMode;
                    _outerYogaHeightMode = hMode;
                    try
                    {
                        capturedChild.Measure(new Size(constraintW, constraintH));
                    }
                    finally
                    {
                        _outerYogaHeightMode = prevYogaH;
                    }
                    panel._measuredThisPass.Add(capturedChild);

                    // CSS Flexbox: Yoga is the authority for the resolved
                    // slot size in Exactly mode. The child's DesiredSize may
                    // exceed `w` for controls that ignore their Measure
                    // constraint (TabView with oversized content, Image with
                    // Stretch=None) — but per CSS the layout box is `w`; the
                    // content paints into the slot and overflows visually
                    // (CSS overflow: visible). Without this clamp, Yoga would
                    // treat the item as DesiredSize-wide and overflow the
                    // panel itself, defeating flex-grow distribution.
                    //
                    // CSS Flexbox §4.5 min-content floor is enforced separately
                    // by writing `node.MinWidth` / `node.MinHeight` in
                    // ApplyAttachedProperties — see ResolveMinDimension and
                    // ComputeMinContent there.
                    var outW = (float)(capturedChild.DesiredSize.Width - mH);
                    var outH = (float)(capturedChild.DesiredSize.Height - mV);
                    if (wMode == YogaMeasureMode.Exactly) outW = (float)w;
                    if (hMode == YogaMeasureMode.Exactly) outH = (float)h;
                    return new YogaSize(Math.Max(0, outW), Math.Max(0, outH));
                };
            }

            // Apply attached properties from the UIElement to the YogaNode
            ApplyAttachedProperties(child, childNode);

            // Mirror WinUI Visibility=Collapsed onto Yoga's Display=None:
            // Collapsed is the XAML equivalent of CSS display:none — the
            // element contributes nothing to main-axis size and no gap slot.
            // StackPanel does the same.
            childNode.Display = child.Visibility == Visibility.Collapsed
                ? YogaDisplay.None
                : YogaDisplay.Flex;

            // Ensure correct child order in Yoga tree
            if (i < _rootNode.ChildCount)
            {
                if (_rootNode.GetChild(i) != childNode)
                {
                    // Remove if present elsewhere and re-insert at correct position
                    _rootNode.RemoveChild(childNode);
                    _rootNode.InsertChild(childNode, i);
                }
            }
            else
            {
                if (childNode.Owner != _rootNode)
                    _rootNode.InsertChild(childNode, i);
            }
        }

        // Remove extra Yoga children beyond current count
        while (_rootNode.ChildCount > Children.Count)
        {
            _rootNode.RemoveChild(_rootNode.ChildCount - 1);
        }
    }

    private void ApplyAttachedProperties(UIElement el, YogaNode node)
    {
        var grow = GetGrow(el);
        var shrink = GetShrink(el);
        var basis = GetBasis(el);
        var alignSelf = GetAlignSelf(el);
        var position = GetPosition(el);
        var minWidthExplicit = GetMinWidth(el);
        var minHeightExplicit = GetMinHeight(el);

        node.Style.FlexGrow = (float)grow;
        node.Style.FlexShrink = (float)shrink;
        node.Style.FlexBasis = double.IsNaN(basis) ? YogaValue.Auto : YogaValue.Point((float)basis);
        node.Style.AlignSelf = alignSelf;
        node.Style.PositionType = position;

        // ── CSS Flexbox §4.5 automatic minimum size ──
        // Compute effective MinWidth / MinHeight per CSS spec:
        //   • User-set explicit value (not NaN) wins, even 0.
        //   • Auto-min applies only on the main axis (per CSS spec). The
        //     cross-axis floor defaults to 0 unless explicitly set.
        //   • Main-axis auto = min(specified-size suggestion, content-size suggestion)
        //         specified-size = explicit `basis` if definite, else +∞
        //         content-size   = min-content (approximated via Measure(0,∞))
        //   • Short-circuit: basis==0 → 0 (no pre-measure needed).
        //   • Short-circuit: ScrollViewer/ScrollView → 0 (don't realize
        //     virtualized content during a sizing-only pre-measure).
        var mainAxisIsRow = FlexDirectionHelper.IsRow(Direction);
        node.MinWidth = ResolveMinDimension(
            el, axisIsMain: mainAxisIsRow,
            explicitMin: minWidthExplicit, basis: basis, isWidth: true);
        node.MinHeight = ResolveMinDimension(
            el, axisIsMain: !mainAxisIsRow,
            explicitMin: minHeightExplicit, basis: basis, isWidth: false);

        // Position insets
        var left = GetLeft(el);
        var top = GetTop(el);
        var right = GetRight(el);
        var bottom = GetBottom(el);

        node.Style.Position[(int)YogaEdge.Left] = double.IsNaN(left) ? YogaValue.Undefined : YogaValue.Point((float)left);
        node.Style.Position[(int)YogaEdge.Top] = double.IsNaN(top) ? YogaValue.Undefined : YogaValue.Point((float)top);
        node.Style.Position[(int)YogaEdge.Right] = double.IsNaN(right) ? YogaValue.Undefined : YogaValue.Point((float)right);
        node.Style.Position[(int)YogaEdge.Bottom] = double.IsNaN(bottom) ? YogaValue.Undefined : YogaValue.Point((float)bottom);

        // If the child has explicit Width/Height set, pass them to Yoga
        if (el is FrameworkElement fe)
        {
            node.Width = double.IsNaN(fe.Width) ? YogaValue.Auto : YogaValue.Point((float)fe.Width);
            node.Height = double.IsNaN(fe.Height) ? YogaValue.Auto : YogaValue.Point((float)fe.Height);

            // Margins
            var margin = fe.Margin;
            if (margin.Left != 0 || margin.Top != 0 || margin.Right != 0 || margin.Bottom != 0)
            {
                node.SetMargin(YogaEdge.Left, YogaValue.Point((float)margin.Left));
                node.SetMargin(YogaEdge.Top, YogaValue.Point((float)margin.Top));
                node.SetMargin(YogaEdge.Right, YogaValue.Point((float)margin.Right));
                node.SetMargin(YogaEdge.Bottom, YogaValue.Point((float)margin.Bottom));
            }
            else
            {
                node.SetMargin(YogaEdge.Left, YogaValue.Undefined);
                node.SetMargin(YogaEdge.Top, YogaValue.Undefined);
                node.SetMargin(YogaEdge.Right, YogaValue.Undefined);
                node.SetMargin(YogaEdge.Bottom, YogaValue.Undefined);
            }
        }
    }

    // Resolves CSS Flexbox §4.5 automatic minimum size for one axis. See
    // ApplyAttachedProperties for the full algorithm; this helper handles the
    // per-axis decision tree.
    private YogaValue ResolveMinDimension(
        UIElement el, bool axisIsMain, double explicitMin, double basis, bool isWidth)
    {
        // 1. User-set explicit min wins (including 0).
        if (!double.IsNaN(explicitMin))
            return YogaValue.Point((float)Math.Max(0, explicitMin));

        // 2. Cross axis: CSS default is 0 (Undefined → no floor).
        if (!axisIsMain)
            return YogaValue.Undefined;

        // 3. Main axis, basis explicitly 0: specified-size = 0, so
        //    min(0, min-content) = 0. Short-circuit, no pre-measure.
        if (!double.IsNaN(basis) && basis <= 0)
            return YogaValue.Point(0);

        // 4. Main axis, child is a virtualizing/scrolling container: skip
        //    min-content pre-measure to avoid realizing virtualized content.
        //    Documented opt-out — user can still set explicit minWidth/minHeight.
        if (IsScrollLikeContainer(el))
            return YogaValue.Point(0);

        // 5. Main axis, compute min-content via Measure with content cap = margin
        //    (Yoga content-area constraint of 0 → WinUI total constraint of margin).
        //    Approximation: WinUI Measure does not expose CSS min-content directly;
        //    Measure(0, ∞) returns the widest unbreakable content for text and the
        //    natural size for fixed-size controls.
        var minContent = ComputeMinContent(el, isWidth);

        // 6. If basis is definite and positive, automatic min = min(basis, min-content).
        if (!double.IsNaN(basis))
            return YogaValue.Point((float)Math.Max(0, Math.Min(basis, minContent)));

        // 7. basis == auto: automatic min = min-content.
        return YogaValue.Point((float)Math.Max(0, minContent));
    }

    private static bool IsScrollLikeContainer(UIElement el)
    {
        // ScrollViewer / ScrollView naturally allow content to be larger than
        // their viewport; their CSS-equivalent overflow is scroll, which per
        // §4.5 makes the automatic minimum size 0.
        return el is ScrollViewer
            || el is Microsoft.UI.Xaml.Controls.ScrollView;
    }

    private double ComputeMinContent(UIElement el, bool isWidth)
    {
        // Measure with a 0-content constraint on the target axis to force the
        // child to report its tightest natural size. Margin is added so the
        // WinUI Measure subtraction yields the correct content area (matches
        // the MeasureFunc bridge convention).
        var fe = el as FrameworkElement;
        var margin = fe?.Margin ?? default;
        double mH = margin.Left + margin.Right;
        double mV = margin.Top + margin.Bottom;

        // Calling Measure here pollutes the child's cached DesiredSize, but
        // the subsequent Yoga MeasureFunc pass (or the final MeasureOverride
        // sweep) re-measures with the real constraint. No restore-measure is
        // needed and would itself perturb layout.
        var constraint = isWidth
            ? new Size(mH, double.PositiveInfinity)
            : new Size(double.PositiveInfinity, mV);
        el.Measure(constraint);
        return isWidth
            ? Math.Max(0, el.DesiredSize.Width - mH)
            : Math.Max(0, el.DesiredSize.Height - mV);
    }
}
