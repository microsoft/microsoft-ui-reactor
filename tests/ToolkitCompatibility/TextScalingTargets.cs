namespace ToolkitCompatibility;

internal sealed class UnsupportedTextShapeException(string message) : Exception(message);

internal static class TextScalingTargets
{
    internal static bool Supports(string control) => control is
        "SettingsCard" or "SettingsExpander" or "Segmented" or
        "HeaderedContentControl" or "HeaderedItemsControl" or "HeaderedTreeView" or
        "LayoutTransformControl" or "ConstrainedBox" or "UniformGrid" or
        "DockPanel" or "WrapPanel" or "StaggeredPanel";

    internal static T SelectOne<T>(string role, string expectedText, IEnumerable<T> candidates,
        Func<T, string> readText, IReadOnlySet<T>? excluded = null) where T : notnull
    {
        if (string.IsNullOrWhiteSpace(expectedText))
            throw new InvalidOperationException($"{role}: expected text is empty.");
        var matches = candidates.Where(candidate => excluded?.Contains(candidate) != true &&
            readText(candidate) == expectedText).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                $"{role}: expected exactly one owned TextBlock for '{expectedText}', found {matches.Length}.");
        return matches[0];
    }

    internal static void RequireCount(string role, int expected, int actual)
    {
        if (expected <= 0 || actual != expected)
            throw new InvalidOperationException($"{role}: expected {expected} targets, found {actual}.");
    }

    internal static T RequireTarget<T>(string role, object? value) where T : class
    {
        if (value is null)
            throw new InvalidOperationException($"{role}: declared target is missing.");
        return value as T ??
            throw new UnsupportedTextShapeException($"{role}: present target has an unsupported shape.");
    }

    internal static void RequirePlainText<T>(string role, IEnumerable<T> inlines, Func<T, bool> equivalentRun)
    {
        var values = inlines.ToArray();
        if (values.Length > 1 || values.Length == 1 && !equivalentRun(values[0]))
            throw new UnsupportedTextShapeException(
                $"{role}: inline/rich typography is not covered by the plain-TextBlock oracle.");
    }
}
