using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Resolves a theme resource for a given theme the way a <c>{ThemeResource}</c> reference does,
/// without reading the theme dictionaries from code (issue #1534).
/// </summary>
/// <remarks>
/// <para>WinUI creates a theme dictionary entry the first time it is read, and resolves the
/// references inside it for the <em>application</em> theme. Code that reads the entry of the
/// theme the app is not using therefore gets the app theme's value back, and WinUI keeps that
/// value for every element in the app: in a Light app, reading the Dark entry of
/// <c>ControlFillColorSecondaryBrush</c> returned the Light color, and every Dark element then
/// rendered the Light color too. A window root with <c>RequestedTheme</c> does not help; only the
/// application theme counts. Reading the application theme's own entries is safe.</para>
/// <para>WinUI resolves a theme resource reference on an element for that element's theme and
/// creates the entry correctly, so this calls
/// <see cref="FrameworkElement.SetThemeResourceBinding"/> for <see cref="FrameworkElement.Tag"/>
/// on a new, unparented <see cref="Border"/> with the requested
/// <see cref="FrameworkElement.RequestedTheme"/>, and reads the value back (the API has no Get
/// method). <c>Tag</c> holds a value of any type, and the key needs no quoting. WinUI does the
/// lookup, so its rules apply, including the fallback to the <c>Default</c> dictionary, and it
/// returns the dictionary's own instance (issue #1634).</para>
/// <para>UI thread only, like every other read of the application resources.</para>
/// </remarks>
internal static class ThemeResourceProbe
{
    /// <summary>
    /// Resolves <paramref name="resourceKey"/> for <paramref name="theme"/> through WinUI. Returns
    /// false when WinUI cannot resolve the key for that theme.
    /// </summary>
    internal static bool TryResolve(string resourceKey, ElementTheme theme, out object? value)
    {
        value = null;
        if (string.IsNullOrEmpty(resourceKey)) return false;

        var probe = new Border { RequestedTheme = theme };
        try
        {
            probe.SetThemeResourceBinding(FrameworkElement.TagProperty, resourceKey);
        }
        catch (global::System.Runtime.InteropServices.COMException)
        {
            // WinUI checks the key when the binding is set: a key it cannot find for the app theme
            // or for the requested one (missing, or defined only for some themes) throws. Nothing
            // is cached, so a dictionary merged in later is picked up.
            return false;
        }
        value = probe.Tag;
        return value is not null;
    }
}
