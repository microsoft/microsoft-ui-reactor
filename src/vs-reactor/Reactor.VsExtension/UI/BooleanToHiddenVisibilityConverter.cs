#nullable enable

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Microsoft.UI.Reactor.VsExtension.UI
{
    /// <summary>
    /// Maps a <see cref="bool"/> to <see cref="Visibility.Visible"/> when true and
    /// <see cref="Visibility.Hidden"/> when false. Unlike the framework's
    /// <see cref="BooleanToVisibilityConverter"/> (which maps false to
    /// <see cref="Visibility.Collapsed"/>), this converter preserves layout space
    /// for the hidden element.
    ///
    /// We need that distinction for the embedded preview's <c>HwndHostPlaceholder</c>:
    /// when collapsed, WPF skips layout entirely so the HwndHost never raises
    /// <c>OnWindowPositionChanged</c>. That left <c>ViewModel.LastPlaceholderRect</c>
    /// stuck at its default 0,0,0,0, and the subsequent <c>AckEmbedAsync</c> handshake
    /// would tell the child app to resize to width=0, height=0 — producing a
    /// black, invisible client area once the placeholder is finally shown.
    /// Visibility.Hidden keeps layout active (so OnWindowPositionChanged fires and
    /// the rect is correct when AckEmbedAsync runs) while still SW_HIDE'ing the
    /// underlying HWND so WPF airspace lets our placeholder overlay show through.
    /// </summary>
    public sealed class BooleanToHiddenVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? Visibility.Visible : Visibility.Hidden;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility v && v == Visibility.Visible;
        }
    }
}
