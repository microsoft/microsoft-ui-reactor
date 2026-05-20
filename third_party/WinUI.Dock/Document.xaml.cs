using System.ComponentModel;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml.Input;

namespace WinUI.Dock;

[ContentProperty(Name = nameof(Content))]
public partial class Document : DockModule
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title),
                                                                                          typeof(string),
                                                                                          typeof(Document),
                                                                                          new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(nameof(Content),
                                                                                            typeof(object),
                                                                                            typeof(Document),
                                                                                            new PropertyMetadata(null));

    public static readonly DependencyProperty CanPinProperty = DependencyProperty.Register(nameof(CanPin),
                                                                                           typeof(bool),
                                                                                           typeof(Document),
                                                                                           new PropertyMetadata(false));

    public static readonly DependencyProperty CanCloseProperty = DependencyProperty.Register(nameof(CanClose),
                                                                                             typeof(bool),
                                                                                             typeof(Document),
                                                                                             new PropertyMetadata(false));

    private static readonly DependencyProperty ActualTitleProperty = DependencyProperty.Register(nameof(ActualTitle),
                                                                                                 typeof(string),
                                                                                                 typeof(Document),
                                                                                                 new PropertyMetadata(string.Empty));

    public Document()
    {
        DefaultStyleKey = typeof(Document);

        AddHandler(PointerPressedEvent, (PointerEventHandler)((_, _) => Root?.ActiveDocument = this), true);

        ResetPreferredSide(null);
    }

    public new DocumentGroup? Owner
    {
        get => (DocumentGroup)GetValue(OwnerProperty);
        internal set => SetValue(OwnerProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public bool CanPin
    {
        get => (bool)GetValue(CanPinProperty);
        set => SetValue(CanPinProperty, value);
    }

    public bool CanClose
    {
        get => (bool)GetValue(CanCloseProperty);
        set => SetValue(CanCloseProperty, value);
    }

    public string ActualTitle
    {
        get => (string)GetValue(ActualTitleProperty);
        private set => SetValue(ActualTitleProperty, value);
    }

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public new double MinWidth
    {
        get => base.MinWidth;
        set => base.MinWidth = value;
    }

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public new double Width
    {
        get => base.Width;
        set => base.Width = value;
    }

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public new double MaxWidth
    {
        get => base.MaxWidth;
        set => base.MaxWidth = value;
    }

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public new double MinHeight
    {
        get => base.MinHeight;
        set => base.MinHeight = value;
    }

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public new double Height
    {
        get => base.Height;
        set => base.Height = value;
    }

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public new double MaxHeight
    {
        get => base.MaxHeight;
        set => base.MaxHeight = value;
    }

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public new Thickness Padding
    {
        get => base.Padding;
        set => base.Padding = value;
    }

    internal DockSide PreferredSide { get; set; }

    internal int PreferredSideIndex { get; set; }

    public void DockTo(Document dest, DockTarget target)
    {
        DockManager? manager = Root;

        switch (target)
        {
            case DockTarget.Center:
            case DockTarget.SplitLeft:
            case DockTarget.SplitTop:
            case DockTarget.SplitRight:
            case DockTarget.SplitBottom:
                if (dest.Owner is not null)
                {
                    Detach();

                    dest.Owner.Dock(this, target);
                }
                break;
            case DockTarget.DockLeft:
            case DockTarget.DockTop:
            case DockTarget.DockRight:
            case DockTarget.DockBottom:
                if (dest.Root is not null)
                {
                    Detach();

                    dest.Root.Dock(this, target);
                }
                break;
        }

        if (manager is not null)
        {
            FloatingWindowHelpers.CloseEmptyWindows(manager);
        }
    }

    internal override void SaveLayout(JsonObject writer)
    {
        writer.WriteByModuleType(this);
        writer.WriteDockModuleProperties(this);

        writer[nameof(Title)] = Title;
        writer[nameof(CanPin)] = CanPin;
        writer[nameof(CanClose)] = CanClose;
    }

    internal override void LoadLayout(JsonObject reader)
    {
        reader.ReadDockModuleProperties(this);

        Title = reader[nameof(Title)].Deserialize<string>();
        CanPin = reader[nameof(CanPin)].Deserialize<bool>();
        CanClose = reader[nameof(CanClose)].Deserialize<bool>();
    }

    internal void ResetPreferredSide(DockTarget? target)
    {
        PreferredSide = (DockSide)(PreferredSideIndex = -1);

        if (target.HasValue)
        {
            switch (target)
            {
                case DockTarget.DockLeft:
                    PreferredSide = DockSide.Left;
                    break;
                case DockTarget.DockTop:
                    PreferredSide = DockSide.Top;
                    break;
                case DockTarget.DockRight:
                    PreferredSide = DockSide.Right;
                    break;
                case DockTarget.DockBottom:
                    PreferredSide = DockSide.Bottom;
                    break;
            }
        }
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        Document document = (Document)d;

        document.ActualTitle = document.Title.Split("##").LastOrDefault() ?? document.Title;
    }
}
