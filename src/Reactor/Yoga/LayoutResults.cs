// C# port of Meta's Yoga layout engine LayoutResults.
// Ported from yoga/node/LayoutResults.h, yoga/node/CachedMeasurement.h

using Microsoft.UI.Reactor.Layout;

namespace Microsoft.UI.Reactor.Layout;

/// <summary>
/// Cached measurement entry for avoiding redundant measure calls.
/// </summary>
internal struct CachedMeasurement
{
    public float AvailableWidth = -1;
    public float AvailableHeight = -1;
    public SizingMode WidthSizingMode = SizingMode.MaxContent;
    public SizingMode HeightSizingMode = SizingMode.MaxContent;
    public float ComputedWidth = -1;
    public float ComputedHeight = -1;

    public CachedMeasurement() { }

    public bool Equals(CachedMeasurement other)
    {
        bool isEqual = WidthSizingMode == other.WidthSizingMode &&
                       HeightSizingMode == other.HeightSizingMode;

        if (!YogaFloat.IsUndefined(AvailableWidth) || !YogaFloat.IsUndefined(other.AvailableWidth))
            isEqual = isEqual && AvailableWidth == other.AvailableWidth;
        if (!YogaFloat.IsUndefined(AvailableHeight) || !YogaFloat.IsUndefined(other.AvailableHeight))
            isEqual = isEqual && AvailableHeight == other.AvailableHeight;
        if (!YogaFloat.IsUndefined(ComputedWidth) || !YogaFloat.IsUndefined(other.ComputedWidth))
            isEqual = isEqual && ComputedWidth == other.ComputedWidth;
        if (!YogaFloat.IsUndefined(ComputedHeight) || !YogaFloat.IsUndefined(other.ComputedHeight))
            isEqual = isEqual && ComputedHeight == other.ComputedHeight;

        return isEqual;
    }
}

/// <summary>
/// Stores the computed layout results for a YogaNode after CalculateLayout().
/// </summary>
internal sealed class LayoutResults
{
    public const int MaxCachedMeasurements = 8;

    public uint ComputedFlexBasisGeneration;
    public float ComputedFlexBasis = float.NaN;

    // Cache invalidation tracking
    public uint GenerationCount;
    public uint ConfigVersion;
    public FlexLayoutDirection LastOwnerDirection = FlexLayoutDirection.Inherit;

    public uint NextCachedMeasurementsIndex;

    // Inline fixed-size buffer (#142): replaces a CachedMeasurement[8] heap
    // array. Exposed as a ref-returning property so existing in-place element
    // mutation (CachedMeasurements[idx].AvailableWidth = ...) is unchanged.
    private CachedMeasurementArray _cachedMeasurements;
    public ref CachedMeasurementArray CachedMeasurements => ref _cachedMeasurements;
    public CachedMeasurement CachedLayout;

    // Direction and overflow
    private FlexLayoutDirection _direction = FlexLayoutDirection.Inherit;
    private bool _hadOverflow;

    // Dimensions / edges stored inline (#142) instead of 7 separate float[]
    // arrays. For a ~200-node tree this removes ~1400 GC objects (7 arrays/node)
    // — a major part of the Yoga memory gap vs the C++ original's inline members.
    private Float2 _dimensions;
    private Float2 _measuredDimensions;
    private Float2 _rawDimensions;

    // Position, margin, border, padding (indexed by PhysicalEdge: Left=0, Top=1, Right=2, Bottom=3)
    private Float4 _position;
    private Float4 _margin;
    private Float4 _border;
    private Float4 _padding;

    public LayoutResults()
    {
        // CachedMeasurement's field initializers (the -1 sentinels) only run via
        // its constructor, NOT for default-initialized inline-array elements, so
        // seed each slot explicitly to preserve the previous array behavior.
        for (int i = 0; i < MaxCachedMeasurements; i++)
            _cachedMeasurements[i] = new CachedMeasurement();
        CachedLayout = new CachedMeasurement();

        // The _dimensions family historically defaulted to NaN (the float[]
        // initializers); _position/_margin/_border/_padding defaulted to 0,
        // which matches a zero-initialized inline buffer (no seeding needed).
        _dimensions[0] = float.NaN; _dimensions[1] = float.NaN;
        _measuredDimensions[0] = float.NaN; _measuredDimensions[1] = float.NaN;
        _rawDimensions[0] = float.NaN; _rawDimensions[1] = float.NaN;
    }

    public FlexLayoutDirection Direction
    {
        get => _direction;
        set => _direction = value;
    }

    public bool HadOverflow
    {
        get => _hadOverflow;
        set => _hadOverflow = value;
    }

    public float GetDimension(YogaDimension axis) => _dimensions[(int)axis];
    public void SetDimension(YogaDimension axis, float value) => _dimensions[(int)axis] = value;

    public float GetMeasuredDimension(YogaDimension axis) => _measuredDimensions[(int)axis];
    public void SetMeasuredDimension(YogaDimension axis, float value) => _measuredDimensions[(int)axis] = value;

    public float GetRawDimension(YogaDimension axis) => _rawDimensions[(int)axis];
    public void SetRawDimension(YogaDimension axis, float value) => _rawDimensions[(int)axis] = value;

    public float GetPosition(YogaPhysicalEdge edge) => _position[(int)edge];
    public void SetPosition(YogaPhysicalEdge edge, float value) => _position[(int)edge] = value;

    public float GetMargin(YogaPhysicalEdge edge) => _margin[(int)edge];
    public void SetMargin(YogaPhysicalEdge edge, float value) => _margin[(int)edge] = value;

    public float GetBorder(YogaPhysicalEdge edge) => _border[(int)edge];
    public void SetBorder(YogaPhysicalEdge edge, float value) => _border[(int)edge] = value;

    public float GetPadding(YogaPhysicalEdge edge) => _padding[(int)edge];
    public void SetPadding(YogaPhysicalEdge edge, float value) => _padding[(int)edge] = value;

    /// <summary>
    /// Reset all layout results to default values.
    /// </summary>
    public void Reset()
    {
        ComputedFlexBasisGeneration = 0;
        ComputedFlexBasis = float.NaN;
        GenerationCount = 0;
        ConfigVersion = 0;
        LastOwnerDirection = FlexLayoutDirection.Inherit;
        NextCachedMeasurementsIndex = 0;
        _direction = FlexLayoutDirection.Inherit;
        _hadOverflow = false;

        _dimensions[0] = float.NaN;
        _dimensions[1] = float.NaN;
        _measuredDimensions[0] = float.NaN;
        _measuredDimensions[1] = float.NaN;
        _rawDimensions[0] = float.NaN;
        _rawDimensions[1] = float.NaN;

        for (int i = 0; i < 4; i++)
        {
            _position[i] = 0;
            _margin[i] = 0;
            _border[i] = 0;
            _padding[i] = 0;
        }

        for (int i = 0; i < MaxCachedMeasurements; i++)
            _cachedMeasurements[i] = new CachedMeasurement();
        CachedLayout = new CachedMeasurement();
    }

    public bool EqualTo(LayoutResults other)
    {
        if (_direction != other._direction || _hadOverflow != other._hadOverflow)
            return false;

        for (int i = 0; i < 2; i++)
        {
            if (!YogaFloat.InexactEquals(_dimensions[i], other._dimensions[i]))
                return false;
        }

        for (int i = 0; i < 2; i++)
        {
            if (!YogaFloat.InexactEquals(_measuredDimensions[i], other._measuredDimensions[i]))
                return false;
        }

        for (int i = 0; i < 4; i++)
        {
            if (!YogaFloat.InexactEquals(_position[i], other._position[i]) ||
                !YogaFloat.InexactEquals(_margin[i], other._margin[i]) ||
                !YogaFloat.InexactEquals(_border[i], other._border[i]) ||
                !YogaFloat.InexactEquals(_padding[i], other._padding[i]))
                return false;
        }

        return true;
    }
}

// AI-HINT (perf #142): fixed-size inline buffers embedded directly in the
// LayoutResults heap object, replacing 7 float[] arrays + a CachedMeasurement[8]
// per node. For a ~200-node tree that removes ~1600 GC objects — a dominant
// part of the Yoga memory gap vs the C++ original (which uses inline members).
// InlineArray is a C# 12 feature: element access is compiler-generated and
// fully AOT/trim safe (no reflection, no Unsafe). Each struct has exactly one
// instance field, as InlineArray requires.

/// <summary>Inline buffer of 2 floats (YogaDimension: Width=0, Height=1).</summary>
[global::System.Runtime.CompilerServices.InlineArray(2)]
internal struct Float2
{
    private float _element0;
}

/// <summary>Inline buffer of 4 floats (YogaPhysicalEdge: Left=0..Bottom=3).</summary>
[global::System.Runtime.CompilerServices.InlineArray(4)]
internal struct Float4
{
    private float _element0;
}

/// <summary>Inline buffer of <see cref="LayoutResults.MaxCachedMeasurements"/> (8) cached measurements.</summary>
[global::System.Runtime.CompilerServices.InlineArray(8)]
internal struct CachedMeasurementArray
{
    private CachedMeasurement _element0;
}
