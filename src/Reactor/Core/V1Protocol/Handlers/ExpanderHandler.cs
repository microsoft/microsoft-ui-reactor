using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

// Spec 047 §14 Phase 3 prelude — Expander callback/template closure.
//
// ExpanderDescriptor diverges from the hand-coded MountExpander/UpdateExpander
// bodies for callbacks added/changed on UPDATE (the IsExpanded-changed
// callback, HeaderTemplate, ContentTransitions, ExpandDirection). Symptom
// under V1 ON: ExpanderUpdate_CallbacksFire — an Expander mounted with no
// callback, then updated to add onIsExpandedChanged, never fires the callback
// when IsExpanded is toggled afterward.
//
// Fix: Path B delegate to the complete legacy MountExpander/UpdateExpander
// bodies (identical to V1 OFF). Expander has Header + Content child elements;
// ContinueDefaultTraversal lets the engine's default unmount recursion run
// (parity with V1 OFF). Descriptor retained for isolated selftests.

/// <summary>§14 prelude — Expander (header/content + IsExpanded callback wiring).</summary>
internal sealed class ExpanderHandler : IDecoratorElementHandler<ExpanderElement>
{
    public UIElement Mount(MountContext ctx, ExpanderElement el)
        => ctx.Reconciler.MountExpander(el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, ExpanderElement oldEl, ExpanderElement newEl, UIElement control)
        => ctx.Reconciler.UpdateExpander(oldEl, newEl, (WinUI.Expander)control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, ExpanderElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}
