using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 8) — descriptor variant of the hand-coded
/// <c>MountStack</c> / <c>UpdateStack</c> arms in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a zero-event panel container — two unconditional
/// one-way props (<c>Orientation</c>, <c>Spacing</c>) and two
/// <c>OneWayConditional</c> props (<c>HorizontalAlignment</c>,
/// <c>VerticalAlignment</c>). Children are dispatched through the
/// <see cref="Panel{TElement,TControl}"/> strategy which appends on Mount
/// and structurally reconciles index-aligned slots on Update — same shape
/// as the hand-coded path's <c>ReconcileChildren</c> call.</para>
///
/// <para><b>Known gaps:</b> the legacy hand-coded path doesn't apply any
/// per-child attached properties on a <c>StackPanel</c> (Stack has none).
/// Parity holds.</para>
/// </summary>
internal static class StackPanelDescriptor
{
    private static readonly Panel<StackElement, WinUI.StackPanel> ChildrenStrategy =
        new Panel<StackElement, WinUI.StackPanel>(
            GetChildren: static e => e.Children,
            GetCollection: static c => c.Children);

    public static readonly ControlDescriptor<StackElement, WinUI.StackPanel> Descriptor =
        new ControlDescriptor<StackElement, WinUI.StackPanel>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.Orientation,
            set: static (c, v) => c.Orientation = v)
        .OneWay(
            get: static e => e.Spacing,
            set: static (c, v) => c.Spacing = v)
        .OneWayConditional(
            get:         static e => e.HorizontalAlignment,
            set:         static (c, v) => c.HorizontalAlignment = v!.Value,
            shouldWrite: static e => e.HorizontalAlignment.HasValue)
        .OneWayConditional(
            get:         static e => e.VerticalAlignment,
            set:         static (c, v) => c.VerticalAlignment = v!.Value,
            shouldWrite: static e => e.VerticalAlignment.HasValue);
}
