using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Microsoft.UI.Reactor.Navigation;

/// <summary>
/// Owns one navigation's native connected animation. Unlike the unmount/mount
/// modifier path, both pages stay alive until this operation finishes.
/// </summary>
internal sealed class ConnectedNavigationOperation : IDisposable
{
    private readonly Core.Reconciler _reconciler;
    private readonly UIElement _outgoing;
    private readonly UIElement _incoming;
    private readonly Action _complete;
    private readonly NavigationMode _mode;
    private readonly NavigationTransition _pageTransition;
    private readonly string _key;
    private readonly List<UIElement> _claimed = new();
    private readonly long _generation;
    private readonly DispatcherQueueTimer _deadline;
    private ConnectedAnimation? _animation;
    private UIElement? _target;
    private int _framesRemaining = 8;
    private bool _finished;
    private bool _started;
    private ConnectedNavigationCompletion _completion;
    private TransitionEngine.HitTestSuppression? _outgoingSuppression;
    private TransitionEngine.HitTestSuppression? _incomingSuppression;
    private CompositionScopedBatch? _pageAnimationBatch;
    private Action? _fallbackComplete;
    internal bool IsCompleted { get; private set; }
    internal string? NativeAnimationKey { get; private set; }
    internal CompositionScopedBatch? PageAnimationBatch => _pageAnimationBatch;
    private static long s_nextId;

    internal ConnectedNavigationOperation(
        Core.Reconciler reconciler, UIElement outgoing, UIElement incoming,
        string key, NavigationTransition pageTransition, NavigationMode mode, Action complete)
    {
        _reconciler = reconciler;
        _outgoing = outgoing;
        _incoming = incoming;
        _mode = mode;
        _pageTransition = pageTransition;
        _key = key;
        _complete = complete;
        _generation = TransitionEngine.ClaimForTransition(outgoing, incoming);
        Core.ElementPool.MarkCompositorTainted(outgoing);
        Core.ElementPool.MarkCompositorTainted(incoming);
        // A previous slide/drill-in may still own compositor properties or
        // hit-test suppression on a page now reused by this navigation.
        TransitionEngine.NormalizeVisual(ElementCompositionPreview.GetElementVisual(outgoing));
        TransitionEngine.NormalizeVisual(ElementCompositionPreview.GetElementVisual(incoming));
        TransitionEngine.ClearHitTestSuppression(outgoing);
        TransitionEngine.ClearHitTestSuppression(incoming);
        ElementCompositionPreview.GetElementVisual(incoming).Opacity = 0;
        var sources = Collect(reconciler, outgoing);
        var targets = Collect(reconciler, incoming);
        var source = FindUnique(sources, key, out _);
        _target = FindUnique(targets, key, out var ambiguousTarget);

        // Suppress ALL keyed children of these pages, not only the selected hero.
        // Otherwise finalizing a list would prepare unpicked rows and leave ghosts.
        foreach (var element in sources) Claim(element);
        foreach (var element in targets) Claim(element);

        _deadline = incoming.DispatcherQueue.CreateTimer();
        _deadline.Interval = TimeSpan.FromSeconds(3);
        _deadline.IsRepeating = false;
        _deadline.Tick += OnDeadline;

        if (!TransitionEngine.AnimationsEnabled)
        {
            Finish(fallback: false);
            return;
        }

        // A missing destination may simply be waiting for its template or first
        // component effect. Only an invalid source/key or an ambiguous target is
        // terminal here; destination discovery participates in the layout wait.
        if (source is null || ambiguousTarget || string.IsNullOrWhiteSpace(key))
        {
            Finish(fallback: true);
            return;
        }

        try
        {
            // Give each transaction its own native key: user keys are page-local,
            // whereas ConnectedAnimationService is shared by the whole view.
            var nativeKey = "Reactor.Navigation." + global::System.Threading.Interlocked.Increment(ref s_nextId);
            // Materialize and retain composition nodes before preparing. These
            // controls must not return to the pool after direct compositor access.
            ElementCompositionPreview.GetElementVisual(source);
            Core.ElementPool.MarkCompositorTainted(source);
            _animation = ConnectedAnimationService.GetForCurrentView().PrepareToAnimate(nativeKey, source);
            NativeAnimationKey = nativeKey;
            _animation.Configuration = mode == NavigationMode.Pop
                ? new DirectConnectedAnimationConfiguration()
                : new GravityConnectedAnimationConfiguration();
            _animation.Completed += OnCompleted;
            CompositionTarget.Rendering += OnRendering;
            _deadline.Start();
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            Core.Diagnostics.DiagnosticLog.SwallowedError(
                Core.Diagnostics.LogCategory.Navigation, "ConnectedNavigation.Prepare", ex);
            Finish(fallback: true);
        }
    }

    internal static List<UIElement> Collect(Core.Reconciler reconciler, UIElement root)
    {
        var result = new List<UIElement>();
        var pending = new Stack<DependencyObject>();
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        Action<UIElement> pushChild = child => pending.Push(child);
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current)) continue;
            // The selected page can contain an active nested NavigationHost
            // (for example, a shell retained beneath a transfer overlay).
            // Its live descendants participate; cached pages are not attached.
            if (current is UIElement element
                && Core.Reconciler.GetElementTag(element)?.ConnectedAnimationKey is not null)
                result.Add(element);
            for (var i = VisualTreeHelper.GetChildrenCount(current) - 1; i >= 0; i--)
                pending.Push(VisualTreeHelper.GetChild(current, i));
            if (current is UIElement mounted)
                reconciler.VisitMountedChildControls(mounted, pushChild);
        }
        return result;
    }

    private static UIElement? FindUnique(List<UIElement> elements, string key, out bool ambiguous)
    {
        ambiguous = false;
        UIElement? match = null;
        foreach (var element in elements)
        {
            if (!string.Equals(Core.Reconciler.GetElementTag(element)?.ConnectedAnimationKey, key, StringComparison.Ordinal))
                continue;
            if (match is not null)
            {
                ambiguous = true;
                return null;
            }
            match = element;
        }
        return match;
    }

    private void Claim(UIElement element)
    {
        if (_claimed.Contains(element)) return;
        _claimed.Add(element);
        _reconciler.ClaimNavigationConnectedElement(element);
    }

    private void OnRendering(object? sender, object args)
    {
        if (_finished || _started) return;
        try
        {
            // Re-resolve while waiting: Loaded/effects can create or replace the
            // selected card after the page's initial mount, even inside a template.
            var candidates = Collect(_reconciler, _incoming);
            foreach (var candidate in candidates) Claim(candidate);
            _target = FindUnique(candidates, _key, out var ambiguous);
            if (ambiguous)
            {
                Finish(fallback: true);
                return;
            }
            if (_target is FrameworkElement { XamlRoot: not null, ActualWidth: > 0, ActualHeight: > 0 } destination
                && ReferenceEquals(destination.XamlRoot, _outgoing.XamlRoot)
                && _animation is { } animation)
            {
                ElementCompositionPreview.GetElementVisual(destination);
                Core.ElementPool.MarkCompositorTainted(destination);
                if (!animation.TryStart(destination))
                {
                    if (--_framesRemaining == 0) Finish(fallback: true);
                    return;
                }
                _started = true;
                // The deadline bounds target readiness. Playback is governed by
                // the native animation and the app-selected page motion.
                _deadline.Stop();
                _reconciler.ConnectedAnimationStartCount++;
                CompositionTarget.Rendering -= OnRendering;
                // Disposal can run reentrantly from native event handlers.
                if (_finished) return;
                // The app selects page motion independently of the shared card.
                StartPageAnimation();
                return;
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            Core.Diagnostics.DiagnosticLog.SwallowedError(
                Core.Diagnostics.LogCategory.Navigation, "ConnectedNavigation.TryStart", ex);
        }
        if (--_framesRemaining == 0) Finish(fallback: true);
    }

    private void StartPageAnimation()
    {
        var outgoing = ElementCompositionPreview.GetElementVisual(_outgoing);
        var incoming = ElementCompositionPreview.GetElementVisual(_incoming);
        _outgoingSuppression = TransitionEngine.SuppressHitTesting(_outgoing);
        if (_pageTransition is SlideTransition)
            _incomingSuppression = TransitionEngine.SuppressHitTesting(_incoming);
        TransitionEngine.PrepareTransitionAnimations(incoming.Compositor, outgoing, incoming, _pageTransition);
        var batch = incoming.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _pageAnimationBatch = batch;
        batch.Completed += OnPageAnimationCompleted;
        TransitionEngine.StartTransitionAnimations(incoming.Compositor, outgoing, incoming, _pageTransition, _mode);
        batch.End();
    }

    private void OnPageAnimationCompleted(object sender, CompositionBatchCompletedEventArgs args)
    {
        ReleasePageAnimationBatch();
        if (_completion.CompletePage()) Finish(fallback: false);
    }

    private void OnCompleted(ConnectedAnimation sender, object args)
    {
        // TryStart can complete synchronously. Page motion still gets to start
        // and participates in the same two-part completion barrier.
        if (_completion.CompleteConnected()) Finish(fallback: false);
    }

    private void ReleasePageAnimationBatch()
    {
        if (_pageAnimationBatch is not { } batch) return;
        _pageAnimationBatch = null;
        batch.Completed -= OnPageAnimationCompleted;
        batch.Dispose();
    }

    private void OnDeadline(DispatcherQueueTimer sender, object args)
    {
        if (!_started) Finish(fallback: true);
    }

    private void Finish(bool fallback)
    {
        if (_finished) return;
        _finished = true;
        CompositionTarget.Rendering -= OnRendering;
        _deadline.Stop();
        _deadline.Tick -= OnDeadline;
        ReleasePageAnimationBatch();
        if (_animation is { } animation)
        {
            _animation = null;
            animation.Completed -= OnCompleted;
            Cancel(animation);
        }

        void Complete()
        {
            if (IsCompleted) return;
            IsCompleted = true;
            _fallbackComplete = null;
            try
            {
                if (TransitionEngine.StillOwns(_incoming, _generation))
                {
                    TransitionEngine.NormalizeVisual(ElementCompositionPreview.GetElementVisual(_incoming));
                }
                TransitionEngine.RestoreHitTesting(_incoming, _incomingSuppression);
                _complete();
            }
            finally
            {
                // Finalize/unmount while the modifier path is still suppressed.
                foreach (var element in _claimed) _reconciler.ReleaseNavigationConnectedElement(element);
                _claimed.Clear();
                TransitionEngine.RestoreHitTesting(_outgoing, _outgoingSuppression);
                if (TransitionEngine.StillOwns(_outgoing, _generation))
                {
                    TransitionEngine.NormalizeVisual(ElementCompositionPreview.GetElementVisual(_outgoing));
                }
            }
        }

        if (fallback)
        {
            _fallbackComplete = Complete;
            TransitionEngine.RunTransition(_outgoing, _incoming, _pageTransition, _mode, Complete);
        }
        else
            Complete();
    }

    public void Dispose()
    {
        // The source stays attached until Complete finalizes its page. No native
        // snapshot may outlive the transaction or bleed into a later navigation.
        if (!_finished && _animation is { } animation)
        {
            animation.Completed -= OnCompleted;
            Cancel(animation);
            _animation = null;
        }
        _fallbackComplete?.Invoke();
        Finish(fallback: false);
    }

    private static void Cancel(ConnectedAnimation animation)
    {
        try { animation.Cancel(); }
        catch (COMException ex)
        {
            Core.Diagnostics.DiagnosticLog.SwallowedError(
                Core.Diagnostics.LogCategory.Navigation, "ConnectedNavigation.Cancel", ex);
        }
    }

    internal static void UnmountPage(Core.Reconciler reconciler, UIElement page)
    {
        // A page's marked elements are only animation candidates. Navigating
        // away without selecting a Connected transition must not snapshot them.
        var elements = Collect(reconciler, page);
        foreach (var element in elements) reconciler.ClaimNavigationConnectedElement(element);
        try { reconciler.UnmountChild(page); }
        finally
        {
            foreach (var element in elements) reconciler.ReleaseNavigationConnectedElement(element);
        }
    }
}
