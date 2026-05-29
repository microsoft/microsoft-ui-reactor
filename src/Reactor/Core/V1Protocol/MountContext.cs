using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

/// <summary>
/// Spec 047 §4 / §14 Phase 1 (1.6) — mount-time context passed to
/// <see cref="IElementHandler{TElement,TControl}.Mount"/>.
///
/// <para><b>UI-thread guarantee (Q14):</b> handlers run on the reconciler's
/// UI dispatcher. Mount bodies may freely access control state without
/// synchronization. Calls from other threads throw in Debug builds today;
/// Phase 1 plan is to measure first before tightening to unconditional
/// throw. See <c>docs/specs/047/phase1-results/q14-dispatcher-affinity.md</c>.
/// </para>
///
/// <para><b>Allocation contract:</b> <see cref="MountContext"/> is a
/// <c>readonly ref struct</c>, so it cannot escape the call stack and never
/// allocates on its own. Hot path (Mount body) pays only the dictionary
/// lookup + interface call already incurred by the V1HandlerRegistry
/// dispatch.</para>
///
/// <para><b>Sample skeleton</b> (per spec §4 ToggleSwitchHandler example):
/// <code>
/// public sealed class ToggleSwitchHandler : IElementHandler&lt;ToggleSwitchElement, ToggleSwitch&gt;
/// {
///     public ToggleSwitch Mount(MountContext ctx, ToggleSwitchElement el)
///     {
///         var ctrl = ctx.RentControl&lt;ToggleSwitch&gt;();
///         ctrl.IsOn = el.IsOn;
///         ctrl.OnContent = el.OnContent;
///         var bind = ctx.BindFor(ctrl, el);
///         bind.OnCustomEvent&lt;RoutedEventArgs&gt;(
///             (c, h) =&gt; ((ToggleSwitch)c).Toggled += (s, e) =&gt; h(s, e),
///             (c, h) =&gt; { /* trampoline lives for the control's lifetime */ },
///             (e, args) =&gt; e.OnIsOnChanged?.Invoke(((ToggleSwitch)args.OriginalSource).IsOn));
///         return ctrl;
///     }
///     public void Update(UpdateContext ctx, ToggleSwitchElement oldEl, ToggleSwitchElement newEl, ToggleSwitch ctrl)
///     {
///         if (oldEl.IsOn != newEl.IsOn)
///             ctx.BindFor(ctrl, newEl).WriteSuppressed(() =&gt; ctrl.IsOn = newEl.IsOn);
///     }
/// }
/// </code></para>
/// </summary>
// <snippet:mount-context>
[Experimental("REACTOR_V1_PREVIEW")]
public readonly ref struct MountContext
{
    private readonly Reconciler _reconciler;
    private readonly Action _requestRerender;

    internal MountContext(Reconciler reconciler, Action requestRerender)
    {
        _reconciler = reconciler;
        _requestRerender = requestRerender;
    }

    /// <summary>The rerender callback for the owning component subtree.</summary>
    public Action RequestRerender => _requestRerender;

    /// <summary>The owning reconciler. Provided as an escape hatch for handlers
    /// that need to forward a child mount through some non-strategy path.</summary>
    public Reconciler Reconciler => _reconciler;

    /// <summary>Mount a child element through the reconciler. The returned
    /// <see cref="UIElement"/> is whatever the reconciler decided to mount
    /// (possibly null for <c>EmptyElement</c>).</summary>
    public UIElement? MountChild(Element child) => _reconciler.Mount(child, _requestRerender);

    /// <summary>Apply a setter array to the control. Equivalent to the public
    /// <see cref="Reconciler.ApplySetters{T}(Action{T}[], T)"/> helper; provided
    /// on the context for symmetry with handler-authored mount bodies.</summary>
    public void ApplySetters<T>(Action<T>[] setters, T control) where T : class
        => Reconciler.ApplySetters(setters, control);

    /// <summary>Construct a per-binding event helper for the given control + element.
    /// The returned binding closes over the control identity, not the element
    /// — handlers re-fetch the live element via <c>ReactorState.Element</c> on
    /// each event fire, so the same binding survives element re-renders.</summary>
    public ReactorBinding<TElement> BindFor<TElement>(FrameworkElement control, TElement element)
        where TElement : Element
        => new ReactorBinding<TElement>(_reconciler, control, element);

    /// <summary>Rent a control instance from the per-type pool, or allocate
    /// a fresh one if the pool is empty / the policy opts out.</summary>
    public T RentControl<T>(PoolPolicy<T>? policy = null, Func<T>? factory = null) where T : class, new()
        => _reconciler.RentControl(policy, factory);

    /// <summary>Push a typed context value for the duration of the returned
    /// <see cref="IDisposable"/>. Used by handlers that mount children which
    /// must see an author-supplied context.</summary>
    public IDisposable PushContext<T>(Context<T> context, T value) => _reconciler.PushContextDisposable(context, value);

    /// <summary>Push a stagger scope; children mounted inside the scope
    /// consume stagger indices for their enter transitions.</summary>
    public IDisposable PushStaggerScope(TimeSpan delay) => _reconciler.PushStaggerScopeDisposable(delay);

    /// <summary>Escape hatch (Q11) — attach a raw routed handler directly to
    /// the control. Handlers should prefer the typed <c>On*</c> family on
    /// <see cref="ReactorBinding{TElement}"/>; use this only for events the
    /// binding doesn't cover (e.g. <c>UIElement.PreviewKeyDown</c> on a
    /// non-FrameworkElement, or app-side custom routed events).</summary>
    public void AddRawRoutedHandler(UIElement target, RoutedEvent re, Delegate h, bool handledEventsToo)
        => target.AddHandler(re, h, handledEventsToo);
}
// </snippet:mount-context>

/// <summary>
/// Spec 047 §4 / §14 Phase 1 (1.6) — update-time context passed to
/// <see cref="IElementHandler{TElement,TControl}.Update"/>.
///
/// Same shape as <see cref="MountContext"/> minus <c>RentControl</c> —
/// updates run against an existing control, so allocation is forbidden on
/// this path. UI-thread guarantee applies (see <see cref="MountContext"/>).
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
public readonly ref struct UpdateContext
{
    private readonly Reconciler _reconciler;
    private readonly Action _requestRerender;

    internal UpdateContext(Reconciler reconciler, Action requestRerender)
    {
        _reconciler = reconciler;
        _requestRerender = requestRerender;
    }

    public Action RequestRerender => _requestRerender;
    public Reconciler Reconciler => _reconciler;

    public UIElement? MountChild(Element child) => _reconciler.Mount(child, _requestRerender);
    public void ApplySetters<T>(Action<T>[] setters, T control) where T : class
        => Reconciler.ApplySetters(setters, control);
    public ReactorBinding<TElement> BindFor<TElement>(FrameworkElement control, TElement element)
        where TElement : Element
        => new ReactorBinding<TElement>(_reconciler, control, element);
    public IDisposable PushContext<T>(Context<T> context, T value) => _reconciler.PushContextDisposable(context, value);
    public IDisposable PushStaggerScope(TimeSpan delay) => _reconciler.PushStaggerScopeDisposable(delay);
    public void AddRawRoutedHandler(UIElement target, RoutedEvent re, Delegate h, bool handledEventsToo)
        => target.AddHandler(re, h, handledEventsToo);
}

/// <summary>
/// Spec 047 §4 / §14 Phase 1 (1.6) — unmount-time context. Handlers use
/// <see cref="ReturnControl{T}"/> to participate in the pool reset
/// contract; the engine takes care of detaching from the parent tree.
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
public readonly ref struct UnmountContext
{
    private readonly Reconciler _reconciler;

    internal UnmountContext(Reconciler reconciler)
    {
        _reconciler = reconciler;
    }

    public Action RequestRerender => static () => { };
    public Reconciler Reconciler => _reconciler;

    /// <summary>Return a control to its per-type pool via the engine's
    /// reset contract. Safe to call twice — the pool dedupes by stack cap.</summary>
    public void ReturnControl<T>(T control, PoolPolicy<T>? policy = null) where T : class
        => _reconciler.ReturnControl(control, policy);
}
