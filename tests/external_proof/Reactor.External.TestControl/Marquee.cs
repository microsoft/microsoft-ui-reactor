using System;
using Microsoft.UI.Reactor.Core.V1Protocol;

namespace Reactor.External.TestControl;

/// <summary>
/// Spec 048 §6 (Pattern A) — the public construction holder for the
/// external <see cref="MarqueeElement"/>. Authoring an external Reactor
/// control means shipping three pieces — the element record, the WinUI
/// control, the handler — plus this thin holder type whose static
/// constructor registers the handler against the global
/// <see cref="ControlRegistry"/>.
///
/// <para><b>Why the holder exists (spec §5).</b> The element is pure
/// data, references nothing about its handler or its WinUI control, and
/// the trimmer cannot keep handlers it cannot reach. The factory holder
/// is the static-reference chokepoint: <see cref="Of(string)"/> is the
/// only path by which an app can obtain a <see cref="MarqueeElement"/>
/// (the element's primary constructor is <c>internal</c>), so the
/// trimmer's reachability follows <c>Marquee → cctor → MarqueeHandler →
/// MarqueeControl</c>. An app that never calls
/// <see cref="Of(string)"/> lets the entire chain be removed —
/// satisfying spec 048 §3 requirements 1–4 in a single shape.</para>
///
/// <para><b>Static cctor + <c>static</c> lambda.</b> The CLR guarantees
/// the type initializer runs before the first <see cref="Of(string)"/>
/// returns. <see cref="ControlRegistry.Register{TElement,TControl}"/>
/// is idempotent first-wins, so multiple parallel callers race
/// harmlessly. The <c>static</c> keyword on the lambda is mandatory
/// (spec §6) — it guarantees the delegate is cached in a static field
/// (one allocation, ever) and captures nothing (the closure-allocation
/// path becomes a compile error).</para>
///
/// <para><b>Author surface.</b> External authors mirror this shape for
/// each of their controls. The pattern is small enough to write by hand;
/// when the count grows past a few, Pattern B (<c>Reg&lt;E, C, H&gt;</c>
/// per-closed-type cctor) scales to the ~50-factory built-in catalog.</para>
/// </summary>
public static class Marquee
{
    static Marquee() =>
        ControlRegistry.Register<MarqueeElement, MarqueeControl>(static () => new MarqueeHandler());

    /// <summary>Sole public construction path for <see cref="MarqueeElement"/>.
    /// Calling this guarantees the <see cref="MarqueeHandler"/> has been
    /// registered globally before the returned element can be mounted.</summary>
    public static MarqueeElement Of(string caption) => new(caption);

    /// <summary>Construct a <see cref="MarqueeElement"/> with a
    /// <see cref="MarqueeElement.OnCaptionChanged"/> callback. Same
    /// registration guarantee as <see cref="Of(string)"/>.</summary>
    public static MarqueeElement Of(string caption, Action<string> onCaptionChanged) =>
        new(caption, onCaptionChanged);
}
