using System;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

/// <summary>
/// Spec 048 §3.4 — the base-derived sibling of
/// <see cref="RegDecorator{TElement, THandler}"/>. One <i>closed-generic</i>
/// touch of <see cref="Done"/> registers a decorator handler against
/// <typeparamref name="TBase"/> so every concrete element whose runtime
/// type derives from <typeparamref name="TBase"/> dispatches through it.
///
/// <para><b>When to use.</b> Decorator handlers for non-generic
/// intermediate bases whose closed-T variants should all share the same
/// handler (e.g. <c>TemplatedListElementBase</c>,
/// <c>LazyStackElementBase</c>). Exact-type
/// <see cref="RegDecorator{TElement, THandler}"/> entries still win at
/// dispatch — base-derived is a fallback path.</para>
///
/// <para><b>Authoring pattern.</b>
/// <code>
/// public static TemplatedListViewElement&lt;T&gt; TemplatedListView&lt;T&gt;(...)
/// {
///     _ = RegBaseDecorator&lt;TemplatedListElementBase, TemplatedListHandler&gt;.Done;
///     return new TemplatedListViewElement&lt;T&gt;(...);
/// }
/// </code></para>
/// </summary>
/// <typeparam name="TBase">The non-generic intermediate base. The handler
/// is registered against this type and dispatches to every concrete element
/// whose runtime type derives from it.</typeparam>
/// <typeparam name="THandler">The decorator handler implementation type.
/// Must have a public parameterless constructor.</typeparam>
internal static class RegBaseDecorator<TBase, THandler>
    where TBase : Element
    where THandler : IDecoratorElementHandler<TBase>, new()
{
    // Explicit (empty) static constructor — disables `beforefieldinit` so
    // Init() runs precisely on the first read of Done (the factory touch),
    // not earlier. See Reg<>.cctor for the full rationale.
    static RegBaseDecorator() { }

    /// <summary>
    /// The static-field touch that drives base-derived decorator
    /// registration. Reading this field once on a fresh closed-generic
    /// instantiation triggers the closed generic's precise before-first-use
    /// cctor, which runs <see cref="Init"/> and registers the handler
    /// factory under <typeparamref name="TBase"/> in the global
    /// <see cref="ControlRegistry"/>.
    /// </summary>
    internal static readonly byte Done = Init();

    private static byte Init()
    {
        // STATIC-LAMBDA MANDATE — same rationale as RegDecorator<>.Init.
        ControlRegistry.RegisterDecoratorForDerivedTypes<TBase>(static () => new THandler());
        return 1;
    }
}
