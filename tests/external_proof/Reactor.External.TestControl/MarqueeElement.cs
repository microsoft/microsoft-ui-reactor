using System;
using Microsoft.UI.Reactor.Core;

namespace Reactor.External.TestControl;

/// <summary>
/// Spec 047 §14 Phase 1 (1.16) — element record for the external
/// <see cref="MarqueeControl"/>. Inherits from <see cref="Element"/>
/// (Reactor's public element base), carries the value-bearing
/// <see cref="Caption"/> prop, the <see cref="OnCaptionChanged"/>
/// callback, and a <see cref="Setters"/> array (parity with built-in
/// elements — exercises the public <c>ApplySetters</c> through the
/// public <c>MountContext.ApplySetters</c>).
/// </summary>
public record MarqueeElement(string Caption, Action<string>? OnCaptionChanged = null) : Element
{
    public Action<MarqueeControl>[] Setters { get; init; } = Array.Empty<Action<MarqueeControl>>();
}
