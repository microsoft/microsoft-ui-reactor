using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using WinBorder = Microsoft.UI.Xaml.Controls.Border;

namespace Microsoft.UI.Reactor;

/// <summary>Options for <see cref="Factories.FitParent"/>.</summary>
public sealed record FitParentOptions
{
    /// <summary>Shared default instance (Debounce 150 ms, ScaleDuringResize true).</summary>
    public static FitParentOptions Default { get; } = new();

    /// <summary>
    /// Quiet period after the last size change before the content is rebuilt at the new size.
    /// Values &lt;= <see cref="TimeSpan.Zero"/> commit every usable change immediately (no
    /// transient scale). Values above 10 seconds are clamped to 10 seconds; nothing throws.
    /// Each commit is a root render pass (every <c>FuncElement</c> re-renders), so a zero
    /// debounce costs one root render per pixel of a window drag.
    /// </summary>
    public TimeSpan Debounce { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Scale the previously rendered content about its arranged centre while a resize is
    /// pending, so it follows the drag until the rebuild lands. When <c>false</c>, the old
    /// content is shown unscaled, clipped to the host, until the commit.
    /// </summary>
    public bool ScaleDuringResize { get; init; } = true;

    // Test-only, per-instance seam (design rev 3, R2-3). Not public API.
    internal Action<FitParentBox>? TestBoxCreated { get; init; }
}

public static partial class Factories
{
    /// <summary>
    /// Fills its parent and renders <paramref name="render"/> at the parent's committed size in
    /// DIPs. Use it for content whose geometry is baked at build time from an explicit
    /// width/height (charts, <c>D3Canvas</c> content, hand-built shape paths), which WinUI's
    /// stretch layout cannot resize.
    /// </summary>
    /// <param name="render">
    /// Builds the content for a width and height. It only ever receives finite values greater
    /// than zero, and is called again whenever the committed size changes (and on every render
    /// of the enclosing component).
    /// </param>
    /// <param name="options">Debounce and transient-scale options; <c>null</c> uses <see cref="FitParentOptions.Default"/>.</param>
    /// <returns>An element that sizes itself to its parent.</returns>
    /// <remarks>
    /// <para>
    /// FitParent must receive a <b>finite</b> size from its parent on <b>both</b> axes: a Flex
    /// child with grow, a Grid <c>*</c> track, the window root, or an explicit
    /// <c>.Width(n)</c>/<c>.Height(n)</c>. Placed directly inside a <c>ScrollView</c> or
    /// <c>ScrollViewer</c>, it fills the viewport. Its content sits in a zero-desired-size layer,
    /// so on an unsized axis (an unsized <c>VStack</c> child, including one inside a scroller, a
    /// Grid <c>Auto</c> track, a horizontal <c>StackPanel</c>) it measures 0 and stays an empty
    /// placeholder: <paramref name="render"/> is never called. Give that axis a size to fix it.
    /// </para>
    /// <para>
    /// The first usable size commits without a debounce (one empty placeholder frame). Later
    /// size changes scale the existing content (see <see cref="FitParentOptions.ScaleDuringResize"/>)
    /// and rebuild after <see cref="FitParentOptions.Debounce"/>, which is clamped to
    /// <c>[0, 10 s]</c>. Each rebuild is a root render pass. Changes of 0.5 DIP or less on both
    /// axes are ignored. Modifiers applied to the returned element (<c>.Flex</c>,
    /// <c>.Margin</c>, <c>.Width</c>, <c>.Key</c>, …) land on an outer wrapper, not on the
    /// content.
    /// </para>
    /// <para>
    /// Prefer <c>Viewbox</c> when stretching strokes and text is acceptable: it costs no
    /// re-render.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="render"/> is <c>null</c>.</exception>
    public static Element FitParent(Func<double, double, Element> render, FitParentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(render);
        return new FuncElement(ctx => RenderFitParent(ctx, render, options));
    }

    private static Element RenderFitParent(RenderContext ctx, Func<double, double, Element> render, FitParentOptions? options)
    {
        // Hook order is fixed and unconditional.
        var hostRef = ctx.UseElementRef<WinBorder>();
        var (committed, setCommitted) = ctx.UseState<(double W, double H)>((0, 0));
        var box = ctx.UseMemo(static () => new FitParentBox(), Array.Empty<object>());

        var opts = options ?? FitParentOptions.Default;
        if (!box.Announced)
        {
            box.Announced = true;
            opts.TestBoxCreated?.Invoke(box);
        }
        box.Options = opts;
        box.Committed = committed;
        box.SetCommitted = setCommitted;
        box.Host = hostRef;

        ctx.UseEffect(box.MountEffect);
        // The options are deps too: an options-only render must reconcile a pending resize (a new
        // debounce reschedules or commits it, ScaleDuringResize = false drops the transient).
        ctx.UseEffect(box.AfterCommitAction, committed,
            FitParentMath.EffectiveDebounce(opts.Debounce), opts.ScaleDuringResize);

        Element? child = null;
        if (FitParentMath.IsUsableSize(committed.W, committed.H) && render(committed.W, committed.H) is { } content)
        {
            // Canvas reports a 0×0 desired size, so the host's size comes only from its parent
            // (no content → host feedback loop); the shell Border lays the content out at w×h.
            child = Canvas(Border(content).Width(committed.W).Height(committed.H));
        }

        return Border(child)
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch)
            .Ref(hostRef)
            .OnSizeChanged(box.SizeChangedHandler)
            .OnMount(box.AttachLoadedAction)
            .OnUpdateAdd(box.HostUpdatedAction);
    }
}

/// <summary>
/// Per-instance mutable state for <see cref="Factories.FitParent"/>: the pending size, the
/// committed-size mirror, the debounce timer and the cached handler delegates. Created once per
/// hook scope via <c>UseMemo</c>. Everything runs on the owning UI thread, so there is no lock.
/// </summary>
internal sealed class FitParentBox
{
    private readonly TypedEventHandler<DispatcherQueueTimer, object> _onTick;
    private readonly RoutedEventHandler _onLoaded;
    private DispatcherQueueTimer? _timer;
    private FrameworkElement? _loadedTarget;
    private CompositeTransform? _transform;
    private RectangleGeometry? _clip;
    private (double W, double H) _pending;
    // Last host size seen by HandleSize, usable or not; drives the stale-content clip.
    private (double W, double H) _observed;

    internal FitParentBox()
    {
        _onTick = (_, _) => OnTick();
        _onLoaded = OnLoaded;
        SizeChangedHandler = (_, e) => OnSizeChangedCore(e.NewSize.Width, e.NewSize.Height);
        AttachLoadedAction = AttachLoaded;
        HostUpdatedAction = OnHostUpdated;
        AfterCommitAction = AfterCommit;
        var dispose = (Action)Dispose;
        MountEffect = () => dispose;
    }

    // Cached delegates, bound once so a render allocates no per-handler delegate.
    internal Action<object, SizeChangedEventArgs> SizeChangedHandler { get; }
    internal Action<FrameworkElement> AttachLoadedAction { get; }
    internal Action<FrameworkElement> HostUpdatedAction { get; }
    internal Action AfterCommitAction { get; }
    internal Func<Action> MountEffect { get; }

    // Refreshed by every render.
    internal FitParentOptions Options { get; set; } = FitParentOptions.Default;
    internal (double W, double H) Committed { get; set; }
    internal Action<(double W, double H)>? SetCommitted { get; set; }
    internal ElementRef<WinBorder>? Host { get; set; }
    internal bool Announced { get; set; }

    // ── Test seam (design §4.6) ─────────────────────────────────────

    /// <summary>The debounce timer exists and is running.</summary>
    internal bool IsTimerRunning => _timer is { IsRunning: true };

    /// <summary>Tick / SizeChanged invocations that hit the <see cref="Disposed"/> early return.</summary>
    internal int LateTickCount { get; private set; }

    /// <summary>Every SizeChanged call, counted before any guard.</summary>
    internal int SizeChangedCount { get; private set; }

    /// <summary>Loaded-fallback invocations that passed a usable size on to HandleSize.</summary>
    internal int LoadedCommitCount { get; private set; }

    /// <summary><see cref="SizeChangedCount"/> when the Loaded fallback last committed; -1 if it never did.</summary>
    internal int SizeChangedCountAtLoadedCommit { get; private set; } = -1;

    internal bool Disposed { get; private set; }

    /// <summary>
    /// Counts SizeChanged but ignores it, so a fixture can force the Loaded fallback: WinUI
    /// raises SizeChanged on a freshly mounted host too, which otherwise commits first.
    /// </summary>
    internal bool IgnoreSizeChangedForTest { get; set; }

    /// <summary>Runs the real Tick handler body.</summary>
    internal void InvokeTickForTest() => OnTick();

    /// <summary>Runs the real SizeChanged handler body.</summary>
    internal void InvokeSizeChangedForTest(double w, double h) => OnSizeChangedCore(w, h);

    // ── Handlers ────────────────────────────────────────────────────

    private bool IsCommitted => FitParentMath.IsUsableSize(Committed.W, Committed.H);

    private void OnSizeChangedCore(double w, double h)
    {
        SizeChangedCount++;
        if (Disposed)
        {
            LateTickCount++;
            return;
        }
        if (IgnoreSizeChangedForTest)
            return;
        HandleSize(w, h);
    }

    private void OnTick()
    {
        if (Disposed)
        {
            LateTickCount++;
            return;
        }
        // A commit that landed while the timer ran may already match _pending to within jitter.
        if (FitParentMath.IsUsableSize(_pending.W, _pending.H)
            && FitParentMath.IsSignificantChange(Committed.W, Committed.H, _pending.W, _pending.H))
            SetCommitted?.Invoke(_pending);
    }

    // Subscribing Loaded per mount departs from the "subscribe once through a trampoline" rule
    // for poolable controls, and is still pool-safe: at most one handler per box is attached
    // (a re-attach removes the previous one first), the handler removes itself once it has seen
    // a usable size or a commit has landed, and Dispose removes it if it is still attached. So a
    // recycled Border never accumulates FitParent Loaded handlers, and removing our own delegate
    // is safe even if the Border has since been re-rented.
    private void AttachLoaded(FrameworkElement fe)
    {
        if (Disposed || IsCommitted)
            return;
        DetachLoaded();
        fe.Loaded += _onLoaded;
        _loadedTarget = fe;
    }

    private void DetachLoaded()
    {
        if (_loadedTarget is { } target)
        {
            target.Loaded -= _onLoaded;
            _loadedTarget = null;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Disposed)
            return;
        if (IsCommitted)
        {
            DetachLoaded();
            return;
        }
        if (sender is not FrameworkElement fe)
            return;
        var w = fe.ActualWidth;
        var h = fe.ActualHeight;
        // A Loaded raised before layout reports 0×0; stay attached so the fallback is not lost.
        if (!FitParentMath.IsUsableSize(w, h))
            return;
        DetachLoaded();
        LoadedCommitCount++;
        SizeChangedCountAtLoadedCommit = SizeChangedCount;
        HandleSize(w, h);
    }

    // Covers the hot-reload reset, where the host is updated rather than mounted.
    private void OnHostUpdated(FrameworkElement fe)
    {
        if (Disposed || IsCommitted)
            return;
        // An uncommitted box never applies a transient, so any RenderTransform on this host is a
        // previous box's, left behind by a hooks reset during a pending resize (the host Border is
        // retained, not pooled, so CleanElement never ran). The stale Clip needs no counterpart:
        // SyncClip replaces or removes whatever clip the host carries.
        fe.ClearValue(UIElement.RenderTransformProperty);
        HandleSize(fe.ActualWidth, fe.ActualHeight);
    }

    private void HandleSize(double w, double h)
    {
        _observed = (w, h);
        HandleSizeCore(w, h);
        SyncClip();
    }

    private void HandleSizeCore(double w, double h)
    {
        if (!FitParentMath.IsUsableSize(w, h))
        {
            // Nothing can be committed at this size. Drop any pending rebuild (the host no longer
            // has that size) and the transient; SyncClip then hides the stale content. Invalidating
            // _pending matters when a commit is already queued: otherwise AfterCommitCore would
            // see the last usable size as "the resize continued" and re-arm the timer for it.
            _pending = default;
            if (IsCommitted)
            {
                StopTimer();
                ClearTransform();
            }
            return;
        }

        _pending = (w, h);
        var significant = FitParentMath.IsSignificantChange(Committed.W, Committed.H, w, h);
        var debounce = FitParentMath.EffectiveDebounce(Options.Debounce);

        if (!IsCommitted || debounce == TimeSpan.Zero || EnsureTimer() is not { } timer)
        {
            StopTimer();
            if (significant)
                SetCommitted?.Invoke((w, h));
            else
                ClearTransform();
            return;
        }

        if (!significant)
        {
            StopTimer();
            ClearTransform();
            return;
        }

        ApplyTransientOrClear(w, h);
        timer.Stop();
        timer.Interval = debounce;
        timer.Start();
    }

    // Runs as an effect on every pass where the committed size or an option changed. Effects flush
    // before the child is reconciled, but in the same dispatcher work item as the reconcile, layout
    // and paint, so clearing here never shows a frame of old content unscaled or new content scaled.
    private void AfterCommit()
    {
        if (Disposed)
            return;
        if (IsCommitted)
            DetachLoaded();
        if (Host?.Current is null)
            return;
        AfterCommitCore();
        SyncClip();
    }

    private void AfterCommitCore()
    {
        if (!FitParentMath.IsSignificantChange(Committed.W, Committed.H, _pending.W, _pending.H)
            || !FitParentMath.IsUsableSize(_pending.W, _pending.H))
        {
            // The commit caught up with the pending size: a still-running debounce would only
            // re-commit a jitter-sized change.
            StopTimer();
            ClearTransform();
            return;
        }

        // The resize continued while the commit render was queued.
        var debounce = FitParentMath.EffectiveDebounce(Options.Debounce);
        if (debounce == TimeSpan.Zero || EnsureTimer() is not { } timer)
        {
            ClearTransform();
            SetCommitted?.Invoke(_pending);
            return;
        }
        ApplyTransientOrClear(_pending.W, _pending.H);
        timer.Stop();
        timer.Interval = debounce;
        timer.Start();
    }

    private DispatcherQueueTimer? EnsureTimer()
    {
        if (_timer is not null)
            return _timer;
        var dq = DispatcherQueue.GetForCurrentThread();
        if (dq is null)
            return null;
        var timer = dq.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += _onTick;
        _timer = timer;
        return timer;
    }

    private void StopTimer() => _timer?.Stop();

    private void ApplyTransientOrClear(double w, double h)
    {
        if (!Options.ScaleDuringResize
            || FitParentMath.ComputeTransient(Committed.W, Committed.H, w, h) is not { } t)
        {
            ClearTransform();
            return;
        }
        if (Host?.Current is not { } host)
            return;

        // RenderTransformOrigin is never written: CenterX/CenterY carry the pivot, so nothing
        // leaks into a pooled Border beyond RenderTransform, which pool return clears.
        if (_transform is null || !ReferenceEquals(host.RenderTransform, _transform))
        {
            _transform = new CompositeTransform();
            host.RenderTransform = _transform;
        }
        _transform.CenterX = t.CenterX;
        _transform.CenterY = t.CenterY;
        _transform.ScaleX = t.Scale;
        _transform.ScaleY = t.Scale;
        _transform.TranslateX = t.TranslateX;
        _transform.TranslateY = t.TranslateY;
    }

    private void ClearTransform()
    {
        // ClearValue, not `= null`: it restores the default exactly as ElementPool.CleanElement does,
        // leaving no local value behind. Only our own transient is cleared, never another owner's.
        if (_transform is not null && Host?.Current is { } host && ReferenceEquals(host.RenderTransform, _transform))
            host.ClearValue(UIElement.RenderTransformProperty);
        _transform = null;
    }

    // The Canvas layer does not clip and reports 0×0, so without this, committed content that no
    // longer matches the host (host shrunk to an unusable size, ScaleDuringResize = false, or an
    // immediate commit still queued) draws at its old size over siblings. Derived from state, not
    // from the call site, so every path converges on the same clip. Pool return clears the clip
    // (ElementPool.CleanElement), so Dispose never has to touch it.
    private void SyncClip()
    {
        if (Disposed || Host?.Current is not { } host)
            return;
        var transientApplied = _transform is not null && ReferenceEquals(host.RenderTransform, _transform);
        if (FitParentMath.StaleClipSize(Committed.W, Committed.H, _observed.W, _observed.H, transientApplied) is { } size)
        {
            _clip ??= new RectangleGeometry();
            var rect = new Rect(0, 0, size.W, size.H);
            if (_clip.Rect != rect)
                _clip.Rect = rect;
            if (!ReferenceEquals(host.Clip, _clip))
                host.Clip = _clip;
        }
        else if (host.Clip is not null)
        {
            host.Clip = null;
        }
    }

    // Touches only the timer and this box's own Loaded delegate: the host may already have been
    // returned to the pool and re-rented, so no other host state is written here. The transient
    // RenderTransform and the stale-content Clip are both cleared by ElementPool.CleanElement.
    private void Dispose()
    {
        if (Disposed)
            return;
        Disposed = true;
        if (_timer is { } timer)
        {
            timer.Stop();
            timer.Tick -= _onTick;
            _timer = null;
        }
        DetachLoaded();
    }
}
