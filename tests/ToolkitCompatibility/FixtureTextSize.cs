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

    internal static double WrapRowWidth(double tileWidth, int columns, double rasterizationScale)
    {
        if (!double.IsFinite(tileWidth) || tileWidth <= 0 || columns <= 0 ||
            !double.IsFinite(rasterizationScale) || rasterizationScale <= 0)
            throw new InvalidOperationException("Wrap fixture needs positive tile width, column count and rasterization scale.");
        // Children round independently. One physical pixel also absorbs float conversion
        // at the row boundary without changing the intended number of columns.
        double width = (columns * Math.Ceiling(tileWidth * rasterizationScale) + 1) / rasterizationScale;
        if (!double.IsFinite(width) || width <= 0)
            throw new InvalidOperationException("Wrap fixture row width is not finite and positive.");
        return width;
    }
}
