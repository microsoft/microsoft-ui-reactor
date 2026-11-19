using ToolkitCompatibility;
using Xunit;

namespace ToolkitCompatibility.HeadlessTests;

public sealed class TextScalingMeasurementsTests
{
    [Theory]
    [InlineData("SettingsCard", 1, "approval")]
    [InlineData("SettingsCard", 1.01, null)]
    [InlineData("SettingsCard", 2.25, null)]
    [InlineData("SettingsExpander", 1, "not-implemented")]
    [InlineData("SettingsExpander", 2, "not-implemented")]
    [InlineData("GridSplitter", 2, "not-implemented")]
    [InlineData("Unknown", 2, "not-implemented")]
    public void Gate_OnlyAllowsImplementedCoverageAtLargerRealScale(string control, double scale, string? blocker)
    {
        Assert.Equal(blocker, TextScalingMeasurements.BlockReason(control, scale));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Gate_RejectsInvalidOsMeasurement(double scale)
    {
        Assert.Throws<InvalidOperationException>(() => TextScalingMeasurements.BlockReason("SettingsCard", scale));
    }

    [Theory]
    [InlineData("", true, false)]
    [InlineData(" ", true, false)]
    [InlineData("Description", false, false)]
    [InlineData("Description", true, true)]
    public void Text_RejectsEmptyDisabledOrTrimmedContent(string text, bool scaling, bool trimmed)
    {
        TextScalingMeasurements.RequireText("Description", "Visible description", true, false);
        Assert.Throws<InvalidOperationException>(() =>
            TextScalingMeasurements.RequireText("Description", text, scaling, trimmed));
    }

    [Theory]
    [InlineData(98, 40, 100, 40)]
    [InlineData(100, 38, 100, 40)]
    [InlineData(100, 40, 102, 40)]
    [InlineData(100, 40, 100, 42)]
    [InlineData(0, 40, 100, 40)]
    [InlineData(100, 0, 100, 40)]
    [InlineData(100, 40, 0, 40)]
    [InlineData(100, 40, 100, 0)]
    [InlineData(double.NaN, 40, 100, 40)]
    [InlineData(100, double.PositiveInfinity, 100, 40)]
    [InlineData(100, 40, double.PositiveInfinity, 40)]
    [InlineData(100, 40, 100, double.NaN)]
    public void Fit_RejectsOverflowAndInvalidMeasurements(double width, double height, double requiredWidth, double requiredHeight)
    {
        TextScalingMeasurements.RequireFits("Header", 100, 40, 100, 40);
        TextScalingMeasurements.RequireFits("Header", 100, 40, 101, 41);
        Assert.Throws<InvalidOperationException>(() =>
            TextScalingMeasurements.RequireFits("Header", width, height, requiredWidth, requiredHeight));
    }

    [Theory]
    [InlineData(-2, 0, 80, 20, 100, 40)]
    [InlineData(0, -2, 80, 20, 100, 40)]
    [InlineData(22, 0, 80, 20, 100, 40)]
    [InlineData(0, 22, 80, 20, 100, 40)]
    [InlineData(0, 0, 0, 20, 100, 40)]
    [InlineData(0, 0, 80, 0, 100, 40)]
    [InlineData(0, 0, 80, 20, 0, 40)]
    [InlineData(0, 0, 80, 20, 100, 0)]
    [InlineData(double.NaN, 0, 80, 20, 100, 40)]
    [InlineData(0, double.PositiveInfinity, 80, 20, 100, 40)]
    [InlineData(0, 0, double.PositiveInfinity, 20, 100, 40)]
    [InlineData(0, 0, 80, double.NaN, 100, 40)]
    [InlineData(0, 0, 80, 20, double.PositiveInfinity, 40)]
    [InlineData(0, 0, 80, 20, 100, double.NaN)]
    public void Ancestors_RejectClippingInEveryDirectionAndInvalidBounds(double x, double y, double width, double height,
        double ancestorWidth, double ancestorHeight)
    {
        TextScalingMeasurements.RequireContained("Description", 10, 10, 80, 20, 100, 40);
        TextScalingMeasurements.RequireContained("Description", -1, -1, 102, 42, 100, 40);
        Assert.Throws<InvalidOperationException>(() =>
            TextScalingMeasurements.RequireContained("Description", x, y, width, height, ancestorWidth, ancestorHeight));
    }
}
