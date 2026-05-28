using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 deferred specialized controls — descriptor variant of
/// the hand-coded MapControl mount/update arms.
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class MapControlDescriptor
{
    public static readonly ControlDescriptor<MapControlElement, WinUI.MapControl> Descriptor =
        new ControlDescriptor<MapControlElement, WinUI.MapControl>
        {
            Children = new None<MapControlElement, WinUI.MapControl>(),
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.ZoomLevel,
            set: static (c, v) => c.ZoomLevel = v)
        .OneWayConditional(
            get:         static e => e.MapServiceToken,
            set:         static (c, v) => c.MapServiceToken = v!,
            shouldWrite: static e => e.MapServiceToken is not null);
}
