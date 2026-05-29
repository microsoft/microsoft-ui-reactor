using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

// Spec 047 §14 Phase 3 prelude — typed templated-list closure
// (ListView<T> / GridView<T> / FlipView<T>).
//
// The descriptor ports (TemplatedListViewDescriptor / TemplatedGridViewDescriptor
// / TemplatedFlipViewDescriptor, registered for derived types) do not run the
// legacy move/reorder animation pipeline. Legacy UpdateTemplatedListView
// (Reconciler.Update.cs) calls ApplyMoveAnimations(lvb, movedRows, ambient.Kind)
// so a keyed reorder under Animations.Animate(Spring, ...) attaches the
// Offset implicit animation to the moved container's Visual. Symptom under
// V1 ON: AAF_MoveSpring_OffsetImplicitAttached — the moved row's container
// never receives ImplicitAnimations["Offset"].
//
// Fix: a single Path B decorator registered on the common base
// TemplatedListElementBase (all three typed bases derive from it), replacing
// the three per-kind descriptor-for-derived registrations. Mount delegates to
// MountTemplatedList (which switches on TemplatedControlKind to produce the
// right WinUI control); Update dispatches to the matching legacy update body
// by control type (mirrors the legacy reconcile switch). Identical to V1 OFF.
// ContinueDefaultTraversal on unmount; ListViewBase container recycling is
// owned by the legacy recycle path, same as V1 OFF. Descriptors retained for
// isolated selftests.

/// <summary>§14 prelude — typed templated lists (ListView/GridView/FlipView&lt;T&gt;).</summary>
internal sealed class TemplatedListHandler : IDecoratorElementHandler<TemplatedListElementBase>
{
    public UIElement Mount(MountContext ctx, TemplatedListElementBase el)
        => ctx.Reconciler.MountTemplatedList(el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, TemplatedListElementBase oldEl, TemplatedListElementBase newEl, UIElement control)
    {
        var rr = ctx.RequestRerender;
        UIElement? replacement = control switch
        {
            WinUI.GridView gv => ctx.Reconciler.UpdateTemplatedGridView(oldEl, newEl, gv, rr),
            WinUI.FlipView fv => ctx.Reconciler.UpdateTemplatedFlipView(oldEl, newEl, fv, rr),
            WinUI.ListView lv => ctx.Reconciler.UpdateTemplatedListView(oldEl, newEl, lv, rr),
            _ => throw new InvalidOperationException(
                $"TemplatedListHandler: unexpected control type {control.GetType().Name} for element {newEl.GetType().Name}."),
        };
        return replacement ?? control;
    }

    public V1UnmountDisposition Unmount(UnmountContext ctx, TemplatedListElementBase? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}
