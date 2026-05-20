using System.Collections.ObjectModel;
using System.ComponentModel;

namespace WinUI.Dock;

[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class Sidebar : UserControl
{
    public static readonly DependencyProperty DocumentsProperty = DependencyProperty.Register(nameof(Documents),
                                                                                              typeof(ObservableCollection<Document>),
                                                                                              typeof(Sidebar),
                                                                                              new PropertyMetadata(null));

    public static readonly DependencyProperty ManagerProperty = DependencyProperty.Register(nameof(Manager),
                                                                                            typeof(DockManager),
                                                                                            typeof(Sidebar),
                                                                                            new PropertyMetadata(null));

    public static readonly DependencyProperty SideProperty = DependencyProperty.Register(nameof(Side),
                                                                                         typeof(DockSide),
                                                                                         typeof(Sidebar),
                                                                                         new PropertyMetadata(DockSide.Left));

    public Sidebar()
    {
        InitializeComponent();
    }

    public ObservableCollection<Document>? Documents
    {
        get => (ObservableCollection<Document>?)GetValue(DocumentsProperty);
        set => SetValue(DocumentsProperty, value);
    }

    public DockManager? Manager
    {
        get => (DockManager)GetValue(ManagerProperty);
        set => SetValue(ManagerProperty, value);
    }

    public DockSide Side
    {
        get => (DockSide)GetValue(SideProperty);
        set => SetValue(SideProperty, value);
    }

    private void OnLoaded(object _, RoutedEventArgs __)
    {
        VisualStateManager.GoToState(this, Side.ToString(), false);
    }

    private void Document_Click(object sender, RoutedEventArgs _)
    {
        new SidePopup((Document)((Button)sender).DataContext, Manager!, Side).Show();
    }
}
