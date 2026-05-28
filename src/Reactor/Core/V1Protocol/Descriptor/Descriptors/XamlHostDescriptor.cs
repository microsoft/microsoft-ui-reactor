using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 completion — decorator-style port of XamlHostElement.
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class XamlHostDescriptor
{
    public static readonly IDecoratorElementHandler<XamlHostElement> Handler = new XamlHostHandler();

    private sealed class XamlHostHandler : IDecoratorElementHandler<XamlHostElement>
    {
        public UIElement Mount(MountContext ctx, XamlHostElement element)
        {
            var control = element.Factory();
            element.Updater?.Invoke(control);
            Reconciler.SetElementTag(control, element);
            return control;
        }

        public UIElement Update(UpdateContext ctx, XamlHostElement oldEl, XamlHostElement newEl, UIElement control)
        {
            var frameworkElement = (FrameworkElement)control;
            newEl.Updater?.Invoke(frameworkElement);
            Reconciler.SetElementTag(frameworkElement, newEl);
            return frameworkElement;
        }

        public V1UnmountDisposition Unmount(UnmountContext ctx, XamlHostElement? element, UIElement control)
        {
            Reconciler.DetachReactorState((FrameworkElement)control);
            return V1UnmountDisposition.SkipPool;
        }
    }
}
