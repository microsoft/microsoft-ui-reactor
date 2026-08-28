using Microsoft.UI.Composition;

namespace Microsoft.UI.Reactor.Navigation;

/// <summary>
/// Direction for slide-based transitions.
/// </summary>
public enum SlideDirection
{
    FromRight,
    FromLeft,
    FromBottom,
    FromTop,
}

/// <summary>
/// Controls whether a page's component tree is cached across navigations.
/// </summary>
public enum NavigationCacheMode
{
    /// <summary>Page is always unmounted on navigate-away and remounted on navigate-to.</summary>
    Disabled,
    /// <summary>Page is cached up to the host's CacheSize limit (LRU eviction).</summary>
    Enabled,
    /// <summary>Page is always cached and never evicted by LRU.</summary>
    Required,
}

/// <summary>
/// Abstract base for navigation transition definitions.
/// Concrete types describe the animation; the TransitionEngine (Phase 4) executes them.
/// Static factory methods provide a convenient API.
/// </summary>
public abstract record NavigationTransition
{
    /// <summary>
    /// The transition a host or navigation gets when it doesn't ask for one. Currently
    /// resolves to <see cref="Entrance"/>, which is what WinUI's <c>Frame</c> plays when
    /// <c>Navigate</c> is called with no <c>NavigationTransitionInfo</c>.
    /// </summary>
    /// <remarks>
    /// This is a policy alias, not a motion — it means "whatever Reactor defaults to". Use
    /// <see cref="Entrance"/> when you want the entrance motion specifically and don't want
    /// the call site to silently follow a future change of default.
    /// </remarks>
    public static readonly NavigationTransition Default = new EntranceTransition();

    /// <summary>No animation — instant swap.</summary>
    public static readonly NavigationTransition None = new SuppressTransition();

    /// <summary>
    /// Entrance transition — the incoming page slides up a short distance and fades in.
    /// This is WinUI's own default page transition
    /// (<c>EntranceNavigationTransitionInfo</c>, the "page refresh" animation), and the
    /// motion <see cref="Default"/> currently resolves to.
    /// </summary>
    public static NavigationTransition Entrance() => new EntranceTransition();

    /// <summary>
    /// Slide transition. With no arguments this is WinUI's slide specification — vertical,
    /// 600 ms, exponential easing.
    /// </summary>
    /// <remarks>
    /// Supplying <paramref name="duration"/>, <paramref name="easing"/>, or
    /// <paramref name="distance"/> switches to Reactor's customizable simultaneous slide,
    /// which is a structurally different animation — not the WinUI slide with one value
    /// changed. <see cref="SlideDirection.FromTop"/> is Reactor-only and always takes that
    /// path. Note the default direction is <see cref="SlideDirection.FromBottom"/>; pass
    /// <see cref="SlideDirection.FromRight"/> explicitly for a horizontal push.
    /// </remarks>
    public static NavigationTransition Slide(
        SlideDirection direction = SlideDirection.FromBottom,
        TimeSpan? duration = null,
        CompositionEasingFunction? easing = null,
        float? distance = null)
        => new SlideTransition
        {
            Direction = direction,
            Duration = duration,
            Easing = easing,
            Distance = distance,
        };

    /// <summary>Crossfade transition.</summary>
    public static NavigationTransition Fade(TimeSpan? duration = null)
        => new FadeTransition { Duration = duration };

    /// <summary>Drill-in (scale + fade from center) transition.</summary>
    public static NavigationTransition DrillIn(TimeSpan? duration = null)
        => new DrillInTransition { Duration = duration };

    /// <summary>
    /// Connected animation transition (stub — shared-element animation is not implemented yet,
    /// so this currently plays <see cref="Entrance"/>).
    /// </summary>
    public static NavigationTransition Connected(string animationKey)
        => new ConnectedTransition { AnimationKey = animationKey };

    /// <summary>
    /// Spring-physics slide transition. A Reactor extension with no WinUI counterpart, so it
    /// keeps a horizontal default rather than following <see cref="Slide"/>'s vertical one.
    /// </summary>
    public static NavigationTransition Spring(
        float dampingRatio = 0.6f,
        float period = 0.08f,
        SlideDirection direction = SlideDirection.FromRight)
        => new SpringSlideTransition
        {
            DampingRatio = dampingRatio,
            Period = period,
            Direction = direction,
        };
}

/// <summary>
/// Entrance transition — slide up + fade in on the incoming page. Mirrors WinUI's
/// <c>EntranceNavigationTransitionInfo</c>, the animation a <c>Frame</c> plays when
/// <c>Navigate</c> is called without a <c>NavigationTransitionInfo</c>.
/// </summary>
public sealed record EntranceTransition : NavigationTransition;

/// <summary>Slide transition — animate offset and opacity.</summary>
public sealed record SlideTransition : NavigationTransition
{
    public SlideDirection Direction { get; init; } = SlideDirection.FromBottom;
    public TimeSpan? Duration { get; init; }
    public CompositionEasingFunction? Easing { get; init; }
    /// <summary>
    /// Custom slide distance in pixels. Supplying one selects Reactor's customizable slide,
    /// where it is the distance travelled; leaving it null keeps whichever path the other
    /// properties select — WinUI's specification when they are all null, otherwise the
    /// customizable slide's own 200px default.
    /// </summary>
    public float? Distance { get; init; }
}

/// <summary>Crossfade transition — animate opacity on both visuals.</summary>
public sealed record FadeTransition : NavigationTransition
{
    public TimeSpan? Duration { get; init; }
}

/// <summary>
/// WinUI drill-in transition. Supplying a duration opts into Reactor's
/// customizable symmetric drill-in behavior.
/// </summary>
public sealed record DrillInTransition : NavigationTransition
{
    public TimeSpan? Duration { get; init; }
}

/// <summary>Connected animation transition (stub — full implementation deferred to Phase 6).</summary>
public sealed record ConnectedTransition : NavigationTransition
{
    public required string AnimationKey { get; init; }
}

/// <summary>
/// Spring-physics slide transition. Unlike <see cref="SlideTransition"/> this is a Reactor
/// extension with no WinUI counterpart, so it keeps its own horizontal default rather than
/// following WinUI's vertical slide specification.
/// </summary>
public sealed record SpringSlideTransition : NavigationTransition
{
    public float DampingRatio { get; init; } = 0.6f;
    public float Period { get; init; } = 0.08f;
    public SlideDirection Direction { get; init; } = SlideDirection.FromRight;
}

/// <summary>No animation — instant swap.</summary>
public sealed record SuppressTransition : NavigationTransition;
