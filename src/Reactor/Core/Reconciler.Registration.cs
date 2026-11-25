using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;
using ReactorSourceMap = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap;

namespace Microsoft.UI.Reactor.Core;

// Reconciler partial: Element type / handler registration (RegisterType, RegisterHandler, TypeRegistration).
public sealed partial class Reconciler
{
    /// <summary>
    /// Registers a custom element type so the reconciler knows how to mount, update, and unmount it.
    /// Registered types take priority over built-in types.
    ///
    /// Spec 047 §13 Q17 / §14 Phase 1 (1.9) — v1 semantics:
    ///   - Exact-type lookup via <c>element.GetType()</c>; no base-class fallback.
    ///   - Throws <see cref="InvalidOperationException"/> on duplicate registration,
    ///     including duplicates against a previously-registered V1 handler
    ///     (<see cref="V1HandlerRegistry"/>).
    ///   - Throws on open-generic element types.
    ///
    /// The mount and update handlers receive the Reconciler instance so they can
    /// recursively mount/update/unmount child elements without capturing external state.
    ///
    /// The reconciler records the element on the control it gets back from <c>mount</c> or
    /// <c>update</c> (the element tag) wherever it reads the tag back itself: keyed
    /// reconciliation, the <c>unmount</c> lookup, <c>.Ref(...)</c> cleanup and exit
    /// transitions. The callbacks don't call <see cref="SetElementTag"/> for those. Code of your
    /// own that reads the element back with <see cref="GetElementTag(FrameworkElement)"/>, such
    /// as an event handler, still tags the control itself. The tag lives on a
    /// <see cref="FrameworkElement"/>. <typeparamref name="TControl"/> may be declared as
    /// <see cref="UIElement"/>, but in WinUI 3 <see cref="FrameworkElement"/> is its only
    /// subclass, so every control a registration returns is tagged the same way.
    ///
    /// <c>mount</c> and <c>update</c> are expected to return a control the registration owns.
    /// The tag holds one element, so a control that already carries the tag of an element of
    /// another type (registration is by exact type, so a derived type counts), for example one
    /// <c>mount</c> got from <see cref="Mount"/> for a child element, keeps that tag:
    /// overwriting it would silence that element's own callbacks (the collision issue #942
    /// tracks for target-wrapping decorators). The registration's key, ref, transitions and
    /// <c>unmount</c> are not tracked for such a control. To wrap built-in elements, compose
    /// them in a component instead.
    ///
    /// <c>update</c> returns null after patching the control in place; returning the control
    /// it was handed means the same. Return a different control only when you replaced it.
    ///
    /// <c>unmount</c> runs when the reconciler unmounts the control, in place of its own walk
    /// over the control's children: a child the callbacks mounted through the reconciler has to
    /// be torn down there, for example with <see cref="UnmountChild"/>. Calling
    /// <see cref="UnmountChild"/> on the control itself from <c>unmount</c> walks its children
    /// once rather than calling <c>unmount</c> again. Without an <c>unmount</c> callback the
    /// reconciler walks the control's children itself. A control that <c>update</c> replaced is
    /// unmounted wherever the reconciler reconciles the slot that holds it: a panel, a
    /// <c>Border</c>, a named slot such as <c>SplitView.Pane</c>, a tab's content. A few slots
    /// that a control fills by hand still swap a replaced control out without <c>unmount</c>:
    /// <c>CommandBar</c> content, <c>Expander</c> content and header template,
    /// <c>ContentDialog</c>, <c>Flyout</c> and <c>Popup</c> content, content attached with
    /// <c>.WithFlyout</c>, <c>.WithContextFlyout</c> or <c>.WithToolTip(element)</c>, and the
    /// realized item content of <c>ListView</c>, <c>GridView</c>, <c>TreeView&lt;T&gt;</c> and
    /// templated <c>FlipView</c>.
    ///
    /// Not part of the <c>REACTOR_V1_PREVIEW</c> surface — this is the legacy
    /// type-registry path, public since before Spec 047. The §13 Q17 hardening
    /// (throw on duplicate, no base-class fallback, no open generics) tightens
    /// previously-undefined behavior; it does not change the shape of this API.
    /// </summary>
    /// <remarks>
    /// A value-bearing custom control (one that raises the same change event for
    /// a programmatic write as for a user edit) must suppress its own write echo
    /// from the <c>update</c> callback. Use the supported public primitive
    /// <see cref="ReactorBinding"/>'s
    /// <c>WriteSuppressed</c> method — wrap the programmatic write, gate it on an
    /// equality check, and pair it 1:1 with the write. For new controls prefer
    /// the V1 path: a <see cref="V1Protocol.Descriptor.ControlDescriptor{TElement,TControl}"/>
    /// (the primary, descriptor-first authoring shape — its <c>.Controlled</c> /
    /// <c>.HandCodedControlled</c> handle echo suppression for you), or a
    /// hand-coded <see cref="V1Protocol.IElementHandler{TElement,TControl}"/> via
    /// <see cref="RegisterHandler{TElement,TControl}"/> only for irregular
    /// controls (its <see cref="V1Protocol.ReactorBinding{TElement}"/> wraps the
    /// same primitive). See <c>docs/guide/extending-reactor-controls.md</c>
    /// (issue #206).
    /// </remarks>
    public void RegisterType<TElement, TControl>(
        Func<Reconciler, TElement, Action, TControl> mount,
        Func<Reconciler, TElement, TElement, TControl, Action, UIElement?> update,
        Action<Reconciler, TControl>? unmount = null)
        where TElement : Element
        where TControl : UIElement
    {
        EnsureRegistrableElementType(typeof(TElement), typeof(TControl), "RegisterType");
        _typeRegistry.Add(typeof(TElement), new TypeRegistration<TElement, TControl>(mount, update, unmount));
    }

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.9) — shared duplicate / open-generic guard for
    /// both <see cref="RegisterType{TElement,TControl}"/> and
    /// <see cref="RegisterHandler{TElement,TControl}"/>. Throws on any
    /// cross-registry collision; message names both the existing and the new
    /// TControl types so the author sees what they're shadowing.
    /// </summary>
    internal void EnsureRegistrableElementType(Type elementType, Type newControlType, string callerName)
    {
        if (elementType.ContainsGenericParameters)
        {
            throw new InvalidOperationException(
                $"{callerName}: open-generic element types (e.g. typeof(DataGrid<>)) are not " +
                $"supported in v1 (spec 047 §13 Q17). Element type: '{elementType.FullName}'.");
        }
        if (_typeRegistry.TryGetValue(elementType, out var existing))
        {
            var existingControl = existing.GetType().GetGenericArguments() is { Length: 2 } args
                ? args[1].FullName
                : "<unknown>";
            throw new InvalidOperationException(
                $"Cannot register handler for '{elementType.FullName}' (-> '{newControlType.FullName}'): " +
                $"a handler is already registered (-> '{existingControl}'). " +
                "Duplicate registrations are forbidden in v1 (spec 047 §13 Q17); " +
                "build a separate Reconciler instance if you need a different mapping.");
        }
        if (_v1Handlers.ContainsKey(elementType))
        {
            throw new InvalidOperationException(
                $"Cannot register handler for '{elementType.FullName}' (-> '{newControlType.FullName}'): " +
                "a V1 element handler is already registered for this element type. " +
                "Duplicate registrations are forbidden in v1 (spec 047 §13 Q17); " +
                "build a separate Reconciler instance if you need a different mapping.");
        }
    }

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.6 / 1.9) — register a v1
    /// <see cref="V1Protocol.IElementHandler{TElement,TControl}"/> for the
    /// given element type. Dispatched ahead of <see cref="RegisterType{T,U}"/>
    /// callbacks. Throws on duplicate
    /// (including across registries) and on open-generic element types
    /// (spec §13 Q17).
    /// </summary>
    /// <remarks>
    /// Value-bearing handlers (a control whose programmatic property write
    /// raises the same change event as a user edit) must suppress their own
    /// write echo with the supported public primitive
    /// <see cref="V1Protocol.ReactorBinding{TElement}"/>'s <c>WriteSuppressed</c>
    /// method (or the static <see cref="ReactorBinding"/>); descriptor authors get it
    /// via <c>.Controlled</c> / <c>.HandCodedControlled</c>. See
    /// <see cref="V1Protocol.IElementHandler{TElement,TControl}"/> and
    /// <c>docs/guide/extending-reactor-controls.md</c> (issue #206).
    /// </remarks>
    public void RegisterHandler<TElement, TControl>(V1Protocol.IElementHandler<TElement, TControl> handler)
        where TElement : Element
        where TControl : UIElement
    {
        ArgumentNullException.ThrowIfNull(handler);
        EnsureRegistrableElementType(typeof(TElement), typeof(TControl), "RegisterHandler");
        _v1Handlers.Add(typeof(TElement), new V1Protocol.V1HandlerAdapter<TElement, TControl>(handler));
        (_externalHandlerTypes ??= new()).Add(typeof(TElement));
    }

    /// <summary>
    /// §14 Phase 3 close-out — register a v1 handler that catches every
    /// closed runtime type whose chain reaches <typeparamref name="TBase"/>.
    /// Used by the typed templated-list descriptor ports
    /// (<c>TemplatedListViewElement&lt;T&gt;</c> family) so a single
    /// registration on a non-generic intermediate base routes every closed-T
    /// variant — same T-erasure pattern the legacy
    /// <see cref="Reconciler.Mount"/> switch uses.
    ///
    /// <para>Exact-type registrations via
    /// <see cref="RegisterHandler{TElement,TControl}"/> always win over a
    /// derived-type registration. Throws on duplicate base-type
    /// registration.</para>
    /// </summary>
    public void RegisterHandlerForDerivedTypes<TBase, TControl>(V1Protocol.IElementHandler<TBase, TControl> handler)
        where TBase : Element
        where TControl : UIElement
    {
        ArgumentNullException.ThrowIfNull(handler);
        // Open-generic base types (e.g. typeof(Foo<>)) and the legacy
        // duplicate-registration policy are both validated in
        // EnsureRegistrableElementType — reuse it.
        EnsureRegistrableElementType(typeof(TBase), typeof(TControl), "RegisterHandlerForDerivedTypes");
        _v1Handlers.AddForDerivedTypes(typeof(TBase), new V1Protocol.V1HandlerAdapter<TBase, TControl>(handler));
        (_externalHandlerBaseTypes ??= new()).Add(typeof(TBase));
    }

    /// <summary>
    /// Spec 047 §14 Phase 3 completion — register a decorator-style V1
    /// handler for elements whose returned <see cref="UIElement"/>
    /// identity may change on update or whose unmount disposition
    /// diverges from the standard pool-return. See
    /// <see cref="V1Protocol.IDecoratorElementHandler{TElement}"/> for
    /// the use cases (target-wrapping flyouts, modal lifecycle wrappers,
    /// polymorphic mounts, interop bridges).
    ///
    /// <para>This registration sits on the same dispatch table as
    /// <see cref="RegisterHandler{TElement,TControl}"/> — collisions
    /// throw via <see cref="EnsureRegistrableElementType"/>.</para>
    /// </summary>
    internal void RegisterDecoratorHandler<TElement>(V1Protocol.IDecoratorElementHandler<TElement> handler)
        where TElement : Element
    {
        ArgumentNullException.ThrowIfNull(handler);
        EnsureRegistrableElementType(typeof(TElement), typeof(UIElement), "RegisterDecoratorHandler");
        _v1Handlers.Add(typeof(TElement), new V1Protocol.V1DecoratorHandlerAdapter<TElement>(handler));
    }

    /// <summary>
    /// Spec 047 §14 Phase 3 prelude — register a decorator-style V1 handler
    /// that catches every closed runtime type whose chain reaches
    /// <typeparamref name="TBase"/> (the derived-type analogue of
    /// <see cref="RegisterDecoratorHandler{TElement}"/>). Used by the
    /// Lazy*Stack family, whose closed-T variants all derive from the
    /// non-generic <c>LazyStackElementBase</c> and need the decorator
    /// shape (control identity differs from the descriptor port — the
    /// legacy mount wraps the ItemsRepeater in a ScrollViewer — and
    /// <see cref="V1Protocol.V1UnmountDisposition.ContinueDefaultTraversal"/> so the
    /// engine recurses ScrollViewer → ItemsRepeater → realized rows on
    /// unmount).
    /// </summary>
    internal void RegisterDecoratorHandlerForDerivedTypes<TBase>(V1Protocol.IDecoratorElementHandler<TBase> handler)
        where TBase : Element
    {
        ArgumentNullException.ThrowIfNull(handler);
        EnsureRegistrableElementType(typeof(TBase), typeof(UIElement), "RegisterDecoratorHandlerForDerivedTypes");
        _v1Handlers.AddForDerivedTypes(typeof(TBase), new V1Protocol.V1DecoratorHandlerAdapter<TBase>(handler));
    }

    // Borders whose built-in BorderElement descriptor already released or replaced a removed
    // Background modifier in the current update; Update consumes the mark before ApplyModifiers.
    private readonly HashSet<UIElement> _backgroundRemovalHandled = new(ReferenceEqualityComparer.Instance);

    internal void MarkBackgroundRemovalHandled(UIElement control) => _backgroundRemovalHandled.Add(control);

    // Test seam: marks still held after an update (always 0 once Update returns or throws).
    internal int BackgroundRemovalMarkCount => _backgroundRemovalHandled.Count;

    internal interface ITypeRegistration
    {
        UIElement Mount(Element element, Action requestRerender, Reconciler reconciler);
        UIElement? Update(Element oldEl, Element newEl, UIElement control, Action requestRerender, Reconciler reconciler);
        void Unmount(UIElement control, Reconciler reconciler);
        bool HasUnmount { get; }
    }

    private sealed class TypeRegistration<TElement, TControl> : ITypeRegistration
        where TElement : Element
        where TControl : UIElement
    {
        private readonly Func<Reconciler, TElement, Action, TControl> _mount;
        private readonly Func<Reconciler, TElement, TElement, TControl, Action, UIElement?> _update;
        private readonly Action<Reconciler, TControl>? _unmount;

        public TypeRegistration(
            Func<Reconciler, TElement, Action, TControl> mount,
            Func<Reconciler, TElement, TElement, TControl, Action, UIElement?> update,
            Action<Reconciler, TControl>? unmount)
        {
            _mount = mount;
            _update = update;
            _unmount = unmount;
        }

        public bool HasUnmount => _unmount is not null;

        public UIElement Mount(Element element, Action requestRerender, Reconciler reconciler)
        {
            var control = _mount(reconciler, (TElement)element, requestRerender);
            TagControl(control, element);
            return control;
        }

        public UIElement? Update(Element oldEl, Element newEl, UIElement control, Action requestRerender, Reconciler reconciler)
        {
            // Guard against control type mismatch (e.g., recycled from pool or element type changed at this position).
            // If the existing control isn't our expected type, force a fresh mount instead of crashing.
            var result = control is not TControl typedControl || oldEl is not TElement typedOldEl
                ? _mount(reconciler, (TElement)newEl, requestRerender)
                : _update(reconciler, typedOldEl, (TElement)newEl, typedControl, requestRerender);

            // Returning the control it was handed means the callback patched it in place, the
            // same as returning null. The child reconcilers read any non-null result as a
            // replacement and unmount the control they hold, which would run this registration's
            // unmount callback against a control that stays mounted. A second managed wrapper
            // for the same native control counts too.
            if (result is not null && IsSameControl(result, control))
                result = null;

            TagControl(result ?? control, newEl);
            return result;
        }

        public void Unmount(UIElement control, Reconciler reconciler)
        {
            if (control is TControl typedControl)
                _unmount?.Invoke(reconciler, typedControl);
        }

        // The reconciler tags the control so the callbacks don't have to. The keyed-middle
        // reconcile finds a surviving child by the key on its tag, the unmount path finds this
        // registration's unmount callback through it, and .Ref(...) cleanup reads it too.
        // Same allocation gate as SetElementTagIfNeeded, so an unkeyed, extras-free leaf gets
        // no ReactorState, except that a registration with an unmount callback always tags:
        // that lookup goes through the tag whatever the element carries.
        private void TagControl(UIElement control, Element element)
        {
            // The tag lives on FrameworkElement, UIElement's only subclass in WinUI 3, so a
            // TControl declared as UIElement still arrives here as a FrameworkElement.
            if (control is not FrameworkElement fe) return;
            if (fe.GetValue(ReactorAttached.StateProperty) is ReactorState state)
            {
                // The callback returned a control the reconciler mounted for an element of
                // another type, such as a child's. That element's own event trampolines and
                // unmount read the tag, so it keeps it. Registration is by exact type, so a
                // derived element type is another type here too.
                if (state.Element is { } owner && owner.GetType() != typeof(TElement)) return;
                state.Element = element;
                return;
            }
            if (HasUnmount || NeedsTag(element))
                fe.SetValue(ReactorAttached.StateProperty, new ReactorState { Element = element });
        }
    }
}
