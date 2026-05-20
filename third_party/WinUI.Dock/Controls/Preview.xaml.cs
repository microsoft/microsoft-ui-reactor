using System.ComponentModel;

namespace WinUI.Dock;

[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class Preview : UserControl
{
    public Preview()
    {
        InitializeComponent();
    }

    public void Show(double width,
                     double height,
                     HorizontalAlignment horizontalAlignment,
                     VerticalAlignment verticalAlignment)
    {
        Visibility = Visibility.Visible;

        Main.Width = width;
        Main.Height = height;
        Main.HorizontalAlignment = horizontalAlignment;
        Main.VerticalAlignment = verticalAlignment;
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
    }
}