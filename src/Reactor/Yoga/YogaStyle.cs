// C# port of Meta's Yoga layout engine Style.
// Ported from yoga/style/Style.h
// Simplifies C++ StyleValuePool to plain fields (memory compaction less important in managed code).

using Microsoft.UI.Reactor.Layout;

namespace Microsoft.UI.Reactor.Layout;

/// <summary>
/// Stores all CSS-like style properties for a YogaNode.
/// Simplified from C++ version: replaces StyleValuePool with direct fields.
/// </summary>
/// <remarks>
/// NOTE: CSS Grid properties (gridTemplateColumns, gridTemplateRows, gridAutoColumns,
/// gridAutoRows, gridColumnStart/End, gridRowStart/End) present in the C++ Yoga
/// implementation are intentionally omitted — grid layout is not yet supported in
/// this C# port.
/// </remarks>
internal sealed class YogaStyle
{
    public const float DefaultFlexGrow = 0.0f;
    public const float DefaultFlexShrink = 0.0f;
    public const float WebDefaultFlexShrink = 1.0f;

    // Enum properties
    public FlexLayoutDirection Direction = FlexLayoutDirection.Inherit;
    public FlexDirection FlexDirection = FlexDirection.Column;
    public FlexJustify JustifyContent = FlexJustify.FlexStart;
    public FlexJustify JustifyItems = FlexJustify.Stretch;
    public FlexJustify JustifySelf = FlexJustify.Auto;
    public FlexAlign AlignContent = FlexAlign.FlexStart;
    public FlexAlign AlignItems = FlexAlign.Stretch;
    public FlexAlign AlignSelf = FlexAlign.Auto;
    public FlexPositionType PositionType = FlexPositionType.Relative;
    public FlexWrap FlexWrap = FlexWrap.NoWrap;
    public YogaOverflow Overflow = YogaOverflow.Visible;
    public YogaDisplay Display = YogaDisplay.Flex;
    public YogaBoxSizing BoxSizing = YogaBoxSizing.BorderBox;

    // Flex properties (NaN = undefined)
    public float Flex = float.NaN;
    public float FlexGrow = float.NaN;
    public float FlexShrink = float.NaN;
    public YogaValue FlexBasis = YogaValue.Auto;

    // Edge / gutter / dimension values are stored inline (see #143 InlineArray
    // structs at the bottom of this file) instead of 8 separate YogaValue[]
    // arrays. Exposed as ref-returning properties so existing indexer reads and
    // writes (style.Margin[i], style.Position[i] = v) keep working unchanged.
    private EdgeValues _margin;
    private EdgeValues _position;
    private EdgeValues _padding;
    private EdgeValues _border;
    private GutterValues _gap;            // Column=0, Row=1, All=2
    private DimensionValues _dimensions;  // Width=0, Height=1
    private DimensionValues _minDimensions;
    private DimensionValues _maxDimensions;

    // Edge-indexed values (indexed by YogaEdge: Left=0..All=8)
    public ref EdgeValues Margin => ref _margin;
    public ref EdgeValues Position => ref _position;
    public ref EdgeValues Padding => ref _padding;
    public ref EdgeValues Border => ref _border;

    // Gutter-indexed values (Column=0, Row=1, All=2)
    public ref GutterValues Gap => ref _gap;

    // Dimension-indexed values (Width=0, Height=1)
    public ref DimensionValues Dimensions => ref _dimensions;
    public ref DimensionValues MinDimensions => ref _minDimensions;
    public ref DimensionValues MaxDimensions => ref _maxDimensions;

    public YogaStyle()
    {
        // Explicitly seed the inline buffers to match the historic array
        // initializers. default(YogaValue) is (0, Undefined) — NOT the
        // (NaN, Undefined) sentinel — so leaving them zero-initialized would
        // subtly change == and Resolve() behavior. Dimensions default to Auto.
        for (int i = 0; i < 9; i++)
        {
            _margin[i] = YogaValue.Undefined;
            _position[i] = YogaValue.Undefined;
            _padding[i] = YogaValue.Undefined;
            _border[i] = YogaValue.Undefined;
        }
        _gap[0] = YogaValue.Undefined;
        _gap[1] = YogaValue.Undefined;
        _gap[2] = YogaValue.Undefined;
        _dimensions[0] = YogaValue.Auto;
        _dimensions[1] = YogaValue.Auto;
        _minDimensions[0] = YogaValue.Undefined;
        _minDimensions[1] = YogaValue.Undefined;
        _maxDimensions[0] = YogaValue.Undefined;
        _maxDimensions[1] = YogaValue.Undefined;
    }

    // Aspect ratio (NaN = undefined)
    private float _aspectRatio = float.NaN;
    public float AspectRatio
    {
        get => _aspectRatio;
        set
        {
            // SECURITY (TASK-083): aspect ratio must be a positive finite
            // number. Negatives produce negative computed dimensions; ±∞
            // propagates into Arrange and crashes WinUI. NaN is the
            // intentional "undefined" sentinel and is allowed.
            if (!float.IsNaN(value))
            {
                if (value <= 0 || float.IsInfinity(value))
                    throw new ArgumentOutOfRangeException(nameof(value),
                        $"AspectRatio must be a positive finite number or NaN; got {value}.");
            }
            _aspectRatio = value;
        }
    }

    // ── Edge computation (resolves Start/End/Horizontal/Vertical/All fallbacks) ──

    public YogaValue ComputeColumnGap()
    {
        var col = _gap[(int)YogaGutter.Column];
        return col.IsDefined ? col : _gap[(int)YogaGutter.All];
    }

    public YogaValue ComputeRowGap()
    {
        var row = _gap[(int)YogaGutter.Row];
        return row.IsDefined ? row : _gap[(int)YogaGutter.All];
    }

    /// <summary>Resolve the left edge value considering Start/End/Left/Horizontal/All fallbacks.</summary>
    private static YogaValue ComputeLeftEdge(ReadOnlySpan<YogaValue> edges, FlexLayoutDirection layoutDirection)
    {
        if (layoutDirection == FlexLayoutDirection.LTR && edges[(int)YogaEdge.Start].IsDefined)
            return edges[(int)YogaEdge.Start];
        if (layoutDirection == FlexLayoutDirection.RTL && edges[(int)YogaEdge.End].IsDefined)
            return edges[(int)YogaEdge.End];
        if (edges[(int)YogaEdge.Left].IsDefined)
            return edges[(int)YogaEdge.Left];
        if (edges[(int)YogaEdge.Horizontal].IsDefined)
            return edges[(int)YogaEdge.Horizontal];
        return edges[(int)YogaEdge.All];
    }

    private static YogaValue ComputeTopEdge(ReadOnlySpan<YogaValue> edges)
    {
        if (edges[(int)YogaEdge.Top].IsDefined) return edges[(int)YogaEdge.Top];
        if (edges[(int)YogaEdge.Vertical].IsDefined) return edges[(int)YogaEdge.Vertical];
        return edges[(int)YogaEdge.All];
    }

    private static YogaValue ComputeRightEdge(ReadOnlySpan<YogaValue> edges, FlexLayoutDirection layoutDirection)
    {
        if (layoutDirection == FlexLayoutDirection.LTR && edges[(int)YogaEdge.End].IsDefined)
            return edges[(int)YogaEdge.End];
        if (layoutDirection == FlexLayoutDirection.RTL && edges[(int)YogaEdge.Start].IsDefined)
            return edges[(int)YogaEdge.Start];
        if (edges[(int)YogaEdge.Right].IsDefined)
            return edges[(int)YogaEdge.Right];
        if (edges[(int)YogaEdge.Horizontal].IsDefined)
            return edges[(int)YogaEdge.Horizontal];
        return edges[(int)YogaEdge.All];
    }

    private static YogaValue ComputeBottomEdge(ReadOnlySpan<YogaValue> edges)
    {
        if (edges[(int)YogaEdge.Bottom].IsDefined) return edges[(int)YogaEdge.Bottom];
        if (edges[(int)YogaEdge.Vertical].IsDefined) return edges[(int)YogaEdge.Vertical];
        return edges[(int)YogaEdge.All];
    }

    private static YogaValue ComputeEdge(ReadOnlySpan<YogaValue> edges, YogaPhysicalEdge edge, FlexLayoutDirection direction)
    {
        return edge switch
        {
            YogaPhysicalEdge.Left => ComputeLeftEdge(edges, direction),
            YogaPhysicalEdge.Top => ComputeTopEdge(edges),
            YogaPhysicalEdge.Right => ComputeRightEdge(edges, direction),
            YogaPhysicalEdge.Bottom => ComputeBottomEdge(edges),
            _ => YogaValue.Undefined,
        };
    }

    // ── Position queries ──

    public YogaValue ComputePosition(YogaPhysicalEdge edge, FlexLayoutDirection direction)
        => ComputeEdge(_position, edge, direction);

    public YogaValue ComputeMargin(YogaPhysicalEdge edge, FlexLayoutDirection direction)
        => ComputeEdge(_margin, edge, direction);

    public YogaValue ComputePadding(YogaPhysicalEdge edge, FlexLayoutDirection direction)
        => ComputeEdge(_padding, edge, direction);

    public YogaValue ComputeBorder(YogaPhysicalEdge edge, FlexLayoutDirection direction)
        => ComputeEdge(_border, edge, direction);

    // ── Inset queries ──

    public bool HorizontalInsetsDefined =>
        _position[(int)YogaEdge.Left].IsDefined ||
        _position[(int)YogaEdge.Right].IsDefined ||
        _position[(int)YogaEdge.All].IsDefined ||
        _position[(int)YogaEdge.Horizontal].IsDefined ||
        _position[(int)YogaEdge.Start].IsDefined ||
        _position[(int)YogaEdge.End].IsDefined;

    public bool VerticalInsetsDefined =>
        _position[(int)YogaEdge.Top].IsDefined ||
        _position[(int)YogaEdge.Bottom].IsDefined ||
        _position[(int)YogaEdge.All].IsDefined ||
        _position[(int)YogaEdge.Vertical].IsDefined;

    // ── Flex-direction-aware computed values ──

    public bool IsFlexStartPositionDefined(FlexDirection axis, FlexLayoutDirection direction)
        => ComputePosition(FlexDirectionHelper.FlexStartEdge(axis), direction).IsDefined;

    public bool IsFlexStartPositionAuto(FlexDirection axis, FlexLayoutDirection direction)
        => ComputePosition(FlexDirectionHelper.FlexStartEdge(axis), direction).IsAuto;

    public bool IsInlineStartPositionDefined(FlexDirection axis, FlexLayoutDirection direction)
        => ComputePosition(FlexDirectionHelper.InlineStartEdge(axis, direction), direction).IsDefined;

    public bool IsInlineStartPositionAuto(FlexDirection axis, FlexLayoutDirection direction)
        => ComputePosition(FlexDirectionHelper.InlineStartEdge(axis, direction), direction).IsAuto;

    public bool IsFlexEndPositionDefined(FlexDirection axis, FlexLayoutDirection direction)
        => ComputePosition(FlexDirectionHelper.FlexEndEdge(axis), direction).IsDefined;

    public bool IsFlexEndPositionAuto(FlexDirection axis, FlexLayoutDirection direction)
        => ComputePosition(FlexDirectionHelper.FlexEndEdge(axis), direction).IsAuto;

    public bool IsInlineEndPositionDefined(FlexDirection axis, FlexLayoutDirection direction)
        => ComputePosition(FlexDirectionHelper.InlineEndEdge(axis, direction), direction).IsDefined;

    public bool IsInlineEndPositionAuto(FlexDirection axis, FlexLayoutDirection direction)
        => ComputePosition(FlexDirectionHelper.InlineEndEdge(axis, direction), direction).IsAuto;

    // ── Computed position values ──

    public float ComputeFlexStartPosition(FlexDirection axis, FlexLayoutDirection direction, float axisSize)
        => YogaFloat.UnwrapOrDefault(ComputePosition(FlexDirectionHelper.FlexStartEdge(axis), direction).Resolve(axisSize));

    public float ComputeInlineStartPosition(FlexDirection axis, FlexLayoutDirection direction, float axisSize)
        => YogaFloat.UnwrapOrDefault(ComputePosition(FlexDirectionHelper.InlineStartEdge(axis, direction), direction).Resolve(axisSize));

    public float ComputeFlexEndPosition(FlexDirection axis, FlexLayoutDirection direction, float axisSize)
        => YogaFloat.UnwrapOrDefault(ComputePosition(FlexDirectionHelper.FlexEndEdge(axis), direction).Resolve(axisSize));

    public float ComputeInlineEndPosition(FlexDirection axis, FlexLayoutDirection direction, float axisSize)
        => YogaFloat.UnwrapOrDefault(ComputePosition(FlexDirectionHelper.InlineEndEdge(axis, direction), direction).Resolve(axisSize));

    // ── Computed margin values ──

    public float ComputeFlexStartMargin(FlexDirection axis, FlexLayoutDirection direction, float widthSize)
        => YogaFloat.UnwrapOrDefault(ComputeMargin(FlexDirectionHelper.FlexStartEdge(axis), direction).Resolve(widthSize));

    public float ComputeInlineStartMargin(FlexDirection axis, FlexLayoutDirection direction, float widthSize)
        => YogaFloat.UnwrapOrDefault(ComputeMargin(FlexDirectionHelper.InlineStartEdge(axis, direction), direction).Resolve(widthSize));

    public float ComputeFlexEndMargin(FlexDirection axis, FlexLayoutDirection direction, float widthSize)
        => YogaFloat.UnwrapOrDefault(ComputeMargin(FlexDirectionHelper.FlexEndEdge(axis), direction).Resolve(widthSize));

    public float ComputeInlineEndMargin(FlexDirection axis, FlexLayoutDirection direction, float widthSize)
        => YogaFloat.UnwrapOrDefault(ComputeMargin(FlexDirectionHelper.InlineEndEdge(axis, direction), direction).Resolve(widthSize));

    // ── Computed border values (clamped to >= 0) ──

    public float ComputeFlexStartBorder(FlexDirection axis, FlexLayoutDirection direction)
        => YogaFloat.MaxOrDefined(ComputeBorder(FlexDirectionHelper.FlexStartEdge(axis), direction).Resolve(0), 0);

    public float ComputeInlineStartBorder(FlexDirection axis, FlexLayoutDirection direction)
        => YogaFloat.MaxOrDefined(ComputeBorder(FlexDirectionHelper.InlineStartEdge(axis, direction), direction).Resolve(0), 0);

    public float ComputeFlexEndBorder(FlexDirection axis, FlexLayoutDirection direction)
        => YogaFloat.MaxOrDefined(ComputeBorder(FlexDirectionHelper.FlexEndEdge(axis), direction).Resolve(0), 0);

    public float ComputeInlineEndBorder(FlexDirection axis, FlexLayoutDirection direction)
        => YogaFloat.MaxOrDefined(ComputeBorder(FlexDirectionHelper.InlineEndEdge(axis, direction), direction).Resolve(0), 0);

    // ── Computed padding values (clamped to >= 0) ──

    public float ComputeFlexStartPadding(FlexDirection axis, FlexLayoutDirection direction, float widthSize)
        => YogaFloat.MaxOrDefined(ComputePadding(FlexDirectionHelper.FlexStartEdge(axis), direction).Resolve(widthSize), 0);

    public float ComputeInlineStartPadding(FlexDirection axis, FlexLayoutDirection direction, float widthSize)
        => YogaFloat.MaxOrDefined(ComputePadding(FlexDirectionHelper.InlineStartEdge(axis, direction), direction).Resolve(widthSize), 0);

    public float ComputeFlexEndPadding(FlexDirection axis, FlexLayoutDirection direction, float widthSize)
        => YogaFloat.MaxOrDefined(ComputePadding(FlexDirectionHelper.FlexEndEdge(axis), direction).Resolve(widthSize), 0);

    public float ComputeInlineEndPadding(FlexDirection axis, FlexLayoutDirection direction, float widthSize)
        => YogaFloat.MaxOrDefined(ComputePadding(FlexDirectionHelper.InlineEndEdge(axis, direction), direction).Resolve(widthSize), 0);

    // ── Combined padding + border ──

    public float ComputeInlineStartPaddingAndBorder(FlexDirection axis, FlexLayoutDirection direction, float widthSize)
        => ComputeInlineStartPadding(axis, direction, widthSize) + ComputeInlineStartBorder(axis, direction);

    public float ComputeFlexStartPaddingAndBorder(FlexDirection axis, FlexLayoutDirection direction, float widthSize)
        => ComputeFlexStartPadding(axis, direction, widthSize) + ComputeFlexStartBorder(axis, direction);

    public float ComputeInlineEndPaddingAndBorder(FlexDirection axis, FlexLayoutDirection direction, float widthSize)
        => ComputeInlineEndPadding(axis, direction, widthSize) + ComputeInlineEndBorder(axis, direction);

    public float ComputeFlexEndPaddingAndBorder(FlexDirection axis, FlexLayoutDirection direction, float widthSize)
        => ComputeFlexEndPadding(axis, direction, widthSize) + ComputeFlexEndBorder(axis, direction);

    public float ComputePaddingAndBorderForDimension(FlexLayoutDirection direction, YogaDimension dimension, float widthSize)
    {
        var flexDir = dimension == YogaDimension.Width ? FlexDirection.Row : FlexDirection.Column;
        return ComputeFlexStartPaddingAndBorder(flexDir, direction, widthSize)
             + ComputeFlexEndPaddingAndBorder(flexDir, direction, widthSize);
    }

    public float ComputeBorderForAxis(FlexDirection axis)
        => ComputeInlineStartBorder(axis, FlexLayoutDirection.LTR) + ComputeInlineEndBorder(axis, FlexLayoutDirection.LTR);

    public float ComputeMarginForAxis(FlexDirection axis, float widthSize)
        => ComputeInlineStartMargin(axis, FlexLayoutDirection.LTR, widthSize) + ComputeInlineEndMargin(axis, FlexLayoutDirection.LTR, widthSize);

    public float ComputeGapForAxis(FlexDirection axis, float ownerSize)
    {
        var gap = FlexDirectionHelper.IsRow(axis) ? ComputeColumnGap() : ComputeRowGap();
        return YogaFloat.MaxOrDefined(gap.Resolve(ownerSize), 0);
    }

    // ── Auto margin queries ──

    public bool FlexStartMarginIsAuto(FlexDirection axis, FlexLayoutDirection direction)
        => ComputeMargin(FlexDirectionHelper.FlexStartEdge(axis), direction).IsAuto;

    public bool FlexEndMarginIsAuto(FlexDirection axis, FlexLayoutDirection direction)
        => ComputeMargin(FlexDirectionHelper.FlexEndEdge(axis), direction).IsAuto;

    public bool InlineStartMarginIsAuto(FlexDirection axis, FlexLayoutDirection direction)
        => ComputeMargin(FlexDirectionHelper.InlineStartEdge(axis, direction), direction).IsAuto;

    public bool InlineEndMarginIsAuto(FlexDirection axis, FlexLayoutDirection direction)
        => ComputeMargin(FlexDirectionHelper.InlineEndEdge(axis, direction), direction).IsAuto;

    // ── Resolved min/max dimensions (accounting for box-sizing) ──

    public float ResolvedMinDimension(FlexLayoutDirection direction, YogaDimension axis, float referenceLength, float ownerWidth)
    {
        float value = _minDimensions[(int)axis].Resolve(referenceLength);
        if (BoxSizing == YogaBoxSizing.BorderBox)
            return value;

        // Match C++ FloatOptional addition: always add padding+border in content-box mode,
        // even when value is undefined — the padding+border itself forms a minimum.
        float paddingAndBorder = ComputePaddingAndBorderForDimension(direction, axis, ownerWidth);
        float pb = YogaFloat.IsDefined(paddingAndBorder) ? paddingAndBorder : 0;
        return value + pb;
    }

    public float ResolvedMaxDimension(FlexLayoutDirection direction, YogaDimension axis, float referenceLength, float ownerWidth)
    {
        float value = _maxDimensions[(int)axis].Resolve(referenceLength);
        if (BoxSizing == YogaBoxSizing.BorderBox)
            return value;

        // Match C++ FloatOptional addition: always add padding+border in content-box mode.
        float paddingAndBorder = ComputePaddingAndBorderForDimension(direction, axis, ownerWidth);
        float pb = YogaFloat.IsDefined(paddingAndBorder) ? paddingAndBorder : 0;
        return value + pb;
    }
}

// AI-HINT (perf #143): fixed-size inline value buffers embedded directly in the
// YogaStyle heap object. These replace 8 separate YogaValue[] arrays per node —
// for a ~200-node tree that removes ~1600 GC objects (a dominant Yoga memory
// regression vs the C++ original, which uses inline members). InlineArray is a
// first-class C# 12 feature: indexing is compiler-generated and fully AOT/trim
// safe (no reflection, no Unsafe). Each struct has exactly one instance field,
// as InlineArray requires. They are explicitly initialized in the YogaStyle
// constructor (default(YogaValue) is (0, Undefined), not the (NaN, Undefined)
// sentinel — and dimensions default to Auto), so == and Resolve() semantics
// stay byte-identical to the previous arrays.

/// <summary>Edge-indexed inline buffer (YogaEdge: Left=0 .. All=8).</summary>
[global::System.Runtime.CompilerServices.InlineArray(9)]
internal struct EdgeValues
{
    private YogaValue _element0;
}

/// <summary>Gutter-indexed inline buffer (Column=0, Row=1, All=2).</summary>
[global::System.Runtime.CompilerServices.InlineArray(3)]
internal struct GutterValues
{
    private YogaValue _element0;
}

/// <summary>Dimension-indexed inline buffer (Width=0, Height=1).</summary>
[global::System.Runtime.CompilerServices.InlineArray(2)]
internal struct DimensionValues
{
    private YogaValue _element0;
}
