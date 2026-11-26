using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace ToolkitCompatibility;

// Shared with the Reactor-free native probe. Observes existing peers only.
internal static class SegmentedUiaDiagnostics
{
    internal static async Task<bool> CaptureAsync(
        ListViewBase control, string variant, Action<string> note,
        Func<Func<bool>, string, Task> wait)
    {
        int originalIndex = control.SelectedIndex;
        bool identitiesMatch = control.Items.Count > 1;
        note($"UIA diagnostic variant={variant}; control={control.GetType().FullName}; " +
            $"assembly={control.GetType().Assembly.FullName}; WinUI={typeof(FrameworkElement).Assembly.FullName}; " +
            $"Name='{AutomationProperties.GetName(control)}'; SelectionMode={control.SelectionMode}");
        try
        {
            foreach (int index in new[] { 0, 1 })
            {
                control.SelectedIndex = index;
                await wait(() => control.SelectedIndex == index && control.XamlRoot is not null &&
                    control.ActualWidth > 0 && control.ActualHeight > 0 &&
                    Enumerable.Range(0, control.Items.Count).All(i => control.ContainerFromIndex(i) is UIElement),
                    $"UIA diagnostic {variant} selection {index} realized");
                note($"UIA diagnostic variant={variant}; SelectedIndex={control.SelectedIndex}; " +
                    $"SelectedItem='{Label(control.SelectedItem)}'; Items={control.Items.Count}");
                var root = FrameworkElementAutomationPeer.CreatePeerForElement(control);
                var peers = ReadTree(root, note);
                var selection = root?.GetPattern(PatternInterface.Selection) as ISelectionProvider;
                note($"UIA diagnostic root Selection count={selection?.GetSelection().Length.ToString() ?? "(absent)"}; " +
                    $"CanSelectMultiple={selection?.CanSelectMultiple.ToString() ?? "(absent)"}");
                for (int i = 0; i < control.Items.Count; i++)
                {
                    var container = (UIElement)control.ContainerFromIndex(i);
                    var peer = FrameworkElementAutomationPeer.CreatePeerForElement(container);
                    Describe(peer, $"container[{i}]", note);
                    if (peer?.EventsSource is { } source)
                        Describe(source, $"container[{i}].EventsSource", note);
                    string label = Label(control.Items[i]);
                    var matches = peers.Where(p => p.GetName() == label &&
                        p.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider).ToArray();
                    var item = peer?.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider
                        ?? (matches.Length == 1
                            ? matches[0].GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider : null);
                    bool matchesNative = item is not null && item.IsSelected == (i == index);
                    identitiesMatch &= matchesNative;
                    note($"UIA diagnostic item={i}; label='{label}'; named-tree-providers={matches.Length}; " +
                        $"IsSelected={item?.IsSelected.ToString() ?? "(unavailable)"}; expected={i == index}; matches={matchesNative}");
                }
            }
        }
        finally
        {
            control.SelectedIndex = originalIndex;
        }
        note($"UIA diagnostic variant={variant}; both-selection-identities-observed={identitiesMatch}; " +
            "in-process observation only, not a cross-process accessibility verdict");
        return identitiesMatch;
    }

    private static List<AutomationPeer> ReadTree(AutomationPeer? root, Action<string> note)
    {
        const int maxDepth = 16;
        const int maxPeers = 256;
        var peers = new List<AutomationPeer>();
        var seen = new HashSet<AutomationPeer>(ReferenceEqualityComparer.Instance);
        var pending = new Queue<(AutomationPeer Peer, int Depth, string Path)>();
        if (root is not null) pending.Enqueue((root, 0, "root"));
        else Describe(null, "root", note);
        bool truncated = false;
        while (pending.Count > 0 && peers.Count < maxPeers)
        {
            var (peer, depth, path) = pending.Dequeue();
            if (!seen.Add(peer)) continue;
            peers.Add(peer);
            Describe(peer, path, note);
            var children = peer.GetChildren();
            if (children is null) continue;
            for (int i = 0; i < children.Count; i++)
            {
                if (depth == maxDepth || peers.Count + pending.Count >= maxPeers)
                {
                    truncated = true;
                    break;
                }
                pending.Enqueue((children[i], depth + 1, $"{path}/{i}"));
            }
        }
        note($"UIA diagnostic tree peers={peers.Count}; max-depth={maxDepth}; max-peers={maxPeers}; truncated={truncated}");
        return peers;
    }

    private static void Describe(AutomationPeer? peer, string path, Action<string> note)
    {
        if (peer is null)
        {
            note($"UIA diagnostic path={path}; peer=(none)");
            return;
        }
        var selection = peer.GetPattern(PatternInterface.Selection);
        var item = peer.GetPattern(PatternInterface.SelectionItem);
        note($"UIA diagnostic path={path}; peer={peer.GetType().FullName}; class='{peer.GetClassName()}'; " +
            $"name='{peer.GetName()}'; id='{peer.GetAutomationId()}'; role={peer.GetAutomationControlType()}; " +
            $"Selection={selection?.GetType().FullName ?? "(none)"}; typed-selection={selection is ISelectionProvider}; " +
            $"SelectionItem={item?.GetType().FullName ?? "(none)"}; typed-item={item is ISelectionItemProvider}; " +
            $"IsSelected={(item as ISelectionItemProvider)?.IsSelected.ToString() ?? "(unavailable)"}");
    }

    private static string Label(object? item) =>
        (item is ContentControl container ? container.Content : item)?.ToString() ?? "";
}
