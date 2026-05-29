using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 2 (Q1 spike) — descriptor variant of the Phase 1
/// hand-coded <see cref="V1Protocol.Handlers.ToggleSwitchHandler"/>.
///
/// <para>Drives the §13 Q1 head-to-head: same element record, same v1
/// protocol surface, same dispatch shell — only the body differs. Any
/// measured delta vs. the hand-coded port is attributable to the
/// descriptor interpreter (entry list iteration + closure dispatch +
/// per-control CWT subscription gate).</para>
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>IsOn</c> — controlled (Toggled event, OnIsOnChanged callback).</item>
///   <item><c>OnContent</c> / <c>OffContent</c> — one-way (string props).</item>
///   <item><c>Header</c> — one-way conditional (write only when non-null,
///   matching the hand-coded gate so default Header behavior survives).</item>
///   <item><c>Setters</c> — applied after every prop entry runs.</item>
/// </list></para>
///
/// <para><b>Event wiring path:</b> the controlled-entry path uses the
/// same internal <c>GetOrCreateControlEventPayload&lt;T&gt;</c> trampoline
/// storage the hand-coded handlers use — see
/// <see cref="PropEntry{TElement,TControl}"/>'s
/// <c>DescriptorControlledPayload</c> fast path. Zero per-mount closures
/// for the trampoline itself; the per-control subscription is gated by a
/// typed-payload null check identical in shape to
/// <c>ToggleSwitchHandler.EnsureToggledWiring</c>. The residual
/// descriptor-vs-handler gap on M2 (~+9.6%, see
/// <c>docs/specs/047/phase2-results/.../2026-05-26-q1-fastpath-3x5-stableac/</c>)
/// is intrinsic interpreter overhead (virtual <c>PropEntry.Mount</c>
/// dispatch + getter/setter delegate invocations), not the event path.</para>
/// </summary>
internal static class ToggleSwitchDescriptor
{
    public static readonly ControlDescriptor<ToggleSwitchElement, WinUI.ToggleSwitch> Descriptor =
        new ControlDescriptor<ToggleSwitchElement, WinUI.ToggleSwitch>
        {
            Children = new None<ToggleSwitchElement, WinUI.ToggleSwitch>(),
            GetSetters = static e => e.Setters,
        }
        .Controlled<bool, RoutedEventArgs>(
            get:         static e => e.IsOn,
            set:         static (c, v) => c.IsOn = v,
            // The inner closure `(s, e) => h(s, e)` bridges
            // EventHandler<RoutedEventArgs> -> RoutedEventHandler; it cannot
            // be removed because the two delegate types are unrelated. The
            // closure is allocated once per Toggled subscription and the
            // unsubscribe lambda is a no-op, so this is only safe because
            // PropEntry's CWT-gated DescriptorControlledPayload ensures
            // subscribe runs exactly once per control lifetime. If that
            // gate ever changes, this lambda will silently leak handlers.
            subscribe:   static (fe, h) => ((WinUI.ToggleSwitch)fe).Toggled += (s, e) => h(s, e),
            unsubscribe: static (fe, h) => { /* trampoline lives for control lifetime — see CWT gate in PropEntry */ },
            callback:    static e => e.OnIsOnChanged,
            readBack:    static c => c.IsOn)
        .OneWay(
            get: static e => e.OnContent,
            set: static (c, v) => c.OnContent = v)
        .OneWay(
            get: static e => e.OffContent,
            set: static (c, v) => c.OffContent = v)
        .OneWayConditional(
            get:         static e => e.Header,
            set:         static (c, v) => c.Header = v,
            shouldWrite: static e => e.Header is not null);
}
