using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Spec 067 §4.2 row C: the conversions a converted modifier applies before its write. A slot's
/// <c>Converter</c> names a method here, and the generated apply arm calls it.
/// </summary>
internal static class ModifierConverters
{
    /// <summary><c>IsVisible</c>: <see langword="true"/> is <see cref="Visibility.Visible"/>, <see langword="false"/> is <see cref="Visibility.Collapsed"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Visibility ToVisibility(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
