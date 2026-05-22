using Microsoft.UI.Reactor.Core;
using Windows.System;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.10 — keyboard navigation chords for the docking host.
//
//  Pure helpers + Command-array builder. The host component wraps its
//  rendered subtree in a CommandHost (spec Reactor.Core) so accelerators
//  fire only when focus lives within the dock host's visual subtree.
//
//  Chords landed here:
//    • Ctrl+PageUp / Ctrl+PageDown — prev/next tab in the active group
//    • Ctrl+F4 / Ctrl+W — close active document if CanClose
//    • Ctrl+Shift+M — enter keyboard drop-target mode (flips ShowDropTargets)
//
//  Additional chords driving the §2.10 navigator overlay:
//    • Ctrl+Tab — VS-style pane navigator (forward cycle)
//    • Ctrl+Shift+Tab — pane navigator (reverse cycle)
//  Both route through DockChordBridge.Handlers.OpenNavigator(±1); the
//  DockNavigatorPopup primitive handles the popup + Ctrl-release commit
//  + Esc cancel state machine outside the reconciler render tree.
//
//  Chord still deferred:
//    • Alt+F7 — hidden-pane picker (uses the same overlay primitive)
//
//  All chords scope to the dock host subtree via CommandHostElement's
//  IsDescendantOf focus check (Reconciler.Mount.cs).
// ════════════════════════════════════════════════════════════════════════

internal static class DockHostKeyboard
{
    /// <summary>
    /// Find the tab group containing a pane whose <see cref="DockableContent.Key"/>
    /// equals <paramref name="key"/> (by Equals). Walks the entire tree.
    /// Returns (null, null, -1) when not found.
    /// </summary>
    public static (DockTabGroup? Group, string? Path, int Index) FindGroupContainingKey(
        DockNode? root, object? key)
    {
        if (root is null || key is null) return (null, null, -1);
        return Inner(root, "0", key);

        static (DockTabGroup?, string?, int) Inner(DockNode node, string path, object key)
        {
            switch (node)
            {
                case DockTabGroup grp:
                    for (int i = 0; i < grp.Documents.Count; i++)
                    {
                        if (Equals(grp.Documents[i].Key, key))
                            return (grp, path, i);
                    }
                    return (null, null, -1);
                case DockSplit split:
                    for (int i = 0; i < split.Children.Count; i++)
                    {
                        var r = Inner(split.Children[i], $"{path}/{i}", key);
                        if (r.Item1 is not null) return r;
                    }
                    return (null, null, -1);
                default:
                    return (null, null, -1);
            }
        }
    }

    /// <summary>
    /// Returns the first <see cref="DockTabGroup"/> reachable from the root
    /// in depth-first left-to-right order, plus its path. Used as the
    /// fallback "active group" when no document has been explicitly
    /// activated.
    /// </summary>
    public static (DockTabGroup? Group, string? Path) FindFirstGroup(DockNode? root)
    {
        if (root is null) return (null, null);
        return Inner(root, "0");

        static (DockTabGroup?, string?) Inner(DockNode node, string path)
        {
            switch (node)
            {
                case DockTabGroup grp: return (grp, path);
                case DockSplit split:
                    for (int i = 0; i < split.Children.Count; i++)
                    {
                        var r = Inner(split.Children[i], $"{path}/{i}");
                        if (r.Item1 is not null) return r;
                    }
                    return (null, null);
                default: return (null, null);
            }
        }
    }

    /// <summary>
    /// Flatten the layout into a depth-first list of leaf panes. Used by
    /// the §2.10 Ctrl+Tab navigator to populate the picker list. Returns
    /// every <see cref="DockableContent"/> reachable from the root,
    /// excluding side strips (those are surfaced via the side popup).
    /// </summary>
    public static IReadOnlyList<DockableContent> EnumerateLeaves(DockNode? root)
    {
        if (root is null) return Array.Empty<DockableContent>();
        var result = new List<DockableContent>();
        Walk(root, result);
        return result;

        static void Walk(DockNode node, List<DockableContent> acc)
        {
            switch (node)
            {
                case DockableContent leaf:
                    acc.Add(leaf);
                    break;
                case DockTabGroup grp:
                    foreach (var d in grp.Documents) acc.Add(d);
                    break;
                case DockSplit split:
                    foreach (var c in split.Children) Walk(c, acc);
                    break;
            }
        }
    }

    /// <summary>
    /// Index of <paramref name="key"/> in the enumeration produced by
    /// <see cref="EnumerateLeaves"/>. Returns -1 when not present.
    /// </summary>
    public static int IndexOfKey(IReadOnlyList<DockableContent> leaves, object? key)
    {
        if (key is null) return -1;
        for (int i = 0; i < leaves.Count; i++)
        {
            if (Equals(leaves[i].Key, key)) return i;
        }
        return -1;
    }

    /// <summary>
    /// Cycle the selected index within a group's document range by
    /// <paramref name="delta"/>. Wraps at both ends so PageDown on the
    /// last tab lands on the first, matching VS parity.
    /// </summary>
    public static int CycleIndex(int current, int delta, int count)
    {
        if (count <= 0) return 0;
        var next = (current + delta) % count;
        if (next < 0) next += count;
        return next;
    }

    /// <summary>
    /// Build the chord <see cref="Command"/> set scoped to a single host
    /// render. Callers must thread the closures through with fresh state
    /// each render (the host's CommandHost reconciler rebuilds
    /// accelerators when the Commands array reference changes).
    /// </summary>
    public static Command[] BuildChords(
        Action invokeNextTab,
        Action invokePrevTab,
        Action invokeCloseActive,
        Action invokeKeyboardDropMode)
    {
        return new[]
        {
            new Command
            {
                Label = "Next tab",
                Execute = invokeNextTab,
                Accelerator = new KeyboardAcceleratorData(VirtualKey.PageDown, VirtualKeyModifiers.Control),
            },
            new Command
            {
                Label = "Previous tab",
                Execute = invokePrevTab,
                Accelerator = new KeyboardAcceleratorData(VirtualKey.PageUp, VirtualKeyModifiers.Control),
            },
            new Command
            {
                Label = "Close active document",
                Execute = invokeCloseActive,
                Accelerator = new KeyboardAcceleratorData(VirtualKey.F4, VirtualKeyModifiers.Control),
            },
            new Command
            {
                Label = "Close active document (alt)",
                Execute = invokeCloseActive,
                Accelerator = new KeyboardAcceleratorData(VirtualKey.W, VirtualKeyModifiers.Control),
            },
            new Command
            {
                Label = "Show docking targets",
                Execute = invokeKeyboardDropMode,
                Accelerator = new KeyboardAcceleratorData(VirtualKey.M, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift),
            },
        };
    }
}
