using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 completion — descriptor variant of the hand-coded
/// <c>MountAnnounceRegion</c> live-region helper.
///
/// <para><b>Coverage:</b> creates the hidden zero-size <see cref="WinUI.TextBlock"/>,
/// applies the UIA live-region attached properties, and connects the
/// <see cref="AnnounceHandle"/> once at Mount. The legacy update arm is static;
/// the descriptor mirrors that with a no-op Update lambda.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class AnnounceRegionDescriptor
{
    public static readonly ControlDescriptor<AnnounceRegionElement, WinUI.TextBlock> Descriptor =
        new ControlDescriptor<AnnounceRegionElement, WinUI.TextBlock>
        {
            Children = new None<AnnounceRegionElement, WinUI.TextBlock>(),
        }
        .Imperative(
            mount: static (tb, ann) =>
            {
                tb.Width = 0;
                tb.Height = 0;
                tb.Opacity = 0;
                tb.IsHitTestVisible = false;
                tb.IsTabStop = false;
                AutomationProperties.SetLiveSetting(tb, AutomationLiveSetting.Polite);
                AutomationProperties.SetAccessibilityView(tb, AccessibilityView.Raw);
                ann.Handle.SetTextBlock(tb);
            },
            update: static (tb, oldAnn, newAnn) => { });
}
