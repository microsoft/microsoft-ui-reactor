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
//  Chords deferred to a follow-up pass (separate overlays + state machine):
//    • Ctrl+Tab — VS-style pane navigator
//    • Alt+F7 — hidden-pane picker
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
