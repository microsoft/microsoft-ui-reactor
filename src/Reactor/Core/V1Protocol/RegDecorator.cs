using System;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

/// <summary>
/// Spec 048 §3.4 — the decorator sibling of
/// <see cref="Reg{TElement, TControl, THandler}"/>. One <i>closed-generic</i>
/// touch of <see cref="Done"/> from inside a factory method causes the CLR
/// to run the generic type's static constructor exactly once per process,
/// which in turn calls <see cref="ControlRegistry.RegisterDecorator{TElement}"/>
/// with a <see langword="static"/> lambda that closes over
/// <typeparamref name="THandler"/>.
///
/// <para><b>Why a separate shim?</b>
/// <see cref="IDecoratorElementHandler{TElement}"/> is a distinct,
/// non-inheriting interface from
/// <see cref="IElementHandler{TElement, TControl}"/>. Decorator handlers
/// (Button, CheckBox, the keyed-reconcile panels, the overlay family,
/// XamlHost/XamlPage, polymorphic IconElement, etc.) don't carry a
/// statically-known <c>TControl</c> in the registration frame — the
/// returned <see cref="Microsoft.UI.Xaml.UIElement"/> identity may change
/// on update, or the control type only becomes known once the handler
/// runs. So this shim has just two type parameters (<typeparamref name="TElement"/>,
/// <typeparamref name="THandler"/>); the WinUI control type is rooted
/// transitively through the handler's own static reference graph
/// (e.g., a handler's <c>ctx.RentControl&lt;WinUI.Button&gt;()</c> call
/// or its descriptor-field access).</para>
///
/// <para><b>Authoring pattern.</b>
/// <code>
/// public static ButtonElement Button(string label, Action? onClick = null)
/// {
///     _ = RegDecorator&lt;ButtonElement, ButtonHandler&gt;.Done;
///     return new ButtonElement(label, onClick);
/// }
/// </code>
/// The discard (<c>_ =</c>) is intentional — same rationale as
/// <see cref="Reg{TElement, TControl, THandler}.Done"/>: forces the field
/// read so Release-build dead-code elimination can't drop the touch.</para>
///
/// <para><b>One-shim invariant (§3.4 authoring rule).</b> A single
/// element type must use <i>either</i>
/// <c>Reg&lt;TElement, TControl, THandler&gt;.Done</c> (value-handler
/// path) <i>or</i> <see cref="Done"/> (decorator path), never both.
/// <see cref="ControlRegistry"/>'s first-wins TryAdd silently keeps
/// whichever cctor fires first, so mixing the two paths produces a
/// non-deterministic dispatch outcome dependent on the app's factory
/// call order. See <see cref="ControlRegistry.RegisterDecorator{TElement}"/>
/// for the full discussion and the singleton-handler alternative for
/// decorator handlers whose underlying type is private nested
/// (IconDescriptor.Handler, XamlHostDescriptor.Handler,
/// XamlPageDescriptor.Handler).</para>
///
/// <para><b>One-shot semantics.</b> CLR guarantees the static initializer
/// of <c>RegDecorator&lt;TElement, THandler&gt;</c> runs at most once per
/// process per closed-generic instantiation, with thread-safe
/// before-first-use semantics — identical to
/// <see cref="Reg{TElement, TControl, THandler}"/>.</para>
///
/// <para><b>Idempotence under aliasing.</b> Multiple factories may
/// legally touch the same <c>RegDecorator&lt;TElement, THandler&gt;</c>;
/// the cctor still fires only once. The first-wins TryAdd in
/// <see cref="ControlRegistry"/> silently drops any second registration
/// for the same TElement (whether from this shim or
/// <see cref="Reg{TElement, TControl, THandler}"/>).</para>
/// </summary>
/// <typeparam name="TElement">The element record type the handler
/// dispatches against. The dispatch key in
/// <see cref="ControlRegistry"/>.</typeparam>
/// <typeparam name="THandler">The decorator handler implementation type.
/// Must have a public parameterless constructor — the registration site
/// has no way to thread constructor arguments, by design (handlers are
/// stateless w.r.t. registration). Decorator handlers exposed as
/// <c>public static readonly</c> singletons on a private nested type
/// (Icon/XamlHost/XamlPage) cannot satisfy this constraint and must
/// instead call <see cref="ControlRegistry.RegisterDecorator{TElement}"/>
/// directly with a static-lambda singleton accessor.</typeparam>
internal static class RegDecorator<TElement, THandler>
    where TElement : Element
    where THandler : IDecoratorElementHandler<TElement>, new()
{
    // Explicit (empty) static constructor — disables `beforefieldinit` so
    // Init() runs precisely on the first read of Done (the factory touch),
    // not earlier. See Reg<>.cctor for the full rationale.
    static RegDecorator() { }

    /// <summary>
    /// The static-field touch that drives the decorator registration path.
    /// Reading this field once on a fresh closed-generic instantiation
    /// triggers the closed generic's precise before-first-use cctor, which
    /// runs <see cref="Init"/> and registers the decorator handler factory
    /// with the global <see cref="ControlRegistry"/>. The actual
    /// <see cref="byte"/> value is unused — the field is a side-effect
    /// carrier sized for minimum per-closed-generic static-data footprint.
    /// </summary>
    internal static readonly byte Done = Init();

    private static byte Init()
    {
        // STATIC-LAMBDA MANDATE — same rationale as
        // Reg<TElement,TControl,THandler>.Init. No closure object is
        // allocated; the trimmer's static-reference graph from the call
        // site reaches THandler through exactly one frame (this Init),
        // keeping the rooted-iff-reachable property crisp.
        ControlRegistry.RegisterDecorator<TElement>(static () => new THandler());
        return 1;
    }
}
