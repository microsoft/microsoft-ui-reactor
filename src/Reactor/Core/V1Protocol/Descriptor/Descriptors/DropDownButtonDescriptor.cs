using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 4) — descriptor variant of the hand-coded
/// <c>MountDropDownButton</c> / <c>UpdateDropDownButton</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Label</c> — one-way (Content). DropDownButton's legacy arm
///   has no <c>Click</c> event subscription.</item>
///   <item><c>Flyout</c> — <c>.OneWayBridged&lt;Element?&gt;</c> entry whose
///   set lambda calls <c>Reconciler.CreateFlyoutForDescriptor(v, rr)</c>
///   to produce a <c>FlyoutBase?</c> and assign it to
///   <c>DropDownButton.Flyout</c>. Mirrors the legacy mount arm's flyout
///   construction path (which feeds the same engine-internal
///   <c>CreateFlyoutFromElement</c> helper via the null-tolerant sibling
///   <c>CreateFlyoutForDescriptor</c>). Comparer is
///   <see cref="ElementReferenceComparer"/> (reference identity over
///   <c>Element?</c>) — matches the <c>GridDescriptor</c> definition-rebuild
///   pattern, so the flyout is only torn down + rebuilt when the
///   Flyout element reference actually changes. The default
///   <c>EqualityComparer&lt;Element?&gt;.Default</c> would do record-style
///   structural equality and could miss a Flyout content swap that
///   produced a structurally-equal Element.</item>
/// </list></para>
/// </summary>
internal static class DropDownButtonDescriptor
{
    public static readonly ControlDescriptor<DropDownButtonElement, WinUI.DropDownButton> Descriptor =
        new ControlDescriptor<DropDownButtonElement, WinUI.DropDownButton>
        {
            Children = new None<DropDownButtonElement, WinUI.DropDownButton>(),
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.Label,
            set: static (c, v) => c.Content = v)
        .OneWayBridged<Element?>(
            get:         static e => e.Flyout,
            set:         static (c, v, rec, rr) => c.Flyout = rec.CreateFlyoutForDescriptor(v, rr),
            shouldWrite: static e => e.Flyout is not null,
            comparer:    ElementReferenceComparer.Instance);

}
