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

// Reconciler partial: Pool rent / return (RentControl, ReturnControl, pool lanes).
public sealed partial class Reconciler
{
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
        // The lane belongs to the first control a mount rents, never to a later one.
        bool visualLane = _rentContext.VisualLane;
        _rentContext = _rentContext with { Armed = false };
        // Default = poolable; only skip the pool when the policy explicitly opts out.
        bool poolable = policy?.IsPoolable != false;
        if (poolable && typeof(FrameworkElement).IsAssignableFrom(typeof(T)))
        {
            var rented = _pool.TryRent(typeof(T), visualLane);
            if (rented is T t)
            {
                // Armed once the mount succeeds (Mount), so a mount that throws leaves no subscription.
                if (visualLane && ElementPool.IsVisualTaken(rented)) _visualLaneRental = rented;
                return t;
            }
        }
        return factory is not null ? factory() : new T();
    }

    // Spec 066 §7.2: which pool lane the control the current mount rents may come from.
    private PoolRentContext _rentContext;

    // The visual-lane control the current mount rented, whose load-time sync starts once the mount succeeds.
    private FrameworkElement? _visualLaneRental;

    // Element types (exact, or a base of them) registered on this reconciler by app code. Their
    // handlers may write facades.
    private HashSet<Type>? _externalHandlerTypes;
    private List<Type>? _externalHandlerBaseTypes;

    /// <summary>
    /// Whether <paramref name="elementType"/> may mount through an app handler, which could write a
    /// facade that throws on a visual-lane control (spec 066 §7.2). Covers per-host registrations,
    /// derived-type registrations and global overrides of a built-in.
    /// </summary>
    private bool MayUseAppHandler(Type elementType)
    {
        if (_externalHandlerTypes?.Contains(elementType) == true || V1Protocol.ControlRegistry.IsContested(elementType))
            return true;
        if (_externalHandlerBaseTypes is { } bases)
            foreach (var b in bases)
                if (b.IsAssignableFrom(elementType)) return true;
        return false;
    }

    /// <summary>
    /// Spec 066 §7.2. A mount may rent from the visual lane only when it declares a feature that
    /// takes the visual (<i>demand</i>), and nothing in the chain over the control can write a
    /// facade that throws on a taken visual but works on a fresh control: no setters or mount
    /// callbacks (<i>blocked</i>), and a built-in handler (<i>candidate</i>). Demand also comes
    /// from a transparent keyed <c>Memo</c> over the element. A setter or callback anywhere above
    /// a mount that hasn't rented yet blocks it. Anything unknown takes the plain lane.
    /// </summary>
    internal readonly record struct PoolRentContext(bool Armed, bool Transparent, bool Demand, bool Blocked, bool Candidate)
    {
        public bool VisualLane => Armed && Candidate && Demand && !Blocked;
    }

    internal static PoolRentContext ForMount(Element element, ElementModifiers? modifiers, PoolRentContext outer)
    {
        bool candidate = VisualLaneTypes.Contains(element.GetType());
        bool setters = candidate && HasSetters(element);
        // ModifierTable.VisualOwningModifiers (spec 066 §9.1), for the layers landed so far, when the
        // feature takes the visual at mount. InteractionStates takes it on the first state change,
        // an exit-only transition on removal, and a scroll link without a ScrollViewer never, so a
        // fresh control keeps writable facades until then and those don't count.
        bool demand = element.AnimationConfig is not null || element.ElementTransition?.GetEnterTransition() is not null
            || element.KeyframeAnimations is not null
            || element.LayoutAnimation is not null || element.ScrollAnimation?.ScrollViewer is not null
            || TakesVisual(element.ImplicitTransitions) || DeclaresTransform(modifiers)
            || (outer.Armed && outer.Transparent && outer.Demand);
        bool blocked = setters || modifiers is { OnMountAction: not null } or { OnUpdateAction: not null }
            || (outer.Armed && outer.Blocked);
        return new(Armed: true, Transparent: element is KeyedMemoElement, demand, blocked, candidate);
    }

    // Spec 066 layer 4: a transform modifier takes the visual at mount unless its value is the
    // identity (§3 invariant 2). A rented control's facades read identity, and setters block the
    // lane anyway, so the declared value decides. Layer 5: so does a .TransformOrigin other than
    // (0, 0), which starts its expression at mount.
    private static bool DeclaresTransform(ElementModifiers? m)
        => m is not null
            && ((m.Scale is { } scale && scale != Vector3.One) || (m.Rotation is { } rotation && rotation != 0)
                || (m.Translation is { } translation && translation != Vector3.Zero)
                || (m.CenterPoint is { } center && center != Vector3.Zero)
                || (m.TransformOrigin is { } origin && origin != Vector2.Zero));

    // The built-in element types whose handler rents a poolable control. Exact types: a record
    // derived from one may come with its own handler.
    private static readonly HashSet<Type> VisualLaneTypes =
    [
        typeof(BorderElement), typeof(StackElement), typeof(GridElement), typeof(CanvasElement),
        typeof(TextBlockElement), typeof(RichTextBlockElement), typeof(ScrollViewerElement),
        typeof(ViewboxElement), typeof(ProgressElement), typeof(ProgressRingElement),
        typeof(ImageElement), typeof(InfoBadgeElement), typeof(ButtonElement), typeof(TextBoxElement),
        typeof(ToggleSwitchElement),
    ];

    private static bool HasSetters(Element element) => element switch
    {
        BorderElement e => e.Setters.Length > 0,
        StackElement e => e.Setters.Length > 0,
        GridElement e => e.Setters.Length > 0,
        CanvasElement e => e.Setters.Length > 0,
        TextBlockElement e => e.Setters.Length > 0,
        RichTextBlockElement e => e.Setters.Length > 0,
        ScrollViewerElement e => e.Setters.Length > 0,
        ViewboxElement e => e.Setters.Length > 0,
        ProgressElement e => e.Setters.Length > 0,
        ProgressRingElement e => e.Setters.Length > 0,
        ImageElement e => e.Setters.Length > 0,
        InfoBadgeElement e => e.Setters.Length > 0,
        ButtonElement e => e.Setters.Length > 0,
        TextBoxElement e => e.Setters.Length > 0,
        ToggleSwitchElement e => e.Setters.Length > 0,
        _ => true,
    };
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
                if (rs.DecoratorElement is not null) V1Protocol.OverlayLifecycle.ForgetFlyoutOwner(fe);
                ClearTrackedProducerRefs(fe);
                ReferenceEdgeWiring.TeardownReferenceEdges(fe);
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
                rs.PendingLabeledBy = null;
                rs.UnmountAction = null;
                rs.UnmountActionOwner = null;
                ClearRefreshSnapshots(rs);
                rs.Element = null;
                rs.DecoratorElement = null;
                rs.ModifierTargets = null;
                rs.MemoTransition = null;
                rs.MemoWrapsDecorator = false;
            }
            // Clear Reactor-set DataContext (FrameworkElement-only DP).
            fe.ClearValue(FrameworkElement.DataContextProperty);
            // The element pointer is gone; so is what was published about it, even when the
            // pool then declines the control (pooling disabled, full, or not poolable).
            ClearPublishedSource(fe);

            _pool.Return(fe);
        }

        // Author-defined extra reset runs last (per Q18 contract).
        policy?.Reset?.Invoke(control);
    }
}
