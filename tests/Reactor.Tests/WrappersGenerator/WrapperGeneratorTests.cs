using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.UI.Reactor.Wrappers.Generator;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.WrappersGenerator;

/// <summary>
/// Unit tests for <see cref="WrapperGenerator"/>. Drives the generator over an
/// in-memory compilation with stubbed WinUI types and asserts the emitted
/// wrapper source (the generated code is not compiled here — these tests pin
/// the shape of what the generator writes). The marker attribute is fully
/// qualified at each use site so it sits after the stub namespaces without a
/// misplaced <c>using</c>.
/// </summary>
public class WrapperGeneratorTests
{
    // Minimal WinUI shape: FrameworkElement / Control (the cutoff) / a control
    // with one property of each supported kind, a Content slot, a read-only
    // property (must be skipped), and a RoutedEventHandler event.
    private const string Stubs = @"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
    public delegate void RoutedEventHandler(object sender, object e);
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
}
namespace App
{
    public enum FakeMode { A, B }
    public class FakeControl : Microsoft.UI.Xaml.Controls.Control
    {
        public string Header { get; set; }
        public bool IsActive { get; set; }
        public int Count { get; set; }
        public double Ratio { get; set; }
        public FakeMode Mode { get; set; }
        public object Content { get; set; }
        public object CommandParameter { get; set; }
        public bool IsPressed { get; }   // read-only -> skipped
#pragma warning disable CS0067
        public event Microsoft.UI.Xaml.RoutedEventHandler Clicked;
#pragma warning restore CS0067
    }
}
";

    private static GeneratorDriverRunResult Run(string userSource)
    {
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        var refs = tpa.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();

        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            "WrapperTests",
            new[] { CSharpSyntaxTree.ParseText(userSource, parse) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new WrapperGenerator().AsSourceGenerator() },
            parseOptions: parse);

        return driver.RunGenerators(compilation).GetRunResult();
    }

    private static string WrapperFor(GeneratorDriverRunResult result, string elementName) =>
        result.GeneratedTrees
            .Single(t => t.FilePath.EndsWith($"{elementName}.Wrapper.g.cs", StringComparison.Ordinal))
            .GetText().ToString();

    private static string DescriptorFor(GeneratorDriverRunResult result, string elementName) =>
        result.GeneratedTrees
            .Single(t => t.FilePath.EndsWith($"{elementName}.Descriptor.g.cs", StringComparison.Ordinal))
            .GetText().ToString();

    [Fact]
    public void DescriptorOnly_Mode_Emits_Descriptor_And_Registration_Without_Props_Or_Factory()
    {
        // Spec 058 §15 (P5) — the descriptor-only ("attach") mode generates ONLY
        // the ControlDescriptor + Pattern-A registration against an existing,
        // author-written record (which keeps its own props + factory). It is
        // RECORD-DRIVEN: only control members the record actually declares are
        // mapped. It must NOT emit init-properties, a Setters property, or a
        // factory method.
        var result = Run(Stubs + @"
namespace Microsoft.UI.Reactor.Core { public abstract record Element; }
[Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(App.FakeControl))]
public partial record FakeControlElement : Microsoft.UI.Reactor.Core.Element
{
    public string? Header { get; init; }
    public Microsoft.UI.Reactor.Core.Element? Content { get; init; }
    public System.Action? OnClicked { get; init; }
    public System.Action<App.FakeControl>[] Setters { get; init; } = System.Array.Empty<System.Action<App.FakeControl>>();
}
");
        var src = DescriptorFor(result, "FakeControlElement");

        // Opens the partial WITHOUT redeclaring the base (the existing record owns it).
        Assert.DoesNotContain("partial record FakeControlElement : global::Microsoft.UI.Reactor.Core.Element", src);

        // The descriptor + self-registration ARE emitted, referencing existing members.
        Assert.Contains("public static readonly global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<FakeControlElement, global::App.FakeControl> Descriptor", src);
        Assert.Contains("GetSetters = static e => e.Setters,", src);
        Assert.Contains("static e => e.Header", src);
        Assert.Contains("c.Header = v", src);
        // RegisterControlAssembly is NOT emitted in descriptor-only mode (built-in
        // targets already have XAML metadata; the call is unsafe headless).
        Assert.DoesNotContain("RegisterControlAssembly", src);
        Assert.Contains("ControlRegistry.Register<FakeControlElement, global::App.FakeControl>", src);
        Assert.Contains("new global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.DescriptorHandler<FakeControlElement, global::App.FakeControl>(Descriptor)", src);

        // Content slot references the control's content property name on BOTH sides
        // (the record declares `Content`, which matches FakeControl.Content).
        Assert.Contains("GetChild: static e => e.Content,", src);

        // Record-driven: a control member the record did NOT declare (IsActive,
        // Count, Ratio, Mode) is NOT mapped.
        Assert.DoesNotContain("e.IsActive", src);
        Assert.DoesNotContain("e.Count", src);
        Assert.DoesNotContain("e.Ratio", src);

        // The declared event IS wired (record declares OnClicked).
        Assert.Contains("HandCodedEvent<__EventPayload", src);
        Assert.Contains("live.OnClicked?.Invoke();", src);

        // NO init-property declarations, NO Setters declaration, NO factory.
        Assert.DoesNotContain("{ get; init; }", src);
        Assert.DoesNotContain("public static FakeControlElement FakeControl(", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Interface_Typed_Reference_Prop_Is_Surfaced_But_Collection_Interface_Is_Excluded()
    {
        // Spec 058 §15 (P5.14 follow-up) — a plain data interface (INumberFormatter2,
        // ICommand, …) is a valid raw nullable one-way value and IS surfaced. Only
        // delegates/arrays/templates/UIElement-content and *collection* interfaces
        // (anything implementing IEnumerable) stay excluded.
        var result = Run(@"
namespace Microsoft.UI.Reactor.Core { public abstract record Element; }
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
    public interface IFormatter {}
    public class Fmt : Control
    {
        public IFormatter Formatter { get; set; }
        public System.Collections.IEnumerable Bag { get; set; }
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(Microsoft.UI.Xaml.Controls.Fmt))]
public partial record FmtElement : Microsoft.UI.Reactor.Core.Element
{
    public Microsoft.UI.Xaml.Controls.IFormatter? Formatter { get; init; }
    public System.Collections.IEnumerable? Bag { get; init; }
    public System.Action<Microsoft.UI.Xaml.Controls.Fmt>[] Setters { get; init; } = System.Array.Empty<System.Action<Microsoft.UI.Xaml.Controls.Fmt>>();
}
");
        var src = DescriptorFor(result, "FmtElement");

        // The interface-typed value prop is surfaced (NOT silently dropped),
        // mapped as a nullable one-way value written when non-null.
        Assert.Contains("c.Formatter = v", src);
        Assert.Contains("static e => e.Formatter is not null", src);

        // The collection interface (IEnumerable) is still excluded — a raw
        // collection write is not a declarative value prop.
        Assert.DoesNotContain("c.Bag", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void WrapConvert_Surfaces_Struct_Through_Scalar_Via_Single_Arg_Ctor()
    {
        // Spec 058 §15 (P5.2) — [WrapConvert] surfaces a struct-typed control
        // property (here a CornerRadius-like struct with a single double ctor)
        // through an ergonomic `double?` element prop, written via the struct's
        // single-argument constructor. The element value type is inferred from
        // the ctor parameter. General (works in normal generation mode too).
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
    public struct FakeCorner { public FakeCorner(double uniform) {} public FakeCorner(double a, double b, double c, double d) {} }
    public class CornerCtl : Control
    {
        public FakeCorner Corner { get; set; }
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(Microsoft.UI.Xaml.Controls.CornerCtl))]
[Microsoft.UI.Reactor.Wrappers.WrapConvert(""Corner"")]
public partial record CornerElement;
");
        var src = WrapperFor(result, "CornerElement");

        // Element prop is the ctor parameter type (double?), NOT FakeCorner?.
        Assert.Contains("public double? Corner { get; init; }", src);
        Assert.DoesNotContain("FakeCorner? Corner", src);

        // The descriptor writes via the struct's single-arg ctor, as a skip-write
        // OneWayConditional over the scalar.
        Assert.Contains(".OneWayConditional<double>(static e => e.Corner!.Value, static (c, v) => c.Corner = new global::Microsoft.UI.Xaml.Controls.FakeCorner(v), static e => e.Corner.HasValue)", src);

        // Factory parameter is the ergonomic scalar.
        Assert.Contains("double? corner = default", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void WrapManual_Emits_Customize_Hook_And_Excludes_Manual_Prop()
    {
        // Spec 058 §15 — [WrapManual] excludes a bespoke prop from auto-discovery
        // and routes the generated descriptor through an author-implemented
        // partial Customize hook (for composite/derived/method-based props the
        // generator can't infer).
        var result = Run(@"
namespace Microsoft.UI.Reactor.Core { public abstract record Element; }
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
    public class Gizmo : Control
    {
        public string Label { get; set; }
        public string Mode { get; set; }
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(Microsoft.UI.Xaml.Controls.Gizmo))]
[Microsoft.UI.Reactor.Wrappers.WrapManual(""Mode"")]
public partial record GizmoElement : Microsoft.UI.Reactor.Core.Element
{
    public string? Label { get; init; }
    public string? Mode { get; init; }
    public System.Action<Microsoft.UI.Xaml.Controls.Gizmo>[] Setters { get; init; } = System.Array.Empty<System.Action<Microsoft.UI.Xaml.Controls.Gizmo>>();
}
");
        var src = DescriptorFor(result, "GizmoElement");

        // The auto-mappable prop is still mapped...
        Assert.Contains("c.Label = v", src);
        // ...but the manual prop is NOT auto-mapped (no `c.Mode = v`).
        Assert.DoesNotContain("c.Mode", src);

        // The descriptor is routed through the Customize hook, and the partial
        // hook declaration is emitted for the author to implement.
        Assert.Contains("Customize(", src);
        Assert.Contains("new global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<GizmoElement", src);
        Assert.Contains("private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<GizmoElement, global::Microsoft.UI.Xaml.Controls.Gizmo> Customize(", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void DescriptorOnly_OneWay_Prop_With_Control_ChangedEvent_But_No_Callback_Is_Demoted_Not_Dropped()
    {
        // Spec 058 §15 (P5.4) — a prop the control auto-pairs to controlled (it has
        // a {Prop}Changed event, e.g. ProgressBar.Value via RangeBase.ValueChanged)
        // but whose record declares NO On{Prop}Changed callback must be emitted as
        // ONE-WAY, not dropped. Dropping would silently stop writing the prop.
        var result = Run(@"
namespace Microsoft.UI.Reactor.Core { public abstract record Element; }
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
    public delegate void RangeChanged(object sender, object e);
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
    public class Bar : Control
    {
        public double Value { get; set; }
#pragma warning disable CS0067
        public event Microsoft.UI.Xaml.RangeChanged ValueChanged;
#pragma warning restore CS0067
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(Microsoft.UI.Xaml.Controls.Bar))]
public partial record BarElement : Microsoft.UI.Reactor.Core.Element
{
    public double? Value { get; init; }
    public System.Action<Microsoft.UI.Xaml.Controls.Bar>[] Setters { get; init; } = System.Array.Empty<System.Action<Microsoft.UI.Xaml.Controls.Bar>>();
}
");
        var src = DescriptorFor(result, "BarElement");

        // Value is emitted ONE-WAY (conditional), NOT dropped, NOT controlled.
        Assert.Contains("c.Value = v", src);
        Assert.Contains(".OneWayConditional<double>(static e => e.Value!.Value", src);
        Assert.DoesNotContain(".Controlled<", src);
        Assert.DoesNotContain("ValueChanged +=", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Emits_Props_Content_Event_Factory_And_Registration()
    {
        var result = Run(Stubs + @"
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.FakeControl), Exclude = new[] { ""CommandParameter"" })]
public partial record FakeControlElement;
");
        var src = WrapperFor(result, "FakeControlElement");

        // Element record + supported property kinds (nullable-backed).
        Assert.Contains("partial record FakeControlElement", src);
        Assert.Contains("public string? Header { get; init; }", src);
        Assert.Contains("public bool? IsActive { get; init; }", src);
        Assert.Contains("public int? Count { get; init; }", src);
        Assert.Contains("public double? Ratio { get; init; }", src);
        Assert.Contains("public global::App.FakeMode? Mode { get; init; }", src);

        // Content child slot + event callback.
        Assert.Contains("public global::Microsoft.UI.Reactor.Core.Element? Content", src);
        Assert.Contains("public global::System.Action? OnClicked", src);
        Assert.Contains("HandCodedEvent<__EventPayload", src);

        // Parameterized factory named after the control + self-registration.
        Assert.Contains("public static FakeControlElement FakeControl(", src);
        Assert.Contains("RegisterControlAssembly(typeof(global::App.FakeControl).Assembly)", src);
        Assert.Contains("ControlRegistry.Register<FakeControlElement, global::App.FakeControl>", src);

        // Excluded + read-only members are dropped.
        Assert.DoesNotContain("CommandParameter", src);
        Assert.DoesNotContain("IsPressed", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void AutoDiscover_False_Surfaces_Only_Included_Props()
    {
        var result = Run(Stubs + @"
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.FakeControl), AutoDiscover = false, Include = new[] { ""Header"" })]
public partial record FakeControlElement;
");
        var src = WrapperFor(result, "FakeControlElement");

        Assert.Contains("public string? Header { get; init; }", src);
        Assert.DoesNotContain("IsActive", src);
        Assert.DoesNotContain("public int? Count", src);
        Assert.DoesNotContain("public double? Ratio", src);
    }

    [Fact]
    public void AuthorDeclared_Member_Is_Not_Regenerated()
    {
        var result = Run(Stubs + @"
#nullable enable
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.FakeControl), AutoDiscover = false, Include = new[] { ""Header"" })]
public partial record FakeControlElement
{
    public string? Header { get; init; }   // author override
}
");
        var src = WrapperFor(result, "FakeControlElement");

        // The generator must not emit a second Header property or its descriptor entry.
        Assert.DoesNotContain("Maps <c>FakeControl.Header</c>", src);
        Assert.DoesNotContain("c.Header = v", src);
    }

    [Fact]
    public void Invalid_Target_Reports_REACTORGEN001()
    {
        var result = Run(@"
public class NotAControl {}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(NotAControl))]
public partial record NotAControlElement;
");
        Assert.Contains(result.Diagnostics, d => d.Id == "REACTORGEN001");
    }

    [Fact]
    public void Paired_Value_And_ValueChanged_Emit_Controlled_Optional_Prop()
    {
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
}
namespace Windows.Foundation
{
    public delegate void TypedEventHandler<TSender, TResult>(TSender sender, TResult args);
}
namespace App
{
    public class Rating : Microsoft.UI.Xaml.Controls.Control
    {
        public double Value { get; set; }
#pragma warning disable CS0067
        public event Windows.Foundation.TypedEventHandler<Rating, object> ValueChanged;
#pragma warning restore CS0067
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Rating))]
public partial record RatingElement;
");
        var src = WrapperFor(result, "RatingElement");

        // Controlled value ⇒ Optional<T> field + On{Prop}Changed callback.
        Assert.Contains("public global::Microsoft.UI.Reactor.Optional<double> Value { get; init; }", src);
        Assert.Contains("public global::System.Action<double>? OnValueChanged { get; init; }", src);

        // Wired via the public .Controlled entry (echo encapsulated), not OneWay.
        Assert.Contains(".Controlled<double,", src);
        Assert.Contains(".ValueChanged += (s, e) => h(s, e)", src);
        Assert.Contains("callback:    static e => e.OnValueChanged", src);
        Assert.Contains("readBack:    static c => c.Value", src);
        Assert.DoesNotContain("OneWayConditional<double>", src);

        // Factory exposes both the Optional value and the change callback.
        Assert.Contains("global::System.Action<double>? onValueChanged = null", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void WrapControlled_Override_Binds_NonConventional_Change_Event()
    {
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
    public class RoutedEventArgs {}
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs e);
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
}
namespace App
{
    public class Toggle : Microsoft.UI.Xaml.Controls.Control
    {
        public bool IsOn { get; set; }
#pragma warning disable CS0067
        public event Microsoft.UI.Xaml.RoutedEventHandler Toggled;
#pragma warning restore CS0067
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Toggle))]
[Microsoft.UI.Reactor.Wrappers.WrapControlled(""IsOn"", ChangedEvent = ""Toggled"")]
public partial record ToggleElement;
");
        var src = WrapperFor(result, "ToggleElement");

        // IsOn becomes controlled (Optional<bool> + callback), bound to Toggled.
        Assert.Contains("public global::Microsoft.UI.Reactor.Optional<bool> IsOn { get; init; }", src);
        Assert.Contains("public global::System.Action<bool>? OnIsOnChanged { get; init; }", src);
        Assert.Contains(".Controlled<bool,", src);
        Assert.Contains(".Toggled += (s, e) => h(s, e)", src);
        Assert.Contains("callback:    static e => e.OnIsOnChanged", src);
        Assert.Contains("readBack:    static c => c.IsOn", src);

        // The Toggled event is consumed by IsOn — NOT also emitted as a
        // fire-and-forget OnToggled callback, and IsOn is not one-way.
        Assert.DoesNotContain("OnToggled", src);
        Assert.DoesNotContain("OneWayConditional<bool>", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void WrapControlled_Events_Binds_Multiple_Change_Events()
    {
        // Multi-event two-way (CheckBox/RadioButton-style): IsChecked is driven
        // by Checked + Unchecked, the value read back from the control property.
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
    public class RoutedEventArgs {}
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs e);
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
}
namespace App
{
    public class Toggle : Microsoft.UI.Xaml.Controls.Control
    {
        public bool? IsChecked { get; set; }
#pragma warning disable CS0067
        public event Microsoft.UI.Xaml.RoutedEventHandler Checked;
        public event Microsoft.UI.Xaml.RoutedEventHandler Unchecked;
#pragma warning restore CS0067
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Toggle))]
[Microsoft.UI.Reactor.Wrappers.WrapControlled(""IsChecked"", Events = new[] { ""Checked"", ""Unchecked"" })]
public partial record ToggleElement;
");
        var src = WrapperFor(result, "ToggleElement");

        // bool? control prop is surfaced faithfully as Optional<bool?> tri-state.
        Assert.Contains("public global::Microsoft.UI.Reactor.Optional<bool?> IsChecked { get; init; }", src);
        Assert.Contains("public global::System.Action<bool?>? OnIsCheckedChanged { get; init; }", src);
        Assert.Contains(".Controlled<bool?,", src);

        // Both events wired to the shared handler in a multi-line subscribe block.
        Assert.Contains("subscribe:   static (fe, h) =>", src);
        Assert.Contains(".Checked += (s, e) => h(s, e);", src);
        Assert.Contains(".Unchecked += (s, e) => h(s, e);", src);
        Assert.Contains("readBack:    static c => c.IsChecked", src);

        // Both events are consumed (no fire-and-forget OnChecked/OnUnchecked).
        Assert.DoesNotContain("OnChecked", src);
        Assert.DoesNotContain("OnUnchecked", src);
        Assert.DoesNotContain("OneWayConditional<bool?>", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Writable_Interface_Items_Emit_Observable_Collection_Setter()
    {
        var result = Run(Stubs + @"
namespace App
{
    public class DynamicItems : Microsoft.UI.Xaml.Controls.Control
    {
        public global::System.Collections.Generic.IList<object> Items { get; set; }
            = new global::System.Collections.Generic.List<object>();
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.DynamicItems))]
public partial record DynamicItemsElement;
");
        var src = WrapperFor(result, "DynamicItemsElement");
        Assert.Contains("SetCollection = static (c, items) => c.Items = items,", src);
        Assert.DoesNotContain("c.Items = v", src);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("public Microsoft.UI.Xaml.Controls.ItemCollection Items { get; set; }")]
    [InlineData("public global::System.Collections.Generic.IList<string> Items { get; set; }")]
    [InlineData("public global::System.Collections.Generic.IList<object> Items { get; init; }")]
    [InlineData("public global::System.Collections.Generic.IList<object> Items { get; private set; }")]
    public void Incompatible_Items_Properties_Do_Not_Emit_Collection_Setter(string property)
    {
        var result = Run(Stubs + @"
namespace Microsoft.UI.Xaml.Controls
{
    public class ItemCollection : global::System.Collections.Generic.List<object> {}
}
namespace App
{
    public class RestrictedItems : Microsoft.UI.Xaml.Controls.Control
    {
        " + property + @"
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.RestrictedItems))]
public partial record RestrictedItemsElement;
");
        Assert.DoesNotContain("SetCollection =", WrapperFor(result, "RestrictedItemsElement"));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Items_Control_Emit_ItemsHost_Strategy_And_Items_Slot()
    {
        // A control with a public `Items` of type ItemCollection
        // (ItemsControl-derived: ListBox/ComboBox/ListView/GridView) gets an
        // ItemsHost children strategy + an Items element slot, NOT a single-
        // content slot. Items is read-only on the control, so it must NOT be
        // mistaken for a settable value prop.
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
    public class ItemCollection : global::System.Collections.Generic.List<object> {}
    public class ItemsControl : Control
    {
        public ItemCollection Items { get; } = new ItemCollection();
    }
    public class MyList : ItemsControl
    {
        public int MaxRows { get; set; }
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(Microsoft.UI.Xaml.Controls.MyList))]
public partial record MyListElement;
");
        var src = WrapperFor(result, "MyListElement");

        // Items element slot is an IReadOnlyList<object>, default empty.
        Assert.Contains("public global::System.Collections.Generic.IReadOnlyList<object> Items { get; init; }", src);

        // Descriptor uses the ItemsHost strategy, not SingleContent or Panel.
        Assert.Contains("Children = new global::Microsoft.UI.Reactor.Core.V1Protocol.ItemsHost<MyListElement,", src);
        Assert.Contains("GetItems:      static e => e.Items,", src);
        Assert.Contains("GetCollection: static c => c.Items),", src);
        Assert.DoesNotContain("SetCollection =", src);
        Assert.DoesNotContain("SingleContent", src);
        Assert.DoesNotContain("Panel<MyListElement", src);

        // The plain value prop (MaxRows) is still surfaced; Items is NOT a value prop.
        Assert.Contains("public int? MaxRows { get; init; }", src);

        // Factory takes params object[] items.
        Assert.Contains("params object[] items", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Exclude_Items_Suppresses_ItemsHost_Strategy()
    {
        // Spec 058 §15 — `Exclude = ["Items"]` opts a control out of the auto
        // ItemsHost (e.g. TokenizingTextBox manages its Items internally and throws
        // on direct Items.Clear()/Add()). No items slot, no ItemsHost, no params.
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
    public class ItemCollection : global::System.Collections.Generic.List<object> {}
    public class ItemsControl : Control { public ItemCollection Items { get; } = new ItemCollection(); }
    public class Tokenizer : ItemsControl { public string Text { get; set; } }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(Microsoft.UI.Xaml.Controls.Tokenizer), Exclude = new[] { ""Items"" })]
public partial record TokenizerElement;
");
        var src = WrapperFor(result, "TokenizerElement");

        // No ItemsHost strategy, no Items slot, no params object[] items.
        Assert.DoesNotContain("ItemsHost", src);
        Assert.DoesNotContain("params object[] items", src);
        Assert.DoesNotContain("public global::System.Collections.Generic.IReadOnlyList<object> Items", src);
        // The plain value prop is still surfaced.
        Assert.Contains("public string? Text { get; init; }", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Panel_Children_Emit_Panel_Strategy_And_Children_Slot()
    {
        // A control with a public `Children` of type UIElementCollection
        // (StackPanel/Canvas/Grid-style) gets a Panel children strategy + a
        // Children element slot, NOT a single-content slot.
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Controls
{
    public class UIElementCollection {}
    public class Panel : Microsoft.UI.Xaml.FrameworkElement
    {
        public UIElementCollection Children { get; } = new UIElementCollection();
    }
    public class MyStack : Panel
    {
        public double Spacing { get; set; }
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(Microsoft.UI.Xaml.Controls.MyStack))]
public partial record MyStackElement;
");
        var src = WrapperFor(result, "MyStackElement");

        // Children element slot is an IReadOnlyList<Element>, default empty.
        Assert.Contains("public global::System.Collections.Generic.IReadOnlyList<global::Microsoft.UI.Reactor.Core.Element> Children { get; init; }", src);

        // Descriptor uses the Panel children strategy, not SingleContent.
        Assert.Contains("Children = new global::Microsoft.UI.Reactor.Core.V1Protocol.Panel<MyStackElement,", src);
        Assert.Contains("GetChildren: static e => e.Children,", src);
        Assert.Contains("GetCollection: static c => c.Children),", src);
        Assert.DoesNotContain("SingleContent", src);

        // The plain value prop (Spacing) is still surfaced; Children is NOT a value prop.
        Assert.Contains("public double? Spacing { get; init; }", src);

        // Factory takes params children.
        Assert.Contains("params global::Microsoft.UI.Reactor.Core.Element[] children", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void WrapPanelChildren_Wires_PerChild_And_AfterAll_Attached_Hooks()
    {
        // Spec 058 §15 (P5.19) — [WrapPanelChildren] wires the generated Panel
        // children strategy's per-child / two-pass attached-prop hook to a named
        // static method on the record, so attached-property panels (Grid, WrapGrid,
        // RelativePanel) need no hand-written strategy holder + Customize boilerplate.
        var result = Run(@"
namespace Microsoft.UI.Reactor.Core { public abstract record Element; }
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Controls
{
    public class UIElementCollection {}
    public class Panel : Microsoft.UI.Xaml.FrameworkElement
    {
        public UIElementCollection Children { get; } = new UIElementCollection();
    }
    public class MyGrid : Panel {}
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(Microsoft.UI.Xaml.Controls.MyGrid))]
[Microsoft.UI.Reactor.Wrappers.WrapPanelChildren(PerChild = ""ApplyAttached"")]
public partial record MyGridElement : Microsoft.UI.Reactor.Core.Element
{
    public System.Collections.Generic.IReadOnlyList<Microsoft.UI.Reactor.Core.Element> Children { get; init; }
    public System.Action<Microsoft.UI.Xaml.Controls.MyGrid>[] Setters { get; init; } = System.Array.Empty<System.Action<Microsoft.UI.Xaml.Controls.MyGrid>>();
    private static void ApplyAttached(Microsoft.UI.Xaml.Controls.MyGrid p, Microsoft.UI.Xaml.UIElement ui, Microsoft.UI.Reactor.Core.Element el) {}
}
");
        var src = DescriptorFor(result, "MyGridElement");

        // The Panel strategy is emitted with the per-child attached hook wired.
        Assert.Contains("Children = new global::Microsoft.UI.Reactor.Core.V1Protocol.Panel<MyGridElement,", src);
        Assert.Contains("PerChildAttached = ApplyAttached,", src);
        Assert.DoesNotContain("PerChildAttachedAfterAll", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void WrapPanelChildren_AfterAll_Wires_TwoPass_Hook()
    {
        // The two-pass after-all hook (RelativePanel sibling-name resolution).
        var result = Run(@"
namespace Microsoft.UI.Reactor.Core { public abstract record Element; }
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Controls
{
    public class UIElementCollection {}
    public class Panel : Microsoft.UI.Xaml.FrameworkElement
    {
        public UIElementCollection Children { get; } = new UIElementCollection();
    }
    public class MyRel : Panel {}
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(Microsoft.UI.Xaml.Controls.MyRel))]
[Microsoft.UI.Reactor.Wrappers.WrapPanelChildren(AfterAll = ""ApplyAfterAll"")]
public partial record MyRelElement : Microsoft.UI.Reactor.Core.Element
{
    public System.Collections.Generic.IReadOnlyList<Microsoft.UI.Reactor.Core.Element> Children { get; init; }
    public System.Action<Microsoft.UI.Xaml.Controls.MyRel>[] Setters { get; init; } = System.Array.Empty<System.Action<Microsoft.UI.Xaml.Controls.MyRel>>();
    private static void ApplyAfterAll(Microsoft.UI.Xaml.Controls.MyRel p, System.Collections.Generic.IReadOnlyList<(Microsoft.UI.Xaml.UIElement, Microsoft.UI.Reactor.Core.Element)> pairs) {}
}
");
        var src = DescriptorFor(result, "MyRelElement");

        Assert.Contains("PerChildAttachedAfterAll = ApplyAfterAll,", src);
        Assert.DoesNotContain("PerChildAttached =", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void NonScalar_Struct_And_Reference_Props_Are_Nullable_OneWay()
    {
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public struct Thickness { }
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Media { public class Brush { } }
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
}
namespace App
{
    public class Fancy : Microsoft.UI.Xaml.Controls.Control
    {
        public Microsoft.UI.Xaml.Thickness Pad { get; set; }      // struct -> nullable one-way
        public Microsoft.UI.Xaml.Media.Brush Fill { get; set; }   // reference -> nullable one-way
        public Microsoft.UI.Xaml.UIElement Child { get; set; }    // UIElement -> skipped (content, not a value prop)
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Fancy))]
public partial record FancyElement;
");
        var src = WrapperFor(result, "FancyElement");

        // Value-type struct → nullable one-way, written via .Value.
        Assert.Contains("public global::Microsoft.UI.Xaml.Thickness? Pad { get; init; }", src);
        Assert.Contains(".OneWayConditional<global::Microsoft.UI.Xaml.Thickness>(static e => e.Pad!.Value, static (c, v) => c.Pad = v, static e => e.Pad.HasValue)", src);

        // Reference type → nullable one-way, written when non-null.
        Assert.Contains("public global::Microsoft.UI.Xaml.Media.Brush? Fill { get; init; }", src);
        Assert.Contains(".OneWayConditional<global::Microsoft.UI.Xaml.Media.Brush>(static e => e.Fill!, static (c, v) => c.Fill = v, static e => e.Fill is not null)", src);

        // UIElement-derived props are NOT surfaced as raw value props.
        Assert.DoesNotContain("Child", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void WrapAlias_Surfaces_Control_Prop_Under_Friendly_Name()
    {
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
}
namespace App
{
    public class Range : Microsoft.UI.Xaml.Controls.Control
    {
        public double Minimum { get; set; }
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Range))]
[Microsoft.UI.Reactor.Wrappers.WrapAlias(""Min"", ""Minimum"")]
public partial record RangeElement;
");
        var src = WrapperFor(result, "RangeElement");

        // Element field + factory use the friendly name…
        Assert.Contains("public double? Min { get; init; }", src);
        Assert.Contains("double? min = default", src);
        // …while the descriptor reads/writes the real control property.
        Assert.Contains("static e => e.Min!.Value, static (c, v) => c.Minimum = v, static e => e.Min.HasValue", src);
        // The control property name is NOT surfaced verbatim.
        Assert.DoesNotContain("public double? Minimum", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void TypedEventHandler_FireAndForget_Event_Auto_Surfaces_Whole_Args()
    {
        // A typed event (TypedEventHandler<S,A>) now AUTO-surfaces its whole args as
        // Action<A> with no [WrapEvent] needed — A is meaningful (not object).
        var result = Run(@"
namespace Windows.Foundation { public delegate void TypedEventHandler<TSender, TResult>(TSender sender, TResult args); }
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
}
namespace App
{
    public sealed class TabsArgs { }
    public class Tabs : Microsoft.UI.Xaml.Controls.Control
    {
#pragma warning disable CS0067
        public event Windows.Foundation.TypedEventHandler<Tabs, App.TabsArgs> TabClosed;
#pragma warning restore CS0067
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Tabs))]
public partial record TabsElement;
");
        var src = WrapperFor(result, "TabsElement");

        // Auto whole-args: Action<TabsArgs>, trampoline invokes with the args object.
        Assert.Contains("public global::System.Action<global::App.TabsArgs>? OnTabClosed { get; init; }", src);
        Assert.Contains("HandCodedEvent<__EventPayload, global::Windows.Foundation.TypedEventHandler<global::App.Tabs, global::App.TabsArgs>>", src);
        Assert.Contains("live.OnTabClosed?.Invoke(args)", src);
        Assert.Contains("c.TabClosed += h", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void EventHandlerOfT_Auto_Surfaces_Whole_Args_And_Object_Is_Skipped()
    {
        // System.EventHandler<A> with meaningful A auto-surfaces as Action<A> (no
        // [WrapEvent]); EventHandler<object> has uninteresting args and is skipped.
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
}
namespace App
{
    public sealed class OpenedArgs { }
    public class Combo : Microsoft.UI.Xaml.Controls.Control
    {
#pragma warning disable CS0067
        public event System.EventHandler<App.OpenedArgs> Opened;
        public event System.EventHandler<object> Pinged;
#pragma warning restore CS0067
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Combo))]
public partial record ComboElement;
");
        var src = WrapperFor(result, "ComboElement");

        // Meaningful args → auto Action<OpenedArgs>, trampoline passes the args.
        Assert.Contains("public global::System.Action<global::App.OpenedArgs>? OnOpened { get; init; }", src);
        Assert.Contains("live.OnOpened?.Invoke(args)", src);
        // object args → not surfaced at all.
        Assert.DoesNotContain("OnPinged", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void WrapOneWay_Forces_OneWay_Despite_Changed_Event()
    {
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
    public delegate void RoutedEventHandler(object sender, object e);
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
}
namespace App
{
    public class Gauge : Microsoft.UI.Xaml.Controls.Control
    {
        public double Value { get; set; }
#pragma warning disable CS0067
        public event Microsoft.UI.Xaml.RoutedEventHandler ValueChanged;
#pragma warning restore CS0067
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Gauge), Exclude = new[] { ""ValueChanged"" })]
[Microsoft.UI.Reactor.Wrappers.WrapOneWay(""Value"")]
public partial record GaugeElement;
");
        var src = WrapperFor(result, "GaugeElement");

        // Value stays one-way (nullable), NOT controlled, despite ValueChanged.
        Assert.Contains("public double? Value { get; init; }", src);
        Assert.Contains(".OneWayConditional<double>(static e => e.Value!.Value", src);
        Assert.DoesNotContain(".Controlled<double,", src);
        Assert.DoesNotContain("OnValueChanged", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Nullable_TriState_Prop_Is_Optional_Backed()
    {
        // A bool? control prop is tri-state (true/false/null) — the spec-050
        // Optional<U?> primitive distinguishes "unset" from "set to null".
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Windows.Foundation { public delegate void TypedEventHandler<TSender, TResult>(TSender sender, TResult args); }
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
}
namespace App
{
    public sealed class PickedArgs { }
    public class TriPicker : Microsoft.UI.Xaml.Controls.Control
    {
        public bool? State { get; set; }                          // one-way nullable
        public int? Choice { get; set; }                          // controlled nullable (Choice + ChoiceChanged)
#pragma warning disable CS0067
        public event Windows.Foundation.TypedEventHandler<TriPicker, App.PickedArgs> ChoiceChanged;
#pragma warning restore CS0067
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.TriPicker))]
public partial record TriPickerElement;
");
        var src = WrapperFor(result, "TriPickerElement");

        // One-way nullable → Optional<bool?> element prop, Optional-gated write.
        Assert.Contains("public global::Microsoft.UI.Reactor.Optional<bool?> State { get; init; }", src);
        Assert.Contains(".OneWayConditional<bool?>(static e => e.State!.Value, static (c, v) => c.State = v, static e => e.State.HasValue)", src);

        // Controlled nullable → Optional<int?> + .Controlled<int?, …>.
        Assert.Contains("public global::Microsoft.UI.Reactor.Optional<int?> Choice { get; init; }", src);
        Assert.Contains(".Controlled<int?,", src);
        Assert.Contains("public global::System.Action<int?>? OnChoiceChanged { get; init; }", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void DpBacked_OneWay_Prop_Uses_Optional_With_ClearValue()
    {
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyProperty { }
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
}
namespace App
{
    public class Styled : Microsoft.UI.Xaml.Controls.Control
    {
        public static readonly Microsoft.UI.Xaml.DependencyProperty CaptionProperty = null!;
        public string Caption { get; set; }   // DP-backed → Optional + ClearValue
        public string Note { get; set; }       // no DP → nullable skip-write
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Styled))]
public partial record StyledElement;
");
        var src = WrapperFor(result, "StyledElement");

        // DP-backed → spec-050 Optional<T> + dp (Unset ⇒ ClearValue).
        Assert.Contains("public global::Microsoft.UI.Reactor.Optional<string> Caption { get; init; }", src);
        Assert.Contains(".OneWay<string>(static e => e.Caption, static (c, v) => c.Caption = v, global::App.Styled.CaptionProperty)", src);

        // No DP → unchanged nullable skip-write fallback.
        Assert.Contains("public string? Note { get; init; }", src);
        Assert.Contains(".OneWayConditional<string>(static e => e.Note!", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ContentProperty_Attribute_Drives_Content_Slot()
    {
        // A [ContentProperty(Name="Child")] control (like Border) gets a single
        // content slot mapped to Child — not a hardcoded "Content".
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Markup
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Class)]
    public sealed class ContentPropertyAttribute : global::System.Attribute { public string Name { get; set; } = """"; }
}
namespace App
{
    [global::Microsoft.UI.Xaml.Markup.ContentProperty(Name = ""Child"")]
    public class Frame : Microsoft.UI.Xaml.FrameworkElement
    {
        public Microsoft.UI.Xaml.UIElement Child { get; set; }
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Frame))]
public partial record FrameElement;
");
        var src = WrapperFor(result, "FrameElement");

        Assert.Contains("public global::Microsoft.UI.Reactor.Core.Element? Content { get; init; }", src);
        Assert.Contains("SetChild: static (c, ui) => c.Child = ui", src);
        Assert.Contains("GetCurrentChild = static c => c.Child as global::Microsoft.UI.Xaml.UIElement", src);
        Assert.Contains("global::Microsoft.UI.Reactor.Core.Element? content = null", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void WrapContent_Override_Picks_The_Content_Slot()
    {
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace App
{
    public class Holder : Microsoft.UI.Xaml.FrameworkElement
    {
        public Microsoft.UI.Xaml.UIElement Body { get; set; }
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Holder))]
[Microsoft.UI.Reactor.Wrappers.WrapContent(""Body"")]
public partial record HolderElement;
");
        var src = WrapperFor(result, "HolderElement");

        Assert.Contains("SetChild: static (c, ui) => c.Body = ui", src);
        Assert.Contains("GetCurrentChild = static c => c.Body as global::Microsoft.UI.Xaml.UIElement", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Aliasing_The_Content_Prop_Surfaces_It_As_A_Value()
    {
        // [WrapAlias("Label","Content")] means "treat Content as a Label value, not a
        // child slot". Content is `object`, so it surfaces as `object?` (full-wrapper
        // maps object → object?, accepting a string or any value) — not a child slot.
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Markup
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Class)]
    public sealed class ContentPropertyAttribute : global::System.Attribute { public string Name { get; set; } = """"; }
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
}
namespace App
{
    [global::Microsoft.UI.Xaml.Markup.ContentProperty(Name = ""Content"")]
    public class Chk : Microsoft.UI.Xaml.Controls.Control
    {
        public object Content { get; set; }
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Chk))]
[Microsoft.UI.Reactor.Wrappers.WrapAlias(""Label"", ""Content"")]
public partial record ChkElement;
");
        var src = WrapperFor(result, "ChkElement");

        // Content (object) is surfaced as an `object?` Label value prop, not a child slot.
        Assert.Contains("public object? Label { get; init; }", src);
        Assert.Contains(".OneWayConditional<object>(static e => e.Label!, static (c, v) => c.Content = v, static e => e.Label is not null)", src);
        Assert.DoesNotContain("SingleContent", src);
        Assert.DoesNotContain("public global::Microsoft.UI.Reactor.Core.Element? Content", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Object_Value_Prop_Surfaces_As_Object_In_Full_Wrapper()
    {
        // A raw `object` value prop (ItemsSource/SuggestedItemsSource/CommandParameter/…)
        // surfaces as object? in full-wrapper mode, so a list or any value flows
        // declaratively — no imperative .OnMount escape hatch needed.
        var result = Run(@"
namespace Microsoft.UI.Xaml { public class DependencyObject {} public class UIElement : DependencyObject {} public class FrameworkElement : UIElement {} }
namespace Microsoft.UI.Xaml.Controls { public class Control : Microsoft.UI.Xaml.FrameworkElement {} }
namespace App { public class Picker : Microsoft.UI.Xaml.Controls.Control { public object Suggestions { get; set; } } }
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Picker))]
public partial record PickerElement;
");
        var src = WrapperFor(result, "PickerElement");

        Assert.Contains("public object? Suggestions { get; init; }", src);
        Assert.Contains(".OneWayConditional<object>(static e => e.Suggestions!, static (c, v) => c.Suggestions = v, static e => e.Suggestions is not null)", src);
        Assert.DoesNotContain("string? Suggestions", src);

        Assert.Empty(result.Diagnostics);
    }

    private static string PolymorphicFor(GeneratorDriverRunResult result, string elementName) =>
        result.GeneratedTrees
            .Single(t => t.FilePath.EndsWith($"{elementName}.Polymorphic.g.cs", StringComparison.Ordinal))
            .GetText().ToString();

    [Fact]
    public void WrapPolymorphic_Emits_Decorator_Handler_With_Resolver_Reconcile_And_Registration()
    {
        // Spec 058 §15 (P5.27) — [WrapPolymorphic] emits an IDecoratorElementHandler
        // (NOT a ControlDescriptor): Mount calls the Resolve method instead of
        // `new TControl()`; Update re-resolves and either patches in place via the
        // Reconcile method (when the runtime control type is unchanged) or rebuilds;
        // a Setters member is applied after Mount/Update; registration is a Pattern-A
        // cctor calling ControlRegistry.RegisterDecorator<TElement>.
        var result = Run(@"
namespace Microsoft.UI.Reactor.Core { public abstract record Element; }
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Controls
{
    // Abstract base — not instantiable, so the descriptor path's `new TControl()`
    // could never work; the polymorphic path resolves a concrete subtype instead.
    public abstract class IconBase : Microsoft.UI.Xaml.FrameworkElement {}
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(Microsoft.UI.Xaml.Controls.IconBase))]
[Microsoft.UI.Reactor.Wrappers.WrapPolymorphic(""Resolve"", Reconcile = ""Patch"")]
public partial record GlyphElement : Microsoft.UI.Reactor.Core.Element
{
    public System.Action<Microsoft.UI.Xaml.Controls.IconBase>[] Setters { get; init; } = System.Array.Empty<System.Action<Microsoft.UI.Xaml.Controls.IconBase>>();
    private static Microsoft.UI.Xaml.Controls.IconBase? Resolve(GlyphElement e) => null;
    private static bool Patch(GlyphElement o, GlyphElement n, Microsoft.UI.Xaml.Controls.IconBase c) => true;
}
");
        var src = PolymorphicFor(result, "GlyphElement");

        // A decorator handler is emitted — NOT a ControlDescriptor.
        Assert.Contains("private sealed class __PolymorphicHandler : global::Microsoft.UI.Reactor.Core.V1Protocol.IDecoratorElementHandler<GlyphElement>", src);
        Assert.DoesNotContain("ControlDescriptor<GlyphElement", src);

        // Mount resolves via the author's Resolve method (not `new TControl()`),
        // falls back to an empty TextBlock on null, tags + applies setters.
        Assert.Contains("var __c = GlyphElement.Resolve(element);", src);
        Assert.Contains("if (__c is null) return new global::Microsoft.UI.Xaml.Controls.TextBlock { Text = string.Empty };", src);
        Assert.DoesNotContain("new global::Microsoft.UI.Xaml.Controls.IconBase()", src);
        Assert.Contains("global::Microsoft.UI.Reactor.Core.Reconciler.ApplySetters(element.Setters, __c);", src);

        // Update re-resolves, rebuilds on type change OR when Reconcile returns false.
        Assert.Contains("var __fresh = GlyphElement.Resolve(newEl);", src);
        Assert.Contains("if (control is not global::Microsoft.UI.Xaml.Controls.IconBase __typed", src);
        Assert.Contains("|| __fresh.GetType() != __typed.GetType()", src);
        Assert.Contains("|| !GlyphElement.Patch(oldEl, newEl, __typed))", src);

        // Pattern-A registration via RegisterDecorator (decorator, not Register<E,C>).
        Assert.Contains("static GlyphElement()", src);
        Assert.Contains("global::Microsoft.UI.Reactor.Core.V1Protocol.ControlRegistry.RegisterDecorator<GlyphElement>(static () => new __PolymorphicHandler());", src);
        Assert.Contains("=> global::Microsoft.UI.Reactor.Core.V1Protocol.V1UnmountDisposition.CollectSelf;", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void WrapPolymorphic_Without_Reconcile_Or_Setters_Omits_Patch_And_ApplySetters()
    {
        // Reconcile and Setters are both optional: with no Reconcile the same-type
        // arm never short-circuits to a rebuild (patch-less in-place reuse); with no
        // Setters member ApplySetters is not emitted. An explicit EmptySentinel
        // overrides the default empty-TextBlock placeholder.
        var result = Run(@"
namespace Microsoft.UI.Reactor.Core { public abstract record Element; }
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Controls
{
    public abstract class HostBase : Microsoft.UI.Xaml.FrameworkElement {}
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(Microsoft.UI.Xaml.Controls.HostBase))]
[Microsoft.UI.Reactor.Wrappers.WrapPolymorphic(""Make"", EmptySentinel = ""Empty"")]
public partial record HostElement : Microsoft.UI.Reactor.Core.Element
{
    private static Microsoft.UI.Xaml.Controls.HostBase? Make(HostElement e) => null;
    private static Microsoft.UI.Xaml.UIElement Empty() => null!;
}
");
        var src = PolymorphicFor(result, "HostElement");

        // No Reconcile → the rebuild condition has no `|| !Patch(...)` clause.
        Assert.DoesNotContain("Patch", src);
        Assert.DoesNotContain("|| !HostElement.", src);
        // No Setters member → ApplySetters is not emitted.
        Assert.DoesNotContain("ApplySetters", src);
        // EmptySentinel override replaces the default empty TextBlock.
        Assert.Contains("if (__c is null) return HostElement.Empty();", src);
        Assert.DoesNotContain("new global::Microsoft.UI.Xaml.Controls.TextBlock", src);

        Assert.Empty(result.Diagnostics);
    }

    private static string DecoratorFor(GeneratorDriverRunResult result, string elementName) =>
        result.GeneratedTrees
            .Single(t => t.FilePath.EndsWith($"{elementName}.Decorator.g.cs", StringComparison.Ordinal))
            .GetText().ToString();

    [Fact]
    public void WrapDecorator_Emits_Monomorphic_Lifecycle_Handler_With_Create_Update_Unmount_And_Registration()
    {
        // Spec 058 §15 (P5.28) — [WrapDecorator] emits an IDecoratorElementHandler
        // (NOT a ControlDescriptor) for a monomorphic create-once / mutate-in-place
        // control: Mount calls Create + tags; Update casts the EXISTING control, runs
        // OnUpdate, re-tags, and returns the same instance (never re-creates); Unmount
        // runs OnUnmount then DetachReactorState + returns SkipPool; Pattern-A cctor
        // registers via RegisterDecorator.
        var result = Run(@"
namespace Microsoft.UI.Reactor.Core { public abstract record Element; }
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Frame : Microsoft.UI.Xaml.FrameworkElement {}
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(Microsoft.UI.Xaml.Controls.Frame))]
[Microsoft.UI.Reactor.Wrappers.WrapDecorator(""Make"", OnUpdate = ""Patch"", OnUnmount = ""Teardown"")]
public partial record PageElement : Microsoft.UI.Reactor.Core.Element
{
    private static Microsoft.UI.Xaml.Controls.Frame Make(PageElement e) => new Microsoft.UI.Xaml.Controls.Frame();
    private static void Patch(PageElement o, PageElement n, Microsoft.UI.Xaml.Controls.Frame c) {}
    private static void Teardown(Microsoft.UI.Xaml.Controls.Frame c) {}
}
");
        var src = DecoratorFor(result, "PageElement");

        // A decorator handler — NOT a ControlDescriptor.
        Assert.Contains("private sealed class __DecoratorHandler : global::Microsoft.UI.Reactor.Core.V1Protocol.IDecoratorElementHandler<PageElement>", src);
        Assert.DoesNotContain("ControlDescriptor<PageElement", src);

        // Mount creates via Create, tags, returns.
        Assert.Contains("var __c = PageElement.Make(element);", src);
        Assert.Contains("global::Microsoft.UI.Reactor.Core.Reconciler.SetElementTag(__c, element);", src);

        // Update casts the EXISTING control, runs OnUpdate in place, returns same control.
        Assert.Contains("var __c = (global::Microsoft.UI.Xaml.Controls.Frame)control;", src);
        Assert.Contains("PageElement.Patch(oldEl, newEl, __c);", src);
        Assert.DoesNotContain("PageElement.Make(newEl)", src); // never re-creates on update

        // Unmount runs OnUnmount, detaches, SkipPool (author-owned interop control).
        Assert.Contains("PageElement.Teardown(__c);", src);
        Assert.Contains("global::Microsoft.UI.Reactor.Core.Reconciler.DetachReactorState(__c);", src);
        Assert.Contains("return global::Microsoft.UI.Reactor.Core.V1Protocol.V1UnmountDisposition.SkipPool;", src);

        // Pattern-A registration via RegisterDecorator.
        Assert.Contains("static PageElement()", src);
        Assert.Contains("global::Microsoft.UI.Reactor.Core.V1Protocol.ControlRegistry.RegisterDecorator<PageElement>(static () => new __DecoratorHandler());", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void WrapDecorator_Without_OnUpdate_Or_OnUnmount_Omits_Those_Calls()
    {
        // OnUpdate / OnUnmount are optional: Update just re-tags the existing control;
        // Unmount only DetachReactorState + SkipPool.
        var result = Run(@"
namespace Microsoft.UI.Reactor.Core { public abstract record Element; }
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(Microsoft.UI.Xaml.FrameworkElement))]
[Microsoft.UI.Reactor.Wrappers.WrapDecorator(""Make"")]
public partial record HostElement : Microsoft.UI.Reactor.Core.Element
{
    private static Microsoft.UI.Xaml.FrameworkElement Make(HostElement e) => null!;
}
");
        var src = DecoratorFor(result, "HostElement");

        // Mount creates via Make; Update casts + re-tags + returns the same control.
        Assert.Contains("var __c = HostElement.Make(element);", src);
        Assert.Contains("var __c = (global::Microsoft.UI.Xaml.FrameworkElement)control;", src);
        // No OnUpdate → no `(oldEl, newEl, __c)` in-place mutation call is emitted.
        Assert.DoesNotContain("(oldEl, newEl, __c)", src);
        // Unmount has only DetachReactorState + SkipPool (no OnUnmount teardown call).
        Assert.Contains("global::Microsoft.UI.Reactor.Core.Reconciler.DetachReactorState(__c);", src);
        Assert.Contains("return global::Microsoft.UI.Reactor.Core.V1Protocol.V1UnmountDisposition.SkipPool;", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void WrapLifecycle_Factory_Wires_OnMount_And_OnUnmount()
    {
        // Spec 058 §15 (P5.30) — [WrapLifecycle] wires the named static methods
        // through the element's .OnMount/.OnUnmount modifiers in the generated
        // factory, so an imperative control auto-starts on mount / stops on unmount
        // with no call-site boilerplate.
        var result = Run(Stubs + @"
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.FakeControl))]
[Microsoft.UI.Reactor.Wrappers.WrapLifecycle(""Start"", OnUnmounted = ""Stop"")]
public partial record FakeControlElement
{
    private static void Start(App.FakeControl c) { }
    private static void Stop(App.FakeControl c) { }
}
");
        var src = WrapperFor(result, "FakeControlElement");

        // Factory wraps `new() { ... }` with OnMountAdd + OnUnmountAdd static calls,
        // casting the FrameworkElement to the control type.
        Assert.Contains("global::Microsoft.UI.Reactor.ElementExtensions.OnMountAdd(", src);
        Assert.Contains("static __fe => Start((global::App.FakeControl)__fe)", src);
        Assert.Contains("global::Microsoft.UI.Reactor.ElementExtensions.OnUnmountAdd(", src);
        Assert.Contains("static __fe => Stop((global::App.FakeControl)__fe)", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void WrapLifecycle_Without_OnUnmounted_Wires_Only_OnMount()
    {
        var result = Run(Stubs + @"
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.FakeControl))]
[Microsoft.UI.Reactor.Wrappers.WrapLifecycle(""Start"")]
public partial record FakeControlElement
{
    private static void Start(App.FakeControl c) { }
}
");
        var src = WrapperFor(result, "FakeControlElement");

        Assert.Contains("global::Microsoft.UI.Reactor.ElementExtensions.OnMountAdd(", src);
        Assert.DoesNotContain("OnUnmountAdd", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Content_Slot_Narrower_Than_UIElement_Is_DownCast()
    {
        // The SingleContent strategy hands SetChild a UIElement?, but a content
        // property narrower than UIElement (e.g. LayoutTransformControl.Child :
        // FrameworkElement) needs a down-cast — otherwise CS0266. object/UIElement
        // content assign directly (see ContentProperty_Attribute_Drives_Content_Slot).
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Markup
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Class)]
    public sealed class ContentPropertyAttribute : global::System.Attribute { public string Name { get; set; } = """"; }
}
namespace App
{
    [global::Microsoft.UI.Xaml.Markup.ContentProperty(Name = ""Child"")]
    public class Transformer : Microsoft.UI.Xaml.FrameworkElement
    {
        public Microsoft.UI.Xaml.FrameworkElement Child { get; set; }
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Transformer))]
public partial record TransformerElement;
");
        var src = WrapperFor(result, "TransformerElement");

        Assert.Contains("SetChild: static (c, ui) => c.Child = ui as global::Microsoft.UI.Xaml.FrameworkElement)", src);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void FullWrapper_ContentPresenter_Derived_Trims_FrameworkElement_Plumbing()
    {
        // A ContentPresenter/Panel-derived full wrapper surfaces only its OWN
        // members — the UIElement/FrameworkElement/ContentPresenter layout & input
        // plumbing (Width, AllowDrop, Background, …) is modeled by Reactor's generic
        // element modifiers, the same boundary Control-derived controls get via the
        // Control cutoff. The content slot is still discovered.
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement
    {
        public double Width { get; set; }
        public bool AllowDrop { get; set; }
    }
}
namespace Microsoft.UI.Xaml.Controls
{
    public class ContentPresenter : Microsoft.UI.Xaml.FrameworkElement
    {
        public object Content { get; set; }
        public object Background { get; set; }
    }
}
namespace App
{
    public class Constrained : Microsoft.UI.Xaml.Controls.ContentPresenter
    {
        public double ConstraintFactor { get; set; }
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Constrained))]
public partial record ConstrainedElement;
");
        var src = WrapperFor(result, "ConstrainedElement");

        // The control's own prop is surfaced; the content slot is discovered.
        Assert.Contains("ConstraintFactor", src);
        Assert.Contains("global::Microsoft.UI.Reactor.Core.Element? content = null", src);
        // FrameworkElement / ContentPresenter plumbing is NOT surfaced.
        Assert.DoesNotContain("Width", src);
        Assert.DoesNotContain("AllowDrop", src);
        Assert.DoesNotContain("Background", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void FullWrapper_Skips_Element_Base_Modifier_Props()
    {
        // A control that redeclares a prop whose name is already a generic element
        // modifier on the Reactor `Element` base (Padding, …) — some WCT panels/Grid
        // do — must NOT surface it: that would shadow the fluent modifier (CS0108)
        // and give two ways to set the same thing. Authors use the modifier instead.
        // (Relies on the real Reactor.Core.Element from the referenced assembly,
        // which exposes Padding/Width/Height/Margin/… as modifiers.)
        var result = Run(@"
namespace Microsoft.UI.Xaml
{
    public class DependencyObject {}
    public class UIElement : DependencyObject {}
    public class FrameworkElement : UIElement {}
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement {}
}
namespace App
{
    public class Gadget : Microsoft.UI.Xaml.Controls.Control
    {
        public double Padding { get; set; }   // collides with Element.Padding modifier
        public string Label { get; set; }
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Gadget))]
public partial record GadgetElement;
");
        var src = WrapperFor(result, "GadgetElement");

        Assert.Contains("Label", src);
        Assert.DoesNotContain("Padding", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Two_Controlled_Props_On_One_Control_Reports_REACTORGEN012()
    {
        // A control has a single per-control controlled-event state slot, so only
        // one controlled/two-way prop works; a second silently never fires. Two
        // [WrapControlled] props bound to the same event (RangeSelector pattern)
        // must surface a REACTORGEN012 warning (source is still emitted).
        var result = Run(Stubs + @"
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.FakeControl))]
[Microsoft.UI.Reactor.Wrappers.WrapControlled(""Count"", ChangedEvent = ""Clicked"")]
[Microsoft.UI.Reactor.Wrappers.WrapControlled(""Ratio"", ChangedEvent = ""Clicked"")]
public partial record FakeControlElement;
");
        Assert.Contains(result.Diagnostics, d => d.Id == "REACTORGEN012");
        // Source is still emitted (the warning doesn't block generation).
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.EndsWith("FakeControlElement.Wrapper.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void FullWrapper_Settable_Typed_Collection_Surfaces_As_OneWay_Prop()
    {
        // A SETTABLE typed collection prop (e.g. MetadataControl.Items :
        // IEnumerable<MetadataItem>) is a legitimate declarative value — surfaced
        // as a one-way prop assigned wholesale, not forced through the Setters
        // escape hatch. (Descriptor-only mode still excludes collections — see
        // Interface_Typed_Reference_Prop_Is_Surfaced_But_Collection_Interface_Is_Excluded.)
        var result = Run(@"
namespace Microsoft.UI.Xaml { public class DependencyObject {} public class UIElement : DependencyObject {} public class FrameworkElement : UIElement {} }
namespace Microsoft.UI.Xaml.Controls { public class Control : Microsoft.UI.Xaml.FrameworkElement {} }
namespace App
{
    public class Tag {}
    public class Tagged : Microsoft.UI.Xaml.Controls.Control
    {
        public System.Collections.Generic.IEnumerable<Tag> Tags { get; set; }
    }
}
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.Tagged))]
public partial record TaggedElement;
");
        var src = WrapperFor(result, "TaggedElement");

        // Surfaced as a declarative one-way collection prop assigned wholesale.
        Assert.Contains("Tags", src);
        Assert.Contains("c.Tags = v", src);
        Assert.Contains("global::System.Collections.Generic.IEnumerable<global::App.Tag>", src);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void FullWrapper_Emits_Strongly_Typed_Set_Escape_Hatch()
    {
        // Full wrappers get a strongly-typed `.Set(Action<TControl>)` chainable
        // method (appends to Setters) so imperative escape-hatch usage reads
        // `.Set(c => …)` instead of `with { Setters = new Action<TControl>[] { … } }`.
        var result = Run(Stubs + @"
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.FakeControl))]
public partial record FakeControlElement;
");
        var src = WrapperFor(result, "FakeControlElement");

        Assert.Contains("public FakeControlElement Set(global::System.Action<global::App.FakeControl> configure)", src);
        Assert.Contains("Setters = [.. Setters, configure]", src);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Generator_DoesNotEmit_TheAuthoringAttribute()
    {
        // Regression guard (CS0436): the wrapper-authoring attributes are hosted in
        // Reactor.Wrappers.Abstractions, so the generator must NOT also emit its own
        // copy via RegisterPostInitializationOutput. When it did, the internal copy
        // leaked from Reactor.dll through InternalsVisibleTo into friend assemblies
        // that ALSO run the generator (e.g. Reactor.AppTests.Host), where the locally
        // generated copy collided with the imported one — failing Release builds under
        // TreatWarningsAsErrors. (The attribute resolves here from the referenced
        // abstractions assembly, which is on the in-memory compilation's TPA refs.)
        var result = Run(Stubs + @"
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.FakeControl))]
public partial record FakeControlElement;
");

        Assert.DoesNotContain(
            result.GeneratedTrees,
            t => Path.GetFileName(t.FilePath)
                .Contains("GenerateReactorWrapperAttribute", StringComparison.Ordinal));

        // …and the generator still ran: the attribute bound from the referenced
        // abstractions assembly, so the wrapper was produced.
        Assert.Contains(
            result.GeneratedTrees,
            t => t.FilePath.EndsWith("FakeControlElement.Wrapper.g.cs", StringComparison.Ordinal));
    }

    // A control with a secondary element slot (UIElement-derived HeaderIcon +
    // object-typed Banner), used by the [WrapElementSlot] tests below.
    private const string SlotStubs = @"
namespace Microsoft.UI.Xaml.Controls
{
    public class IconElement : Microsoft.UI.Xaml.FrameworkElement {}
}
namespace App
{
    public class SlotControl : Microsoft.UI.Xaml.Controls.Control
    {
        public object Content { get; set; }
        public Microsoft.UI.Xaml.Controls.IconElement HeaderIcon { get; set; }
        public object Banner { get; set; }
    }
}
";

    [Fact]
    public void WrapElementSlot_FullWrapper_Emits_InitProp_Factory_And_CastBridge()
    {
        // [WrapElementSlot("HeaderIcon")] on a full wrapper surfaces an Element?
        // init prop + factory param, and emits a state-preserving ImperativeBridged
        // entry (mount via ctx.MountChild, update via ctx.ReconcileChild). Because
        // HeaderIcon's control type is narrower than UIElement, the mounted child is
        // down-cast to that type.
        var result = Run(Stubs + SlotStubs + @"
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.SlotControl))]
[Microsoft.UI.Reactor.Wrappers.WrapElementSlot(""HeaderIcon"")]
public partial record SlotControlElement;
");
        var src = WrapperFor(result, "SlotControlElement");

        // Element-facing init prop + factory param.
        Assert.Contains("public global::Microsoft.UI.Reactor.Core.Element? HeaderIcon { get; init; }", src);
        Assert.Contains("global::Microsoft.UI.Reactor.Core.Element? headerIcon = null", src);
        Assert.Contains("HeaderIcon = headerIcon", src);

        // ImperativeBridged mount/reconcile with a hard cast to the narrow control type.
        Assert.Contains(".ImperativeBridged(", src);
        Assert.Contains("unmount: static (ctx, c) =>", src);
        Assert.Contains("is not SlotControlElement { HeaderIcon: not null }", src);
        Assert.Contains("if (__child is not null) ctx.Reconciler.UnmountChild(__child);", src);
        Assert.Contains("if (e.HeaderIcon is not null) c.HeaderIcon = (global::Microsoft.UI.Xaml.Controls.IconElement)ctx.MountChild(e.HeaderIcon)!;", src);
        Assert.Contains("var __next = ctx.ReconcileChild(__o.HeaderIcon, __n.HeaderIcon, __existing);", src);
        Assert.Contains("if (!ReferenceEquals(__existing, __next)) c.HeaderIcon = (global::Microsoft.UI.Xaml.Controls.IconElement)__next!;", src);

        // Explicit removal: when the new slot is null but the old was not, the child is
        // unmounted and the control property is cleared.
        Assert.Contains("if (__o.HeaderIcon is not null) ctx.ReconcileChild(__o.HeaderIcon, null, c.HeaderIcon as global::Microsoft.UI.Xaml.UIElement);", src);
        Assert.Contains("c.HeaderIcon = null!;", src);
        // Both-null stays an early return (preserves the control's own default).
        Assert.Contains("if (__o.HeaderIcon is null && __n.HeaderIcon is null) return;", src);

        // The slot's control property is NOT also surfaced as a value prop.
        Assert.DoesNotContain("c.HeaderIcon = v", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void WrapElementSlot_ObjectTypedSlot_Emits_NoCast()
    {
        // An object-typed control property (Banner) takes the mounted UIElement
        // directly — no down-cast.
        var result = Run(Stubs + SlotStubs + @"
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.SlotControl))]
[Microsoft.UI.Reactor.Wrappers.WrapElementSlot(""Banner"")]
public partial record SlotControlElement;
");
        var src = WrapperFor(result, "SlotControlElement");

        Assert.Contains("if (e.Banner is not null) c.Banner = ctx.MountChild(e.Banner)!;", src);
        Assert.Contains("if (!ReferenceEquals(__existing, __next)) c.Banner = __next!;", src);
        // No value-prop write and no object? value prop leaked for the slot.
        Assert.DoesNotContain("c.Banner = v", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void WrapElementSlot_ControlPropertyMapping_Writes_TargetProperty()
    {
        // ControlProperty renames the element-facing slot relative to the control
        // property it writes: element prop `Glyph` -> control `HeaderIcon`.
        var result = Run(Stubs + SlotStubs + @"
[Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(App.SlotControl))]
[Microsoft.UI.Reactor.Wrappers.WrapElementSlot(""Glyph"", ControlProperty = ""HeaderIcon"")]
public partial record SlotControlElement;
");
        var src = WrapperFor(result, "SlotControlElement");

        Assert.Contains("public global::Microsoft.UI.Reactor.Core.Element? Glyph { get; init; }", src);
        Assert.Contains("if (e.Glyph is not null) c.HeaderIcon = (global::Microsoft.UI.Xaml.Controls.IconElement)ctx.MountChild(e.Glyph)!;", src);
        Assert.Contains("var __next = ctx.ReconcileChild(__o.Glyph, __n.Glyph, __existing);", src);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void WrapElementSlot_DescriptorOnly_Emits_Bridge_Without_InitProp_Or_Factory()
    {
        // Descriptor-only mode: the author-written record declares the Element?
        // slot prop itself, so the generator emits ONLY the ImperativeBridged chain
        // entry — no init prop, no factory.
        var result = Run(Stubs + SlotStubs + @"
namespace Microsoft.UI.Reactor.Core { public abstract record Element; }
[Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(App.SlotControl))]
public partial record SlotControlElement : Microsoft.UI.Reactor.Core.Element
{
    public Microsoft.UI.Reactor.Core.Element? Content { get; init; }
    public Microsoft.UI.Reactor.Core.Element? HeaderIcon { get; init; }
}
[Microsoft.UI.Reactor.Wrappers.WrapElementSlot(""HeaderIcon"")]
public partial record SlotControlElement;
");
        var src = DescriptorFor(result, "SlotControlElement");

        Assert.Contains(".ImperativeBridged(", src);
        Assert.Contains("var __next = ctx.ReconcileChild(__o.HeaderIcon, __n.HeaderIcon, __existing);", src);
        // Descriptor-only: no init-prop declarations, no factory.
        Assert.DoesNotContain("{ get; init; }", src);
        Assert.DoesNotContain("public static SlotControlElement SlotControl(", src);

        Assert.Empty(result.Diagnostics);
    }
}
