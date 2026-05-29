using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 1) — descriptor variant of the hand-coded
/// <c>MountRadioButton</c> / <c>UpdateRadioButton</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>IsChecked</c> — controlled (Checked + Unchecked events,
///   shared trampoline, <c>OnIsCheckedChanged</c> callback). RadioButton
///   inherits <c>bool?</c> IsChecked from <see cref="WinUI.Primitives.ToggleButton"/>;
///   the descriptor's read-back coerces null to false (matches the legacy
///   arm's two-event handler shape).</item>
///   <item><c>Label</c> — one-way (Content).</item>
///   <item><c>GroupName</c> — one-way conditional (write only when non-null;
///   matches the hand-coded arm).</item>
/// </list></para>
/// </summary>
internal static class RadioButtonDescriptor
{
    public static readonly ControlDescriptor<RadioButtonElement, WinUI.RadioButton> Descriptor =
        new ControlDescriptor<RadioButtonElement, WinUI.RadioButton>
        {
            Children = new None<RadioButtonElement, WinUI.RadioButton>(),
            GetSetters = static e => e.Setters,
        }
        .Controlled<bool, RoutedEventArgs>(
            get:         static e => e.IsChecked,
            set:         static (c, v) => c.IsChecked = v,
            subscribe:   static (fe, h) =>
            {
                var rb = (WinUI.RadioButton)fe;
                rb.Checked   += (s, e) => h(s, e);
                rb.Unchecked += (s, e) => h(s, e);
            },
            unsubscribe: static (fe, h) => { /* trampolines live for control lifetime — see CWT gate in PropEntry */ },
            callback:    static e => e.OnIsCheckedChanged,
            readBack:    static c => c.IsChecked ?? false)
        .OneWay(
            get: static e => e.Label,
            set: static (c, v) => c.Content = v)
        .OneWayConditional(
            get:         static e => e.GroupName,
            set:         static (c, v) => c.GroupName = v,
            shouldWrite: static e => e.GroupName is not null);
}
