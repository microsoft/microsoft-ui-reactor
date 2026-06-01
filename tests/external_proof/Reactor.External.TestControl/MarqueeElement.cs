using System;
using Microsoft.UI.Reactor.Core;

namespace Reactor.External.TestControl;

/// <summary>
/// Spec 048 §6 (Pattern A) — element record for the external
/// <see cref="MarqueeControl"/>. Inherits from <see cref="Element"/>
/// (Reactor's public element base), carries the value-bearing
/// <see cref="Caption"/> prop, the <see cref="OnCaptionChanged"/>
/// callback, and a <see cref="Setters"/> array (parity with built-in
/// elements — exercises the public <c>ApplySetters</c> through the
/// public <c>MountContext.ApplySetters</c>).
///
/// <para><b>Construction discipline (spec 048 §6).</b> The primary
/// constructor is <c>internal</c> to <c>Reactor.External.TestControl</c>:
/// the sole external construction path is <see cref="Marquee.Of(string)"/>
/// (and its overload). This is not stylistic — it is the trim-story
/// invariant: a <c>new MarqueeElement(...)</c> from outside this
/// assembly would bypass the <see cref="Marquee"/> static cctor's
/// <see cref="Microsoft.UI.Reactor.Core.V1Protocol.ControlRegistry.Register{TElement,TControl}"/>
/// call and dispatch would miss. Closing the constructor makes that
/// shape unrepresentable; an external app must reach the handler/control
/// through <see cref="Marquee"/>, and the trimmer follows the same
/// chain — keeping handler + WinUI control iff the factory is reachable.
/// <see langword="init"/> properties and <c>with</c> expressions remain
/// public because the synthesized <c>Clone</c> method is public and the
/// copy constructor is <c>protected</c>; both surfaces work across the
/// assembly boundary without re-rooting the registration path.</para>
/// </summary>
public sealed record MarqueeElement : Element
{
    public string Caption { get; init; }
    public Action<string>? OnCaptionChanged { get; init; }
    public Action<MarqueeControl>[] Setters { get; init; } = Array.Empty<Action<MarqueeControl>>();

    internal MarqueeElement(string caption, Action<string>? onCaptionChanged = null)
    {
        Caption = caption;
        OnCaptionChanged = onCaptionChanged;
    }
}
