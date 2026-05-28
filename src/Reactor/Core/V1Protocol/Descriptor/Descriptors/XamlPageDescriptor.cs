using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 completion — decorator-style port of XamlPageElement.
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class XamlPageDescriptor
{
    public static readonly IDecoratorElementHandler<XamlPageElement> Handler = new XamlPageHandler();

    private sealed class XamlPageHandler : IDecoratorElementHandler<XamlPageElement>
    {
        public UIElement Mount(MountContext ctx, XamlPageElement element)
        {
            var frame = new WinUI.Frame();
            frame.Navigate(element.PageType, element.Parameter);
            Reconciler.SetElementTag(frame, element);
            return frame;
        }

        public UIElement Update(UpdateContext ctx, XamlPageElement oldEl, XamlPageElement newEl, UIElement control)
        {
            var frame = (WinUI.Frame)control;
            if (oldEl.PageType != newEl.PageType || !Equals(oldEl.Parameter, newEl.Parameter))
                frame.Navigate(newEl.PageType, newEl.Parameter);
            Reconciler.SetElementTag(frame, newEl);
            return frame;
        }

        public V1UnmountDisposition Unmount(UnmountContext ctx, XamlPageElement? element, UIElement control)
        {
            var frame = (WinUI.Frame)control;
            frame.Content = null;
            Reconciler.DetachReactorState(frame);
            return V1UnmountDisposition.SkipPool;
        }
    }
}
