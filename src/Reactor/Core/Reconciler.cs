using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core.Diagnostics;
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

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// The reconciler diffs old and new element trees and patches the real WinUI control tree.
///
/// Split across partial classes:
///   - Reconciler.cs           — orchestration, children, unmount, helpers
///   - Reconciler.Mount.cs     — Mount() dispatch + per-control MountXxx methods
///   - Reconciler.Update.cs    — Update() dispatch + per-control UpdateXxx methods
/// </summary>
public sealed partial class Reconciler : IDisposable
{
    private readonly Dictionary<UIElement, ComponentNode> _componentNodes = new();
    private readonly Dictionary<UIElement, ErrorBoundaryNode> _errorBoundaryNodes = new();
    private readonly Dictionary<UIElement, NavigationHostNode> _navigationHostNodes = new();
    private readonly ElementPool _pool = new();
    private readonly Dictionary<Type, ITypeRegistration> _typeRegistry = new();
    // Null when no caller-supplied logger and no devtools logger is published.
    // All call sites use null-conditional access so the M.E.Logging code path
    // doesn't run (and stays JIT-cold) for default apps.
    private readonly ILogger? _logger;
    private readonly List<(ConnectedAnimation Animation, UIElement Target)> _pendingConnectedAnimationStarts = new();
    private readonly ContextScope _contextScope = new();
    private int _errorBoundaryDepth;
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

    // ── Style cache: avoids redundant XamlReader.Load() for identical theme binding sets ──
    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, Style> _styleCache = new();

    /// <summary>
    /// Builds a deterministic cache key for a style based on its target type and
    /// the set of ThemeRef bindings. Keys are sorted by property name so that
    /// dictionaries with the same entries in different enumeration order produce
    /// the same key.
    /// </summary>
    internal static string BuildCacheKey(string targetType, IReadOnlyDictionary<string, ThemeRef> bindings)
    {
        // Format: "TargetType|Prop1=Key1|Prop2=Key2" with properties sorted by Ordinal
        var sortedKeys = bindings.Keys.ToArray();
        Array.Sort(sortedKeys, StringComparer.Ordinal);
        var sb = new global::System.Text.StringBuilder(targetType);
        foreach (var key in sortedKeys)
        {
            sb.Append('|').Append(key).Append('=').Append(bindings[key].ResourceKey);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Clears the compiled style cache. Called on theme change as conservative
    /// memory cleanup — not a correctness requirement since {ThemeResource}
    /// setters are live-resolved by WinUI.
    /// </summary>
    internal static void ClearStyleCache() => _styleCache.Clear();


    /// <summary>
    /// Thread-static stagger context for enter transitions. When a parent with StaggerConfig
    /// mounts children, it pushes this context so each child's ApplyEnterTransition can
    /// compute stagger delay from its index among siblings.
    /// </summary>
    [ThreadStatic] private static StaggerScope? _staggerScope;

    private sealed class StaggerScope
    {
        public TimeSpan Delay;
        public int NextIndex;
        public StaggerScope? Previous;
    }

    private static void PushStaggerScope(TimeSpan delay)
    {
        _staggerScope = new StaggerScope { Delay = delay, NextIndex = 0, Previous = _staggerScope };
    }

    private static void PopStaggerScope()
    {
        if (_staggerScope is not null)
            _staggerScope = _staggerScope.Previous;
    }

    private static (int index, TimeSpan delay) ConsumeStaggerIndex()
    {
        if (_staggerScope is null) return (0, default);
        var idx = _staggerScope.NextIndex++;
        return (idx, _staggerScope.Delay);
    }

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

    // Hot reload signal: when set, the next top-level Reconcile() pass bypasses
    // Component memo (props/deps equality) so updated method bodies are picked up
    // even when props are unchanged. Cleared at the start of that pass.
    internal volatile bool ForceFullRenderPending;
    private bool _forceFullRenderActive;

    // True only during a force pass and only for the wrapper elements whose
    // skip would prevent ReconcileComponent from running. Used by Update()
    // and ChildReconciler to bypass their structural-equality short-circuits.
    internal bool ForceRenderThroughWrapper(Element el) =>
        _forceFullRenderActive && el is ComponentElement or MemoElement or FuncElement;

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

    // ── Reconcile-highlight capture (gated by ReactorFeatureFlags.HighlightReconcileChanges) ──
    private List<UIElement>? _highlightMounted;
    private List<UIElement>? _highlightModified;

    // ── Layout-cost component lifecycle (gated by ReactorFeatureFlags.ShowLayoutCost) ──
    // Fired from the three MountComponent/MountFunc/MountMemo paths and from
    // Unmount. Subscribers wire the attribution aggregator without the core
    // reconciler taking a dependency on the Hosting.LayoutCost layer. Only
    // raised when the flag is on so the no-op cost is a single bool check.
    /// <summary>Fired when a Component (class/func/memo) is mounted. Args: wrapper Border, display name, depth.</summary>
    internal event Action<UIElement, string, int>? LayoutCostComponentMounted;
    /// <summary>Fired when a Component is unmounted. Arg: wrapper Border.</summary>
    internal event Action<UIElement>? LayoutCostComponentUnmounted;

    private int _layoutCostComponentDepth;

    internal void RaiseLayoutCostComponentMounted(UIElement wrapper, string displayName)
    {
        if (!ReactorFeatureFlags.ShowLayoutCost) return;
        LayoutCostComponentMounted?.Invoke(wrapper, displayName, _layoutCostComponentDepth);
    }
    internal void RaiseLayoutCostComponentUnmounted(UIElement wrapper)
    {
        if (!ReactorFeatureFlags.ShowLayoutCost) return;
        LayoutCostComponentUnmounted?.Invoke(wrapper);
    }

    /// <summary>
    /// Enumerate every currently-mounted Component wrapper + its display name.
    /// Used by the layout-cost overlay to back-fill rollups when the flag is
    /// flipped on mid-session (Components mounted before the flip never fired
    /// <see cref="LayoutCostComponentMounted"/>).
    /// </summary>
    /// <remarks>
    /// Depth is not tracked per-node, so this method reports depth 0 for all
    /// entries. Depth is only used by spatial attribution's innermost-wins
    /// tiebreak; getting it wrong for back-filled components just biases that
    /// tiebreak — acceptable for a dev-time overlay.
    /// </remarks>
    internal IEnumerable<(UIElement Wrapper, string DisplayName)> EnumerateComponentWrappers()
    {
        foreach (var kv in _componentNodes)
        {
            var node = kv.Value;
            var name = node.Component?.GetType().Name
                ?? node.Element?.GetType().Name
                ?? "Component";
            yield return (kv.Key, name);
        }
    }

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
    /// <c>_typeRegistry</c>). Dispatch order is V1 → external → the eight
    /// composition primitives that sit above the protocol.
    /// </summary>
    public Reconciler(ILogger? logger = null)
    {
        _logger = logger;
        RegisterV1BuiltInHandlers();
    }

    /// <summary>
    /// Spec 047 §14 Phase 3 completion — register all built-in V1 handlers
    /// (Phase 1 hand-coded handlers + Phase 3 descriptor-driven ports) into
    /// <see cref="V1HandlerRegistry"/>. Sole registration site; built-in
    /// handler / descriptor types stay internal.
    ///
    /// <para>Carved (not registered here — kept on the legacy
    /// <see cref="Mount(Element, Action)"/> switch by design):</para>
    /// <list type="bullet">
    ///   <item><b>Composition primitives</b> — <see cref="ComponentElement"/>,
    ///   <see cref="FuncElement"/>, <see cref="MemoElement"/>,
    ///   <see cref="ErrorBoundaryElement"/>, <c>CommandHostElement</c>,
    ///   <see cref="Controls.Validation.FormFieldElement"/>,
    ///   <see cref="Controls.Validation.ValidationVisualizerElement"/>,
    ///   <see cref="Controls.Validation.ValidationRuleElement"/>. These sit
    ///   <i>above</i> the V1 handler protocol (they orchestrate child
    ///   reconciliation rather than wrap a single WinUI control), so Phase 4
    ///   cleanup keeps their legacy arms.</item>
    ///   <item><b>Interop bridges</b> — <see cref="Hosting.XamlHostElement"/>,
    ///   <see cref="Hosting.XamlPageElement"/>. V1 descriptors exist
    ///   (<c>XamlHostDescriptor</c>, <c>XamlPageDescriptor</c>) but stay
    ///   unregistered because <see cref="Hosting.XamlInterop.Register(Reconciler)"/>
    ///   populates the external <c>_typeRegistry</c> during app startup;
    ///   auto-registering V1 would clash via
    ///   <see cref="EnsureRegistrableElementType"/>. Unification is Phase 4
    ///   follow-up.</item>
    ///   <item><b>Overlays — PORTED (§14 Phase 3 prelude)</b> —
    ///   <c>ContentDialogElement</c>, <c>FlyoutElement</c>,
    ///   <c>MenuBarElement</c>, <c>CommandBarElement</c>,
    ///   <c>MenuFlyoutElement</c>, <c>PopupElement</c>,
    ///   <c>CommandBarFlyoutElement</c> now route through V1 via
    ///   decorator-style handlers in <c>V1Protocol.Handlers</c> that delegate
    ///   to the legacy <c>MountXxx</c>/<c>UpdateXxx</c> bodies and return
    ///   <see cref="V1Protocol.V1UnmountDisposition.ContinueDefaultTraversal"/>
    ///   on unmount — so the engine falls through to the same
    ///   <see cref="UnmountRecursive"/> type-based recursion that runs when
    ///   the flag is OFF, making mount/update/unmount byte-identical
    ///   V1 ON ≡ V1 OFF.</item>
    ///   <item><b>Stateful host — PORTED (§14 Phase 3 prelude)</b> —
    ///   <c>NavigationHostElement</c> now routes through V1 via
    ///   <see cref="V1Protocol.Handlers.NavigationHostHandler"/> (Path B
    ///   delegate). Per-instance route/cache/transition state is still torn
    ///   down by the flag-independent intercept in
    ///   <see cref="UnmountRecursive"/> (fires before the V1 unmount arm), so
    ///   unmount is byte-identical V1 ON ≡ V1 OFF.</item>
    ///
    ///   <item><b>TabView — PORTED (§14 Phase 4 §4.0.3)</b> —
    ///   <c>TabViewElement</c> now routes through V1 via the full
    ///   <c>TabViewDescriptor</c> + <c>TabItemsHost</c> port, which owns the
    ///   complete behavior: spec 045 §2.4 drag pipeline, §2.2 pinnable headers
    ///   (via <c>Reconciler.BuildTabHeader</c> / <c>TryUpdatePinHeaderInPlace</c>),
    ///   in-place tab content reconcile, conditional SelectedIndex, and
    ///   TabStripHeader / TabStripFooter Element slots (via
    ///   <c>.ImperativeBridged</c>). Supersedes the retired delegate
    ///   <c>TabViewHandler</c>. Unmount is byte-identical V1 ON ≡ V1 OFF
    ///   (a <c>WinUI.TabView</c> is an <c>ItemsControl</c> that pools without
    ///   child recursion in both paths).</item>
    ///   <item><b>Items host — PORTED (§14 Phase 3 prelude)</b> —
    ///   <c>GridViewElement</c> now routes through V1 via the hand-coded
    ///   <see cref="V1Protocol.Handlers.GridViewHandler"/> (Path B delegate),
    ///   which mirrors <see cref="V1Protocol.Handlers.ListViewHandler"/>'s
    ///   lazy <c>ItemsSource + ContainerContentChanging</c> realization. The
    ///   <c>GridViewDescriptor</c>'s <c>ItemsHost&lt;&gt;</c> strategy stays
    ///   unregistered — it pre-mounts every item with no virtualization,
    ///   which would regress the recycle contract.</item>
    /// </list>
    /// </summary>
    private void RegisterV1BuiltInHandlers()
    {
        // ── Phase 1 hand-coded handlers (battle-tested, hot-path tuned) ──
        RegisterHandler<ToggleSwitchElement, WinUI.ToggleSwitch>(new V1Protocol.Handlers.ToggleSwitchHandler());
        RegisterHandler<SliderElement, WinUI.Slider>(new V1Protocol.Handlers.SliderHandler());
        RegisterHandler<TextBoxElement, WinUI.TextBox>(new V1Protocol.Handlers.TextBoxHandler());
        RegisterHandler<BorderElement, WinUI.Border>(new V1Protocol.Handlers.BorderHandler());
        RegisterHandler<ListViewElement, WinUI.ListView>(new V1Protocol.Handlers.ListViewHandler());

        // ── §14 Phase 3 prelude carve-closures (delegate to engine bodies) ──
        RegisterHandler<NavigationHostElement, WinUI.Grid>(new V1Protocol.Handlers.NavigationHostHandler());
        RegisterHandler<GridViewElement, WinUI.GridView>(new V1Protocol.Handlers.GridViewHandler());

        // Overlays — decorator-style ports. Each delegates to the legacy
        // MountXxx/UpdateXxx body and returns ContinueDefaultTraversal on
        // unmount, so the V1 ON path is byte-identical to V1 OFF (which skips
        // the V1 arm and runs the same UnmountRecursive type-based recursion).
        RegisterDecoratorHandler<ContentDialogElement>(new V1Protocol.Handlers.ContentDialogHandler());
        RegisterDecoratorHandler<FlyoutElement>(new V1Protocol.Handlers.FlyoutHandler());
        RegisterDecoratorHandler<MenuBarElement>(new V1Protocol.Handlers.MenuBarHandler());
        RegisterDecoratorHandler<CommandBarElement>(new V1Protocol.Handlers.CommandBarHandler());
        RegisterDecoratorHandler<MenuFlyoutElement>(new V1Protocol.Handlers.MenuFlyoutHandler());
        RegisterDecoratorHandler<PopupElement>(new V1Protocol.Handlers.PopupHandler());
        RegisterDecoratorHandler<CommandBarFlyoutElement>(new V1Protocol.Handlers.CommandBarFlyoutHandler());

        // Button — delegate to the COMPLETE legacy MountButton/UpdateButton
        // bodies (the ButtonDescriptor only handles the string-Label fast path
        // and drops ContentElement; the delegate runs the full legacy impl so
        // element content round-trips). Decorator shape so unmount falls
        // through to ContentControl recursion (cleanup parity for an element
        // child) — see ButtonHandler. Supersedes the registered descriptor.
        RegisterDecoratorHandler<ButtonElement>(new V1Protocol.Handlers.ButtonHandler());

        // TabView — §4.0.3 full descriptor port. TabViewDescriptor now owns
        // the complete behavior (drag pipeline, pinnable headers, strip
        // header/footer slots, in-place content reconcile, conditional
        // SelectedIndex), so it supersedes the delegate TabViewHandler.
        // Registered below with the standard concrete descriptors.

        // ── §14 Phase 3 base-derived (templated/lazy/items hosts) ────────
        // Each closed-T leaf routes through the same descriptor via the
        // base-derived fallback walk (matches the legacy switch's erasure
        // pattern). Parallel implementations: TemplatedListView/GridView
        // overlap with the Phase 1 ListViewHandler — exact-type wins, so
        // ListView itself uses ListViewHandler; only typed templated
        // variants route through these base-derived registrations.
        // §14: typed templated lists route through one Path B delegate on the
        // common base (legacy move/reorder animations) — see TemplatedListHandler.
        RegisterDecoratorHandlerForDerivedTypes<TemplatedListElementBase>(new V1Protocol.Handlers.TemplatedListHandler());
        RegisterDecoratorHandlerForDerivedTypes<LazyStackElementBase>(new V1Protocol.Handlers.LazyStackHandler()); // §14: ScrollViewer-wrapped — see LazyStackHandler
        RegisterDescriptorForDerivedTypes(V1Protocol.Descriptor.Descriptors.ItemsRepeaterDescriptor.Descriptor);
        RegisterDescriptorForDerivedTypes(V1Protocol.Descriptor.Descriptors.ItemsViewDescriptor.Descriptor);

        // ── §14 Phase 3 standard concrete descriptors (alphabetical) ────
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.AnimatedIconDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.AnimatedVisualPlayerDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.AnnotatedScrollBarDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.AnnounceRegionDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.AutoSuggestBoxDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.BreadcrumbBarDescriptor.Descriptor);
        // ButtonDescriptor is intentionally NOT registered — superseded by the
        // delegate ButtonHandler above (ContentElement coverage + unmount
        // parity). The descriptor type is retained for its isolated selftests
        // and the perf-bench descriptor variant.
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.CalendarDatePickerDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.CalendarViewDescriptor.Descriptor);
        RegisterDecoratorHandler<CanvasElement>(new V1Protocol.Handlers.CanvasPanelHandler()); // §14: keyed reconcile — see PanelDelegateHandlers
        RegisterDecoratorHandler<CheckBoxElement>(new V1Protocol.Handlers.CheckBoxHandler()); // §14: value-control echo-suppression — see CheckBoxHandler
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.ColorPickerDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.ComboBoxDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.DatePickerDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.DropDownButtonDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.EllipseDescriptor.Descriptor);
        RegisterDecoratorHandler<ExpanderElement>(new V1Protocol.Handlers.ExpanderHandler()); // §14: callback/template wiring — see ExpanderHandler
        RegisterDecoratorHandler<FlexElement>(new V1Protocol.Handlers.FlexPanelHandler()); // §14: keyed reconcile — see PanelDelegateHandlers
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.FlipViewDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.FrameDescriptor.Descriptor);
        RegisterDecoratorHandler<GridElement>(new V1Protocol.Handlers.GridPanelHandler()); // §14: keyed reconcile — see PanelDelegateHandlers
        // Spec 047 §14 Phase 3 prelude — GridViewElement now routes through V1
        // via the hand-coded V1Protocol.Handlers.GridViewHandler (registered
        // above with the Phase 1 handlers), which preserves the legacy
        // ItemsSource + ContainerContentChanging lazy realization. The
        // GridViewDescriptor stays carved: its ItemsHost<> strategy pre-mounts
        // every item into GridView.Items (one container per item, no
        // virtualization, no recycle), which would silently regress production
        // memory and lifecycle (item Mount/Unmount fires for every item
        // up-front instead of per-viewport, breaking the recycle contract).
        // RegisterDescriptor(V1Protocol.Descriptor.Descriptors.GridViewDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.HyperlinkButtonDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.ImageDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.InfoBadgeDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.InfoBarDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.ItemContainerDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.LineDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.ListBoxDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.MapControlDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.MediaPlayerElementDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.NavigationViewDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.NumberBoxDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.ParallaxViewDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.PasswordBoxDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.PathDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.PersonPictureDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.PipsPagerDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.PivotDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.ProgressBarDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.ProgressRingDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.RadioButtonDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.RadioButtonsDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.RatingControlDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.RectangleDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.RefreshContainerDescriptor.Descriptor);
        RegisterDecoratorHandler<RelativePanelElement>(new V1Protocol.Handlers.RelativePanelHandler()); // §14: keyed reconcile — see PanelDelegateHandlers
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.RepeatButtonDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.RichEditBoxDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.RichTextBlockDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.ScrollViewDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.ScrollViewerDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.SelectorBarDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.SemanticDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.SemanticZoomDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.SplitButtonDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.SplitViewDescriptor.Descriptor);
        RegisterDecoratorHandler<StackElement>(new V1Protocol.Handlers.StackPanelHandler()); // §14: keyed reconcile — see PanelDelegateHandlers
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.SwipeControlDescriptor.Descriptor);
        // Spec 047 §14 Phase 4 (§4.0.3) — TabViewElement routes through V1 via
        // the full TabViewDescriptor port (drag pipeline, pinnable headers,
        // in-place content reconcile, conditional SelectedIndex, TabStripHeader
        // / TabStripFooter Element slots). Supersedes the delegate
        // TabViewHandler. Unmount is byte-identical V1 ON ≡ V1 OFF (a
        // WinUI.TabView is an ItemsControl that pools without child recursion in
        // both paths).
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.TabViewDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.TeachingTipDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.TextBlockDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.TimePickerDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.TitleBarDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.ToggleButtonDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.ToggleSplitButtonDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.TreeViewDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.ViewboxDescriptor.Descriptor);
        RegisterDescriptor(V1Protocol.Descriptor.Descriptors.WebView2Descriptor.Descriptor);
        RegisterDecoratorHandler<WrapGridElement>(new V1Protocol.Handlers.WrapGridHandler()); // §14: keyed reconcile — see PanelDelegateHandlers

        // ── §14 Phase 3 completion decorator-style handlers ──────────────
        RegisterDecoratorHandler<IconElement>(V1Protocol.Descriptor.Descriptors.IconDescriptor.Handler);

        // ── §4.0.5 — XAML interop bridges own their V1 registration ──────
        // Spec 047 §14 Phase 4 (4.0.5): the two reverse-embedding element
        // types are now owned by V1 auto-registration. Hosting.XamlInterop.Register
        // (still a public API for source compat) skips populating _typeRegistry
        // for any type already registered here, so it no longer clashes with
        // EnsureRegistrableElementType. The decorator handlers reimplement the
        // legacy MountXamlHost/MountXamlPage bodies and the UnmountRecursive
        // intercepts (frame.Content=null + DetachReactorState), so the V1 ON
        // path is behavior-identical to V1 OFF.
        RegisterDecoratorHandler<XamlPageElement>(V1Protocol.Descriptor.Descriptors.XamlPageDescriptor.Handler);
        RegisterDecoratorHandler<XamlHostElement>(V1Protocol.Descriptor.Descriptors.XamlHostDescriptor.Handler);
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

    /// <summary>
    /// Spec 047 §14 Phase 3 completion — sugar wrapper around
    /// <see cref="RegisterHandler{TElement,TControl}"/> for built-in
    /// descriptor-driven ports. Keeps <see cref="RegisterV1BuiltInHandlers"/>
    /// readable as a flat list of descriptors.
    /// </summary>
    private void RegisterDescriptor<TElement, TControl>(
        V1Protocol.Descriptor.ControlDescriptor<TElement, TControl> descriptor)
        where TElement : Element
        where TControl : FrameworkElement, new()
        => RegisterHandler<TElement, TControl>(
            new V1Protocol.Descriptor.DescriptorHandler<TElement, TControl>(descriptor));

    /// <summary>
    /// Spec 047 §14 Phase 3 completion — sugar wrapper around
    /// <see cref="RegisterHandlerForDerivedTypes{TBase,TControl}"/> for
    /// base-derived descriptor ports (typed templated lists, lazy stacks,
    /// items hosts).
    /// </summary>
    private void RegisterDescriptorForDerivedTypes<TBase, TControl>(
        V1Protocol.Descriptor.ControlDescriptor<TBase, TControl> descriptor)
        where TBase : Element
        where TControl : FrameworkElement, new()
        => RegisterHandlerForDerivedTypes<TBase, TControl>(
            new V1Protocol.Descriptor.DescriptorHandler<TBase, TControl>(descriptor));

    // ── V1 handler registry (spec 047 §14 Phase 1, Q1.1) ──────────────
    // Keyed by exact element type, separate from _typeRegistry so that
    // external RegisterType callers and built-in V1 ports stay isolated.
    // Add is internal-only for Phase 1; the public author-facing surface
    // (`RegisterHandler<TElement,TControl>(IElementHandler<…>)`) lands in
    // sections 1.6 / 1.9.
    internal readonly V1HandlerRegistry _v1Handlers = new();

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

    // ════════════════════════════════════════════════════════════════════
    //  ReactorAttached.StateProperty  (ReactorState)
    // ════════════════════════════════════════════════════════════════════
    //
    // A single attached DP carrying a ReactorState wrapper that bundles
    //   (a) the current Reactor Element for this native element, and
    //   (b) the per-element ModifierEventHandlerState (current user handlers +
    //       stable trampoline delegates).
    //
    // The DP lives on the native DependencyObject, so every C# RCW pointing
    // at the same native element observes the same ReactorState — fixing the
    // duplicate-RCW event-subscription bug where ConditionalWeakTable keyed
    // per-RCW would return a different ModifierEventHandlerState for each wrapper
    // and attach duplicate trampolines on the native event source.
    //
    // Lifecycle:
    //   - Pool return / ListView recycle: ClearElementTag nulls state.Element
    //     only, preserving state.Events. Attached trampolines survive; next
    //     rent/realize flows through Ensure*Subscribed which refreshes
    //     state.Current* rather than attaching duplicates. Re-attaching on
    //     every rent would re-introduce the bug this module exists to fix.
    //   - Permanent unmount (XamlHost/XamlPage — not pooled, may stay rooted
    //     by app code): DetachReactorState nulls Element, clears Current*,
    //     drops state.Events. Any orphan trampoline still on the native event
    //     source becomes a no-op if it fires.
    internal sealed class ReactorState
    {
        public Element? Element;
        public ModifierEventHandlerState? Modifiers;
        // Per-native-element echo-suppress counter consumed by ChangeEchoSuppressor.
        // Lives here (not in a CWT-by-RCW) so that BeginSuppress on one managed
        // wrapper and ShouldSuppress on a different wrapper for the same native
        // DependencyObject see the same counter. See ChangeEchoSuppressor.cs.
        public int EchoSuppressCount;
        // §8.2 — depth of the active setter-suppression scope. While > 0, any
        // change event on this control is dropped without consuming a token
        // from EchoSuppressCount. See ApplySetters / ChangeEchoSuppressor.
        public int EchoSuppressScopeDepth;
        // Spec 047 §8 value-diff echo suppression. A programmatic controlled
        // write on a migrated single-value, exact-comparable, synchronous
        // round-trip (e.g. ComboBox.SelectedIndex, ToggleSwitch.IsOn) arms this
        // one-shot predicate with the value it just wrote; the change-event
        // trampoline drops the single matching echo (readback satisfies the
        // predicate) via ChangeEchoSuppressor.ShouldSuppressEcho, then clears it.
        // Replaces the causal counter on those paths. The counter
        // (EchoSuppressCount / EchoSuppressScopeDepth) is RETAINED as the
        // fallback for paths value-diff cannot model: coercion (Slider/NumberBox
        // Min/Max), collection batch (CalendarView), the setter scope, the public
        // ReactorBinding.WriteSuppressed API, and double-valued controls.
        //
        // Single pending slot — correct only because migrated controls have
        // exactly one controlled round-trip property whose event fires
        // synchronously inside the write. Reset everywhere the counter is reset
        // (pool return / ClearCurrentEventHandlers / DetachReactorState) so a
        // stale arm can't suppress the first real event of a later lifecycle.
        public Func<object?, bool>? PendingEchoMatch;
        // Spec 042 Phase 1 — keyed-list reconciliation state. Set when the
        // host element is a templated items control (ListView/GridView/
        // ItemsRepeater). The internal ObservableCollection<ReactorRow> in
        // ListState is what WinUI binds to so incremental insert/remove/move
        // ops translate into container-level animations rather than full
        // re-realization.
        public Internal.ReactorListState? ListState;

        // Spec 047 §14 Phase 3 close-out — descriptor-side per-index view
        // source. Populated by Reconciler.BindKeyedItemsSource when a
        // TemplatedItems<TItem,TElement,TControl> strategy is in effect.
        // HandleTemplatedContainerContentChanging and RefreshRealizedContainers
        // prefer this over the legacy TemplatedListElementBase fallback so
        // descriptor-driven controls never need to inherit from the legacy
        // abstract base.
        public Internal.IItemViewSource? ItemViewSource;

        // Spec 047 §9.2 / §14 Phase 1 (1.7) — per-control event payload box.
        // Holds a strongly-typed payload struct (e.g. ToggleSwitchEventPayload)
        // discriminated by HandlerType. Populated for ported V1 controls and
        // for the control-intrinsic events (Button.Click, NumberBox immediate
        // inner-TextBox) that migrated off the routed ModifierEventHandlerState.
        // PRESERVED across pool rent/return per the Q18 contract (issue #114) —
        // trampolines stay subscribed for the control's lifetime; a stale type
        // is never observable post-rent because GetOrCreateControlEventPayload
        // re-creates the box on a HandlerType mismatch.
        public object? ControlEventState;
    }

    internal static class ReactorAttached
    {
        public static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached(
                "ReactorState",
                typeof(ReactorState),
                typeof(ReactorAttached),
                new PropertyMetadata(null));
    }

    internal static ReactorState GetOrCreateReactorState(FrameworkElement fe)
    {
        if (fe.GetValue(ReactorAttached.StateProperty) is ReactorState state)
            return state;
        state = new ReactorState();
        fe.SetValue(ReactorAttached.StateProperty, state);
        return state;
    }

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.3) — promoted from internal. Associates a
    /// control with its current Reactor element via the
    /// <see cref="ReactorAttached.StateProperty"/> attached DP so that
    /// trampolines can resolve the live element on dispatch.
    /// Provisional API; see <c>REACTOR_V1_PREVIEW</c>.
    /// </summary>
    public static void SetElementTag(FrameworkElement control, Element? element)
    {
        if (control.GetValue(ReactorAttached.StateProperty) is ReactorState state)
        {
            state.Element = element;
            return;
        }
        if (element is null) return; // nothing to store
        state = new ReactorState { Element = element };
        control.SetValue(ReactorAttached.StateProperty, state);
    }

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.3) — promoted from internal. Retrieves the
    /// element associated with a control, or null.
    /// Provisional API; see <c>REACTOR_V1_PREVIEW</c>.
    /// </summary>
    public static Element? GetElementTag(UIElement control) =>
        control is FrameworkElement fe
            ? (fe.GetValue(ReactorAttached.StateProperty) as ReactorState)?.Element
            : null;

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.3) — promoted from internal.
    /// Provisional API; see <c>REACTOR_V1_PREVIEW</c>.
    /// </summary>
    public static Element? GetElementTag(FrameworkElement fe) =>
        (fe.GetValue(ReactorAttached.StateProperty) as ReactorState)?.Element;

    // ────────────────────────────────────────────────────────────────────
    // Spec 042 Phase 1 — keyed-list reconciliation state accessors.
    // Stored on the same attached ReactorState as Element/Events so we do
    // not pay for a second DependencyProperty on every realized container.
    // Only mounted templated items controls populate this.
    // ────────────────────────────────────────────────────────────────────

    internal static Internal.ReactorListState? GetListState(DependencyObject control)
    {
        if (control is FrameworkElement fe
            && fe.GetValue(ReactorAttached.StateProperty) is ReactorState state)
            return state.ListState;
        return null;
    }

    internal static void SetListState(FrameworkElement control, Internal.ReactorListState listState)
    {
        if (control.GetValue(ReactorAttached.StateProperty) is ReactorState state)
        {
            state.ListState = listState;
            return;
        }
        state = new ReactorState { ListState = listState };
        control.SetValue(ReactorAttached.StateProperty, state);
    }

    // ── §14 Phase 3 close-out: per-control descriptor view source ──────

    internal static Internal.IItemViewSource? GetItemViewSource(DependencyObject control)
    {
        if (control is FrameworkElement fe
            && fe.GetValue(ReactorAttached.StateProperty) is ReactorState state)
            return state.ItemViewSource;
        return null;
    }

    internal static void SetItemViewSource(FrameworkElement control, Internal.IItemViewSource viewSource)
    {
        if (control.GetValue(ReactorAttached.StateProperty) is ReactorState state)
        {
            state.ItemViewSource = viewSource;
            return;
        }
        state = new ReactorState { ItemViewSource = viewSource };
        control.SetValue(ReactorAttached.StateProperty, state);
    }

    /// <summary>
    /// Clears the Element pointer while preserving ModifierEventHandlerState. Call on
    /// pool return — attached trampolines stay valid so that rent/re-mount
    /// reuses the same subscriptions (re-attaching would re-introduce the
    /// duplicate-subscription bug).
    /// </summary>
    internal static void ClearElementTag(FrameworkElement fe)
    {
        if (fe.GetValue(ReactorAttached.StateProperty) is ReactorState state)
            state.Element = null;
    }

    /// <summary>
    /// Clears the Current* user-handler delegates from the per-element
    /// ModifierEventHandlerState while leaving the trampoline subscription on the
    /// underlying control. Call from <see cref="ElementPool.CleanElement"/>
    /// so the next rent doesn't fire the previous component's captured
    /// rerender closure. TASK-060.
    /// Also resets the echo-suppress counter so a stranded BeginSuppress
    /// (e.g. a write whose change-event got swallowed) can't suppress a
    /// real user event after the element is rented to a new owner.
    /// </summary>
    internal static void ClearCurrentEventHandlers(FrameworkElement fe)
    {
        if (fe.GetValue(ReactorAttached.StateProperty) is ReactorState state)
        {
            state.Modifiers?.ClearCurrentHandlers();
            state.EchoSuppressCount = 0;
            state.EchoSuppressScopeDepth = 0;
            state.PendingEchoMatch = null;
        }
    }

    /// <summary>
    /// Fully detaches reactor state from a control: nulls the Element and
    /// clears the ModifierEventHandlerState's Current* delegates so any already-
    /// attached trampoline on the native event source becomes a no-op if it
    /// fires. Use when the control leaves reactor's ownership for good
    /// (XamlHost / XamlPage unmount) but may remain alive because the app
    /// holds a reference — without this, captured closures stay reachable
    /// and stale reactor callbacks can still fire.
    /// </summary>
    /// <summary>
    /// Spec 047 §14 Phase 1 (1.3) — promoted from internal.
    /// Provisional API; see <c>REACTOR_V1_PREVIEW</c>.
    /// </summary>
    public static void DetachReactorState(FrameworkElement fe)
    {
        if (fe.GetValue(ReactorAttached.StateProperty) is not ReactorState state)
            return;
        state.Element = null;
        state.Modifiers?.ClearCurrentHandlers();
        state.Modifiers = null;
        state.EchoSuppressCount = 0;
        state.EchoSuppressScopeDepth = 0;
        state.PendingEchoMatch = null;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Lazy event wiring for poolable types
    // ════════════════════════════════════════════════════════════════════
    //
    // Non-poolable controls are created fresh on every Mount, so lazy wiring
    // works off the (oldEl, newEl) diff alone — wire at Mount when the handler
    // is non-null, wire at Update when it transitions null → non-null.
    //
    // Poolable controls (Button, TextBox, ToggleSwitch) are different: the pool
    // intentionally retains WinRT event subscriptions across rent/return (see
    // ElementPool.CleanElement), but clears Tag. So "Tag is null" on rent does
    // NOT mean "unwired" — the control may already have a trampoline attached.
    // We need a per-control dedupe key that survives pool cycles to avoid
    // double-subscription.
    //
    // All poolable wiring dedupes through ModifierEventHandlerState (attached on
    // ReactorAttached.StateProperty, which is a DependencyProperty keyed by
    // native DO identity). Two managed RCWs over the same native DependencyObject
    // resolve to the same ModifierEventHandlerState, so the *Trampoline-is-not-null
    // check correctly short-circuits. This was previously split between an
    // unsafe managed-FE-keyed CWT (Button, TextBox, Image, ScrollViewer) and
    // ModifierEventHandlerState (ToggleSwitch); issue #114 unified everything onto
    // ModifierEventHandlerState.

    // ════════════════════════════════════════════════════════════════════
    //  Canvas anchor positioning
    // ════════════════════════════════════════════════════════════════════
    //  CanvasAttached.AnchorX/AnchorY express positioning as a fraction of
    //  the element's rendered size (0,0 = top-left, 0.5,0.5 = centered).
    //  Final position depends on ActualWidth/ActualHeight, which is unknown
    //  at mount time, so we recompute on Loaded + SizeChanged. The current
    //  CanvasAttached is held in per-FE state so updates can swap anchor
    //  values without re-subscribing.

    private sealed class CanvasAnchorState
    {
        public CanvasAttached Current = new();
        public bool Subscribed;
    }

    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, CanvasAnchorState> _canvasAnchorStates = new();

    internal static void ApplyCanvasPosition(FrameworkElement fe, CanvasAttached ca)
    {
        if (ca.AnchorX == 0 && ca.AnchorY == 0)
        {
            WinUI.Canvas.SetLeft(fe, ca.Left);
            WinUI.Canvas.SetTop(fe, ca.Top);
            // If anchor handlers were previously installed (e.g. element re-used after
            // a different anchor), keep them but update Current so they become a no-op.
            if (_canvasAnchorStates.TryGetValue(fe, out var existing))
                existing.Current = ca;
            return;
        }

        var state = _canvasAnchorStates.GetValue(fe, static _ => new CanvasAnchorState());
        state.Current = ca;
        RecomputeCanvasAnchor(fe, state.Current);

        if (state.Subscribed) return;
        state.Subscribed = true;

        fe.SizeChanged += (_, _) =>
        {
            if (_canvasAnchorStates.TryGetValue(fe, out var s))
                RecomputeCanvasAnchor(fe, s.Current);
        };
        fe.Loaded += (_, _) =>
        {
            if (_canvasAnchorStates.TryGetValue(fe, out var s))
                RecomputeCanvasAnchor(fe, s.Current);
        };
    }

    private static void RecomputeCanvasAnchor(FrameworkElement fe, CanvasAttached ca)
    {
        WinUI.Canvas.SetLeft(fe, ca.Left - ca.AnchorX * fe.ActualWidth);
        WinUI.Canvas.SetTop(fe, ca.Top - ca.AnchorY * fe.ActualHeight);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Extensible type registry (Feature 1: RegisterType API)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Registers a custom element type so the reconciler knows how to mount, update, and unmount it.
    /// Registered types take priority over built-in types.
    ///
    /// Spec 047 §13 Q17 / §14 Phase 1 (1.9) — v1 semantics:
    ///   - Exact-type lookup via <c>element.GetType()</c>; no base-class fallback.
    ///   - Throws <see cref="InvalidOperationException"/> on duplicate registration,
    ///     including duplicates against a previously-registered V1 handler
    ///     (<see cref="V1HandlerRegistry"/>).
    ///   - Throws on open-generic element types.
    ///
    /// The mount and update handlers receive the Reconciler instance so they can
    /// recursively mount/update/unmount child elements without capturing external state.
    ///
    /// Not part of the <c>REACTOR_V1_PREVIEW</c> surface — this is the legacy
    /// type-registry path, public since before Spec 047. The §13 Q17 hardening
    /// (throw on duplicate, no base-class fallback, no open generics) tightens
    /// previously-undefined behavior; it does not change the shape of this API.
    /// </summary>
    public void RegisterType<TElement, TControl>(
        Func<Reconciler, TElement, Action, TControl> mount,
        Func<Reconciler, TElement, TElement, TControl, Action, UIElement?> update,
        Action<Reconciler, TControl>? unmount = null)
        where TElement : Element
        where TControl : UIElement
    {
        EnsureRegistrableElementType(typeof(TElement), typeof(TControl), "RegisterType");
        _typeRegistry.Add(typeof(TElement), new TypeRegistration<TElement, TControl>(mount, update, unmount));
    }

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.9) — shared duplicate / open-generic guard for
    /// both <see cref="RegisterType{TElement,TControl}"/> and
    /// <see cref="RegisterHandler{TElement,TControl}"/>. Throws on any
    /// cross-registry collision; message names both the existing and the new
    /// TControl types so the author sees what they're shadowing.
    /// </summary>
    internal void EnsureRegistrableElementType(Type elementType, Type newControlType, string callerName)
    {
        if (elementType.ContainsGenericParameters)
        {
            throw new InvalidOperationException(
                $"{callerName}: open-generic element types (e.g. typeof(DataGrid<>)) are not " +
                $"supported in v1 (spec 047 §13 Q17). Element type: '{elementType.FullName}'.");
        }
        if (_typeRegistry.TryGetValue(elementType, out var existing))
        {
            var existingControl = existing.GetType().GetGenericArguments() is { Length: 2 } args
                ? args[1].FullName
                : "<unknown>";
            throw new InvalidOperationException(
                $"Cannot register handler for '{elementType.FullName}' (-> '{newControlType.FullName}'): " +
                $"a handler is already registered (-> '{existingControl}'). " +
                "Duplicate registrations are forbidden in v1 (spec 047 §13 Q17); " +
                "build a separate Reconciler instance if you need a different mapping.");
        }
        if (_v1Handlers.ContainsKey(elementType))
        {
            throw new InvalidOperationException(
                $"Cannot register handler for '{elementType.FullName}' (-> '{newControlType.FullName}'): " +
                "a V1 element handler is already registered for this element type. " +
                "Duplicate registrations are forbidden in v1 (spec 047 §13 Q17); " +
                "build a separate Reconciler instance if you need a different mapping.");
        }
    }

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.6 / 1.9) — register a v1
    /// <see cref="V1Protocol.IElementHandler{TElement,TControl}"/> for the
    /// given element type. Dispatched ahead of <see cref="RegisterType{T,U}"/>
    /// callbacks. Throws on duplicate
    /// (including across registries) and on open-generic element types
    /// (spec §13 Q17).
    /// </summary>
    public void RegisterHandler<TElement, TControl>(V1Protocol.IElementHandler<TElement, TControl> handler)
        where TElement : Element
        where TControl : UIElement
    {
        ArgumentNullException.ThrowIfNull(handler);
        EnsureRegistrableElementType(typeof(TElement), typeof(TControl), "RegisterHandler");
        _v1Handlers.Add(typeof(TElement), new V1Protocol.V1HandlerAdapter<TElement, TControl>(handler));
    }

    /// <summary>
    /// §14 Phase 3 close-out — register a v1 handler that catches every
    /// closed runtime type whose chain reaches <typeparamref name="TBase"/>.
    /// Used by the typed templated-list descriptor ports
    /// (<c>TemplatedListViewElement&lt;T&gt;</c> family) so a single
    /// registration on a non-generic intermediate base routes every closed-T
    /// variant — same T-erasure pattern the legacy
    /// <see cref="Reconciler.Mount"/> switch uses.
    ///
    /// <para>Exact-type registrations via
    /// <see cref="RegisterHandler{TElement,TControl}"/> always win over a
    /// derived-type registration. Throws on duplicate base-type
    /// registration.</para>
    /// </summary>
    public void RegisterHandlerForDerivedTypes<TBase, TControl>(V1Protocol.IElementHandler<TBase, TControl> handler)
        where TBase : Element
        where TControl : UIElement
    {
        ArgumentNullException.ThrowIfNull(handler);
        // Open-generic base types (e.g. typeof(Foo<>)) and the legacy
        // duplicate-registration policy are both validated in
        // EnsureRegistrableElementType — reuse it.
        EnsureRegistrableElementType(typeof(TBase), typeof(TControl), "RegisterHandlerForDerivedTypes");
        _v1Handlers.AddForDerivedTypes(typeof(TBase), new V1Protocol.V1HandlerAdapter<TBase, TControl>(handler));
    }

    /// <summary>
    /// Spec 047 §14 Phase 3 completion — register a decorator-style V1
    /// handler for elements whose returned <see cref="UIElement"/>
    /// identity may change on update or whose unmount disposition
    /// diverges from the standard pool-return. See
    /// <see cref="V1Protocol.IDecoratorElementHandler{TElement}"/> for
    /// the use cases (target-wrapping flyouts, modal lifecycle wrappers,
    /// polymorphic mounts, interop bridges).
    ///
    /// <para>This registration sits on the same dispatch table as
    /// <see cref="RegisterHandler{TElement,TControl}"/> — collisions
    /// throw via <see cref="EnsureRegistrableElementType"/>.</para>
    /// </summary>
    internal void RegisterDecoratorHandler<TElement>(V1Protocol.IDecoratorElementHandler<TElement> handler)
        where TElement : Element
    {
        ArgumentNullException.ThrowIfNull(handler);
        EnsureRegistrableElementType(typeof(TElement), typeof(UIElement), "RegisterDecoratorHandler");
        _v1Handlers.Add(typeof(TElement), new V1Protocol.V1DecoratorHandlerAdapter<TElement>(handler));
    }

    /// <summary>
    /// Spec 047 §14 Phase 3 prelude — register a decorator-style V1 handler
    /// that catches every closed runtime type whose chain reaches
    /// <typeparamref name="TBase"/> (the derived-type analogue of
    /// <see cref="RegisterDecoratorHandler{TElement}"/>). Used by the
    /// Lazy*Stack family, whose closed-T variants all derive from the
    /// non-generic <c>LazyStackElementBase</c> and need the decorator
    /// shape (control identity differs from the descriptor port — the
    /// legacy mount wraps the ItemsRepeater in a ScrollViewer — and
    /// <see cref="V1Protocol.V1UnmountDisposition.ContinueDefaultTraversal"/> so the
    /// engine recurses ScrollViewer → ItemsRepeater → realized rows on
    /// unmount).
    /// </summary>
    internal void RegisterDecoratorHandlerForDerivedTypes<TBase>(V1Protocol.IDecoratorElementHandler<TBase> handler)
        where TBase : Element
    {
        ArgumentNullException.ThrowIfNull(handler);
        EnsureRegistrableElementType(typeof(TBase), typeof(UIElement), "RegisterDecoratorHandlerForDerivedTypes");
        _v1Handlers.AddForDerivedTypes(typeof(TBase), new V1Protocol.V1DecoratorHandlerAdapter<TBase>(handler));
    }

    // ════════════════════════════════════════════════════════════════════
    //  Pool rent / return — public author-facing surface (spec 047 §13 Q18)
    // ════════════════════════════════════════════════════════════════════
    //
    // Pool key is typeof(TControl) only (Q18 — finer keys deferred to Phase 3+).
    // Mount-time allocations route here so external authors get the same
    // pool behavior as built-in ports.
    //
    // TODO(Phase 4): the dirty-rent diagnostic ("structured log entry on
    // dirty rent") is unimplementable without a way to inspect the control's
    // state; the engine trusts ElementPool.CleanElement to scrub on return.
    // When the per-control reset contract becomes declarative (descriptor-
    // driven, Phase 4), the engine will know what slots to inspect.

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.5) — rent a control instance, either from
    /// the per-type pool (if poolable and a pooled instance is available)
    /// or via <paramref name="factory"/> / <c>new T()</c>. Pool key is
    /// <c>typeof(T)</c> only.
    /// </summary>
    public T RentControl<T>(PoolPolicy<T>? policy = null, Func<T>? factory = null) where T : class, new()
    {
        // Default = poolable; only skip the pool when the policy explicitly opts out.
        bool poolable = policy?.IsPoolable != false;
        if (poolable && typeof(FrameworkElement).IsAssignableFrom(typeof(T)))
        {
            var rented = _pool.TryRent(typeof(T));
            if (rented is T t) return t;
        }
        return factory is not null ? factory() : new T();
    }

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.5) — return a control to its per-type pool,
    /// after running the Q18 reset contract:
    /// <list type="bullet">
    ///   <item>Clear <c>ControlEventState</c> (per-control event payload box, §9.2).</item>
    ///   <item>Clear <c>ModifierEventHandlerState</c> (modifier / routed-input handler state).</item>
    ///   <item>Clear <c>ReactorAttached.StateProperty</c> Tag / DataContext set by Reactor.</item>
    ///   <item>Invoke <c>policy.Reset</c> last (author-defined extra cleanup).</item>
    /// </list>
    /// Double-return is safe: <see cref="ElementPool.Return"/> dedupes via
    /// the per-type stack cap (<c>MaxPerType</c>).
    /// </summary>
    public void ReturnControl<T>(T control, PoolPolicy<T>? policy = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(control);
        if (policy?.IsPoolable == false) return;

        // Engine reset contract (Q18). Order is intentional — clear the
        // attached-state box BEFORE the pool's CleanElement runs so any
        // ControlEventState payload is gone before the next rent.
        if (control is FrameworkElement fe)
        {
            if (fe.GetValue(ReactorAttached.StateProperty) is ReactorState rs)
            {
                rs.Modifiers?.ClearCurrentHandlers();
                // Spec 047 §9.2 / Phase 1 KD-3 — typed per-control event
                // payloads (ToggleSwitch / Slider / TextBox / Button / ...)
                // are intentionally preserved across pool rent/return cycles.
                // Their trampolines stay subscribed to native WinUI events for
                // the control's lifetime and read live state via GetElementTag
                // — clearing the box would force re-allocation on every rent
                // and double-subscribe to the native event. Mirrors the legacy
                // ModifierEventHandlerState contract (ClearCurrentHandlers nulls user
                // delegates only; trampoline slots stay). The generic-anchor
                // CustomEventAnchorPayload from V1 OnCustomEvent has the same
                // shape, so it stays too — handlers that use the generic
                // surface must be idempotent on rent (Phase 1 known caveat
                // for OnCustomEvent; see Phase 1 task file KD-4).
                rs.EchoSuppressCount = 0;
                rs.EchoSuppressScopeDepth = 0;
                rs.PendingEchoMatch = null;
                rs.Element = null;
            }
            // Clear Reactor-set DataContext (FrameworkElement-only DP).
            fe.ClearValue(FrameworkElement.DataContextProperty);

            _pool.Return(fe);
        }

        // Author-defined extra reset runs last (per Q18 contract).
        policy?.Reset?.Invoke(control);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Internal binding shims for V1 ReactorBinding<TElement> (1.6)
    // ════════════════════════════════════════════════════════════════════
    //
    // Each shim corresponds to an Ensure*Subscribed helper in this file.
    // They exist so ReactorBinding<TElement>.On<Event>(Action<TElement,…>)
    // can update the per-control trampoline without exposing the private
    // ModifierEventHandlerState type. Strongly-typed delegates only — no reflection.

    internal static void BindOnPointerPressed(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
        => EnsurePointerPressedSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnPointerMoved(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
        => EnsurePointerMovedSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnPointerReleased(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
        => EnsurePointerReleasedSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnPointerEntered(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
        => EnsurePointerEnteredSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnPointerExited(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
        => EnsurePointerExitedSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnPointerCaptureLost(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
        => EnsurePointerCaptureLostSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnPointerWheelChanged(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
        => EnsurePointerWheelChangedSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnTapped(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs>? handler)
        => EnsureTappedSubscribed(fe, GetOrCreateModifierState(fe), handler, oldHandler: null);
    internal static void BindOnDoubleTapped(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs>? handler)
        => EnsureDoubleTappedSubscribed(fe, GetOrCreateModifierState(fe), handler, oldHandler: null);
    internal static void BindOnRightTapped(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs>? handler)
        => EnsureRightTappedSubscribed(fe, GetOrCreateModifierState(fe), handler, oldHandler: null);
    internal static void BindOnHolding(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.HoldingRoutedEventArgs>? handler)
        => EnsureHoldingSubscribed(fe, GetOrCreateModifierState(fe), handler, oldHandler: null);
    internal static void BindOnKeyDown(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? handler)
        => EnsureKeyDownSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnKeyUp(FrameworkElement fe, Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? handler)
        => EnsureKeyUpSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnGotFocus(FrameworkElement fe, Action<object, RoutedEventArgs>? handler)
        => EnsureGotFocusSubscribed(fe, GetOrCreateModifierState(fe), handler);
    internal static void BindOnLostFocus(FrameworkElement fe, Action<object, RoutedEventArgs>? handler)
        => EnsureLostFocusSubscribed(fe, GetOrCreateModifierState(fe), handler);

    /// <summary>
    /// Spec 047 §9.2 / §14 Phase 1 (1.7) — anchor a strongly-typed
    /// trampoline delegate to the control's per-control event payload box
    /// so the GC keeps the captured closure alive while WinUI's
    /// subscription list holds the native side. Pool reset clears the
    /// box on return.
    /// </summary>
    internal static void AnchorCustomEventTrampoline(FrameworkElement fe, Delegate trampoline)
    {
        var state = GetOrCreateReactorState(fe);
        if (state.ControlEventState is not ControlEventStateBox box
            || box.HandlerType != typeof(V1Protocol.CustomEventAnchorPayload))
        {
            box = new ControlEventStateBox
            {
                HandlerType = typeof(V1Protocol.CustomEventAnchorPayload),
                Payload = new V1Protocol.CustomEventAnchorPayload()
            };
            state.ControlEventState = box;
        }
        ((V1Protocol.CustomEventAnchorPayload)box.Payload).Trampolines.Add(trampoline);
    }

    /// <summary>
    /// Spec 047 §9.2 / §14 Phase 1 — get-or-create a typed per-control event
    /// payload (e.g. <c>ToggleSwitchEventPayload</c>). The payload survives
    /// pool rent/return cycles so trampolines wired once per control lifetime
    /// stay alive (mirrors the legacy <c>ModifierEventHandlerState</c> "stable
    /// trampoline + mutable Current* slot" pattern documented in
    /// <c>Reconciler.cs:3266-3289</c>). Authors of built-in ports use this
    /// instead of <see cref="V1Protocol.ReactorBinding{TElement}.OnCustomEvent"/>
    /// for the seven audited control-intrinsic events — see
    /// <c>ControlEventPayloads.cs</c>.
    /// </summary>
    internal static T GetOrCreateControlEventPayload<T>(FrameworkElement fe) where T : class, new()
    {
        var state = GetOrCreateReactorState(fe);
        if (state.ControlEventState is ControlEventStateBox existing
            && existing.HandlerType == typeof(T))
        {
            return (T)existing.Payload;
        }
        var payload = new T();
        state.ControlEventState = new ControlEventStateBox
        {
            HandlerType = typeof(T),
            Payload = payload,
        };
        return payload;
    }

    /// <summary>
    /// Spec 047 §8 — non-allocating sibling of
    /// <see cref="GetOrCreateControlEventPayload{T}"/>. Returns the existing
    /// per-control payload of type <typeparamref name="T"/>, or <c>null</c> if
    /// the control has no <c>ReactorState</c>, no event-state box, or a box of a
    /// different handler type. Unlike the get-or-create form this never creates
    /// state, so callers on a hot/read path (e.g. the controlled-prop value-diff
    /// echo check) can probe without forcing allocation on callback-less or
    /// unsubscribed controls.
    /// </summary>
    internal static T? TryGetControlEventPayload<T>(FrameworkElement fe) where T : class
    {
        if (fe.GetValue(ReactorAttached.StateProperty) is ReactorState state
            && state.ControlEventState is ControlEventStateBox box
            && box.HandlerType == typeof(T))
        {
            return (T)box.Payload;
        }
        return null;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Disposable wrappers for V1 MountContext (1.6)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.6) — typed context push that returns an
    /// <see cref="IDisposable"/> the handler disposes when its scope ends.
    /// The underlying scope is the same one used by built-in ContextProvider
    /// elements.
    /// </summary>
    internal IDisposable PushContextDisposable<T>(Context<T> context, T value)
    {
        var dict = new Dictionary<ContextBase, object?>(1) { [context] = value };
        _contextScope.Push(dict);
        return new PopOnDispose(_contextScope, 1);
    }

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.6) — push a stagger scope for child enter
    /// transitions. Returns an <see cref="IDisposable"/> that pops the
    /// scope on dispose.
    /// </summary>
    internal IDisposable PushStaggerScopeDisposable(TimeSpan delay)
    {
        PushStaggerScope(delay);
        return new PopStaggerOnDispose();
    }

    private sealed class PopOnDispose : IDisposable
    {
        private ContextScope? _scope;
        private readonly int _count;
        public PopOnDispose(ContextScope scope, int count) { _scope = scope; _count = count; }
        public void Dispose()
        {
            var s = _scope;
            if (s is null) return;
            _scope = null;
            s.Pop(_count);
        }
    }

    private sealed class PopStaggerOnDispose : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            PopStaggerScope();
        }
    }

    internal interface ITypeRegistration
    {
        UIElement Mount(Element element, Action requestRerender, Reconciler reconciler);
        UIElement? Update(Element oldEl, Element newEl, UIElement control, Action requestRerender, Reconciler reconciler);
        void Unmount(UIElement control, Reconciler reconciler);
        bool HasUnmount { get; }
    }

    private sealed class TypeRegistration<TElement, TControl> : ITypeRegistration
        where TElement : Element
        where TControl : UIElement
    {
        private readonly Func<Reconciler, TElement, Action, TControl> _mount;
        private readonly Func<Reconciler, TElement, TElement, TControl, Action, UIElement?> _update;
        private readonly Action<Reconciler, TControl>? _unmount;

        public TypeRegistration(
            Func<Reconciler, TElement, Action, TControl> mount,
            Func<Reconciler, TElement, TElement, TControl, Action, UIElement?> update,
            Action<Reconciler, TControl>? unmount)
        {
            _mount = mount;
            _update = update;
            _unmount = unmount;
        }

        public bool HasUnmount => _unmount is not null;

        public UIElement Mount(Element element, Action requestRerender, Reconciler reconciler)
            => _mount(reconciler, (TElement)element, requestRerender);

        public UIElement? Update(Element oldEl, Element newEl, UIElement control, Action requestRerender, Reconciler reconciler)
        {
            // Guard against control type mismatch (e.g., recycled from pool or element type changed at this position).
            // If the existing control isn't our expected type, force a fresh mount instead of crashing.
            if (control is not TControl typedControl || oldEl is not TElement typedOldEl)
                return _mount(reconciler, (TElement)newEl, requestRerender);

            return _update(reconciler, typedOldEl, (TElement)newEl, typedControl, requestRerender);
        }

        public void Unmount(UIElement control, Reconciler reconciler)
        {
            if (control is TControl typedControl)
                _unmount?.Invoke(reconciler, typedControl);
        }
    }

    // <snippet:reconciler-entry>
    public UIElement? Reconcile(
        Element? oldElement,
        Element? newElement,
        UIElement? existingControl,
        Action requestRerender)
    {
        // Trace only top-level reconcile passes (depth == 0) to avoid flooding
        // the provider with per-subtree entries; nested Reconcile() calls during
        // the same pass don't emit their own start/stop. Gate the depth counter
        // and Start emit on IsEnabled so the disabled path pays nothing extra.
        bool emitTrace = Diagnostics.ReactorEventSource.Log.IsEnabled(
            global::System.Diagnostics.Tracing.EventLevel.Informational,
            Diagnostics.ReactorEventSource.Keywords.Reconcile)
            && _reconcileTraceDepth++ == 0;
        if (emitTrace)
        {
            Diagnostics.ReactorEventSource.Log.ReconcileStart(
                newElement?.GetType().Name ?? "null");
        }
        if (_debugReconcileDepth++ == 0)
        {
            DebugElementsDiffed = 0;
            DebugElementsSkipped = 0;
            DebugUIElementsCreated = 0;
            DebugUIElementsModified = 0;
            if (ReactorFeatureFlags.HighlightReconcileChanges)
            {
                (_highlightMounted ??= new()).Clear();
                (_highlightModified ??= new()).Clear();
            }
            // Consume the hot-reload signal exactly once per top-level pass so
            // every component re-runs Render() even when props/deps are unchanged.
            _forceFullRenderActive = ForceFullRenderPending;
            ForceFullRenderPending = false;

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
            if (newElement is null or EmptyElement)
            {
                if (existingControl is not null)
                    Unmount(existingControl);
                return null;
            }

            if (oldElement is null or EmptyElement || existingControl is null)
                return Mount(newElement, requestRerender);

            return ReconcileImperative(oldElement, newElement, existingControl, requestRerender);
            // </snippet:reconciler-entry>
        }
        finally
        {
            if (emitTrace)
            {
                _reconcileTraceDepth--;
                Diagnostics.ReactorEventSource.Log.ReconcileStop(
                    DebugElementsDiffed, DebugElementsSkipped,
                    DebugUIElementsCreated, DebugUIElementsModified);
            }
        }
        } finally
        {
            if (--_debugReconcileDepth == 0)
            {
                _forceFullRenderActive = false;
                _dirtyAncestorPath?.Clear();
            }
        }
    }

    private void PopulateDirtyAncestorPath()
    {
        // Hot path — most renders have zero self-triggered nodes (the
        // pass was triggered by a prop change higher up). Avoid the
        // HashSet allocation entirely until we find one.
        HashSet<UIElement>? set = null;
        foreach (var (control, node) in _componentNodes)
        {
            if (!node.SelfTriggered) continue;
            set ??= _dirtyAncestorPath ?? new HashSet<UIElement>();
            // Add the control itself first — Update on the wrapper
            // element that owns this control needs to bypass too so it
            // reaches the Component's UpdateComponent path.
            set.Add(control);
            var cursor = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(control) as UIElement;
            while (cursor is not null)
            {
                if (!set.Add(cursor)) break; // already on a previously-walked path
                cursor = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(cursor) as UIElement;
            }
        }
        _dirtyAncestorPath = set;
    }

    // Tracks top-level Reconcile() entries so trace start/stop only fires once
    // per pass. Only mutated when the Reconcile keyword is enabled.
    private int _reconcileTraceDepth;

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
            var replacement = Update(oldElement, newElement, existingControl, requestRerender);
            if (replacement is not null && replacement != existingControl)
                Unmount(existingControl);
            return replacement ?? existingControl;
        }

        Unmount(existingControl);
        return Mount(newElement, requestRerender);
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
            }
            else if (node.Context is not null && newEl is MemoElement newMemo)
            {
                // MemoElement memo check
                var oldDeps = node.MemoDependencies;
                var newDeps = newMemo.Dependencies;
                bool depsChanged = oldDeps is null && newDeps is null
                    ? false // both null = render once, never re-render from parent
                    : oldDeps is null || newDeps is null || !DepsEqual(oldDeps, newDeps);
                bool contextChanged = HasConsumedContextChanged(node);
                skipRender = !depsChanged && !contextChanged;
            }

            if (skipRender)
            {
                // Still update the element reference (modifiers may have changed on the ComponentElement itself)
                node.Element = newEl;
                return;
            }
        }

        // ── Render the component ──
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
            componentName = node.Component?.GetType().Name ?? newEl.GetType().Name;
            Diagnostics.ReactorEventSource.Log.ComponentRenderStart(
                componentName, selfTriggered ? "self" : "parent");
            renderStart = global::System.Diagnostics.Stopwatch.GetTimestamp();
        }

        Element newChildElement;
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

                node.Component.Context.BeginRender(componentRerender, _contextScope);
                newChildElement = node.Component.Render();
                FlushEffectsTraced(node.Component.Context, componentName);
            }
            else if (node.Context is not null && newEl is FuncElement func)
            {
                node.Context.BeginRender(componentRerender, _contextScope);
                newChildElement = func.RenderFunc(node.Context);
                FlushEffectsTraced(node.Context, componentName);
            }
            else if (node.Context is not null && newEl is MemoElement memo)
            {
                node.Context.BeginRender(componentRerender, _contextScope);
                newChildElement = memo.RenderFunc(node.Context);
                FlushEffectsTraced(node.Context, componentName);
            }
            else
            {
                if (traceRender)
                    Diagnostics.ReactorEventSource.Log.ComponentRenderStop(componentName!, 0);
                return;
            }
        }
        catch (Exception ex) when (_errorBoundaryDepth == 0 && ex is not OutOfMemoryException and not StackOverflowException)
        {
            _logger?.LogError(ex, "Component Render() threw: {ComponentName}", newEl.GetType().Name);
            if (Diagnostics.ReactorEventSource.Log.IsEnabled(
                    global::System.Diagnostics.Tracing.EventLevel.Error,
                    Diagnostics.ReactorEventSource.Keywords.Errors))
            {
                Diagnostics.ReactorEventSource.Log.RenderError(
                    componentName ?? newEl.GetType().Name, ex.GetType().Name, ex.Message);
            }
            newChildElement = ErrorFallback.BuildElement(ex);
        }

        if (traceRender)
        {
            var renderElapsedUs = (long)((global::System.Diagnostics.Stopwatch.GetTimestamp() - renderStart)
                * 1_000_000.0 / global::System.Diagnostics.Stopwatch.Frequency);
            Diagnostics.ReactorEventSource.Log.ComponentRenderStop(componentName!, renderElapsedUs);
        }

        // Dereference the Border wrapper to get the actual child control.
        // Each component is wrapped in a Border as an identity anchor, so we
        // reconcile the child inside the wrapper, not the wrapper itself.
        var existingChild = (control as Border)?.Child;
        var newControl = Reconcile(node.RenderedElement, newChildElement, existingChild, componentRerender);
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
    private static Action CreateComponentRerender(ComponentNode node, Action requestRerender)
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
            // dispatcher captured at host bring-up so we don't race the
            // reconciler and ElementPool from a background thread.
            // A non-null GetForCurrentThread() doesn't imply UI affinity —
            // the only authoritative check is HasThreadAccess on the UI DQ.
            var uiDq = Microsoft.UI.Reactor.ReactorApp.UIDispatcher;
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
                    InvokeRerenderTracked(requestRerender);
                });
                return;
            }

            // Either we're on the UI thread, or no UI DQ has been captured
            // (test/headless host) — run inline.
            node.SelfTriggered = true;
            InvokeRerenderTracked(requestRerender);
        };
    }

    private static void InvokeRerenderTracked(Action requestRerender)
    {
        t_rerenderDepth++;
        try { requestRerender(); }
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
    {
        var childCollection = new PanelChildCollection(panel);
        // Skip the try/finally and event emit when the Reconcile keyword is off.
        if (!Diagnostics.ReactorEventSource.Log.IsEnabled(
                global::System.Diagnostics.Tracing.EventLevel.Informational,
                Diagnostics.ReactorEventSource.Keywords.Reconcile))
        {
            ChildReconciler.Reconcile(oldChildren, newChildren, childCollection, this, requestRerender);
            return;
        }
        Diagnostics.ReactorEventSource.Log.ChildReconcileStart(oldChildren.Length, newChildren.Length);
        try { ChildReconciler.Reconcile(oldChildren, newChildren, childCollection, this, requestRerender); }
        finally { Diagnostics.ReactorEventSource.Log.ChildReconcileStop(); }
    }

    private void ReconcileItemsChildren(
        Element[] oldChildren, Element[] newChildren,
        WinUI.ItemsControl itemsControl, Action requestRerender)
    {
        var childCollection = new ItemsControlChildCollection(itemsControl);
        if (!Diagnostics.ReactorEventSource.Log.IsEnabled(
                global::System.Diagnostics.Tracing.EventLevel.Informational,
                Diagnostics.ReactorEventSource.Keywords.Reconcile))
        {
            ChildReconciler.Reconcile(oldChildren, newChildren, childCollection, this, requestRerender);
            return;
        }
        Diagnostics.ReactorEventSource.Log.ChildReconcileStart(oldChildren.Length, newChildren.Length);
        try { ChildReconciler.Reconcile(oldChildren, newChildren, childCollection, this, requestRerender); }
        finally { Diagnostics.ReactorEventSource.Log.ChildReconcileStop(); }
    }

    /// <summary>
    /// Updates a single child element. Returns non-null if the child control was replaced.
    /// Public so registered type handlers can recursively reconcile children.
    /// </summary>
    public UIElement? UpdateChild(Element oldEl, Element newEl, UIElement control, Action requestRerender)
    {
        return Update(oldEl, newEl, control, requestRerender);
    }

    /// <summary>
    /// Unmounts a child control. Public so registered type handlers can unmount children.
    /// </summary>
    public void UnmountChild(UIElement control)
    {
        Unmount(control);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Unmount
    // ════════════════════════════════════════════════════════════════════

    private void Unmount(UIElement control)
    {
        UnmountRecursive(control);
    }

    /// <summary>
    /// Spec 047 §14 Phase 4 (4.0.2) — tears down the per-instance navigation
    /// state (route subscription, cache, current child control) tracked for a
    /// <see cref="NavigationHostElement"/>'s host Grid. Owned by
    /// <c>NavigationHostHandler.Unmount</c> on the V1 path; also invoked by the
    /// V1-OFF fallback in <see cref="UnmountRecursive"/>. No-op if the control
    /// is not a tracked navigation host.
    /// </summary>
    internal void CleanupNavigationHostNode(UIElement control)
    {
        if (!_navigationHostNodes.TryGetValue(control, out var navNode))
            return;
        if (navNode.RouteChangedHandler is not null)
            navNode.Handle.RouteChanged -= navNode.RouteChangedHandler;
        navNode.Handle.Detach();
        navNode.Cache?.Clear();
        if (navNode.CurrentChildControl is not null)
            UnmountRecursive(navNode.CurrentChildControl);
        _navigationHostNodes.Remove(control);
    }

    private void UnmountRecursive(UIElement control)
    {
        // Capture connected animation snapshot while element is still in the visual tree
        if (control is FrameworkElement caFe && GetElementTag(caFe) is Element caEl
            && caEl.ConnectedAnimationKey is not null)
        {
            try
            {
                var service = ConnectedAnimationService.GetForCurrentView();
                service.PrepareToAnimate(caEl.ConnectedAnimationKey, control);
            }
            catch (global::System.Runtime.InteropServices.COMException ex) when (Diagnostics.HResults.IsTeardownReentry(ex.HResult)) { }
        }

        // Clean up animation state (mirrors UnmountAndCollect)
        if (control is FrameworkElement animFe && GetElementTag(animFe) is Element animEl)
        {
            if (animEl.InteractionStates is not null)
                ClearInteractionStates(control);
            if (animEl.KeyframeAnimations is not null)
                ClearKeyframeAnimations(control, animEl.KeyframeAnimations);
            if (animEl.ScrollAnimation is not null)
                ClearScrollAnimation(control, animEl.ScrollAnimation);
        }

        if (_componentNodes.TryGetValue(control, out var node))
        {
            Diagnostics.ReactorEventSource.Log.ComponentUnmount(
                node.Component?.GetType().Name ?? node.Element?.GetType().Name ?? "unknown");
            node.Component?.Context.RunCleanups();
            node.Context?.RunCleanups();
            _componentNodes.Remove(control);
            if (ReactorFeatureFlags.ShowLayoutCost)
                RaiseLayoutCostComponentUnmounted(control);
        }

        _errorBoundaryNodes.Remove(control);

        // Spec 047 §14 Phase 1 (1.1) — V1 handler unmount dispatch. Standard
        // handlers return CollectSelf (terminate the traversal — children
        // already torn down by the handler; e.g. NavigationHostHandler.Unmount
        // runs CleanupNavigationHostNode). Phase 3 completion decorator handlers
        // may return ContinueDefaultTraversal to let the engine recurse into the
        // wrapped child whose control they returned (e.g., FlyoutElement returns
        // the Target's mounted control; the Target's children still need their
        // own unmount).
        if (control is FrameworkElement v1Fe && GetElementTag(v1Fe) is Element v1TagEl
            && _v1Handlers.TryGet(v1TagEl.GetType(), out var v1Entry) && v1Entry.HasUnmount)
        {
            var v1Disposition = v1Entry.Unmount(control, this);
            if (v1Disposition != V1Protocol.V1UnmountDisposition.ContinueDefaultTraversal)
                return;
        }

        // Check registered type unmount handlers via the attached element
        if (control is FrameworkElement fe && GetElementTag(fe) is Element tagEl
            && _typeRegistry.TryGetValue(tagEl.GetType(), out var reg) && reg.HasUnmount)
        {
            reg.Unmount(control, this);
            return;
        }

        // XamlHostElement children were created outside Reactor's tree —
        // do NOT recurse into them (they may have stale parent references
        // or be types Reactor doesn't know how to clean). Fully detach
        // reactor state so retained-by-app references can't fire stale
        // callbacks through orphaned trampolines.
        if (control is FrameworkElement hostFe && GetElementTag(hostFe) is XamlHostElement)
        {
            DetachReactorState(hostFe);
            return;
        }

        // XamlPageElement — clear content to trigger Page.OnNavigatedFrom cleanup
        if (control is WinUI.Frame pageFrame && GetElementTag(pageFrame) is XamlPageElement)
        {
            pageFrame.Content = null;
            DetachReactorState(pageFrame);
            return;
        }

        if (control is WinUI.Panel panel)
        {
            foreach (var child in panel.Children)
                UnmountRecursive(child);
        }
        else if (control is WinUI.ItemsRepeater repeater)
        {
            // ItemsRepeater projects to C# as a FrameworkElement (not a
            // Panel — see microsoft-ui-xaml-lift/.../ItemsRepeater.idl), so
            // the Panel branch above doesn't catch it even though the
            // framework keeps both realized and recycled containers in
            // its visual subtree. Without this branch, row Components'
            // UseEffect cleanups would never run when the LazyStack is
            // unmounted (e.g., on navigation), leaking any in-flight
            // timers / subscriptions / async loops. We walk via
            // VisualTreeHelper because the public ItemsRepeater surface
            // doesn't expose a Children collection. (PR #324 review)
            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(repeater);
            for (int i = 0; i < count; i++)
            {
                if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(repeater, i) is UIElement child)
                    UnmountRecursive(child);
            }
        }
        else if (control is WinUI.Border border && border.Child is not null)
        {
            UnmountRecursive(border.Child);
        }
        else if (control is WinUI.ScrollViewer sv && sv.Content is UIElement svChild)
        {
            UnmountRecursive(svChild);
        }
        else if (control is WinUI.UserControl uc && uc.Content is UIElement ucChild)
        {
            UnmountRecursive(ucChild);
        }
        else if (control is WinUI.ContentControl cc && cc.Content is UIElement ccChild)
        {
            UnmountRecursive(ccChild);
        }
    }

    /// <summary>
    /// Unmounts and returns all descendants + root to the pool.
    /// Call AFTER the root has been detached from the visual tree.
    /// Collects all controls first, then pools bottom-up so DetachFromParent
    /// removes children before parents clear their collections.
    /// </summary>
    /// <summary>
    /// Removes a child from its collection with exit transition support.
    /// If the child has an ElementTransition with an exit side, the removal is deferred
    /// until the exit animation completes. Otherwise, immediate removal.
    /// </summary>
    internal void RemoveChildWithExitTransition(IChildCollection children, int index)
    {
        var control = children.Get(index);
        var transition = (control is FrameworkElement fe && GetElementTag(fe) is Element el)
            ? el.ElementTransition : null;

        if (transition?.GetExitTransition() is not null)
        {
            // Defer removal: play exit animation, then remove + pool on completion.
            ApplyExitTransition(control, transition, () =>
            {
                // Find current index — it may have shifted if earlier items were removed.
                for (int i = 0; i < children.Count; i++)
                {
                    if (ReferenceEquals(children.Get(i), control))
                    {
                        children.RemoveAt(i);
                        break;
                    }
                }
                UnmountAndPool(control);
            });
            return;
        }

        // Spec 042 §6 — under an Animations.Animate ambient and with no
        // per-element exit transition, fade the child out in place before
        // removal so the structural change reads visually. The exit is
        // best-effort: if Composition throws we fall through to the
        // synchronous remove below.
        var ambient = Microsoft.UI.Reactor.Core.Internal.AnimationAmbient.Current;
        if (ambient is { HasEffect: true }
            && TryApplyAmbientExit(control, ambient.Kind, onComplete: () =>
            {
                for (int i = 0; i < children.Count; i++)
                {
                    if (ReferenceEquals(children.Get(i), control))
                    {
                        children.RemoveAt(i);
                        break;
                    }
                }
                UnmountAndPool(control);
            }))
        {
            return;
        }

        children.RemoveAt(index);
        UnmountAndPool(control);
    }

    /// <summary>
    /// Spec 042 §6 — fade-out a child whose exit was triggered under an
    /// <see cref="Animations.Animate"/> transaction with no per-element
    /// transition set. Returns <see langword="true"/> when the fade was
    /// scheduled (the caller must wait for the completion callback to
    /// remove the child); <see langword="false"/> when Composition rejects
    /// the request (headless / disposed contexts) so the caller falls back
    /// to synchronous removal.
    /// </summary>
    private static bool TryApplyAmbientExit(UIElement control, AnimationKind kind, Action onComplete)
    {
        var curve = Microsoft.UI.Reactor.Core.Internal.AnimationKindMap.ToCurve(kind);
        if (curve is null) return false;

        try
        {
            var visual = global::Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(control);
            var compositor = visual.Compositor;
            var batch = compositor.CreateScopedBatch(global::Microsoft.UI.Composition.CompositionBatchTypes.Animation);
            var anim = Microsoft.UI.Reactor.Animation.AnimationHelper.CreateScalarTargetAnimation(compositor, 0f, curve);
            visual.StartAnimation("Opacity", anim);
            batch.End();
            batch.Completed += (_, _) =>
            {
                // Reset opacity so a recycled element doesn't reappear
                // invisible if it gets re-mounted later.
                visual.Opacity = 1f;
                onComplete();
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Replaces a child at an index with exit transition support on the old child.
    /// If the old child has an exit transition, the new child is inserted immediately
    /// and the old child animates out then gets removed. Otherwise, immediate replace.
    /// </summary>
    internal void ReplaceChildWithExitTransition(IChildCollection children, int index, UIElement newControl)
    {
        var oldControl = children.Get(index);
        var transition = (oldControl is FrameworkElement fe && GetElementTag(fe) is Element el)
            ? el.ElementTransition : null;

        if (transition?.GetExitTransition() is not null)
        {
            // Replace immediately with the new control so the UI updates.
            children.Replace(index, newControl);
            // Re-insert the old control after the new one for exit animation.
            // It will be positioned by layout but the animation (fade/slide) makes
            // it visually leave. We insert it right after the replacement.
            children.Insert(index + 1, oldControl);
            ApplyExitTransition(oldControl, transition, () =>
            {
                for (int i = 0; i < children.Count; i++)
                {
                    if (ReferenceEquals(children.Get(i), oldControl))
                    {
                        children.RemoveAt(i);
                        break;
                    }
                }
                UnmountAndPool(oldControl);
            });
        }
        else
        {
            Unmount(oldControl);
            children.Replace(index, newControl);
        }
    }

    internal void UnmountAndPool(UIElement control)
    {
        var toPool = new List<FrameworkElement>();
        UnmountAndCollect(control, toPool);

        // Pool top-down: parent's CleanElement calls Children.Clear() which
        // detaches children, so by the time children are pooled they're parentless.
        for (int i = 0; i < toPool.Count; i++)
            _pool.Return(toPool[i]);
    }

    private void UnmountAndCollect(UIElement control, List<FrameworkElement> toPool)
    {
        // Capture connected animation snapshot while element is still in the visual tree
        if (control is FrameworkElement caFe && GetElementTag(caFe) is Element caEl
            && caEl.ConnectedAnimationKey is not null)
        {
            try
            {
                var service = ConnectedAnimationService.GetForCurrentView();
                service.PrepareToAnimate(caEl.ConnectedAnimationKey, control);
            }
            catch (global::System.Runtime.InteropServices.COMException ex) when (Diagnostics.HResults.IsTeardownReentry(ex.HResult)) { }
        }

        // Clean up animation state
        if (control is FrameworkElement animFe && GetElementTag(animFe) is Element animEl)
        {
            if (animEl.InteractionStates is not null)
                ClearInteractionStates(control);
            if (animEl.KeyframeAnimations is not null)
                ClearKeyframeAnimations(control, animEl.KeyframeAnimations);
            if (animEl.ScrollAnimation is not null)
                ClearScrollAnimation(control, animEl.ScrollAnimation);
        }

        // Run cleanup logic (component teardown, etc.)
        if (_componentNodes.TryGetValue(control, out var node))
        {
            Diagnostics.ReactorEventSource.Log.ComponentUnmount(
                node.Component?.GetType().Name ?? node.Element?.GetType().Name ?? "unknown");
            node.Component?.Context.RunCleanups();
            node.Context?.RunCleanups();
            _componentNodes.Remove(control);
            if (ReactorFeatureFlags.ShowLayoutCost)
                RaiseLayoutCostComponentUnmounted(control);
        }

        // Spec 047 §14 Phase 1 (1.1) — V1 handler unmount on the
        // UnmountAndCollect path. Standard handlers return CollectSelf
        // (mirrors the legacy registry behavior: pool the control, no
        // recursion). Phase 3 completion decorator handlers may opt out
        // of pooling (SkipPool — handler-managed teardown) or fall back
        // to the default child traversal (ContinueDefaultTraversal —
        // wrapped child owns the control identity, default recursion
        // reaches the wrapped element's own unmount).
        if (control is FrameworkElement v1Fe && GetElementTag(v1Fe) is Element v1TagEl
            && _v1Handlers.TryGet(v1TagEl.GetType(), out var v1Entry) && v1Entry.HasUnmount)
        {
            var v1Disposition = v1Entry.Unmount(control, this);
            switch (v1Disposition)
            {
                case V1Protocol.V1UnmountDisposition.CollectSelf:
                    if (control is FrameworkElement v1Pool)
                        toPool.Add(v1Pool);
                    return;
                case V1Protocol.V1UnmountDisposition.SkipPool:
                    return;
                case V1Protocol.V1UnmountDisposition.ContinueDefaultTraversal:
                    break;
            }
        }

        if (control is FrameworkElement fe && GetElementTag(fe) is Element tagEl
            && _typeRegistry.TryGetValue(tagEl.GetType(), out var reg) && reg.HasUnmount)
        {
            reg.Unmount(control, this);
            // Collect this control for pooling, but do NOT recurse into children —
            // they were created outside Reactor's tree and must not be pooled.
            // (Mirrors UnmountRecursive which returns early in this case.)
            if (control is FrameworkElement poolCandidate2)
                toPool.Add(poolCandidate2);
            return;
        }

        // Recurse into children.
        if (control is WinUI.Panel panel)
        {
            foreach (var child in panel.Children)
                UnmountAndCollect(child, toPool);
        }
        else if (control is WinUI.Border border && border.Child is not null)
        {
            UnmountAndCollect(border.Child, toPool);
        }
        else if (control is WinUI.ScrollViewer sv && sv.Content is UIElement svChild)
        {
            UnmountAndCollect(svChild, toPool);
        }
        else if (control is WinUI.UserControl uc && uc.Content is UIElement ucChild)
        {
            UnmountAndCollect(ucChild, toPool);
        }
        else if (control is WinUI.ContentControl cc && cc.Content is UIElement ccChild)
        {
            UnmountAndCollect(ccChild, toPool);
            cc.Content = null; // Detach so pooled child has no parent
        }

        if (control is FrameworkElement poolCandidate)
            toPool.Add(poolCandidate);
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
        // MemoElement can always update to MemoElement (same type check above handles it)
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
            var replacement = Update(oldChild, newChild, existing, requestRerender);
            return replacement ?? existing;
        }
        if (existing is not null) Unmount(existing);
        return Mount(newChild, requestRerender);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Shared helpers (used by Mount + Update)
    // ════════════════════════════════════════════════════════════════════

    private static bool DepsEqual(object?[] prev, object?[] next)
    {
        if (prev.Length != next.Length) return false;
        for (int i = 0; i < prev.Length; i++)
        {
            if (!Equals(prev[i], next[i])) return false;
        }
        return true;
    }

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
        try
        {
            foreach (var setter in setters) setter(control);
        }
        finally
        {
            if (state is not null) state.EchoSuppressScopeDepth--;
        }
    }

    // WPF/WinUI auto-populate UIA Name from a control's string Content through the
    // automation peer, but AutomationProperties.GetName on the raw element only
    // returns an explicitly-set attached property. UIA clients that read the attached
    // property directly (our own tree walker, Appium's AutomationName getter, some
    // screen readers that probe before invoking the peer) see an empty string when
    // the author never set .AutomationName(). Mirroring the caption into the
    // attached property at mount makes both lookup paths agree, so
    // click { selector: "[name='+ 1']" } and a screen reader saying "+ 1" both work
    // without the author having to say so twice. Skips when the author already set
    // an AutomationName via modifier or setter — explicit always wins.
    /// <summary>
    /// Spec 047 §14 Phase 1 (1.3) — promoted from internal. Mirrors a control's
    /// caption into <c>AutomationProperties.Name</c> when the author has not set
    /// one explicitly, so UIA clients that read the attached property directly
    /// (not via the peer) still find the name. Provisional API;
    /// see <c>REACTOR_V1_PREVIEW</c>.
    /// </summary>
    public static void ApplyDefaultAutomationName(FrameworkElement fe, string? caption)
    {
        if (fe is null) return;
        if (string.IsNullOrWhiteSpace(caption)) return;
        var existing = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(fe);
        if (!string.IsNullOrEmpty(existing)) return;
        var trimmed = caption.Length > 100 ? caption.Substring(0, 100) : caption;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fe, trimmed);
    }

    // Update variant: a label change ("+ 1" → "+ 2") should update UIA Name as
    // long as the author didn't override it. We can't distinguish "author set
    // it to the previous caption" from "our default set it" without tracking
    // provenance, so the rule is: overwrite when the current value is empty or
    // equals the previous caption — any other value means the author intervened
    // (via modifier or setter) and we leave it alone.
    /// <summary>
    /// Spec 047 §14 Phase 1 (1.3) — promoted from internal. Update-time pair
    /// for <see cref="ApplyDefaultAutomationName"/>; overwrites the
    /// automation Name only when it matches the previous caption (i.e. the
    /// author hasn't intervened). Provisional API; see <c>REACTOR_V1_PREVIEW</c>.
    /// </summary>
    public static void UpdateDefaultAutomationName(FrameworkElement fe, string? oldCaption, string? newCaption)
    {
        if (fe is null) return;
        if (string.IsNullOrWhiteSpace(newCaption)) return;
        var current = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(fe);
        bool authorOverride =
            !string.IsNullOrEmpty(current) &&
            (oldCaption is null || !string.Equals(current, oldCaption, StringComparison.Ordinal));
        if (authorOverride) return;
        var trimmed = newCaption.Length > 100 ? newCaption.Substring(0, 100) : newCaption;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fe, trimmed);
    }

    internal static string? ExtractElementCaption(Element? element) => element switch
    {
        TextBlockElement te => te.Content,
        null => null,
        _ => null,
    };

    // Resolves the caption-bearing string for controls whose visible text is
    // implicit (Button's Label, CheckBox's Label, TextBlock's Content, …). Returns
    // null for elements without a natural caption so ApplyDefaultAutomationName
    // leaves them alone. Kept alongside the helper so adding new caption-bearing
    // controls is one place to change.
    internal static string? ResolveCaptionForElement(Element element) => element switch
    {
        TextBlockElement te => te.Content,
        ButtonElement be => be.Label ?? ExtractElementCaption(be.ContentElement),
        HyperlinkButtonElement hle => hle.Content,
        RepeatButtonElement rbe => rbe.Label,
        ToggleButtonElement tbe => tbe.Label,
        DropDownButtonElement dde => dde.Label,
        SplitButtonElement sbe => sbe.Label,
        ToggleSplitButtonElement tsbe => tsbe.Label,
        CheckBoxElement cbe => cbe.Label,
        RadioButtonElement rbe => rbe.Label,
        ToggleSwitchElement tse => tse.Header as string ?? tse.OnContent ?? tse.OffContent,
        TextBoxElement tfe => tfe.Header as string ?? tfe.PlaceholderText,
        _ => null,
    };

    internal static void ApplyTransitions(UIElement uie, ImplicitTransitions? implicitT, ThemeTransitions? themeT)
    {
        if (implicitT is not null)
        {
            try
            {
                if (implicitT.Opacity is not null)
                    uie.OpacityTransition = implicitT.Opacity;
                if (implicitT.Rotation is not null)
                    uie.RotationTransition = implicitT.Rotation;
                if (implicitT.Scale is not null)
                    uie.ScaleTransition = implicitT.Scale;
                if (implicitT.Translation is not null)
                    uie.TranslationTransition = implicitT.Translation;
            }
            catch (UnauthorizedAccessException)
            {
                // WinUI blocks XAML implicit transition APIs once GetElementVisual()
                // has been called on the element (e.g., from .Animate(), enter transitions,
                // or a previous owner via the pool). Fall back to compositor implicit
                // animations which always work.
                ApplyTransitionsViaCompositor(uie, implicitT);
            }
            if (implicitT.Background is not null)
            {
                switch (uie)
                {
                    case WinUI.Grid g: g.BackgroundTransition = implicitT.Background; break;
                    case WinUI.StackPanel sp: sp.BackgroundTransition = implicitT.Background; break;
                    case WinUI.ContentPresenter cp: cp.BackgroundTransition = implicitT.Background; break;
                }
            }
        }

        if (themeT?.Children is { } children)
        {
            var tc = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
            foreach (var t in children) tc.Add(t);
            switch (uie)
            {
                case WinUI.StackPanel sp: sp.ChildrenTransitions = tc; break;
                case WinUI.Grid g: g.ChildrenTransitions = tc; break;
                case WinUI.Canvas c: c.ChildrenTransitions = tc; break;
                case WinUI.Border b: b.ChildTransitions = tc; break;
                case WinUI.ContentPresenter cp: cp.ContentTransitions = tc; break;
                case WinUI.ContentControl cc: cc.ContentTransitions = tc; break;
            }
        }

        if (themeT?.ItemContainer is { } itemTransitions)
        {
            var tc = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
            foreach (var t in itemTransitions) tc.Add(t);
            if (uie is WinUI.ListViewBase lvb)
                lvb.ItemContainerTransitions = tc;
        }
    }

    /// <summary>
    /// Fallback: applies XAML-style implicit transitions via compositor ImplicitAnimationCollection
    /// when the XAML API is blocked by a prior GetElementVisual() call.
    /// </summary>
    private static void ApplyTransitionsViaCompositor(UIElement uie, ImplicitTransitions implicitT)
    {
        ElementPool.MarkCompositorTainted(uie);
        var visual = ElementCompositionPreview.GetElementVisual(uie);
        var compositor = visual.Compositor;
        var implicitAnimations = visual.ImplicitAnimations ?? compositor.CreateImplicitAnimationCollection();

        if (implicitT.Opacity is not null)
        {
            var dur = implicitT.Opacity.Duration;
            implicitAnimations["Opacity"] = AnimationHelper.CreateScalarImplicitAnimation(
                compositor, "Opacity", new Animation.LinearCurve(dur));
        }
        if (implicitT.Rotation is not null)
        {
            var dur = implicitT.Rotation.Duration;
            implicitAnimations["RotationAngle"] = AnimationHelper.CreateScalarImplicitAnimation(
                compositor, "RotationAngle", new Animation.LinearCurve(dur));
        }
        if (implicitT.Scale is not null)
        {
            var dur = implicitT.Scale.Duration;
            implicitAnimations["Scale"] = AnimationHelper.CreateVector3ImplicitAnimation(
                compositor, "Scale", new Animation.LinearCurve(dur));
        }
        if (implicitT.Translation is not null)
        {
            var dur = implicitT.Translation.Duration;
            implicitAnimations["Offset"] = AnimationHelper.CreateVector3ImplicitAnimation(
                compositor, "Offset", new Animation.LinearCurve(dur));
        }

        visual.ImplicitAnimations = implicitAnimations;
    }

    /// <summary>
    /// Sets up Composition-layer implicit animations on the element's Visual so that
    /// layout-driven Offset (and optionally Size) changes animate smoothly.
    /// Runs entirely on the Composition thread — zero managed-code callbacks during animation.
    /// </summary>
    internal static void ApplyLayoutAnimation(UIElement uie, LayoutAnimationConfig config)
    {
        ElementPool.MarkCompositorTainted(uie);
        var visual = ElementCompositionPreview.GetElementVisual(uie);
        var compositor = visual.Compositor;

        var implicitAnimations = compositor.CreateImplicitAnimationCollection();

        if (config.AnimateOffset)
        {
            if (config.UseSpring)
            {
                var spring = compositor.CreateSpringVector3Animation();
                spring.DampingRatio = config.DampingRatio;
                spring.Period = TimeSpan.FromSeconds(config.Period);
                spring.Target = "Offset";
                implicitAnimations["Offset"] = spring;
            }
            else
            {
                var anim = compositor.CreateVector3KeyFrameAnimation();
                anim.InsertExpressionKeyFrame(1.0f, "this.FinalValue");
                anim.Duration = config.Duration;
                anim.Target = "Offset";
                implicitAnimations["Offset"] = anim;
            }
        }

        if (config.AnimateSize)
        {
            if (config.UseSpring)
            {
                var spring = compositor.CreateSpringVector2Animation();
                spring.DampingRatio = config.DampingRatio;
                spring.Period = TimeSpan.FromSeconds(config.Period);
                spring.Target = "Size";
                implicitAnimations["Size"] = spring;
            }
            else
            {
                var anim = compositor.CreateVector2KeyFrameAnimation();
                anim.InsertExpressionKeyFrame(1.0f, "this.FinalValue");
                anim.Duration = config.Duration;
                anim.Target = "Size";
                implicitAnimations["Size"] = anim;
            }
        }

        visual.ImplicitAnimations = implicitAnimations;
    }

    /// <summary>
    /// Clears Composition-layer implicit animations from an element's Visual.
    /// Called when an element previously had LayoutAnimation but no longer does
    /// (e.g., after a state change removes the modifier, or a pooled control is reused).
    /// </summary>
    internal static void ClearLayoutAnimation(UIElement uie)
    {
        var visual = ElementCompositionPreview.GetElementVisual(uie);
        visual.ImplicitAnimations = null;
    }

    // ════════════════════════════════════════════════════════════════
    //  Property animation (.Animate() modifier)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates ImplicitAnimationCollection entries on the element's composition Visual
    /// for the targeted properties using the specified Curve. Merges with existing
    /// layout animation entries (Offset/Size) to avoid overwriting each other.
    /// </summary>
    internal static void ApplyPropertyAnimation(UIElement uie, AnimationConfig config, LayoutAnimationConfig? layoutConfig)
    {
        ElementPool.MarkCompositorTainted(uie);
        var visual = ElementCompositionPreview.GetElementVisual(uie);
        var compositor = visual.Compositor;

        // Start from existing implicit animations (layout may have set Offset/Size)
        // or create a new collection
        var implicitAnimations = visual.ImplicitAnimations ?? compositor.CreateImplicitAnimationCollection();

        var props = config.Properties;
        var curve = config.Curve;

        if (props.HasFlag(AnimateProperty.Opacity))
            implicitAnimations["Opacity"] = AnimationHelper.CreateScalarImplicitAnimation(compositor, "Opacity", curve);

        if (props.HasFlag(AnimateProperty.Offset))
        {
            // Only add Offset if layout animation hasn't already claimed it
            if (layoutConfig is null || !layoutConfig.AnimateOffset)
                implicitAnimations["Offset"] = AnimationHelper.CreateVector3ImplicitAnimation(compositor, "Offset", curve);
        }

        if (props.HasFlag(AnimateProperty.Scale))
            implicitAnimations["Scale"] = AnimationHelper.CreateVector3ImplicitAnimation(compositor, "Scale", curve);

        if (props.HasFlag(AnimateProperty.Rotation))
            implicitAnimations["RotationAngle"] = AnimationHelper.CreateScalarImplicitAnimation(compositor, "RotationAngle", curve);

        if (props.HasFlag(AnimateProperty.CenterPoint))
            implicitAnimations["CenterPoint"] = AnimationHelper.CreateVector3ImplicitAnimation(compositor, "CenterPoint", curve);

        visual.ImplicitAnimations = implicitAnimations;
    }

    /// <summary>
    /// Clears property animation entries from an element's Visual's ImplicitAnimationCollection.
    /// Preserves layout animation entries if they exist.
    /// </summary>
    internal static void ClearPropertyAnimation(UIElement uie, LayoutAnimationConfig? layoutConfig)
    {
        if (layoutConfig is null)
        {
            // No layout animation either — clear everything
            var visual = ElementCompositionPreview.GetElementVisual(uie);
            visual.ImplicitAnimations = null;
        }
        // If layout animation exists, ApplyLayoutAnimation will recreate the collection
        // with just layout entries on next update
    }

    // ════════════════════════════════════════════════════════════════
    //  Enter/exit transitions (.Transition() modifier)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies enter transition on mount: sets initial visual state then animates to final state.
    /// </summary>
    internal static void ApplyEnterTransition(UIElement uie, ElementTransition transition, int staggerIndex = 0, TimeSpan staggerDelay = default)
    {
        var enter = transition.GetEnterTransition();
        if (enter is null) return;
        ElementPool.MarkCompositorTainted(uie);

        var visual = ElementCompositionPreview.GetElementVisual(uie);
        var compositor = visual.Compositor;
        var curve = transition.Curve ?? Curve.Ease(300, Easing.Decelerate);

        // Override with ambient scope if present
        if (AnimationScope.HasScope && AnimationScope.Current is not null)
            curve = AnimationScope.Current;

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var delay = staggerIndex > 0 ? TimeSpan.FromTicks(staggerDelay.Ticks * staggerIndex) : TimeSpan.Zero;

        ApplyTransitionAnimations(uie, visual, compositor, enter, curve, isEnter: true, delay);

        batch.End();
    }

    /// <summary>
    /// Applies exit transition before unmount: animates out, then invokes onComplete to remove/pool.
    /// </summary>
    internal void ApplyExitTransition(UIElement uie, ElementTransition transition, Action onComplete, int staggerIndex = 0, TimeSpan staggerDelay = default)
    {
        var exit = transition.GetExitTransition();
        if (exit is null) { onComplete(); return; }
        ElementPool.MarkCompositorTainted(uie);

        var visual = ElementCompositionPreview.GetElementVisual(uie);
        var compositor = visual.Compositor;
        var curve = transition.Curve ?? Curve.Ease(300, Easing.Decelerate);

        if (AnimationScope.HasScope && AnimationScope.Current is not null)
            curve = AnimationScope.Current;

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var delay = staggerIndex > 0 ? TimeSpan.FromTicks(staggerDelay.Ticks * staggerIndex) : TimeSpan.Zero;

        ApplyTransitionAnimations(uie, visual, compositor, exit, curve, isEnter: false, delay);

        batch.End();
        batch.Completed += (_, _) =>
        {
            // Reset visual state after exit
            visual.Opacity = 1;
            visual.Offset = Vector3.Zero;
            visual.Scale = Vector3.One;
            onComplete();
        };
    }

    private static void ApplyTransitionAnimations(
        UIElement uie, Visual visual, Compositor compositor,
        Animation.Transition transition, Curve curve, bool isEnter, TimeSpan delay)
    {
        switch (transition)
        {
            case FadeTransition:
                ApplyFadeTransition(uie, compositor, curve, isEnter, delay);
                break;
            case SlideTransition slide:
                ApplySlideTransition(uie, visual, compositor, slide.Edge, curve, isEnter, delay);
                break;
            case ScaleTransition scale:
                ApplyScaleTransition(uie, visual, compositor, scale.From, curve, isEnter, delay);
                break;
            case CombinedTransition combined:
                ApplyTransitionAnimations(uie, visual, compositor, combined.First, curve, isEnter, delay);
                ApplyTransitionAnimations(uie, visual, compositor, combined.Second, curve, isEnter, delay);
                break;
            case AsymmetricTransition asym:
                var inner = isEnter ? asym.EnterTransition : asym.ExitTransition;
                if (inner is not null)
                    ApplyTransitionAnimations(uie, visual, compositor, inner, curve, isEnter, delay);
                break;
            case DirectionalTransition dir:
                var dirInner = isEnter ? dir.EnterTransition : dir.ExitTransition;
                if (dirInner is not null)
                    ApplyTransitionAnimations(uie, visual, compositor, dirInner, curve, isEnter, delay);
                break;
        }
    }

    private static void ApplyFadeTransition(UIElement uie, Compositor compositor, Curve curve, bool isEnter, TimeSpan delay)
    {
        var visual = ElementCompositionPreview.GetElementVisual(uie);
        if (isEnter)
        {
            visual.Opacity = 0;
            var anim = AnimationHelper.CreateScalarTargetAnimation(compositor, 1.0f, curve);
            if (delay > TimeSpan.Zero) AnimationHelper.SetDelay(anim, delay);
            visual.StartAnimation("Opacity", anim);
        }
        else
        {
            var anim = AnimationHelper.CreateScalarTargetAnimation(compositor, 0f, curve);
            if (delay > TimeSpan.Zero) AnimationHelper.SetDelay(anim, delay);
            visual.StartAnimation("Opacity", anim);
        }
    }

    private static void ApplySlideTransition(UIElement uie, Visual visual, Compositor compositor, Edge edge, Curve curve, bool isEnter, TimeSpan delay)
    {
        var slideDistance = 40f;
        var offset = edge switch
        {
            Edge.Left => new Vector3(-slideDistance, 0, 0),
            Edge.Top => new Vector3(0, -slideDistance, 0),
            Edge.Right => new Vector3(slideDistance, 0, 0),
            Edge.Bottom => new Vector3(0, slideDistance, 0),
            _ => new Vector3(0, slideDistance, 0),
        };

        if (isEnter)
        {
            visual.Offset = offset;
            var anim = AnimationHelper.CreateVector3TargetAnimation(compositor, Vector3.Zero, curve);
            if (delay > TimeSpan.Zero) AnimationHelper.SetDelay(anim, delay);
            visual.StartAnimation("Offset", anim);
        }
        else
        {
            var anim = AnimationHelper.CreateVector3TargetAnimation(compositor, offset, curve);
            if (delay > TimeSpan.Zero) AnimationHelper.SetDelay(anim, delay);
            visual.StartAnimation("Offset", anim);
        }
    }

    private static void ApplyScaleTransition(UIElement uie, Visual visual, Compositor compositor, float from, Curve curve, bool isEnter, TimeSpan delay)
    {
        if (isEnter)
        {
            visual.Scale = new Vector3(from, from, 1f);
            var anim = AnimationHelper.CreateVector3TargetAnimation(compositor, Vector3.One, curve);
            if (delay > TimeSpan.Zero) AnimationHelper.SetDelay(anim, delay);
            visual.StartAnimation("Scale", anim);
        }
        else
        {
            var anim = AnimationHelper.CreateVector3TargetAnimation(compositor, new Vector3(from, from, 1f), curve);
            if (delay > TimeSpan.Zero) AnimationHelper.SetDelay(anim, delay);
            visual.StartAnimation("Scale", anim);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Interaction states (.InteractionStates() modifier)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// State tracking for elements with InteractionStates — stores current state and cached animations.
    /// Uses a static dictionary keyed by UIElement.
    /// </summary>
    private static readonly Dictionary<UIElement, InteractionStateTracker> _interactionTrackers = new();

    private sealed class InteractionStateTracker
    {
        public InteractionState CurrentState;
        public InteractionStatesConfig Config = null!;
        public Brush? NormalBackground;
        public Brush? NormalForeground;
        public Brush? NormalBorderBrush;
    }

    private enum InteractionState { Normal, PointerOver, Pressed, Focused }

    /// <summary>
    /// Sets up or updates InteractionStates on an element. Registers pointer event handlers
    /// on first setup; updates cached config on subsequent calls.
    /// </summary>
    internal static void ApplyInteractionStates(UIElement uie, InteractionStatesConfig config)
    {
        ElementPool.MarkCompositorTainted(uie);
        if (!_interactionTrackers.TryGetValue(uie, out var tracker))
        {
            tracker = new InteractionStateTracker();
            _interactionTrackers[uie] = tracker;

            // Capture normal brush values
            if (uie is FrameworkElement fe)
            {
                tracker.NormalBackground = fe switch
                {
                    WinUI.Panel p => p.Background,
                    WinUI.Control c => c.Background,
                    WinUI.Border b => b.Background,
                    _ => null,
                };
                tracker.NormalForeground = fe switch
                {
                    WinUI.Control c => c.Foreground,
                    TextBlock tb => tb.Foreground,
                    _ => null,
                };
                tracker.NormalBorderBrush = fe switch
                {
                    WinUI.Control c => c.BorderBrush,
                    WinUI.Border b => b.BorderBrush,
                    _ => null,
                };
            }

            // Register handlers
            uie.PointerEntered += OnInteractionPointerEntered;
            uie.PointerExited += OnInteractionPointerExited;
            uie.PointerPressed += OnInteractionPointerPressed;
            uie.PointerReleased += OnInteractionPointerReleased;
            uie.PointerCaptureLost += OnInteractionPointerCaptureLost;
            uie.GotFocus += OnInteractionGotFocus;
            uie.LostFocus += OnInteractionLostFocus;
        }

        tracker.Config = config;
    }

    /// <summary>
    /// Removes InteractionStates from an element, unregistering handlers and clearing state.
    /// </summary>
    internal static void ClearInteractionStates(UIElement uie)
    {
        if (!_interactionTrackers.Remove(uie)) return;

        uie.PointerEntered -= OnInteractionPointerEntered;
        uie.PointerExited -= OnInteractionPointerExited;
        uie.PointerPressed -= OnInteractionPointerPressed;
        uie.PointerReleased -= OnInteractionPointerReleased;
        uie.PointerCaptureLost -= OnInteractionPointerCaptureLost;
        uie.GotFocus -= OnInteractionGotFocus;
        uie.LostFocus -= OnInteractionLostFocus;
    }

    private static void OnInteractionPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is UIElement uie && _interactionTrackers.TryGetValue(uie, out var tracker))
            TransitionToState(uie, tracker, InteractionState.PointerOver);
    }

    private static void OnInteractionPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is UIElement uie && _interactionTrackers.TryGetValue(uie, out var tracker))
            TransitionToState(uie, tracker, InteractionState.Normal);
    }

    private static void OnInteractionPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is UIElement uie && _interactionTrackers.TryGetValue(uie, out var tracker))
            TransitionToState(uie, tracker, InteractionState.Pressed);
    }

    private static void OnInteractionPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is UIElement uie && _interactionTrackers.TryGetValue(uie, out var tracker))
        {
            // Released goes back to PointerOver (pointer is still over the element)
            TransitionToState(uie, tracker, InteractionState.PointerOver);
        }
    }

    private static void OnInteractionPointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is UIElement uie && _interactionTrackers.TryGetValue(uie, out var tracker))
            TransitionToState(uie, tracker, InteractionState.Normal);
    }

    private static void OnInteractionGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement uie && _interactionTrackers.TryGetValue(uie, out var tracker)
            && tracker.Config.Focused is not null)
            TransitionToState(uie, tracker, InteractionState.Focused);
    }

    private static void OnInteractionLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement uie && _interactionTrackers.TryGetValue(uie, out var tracker))
            TransitionToState(uie, tracker, InteractionState.Normal);
    }

    private static void TransitionToState(UIElement uie, InteractionStateTracker tracker, InteractionState newState)
    {
        if (tracker.CurrentState == newState) return;
        tracker.CurrentState = newState;

        var config = tracker.Config;
        var curve = config.Curve ?? Curve.Ease(200, Easing.Standard);

        // Resolve effective state values (Pressed inherits from PointerOver)
        var values = newState switch
        {
            InteractionState.PointerOver => config.PointerOver,
            InteractionState.Pressed => MergePressed(config.PointerOver, config.Pressed),
            InteractionState.Focused => config.Focused,
            _ => null, // Normal
        };

        var visual = ElementCompositionPreview.GetElementVisual(uie);
        var compositor = visual.Compositor;

        // Compositor properties — animate via Visual
        var targetOpacity = values?.Opacity ?? 1.0f;
        var opacityAnim = AnimationHelper.CreateScalarTargetAnimation(compositor, targetOpacity, curve);
        visual.StartAnimation("Opacity", opacityAnim);

        if (values?.Scale.HasValue == true || values?.ScaleV.HasValue == true || newState == InteractionState.Normal)
        {
            var targetScale = values?.ScaleV ?? (values?.Scale.HasValue == true ? new Vector3(values.Scale.Value, values.Scale.Value, 1f) : Vector3.One);
            var scaleAnim = AnimationHelper.CreateVector3TargetAnimation(compositor, targetScale, curve);
            visual.StartAnimation("Scale", scaleAnim);
        }

        if (values?.Translation.HasValue == true || newState == InteractionState.Normal)
        {
            var targetTranslation = values?.Translation ?? Vector3.Zero;
            var translationAnim = AnimationHelper.CreateVector3TargetAnimation(compositor, targetTranslation, curve);
            visual.StartAnimation("Offset", translationAnim);
        }

        if (values?.Rotation.HasValue == true || newState == InteractionState.Normal)
        {
            var targetRotation = values?.Rotation ?? 0f;
            var rotationAnim = AnimationHelper.CreateScalarTargetAnimation(compositor, targetRotation, curve);
            visual.StartAnimation("RotationAngle", rotationAnim);
        }

        // Brush properties — direct set
        if (uie is FrameworkElement fe)
        {
            var bg = values?.Background ?? tracker.NormalBackground;
            if (bg is not null)
            {
                if (fe is WinUI.Panel p) p.Background = bg;
                else if (fe is WinUI.Control c) c.Background = bg;
                else if (fe is WinUI.Border b) b.Background = bg;
            }

            var fg = values?.Foreground ?? tracker.NormalForeground;
            if (fg is not null)
            {
                if (fe is WinUI.Control c) c.Foreground = fg;
                else if (fe is TextBlock tb) tb.Foreground = fg;
            }

            var bb = values?.BorderBrush ?? tracker.NormalBorderBrush;
            if (bb is not null)
            {
                if (fe is WinUI.Control c) c.BorderBrush = bb;
                else if (fe is WinUI.Border b) b.BorderBrush = bb;
            }
        }
    }

    private static InteractionStateValues? MergePressed(InteractionStateValues? pointerOver, InteractionStateValues? pressed)
    {
        if (pressed is null) return pointerOver;
        if (pointerOver is null) return pressed;

        // Pressed inherits unoverridden values from PointerOver
        return new InteractionStateValues(
            Opacity: pressed.Opacity ?? pointerOver.Opacity,
            Scale: pressed.Scale ?? pointerOver.Scale,
            ScaleV: pressed.ScaleV ?? pointerOver.ScaleV,
            Translation: pressed.Translation ?? pointerOver.Translation,
            Rotation: pressed.Rotation ?? pointerOver.Rotation,
            Background: pressed.Background ?? pointerOver.Background,
            Foreground: pressed.Foreground ?? pointerOver.Foreground,
            BorderBrush: pressed.BorderBrush ?? pointerOver.BorderBrush);
    }

    // ════════════════════════════════════════════════════════════════
    //  Stagger animation (DelayTime on child animations)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies stagger delays to an element's Visual's implicit animations.
    /// Called after layout/property animations are set up on children.
    /// </summary>
    internal static void ApplyStaggerDelays(UIElement parent, StaggerConfig config)
    {
        if (parent is not WinUI.Panel panel) return;

        for (int i = 0; i < panel.Children.Count; i++)
        {
            var child = panel.Children[i];
            var visual = ElementCompositionPreview.GetElementVisual(child);
            if (visual.ImplicitAnimations is null) continue;

            var delay = TimeSpan.FromTicks(config.Delay.Ticks * i);
            foreach (var key in new[] { "Offset", "Opacity", "Scale", "RotationAngle", "Size", "CenterPoint" })
            {
                try
                {
                    var anim = visual.ImplicitAnimations[key];
                    if (anim is KeyFrameAnimation kfa) kfa.DelayTime = delay;
                    else if (anim is SpringScalarNaturalMotionAnimation ssa) ssa.DelayTime = delay;
                    else if (anim is SpringVector3NaturalMotionAnimation sva) sva.DelayTime = delay;
                }
                catch { /* Key not present in collection — skip */ }
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Keyframe animation (.Keyframes() modifier)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tracks previous trigger values for keyframe animations, keyed by (UIElement, name).
    /// </summary>
    private static readonly Dictionary<(UIElement, string), object?> _keyframeTriggerValues = new();

    /// <summary>
    /// Checks trigger values and starts keyframe animations when they change.
    /// </summary>
    internal static void ApplyKeyframeAnimations(UIElement uie, KeyframeEntry[] entries)
    {
        ElementPool.MarkCompositorTainted(uie);
        var visual = ElementCompositionPreview.GetElementVisual(uie);
        var compositor = visual.Compositor;

        foreach (var entry in entries)
        {
            var key = (uie, entry.Name);
            var changed = false;

            if (_keyframeTriggerValues.TryGetValue(key, out var prevTrigger))
                changed = !Equals(prevTrigger, entry.Trigger);
            else
                changed = true; // First mount

            _keyframeTriggerValues[key] = entry.Trigger;

            if (!changed) continue;

            var def = entry.Definition;
            var group = compositor.CreateAnimationGroup();

            // Create per-property keyframe animations
            bool hasOpacity = false, hasScale = false, hasTranslation = false, hasRotation = false;
            foreach (var kf in def.Keyframes)
            {
                if (kf.Opacity.HasValue) hasOpacity = true;
                if (kf.Scale.HasValue) hasScale = true;
                if (kf.Translation.HasValue) hasTranslation = true;
                if (kf.Rotation.HasValue) hasRotation = true;
            }

            if (hasOpacity)
            {
                var anim = compositor.CreateScalarKeyFrameAnimation();
                anim.Duration = def.Duration;
                anim.Target = "Opacity";
                if (def.Loop) anim.IterationBehavior = AnimationIterationBehavior.Forever;
                foreach (var kf in def.Keyframes)
                {
                    if (!kf.Opacity.HasValue) continue;
                    if (kf.Easing.HasValue)
                    {
                        var e = kf.Easing.Value;
                        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(e.X1, e.Y1), new Vector2(e.X2, e.Y2));
                        anim.InsertKeyFrame(kf.Progress, kf.Opacity.Value, easing);
                    }
                    else
                        anim.InsertKeyFrame(kf.Progress, kf.Opacity.Value);
                }
                group.Add(anim);
            }

            if (hasScale)
            {
                var anim = compositor.CreateVector3KeyFrameAnimation();
                anim.Duration = def.Duration;
                anim.Target = "Scale";
                if (def.Loop) anim.IterationBehavior = AnimationIterationBehavior.Forever;
                foreach (var kf in def.Keyframes)
                {
                    if (!kf.Scale.HasValue) continue;
                    if (kf.Easing.HasValue)
                    {
                        var e = kf.Easing.Value;
                        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(e.X1, e.Y1), new Vector2(e.X2, e.Y2));
                        anim.InsertKeyFrame(kf.Progress, kf.Scale.Value, easing);
                    }
                    else
                        anim.InsertKeyFrame(kf.Progress, kf.Scale.Value);
                }
                group.Add(anim);
            }

            if (hasTranslation)
            {
                var anim = compositor.CreateVector3KeyFrameAnimation();
                anim.Duration = def.Duration;
                anim.Target = "Offset";
                if (def.Loop) anim.IterationBehavior = AnimationIterationBehavior.Forever;
                foreach (var kf in def.Keyframes)
                {
                    if (!kf.Translation.HasValue) continue;
                    if (kf.Easing.HasValue)
                    {
                        var e = kf.Easing.Value;
                        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(e.X1, e.Y1), new Vector2(e.X2, e.Y2));
                        anim.InsertKeyFrame(kf.Progress, kf.Translation.Value, easing);
                    }
                    else
                        anim.InsertKeyFrame(kf.Progress, kf.Translation.Value);
                }
                group.Add(anim);
            }

            if (hasRotation)
            {
                var anim = compositor.CreateScalarKeyFrameAnimation();
                anim.Duration = def.Duration;
                anim.Target = "RotationAngle";
                if (def.Loop) anim.IterationBehavior = AnimationIterationBehavior.Forever;
                foreach (var kf in def.Keyframes)
                {
                    if (!kf.Rotation.HasValue) continue;
                    if (kf.Easing.HasValue)
                    {
                        var e = kf.Easing.Value;
                        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(e.X1, e.Y1), new Vector2(e.X2, e.Y2));
                        anim.InsertKeyFrame(kf.Progress, kf.Rotation.Value, easing);
                    }
                    else
                        anim.InsertKeyFrame(kf.Progress, kf.Rotation.Value);
                }
                group.Add(anim);
            }

            // Start the animation group via Visual
            foreach (CompositionAnimation anim in group)
                visual.StartAnimation(anim.Target, anim);
        }
    }

    internal static void ClearKeyframeAnimations(UIElement uie, KeyframeEntry[] entries)
    {
        foreach (var entry in entries)
            _keyframeTriggerValues.Remove((uie, entry.Name));

        // Stop composition animations so they don't keep running on pooled/unmounted controls.
        // We stop all four possible targets since KeyframeAnimationDef doesn't track which
        // targets were started — StopAnimation is a no-op if no animation is running on that property.
        try
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(uie);
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Scale");
            visual.StopAnimation("Offset");
            visual.StopAnimation("RotationAngle");
        }
        catch { /* No compositor (e.g. unit tests) */ }
    }

    // ════════════════════════════════════════════════════════════════
    //  Scroll-linked expression animation (.ScrollLinked() modifier)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies scroll-linked expression animations to an element's Visual.
    /// </summary>
    internal static void ApplyScrollAnimation(UIElement uie, ScrollAnimationConfig config)
    {
        if (config.ScrollViewer is null) return;
        ElementPool.MarkCompositorTainted(uie);
        var visual = ElementCompositionPreview.GetElementVisual(uie);
        var compositor = visual.Compositor;
        var scrollPropertySet = ElementCompositionPreview.GetScrollViewerManipulationPropertySet(config.ScrollViewer);

        foreach (var expr in config.Expressions)
        {
            var animation = compositor.CreateExpressionAnimation(expr.Expression);
            animation.SetReferenceParameter("scroll", scrollPropertySet);
            visual.StartAnimation(expr.Property, animation);
        }
    }

    internal static void ClearScrollAnimation(UIElement uie, ScrollAnimationConfig config)
    {
        var visual = ElementCompositionPreview.GetElementVisual(uie);
        foreach (var expr in config.Expressions)
            visual.StopAnimation(expr.Property);
    }

    // ════════════════════════════════════════════════════════════════
    //  Connected animations (cross-container transitions)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Queues a connected animation start for an element that was just mounted.
    /// Called from Mount() when the element has a ConnectedAnimationKey and a
    /// prepared animation exists with that key.
    /// </summary>
    internal void QueueConnectedAnimationStart(UIElement target, string key)
    {
        try
        {
            var service = ConnectedAnimationService.GetForCurrentView();
            var anim = service.GetAnimation(key);
            if (anim is not null)
                _pendingConnectedAnimationStarts.Add((anim, target));
        }
        catch (global::System.Runtime.InteropServices.COMException ex) when (Diagnostics.HResults.IsTeardownReentry(ex.HResult)) { }
    }

    /// <summary>
    /// Starts all queued connected animations. Call AFTER the new tree has been
    /// attached to the visual tree (e.g., after Window.Content = newControl).
    /// </summary>
    public void FlushConnectedAnimations()
    {
        if (_pendingConnectedAnimationStarts.Count == 0) return;

        foreach (var (anim, target) in _pendingConnectedAnimationStarts)
        {
            try { anim.TryStart(target); }
            catch (global::System.Runtime.InteropServices.COMException ex) when (Diagnostics.HResults.IsTeardownReentry(ex.HResult)) { }
        }
        _pendingConnectedAnimationStarts.Clear();
    }

    internal void ApplyModifiers(FrameworkElement fe, ElementModifiers m, Action requestRerender)
        => ApplyModifiers(fe, null, m, requestRerender);

    internal void ApplyModifiers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m, Action requestRerender)
    {
        // Guard each property: only call into WinUI when the value actually changed.
        // Each WinUI property set is a managed→native interop call, so avoiding
        // unnecessary sets is critical for large element counts.

        // RequestedTheme must be set BEFORE ApplyThemeBindings so that ThemeRef
        // bindings resolve against the correct theme variant.
        if (m.RequestedTheme.HasValue && m.RequestedTheme != oldM?.RequestedTheme)
            fe.RequestedTheme = m.RequestedTheme.Value;
        else if (!m.RequestedTheme.HasValue && oldM?.RequestedTheme.HasValue == true)
            fe.ClearValue(FrameworkElement.RequestedThemeProperty);

        // Apply physical margin, then overlay logical (BiDi-aware) inline margin
        var resolvedMargin = m.Margin ?? oldM?.Margin;
        if (m.MarginInlineStart.HasValue || m.MarginInlineEnd.HasValue)
        {
            var isRtl = fe.FlowDirection == FlowDirection.RightToLeft;
            var baseMargin = resolvedMargin ?? fe.Margin;
            var left = isRtl ? (m.MarginInlineEnd ?? baseMargin.Left) : (m.MarginInlineStart ?? baseMargin.Left);
            var right = isRtl ? (m.MarginInlineStart ?? baseMargin.Right) : (m.MarginInlineEnd ?? baseMargin.Right);
            resolvedMargin = new Thickness(left, baseMargin.Top, right, baseMargin.Bottom);
        }
        if (resolvedMargin.HasValue && resolvedMargin != oldM?.Margin) fe.Margin = resolvedMargin.Value;
        else if (!resolvedMargin.HasValue && oldM?.Margin.HasValue == true) fe.Margin = new Thickness(0);

        // Apply physical padding, then overlay logical (BiDi-aware) inline padding
        var resolvedPadding = m.Padding ?? oldM?.Padding;
        if (m.PaddingInlineStart.HasValue || m.PaddingInlineEnd.HasValue)
        {
            var isRtl = fe.FlowDirection == FlowDirection.RightToLeft;
            var basePad = resolvedPadding ?? (fe is WinUI.Control pc ? pc.Padding : fe is WinUI.Border pb ? pb.Padding : fe is WinUI.StackPanel psp ? psp.Padding : new Thickness());
            var left = isRtl ? (m.PaddingInlineEnd ?? basePad.Left) : (m.PaddingInlineStart ?? basePad.Left);
            var right = isRtl ? (m.PaddingInlineStart ?? basePad.Right) : (m.PaddingInlineEnd ?? basePad.Right);
            resolvedPadding = new Thickness(left, basePad.Top, right, basePad.Bottom);
        }
        if (resolvedPadding.HasValue && resolvedPadding != oldM?.Padding)
        {
            if (fe is WinUI.Control padCtrl) padCtrl.Padding = resolvedPadding.Value;
            else if (fe is WinUI.Border padBdr) padBdr.Padding = resolvedPadding.Value;
            else if (fe is WinUI.StackPanel padSp) padSp.Padding = resolvedPadding.Value;
        }
        else if (!resolvedPadding.HasValue && oldM?.Padding.HasValue == true)
        {
            if (fe is WinUI.Control padCtrl) padCtrl.Padding = new Thickness(0);
            else if (fe is WinUI.Border padBdr) padBdr.Padding = new Thickness(0);
            else if (fe is WinUI.StackPanel padSp) padSp.Padding = new Thickness(0);
        }
        if (m.Width.HasValue && m.Width != oldM?.Width) fe.Width = m.Width.Value;
        else if (!m.Width.HasValue && oldM?.Width.HasValue == true) fe.Width = double.NaN;
        if (m.Height.HasValue && m.Height != oldM?.Height) fe.Height = m.Height.Value;
        else if (!m.Height.HasValue && oldM?.Height.HasValue == true) fe.Height = double.NaN;
        if (m.MinWidth.HasValue && m.MinWidth != oldM?.MinWidth) fe.MinWidth = m.MinWidth.Value;
        else if (!m.MinWidth.HasValue && oldM?.MinWidth.HasValue == true) fe.MinWidth = 0;
        if (m.MinHeight.HasValue && m.MinHeight != oldM?.MinHeight) fe.MinHeight = m.MinHeight.Value;
        else if (!m.MinHeight.HasValue && oldM?.MinHeight.HasValue == true) fe.MinHeight = 0;
        if (m.MaxWidth.HasValue && m.MaxWidth != oldM?.MaxWidth) fe.MaxWidth = m.MaxWidth.Value;
        else if (!m.MaxWidth.HasValue && oldM?.MaxWidth.HasValue == true) fe.MaxWidth = double.PositiveInfinity;
        if (m.MaxHeight.HasValue && m.MaxHeight != oldM?.MaxHeight) fe.MaxHeight = m.MaxHeight.Value;
        else if (!m.MaxHeight.HasValue && oldM?.MaxHeight.HasValue == true) fe.MaxHeight = double.PositiveInfinity;
        if (m.HorizontalAlignment.HasValue && m.HorizontalAlignment != oldM?.HorizontalAlignment) fe.HorizontalAlignment = m.HorizontalAlignment.Value;
        else if (!m.HorizontalAlignment.HasValue && oldM?.HorizontalAlignment.HasValue == true) fe.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (m.VerticalAlignment.HasValue && m.VerticalAlignment != oldM?.VerticalAlignment) fe.VerticalAlignment = m.VerticalAlignment.Value;
        else if (!m.VerticalAlignment.HasValue && oldM?.VerticalAlignment.HasValue == true) fe.VerticalAlignment = VerticalAlignment.Stretch;
        if (m.Opacity.HasValue && m.Opacity != oldM?.Opacity)
            AnimationHelper.SetOrAnimate(fe, "Opacity", (float)m.Opacity.Value);
        else if (!m.Opacity.HasValue && oldM?.Opacity.HasValue == true)
            fe.Opacity = 1.0;
        if (m.Scale.HasValue && m.Scale != oldM?.Scale)
            AnimationHelper.SetOrAnimateVector3(fe, "Scale", m.Scale.Value);
        if (m.Rotation.HasValue && m.Rotation != oldM?.Rotation)
            AnimationHelper.SetOrAnimate(fe, "Rotation", m.Rotation.Value);
        if (m.Translation.HasValue && m.Translation != oldM?.Translation)
            AnimationHelper.SetOrAnimateVector3(fe, "Translation", m.Translation.Value);
        if (m.CenterPoint.HasValue && m.CenterPoint != oldM?.CenterPoint)
            AnimationHelper.SetOrAnimateVector3(fe, "CenterPoint", m.CenterPoint.Value);
        if (m.IsVisible.HasValue && m.IsVisible != oldM?.IsVisible)
            fe.Visibility = m.IsVisible.Value ? Visibility.Visible : Visibility.Collapsed;
        else if (!m.IsVisible.HasValue && oldM?.IsVisible.HasValue == true)
            fe.Visibility = Visibility.Visible;
        if (m.RichToolTip is not null)
        {
            var oldTipEl = oldM?.RichToolTip;
            var existingTip = WinUI.ToolTipService.GetToolTip(fe) as UIElement;
            if (oldTipEl is not null && existingTip is not null && CanUpdate(oldTipEl, m.RichToolTip))
            {
                var replacement = Update(oldTipEl, m.RichToolTip, existingTip, requestRerender);
                if (replacement is not null)
                    WinUI.ToolTipService.SetToolTip(fe, replacement);
            }
            else
            {
                WinUI.ToolTipService.SetToolTip(fe, Mount(m.RichToolTip, requestRerender));
            }
        }
        else if (m.ToolTip is not null && m.ToolTip != oldM?.ToolTip)
            WinUI.ToolTipService.SetToolTip(fe, m.ToolTip);
        else if (m.RichToolTip is null && m.ToolTip is null && (oldM?.RichToolTip is not null || oldM?.ToolTip is not null))
            fe.ClearValue(WinUI.ToolTipService.ToolTipProperty);

        if (m.AttachedFlyout is not null)
            ApplyFlyoutAttachment(fe, oldM?.AttachedFlyout, m.AttachedFlyout, requestRerender);

        if (m.ContextFlyout is not null)
        {
            var oldContextEl = oldM?.ContextFlyout;
            if (oldContextEl is not null && fe.ContextFlyout is WinPrim.FlyoutBase existingCtx)
                UpdateFlyoutInPlace(existingCtx, oldContextEl, m.ContextFlyout, requestRerender);
            else
                fe.ContextFlyout = CreateFlyoutFromElement(m.ContextFlyout, requestRerender);
        }
        else if (oldM?.ContextFlyout is not null)
            fe.ContextFlyout = null;

        // IsEnabled (on Control)
        if (m.IsEnabled.HasValue && m.IsEnabled != oldM?.IsEnabled && fe is WinUI.Control enCtrl)
            enCtrl.IsEnabled = m.IsEnabled.Value;
        else if (!m.IsEnabled.HasValue && oldM?.IsEnabled.HasValue == true && fe is WinUI.Control enCtrl2)
            enCtrl2.IsEnabled = true;

        // CornerRadius (on Control and Border)
        if (m.CornerRadius.HasValue && m.CornerRadius != oldM?.CornerRadius)
        {
            if (fe is WinUI.Control crCtrl) crCtrl.CornerRadius = m.CornerRadius.Value;
            else if (fe is WinUI.Border crBdr) crBdr.CornerRadius = m.CornerRadius.Value;
        }
        else if (!m.CornerRadius.HasValue && oldM?.CornerRadius.HasValue == true)
        {
            if (fe is WinUI.Control crCtrl) crCtrl.CornerRadius = new CornerRadius(0);
            else if (fe is WinUI.Border crBdr) crBdr.CornerRadius = new CornerRadius(0);
        }

        // BorderBrush / BorderThickness (on Control and Border)
        if (m.BorderBrush is not null && !ReferenceEquals(m.BorderBrush, oldM?.BorderBrush))
        {
            if (fe is WinUI.Control bbCtrl) bbCtrl.BorderBrush = m.BorderBrush;
            else if (fe is WinUI.Border bbBdr) bbBdr.BorderBrush = m.BorderBrush;
        }
        else if (m.BorderBrush is null && oldM?.BorderBrush is not null)
        {
            if (fe is WinUI.Control bbCtrl) bbCtrl.ClearValue(WinUI.Control.BorderBrushProperty);
            else if (fe is WinUI.Border bbBdr) bbBdr.ClearValue(WinUI.Border.BorderBrushProperty);
        }
        // Apply physical border thickness, then overlay logical (BiDi-aware) inline border
        var resolvedBorder = m.BorderThickness;
        if (m.BorderInlineStart.HasValue)
        {
            var isRtl = fe.FlowDirection == FlowDirection.RightToLeft;
            var baseBorder = resolvedBorder ?? (fe is WinUI.Control bc ? bc.BorderThickness : fe is WinUI.Border bb ? bb.BorderThickness : new Thickness());
            var inlineStartThickness = m.BorderInlineStart.Value;
            if (isRtl)
                resolvedBorder = new Thickness(baseBorder.Left, baseBorder.Top, inlineStartThickness.Left, baseBorder.Bottom);
            else
                resolvedBorder = new Thickness(inlineStartThickness.Left, baseBorder.Top, baseBorder.Right, baseBorder.Bottom);
        }
        if (resolvedBorder.HasValue && resolvedBorder != oldM?.BorderThickness)
        {
            if (fe is WinUI.Control btCtrl) btCtrl.BorderThickness = resolvedBorder.Value;
            else if (fe is WinUI.Border btBdr) btBdr.BorderThickness = resolvedBorder.Value;
        }
        else if (!resolvedBorder.HasValue && oldM?.BorderThickness.HasValue == true)
        {
            if (fe is WinUI.Control btCtrl) btCtrl.BorderThickness = new Thickness(0);
            else if (fe is WinUI.Border btBdr) btBdr.BorderThickness = new Thickness(0);
        }

        // Background (Panel, Control, or Border)
        if (m.Background is not null && !ReferenceEquals(m.Background, oldM?.Background))
        {
            if (fe is WinUI.Panel panel) panel.Background = m.Background;
            else if (fe is WinUI.Control ctrl2) ctrl2.Background = m.Background;
            else if (fe is WinUI.Border bdr) bdr.Background = m.Background;
        }
        else if (m.Background is null && oldM?.Background is not null)
        {
            if (fe is WinUI.Panel panel) panel.ClearValue(WinUI.Panel.BackgroundProperty);
            else if (fe is WinUI.Control ctrl2) ctrl2.ClearValue(WinUI.Control.BackgroundProperty);
            else if (fe is WinUI.Border bdr) bdr.ClearValue(WinUI.Border.BackgroundProperty);
        }

        // Foreground (Control or TextBlock)
        if (m.Foreground is not null && !ReferenceEquals(m.Foreground, oldM?.Foreground))
        {
            if (fe is WinUI.Control fgCtrl) fgCtrl.Foreground = m.Foreground;
            else if (fe is TextBlock fgTb) fgTb.Foreground = m.Foreground;
        }
        else if (m.Foreground is null && oldM?.Foreground is not null)
        {
            if (fe is WinUI.Control fgCtrl) fgCtrl.ClearValue(WinUI.Control.ForegroundProperty);
            else if (fe is TextBlock fgTb) fgTb.ClearValue(TextBlock.ForegroundProperty);
        }

        // AutomationProperties.Name
        if (m.AutomationName is not null && m.AutomationName != oldM?.AutomationName)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fe, m.AutomationName);
        else if (m.AutomationName is null && oldM?.AutomationName is not null)
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty);

        // AutomationProperties.AutomationId
        if (m.AutomationId is not null && m.AutomationId != oldM?.AutomationId)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(fe, m.AutomationId);
        else if (m.AutomationId is null && oldM?.AutomationId is not null)
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.AutomationIdProperty);

        // ElementSoundMode (on Control, not FrameworkElement)
        if (m.ElementSoundMode.HasValue && m.ElementSoundMode != oldM?.ElementSoundMode && fe is WinUI.Control ctrl)
            ctrl.ElementSoundMode = m.ElementSoundMode.Value;

        // ── Accessibility — Tier 1 (inline properties) ──────────────
        if (m.HeadingLevel.HasValue && m.HeadingLevel != oldM?.HeadingLevel)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHeadingLevel(fe, m.HeadingLevel.Value);

        if (m.IsTabStop.HasValue && m.IsTabStop != oldM?.IsTabStop)
            fe.IsTabStop = m.IsTabStop.Value;

        if (m.TabIndex.HasValue && m.TabIndex != oldM?.TabIndex && fe is WinUI.Control tabIdxCtrl)
            tabIdxCtrl.TabIndex = m.TabIndex.Value;

        if (m.AccessKey is not null && m.AccessKey != oldM?.AccessKey)
            fe.AccessKey = m.AccessKey;
        else if (m.AccessKey is null && oldM?.AccessKey is not null)
            fe.AccessKey = "";

        if (m.XYFocusKeyboardNavigation.HasValue && m.XYFocusKeyboardNavigation != oldM?.XYFocusKeyboardNavigation)
            fe.XYFocusKeyboardNavigation = m.XYFocusKeyboardNavigation.Value;

        // ── Accessibility — Tier 2/3 (lazy sub-record) ─────────────
        var a11y = m.Accessibility;
        var oldA11y = oldM?.Accessibility;
        if (a11y is not null || oldA11y is not null)
            ApplyAccessibilityModifiers(fe, oldA11y, a11y);

        // ── Typography (FontFamily, FontSize, FontWeight) ──────────
        if (m.FontFamily is not null && !ReferenceEquals(m.FontFamily, oldM?.FontFamily))
        {
            if (fe is WinUI.Control ffCtrl) ffCtrl.FontFamily = m.FontFamily;
            else if (fe is TextBlock ffTb) ffTb.FontFamily = m.FontFamily;
        }
        else if (m.FontFamily is null && oldM?.FontFamily is not null)
        {
            if (fe is WinUI.Control ffCtrl) ffCtrl.ClearValue(WinUI.Control.FontFamilyProperty);
            else if (fe is TextBlock ffTb) ffTb.ClearValue(TextBlock.FontFamilyProperty);
        }
        if (m.FontSize.HasValue && m.FontSize != oldM?.FontSize)
        {
            if (fe is WinUI.Control fsCtrl) fsCtrl.FontSize = m.FontSize.Value;
            else if (fe is TextBlock fsTb) fsTb.FontSize = m.FontSize.Value;
        }
        else if (!m.FontSize.HasValue && oldM?.FontSize.HasValue == true)
        {
            if (fe is WinUI.Control fsCtrl) fsCtrl.ClearValue(WinUI.Control.FontSizeProperty);
            else if (fe is TextBlock fsTb) fsTb.ClearValue(TextBlock.FontSizeProperty);
        }
        if (m.FontWeight.HasValue && m.FontWeight != oldM?.FontWeight)
        {
            if (fe is WinUI.Control fwCtrl) fwCtrl.FontWeight = m.FontWeight.Value;
            else if (fe is TextBlock fwTb) fwTb.FontWeight = m.FontWeight.Value;
        }
        else if (!m.FontWeight.HasValue && oldM?.FontWeight.HasValue == true)
        {
            if (fe is WinUI.Control fwCtrl) fwCtrl.ClearValue(WinUI.Control.FontWeightProperty);
            else if (fe is TextBlock fwTb) fwTb.ClearValue(TextBlock.FontWeightProperty);
        }

        // ── Declarative event handlers ────────────────────────────
        // Detach previous handler (if any) before attaching new one.
        // Handlers are stored in Tag via a wrapper so we can find them for detach.
        ApplyEventHandlers(fe, oldM, m);

        // Gesture recognizers (.OnPan / .OnPinch / .OnRotate)
        ApplyGestureHandlers(fe, oldM, m);

        // Drag-and-drop (.OnDragStart / .OnDrop / .OnDragEnter / .OnDragOver / .OnDragLeave)
        ApplyDragDropHandlers(fe, oldM, m);

        // OnMountAction — only run on initial mount (oldM is null)
        if (m.OnMountAction is not null && oldM is null)
            m.OnMountAction(fe);

        // Element ref — populate on mount/update so imperative APIs (FocusManager.Focus)
        // can target the mounted control. Writing on every update is cheap (single field
        // write) and keeps the ref fresh when the pool recycles elements.
        if (m.Ref is not null)
        {
            m.Ref._current = fe;
            AssertTypedRefMatch(m.Ref, fe);
        }
    }

    [global::System.Diagnostics.Conditional("DEBUG")]
    private static void AssertTypedRefMatch(Microsoft.UI.Reactor.Input.ElementRef r, FrameworkElement fe)
    {
        // Spec 033 §3 — typed refs (ElementRef<T>) record their expected concrete
        // type on the inner untyped ref. When a typed ref is bound to an element
        // that is not assignable to T, fail loudly under DEBUG so the bug is caught
        // during dev-loop. RELEASE builds keep the silent-null behavior of the
        // typed wrapper's `Current` property — no perf cost on the hot path.
        var expected = r.ExpectedType;
        if (expected is null) return;
        if (!expected.IsInstanceOfType(fe))
        {
            global::System.Diagnostics.Debug.Fail(
                $"ElementRef<{expected.Name}> attached to a {fe.GetType().Name}. " +
                "Use ElementRef<" + fe.GetType().Name + "> or untyped ElementRef.");
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Declarative event handler management
    // ════════════════════════════════════════════════════════════════
    //  Accessibility modifiers (Tier 2/3 sub-record)
    // ════════════════════════════════════════════════════════════════

    private static void ApplyAccessibilityModifiers(FrameworkElement fe, AccessibilityModifiers? oldA, AccessibilityModifiers? a)
    {
        if (a is null) return;

        if (a.HelpText is not null && a.HelpText != oldA?.HelpText)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(fe, a.HelpText);

        if (a.FullDescription is not null && a.FullDescription != oldA?.FullDescription)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetFullDescription(fe, a.FullDescription);

        if (a.LandmarkType.HasValue && a.LandmarkType != oldA?.LandmarkType)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetLandmarkType(fe, a.LandmarkType.Value);

        if (a.AccessibilityView.HasValue && a.AccessibilityView != oldA?.AccessibilityView)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(fe, a.AccessibilityView.Value);

        if (a.IsRequiredForForm.HasValue && a.IsRequiredForForm != oldA?.IsRequiredForForm)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetIsRequiredForForm(fe, a.IsRequiredForForm.Value);

        if (a.LiveSetting.HasValue && a.LiveSetting != oldA?.LiveSetting)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(fe, a.LiveSetting.Value);

        if (a.PositionInSet.HasValue && a.PositionInSet != oldA?.PositionInSet)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetPositionInSet(fe, a.PositionInSet.Value);

        if (a.SizeOfSet.HasValue && a.SizeOfSet != oldA?.SizeOfSet)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetSizeOfSet(fe, a.SizeOfSet.Value);

        if (a.Level.HasValue && a.Level != oldA?.Level)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetLevel(fe, a.Level.Value);

        if (a.ItemStatus is not null && a.ItemStatus != oldA?.ItemStatus)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(fe, a.ItemStatus);

        if (a.TabFocusNavigation.HasValue && a.TabFocusNavigation != oldA?.TabFocusNavigation)
            fe.TabFocusNavigation = a.TabFocusNavigation.Value;

        // LabeledBy — resolve AutomationId string to the target element in the visual tree.
        // During mount the element may not be in the visual tree yet (XamlRoot is null),
        // so defer resolution to the Loaded event if needed.
        if (a.LabeledBy is not null && a.LabeledBy != oldA?.LabeledBy)
        {
            var target = FindByAutomationId(fe, a.LabeledBy);
            if (target is not null)
            {
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetLabeledBy(fe, target);
            }
            else
            {
                // Element not yet in visual tree — defer until Loaded.
                var labelId = a.LabeledBy;
                void OnLoaded(object sender, RoutedEventArgs _)
                {
                    fe.Loaded -= OnLoaded;
                    var deferred = FindByAutomationId(fe, labelId);
                    if (deferred is not null)
                        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLabeledBy(fe, deferred);
                }
                fe.Loaded += OnLoaded;
            }
        }
        else if (a.LabeledBy is null && oldA?.LabeledBy is not null)
        {
            fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.LabeledByProperty);
        }
    }

    /// <summary>
    /// Walks the visual tree from <paramref name="element"/>'s XamlRoot to find
    /// the first UIElement whose AutomationProperties.AutomationId matches <paramref name="automationId"/>.
    /// </summary>
    private static UIElement? FindByAutomationId(FrameworkElement element, string automationId)
    {
        var root = element.XamlRoot?.Content;
        if (root is null) return null;
        return WalkForAutomationId(root, automationId);
    }

    private static UIElement? WalkForAutomationId(DependencyObject node, string automationId)
    {
        if (node is UIElement uie
            && Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(uie) == automationId)
            return uie;

        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i);
            var found = WalkForAutomationId(child, automationId);
            if (found is not null) return found;
        }
        return null;
    }

    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tracks the currently-attached event handlers on a FrameworkElement so they
    /// can be detached before new ones are attached. Stored as the element's Tag
    /// (or alongside it in a wrapper if Tag is already used for pool identity).
    /// </summary>
    /// <summary>
    /// Per-element handler state. Holds the <b>current</b> user delegate for each event
    /// plus a bit tracking whether the stable trampoline has been attached yet. The
    /// trampoline reads from the mutable <c>Current*</c> field when it fires, so updating
    /// a handler just swaps the field — no WinUI subscribe/unsubscribe churn.
    /// </summary>
    internal sealed class ModifierEventHandlerState
    {
        // Current user handlers (mutable; null means "no-op")
        public Action<object, SizeChangedEventArgs>? CurrentSizeChanged;
        public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? CurrentPointerPressed;
        public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? CurrentPointerMoved;
        public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? CurrentPointerReleased;
        public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? CurrentPointerEntered;
        public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? CurrentPointerExited;
        public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? CurrentPointerCanceled;
        public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? CurrentPointerCaptureLost;
        public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? CurrentPointerWheelChanged;
        public Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs>? CurrentTapped;
        public Action<object, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs>? CurrentDoubleTapped;
        public Action<object, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs>? CurrentRightTapped;
        public Action<object, Microsoft.UI.Xaml.Input.HoldingRoutedEventArgs>? CurrentHolding;
        public Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? CurrentKeyDown;
        public Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? CurrentKeyUp;
        public Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? CurrentPreviewKeyDown;
        public Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? CurrentPreviewKeyUp;
        public Action<UIElement, Microsoft.UI.Xaml.Input.CharacterReceivedRoutedEventArgs>? CurrentCharacterReceived;
        public Action<object, RoutedEventArgs>? CurrentGotFocus;
        public Action<object, RoutedEventArgs>? CurrentLostFocus;
        public Action<UIElement, Microsoft.UI.Xaml.Input.AccessKeyDisplayRequestedEventArgs>? CurrentAccessKeyDisplayRequested;

        // Stable trampoline delegates — captured for reference-equality detach (never used)
        // and to prevent GC collection of the compiler-generated closure.
        public SizeChangedEventHandler? SizeChangedTrampoline;
        public Microsoft.UI.Xaml.Input.PointerEventHandler? PointerPressedTrampoline;
        public Microsoft.UI.Xaml.Input.PointerEventHandler? PointerMovedTrampoline;
        public Microsoft.UI.Xaml.Input.PointerEventHandler? PointerReleasedTrampoline;
        public Microsoft.UI.Xaml.Input.PointerEventHandler? PointerEnteredTrampoline;
        public Microsoft.UI.Xaml.Input.PointerEventHandler? PointerExitedTrampoline;
        public Microsoft.UI.Xaml.Input.PointerEventHandler? PointerCanceledTrampoline;
        public Microsoft.UI.Xaml.Input.PointerEventHandler? PointerCaptureLostTrampoline;
        public Microsoft.UI.Xaml.Input.PointerEventHandler? PointerWheelChangedTrampoline;
        public Microsoft.UI.Xaml.Input.TappedEventHandler? TappedTrampoline;
        public Microsoft.UI.Xaml.Input.DoubleTappedEventHandler? DoubleTappedTrampoline;
        public Microsoft.UI.Xaml.Input.RightTappedEventHandler? RightTappedTrampoline;
        public Microsoft.UI.Xaml.Input.HoldingEventHandler? HoldingTrampoline;
        public Microsoft.UI.Xaml.Input.KeyEventHandler? KeyDownTrampoline;
        public Microsoft.UI.Xaml.Input.KeyEventHandler? KeyUpTrampoline;
        public Microsoft.UI.Xaml.Input.KeyEventHandler? PreviewKeyDownTrampoline;
        public Microsoft.UI.Xaml.Input.KeyEventHandler? PreviewKeyUpTrampoline;
        public global::Windows.Foundation.TypedEventHandler<UIElement, Microsoft.UI.Xaml.Input.CharacterReceivedRoutedEventArgs>? CharacterReceivedTrampoline;
        public RoutedEventHandler? GotFocusTrampoline;
        public RoutedEventHandler? LostFocusTrampoline;
        public global::Windows.Foundation.TypedEventHandler<UIElement, Microsoft.UI.Xaml.Input.AccessKeyDisplayRequestedEventArgs>? AccessKeyDisplayRequestedTrampoline;

        /// <summary>
        /// Null out every Current* user delegate so trampolines already attached
        /// on the native event source become no-ops. Trampoline delegate fields
        /// are left intact — they're rooted by WinUI's subscription list and
        /// can't be detached here without access to the native element.
        /// </summary>
        public void ClearCurrentHandlers()
        {
            CurrentSizeChanged = null;
            CurrentPointerPressed = null;
            CurrentPointerMoved = null;
            CurrentPointerReleased = null;
            CurrentPointerEntered = null;
            CurrentPointerExited = null;
            CurrentPointerCanceled = null;
            CurrentPointerCaptureLost = null;
            CurrentPointerWheelChanged = null;
            CurrentTapped = null;
            CurrentDoubleTapped = null;
            CurrentRightTapped = null;
            CurrentHolding = null;
            CurrentKeyDown = null;
            CurrentKeyUp = null;
            CurrentPreviewKeyDown = null;
            CurrentPreviewKeyUp = null;
            CurrentCharacterReceived = null;
            CurrentGotFocus = null;
            CurrentLostFocus = null;
            CurrentAccessKeyDisplayRequested = null;
        }
    }

    // ModifierEventHandlerState lives on the ReactorState wrapper attached to the
    // native DependencyObject via ReactorAttached.StateProperty. That keys
    // on native identity, so two RCWs pointing at the same element share one
    // ModifierEventHandlerState and one set of trampolines — fixing issue #86.
    private static ModifierEventHandlerState GetOrCreateModifierState(FrameworkElement fe)
    {
        var state = GetOrCreateReactorState(fe);
        return state.Modifiers ??= new ModifierEventHandlerState();
    }

    private static bool HasAnyEventHandler(ElementModifiers? m)
    {
        if (m is null) return false;
        return m.OnSizeChanged is not null
            || m.OnPointerPressed is not null || m.OnPointerMoved is not null || m.OnPointerReleased is not null
            || m.OnPointerEntered is not null || m.OnPointerExited is not null || m.OnPointerCanceled is not null
            || m.OnPointerCaptureLost is not null || m.OnPointerWheelChanged is not null
            || m.OnTapped is not null || m.OnDoubleTapped is not null || m.OnRightTapped is not null || m.OnHolding is not null
            || m.OnKeyDown is not null || m.OnKeyUp is not null
            || m.OnPreviewKeyDown is not null || m.OnPreviewKeyUp is not null
            || m.OnCharacterReceived is not null
            || m.OnGotFocus is not null || m.OnLostFocus is not null
            || m.OnAccessKeyDisplayRequested is not null;
    }

    private static bool HasAnyPointerHandler(ElementModifiers m)
    {
        return m.OnPointerPressed is not null || m.OnPointerMoved is not null || m.OnPointerReleased is not null
            || m.OnPointerEntered is not null || m.OnPointerExited is not null || m.OnPointerCanceled is not null
            || m.OnPointerCaptureLost is not null || m.OnPointerWheelChanged is not null
            || m.OnTapped is not null || m.OnDoubleTapped is not null || m.OnRightTapped is not null || m.OnHolding is not null;
    }

    private static void ApplyEventHandlers(FrameworkElement fe, ElementModifiers? oldM, ElementModifiers m)
    {
        // Fast path: nothing to do
        if (!HasAnyEventHandler(m) && !HasAnyEventHandler(oldM)) return;

        var state = GetOrCreateModifierState(fe);

        // Trampoline pattern: each Ensure* helper points the current-handler field at
        // the new delegate and, if the trampoline isn't attached yet, attaches it once.
        // Subsequent renders that just hand us a fresh closure touch only the field —
        // no add_/remove_ COM traffic on the underlying WinUI event.
        EnsureSizeChangedSubscribed(fe, state, m.OnSizeChanged);
        EnsurePointerPressedSubscribed(fe, state, m.OnPointerPressed);
        EnsurePointerMovedSubscribed(fe, state, m.OnPointerMoved);
        EnsurePointerReleasedSubscribed(fe, state, m.OnPointerReleased);
        EnsurePointerEnteredSubscribed(fe, state, m.OnPointerEntered);
        EnsurePointerExitedSubscribed(fe, state, m.OnPointerExited);
        EnsurePointerCanceledSubscribed(fe, state, m.OnPointerCanceled);
        EnsurePointerCaptureLostSubscribed(fe, state, m.OnPointerCaptureLost);
        EnsurePointerWheelChangedSubscribed(fe, state, m.OnPointerWheelChanged);
        EnsureTappedSubscribed(fe, state, m.OnTapped, oldM?.OnTapped);
        EnsureDoubleTappedSubscribed(fe, state, m.OnDoubleTapped, oldM?.OnDoubleTapped);
        EnsureRightTappedSubscribed(fe, state, m.OnRightTapped, oldM?.OnRightTapped);
        EnsureHoldingSubscribed(fe, state, m.OnHolding, oldM?.OnHolding);
        EnsureKeyDownSubscribed(fe, state, m.OnKeyDown);
        EnsureKeyUpSubscribed(fe, state, m.OnKeyUp);
        EnsurePreviewKeyDownSubscribed(fe, state, m.OnPreviewKeyDown);
        EnsurePreviewKeyUpSubscribed(fe, state, m.OnPreviewKeyUp);
        EnsureCharacterReceivedSubscribed(fe, state, m.OnCharacterReceived);
        EnsureGotFocusSubscribed(fe, state, m.OnGotFocus);
        EnsureLostFocusSubscribed(fe, state, m.OnLostFocus);
        EnsureAccessKeyDisplayRequestedSubscribed(fe, state, m.OnAccessKeyDisplayRequested);

        // Shape auto-fill: Shape subclasses need a non-null Fill to hit-test pointer events.
        // If any pointer-family handler is attached and Fill is null, set transparent brush.
        if (fe is Microsoft.UI.Xaml.Shapes.Shape shape && shape.Fill is null && HasAnyPointerHandler(m))
        {
            shape.Fill = new SolidColorBrush(global::Microsoft.UI.Colors.Transparent);
        }
    }

    // ── Trampoline Ensure* helpers ──────────────────────────────────────
    // Each helper:
    //   1. Updates state.Current<Event> to the new user handler (may be null).
    //   2. On first non-null handler, allocates the stable trampoline, attaches
    //      it to the WinUI event, emits reactor:event.reattach once.
    //   3. Never detaches — the trampoline stays bound for the element's lifetime.
    //      When the user handler becomes null again, the trampoline dispatches no-op.

    // <snippet:event-trampoline>
    private static void EnsureSizeChangedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, SizeChangedEventArgs>? handler)
    {
        state.CurrentSizeChanged = handler;
        if (state.SizeChangedTrampoline is null && handler is not null)
        {
            state.SizeChangedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("SizeChanged");
                state.CurrentSizeChanged?.Invoke(s!, e);
            };
            fe.SizeChanged += state.SizeChangedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("SizeChanged", fe.GetType().Name);
        }
    }
    // </snippet:event-trampoline>

    private static void EnsurePointerPressedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
    {
        state.CurrentPointerPressed = handler;
        if (state.PointerPressedTrampoline is null && handler is not null)
        {
            state.PointerPressedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PointerPressed");
                state.CurrentPointerPressed?.Invoke(s!, e);
            };
            fe.PointerPressed += state.PointerPressedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PointerPressed", fe.GetType().Name);
        }
    }

    private static void EnsurePointerMovedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
    {
        state.CurrentPointerMoved = handler;
        if (state.PointerMovedTrampoline is null && handler is not null)
        {
            state.PointerMovedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PointerMoved");
                state.CurrentPointerMoved?.Invoke(s!, e);
            };
            fe.PointerMoved += state.PointerMovedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PointerMoved", fe.GetType().Name);
        }
    }

    private static void EnsurePointerReleasedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
    {
        state.CurrentPointerReleased = handler;
        if (state.PointerReleasedTrampoline is null && handler is not null)
        {
            state.PointerReleasedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PointerReleased");
                state.CurrentPointerReleased?.Invoke(s!, e);
            };
            fe.PointerReleased += state.PointerReleasedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PointerReleased", fe.GetType().Name);
        }
    }

    private static void EnsurePointerEnteredSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
    {
        state.CurrentPointerEntered = handler;
        if (state.PointerEnteredTrampoline is null && handler is not null)
        {
            state.PointerEnteredTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PointerEntered");
                state.CurrentPointerEntered?.Invoke(s!, e);
            };
            fe.PointerEntered += state.PointerEnteredTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PointerEntered", fe.GetType().Name);
        }
    }

    private static void EnsurePointerExitedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
    {
        state.CurrentPointerExited = handler;
        if (state.PointerExitedTrampoline is null && handler is not null)
        {
            state.PointerExitedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PointerExited");
                state.CurrentPointerExited?.Invoke(s!, e);
            };
            fe.PointerExited += state.PointerExitedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PointerExited", fe.GetType().Name);
        }
    }

    private static void EnsurePointerCanceledSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
    {
        state.CurrentPointerCanceled = handler;
        if (state.PointerCanceledTrampoline is null && handler is not null)
        {
            state.PointerCanceledTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PointerCanceled");
                state.CurrentPointerCanceled?.Invoke(s!, e);
            };
            fe.PointerCanceled += state.PointerCanceledTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PointerCanceled", fe.GetType().Name);
        }
    }

    private static void EnsurePointerCaptureLostSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
    {
        state.CurrentPointerCaptureLost = handler;
        if (state.PointerCaptureLostTrampoline is null && handler is not null)
        {
            state.PointerCaptureLostTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PointerCaptureLost");
                state.CurrentPointerCaptureLost?.Invoke(s!, e);
            };
            fe.PointerCaptureLost += state.PointerCaptureLostTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PointerCaptureLost", fe.GetType().Name);
        }
    }

    private static void EnsurePointerWheelChangedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? handler)
    {
        state.CurrentPointerWheelChanged = handler;
        if (state.PointerWheelChangedTrampoline is null && handler is not null)
        {
            state.PointerWheelChangedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PointerWheelChanged");
                state.CurrentPointerWheelChanged?.Invoke(s!, e);
            };
            fe.PointerWheelChanged += state.PointerWheelChangedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PointerWheelChanged", fe.GetType().Name);
        }
    }

    private static void EnsureTappedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs>? handler, Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs>? oldHandler)
    {
        state.CurrentTapped = handler;
        if (state.TappedTrampoline is null && handler is not null)
        {
            state.TappedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("Tapped");
                state.CurrentTapped?.Invoke(s!, e);
            };
            fe.Tapped += state.TappedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("Tapped", fe.GetType().Name);
        }
        if (handler is not null)
        {
            fe.IsTapEnabled = true;
            // Make tappable elements keyboard-accessible and visible in UIA tree
            if (fe is Microsoft.UI.Xaml.Controls.Control ctrl && !ctrl.IsTabStop)
                ctrl.IsTabStop = true;
        }
        else if (oldHandler is not null)
        {
            fe.IsTapEnabled = false;
        }
    }

    private static void EnsureDoubleTappedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs>? handler, Action<object, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs>? oldHandler)
    {
        state.CurrentDoubleTapped = handler;
        if (state.DoubleTappedTrampoline is null && handler is not null)
        {
            state.DoubleTappedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("DoubleTapped");
                state.CurrentDoubleTapped?.Invoke(s!, e);
            };
            fe.DoubleTapped += state.DoubleTappedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("DoubleTapped", fe.GetType().Name);
        }
        if (handler is not null) fe.IsDoubleTapEnabled = true;
        else if (oldHandler is not null) fe.IsDoubleTapEnabled = false;
    }

    private static void EnsureRightTappedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs>? handler, Action<object, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs>? oldHandler)
    {
        state.CurrentRightTapped = handler;
        if (state.RightTappedTrampoline is null && handler is not null)
        {
            state.RightTappedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("RightTapped");
                state.CurrentRightTapped?.Invoke(s!, e);
            };
            fe.RightTapped += state.RightTappedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("RightTapped", fe.GetType().Name);
        }
        if (handler is not null) fe.IsRightTapEnabled = true;
        else if (oldHandler is not null) fe.IsRightTapEnabled = false;
    }

    private static void EnsureHoldingSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.HoldingRoutedEventArgs>? handler, Action<object, Microsoft.UI.Xaml.Input.HoldingRoutedEventArgs>? oldHandler)
    {
        state.CurrentHolding = handler;
        if (state.HoldingTrampoline is null && handler is not null)
        {
            state.HoldingTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("Holding");
                state.CurrentHolding?.Invoke(s!, e);
            };
            fe.Holding += state.HoldingTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("Holding", fe.GetType().Name);
        }
        if (handler is not null) fe.IsHoldingEnabled = true;
        else if (oldHandler is not null) fe.IsHoldingEnabled = false;
    }

    private static void EnsureKeyDownSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? handler)
    {
        state.CurrentKeyDown = handler;
        if (state.KeyDownTrampoline is null && handler is not null)
        {
            state.KeyDownTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("KeyDown");
                state.CurrentKeyDown?.Invoke(s!, e);
            };
            fe.KeyDown += state.KeyDownTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("KeyDown", fe.GetType().Name);
        }
    }

    private static void EnsureKeyUpSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? handler)
    {
        state.CurrentKeyUp = handler;
        if (state.KeyUpTrampoline is null && handler is not null)
        {
            state.KeyUpTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("KeyUp");
                state.CurrentKeyUp?.Invoke(s!, e);
            };
            fe.KeyUp += state.KeyUpTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("KeyUp", fe.GetType().Name);
        }
    }

    private static void EnsurePreviewKeyDownSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? handler)
    {
        state.CurrentPreviewKeyDown = handler;
        if (state.PreviewKeyDownTrampoline is null && handler is not null)
        {
            state.PreviewKeyDownTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PreviewKeyDown");
                state.CurrentPreviewKeyDown?.Invoke(s!, e);
            };
            fe.PreviewKeyDown += state.PreviewKeyDownTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PreviewKeyDown", fe.GetType().Name);
        }
    }

    private static void EnsurePreviewKeyUpSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? handler)
    {
        state.CurrentPreviewKeyUp = handler;
        if (state.PreviewKeyUpTrampoline is null && handler is not null)
        {
            state.PreviewKeyUpTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("PreviewKeyUp");
                state.CurrentPreviewKeyUp?.Invoke(s!, e);
            };
            fe.PreviewKeyUp += state.PreviewKeyUpTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("PreviewKeyUp", fe.GetType().Name);
        }
    }

    private static void EnsureCharacterReceivedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<UIElement, Microsoft.UI.Xaml.Input.CharacterReceivedRoutedEventArgs>? handler)
    {
        state.CurrentCharacterReceived = handler;
        if (state.CharacterReceivedTrampoline is null && handler is not null)
        {
            state.CharacterReceivedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("CharacterReceived");
                state.CurrentCharacterReceived?.Invoke(s, e);
            };
            fe.CharacterReceived += state.CharacterReceivedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("CharacterReceived", fe.GetType().Name);
        }
    }

    private static void EnsureGotFocusSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, RoutedEventArgs>? handler)
    {
        state.CurrentGotFocus = handler;
        if (state.GotFocusTrampoline is null && handler is not null)
        {
            state.GotFocusTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("GotFocus");
                state.CurrentGotFocus?.Invoke(s!, e);
            };
            fe.GotFocus += state.GotFocusTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("GotFocus", fe.GetType().Name);
        }
    }

    private static void EnsureLostFocusSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<object, RoutedEventArgs>? handler)
    {
        state.CurrentLostFocus = handler;
        if (state.LostFocusTrampoline is null && handler is not null)
        {
            state.LostFocusTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("LostFocus");
                state.CurrentLostFocus?.Invoke(s!, e);
            };
            fe.LostFocus += state.LostFocusTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("LostFocus", fe.GetType().Name);
        }
    }

    private static void EnsureAccessKeyDisplayRequestedSubscribed(FrameworkElement fe, ModifierEventHandlerState state, Action<UIElement, Microsoft.UI.Xaml.Input.AccessKeyDisplayRequestedEventArgs>? handler)
    {
        state.CurrentAccessKeyDisplayRequested = handler;
        if (state.AccessKeyDisplayRequestedTrampoline is null && handler is not null)
        {
            state.AccessKeyDisplayRequestedTrampoline = (s, e) =>
            {
                Diagnostics.ReactorEventSource.Log.EventTrampolineDispatch("AccessKeyDisplayRequested");
                state.CurrentAccessKeyDisplayRequested?.Invoke(s!, e);
            };
            fe.AccessKeyDisplayRequested += state.AccessKeyDisplayRequestedTrampoline;
            Diagnostics.ReactorEventSource.Log.EventTrampolineAttached("AccessKeyDisplayRequested", fe.GetType().Name);
        }
    }

    /// <summary>
    /// Applies ThemeRef bindings by setting properties through WinUI's {ThemeResource}
    /// mechanism. Builds a local Style with ThemeResource setters and applies it to the
    /// element. WinUI then handles theme-reactive resolution natively for system theme
    /// changes (Light ↔ Dark). Note: {ThemeResource} in dynamically-loaded Styles resolves
    /// against the app theme, not per-element RequestedTheme overrides — for subtree theme
    /// overrides, rely on native WinUI control theming instead of ThemeRef bindings.
    /// </summary>
    /// <summary>
    /// Spec 047 §14 Phase 1 (1.3) — promoted from private (per audit
    /// existing-api-surface.md). Applies <see cref="ThemeRef"/> bindings as
    /// a synthesized <c>{ThemeResource}</c>-driven Style. Provisional API;
    /// see <c>REACTOR_V1_PREVIEW</c>.
    /// </summary>
    public static void ApplyThemeBindings(FrameworkElement fe, IReadOnlyDictionary<string, ThemeRef> bindings)
    {
        var targetType = GetStyleTargetType(fe);
        if (targetType is null) return;

        var cacheKey = BuildCacheKey(targetType, bindings);

        // Cache hit: reuse the previously compiled Style
        if (_styleCache.TryGetValue(cacheKey, out var cachedStyle))
        {
            ApplyStyleToElement(fe, cachedStyle);
            return;
        }

        // Cache miss: build XAML, parse, and cache
        var setters = new global::System.Text.StringBuilder();
        foreach (var (property, themeRef) in bindings)
        {
            var dp = GetDependencyPropertyName(fe, property);
            if (dp is null) continue;
            var escapedResourceKey = global::System.Security.SecurityElement.Escape(themeRef.ResourceKey);
            setters.Append($"<Setter Property='{dp}' Value='{{ThemeResource {escapedResourceKey}}}'/>");
        }

        if (setters.Length == 0) return;

        try
        {
            var xaml =
                $"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='{targetType}'>" +
                setters.ToString() +
                "</Style>";
            var style = (Style)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
            _styleCache.TryAdd(cacheKey, style);
            ApplyStyleToElement(fe, style);
        }
        catch (Exception ex)
        {
            Microsoft.UI.Reactor.Core.Diagnostics.DiagnosticLog.SwallowedError(
                Microsoft.UI.Reactor.Core.Diagnostics.LogCategory.Theme,
                "ApplyThemeBindings",
                ex);
        }
    }

    /// <summary>
    /// Applies a cached style to an element. Clears any existing style first to
    /// force WinUI to re-evaluate <c>{ThemeResource}</c> setters against the
    /// element's current effective theme (which may have changed due to a parent's
    /// <see cref="FrameworkElement.RequestedTheme"/> override).
    /// </summary>
    private static void ApplyStyleToElement(FrameworkElement fe, Style cachedStyle)
    {
        // Clearing first forces WinUI to process the subsequent set as a
        // genuine change, re-resolving {ThemeResource} values. Without this,
        // re-applying the same cached Style reference is a no-op.
        if (fe.Style is not null)
            fe.Style = null;
        fe.Style = cachedStyle;
    }

    private static string? GetStyleTargetType(FrameworkElement fe) => fe switch
    {
        WinUI.Border => "Border",
        WinUI.StackPanel => "StackPanel",
        WinUI.Grid => "Grid",
        WinUI.Button => "Button",
        WinUI.TextBox => "TextBox",
        TextBlock => "TextBlock",
        WinUI.ContentControl => "ContentControl",
        WinUI.Panel => "Panel",
        WinUI.Control => "Control",
        _ => fe.GetType().Name,
    };

    private static string? GetDependencyPropertyName(FrameworkElement fe, string property)
    {
        if (property == "Background" && (fe is WinUI.Panel || fe is WinUI.Control || fe is WinUI.Border))
            return "Background";
        if (property == "Foreground" && (fe is WinUI.Control || fe is TextBlock))
            return "Foreground";
        if (property == "BorderBrush" && (fe is WinUI.Control || fe is WinUI.Border))
            return "BorderBrush";
        return null;
    }

    // ── Lightweight Styling: per-control resource overrides ────────────────

    /// <summary>
    /// Tracks which resource keys in <see cref="FrameworkElement.Resources"/> were
    /// set by Reactor (vs. keys set by XAML or other sources). On update, only
    /// Reactor-managed keys are removed when overrides change.
    /// </summary>
    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, HashSet<string>>
        _managedResourceKeys = new();

    /// <summary>
    /// Applies per-control resource overrides (lightweight styling) to a
    /// <see cref="FrameworkElement"/>. Literal values are set directly;
    /// <see cref="ThemeRef"/>-based values are resolved from
    /// <c>Application.Current.Resources</c>.
    /// </summary>
    /// <summary>
    /// Spec 047 §14 Phase 1 (1.3) — promoted from private (per audit
    /// existing-api-surface.md). Applies per-control resource overrides
    /// (lightweight styling). Provisional API; see <c>REACTOR_V1_PREVIEW</c>.
    /// </summary>
    public static void ApplyResourceOverrides(
        FrameworkElement fe,
        Microsoft.UI.Reactor.Elements.ResourceOverrides? oldOverrides,
        Microsoft.UI.Reactor.Elements.ResourceOverrides? newOverrides)
    {
        // Track which keys Reactor has set on this element
        var managed = _managedResourceKeys.GetOrCreateValue(fe);

        // Remove old keys that are no longer present in the new overrides
        if (oldOverrides is not null)
        {
            var newKeys = newOverrides?.AllKeys.ToHashSet() ?? new HashSet<string>();
            foreach (var key in managed.ToArray())
            {
                if (!newKeys.Contains(key))
                {
                    fe.Resources.Remove(key);
                    managed.Remove(key);
                }
            }
        }

        if (newOverrides is null) return;

        fe.Resources ??= new ResourceDictionary();

        // Apply literal resources
        foreach (var (key, value) in newOverrides.Literals)
        {
            fe.Resources[key] = value;
            managed.Add(key);
        }

        // Apply ThemeRef resources (resolved from Application.Current.Resources)
        foreach (var (key, themeRef) in newOverrides.ThemeRefs)
        {
            var resolved = ThemeRef.Resolve(themeRef.ResourceKey, fe);
            if (resolved is not null)
            {
                fe.Resources[key] = resolved;
                managed.Add(key);
            }
        }
    }

    /// <summary>
    /// Sets or updates the flyout on a control. On first mount, creates a new flyout.
    /// On update, reconciles the content inside the existing flyout to keep it open.
    /// </summary>
    private void ApplyFlyoutAttachment(FrameworkElement fe, Element? oldFlyoutEl, Element newFlyoutEl, Action requestRerender)
    {
        // Try to get the existing flyout from the control.
        // SplitButton.Flyout and Button.Flyout are separate properties (different type hierarchies).
        WinPrim.FlyoutBase? existingFlyout = fe switch
        {
            WinUI.SplitButton sb => sb.Flyout,
            WinUI.Button btn => btn.Flyout,  // AppBarButton inherits from Button
            _ => WinPrim.FlyoutBase.GetAttachedFlyout(fe),
        };

        // If we have an existing flyout and old element, try to update in place
        if (oldFlyoutEl is not null && existingFlyout is not null)
        {
            UpdateFlyoutInPlace(existingFlyout, oldFlyoutEl, newFlyoutEl, requestRerender);
            return;
        }

        // First mount — create new flyout
        var flyout = CreateFlyoutFromElement(newFlyoutEl, requestRerender);
        if (flyout is null) return;

        SetFlyoutOnControl(fe, flyout);
    }

    /// <summary>
    /// Updates the content inside an existing flyout without replacing the flyout object.
    /// This keeps the flyout open while its content changes.
    /// </summary>
    private void UpdateFlyoutInPlace(WinPrim.FlyoutBase existingFlyout, Element oldEl, Element newEl, Action requestRerender)
    {
        // ContentFlyout → reconcile child content inside the existing Flyout
        if (newEl is ContentFlyoutElement newCf && existingFlyout is WinUI.Flyout flyout)
        {
            var oldContent = oldEl is ContentFlyoutElement oldCf ? oldCf.Content : null;
            if (oldContent is not null && flyout.Content is UIElement existingContent && CanUpdate(oldContent, newCf.Content))
            {
                var replacement = Update(oldContent, newCf.Content, existingContent, requestRerender);
                if (replacement is not null && !ReferenceEquals(flyout.Content, replacement))
                    flyout.Content = replacement;
            }
            else
            {
                // Type changed — remount content
                flyout.Content = Mount(newCf.Content, requestRerender);
            }
            flyout.Placement = newCf.Placement;
            return;
        }

        // MenuFlyout → recreate items (lightweight, no open-state issue)
        if (newEl is MenuFlyoutContentElement newMf && existingFlyout is WinUI.MenuFlyout menuFlyout)
        {
            menuFlyout.Items.Clear();
            foreach (var item in newMf.Items) menuFlyout.Items.Add(CreateMenuFlyoutItem(item));
            if (newMf.Placement != WinPrim.FlyoutPlacementMode.Auto)
                menuFlyout.Placement = newMf.Placement;
            return;
        }

        // Fallback: plain element → reconcile inside existing Flyout
        if (existingFlyout is WinUI.Flyout plainFlyout && plainFlyout.Content is UIElement existingCtrl)
        {
            if (CanUpdate(oldEl, newEl))
            {
                var replacement = Update(oldEl, newEl, existingCtrl, requestRerender);
                if (replacement is not null && !ReferenceEquals(plainFlyout.Content, replacement))
                    plainFlyout.Content = replacement;
            }
            else
            {
                plainFlyout.Content = Mount(newEl, requestRerender);
            }
        }
    }

    internal void SetFlyoutOnControl(FrameworkElement fe, WinPrim.FlyoutBase flyout)
    {
        // Check SplitButton before Button (SplitButton doesn't inherit from Button,
        // but DropDownButton does, so Button catch-all handles it).
        if (fe is WinUI.SplitButton sb)
            sb.Flyout = flyout;
        else if (fe is WinUI.Button btn)  // AppBarButton, DropDownButton inherit from Button
            btn.Flyout = flyout;
        else
            WinPrim.FlyoutBase.SetAttachedFlyout(fe, flyout);
    }

    /// <summary>
    /// Creates a WinUI FlyoutBase from a Reactor element descriptor.
    /// Recognizes ContentFlyoutElement and MenuFlyoutContentElement for configured flyouts,
    /// and falls back to wrapping plain elements in a basic Flyout.
    /// Used by both ApplyModifiers (for .WithFlyout()/.WithContextFlyout()) and
    /// button mount methods (for direct Flyout parameter).
    /// </summary>
    internal WinPrim.FlyoutBase? CreateFlyoutFromElement(Element flyoutEl, Action requestRerender)
    {
        switch (flyoutEl)
        {
            case ContentFlyoutElement cf:
            {
                var content = Mount(cf.Content, requestRerender);
                return content is not null ? new WinUI.Flyout { Content = content, Placement = cf.Placement } : null;
            }
            case MenuFlyoutContentElement mf:
            {
                var menuFlyout = new WinUI.MenuFlyout();
                // Only set Placement if explicitly specified (Auto can cause assertions on MenuFlyout)
                if (mf.Placement != WinPrim.FlyoutPlacementMode.Auto)
                    menuFlyout.Placement = mf.Placement;
                foreach (var item in mf.Items) menuFlyout.Items.Add(CreateMenuFlyoutItem(item));
                return menuFlyout;
            }
            default:
            {
                var content = Mount(flyoutEl, requestRerender);
                return content is not null ? new WinUI.Flyout { Content = content } : null;
            }
        }
    }

    /// <summary>
    /// Spec 047 §14 Phase 3-final — descriptor-facing sibling of
    /// <see cref="CreateFlyoutFromElement"/>. Same shape as
    /// <see cref="ResolveIconForDescriptor"/>: a thin forwarder that tolerates
    /// <see langword="null"/> input so a <c>.OneWayBridged</c> entry on a
    /// button-family descriptor can wire
    /// <c>(c, v, rec, rr) =&gt; c.Flyout = rec.CreateFlyoutForDescriptor(v, rr)</c>
    /// without an upstream null guard.
    /// </summary>
    internal WinPrim.FlyoutBase? CreateFlyoutForDescriptor(Element? flyoutEl, Action requestRerender)
        => flyoutEl is null ? null : CreateFlyoutFromElement(flyoutEl, requestRerender);

    // ── Enum conversions removed — Reactor now uses WinUI types directly ──

    internal static Symbol ParseSymbol(string name)
    {
        if (Enum.TryParse<Symbol>(name, ignoreCase: true, out var symbol)) return symbol;
        return Symbol.Placeholder;
    }

    // ── Grid definition parsing ─────────────────────────────────────

    internal static ColumnDefinition ParseColumnDef(string def) => def switch
    {
        "*" => new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
        "Auto" or "auto" => new ColumnDefinition { Width = GridLength.Auto },
        _ when double.TryParse(def, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var px) => new ColumnDefinition { Width = new GridLength(px) },
        _ when def.EndsWith('*') && double.TryParse(def[..^1], global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var stars) =>
            new ColumnDefinition { Width = new GridLength(stars, GridUnitType.Star) },
        _ => new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
    };

    internal static RowDefinition ParseRowDef(string def) => def switch
    {
        "*" => new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
        "Auto" or "auto" => new RowDefinition { Height = GridLength.Auto },
        _ when double.TryParse(def, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var px) => new RowDefinition { Height = new GridLength(px) },
        _ when def.EndsWith('*') && double.TryParse(def[..^1], global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var stars) =>
            new RowDefinition { Height = new GridLength(stars, GridUnitType.Star) },
        _ => new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
    };

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
    /// Tracks the state of a mounted NavigationHost in the tree.
    /// Stores the current child control/element and the subscription to route changes
    /// so content can be swapped when navigation occurs.
    /// </summary>
    internal class NavigationHostNode
    {
        /// <summary>The type-erased navigation handle (implements INavigationHandle internally).</summary>
        public Navigation.INavigationHandle Handle { get; set; } = null!;
        /// <summary>The route that was last rendered.</summary>
        public object LastRenderedRoute { get; set; } = null!;
        /// <summary>The element returned by routeMap for the current route.</summary>
        public Element? CurrentChildElement { get; set; }
        /// <summary>The mounted WinUI control for the current route.</summary>
        public UIElement? CurrentChildControl { get; set; }
        /// <summary>The route-mapping function (type-erased).</summary>
        public Func<object, Element> RouteMap { get; set; } = null!;
        /// <summary>The rerender callback for triggering content swap.</summary>
        public Action? RequestRerender { get; set; }
        /// <summary>Handler attached to INavigationHandle.RouteChanged for cleanup.</summary>
        public Action? RouteChangedHandler { get; set; }
        /// <summary>Navigation mode recorded by the lifecycle guard before stack mutation.</summary>
        public Navigation.NavigationMode? PendingNavigationMode { get; set; }
        /// <summary>Previous route recorded by the lifecycle guard before stack mutation.</summary>
        public object? PendingPreviousRoute { get; set; }
        /// <summary>The host-level default transition.</summary>
        public Navigation.NavigationTransition HostTransition { get; set; } = Navigation.NavigationTransition.Default;
        /// <summary>True if a transition animation is currently running.</summary>
        public bool TransitionInProgress { get; set; }
        /// <summary>The host-level cache mode.</summary>
        public Navigation.NavigationCacheMode CacheMode { get; set; } = Navigation.NavigationCacheMode.Disabled;
        /// <summary>Page cache for this NavigationHost (null when CacheMode is Disabled).</summary>
        public Navigation.NavigationCache? Cache { get; set; }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Navigation lifecycle hook traversal
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Collects all <see cref="RenderContext.NavigationLifecycleHookState"/> instances from
    /// the component subtree rooted at <paramref name="root"/>.
    /// </summary>
    internal List<RenderContext.NavigationLifecycleHookState> CollectLifecycleHooks(UIElement? root)
    {
        var hooks = new List<RenderContext.NavigationLifecycleHookState>();
        CollectLifecycleHooksRecursive(root, hooks);
        return hooks;
    }

    private void CollectLifecycleHooksRecursive(UIElement? control, List<RenderContext.NavigationLifecycleHookState> hooks)
    {
        if (control is null) return;

        if (_componentNodes.TryGetValue(control, out var node))
        {
            var ctx = node.Component?.Context ?? node.Context;
            var hook = ctx?.GetNavigationLifecycleHook();
            if (hook is not null)
                hooks.Add(hook);
        }

        // Recurse into children (Border wraps components, Panel/Grid wraps layouts)
        if (control is WinUI.Panel panel)
        {
            foreach (UIElement child in panel.Children)
                CollectLifecycleHooksRecursive(child, hooks);
        }
        else if (control is WinUI.Border border && border.Child is not null)
        {
            CollectLifecycleHooksRecursive(border.Child, hooks);
        }
        else if (control is WinUI.ContentControl cc && cc.Content is UIElement content)
        {
            CollectLifecycleHooksRecursive(content, hooks);
        }
    }

    /// <summary>
    /// Invokes post-navigation lifecycle callbacks: onNavigatedTo on the new page,
    /// then onNavigatedFrom on the old page (using pre-collected hooks).
    /// </summary>
    internal void InvokePostNavigationLifecycle(
        UIElement? newChildControl,
        List<RenderContext.NavigationLifecycleHookState>? oldHooks,
        object currentRoute, object? previousRoute, Navigation.NavigationMode mode)
    {
        // onNavigatedTo on the new page's component tree
        var newHooks = CollectLifecycleHooks(newChildControl);
        var navigatedToCtx = new Navigation.NavigatedToContext(currentRoute, previousRoute, mode);
        foreach (var hook in newHooks)
        {
            hook.OnNavigatedTo?.Invoke(navigatedToCtx);
        }

        // onNavigatedFrom on the old page (callbacks captured at last render)
        if (oldHooks is not null)
        {
            var navigatedFromCtx = new Navigation.NavigatedFromContext(
                previousRoute!, currentRoute, mode);
            foreach (var hook in oldHooks)
            {
                hook.OnNavigatedFrom?.Invoke(navigatedFromCtx);
            }
        }
    }

    /// <summary>
    /// Invokes <c>onNavigatingTo</c> (destination-side guard) on all lifecycle hooks
    /// in the new page's subtree. Returns true if navigation should proceed.
    /// </summary>
    internal bool InvokeNavigatingTo(
        UIElement? newChildControl,
        object currentRoute, object? previousRoute, Navigation.NavigationMode mode)
    {
        var hooks = CollectLifecycleHooks(newChildControl);
        var ctx = new Navigation.NavigatingToContext(currentRoute, previousRoute, mode);
        foreach (var hook in hooks)
        {
            hook.OnNavigatingTo?.Invoke(ctx);
            if (ctx.IsCancelled)
            {
                Navigation.NavigationDiagnostics.OnNavigationCancelled(
                    previousRoute ?? currentRoute, currentRoute, mode, "destination guard");
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Invokes <c>onNavigatingFrom</c> on all lifecycle hooks in the subtree.
    /// Sets <see cref="Navigation.NavigatingFromContext.IsCancelled"/> if any callback cancels.
    /// </summary>
    internal void InvokeNavigatingFrom(UIElement? root, Navigation.NavigatingFromContext ctx)
    {
        if (root is null) return;

        if (_componentNodes.TryGetValue(root, out var node))
        {
            var renderCtx = node.Component?.Context ?? node.Context;
            var hook = renderCtx?.GetNavigationLifecycleHook();
            hook?.OnNavigatingFrom?.Invoke(ctx);
            if (ctx.IsCancelled) return;
        }

        if (root is WinUI.Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                InvokeNavigatingFrom(child, ctx);
                if (ctx.IsCancelled) return;
            }
        }
        else if (root is WinUI.Border border && border.Child is not null)
        {
            InvokeNavigatingFrom(border.Child, ctx);
        }
        else if (root is WinUI.ContentControl cc && cc.Content is UIElement content)
        {
            InvokeNavigatingFrom(content, ctx);
        }
    }

    public void Dispose()
    {
        foreach (var node in _componentNodes.Values)
        {
            node.Context?.RunCleanups();
            node.Component?.Context?.RunCleanups();
        }
        _componentNodes.Clear();
        _errorBoundaryNodes.Clear();
        foreach (var node in _navigationHostNodes.Values)
        {
            if (node.RouteChangedHandler is not null)
                node.Handle.RouteChanged -= node.RouteChangedHandler;
            node.Handle.Detach();
            if (node.CurrentChildControl is not null)
                UnmountRecursive(node.CurrentChildControl);
            node.Cache?.Clear();
        }
        _navigationHostNodes.Clear();
        _pool.Clear();
    }
}
