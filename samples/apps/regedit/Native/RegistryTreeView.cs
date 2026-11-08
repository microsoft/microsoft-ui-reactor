using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ReactorRegedit.Core;
using ReactorRegedit.Services;
using Windows.System;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace ReactorRegedit.Native;

internal enum TreeNodeKind { Computer, Machine, Hive, Key }

/// <summary>One node of the registry tree (a row when visible).</summary>
internal sealed class TreeNode(string name, string path, TreeNodeKind kind, TreeNode? parent)
{
    public string Name { get; set; } = name;
    public string Path { get; set; } = path;
    public TreeNodeKind Kind { get; } = kind;
    public TreeNode? Parent { get; } = parent;
    public int Depth { get; } = parent is null || kind == TreeNodeKind.Machine ? 0 : parent.Depth + 1;
    public List<TreeNode>? Children { get; set; }
    public bool HasChildren { get; set; } = true;
    public bool IsExpanded { get; set; }
    public override string ToString() => Name;
}

/// <summary>Callbacks the tree raises into the Reactor shell.</summary>
internal sealed class TreeHandlers
{
    public Action<string>? SelectionChanged { get; init; }
    public Action<TreeNode>? DeleteRequested { get; init; }
    /// <summary>Performs a rename; returns the new path or null on failure (error already shown).</summary>
    public Func<string, string, Task<string?>>? RenameRequested { get; init; }
    public Func<TreeNode, IEnumerable<MenuSpec>>? BuildContextMenu { get; init; }
    public Action<string>? Error { get; init; }
    public Action? FocusEntered { get; init; }
}

/// <summary>
/// The key tree: a flattened, virtualized ListView of visible nodes that behaves like
/// a tree (indentation, chevrons, lazy loading, TreeItem automation peers). Owning the
/// rows directly gives compact 22 epx rows, exact keyboard behaviour, instant expand
/// without "Loading…" placeholders and programmatic reveal/scroll.
/// </summary>
internal sealed partial class RegistryTreeView : Grid
{
    public const double RowHeight = 22;
    private const double Indent = 14;

    private readonly TreeListView _list;
    private readonly RowCollection<TreeNode> _rows = [];
    private readonly TreeNode _computer;
    // Invisible parent of remote machine roots (F19), which show as top-level siblings of Computer.
    private readonly TreeNode _network = new("", "", TreeNodeKind.Computer, null) { IsExpanded = true, Children = [] };
    private readonly DispatcherQueue _dispatcher;
    private int _suppressSelection;
    private string _typeAhead = "";
    private long _typeAheadTicks;
    private TreeNode? _renaming;

    public TreeHandlers Handlers { get; set; } = new();

    public RegistryTreeView()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _computer = new TreeNode(RegPath.ComputerLabel, "", TreeNodeKind.Computer, null) { IsExpanded = true };
        _computer.Children = RegPath.RootNames
            .Select(h => new TreeNode(h, h, TreeNodeKind.Hive, _computer))
            .ToList();

        _list = new TreeListView(this)
        {
            SelectionMode = ListViewSelectionMode.Single,
            ItemsSource = _rows,
            ItemTemplate = ListHelpers.EmptyGridTemplate(),
            ItemContainerStyle = ListHelpers.CompactItemStyle(RowHeight),
            IsItemClickEnabled = false,
            SingleSelectionFollowsFocus = true,
            Padding = new Thickness(0, 2, 0, 2),
        };
        AutomationProperties.SetName(_list, "Registry keys");
        ListHelpers.ApplySelectionBrushes(_list);
        _list.ContainerContentChanging += OnContainerContentChanging;
        _list.SelectionChanged += OnSelectionChanged;
        _list.PreviewKeyDown += OnPreviewKeyDown;
        _list.CharacterReceived += OnCharacterReceived;
        _list.ContextRequested += OnContextRequested;
        _list.DoubleTapped += OnDoubleTapped;
        _list.GotFocus += (_, _) => Handlers.FocusEntered?.Invoke();
        Children.Add(_list);

        SyncRows();
        _list.SelectedItem = _computer;
    }

    public TreeNode? SelectedNode => _list.SelectedItem as TreeNode;
    public string SelectedPath => SelectedNode?.Path ?? "";

    public void FocusTree()
    {
        if (SelectedNode is { } n && _list.ContainerFromItem(n) is ListViewItem c)
            c.Focus(FocusState.Programmatic);
        else
            _list.Focus(FocusState.Programmatic);
    }

    public bool HasFocusWithin()
    {
        if (XamlRoot is null) return false;
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        return ListHelpers.FindAncestor<RegistryTreeView>(focused) == this;
    }

    // ── Row visuals ──────────────────────────────────────────────────

    private sealed class RowParts
    {
        public required FontIcon Chevron;
        public required Border ChevronHit;
        public required FrameworkElement FolderClosed, FolderOpen, Hive, Computer;
        public required TextBlock Text;
        public TreeNode? Node;
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        var node = (TreeNode)args.Item;
        var root = (Grid)args.ItemContainer.ContentTemplateRoot;
        if (root.Tag is not RowParts parts)
        {
            parts = BuildRow(root);
            root.Tag = parts;
        }
        parts.Node = node;
        if (args.ItemContainer is TreeRowItem tri) tri.Node = node;

        root.Padding = new Thickness(node.Depth * Indent, 0, 0, 0);
        var showChevron = node.Kind != TreeNodeKind.Computer && node.HasChildren;
        parts.Chevron.Glyph = node.IsExpanded ? "\uE70D" : "\uE76C";
        parts.ChevronHit.Visibility = showChevron ? Visibility.Visible : Visibility.Collapsed;
        parts.FolderClosed.Visibility = node.Kind == TreeNodeKind.Key && !node.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        parts.FolderOpen.Visibility = node.Kind == TreeNodeKind.Key && node.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        parts.Hive.Visibility = node.Kind == TreeNodeKind.Hive ? Visibility.Visible : Visibility.Collapsed;
        parts.Computer.Visibility = node.Kind is TreeNodeKind.Computer or TreeNodeKind.Machine ? Visibility.Visible : Visibility.Collapsed;
        parts.Text.Text = node.Name;

        AutomationProperties.SetName(args.ItemContainer, node.Name);
        AutomationProperties.SetLevel(args.ItemContainer, node.Depth + 1);
        var siblings = node.Parent?.Children;
        if (siblings is not null)
        {
            AutomationProperties.SetPositionInSet(args.ItemContainer, siblings.IndexOf(node) + 1);
            AutomationProperties.SetSizeOfSet(args.ItemContainer, siblings.Count);
        }
        args.Handled = true;
    }

    private RowParts BuildRow(Grid root)
    {
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var chevron = new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 9,
            Glyph = "\uE76C",
            Opacity = 0.75,
        };
        var hit = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Child = chevron,
            Width = 16,
            Height = RowHeight,
        };
        hit.PointerPressed += (s, e) =>
        {
            e.Handled = true;
            if (((FrameworkElement)s).Tag is RowParts p && p.Node is { } n) Toggle(n);
        };
        hit.PointerReleased += (_, e) => e.Handled = true;
        hit.DoubleTapped += (_, e) => e.Handled = true;
        hit.Tapped += (_, e) => e.Handled = true;
        root.Children.Add(hit);

        var icons = new Grid { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        var closed = Icons.Folder(false);
        var open = Icons.Folder(true);
        var hive = Icons.Hive();
        var computer = Icons.Computer();
        icons.Children.Add(closed);
        icons.Children.Add(open);
        icons.Children.Add(hive);
        icons.Children.Add(computer);
        Grid.SetColumn(icons, 1);
        root.Children.Add(icons);

        var text = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            FontSize = 13,
        };
        Grid.SetColumn(text, 2);
        root.Children.Add(text);

        var parts = new RowParts
        {
            Chevron = chevron, ChevronHit = hit, FolderClosed = closed, FolderOpen = open,
            Hive = hive, Computer = computer, Text = text,
        };
        hit.Tag = parts;
        return parts;
    }

    private void RefreshRowVisual(TreeNode node)
    {
        if (_list.ContainerFromItem(node) is ListViewItem c &&
            c.ContentTemplateRoot is Grid { Tag: RowParts parts })
        {
            parts.Chevron.Glyph = node.IsExpanded ? "\uE70D" : "\uE76C";
            parts.ChevronHit.Visibility = node.Kind != TreeNodeKind.Computer && node.HasChildren ? Visibility.Visible : Visibility.Collapsed;
            parts.FolderClosed.Visibility = node.Kind == TreeNodeKind.Key && !node.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            parts.FolderOpen.Visibility = node.Kind == TreeNodeKind.Key && node.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            parts.Text.Text = node.Name;
            var oldName = AutomationProperties.GetName(c);
            if (oldName != node.Name)
            {
                AutomationProperties.SetName(c, node.Name);
                FrameworkElementAutomationPeer.FromElement(c)?.RaisePropertyChangedEvent(
                    AutomationElementIdentifiers.NameProperty, oldName, node.Name);
            }
        }
    }

    // ── Structure ────────────────────────────────────────────────────

    private void SyncRows()
    {
        var visible = new List<TreeNode>(_rows.Count + 16);
        void Walk(TreeNode n)
        {
            visible.Add(n);
            if (n.IsExpanded && n.Children is { } kids)
                foreach (var k in kids) Walk(k);
        }
        Walk(_computer);
        foreach (var machine in _network.Children!) Walk(machine);

        var selected = SelectedNode;
        var sv = ListHelpers.FindScrollViewer(_list);
        var offset = sv?.VerticalOffset ?? 0;
        _suppressSelection++;
        bool reset;
        try
        {
            reset = _rows.SyncTo(visible);
            if (selected is not null && visible.Contains(selected) && !ReferenceEquals(_list.SelectedItem, selected))
                _list.SelectedItem = selected;
        }
        finally { _suppressSelection--; }

        if (reset && sv is not null)
        {
            var restore = offset;
            _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => sv.ChangeView(null, restore, null, true));
        }

        // The selected node may have been hidden by a collapse: move selection to the
        // nearest visible ancestor, like a native tree.
        if (selected is not null && !visible.Contains(selected))
        {
            var anc = selected.Parent;
            while (anc is not null && !visible.Contains(anc)) anc = anc.Parent;
            _list.SelectedItem = anc ?? _computer;
        }
    }

    /// <summary>Adds a connected remote computer as a top-level root with its hives (F19).</summary>
    public void AddMachine(string machine, IEnumerable<string> hives)
    {
        var path = @"\\" + machine;
        if (_network.Children!.Any(m => m.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
        var node = new TreeNode(machine, path, TreeNodeKind.Machine, _network) { IsExpanded = true };
        node.Children = hives.Select(h => new TreeNode(h, path + "\\" + h, TreeNodeKind.Hive, node)).ToList();
        node.HasChildren = node.Children.Count > 0;
        _network.Children!.Add(node);
        SyncRows();
    }

    /// <summary>Removes a remote computer root; a selection inside it moves to Computer.</summary>
    public void RemoveMachine(string machine)
    {
        var node = _network.Children!.FirstOrDefault(m => m.Path.Equals(@"\\" + machine, StringComparison.OrdinalIgnoreCase));
        if (node is null) return;
        var selectedInside = SelectedNode is { } s && RegPath.IsSameOrDescendant(s.Path, node.Path) && RegPath.IsRemote(s.Path);
        _network.Children!.Remove(node);
        SyncRows();
        if (selectedInside) _list.SelectedItem = _computer;
    }

    private async Task<bool> LoadChildrenAsync(TreeNode node, bool reportErrors = true)
    {
        if (node.Kind is TreeNodeKind.Computer or TreeNodeKind.Machine) return true;
        try
        {
            var path = node.Path;
            var entries = await Task.Run(() => RegistryService.GetSubKeys(path));
            MergeChildren(node, entries);
            return true;
        }
        catch (RegistryOperationException ex)
        {
            node.Children = [];
            node.HasChildren = false;
            node.IsExpanded = false;
            if (reportErrors && ex.IsNotFound && node.Kind == TreeNodeKind.Key) await HandleVanishedAsync(node.Path);
            else if (reportErrors) Handlers.Error?.Invoke(ex.Message);
            return false;
        }
    }

    private static void MergeChildren(TreeNode node, KeyEntry[] entries)
    {
        var old = node.Children?.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase)
                  ?? new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
        var list = new List<TreeNode>(entries.Length);
        foreach (var e in entries)
        {
            if (old.TryGetValue(e.Name, out var existing) && existing.Name == e.Name)
            {
                existing.HasChildren = e.HasChildren || (existing.Children?.Count > 0);
                if (!e.HasChildren) { existing.IsExpanded = false; existing.Children = null; existing.HasChildren = false; }
                list.Add(existing);
            }
            else
            {
                list.Add(new TreeNode(e.Name, e.Path, TreeNodeKind.Key, node) { HasChildren = e.HasChildren });
            }
        }
        node.Children = list;
        node.HasChildren = list.Count > 0;
        if (list.Count == 0) node.IsExpanded = false;
    }

    public async void Toggle(TreeNode node)
    {
        if (node.IsExpanded) Collapse(node);
        else await ExpandAsync(node);
    }

    public async Task ExpandAsync(TreeNode node)
    {
        if (node.Kind == TreeNodeKind.Computer || !node.HasChildren || node.IsExpanded) return;
        if (!await LoadChildrenAsync(node))
        {
            RefreshRowVisual(node);
            return;
        }
        node.IsExpanded = node.HasChildren;
        RefreshRowVisual(node);
        SyncRows();
    }

    public void Collapse(TreeNode node)
    {
        if (node.Kind == TreeNodeKind.Computer || !node.IsExpanded) return;
        node.IsExpanded = false;
        RefreshRowVisual(node);
        SyncRows();
    }

    private TreeNode? FindLoaded(string path)
    {
        if (path.Length == 0) return _computer;
        TreeNode cur = RegPath.IsRemote(path) ? _network : _computer;
        foreach (var seg in RegPath.AncestorsAndSelf(path))
        {
            var next = cur.Children?.FirstOrDefault(c => c.Path.Equals(seg, StringComparison.OrdinalIgnoreCase));
            if (next is null) return null;
            cur = next;
        }
        return cur;
    }

    /// <summary>
    /// Expands every ancestor of <paramref name="path"/>, selects it and scrolls it into
    /// view. Returns false (and leaves the selection alone) when the key isn't found.
    /// </summary>
    public async Task<bool> RevealAsync(string path, bool focus = false)
    {
        TreeNode cur = RegPath.IsRemote(path) ? _network : _computer;
        foreach (var seg in RegPath.AncestorsAndSelf(path))
        {
            if (cur.Kind != TreeNodeKind.Computer && cur.Children is null && !await LoadChildrenAsync(cur))
            {
                SyncRows();
                return false;
            }
            var next = FindChild(cur, seg);
            if (next is null && cur.Kind != TreeNodeKind.Computer)
            {
                // Possibly created since we enumerated: reload once.
                await LoadChildrenAsync(cur);
                next = FindChild(cur, seg);
            }
            if (next is null)
            {
                SyncRows();
                return false;
            }
            if (cur.Kind != TreeNodeKind.Computer && !cur.IsExpanded)
            {
                cur.HasChildren = true;
                cur.IsExpanded = true;
                RefreshRowVisual(cur);
            }
            cur = next;
        }
        SyncRows();
        _list.SelectedItem = cur;
        ScrollToCenter(cur);
        if (focus) _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, FocusTree);
        return true;
    }

    private void ScrollToCenter(TreeNode node) => ListHelpers.ScrollRowIntoView(_list, node, RowHeight);

    /// <summary>
    /// A key was found to be gone (deleted outside the app): report it, drop it (and any
    /// vanished ancestors) from the tree and select the nearest key that still exists.
    /// </summary>
    public async Task HandleVanishedAsync(string path)
    {
        Handlers.Error?.Invoke($"Cannot find the key {RegPath.ToDisplay(path)}. It may have been deleted.");
        string ancestorPath = RegPath.Parent(path);
        while (ancestorPath.Length > 0 && !RegPath.IsHive(ancestorPath) &&
               !await Task.Run(() => RegistryService.KeyExists(ancestorPath)))
            ancestorPath = RegPath.Parent(ancestorPath);
        var anchor = FindLoaded(ancestorPath) ?? _computer;
        if (anchor.Kind != TreeNodeKind.Computer) await LoadChildrenAsync(anchor, reportErrors: false);
        RefreshRowVisual(anchor);
        SyncRows();
        if (!ReferenceEquals(_list.SelectedItem, anchor)) Select(anchor);
        else Handlers.SelectionChanged?.Invoke(anchor.Path);
    }

    private static TreeNode? FindChild(TreeNode parent, string path) =>
        parent.Children?.FirstOrDefault(c => c.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    /// <summary>
    /// Re-enumerates the children of <paramref name="path"/> (if they were loaded) and
    /// updates its chevron. Used after create/delete/rename/import.
    /// </summary>
    public async Task RefreshKeyAsync(string path)
    {
        var node = FindLoaded(path);
        if (node is null) return;
        if (node.Kind == TreeNodeKind.Computer) return;
        if (node.Children is not null || node.IsExpanded)
        {
            await LoadChildrenAsync(node, reportErrors: false);
        }
        else
        {
            var p = node.Path;
            node.HasChildren = await Task.Run(() => RegistryService.HasSubKeys(p));
        }
        RefreshRowVisual(node);
        SyncRows();
    }

    /// <summary>F5: re-enumerates every expanded node; vanished keys disappear.</summary>
    public async Task RefreshAllAsync()
    {
        var selectedBefore = SelectedNode;
        var expanded = new List<TreeNode>();
        void Walk(TreeNode n)
        {
            if (n.IsExpanded || n.Kind == TreeNodeKind.Computer)
            {
                if (n.Kind != TreeNodeKind.Computer) expanded.Add(n);
                if (n.Children is { } kids) foreach (var k in kids) Walk(k);
            }
        }
        Walk(_computer);
        Walk(_network);
        foreach (var n in expanded)
        {
            if (n.Parent is { } p && p.Children?.Contains(n) == false) continue;
            await LoadChildrenAsync(n, reportErrors: false);
        }
        SyncRows();
        foreach (var n in expanded) RefreshRowVisual(n);

        // The selected key was deleted elsewhere: SyncRows already moved the selection to the
        // nearest surviving ancestor; tell the user why, like every other vanished-key path.
        if (selectedBefore is { Kind: TreeNodeKind.Key } && !IsAttached(selectedBefore))
            Handlers.Error?.Invoke($"Cannot find the key {RegPath.ToDisplay(selectedBefore.Path)}. It may have been deleted.");
    }

    private static bool IsAttached(TreeNode node)
    {
        for (var n = node; n.Parent is { } parent; n = parent)
            if (parent.Children?.Contains(n) != true) return false;
        return true;
    }

    /// <summary>Renames a node in place after a successful key rename.</summary>
    public void RenameNode(string oldPath, string newPath)
    {
        var node = FindLoaded(oldPath);
        if (node is null) return;
        node.Name = RegPath.Leaf(newPath);
        RepathSubtree(node, newPath);
        if (node.Parent?.Children is { } siblings)
        {
            siblings.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        }
        SyncRows();
        RefreshRowVisual(node);
        ListHelpers.ScrollRowIntoView(_list, node, RowHeight);
    }

    private static void RepathSubtree(TreeNode node, string newPath)
    {
        node.Path = newPath;
        if (node.Children is null) return;
        foreach (var c in node.Children) RepathSubtree(c, newPath + "\\" + c.Name);
    }

    // ── Inline rename ────────────────────────────────────────────────

    public async void BeginRename(string path)
    {
        var node = FindLoaded(path);
        if (node is null || node.Kind != TreeNodeKind.Key || _renaming is not null) return;
        ListHelpers.ScrollRowIntoView(_list, node, RowHeight);
        _list.UpdateLayout();
        await Task.Yield();
        if (_list.ContainerFromItem(node) is not ListViewItem container ||
            container.ContentTemplateRoot is not Grid { Tag: RowParts parts } root)
            return;

        _renaming = node;
        var editor = ListHelpers.InlineEditor(node.Name);
        Grid.SetColumn(editor, 2);
        parts.Text.Visibility = Visibility.Collapsed;
        root.Children.Add(editor);
        bool done = false;

        async void Finish(bool commit)
        {
            if (done) return;
            done = true;
            var newName = editor.Text;
            root.Children.Remove(editor);
            parts.Text.Visibility = Visibility.Visible;
            _renaming = null;
            if (commit && newName != node.Name && Handlers.RenameRequested is { } rename)
            {
                await rename(node.Path, newName);
            }
            FocusTree();
        }

        editor.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) { e.Handled = true; Finish(true); }
            else if (e.Key == VirtualKey.Escape) { e.Handled = true; Finish(false); }
        };
        editor.LostFocus += (_, _) => Finish(true);
        editor.Focus(FocusState.Programmatic);
        editor.SelectAll();
    }

    // ── Input ────────────────────────────────────────────────────────

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection > 0) return;
        if (_list.SelectedItem is TreeNode n) Handlers.SelectionChanged?.Invoke(n.Path);
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ListHelpers.FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject) is { } c &&
            _list.ItemFromContainer(c) is TreeNode n)
        {
            Toggle(n);
            e.Handled = true;
        }
    }

    private async void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        var node = SelectedNode;
        if (node is null) return;
        var ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                    & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        switch (e.Key)
        {
            case VirtualKey.Right:
                e.Handled = true;
                if (node.Kind == TreeNodeKind.Computer || node.IsExpanded)
                {
                    if (node.Children is { Count: > 0 } kids) Select(kids[0]);
                }
                else if (node.HasChildren)
                {
                    await ExpandAsync(node);
                }
                else if (node.Kind == TreeNodeKind.Key)
                {
                    // A leaf: nothing to expand, but make sure it still exists (classic regedit
                    // re-enumerates here too) and pick up subkeys created since we looked.
                    var p = node.Path;
                    if (!await Task.Run(() => RegistryService.KeyExists(p))) await HandleVanishedAsync(p);
                    else if (await Task.Run(() => RegistryService.HasSubKeys(p)))
                    {
                        node.HasChildren = true;
                        await ExpandAsync(node);
                    }
                }
                break;
            case VirtualKey.Left:
                e.Handled = true;
                if (node.IsExpanded && node.Kind != TreeNodeKind.Computer) Collapse(node);
                else if (node.Parent is { } parent && !ReferenceEquals(parent, _network)) Select(parent);
                break;
            case VirtualKey.Add:
                e.Handled = true;
                await ExpandAsync(node);
                break;
            case VirtualKey.Subtract:
                e.Handled = true;
                Collapse(node);
                break;
            case VirtualKey.Enter:
                e.Handled = true;
                Toggle(node);
                break;
            case VirtualKey.F2 when !ctrl:
                e.Handled = true;
                BeginRename(node.Path);
                break;
            case VirtualKey.Delete when !ctrl:
                e.Handled = true;
                if (node.Kind == TreeNodeKind.Key) Handlers.DeleteRequested?.Invoke(node);
                break;
        }
    }

    private void Select(TreeNode node)
    {
        _list.SelectedItem = node;
        ListHelpers.ScrollRowIntoView(_list, node, RowHeight);
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, FocusTree);
    }

    /// <summary>Type-ahead: typed letters jump to the next sibling whose name starts with them.</summary>
    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox || char.IsControl(e.Character)) return;
        var node = SelectedNode;
        if (node?.Parent?.Children is not { Count: > 0 } siblings) return;
        e.Handled = true;

        var now = Environment.TickCount64;
        var ch = char.ToString(e.Character);
        bool continuing = now - _typeAheadTicks < 1000;
        _typeAheadTicks = now;
        // Repeating the same single letter cycles through matches.
        _typeAhead = continuing && !(_typeAhead.Length == 1 && _typeAhead.Equals(ch, StringComparison.OrdinalIgnoreCase))
            ? _typeAhead + ch
            : ch;
        int start = siblings.IndexOf(node);
        int offset = _typeAhead.Length == 1 ? 1 : 0;
        for (int i = 0; i < siblings.Count; i++)
        {
            var candidate = siblings[(start + offset + i) % siblings.Count];
            if (candidate.Name.StartsWith(_typeAhead, StringComparison.OrdinalIgnoreCase))
            {
                if (!ReferenceEquals(candidate, node)) Select(candidate);
                return;
            }
        }
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        var container = ListHelpers.FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
        if (container is null || _list.ItemFromContainer(container) is not TreeNode node) return;
        e.Handled = true;
        if (!ReferenceEquals(SelectedNode, node)) _list.SelectedItem = node;
        if (Handlers.BuildContextMenu is not { } build) return;
        var flyout = ListHelpers.BuildMenu(build(node));
        if (e.TryGetPosition(container, out var pt))
            flyout.ShowAt(container, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = pt });
        else
            flyout.ShowAt(container);
    }

    // ── Automation: present the rows as a real tree ──────────────────

    internal sealed partial class TreeListView(RegistryTreeView owner) : ListView
    {
        public RegistryTreeView Owner { get; } = owner;
        protected override DependencyObject GetContainerForItemOverride() => new TreeRowItem(Owner);
        protected override bool IsItemItsOwnContainerOverride(object item) => item is TreeRowItem;
        protected override AutomationPeer OnCreateAutomationPeer() => new TreeListPeer(this);
    }

    internal sealed partial class TreeListPeer(ListView owner) : ListViewAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Tree;
        protected override string GetClassNameCore() => "RegistryTree";
    }

    internal sealed partial class TreeRowItem(RegistryTreeView owner) : ListViewItem
    {
        public RegistryTreeView Owner { get; } = owner;
        public TreeNode? Node { get; set; }
        protected override AutomationPeer OnCreateAutomationPeer() => new TreeRowPeer(this);
    }

    internal sealed partial class TreeRowPeer(TreeRowItem owner) : ListViewItemAutomationPeer(owner), IExpandCollapseProvider
    {
        private readonly TreeRowItem _item = owner;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TreeItem;
        protected override string GetClassNameCore() => "RegistryTreeItem";

        // Bound to the node so the announced name can never lag behind the label.
        protected override string GetNameCore() => _item.Node?.Name ?? base.GetNameCore();

        protected override object GetPatternCore(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPatternCore(patternInterface);

        public ExpandCollapseState ExpandCollapseState => _item.Node switch
        {
            null => ExpandCollapseState.LeafNode,
            { Kind: TreeNodeKind.Computer } => ExpandCollapseState.Expanded,
            { HasChildren: false } => ExpandCollapseState.LeafNode,
            { IsExpanded: true } => ExpandCollapseState.Expanded,
            _ => ExpandCollapseState.Collapsed,
        };

        public void Expand()
        {
            if (_item.Node is { } n) _ = _item.Owner.ExpandAsync(n);
        }

        public void Collapse()
        {
            if (_item.Node is { } n) _item.Owner.Collapse(n);
        }
    }
}
