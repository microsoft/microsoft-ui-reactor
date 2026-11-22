using ToolkitCompatibility;
using Xunit;

namespace ToolkitCompatibility.HeadlessTests;

public sealed class AccessibilityEvidenceTests
{
    private sealed record Candidate(string Slot, string Text);

    [Fact]
    public void Selector_UsesExactTextAndExcludesOtherSlotsWithTheSameText()
    {
        var content = new Candidate("Content", "shared label");
        var header = new Candidate("Header", "shared label");
        var wrong = new Candidate("Header", "stale label");
        Candidate[] candidates = [wrong, content, header];

        Assert.Same(header, TextScalingTargets.SelectOne("Header", "shared label", candidates,
            c => c.Text, new HashSet<Candidate> { content }));
        Assert.Same(content, TextScalingTargets.SelectOne("Content", "shared label", candidates,
            c => c.Text, new HashSet<Candidate> { header }));
    }

    [Fact]
    public void Selector_RejectsMissingAmbiguousAndOutOfScopeText()
    {
        var first = new Candidate("Header", "label");
        var duplicate = new Candidate("Unexpected header", "label");
        Assert.Throws<InvalidOperationException>(() =>
            TextScalingTargets.SelectOne<Candidate>("Header", "label", [], c => c.Text));
        Assert.Throws<InvalidOperationException>(() =>
            TextScalingTargets.SelectOne("Header", "label", new[] { first, duplicate }, c => c.Text));
        Assert.Throws<InvalidOperationException>(() =>
            TextScalingTargets.SelectOne("Header", "label", new[] { first }, c => c.Text,
                new HashSet<Candidate> { first }));
        Assert.Throws<InvalidOperationException>(() =>
            TextScalingTargets.SelectOne("Header", "different label", new[] { first }, c => c.Text));
        Assert.Throws<InvalidOperationException>(() =>
            TextScalingTargets.SelectOne("Header", "", new[] { new Candidate("Header", "") }, c => c.Text));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 2)]
    [InlineData(6, 5)]
    [InlineData(6, 7)]
    [InlineData(0, 0)]
    public void TargetCount_RejectsEmptyMissingAndExtraTargets(int expected, int actual)
    {
        TextScalingTargets.RequireCount("Tiles", 6, 6);
        Assert.Throws<InvalidOperationException>(() =>
            TextScalingTargets.RequireCount("Tiles", expected, actual));
    }

    [Fact]
    public void DeclaredTarget_DistinguishesMissingFromPresentUnsupportedShapes()
    {
        var target = new Candidate("Content", "label");
        Assert.Same(target, TextScalingTargets.RequireTarget<Candidate>("Content", target));
        Assert.Throws<InvalidOperationException>(() =>
            TextScalingTargets.RequireTarget<Candidate>("Content", null));
        Assert.Throws<UnsupportedTextShapeException>(() =>
            TextScalingTargets.RequireTarget<Candidate>("Content", "present, but not the declared shape"));
    }

    [Fact]
    public void RichShape_RequiresNoInlinesOrExactlyOneEquivalentRun()
    {
        var run = new Candidate("Run", "label");
        TextScalingTargets.RequirePlainText<Candidate>("Header", [], _ => throw new InvalidOperationException());
        TextScalingTargets.RequirePlainText("Header", new[] { run }, c => c.Text == "label");
        var error = Assert.Throws<UnsupportedTextShapeException>(() =>
            TextScalingTargets.RequirePlainText("Header", new[] { run }, c => c.Text == "different"));
        Assert.Contains("Header", error.Message);
        Assert.Throws<UnsupportedTextShapeException>(() =>
            TextScalingTargets.RequirePlainText("Header", new[] { run, run }, _ => true));
    }

    [Theory]
    [InlineData("SettingsExpander")]
    [InlineData("Segmented")]
    [InlineData("HeaderedContentControl")]
    [InlineData("HeaderedItemsControl")]
    [InlineData("HeaderedTreeView")]
    [InlineData("LayoutTransformControl")]
    [InlineData("ConstrainedBox")]
    [InlineData("UniformGrid")]
    [InlineData("DockPanel")]
    [InlineData("WrapPanel")]
    [InlineData("StaggeredPanel")]
    public void BoundedSelectors_StillRequireAnActualLargerOsScale(string control)
    {
        Assert.Equal("approval", TextScalingMeasurements.BlockReason(control, 1));
        Assert.Null(TextScalingMeasurements.BlockReason(control, 1.5));
    }

    [Theory]
    [InlineData("RichSuggestBox")]
    [InlineData("TokenizingTextBox")]
    [InlineData("RadialGauge")]
    [InlineData("ColorPicker")]
    [InlineData("MetadataControl")]
    [InlineData("GridSplitter")]
    [InlineData("ContentSizer")]
    [InlineData("PropertySizer")]
    [InlineData("RangeSelector")]
    [InlineData("ImageCropper")]
    [InlineData("CameraPreview")]
    [InlineData("SwitchPresenter")]
    [InlineData("TabbedCommandBar")]
    public void UnsupportedControls_DoNotPassJustBecauseTheOsScaleIsLarger(string control)
    {
        Assert.Equal("not-implemented", TextScalingMeasurements.BlockReason(control, 1));
        Assert.Equal("not-implemented", TextScalingMeasurements.BlockReason(control, 1.5));
    }

    [Theory]
    [InlineData(false, "Contrast A", 1.5)]
    [InlineData(true, "Contrast B", 1.5)]
    [InlineData(true, "Contrast A", 1.5000000000001)]
    public void Profile_RejectsChangesToEitherOsSettingOrTheActiveScheme(
        bool highContrast, string scheme, double scale)
    {
        var before = new AccessibilityProfile(true, "Contrast A", 1.5);
        before.RequireStable(new AccessibilityProfile(true, "Contrast A", 1.5));
        var error = Assert.Throws<InvalidOperationException>(() =>
            before.RequireStable(new AccessibilityProfile(highContrast, scheme, scale)));
        Assert.Contains("changed during", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Profile_RejectsInvalidMeasurementsBeforeAndAfter(double scale)
    {
        var valid = new AccessibilityProfile(false, "", 1.5);
        var invalid = valid with { TextScaleFactor = scale };
        valid.RequireStable(new AccessibilityProfile(false, "", 1.5));
        Assert.Contains("Invalid OS TextScaleFactor",
            Assert.Throws<InvalidOperationException>(() => valid.RequireStable(invalid)).Message);
        Assert.Contains("Invalid OS TextScaleFactor",
            Assert.Throws<InvalidOperationException>(() => invalid.RequireStable(valid)).Message);
    }

    [Fact]
    public void Profile_RequiresASchemeOnlyWhenHighContrastIsActive()
    {
        new AccessibilityProfile(false, "", 1.5).RequireValid();
        Assert.Throws<InvalidOperationException>(() =>
            new AccessibilityProfile(true, "", 1.5).RequireValid());
    }
}
