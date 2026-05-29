using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 deferred specialized controls — descriptor variant of
/// the hand-coded ParallaxView mount/update arms.
/// </summary>
internal static class ParallaxViewDescriptor
{
    private static readonly SingleContent<ParallaxViewElement, WinUI.ParallaxView> ChildrenStrategy =
        new SingleContent<ParallaxViewElement, WinUI.ParallaxView>(
            GetChild: static e => e.Child,
            SetChild: static (c, ui) => c.Child = ui)
        {
            GetCurrentChild = static c => c.Child as UIElement,
        };

    public static readonly ControlDescriptor<ParallaxViewElement, WinUI.ParallaxView> Descriptor =
        new ControlDescriptor<ParallaxViewElement, WinUI.ParallaxView>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.VerticalShift,
            set: static (c, v) => c.VerticalShift = v)
        .OneWay(
            get: static e => e.HorizontalShift,
            set: static (c, v) => c.HorizontalShift = v)
        .OneWay(
            get: static e => e.VerticalSourceStartOffset,
            set: static (c, v) => c.VerticalSourceStartOffset = v)
        .OneWay(
            get: static e => e.VerticalSourceEndOffset,
            set: static (c, v) => c.VerticalSourceEndOffset = v)
        .OneWayConditional(
            get:         static e => e.Source,
            set:         static (c, v) => c.Source = v!,
            shouldWrite: static e => e.Source is not null,
            comparer:    UIElementReferenceComparer.Instance);

    private sealed class UIElementReferenceComparer : global::System.Collections.Generic.IEqualityComparer<UIElement?>
    {
        public static readonly UIElementReferenceComparer Instance = new();
        public bool Equals(UIElement? x, UIElement? y) => ReferenceEquals(x, y);
        public int GetHashCode(UIElement obj)
            => global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
