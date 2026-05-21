using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Extension method for setting flex attached properties on elements.
/// </summary>
public static class FlexExtensions
{
    // <snippet:flex-modifier>
    public static T Flex<T>(this T el,
        double grow = 0,
        double shrink = 1,
        double? basis = null,
        double? minWidth = null,
        double? minHeight = null,
        FlexAlign? alignSelf = null,
        FlexPositionType position = FlexPositionType.Relative,
        double? left = null,
        double? top = null,
        double? right = null,
        double? bottom = null
    ) where T : Element
    {
        if (grow < 0 || double.IsNaN(grow) || double.IsInfinity(grow))
            throw new ArgumentOutOfRangeException(nameof(grow), "Grow must be a non-negative, finite value.");

        if (shrink < 0 || double.IsNaN(shrink) || double.IsInfinity(shrink))
            throw new ArgumentOutOfRangeException(nameof(shrink), "Shrink must be a non-negative, finite value.");

        if (minWidth is { } mw && (mw < 0 || double.IsNaN(mw) || double.IsInfinity(mw)))
            throw new ArgumentOutOfRangeException(nameof(minWidth), "MinWidth must be a non-negative, finite value (or null for CSS `min-width: auto`).");

        if (minHeight is { } mh && (mh < 0 || double.IsNaN(mh) || double.IsInfinity(mh)))
            throw new ArgumentOutOfRangeException(nameof(minHeight), "MinHeight must be a non-negative, finite value (or null for CSS `min-height: auto`).");

        return (T)el.SetAttached(new FlexAttached(grow, shrink, basis, minWidth, minHeight, alignSelf, position, left, top, right, bottom));
    }
    // </snippet:flex-modifier>
}
