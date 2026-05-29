using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

/// <summary>
/// Spec 047 §14 Phase 3 prelude — Button port via Path B delegate.
///
/// <para><b>Why delegate, not <c>ButtonDescriptor</c>:</b> the descriptor only
/// expresses the string-<c>Label</c> fast path and ignores
/// <see cref="ButtonElement.ContentElement"/> (a Button hosting a child
/// Element rather than a string). When the type was registered as a
/// descriptor that gap became a real V1-ON regression — controls such as the
/// PropertyGrid expand button render blank under V1 because their element
/// content is dropped. The descriptor cannot cleanly add element content: its
/// <c>None</c> children strategy plus the <c>OneWay(Label → Content)</c> write
/// would conflict with a <c>SingleContent</c> strategy (the engine clears the
/// slot unconditionally and both paths target <c>Content</c>). Delegating to
/// the engine's existing internal <see cref="Reconciler.MountButton"/> /
/// <see cref="Reconciler.UpdateButton"/> bodies runs the COMPLETE legacy
/// implementation (enabled / disabled-focusable dim, Click trampoline,
/// setters AND <c>ContentElement</c> mount + in-place reconcile), so V1 ON is
/// byte-identical to V1 OFF.</para>
///
/// <para><b>Decorator shape for unmount parity:</b> <c>WinUI.Button</c> is a
/// <c>ContentControl</c>. When the V1 flag is OFF the engine recurses into
/// <c>Content</c> in both unmount paths (UnmountRecursive and
/// UnmountAndCollect) so a <c>ContentElement</c> subtree containing a Component
/// runs its <c>UseEffect</c> cleanup. A standard <c>IElementHandler</c> would
/// return <see cref="V1UnmountDisposition.CollectSelf"/> and pool the button
/// WITHOUT recursing — leaking that cleanup. Returning
/// <see cref="V1UnmountDisposition.ContinueDefaultTraversal"/> makes the engine
/// fall through to the same <c>ContentControl</c> recursion V1 OFF uses, so
/// teardown is identical across both flags.</para>
///
/// <para><b>No control substitution:</b> <see cref="Reconciler.UpdateButton"/>
/// always returns <c>null</c> (pure in-place reconcile), so the
/// null-forgiving <c>?? control</c> keeps the existing control identity.</para>
/// </summary>
internal sealed class ButtonHandler : IDecoratorElementHandler<ButtonElement>
{
    public UIElement Mount(MountContext ctx, ButtonElement el)
        => ctx.Reconciler.MountButton(el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, ButtonElement oldEl, ButtonElement newEl, UIElement control)
        => ctx.Reconciler.UpdateButton(oldEl, newEl, (WinUI.Button)control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, ButtonElement? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}
