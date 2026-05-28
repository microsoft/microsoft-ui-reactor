using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

/// <summary>
/// Spec 047 §14 Phase 3 completion — controls the engine's pool-return /
/// child-traversal decision after a V1 handler's <c>Unmount</c> body
/// runs. Returned from <see cref="IV1HandlerEntry.Unmount"/>; the
/// standard hand-coded + descriptor handlers always return
/// <see cref="CollectSelf"/>, matching the pre-Phase-3-completion
/// behavior. Decorator-style handlers (<see cref="IDecoratorElementHandler{TElement}"/>)
/// return the other dispositions to opt out of pool return / let the
/// engine continue the default unmount traversal into the wrapped child.
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
public enum V1UnmountDisposition
{
    /// <summary>Default for standard handlers. The handler created the
    /// control via <c>RentControl</c>; the engine pools the control
    /// (if poolable) and stops the traversal — children were already
    /// torn down by the handler.</summary>
    CollectSelf,

    /// <summary>The handler's returned control is owned by another
    /// element (e.g., a decorator's wrapped <c>Target</c> child element).
    /// The engine must NOT pool the control and MUST continue the
    /// default unmount traversal as if no V1 handler had claimed it,
    /// so the inner element's own teardown runs.</summary>
    ContinueDefaultTraversal,

    /// <summary>The handler manages teardown itself; the control is a
    /// placeholder/wrapper that should NOT be pooled and whose children
    /// were already torn down by the handler. Engine returns immediately
    /// without pool collection or traversal.</summary>
    SkipPool,
}

/// <summary>
/// Spec 047 §14 Phase 3 completion — decorator-style V1 handler contract
/// for elements whose returned <see cref="UIElement"/> identity may
/// change on update or whose unmount disposition diverges from the
/// standard pool-return.
///
/// <para>Use this contract instead of <see cref="IElementHandler{TElement,TControl}"/>
/// for:</para>
/// <list type="bullet">
///   <item><b>Target-wrapping decorators</b> (Flyout, MenuFlyout,
///   CommandBarFlyout) where the returned <see cref="UIElement"/> IS
///   the user's inner <c>Target</c> child — the control type isn't
///   known until the element is inspected, and may change on update
///   if the user swaps Target's element type.</item>
///   <item><b>Modal lifecycle wrappers</b> (ContentDialog, Popup) where
///   the returned control is a placeholder and the actual modal surface
///   is created lazily or as a sibling.</item>
///   <item><b>Polymorphic mounts</b> (IconElement) where the concrete
///   control type depends on the element's runtime subtype.</item>
///   <item><b>Interop bridges</b> (XamlHost, XamlPage) where the
///   returned control hosts arbitrary user-side XAML.</item>
/// </list>
///
/// <para>Distinct from <see cref="IElementHandler{TElement,TControl}"/>
/// to keep the §13 Q12 "no substitution" invariant on the standard
/// author-facing surface — only built-in V1 ports use this decorator
/// shape. External authors continue to use
/// <see cref="IElementHandler{TElement,TControl}"/>.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
public interface IDecoratorElementHandler<TElement>
    where TElement : Element
{
    /// <summary>Mount the element and return the <see cref="UIElement"/>
    /// the engine installs in the parent's slot. Decorator handlers may
    /// recursively mount inner elements via <c>ctx.MountChild(...)</c>
    /// and return one of them (e.g., FlyoutElement returns its Target's
    /// mounted control).</summary>
    UIElement Mount(MountContext ctx, TElement element);

    /// <summary>Diff old vs. new element and apply updates. Returns the
    /// <see cref="UIElement"/> that should occupy the parent's slot
    /// after the update — typically the same <paramref name="control"/>
    /// instance for an in-place update, or a different instance when
    /// the wrapped target changed type. The engine installs the returned
    /// value in the slot (replacing <paramref name="control"/> if
    /// different).</summary>
    UIElement Update(UpdateContext ctx, TElement oldEl, TElement newEl, UIElement control);

    /// <summary>Unmount the element and return the engine's
    /// post-unmount disposition for the control. The <paramref name="element"/>
    /// is read from the attached state DP at unmount time; it may be
    /// null if the tag was already detached (defensive — should not
    /// happen for well-behaved decorator handlers).</summary>
    V1UnmountDisposition Unmount(UnmountContext ctx, TElement? element, UIElement control);
}
