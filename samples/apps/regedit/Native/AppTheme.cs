using Microsoft.UI.Xaml;

namespace ReactorRegedit.Native;

/// <summary>App-wide resource setup.</summary>
internal static class AppTheme
{
    /// <summary>
    /// F01 — WinUI compact sizing for every control (menus, text boxes, buttons,
    /// dialogs). Must run before the first control is created so styles pick it up.
    /// </summary>
    public static void ApplyCompactDensity()
    {
        var resources = Application.Current.Resources;
        try
        {
            resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("ms-appx:///Microsoft.UI.Xaml/DensityStyles/Compact.xaml"),
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Compact density dictionary unavailable: {ex.Message}");
        }

        // A few sizes Compact.xaml leaves at their touch-friendly defaults. The app
        // dictionary has a Source, so overrides go in their own merged dictionary.
        var overrides = new ResourceDictionary
        {
            ["MenuBarHeight"] = 30.0,
            ["MenuBarItemButtonPadding"] = new Thickness(8, 0, 8, 0),
            ["MenuFlyoutItemThemePaddingNarrow"] = new Thickness(11, 4, 11, 5),
            ["MenuFlyoutItemThemePadding"] = new Thickness(11, 5, 11, 6),
            ["TextControlThemeMinHeight"] = 24.0,
            ["ContentDialogPadding"] = new Thickness(20),
        };
        resources.MergedDictionaries.Add(overrides);
    }
}
