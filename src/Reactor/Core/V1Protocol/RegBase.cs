using System;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

/// <summary>
/// Spec 048 §3.4 — the base-derived sibling of
/// <see cref="Reg{TElement, TControl, THandler}"/>. One <i>closed-generic</i>
/// touch of <see cref="Done"/> from inside a factory method causes the CLR
/// to run the generic type's static constructor exactly once per process,
/// which in turn calls
/// <see cref="ControlRegistry.RegisterForDerivedTypes{TBase,TControl}"/>
/// with a <see langword="static"/> lambda that closes over
/// <typeparamref name="THandler"/>.
///
/// <para><b>When to use.</b> A non-generic intermediate base type whose
/// closed-generic / concrete derivations should all share the same handler
/// (e.g. <c>ItemsRepeaterElement</c>, <c>ItemsViewElement</c>). Exact-type
/// registrations (<see cref="Reg{TElement, TControl, THandler}"/>) still
/// win at dispatch — base-derived is a fallback path.</para>
///
/// <para><b>Authoring pattern.</b>
/// <code>
/// public static ItemsRepeaterElement&lt;T&gt; ItemsRepeater&lt;T&gt;(...)
/// {
///     _ = RegBase&lt;ItemsRepeaterElement, ItemsRepeater, ItemsRepeaterHandler&gt;.Done;
///     return new ItemsRepeaterElement&lt;T&gt;(...);
/// }
/// </code></para>
/// </summary>
/// <typeparam name="TBase">The non-generic intermediate base. The handler
/// is registered against this type and dispatches to every concrete element
/// whose runtime type derives from it.</typeparam>
/// <typeparam name="TControl">The WinUI control the handler mounts.</typeparam>
/// <typeparam name="THandler">The handler implementation type. Must have a
/// public parameterless constructor.</typeparam>
internal static class RegBase<TBase, TControl, THandler>
    where TBase : Element
    where TControl : UIElement
    where THandler : IElementHandler<TBase, TControl>, new()
{
    // Explicit (empty) static constructor — disables `beforefieldinit` so
    // Init() runs precisely on the first read of Done (the factory touch),
    // not earlier. See Reg<>.cctor for the full rationale.
    static RegBase() { }

    /// <summary>
    /// The static-field touch that drives base-derived registration. Reading
    /// this field once on a fresh closed-generic instantiation triggers the
    /// closed generic's precise before-first-use cctor, which runs
    /// <see cref="Init"/> and registers the handler factory under
    /// <typeparamref name="TBase"/> in the global
    /// <see cref="ControlRegistry"/>.
    /// </summary>
    internal static readonly byte Done = Init();

    private static byte Init()
    {
        // STATIC-LAMBDA MANDATE — same rationale as Reg<>.Init.
        ControlRegistry.RegisterForDerivedTypes<TBase, TControl>(static () => new THandler());
        return 1;
    }
}
