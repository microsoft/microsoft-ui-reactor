using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Media;

class MapControlPage : Component
{
    public override Element Render()
    {
        return ScrollView(VStack(16,
            PageHeader("MapControl", "Displays an interactive map. Tiles require a Bing Maps service token."),

            SampleCard("Interactive map",
                VStack(8,
                    MapControl(zoomLevel: 4).Height(320).Width(480)
                        .Background(Theme.SubtleFill).CornerRadius(6),
                    Caption("Supply a MapServiceToken to load map tiles: MapControl(mapServiceToken: \"<key>\", zoomLevel: 12).")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
MapControl(mapServiceToken: ""<your-key>"", zoomLevel: 12)
    .Height(320).Width(480)
// Pan and zoom with mouse/touch. Center and layers can be set via
//   .Set(map => { map.Center = ...; })
")
        ).Margin(36, 24, 36, 36));
    }
}
