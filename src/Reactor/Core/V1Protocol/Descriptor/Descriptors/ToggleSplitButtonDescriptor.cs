using System.Collections.Generic;
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
///   <item><c>Flyout</c> — <c>.OneWayBridged&lt;Element?&gt;</c> entry whose
///   set lambda calls <c>Reconciler.CreateFlyoutForDescriptor(v, rr)</c>
///   to produce a <c>FlyoutBase?</c> and assign it to
///   <c>ToggleSplitButton.Flyout</c>. Mirrors the legacy mount arm's
///   flyout construction path. Comparer is
///   <see cref="ElementReferenceComparer"/> (reference identity over
///   <c>Element?</c>) — matches the <c>GridDescriptor</c> definition-rebuild
///   pattern, so the flyout is only torn down + rebuilt when the
///   Flyout element reference actually changes.</item>
/// </list></para>
/// </summary>
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
            set: static (c, v) => c.Content = v)
        .OneWayBridged<Element?>(
            get:         static e => e.Flyout,
            set:         static (c, v, rec, rr) => c.Flyout = rec.CreateFlyoutForDescriptor(v, rr),
            shouldWrite: static e => e.Flyout is not null,
            comparer:    ElementReferenceComparer.Instance);

}
