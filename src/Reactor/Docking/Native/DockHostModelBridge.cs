using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.16 — DockHostModel bridge.
//
//  The DockHostNativeComponent stashes its live DockHostModel instance here
//  on every render so external callers (tests, devtools, future apps) can
//  grab the same model the component is reading/writing. Pattern mirrors
//  DockChordBridge — same lifetime invariants, same ConditionalWeakTable
//  for GC hygiene.
//
//  Apps inside the host subtree should resolve the model via the
//  DockContexts.Host context (§2.17) instead — that path doesn't require
//  a reference to the DockManager element.
// ════════════════════════════════════════════════════════════════════════

internal static class DockHostModelBridge
{
    private static readonly ConditionalWeakTable<DockManager, DockHostModel> _table = new();

    public static void Set(DockManager element, DockHostModel model)
    {
        _table.Remove(element);
        _table.Add(element, model);
    }

    public static DockHostModel? Get(DockManager? element)
    {
        if (element is null) return null;
        return _table.TryGetValue(element, out var m) ? m : null;
    }

    public static void Clear(DockManager element) => _table.Remove(element);
}
