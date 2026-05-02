using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Declarative selector for the WinUI <see cref="SystemBackdrop"/> applied to a
/// Reactor-hosted <see cref="Microsoft.UI.Xaml.Window"/>. Set via the
/// <see cref="Microsoft.UI.Reactor.BackdropExtensions.Backdrop{T}(T, BackdropKind)"/>
/// modifier on a Reactor root tree.
/// </summary>
/// <remarks>
/// Spec 033 §6. Mica/Acrylic backdrops are a Windows 11 (build 22000+) feature.
/// On Windows 10 the WinUI <see cref="Microsoft.UI.Xaml.Window.SystemBackdrop"/>
/// setter no-ops; we delegate to that behavior rather than reimplementing the
/// fallback ladder.
/// </remarks>
public enum BackdropKind
{
    /// <summary>No backdrop. Clears any previously-set backdrop on the host window.</summary>
    None,

    /// <summary>Mica (window-tint material). Best for primary application surfaces on Win11.</summary>
    Mica,

    /// <summary>Mica (alt). The same material with a slightly different luminance/tint balance.</summary>
    MicaAlt,

    /// <summary>Desktop Acrylic (translucent blur). Best for transient surfaces — flyouts, dialogs.</summary>
    DesktopAcrylic,

    /// <summary>Acrylic (thin variant). Subtler blur than <see cref="DesktopAcrylic"/>.</summary>
    AcrylicThin,
}

/// <summary>
/// Tagged value stored in <see cref="ElementModifiers.Backdrop"/>. Holds either a
/// <see cref="BackdropKind"/> selector (the common case, materialized by the host
/// into a built-in WinUI backdrop) or a custom factory delegate (escape hatch for
/// tinted Mica or third-party <see cref="SystemBackdrop"/> subclasses) — never
/// both, never neither. The "exactly one set" invariant is enforced by the
/// private constructor; construct via <see cref="Of(BackdropKind)"/> or
/// <see cref="Of(global::System.Func{SystemBackdrop})"/>.
/// </summary>
/// <remarks>
/// Equality is value-based on <see cref="Kind"/> and reference-based on
/// <see cref="Factory"/> — matching the change-detection in
/// <c>BackdropApplier</c>, which uses <c>ReferenceEquals</c> on the factory.
/// Two factory choices compare equal only if they hold the same delegate
/// instance; callers who want stable identity across re-renders should hold
/// the factory in a captured local or a hook state cell.
/// </remarks>
public sealed record BackdropChoice
{
    /// <summary>The kind selector, or null when this choice carries a factory.</summary>
    public BackdropKind? Kind { get; }

    /// <summary>Custom factory; null when this choice carries a kind selector.</summary>
    public global::System.Func<SystemBackdrop?>? Factory { get; }

    private BackdropChoice(BackdropKind? kind, global::System.Func<SystemBackdrop?>? factory)
    {
        Kind = kind;
        Factory = factory;
    }

    /// <summary>Construct from a kind selector.</summary>
    public static BackdropChoice Of(BackdropKind kind) => new(kind, factory: null);

    /// <summary>Construct from a factory delegate.</summary>
    public static BackdropChoice Of(global::System.Func<SystemBackdrop?> factory) =>
        new(kind: null, factory ?? throw new global::System.ArgumentNullException(nameof(factory)));

    /// <summary>
    /// Reference equality on <see cref="Factory"/> (rather than the record-default
    /// structural delegate equality) so two distinct closures wrapping the same
    /// method group compare unequal — matches <c>BackdropApplier</c>'s
    /// <c>ReferenceEquals</c> change-detection.
    /// </summary>
    public bool Equals(BackdropChoice? other) =>
        other is not null
        && Kind == other.Kind
        && ReferenceEquals(Factory, other.Factory);

    public override int GetHashCode() =>
        global::System.HashCode.Combine(
            Kind,
            Factory is null
                ? 0
                : global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Factory));
}
