using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Hosting.LayoutCost;

/// <summary>
/// <c>UIElement</c> ↔ native <c>CUIElement*</c> lookup for layout-cost
/// attribution. See spec §Attribution strategy.
/// </summary>
/// <remarks>
/// <para><b>Interop path.</b> On lifted WinUI the native-pointer surface of
/// <c>UIElement</c> is not in the public SDK headers — the documented WinRT
/// <c>IUIElement7</c> on WPF's desktop WinUI does not lift, and the
/// internal <c>CUIElement*</c> is only reachable via ABI-level interop
/// (<c>ICoreObjectReference</c> on the ABI surface, or the lifted internal
/// <c>NativePointer</c> accessor). <b>v1 uses spatial-only attribution</b>:
/// this class remembers the ElementId pulled from the ETW payload as soon
/// as we see the first event for that UIElement, but falls back to the
/// innermost bounding-rect match (via <see cref="SpatialIndex"/>) for any
/// lookup. Once the interop path is validated we'll add direct
/// <c>UIElement.GetNativePointer()</c> calls inside
/// <see cref="Track"/>.</para>
/// <para>Spec §Open-questions-1 is resolved as: <b>spatial-only for v1</b>;
/// clipped/overflowing children attribute to whoever sits under their
/// screen position, which is documented as a known limitation.</para>
/// </remarks>
internal sealed class PointerMap
{
    private readonly ConditionalWeakTable<UIElement, object> _elementToId = new();
    private readonly Dictionary<ulong, ComponentIdentity> _idToComponent = new();

    /// <summary>
    /// Records that <paramref name="element"/> is owned by
    /// <paramref name="owner"/>. If the native pointer can be obtained
    /// synchronously (v2), it is stashed here so attribution becomes O(1).
    /// v1 is a no-op placeholder — attribution falls through to
    /// <see cref="SpatialIndex"/>.
    /// </summary>
    public void Track(UIElement element, ComponentIdentity owner)
    {
        // v1: no-op. ElementId becomes known only when we later see it on an
        // ETW payload; we stash the binding via RegisterElementId below.
        _ = element;
        _ = owner;
    }

    /// <summary>Called the first time an ETW event surfaces an ElementId for a tracked element.</summary>
    public void RegisterElementId(ulong elementId, ComponentIdentity owner)
    {
        _idToComponent[elementId] = owner;
    }

    public bool TryGetComponent(ulong elementId, out ComponentIdentity owner)
        => _idToComponent.TryGetValue(elementId, out owner);

    public void Untrack(UIElement element)
    {
        _elementToId.Remove(element);
        // We cannot efficiently walk `_idToComponent` to remove by ownership
        // here because ElementIds may be reused after free. The map is
        // overwritten on the next Track() for that id.
    }

    public void Clear()
    {
        // ConditionalWeakTable has no Clear(); drop refs by letting it GC.
        _idToComponent.Clear();
    }
}
