using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 1) — descriptor variant of the hand-coded
/// <c>MountCheckBox</c> / <c>UpdateCheckBox</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>IsChecked</c> — controlled (Checked + Unchecked events,
///   shared trampoline, <c>OnIsCheckedChanged</c> callback). Both events
///   wire to the same <c>EventHandler&lt;RoutedEventArgs&gt;</c> in the
///   subscribe lambda; the trampoline reads back <c>c.IsChecked ?? false</c>
///   so the same bool flows regardless of which event fired.</item>
///   <item><c>Label</c> — one-way (Content).</item>
///   <item><c>IsThreeState</c> — one-way write.</item>
/// </list></para>
///
/// <para><b>Known gaps vs. hand-coded handler:</b>
/// <list type="bullet">
///   <item><b>Three-state mode:</b> when <c>IsThreeState=true</c> the legacy
///   arm writes <c>CheckedState</c> (bool?) and fires
///   <c>OnCheckedStateChanged</c>. The descriptor only writes
///   <c>IsChecked</c> (bool) — three-state authors must continue to use the
///   legacy arm (V1-OFF) until the descriptor is extended.</item>
///   <item><b><c>OnCheckedStateChanged</c> callback:</b> not fired by the
///   descriptor (mirrored gap). The Indeterminate event is not subscribed.</item>
/// </list>
/// These gaps mirror the Phase 3 prereq pattern (see
/// <see cref="TextBoxDescriptor"/>'s snap-back gap) — the bulk-port lands
/// the common case, follow-up work closes the remaining shape.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class CheckBoxDescriptor
{
    public static readonly ControlDescriptor<CheckBoxElement, WinUI.CheckBox> Descriptor =
        new ControlDescriptor<CheckBoxElement, WinUI.CheckBox>
        {
            Children = new None<CheckBoxElement, WinUI.CheckBox>(),
            GetSetters = static e => e.Setters,
        }
        .Controlled<bool, RoutedEventArgs>(
            get:         static e => e.IsChecked,
            set:         static (c, v) => c.IsChecked = v,
            // Two events feed one trampoline — both write IsChecked, both
            // route through `h` which reads back the new state. See
            // ToggleSwitchDescriptor for the closure / CWT-gate invariant.
            subscribe:   static (fe, h) =>
            {
                var cb = (WinUI.CheckBox)fe;
                cb.Checked   += (s, e) => h(s, e);
                cb.Unchecked += (s, e) => h(s, e);
            },
            unsubscribe: static (fe, h) => { /* trampolines live for control lifetime — see CWT gate in PropEntry */ },
            callback:    static e => e.OnIsCheckedChanged,
            readBack:    static c => c.IsChecked ?? false)
        .OneWayConditional(
            get:         static e => e.Label,
            set:         static (c, v) => c.Content = v,
            shouldWrite: static e => e.Label is not null)
        .OneWay(
            get: static e => e.IsThreeState,
            set: static (c, v) => c.IsThreeState = v);
}
