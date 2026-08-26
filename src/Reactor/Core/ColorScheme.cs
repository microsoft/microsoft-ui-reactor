using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Represents a color scheme variant a component can adapt its rendering to.
/// <para>
/// Note that <see cref="RenderContext.UseColorScheme"/> reports the app-global theme and
/// only ever returns <see cref="Light"/> or <see cref="Dark"/>; <see cref="HighContrast"/>
/// is produced solely by the <see cref="ElementTheme.Default"/> mapping arm below. Use
/// <see cref="RenderContext.UseHighContrast"/> to detect forced-colors mode.
/// </para>
/// </summary>
public enum ColorScheme
{
    /// <summary>The standard light theme.</summary>
    Light,

    /// <summary>The standard dark theme.</summary>
    Dark,

    /// <summary>Windows High Contrast mode is active.</summary>
    HighContrast,
}

/// <summary>
/// Tracks the effective color scheme and provides mapping from WinUI
/// <see cref="ElementTheme"/> values to <see cref="ColorScheme"/>.
/// </summary>
internal class ColorSchemeContext
{
    public ColorScheme CurrentScheme { get; private set; } = ColorScheme.Light;

    /// <summary>
    /// Updates the current scheme based on an <see cref="ElementTheme"/> value.
    /// <see cref="ElementTheme.Dark"/> → <see cref="ColorScheme.Dark"/>,
    /// <see cref="ElementTheme.Light"/> → <see cref="ColorScheme.Light"/>,
    /// <see cref="ElementTheme.Default"/> → checks High Contrast, then falls back to <see cref="ColorScheme.Light"/>.
    /// </summary>
    public void Update(ElementTheme actualTheme)
    {
        CurrentScheme = actualTheme switch
        {
            ElementTheme.Dark => ColorScheme.Dark,
            ElementTheme.Light => ColorScheme.Light,
            _ => DetectHighContrast() ? ColorScheme.HighContrast : ColorScheme.Light,
        };
    }

    /// <summary>
    /// Maps an <see cref="ElementTheme"/> to <see cref="ColorScheme"/> with
    /// High Contrast detection for the Default case.
    /// </summary>
    internal static ColorScheme FromActualTheme(ElementTheme actualTheme)
    {
        return actualTheme switch
        {
            ElementTheme.Dark => ColorScheme.Dark,
            ElementTheme.Light => ColorScheme.Light,
            _ => DetectHighContrast() ? ColorScheme.HighContrast : ColorScheme.Light,
        };
    }

    private static bool DetectHighContrast()
    {
        try
        {
            var settings = new global::Windows.UI.ViewManagement.AccessibilitySettings();
            return settings.HighContrast;
        }
        catch
        {
            return false;
        }
    }
}
