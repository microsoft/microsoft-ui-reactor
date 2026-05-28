using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 8) — descriptor variant of the hand-coded
/// <c>MountRelativePanel</c> / <c>UpdateRelativePanel</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b> a zero-event, zero-prop panel container — the
/// element record itself carries no props beyond <c>Children</c> and
/// <c>Setters</c>. Children are dispatched through the
/// <see cref="Panel{TElement,TControl}"/> strategy.</para>
///
/// <para><b>Known gap — carved to Phase 4:</b> the legacy hand-coded path
/// executes a two-pass resolution to apply
/// <see cref="RelativePanelAttached"/> attached properties (RightOf / Below
/// / AlignLeftWith / AlignWithPanel etc.) using a name → control map built
/// up across all children. Phase 3-final Batch A's
/// <see cref="Panel{TElement,TControl}.PerChildAttached"/> hook fires
/// per-child sequentially — at the moment any given child's callback
/// fires, later siblings haven't been mounted yet, so name references
/// like <c>RightOf="foo"</c> can't resolve. A correct port requires
/// either a post-loop second-pass shape on <see cref="Panel{TElement,TControl}"/>
/// or a dedicated <c>NamedRelativePanel&lt;…&gt;</c> strategy; both are
/// out of Batch E's scope. Pure-children scenarios remain at parity;
/// authors who depend on <c>RelativePanelAttached</c> stay on V1 OFF
/// (legacy arm) until the Phase 4 follow-up.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class RelativePanelDescriptor
{
    private static readonly Panel<RelativePanelElement, WinUI.RelativePanel> ChildrenStrategy =
        new Panel<RelativePanelElement, WinUI.RelativePanel>(
            GetChildren: static e => e.Children,
            GetCollection: static c => c.Children);

    public static readonly ControlDescriptor<RelativePanelElement, WinUI.RelativePanel> Descriptor =
        new ControlDescriptor<RelativePanelElement, WinUI.RelativePanel>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        };
}
