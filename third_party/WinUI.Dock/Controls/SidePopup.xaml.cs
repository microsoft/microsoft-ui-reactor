using System.ComponentModel;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace WinUI.Dock;

[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class SidePopup : UserControl
{
    private readonly Popup popup;

    private string documentKey = string.Empty;

    public SidePopup(Document document, DockManager manager, DockSide side)
    {
        InitializeComponent();

        Document = document;
        Manager = manager;
        Side = side;

        switch (Side)
        {
            case DockSide.Left:
                {
                    Width = double.IsNaN(Document.Width) ? Manager.PopupContainer!.ActualWidth / 3 : Document.Width;
                    Height = Manager.PopupContainer!.ActualHeight;

                    Layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
                    Layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Auto) });

                    Grid.SetColumn(Root, 0);
                    Grid.SetColumn(Sizer, 1);

                    Sizer.Orientation = Orientation.Vertical;
                    Sizer.IsDragInverted = false;
                }
                break;
            case DockSide.Top:
                {
                    Width = Manager.PopupContainer!.ActualWidth;
                    Height = double.IsNaN(Document.Height) ? Manager.PopupContainer!.ActualHeight / 3 : Document.Height;

                    Layout.RowDefinitions.Add(new RowDefinition { Height = new(1, GridUnitType.Star) });
                    Layout.RowDefinitions.Add(new RowDefinition { Height = new(1, GridUnitType.Auto) });

                    Grid.SetRow(Root, 0);
                    Grid.SetRow(Sizer, 1);

                    Sizer.Orientation = Orientation.Horizontal;
                    Sizer.IsDragInverted = false;
                }
                break;
            case DockSide.Right:
                {
                    Width = double.IsNaN(Document.Width) ? Manager.PopupContainer!.ActualWidth / 3 : Document.Width;
                    Height = Manager.PopupContainer!.ActualHeight;

                    Layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Auto) });
                    Layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });

                    Grid.SetColumn(Sizer, 0);
                    Grid.SetColumn(Root, 1);

                    Sizer.Orientation = Orientation.Vertical;
                    Sizer.IsDragInverted = true;
                }
                break;
            case DockSide.Bottom:
                {
                    Width = Manager.PopupContainer!.ActualWidth;
                    Height = double.IsNaN(Document.Height) ? Manager.PopupContainer!.ActualHeight / 3 : Document.Height;

                    Layout.RowDefinitions.Add(new RowDefinition { Height = new(1, GridUnitType.Auto) });
                    Layout.RowDefinitions.Add(new RowDefinition { Height = new(1, GridUnitType.Star) });

                    Grid.SetRow(Sizer, 0);
                    Grid.SetRow(Root, 1);

                    Sizer.Orientation = Orientation.Horizontal;
                    Sizer.IsDragInverted = true;
                }
                break;
        }

        popup = new()
        {
            Child = this,
            IsLightDismissEnabled = true,
            XamlRoot = Manager.PopupContainer!.XamlRoot
        };

        popup.Closed += (_, _) => Detach();
    }

    public Document? Document { get; private set; }

    public DockManager Manager { get; }

    public DockSide Side { get; }

    public void Show()
    {
        popup.IsOpen = true;

        Manager.ActiveDocument = Document;
        Manager.PopupContainer!.Child = popup;
    }

    private void OnSizeChanged(object _, SizeChangedEventArgs __)
    {
        if (Side is DockSide.Right)
        {
            popup.HorizontalOffset = Manager.PopupContainer!.ActualWidth - ActualWidth;
        }
        else if (Side is DockSide.Bottom)
        {
            popup.VerticalOffset = Manager.PopupContainer!.ActualHeight - ActualHeight;
        }
    }

    private void Header_DragStarting(UIElement _, DragStartingEventArgs args)
    {
        args.Data.SetData(DragDropHelpers.DocumentKey, documentKey = DragDropHelpers.GetDocumentKey(Document!));

        Detach(true);
    }

    private void Header_DropCompleted(UIElement _, DropCompletedEventArgs args)
    {
        if (DragDropHelpers.GetDocument(documentKey) is Document document)
        {
            if (args.DropResult is not DataPackageOperation.Move)
            {
                new FloatingWindow(Manager, document).Activate();
            }

            DragDropHelpers.RemoveDocumentKey(documentKey);
        }
    }

    private void Header_PointerPressed(object _, PointerRoutedEventArgs __)
    {
        Document!.Root!.ActiveDocument = Document;
    }

    private void Pin_Click(object _, RoutedEventArgs __)
    {
        Document document = Document!;

        document.PreferredSide = Side;
        document.PreferredSideIndex = Side switch
        {
            DockSide.Left => Manager.LeftSide.IndexOf(document),
            DockSide.Top => Manager.TopSide.IndexOf(document),
            DockSide.Right => Manager.RightSide.IndexOf(document),
            DockSide.Bottom => Manager.BottomSide.IndexOf(document),
            _ => throw new NotSupportedException()
        };

        Detach(true);

        Manager.Dock(document, Side switch
        {
            DockSide.Left => DockTarget.DockLeft,
            DockSide.Top => DockTarget.DockTop,
            DockSide.Right => DockTarget.DockRight,
            DockSide.Bottom => DockTarget.DockBottom,
            _ => throw new NotSupportedException()
        });

        Manager.ActiveDocument = document;
    }

    private void Close_Click(object _, RoutedEventArgs __)
    {
        Detach(true);
    }

    private void Detach(bool remove = false)
    {
        if (Document is null)
        {
            return;
        }

        if (remove)
        {
            switch (Side)
            {
                case DockSide.Left:
                    Manager.LeftSide.Remove(Document);
                    break;
                case DockSide.Top:
                    Manager.TopSide.Remove(Document);
                    break;
                case DockSide.Right:
                    Manager.RightSide.Remove(Document);
                    break;
                case DockSide.Bottom:
                    Manager.BottomSide.Remove(Document);
                    break;
            }
        }

        if (Side is DockSide.Left or DockSide.Right)
        {
            Document.Width = ActualWidth;
        }
        else
        {
            Document.Height = ActualHeight;
        }

        Document = null;

        Bindings.Update();

        popup.IsOpen = false;

        Manager.ActiveDocument = null;
        Manager.PopupContainer!.Child = null;
    }
}
