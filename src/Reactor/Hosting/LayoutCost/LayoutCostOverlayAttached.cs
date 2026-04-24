using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Hosting.LayoutCost;

/// <summary>
/// Attached properties published by the layout-cost overlay. Currently only
/// exposes <see cref="IsOverlayChromeProperty"/>: any UIElement flagged true
/// (or any descendant of one) has its ETW layout events excluded from
/// attribution so the overlay's own chrome (hover readout, pin panel) never
/// appears in its own numbers.
/// </summary>
public static class LayoutCostOverlayAttached
{
    public static readonly DependencyProperty IsOverlayChromeProperty =
        DependencyProperty.RegisterAttached(
            "IsOverlayChrome",
            typeof(bool),
            typeof(LayoutCostOverlayAttached),
            new PropertyMetadata(false));

    public static bool GetIsOverlayChrome(UIElement element)
        => (bool)element.GetValue(IsOverlayChromeProperty);

    public static void SetIsOverlayChrome(UIElement element, bool value)
        => element.SetValue(IsOverlayChromeProperty, value);
}
