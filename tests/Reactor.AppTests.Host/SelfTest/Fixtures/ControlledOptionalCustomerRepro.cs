using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static partial class ControlledOptionalCustomerRepro
{
    private static int _registered;

    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureRegistered();

            var selectedCallbacks = 0;
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (gridStyle, setGridStyle) = ctx.UseState(false);
                return VStack(
                    new BasemapGalleryElement
                    {
                        GalleryViewStyle = gridStyle,
                        OnBasemapSelected = _ => selectedCallbacks++,
                    },
                    ToggleSwitch(gridStyle, setGridStyle, onContent: "Grid", offContent: "List"));
            });

            await Harness.Render();
            var gallery = H.FindControl<BasemapGalleryControl>(_ => true);
            H.Check("ControlledOptionalCustomerRepro_GalleryMounted", gallery is not null);
            if (gallery is null) return;

            gallery.SelectUserBasemap("streets");
            await Harness.Render();
            H.Check("ControlledOptionalCustomerRepro_UserSelectionRecorded", gallery.SelectedBasemap == "streets" && selectedCallbacks == 1);

            var toggle = H.FindControl<WinUI.ToggleSwitch>(_ => true);
            H.Check("ControlledOptionalCustomerRepro_SiblingToggleMounted", toggle is not null);
            if (toggle is null) return;

            toggle.IsOn = true;
            await Harness.Render();

            H.Check("ControlledOptionalCustomerRepro_UnsetSurvivesSiblingToggleRerender", gallery.SelectedBasemap == "streets");
            H.Check("ControlledOptionalCustomerRepro_SiblingPropUpdated", gallery.GalleryViewStyle);
        }
    }

    private static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;
        ControlRegistry.Register<BasemapGalleryElement, BasemapGalleryControl>(static () =>
            new DescriptorHandler<BasemapGalleryElement, BasemapGalleryControl>(Descriptor));
    }

    private static readonly ControlDescriptor<BasemapGalleryElement, BasemapGalleryControl> Descriptor =
        new ControlDescriptor<BasemapGalleryElement, BasemapGalleryControl>
        {
            Children = new None<BasemapGalleryElement, BasemapGalleryControl>(),
        }
        .OneWay(
            get: static e => e.GalleryViewStyle,
            set: static (c, v) => c.GalleryViewStyle = v)
        .Controlled<string?, EventArgs>(
            get: static e => e.SelectedBasemap,
            set: static (c, v) => c.SelectedBasemap = v,
            subscribe: static (fe, h) => ((BasemapGalleryControl)fe).SelectedBasemapChanged += h,
            unsubscribe: static (fe, h) => ((BasemapGalleryControl)fe).SelectedBasemapChanged -= h,
            callback: static e => e.OnBasemapSelected,
            readBack: static c => c.SelectedBasemap);

    private sealed record BasemapGalleryElement : Element
    {
        public Optional<string?> SelectedBasemap { get; init; } = default;
        public Action<string?>? OnBasemapSelected { get; init; }
        public bool GalleryViewStyle { get; init; }
        internal override bool HasCallbacks => OnBasemapSelected is not null;
    }

    private sealed partial class BasemapGalleryControl : FrameworkElement
    {
        private string? _selectedBasemap;

        public event EventHandler<EventArgs>? SelectedBasemapChanged;

        public bool GalleryViewStyle { get; set; }

        public string? SelectedBasemap
        {
            get => _selectedBasemap;
            set
            {
                if (_selectedBasemap == value) return;
                _selectedBasemap = value;
                SelectedBasemapChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void SelectUserBasemap(string basemap) => SelectedBasemap = basemap;
    }
}
