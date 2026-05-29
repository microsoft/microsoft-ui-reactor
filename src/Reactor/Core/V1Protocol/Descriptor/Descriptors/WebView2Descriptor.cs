using System.Diagnostics.CodeAnalysis;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 deferred specialized controls — descriptor variant of
/// the hand-coded WebView2 mount/update arms.
/// </summary>
internal static class WebView2Descriptor
{
    private static readonly TypedEventHandler<WinUI.WebView2, CoreWebView2NavigationStartingEventArgs>
        NavigationStartingTrampoline = (s, args) =>
        {
            if (global::System.Uri.TryCreate(args.Uri, global::System.UriKind.RelativeOrAbsolute, out var uri))
                (Reconciler.GetElementTag(s) as WebView2Element)?.OnNavigationStarting?.Invoke(uri);
        };

    private static readonly TypedEventHandler<WinUI.WebView2, CoreWebView2NavigationCompletedEventArgs>
        NavigationCompletedTrampoline = (s, _) =>
            (Reconciler.GetElementTag(s) as WebView2Element)?.OnNavigationCompleted?.Invoke(s.Source);

    private static readonly TypedEventHandler<WinUI.WebView2, CoreWebView2WebMessageReceivedEventArgs>
        WebMessageReceivedTrampoline = (s, args) =>
        {
            if (Reconciler.GetElementTag(s) is not WebView2Element { OnWebMessageReceived: { } handler }) return;
            string payload;
            try { payload = args.TryGetWebMessageAsString(); }
            catch { payload = args.WebMessageAsJson; }
            handler(payload);
        };

    private static readonly TypedEventHandler<WinUI.WebView2, WinUI.CoreWebView2InitializedEventArgs>
        CoreInitializedTrampoline = (s, _) =>
            (Reconciler.GetElementTag(s) as WebView2Element)?.OnCoreWebView2Initialized?.Invoke();

    public static readonly ControlDescriptor<WebView2Element, WinUI.WebView2> Descriptor =
        new ControlDescriptor<WebView2Element, WinUI.WebView2>
        {
            Children = new None<WebView2Element, WinUI.WebView2>(),
            GetSetters = static e => e.Setters,
        }
        .HandCodedEvent<WebView2EventPayload,
            TypedEventHandler<WinUI.WebView2, CoreWebView2NavigationStartingEventArgs>>(
            subscribe:        static (c, h) => c.NavigationStarting += h,
            callbackPresent:  static e => e.OnNavigationStarting,
            trampoline:       NavigationStartingTrampoline,
            slotIsNull:       static p => p.NavigationStartingTrampoline is null,
            setSlot:          static (p, h) => p.NavigationStartingTrampoline = h)
        .HandCodedEvent<WebView2EventPayload,
            TypedEventHandler<WinUI.WebView2, CoreWebView2NavigationCompletedEventArgs>>(
            subscribe:        static (c, h) => c.NavigationCompleted += h,
            callbackPresent:  static e => e.OnNavigationCompleted,
            trampoline:       NavigationCompletedTrampoline,
            slotIsNull:       static p => p.NavigationCompletedTrampoline is null,
            setSlot:          static (p, h) => p.NavigationCompletedTrampoline = h)
        .HandCodedEvent<WebView2EventPayload,
            TypedEventHandler<WinUI.WebView2, CoreWebView2WebMessageReceivedEventArgs>>(
            subscribe:        static (c, h) => c.WebMessageReceived += h,
            callbackPresent:  static e => e.OnWebMessageReceived,
            trampoline:       WebMessageReceivedTrampoline,
            slotIsNull:       static p => p.WebMessageReceivedTrampoline is null,
            setSlot:          static (p, h) => p.WebMessageReceivedTrampoline = h)
        .HandCodedEvent<WebView2EventPayload,
            TypedEventHandler<WinUI.WebView2, WinUI.CoreWebView2InitializedEventArgs>>(
            subscribe:        static (c, h) => c.CoreWebView2Initialized += h,
            callbackPresent:  static e => e.OnCoreWebView2Initialized,
            trampoline:       CoreInitializedTrampoline,
            slotIsNull:       static p => p.CoreInitializedTrampoline is null,
            setSlot:          static (p, h) => p.CoreInitializedTrampoline = h)
        .OneWayConditional(
            get:         static e => e.Source,
            set:         static (c, v) => c.Source = v!,
            shouldWrite: static e => e.Source is not null);
}
