using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// Walks a WinUI visual tree rooted at <c>Window.Content</c> and assigns each element the
/// content-addressed node id the preview capture server's <c>/references</c> overlay uses.
/// Must be called on the UI dispatcher — <see cref="VisualTreeHelper.GetChild"/> requires it.
/// </summary>
internal sealed class TreeWalker
{
    /// <summary>
    /// Hard cap on visited elements per <c>Walk</c>. TASK-013: a runaway tree
    /// (e.g. a recursive ItemsControl template) would otherwise pin the UI
    /// dispatcher for seconds. When the cap is hit the walker stops descending.
    /// </summary>
    public const int MaxNodes = 5000;

    /// <summary>
    /// Hard cap on recursion depth. TASK-013: same rationale.
    /// </summary>
    public const int MaxDepth = 64;

    private readonly string _windowId;
    private readonly int _maxNodes;
    private readonly int _maxDepth;
    private int _visited;

    /// <summary>
    /// Spec 057 §11 Phase 3 (3.1) — every <see cref="FrameworkElement"/> visited by the
    /// last <see cref="Walk"/>, mapped to the node id assigned to it. The reference-edge
    /// overlay (<see cref="ReferenceOverlay"/>) keys its edges by these ids. Reference-keyed
    /// so two distinct controls that compare equal by value still map independently.
    /// </summary>
    private readonly Dictionary<FrameworkElement, string> _elementIds =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Walked elements in document order (spec 057 §11 Phase 3, 3.1 overlay).</summary>
    private readonly List<FrameworkElement> _walkedElements = new();

    /// <summary><see cref="FrameworkElement"/> → node-id map from the last <see cref="Walk"/>.</summary>
    public IReadOnlyDictionary<FrameworkElement, string> ElementIds => _elementIds;

    /// <summary>Framework elements visited by the last <see cref="Walk"/>, in document order.</summary>
    public IReadOnlyList<FrameworkElement> WalkedElements => _walkedElements;

    public TreeWalker(string windowId) : this(windowId, MaxNodes, MaxDepth)
    {
    }

    /// <summary>Test seam: a walker with smaller caps, so a selftest can prove they bound the walk.</summary>
    internal TreeWalker(string windowId, int maxNodes, int maxDepth)
    {
        _windowId = windowId;
        _maxNodes = maxNodes;
        _maxDepth = maxDepth;
    }

    /// <summary>Walks the subtree rooted at <paramref name="root"/>, replacing the previous walk's ids.</summary>
    public void Walk(UIElement? root)
    {
        _visited = 0;
        _elementIds.Clear();
        _walkedElements.Clear();
        if (root is null) return;
        WalkInto(root, parent: null, siblingIndex: 0, depth: 0);
    }

    private void WalkInto(UIElement element, NodeDescriptor? parent, int siblingIndex, int depth)
    {
        // SECURITY (TASK-013): cap node count and recursion depth so a hostile
        // template loop or deeply nested layout cannot freeze the UI dispatcher.
        if (_visited >= _maxNodes || depth >= _maxDepth) return;
        _visited++;

        var typeName = element.GetType().Name;
        var automationId = AutomationProperties.GetAutomationId(element);

        // The parent is threaded down as the ancestor chain. NodeIdBuilder anchors to the
        // nearest *stable* ancestor (one with an AutomationId or source location); if none
        // exists, it walks the full chain up to the root and includes each parent's
        // type+siblingIndex in the content-addressed id. Without this, two siblings of the
        // same type with the same parent-local index but different grandparents would
        // collide — e.g., two TextBoxes each as the 2nd child of their own StackPanel.
        var descriptor = new NodeDescriptor(
            WindowId: _windowId,
            ComponentName: parent?.ComponentName ?? typeName,
            AutomationId: string.IsNullOrEmpty(automationId) ? null : automationId,
            ReactorSource: null,
            TypeName: typeName,
            SiblingIndex: siblingIndex,
            StableAncestor: parent);

        if (element is FrameworkElement fe)
        {
            _elementIds[fe] = NodeIdBuilder.Build(descriptor);
            _walkedElements.Add(fe);
        }

        int childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < childCount; i++)
        {
            if (VisualTreeHelper.GetChild(element, i) is UIElement child)
                WalkInto(child, parent: descriptor, siblingIndex: i, depth: depth + 1);
        }
    }
}
