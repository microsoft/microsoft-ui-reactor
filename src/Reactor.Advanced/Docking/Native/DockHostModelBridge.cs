using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.16 — DockHostModel bridge.
//
//  The DockHostNativeComponent stashes its live DockHostModel instance here
//  on every render so external callers (tests, devtools, future apps) can
//  grab the same model the component is reading/writing. Pattern mirrors
//  DockChordBridge — keyed by the host (DockHostIdentity), so any
//  DockManager instance the host rendered resolves to the model, and
//  cleared on unmount.
//
//  Apps inside the host subtree should resolve the model via the
//  DockContexts.Host context (§2.17) instead — that path doesn't require
//  a reference to the DockManager element.
// ════════════════════════════════════════════════════════════════════════

internal static class DockHostModelBridge
{
    private static readonly ConditionalWeakTable<object, DockHostModel> _table = new();

    public static void Set(DockManager element, DockHostModel model) =>
        _table.AddOrUpdate(DockHostIdentity.KeyFor(element), model);

    public static DockHostModel? Get(DockManager? element)
    {
        if (element is null) return null;
        return _table.TryGetValue(DockHostIdentity.KeyFor(element), out var m) ? m : null;
    }

    public static void Clear(DockManager element) => _table.Remove(DockHostIdentity.KeyFor(element));

    public static void Clear(DockHostIdentity host) => _table.Remove(host);
}
