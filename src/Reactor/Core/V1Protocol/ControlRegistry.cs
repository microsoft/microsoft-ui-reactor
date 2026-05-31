using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

/// <summary>
/// Spec 048 §8 — the global, lazy, lock-free control registry that backs the
/// factory-as-registration pattern. Holds <see cref="Type"/> → factory-of-
/// <see cref="IV1HandlerEntry"/> entries; never roots a handler or WinUI
/// control type by itself. Every static reference to a handler/control type
/// lives in the <i>callers</i> of <see cref="Register{TElement,TControl}"/> —
/// per-control factory cctors (Pattern A) or <c>Reg&lt;…&gt;</c> static-field
/// initializers (Pattern B), each on a per-control rooted path. The trimmer
/// therefore keeps a control iff its factory is reachable from the app entry
/// point.
///
/// <para><b>Idempotent first-wins.</b> Repeat <see cref="Register{TElement,TControl}"/>
/// calls for the same element type are a silent no-op (spec §12.1). Multiple
/// factories legitimately map to the same element type — e.g. <c>TextBlock()</c>,
/// <c>Heading()</c>, and <c>Subheading()</c> all produce
/// <c>TextBlockElement</c> (spec §10.3) — and a throw from a cctor would
/// surface as a non-deterministic <c>TypeInitializationException</c> at the
/// first-use point. The strict throw-on-duplicate policy from spec 047
/// §13 Q17 is preserved on the explicit per-host
/// <see cref="Reconciler.RegisterHandler{TElement,TControl}"/> path.</para>
///
/// <para><b>Hot path.</b> Per spec §9, <see cref="Register{TElement,TControl}"/>
/// runs at most once per element type per process, on the cold first-use
/// path. The steady-state per-host dispatch lookup short-circuits in the
/// per-host <c>_v1Handlers</c> cache populated on the first registry hit;
/// the registry itself is consulted at most once per (host, element type)
/// pair.</para>
///
/// <para><b>AOT.</b> No reflection, no <see cref="Type.MakeGenericType"/>;
/// the closed-type capture happens inside the generic
/// <see cref="Register{TElement,TControl}"/> entry point so the AOT compiler
/// can see the closed types statically. The internal map is keyed by
/// <see cref="Type"/> and stores <see cref="Func{TResult}"/> delegates only —
/// no MakeGenericType, no runtime type construction.</para>
/// </summary>
public static class ControlRegistry
{
    // Spec §8 — backed by ConcurrentDictionary<Type, Func<IV1HandlerEntry>>.
    // The value is a *factory of the type-erased adapter* so the dispatcher
    // can produce a fresh adapter on first per-host hit without re-running
    // the generic dance. The factory itself is allocated once, inside the
    // generic Register<E,C> below, and never references the handler/control
    // type from any path the trimmer can see outside that generic frame.
    private static readonly ConcurrentDictionary<Type, Func<IV1HandlerEntry>> s_entries = new();

    // Spec 048 §3.4 — base-derived global path. Mirrors V1HandlerRegistry's
    // _baseEntries / _baseCache pair so a single registration on a non-generic
    // intermediate base routes every closed-T variant (the T-erasure pattern
    // used by TemplatedListElementBase, LazyStackElementBase, ItemsRepeater,
    // and ItemsView). Lookup falls back to a BaseType walk after exact-match
    // miss; results are memoised (including null markers) so steady-state is
    // O(1) per derived type.
    private static readonly ConcurrentDictionary<Type, Func<IV1HandlerEntry>> s_baseEntries = new();
    private static readonly ConcurrentDictionary<Type, Func<IV1HandlerEntry>?> s_baseCache = new();

    /// <summary>
    /// Spec §8 — register a handler factory for <typeparamref name="TElement"/>.
    /// Idempotent first-wins: if an entry already exists for
    /// <c>typeof(TElement)</c>, this call is a silent no-op. The handler
    /// factory is invoked at most once per (host, element type) — on the
    /// first dispatch hit — and the resulting adapter is cached into the
    /// host's <c>_v1Handlers</c> map so steady-state dispatch is the
    /// existing fast per-host lookup.
    ///
    /// <para>The <paramref name="handlerFactory"/> delegate <b>should</b> be
    /// a <c>static</c> lambda (no captures) — Pattern A / Pattern B both
    /// rely on the single allocation being interned in a static field at
    /// the call site. A capturing lambda is functionally correct but
    /// allocates a closure on every <c>Reg&lt;&gt;.Init</c> / cctor run,
    /// undoing the cost-model claim in spec §9.</para>
    /// </summary>
    /// <typeparam name="TElement">The element record type the handler
    /// dispatches against. Used as the dispatch key (<see cref="Type"/>).</typeparam>
    /// <typeparam name="TControl">The WinUI control the handler mounts.</typeparam>
    /// <param name="handlerFactory">A factory that, when invoked, returns a
    /// fresh handler. Strongly recommended to be a <c>static</c> lambda
    /// (e.g. <c>static () =&gt; new MarqueeHandler()</c>) so the delegate is
    /// cached in a static field and no closure is allocated.</param>
    public static void Register<TElement, TControl>(
        Func<IElementHandler<TElement, TControl>> handlerFactory)
        where TElement : Element
        where TControl : UIElement
    {
        ArgumentNullException.ThrowIfNull(handlerFactory);

        // Wrap the handler factory in an adapter factory. The closure
        // captures `handlerFactory` only; the closed generic types
        // TElement/TControl are seen statically by the JIT/AOT compiler
        // because this method's frame is itself closed-generic at every
        // call site. Allocated once per (element type) on registration,
        // not per dispatch.
        Func<IV1HandlerEntry> adapterFactory = () =>
            new V1HandlerAdapter<TElement, TControl>(handlerFactory());

        // First-wins: TryAdd silently no-ops on repeat. Lock-free — relies on
        // ConcurrentDictionary's per-bucket fine-grained locking, not a
        // process-wide monitor.
        s_entries.TryAdd(typeof(TElement), adapterFactory);
    }

    /// <summary>
    /// Spec 048 §3.4 — decorator sibling of <see cref="Register{TElement,TControl}"/>
    /// for handlers that implement
    /// <see cref="IDecoratorElementHandler{TElement}"/> (a separate,
    /// non-inheriting interface from <see cref="IElementHandler{TElement,TControl}"/>).
    /// Decorator handlers cover target-wrapping flyouts, modal lifecycle
    /// wrappers, polymorphic mounts (IconElement), interop bridges
    /// (XamlHost/XamlPage), and the keyed-reconcile panels (Stack/Grid/
    /// Canvas/RelativePanel/WrapGrid/Flex) + Button/CheckBox/Expander — see
    /// <see cref="IDecoratorElementHandler{TElement}"/> for the full
    /// use-case list.
    ///
    /// <para>Stores the same <see cref="Func{IV1HandlerEntry}"/> shape as
    /// the value-handler path, so <see cref="Reconciler.TryResolveFromControlRegistry"/>
    /// (dispatch arm 3) consumes both uniformly — no dispatch changes
    /// required. The adapter wrapping is the only difference:
    /// <see cref="V1DecoratorHandlerAdapter{TElement}"/> versus
    /// <see cref="V1HandlerAdapter{TElement,TControl}"/>.</para>
    ///
    /// <para><b>One-shim invariant (§3.4 authoring rule).</b> For any given
    /// element type, factories must touch <i>exactly one</i> of
    /// <c>Reg&lt;…&gt;.Done</c> or <c>RegDecorator&lt;…&gt;.Done</c> —
    /// never both. The first-wins TryAdd silently keeps whichever path's
    /// cctor fires first; mixing them creates a non-deterministic
    /// dispatch outcome that depends on factory call order across the
    /// app's startup graph. This contrasts with the harmless aliasing
    /// case where multiple factories share the <i>same</i> closed-generic
    /// shim (spec §10.3 Heading/Subheading both touching
    /// <c>Reg&lt;TextBlockElement, TextBlock, TextBlockHandler&gt;</c>).</para>
    ///
    /// <para><b>Singleton-handler factories.</b> Decorator handlers exposed
    /// as <c>public static readonly</c> singletons (e.g.
    /// <c>IconDescriptor.Handler</c>, <c>XamlHostDescriptor.Handler</c>,
    /// <c>XamlPageDescriptor.Handler</c>) cannot satisfy
    /// <c>RegDecorator</c>'s <c>new()</c> constraint because the underlying
    /// handler types are private nested classes. Those factories register
    /// directly via this method with a <c>static</c> lambda returning the
    /// singleton:
    /// <code>
    /// public static IconElement Icon(IconKind kind)
    /// {
    ///     ControlRegistry.RegisterDecorator&lt;IconElement&gt;(
    ///         static () =&gt; IconDescriptor.Handler);
    ///     return new IconElement(kind);
    /// }
    /// </code>
    /// This is functionally equivalent to <c>RegDecorator&lt;…&gt;.Done</c>
    /// — the static lambda still has no captures, the TryAdd still
    /// first-wins, and the trim story still routes every reference to the
    /// private handler type through the factory call-site path the
    /// trimmer can see.</para>
    /// </summary>
    /// <typeparam name="TElement">The element record type the decorator
    /// handler dispatches against. Used as the dispatch key
    /// (<see cref="Type"/>).</typeparam>
    /// <param name="handlerFactory">A factory that, when invoked, returns
    /// a fresh decorator handler. Strongly recommended to be a
    /// <c>static</c> lambda (e.g.
    /// <c>static () =&gt; new ButtonHandler()</c>) so the delegate is
    /// cached in a static field and no closure is allocated.</param>
    public static void RegisterDecorator<TElement>(
        Func<IDecoratorElementHandler<TElement>> handlerFactory)
        where TElement : Element
    {
        ArgumentNullException.ThrowIfNull(handlerFactory);

        // Same closed-generic capture pattern as Register<E,C> — the
        // TElement frame is statically visible to the JIT/AOT compiler at
        // every call site. Allocated once per (element type) on
        // registration, not per dispatch.
        Func<IV1HandlerEntry> adapterFactory = () =>
            new V1DecoratorHandlerAdapter<TElement>(handlerFactory());

        s_entries.TryAdd(typeof(TElement), adapterFactory);
    }

    /// <summary>
    /// Spec 048 §3.4 — base-derived sibling of <see cref="Register{TElement,TControl}"/>.
    /// Registers a handler for <typeparamref name="TBase"/> that also catches
    /// every concrete element type whose runtime <see cref="Type"/> derives
    /// from <typeparamref name="TBase"/>. Exact-type registrations (via
    /// <see cref="Register{TElement,TControl}"/> /
    /// <see cref="RegisterDecorator{TElement}"/>) take precedence over a
    /// base-derived registration, matching the per-host
    /// <c>V1HandlerRegistry.TryGet</c> precedence.
    ///
    /// <para>Used for the four cases that registered through
    /// <c>Reconciler.RegisterDescriptorForDerivedTypes</c> on the per-host
    /// path: <c>TemplatedListElementBase</c>, <c>LazyStackElementBase</c>,
    /// <c>ItemsRepeaterElement</c>, and <c>ItemsViewElement</c> — each is a
    /// non-generic intermediate base whose closed-T variants
    /// (<c>TemplatedListViewElement&lt;Person&gt;</c>, etc.) should all share
    /// the same handler.</para>
    /// </summary>
    public static void RegisterForDerivedTypes<TBase, TControl>(
        Func<IElementHandler<TBase, TControl>> handlerFactory)
        where TBase : Element
        where TControl : UIElement
    {
        ArgumentNullException.ThrowIfNull(handlerFactory);

        Func<IV1HandlerEntry> adapterFactory = () =>
            new V1HandlerAdapter<TBase, TControl>(handlerFactory());

        if (s_baseEntries.TryAdd(typeof(TBase), adapterFactory))
        {
            // Invalidate any prior "no base match" cache entries so a
            // derived type that previously missed will pick this base up
            // on its next dispatch. Exact-match cache entries don't apply
            // here (s_entries is consulted before s_baseCache).
            s_baseCache.Clear();
        }
    }

    /// <summary>
    /// Spec 048 §3.4 — decorator sibling of <see cref="RegisterForDerivedTypes{TBase,TControl}"/>.
    /// </summary>
    public static void RegisterDecoratorForDerivedTypes<TBase>(
        Func<IDecoratorElementHandler<TBase>> handlerFactory)
        where TBase : Element
    {
        ArgumentNullException.ThrowIfNull(handlerFactory);

        Func<IV1HandlerEntry> adapterFactory = () =>
            new V1DecoratorHandlerAdapter<TBase>(handlerFactory());

        if (s_baseEntries.TryAdd(typeof(TBase), adapterFactory))
        {
            s_baseCache.Clear();
        }
    }

    /// <summary>
    /// Spec §8 — internal resolution hatch the <see cref="Reconciler"/>
    /// consults when its per-host <c>_v1Handlers</c> and per-host
    /// <c>_typeRegistry</c> both miss. On a hit, the caller is responsible
    /// for invoking the returned factory <i>once</i> and caching the result
    /// into its per-host <c>_v1Handlers</c> so subsequent dispatches on the
    /// same host short-circuit before this lookup.
    /// </summary>
    /// <param name="elementType">The exact runtime element type from
    /// <c>element.GetType()</c>.</param>
    /// <param name="entry">When this method returns <see langword="true"/>,
    /// the adapter factory that, when invoked, produces a fresh
    /// <see cref="IV1HandlerEntry"/>. The caller invokes the factory and
    /// caches the result; this method never invokes it itself (callers may
    /// race; the per-host cache handles the de-dup deterministically).</param>
    internal static bool TryResolve(
        Type elementType,
        [NotNullWhen(true)] out Func<IV1HandlerEntry>? entry)
    {
        if (s_entries.TryGetValue(elementType, out entry)) return true;
        // §3.4 base-derived fallback. Skip the walk fast-path when no
        // base-derived entries have ever been registered.
        if (s_baseEntries.IsEmpty) { entry = null; return false; }
        if (s_baseCache.TryGetValue(elementType, out var cached))
        {
            entry = cached;
            return cached is not null;
        }
        for (var t = elementType.BaseType; t is not null && t != typeof(object); t = t.BaseType)
        {
            if (s_baseEntries.TryGetValue(t, out var found))
            {
                s_baseCache.TryAdd(elementType, found);
                entry = found;
                return true;
            }
        }
        // Cache the negative result so subsequent dispatches don't walk
        // the inheritance chain again.
        s_baseCache.TryAdd(elementType, null);
        entry = null;
        return false;
    }

    /// <summary>
    /// Spec §8 — true if a global registration exists for the given element
    /// type. Used by diagnostics; the Reconciler's dispatch path uses
    /// <see cref="TryResolve"/> directly. Reports <i>exact-type</i> entries
    /// only — for the §3.4 base-derived effective-dispatch check use
    /// <see cref="ContainsForType"/>.
    /// </summary>
    internal static bool Contains(Type elementType) => s_entries.ContainsKey(elementType);

    /// <summary>
    /// Spec 048 §3.4 — true if dispatch is wired for <paramref name="elementType"/>
    /// either by an exact-type registration OR by a base-derived registration
    /// on one of its ancestors. Test/diagnostic surface mirroring the
    /// effective behaviour of <see cref="TryResolve"/>.
    /// </summary>
    internal static bool ContainsForType(Type elementType)
        => TryResolve(elementType, out _);

    /// <summary>
    /// Spec 048 §3.4 — true if <paramref name="baseType"/> itself was
    /// registered via <see cref="RegisterForDerivedTypes{TBase,TControl}"/> /
    /// <see cref="RegisterDecoratorForDerivedTypes{TBase}"/>.
    /// </summary>
    internal static bool ContainsBase(Type baseType) => s_baseEntries.ContainsKey(baseType);

}
