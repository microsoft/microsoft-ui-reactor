using System;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

/// <summary>
/// Spec 048 §7 — the per-control registration shim used by the
/// factory-as-registration pattern (Pattern B). One <i>closed-generic</i>
/// touch of <see cref="Done"/> from inside a factory method causes the CLR
/// to run the generic type's static constructor exactly once per process,
/// which in turn calls <see cref="ControlRegistry.Register{TElement,TControl}"/>
/// with a <see langword="static"/> lambda that closes over
/// <typeparamref name="THandler"/>.
///
/// <para>The closed-generic frame is the entire purpose of this type: the
/// trimmer sees one rooted instantiation of
/// <c>Reg&lt;TElement, TControl, THandler&gt;</c> for each factory that
/// references it, and each instantiation roots <typeparamref name="THandler"/>
/// (and through it, <typeparamref name="TControl"/>). Factories that are
/// never reachable from the app entry point therefore drop their
/// <c>Reg&lt;…&gt;</c> instantiation, and the handler / control disappear
/// from the trimmed output. This is the mechanism behind spec §11's
/// trim-proof.</para>
///
/// <para><b>Authoring pattern (spec §7):</b>
/// <code>
/// public static TextBlockElement TextBlock(string text)
/// {
///     _ = Reg&lt;TextBlockElement, Microsoft.UI.Xaml.Controls.TextBlock, TextBlockHandler&gt;.Done;
///     return new TextBlockElement(text);
/// }
/// </code>
/// The discard (<c>_ =</c>) is intentional: it forces the field read so the
/// JIT cannot dead-code-eliminate the touch in a Release build, while
/// communicating to the reader that the value is irrelevant — only the
/// side effect of the cctor matters.</para>
///
/// <para><b>One-shot semantics.</b> The CLR guarantees that the static
/// initializer of <c>Reg&lt;TElement, TControl, THandler&gt;</c> runs at
/// most once per process per closed-generic instantiation, with
/// thread-safe before-first-use semantics. The first reader pays the
/// registry insertion cost; every subsequent reader pays a single field
/// load (which the JIT routinely folds into the element-record allocation
/// site — see spec §9 perf claims).</para>
///
/// <para><b>Idempotence under aliasing.</b> Multiple factories may legally
/// touch the same <c>Reg&lt;TElement, TControl, THandler&gt;</c> — e.g.
/// <c>TextBlock()</c>, <c>Heading()</c>, and <c>Subheading()</c> all
/// produce <c>TextBlockElement</c> (spec §10.3). Each call site sees the
/// same closed-generic frame, so the cctor still fires only once. If two
/// distinct factories instead touched
/// <c>Reg&lt;TextBlockElement, TextBlock, HandlerA&gt;</c> and
/// <c>Reg&lt;TextBlockElement, TextBlock, HandlerB&gt;</c>, each cctor
/// would fire once and the registry's idempotent first-wins TryAdd
/// (spec §8) would silently keep the first registration — the second
/// handler is harmlessly dropped on the floor. This matches spec §12.1.</para>
/// </summary>
/// <typeparam name="TElement">The element record type the handler
/// dispatches against. The dispatch key in
/// <see cref="ControlRegistry"/>.</typeparam>
/// <typeparam name="TControl">The WinUI control the handler mounts.</typeparam>
/// <typeparam name="THandler">The handler implementation type. Must have a
/// public parameterless constructor — the registration site has no way to
/// thread constructor arguments, by design (spec §6: handlers are stateless
/// w.r.t. registration).</typeparam>
internal static class Reg<TElement, TControl, THandler>
    where TElement : Element
    where TControl : UIElement
    where THandler : IElementHandler<TElement, TControl>, new()
{
    // Explicit (empty) static constructor — disables the C# compiler's
    // `beforefieldinit` flag and binds initialization to "precise
    // before-first-use" semantics (ECMA-335 §I.8.9.5). Without this, the
    // CLR is free to run Init() at any time before the first read of
    // Done — including during the JIT of an unrelated method that merely
    // references this closed generic — which would weaken the documented
    // "first factory touch triggers registration" guarantee below.
    static Reg() { }

    /// <summary>
    /// Spec §7 — the static-field touch that drives Pattern B registration.
    /// Reading this field once on a fresh closed-generic instantiation
    /// triggers the closed generic's precise before-first-use cctor, which
    /// runs <see cref="Init"/> and registers the handler factory with the
    /// global <see cref="ControlRegistry"/>. The actual <see cref="byte"/>
    /// value is unused — the field is a side-effect carrier sized for
    /// minimum per-closed-generic static-data footprint.
    /// </summary>
    internal static readonly byte Done = Init();

    private static byte Init()
    {
        // STATIC-LAMBDA MANDATE (spec §6 ¶ "The static keyword on the lambda
        // is mandatory"). Do NOT change this to a capturing lambda. The
        // static keyword guarantees:
        //   1. No closure object is allocated — the delegate is interned in
        //      a hidden static field on this generic type. Spec §9's
        //      per-factory cost claim (one field load + one already-jitted
        //      delegate call) depends on this.
        //   2. The trimmer's static-reference graph from the call site
        //      reaches THandler through exactly one frame (this Init()),
        //      keeping the rooted-iff-reachable property crisp. A
        //      capturing lambda inserts an opaque display-class frame the
        //      analyzer may not see through cleanly.
        ControlRegistry.Register<TElement, TControl>(static () => new THandler());
        return 1;
    }
}
