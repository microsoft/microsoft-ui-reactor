using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Windows.Media.Core;
using Windows.Media.Playback;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 deferred specialized controls — descriptor variant of
/// the hand-coded MediaPlayerElement mount/update arms.
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class MediaPlayerElementDescriptor
{
    public static readonly ControlDescriptor<MediaPlayerElementElement, WinUI.MediaPlayerElement> Descriptor =
        new ControlDescriptor<MediaPlayerElementElement, WinUI.MediaPlayerElement>
        {
            Children = new None<MediaPlayerElementElement, WinUI.MediaPlayerElement>(),
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.AreTransportControlsEnabled,
            set: static (c, v) => c.AreTransportControlsEnabled = v)
        .OneWay(
            get: static e => e.AutoPlay,
            set: static (c, v) => c.AutoPlay = v)
        .Imperative(
            mount: static (c, _) => SubscribeMediaPlayerEvents(c),
            update: static (_, _, _) => { })
        .Initial(
            get: static e => e.Source,
            set: static (c, v) =>
            {
                if (v is not null && global::System.Uri.TryCreate(v, global::System.UriKind.RelativeOrAbsolute, out var uri))
                    c.Source = MediaSource.CreateFromUri(uri);
            });

    private static void SubscribeMediaPlayerEvents(WinUI.MediaPlayerElement control)
    {
        var player = control.MediaPlayer;
        if (player is null) return;
        player.MediaOpened += (_, _) => DispatchToElement(control, static el => el.OnMediaOpened?.Invoke());
        player.MediaEnded += (_, _) => DispatchToElement(control, static el => el.OnMediaEnded?.Invoke());
        player.MediaFailed += (_, args) =>
        {
            var message = args.ErrorMessage ?? args.Error.ToString();
            DispatchToElement(control, el => el.OnMediaFailed?.Invoke(message));
        };
    }

    private static void DispatchToElement(FrameworkElement control, global::System.Action<MediaPlayerElementElement> body)
    {
        var dispatcher = control.DispatcherQueue;
        if (dispatcher is null) return;
        dispatcher.TryEnqueue(() =>
        {
            if (Reconciler.GetElementTag(control) is MediaPlayerElementElement element) body(element);
        });
    }
}
