using System;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Xaml.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

// Shared V1 projection helpers for Reactor icon descriptors and symbol strings.
internal static class IconResolver
{
    /// <summary>
    /// Icon font stack used when the <c>SymbolThemeFontFamily</c> theme resource cannot be
    /// resolved (no <c>Application.Current</c>, or a host that did not merge
    /// <c>XamlControlsResources</c>). "Segoe Fluent Icons" ships with Windows 11; the
    /// "Segoe MDL2 Assets" tail keeps glyphs rendering on Windows 10, which WinUI 3 still
    /// supports. Always resolve it through <c>WinRTCache.GetFontFamily</c> — a raw
    /// <c>new FontFamily(...)</c> crosses the managed→WinRT boundary on every call.
    /// </summary>
    internal const string SymbolFontFallback = "Segoe Fluent Icons, Segoe MDL2 Assets";

    /// <summary>
    /// Resolves the <c>SymbolThemeFontFamily</c> theme resource, falling back to
    /// <see cref="SymbolFontFallback"/> when it is unavailable.
    /// <para>
    /// Uses <c>TryGetValue</c> rather than the <c>Resources[key]</c> indexer on purpose:
    /// <c>ResourceDictionary</c> implements <c>IDictionary</c>, so the indexer <em>throws</em>
    /// on a missing key instead of returning null. With the indexer the <c>??</c> fallback
    /// below could never run — a host that had not merged <c>XamlControlsResources</c> would
    /// get a <c>KeyNotFoundException</c> rather than the fallback stack this method exists to
    /// provide. Matches the lookup idiom already used by <c>Theme.cs</c>.
    /// </para>
    /// </summary>
    private static Microsoft.UI.Xaml.Media.FontFamily ResolveSymbolFontFamily()
    {
        var resources = Microsoft.UI.Xaml.Application.Current?.Resources;
        if (resources is not null
            && resources.TryGetValue("SymbolThemeFontFamily", out var value)
            && value is Microsoft.UI.Xaml.Media.FontFamily themeFont)
        {
            return themeFont;
        }
        return WinRTCache.GetFontFamily(SymbolFontFallback);
    }

    /// <summary>Descriptor-accessible bridge to <see cref="ResolveIcon"/>
    /// for icon-bearing descriptor controls. Static so it can be invoked from a
    /// descriptor lambda without a Reconciler instance.</summary>
    internal static WinUI.IconElement? ResolveIconForDescriptor(IconData? iconData)
        => ResolveIcon(iconData, null);

    internal static WinUI.IconElement? ResolveIcon(IconData? iconData, string? iconSymbol)
    {
        if (iconData is not null)
        {
            return iconData switch
            {
                SymbolIconData sym => ResolveIconString(sym.Symbol) ?? new WinUI.SymbolIcon(Symbol.Placeholder),
                FontIconData fi => CreateFontIcon(fi),
                BitmapIconData bi => new WinUI.BitmapIcon { UriSource = bi.Source, ShowAsMonochrome = bi.ShowAsMonochrome },
                PathIconData pi => CreatePathIcon(pi),
                ImageIconData ii => new WinUI.ImageIcon { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(ii.Source) },
                _ => null,
            };
        }
        if (iconSymbol is not null) return ResolveIconString(iconSymbol);
        return null;
    }

    // Handles both Symbol enum names ("Home", "Edit") and raw Segoe Fluent
    // glyphs (""). A Symbol enum mismatch used to collapse to
    // Symbol.Placeholder, which rendered as a diamond — fall through to a
    // FontIcon with SymbolThemeFontFamily so glyph strings render correctly.
    internal static WinUI.IconElement? ResolveIconString(string iconSymbol)
    {
        if (string.IsNullOrEmpty(iconSymbol)) return null;
        if (Enum.TryParse<Symbol>(iconSymbol, ignoreCase: true, out var symbol))
            return new WinUI.SymbolIcon(symbol);
        // Treat as a Segoe Fluent / MDL2 glyph codepoint.
        return new WinUI.FontIcon
        {
            Glyph = iconSymbol,
            FontFamily = ResolveSymbolFontFamily(),
        };
    }

    // IconSource counterpart for controls (TabView, etc.) that take an
    // IconSource instead of IconElement. Same glyph-fallback semantics as
    // ResolveIconString.
    internal static WinUI.IconSource? ResolveIconSource(string? iconSymbol)
    {
        if (string.IsNullOrEmpty(iconSymbol)) return null;
        if (Enum.TryParse<Symbol>(iconSymbol, ignoreCase: true, out var symbol))
            return new WinUI.SymbolIconSource { Symbol = symbol };
        return new WinUI.FontIconSource
        {
            Glyph = iconSymbol,
            FontFamily = ResolveSymbolFontFamily(),
        };
    }

    /// <summary>
    /// Strongly-typed <see cref="IconData"/> → <see cref="WinUI.IconSource"/>
    /// projection. Used by controls that expose an <c>IconSource</c> slot
    /// (TitleBar, TabView, etc.). Returns null on unknown subtypes so the
    /// caller can fall through to the string-glyph overload.
    /// </summary>
    internal static WinUI.IconSource? ResolveIconSource(IconData? iconData) => iconData switch
    {
        null => null,
        SymbolIconData sym => ResolveIconSource(sym.Symbol),
        FontIconData fi => CreateFontIconSource(fi),
        BitmapIconData bi => new WinUI.BitmapIconSource { UriSource = bi.Source, ShowAsMonochrome = bi.ShowAsMonochrome },
        PathIconData pi => CreatePathIconSource(pi),
        ImageIconData ii => new WinUI.ImageIconSource
        {
            ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(ii.Source),
        },
        _ => null,
    };

    internal static WinUI.PathIconSource? CreatePathIconSource(PathIconData pi)
        => new WinUI.PathIconSource { Data = PathMarkup.Convert(pi.Data) };

    internal static WinUI.FontIcon CreateFontIcon(FontIconData fi)
    {
        var icon = new WinUI.FontIcon { Glyph = fi.Glyph };
        if (fi.FontFamily is not null) icon.FontFamily = WinRTCache.GetFontFamily(fi.FontFamily);
        if (fi.FontSize.HasValue) icon.FontSize = fi.FontSize.Value;
        return icon;
    }

    // Mirror of CreateFontIcon for the IconSource slot (TitleBar, TabView, etc.).
    // FontFamily / FontSize are only assigned when specified: a FontIconSource
    // with FontSize = double.NaN (or a null FontFamily) is rejected by
    // TitleBar.set_IconSource with E_INVALIDARG ("Value does not fall within the
    // expected range"), so leave them at their control defaults when unset.
    internal static WinUI.FontIconSource CreateFontIconSource(FontIconData fi)
    {
        var src = new WinUI.FontIconSource { Glyph = fi.Glyph };
        if (fi.FontFamily is not null) src.FontFamily = WinRTCache.GetFontFamily(fi.FontFamily);
        if (fi.FontSize.HasValue) src.FontSize = fi.FontSize.Value;
        return src;
    }

    internal static WinUI.PathIcon CreatePathIcon(PathIconData pi)
        => new WinUI.PathIcon { Data = PathMarkup.Convert(pi.Data) };

    internal static Symbol ParseSymbol(string name)
    {
        if (Enum.TryParse<Symbol>(name, ignoreCase: true, out var symbol)) return symbol;
        return Symbol.Placeholder;
    }
}
