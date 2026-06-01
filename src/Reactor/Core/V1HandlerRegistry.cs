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
/// Dispatch shape consumed by Reconciler when routing an element through the
/// V1 protocol. The
/// concrete implementations are:
/// <list type="bullet">
///   <item><see cref="V1Protocol.V1HandlerAdapter{TElement,TControl}"/>
///   — standard adapter wrapping an <c>IElementHandler&lt;TElement,TControl&gt;</c>.</item>
///   <item><see cref="V1Protocol.V1DecoratorHandlerAdapter{TElement}"/>
///   — Phase 3 completion adapter wrapping an
///   <c>IDecoratorElementHandler&lt;TElement&gt;</c> for decorator /
///   modal / polymorphic / interop cases that need control substitution
///   on update and/or non-default unmount disposition.</item>
/// </list>
/// </summary>
internal interface IV1HandlerEntry
{
    UIElement Mount(Element element, Action requestRerender, Reconciler reconciler);
    /// <summary>Update returns the <see cref="UIElement"/> that should
    /// occupy the parent's slot after the update. Standard handlers
    /// always return <paramref name="control"/> unchanged (the
    /// §13 Q12 "no substitution" invariant). Decorator-style handlers
    /// (<see cref="V1Protocol.IDecoratorElementHandler{TElement}"/>)
    /// may return a different instance when the wrapped target's
    /// element type changed.</summary>
    UIElement Update(Element oldEl, Element newEl, UIElement control, Action requestRerender, Reconciler reconciler);
    /// <summary>Unmount returns the engine's post-unmount disposition
    /// for the control. Standard handlers always return
    /// <see cref="V1Protocol.V1UnmountDisposition.CollectSelf"/>;
    /// decorator-style handlers may opt out of pool return / let the
    /// engine continue the default traversal into wrapped children.</summary>
    V1Protocol.V1UnmountDisposition Unmount(UIElement control, Reconciler reconciler);
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
    // §14 Phase 3 close-out — base-derived entries catch any element whose
    // concrete runtime type derives from the registered base. Used by the
    // typed templated-list descriptor ports (TemplatedListViewElement<T>
    // family) so a single descriptor registration on a non-generic
    // intermediate base routes every closed-T variant — same T-erasure
    // model the legacy Reconciler.Mount switch uses.
    private readonly Dictionary<Type, IV1HandlerEntry> _baseEntries = new();
    // Cache resolved base lookups so the walk is O(1) in steady state.
    // Null marker means "checked and no base match" so we don't re-walk.
    private readonly Dictionary<Type, IV1HandlerEntry?> _baseCache = new();

    public bool TryGet(Type elementType, out IV1HandlerEntry entry)
    {
        if (_entries.TryGetValue(elementType, out entry!)) return true;
        if (_baseEntries.Count == 0) { entry = null!; return false; }
        if (_baseCache.TryGetValue(elementType, out var cached))
        {
            entry = cached!;
            return cached is not null;
        }
        for (var t = elementType.BaseType; t is not null && t != typeof(object); t = t.BaseType)
        {
            if (_baseEntries.TryGetValue(t, out entry!))
            {
                _baseCache[elementType] = entry;
                return true;
            }
        }
        _baseCache[elementType] = null;
        entry = null!;
        return false;
    }

    public bool ContainsKey(Type elementType) => _entries.ContainsKey(elementType) || _baseEntries.ContainsKey(elementType);

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

    /// <summary>
    /// §14 Phase 3 close-out — register a handler that catches every
    /// closed runtime type whose type chain reaches <paramref name="baseType"/>.
    /// Exact-type entries in <see cref="Add"/> always take precedence, so
    /// a derived type registered explicitly stays distinct. Throws on
    /// duplicate base-type registration. Invalidates the resolution cache.
    /// </summary>
    public void AddForDerivedTypes(Type baseType, IV1HandlerEntry entry)
    {
        if (_baseEntries.ContainsKey(baseType))
        {
            throw new InvalidOperationException(
                $"V1 handler already registered for derived types of '{baseType.FullName}'. " +
                "Duplicate registration is forbidden in v1 (spec 047 §13 Q17).");
        }
        _baseEntries.Add(baseType, entry);
        // A previously-cached miss may now resolve via this base — invalidate.
        if (_baseCache.Count > 0) _baseCache.Clear();
    }
}
