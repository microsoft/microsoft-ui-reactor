using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class PropertyGridFixtures
{
    // ================================================================
    //  Test models
    // ================================================================

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

    private class CollectionModel
    {
        public List<string> Tags { get; set; } = new() { "a", "b" };
        public string[] Codes { get; set; } = ["x", "y"];
        public IList<string> InterfaceTags { get; set; } = new List<string> { "il-a", "il-b" };
        public ICollection<string> CollectionTags { get; set; } = new List<string> { "col-a", "col-b" };
        public ISet<string> SetContractTags { get; set; } = new HashSet<string> { "iset-a", "iset-b" };
        public IReadOnlyList<string> ReadOnlyTags { get; set; } = new List<string> { "ro-a", "ro-b" };
        public IReadOnlyCollection<string> ReadOnlyCollectionTags { get; set; } = new List<string> { "roc-a", "roc-b" };
        public IReadOnlySet<string> ReadOnlySetTags { get; set; } = new HashSet<string> { "ros-a", "ros-b" };
        public IEnumerable<string> EnumerableTags { get; set; } = ["en-a", "en-b"];
        public HashSet<string> SetTags { get; set; } = ["set-a", "set-b"];
        public Queue<string> QueueTags { get; set; } = new(["queue-a", "queue-b"]);
        public Stack<string> StackTags { get; set; } = new(["stack-a", "stack-b"]);
        public LinkedList<string> LinkedTags { get; set; } = new(["link-a", "link-b"]);
        public global::System.Collections.ArrayList Objects { get; set; } = new() { "obj-a", "obj-b" };
        public global::System.Collections.IList NonGenericList { get; set; } = new global::System.Collections.ArrayList { "ng-list-a", "ng-list-b" };
        public global::System.Collections.ICollection NonGenericCollection { get; set; } = new global::System.Collections.ArrayList { "ng-col-a", "ng-col-b" };
        public global::System.Collections.IEnumerable NonGenericEnumerable { get; set; } = new global::System.Collections.ArrayList { "ng-en-a", "ng-en-b" };
        public IList<string> GenericOnlyTags { get; set; } = new GenericOnlyList<string>(["go-a", "go-b"]);
        public Dictionary<string, int> Map { get; set; } = new() { ["one"] = 1, ["two"] = 2 };
        public string Text { get; set; } = "not a collection";
    }

    private sealed class GenericOnlyList<T> : IList<T>
    {
        private readonly List<T> _items = [];

        public GenericOnlyList(IEnumerable<T> items) => _items.AddRange(items);

        public T this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(T item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(T item) => _items.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(T item) => _items.IndexOf(item);
        public void Insert(int index, T item) => _items.Insert(index, item);
        public bool Remove(T item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ObservableItem(string name) : INotifyPropertyChanged
    {
        private string _name = name;

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public override string ToString() => Name;
    }

    private class ArrayOnlyModel
    {
        public string[] Codes { get; set; } = ["x", "y"];
    }

    private class CollectionContractModel
    {
        public ICollection<string> Items { get; set; } = new List<string> { "col-a", "col-b" };
    }

    private class EnumerableAdapterModel
    {
        public IEnumerable<string> Source { get; set; } = ["adapt-a", "adapt-b"];
    }

    private class NonGenericListModel
    {
        public global::System.Collections.ArrayList Items { get; set; } = new() { "obj-a", "obj-b" };
    }

    private class ObservableCollectionModel
    {
        public global::System.Collections.ObjectModel.ObservableCollection<string> Items { get; set; } = new() { "obs-a", "obs-b" };
    }

    private class ObservableItemCollectionModel
    {
        public global::System.Collections.ObjectModel.ObservableCollection<ObservableItem> Items { get; set; } =
            new() { new ObservableItem("item-a"), new ObservableItem("item-b") };
    }

    private static TypeRegistry CreateCollectionSelfTestRegistry()
    {
        var registry = new TypeRegistry();

        registry.Register<CollectionModel>(new TypeMetadata
        {
            Decompose = owner =>
            {
                var model = (CollectionModel)owner;
                return
                [
                    Field(nameof(CollectionModel.Tags), typeof(List<string>), _ => model.Tags,
                        (obj, value) => { ((CollectionModel)obj).Tags = (List<string>)value!; return obj; }),
                    Field(nameof(CollectionModel.Codes), typeof(string[]), _ => model.Codes,
                        (obj, value) => { ((CollectionModel)obj).Codes = (string[])value!; return obj; }),
                    Field(nameof(CollectionModel.InterfaceTags), typeof(IList<string>), _ => model.InterfaceTags,
                        (obj, value) => { ((CollectionModel)obj).InterfaceTags = (IList<string>)value!; return obj; }),
                    Field(nameof(CollectionModel.CollectionTags), typeof(ICollection<string>), _ => model.CollectionTags,
                        (obj, value) => { ((CollectionModel)obj).CollectionTags = (ICollection<string>)value!; return obj; }),
                    Field(nameof(CollectionModel.SetContractTags), typeof(ISet<string>), _ => model.SetContractTags,
                        (obj, value) => { ((CollectionModel)obj).SetContractTags = (ISet<string>)value!; return obj; }),
                    Field(nameof(CollectionModel.ReadOnlyTags), typeof(IReadOnlyList<string>), _ => model.ReadOnlyTags),
                    Field(nameof(CollectionModel.ReadOnlyCollectionTags), typeof(IReadOnlyCollection<string>), _ => model.ReadOnlyCollectionTags),
                    Field(nameof(CollectionModel.ReadOnlySetTags), typeof(IReadOnlySet<string>), _ => model.ReadOnlySetTags),
                    Field(nameof(CollectionModel.EnumerableTags), typeof(IEnumerable<string>), _ => model.EnumerableTags),
                    Field(nameof(CollectionModel.SetTags), typeof(HashSet<string>), _ => model.SetTags),
                    Field(nameof(CollectionModel.QueueTags), typeof(Queue<string>), _ => model.QueueTags),
                    Field(nameof(CollectionModel.StackTags), typeof(Stack<string>), _ => model.StackTags),
                    Field(nameof(CollectionModel.LinkedTags), typeof(LinkedList<string>), _ => model.LinkedTags),
                    Field(nameof(CollectionModel.Objects), typeof(global::System.Collections.ArrayList), _ => model.Objects),
                    Field(nameof(CollectionModel.NonGenericList), typeof(global::System.Collections.IList), _ => model.NonGenericList),
                    Field(nameof(CollectionModel.NonGenericCollection), typeof(global::System.Collections.ICollection), _ => model.NonGenericCollection),
                    Field(nameof(CollectionModel.NonGenericEnumerable), typeof(global::System.Collections.IEnumerable), _ => model.NonGenericEnumerable),
                    Field(nameof(CollectionModel.GenericOnlyTags), typeof(IList<string>), _ => model.GenericOnlyTags),
                    Field(nameof(CollectionModel.Map), typeof(Dictionary<string, int>), _ => model.Map),
                    Field(nameof(CollectionModel.Text), typeof(string), _ => model.Text),
                ];
            },
        });

        registry.Register<ArrayOnlyModel>(new TypeMetadata
        {
            Decompose = owner =>
            {
                var model = (ArrayOnlyModel)owner;
                return
                [
                    Field(nameof(ArrayOnlyModel.Codes), typeof(string[]), _ => model.Codes,
                        (obj, value) => { ((ArrayOnlyModel)obj).Codes = (string[])value!; return obj; }),
                ];
            },
        });

        registry.Register<CollectionContractModel>(new TypeMetadata
        {
            Decompose = owner =>
            {
                var model = (CollectionContractModel)owner;
                return
                [
                    Field(nameof(CollectionContractModel.Items), typeof(ICollection<string>), _ => model.Items,
                        (obj, value) => { ((CollectionContractModel)obj).Items = (ICollection<string>)value!; return obj; }),
                ];
            },
        });

        registry.Register<NonGenericListModel>(new TypeMetadata
        {
            Decompose = owner =>
            {
                var model = (NonGenericListModel)owner;
                return
                [
                    Field(nameof(NonGenericListModel.Items), typeof(global::System.Collections.ArrayList), _ => model.Items,
                        (obj, value) => { ((NonGenericListModel)obj).Items = (global::System.Collections.ArrayList)value!; return obj; }),
                ];
            },
        });

        registry.Register<ObservableCollectionModel>(new TypeMetadata
        {
            Decompose = owner =>
            {
                var model = (ObservableCollectionModel)owner;
                return
                [
                    Field(nameof(ObservableCollectionModel.Items), typeof(global::System.Collections.ObjectModel.ObservableCollection<string>), _ => model.Items),
                ];
            },
        });

        registry.Register<ObservableItemCollectionModel>(new TypeMetadata
        {
            Decompose = owner =>
            {
                var model = (ObservableItemCollectionModel)owner;
                return
                [
                    Field(nameof(ObservableItemCollectionModel.Items), typeof(global::System.Collections.ObjectModel.ObservableCollection<ObservableItem>), _ => model.Items),
                ];
            },
        });

        return registry;
    }

    private static FieldDescriptor Field(
        string name,
        Type fieldType,
        Func<object, object?> getValue,
        Func<object, object?, object>? setValue = null)
        => new()
        {
            Name = name,
            FieldType = fieldType,
            GetValue = getValue,
            SetValue = setValue,
            IsReadOnly = setValue is null,
        };

    // ================================================================
    //  Fixtures
    // ================================================================

    internal class Reflection_MutableObject(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var model = new PersonModel();
            var registry = new TypeRegistry();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                ctx.UseObservable(model);
                return VStack(
                    PropertyGrid(model, registry),
                    TextBlock($"Live: {model.Name},{model.Age},{model.Active}")
                );
            });
            await Harness.Render();

            H.Check("MutableObject_ShowsName", H.FindText("Name") is not null);
            H.Check("MutableObject_ShowsAge", H.FindText("Age") is not null);
            H.Check("MutableObject_ShowsActive", H.FindText("Active") is not null);
            H.Check("MutableObject_ShowsLiveValues",
                H.FindText("Live: Alice,30,True") is not null);
        }
    }

    internal class Reflection_Categorized(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var model = new CategorizedModel();
            var registry = new TypeRegistry();

            var host = H.CreateHost();
            host.Mount(_ => PropertyGrid(model, registry));
            await Harness.Render();

            H.Check("Categorized_ShowsGeneralCategory",
                H.FindTextContaining("General") is not null);
            H.Check("Categorized_ShowsLayoutCategory",
                H.FindTextContaining("Layout") is not null);
            H.Check("Categorized_ShowsTitle", H.FindText("Title") is not null);
            H.Check("Categorized_ShowsDisplayName",
                H.FindText("Width (px)") is not null);
            H.Check("Categorized_HidesInternalId",
                H.FindText("InternalId") is null);
            H.Check("Categorized_ShowsStatus", H.FindText("Status") is not null);
        }
    }

    internal class Reflection_EnumEditor(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var model = new MaterialModel();
            var registry = new TypeRegistry();

            var host = H.CreateHost();
            host.Mount(_ => PropertyGrid(model, registry));
            await Harness.Render();

            H.Check("Enum_ShowsBlendProperty", H.FindText("Blend") is not null);

            var comboBox = H.FindControl<ComboBox>(_ => true);
            H.Check("Enum_HasComboBox", comboBox is not null);
        }
    }

    internal class Nested_ImmutableRecord(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var model = new ShapeModel();
            var registry = new TypeRegistry();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                ctx.UseObservable(model);
                return VStack(
                    PropertyGrid(model, registry),
                    TextBlock($"Pos: {model.Position}")
                );
            });
            await Harness.Render();

            H.Check("NestedRecord_ShowsName", H.FindText("Name") is not null);
            H.Check("NestedRecord_ShowsPosition", H.FindText("Position") is not null);
            H.Check("NestedRecord_ShowsPositionValue",
                H.FindTextContaining("(10, 20)") is not null);

            var expandBtn = H.FindControl<Button>(b =>
                b.Content is string s && s.Contains("\u25B6") ||
                b.Content is TextBlock tb && tb.Text.Contains("\u25B6"));
            H.Check("NestedRecord_HasExpandButton", expandBtn is not null);

            if (expandBtn is not null)
            {
                var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(expandBtn);
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)
                    peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await Harness.Render();

                H.Check("NestedRecord_ExpandedShowsX", H.FindText("X") is not null);
                H.Check("NestedRecord_ExpandedShowsY", H.FindText("Y") is not null);
            }
        }
    }

    internal class Immutable_Root(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var registry = new TypeRegistry();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (point, setPoint) = ctx.UseState(new Point2D(5, 10));
                return VStack(
                    PropertyGrid(point, registry,
                        onRootChanged: obj => setPoint((Point2D)obj)),
                    TextBlock($"Current: {point}")
                );
            });
            await Harness.Render();

            H.Check("ImmutableRoot_ShowsX", H.FindText("X") is not null);
            H.Check("ImmutableRoot_ShowsY", H.FindText("Y") is not null);
            H.Check("ImmutableRoot_ShowsInitialValue",
                H.FindText("Current: (5, 10)") is not null);
        }
    }

    internal class Custom_Editor(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var registry = new TypeRegistry();
            registry.Register<Point2D>(new TypeMetadata
            {
                Editor = (val, onChange) =>
                {
                    var p = (Point2D)val;
                    return TextBlock($"Custom: {p.X},{p.Y}");
                },
                Decompose = val =>
                {
                    var p = (Point2D)val;
                    return new List<Microsoft.UI.Reactor.Data.FieldDescriptor>
                    {
                        new Microsoft.UI.Reactor.Data.FieldDescriptor { Name = "X", FieldType = typeof(double),
                                GetValue = _ => p.X, Order = 0 },
                        new Microsoft.UI.Reactor.Data.FieldDescriptor { Name = "Y", FieldType = typeof(double),
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

            var host = H.CreateHost();
            host.Mount(_ => PropertyGrid(model, registry));
            await Harness.Render();

            H.Check("CustomEditor_ShowsCustomText",
                H.FindTextContaining("Custom: 10,20") is not null);
        }
    }

    internal class Target_Switching(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var person = new PersonModel();
            var material = new MaterialModel();
            var shape = new ShapeModel();
            var registry = new TypeRegistry();

            object target = person;
            Action? rerender = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (_, forceRender) = ctx.UseReducer(false);
                rerender = () => forceRender(v => !v);
                ctx.UseObservable(person);
                ctx.UseObservable(material);
                ctx.UseObservable(shape);
                return PropertyGrid(target, registry);
            });
            await Harness.Render();

            H.Check("Switching_PersonShows", H.FindText("Name") is not null);

            target = material;
            rerender?.Invoke();
            await Harness.Render();
            H.Check("Switching_MaterialShows", H.FindText("Blend") is not null);

            target = shape;
            rerender?.Invoke();
            await Harness.Render();
            H.Check("Switching_ShapeShows", H.FindText("Position") is not null);

            target = person;
            rerender?.Invoke();
            await Harness.Render();
            H.Check("Switching_BackToPersonShows", H.FindText("Name") is not null);

            for (int i = 0; i < 5; i++)
            {
                target = material; rerender?.Invoke(); await Harness.Render();
                target = person; rerender?.Invoke(); await Harness.Render();
                target = shape; rerender?.Invoke(); await Harness.Render();
            }
            await Harness.Render();
            H.Check("Switching_RapidNoCrash", true);
        }
    }

    internal class Category_ExpandCollapse(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var model = new CategorizedModel();
            var registry = new TypeRegistry();

            var host = H.CreateHost();
            host.Mount(_ => PropertyGrid(model, registry));
            await Harness.Render();

            H.Check("CategoryCollapse_TitleVisible", H.FindText("Title") is not null);
            H.Check("CategoryCollapse_WidthVisible",
                H.FindText("Width (px)") is not null);

            var generalBtn = H.FindControl<Button>(b =>
            {
                if (b.Content is Microsoft.UI.Xaml.UIElement)
                {
                    var tb = FindTextInElement(b, "General");
                    return tb is not null;
                }
                return b.Content is string s && s.Contains("General");
            });
            H.Check("CategoryCollapse_FindGeneralButton", generalBtn is not null);

            if (generalBtn is not null)
            {
                var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(generalBtn);
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)
                    peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await Harness.Render();

                H.Check("CategoryCollapse_TitleHidden", H.FindText("Title") is null);
                H.Check("CategoryCollapse_WidthStillVisible",
                    H.FindText("Width (px)") is not null);
            }
        }

        private static TextBlock? FindTextInElement(Microsoft.UI.Xaml.DependencyObject root, string text)
        {
            if (root is TextBlock tb && tb.Text?.Contains(text) == true) return tb;
            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = FindTextInElement(
                    Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i), text);
                if (found is not null) return found;
            }
            return null;
        }
    }

    internal class DeepNesting_RecordInRecord(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var config = new AppConfig("MyApp", new Theme("Dark", new Point2D(0, 0)));
            var registry = new TypeRegistry();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (c, setC) = ctx.UseState(config);
                return VStack(
                    PropertyGrid(c, registry,
                        onRootChanged: obj => setC((AppConfig)obj)),
                    TextBlock($"Config: {c.Label}, {c.Theme.Name}, ({c.Theme.Origin.X},{c.Theme.Origin.Y})")
                );
            });
            await Harness.Render();

            H.Check("DeepNesting_ShowsLabel", H.FindText("Label") is not null);
            H.Check("DeepNesting_ShowsTheme", H.FindText("Theme") is not null);
            H.Check("DeepNesting_ShowsInitialConfig",
                H.FindTextContaining("Config: MyApp") is not null);
        }
    }

    internal class INPC_ExternalMutation(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var person = new PersonModel { Name = "Alice", Age = 30 };
            var registry = new TypeRegistry();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                ctx.UseObservable(person);
                return VStack(
                    PropertyGrid(person, registry),
                    TextBlock($"Live: {person.Name}"),
                    Button("MutateName", () => person.Name = "Bob")
                );
            });
            await Harness.Render();

            H.Check("INPC_InitialName", H.FindText("Live: Alice") is not null);

            H.ClickButton("MutateName");
            await Harness.Render();

            H.Check("INPC_MutatedName", H.FindText("Live: Bob") is not null);
        }
    }

    internal class Array_CollectionVariantMatrix(Harness h) : SelfTestFixtureBase(h)
    {
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors, typeof(CollectionModel))]
        public override async Task RunAsync()
        {
            var model = new CollectionModel();
            var registry = CreateCollectionSelfTestRegistry();

            var host = H.CreateHost();
            host.Mount(_ => PropertyGrid(model, registry));
            await Harness.Render();

            H.Check("ArrayEditors_ListToolbar", H.FindText("Tags (2)") is not null);
            H.Check("ArrayEditors_ArrayToolbar", H.FindText("Codes (2)") is not null);
            H.Check("ArrayEditors_IListToolbar", H.FindText("InterfaceTags (2)") is not null);
            H.Check("ArrayEditors_ICollectionToolbar", H.FindText("CollectionTags (2)") is not null);
            H.Check("ArrayEditors_ISetToolbar", H.FindText("SetContractTags (2)") is not null);
            H.Check("ArrayEditors_ReadOnlyToolbar", H.FindText("ReadOnlyTags (2)") is not null);
            H.Check("ArrayEditors_ReadOnlyCollectionToolbar", H.FindText("ReadOnlyCollectionTags (2)") is not null);
            H.Check("ArrayEditors_ReadOnlySetToolbar", H.FindText("ReadOnlySetTags (2)") is not null);
            H.Check("ArrayEditors_SetToolbar", H.FindText("SetTags (2)") is not null);
            H.Check("ArrayEditors_QueueToolbar", H.FindText("QueueTags (2)") is not null);
            H.Check("ArrayEditors_StackToolbar", H.FindText("StackTags (2)") is not null);
            H.Check("ArrayEditors_LinkedListToolbar", H.FindText("LinkedTags (2)") is not null);
            H.Check("ArrayEditors_NonGenericToolbar", H.FindText("Objects (2)") is not null);
            H.Check("ArrayEditors_NonGenericIListToolbar", H.FindText("NonGenericList (2)") is not null);
            H.Check("ArrayEditors_NonGenericICollectionToolbar", H.FindText("NonGenericCollection (2)") is not null);
            H.Check("ArrayEditors_GenericOnlyIListToolbar", H.FindText("GenericOnlyTags (2)") is not null);
            H.Check("ArrayEditors_HidesListToString",
                H.FindTextContaining("System.Collections.Generic.List") is null);
            H.Check("ArrayEditors_DoesNotTreatDictionaryAsCollection",
                H.FindText("Map (2)") is null);
            H.Check("ArrayEditors_DoesNotTreatStringAsCollection",
                H.FindText("Text (16)") is null);
            H.Check("ArrayEditors_DoesNotTreatGenericEnumerableAsCollection",
                H.FindText("EnumerableTags (2)") is null);
            H.Check("ArrayEditors_DoesNotTreatNonGenericEnumerableAsCollection",
                H.FindText("NonGenericEnumerable (2)") is null);
            H.Check("ArrayEditors_ShowsListItems",
                H.FindText("a") is not null && H.FindText("b") is not null);
            H.Check("ArrayEditors_ShowsArrayItems",
                H.FindText("x") is not null && H.FindText("y") is not null);
            H.Check("ArrayEditors_ShowsConcreteCollectionItems",
                H.FindText("queue-a") is not null
                && H.FindText("stack-a") is not null
                && H.FindText("link-a") is not null);
            H.Check("ArrayEditors_ShowsItemRows",
                H.FindText("[0]") is not null && H.FindText("[1]") is not null);

            H.Check("ArrayEditors_ShowsListAddButton", FindAddButton(H, "Tags") is not null);
            H.Check("ArrayEditors_ArrayAddButtonMatchesRuntime",
                (FindAddButton(H, "Codes") is not null) == global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported);
            H.Check("ArrayEditors_ShowsIListAddButton", FindAddButton(H, "InterfaceTags") is not null);
            H.Check("ArrayEditors_ShowsICollectionAddButton", FindAddButton(H, "CollectionTags") is not null);
            H.Check("ArrayEditors_HidesISetAddButton", FindAddButton(H, "SetContractTags") is null);
            H.Check("ArrayEditors_HidesReadOnlyAddButton", FindAddButton(H, "ReadOnlyTags") is null);
            H.Check("ArrayEditors_HidesReadOnlyCollectionAddButton", FindAddButton(H, "ReadOnlyCollectionTags") is null);
            H.Check("ArrayEditors_HidesReadOnlySetAddButton", FindAddButton(H, "ReadOnlySetTags") is null);
            H.Check("ArrayEditors_HidesSetAddButton", FindAddButton(H, "SetTags") is null);
            H.Check("ArrayEditors_HidesQueueAddButton", FindAddButton(H, "QueueTags") is null);
            H.Check("ArrayEditors_HidesStackAddButton", FindAddButton(H, "StackTags") is null);
            H.Check("ArrayEditors_HidesLinkedListAddButton", FindAddButton(H, "LinkedTags") is null);
            H.Check("ArrayEditors_HidesObjectAddButton", FindAddButton(H, "Objects") is null);
            H.Check("ArrayEditors_HidesGenericOnlyAddButton", FindAddButton(H, "GenericOnlyTags") is null);
        }
    }

    internal class Array_List_AddRemove(Harness h) : SelfTestFixtureBase(h)
    {
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors, typeof(CollectionModel))]
        public override async Task RunAsync()
        {
            var model = new CollectionModel();
            var registry = CreateCollectionSelfTestRegistry();

            var host = H.CreateHost();
            host.Mount(_ =>
            {
                var grid = PropertyGrid(model, registry);
                return grid with { Props = grid.Props with { Filter = d => d.Name == nameof(CollectionModel.Tags) } };
            });
            await Harness.Render();

            Invoke(FindAddButton(H, "Tags")!);
            await Harness.Render();

            H.Check("ArrayListOps_AddMutatesList", model.Tags.Count == 3);
            H.Check("ArrayListOps_AddRerendersToolbar", H.FindText("Tags (3)") is not null);

            H.ClickButton("\u2715");
            await Harness.Render();

            H.Check("ArrayListOps_RemoveMutatesList", model.Tags.Count == 2);
            H.Check("ArrayListOps_RemoveRerendersToolbar", H.FindText("Tags (2)") is not null);
        }
    }

    internal class Array_Array_AddRemove_ReplacesProperty(Harness h) : SelfTestFixtureBase(h)
    {
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors, typeof(ArrayOnlyModel))]
        public override async Task RunAsync()
        {
            var model = new ArrayOnlyModel();
            var registry = CreateCollectionSelfTestRegistry();

            var host = H.CreateHost();
            host.Mount(_ => PropertyGrid(model, registry));
            await Harness.Render();

            if (!global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
            {
                H.Check("ArrayArrayOps_AddHiddenWithoutDynamicCode", FindAddButton(H, "Codes") is null);
                return;
            }

            var original = model.Codes;
            Invoke(FindAddButton(H, "Codes")!);
            await Harness.Render();

            H.Check("ArrayArrayOps_AddReplacesArray", !ReferenceEquals(original, model.Codes));
            H.Check("ArrayArrayOps_AddRerendersToolbar", H.FindText("Codes (3)") is not null);

            var afterAdd = model.Codes;
            H.ClickButton("\u2715");
            await Harness.Render();

            H.Check("ArrayArrayOps_RemoveReplacesArray", !ReferenceEquals(afterAdd, model.Codes));
            H.Check("ArrayArrayOps_RemoveRerendersToolbar", H.FindText("Codes (2)") is not null);
        }
    }

    internal class Array_ICollection_AddRemove(Harness h) : SelfTestFixtureBase(h)
    {
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors, typeof(CollectionContractModel))]
        public override async Task RunAsync()
        {
            var model = new CollectionContractModel();
            var registry = CreateCollectionSelfTestRegistry();

            var host = H.CreateHost();
            host.Mount(_ => PropertyGrid(model, registry));
            await Harness.Render();

            Invoke(FindAddButton(H, "Items")!);
            await Harness.Render();

            H.Check("ArrayICollectionOps_AddMutatesRuntimeList", model.Items.Count == 3);
            H.Check("ArrayICollectionOps_AddRerendersToolbar", H.FindText("Items (3)") is not null);

            H.ClickButton("\u2715");
            await Harness.Render();

            H.Check("ArrayICollectionOps_RemoveMutatesRuntimeList", model.Items.Count == 2);
            H.Check("ArrayICollectionOps_RemoveRerendersToolbar", H.FindText("Items (2)") is not null);
        }
    }

    internal class Array_NonGenericList_RemoveOnly(Harness h) : SelfTestFixtureBase(h)
    {
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors, typeof(NonGenericListModel))]
        public override async Task RunAsync()
        {
            var model = new NonGenericListModel();
            var registry = CreateCollectionSelfTestRegistry();

            var host = H.CreateHost();
            host.Mount(_ => PropertyGrid(model, registry));
            await Harness.Render();

            H.Check("ArrayNonGenericOps_HidesAdd", FindAddButton(H, "Items") is null);

            H.ClickButton("\u2715");
            await Harness.Render();

            H.Check("ArrayNonGenericOps_RemoveMutatesList", model.Items.Count == 1);
            H.Check("ArrayNonGenericOps_RemoveRerendersToolbar", H.FindText("Items (1)") is not null);
        }
    }

    internal class Array_ObservableCollection_ExternalAndGridMutations(Harness h) : SelfTestFixtureBase(h)
    {
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors, typeof(ObservableCollectionModel))]
        public override async Task RunAsync()
        {
            var model = new ObservableCollectionModel();
            var registry = CreateCollectionSelfTestRegistry();

            var host = H.CreateHost();
            host.Mount(_ => VStack(
                PropertyGrid(model, registry),
                Button("ExternalAddObs", () => model.Items.Add("obs-c")),
                Button("ExternalRemoveObs", () => model.Items.RemoveAt(0))));
            await Harness.Render();

            H.Check("ArrayObservableOps_InitialToolbar", H.FindText("Items (2)") is not null);

            H.ClickButton("ExternalAddObs");
            await Harness.Render();

            H.Check("ArrayObservableOps_ExternalAddRerenders", H.FindText("Items (3)") is not null);
            H.Check("ArrayObservableOps_ExternalAddItemVisible", H.FindText("obs-c") is not null);

            H.ClickButton("ExternalRemoveObs");
            await Harness.Render();

            H.Check("ArrayObservableOps_ExternalRemoveRerenders", H.FindText("Items (2)") is not null);
            H.Check("ArrayObservableOps_NoGridAddForGenericOnlyList", FindAddButton(H, "Items") is null);
        }
    }

    internal class Array_ObservableItem_PropertyChanged_Rerenders(Harness h) : SelfTestFixtureBase(h)
    {
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors, typeof(ObservableItemCollectionModel))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors, typeof(ObservableItem))]
        public override async Task RunAsync()
        {
            var model = new ObservableItemCollectionModel();
            var registry = CreateCollectionSelfTestRegistry();

            var host = H.CreateHost();
            host.Mount(_ => VStack(
                PropertyGrid(model, registry),
                Button("RenameFirstObservableItem", () => model.Items[0].Name = "item-a-renamed"),
                Button("AddObservableItem", () => model.Items.Add(new ObservableItem("item-c")))));
            await Harness.Render();

            H.Check("ArrayObservableItem_InitialItemVisible", H.FindText("item-a") is not null);

            H.ClickButton("RenameFirstObservableItem");
            await Harness.Render();

            H.Check("ArrayObservableItem_RenameRerenders", H.FindText("item-a-renamed") is not null);

            H.ClickButton("AddObservableItem");
            await Harness.Render();

            H.Check("ArrayObservableItem_AddRerendersToolbar", H.FindText("Items (3)") is not null);
            H.Check("ArrayObservableItem_AddedItemVisible", H.FindText("item-c") is not null);

            model.Items[2].Name = "item-c-renamed";
            await Harness.Render();

            H.Check("ArrayObservableItem_NewItemSubscribedAfterAdd", H.FindText("item-c-renamed") is not null);
        }
    }

    internal class Array_CustomMetadata_EnumerableAdapter(Harness h) : SelfTestFixtureBase(h)
    {
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors, typeof(EnumerableAdapterModel))]
        public override async Task RunAsync()
        {
            var model = new EnumerableAdapterModel();
            var registry = new TypeRegistry();
            registry.Register<EnumerableAdapterModel>(new TypeMetadata
            {
                Decompose = owner =>
                {
                    var current = ((EnumerableAdapterModel)owner).Source.ToArray();
                    return
                    [
                        new FieldDescriptor
                        {
                            Name = nameof(EnumerableAdapterModel.Source),
                            DisplayName = "AdaptedSource",
                            FieldType = typeof(IReadOnlyList<string>),
                            GetValue = _ => current,
                            IsReadOnly = true,
                        }
                    ];
                },
            });

            var host = H.CreateHost();
            host.Mount(_ => PropertyGrid(model, registry));
            await Harness.Render();

            H.Check("ArrayEnumerableAdapter_ShowsToolbar", H.FindText("AdaptedSource (2)") is not null);
            H.Check("ArrayEnumerableAdapter_ShowsItems",
                H.FindText("adapt-a") is not null && H.FindText("adapt-b") is not null);
            H.Check("ArrayEnumerableAdapter_ReadOnlyNoAdd", FindAddButton(H, "AdaptedSource") is null);
        }
    }

    private static Button? FindAddButton(Harness h, string propertyName)
        => h.FindControl<Button>(b =>
            Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == $"Add {propertyName} item");

    private static void Invoke(Button button)
    {
        var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)
            peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
    }
}
