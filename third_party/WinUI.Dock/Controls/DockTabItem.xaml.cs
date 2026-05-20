using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace WinUI.Dock;

[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class DockTabItem : TabViewItem
{
    private string managerKey = string.Empty;
    private string documentKey = string.Empty;

    public DockTabItem(TabView root, Document document)
    {
        InitializeComponent();

        SetBinding(BorderBrushProperty, new Binding() { Source = root, Path = new(nameof(BorderBrush)) });

        Document = document;

        ActiveIndicator.SetBinding(VisibilityProperty, new Binding
        {
            Source = Document.Root,
            Path = new(nameof(DockManager.ActiveDocument)),
            Converter = new ActiveDocumentToVisibilityConverter(),
            ConverterParameter = Document
        });
    }

    public Document? Document { get; private set; }

    public double TabWidth { get => Tab.Width; set => Tab.Width = value; }

    public double TabMaxWidth { get => Tab.MaxWidth; set => Tab.MaxWidth = value; }

    public void UpdateVisualState(TabPosition tabPosition)
    {
        bool isBottom = tabPosition is TabPosition.Bottom;

        double scale = isBottom ? -1.0 : 1.0;

        HeaderScale.ScaleY = scale;
        ContentScale.ScaleY = scale;

        HeaderOptions.Visibility = isBottom ? Visibility.Collapsed : Visibility.Visible;
        ContentOptions.Visibility = isBottom ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Detach()
    {
        Document = null;

        Bindings.Update();
    }

    protected override void OnPointerEntered(PointerRoutedEventArgs e)
    {
        base.OnPointerEntered(e);

        HeaderOptions.Opacity = 1.0;
    }

    protected override void OnPointerExited(PointerRoutedEventArgs e)
    {
        base.OnPointerExited(e);

        HeaderOptions.Opacity = 0.0;
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);

        Document!.Root!.ActiveDocument = Document;
    }

    private void OnDragStarting(UIElement _, DragStartingEventArgs args)
    {
        args.Data.SetData(DragDropHelpers.ManagerKey, managerKey = DragDropHelpers.GetManagerKey(Document!.Root!));
        args.Data.SetData(DragDropHelpers.DocumentKey, documentKey = DragDropHelpers.GetDocumentKey(Document!));

        Document.Detach();
    }

    private void OnDropCompleted(UIElement _, DropCompletedEventArgs args)
    {
        if (DragDropHelpers.GetManager(managerKey) is DockManager manager && DragDropHelpers.GetDocument(documentKey) is Document document)
        {
            // In multi-window drag-and-drop operations, if the original window closes prematurely,
            // it may lead to incorrect handling of the drop result.
            FloatingWindowHelpers.CloseEmptyWindows(manager);

            if (args.DropResult is not DataPackageOperation.Move)
            {
                new FloatingWindow(manager, document).Activate();
            }

            DragDropHelpers.RemoveManagerKey(managerKey);
            DragDropHelpers.RemoveDocumentKey(documentKey);
        }
    }

    private void ContentOptions_PointerPressed(object _, PointerRoutedEventArgs __)
    {
        Document!.Root!.ActiveDocument = Document;
    }

    private void Pin_Click(object _, RoutedEventArgs __)
    {
        DockManager manager = Document!.Root!;

        switch (Document.PreferredSide)
        {
            case DockSide.Left:
                TryInsert(manager.LeftSide, Document);
                break;
            case DockSide.Top:
                TryInsert(manager.TopSide, Document);
                break;
            case DockSide.Right:
                TryInsert(manager.RightSide, Document);
                break;
            case DockSide.Bottom:
                TryInsert(manager.BottomSide, Document);
                break;
            default:
                {
                    Point point = TransformToVisual(manager).TransformPoint(new Point(0, 0));

                    double left = point.X;
                    double top = point.Y;
                    double right = manager.ActualWidth - point.X;
                    double bottom = manager.ActualHeight - point.Y;

                    if (Document.ActualWidth < Document.ActualHeight)
                    {
                        if (left < right)
                        {
                            manager.LeftSide.Add(Document);
                        }
                        else
                        {
                            manager.RightSide.Add(Document);
                        }
                    }
                    else if (top < bottom)
                    {
                        manager.TopSide.Add(Document);
                    }
                    else
                    {
                        manager.BottomSide.Add(Document);
                    }
                }
                break;
        }

        Document.Detach();

        // Reference: Comments on lines 91-92.
        FloatingWindowHelpers.CloseEmptyWindows(manager);

        static void TryInsert(ObservableCollection<Document> documents, Document document)
        {
            if (document.PreferredSideIndex is not -1 && documents.FirstOrDefault(item => item.PreferredSideIndex > document.PreferredSideIndex) is Document existingDocument)
            {
                documents.Insert(documents.IndexOf(existingDocument), document);
            }
            else
            {
                documents.Add(document);
            }
        }
    }

    private void Close_Click(object _, RoutedEventArgs __)
    {
        DocumentGroup group = Document!.Owner!;
        DockManager manager = Document.Root!;
        bool wasActive = manager.ActiveDocument == Document;

        Document.Detach();

        if (wasActive)
        {
            manager.ActiveDocument = group.SelectedIndex is not -1 ? (Document)group.Children[group.SelectedIndex] : null;
        }

        // Reference: Comments on lines 91-92.
        FloatingWindowHelpers.CloseEmptyWindows(manager);
    }
}
