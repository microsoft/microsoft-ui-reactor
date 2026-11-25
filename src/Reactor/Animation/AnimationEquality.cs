using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using XamlTransition = Microsoft.UI.Xaml.Media.Animation.Transition;

namespace Microsoft.UI.Reactor.Animation;

/// <summary>
/// Value equality for animation declarations that modifiers rebuild on every render (issue #1702).
/// Element skip checks compare these, so two renders that declare the same animation compare
/// equal even though each allocated fresh arrays and WinUI objects. Unknown WinUI types fall
/// back to reference equality. Reading WinUI objects requires the UI thread, like reconcile.
/// </summary>
internal static class AnimationEquality
{
    internal static bool SequenceEqual<T>(T[]? a, T[]? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Length != b.Length) return false;
        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < a.Length; i++)
            if (!comparer.Equals(a[i], b[i])) return false;
        return true;
    }

    internal static int SequenceHash<T>(T[]? items)
    {
        if (items is null) return 0;
        var hash = new HashCode();
        foreach (var item in items) hash.Add(item);
        return hash.ToHashCode();
    }

    /// <summary>Equality for the implicit transitions behind <c>.OpacityTransition()</c> and friends.</summary>
    internal static bool ImplicitTransitionEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return true;
        return (a, b) switch
        {
            (ScalarTransition sa, ScalarTransition sb) => sa.Duration == sb.Duration,
            (Vector3Transition va, Vector3Transition vb) => va.Duration == vb.Duration && va.Components == vb.Components,
            (BrushTransition ba, BrushTransition bb) => ba.Duration == bb.Duration,
            _ => false,
        };
    }

    internal static bool ThemeTransitionsEqual(XamlTransition[]? a, XamlTransition[]? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (!ThemeTransitionEqual(a[i], b[i])) return false;
        return true;
    }

    /// <summary>
    /// Equality for WinUI's theme transitions, by type and settable properties. A transition type
    /// missing from this table compares by reference, which only costs a skipped update.
    /// </summary>
    private static bool ThemeTransitionEqual(XamlTransition? a, XamlTransition? b)
    {
        if (ReferenceEquals(a, b)) return true;
        return (a, b) switch
        {
            (EntranceThemeTransition ea, EntranceThemeTransition eb) =>
                ea.FromHorizontalOffset == eb.FromHorizontalOffset && ea.FromVerticalOffset == eb.FromVerticalOffset
                && ea.IsStaggeringEnabled == eb.IsStaggeringEnabled,
            (ContentThemeTransition ca, ContentThemeTransition cb) =>
                ca.HorizontalOffset == cb.HorizontalOffset && ca.VerticalOffset == cb.VerticalOffset,
            (PopupThemeTransition pa, PopupThemeTransition pb) =>
                pa.FromHorizontalOffset == pb.FromHorizontalOffset && pa.FromVerticalOffset == pb.FromVerticalOffset,
            (RepositionThemeTransition ra, RepositionThemeTransition rb) => ra.IsStaggeringEnabled == rb.IsStaggeringEnabled,
            (EdgeUIThemeTransition ea, EdgeUIThemeTransition eb) => ea.Edge == eb.Edge,
            (PaneThemeTransition pa, PaneThemeTransition pb) => pa.Edge == pb.Edge,
            (NavigationThemeTransition na, NavigationThemeTransition nb) =>
                ReferenceEquals(na.DefaultNavigationTransitionInfo, nb.DefaultNavigationTransitionInfo),
            (AddDeleteThemeTransition, AddDeleteThemeTransition) => true,
            (ReorderThemeTransition, ReorderThemeTransition) => true,
            _ => false,
        };
    }

    /// <summary>
    /// A solid brush compares by color and opacity, the same rule element brush properties use.
    /// Other brushes compare by reference.
    /// </summary>
    internal static bool BrushesEqual(Brush? a, Brush? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a is SolidColorBrush sa && b is SolidColorBrush sb)
            return sa.Color == sb.Color && sa.Opacity == sb.Opacity;
        return false;
    }

    /// <summary>Hash for a value compared by reference.</summary>
    internal static int IdentityHash(object? value) => value is null ? 0 : RuntimeHelpers.GetHashCode(value);
}

/// <summary>
/// An array compared by its elements, for array-valued animation declarations held in
/// <see cref="Core.AnimationExtras"/>.
/// </summary>
internal readonly struct ValueArray<T>(T[]? items) : IEquatable<ValueArray<T>>
{
    internal T[]? Items { get; } = items;

    public bool Equals(ValueArray<T> other) => AnimationEquality.SequenceEqual(Items, other.Items);

    public override bool Equals(object? obj) => obj is ValueArray<T> other && Equals(other);

    public override int GetHashCode() => AnimationEquality.SequenceHash(Items);
}
