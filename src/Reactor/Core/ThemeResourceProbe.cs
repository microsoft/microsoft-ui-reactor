using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

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
/// <para>WinUI resolves a <c>{ThemeResource}</c> on an element for that element's theme and
/// creates the entry correctly, so this applies a cached <see cref="Style"/> whose setter
/// assigns <c>{ThemeResource key}</c> to <see cref="FrameworkElement.Tag"/> on a new, unparented
/// <see cref="Border"/> with the requested <see cref="FrameworkElement.RequestedTheme"/>, and
/// reads the value back. WinUI does the lookup, so its rules apply, including the fallback to
/// the <c>Default</c> dictionary.</para>
/// <para>UI thread only. The styles are cached per thread because XAML objects belong to the
/// thread that created them.</para>
/// <para>The XamlReader-parsed style is there because stable WinUI has no code API for a
/// <c>{ThemeResource}</c> reference. <c>FrameworkElement.SetThemeResourceBinding</c>
/// (microsoft/microsoft-ui-xaml#11229) is that API, but it is still experimental. Once a stable
/// Windows App SDK ships it, call it on the probe border instead and drop the style cache.</para>
/// </remarks>
internal static class ThemeResourceProbe
{
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [ThreadStatic] private static Dictionary<string, Style>? t_styles;

    /// <summary>
    /// Resolves <paramref name="resourceKey"/> for <paramref name="theme"/> through WinUI. Returns
    /// false when WinUI cannot resolve the key for that theme, or the key cannot be written as a
    /// <c>{ThemeResource}</c> reference.
    /// </summary>
    internal static bool TryResolve(string resourceKey, ElementTheme theme, out object? value)
    {
        value = null;
        var styles = t_styles ??= new Dictionary<string, Style>(StringComparer.Ordinal);
        if (!styles.TryGetValue(resourceKey, out var style))
        {
            var xaml = BuildStyleXaml(resourceKey);
            if (xaml is null) return false;
            try
            {
                style = (Style)XamlReader.Load(xaml);
            }
            catch (Exception ex) when (ex is XamlParseException or global::System.Runtime.InteropServices.COMException)
            {
                // WinUI checks the reference when it loads the style, against the app theme: a key
                // it cannot find there (missing, or defined only for another theme) throws. Not
                // cached, so a dictionary merged in later is picked up.
                return false;
            }
            styles[resourceKey] = style;
        }

        // The style re-resolves the reference on each apply, so a cached style still sees a
        // dictionary that was replaced or removed since (a removed key reads back as null).
        var probe = new Border { RequestedTheme = theme, Style = style };
        value = probe.Tag;
        return value is not null;
    }

    /// <summary>
    /// The XAML of a <c>Border</c> style that sets <c>Tag</c> to <c>{ThemeResource key}</c>, or
    /// null when the key cannot be written as a reference. The quoting and escaping rules are
    /// <see cref="Reconciler.FormatThemeResourceReference"/>'s.
    /// </summary>
    internal static string? BuildStyleXaml(string resourceKey)
    {
        if (Reconciler.FormatThemeResourceReference(resourceKey) is not { } value) return null;

        return $"<Style xmlns='{XamlNamespace}' TargetType='Border'>"
            + $"<Setter Property='Tag' Value=\"{value}\"/>"
            + "</Style>";
    }
}
