using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>One walked node. The preview capture server's <c>/references</c> overlay uses the ids.</summary>
internal sealed class TreeNode
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Name { get; set; }
    public string? AutomationId { get; set; }
    public string? AutomationName { get; set; }
    public BoundsBox Bounds { get; set; }
    public string? Text { get; set; }
    public bool IsVisible { get; set; }
    public string? ParentId { get; set; }
    public List<string> ChildIds { get; set; } = new();
    public object? Reactor { get; set; }
}

internal readonly record struct BoundsBox(double X, double Y, double Width, double Height);

/// <summary>
/// Walks a WinUI visual tree rooted at <c>Window.Content</c> and emits a flat
/// <see cref="TreeNode"/> array. Must be called on the UI dispatcher — <see cref="VisualTreeHelper.GetChild"/> requires it.
/// </summary>
internal sealed class TreeWalker
{
    /// <summary>
    /// Hard cap on emitted nodes per <c>Walk</c>. TASK-013: a runaway tree
    /// (e.g. a recursive ItemsControl template) would otherwise pin the UI
    /// dispatcher for seconds. When the cap is hit, <see cref="Truncated"/>
    /// becomes true and the walker stops descending.
    /// </summary>
    public const int MaxNodes = 5000;

    /// <summary>
    /// Hard cap on recursion depth. TASK-013: same rationale.
    /// </summary>
    public const int MaxDepth = 64;

    private readonly string _windowId;
    private readonly NodeRegistry _registry;

    /// <summary>
    /// Spec 057 §11 Phase 3 (3.1) — every <see cref="FrameworkElement"/> visited by the
    /// last <see cref="Walk"/>, mapped to the node id assigned to it. The reference-edge
    /// overlay (<see cref="ReferenceOverlay"/>) reuses these exact ids so its edges line up
    /// with the tree the inspector already rendered. Reference-keyed so two distinct
    /// controls that compare equal by value still map independently.
    /// </summary>
    private readonly Dictionary<FrameworkElement, string> _elementIds =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Walked elements in document order (spec 057 §11 Phase 3, 3.1 overlay).</summary>
    private readonly List<FrameworkElement> _walkedElements = new();

    /// <summary><see cref="FrameworkElement"/> → node-id map from the last <see cref="Walk"/>.</summary>
    public IReadOnlyDictionary<FrameworkElement, string> ElementIds => _elementIds;

    /// <summary>Framework elements visited by the last <see cref="Walk"/>, in document order.</summary>
    public IReadOnlyList<FrameworkElement> WalkedElements => _walkedElements;

    /// <summary>True when the last <see cref="Walk"/> hit a cap. Callers should
    /// surface this in their response so agents know the result is partial.</summary>
    public bool Truncated { get; private set; }

    public TreeWalker(string windowId, NodeRegistry registry)
    {
        _windowId = windowId;
        _registry = registry;
    }

    /// <summary>Walks the subtree rooted at <paramref name="root"/> and returns flat nodes.</summary>
    public List<TreeNode> Walk(UIElement? root)
    {
        var list = new List<TreeNode>();
        Truncated = false;
        _elementIds.Clear();
        _walkedElements.Clear();
        if (root is null) return list;
        WalkInto(root, parent: null, ancestor: null, siblingIndex: 0, list, depth: 0);
        return list;
    }

    private void WalkInto(
        UIElement element,
        NodeDescriptor? parent,
        NodeDescriptor? ancestor,
        int siblingIndex,
        List<TreeNode> sink,
        int depth)
    {
        // SECURITY (TASK-013): cap node count and recursion depth so a hostile
        // template loop or deeply nested layout cannot freeze the UI dispatcher.
        if (sink.Count >= MaxNodes || depth >= MaxDepth)
        {
            Truncated = true;
            return;
        }
        var typeName = element.GetType().Name;
        var automationId = AutomationProperties.GetAutomationId(element);
        var automationName = AutomationProperties.GetName(element);
        var elementName = (element as FrameworkElement)?.Name;
        var componentName = InferComponentName(element) ?? parent?.ComponentName ?? typeName;

        var descriptor = new NodeDescriptor(
            WindowId: _windowId,
            ComponentName: componentName,
            AutomationId: string.IsNullOrEmpty(automationId) ? null : automationId,
            ReactorSource: null,
            TypeName: typeName,
            SiblingIndex: siblingIndex,
            StableAncestor: ancestor);

        var id = _registry.GetOrCreate(descriptor, element);

        if (element is FrameworkElement walkedFe)
        {
            _elementIds[walkedFe] = id;
            _walkedElements.Add(walkedFe);
        }

        var node = new TreeNode
        {
            Id = id,
            Type = typeName,
            Name = string.IsNullOrEmpty(elementName) ? null : elementName,
            AutomationId = string.IsNullOrEmpty(automationId) ? null : automationId,
            AutomationName = string.IsNullOrEmpty(automationName) ? null : automationName,
            Bounds = ReadBounds(element),
            Text = ExtractText(element),
            IsVisible = element.Visibility == Visibility.Visible,
            ParentId = parent is null ? null : NodeIdBuilder.Build(parent),
        };

        sink.Add(node);

        // Always thread the immediate parent through as the descriptor's ancestor
        // chain. NodeIdBuilder anchors to the nearest *stable* ancestor (one with
        // an AutomationId or source location); if none exists, it walks the full
        // chain up to the root and includes each parent's type+siblingIndex in
        // the content-addressed id. Without this, two siblings of the same type
        // with the same parent-local index but different grandparents would
        // collide — e.g., two TextBoxes each as the 2nd child of their own
        // StackPanel got identical ids before the fix.
        var nextAncestor = descriptor;

        int childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < childCount; i++)
        {
            if (VisualTreeHelper.GetChild(element, i) is UIElement child)
            {
                WalkInto(child, parent: descriptor, ancestor: nextAncestor, siblingIndex: i, sink, depth: depth + 1);
                // Backfill the parent's childIds once we know the child id.
                var childDesc = new NodeDescriptor(
                    WindowId: _windowId,
                    ComponentName: InferComponentName(child) ?? componentName,
                    AutomationId: string.IsNullOrEmpty(AutomationProperties.GetAutomationId(child))
                        ? null
                        : AutomationProperties.GetAutomationId(child),
                    ReactorSource: null,
                    TypeName: child.GetType().Name,
                    SiblingIndex: i,
                    StableAncestor: nextAncestor);
                node.ChildIds.Add(NodeIdBuilder.Build(childDesc));
            }
        }
    }

    private static BoundsBox ReadBounds(UIElement element)
    {
        try
        {
            if (element is FrameworkElement fe)
                return new BoundsBox(0, 0, fe.ActualWidth, fe.ActualHeight);
        }
        catch { }
        return new BoundsBox(0, 0, 0, 0);
    }

    private static string? ExtractText(UIElement element) => element switch
    {
        TextBlock tb => tb.Text,
        TextBox tx => tx.Text,
        Button b => b.Content?.ToString(),
        ContentControl cc => cc.Content?.ToString(),
        _ => null,
    };

    /// <summary>
    /// Best-effort component inference. The root component instance is threaded
    /// into a window tag by the host (Phase 2.8+ wiring); without that tag the
    /// walker falls back to the element's type name so ids still work.
    /// </summary>
    private static string? InferComponentName(UIElement element)
    {
        // Reserved hook for source-map integration (Phase 3 §3.2). For now the
        // root component name is supplied by the caller through the initial
        // descriptor chain; nested components get their own name once the source
        // mapper lands.
        _ = element;
        return null;
    }
}

