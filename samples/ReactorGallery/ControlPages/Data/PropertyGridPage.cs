using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Data;

class PropertyGridPage : Component
{
    // Reflection-based INPC model. Attributes drive category grouping and
    // read-only/hidden handling in the generated editor.
    sealed class Settings : global::System.ComponentModel.INotifyPropertyChanged
    {
        string _name = "Player";
        [PropertyCategory("Appearance")]
        public string Name { get => _name; set { if (_name != value) { _name = value; Raise(nameof(Name)); } } }

        bool _visible = true;
        [PropertyCategory("Appearance")]
        public bool Visible { get => _visible; set { if (_visible != value) { _visible = value; Raise(nameof(Visible)); } } }

        double _opacity = 1.0;
        [PropertyCategory("Appearance")]
        public double Opacity { get => _opacity; set { if (_opacity != value) { _opacity = value; Raise(nameof(Opacity)); } } }

        double _x;
        [PropertyCategory("Transform")]
        [PropertyDisplayName("X Position")]
        public double X { get => _x; set { if (_x != value) { _x = value; Raise(nameof(X)); } } }

        double _y;
        [PropertyCategory("Transform")]
        [PropertyDisplayName("Y Position")]
        public double Y { get => _y; set { if (_y != value) { _y = value; Raise(nameof(Y)); } } }

        [PropertyReadOnly]
        [PropertyCategory("Info")]
        public string Id { get; } = Guid.NewGuid().ToString()[..8];

        public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        void Raise(string n) => PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(n));
    }

    public override Element Render()
    {
        var settings = UseRef(new Settings());
        UseObservable(settings.Current);
        var registry = UseMemo(() => new TypeRegistry());

        return ScrollView(VStack(16,
            PageHeader("PropertyGrid", "Generates a categorized editor UI from an object's properties via reflection."),

            SampleCard("Reflection-based editor",
                VStack(8,
                    Border(PropertyGrid(settings.Current, registry))
                        .Background(Theme.SubtleFill).CornerRadius(6).Padding(4).Width(360),
                    TextBlock($"Live: Name={settings.Current.Name}, Visible={settings.Current.Visible}, " +
                              $"Opacity={settings.Current.Opacity:F2}, X={settings.Current.X:F0}, Y={settings.Current.Y:F0}")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
class Settings : INotifyPropertyChanged
{
    [PropertyCategory(""Appearance"")] public string Name { get; set; }
    [PropertyCategory(""Transform"")] [PropertyDisplayName(""X Position"")] public double X { get; set; }
    [PropertyReadOnly] public string Id { get; }
}

var settings = UseRef(new Settings());
UseObservable(settings.Current);              // re-render on INPC changes
var registry = UseMemo(() => new TypeRegistry());
PropertyGrid(settings.Current, registry)")
        ).Margin(36, 24, 36, 36));
    }
}
