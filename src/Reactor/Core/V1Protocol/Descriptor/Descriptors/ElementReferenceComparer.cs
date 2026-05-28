using System.Collections.Generic;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Reference-identity comparer over <c>Element?</c>. Used by descriptors
/// whose bridged props (typically <c>Flyout</c>-style controlled props on
/// the button family) should only rebuild the native control when the
/// author swapped to a *different* Element instance — the default
/// record-derived equality would compare structurally and miss content
/// swaps that produced a structurally-equal Element.
///
/// <para>Promoted to a shared internal type so the three button-family
/// descriptors (<c>DropDownButton</c>, <c>SplitButton</c>,
/// <c>ToggleSplitButton</c>) and any future bridged-Flyout descriptor
/// share one implementation.</para>
/// </summary>
internal sealed class ElementReferenceComparer : IEqualityComparer<Element?>
{
    public static readonly ElementReferenceComparer Instance = new();
    public bool Equals(Element? x, Element? y) => ReferenceEquals(x, y);
    public int GetHashCode(Element obj) => global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
