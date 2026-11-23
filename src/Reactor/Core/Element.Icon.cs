using System;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Spec 058 §15 (P5.27) — polymorphic descriptor for the standalone
/// <see cref="IconElement"/>. <see cref="IconData"/> is a discriminated union, so the
/// concrete WinUI control (<see cref="WinUI.SymbolIcon"/>/<see cref="WinUI.FontIcon"/>/…)
/// is chosen at runtime. The <c>[WrapPolymorphic]</c> generator capability emits an
/// <c>IDecoratorElementHandler&lt;IconElement&gt;</c> + Pattern-A registration around the
/// <see cref="ResolveIcon"/> resolver and the <see cref="PatchIcon"/> same-subtype patch,
/// replacing the hand-written <c>IconDescriptor</c>.
/// </summary>
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.IconElement))]
[global::Microsoft.UI.Reactor.Wrappers.WrapPolymorphic(nameof(ResolveIcon), Reconcile = nameof(PatchIcon))]
public partial record IconElement
{
    // Mount + type-change rebuild: produce the concrete IconElement subtype from Data.
    private static WinUI.IconElement? ResolveIcon(IconElement element)
        => IconResolver.ResolveIconForDescriptor(element.Data);

    // Same-subtype patch: when the live control's runtime type still matches the new
    // Data subtype, mutate in place and return true; otherwise return false so the
    // generated Update rebuilds via ResolveIcon.
    private static bool PatchIcon(IconElement oldEl, IconElement newEl, WinUI.IconElement icon)
    {
        switch (newEl.Data)
        {
            case SymbolIconData sym when icon is WinUI.SymbolIcon si:
                if (Enum.TryParse<WinUI.Symbol>(sym.Symbol, ignoreCase: true, out var s)) si.Symbol = s;
                return true;
            case FontIconData fi when icon is WinUI.FontIcon fontIcon:
                fontIcon.Glyph = fi.Glyph;
                if (fi.FontFamily is not null)
                    fontIcon.FontFamily = WinRTCache.GetFontFamily(fi.FontFamily);
                if (fi.FontSize is not null) fontIcon.FontSize = fi.FontSize.Value;
                return true;
            case BitmapIconData bi when icon is WinUI.BitmapIcon bitmapIcon:
                bitmapIcon.UriSource = bi.Source;
                bitmapIcon.ShowAsMonochrome = bi.ShowAsMonochrome;
                return true;
            case PathIconData pi when icon is WinUI.PathIcon pathIcon:
                pathIcon.Data = PathMarkup.Convert(pi.Data);
                return true;
            case ImageIconData ii when icon is WinUI.ImageIcon imageIcon:
                imageIcon.Source = new BitmapImage(ii.Source);
                return true;
            default:
                return false;
        }
    }
}
