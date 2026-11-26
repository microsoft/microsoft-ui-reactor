using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;
using ReactorSourceMap = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap;

namespace Microsoft.UI.Reactor.Core;

// Reconciler partial: Connected animations (cross-container transitions).
public sealed partial class Reconciler
{
    // ════════════════════════════════════════════════════════════════
    //  Connected animations (cross-container transitions)
    // ════════════════════════════════════════════════════════════════

    // Test-only seam (InternalsVisibleTo Reactor.Tests / Reactor.AppTests.Host): counts
    // connected animations that actually STARTED (ConnectedAnimation.TryStart returned
    // true). A selftest asserts this increments across a source→destination transition;
    // the settled visual tree looks identical whether or not the animation ran, so the
    // start count is the only non-vacuous oracle available to an automated test.
    internal int ConnectedAnimationStartCount;

    // Test-only seam, same visibility: counts unmounting sources that were NOT prepared
    // because WinUI had not yet rendered a frame since their key was last prepared (see
    // PrepareConnectedAnimationSource). The settled tree is the same either way.
    internal int ConnectedAnimationPreparationsSkipped;

    // Keys this reconcile pass actually published a snapshot for. The flush resolves
    // queued destinations only against these, because ConnectedAnimationService is
    // view-wide and unclaimed preparations are deliberately left to expire on their own
    // (~1s): in principle a LATER render mounting the same key could consume one of those
    // and animate unrelated UI from wherever the old source sat.
    //
    // Carried as defence-in-depth, and honestly so: I could not build a NON-VACUOUS
    // regression test for it. A fixture that unmounts a keyed source in one pass and
    // mounts the same key in a later pass reads green with this guard disabled, i.e. WinUI
    // did not hand the stale snapshot over anyway — so the hazard may be theoretical in
    // this WinUI build. The guard is a strict narrowing (it can only prevent starts, never
    // cause one) and costs a hash lookup, so it stays; but it is NOT test-proven, and a
    // future change here should not assume a test would catch a regression.
    //
    // Bookkeeping only. Nothing is invoked on the service, so an unclaimed preparation is
    // never cancelled; see PrepareConnectedAnimationSource for why a cancel can crash.
    private readonly HashSet<string> _preparedConnectedAnimationKeys = new(StringComparer.Ordinal);

    private readonly Dictionary<UIElement, int> _navigationConnectedElements = new();
    internal void ClaimNavigationConnectedElement(UIElement element) =>
        _navigationConnectedElements[element] = _navigationConnectedElements.GetValueOrDefault(element) + 1;
    internal void ReleaseNavigationConnectedElement(UIElement element)
    {
        if (_navigationConnectedElements.GetValueOrDefault(element) <= 1) _navigationConnectedElements.Remove(element);
        else _navigationConnectedElements[element]--;
    }

    /// <summary>
    /// Publishes the source snapshot for an unmounting element, so a destination mounting
    /// under the same key in this pass can travel from it.
    /// </summary>
    /// <remarks>
    /// Preparation is speculative: every unmounting element carrying a key gets a
    /// snapshot, because the reconciler cannot know which sibling the user's interaction
    /// designated as the source. Collapsing a list of N keyed rows to one detail element
    /// therefore leaves N-1 preparations that nothing claims, and WinUI keeps each of
    /// those composited at its source's old position until it times out — roughly a
    /// second of the old list ghosting over the new view.
    ///
    /// <para>That is a known cosmetic wart, deliberately left alone, because WinUI faults
    /// (0xC0000005 at <c>CConnectedAnimationService::PreCommit</c>) when a preparation is
    /// cancelled before it has rendered a frame and its source left the tree by itself.
    /// The source is removed in the same pass it is prepared in, so WinUI retains it for
    /// that frame's commit, and only the preparation gives it a composition node; a cancel
    /// takes the node away and the commit dereferences null (issue #1152). An earlier
    /// revision withdrew unclaimed preparations with <c>ConnectedAnimation.Cancel()</c> at
    /// flush time and crashed this way; <c>ConnectedAnimation_OrphanOnlyPassDoesNotCrash</c>
    /// pins the no-crash behaviour.</para>
    ///
    /// <para>Preparing a key that is still in use cancels its earlier animation the same way.
    /// That happens when two passes run before WinUI renders a frame and both unmount an
    /// element with the key, so a key is not prepared again until WinUI has rendered a frame
    /// since its last preparation (<see cref="ConnectedAnimationFrameGate"/>). The skipped
    /// source does not animate, and the earlier preparation stands. In the round trip that
    /// reaches this (Go, Back, Go before one frame), the skipped source was mounted after
    /// the last frame, so it never reached the screen.
    /// <c>ConnectedAnimation_RepreparedBeforeFrameDoesNotCrash</c> pins this.</para>
    ///
    /// <para>Not covered: WinUI also cancels a started animation when its destination leaves
    /// the tree. A pass that removes a destination before WinUI has rendered a frame since
    /// its animation started can still fault.</para>
    /// </remarks>
    private void PrepareConnectedAnimationSource(string key, UIElement control)
    {
        if (_navigationConnectedElements.ContainsKey(control)) return;
        if (ConnectedAnimationFrameGate.IsAwaitingFrame(key))
        {
            ConnectedAnimationPreparationsSkipped++;
            return;
        }

        try
        {
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate(key, control);
            _preparedConnectedAnimationKeys.Add(key);
            ConnectedAnimationFrameGate.Prepared(key);
            // WinUI captures the source at the next frame, so the pool must not reset or rent it first.
            // Its return still drops the element it hosted, so a callback can't reach the old component.
            ElementPool.ExcludeFromPool(control, releaseOnReturn: true);
        }
        catch (global::System.Runtime.InteropServices.COMException ex) when (Diagnostics.HResults.IsTeardownReentry(ex.HResult))
        {
            Diagnostics.DiagnosticLog.SwallowedError(
                Diagnostics.LogCategory.Reactor, "Reconciler.PrepareConnectedAnimationSource", ex);
        }
    }

    /// <summary>
    /// Queues a connected animation start for an element that was just mounted.
    /// Called from Mount() whenever the element carries a ConnectedAnimationKey.
    /// </summary>
    /// <remarks>
    /// The key is resolved to a <see cref="ConnectedAnimation"/> in
    /// <see cref="FlushConnectedAnimations"/>, NOT here. Mount runs before the unmount
    /// that publishes the source snapshot: the child-reconciler's type-mismatch arm
    /// mounts the replacement first and only then calls
    /// <see cref="ReplaceChildWithExitTransition"/>, which unmounts the old control (and
    /// the keyed/LIS arms mount inserts before removing leftovers). Looking the key up
    /// at mount time therefore observed the service *before*
    /// <c>PrepareToAnimate</c> had run for it, returned null, and silently dropped the
    /// animation — the destination simply appeared at its final position. Deferring the
    /// lookup to the flush, which runs after the whole reconcile pass has completed,
    /// makes the source snapshot visible to the destination regardless of the order the
    /// reconciler happened to visit the two subtrees in.
    /// </remarks>
    internal void QueueConnectedAnimationStart(UIElement target, string key)
        => _pendingConnectedAnimationStarts.Add((key, target));

    /// <summary>
    /// Starts all queued connected animations. Call AFTER the new tree has been attached
    /// to the visual tree (e.g., after Window.Content = newControl).
    /// </summary>
    /// <remarks>
    /// Source and destination must appear in the SAME reconcile pass — that is the shape
    /// Reactor's model produces, since a single state change swaps both subtrees.
    /// </remarks>
    public void FlushConnectedAnimations()
    {
        if (_pendingConnectedAnimationStarts.Count == 0)
            return;

        try
        {
            var service = ConnectedAnimationService.GetForCurrentView();

            foreach (var (key, target) in _pendingConnectedAnimationStarts)
            {
                if (_navigationConnectedElements.ContainsKey(target)) continue;
                try
                {
                    // Scoped to this pass on purpose: the service is view-wide and holds
                    // unclaimed snapshots for ~1s, so an unscoped lookup could let a
                    // later render travel from a stale one. Defence-in-depth — see the
                    // note on _preparedConnectedAnimationKeys about the missing test.
                    if (!_preparedConnectedAnimationKeys.Contains(key))
                        continue;

                    if (service.GetAnimation(key) is { } anim && anim.TryStart(target))
                        ConnectedAnimationStartCount++;
                }
                catch (global::System.Runtime.InteropServices.COMException ex) when (Diagnostics.HResults.IsTeardownReentry(ex.HResult))
                {
                    Diagnostics.DiagnosticLog.SwallowedError(
                        Diagnostics.LogCategory.Reactor, "Reconciler.FlushConnectedAnimations.start", ex);
                }
            }
        }
        catch (global::System.Runtime.InteropServices.COMException ex) when (Diagnostics.HResults.IsTeardownReentry(ex.HResult))
        {
            Diagnostics.DiagnosticLog.SwallowedError(
                Diagnostics.LogCategory.Reactor, "Reconciler.FlushConnectedAnimations", ex);
        }

        _pendingConnectedAnimationStarts.Clear();
        _preparedConnectedAnimationKeys.Clear();
    }
}
