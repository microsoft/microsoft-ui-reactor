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

// Reconciler partial: ErrorBoundary child mount journaling and rollback, and isolated unmount cleanup (issue #1291).
public sealed partial class Reconciler
{
    private int _errorBoundaryDepth;

    /// <summary>
    /// Component and error-boundary wrappers registered while an <see cref="ErrorBoundaryElement"/>
    /// mounts or retries its child (issue #1291). <c>null</c> when no boundary is doing so,
    /// so ordinary mounts pay nothing. When the child throws part-way, the partly built
    /// subtree is discarded without ever being attached, so normal unmount can never reach
    /// it; <see cref="RollBackBoundaryMount"/> uses this record to run its components' effect
    /// cleanups instead of leaving their subscriptions alive until host disposal.
    /// </summary>
    private List<UIElement>? _boundaryMountJournal;

    /// <summary>
    /// Controls whose <see cref="Mount"/> completed while an <see cref="ErrorBoundaryElement"/>
    /// mounts or retries its child (issue #1291), collapsed as they nest: when a mount
    /// completes, the roots its children left are replaced by its own control, which reaches
    /// them through the normal unmount walk. <c>null</c> when no boundary is doing so. After a
    /// failure, the roots past the boundary's start are the completed subtrees the partly
    /// built child left behind; <see cref="RollBackBoundaryMount"/> unmounts them, which tears
    /// down everything a finished control registered (interaction states, keyframe and
    /// scroll animations, reference edges, <c>.OnUnmount</c>, handler state), not only the
    /// entries in <see cref="_boundaryMountJournal"/>.
    /// </summary>
    private List<UIElement>? _boundaryMountRoots;

    /// <summary>What <see cref="BeginBoundaryMount"/> started and <see cref="EndBoundaryMount"/> restores.</summary>
    private readonly record struct BoundaryMountRecord(
        List<UIElement>? OuterJournal, List<UIElement> Journal, int RootsStart, bool OwnsRoots);

    /// <summary>
    /// Starts recording registrations and completed roots for a boundary's child.
    /// </summary>
    private BoundaryMountRecord BeginBoundaryMount()
    {
        var outer = _boundaryMountJournal;
        var journal = new List<UIElement>();
        _boundaryMountJournal = journal;
        bool ownsRoots = _boundaryMountRoots is null;
        _boundaryMountRoots ??= new List<UIElement>();
        return new BoundaryMountRecord(outer, journal, _boundaryMountRoots.Count, ownsRoots);
    }

    /// <summary>
    /// Restores the enclosing record. A child that mounted successfully belongs to the
    /// enclosing boundary's subtree too, so its registrations move up: if that boundary's
    /// child fails later, they are discarded with it.
    /// </summary>
    private void EndBoundaryMount(BoundaryMountRecord record)
    {
        _boundaryMountJournal = record.OuterJournal;
        if (record.OuterJournal is not null && record.Journal.Count > 0)
            record.OuterJournal.AddRange(record.Journal);
        record.Journal.Clear();
        if (record.OwnsRoots)
            _boundaryMountRoots = null;
    }

    /// <summary>True when the boundary's child left anything to roll back.</summary>
    private bool HasBoundaryMountLeftovers(BoundaryMountRecord record) =>
        record.Journal.Count > 0 || (_boundaryMountRoots?.Count ?? 0) > record.RootsStart;

    /// <summary>
    /// Called when a <see cref="Mount"/> completes: its control replaces the roots its
    /// children recorded, since unmounting it reaches them. No-op unless a boundary is
    /// recording.
    /// </summary>
    private void NoteMountCompleted(int rootsStart, UIElement? control)
    {
        if (_boundaryMountRoots is not { } roots || rootsStart < 0) return;
        if (roots.Count > rootsStart)
            roots.RemoveRange(rootsStart, roots.Count - rootsStart);
        if (control is not null)
            roots.Add(control);
    }

    /// <summary>
    /// Discards what a failed boundary child left behind, deepest first: unmounts the
    /// subtrees that completed (see <see cref="_boundaryMountRoots"/>), then drops the
    /// remaining wrappers of mounts that never completed from the node tables, running their
    /// components' effect cleanups and their <c>.OnUnmount</c> actions and tearing down
    /// navigation hosts. <paramref name="attachedChild"/> is the boundary's previous child on
    /// a retry, which the caller unmounts itself: completed subtrees attached under it are
    /// left to that unmount. Entries already handled by the normal unmount path are skipped,
    /// so nothing runs twice. Every entry is processed even when one fails: failures are
    /// logged, and one the app declined via <see cref="RenderError.Propagate"/> is rethrown
    /// once all have run.
    /// </summary>
    private void RollBackBoundaryMount(BoundaryMountRecord record, UIElement? attachedChild)
    {
        var journal = record.Journal;
        List<UIElement>? orphans = null;
        if (_boundaryMountRoots is { } roots && roots.Count > record.RootsStart)
        {
            orphans = roots.GetRange(record.RootsStart, roots.Count - record.RootsStart);
            roots.RemoveRange(record.RootsStart, orphans.Count);
        }

        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? propagated = null;
        void OnCleanupError(Exception ex)
        {
            if (RenderErrorDispatch.IsPropagating(ex))
                propagated ??= global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
            else
                _logger?.LogWarning(ex, "Teardown threw while discarding a failed ErrorBoundary child");
        }

        using (IsolateUnmountCleanupFailures(OnCleanupError))
        {
            if (orphans is not null)
            {
                for (int i = orphans.Count - 1; i >= 0; i--)
                {
                    var orphan = orphans[i];
                    if (attachedChild is not null && IsAttachedUnder(orphan, attachedChild))
                        continue;
                    try
                    {
                        UnmountRecursive(orphan);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                    {
                        OnCleanupError(ex);
                    }
                }
            }

            for (int i = journal.Count - 1; i >= 0; i--)
            {
                var wrapper = journal[i];
                _errorBoundaryNodes.Remove(wrapper);
                if (_componentNodes.Remove(wrapper, out var node))
                {
                    if (Diagnostics.ReactorEventSource.Log.IsEnabled(
                            global::System.Diagnostics.Tracing.EventLevel.Informational,
                            Diagnostics.ReactorEventSource.Keywords.Lifecycle))
                    {
                        Diagnostics.ReactorEventSource.Log.ComponentUnmount(
                            Diagnostics.ComponentNames.For(node.Component, node.Element));
                    }
                    if (node.DiagnosticId != 0)
                        Diagnostics.ComponentRenderControls.Registry.Forget(node.DiagnosticId, wrapper);
                    RunUnmountCleanups(node);
                }
                if (wrapper is FrameworkElement fe)
                    RunUnmountAction(fe);
                if (_navigationHostNodes.ContainsKey(wrapper))
                    CleanupNavigationHostNode(wrapper);
            }
        }
        journal.Clear();
        propagated?.Throw();
    }

    /// <summary>Records a registration made while a boundary child mounts; see <see cref="_boundaryMountJournal"/>.</summary>
    internal void NoteBoundaryMountRegistration(UIElement control) => _boundaryMountJournal?.Add(control);

    /// <summary>
    /// When set, unmount runs every component cleanup, <c>.OnUnmount</c> action and control
    /// handler / descriptor unmount hook and hands failures here instead of stopping at the
    /// first one (issue #1291). Used where a
    /// teardown must complete regardless — replacing or releasing a tree for a render-error
    /// outcome, rolling back a failed boundary child, and sweeping a templated list's parked
    /// containers — because nothing could finish the job later. <c>null</c> keeps the normal
    /// behaviour: the first failure escapes.
    /// </summary>
    private Action<Exception>? _unmountCleanupErrorSink;
    private readonly object _unmountActionOwner = new();

    internal IsolatedUnmountScope IsolateUnmountCleanupFailures(Action<Exception> onError)
    {
        var previous = _unmountCleanupErrorSink;
        _unmountCleanupErrorSink = onError;
        return new IsolatedUnmountScope(this, previous);
    }

    internal readonly struct IsolatedUnmountScope(Reconciler owner, Action<Exception>? previous) : IDisposable
    {
        public void Dispose() => owner._unmountCleanupErrorSink = previous;
    }

    /// <summary>Whether an <see cref="IsolateUnmountCleanupFailures"/> scope is collecting failures.</summary>
    internal bool IsolatesUnmountCleanupFailures => _unmountCleanupErrorSink is not null;

    /// <summary>Hands a teardown failure to the active isolation scope (a control handler's or
    /// descriptor's unmount hook, which runs outside <see cref="RunUnmountCleanups"/>).</summary>
    internal void ReportIsolatedUnmountFailure(Exception ex) => _unmountCleanupErrorSink?.Invoke(ex);

    private void RunUnmountCleanups(ComponentNode node)
    {
        if (_unmountCleanupErrorSink is { } sink)
        {
            node.Component?.Context.RunCleanupsIsolated(sink);
            node.Context?.RunCleanupsIsolated(sink);
        }
        else
        {
            node.Component?.Context.RunCleanups();
            node.Context?.RunCleanups();
        }
    }

    private void RunUnmountAction(FrameworkElement control)
    {
        if (!TryGetReactorState(control, out var state)
            || !ReferenceEquals(state.UnmountActionOwner, _unmountActionOwner)
            || state.UnmountAction is not { } onUnmount)
            return;
        state.UnmountAction = null;
        state.UnmountActionOwner = null;
        if (_unmountCleanupErrorSink is not { } sink)
        {
            onUnmount(control);
            return;
        }
        try
        {
            onUnmount(control);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            sink(ex);
        }
    }

    // True when control sits in ancestor's subtree (logical parent first, then visual).
    private static bool IsAttachedUnder(UIElement control, UIElement ancestor)
    {
        DependencyObject? current = control;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor)) return true;
            current = (current as FrameworkElement)?.Parent ?? VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    /// <summary>
    /// <see cref="RollBackBoundaryMount"/> for a child discarded because an exception is
    /// escaping the boundary: cleanup failures are logged, never thrown, so they cannot
    /// replace the exception that is already unwinding.
    /// </summary>
    private void RollBackDiscardedBoundaryMount(BoundaryMountRecord record, UIElement? attachedChild)
    {
        try
        {
            RollBackBoundaryMount(record, attachedChild);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _logger?.LogWarning(ex, "Effect cleanup threw while discarding an ErrorBoundary child after an escaping exception");
        }
    }
}
