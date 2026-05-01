using System.ComponentModel;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using Component = Microsoft.UI.Reactor.Core.Component;
using Microsoft.UI.Reactor.Data;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

internal static class PropertyGridFixtures
{
    // ════════════════════════════════════════════════════════════════
    //  Test models
    // ════════════════════════════════════════════════════════════════

    private class PersonModel : INotifyPropertyChanged
    {
        private string _name = "Alice";
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPC(nameof(Name)); } }
        }
        private int _age = 30;
        public int Age
        {
            get => _age;
            set { if (_age != value) { _age = value; OnPC(nameof(Age)); } }
        }
        private bool _active = true;
        public bool Active
        {
            get => _active;
            set { if (_active != value) { _active = value; OnPC(nameof(Active)); } }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPC(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private class CategorizedModel : INotifyPropertyChanged
    {
        private string _title = "Doc1";
        [PropertyCategory("General")]
        [PropertyDescription("The document title")]
        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPC(nameof(Title)); } }
        }

        private double _width = 100;
        [PropertyCategory("Layout")]
        [PropertyDisplayName("Width (px)")]
        public double Width
        {
            get => _width;
            set { if (_width != value) { _width = value; OnPC(nameof(Width)); } }
        }

        private double _height = 200;
        [PropertyCategory("Layout")]
        public double Height
        {
            get => _height;
            set { if (_height != value) { _height = value; OnPC(nameof(Height)); } }
        }

        [PropertyHidden]
        public int InternalId { get; set; } = 99;

        [PropertyReadOnly]
        public string Status { get; set; } = "OK";

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPC(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private record Point2D(double X, double Y)
    {
        public override string ToString() => $"({X}, {Y})";
    }

    private class ShapeModel : INotifyPropertyChanged
    {
        private string _name = "Circle";
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPC(nameof(Name)); } }
        }

        private Point2D _position = new(10, 20);
        public Point2D Position
        {
            get => _position;
            set { if (_position != value) { _position = value; OnPC(nameof(Position)); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPC(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private record Theme(string Name, Point2D Origin);
    private record AppConfig(string Label, Theme Theme);

    private enum BlendMode { Normal, Multiply, Screen, Overlay }

    private class MaterialModel : INotifyPropertyChanged
    {
        private string _name = "Default";
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPC(nameof(Name)); } }
        }
        private BlendMode _blend = BlendMode.Normal;
        public BlendMode Blend
        {
            get => _blend;
            set { if (_blend != value) { _blend = value; OnPC(nameof(Blend)); } }
        }
        private double _opacity = 1.0;
        public double Opacity
        {
            get => _opacity;
            set { if (_opacity != value) { _opacity = value; OnPC(nameof(Opacity)); } }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPC(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // ════════════════════════════════════════════════════════════════
    //  Fixtures
    // ════════════════════════════════════════════════════════════════

    // 1. Basic mutable object renders all primitive editors
    internal class Reflection_MutableObjectComponent : Component
    {
        private readonly PersonModel _model = new();
        private readonly TypeRegistry _registry = new();

        public override Element Render()
        {
            UseObservable(_model);
            return VStack(
                PropertyGrid(_model, _registry)
                    .AutomationId("MutableObjectGrid"),
                TextBlock($"Live: {_model.Name},{_model.Age},{_model.Active}")
                    .AutomationId("MutableObjectLive")
            );
        }
    }

    internal static Element Reflection_MutableObject(RenderContext ctx) =>
        Component<Reflection_MutableObjectComponent>();

    // 2. Categories, hidden, read-only attributes
    internal static Element Reflection_Categorized(RenderContext ctx) =>
        PropertyGrid(new CategorizedModel(), new TypeRegistry())
            .AutomationId("CategorizedGrid");

    // 3. Enum renders as ComboBox
    internal static Element Reflection_EnumEditor(RenderContext ctx) =>
        PropertyGrid(new MaterialModel(), new TypeRegistry())
            .AutomationId("EnumEditorGrid");

    // 4. Nested immutable record with expand/collapse
    internal class Nested_ImmutableRecordComponent : Component
    {
        private readonly ShapeModel _model = new();
        private readonly TypeRegistry _registry = new();

        public override Element Render()
        {
            UseObservable(_model);
            return VStack(
                PropertyGrid(_model, _registry)
                    .AutomationId("NestedRecordGrid"),
                TextBlock($"Pos: {_model.Position}").AutomationId("NestedRecordPos")
            );
        }
    }

    internal static Element Nested_ImmutableRecord(RenderContext ctx) =>
        Component<Nested_ImmutableRecordComponent>();

    // 5. Fully immutable root with OnRootChanged
    internal class Immutable_RootComponent : Component
    {
        private readonly TypeRegistry _registry = new();

        public override Element Render()
        {
            var (point, setPoint) = UseState(new Point2D(5, 10));
            return VStack(
                PropertyGrid(point, _registry,
                    onRootChanged: obj => setPoint((Point2D)obj))
                    .AutomationId("ImmutableRootGrid"),
                TextBlock($"Current: {point}").AutomationId("ImmutableRootValue")
            );
        }
    }

    internal static Element Immutable_Root(RenderContext ctx) =>
        Component<Immutable_RootComponent>();

    // 6. Custom TypeMetadata with explicit editor
    internal static Element Custom_Editor(RenderContext ctx)
    {
        var registry = new TypeRegistry();
        registry.Register<Point2D>(new TypeMetadata
        {
            Editor = (val, onChange) =>
            {
                var p = (Point2D)val;
                return TextBlock($"Custom: {p.X},{p.Y}").AutomationId("CustomEditorText");
            },
            Decompose = val =>
            {
                var p = (Point2D)val;
                return new List<FieldDescriptor>
                {
                    new FieldDescriptor { Name = "X", FieldType = typeof(double),
                            GetValue = _ => p.X, Order = 0 },
                    new FieldDescriptor { Name = "Y", FieldType = typeof(double),
                            GetValue = _ => p.Y, Order = 1 },
                };
            },
            Compose = (val, updates) =>
            {
                var p = (Point2D)val;
                var x = updates.TryGetValue("X", out var ux) ? (double)ux : p.X;
                var y = updates.TryGetValue("Y", out var uy) ? (double)uy : p.Y;
                return new Point2D(x, y);
            },
        });

        var model = new ShapeModel();
        return PropertyGrid(model, registry)
            .AutomationId("CustomEditorGrid");
    }

    // 7. Switching targets doesn't crash
    internal class Target_SwitchingComponent : Component
    {
        private readonly PersonModel _person = new();
        private readonly MaterialModel _material = new();
        private readonly ShapeModel _shape = new();
        private readonly TypeRegistry _registry = new();

        public override Element Render()
        {
            var (targetIdx, setTargetIdx) = UseState(0);
            UseObservable(_person);
            UseObservable(_material);
            UseObservable(_shape);

            object target = targetIdx switch
            {
                0 => _person,
                1 => _material,
                2 => _shape,
                _ => _person,
            };

            return VStack(
                HStack(
                    Button("Person", () => setTargetIdx(0)).AutomationId("TargetPersonBtn"),
                    Button("Material", () => setTargetIdx(1)).AutomationId("TargetMaterialBtn"),
                    Button("Shape", () => setTargetIdx(2)).AutomationId("TargetShapeBtn")
                ),
                PropertyGrid(target, _registry)
                    .AutomationId("SwitchingGrid")
            );
        }
    }

    internal static Element Target_Switching(RenderContext ctx) =>
        Component<Target_SwitchingComponent>();

    // 8. Category expand/collapse
    internal static Element Category_ExpandCollapse(RenderContext ctx) =>
        PropertyGrid(new CategorizedModel(), new TypeRegistry())
            .AutomationId("CategoryGrid");

    // 9. Deep nesting: record inside record
    internal class DeepNesting_RecordInRecordComponent : Component
    {
        private readonly TypeRegistry _registry = new();

        public override Element Render()
        {
            var config = new AppConfig("MyApp", new Theme("Dark", new Point2D(0, 0)));
            var (c, setC) = UseState(config);
            return VStack(
                PropertyGrid(c, _registry,
                    onRootChanged: obj => setC((AppConfig)obj))
                    .AutomationId("DeepNestingGrid"),
                TextBlock($"Config: {c.Label}, {c.Theme.Name}, ({c.Theme.Origin.X},{c.Theme.Origin.Y})")
                    .AutomationId("DeepNestingValue")
            );
        }
    }

    internal static Element DeepNesting_RecordInRecord(RenderContext ctx) =>
        Component<DeepNesting_RecordInRecordComponent>();

    // 10. INPC mutation re-renders the grid
    internal class INPC_ExternalMutationComponent : Component
    {
        private readonly PersonModel _person = new() { Name = "Alice", Age = 30 };
        private readonly TypeRegistry _registry = new();

        public override Element Render()
        {
            UseObservable(_person);
            return VStack(
                PropertyGrid(_person, _registry)
                    .AutomationId("INPCGrid"),
                TextBlock($"Live: {_person.Name}").AutomationId("INPCLive"),
                Button("MutateName", () => _person.Name = "Bob").AutomationId("INPCMutateBtn")
            );
        }
    }

    internal static Element INPC_ExternalMutation(RenderContext ctx) =>
        Component<INPC_ExternalMutationComponent>();
}
