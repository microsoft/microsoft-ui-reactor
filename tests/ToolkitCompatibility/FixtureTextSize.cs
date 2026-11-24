namespace ToolkitCompatibility;

internal readonly record struct FixtureTextSize(double Width, double Height)
{
    internal static FixtureTextSize FromMeasurements(IEnumerable<FixtureTextSize> measurements)
    {
        var sizes = measurements.ToArray();
        if (sizes.Length == 0 || sizes.Any(s =>
            !double.IsFinite(s.Width) || s.Width <= 0 || !double.IsFinite(s.Height) || s.Height <= 0))
            throw new InvalidOperationException("Fixture labels need finite, positive native text measurements.");
        return new(Math.Ceiling(sizes.Max(s => s.Width)), Math.Ceiling(sizes.Max(s => s.Height)));
    }

    internal double WidthAtLeast(double minimum) => Math.Max(minimum, Width);
    internal double HeightAtLeast(double minimum) => Math.Max(minimum, Height);
}
