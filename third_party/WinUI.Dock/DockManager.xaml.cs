using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WinUI.Dock;

public record ActiveDocumentChangedEventArgs(Document? OldDocument, Document? NewDocument);

[ContentProperty(Name = nameof(Panel))]
[TemplatePart(Name = "PART_PopupContainer", Type = typeof(Border))]
[TemplatePart(Name = "PART_Preview", Type = typeof(Preview))]
public partial class DockManager : Control
{
    public static readonly DependencyProperty AdapterProperty = DependencyProperty.Register(nameof(Adapter),
                                                                                            typeof(IDockAdapter),
                                                                                            typeof(DockManager),
                                                                                            new PropertyMetadata(null));

    public static readonly DependencyProperty BehaviorProperty = DependencyProperty.Register(nameof(Behavior),
                                                                                             typeof(IDockBehavior),
                                                                                             typeof(DockManager),
                                                                                             new PropertyMetadata(null));

    public static readonly DependencyProperty PanelProperty = DependencyProperty.Register(nameof(Panel),
                                                                                          typeof(LayoutPanel),
                                                                                          typeof(DockManager),
                                                                                          new PropertyMetadata(null, OnPanelChanged));

    public static readonly DependencyProperty ActiveDocumentProperty = DependencyProperty.Register(nameof(ActiveDocument),
                                                                                                   typeof(Document),
                                                                                                   typeof(DockManager),
                                                                                                   new PropertyMetadata(null, OnActiveDocumentChanged));

    private Preview? preview;

    public DockManager()
    {
        DefaultStyleKey = typeof(DockManager);

        Unloaded += (_, _) => FloatingWindowHelpers.CloseAllWindows(this);

        LeftSide.CollectionChanged += OnSideCollectionChanged;
        TopSide.CollectionChanged += OnSideCollectionChanged;
        RightSide.CollectionChanged += OnSideCollectionChanged;
        BottomSide.CollectionChanged += OnSideCollectionChanged;
    }

    public IDockAdapter? Adapter
    {
        get => (IDockAdapter)GetValue(AdapterProperty);
        set => SetValue(AdapterProperty, value);
    }

    public IDockBehavior? Behavior
    {
        get => (IDockBehavior)GetValue(BehaviorProperty);
        set => SetValue(BehaviorProperty, value);
    }

    public LayoutPanel? Panel
    {
        get => (LayoutPanel)GetValue(PanelProperty);
        set => SetValue(PanelProperty, value);
    }

    public Document? ActiveDocument
    {
        get => (Document)GetValue(ActiveDocumentProperty);
        set => SetValue(ActiveDocumentProperty, value);
    }

    public ObservableCollection<Document> LeftSide { get; } = [];

    public ObservableCollection<Document> TopSide { get; } = [];

    public ObservableCollection<Document> RightSide { get; } = [];

    public ObservableCollection<Document> BottomSide { get; } = [];

    internal Border? PopupContainer { get; private set; }

    public event EventHandler<ActiveDocumentChangedEventArgs>? ActiveDocumentChanged;

    public void ClearLayout()
    {
        Panel = null;
        LeftSide.Clear();
        TopSide.Clear();
        RightSide.Clear();
        BottomSide.Clear();

        FloatingWindowHelpers.CloseAllWindows(this);
    }

    public string SaveLayout()
    {
        JsonObject writer = [];

        if (ActiveDocument is not null)
        {
            writer[nameof(ActiveDocument)] = ActiveDocument.Path();
        }

        if (Panel is not null)
        {
            JsonObject panelWriter = [];
            Panel.SaveLayout(panelWriter);

            writer[nameof(Panel)] = panelWriter;
        }

        writer.WriteSideDocuments(nameof(LeftSide), LeftSide);
        writer.WriteSideDocuments(nameof(TopSide), TopSide);
        writer.WriteSideDocuments(nameof(RightSide), RightSide);
        writer.WriteSideDocuments(nameof(BottomSide), BottomSide);

        writer["Windows"] = new JsonArray([.. FloatingWindowHelpers.GetWindows(this).Select(static item =>
        {
            JsonObject itemWriter = [];
            item.SaveLayout(itemWriter);

            return itemWriter;
        })]);

        return writer.ToJsonString(LayoutHelpers.SerializerContext.Options);
    }

    public void LoadLayout(string layout)
    {
        if (string.IsNullOrEmpty(layout))
        {
            return;
        }

        ClearLayout();

        using JsonDocument document = JsonDocument.Parse(layout);

        JsonObject reader = JsonObject.Create(document.RootElement)!;

        string? activeDocumentPath = reader[nameof(ActiveDocument)]?.GetValue<string>();

        if (reader.ContainsKey(nameof(Panel)))
        {
            Panel = new();
            Panel.LoadLayout(reader[nameof(Panel)]!.AsObject());

            InvokeCreateNewDocument(Panel.Children);
        }

        reader.ReadSideDocuments(nameof(LeftSide), LeftSide);
        InvokeCreateNewDocument(LeftSide);

        reader.ReadSideDocuments(nameof(TopSide), TopSide);
        InvokeCreateNewDocument(TopSide);

        reader.ReadSideDocuments(nameof(RightSide), RightSide);
        InvokeCreateNewDocument(RightSide);

        reader.ReadSideDocuments(nameof(BottomSide), BottomSide);
        InvokeCreateNewDocument(BottomSide);

        foreach (JsonObject windowReader in reader["Windows"]!.AsArray().Cast<JsonObject>())
        {
            FloatingWindow window = new(this, null);
            window.LoadLayout(windowReader);
            window.Activate();

            InvokeCreateNewDocument(window.Panel.Children);
        }

        void InvokeCreateNewDocument(IEnumerable<DockModule> modules)
        {
            foreach (DockModule module in modules)
            {
                if (module is Document document)
                {
                    Adapter?.OnCreated(document);

                    if (document.Path() == activeDocumentPath)
                    {
                        ActiveDocument = document;
                    }
                }
                else if (module is DockContainer container)
                {
                    InvokeCreateNewDocument(container.Children);
                }
            }
        }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        PopupContainer = GetTemplateChild("PART_PopupContainer") as Border;

        preview = GetTemplateChild("PART_Preview") as Preview;
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);

        Behavior?.ActivateMainWindow();

        if (e.DataView.Contains(DragDropHelpers.DocumentKey))
        {
            VisualStateManager.GoToState(this, Panel is null || Panel.Children.Count is 0 ? "ShowAllDockTargets" : "ShowSideDockTargets", false);
        }
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);

        VisualStateManager.GoToState(this, "HideDockTargets", false);
    }

    internal void ShowDockPreview(Document document, DockTarget dockTarget)
    {
        switch (dockTarget)
        {
            case DockTarget.Center:
                preview?.Show(double.NaN,
                              double.NaN,
                              HorizontalAlignment.Stretch,
                              VerticalAlignment.Stretch);
                break;
            case DockTarget.DockLeft:
                preview?.Show(Panel!.CalculateWidth(document),
                              double.NaN,
                              HorizontalAlignment.Left,
                              VerticalAlignment.Stretch);
                break;
            case DockTarget.DockTop:
                preview?.Show(double.NaN,
                              Panel!.CalculateHeight(document),
                              HorizontalAlignment.Stretch,
                              VerticalAlignment.Top);
                break;
            case DockTarget.DockRight:
                preview?.Show(Panel!.CalculateWidth(document),
                              double.NaN,
                              HorizontalAlignment.Right,
                              VerticalAlignment.Stretch);
                break;
            case DockTarget.DockBottom:
                preview?.Show(double.NaN,
                              Panel!.CalculateHeight(document),
                              HorizontalAlignment.Stretch,
                              VerticalAlignment.Bottom);
                break;
        }
    }

    internal void HideDockPreview()
    {
        preview?.Hide();
    }

    internal void Dock(Document document, DockTarget target)
    {
        VisualStateManager.GoToState(this, "HideDockTargets", false);

        DocumentGroup group = new();
        group.Children.Add(document);

        Adapter?.OnCreated(group, document);

        LayoutPanel panel = new();
        panel.Children.Add(group);

        switch (target)
        {
            case DockTarget.DockLeft:
                {
                    panel.Orientation = Orientation.Horizontal;

                    if (Panel is not null)
                    {
                        panel.Children.Add(Panel);
                    }
                }
                break;
            case DockTarget.DockTop:
                {
                    panel.Orientation = Orientation.Vertical;

                    if (Panel is not null)
                    {
                        panel.Children.Add(Panel);
                    }
                }
                break;
            case DockTarget.DockRight:
                {
                    panel.Orientation = Orientation.Horizontal;

                    if (Panel is not null)
                    {
                        panel.Children.Insert(0, Panel);
                    }
                }
                break;
            case DockTarget.DockBottom:
                {
                    panel.Orientation = Orientation.Vertical;

                    if (Panel is not null)
                    {
                        panel.Children.Insert(0, Panel);
                    }
                }
                break;
        }

        Panel = panel;

        Behavior?.OnDocked(document, this, target);
    }

    internal void HideDockTargets()
    {
        VisualStateManager.GoToState(this, "HideDockTargets", false);
    }

    private void OnSideCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (Document document in e.OldItems)
            {
                document.Root = null;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (Document document in e.NewItems)
            {
                document.Root = this;
            }
        }
    }

    private static void OnPanelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LayoutPanel oldPanel)
        {
            oldPanel.Root = null;
        }

        if (e.NewValue is LayoutPanel newPanel)
        {
            newPanel.Root = (DockManager)d;
        }
    }

    private static void OnActiveDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        DockManager manager = (DockManager)d;

        manager.ActiveDocumentChanged?.Invoke(manager, new ActiveDocumentChangedEventArgs(e.OldValue as Document, e.NewValue as Document));
    }
}
