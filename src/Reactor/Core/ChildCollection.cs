using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Abstraction over a collection of UIElement children.
/// Allows the reconciler to work with any container type (Panel, ItemsControl, etc.).
/// </summary>
internal interface IChildCollection
{
    int Count { get; }
    UIElement Get(int index);
    void Insert(int index, UIElement element);
    void RemoveAt(int index);
    void Move(int oldIndex, int newIndex);
    void Replace(int index, UIElement element);
}

/// <summary>
/// Wraps Panel.Children (StackPanel, Grid, Canvas, etc.).
/// </summary>
internal sealed class PanelChildCollection : IChildCollection
{
    private readonly UIElementCollection _children;

    public PanelChildCollection(WinUI.Panel panel)
    {
        _children = panel.Children;
    }

    // Spec 047 §14 — the V1 descriptor Panel<> strategy resolves the live
    // collection via Func<TControl, UIElementCollection> rather than holding a
    // WinUI.Panel reference, so it constructs the wrapper from the collection
    // directly. Identical semantics to the panel-based ctor.
    public PanelChildCollection(UIElementCollection children)
    {
        _children = children;
    }

    public int Count => _children.Count;
    public UIElement Get(int index) => _children[index];
    public void Insert(int index, UIElement element) => _children.Insert(index, element);
    public void RemoveAt(int index) => _children.RemoveAt(index);

    public void Move(int oldIndex, int newIndex)
    {
        if (oldIndex == newIndex) return;
        Debug.Assert(oldIndex >= 0 && oldIndex < _children.Count, $"oldIndex {oldIndex} out of range [0, {_children.Count})");
        Debug.Assert(newIndex >= 0 && newIndex < _children.Count, $"newIndex {newIndex} out of range [0, {_children.Count})");
        var item = _children[oldIndex];
        _children.RemoveAt(oldIndex);
        // newIndex is the final desired position — no adjustment needed.
        // After RemoveAt, inserting at newIndex places the item at that index
        // in the resulting collection.
        _children.Insert(newIndex, item);
    }

    public void Replace(int index, UIElement element)
    {
        // Use explicit RemoveAt+Insert instead of indexer assignment.
        // WinUI's _children[i] = x doesn't always fully disconnect the old
        // element's internal parent state, causing COMException when the old
        // element is later reused from the pool.
        _children.RemoveAt(index);
        _children.Insert(index, element);
    }
}
