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

/// <summary>
/// The reconciler diffs old and new element trees and patches the real WinUI control tree.
///
/// Split across partial classes, one file per concern, so unrelated changes don't collide:
///   - Reconciler.cs           — orchestration: Reconcile entry, components, children, CanUpdate
///   - Reconciler.Mount.cs     — Mount() dispatch + composition-primitive handlers
///   - Reconciler.Update.cs    — Update() dispatch + composition-primitive handlers
///   - Reconciler.*.cs         — one topic each (Modifiers, Keyframes, Transitions, ThemeBindings,
///                               Events, Unmount, ReactorState, ...); add code to the matching file
/// </summary>
public sealed partial class Reconciler : IDisposable
{
    private readonly Dictionary<UIElement, ComponentNode> _componentNodes = new();
    private readonly Dictionary<UIElement, ErrorBoundaryNode> _errorBoundaryNodes = new();
    // Test seam: boundaries still registered, so a teardown path that leaks one is observable.
    internal int ErrorBoundaryNodeCount => _errorBoundaryNodes.Count;
    // internal so NavigationHostLifecycle (V1-owned mount/update) can record and
    // look up per-instance navigation state; teardown stays here (Dispose loop +
    // CleanupNavigationHostNode).
    internal readonly Dictionary<UIElement, NavigationHostNode> _navigationHostNodes = new();
    // Internal so V1-owned lifecycle classes (e.g. LazyStackLifecycle) can rent
    // pooled controls and pass the pool into element factories.
    internal readonly ElementPool _pool = new();
    private readonly Dictionary<Type, ITypeRegistration> _typeRegistry = new();
    // Null when no caller-supplied logger and no devtools logger is published.
    // All call sites use null-conditional access so the M.E.Logging code path
    // doesn't run (and stays JIT-cold) for default apps.
    // Internal so V1-owned lifecycle classes can pass it into shared keyed-diff helpers.
    internal readonly ILogger? _logger;
    private readonly List<(string Key, UIElement Target)> _pendingConnectedAnimationStarts = new();
    private readonly ContextScope _contextScope = new();

    /// <summary>
    /// The host that owns this reconciler's content (<see cref="Microsoft.UI.Reactor.Hosting.ReactorHost"/>
    /// or <see cref="Microsoft.UI.Reactor.Hosting.ReactorHostControl"/>), or null for a bare
    /// reconciler (unit tests). Every component context it renders records it as its owner,
    /// including renders outside the host's render pass (virtualized rows); the picker methods
    /// resolve their owning window through it at call time.
    /// </summary>
    internal IContentOwner? Owner { get; set; }
    /// <summary>
    /// The XAML <see cref="Window"/> of the <see cref="Microsoft.UI.Reactor.Hosting.ReactorHost"/> that
    /// owns this reconciler, with or without a <see cref="Microsoft.UI.Reactor.ReactorWindow"/> (<see cref="Owner"/>); null for
    /// <see cref="Microsoft.UI.Reactor.Hosting.ReactorHostControl"/>. Lets a mounting
    /// <c>TitleBar</c> claim its own host's window instead of whichever host is ambient.
    /// </summary>
    internal Window? HostWindow { get; set; }

    // Issue #917 — withdraws an unmounting TitleBar from this reconciler's own window (not the
    // ambient host's). Skipped once the window is closing or disposed: ReactorWindow.Dispose
    // already drops its title-bar bookkeeping, and resetting the caption state of a window
    // being torn down would undo PrepareTitleBarForClose (issue #537).
    private void WithdrawTitleBar(WinUI.TitleBar unmounting)
    {
        if (HostWindow is null)
        {
            _embeddedTitleBars?.Remove(unmounting);
            WithdrawEmbeddedTitleBar(unmounting);
            return;
        }
        if (Owner?.ReactorWindow is { IsClosingOrDisposed: false } window)
            window.ClearTitleBarControl(unmounting);
    }

    // TitleBars in ReactorHostControl content, registered (once loaded) with the ReactorWindow
    // that contains them; released on unmount and on reconciler disposal. (issue #1396)
    private HashSet<WinUI.TitleBar>? _embeddedTitleBars;

    internal void TrackEmbeddedTitleBar(WinUI.TitleBar titleBar) => (_embeddedTitleBars ??= new()).Add(titleBar);

    private static void WithdrawEmbeddedTitleBar(WinUI.TitleBar titleBar)
    {
        if (TitleBarElement.ReleaseEmbedded(titleBar) is { IsClosingOrDisposed: false } window)
            window.ClearTitleBarControl(titleBar);
    }
    // Ancestor-aware effective theme for the subtree currently being mounted/updated,
    // threaded top-down through Mount/Update (an element's own RequestedTheme wins,
    // otherwise the subtree inherits its ancestor's). ElementTheme.Default means "no
    // Reactor-tree ancestor override" — resolution then falls back to the element's own
    // fe-based walk / app theme. At mount the element is not yet parented, so an
    // ancestor RequestedTheme is unreachable via fe's visual-tree walk; this field
    // carries the reconciler's top-down knowledge so ThemeRef-backed ResourceOverrides
    // resolve their concrete brush against the correct effective theme at mount.
    private ElementTheme _ambientRequestedTheme = ElementTheme.Default;

    /// <summary>
    /// Native parent of the host's content, also used when installing error fallbacks.
    /// </summary>
    internal DependencyObject? ThemeAnchor { get; set; }
    private ElementTheme _renderedHostTheme;
    private ElementTheme _realizationTheme;
    private int _realizationThemeDepth;
    private bool _realizationResourceRefresh;
    internal bool HostThemeChanged => InheritedRequestedTheme(ThemeAnchor) != _renderedHostTheme;

    /// <summary>
    /// The nearest explicit <c>RequestedTheme</c> on <paramref name="start"/> or its visual
    /// ancestors, or Default. Covers themes set by Reactor on realized controls and themes
    /// an app sets outside the Reactor tree (for example on a window root).
    /// </summary>
    internal static ElementTheme InheritedRequestedTheme(DependencyObject? start)
    {
        for (var node = start; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement { RequestedTheme: not ElementTheme.Default } themed)
                return themed.RequestedTheme;
        }
        return ElementTheme.Default;
    }

    /// <summary>
    /// Layout-time realization (an ItemsRepeater or ItemsView asking for a row) runs outside
    /// any root pass, so no ambient theme is threaded to the row. Seed it from the parented
    /// realization target so ThemeRef resource overrides on the still-unparented row resolve
    /// against the theme it will render under. A realization nested in a pass keeps the
    /// pass's ambient.
    /// </summary>
    internal RealizationThemeScope EnterRealizationTheme(DependencyObject? realizationParent, UIElement? reused = null)
    {
        var inherited = _debugReconcileDepth > 0
            ? _ambientRequestedTheme : InheritedRequestedTheme(realizationParent);
        bool refresh = reused is FrameworkElement reusedElement && TryGetReactorState(reusedElement, out var state)
            && state.RealizationTheme is { } previous && previous != inherited;
        var scope = new RealizationThemeScope(this, inherited);
        _realizationThemeDepth++;
        _realizationTheme = inherited;
        _ambientRequestedTheme = inherited;
        // A parked row missed the host refresh. Its unchanged children must not skip.
        if (refresh)
        {
            _realizationResourceRefresh = true;
            _resourceRefreshActive = true;
            _forceFullRenderActive = true;
        }
        return scope;
    }

    internal readonly struct RealizationThemeScope : IDisposable
    {
        private readonly Reconciler? _owner;
        private readonly ElementTheme _realization;
        private readonly ElementTheme _ambient;
        private readonly bool _realizationRefresh;
        private readonly bool _refreshActive;
        private readonly bool _forceActive;
        internal ElementTheme Theme { get; }

        internal RealizationThemeScope(Reconciler owner, ElementTheme theme)
        {
            _owner = owner;
            Theme = theme;
            _realization = owner._realizationTheme;
            _ambient = owner._ambientRequestedTheme;
            _realizationRefresh = owner._realizationResourceRefresh;
            _refreshActive = owner._resourceRefreshActive;
            _forceActive = owner._forceFullRenderActive;
        }

        public void Dispose()
        {
            if (_owner is null)
                return;
            _owner._realizationThemeDepth--;
            _owner._realizationTheme = _realization;
            _owner._ambientRequestedTheme = _ambient;
            _owner._realizationResourceRefresh = _realizationRefresh;
            _owner._resourceRefreshActive = _refreshActive;
            _owner._forceFullRenderActive = _forceActive;
        }
    }

    /// <summary>
    /// Supplies the owning host's effective <see cref="RenderErrorHandler"/> (issue #1291).
    /// Set by <c>ReactorHost</c> / <c>ReactorHostControl</c>; a reconciler without a host
    /// falls back to <see cref="ReactorApp.DefaultRenderErrorHandler"/>.
    /// </summary>
    internal Func<RenderErrorHandler?>? RenderErrorHandlerProvider { get; set; }

    private RenderErrorHandler? ResolveRenderErrorHandler() =>
        RenderErrorHandlerProvider is { } provider ? provider() : ReactorApp.DefaultRenderErrorHandler;

    // Nesting depth of the public Reconcile / Mount entry points (issue #1291). The outermost
    // entry — whichever a caller reaches first — opens a render-error propagation scope, so a
    // standalone caller (no host render loop) cannot leave propagation markers behind;
    // recursive mounts and nested passes just count.
    private int _entryDepth;

    private EntryFrame EnterFrame(string perfEntry, Element? element)
    {
        var scope = _entryDepth == 0 ? RenderErrorDispatch.EnterPropagationScope() : default;
        bool perfSpan = false;
        if (_entryDepth == 0)
        {
            _passId++;
            // The perf Reconcile span covers each outermost entry: a host pass's reconcile, and
            // a row an ItemsRepeater / ListView realizes or updates during layout, which runs
            // outside any host pass. Stopped in EntryFrame.Dispose, after the whole entry.
            if (Diagnostics.ReactorPerfTrace.PassEnabled)
            {
                Diagnostics.ReactorPerfTrace.ReconcileStart(perfEntry,
                    element is null ? "null" : Diagnostics.ComponentNames.For(element.GetType()));
                perfSpan = true;
                // Tally this entry's panel child edits for the Reconcile Stop.
                _perfEdits = default;
                _perfTally = true;
            }
        }
        _entryDepth++;
        return new EntryFrame(this, scope, perfSpan);
    }

    // The panel child edits made during the open perf Reconcile span (all containers, every
    // depth), written on its Stop. _perfTally is true only while that span is open, so with
    // no session listening ReconcileChildrenTraced reads one bool and counts nothing.
    private bool _perfTally;
    private PerfEdits _perfEdits;

    // Test-only: the tally must be closed between entries, or every child reconcile pays for it.
    internal bool PerfTallyOpenForTests => _perfTally;

    /// <summary>
    /// Counts controls this reconciler created, updated, removed or moved toward the open perf
    /// <c>ComponentRender</c> spans' <c>changes</c> (<see cref="Diagnostics.ComponentPerfTally"/>).
    /// One static gate read when nobody listens.
    /// </summary>
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static void CountPerfChanges(uint count = 1)
    {
        if (Diagnostics.ReactorPerfTrace.ComponentEnabled)
            Diagnostics.ComponentPerfTally.AddChanges(count);
    }

    /// <summary>
    /// Whether an in-place update changed what the control shows, for the perf <c>changes</c>
    /// count. <paramref name="shallowEqual"/> is <see cref="Element.ShallowEquals"/> of the two
    /// elements, which <see cref="Update"/> already computed. Counts a change when the effective
    /// modifiers differ in anything but callback identity
    /// (<see cref="Element.ModifiersVisiblyEqual"/>), or the element's own properties differ.
    /// Own properties ignore a container's or items host's children and items, which report
    /// their own edits: <see cref="Element.OwnPropsEqual"/> (the reconcile-highlight overlay's
    /// rule) plus the attached layout, theme bindings, resource overrides and animations it does
    /// not read (<see cref="Element.ExtrasEqual"/>). A type neither comparison knows counts as
    /// changed. A keyed <c>Memo</c> whose key held keeps its control (its inner update, if any,
    /// counts itself).
    /// </summary>
    internal static bool PerfUpdateChanged(Element oldEl, Element newEl,
        ElementModifiers? oldModifiers, ElementModifiers? modifiers, bool shallowEqual)
    {
        if (!Element.ModifiersVisiblyEqual(oldModifiers, modifiers)) return true;
        if (shallowEqual) return false;
        // Only a keyed Memo's factory output is stable under its key; the wrapper's own extras
        // (theme bindings, overrides, attached values) are applied on every update.
        if (oldEl is KeyedMemoElement && newEl is KeyedMemoElement) return !Element.ExtrasEqual(oldEl, newEl);
        if (Element.OwnPropsEqual(oldEl, newEl) && Element.ExtrasEqual(oldEl, newEl)) return false;
        // ShallowEquals also compares the element's own modifiers, callbacks included. The
        // effective modifiers are visibly equal (checked above), so when they differ in identity
        // only, compare the rest of the element without them.
        return ReferenceEquals(oldEl.Modifiers, newEl.Modifiers)
            || Element.ModifiersEqual(oldEl.Modifiers, newEl.Modifiers)
            || !Element.ShallowEquals(oldEl, newEl with { Modifiers = oldEl.Modifiers });
    }

    private struct PerfEdits
    {
        public uint Inserted, Removed, Moved, Replaced;

        public void Add(PanelChildCollection collection)
        {
            Inserted += collection.InsertCount;
            Removed += collection.RemoveCount;
            Moved += collection.MoveCount;
            Replaced += collection.ReplaceCount;
        }
    }

    // Identifies the outermost Reconcile/Mount pass in flight (advanced on entry at depth
    // 0). A virtualized host records the pass and the subtree theme it was reached with,
    // so a row realized re-entrantly later in the SAME pass can use that pending theme:
    // the native RequestedTheme modifier is only applied after children are dispatched,
    // so the host's ActualTheme still shows the previous theme at that point.
    private long _passId;

    private readonly struct EntryFrame(Reconciler owner, RenderErrorDispatch.PropagationScope scope, bool perfSpan) : IDisposable
    {
        public void Dispose()
        {
            scope.Dispose();
            owner._entryDepth--;
            if (perfSpan)
            {
                owner._perfTally = false;
                var edits = owner._perfEdits;
                Diagnostics.ReactorPerfTrace.ReconcileStop(edits.Inserted, edits.Removed, edits.Moved, edits.Replaced);
            }
        }
    }

    /// <summary>In-tree placeholder for a component whose render or effect flush threw.</summary>
    private Element BuildInTreeFallback(Exception ex, bool inEffects, string? componentName) =>
        RenderErrorDispatch.BuildInTreeFallback(
            ResolveRenderErrorHandler(),
            new RenderError(ex, inEffects ? RenderErrorSource.Effects : RenderErrorSource.ComponentRender,
                componentName, isHostLevel: false),
            _logger);
    /// <summary>
    /// Active rerender-callback depth. Throws past
    /// <see cref="MaxRerenderReentrancy"/> so a component that synchronously
    /// invokes its own <c>requestRerender</c> from inside <c>Render()</c> or
    /// effect cleanup fails fast instead of recursing to a stack overflow.
    /// TASK-059.
    /// </summary>
    [ThreadStatic]
    private static int t_rerenderDepth;
    /// <summary>Hard depth cap — see <see cref="t_rerenderDepth"/>.</summary>
    private const int MaxRerenderReentrancy = 50;

    /// <summary>
    /// Per-reconcile counters for diagnosing diff and mount/update volume.
    /// Reset before each top-level Reconcile() call; read afterward. Always
    /// populated (including Release builds) so ETW trace consumers can read
    /// them off the <c>ReconcileStop</c> event payload.
    /// </summary>
    public int DebugElementsDiffed;
    public int DebugElementsSkipped;
    public int DebugUIElementsCreated;
    public int DebugUIElementsModified;
    private int _debugReconcileDepth;

    /// <summary>
    /// Mounts a ContentDialog's body. A dialog opened later (deferred to the placeholder's
    /// Loaded, or on a state flip handled outside a render) mounts outside any owner scope; the
    /// placeholder's published <c>owner=</c> is the owner its body was declared under, so it is
    /// restored for the mount. Nested components still scope their own subtrees.
    /// </summary>
    internal UIElement? MountDialogContent(
        FrameworkElement placeholder,
        Element content,
        Action requestRerender,
        ElementTheme theme,
        V1Protocol.OverlayLifecycle.ContentDialogLifetime lifetime)
    {
        if (lifetime.Retired || IsDisposingContentDialogs) return null;

        var previousTheme = _ambientRequestedTheme;
        _ambientRequestedTheme = theme;
        UIElement? mounted;
        try
        {
            if (global::Microsoft.UI.Reactor.Hosting.ReactorFeatures.DevtoolsSupported && Diagnostics.ReactorSourcePublisher.IsEnabled
                && CurrentDiagnosticOwner is null
                && placeholder.GetValue(Diagnostics.ReactorDiagnostics.SourceProperty) is string published
                && Diagnostics.ReactorSourcePublisher.Owner(published) is { } owner)
                mounted = MountUnderOwner(content, requestRerender, owner);
            else
                mounted = Mount(content, requestRerender);
        }
        finally { _ambientRequestedTheme = previousTheme; }

        // A child setter can dispose the host while Mount is walking siblings.
        // Disposal drains the current component set, so tear down the completed
        // side-mounted subtree too; this also makes a later dialog.Content write a no-op.
        if (lifetime.Retired || IsDisposingContentDialogs)
        {
            if (mounted is not null) UnmountChild(mounted);
            return null;
        }
        return mounted;
    }

    private UIElement? MountUnderOwner(Element element, Action requestRerender, string owner)
    {
        var previous = _diagOwner;
        _diagOwner = owner;
        try { return Mount(element, requestRerender); }
        finally { _diagOwner = previous; }
    }

    private UIElement? ReconcileUnderOwner(
        Element? oldElement, Element? newElement, UIElement? existing, Action requestRerender, string owner)
    {
        var previous = _diagOwner;
        _diagOwner = owner;
        try { return Reconcile(oldElement, newElement, existing, requestRerender); }
        finally { _diagOwner = previous; }
    }

    // Hot reload signal: when set, the next top-level Reconcile() pass bypasses
    // Component memo (props/deps equality) so updated method bodies are picked up
    // even when props are unchanged. Cleared at the start of that pass.
    internal volatile bool ForceFullRenderPending;
    private bool _forceFullRenderActive;

    // Diagnostics only: the next top-level pass is the retry a host schedules after a
    // hot-reload hook-order recovery. That retry runs outside a hot-reload pass, so
    // without this ComponentRendered would attribute its renders to their ordinary
    // reasons, not "hotReload". Consumed by that pass; never changes render behaviour.
    internal volatile bool HotReloadRetryPending;
    private bool _hotReloadRetryActive;

    // True for the duration of a hot-reload force pass. Read by ChildReconciler's
    // positional fast path: while a force pass is active, untouched wrapper cells
    // (Component/Memo/Func) in a memoized range must still re-render through the
    // wrapper, so the structural skip must defer to the full walk (which honours
    // ForceRenderThroughWrapper per cell). A pure hot-reload force does NOT mark
    // any node SelfTriggered before the dirty-ancestor path is built, so the
    // IsOnDirtyAncestorPath gate alone does not cover this case.
    internal bool ForceFullRenderActive => _forceFullRenderActive;

    // Theme.NotifyResourcesChanged signal: the host's next root pass is a force pass that
    // ALSO declines every structural skip, not just the wrapper ones. A reference-equal
    // subtree (a reused or UseMemo'd element) is otherwise skipped wholesale, and its
    // ThemeRef modifiers / ThemeRef-backed resource overrides would never be re-applied
    // against the edited resources. Implies a force pass. Set from any thread; taken
    // atomically by BeginRootPass, so a request racing that pass is kept for the next one
    // rather than lost, and only the host's root pass can consume it.
    private int _resourceRefreshPending;
    private bool _resourceRefreshArmed;
    private bool _resourceRefreshActive;

    /// <summary>Requests a resource-refresh pass (see above). Thread-safe.</summary>
    internal void RequestResourceRefresh() => Interlocked.Exchange(ref _resourceRefreshPending, 1);

    /// <summary>
    /// Called by a host before its root render: moves a pending resource refresh onto that
    /// pass's <see cref="Reconcile"/>. Out-of-band top-level reconciles (ElementFactory
    /// realizing or refreshing a row) don't call it, so they can't consume the request.
    /// UI thread only; pair with <see cref="EndRootPass"/> in a finally.
    /// </summary>
    internal void BeginRootPass()
    {
        // Before Windows 10 2004 (19041) "Animation effects" has no change notification, and an
        // unchanged element skips its update, so each root pass polls the setting (issue #1702).
        if (!UiSettingsCapabilities.HasAnimationsEnabledChanged)
            global::Microsoft.UI.Reactor.Navigation.TransitionEngine.PollAnimationsEnabled();
        var pending = Interlocked.Exchange(ref _resourceRefreshPending, 0) != 0;
        _resourceRefreshArmed |= pending || HostThemeChanged;
    }

    /// <summary>
    /// Ends a host root pass. A request <see cref="BeginRootPass"/> armed that the pass never
    /// consumed (the render failed or returned before reconciling) goes back to pending.
    /// </summary>
    internal void EndRootPass()
    {
        if (!_resourceRefreshArmed) return;
        _resourceRefreshArmed = false;
        Interlocked.Exchange(ref _resourceRefreshPending, 1);
    }

    internal bool ResourceRefreshPendingForTest => Volatile.Read(ref _resourceRefreshPending) != 0;
    internal bool ResourceRefreshArmedForTest => _resourceRefreshArmed;

    // True only during a force pass, for the wrapper elements whose skip would prevent
    // ReconcileComponent from running — and for every element during a resource-refresh
    // pass. Used by Update() and ChildReconciler to bypass their structural-equality
    // short-circuits.
    internal bool ForceRenderThroughWrapper(Element el) =>
        _forceFullRenderActive
        && (_resourceRefreshActive || el is ComponentElement or MemoElement or FuncElement);

    // True for the duration of a resource-refresh pass. Read by the skip arms that are not
    // shallow-equality gates (a KeyedMemoElement with an unchanged key, an ItemsHost whose
    // item list is unchanged) so they still reconcile their element children.
    internal bool ResourceRefreshActive => _resourceRefreshActive;

    /// <summary>
    /// For a ListView/GridView whose item array was kept (same reference) during a resource
    /// refresh: reconcile each realized container's item in place, so theme values inside
    /// kept items are re-applied without swapping ItemsSource (which would reset selection
    /// and scroll). Unrealized items mount later against the already-cleared cache. Walks
    /// the realized panel children (O(realized)), as <c>RefreshRealizedContainers</c> does,
    /// rather than probing every index of a virtualized list.
    /// </summary>
    internal void RefreshRealizedItemContainers(
        WinUI.ListViewBase list, Element[] items, Action requestRerender)
    {
        if (!_resourceRefreshActive || list.ItemsPanelRoot is not { } panel) return;
        // Snapshot: reconciling may mount controls, and Children can't change mid-enumeration.
        var realized = panel.Children
            .OfType<WinUI.Primitives.SelectorItem>()
            .Select(static container => (Container: container, Shell: container.ContentTemplateRoot as WinUI.ContentControl))
            .Where(static c => c.Shell?.Content is UIElement)
            .ToList();
        foreach (var (container, cc) in realized)
        {
            var existing = (UIElement)cc!.Content;
            int i = list.IndexFromContainer(container);
            if (i < 0 || i >= items.Length) continue;
            var next = ReconcileV1Child(items[i], items[i], existing, requestRerender);
            if (next is not null && !ReferenceEquals(next, existing))
                cc.Content = next;
            // The item's own automation name can change in place too (a keyed memo whose output
            // reads a resource), so forward it after every reconcile, as RefreshRealizedContainers does.
            PropagateItemAutomationName(container, cc.Content as UIElement);
        }
    }

    // Set of realized UIElements that lie on the path from the root to a
    // ComponentNode whose <see cref="ComponentNode.SelfTriggered"/> is true.
    // Populated at the start of each top-level Reconcile pass by walking
    // up the visual tree from each self-triggered node, and consumed by
    // <see cref="Update"/> to bypass its shallow-equality short-circuit
    // so the render-pass can reach the self-triggered descendant.
    //
    // Without this, a setState inside a Component whose ancestor elements
    // are structurally unchanged (e.g. a dynamically-docked pane whose
    // wrapper Border/Provide chain has stable fields) gets its render
    // swallowed by the parent's shallow-equality skip — the new state
    // never lands in the visual tree. Reproduced by
    // NativeDocking_DynamicallyDockedContent_IsInteractive (counter
    // inside a docked Document — click fires, setCount marks
    // node.SelfTriggered, but the TextBlock never advances past 0).
    private HashSet<UIElement>? _dirtyAncestorPath;
    internal bool IsOnDirtyAncestorPath(UIElement? control) =>
        control is not null && _dirtyAncestorPath is { } set && set.Contains(control);

    // For each control on the dirty-ancestor path, the child (a UIElement) or children (a
    // List<UIElement>, when two self-triggered nodes share an ancestor) the path continues
    // through. Built by the same walk as _dirtyAncestorPath and cleared with it. It lets
    // ChildReconciler find which of a panel's children lead to pending work with one IndexOf
    // per dirty child, so its skip arms never have to read every skipped sibling to ask.
    private Dictionary<UIElement, object>? _dirtyPathChildren;

    /// <summary>
    /// Which children of <paramref name="parentControl"/>, held in
    /// <paramref name="children"/>, lie on the dirty-ancestor path — the children a reconcile of
    /// this container must descend into even when their elements are unchanged, because a
    /// component below them updated its own state.
    /// </summary>
    /// <remarks>
    /// Costs one field check when nothing in the pass self-triggered, and one hash lookup when
    /// something did but this container is not above it. Otherwise each dirty child is located
    /// with a single <see cref="IChildCollection.IndexOf"/>. When a dirty child can't be located
    /// in <paramref name="children"/> (a descriptor whose collection is not the parent's own
    /// <c>Children</c>), or the parent is unknown, the result asks the caller to test each
    /// skip-eligible child's control instead: slower, but it never skips a pending component.
    /// </remarks>
    internal DirtyChildIndices ResolveDirtyChildIndices(UIElement? parentControl, IChildCollection children)
    {
        if (_dirtyAncestorPath is not { Count: > 0 } path)
            return default;
        if (parentControl is null)
            return DirtyChildIndices.ProbeEachChild;
        if (!path.Contains(parentControl))
            return default;
        // On the path with no recorded child: the parent is itself a self-triggered control,
        // and nothing below it is pending.
        if (_dirtyPathChildren is null || !_dirtyPathChildren.TryGetValue(parentControl, out var recorded))
            return default;

        if (recorded is UIElement single)
        {
            int index = children.IndexOf(single);
            return index >= 0 ? DirtyChildIndices.At(index) : DirtyChildIndices.ProbeEachChild;
        }

        var many = (List<UIElement>)recorded;
        var indices = new int[many.Count];
        for (int k = 0; k < many.Count; k++)
        {
            int index = children.IndexOf(many[k]);
            if (index < 0)
                return DirtyChildIndices.ProbeEachChild;
            indices[k] = index;
        }
        return DirtyChildIndices.AtAll(indices);
    }

    private static void AddDirtyPathChild(Dictionary<UIElement, object> edges, UIElement parent, UIElement child)
    {
        if (!edges.TryGetValue(parent, out var existing))
        {
            edges[parent] = child;
            return;
        }
        if (existing is UIElement single)
        {
            if (!ReferenceEquals(single, child))
                edges[parent] = new List<UIElement>(2) { single, child };
            return;
        }
        var list = (List<UIElement>)existing;
        foreach (var known in list)
        {
            if (ReferenceEquals(known, child))
                return;
        }
        list.Add(child);
    }

    // ── Reconcile-highlight capture (gated by ReactorFeatureFlags.HighlightReconcileChanges) ──
    private List<UIElement>? _highlightMounted;
    private List<UIElement>? _highlightModified;

    /// <summary>
    /// UIElements that were newly mounted during the last top-level Reconcile pass.
    /// Only populated when <see cref="ReactorFeatureFlags.HighlightReconcileChanges"/> is true;
    /// returns an empty list otherwise so callers never see stale data after the flag is toggled off.
    /// </summary>
    public IReadOnlyList<UIElement> LastMountedElements =>
        ReactorFeatureFlags.HighlightReconcileChanges
            ? (IReadOnlyList<UIElement>?)_highlightMounted ?? Array.Empty<UIElement>()
            : Array.Empty<UIElement>();

    /// <summary>
    /// UIElements that were modified in-place during the last top-level Reconcile pass.
    /// Only populated when <see cref="ReactorFeatureFlags.HighlightReconcileChanges"/> is true;
    /// returns an empty list otherwise so callers never see stale data after the flag is toggled off.
    /// </summary>
    public IReadOnlyList<UIElement> LastModifiedElements =>
        ReactorFeatureFlags.HighlightReconcileChanges
            ? (IReadOnlyList<UIElement>?)_highlightModified ?? Array.Empty<UIElement>()
            : Array.Empty<UIElement>();

    /// <summary>
    /// The element pool used by this reconciler. Disable via Pool.Enabled = false
    /// to prevent recycled controls from retaining stale property state.
    /// </summary>
    public ElementPool Pool => _pool;

    /// <summary>
    /// EXP-2: When true, UpdateText uses bitmask diff (old vs new Element comparison)
    /// instead of reading WinUI control properties via COM interop to guard writes.
    /// </summary>
    private static volatile bool _enableBitmaskDiff;
    public static bool EnableBitmaskDiff
    {
        get => _enableBitmaskDiff;
        set => _enableBitmaskDiff = value;
    }

    /// <summary>
    /// Spec 047 §14 — the V1 protocol is the unconditional production path.
    /// The Phase 1 <c>UseV1Protocol</c> feature flag, its AppContext switch,
    /// and the descriptor-vs-handler A|B harness ctor were all removed in
    /// §4.6 once the legacy <c>MountXxx</c>/<c>UpdateXxx</c> switch was deleted
    /// (§4.5).
    ///
    /// Built-in V1 handlers register automatically into the internal
    /// <see cref="V1HandlerRegistry"/>; external authors add handlers via
    /// <see cref="RegisterHandler{TElement,TControl}"/> /
    /// <see cref="RegisterType{TElement,TControl}"/> (which populate
    /// <c>_typeRegistry</c>). Dispatch order is V1 → external → the four
    /// composition primitives that sit above the protocol.
    /// </summary>
    public Reconciler(ILogger? logger = null)
    {
        _logger = logger;
        // Registered before the first render, so a perf session already running (or started
        // later, e.g. by `winapp perf`) enables the provider. Once per process.
        Diagnostics.ReactorPerfTrace.EnsureRegistered();
        // Spec 048 §3.4 — no more bootstrap. Built-in handlers register
        // themselves lazily on first factory call (per-control Reg<>/
        // RegDecorator<> cctor latch in `Dsl.cs`), or callers register
        // explicitly via `ControlRegistry.Register<,>`. Constructing an
        // element record directly (e.g. `new TextBlockElement("hi")`)
        // without ever touching its factory or registering its handler
        // throws on first mount with a diagnostic pointing at issue #486.
    }



    /// <summary>
    /// Spec 047 §14 Phase 4 (4.0.5) — true if an element type already has a
    /// handler in either the V1 registry or the external type registry. Lets
    /// idempotent external registrars (e.g. <see cref="Hosting.XamlInterop.Register"/>)
    /// avoid the <see cref="EnsureRegistrableElementType"/> duplicate throw when
    /// V1 auto-registration already owns the type.
    /// </summary>
    internal bool IsElementTypeRegistered(Type elementType)
        => _v1Handlers.ContainsKey(elementType) || _typeRegistry.ContainsKey(elementType);

    // ── V1 handler registry (spec 047 §14 Phase 1, Q1.1) ──────────────
    // Keyed by exact element type, separate from _typeRegistry so that
    // external RegisterType callers and built-in V1 ports stay isolated.
    // Add is internal-only for Phase 1; the public author-facing surface
    // (`RegisterHandler<TElement,TControl>(IElementHandler<…>)`) lands in
    // sections 1.6 / 1.9.
    internal readonly V1HandlerRegistry _v1Handlers = new();

    /// <summary>
    /// Spec 048 §8 — arm 3 of the dispatch precedence: on a per-host
    /// <c>_v1Handlers</c> and per-host <c>_typeRegistry</c> miss, consult
    /// the global lazy <see cref="V1Protocol.ControlRegistry"/>. On a hit
    /// the factory is invoked exactly once and the resulting adapter is
    /// cached into per-host <c>_v1Handlers</c> so steady-state dispatch
    /// short-circuits in arm 1 (the existing fast per-host lookup) on the
    /// next call for this element type on this host.
    ///
    /// <para>Precedence enforced at the call sites (Mount / Update):
    /// (1) per-host <c>_v1Handlers</c> →
    /// (2) per-host <c>_typeRegistry</c> →
    /// (3) global <see cref="V1Protocol.ControlRegistry"/> →
    /// (4) composition-primitive switch.</para>
    /// </summary>
    /// <param name="elementType">Exact runtime element type
    /// (<c>element.GetType()</c>); not a base type. Dispatch is exact-match
    /// (spec 047 §13 Q17).</param>
    /// <param name="entry">On hit, the type-erased adapter to dispatch
    /// through (and now cached in <c>_v1Handlers</c>).</param>
    internal bool TryResolveFromControlRegistry(Type elementType, out IV1HandlerEntry entry)
    {
        if (V1Protocol.ControlRegistry.TryResolve(elementType, out var factory))
        {
            entry = factory();
            // _v1Handlers is UI-thread-only; the caller has already verified
            // there is no per-host entry for this element type, so the Add
            // can never see a duplicate it didn't itself just plant.
            _v1Handlers.Add(elementType, entry);
            return true;
        }

        entry = null!;
        return false;
    }

    /// <summary>
    /// Associates a control with its current element via Tag.
    /// Only call for interactive controls that need the Tag-based event handler pattern.
    /// Layout-only controls (Border, StackPanel, TextBlock, etc.) should NOT set Tag
    /// to avoid expensive COM DependencyProperty calls on the hot path.
    /// </summary>
    /// <summary>
    /// A shared DataTemplate containing a ContentControl shell.
    /// Parsed once via XamlReader.Load, reused across all items controls (ListView, GridView, FlipView).
    /// </summary>
    internal static readonly Lazy<DataTemplate> SharedContentControlTemplate = new(() =>
        (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<ContentControl HorizontalContentAlignment='Stretch' VerticalContentAlignment='Stretch'/>" +
            "</DataTemplate>"));

    /// <summary>
    /// Spec 047 §14 Phase 3 finish — text-bound TreeView item template
    /// shared between the legacy <c>MountTreeView</c> arm and the
    /// <see cref="V1Protocol.TreeChildren{TElement,TControl}"/> strategy.
    /// In node mode the template's DataContext is <c>TreeViewNode</c>, so
    /// <c>{Binding Content.Content}</c> resolves <c>TreeViewNode.Content</c>
    /// (a <c>TreeViewNodeData</c>) → its <c>Content</c> (the display string).
    /// </summary>
    internal static readonly Lazy<DataTemplate> TreeViewTextItemTemplate = new(() =>
        (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<TextBlock Text='{Binding Content.Content}'/>" +
            "</DataTemplate>"));

    /// <summary>
    /// Reconciles one child slot: brings <paramref name="existingControl"/>, the control mounted
    /// for <paramref name="oldElement"/>, up to date with <paramref name="newElement"/>, and returns
    /// the control the slot should now hold.
    /// </summary>
    /// <remarks>
    /// When <paramref name="newElement"/> has the element type and key of
    /// <paramref name="oldElement"/>, the control is patched in place and returned unchanged,
    /// unless the update had to build a new control. In every other case the old control is
    /// unmounted before the new one is returned: a changed element type or key, an update that
    /// built a new control, or a null or empty <paramref name="newElement"/>, which returns null.
    /// Without an <paramref name="existingControl"/> the new element is mounted. Put the result in
    /// the slot when it differs from <paramref name="existingControl"/>. A custom control updates
    /// the child elements it hosts with this method; in an <c>IElementHandler</c>,
    /// <c>UpdateContext.ReconcileChild</c> does the same.
    /// </remarks>
    // <snippet:reconciler-entry>
    public UIElement? Reconcile(
        Element? oldElement,
        Element? newElement,
        UIElement? existingControl,
        Action requestRerender)
    {
        // A top-level pass is an outermost Reactor frame for render-error propagation
        // (issue #1291); see EnterFrame. Nested passes and mounts just count.
        using var entryFrame = EnterFrame(Diagnostics.ReactorPerfTrace.ReconcileEntries.Reconcile, newElement);
        // Declared before the reconcile work so it is disposed after it: validation changes raised by mount,
        // update, or unmount are announced only once the whole pass has finished.
        using var validationScope = Controls.Validation.ValidationRenderScope.BeginReconcile();
        ReferenceDirtySet.BeginCommit();
        try
        {
        // Trace only top-level reconcile passes (depth == 0) to avoid flooding
        // the provider with per-subtree entries; nested Reconcile() calls during
        // the same pass don't emit their own start/stop. Gate the depth counter
        // and Start emit on IsEnabled so the disabled path pays nothing extra.
        // `traceEnabled` and `emitTrace` are tracked separately: every enabled
        // call must decrement the depth it incremented, but only the outermost
        // one emits. Decrementing on `emitTrace` alone would leak the counter
        // once a pass contained a nested reconcile, permanently suppressing
        // every later top-level trace.
        // <snippet:reconcile-trace-start>
        bool traceEnabled = Diagnostics.ReactorEventSource.Log.IsEnabled(
            global::System.Diagnostics.Tracing.EventLevel.Informational,
            Diagnostics.ReactorEventSource.Keywords.Reconcile);
        bool emitTrace = traceEnabled && _reconcileTraceDepth++ == 0;
        if (emitTrace)
        {
            Diagnostics.ReactorEventSource.Log.ReconcileStart(
                newElement?.GetType().Name ?? "null");
        }
        // </snippet:reconcile-trace-start>
        if (_debugReconcileDepth++ == 0)
        {
            DebugElementsDiffed = 0;
            DebugElementsSkipped = 0;
            DebugUIElementsCreated = 0;
            DebugUIElementsModified = 0;
            // Drop destination references a previous pass queued but never flushed. Every
            // shipped host flushes at the end of each render, but a pass that threw
            // mid-reconcile — or a Reconcile() caller that never flushes (tests,
            // embedders) — must not accumulate strong UIElement refs. A list clear, so
            // nothing here can throw and strand the depth counter incremented just above.
            _pendingConnectedAnimationStarts.Clear();
            _preparedConnectedAnimationKeys.Clear();
            if (ReactorFeatureFlags.HighlightReconcileChanges)
            {
                (_highlightMounted ??= new()).Clear();
                (_highlightModified ??= new()).Clear();
            }
            // Consume the hot-reload and resource-refresh signals exactly once per
            // top-level pass so every component re-runs Render() even when props/deps
            // are unchanged (and, for a resource refresh, no element is skipped).
            bool hostPass = _realizationThemeDepth == 0;
            var theme = hostPass ? InheritedRequestedTheme(ThemeAnchor) : _realizationTheme;
            _resourceRefreshActive = hostPass
                ? _resourceRefreshArmed || theme != _renderedHostTheme
                : _realizationResourceRefresh;
            if (hostPass)
                _resourceRefreshArmed = false;
            _forceFullRenderActive = ForceFullRenderPending || _resourceRefreshActive;
            ForceFullRenderPending = false;
            _hotReloadRetryActive = HotReloadRetryPending;
            HotReloadRetryPending = false;
            _ambientRequestedTheme = theme;

            // Build the dirty-ancestor path. For every component node
            // whose SelfTriggered is true, walk up the realized visual
            // tree and add each ancestor control. Consumed by Update's
            // shallow-equality short-circuit so the walk can reach the
            // self-triggered descendant even when its ancestor element
            // records are structurally unchanged.
            PopulateDirtyAncestorPath();
        }
        try {
        try
        {
            UIElement? result;
            if (newElement is null or EmptyElement)
            {
                if (existingControl is not null)
                    Unmount(existingControl);
                result = null;
            }
            else if (oldElement is null or EmptyElement || existingControl is null)
                result = Mount(newElement, requestRerender);
            else
                result = ReconcileImperative(oldElement, newElement, existingControl, requestRerender);
            if (_debugReconcileDepth == 1 && _realizationThemeDepth == 0)
                _renderedHostTheme = _ambientRequestedTheme;
            return result;
            // </snippet:reconciler-entry>
        }
        finally
        {
            // <snippet:reconcile-trace-stop>
            if (traceEnabled)
            {
                _reconcileTraceDepth--;
                if (emitTrace)
                {
                    Diagnostics.ReactorEventSource.Log.ReconcileStop(
                        DebugElementsDiffed, DebugElementsSkipped,
                        DebugUIElementsCreated, DebugUIElementsModified);
                }
            }
        // </snippet:reconcile-trace-stop>
        }
        } finally
        {
            if (--_debugReconcileDepth == 0)
            {
                _forceFullRenderActive = false;
                _resourceRefreshActive = false;
                _hotReloadRetryActive = false;
                _ambientRequestedTheme = ElementTheme.Default;
                _dirtyAncestorPath?.Clear();
                _dirtyPathChildren?.Clear();
            }
        }
        }
        finally
        {
            ReferenceDirtySet.EndCommitAndFlush();
        }
    }

    private void PopulateDirtyAncestorPath()
    {
        // Hot path — most renders have zero self-triggered nodes (the
        // pass was triggered by a prop change higher up). Avoid the
        // HashSet allocation entirely until we find one.
        HashSet<UIElement>? set = null;
        Dictionary<UIElement, object>? edges = null;
        foreach (var (control, node) in _componentNodes)
        {
            if (!node.SelfTriggered) continue;
            set ??= _dirtyAncestorPath ?? new HashSet<UIElement>();
            edges ??= _dirtyPathChildren ?? new Dictionary<UIElement, object>();
            // Add the control itself first — Update on the wrapper
            // element that owns this control needs to bypass too so it
            // reaches the Component's UpdateComponent path.
            set.Add(control);
            var child = control;
            var cursor = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(control) as UIElement;
            while (cursor is not null)
            {
                // Record the edge before the early-out: a second self-triggered node joining
                // an already-walked path still enters it through a child of its own.
                AddDirtyPathChild(edges, cursor, child);
                if (!set.Add(cursor)) break; // already on a previously-walked path
                child = cursor;
                cursor = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(cursor) as UIElement;
            }
        }
        _dirtyAncestorPath = set;
        _dirtyPathChildren = edges;
    }

    // Tracks top-level Reconcile() entries so trace start/stop only fires once
    // per pass. Only mutated when the Reconcile keyword is enabled.
    private int _reconcileTraceDepth;

    // Test-only accessor (InternalsVisibleTo Reactor.Tests / Reactor.AppTests.Host).
    // The invariant is that this returns to 0 after every top-level pass; a
    // non-zero value between passes means a nested Reconcile incremented
    // without a matching decrement, which silently suppresses all later spans.
    internal int ReconcileTraceDepthForTests => _reconcileTraceDepth;

    /// <summary>
    /// Starts the <c>Microsoft-UI-Reactor-Perf</c> ComponentRender span for a component node
    /// when its keyword is on (an inactive span otherwise). Disposed at the end of the mount or
    /// update, so the span covers Render(), the effects flush and the subtree's reconcile — the
    /// component's inclusive cost, including the WinUI controls its subtree creates.
    /// </summary>
    private static Diagnostics.ReactorPerfTrace.ComponentSpan BeginPerfComponentSpan(
        ComponentNode node, Element element, string reason, bool memoMiss = false)
    {
        if (!Diagnostics.ReactorPerfTrace.ComponentEnabled) return default;
        return Diagnostics.ReactorPerfTrace.ComponentSpan.Begin(
            (ulong)node.EnsureDiagnosticId(), PerfComponentName(node, element), reason, element.CallSite, memoMiss);
    }

    /// <summary>
    /// A component node's name on the <c>Microsoft-UI-Reactor-Perf</c> spans: a class
    /// component's type name, or a function / memo component's render-function name
    /// (<see cref="Diagnostics.ComponentNames.ForRenderFunction(Element)"/>). The latter reads
    /// <c>Delegate.Method</c>, so it is resolved on the component's first traced render and
    /// kept for its lifetime, like its componentId.
    /// </summary>
    internal static string PerfComponentName(ComponentNode node, Element? element)
        => node.Component is null && element is FuncElement or MemoElement
            ? node.PerfName ??= Diagnostics.ComponentNames.ForRenderFunction(element)
            : Diagnostics.ComponentNames.For(node.Component, element);

    /// <summary>
    /// Emits <c>ReactorEventSource.ComponentRendered</c> for a component node and keeps
    /// the id → wrapper registry current. Only called when the event is enabled. A null
    /// <paramref name="wrapper"/> reports the render without touching the registry
    /// (a render that threw into an <c>ErrorBoundary</c>, whose wrapper is discarded or
    /// replaced by the boundary's fallback).
    /// </summary>
    private static void EmitComponentRendered(
        ComponentNode node, UIElement? wrapper, Element element, string reason, long startTimestamp)
    {
        long id = node.EnsureDiagnosticId();
        // Rechecks the live gate: the decision to trace was made before Render() ran, and a
        // listener can dispose itself from an earlier event's callback (for instance a
        // hot-reload hook-order retry's aborted attempt). Nothing is mapped while it is off.
        if (wrapper is not null && Diagnostics.ComponentRenderTrace.IsEnabled)
            Diagnostics.ComponentRenderControls.Registry.Track(id, wrapper, mapControlToId: true);
        Diagnostics.ReactorEventSource.Log.ComponentRendered(
            Diagnostics.ComponentNames.For(node.Component, element),
            id,
            reason,
            Diagnostics.ComponentRenderTrace.ElapsedMicroseconds(startTimestamp));
    }

    /// <summary>
    /// Maps a freshly mounted component's id to its wrapper (event enabled only). If an
    /// enclosing ErrorBoundary later discards the subtree, its rollback (unmount of completed
    /// subtrees, or the boundary mount journal) forgets the mapping again. Rechecks the live
    /// gate: a listener can be disabled from the event's own callback, after the mount
    /// decided to trace, and nothing is mapped while it is off.
    /// </summary>
    private static void TrackMountedComponent(ComponentNode node, UIElement wrapper)
    {
        if (Diagnostics.ComponentRenderTrace.IsEnabled)
            Diagnostics.ComponentRenderControls.Registry.Track(node.DiagnosticId, wrapper, mapControlToId: true);
    }

    /// <summary>
    /// <c>ReactorEventSource.RenderError</c> for a component whose Render() threw and was
    /// replaced by the error fallback. Shared by the mount and update paths so both name
    /// the component the same way (<see cref="Diagnostics.ComponentNames"/>). Only the
    /// component name and exception type are written: the sink redacts the message
    /// (TASK-064), so <paramref name="ex"/>'s virtual <c>Message</c> is never read here —
    /// an app exception whose <c>Message</c> override throws must not replace the original
    /// exception on this error path (the ErrorBoundary arms rethrow it unchanged).
    /// </summary>
    internal static void EmitRenderError(string componentName, Exception ex)
    {
        if (Diagnostics.ReactorEventSource.Log.IsEnabled(
                global::System.Diagnostics.Tracing.EventLevel.Error,
                Diagnostics.ReactorEventSource.Keywords.Errors))
        {
            Diagnostics.ReactorEventSource.Log.RenderError(componentName, ex.GetType().Name, string.Empty);
        }
    }

    /// <summary>
    /// <c>ReactorEventSource.CommitError</c> for a host-level failure outside any component's
    /// render or effects (<paramref name="phase"/> is <see cref="RenderErrorSource.Reconcile"/>
    /// or <see cref="RenderErrorSource.Cleanup"/>) that replaced the host's content with the
    /// error fallback. Names the host's root (<c>FuncElement</c> for a render-function root)
    /// and the exception type only — the message is never read, as for
    /// <see cref="EmitRenderError"/>.
    /// </summary>
    internal static void EmitCommitError(RenderErrorSource phase, Component? root, Exception ex)
    {
        if (Diagnostics.ReactorEventSource.Log.IsEnabled(
                global::System.Diagnostics.Tracing.EventLevel.Error,
                Diagnostics.ReactorEventSource.Keywords.Errors))
        {
            Diagnostics.ReactorEventSource.Log.CommitError(
                phase switch
                {
                    RenderErrorSource.Reconcile => nameof(RenderErrorSource.Reconcile),
                    RenderErrorSource.Cleanup => nameof(RenderErrorSource.Cleanup),
                    _ => phase.ToString(),
                },
                root is not null ? Diagnostics.ComponentNames.For(root, element: null) : nameof(FuncElement),
                ex.GetType().Name);
        }
    }

    private static void FlushEffectsTraced(RenderContext ctx, string? componentName)
    {
        // Fast path when the Render keyword is off: no Stopwatch, no event emit.
        if (!Diagnostics.ReactorEventSource.Log.IsEnabled(
                global::System.Diagnostics.Tracing.EventLevel.Informational,
                Diagnostics.ReactorEventSource.Keywords.Render))
        {
            ctx.FlushEffects();
            return;
        }

        var name = componentName ?? string.Empty;
        Diagnostics.ReactorEventSource.Log.EffectsFlushStart(name);
        var start = global::System.Diagnostics.Stopwatch.GetTimestamp();
        try { ctx.FlushEffects(); }
        finally
        {
            var us = (long)((global::System.Diagnostics.Stopwatch.GetTimestamp() - start)
                * 1_000_000.0 / global::System.Diagnostics.Stopwatch.Frequency);
            Diagnostics.ReactorEventSource.Log.EffectsFlushStop(name, us);
        }
    }

    /// <summary>
    /// The original C# imperative reconciliation path.
    /// </summary>
    private UIElement? ReconcileImperative(
        Element oldElement, Element newElement,
        UIElement existingControl, Action requestRerender)
    {
        // Contract: when we return a replacement (either from Update remounting or
        // from a type-change full remount), the caller must place it in the parent
        // collection — e.g. `g.Children[i] = replacement`. WinUI's indexer assignment
        // detaches the old control from its parent as part of the swap, so we must
        // leave the parent collection alone here. The UnmountAndPool path would
        // invoke ElementPool.Return.DetachFromParent, which synchronously removes
        // the old child from the parent's Children collection before the caller's
        // assignment runs; that shifts subsequent sibling indices and corrupts any
        // positional update loop that was iterating over the collection (see #34,
        // where DataGrid row cells were dropped off the end during inline-edit flips).
        //
        // Trade-off: the replaced control is no longer handed back to ElementPool.
        // Pooling is a performance optimization; correctness dominates here. Type
        // flips are uncommon in practice (inline edit transitions, ErrorBoundary
        // remounts) so the lost reuse is minor. A follow-up could reintroduce
        // pool-return by having the caller invoke a post-swap pool hook once the
        // old control is detached, but doing so safely across every Reconcile
        // caller is out of scope for this fix.
        if (CanUpdate(oldElement, newElement))
        {
            // An update that already unmounted the control it replaced (a target-wrapping flyout
            // whose Target changes element type) is not followed by a second unmount.
            var replacement = UpdateSlotChild(oldElement, newElement, existingControl, requestRerender, out var unmountedByUpdate);
            if (replacement is not null && replacement != existingControl && !unmountedByUpdate)
                Unmount(existingControl);
            return replacement ?? existingControl;
        }

        // Hot Reload: an edited component subclass mints a new Type token, so
        // CanUpdate is false above. Migrate the subtree in place (preserving
        // descendant state) instead of unmount/mount. Gated on IsHotReloadLive
        // (MetadataUpdater.IsSupported) so the reflection-bearing path is
        // statically dead under NativeAOT (spec 049 §7/§8).
        if (HotReloadService.IsHotReloadLive
            && TryHotReloadMigrateComponent(oldElement, newElement, existingControl, requestRerender))
            return existingControl;

        Unmount(existingControl);
        return Mount(newElement, requestRerender);
    }

    /// <summary>
    /// Issue #326 (pr-review H1) — adopt a freshly-mounted <paramref name="replacement"/>
    /// into an already-realized, still-parented wrapper so an out-of-band refresh
    /// (<see cref="ElementFactory{T}.RefreshRealizedItems"/>) whose row key changed
    /// (<see cref="CanUpdate"/> == false → <see cref="ReconcileImperative"/> returned a
    /// brand-new control) can reset per-item component state without reparenting.
    ///
    /// <para>The WinUI ItemsRepeater that owns realized rows is NOT a Panel, so its
    /// children cannot be replaced from managed code — there is no Children collection
    /// to assign into the way the normal Reconcile contract
    /// (<c>g.Children[i] = replacement</c>) or the framework's GetElement return-channel
    /// relies on. When both the realized control and the replacement are
    /// component-wrapper Borders (the dominant case: authors put per-item state in a
    /// Component and key it with <c>.WithKey($"{id}:{rev}")</c>), keep the parented
    /// wrapper in place and move the fresh component subtree plus its
    /// <see cref="_componentNodes"/> entry onto it. The discarded replacement wrapper is
    /// left empty for GC.</para>
    ///
    /// <para>Returns false when the shapes don't permit an in-place adopt (e.g. a root
    /// TYPE change where neither side is a component wrapper); the caller then falls
    /// back to tracking-only reconciliation.</para>
    /// </summary>
    internal bool TryAdoptRealizedReplacement(UIElement realized, UIElement replacement)
    {
        if (realized is not Border realizedWrapper || replacement is not Border replacementWrapper)
            return false;
        if (!_componentNodes.TryGetValue(replacement, out var freshNode))
            return false;

        // Migrate the fresh component node onto the still-parented wrapper. The old
        // node keyed on `realized` was already torn down + removed by the Unmount
        // inside ReconcileImperative; overwrite defensively regardless.
        _componentNodes.Remove(replacement);
        _componentNodes[realized] = freshNode;
        // Registry bookkeeping: while ComponentRendered is enabled the id moves onto the
        // realized wrapper. If the listener was disabled (possibly by the fresh mount's own
        // event callback), drop the mapping the mount gave the soon-discarded replacement
        // wrapper; the next traced render of this node re-tracks its id onto `realized`.
        if (freshNode.DiagnosticId != 0)
        {
            if (Diagnostics.ComponentRenderTrace.IsEnabled)
                Diagnostics.ComponentRenderControls.Registry.Track(freshNode.DiagnosticId, realized, mapControlToId: true);
            else
                Diagnostics.ComponentRenderControls.Registry.Forget(freshNode.DiagnosticId, replacement);
        }

        // Move the fresh visual subtree into the parented wrapper. Assigning
        // Border.Child detaches it from `replacementWrapper` first (and detaches the
        // old, already-unmounted subtree from `realizedWrapper`), leaving the
        // replacement wrapper empty + unreferenced.
        var freshChild = replacementWrapper.Child;
        replacementWrapper.Child = null;
        realizedWrapper.Child = freshChild;
        return true;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Component reconciliation
    // ════════════════════════════════════════════════════════════════════

    private void ReconcileComponent(Element oldEl, Element newEl, UIElement control, Action requestRerender)
    {
        if (!_componentNodes.TryGetValue(control, out var node))
        {
            _logger?.LogWarning("ReconcileComponent: component node not found for control — component will not update");
            return;
        }

        // ── Memo check: skip render if props/context unchanged and not self-triggered ──
        bool selfTriggered = node.SelfTriggered;
        node.SelfTriggered = false;

        // ComponentRendered bookkeeping: what forced or let this render through. Plain
        // locals holding interned constants, so they cost nothing when tracing is off.
        bool forcedRender = _forceFullRenderActive;
        string? memoReason = null;
        // Set when the reason classification below already read the event's gate, so
        // the render path reuses it instead of checking a second time.
        bool? traceEnabled = null;
        // A memo gate (class props / ShouldUpdate, Memo dependencies) was evaluated: a render that
        // follows is a memo miss on the perf provider, a skip a memo hit.
        bool memoGated = false;

        // Hot reload forces every component to re-run Render(); the new method
        // body lives only on the type, not in props/deps, so the memo gate
        // would otherwise skip it.
        if (_forceFullRenderActive)
            selfTriggered = true;

        if (!selfTriggered)
        {
            bool skipRender = false;

            if (node.Component is not null && newEl is ComponentElement newCompEl)
            {
                // Class component memo check
                var oldProps = node.PreviousProps;
                var newProps = newCompEl.Props;

                bool propsChanged;
                if (node.Component is IPropsReceiver)
                {
                    // Component<TProps>: delegate to ShouldUpdate(oldProps, newProps)
                    propsChanged = ShouldUpdateWithProps(node.Component, oldProps, newProps);
                }
                else
                {
                    // Propless Component: delegate to ShouldUpdate()
                    propsChanged = node.Component.ShouldUpdate();
                }

                bool contextChanged = HasConsumedContextChanged(node);
                skipRender = !propsChanged && !contextChanged;
                memoGated = true;
                // Props components: a props change wins, else it was the context.
                // Propless components (ShouldUpdate() true or a context change): the
                // context wins, else nothing could skip it.
                memoReason = node.Component is IPropsReceiver
                    ? propsChanged
                        ? Diagnostics.ComponentRenderTrace.Reasons.Props
                        : Diagnostics.ComponentRenderTrace.Reasons.Context
                    : contextChanged
                        ? Diagnostics.ComponentRenderTrace.Reasons.Context
                        : Diagnostics.ComponentRenderTrace.Reasons.Parent;
            }
            else if (node.Context is not null && newEl is MemoElement newMemo)
            {
                // MemoElement memo check
                var oldDeps = node.MemoDependencies;
                var newDeps = newMemo.Dependencies;
                bool depsChanged = oldDeps is null && newDeps is null
                    ? false // both null = render once, never re-render from parent
                    : oldDeps is null || newDeps is null || !RenderContext.DepsEqual(oldDeps, newDeps);
                bool contextChanged = HasConsumedContextChanged(node);
                skipRender = !depsChanged && !contextChanged;
                memoGated = true;
                memoReason = depsChanged
                    ? Diagnostics.ComponentRenderTrace.Reasons.Props
                    : Diagnostics.ComponentRenderTrace.Reasons.Context;
            }
            else if (newEl is FuncElement
                && ((traceEnabled = Diagnostics.ComponentRenderTrace.IsEnabled).Value
                    || Diagnostics.ReactorPerfTrace.ComponentEnabled)
                && HasConsumedContextChanged(node))
            {
                // No gate can skip a function component, so this only refines the
                // reported reason (and is only computed while a render event is traced).
                memoReason = Diagnostics.ComponentRenderTrace.Reasons.Context;
            }

            if (skipRender)
            {
                // A memo hit on the perf provider (the render it saved is not a span).
                if (Diagnostics.ReactorPerfTrace.ComponentEnabled)
                    Diagnostics.ReactorPerfTrace.ComponentSkip((ulong)node.EnsureDiagnosticId(), PerfComponentName(node, newEl));

                // Still update the element reference (modifiers may have changed on the ComponentElement itself)
                node.Element = newEl;

                // Stale-delegate guard (issue #151): even though we skip re-rendering,
                // refresh the live Props on the instance so any handler that reads
                // Props.X at *dispatch* time invokes the CURRENT delegate rather than the
                // one captured when the child last rendered. This is what makes an
                // always-equal callbacks slot (Callbacks<T>) safe: data fields drive the
                // memo decision, but the latest callbacks are still reachable off Props.
                // Mirrors the leaf-level Tag-trampoline refresh (Element.cs HasCallbacks).
                // The skipped data fields are equal by construction, so refreshing them is
                // a no-op for the rendered subtree; only the excluded slots (callbacks)
                // actually change. PreviousProps is kept in sync as the next baseline.
                if (node.Component is IPropsReceiver skipReceiver
                    && newEl is ComponentElement skipCompEl && skipCompEl.Props is not null)
                {
                    skipReceiver.SetProps(skipCompEl.Props);
                    node.PreviousProps = skipCompEl.Props;
                }
                return;
            }
        }

        // ── Render the component ──
        // Perf span: Render(), effects and the subtree reconcile below (closes on every exit).
        using var perfSpan = Diagnostics.ReactorPerfTrace.ComponentEnabled
            ? BeginPerfComponentSpan(node, newEl, Diagnostics.ComponentRenderTrace.ClassifyUpdate(
                forcedRender, HotReloadService.WithinUpdatePass || _hotReloadRetryActive, selfTriggered, memoReason),
                memoMiss: memoGated)
            : default;

        // Pass the component's own wrapped rerender to children so that child state
        // changes propagate SelfTriggered up through all component ancestors.
        var componentRerender = CreateComponentRerender(node, requestRerender);

        // Only compute component name + timestamps when the Render keyword is
        // enabled, so the disabled path avoids the reflection and Stopwatch work.
        bool traceRender = Diagnostics.ReactorEventSource.Log.IsEnabled(
            global::System.Diagnostics.Tracing.EventLevel.Informational,
            Diagnostics.ReactorEventSource.Keywords.Render);
        string? componentName = null;
        long renderStart = 0;
        if (traceRender)
        {
            componentName = Diagnostics.ComponentNames.For(node.Component, newEl);
            Diagnostics.ReactorEventSource.Log.ComponentRenderStart(
                componentName, selfTriggered ? "self" : "parent");
            renderStart = global::System.Diagnostics.Stopwatch.GetTimestamp();
        }
        bool traceRendered = traceEnabled ?? Diagnostics.ComponentRenderTrace.IsEnabled;
        long renderedStart = traceRendered ? global::System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        // The reason is fixed for this render (its inputs do not change below); computed
        // only while traced. renderReported: the fallback catch already emitted.
        string? renderedReason = traceRendered
            ? Diagnostics.ComponentRenderTrace.ClassifyUpdate(
                forcedRender, HotReloadService.WithinUpdatePass || _hotReloadRetryActive, selfTriggered, memoReason)
            : null;
        bool renderReported = false;

        Element newChildElement;
        // The RenderContext backing whichever branch we render (component or
        // func/memo). Used to recover an in-flight hook-order change during a
        // hot-reload pass: reset this context's hook state and re-render once.
        RenderContext? renderCtx = node.Component?.Context ?? node.Context;
        bool hotReloadRetried = false;
        bool inEffects = false;
        while (true)
        {
            inEffects = false;
            try
            {
                if (node.Component is not null)
                {
                    // Update props before re-rendering so the component sees fresh data
                    if (newEl is ComponentElement compEl && compEl.Props is not null
                        && node.Component is IPropsReceiver receiver)
                    {
                        receiver.SetProps(compEl.Props);
                    }

                    node.Component.Context.BeginRender(componentRerender, _contextScope, Owner);
                    using (ValidationRenderScope.Begin(ReadContext(ValidationContexts.Current)))
                    {
                        newChildElement = ValidationRenderScope.ApplyProvide(node.Component.Render());
                    }
                    inEffects = true;
                    FlushEffectsTraced(node.Component.Context, componentName);
                }
                else if (node.Context is not null && newEl is FuncElement func)
                {
                    node.Context.BeginRender(componentRerender, _contextScope, Owner);
                    using (ValidationRenderScope.Begin(ReadContext(ValidationContexts.Current)))
                    {
                        newChildElement = ValidationRenderScope.ApplyProvide(func.RenderFunc(node.Context));
                    }
                    inEffects = true;
                    FlushEffectsTraced(node.Context, componentName);
                }
                else if (node.Context is not null && newEl is MemoElement memo)
                {
                    node.Context.BeginRender(componentRerender, _contextScope, Owner);
                    using (ValidationRenderScope.Begin(ReadContext(ValidationContexts.Current)))
                    {
                        newChildElement = ValidationRenderScope.ApplyProvide(memo.RenderFunc(node.Context));
                    }
                    inEffects = true;
                    FlushEffectsTraced(node.Context, componentName);
                }
                else
                {
                    if (traceRender)
                        Diagnostics.ReactorEventSource.Log.ComponentRenderStop(componentName!, 0);
                    return;
                }
            }
            // Tree-wide hot-reload hook-order recovery: when an edit reorders or
            // retypes hooks in a *non-root* child, its Render() throws here.
            // Inside a hot-reload pass we reset that child's hook state and
            // re-render once (matching the root recovery in the host), so the
            // edit applies and the subtree's descendant state is preserved,
            // instead of falling through to the error-fallback path below.
            catch (HookOrderException ex) when (!hotReloadRetried
                && renderCtx is not null
                && HotReloadService.WithinUpdatePass)
            {
                // The aborted attempt still ran Render(); report it before logging, so a
                // slow logger is not charged to it.
                if (traceRendered)
                    EmitComponentRendered(node, null, newEl, Diagnostics.ComponentRenderTrace.Reasons.HotReload, renderedStart);
                _logger?.LogWarning(ex,
                    "Hot reload: hook order/type changed in child component — " +
                    "resetting state and re-rendering: {ComponentName}",
                    componentName ?? Diagnostics.ComponentNames.For(node.Component, newEl));
                hotReloadRetried = true;
                renderCtx.ResetForHotReload();
                // Time the retry on its own (it reports itself after the loop), starting
                // after the reset ran the effect cleanups.
                if (traceRendered)
                    renderedStart = global::System.Diagnostics.Stopwatch.GetTimestamp();
                continue;
            }
            // Inside an ErrorBoundary the exception propagates to the boundary, which
            // renders its fallback. Name the failing component here, at the throw site —
            // the boundary itself cannot know which descendant threw — so a listener sees
            // the error even though the app recovered. Rethrown unchanged. No OOM/SO carve-out:
            // the boundary catches every exception, so every one it recovers from is reported.
            // Outside a boundary, OOM/SO skip the fallback arm below and propagate to the
            // host, which also recovers — so they are reported here too. An exception the app
            // declined via RenderError.Propagate() was already reported where it was thrown,
            // so it passes through unreported (issue #1291).
            catch (Exception ex) when ((_errorBoundaryDepth > 0 || ex is OutOfMemoryException or StackOverflowException)
                && !RenderErrorDispatch.IsPropagating(ex))
            {
                // The render still happened: report it, as the trailing arm does for the rest.
                if (traceRendered)
                    EmitComponentRendered(node, null, newEl, renderedReason!, renderedStart);
                EmitRenderError(componentName ?? Diagnostics.ComponentNames.For(node.Component, newEl), ex);
                throw;
            }
            catch (Exception ex) when (_errorBoundaryDepth == 0 && ex is not OutOfMemoryException and not StackOverflowException && !RenderErrorDispatch.IsPropagating(ex))
            {
                // Report before building the fallback: its time is not this render's, and
                // a fallback that throws must not swallow the event.
                if (traceRendered)
                {
                    EmitComponentRendered(node, control, newEl, renderedReason!, renderedStart);
                    renderReported = true;
                }
                var failedName = componentName ?? Diagnostics.ComponentNames.For(node.Component, newEl);
                _logger?.LogError(ex, "Component Render() threw: {ComponentName}", failedName);
                // Before the fallback: the app's handler may call Propagate(), which throws.
                EmitRenderError(failedName, ex);
                newChildElement = BuildInTreeFallback(ex, inEffects, node.Component?.GetType().Name);
            }
            // Every other exception propagates (to an enclosing ErrorBoundary, or a fatal
            // one to the host); the render still happened, so report it and rethrow.
            catch (Exception) when (traceRendered)
            {
                EmitComponentRendered(node, null, newEl, renderedReason!, renderedStart);
                throw;
            }
            break;
        }

        if (traceRender)
        {
            var renderElapsedUs = (long)((global::System.Diagnostics.Stopwatch.GetTimestamp() - renderStart)
                * 1_000_000.0 / global::System.Diagnostics.Stopwatch.Frequency);
            Diagnostics.ReactorEventSource.Log.ComponentRenderStop(componentName!, renderElapsedUs);
        }
        if (traceRendered && !renderReported)
            EmitComponentRendered(node, control, newEl, renderedReason!, renderedStart);

        // Dereference the Border wrapper to get the actual child control.
        // Each component is wrapped in a Border as an identity anchor, so we
        // reconcile the child inside the wrapper, not the wrapper itself.
        var existingChild = (control as Border)?.Child;
        var newControl = global::Microsoft.UI.Reactor.Hosting.ReactorFeatures.DevtoolsSupported && Diagnostics.ReactorSourcePublisher.IsEnabled
            ? ReconcileUnderOwner(node.RenderedElement, newChildElement, existingChild, componentRerender,
                Diagnostics.ReactorSourceFormat.ComponentName(node.Component, newEl))
            : Reconcile(node.RenderedElement, newChildElement, existingChild, componentRerender);
        if (control is Border border)
        {
            if (newControl != existingChild)
                border.Child = newControl; // handles both replacement and null (child removed)
        }

        node.RenderedElement = newChildElement;
        node.Element = newEl;
        // Store current props for next memo comparison
        if (newEl is ComponentElement compEl2)
            node.PreviousProps = compEl2.Props;
        else if (newEl is MemoElement memoEl)
            node.MemoDependencies = memoEl.Dependencies;
    }

    /// <summary>
    /// Creates a rerender callback that marks the component node as self-triggered
    /// before invoking the parent requestRerender, so the memo check is bypassed.
    /// Captures the node directly to avoid accessing _componentNodes from background threads.
    /// </summary>
    private Action CreateComponentRerender(ComponentNode node, Action requestRerender)
    {
        return () =>
        {
            // SECURITY (TASK-059): bound rerender re-entrancy. A component
            // that calls its own requestRerender synchronously from Render()
            // or an effect's cleanup would otherwise blow the stack.
            if (t_rerenderDepth >= MaxRerenderReentrancy)
                throw new InvalidOperationException(
                    "Render loop detected: requestRerender re-entered more than " +
                    $"{MaxRerenderReentrancy} times. Likely cause: setState called " +
                    "synchronously inside Render() or effect cleanup.");

            // SECURITY (TASK-063): if invoked off the UI thread (e.g.,
            // setState(threadSafe:true) firing from a worker, or a worker
            // that owns its own DispatcherQueue), marshal onto the UI
            // dispatcher so we don't race the reconciler and ElementPool from
            // a background thread. That is this reconciler's owning thread
            // (its host's), not the process-wide one: hosts on a second UI
            // thread must render on theirs. A non-null GetForCurrentThread()
            // doesn't imply UI affinity — the only authoritative check is
            // HasThreadAccess on the UI DQ.
            var uiDq = OwningDispatcher ?? Microsoft.UI.Reactor.ReactorApp.UIDispatcher;
            bool onUiThread = false;
            if (uiDq is not null)
            {
                try { onUiThread = uiDq.HasThreadAccess; }
                catch { onUiThread = false; }
            }

            if (!onUiThread && uiDq is not null)
            {
                uiDq.TryEnqueue(() =>
                {
                    node.SelfTriggered = true;
                    InvokeRerenderTracked(requestRerender, node);
                });
                return;
            }

            // Either we're on the UI thread, or no UI DQ has been captured
            // (test/headless host) — run inline.
            node.SelfTriggered = true;
            InvokeRerenderTracked(requestRerender, node);
        };
    }

    private static void InvokeRerenderTracked(Action requestRerender, ComponentNode requester)
    {
        t_rerenderDepth++;
        // The perf RenderPass names the component that asked: the innermost wrapper claims the
        // thread before the request climbs through its ancestors' wrappers to the host.
        try { Diagnostics.RenderPassRequests.InvokeAs(requester, requestRerender); }
        finally { t_rerenderDepth--; }
    }

    /// <summary>
    /// Checks whether any context consumed by a component has changed since the last render.
    /// </summary>
    private bool HasConsumedContextChanged(ComponentNode node)
    {
        var renderCtx = node.Component?.Context ?? node.Context;
        if (renderCtx is null) return false;

        foreach (var ctxHook in renderCtx.ContextHooks)
        {
            var currentValue = _contextScope.Read(ctxHook.Context);
            if (!Equals(currentValue, ctxHook.LastValue))
                return true;
        }
        return false;
    }

    internal bool HasConsumedContextChangedInSubtree(UIElement control)
    {
        if (_componentNodes.TryGetValue(control, out var node)
            && HasConsumedContextChanged(node))
            return true;

        // A virtualized host has no realized consumer for rows it has yet to realize,
        // so its own snapshot stands in for them: if the providers above it changed,
        // decline the skip so the Update path re-snapshots before the next realization.
        if (TryGetContextCapture(control) is { } capture
            && (capture.IsStale(_contextScope) || capture.ThemeGeneration != _themeGeneration))
            return true;

        bool found = false;
        ForEachReactorChildControl(control, child =>
        {
            if (HasConsumedContextChangedInSubtree(child))
            {
                found = true;
                return false;
            }

            return true;
        });

        return found;
    }

    internal bool ShouldDeclineSkip(UIElement control)
        => HasActiveContextValues && HasConsumedContextChangedInSubtree(control);

    /// <summary>
    /// Calls ShouldUpdate(oldProps, newProps) on a Component&lt;TProps&gt; via interface dispatch.
    /// </summary>
    private static bool ShouldUpdateWithProps(Component component, object? oldProps, object? newProps)
    {
        if (component is IPropsComparable comparable)
            return comparable.CompareProps(oldProps, newProps);

        // Fallback: always re-render
        return true;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Children reconciliation (keyed LIS + positional)
    // ════════════════════════════════════════════════════════════════════

    private void ReconcileChildren(
        Element[] oldChildren, Element[] newChildren,
        WinUI.Panel panel, Action requestRerender)
        => ReconcileChildrenTraced(oldChildren, newChildren, new PanelChildCollection(panel), requestRerender, panel);

    // Spec 047 §14 — keyed-reconcile entrypoint for the V1 descriptor Panel<>
    // strategy. The descriptor resolves the live collection as a
    // UIElementCollection (not a WinUI.Panel), so this overload wraps that
    // collection directly. Behavior — including the diagnostics ChildReconcile
    // event scope — is identical to the WinUI.Panel-based ReconcileChildren so
    // descriptor panels match the legacy hand-coded Update* methods byte-for-byte.
    internal void ReconcilePanelChildrenInto(
        Element[] oldChildren, Element[] newChildren,
        UIElementCollection collection, Action requestRerender,
        UIElement? parentControl = null)
        => ReconcileChildrenTraced(oldChildren, newChildren, new PanelChildCollection(collection), requestRerender, parentControl);

    private void ReconcileChildrenTraced(
        Element[] oldChildren, Element[] newChildren,
        PanelChildCollection childCollection, Action requestRerender, UIElement? parentControl)
    {
        // Skip the try/finally and event emits when neither provider wants the span.
        bool traceSource = Diagnostics.ReactorEventSource.Log.IsEnabled(
            global::System.Diagnostics.Tracing.EventLevel.Informational,
            Diagnostics.ReactorEventSource.Keywords.Reconcile);
        bool tracePerf = Diagnostics.ReactorPerfTrace.ChildrenEnabled;
        if (!traceSource && !tracePerf)
        {
            if (!_perfTally)
            {
                ChildReconciler.Reconcile(oldChildren, newChildren, childCollection, this, requestRerender, parentControl);
                CountPerfMoves(childCollection);
                return;
            }
            // Only the enclosing Reconcile span's edit tally wants this container's counts.
            try { ChildReconciler.Reconcile(oldChildren, newChildren, childCollection, this, requestRerender, parentControl); }
            finally { _perfEdits.Add(childCollection); }
            CountPerfMoves(childCollection);
            return;
        }
        if (traceSource)
            Diagnostics.ReactorEventSource.Log.ChildReconcileStart(oldChildren.Length, newChildren.Length);
        if (tracePerf)
            Diagnostics.ReactorPerfTrace.ChildReconcileStart((uint)oldChildren.Length, (uint)newChildren.Length);
        try { ChildReconciler.Reconcile(oldChildren, newChildren, childCollection, this, requestRerender, parentControl); }
        finally
        {
            if (traceSource)
                Diagnostics.ReactorEventSource.Log.ChildReconcileStop();
            // The counts are this container's own collection edits; a nested container's edits
            // are reported by its own (nested) span.
            if (tracePerf)
                Diagnostics.ReactorPerfTrace.ChildReconcileStop(
                    childCollection.InsertCount, childCollection.RemoveCount,
                    childCollection.MoveCount, childCollection.ReplaceCount);
            if (_perfTally)
                _perfEdits.Add(childCollection);
        }
        CountPerfMoves(childCollection);
    }

    // A repositioned child is a change the mount and unmount counts do not see (inserts,
    // removals and replacements mount or unmount a control, which counts it).
    private static void CountPerfMoves(PanelChildCollection childCollection)
    {
        if (childCollection.MoveCount != 0)
            CountPerfChanges(childCollection.MoveCount);
    }

    /// <summary>
    /// Low-level update of one child control, for callers that have already checked the new
    /// element can update it in place. Code outside the framework should call
    /// <see cref="Reconcile"/>, which makes that check and unmounts a control it replaces.
    /// </summary>
    /// <remarks>
    /// Patches <paramref name="control"/> to match <paramref name="newEl"/> and returns null, or
    /// returns a new control when the update had to build one. The caller then unmounts
    /// <paramref name="control"/> with <see cref="UnmountChild"/> and puts the new control in its
    /// slot, in that order, as <see cref="Reconcile"/> does: unmounting reads state from the control
    /// while it is still in the tree, such as a connected-animation snapshot. It doesn't check that
    /// <paramref name="newEl"/> has the element type and key of <paramref name="oldEl"/>: a changed
    /// type throws <c>InvalidCastException</c> for a built-in control. The analyzer rule
    /// <c>REACTOR_LIFECYCLE_003</c> flags calls to this method and offers to switch them to
    /// <see cref="Reconcile"/>.
    /// </remarks>
    public UIElement? UpdateChild(Element oldEl, Element newEl, UIElement control, Action requestRerender)
    {
        return Update(oldEl, newEl, control, requestRerender);
    }

    /// <summary>
    /// <see cref="Update"/> for a hosted child slot (dialog/popup/flyout/tooltip content, tree and
    /// list item containers) whose owner swaps a replacement in. When the update hands back a
    /// different control, the old one is unmounted here (unless the update already did) so its
    /// effects, refs and unmount callbacks don't outlive it. Returns the replacement, or null
    /// when the existing control was kept: a drop-in for <see cref="Update"/> at those sites.
    /// </summary>
    internal UIElement? UpdateHostedChild(Element oldEl, Element newEl, UIElement existing, Action requestRerender)
    {
        var replacement = UpdateSlotChild(oldEl, newEl, existing, requestRerender, out var unmountedByUpdate);
        if (replacement is null || IsSameControl(replacement, existing)) return null;
        if (!unmountedByUpdate) Unmount(existing);
        return replacement;
    }

    /// <summary>
    /// <see cref="UpdateChild"/> for a caller that unmounts a replaced control: reports whether
    /// the update already unmounted it (e.g. a same-key Memo remounting its output during a
    /// resource refresh), so the caller doesn't unmount it twice.
    /// </summary>
    internal UIElement? UpdateChildTracked(Element oldEl, Element newEl, UIElement control, Action requestRerender,
        out bool unmountedByUpdate, int childIndex = -1)
    {
        // The child's new position among its panel's children, for a parent .Stagger() (RefreshStaggerDelay).
        var previousIndex = _updatingChildIndex;
        _updatingChildIndex = childIndex;
        try { return UpdateSlotChild(oldEl, newEl, control, requestRerender, out unmountedByUpdate); }
        finally { _updatingChildIndex = previousIndex; }
    }

    // ════════════════════════════════════════════════════════════════════
    //  CanUpdate
    // ════════════════════════════════════════════════════════════════════

    internal bool CanUpdate(Element oldEl, Element newEl)
    {
        if (oldEl.GetType() != newEl.GetType()) return false;
        if (oldEl.Key != newEl.Key) return false;
        if (oldEl is ComponentElement oldComp && newEl is ComponentElement newComp)
            return oldComp.ComponentType == newComp.ComponentType;
        if (oldEl is XamlHostElement oldHost && newEl is XamlHostElement newHost)
            return oldHost.TypeKey == newHost.TypeKey;
        // Issue #327 — a directly-reconciled KeyedMemoElement (used outside a virtualized
        // ElementFactory, which resolves it to its inner before reconcile) can update in place
        // only when its MemoKey is unchanged: same key ⇒ identical factory output by contract ⇒
        // the existing inner control is already correct (the KeyedMemoElement arm in
        // Reconciler.Update then skips). A changed key means different content, so return false to
        // take the standard replace path (unmount + fresh mount of the new factory output) rather
        // than re-invoking the OLD factory at update time to reconstruct the old tree.
        if (oldEl is KeyedMemoElement oldKm && newEl is KeyedMemoElement newKm)
            return Equals(oldKm.MemoKey, newKm.MemoKey);
        // MemoElement can always update to MemoElement (same type check above handles it)
        return true;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Hot Reload — subtree migration on component type identity change
    //  (spec 049 §7 / Phase 3)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When a <see cref="Component"/> subclass is edited under Hot Reload the
    /// runtime mints a <em>new</em> <see cref="Type"/> token, so
    /// <see cref="CanUpdate"/> returns false and the steady-state path would
    /// unmount the whole subtree — discarding every descendant's
    /// <c>UseState</c>. This migrates the node in place instead: it constructs
    /// the post-edit component via the element's factory closure (so
    /// parameterized constructors work — spec §7 Q2), copies surviving instance
    /// fields old → new via <see cref="Microsoft.UI.Reactor.Hosting.ReactorHotReloadCopier"/>,
    /// transfers the live <see cref="RenderContext"/> (hooks + cleanups stay
    /// alive), swaps <see cref="ComponentNode.Component"/>, and re-renders
    /// through the normal <see cref="ReconcileComponent"/> path so prop-set,
    /// Phase-1 hook-order recovery, child reconciliation, tracing and node
    /// bookkeeping all run exactly as a regular update would. The wrapper
    /// <see cref="UIElement"/> is preserved, so <see cref="FrameworkElement"/>
    /// identity (and any running compositor animations on descendant visuals)
    /// survives the edit.
    ///
    /// <para>Returns false — and the caller falls through to unmount/mount —
    /// when the elements are not both class <see cref="ComponentElement"/>s of
    /// the same <c>FullName</c>+assembly, when the keys differ, when the node is
    /// not found, or when the post-edit instance cannot be constructed (no
    /// factory and no parameterless ctor). Only ever reached inside a hot-reload
    /// pass, so it is statically dead under NativeAOT (spec §8).</para>
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reachable only via HotReloadService.IsHotReloadLive; dead under NativeAOT (spec 049 §8).")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Reachable only via HotReloadService.IsHotReloadLive; dead under NativeAOT (spec 049 §8).")]
    internal bool TryHotReloadMigrateComponent(Element oldEl, Element newEl, UIElement existingControl, Action requestRerender)
    {
        // Only a class ComponentElement whose Type token changed but whose
        // logical identity (FullName + assembly) and key are unchanged is an
        // edit we migrate. Anything else (different component, different key,
        // a base-class swap that changes the name) is a genuine identity
        // change the author asked to discard.
        if (oldEl is not ComponentElement oldComp || newEl is not ComponentElement newComp)
            return false;
        if (oldEl.Key != newEl.Key) return false;

        Type oldType = oldComp.ComponentType;
        Type newType = newComp.ComponentType;
        if (oldType.FullName is null || oldType.FullName != newType.FullName) return false;
        if (oldType.Assembly.GetName().Name != newType.Assembly.GetName().Name) return false;

        if (!_componentNodes.TryGetValue(existingControl, out var node) || node.Component is null)
            return false;

        Component newComponent;
        try
        {
            newComponent = newComp.CreateInstance();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Factory closure threw or no usable constructor — let the caller
            // unmount/mount so the edit still takes effect (state is lost). A
            // clean recreate is strictly better than migrating onto a
            // half-built instance. Fatal exceptions deliberately propagate.
            return false;
        }

        Component oldComponent = node.Component;

        // Copy surviving instance fields old → new (added fields keep their
        // default, removed fields drop, native/visual handles are block-listed).
        // Contain reflection faults on pathological user fields so a copy failure
        // falls back to unmount/mount rather than aborting the hot-reload pass —
        // a clean recreate beats proceeding with a partially-migrated instance.
        try
        {
            Microsoft.UI.Reactor.Hosting.ReactorHotReloadCopier.TryMigrate(
                oldComponent, newComponent,
                new HashSet<object>(ReferenceEqualityComparer.Instance));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Field copy faulted midway: discard the partially-migrated instance
            // and recreate the component cleanly. Fatal exceptions propagate.
            return false;
        }

        // Transfer the live render context explicitly (deterministic regardless
        // of copier field-copy order) so hooks and pending cleanups survive.
        // KNOWN LIMITATION (spec 049 §7): UseEffect cleanup closures captured the
        // OLD instance; effects whose deps are unchanged do not re-run, so their
        // cleanups still reference the pre-edit instance until deps change or the
        // subtree unmounts. This matches React's component-identity semantics
        // (preserved state ⇒ effects don't re-fire); most effects close over
        // state values rather than `this`, so this is benign in practice.
        newComponent.Context = oldComponent.Context;

        // Swap the instance and force a render past the memo gate, then delegate
        // to the normal component reconcile path. ReconcileComponent re-sets
        // props from newEl, applies Phase-1 hook-order recovery if the edit
        // changed the hook shape, reconciles the child element against the
        // preserved wrapper, and updates node.Element / node.PreviousProps.
        node.Component = newComponent;
        node.SelfTriggered = true;
        ReconcileComponent(oldEl, newEl, existingControl, requestRerender);

        // Spec 010 — the wrapper is PRESERVED across the migration (that is the whole
        // point), so its back-pointer still names the pre-edit element. Refresh it, or
        // the reported location survives an edit that moved the call site — which is
        // exactly the staleness this route exists to avoid. ReconcileComponent is called
        // directly here rather than through UpdateComponent, so it does not inherit that
        // path's refresh.
        if (existingControl is FrameworkElement migratedFe)
            SetElementTagIfNeeded(migratedFe, newEl);
        if (global::Microsoft.UI.Reactor.Hosting.ReactorFeatures.DevtoolsSupported && Diagnostics.ReactorSourcePublisher.IsEnabled)
            PublishSource(existingControl, newEl);
        Diagnostics.ReactorEventSource.Log.HotReloadStateMigrated(newType.FullName ?? newType.Name);
        return true;
    }

    /// <summary>
    /// Spec 047 §14 Phase 1 — child-slot reconciliation used by the V1
    /// <c>ChildrenStrategy</c> dispatch (SingleContent, NamedSlots). Mirrors
    /// legacy <c>ReconcileChild</c> semantics: structural <c>Update</c> when
    /// <see cref="CanUpdate"/> matches, fresh <c>Mount</c> otherwise, and
    /// <c>Unmount</c> when the slot is being cleared. Returns the
    /// <see cref="UIElement"/> the caller should write back into the slot
    /// (or null when the slot should be cleared).
    ///
    /// <para>When the update returns a new control for the slot, the control it
    /// replaced is unmounted here, as a panel's <c>ChildReconciler</c> and the
    /// component path (<see cref="ReconcileImperative"/>) do. The caller only swaps
    /// the slot, so without this the old subtree's effect cleanups, refs,
    /// <c>.OnUnmount</c> actions and handler unmounts never ran. An update that
    /// returns the control it was handed patched it in place, and an update that
    /// already unmounted the control it replaced is not followed by a second
    /// unmount.</para>
    ///
    /// <para>This exists because the naive replace in early Phase 1
    /// (<c>MountChild</c> + <c>SetChild</c> without comparing old vs new)
    /// destroys descendant state slots on every parent re-render — see the
    /// DynDock / KLR_FlexColumn regressions in
    /// <c>docs/specs/tasks/047-extensible-control-model-phase1-implementation.md</c>
    /// (Phase 1 known defects).</para>
    /// </summary>
    internal UIElement? ReconcileV1Child(Element? oldChild, Element? newChild, UIElement? existing, Action requestRerender)
    {
        if (newChild is null)
        {
            if (existing is not null) Unmount(existing);
            return null;
        }
        if (oldChild is not null && existing is not null && CanUpdate(oldChild, newChild))
        {
            var replacement = UpdateSlotChild(oldChild, newChild, existing, requestRerender, out var unmountedByUpdate);
            if (replacement is null || IsSameControl(replacement, existing))
                return existing;
            if (!unmountedByUpdate)
                Unmount(existing);
            return replacement;
        }
        // Hot Reload component-identity migration (spec 049 §7) — preserve the
        // child subtree's state across an edit instead of unmount/mount.
        if (oldChild is not null && existing is not null
            && HotReloadService.IsHotReloadLive
            && TryHotReloadMigrateComponent(oldChild, newChild, existing, requestRerender))
            return existing;
        if (existing is not null) Unmount(existing);
        return Mount(newChild, requestRerender);
    }

    // The controls the slot updates now running are working on, outermost first, and whether each
    // has been unmounted while its update ran. ReconcileV1Child and ReconcileImperative (behind the
    // public Reconcile) run their updates in these frames. A target-wrapping flyout whose Target
    // changes element type unmounts the old Target itself before it returns the new one
    // (OverlayLifecycle.UpdateFlyoutElement and its MenuFlyout / CommandBarFlyout twins), and a
    // second unmount would run the old subtree's handler and registered unmount callbacks again.
    // Slot updates nest, and a handler deep inside one can unmount a control an outer slot is
    // updating, so every frame watching the unmounted control is marked, not just the innermost.
    private UIElement?[] _slotUpdateControls = Array.Empty<UIElement?>();
    private bool[] _slotUpdateUnmounted = Array.Empty<bool>();
    private int _slotUpdateDepth;

    private UIElement? UpdateSlotChild(Element oldChild, Element newChild, UIElement existing,
        Action requestRerender, out bool unmountedByUpdate)
    {
        // Every access goes through the fields: a nested frame may grow the arrays.
        int frame = _slotUpdateDepth;
        if (frame == _slotUpdateControls.Length)
        {
            int size = frame == 0 ? 8 : frame * 2;
            Array.Resize(ref _slotUpdateControls, size);
            Array.Resize(ref _slotUpdateUnmounted, size);
        }
        _slotUpdateControls[frame] = existing;
        _slotUpdateUnmounted[frame] = false;
        _slotUpdateDepth = frame + 1;
        try
        {
            var replacement = Update(oldChild, newChild, existing, requestRerender);
            unmountedByUpdate = _slotUpdateUnmounted[frame];
            return replacement;
        }
        finally
        {
            _slotUpdateControls[frame] = null;
            _slotUpdateDepth = frame;
        }
    }

    // Matched by reference, not IsSameControl. A frame holds the control it watches, and while a
    // managed wrapper is alive CsWinRT hands back that same wrapper for its native control, so every
    // path that reaches the control to unmount it (the reference an update was handed, or a walk of
    // the visual tree) arrives with this reference. IsSameControl's ReactorState compare is for an
    // update's result, which user code chooses. Here it would add native reads to every unmount
    // inside a slot update, which is nearly every unmount, and attach a ReactorState to a watched
    // control that has none.
    private void NoteUnmountDuringSlotUpdate(UIElement control)
    {
        for (int i = 0; i < _slotUpdateDepth; i++)
        {
            if (ReferenceEquals(_slotUpdateControls[i], control))
                _slotUpdateUnmounted[i] = true;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Shared helpers (used by Mount + Update)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.3) — promoted from internal. Runs each setter
    /// inside the §8.2 echo-suppression scope so user-authored
    /// <c>.Set(c =&gt; c.IsOn = true)</c> writes don't echo through the
    /// engine-wired OnXChanged callback. Provisional API;
    /// see <c>REACTOR_V1_PREVIEW</c>.
    /// </summary>
    public static void ApplySetters<T>(Action<T>[] setters, T control) where T : class
    {
        if (setters.Length == 0) return;
        // §8.2 — run setters inside the echo-suppression scope so that a
        // user-authored `.Set(c => c.IsOn = true)` write does not echo back
        // through the OnXChanged callback the engine already wired. Only
        // enters scope when ReactorState already exists; value-bearing mounts
        // call SetElementTag before ApplySetters, so the state is present for
        // every control that can fire a suppressed change event. Bare leaves
        // (TextBlock, RichTextBlock) skip the scope and pay no allocation.
        ReactorState? state =
            control is FrameworkElement fe
            && fe.GetValue(ReactorAttached.StateProperty) is ReactorState rs
                ? rs : null;
        if (state is not null) state.EchoSuppressScopeDepth++;
        // A setter's opacity write replaces a pending opacity keyframe, as a modifier's does (#1669).
        var opacityProbe = control is UIElement probed ? new OpacityRestartProbe(probed) : default;
        try
        {
            foreach (var setter in setters) setter(control);
            opacityProbe.Complete();
        }
        // Spec 066 §9 row 1: name the modifier when WinUI rejects a facade write after the
        // handoff. The filter returns false, so the exception is unchanged.
        catch (UnauthorizedAccessException) when (FacadeConflictDiagnostic.ReportRejectedSetter(control))
        {
            throw;
        }
        finally
        {
            if (state is not null) state.EchoSuppressScopeDepth--;
        }
    }

    // ── Enum conversions removed — Reactor now uses WinUI types directly ──

    /// <summary>
    /// Tracks the state of a mounted component in the tree.
    /// </summary>
    internal class ComponentNode
    {
        /// <summary>The class-based Component instance (null for function components).</summary>
        public Component? Component { get; set; }
        /// <summary>The RenderContext for function components (null for class components).</summary>
        public RenderContext? Context { get; set; }
        /// <summary>The element tree output from the last Render() call.</summary>
        public Element? RenderedElement { get; set; }
        /// <summary>The ComponentElement or FuncElement that created this node.</summary>
        public Element? Element { get; set; }
        /// <summary>Previous props for memo comparison (class components only).</summary>
        public object? PreviousProps { get; set; }
        /// <summary>Dependencies from the last MemoElement render (null = render once).</summary>
        public object?[]? MemoDependencies { get; set; }
        /// <summary>Set to true when a self-triggered re-render is queued (UseState setter).
        /// Accessed from background threads (UseState callbacks) — use volatile field.</summary>
        private volatile bool _selfTriggered;
        public bool SelfTriggered { get => _selfTriggered; set => _selfTriggered = value; }
        /// <summary>
        /// <c>componentId</c> of this node in <c>ReactorEventSource.ComponentRendered</c> and the
        /// <c>Microsoft-UI-Reactor-Perf</c> events; 0 until first assigned
        /// (<see cref="EnsureDiagnosticId"/>). Lives on the node's <see cref="RenderContext"/>,
        /// so a state change the component makes reports the same id.
        /// </summary>
        public long DiagnosticId => (Component?.Context ?? Context)?.DiagnosticId ?? 0;

        /// <summary><see cref="DiagnosticId"/>, assigning it on first use.</summary>
        public long EnsureDiagnosticId()
            => (Component?.Context ?? Context)?.EnsureDiagnosticId() ?? 0;

        /// <summary>
        /// A function or memo component's name on the perf spans, resolved on its first traced
        /// render (see <see cref="PerfComponentName"/>); null until then and for class components.
        /// </summary>
        public string? PerfName { get; set; }
    }

    /// <summary>
    /// Tracks the state of a mounted ErrorBoundary in the tree.
    /// </summary>
    internal class ErrorBoundaryNode
    {
        public Element ChildElement { get; set; } = null!;
        public Element? RenderedElement { get; set; }
        public Exception? CaughtException { get; set; }
        public Func<Exception, Element> Fallback { get; set; } = null!;
    }

    /// <summary>
    /// Tags the <c>Border</c> wrapper of every mounted component boundary with its element.
    /// Called on this reconciler's UI thread when <see cref="global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled"/>
    /// turns on, so boundaries mounted while it was off become discoverable too — including
    /// cached subtrees that later renders skip without ever reaching <see cref="NeedsTag"/>.
    /// Walks <c>_componentNodes</c>, which holds every mounted Component/Func/Memo.
    /// Only untagged wrappers are filled in: a wrapper that already carries an element keeps
    /// it, because an outer decorator (a <c>Flyout</c> on a component target, for example)
    /// re-tags the wrapper with its own element and resolves its callbacks and unmount
    /// cleanup through that tag.
    /// </summary>
    internal void TagComponentBoundaries()
    {
        foreach (var (wrapper, node) in _componentNodes)
        {
            if (wrapper is not FrameworkElement fe || node.Element is not { } element || !IsComponentBoundary(element))
                continue;
            if (fe.GetValue(ReactorAttached.StateProperty) is ReactorState { Element: not null })
                continue;
            SetElementTagIfNeeded(fe, element);
        }
    }

    /// <summary>
    /// Hot Reload (spec 049 §6 step 3). Invokes <paramref name="action"/> once for
    /// every live <see cref="RenderContext"/> tracked by this reconciler — both
    /// function-component contexts (<see cref="ComponentNode.Context"/>) and
    /// class-component contexts (<see cref="Component.Context"/>). The host calls
    /// this at the start of a hot-reload pass to migrate hook state tree-wide
    /// before any component re-renders. The root component's context is NOT in
    /// <c>_componentNodes</c>, so the host migrates it separately.
    /// </summary>
    internal void ForEachLiveContext(Action<RenderContext> action)
    {
        foreach (var node in _componentNodes.Values)
        {
            if (node.Context is { } funcCtx) action(funcCtx);
            if (node.Component?.Context is { } classCtx) action(classCtx);
        }
    }

    /// <summary>
    /// Drops the <c>ComponentRendered</c> id mapping of every component this reconciler
    /// tracks, for teardown paths that discard the tree without unmounting it
    /// (<see cref="Dispose"/>, a host replacing its content with the error panel).
    /// Cheap when tracing was never on: no id was ever issued, so nothing is touched.
    /// </summary>
    internal void ForgetComponentDiagnostics()
    {
        foreach (var (control, node) in _componentNodes)
        {
            if (node.DiagnosticId != 0)
                Diagnostics.ComponentRenderControls.Registry.Forget(node.DiagnosticId, control);
        }
    }

    internal HashSet<V1Protocol.OverlayLifecycle.ContentDialogLifetime>? ContentDialogLifetimes;
    internal bool IsDisposingContentDialogs { get; private set; }

    internal void RetireContentDialogs()
    {
        if (IsDisposingContentDialogs) return;
        IsDisposingContentDialogs = true;
        if (ContentDialogLifetimes is not { Count: > 0 } lifetimes) return;
        foreach (var lifetime in lifetimes.ToArray())
            lifetime.Retire();
        ContentDialogLifetimes = null;
    }

    public void Dispose()
    {
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? pendingPropagation;
        // Standalone disposal is its own outermost frame (issue #1291).
        using (RenderErrorDispatch.EnterPropagationScope())
            pendingPropagation = DisposeCollectingPropagation();
        pendingPropagation?.Throw();
    }

    /// <summary>
    /// Disposes and returns, rather than throws, a cleanup exception the app asked to
    /// propagate. Hosts call this inside their own propagation scope so the root's and the
    /// reconciler's cleanups share one "only the first propagation is rethrown" decision,
    /// and the host finishes its own teardown before rethrowing.
    /// </summary>
    internal global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? DisposeCollectingPropagation()
    {
        RetireContentDialogs();
        // Hosts dispose through here rather than Dispose(); a disposed reconciler's nodes are dead.
        _disposedForDiagnostics = true;
        // Issue #1291: with a RenderErrorHandler configured, every effect cleanup runs and
        // each failure is reported (Source = Cleanup). With no handler the first throwing
        // cleanup escapes as before.
        // The tree is dropped without unmounting it; its ComponentRendered ids must stop resolving.
        ForgetComponentDiagnostics();
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? pendingPropagation = null;
        // Resolved per failure (a cleanup may change the handler), not once per batch.
        Func<RenderErrorHandler?> cleanupHandler = ResolveRenderErrorHandler;
        foreach (var node in _componentNodes.Values)
        {
            var name = node.Component?.GetType().Name;
            RenderErrorDispatch.RunCleanups(node.Context, cleanupHandler, name, isHostLevel: false, _logger, ref pendingPropagation);
            RenderErrorDispatch.RunCleanups(node.Component?.Context, cleanupHandler, name, isHostLevel: false, _logger, ref pendingPropagation);
        }
        _componentNodes.Clear();
        _errorBoundaryNodes.Clear();
        foreach (var node in _navigationHostNodes.Values)
        {
            node.TransitionGeneration++;
            node.ConnectedOperation?.Dispose();
            node.ConnectedOperation = null;
            if (node.RouteChangedHandler is not null)
                node.Handle.RouteChanged -= node.RouteChangedHandler;
            node.Handle.Detach();
            if (node.CurrentChildControl is not null)
                Navigation.ConnectedNavigationOperation.UnmountPage(this, node.CurrentChildControl);
            node.Cache?.Clear();
        }
        _navigationHostNodes.Clear();
        CancelOpacityRestarts();
        _pool.Clear();
        // The tree is dropped without unmounting, so a TitleBar this (ReactorHostControl)
        // reconciler registered with its containing window would otherwise stay registered
        // there for the window's lifetime. (issue #1396)
        if (_embeddedTitleBars is { } embedded)
        {
            _embeddedTitleBars = null;
            foreach (var bar in embedded)
                WithdrawEmbeddedTitleBar(bar);
        }
        return pendingPropagation;
    }
}
