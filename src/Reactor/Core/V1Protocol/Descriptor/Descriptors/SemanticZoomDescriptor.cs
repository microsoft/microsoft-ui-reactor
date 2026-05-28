using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 deferred specialized controls — descriptor variant of
/// the hand-coded SemanticZoom mount/update arms.
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class SemanticZoomDescriptor
{
    private static readonly NamedSlots<SemanticZoomElement, WinUI.SemanticZoom> ChildrenStrategy =
        new NamedSlots<SemanticZoomElement, WinUI.SemanticZoom>(new[]
        {
            new NamedSlot<SemanticZoomElement, WinUI.SemanticZoom>(
                Name: "ZoomedInView",
                GetChild: static e => e.ZoomedInView,
                SetChild: static (c, ui) =>
                {
                    if (ui is WinUI.ISemanticZoomInformation info) c.ZoomedInView = info;
                })
            {
                GetCurrentChild = static c => c.ZoomedInView as UIElement,
            },
            new NamedSlot<SemanticZoomElement, WinUI.SemanticZoom>(
                Name: "ZoomedOutView",
                GetChild: static e => e.ZoomedOutView,
                SetChild: static (c, ui) =>
                {
                    if (ui is WinUI.ISemanticZoomInformation info) c.ZoomedOutView = info;
                })
            {
                GetCurrentChild = static c => c.ZoomedOutView as UIElement,
            },
        });

    public static readonly ControlDescriptor<SemanticZoomElement, WinUI.SemanticZoom> Descriptor =
        new ControlDescriptor<SemanticZoomElement, WinUI.SemanticZoom>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        };
}
