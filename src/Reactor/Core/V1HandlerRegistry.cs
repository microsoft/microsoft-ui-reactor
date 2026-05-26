using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core;

// Spec 047 §14 Phase 1 — v1 protocol feature flag (1.1) introduces a separate
// registry for ported built-in controls. `IV1HandlerEntry` is the type-erased
// dispatch shape consumed by Reconciler.Mount / Reconciler.Update / Unmount;
// 1.6 added `V1HandlerAdapter<TElement,TControl>` which bridges this shape
// to the public `IElementHandler<TElement, TControl>` author surface.
//
// Throws on duplicate per spec §13 Q17 ("Throw on duplicate registration —
// including duplicates against built-in element types"). 1.9 routes the
// duplicate check through `Reconciler.EnsureRegistrableElementType` so
// cross-registry collisions (RegisterType vs RegisterHandler) are caught.

/// <summary>
/// Dispatch shape consumed by Reconciler when UseV1Protocol is ON. The
/// concrete implementation is <see cref="V1Protocol.V1HandlerAdapter{TElement,TControl}"/>,
/// which closes over a public <c>IElementHandler&lt;TElement, TControl&gt;</c>.
/// </summary>
internal interface IV1HandlerEntry
{
    UIElement Mount(Element element, Action requestRerender, Reconciler reconciler);
    void Update(Element oldEl, Element newEl, UIElement control, Action requestRerender, Reconciler reconciler);
    void Unmount(UIElement control, Reconciler reconciler);
    bool HasUnmount { get; }
}

/// <summary>
/// Spec 047 §14 Phase 1 (1.1) — v1 handler registry. Exact-type keyed
/// dictionary; throws on duplicate (matches §13 Q17 and the 1.9 rules).
/// External <c>RegisterType</c> callers continue to populate
/// <c>Reconciler._typeRegistry</c>; ported built-ins populate this map.
/// </summary>
internal sealed class V1HandlerRegistry
{
    private readonly Dictionary<Type, IV1HandlerEntry> _entries = new();

    public bool TryGet(Type elementType, out IV1HandlerEntry entry)
        => _entries.TryGetValue(elementType, out entry!);

    public bool ContainsKey(Type elementType) => _entries.ContainsKey(elementType);

    /// <summary>
    /// Adds a handler for the given element type. Throws on duplicate per
    /// §13 Q17 / spec 1.9.
    /// </summary>
    public void Add(Type elementType, IV1HandlerEntry entry)
    {
        if (_entries.ContainsKey(elementType))
        {
            throw new InvalidOperationException(
                $"V1 handler already registered for element type '{elementType.FullName}'. " +
                "Duplicate registration is forbidden in v1 (spec 047 §13 Q17).");
        }
        _entries.Add(elementType, entry);
    }
}
