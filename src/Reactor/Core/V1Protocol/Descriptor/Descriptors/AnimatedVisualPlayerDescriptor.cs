using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 deferred specialized controls — descriptor variant of
/// the hand-coded AnimatedVisualPlayer mount/update arms.
/// </summary>
internal static class AnimatedVisualPlayerDescriptor
{
    public static readonly ControlDescriptor<AnimatedVisualPlayerElement, WinUI.AnimatedVisualPlayer> Descriptor =
        new ControlDescriptor<AnimatedVisualPlayerElement, WinUI.AnimatedVisualPlayer>
        {
            Children = new None<AnimatedVisualPlayerElement, WinUI.AnimatedVisualPlayer>(),
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.AutoPlay,
            set: static (c, v) => c.AutoPlay = v);
}
