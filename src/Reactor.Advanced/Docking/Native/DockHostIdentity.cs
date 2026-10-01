using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.25 — which mounted host a DockManager element belongs to.
//
//  Apps build `new DockManager { … }` in their render, and every state
//  change renders the app again (the host's own changes included), so one
//  mounted host sees a new element instance on almost every render. The
//  per-host tables (floating windows, host registry, chord bridge, live
//  announcer, model bridge, drag gate) were keyed by the instance: each
//  render filed its entries under a new key, and unmount, which only knows
//  the last instance, left the rest behind. A pane floated under an earlier
//  instance kept its window open after the host was gone.
//
//  DockingNativeInterop binds every element a host renders to that host's
//  identity before the element renders, and the tables key on
//  `KeyFor(element)`. Any element the host rendered reaches the host's one
//  entry, whichever render wrote it. The host's unmount and its chord
//  accelerators go through the identity itself: an element reused by a new
//  host resolves to the host that rendered it last, and a type change
//  mounts the new host before it unmounts the old one.
// ════════════════════════════════════════════════════════════════════════

/// <summary>The identity of one mounted native dock host.</summary>
internal sealed class DockHostIdentity
{
    private static readonly ConditionalWeakTable<DockManager, DockHostIdentity> s_hostOf = new();

    /// <summary>
    /// Records that this host renders <paramref name="element"/>. Call it
    /// before the element renders, so the entries its render makes are filed
    /// under the host. An element that another host rendered before moves to
    /// this one.
    /// </summary>
    public void Bind(DockManager element) => s_hostOf.AddOrUpdate(element, this);

    /// <summary>
    /// The key the per-host tables file <paramref name="element"/>'s entries
    /// under: the identity of the host that rendered it, or the element
    /// itself when no host has. An unrendered element, such as a bare one in a
    /// unit test, keeps entries of its own.
    /// </summary>
    public static object KeyFor(DockManager element) =>
        s_hostOf.TryGetValue(element, out var host) ? host : element;
}
