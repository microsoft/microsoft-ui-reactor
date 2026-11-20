using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Converts XAML path markup ("M0,0 L8,8 Z") to a <see cref="Geometry"/> with WinUI's own
/// native type converter — the same one a XAML <c>Data="..."</c> attribute goes through —
/// without <c>XamlReader</c>. The string is never spliced into a XAML document, and no
/// runtime XAML parsing is involved, so this is safe under trimming and NativeAOT.
/// </summary>
internal static class PathMarkup
{
    /// <summary>
    /// Returns the geometry described by <paramref name="data"/>, or <see langword="null"/>
    /// for null or blank input. Markup the platform rejects throws, as it would from XAML —
    /// used for author-written markup such as <c>PathIcon(...)</c>, where a typo must stay visible.
    /// </summary>
    internal static Geometry? Convert(string? data)
        => string.IsNullOrWhiteSpace(data)
            ? null
            : XamlBindingHelper.ConvertValue(typeof(Geometry), data) as Geometry;

    /// <summary>
    /// Like <see cref="Convert"/>, but markup the platform rejects — in practice a non-finite
    /// coordinate (<c>NaN</c>, <c>Infinity</c>) from corrupt chart data — returns
    /// <see langword="null"/> so the path is not drawn, and logs a warning. Used for
    /// data-driven markup (<c>PathElement.PathDataString</c>, D3 paths).
    /// </summary>
    internal static Geometry? TryConvert(string? data)
    {
        try
        {
            return Convert(data);
        }
        catch (global::System.Exception ex) when (ex is global::System.ArgumentException
            or global::System.FormatException or global::System.Runtime.InteropServices.COMException)
        {
            if (DiagnosticLog.IsWarningEnabled)
            {
                // The markup itself is app data, so it stays out of the message.
                DiagnosticLog.Warning(LogCategory.Reactor, "PathMarkup.TryConvert",
                    $"Path markup was rejected by the platform converter ({ex.GetType().Name}); the path is not drawn. "
                    + "Non-finite coordinates (NaN, Infinity) are the usual cause — filter them out, e.g. with SetDefined on D3 generators.");
            }
            return null;
        }
    }
}
