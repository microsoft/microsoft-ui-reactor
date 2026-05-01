using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

class PropertyGridDemo : Component
{
    // Immutable nested record — decomposes into Width/Height sub-properties
    record SpriteBounds(double Width, double Height)
    {
        public override string ToString() => $"{Width}\u00d7{Height}";
    }

    // Reflection-based model — no explicit TypeMetadata registration needed
    class SpriteSettings : global::System.ComponentModel.INotifyPropertyChanged
    {
        private string _name = "Player";
        [PropertyCategory("Appearance")]
        [PropertyDescription("Display name of the sprite")]
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(nameof(Name)); } }
        }

        private bool _visible = true;
        [PropertyCategory("Appearance")]
        [PropertyDescription("Whether the sprite is visible in the scene")]
        public bool Visible
        {
            get => _visible;
            set { if (_visible != value) { _visible = value; OnPropertyChanged(nameof(Visible)); } }
        }

        private double _x;
        [PropertyCategory("Transform")]
        [PropertyDisplayName("X Position")]
        [PropertyOrder(0)]
        public double X
        {
            get => _x;
            set { if (_x != value) { _x = value; OnPropertyChanged(nameof(X)); } }
        }

        private double _y;
        [PropertyCategory("Transform")]
        [PropertyDisplayName("Y Position")]
        [PropertyOrder(1)]
        public double Y
        {
            get => _y;
            set { if (_y != value) { _y = value; OnPropertyChanged(nameof(Y)); } }
        }

        private double _rotation;
        [PropertyCategory("Transform")]
        [PropertyOrder(2)]
        public double Rotation
        {
            get => _rotation;
            set { if (_rotation != value) { _rotation = value; OnPropertyChanged(nameof(Rotation)); } }
        }

        private SpriteBounds _bounds = new(64, 64);
        [PropertyCategory("Transform")]
        [PropertyDescription("Size of the sprite bounding box")]
        [PropertyOrder(3)]
        public SpriteBounds Bounds
        {
            get => _bounds;
            set { if (_bounds != value) { _bounds = value; OnPropertyChanged(nameof(Bounds)); } }
        }

        [PropertyHidden]
        public int InternalId { get; set; } = 42;

        [PropertyReadOnly]
        [PropertyCategory("Info")]
        [PropertyDescription("Unique identifier (auto-generated)")]
        public string Id { get; } = Guid.NewGuid().ToString()[..8];

        public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(name));
    }

    // Custom type with explicit TypeMetadata registration
    record RgbColor(byte R, byte G, byte B)
    {
        public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
    }

    enum BlendMode { Normal, Multiply, Screen, Overlay, Additive }

    class MaterialSettings : global::System.ComponentModel.INotifyPropertyChanged
    {
        private string _materialName = "Default";
        public string MaterialName
        {
            get => _materialName;
            set { if (_materialName != value) { _materialName = value; OnPropertyChanged(nameof(MaterialName)); } }
        }

        private BlendMode _blend = BlendMode.Normal;
        public BlendMode Blend
        {
            get => _blend;
            set { if (_blend != value) { _blend = value; OnPropertyChanged(nameof(Blend)); } }
        }

        private double _opacity = 1.0;
        public double Opacity
        {
            get => _opacity;
            set { if (_opacity != value) { _opacity = value; OnPropertyChanged(nameof(Opacity)); } }
        }

        private bool _castShadow = true;
        public bool CastShadow
        {
            get => _castShadow;
            set { if (_castShadow != value) { _castShadow = value; OnPropertyChanged(nameof(CastShadow)); } }
        }

        public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(name));
    }

    public override Element Render()
    {
        var (selectedItem, setSelectedItem) = UseState("Sprite");

        // Create registry with custom type metadata for RgbColor
        var registry = new TypeRegistry();
        registry.Register<RgbColor>(new TypeMetadata
        {
            Editor = (val, onChange) =>
            {
                var c = (RgbColor)val;
                return TextBlock(c.ToString()).SemiBold();
            },
            Decompose = val =>
            {
                var c = (RgbColor)val;
                return new List<FieldDescriptor>
                {
                    new() { Name = "R", FieldType = typeof(byte),
                            GetValue = _ => c.R, Order = 0 },
                    new() { Name = "G", FieldType = typeof(byte),
                            GetValue = _ => c.G, Order = 1 },
                    new() { Name = "B", FieldType = typeof(byte),
                            GetValue = _ => c.B, Order = 2 },
                };
            },
            Compose = (val, updates) =>
            {
                var c = (RgbColor)val;
                var r = updates.TryGetValue("R", out var ur) ? (byte)ur : c.R;
                var g = updates.TryGetValue("G", out var ug) ? (byte)ug : c.G;
                var b = updates.TryGetValue("B", out var ub) ? (byte)ub : c.B;
                return new RgbColor(r, g, b);
            },
            DisplayName = "RGB Color"
        });

        // Shared mutable INPC targets (UseRef for stable identity, UseObservable for re-render)
        var spriteRef = UseRef(new SpriteSettings());
        var materialRef = UseRef(new MaterialSettings());
        UseObservable(spriteRef.Current);
        UseObservable(materialRef.Current);

        // Immutable record with OnRootChanged
        var (color, setColor) = UseState(new RgbColor(255, 128, 0));

        var target = selectedItem switch
        {
            "Sprite" => (object)spriteRef.Current,
            "Material" => materialRef.Current,
            "Color" => color,
            _ => spriteRef.Current
        };

        Action<object>? onRootChanged = selectedItem == "Color"
            ? obj => setColor((RgbColor)obj)
            : null;

        return VStack(16,
            Heading("PropertyGrid Demo"),

            // Item selector
            HStack(8,
                TextBlock("Select target:"),
                ComboBox(new[] { "Sprite", "Material", "Color" },
                    selectedItem switch { "Sprite" => 0, "Material" => 1, "Color" => 2, _ => 0 },
                    i => setSelectedItem(i switch { 0 => "Sprite", 1 => "Material", 2 => "Color", _ => "Sprite" }))
            ),

            // Description
            selectedItem switch
            {
                "Sprite" => TextBlock("Reflection-based: INPC model with Reactor attributes, categories, hidden/read-only props").Foreground(SecondaryText),
                "Material" => TextBlock("Reflection-based: INPC class with enum (ComboBox), bool (ToggleSwitch), double (NumberBox)").Foreground(SecondaryText),
                "Color" => TextBlock("Custom TypeMetadata: immutable record with Editor + Decompose + Compose, OnRootChanged").Foreground(SecondaryText),
                _ => Empty()
            },

            // PropertyGrid
            ScrollView(
                PropertyGrid(target, registry, onRootChanged)
            ),

            // Live readback
            Border(
                VStack(4,
                    TextBlock("Live values (updated via INPC / OnRootChanged):").SemiBold(),
                    selectedItem switch
                    {
                        "Sprite" => TextBlock($"Name={spriteRef.Current.Name}, Visible={spriteRef.Current.Visible}, " +
                                         $"X={spriteRef.Current.X:F1}, Y={spriteRef.Current.Y:F1}, " +
                                         $"Rotation={spriteRef.Current.Rotation:F1}, " +
                                         $"Bounds={spriteRef.Current.Bounds}"),
                        "Material" => TextBlock($"Material={materialRef.Current.MaterialName}, " +
                                           $"Blend={materialRef.Current.Blend}, " +
                                           $"Opacity={materialRef.Current.Opacity:F2}, " +
                                           $"Shadow={materialRef.Current.CastShadow}"),
                        "Color" => TextBlock($"Color = {color}"),
                        _ => Empty()
                    }
                )
            ).Padding(12).Background(SubtleFill)
        );
    }
}
