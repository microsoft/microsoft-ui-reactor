using Microsoft.UI.Reactor.Layout;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Port of upstream facebook/yoga <c>tests/CacheTest.cpp</c> (c3672f90, #1996):
/// direct tests of <c>canUseCachedMeasurement</c>, here
/// <see cref="CacheHelper.CanUseCachedMeasurement"/>.
/// </summary>
public class YogaCacheTests
{
    private readonly YogaConfig _config = new();

    // upstream: CacheTest.negativeCachedMeasurementDisablesCache
    [Fact]
    public void NegativeCachedMeasurementDisablesCache()
    {
        bool negativeWidth = CacheHelper.CanUseCachedMeasurement(
            SizingMode.StretchFit, 100f, SizingMode.StretchFit, 50f,
            SizingMode.StretchFit, 100f, SizingMode.StretchFit, 50f,
            -1f, 50f, 0f, 0f, _config);
        Assert.False(negativeWidth);

        bool negativeHeight = CacheHelper.CanUseCachedMeasurement(
            SizingMode.StretchFit, 100f, SizingMode.StretchFit, 50f,
            SizingMode.StretchFit, 100f, SizingMode.StretchFit, 50f,
            100f, -0.5f, 0f, 0f, _config);
        Assert.False(negativeHeight);
    }

    // upstream: CacheTest.identicalSpecsAllowCacheReuse
    [Fact]
    public void IdenticalSpecsAllowCacheReuse()
    {
        Assert.True(CacheHelper.CanUseCachedMeasurement(
            SizingMode.StretchFit, 100f, SizingMode.FitContent, 50f,
            SizingMode.StretchFit, 100f, SizingMode.FitContent, 50f,
            95f, 45f, 0f, 0f, _config));
    }

    // upstream: CacheTest.incompatibleSpecsDisableCache
    [Fact]
    public void IncompatibleSpecsDisableCache()
    {
        Assert.False(CacheHelper.CanUseCachedMeasurement(
            SizingMode.StretchFit, 200f, SizingMode.StretchFit, 200f,
            SizingMode.MaxContent, float.NaN, SizingMode.MaxContent, float.NaN,
            95f, 95f, 0f, 0f, _config));
    }

    // upstream: CacheTest.stretchFitMatchingComputedSizeReusesCacheAccountingForMargin
    [Fact]
    public void StretchFitMatchingComputedSizeReusesCacheAccountingForMargin()
    {
        Assert.True(CacheHelper.CanUseCachedMeasurement(
            SizingMode.StretchFit, 100f, SizingMode.StretchFit, 60f,
            SizingMode.FitContent, 90f, SizingMode.StretchFit, 60f,
            90f, 60f, 10f, 0f, _config));
        // Without subtracting the margin the request (100) would not match 90.
        Assert.False(CacheHelper.CanUseCachedMeasurement(
            SizingMode.StretchFit, 100f, SizingMode.StretchFit, 60f,
            SizingMode.FitContent, 90f, SizingMode.StretchFit, 60f,
            90f, 60f, 0f, 0f, _config));
    }

    // upstream: CacheTest.maxContentResultStillFitsFitContentRequest
    [Fact]
    public void MaxContentResultStillFitsFitContentRequest()
    {
        Assert.True(CacheHelper.CanUseCachedMeasurement(
            SizingMode.FitContent, 120f, SizingMode.StretchFit, 50f,
            SizingMode.MaxContent, float.NaN, SizingMode.StretchFit, 50f,
            80f, 50f, 0f, 0f, _config));
    }

    // upstream: CacheTest.tighterFitContentReusesCacheWhenPreviousResultStillFits
    [Fact]
    public void TighterFitContentReusesCacheWhenPreviousResultStillFits()
    {
        Assert.True(CacheHelper.CanUseCachedMeasurement(
            SizingMode.FitContent, 100f, SizingMode.StretchFit, 50f,
            SizingMode.FitContent, 150f, SizingMode.StretchFit, 50f,
            80f, 50f, 0f, 0f, _config));
    }

    // upstream: CacheTest.tighterFitContentInvalidatesCacheWhenPreviousResultOverflows
    [Fact]
    public void TighterFitContentInvalidatesCacheWhenPreviousResultOverflows()
    {
        Assert.False(CacheHelper.CanUseCachedMeasurement(
            SizingMode.FitContent, 100f, SizingMode.StretchFit, 50f,
            SizingMode.FitContent, 150f, SizingMode.StretchFit, 50f,
            120f, 50f, 0f, 0f, _config));
    }

    // upstream: CacheTest.pixelGridRoundingMakesSubPixelSpecsEquivalent
    [Fact]
    public void PixelGridRoundingMakesSubPixelSpecsEquivalent()
    {
        _config.PointScaleFactor = 2f;

        Assert.True(CacheHelper.CanUseCachedMeasurement(
            SizingMode.StretchFit, 10.25f, SizingMode.StretchFit, 20f,
            SizingMode.StretchFit, 10.5f, SizingMode.StretchFit, 20f,
            10.5f, 20f, 0f, 0f, _config));
    }
}
