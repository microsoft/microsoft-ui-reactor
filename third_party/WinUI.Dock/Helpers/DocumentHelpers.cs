using Microsoft.UI.Xaml.Media;

namespace WinUI.Dock;

public static class DocumentHelpers
{
    public static Document? GetDocument(object content)
    {
        if (content is DependencyObject reference)
        {
            DependencyObject dependencyObject = VisualTreeHelper.GetParent(reference);

            return dependencyObject is Document document ? document : GetDocument(dependencyObject);
        }

        return null;
    }
}
