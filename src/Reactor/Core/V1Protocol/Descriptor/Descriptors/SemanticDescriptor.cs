using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.Accessibility;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 completion — descriptor variant of the hand-coded
/// <c>MountSemantic</c> / <c>UpdateSemantic</c> accessibility wrapper.
///
/// <para><b>Coverage:</b> projects <see cref="SemanticDescription"/> onto
/// <see cref="SemanticPanel"/> and reconciles the wrapped child via
/// <see cref="SingleContent{TElement,TControl}"/> so descendant state is
/// preserved across wrapper updates.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class SemanticDescriptor
{
    private static readonly SingleContent<SemanticElement, SemanticPanel> ChildrenStrategy =
        new SingleContent<SemanticElement, SemanticPanel>(
            GetChild: static e => e.Child,
            SetChild: static (panel, ui) =>
            {
                panel.Children.Clear();
                if (ui is not null) panel.Children.Add(ui);
            })
        {
            GetCurrentChild = static panel => panel.Children.Count > 0 ? panel.Children[0] : null,
        };

    public static readonly ControlDescriptor<SemanticElement, SemanticPanel> Descriptor =
        new ControlDescriptor<SemanticElement, SemanticPanel>
        {
            Children = ChildrenStrategy,
        }
        .OneWay(
            get: static e => e.Semantics.Role,
            set: static (c, v) => c.SemanticRole = v)
        .OneWay(
            get: static e => e.Semantics.Value,
            set: static (c, v) => c.SemanticValue = v)
        .OneWay(
            get: static e => e.Semantics.RangeMin ?? 0.0,
            set: static (c, v) => c.RangeMinimum = v)
        .OneWay(
            get: static e => e.Semantics.RangeMax ?? 0.0,
            set: static (c, v) => c.RangeMaximum = v)
        .OneWay(
            get: static e => e.Semantics.RangeValue ?? 0.0,
            set: static (c, v) => c.RangeValue = v)
        .OneWay(
            get: static e => e.Semantics.IsReadOnly,
            set: static (c, v) => c.IsReadOnly = v);
}
