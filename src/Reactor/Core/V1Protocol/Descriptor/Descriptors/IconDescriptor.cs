using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 completion — decorator-style port of the polymorphic
/// <c>IconElement</c> mount/update arms.
/// </summary>
internal static class IconDescriptor
{
    public static readonly IDecoratorElementHandler<IconElement> Handler = new IconHandler();

    private sealed class IconHandler : IDecoratorElementHandler<IconElement>
    {
        public UIElement Mount(MountContext ctx, IconElement element)
        {
            var icon = Reconciler.ResolveIconForDescriptor(element.Data);
            if (icon is null)
                return EmptySentinel();

            Reconciler.SetElementTag(icon, element);
            Reconciler.ApplySetters(element.Setters, icon);
            return icon;
        }

        public UIElement Update(UpdateContext ctx, IconElement oldEl, IconElement newEl, UIElement control)
        {
            var fresh = Reconciler.ResolveIconForDescriptor(newEl.Data);
            if (fresh is null)
                return control is WinUI.IconElement ? EmptySentinel() : control;

            if (control is not WinUI.IconElement icon || fresh.GetType() != icon.GetType())
            {
                Reconciler.SetElementTag(fresh, newEl);
                Reconciler.ApplySetters(newEl.Setters, fresh);
                return fresh;
            }

            switch (newEl.Data)
            {
                case SymbolIconData sym when icon is WinUI.SymbolIcon si:
                    if (Enum.TryParse<Symbol>(sym.Symbol, ignoreCase: true, out var s)) si.Symbol = s;
                    break;
                case FontIconData fi when icon is WinUI.FontIcon fontIcon:
                    fontIcon.Glyph = fi.Glyph;
                    if (fi.FontFamily is not null)
                        fontIcon.FontFamily = new FontFamily(fi.FontFamily);
                    if (fi.FontSize is not null) fontIcon.FontSize = fi.FontSize.Value;
                    break;
                case BitmapIconData bi when icon is WinUI.BitmapIcon bitmapIcon:
                    bitmapIcon.UriSource = bi.Source;
                    bitmapIcon.ShowAsMonochrome = bi.ShowAsMonochrome;
                    break;
                case PathIconData pi when icon is WinUI.PathIcon pathIcon:
                    if (Microsoft.UI.Xaml.Markup.XamlReader.Load(
                        $"<Geometry xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>{pi.Data}</Geometry>")
                        is Geometry geo)
                        pathIcon.Data = geo;
                    break;
                case ImageIconData ii when icon is WinUI.ImageIcon imageIcon:
                    imageIcon.Source = new BitmapImage(ii.Source);
                    break;
            }

            Reconciler.SetElementTag(icon, newEl);
            Reconciler.ApplySetters(newEl.Setters, icon);
            return icon;
        }

        public V1UnmountDisposition Unmount(UnmountContext ctx, IconElement? element, UIElement control)
            => V1UnmountDisposition.CollectSelf;

        private static UIElement EmptySentinel()
            => new WinUI.TextBlock { Text = string.Empty };
    }
}
