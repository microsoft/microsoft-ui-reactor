using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Fluent extension methods for Canvas attached properties.
/// Usage: Rectangle().Canvas(left: 50, top: 100)
/// </summary>
public static class CanvasExtensions
{
    /// <summary>
    /// Sets Canvas attached properties (left, top) on this element.
    /// Only meaningful when the element is a child of a Canvas.
    /// </summary>
    public static T Canvas<T>(this T el, double left = 0, double top = 0) where T : Element =>
        (T)el.SetAttached(new CanvasAttached(left, top));

    /// <summary>
    /// Sets Canvas attached properties with an anchor. <paramref name="anchorX"/>/<paramref name="anchorY"/>
    /// are 0..1 fractions of the element's rendered size; the reconciler subtracts that fraction
    /// of the actual width/height from <paramref name="left"/>/<paramref name="top"/> after layout.
    /// 0,0 = top-left (legacy), 0.5,0.5 = centered on (left, top), 1,1 = bottom-right.
    /// </summary>
    public static T Canvas<T>(this T el, double left, double top, double anchorX, double anchorY) where T : Element =>
        (T)el.SetAttached(new CanvasAttached(left, top) { AnchorX = anchorX, AnchorY = anchorY });

    /// <summary>
    /// Centers this element on (<paramref name="x"/>, <paramref name="y"/>) within its parent Canvas.
    /// Equivalent to <c>.Canvas(x, y, anchorX: 0.5, anchorY: 0.5)</c>. Position is recomputed after
    /// layout, so the element does not need a known size at construction time.
    /// </summary>
    public static T CenterAt<T>(this T el, double x, double y) where T : Element =>
        (T)el.SetAttached(new CanvasAttached(x, y) { AnchorX = 0.5, AnchorY = 0.5 });
}
