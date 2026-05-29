using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 deferred specialized controls — descriptor variant of
/// the hand-coded AnnotatedScrollBar mount/update arms.
/// </summary>
internal static class AnnotatedScrollBarDescriptor
{
    public static readonly ControlDescriptor<AnnotatedScrollBarElement, WinUI.AnnotatedScrollBar> Descriptor =
        new ControlDescriptor<AnnotatedScrollBarElement, WinUI.AnnotatedScrollBar>
        {
            Children = new None<AnnotatedScrollBarElement, WinUI.AnnotatedScrollBar>(),
            GetSetters = static e => e.Setters,
        };
}
