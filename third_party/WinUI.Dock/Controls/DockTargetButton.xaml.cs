using System.ComponentModel;
using Windows.ApplicationModel.DataTransfer;

namespace WinUI.Dock;

[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class DockTargetButton : UserControl
{
    public static readonly DependencyProperty DestinationProperty = DependencyProperty.Register(nameof(Destination),
                                                                                                typeof(Control),
                                                                                                typeof(DockTargetButton),
                                                                                                new PropertyMetadata(null));

    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(nameof(Target),
                                                                                           typeof(DockTarget),
                                                                                           typeof(DockTargetButton),
                                                                                           new PropertyMetadata(DockTarget.Center));

    public DockTargetButton()
    {
        InitializeComponent();
    }

    public Control? Destination
    {
        get => (Control)GetValue(DestinationProperty);
        set => SetValue(DestinationProperty, value);
    }

    public DockTarget Target
    {
        get => (DockTarget)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    protected override async void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);

        if (Destination is DockManager manager)
        {
            string documentKey = (string)await e.DataView.GetDataAsync(DragDropHelpers.DocumentKey);

            if (DragDropHelpers.GetDocument(documentKey) is Document document)
            {
                manager.ShowDockPreview(document, Target);
            }
        }
        else if (Destination is DocumentGroup group)
        {
            group.ShowDockPreview(Target);
        }
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);

        if (Destination is DockManager manager)
        {
            manager.HideDockPreview();
        }
        else if (Destination is DocumentGroup group)
        {
            group.HideDockPreview();
        }
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);

        e.AcceptedOperation = DataPackageOperation.Move;
    }

    protected override async void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);

        string documentKey = (string)await e.DataView.GetDataAsync(DragDropHelpers.DocumentKey);

        if (DragDropHelpers.GetDocument(documentKey) is Document document)
        {
            if (Destination is DockManager manager)
            {
                document.ResetPreferredSide(Target);

                manager.HideDockPreview();
                manager.Dock(document, Target);
            }
            else if (Destination is DocumentGroup group)
            {
                group.HideDockPreview();
                group.Dock(document, Target);
            }

            document.Root!.HideDockTargets();
        }
    }

    private void OnLoaded(object _, RoutedEventArgs __)
    {
        VisualStateManager.GoToState(this, Target.ToString(), false);
    }
}
