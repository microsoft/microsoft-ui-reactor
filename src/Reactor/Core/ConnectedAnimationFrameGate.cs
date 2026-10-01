using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Connected-animation keys whose <c>PrepareToAnimate</c> WinUI has not processed yet, per UI
/// thread. Read by <see cref="Reconciler"/> so it never prepares such a key a second time.
/// </summary>
/// <remarks>
/// <para>WinUI does not take a preparation's snapshot when <c>PrepareToAnimate</c> returns. It
/// does so in the next frame's commit. When the source leaves the tree before that frame, which
/// is every source Reactor prepares, WinUI keeps the element in its parent's unloading storage
/// for that commit and lists it in <c>CConnectedAnimationService::m_retainedElements</c>.
/// <c>PreCommit</c> then hides each retained element's composition node, assuming it has one;
/// <c>PostCommit</c> releases them.</para>
///
/// <para>A source that left the tree <i>itself</i>, rather than inside a removed container,
/// owes its composition node to the preparation alone. Cancelling the preparation before that
/// frame clears the requirement, so the frame renders no node for it, and <c>PreCommit</c>
/// dereferences null: <c>0xC0000005</c> at <c>Microsoft.UI.Xaml.dll</c>
/// <c>CConnectedAnimationService::PreCommit+0x81</c> (issue #1152). <c>PrepareToAnimate</c> with
/// a key that is still in use is such a cancel: WinUI cancels the earlier animation before it
/// creates the new one.</para>
///
/// <para>A key therefore stays here from a successful <c>PrepareToAnimate</c> until the next
/// <c>CompositionTarget.Rendered</c>, which WinUI raises only after a frame's
/// <c>PostCommit</c>. A window that renders no frames, minimized for example, keeps its keys
/// until it renders again; until then no commit can fault either.</para>
/// </remarks>
internal static class ConnectedAnimationFrameGate
{
    [ThreadStatic] private static HashSet<string>? t_awaitingFrame;
    [ThreadStatic] private static EventHandler<RenderedEventArgs>? t_onRendered;

    /// <summary>True while WinUI has not rendered a frame since <paramref name="key"/> was prepared.</summary>
    internal static bool IsAwaitingFrame(string key) => t_awaitingFrame?.Contains(key) == true;

    /// <summary>Records a successful <c>PrepareToAnimate</c> for <paramref name="key"/>.</summary>
    internal static void Prepared(string key)
    {
        var keys = t_awaitingFrame ??= new HashSet<string>(StringComparer.Ordinal);
        if (keys.Add(key) && keys.Count == 1)
            CompositionTarget.Rendered += t_onRendered ??= OnRendered;
    }

    private static void OnRendered(object? sender, RenderedEventArgs e)
    {
        CompositionTarget.Rendered -= t_onRendered;
        t_awaitingFrame?.Clear();
    }
}
