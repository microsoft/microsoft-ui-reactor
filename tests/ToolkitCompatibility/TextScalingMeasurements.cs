namespace ToolkitCompatibility;

internal static class TextScalingMeasurements
{
    private const double LayoutTolerance = 1;

    internal static string? BlockReason(string control, double scale)
    {
        Require(Positive(scale), $"Invalid OS TextScaleFactor={scale}.");
        if (control != "SettingsCard") return "not-implemented";
        return scale <= 1 ? "approval" : null;
    }

    internal static void RequireText(string role, string text, bool scaleEnabled, bool trimmed)
    {
        Require(!string.IsNullOrWhiteSpace(text), $"{role}: text is empty.");
        Require(scaleEnabled, $"{role}: OS text scaling is disabled on the TextBlock.");
        Require(!trimmed, $"{role}: native text is trimmed.");
    }

    internal static double ProbeWidth(string role, double actualWidth, double desiredWidth, double slotWidth,
        double marginLeft, double marginRight)
    {
        Require(Positive(actualWidth) && Positive(desiredWidth) && Positive(slotWidth) &&
            double.IsFinite(marginLeft) && double.IsFinite(marginRight),
            $"{role}: invalid/empty measure or arrange width.");
        double desiredContentWidth = desiredWidth - marginLeft - marginRight;
        double slotContentWidth = slotWidth - marginLeft - marginRight;
        Require(Positive(desiredContentWidth) && Positive(slotContentWidth),
            $"{role}: margins leave no finite positive measure or arrange width.");
        return Math.Max(actualWidth, Math.Min(desiredContentWidth, slotContentWidth));
    }

    internal static void RequireFits(string role, double width, double height, double requiredWidth, double requiredHeight)
    {
        Require(Positive(width) && Positive(height) && Positive(requiredWidth) && Positive(requiredHeight),
            $"{role}: invalid/empty layout or full-text measurement ({width}x{height}, required {requiredWidth}x{requiredHeight}).");
        Require(requiredWidth <= width + LayoutTolerance && requiredHeight <= height + LayoutTolerance,
            $"{role}: full text requires {requiredWidth}x{requiredHeight}, allocated {width}x{height}.");
    }

    internal static void RequireContained(string role, double x, double y, double width, double height,
        double ancestorWidth, double ancestorHeight)
    {
        Require(double.IsFinite(x) && double.IsFinite(y) && Positive(width) && Positive(height) &&
            Positive(ancestorWidth) && Positive(ancestorHeight),
            $"{role}: invalid/empty ancestor geometry.");
        Require(x >= -LayoutTolerance && y >= -LayoutTolerance &&
            x + width <= ancestorWidth + LayoutTolerance && y + height <= ancestorHeight + LayoutTolerance,
            $"{role}: text bounds ({x},{y},{width},{height}) exceed ancestor {ancestorWidth}x{ancestorHeight}.");
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
