using System.ComponentModel;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace WinUI.Dock;

public abstract partial class DockModule : Control
{
    public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register(nameof(Owner),
                                                                                          typeof(DockModule),
                                                                                          typeof(DockModule),
                                                                                          new PropertyMetadata(null));

    public static readonly DependencyProperty RootProperty = DependencyProperty.Register(nameof(Root),
                                                                                         typeof(DockManager),
                                                                                         typeof(DockModule),
                                                                                         new PropertyMetadata(null, (d, e) => ((DockModule)d).OnRootChanged((DockManager?)e.OldValue, (DockManager?)e.NewValue)));

    public DockModule? Owner
    {
        get => (DockModule)GetValue(OwnerProperty);
        internal set => SetValue(OwnerProperty, value);
    }

    public DockManager? Root
    {
        get => (DockManager)GetValue(RootProperty);
        internal set => SetValue(RootProperty, value);
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Style Style
    {
        get => base.Style;
        set => base.Style = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new ControlTemplate Template
    {
        get => base.Template;
        set => base.Template = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new double MinWidth
    {
        get => base.MinWidth;
        set => base.MinWidth = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new double Width
    {
        get => base.Width;
        set => base.Width = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new double MaxWidth
    {
        get => base.MaxWidth;
        set => base.MaxWidth = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new double MinHeight
    {
        get => base.MinHeight;
        set => base.MinHeight = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new double Height
    {
        get => base.Height;
        set => base.Height = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new double MaxHeight
    {
        get => base.MaxHeight;
        set => base.MaxHeight = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Thickness Margin
    {
        get => base.Margin;
        set => base.Margin = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Thickness Padding
    {
        get => base.Padding;
        set => base.Padding = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Thickness BorderThickness
    {
        get => base.BorderThickness;
        set => base.BorderThickness = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new CornerRadius CornerRadius
    {
        get => base.CornerRadius;
        set => base.CornerRadius = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new BackgroundSizing BackgroundSizing
    {
        get => base.BackgroundSizing;
        set => base.BackgroundSizing = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new FontFamily FontFamily
    {
        get => base.FontFamily;
        set => base.FontFamily = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new double FontSize
    {
        get => base.FontSize;
        set => base.FontSize = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new FontWeight FontWeight
    {
        get => base.FontWeight;
        set => base.FontWeight = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new FontStyle FontStyle
    {
        get => base.FontStyle;
        set => base.FontStyle = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new FontStretch FontStretch
    {
        get => base.FontStretch;
        set => base.FontStretch = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Brush Foreground
    {
        get => base.Foreground;
        set => base.Foreground = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Brush Background
    {
        get => base.Background;
        set => base.Background = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Brush BorderBrush
    {
        get => base.BorderBrush;
        set => base.BorderBrush = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new HorizontalAlignment HorizontalAlignment
    {
        get => base.HorizontalAlignment;
        set => base.HorizontalAlignment = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new VerticalAlignment VerticalAlignment
    {
        get => base.VerticalAlignment;
        set => base.VerticalAlignment = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new HorizontalAlignment HorizontalContentAlignment
    {
        get => base.HorizontalContentAlignment;
        set => base.HorizontalContentAlignment = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new VerticalAlignment VerticalContentAlignment
    {
        get => base.VerticalContentAlignment;
        set => base.VerticalContentAlignment = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new FlowDirection FlowDirection
    {
        get => base.FlowDirection;
        set => base.FlowDirection = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Visibility Visibility
    {
        get => base.Visibility;
        set => base.Visibility = value;
    }

    internal void Attach(DockModule owner)
    {
        if (Owner == owner)
        {
            return;
        }

        Detach();

        Owner = owner;
        Root = owner.Root;
    }

    internal void Detach(bool detachEmptyContainer = true)
    {
        if (Owner is DockContainer container)
        {
            container.Children.Remove(this);

            if (detachEmptyContainer)
            {
                container.DetachEmptyContainer();
            }
        }

        Owner = null;
        Root = null;
    }

    internal void CopyDimensions(DockModule source)
    {
        MinWidth = source.MinWidth;
        Width = source.Width;
        MaxWidth = source.MaxWidth;

        MinHeight = source.MinHeight;
        Height = source.Height;
        MaxHeight = source.MaxHeight;

        if (this is DockContainer container)
        {
            foreach (DockModule children in container.Children)
            {
                children.ClearDimensions();
            }
        }
    }

    internal void ClearDimensions()
    {
        MinWidth = 0;
        Width = double.NaN;
        MaxWidth = double.PositiveInfinity;

        MinHeight = 0;
        Height = double.NaN;
        MaxHeight = double.PositiveInfinity;

        if (this is DockContainer container)
        {
            foreach (DockModule children in container.Children)
            {
                children.ClearDimensions();
            }
        }
    }

    internal abstract void SaveLayout(JsonObject writer);

    internal abstract void LoadLayout(JsonObject reader);

    protected virtual void OnRootChanged(DockManager? oldRoot, DockManager? newRoot)
    {
    }
}
