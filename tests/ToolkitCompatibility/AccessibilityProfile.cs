namespace ToolkitCompatibility;

internal sealed record AccessibilityProfile(bool HighContrast, string HighContrastScheme, double TextScaleFactor)
{
    internal void RequireValid()
    {
        if (!double.IsFinite(TextScaleFactor) || TextScaleFactor <= 0)
            throw new InvalidOperationException($"Invalid OS TextScaleFactor={TextScaleFactor}.");
        if (HighContrast && string.IsNullOrWhiteSpace(HighContrastScheme))
            throw new InvalidOperationException("Active OS high contrast has no recorded scheme.");
    }

    internal void RequireStable(AccessibilityProfile after)
    {
        RequireValid();
        after.RequireValid();
        if (this != after)
            throw new InvalidOperationException(
                $"OS accessibility profile changed during capture/check; before={this}; after={after}.");
    }

    internal void RequireDocumentThemeResponse(AccessibilityProfile after, bool backgroundsEqual)
    {
        RequireStable(after);
        if (!HighContrast && backgroundsEqual)
            throw new InvalidOperationException("Document background did not respond to Light/Dark outside OS high contrast.");
    }
}
