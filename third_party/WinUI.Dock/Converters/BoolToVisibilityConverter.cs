using Microsoft.UI.Xaml.Data;

namespace WinUI.Dock;

internal partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool boolean ? boolean ? Visibility.Visible : Visibility.Collapsed : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is Visibility visibility && visibility is Visibility.Visible;
    }
}
