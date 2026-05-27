using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 1) — descriptor variant of the hand-coded
/// <c>MountToggleSplitButton</c> / <c>UpdateToggleSplitButton</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>IsChecked</c> — controlled (<c>IsCheckedChanged</c> event,
///   <c>TypedEventHandler&lt;ToggleSplitButton,
///   ToggleSplitButtonIsCheckedChangedEventArgs&gt;</c> bridged to
///   <c>EventHandler&lt;TArgs&gt;</c>, <c>OnIsCheckedChanged</c> callback).
///   ToggleSplitButton's <c>IsChecked</c> is non-nullable <c>bool</c>,
///   so no null-coalesce is needed in read-back.</item>
///   <item><c>Label</c> — one-way (Content).</item>
/// </list></para>
///
/// <para><b>Known gap vs. hand-coded handler:</b> the <c>Flyout</c> child
/// is not yet expressible through the descriptor — descriptor authors
/// pursue flyouts via the setters chain. The legacy arm constructs the
/// flyout in Mount; the descriptor leaves this on the follow-up to the
/// container family port (see §14).</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class ToggleSplitButtonDescriptor
{
    public static readonly ControlDescriptor<ToggleSplitButtonElement, WinUI.ToggleSplitButton> Descriptor =
        new ControlDescriptor<ToggleSplitButtonElement, WinUI.ToggleSplitButton>
        {
            Children = new None<ToggleSplitButtonElement, WinUI.ToggleSplitButton>(),
            GetSetters = static e => e.Setters,
        }
        .Controlled<bool, WinUI.ToggleSplitButtonIsCheckedChangedEventArgs>(
            get:         static e => e.IsChecked,
            set:         static (c, v) => c.IsChecked = v,
            // See ToggleSwitchDescriptor for the closure / CWT-gate invariant.
            subscribe:   static (fe, h) => ((WinUI.ToggleSplitButton)fe).IsCheckedChanged += (s, e) => h(s, e),
            unsubscribe: static (fe, h) => { /* trampoline lives for control lifetime — see CWT gate in PropEntry */ },
            callback:    static e => e.OnIsCheckedChanged,
            readBack:    static c => c.IsChecked)
        .OneWay(
            get: static e => e.Label,
            set: static (c, v) => c.Content = v);
}
