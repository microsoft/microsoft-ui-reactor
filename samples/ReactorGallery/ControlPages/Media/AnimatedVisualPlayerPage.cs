using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.AnimatedVisuals;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Media;

class AnimatedVisualPlayerPage : Component
{
    public override Element Render()
    {
        return ScrollView(VStack(16,
            PageHeader("AnimatedVisualPlayer", "Plays vector (Lottie/codegen) animations. Uses WinUI's built-in animated visuals here."),

            SampleCard("Looping animation",
                HStack(24,
                    AnimatedVisualPlayer().Size(64, 64).OnMount(el =>
                    {
                        var p = (AnimatedVisualPlayer)el;
                        p.Source = new AnimatedGlobalNavigationButtonVisualSource();
                        _ = p.PlayAsync(0, 1, true);
                    }),
                    AnimatedVisualPlayer().Size(64, 64).OnMount(el =>
                    {
                        var p = (AnimatedVisualPlayer)el;
                        p.Source = new AnimatedSettingsVisualSource();
                        _ = p.PlayAsync(0, 1, true);
                    })),
                sourceCode: @"
AnimatedVisualPlayer().Size(64, 64).OnMount(el =>
{
    var p = (AnimatedVisualPlayer)el;                 // OnMount runs once — .Set re-applies on every update
    p.Source = new AnimatedGlobalNavigationButtonVisualSource();
    _ = p.PlayAsync(0, 1, loop: true);
})
// Point Source at your own IAnimatedVisualSource (e.g. a LottieVisualSource
// or a codegen class produced by the Lottie/Windows toolchain).
")
        ).Margin(36, 24, 36, 36));
    }
}
