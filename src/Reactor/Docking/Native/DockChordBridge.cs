using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.10 — keyboard chord bridge.
//
//  The DockHostNativeComponent registers chord delegates here each render;
//  the WinUI accelerators (attached by DockingNativeInterop on the mount
//  Border) look the delegates up by DockManager reference when fired.
//
//  Why not CommandHost: CommandHost mounts a fresh Grid layer between
//  the dock Border and the renderer's outer FlexPanel, which perturbs
//  layout timing in the M19 IDE-layout test (the outer FlexPanel's
//  ActualWidth read at the resize step ends up sub-200 DIP and the
//  test's `Width = ActualWidth - 200` assignment throws). Hooking the
//  accelerators directly on the existing Border keeps the visual tree
//  shape identical to the pre-§2.10 baseline.
//
//  ConditionalWeakTable keys on the DockManager *element instance* —
//  apps that rebuild `new DockManager { … }` on every render rotate
//  through entries; the table is GC-rooted via Reactor's element
//  retention until unmount. Mount/update handlers in the interop layer
//  hold a live ref to the current element, so the bridge entry is
//  reachable for the lifetime of the host.
// ════════════════════════════════════════════════════════════════════════

internal static class DockChordBridge
{
    /// <summary>Chord delegates supplied by the host component each render.</summary>
    public sealed record Handlers(
        Action NextTab,
        Action PrevTab,
        Action CloseActive,
        Action EnterDropMode);

    private static readonly ConditionalWeakTable<DockManager, Handlers> _table = new();

    public static void Set(DockManager element, Handlers handlers)
    {
        _table.Remove(element);
        _table.Add(element, handlers);
    }

    public static Handlers? Get(DockManager? element)
    {
        if (element is null) return null;
        return _table.TryGetValue(element, out var h) ? h : null;
    }

    public static void Clear(DockManager element) => _table.Remove(element);
}
