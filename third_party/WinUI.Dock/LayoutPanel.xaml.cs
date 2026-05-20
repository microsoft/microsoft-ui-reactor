using System.Text.Json.Nodes;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinUI.Dock;

[TemplatePart(Name = "PART_Root", Type = typeof(Grid))]
public partial class LayoutPanel : DockContainer
{
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(nameof(Orientation),
                                                                                                typeof(Orientation),
                                                                                                typeof(LayoutPanel),
                                                                                                new PropertyMetadata(Orientation.Vertical));

    private Grid? root;

    public LayoutPanel()
    {
        DefaultStyleKey = typeof(LayoutPanel);
    }

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    protected override void InitTemplate()
    {
        root = GetTemplateChild("PART_Root") as Grid;
    }

    protected override void InitChildren()
    {
        if (root is null)
        {
            return;
        }

        root.Children.Clear();

        foreach (DockContainer container in Children.Cast<DockContainer>())
        {
            root.Children.Add(container);
        }

        UpdateLayoutStructure();
    }

    protected override void SynchronizeChildren(DockModule[] oldChildren,
                                                int oldStartingIndex,
                                                DockModule[] newChildren,
                                                int newStartingIndex)
    {
        if (root is null)
        {
            return;
        }

        foreach (DockContainer container in oldChildren.Cast<DockContainer>())
        {
            root.Children.Remove(container);
        }

        foreach (DockContainer container in newChildren.Cast<DockContainer>())
        {
            root.Children.Add(container);
        }

        UpdateLayoutStructure();
    }

    protected override bool ValidateChildren()
    {
        return Children.All(static item => item is DockContainer);
    }

    protected override bool ConfirmEmptyContainer()
    {
        return true;
    }

    internal double CalculateHeight(DockModule module)
    {
        if (double.IsNaN(module.Height))
        {
            return Math.Clamp(ActualHeight / (Children.Count + 1), module.MinHeight, module.MaxHeight);
        }

        return Math.Clamp(module.Height, module.MinHeight, module.MaxHeight);
    }

    internal double CalculateWidth(DockModule module)
    {
        if (double.IsNaN(module.Width))
        {
            return Math.Clamp(ActualWidth / (Children.Count + 1), module.MinWidth, module.MaxWidth);
        }

        return Math.Clamp(module.Width, module.MinWidth, module.MaxWidth);
    }

    internal override void SaveLayout(JsonObject writer)
    {
        writer.WriteByModuleType(this);
        writer.WriteDockModuleProperties(this);
        writer.WriteDockContainerChildren(this);

        writer[nameof(Orientation)] = (int)Orientation;
    }

    internal override void LoadLayout(JsonObject reader)
    {
        reader.ReadDockModuleProperties(this);
        reader.ReadDockContainerChildren(this);

        Orientation = (Orientation)reader[nameof(Orientation)].Deserialize<int>();
    }

    private void UpdateLayoutStructure()
    {
        if (root is null)
        {
            return;
        }

        root.RowDefinitions.Clear();
        root.ColumnDefinitions.Clear();
        foreach (UIElement element in root.Children.Where(static item => item is GridSplitter))
        {
            root.Children.Remove(element);
        }

        if (Orientation is Orientation.Vertical)
        {
            foreach (DockModule module in Children)
            {
                bool isNaN = double.IsNaN(module.Height);

                RowDefinition row = new()
                {
                    MinHeight = module.MinHeight,
                    MaxHeight = module.MaxHeight,
                    Height = isNaN ? new(1, GridUnitType.Star) : new(module.Height, GridUnitType.Pixel)
                };

                if (!isNaN)
                {
                    row.RegisterPropertyChangedCallback(RowDefinition.HeightProperty, (_, _) => module.Height = row.Height.Value);
                }

                root.RowDefinitions.Add(row);

                Grid.SetRow(module, root.RowDefinitions.Count - 1);
            }

            for (int i = 1; i < Children.Count; i++)
            {
                GridSplitter splitter = new()
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Top,
                    ResizeDirection = GridSplitter.GridResizeDirection.Rows,
                    RenderTransform = new TranslateTransform() { Y = -12 }
                };

                Grid.SetRow(splitter, i);

                root.Children.Add(splitter);
            }
        }
        else
        {
            foreach (DockModule module in Children)
            {
                bool isNaN = double.IsNaN(module.Width);

                ColumnDefinition column = new()
                {
                    MinWidth = module.MinWidth,
                    MaxWidth = module.MaxWidth,
                    Width = isNaN ? new(1, GridUnitType.Star) : new(module.Width, GridUnitType.Pixel)
                };

                if (!isNaN)
                {
                    column.RegisterPropertyChangedCallback(ColumnDefinition.WidthProperty, (_, _) => module.Width = column.Width.Value);
                }

                root.ColumnDefinitions.Add(column);

                Grid.SetColumn(module, root.ColumnDefinitions.Count - 1);
            }

            for (int i = 1; i < Children.Count; i++)
            {
                GridSplitter splitter = new()
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    ResizeDirection = GridSplitter.GridResizeDirection.Columns,
                    RenderTransform = new TranslateTransform() { X = -12 }
                };

                Grid.SetColumn(splitter, i);

                root.Children.Add(splitter);
            }
        }
    }
}
