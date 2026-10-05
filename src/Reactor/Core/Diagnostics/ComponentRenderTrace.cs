using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Core.Diagnostics;

/// <summary>
/// Shared plumbing for <see cref="ReactorEventSource.ComponentRendered"/>: the enable
/// gate, the instance-id counter and the reason vocabulary. Deliberately free of WinUI
/// types so the classification and gate are unit-testable headless; the UIElement
/// registry lives in <see cref="ComponentRenderControls"/>.
/// </summary>
internal static class ComponentRenderTrace
{
    /// <summary>The <c>reason</c> payload values. Stable wire tokens — never rename.</summary>
    internal static class Reasons
    {
        /// <summary>First render of a newly mounted component (or a host's first root render).</summary>
        public const string Mount = "mount";

        /// <summary>
        /// An update was requested from inside this component's subtree: a hook in this
        /// component, or in a descendant component, changed state (or a control/resource
        /// in the subtree asked for a re-render). Reactor re-renders every component on
        /// the path from the host root to the requester, so ancestors report this too.
        /// A host root reports it for every non-forced re-render.
        /// </summary>
        public const string State = "state";

        /// <summary>The parent re-rendered and this component's props (or a <c>Memo</c>'s dependencies) changed.</summary>
        public const string Props = "props";

        /// <summary>The parent re-rendered and a context value this component consumes changed.</summary>
        public const string Context = "context";

        /// <summary>
        /// The parent re-rendered and this component has no memo gate that could skip it
        /// (a function component, or a propless <c>Component</c> whose
        /// <c>ShouldUpdate()</c> returned true).
        /// </summary>
        public const string Parent = "parent";

        /// <summary>A hot-reload pass forced every component to re-render.</summary>
        public const string HotReload = "hotReload";

        /// <summary>A full re-render was forced outside hot reload (e.g. the reset-all-state escape hatch).</summary>
        public const string Forced = "forced";
    }

    /// <summary>Keyword mask of the event; enabling either bit enables it.</summary>
    internal const EventKeywords Mask =
        ReactorEventSource.Keywords.Render | ReactorEventSource.Keywords.RenderDetail;

    /// <summary>
    /// The single check every render path makes. With no listener this is the
    /// EventSource's own enabled-field test, so the disabled cost is one call that
    /// reads a bool and returns.
    /// </summary>
    internal static bool IsEnabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ReactorEventSource.Log.IsEnabled(EventLevel.Verbose, Mask);
    }

    private static long s_nextId;

    /// <summary>Process-unique, never reused, never 0.</summary>
    internal static long NextId() => Interlocked.Increment(ref s_nextId);

    internal static long ElapsedMicroseconds(long startTimestamp)
        => (long)((global::System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp)
            * 1_000_000.0 / global::System.Diagnostics.Stopwatch.Frequency);

    /// <summary>
    /// Reason for a render on the reconciler's UPDATE path.
    /// </summary>
    /// <param name="forced">The pass was a forced full render (memo gates bypassed).</param>
    /// <param name="hotReloadPass">A hot-reload update pass is open.</param>
    /// <param name="selfTriggered">The node was marked by a re-render request from its subtree.</param>
    /// <param name="memoReason">What the memo gate found when it let the render through, or null when there is no gate.</param>
    internal static string ClassifyUpdate(bool forced, bool hotReloadPass, bool selfTriggered, string? memoReason)
    {
        if (forced) return hotReloadPass ? Reasons.HotReload : Reasons.Forced;
        if (selfTriggered) return hotReloadPass ? Reasons.HotReload : Reasons.State;
        return memoReason ?? Reasons.Parent;
    }

    /// <summary>Reason for a host's ROOT component render.</summary>
    internal static string ClassifyRoot(bool firstRender, bool hotReloadRender, bool forcePending)
    {
        if (firstRender) return Reasons.Mount;
        if (hotReloadRender) return Reasons.HotReload;
        return forcePending ? Reasons.Forced : Reasons.State;
    }
}

/// <summary>
/// Two-way map between <c>componentId</c> values carried by
/// <see cref="ReactorEventSource.ComponentRendered"/> and the realized control whose
/// bounds are that component's. Populated only while the event is enabled, so a
/// process nobody is inspecting never touches it.
///
/// <para>Generic so the bookkeeping is testable headless with plain objects; the
/// runtime instance is <see cref="ComponentRenderControls.Registry"/>.</para>
///
/// <para>Controls are held weakly in the id → control direction. The control → id
/// direction is a slot on the control itself, reached through the accessors passed
/// in: at runtime that is <see cref="Reconciler.ReactorState"/> on the attached DP,
/// so every managed wrapper WinRT projects over the same native control sees the
/// same id (a table keyed by wrapper would miss once the wrapper is re-projected).
/// Ids are dropped explicitly when a component unmounts (a component wrapper may be
/// pooled and later reused by an unrelated element, so a stale control → id entry
/// would misattribute), and dead weak entries are swept as the table grows.</para>
/// </summary>
internal sealed class ComponentControlRegistry<TControl> where TControl : class
{
    private const int MinPruneThreshold = 256;

    private readonly object _gate = new();
    private readonly Dictionary<long, WeakReference<TControl>> _byId = new();
    private readonly Func<TControl, long> _readId;
    private readonly Action<TControl, long> _writeId;
    private int _pruneAt = MinPruneThreshold;

    /// <param name="readId">Reads the control's id slot; 0 when unset.</param>
    /// <param name="writeId">Writes the control's id slot; 0 clears it.</param>
    public ComponentControlRegistry(Func<TControl, long> readId, Action<TControl, long> writeId)
    {
        _readId = readId;
        _writeId = writeId;
    }

    /// <summary>
    /// Records that <paramref name="id"/> currently renders into <paramref name="control"/>.
    /// A null control clears the id → control direction. When
    /// <paramref name="mapControlToId"/> is false only id → control is recorded (a host
    /// root's content control can also be a child component's wrapper, whose own id
    /// must keep winning the reverse lookup).
    /// </summary>
    public void Track(long id, TControl? control, bool mapControlToId)
    {
        if (id == 0) return;
        lock (_gate)
        {
            if (control is null)
            {
                _byId.Remove(id);
                return;
            }

            if (_byId.TryGetValue(id, out var weak))
            {
                if (!weak.TryGetTarget(out var current) || !ReferenceEquals(current, control))
                    weak.SetTarget(control);
            }
            else
            {
                _byId[id] = new WeakReference<TControl>(control);
                if (_byId.Count >= _pruneAt) Prune();
            }

            if (mapControlToId) _writeId(control, id);
        }
    }

    /// <summary>Drops <paramref name="id"/>, and the reverse entry if it still names this id.</summary>
    public void Forget(long id, TControl? control)
    {
        if (id == 0) return;
        lock (_gate)
        {
            _byId.Remove(id);
            if (control is not null && _readId(control) == id)
                _writeId(control, 0);
        }
    }

    public TControl? Resolve(long id)
    {
        if (id == 0) return null;
        lock (_gate)
            return _byId.TryGetValue(id, out var weak) && weak.TryGetTarget(out var control) ? control : null;
    }

    public bool TryGetId(TControl control, out long id)
    {
        lock (_gate)
            id = _readId(control);
        return id != 0;
    }

    internal int CountForTests
    {
        get { lock (_gate) return _byId.Count; }
    }

    internal void PruneForTests()
    {
        lock (_gate) Prune();
    }

    /// <summary>Clears the weak target of <paramref name="id"/>, as if its control had been collected.</summary>
    internal void ExpireForTests(long id)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(id, out var weak)) weak.SetTarget(null!);
        }
    }

    private void Prune()
    {
        List<long>? dead = null;
        foreach (var (id, weak) in _byId)
        {
            if (!weak.TryGetTarget(out _)) (dead ??= new()).Add(id);
        }
        if (dead is not null)
        {
            foreach (var id in dead) _byId.Remove(id);
        }
        _pruneAt = Math.Max(MinPruneThreshold, _byId.Count * 2);
    }
}

/// <summary>Runtime instance of <see cref="ComponentControlRegistry{TControl}"/>.</summary>
internal static class ComponentRenderControls
{
    internal static readonly ComponentControlRegistry<Microsoft.UI.Xaml.UIElement> Registry = new(ReadId, WriteId);

    // Component wrappers are Borders, so the id slot lives on ReactorState. A
    // non-FrameworkElement control has no slot and is never reverse-mapped.
    private static long ReadId(Microsoft.UI.Xaml.UIElement control)
        => control is Microsoft.UI.Xaml.FrameworkElement fe && Reconciler.TryGetReactorState(fe, out var state)
            ? state.ComponentDiagnosticId
            : 0;

    private static void WriteId(Microsoft.UI.Xaml.UIElement control, long id)
    {
        if (control is not Microsoft.UI.Xaml.FrameworkElement fe) return;
        if (id != 0) Reconciler.GetOrCreateReactorState(fe).ComponentDiagnosticId = id;
        else if (Reconciler.TryGetReactorState(fe, out var state)) state.ComponentDiagnosticId = 0;
    }

    /// <summary>
    /// Emits <see cref="ReactorEventSource.ComponentRendered"/> for a host's ROOT
    /// component (which the host renders itself, outside the reconciler) — including a
    /// render that threw, as a child component's does. Assigns the root's id on first
    /// use and marks the root rendered. Must run before Reconcile consumes the host's
    /// pending force flag. The id → control mapping is recorded by the host after
    /// reconcile, once the root content control exists.
    /// </summary>
    /// <returns>Whether the event is enabled.</returns>
    internal static bool TraceRootRendered(
        ref long rootId, ref bool rootRendered, string componentName,
        bool hotReloadRender, bool forcePending, double elapsedMilliseconds)
    {
        bool firstRender = !rootRendered;
        rootRendered = true;
        if (!ComponentRenderTrace.IsEnabled) return false;
        if (rootId == 0) rootId = ComponentRenderTrace.NextId();
        ReactorEventSource.Log.ComponentRendered(
            componentName,
            rootId,
            ComponentRenderTrace.ClassifyRoot(firstRender, hotReloadRender, forcePending),
            (long)(elapsedMilliseconds * 1000.0));
        return true;
    }
}

/// <summary>
/// A host's ROOT-component bookkeeping for <see cref="ReactorEventSource.ComponentRendered"/>,
/// shared by <c>ReactorHost</c> and <c>ReactorHostControl</c> so the two hosts cannot drift.
/// </summary>
internal sealed class RootRenderDiagnostics
{
    // componentId of the root; 0 until the root first renders while the event is enabled.
    private long _id;
    // False until the current root has rendered once (reason "mount").
    private bool _rendered;

    internal long IdForTests => _id;

    /// <summary>A new (or disposed) root is a new component instance: fresh id, next render is "mount".</summary>
    public void Reset()
    {
        if (_id != 0) ComponentRenderControls.Registry.Forget(_id, null);
        _id = 0;
        _rendered = false;
    }

    /// <summary>See <see cref="ComponentRenderControls.TraceRootRendered"/>. Returns whether the event is enabled.</summary>
    public bool TraceRendered(string componentName, bool hotReloadRender, bool forcePending, double elapsedMilliseconds)
        => ComponentRenderControls.TraceRootRendered(
            ref _id, ref _rendered, componentName, hotReloadRender, forcePending, elapsedMilliseconds);

    /// <summary>
    /// Maps the root's id to the control now standing in for its content (the reconciled
    /// root, or the error panel when Render() threw). Never the reverse direction: that
    /// control is often also a child component's wrapper, whose own id must keep winning.
    /// </summary>
    public void TrackContent(Microsoft.UI.Xaml.UIElement? control)
    {
        if (_id != 0) ComponentRenderControls.Registry.Track(_id, control, mapControlToId: false);
    }
}
