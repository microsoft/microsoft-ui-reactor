using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

// Spec 047 §14 Phase 3 prelude — Lazy*Stack ScrollViewer-wrapper closure.
//
// The V1 LazyStackDescriptor port (LazyStackDescriptor.cs) uses ItemsRepeater
// directly as TControl, because a descriptor's RentControl returns a single
// control with no place to install a wrapping ScrollViewer. The legacy
// MountLazyStack instead wraps the ItemsRepeater in a ScrollViewer (with
// orientation-appropriate scrollbars + ScrollViewerSetters). Under V1 ON the
// descriptor path therefore had no ScrollViewer, failing every fixture that
// asserts FindControl<ScrollViewer> (LazyVStack/HStack_ScrollViewer,
// LazyHStack_HScrollEnabled, ScrollPop/Back, HookPaging_Scroll*) and the
// component-cleanup-on-unmount gate (EFR_LazyStackUnmount) since there was no
// ScrollViewer for the engine to recurse through.
//
// Fix: route the Lazy*Stack family through a Path B decorator that runs the
// complete legacy MountLazyStack/UpdateLazyStack bodies (identical to V1 OFF).
// Registered for DERIVED types (LazyVStackElement<T>/LazyHStackElement<T> all
// derive from LazyStackElementBase) via RegisterDecoratorHandlerForDerivedTypes.
// ContinueDefaultTraversal on unmount so the engine falls through to the same
// ScrollViewer -> ItemsRepeater -> realized-row recursion V1 OFF uses, running
// each row component's UseEffect cleanup. The descriptor type is retained for
// its isolated selftests (same pattern as the panel descriptors).

/// <summary>§14 prelude — Lazy*Stack (ScrollViewer-wrapped virtualizing list).</summary>
internal sealed class LazyStackHandler : IDecoratorElementHandler<LazyStackElementBase>
{
    public UIElement Mount(MountContext ctx, LazyStackElementBase el)
        => ctx.Reconciler.MountLazyStack(el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, LazyStackElementBase oldEl, LazyStackElementBase newEl, UIElement control)
        => ctx.Reconciler.UpdateLazyStack(newEl, (WinUI.ScrollViewer)control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, LazyStackElementBase? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}
