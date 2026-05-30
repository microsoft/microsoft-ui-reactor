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
        => s_entries.TryGetValue(elementType, out entry);

    /// <summary>
    /// Spec §8 — true if a global registration exists for the given element
    /// type. Used by diagnostics; the Reconciler's dispatch path uses
    /// <see cref="TryResolve"/> directly.
    /// </summary>
    internal static bool Contains(Type elementType) => s_entries.ContainsKey(elementType);

    /// <summary>
    /// Test-only hatch — clear the global registry. Production code <b>must
    /// not</b> call this; the global registry is process-wide and its
    /// idempotent first-wins semantics depend on it never being reset.
    /// Exposed via <c>InternalsVisibleTo</c> for the registry unit tests
    /// that need a clean slate per case.
    /// </summary>
    internal static void ResetForTesting() => s_entries.Clear();

    /// <summary>
    /// Test-only diagnostic — number of registered element types. Used by
    /// the registry unit tests to assert idempotence (the count must not
    /// grow on repeat <see cref="Register{TElement,TControl}"/> calls).
    /// </summary>
    internal static int Count => s_entries.Count;
}
