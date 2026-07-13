using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Media;

class MediaPlayerElementPage : Component
{
    // A small, widely-used Creative Commons test clip.
    const string SampleVideo =
        "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4";

    public override Element Render()
    {
        return ScrollView(VStack(16,
            PageHeader("MediaPlayerElement", "Embeds audio/video playback with built-in transport controls."),

            SampleCard("Video with transport controls",
                VStack(8,
                    MediaPlayerElement(SampleVideo).Height(280).Width(480),
                    Caption("Streams over the network — playback requires an internet connection.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
MediaPlayerElement(""https://.../BigBuckBunny.mp4"")
    .Height(280).Width(480)
// Transport controls are enabled by default; AutoPlay is opt-in:
//   .Set(mpe => mpe.AutoPlay = true)
")
        ).Margin(36, 24, 36, 36));
    }
}
