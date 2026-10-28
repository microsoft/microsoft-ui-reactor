using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.Wrappers;
using Xunit;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.SourceMap.Tests.FakeGaugeElement;
using static Microsoft.UI.Reactor.SourceMap.Tests.FakePanelElement;
using static Microsoft.UI.Reactor.SourceMap.Tests.FakeDialElement;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Spec 010 — call sites for the factories <c>Reactor.Wrappers.Generator</c> emits
/// (<c>[GenerateReactorWrapper]</c>), and for every DSL call that takes one as an argument.
///
/// <para>Roslyn runs every source generator against the same input compilation, so the
/// source-map generator cannot see a wrapper factory: <c>FakeGauge(...)</c> binds to nothing
/// there. Two things follow, and both used to fail silently:</para>
/// <list type="bullet">
///   <item>the wrapper call itself was never stamped. The wrapper generator, which knows the
///   factory's signature, now intercepts it;</item>
///   <item>the CONTAINING call — <c>VStack(8, FakeGauge())</c> — failed overload resolution
///   in the source-map generator's view and was dropped too, so a component whose root
///   held a wrapped control lost its own location. The source-map generator now re-binds
///   such a call as if each wrapper value were already an <c>Element</c>, and leaves it
///   unstamped where that could change which overload the real compilation picks
///   (<see cref="WrapperFactoryOverloadTests"/>).</item>
/// </list>
///
/// <para>Line oracles are <c>[CallerLineNumber]</c> probes on the same physical line; column
/// oracles are this file's own text, as in <see cref="ColumnTests"/>. Shares the "SourceMap"
/// collection because <see cref="ReactorSourceMap.Enabled"/> is process-global.</para>
/// </summary>
[Collection("SourceMap")]
public sealed class WrapperFactoryInterceptionTests : IDisposable
{
    public WrapperFactoryInterceptionTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    private static int Line([CallerLineNumber] int line = 0) => line;

    private static string SourceLine(int lineNumber)
    {
        string relative = global::System.IO.Path.Join("tests", "Reactor.SourceMap.Tests", "WrapperFactoryInterceptionTests.cs");
        var dir = new global::System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = global::System.IO.Path.Join(dir.FullName, relative);
            if (global::System.IO.File.Exists(candidate))
                return global::System.IO.File.ReadAllLines(candidate)[lineNumber - 1];
            dir = dir.Parent;
        }

        throw new InvalidOperationException("could not locate WrapperFactoryInterceptionTests.cs above " + AppContext.BaseDirectory);
    }

    /// <summary>Asserts <paramref name="el"/> was stamped at <paramref name="line"/>, at the column where <paramref name="name"/> starts.</summary>
    /// <remarks>
    /// The path is compared whole against <c>[CallerFilePath]</c>, so a wrapper interceptor
    /// that skipped the compiler's <c>PathMap</c> (CI builds map to <c>/_/</c>) cannot pass.
    /// </remarks>
    private static void AssertStampedAt(Element el, int line, string name, [CallerFilePath] string expectedPath = "")
    {
        Assert.NotNull(el.CallSite);
        Assert.Equal(line, el.CallSite!.Value.LineNumber);
        Assert.Equal(expectedPath, el.CallSite.Value.FilePath);

        var text = SourceLine(line);
        var column = text.IndexOf(name, StringComparison.Ordinal) + 1;
        Assert.True(column > 0, $"oracle could not find '{name}' in: {text}");
        Assert.Equal(column, el.CallSite.Value.ColumnNumber);
    }

    [Fact]
    public void WrapperFactoryThroughUsingStatic_IsStamped()
    {
        var gauge = FakeGauge(value: 3); var line = Line();

        AssertStampedAt(gauge, line, "FakeGauge(");
        Assert.Equal(3, gauge.Value);
    }

    [Fact]
    public void QualifiedWrapperFactory_IsStamped()
    {
        var border = WrappedBorderElement.Border(); var line = Line();

        AssertStampedAt(border, line, "Border(");
    }

    [Fact]
    public void WrapperFactoryWithParamsChildren_IsStamped()
    {
        // The generated factory ends in `params Element[] children`; the interceptor has to
        // restate it in the same form for an expanded-form call to bind to it.
        var panel = FakePanel(TextBlock("a"), FakeGauge()); var line = Line();

        AssertStampedAt(panel, line, "FakePanel(");
        Assert.Equal(2, panel.Children.Count);
        AssertStampedAt(panel.Children[1], line, "FakeGauge(");
    }

    [Fact]
    public void WrapperElementDeclaredName_IsRecorded()
    {
        var wifiCard = FakeGauge(value: 1);

        Assert.Equal("wifiCard", wifiCard.CallSite!.Value.DeclaredName);
    }

    [Fact]
    public void WrapperElementDeclaredName_ThroughAFluentChain_IsRecorded()
    {
        // `.Width(140)` cannot infer its T from the invisible factory's result, but it is a
        // T M<T>(this T) where T : Element modifier, so the element keeps its name.
        var sizedGauge = FakeGauge(value: 1).Width(140);

        Assert.Equal("sizedGauge", sizedGauge.CallSite!.Value.DeclaredName);
    }

    [Fact]
    public void WrapperFactoryArgumentConvertedToAnElement_IsStampedWithItsOwnPosition()
    {
        // "label" becomes a TextBlock through Element's implicit conversion; like any other
        // factory's converted argument it gets the argument's position, not the call's.
        var panel = FakePanel("label", TextBlock("b")); var line = Line();

        AssertStampedAt(panel, line, "FakePanel(");
        AssertStampedAt(panel.Children[0], line, "\"label\"");
    }

    [Fact]
    public void WrapperFactoryParamsArrayInNormalForm_IsNeverWrittenInto()
    {
        // Duo converts to Element AND to Element[]: C# passes Duo's (empty) array as the
        // params array itself, which must not be stamped as if it held one converted Duo.
        var panel = FakePanel(new Duo()); var line = Line();

        AssertStampedAt(panel, line, "FakePanel(");
        Assert.Empty(panel.Children);
    }

    internal static string KidsOf(object value) => "one";

    internal static Element[] KidsOf(Element value) => [];

    [Fact]
    public void WrapperFactoryArgumentWhoseTypeDependsOnTheRecord_IsNotStamped()
    {
        // KidsOf(gauge) is a string here (it would convert to one Element), but KidsOf(Element)
        // wins once the record is complete and hands over an empty Element[] in normal form.
        FakeGaugeElement gauge = FakeGauge();
        var panel = FakePanel(KidsOf(gauge)); var line = Line();

        AssertStampedAt(panel, line, "FakePanel(");
        Assert.Empty(panel.Children);
    }

    [Fact]
    public void WrapperFactoryArgumentConvertingToARecordArray_IsNotStamped()
    {
        // GaugeShelf -> FakeGaugeElement[] becomes an Element[] by covariance once the record
        // is complete, which makes the call normal form with the shelf's (empty) array.
        var panel = FakePanel(new GaugeShelf()); var line = Line();

        AssertStampedAt(panel, line, "FakePanel(");
        Assert.Empty(panel.Children);
    }

    internal static string KidsBy<T>(object value) => "one";

    internal static Element[] KidsBy<T>(int value) where T : global::System.Collections.Generic.IComparer<FakeGaugeElement> => [];

    [Fact]
    public void WrapperFactoryArgumentFromARecordConstrainedCall_IsNotStamped()
    {
        // KidsBy<IComparer<Element>>(0) is the string overload here; once the constraint is
        // satisfied it is the empty Element[] passed in normal form.
        var panel = FakePanel(KidsBy<global::System.Collections.Generic.IComparer<Element>>(0)); var line = Line();

        AssertStampedAt(panel, line, "FakePanel(");
        Assert.Empty(panel.Children);
    }

    internal static Element[] KidsFrom<T>(string value) where T : global::System.Collections.Generic.IComparer<FakeGaugeElement> => [];

    internal static string KidsFrom<T>(object value) => "one";

    internal static TResult ReadKids<TResult>(global::System.Func<string, TResult> read) => read("x");

    [Fact]
    public void WrapperFactoryArgumentFromARecordConstrainedMethodGroup_IsNotStamped()
    {
        // ReadKids infers a string here (KidsFrom<T>(object)) and an Element[] once the
        // constraint holds (KidsFrom<T>(string)), handing FakePanel an empty array in normal form.
        var panel = FakePanel(ReadKids(KidsFrom<global::System.Collections.Generic.IComparer<Element>>)); var line = Line();

        AssertStampedAt(panel, line, "FakePanel(");
        Assert.Empty(panel.Children);
    }

    [Fact]
    public void DynamicArgument_IsNeverIntercepted()
    {
        // A dynamic argument makes both calls dynamically dispatched once the factory exists;
        // an interceptor for either would not compile.
        dynamic value = 3d;
#pragma warning disable IL2026, IL3050 // dynamic dispatch is the subject of this test
        FakeGaugeElement gauge = FakeGauge(value);
        Element stack = VStack(8, FakeGauge(value));
#pragma warning restore IL2026, IL3050

        Assert.Equal(3, gauge.Value);
        Assert.Null(gauge.CallSite);
        Assert.Null(stack.CallSite);
    }

    internal static double ReadValue(object value) => 1;

    internal static dynamic ReadValue(Element value) => 2d;

    [Fact]
    public void ArgumentThatBecomesDynamic_IsNeverIntercepted()
    {
        // ReadValue(gauge) is a double here and dynamic once the record is an Element, which
        // makes both calls dynamically dispatched.
        FakeGaugeElement gauge = FakeGauge();
#pragma warning disable IL2026, IL3050 // dynamic dispatch is the subject of this test
        FakeGaugeElement made = FakeGauge(ReadValue(gauge));
        Element stack = VStack(8, FakeGauge(ReadValue(gauge)));
#pragma warning restore IL2026, IL3050

        Assert.Equal(2, made.Value);
        Assert.Null(made.CallSite);
        Assert.Null(stack.CallSite);
    }

    [Fact]
    public void WrapperRecordThatSpellsItsOwnBase_IsStampedAndSoIsItsContainer()
    {
        // FakeDialElement declares `: Element` itself, so it is complete here — but its
        // factory is still generated, and still invisible to the source-map generator.
        var dial = FakeDial(); var dialLine = Line();
        AssertStampedAt(dial, dialLine, "FakeDial(");
        Assert.Equal("dial", dial.CallSite!.Value.DeclaredName);

        var stack = VStack(8, FakeDial()); var line = Line();
        AssertStampedAt(stack, line, "VStack(");
        AssertStampedAt(stack.Children[0]!, line, "FakeDial(");
    }

    [Fact]
    public void ContainerTakingAWrapperCall_IsStamped()
    {
        var stack = VStack(8, TextBlock("x"), FakeGauge()); var line = Line();

        AssertStampedAt(stack, line, "VStack(");
        AssertStampedAt(stack.Children[1]!, line, "FakeGauge(");
    }

    [Fact]
    public void ContainerTakingAModifiedWrapperCall_IsStamped()
    {
        // `FakeGauge().Width(140)` has no usable type in the source-map generator's view
        // (`Width<T>` cannot infer T from an error type), which used to drop the HStack.
        var row = HStack(8, FakeGauge().Width(140).Height(140)); var line = Line();

        AssertStampedAt(row, line, "HStack(");
        // A modifier copies the element, so the gauge keeps the factory's location.
        AssertStampedAt(row.Children[0]!, line, "FakeGauge(");
    }

    [Fact]
    public void ContainerTakingALocalFromAWrapperCall_IsStamped()
    {
        var card = FakeGauge(); var cardLine = Line();
        var stack = VStack(8, card); var line = Line();

        AssertStampedAt(stack, line, "VStack(");
        Assert.Same(card, stack.Children[0]);
        AssertStampedAt(card, cardLine, "FakeGauge(");
    }

    /// <summary>A helper typed as the wrapper record, which the source-map generator sees as not-yet-an-Element.</summary>
    private static FakeGaugeElement TypedGauge() => FakeGauge(value: 7);

    private readonly FakeGaugeElement _fieldGauge = FakeGaugeElement.FakeGauge();

    [Fact]
    public void ContainerTakingAnExplicitlyTypedWrapperValue_IsStamped()
    {
        // Each of these binds in the source-map generator's view, but to a record whose
        // `: Element` base the wrapper generator has not emitted yet, so the containing
        // call failed overload resolution and was dropped.
        FakeGaugeElement typed = FakeGauge();
        var stack = VStack(8, typed, TypedGauge(), _fieldGauge, TypedGauge().Width(10)); var line = Line();

        AssertStampedAt(stack, line, "VStack(");
        Assert.Equal(4, stack.Children.Length);
        Assert.Same(typed, stack.Children[0]);
    }

    [Fact]
    public void ContainerTakingAChainOfLocals_IsStamped()
    {
        // `sized` is inferred through `g`, one level deeper than a single local.
        var g = FakeGauge();
        var sized = g.Width(140);
        var row = HStack(8, sized); var line = Line();

        AssertStampedAt(row, line, "HStack(");
    }

    /// <summary>A transparent forwarder: its caller's line is the one that should be reported.</summary>
    [ReactorSourceTransparent]
    internal static Element Card() => FakeGauge(value: 1);

    /// <summary>A transparent helper taking an Element, called with a wrapper call.</summary>
    [ReactorSourceTransparent]
    internal static Element Wrap(Element child) => VStack(child);

    [Fact]
    public void WrapperCallInsideATransparentHelper_ReportsTheCaller()
    {
        // Neither generator stamps the FakeGauge() inside Card(): the call to Card() is
        // intercepted instead, so the gauge reports this line, not the helper's.
        var card = Card(); var line = Line();

        AssertStampedAt(card, line, "Card(");
        Assert.IsType<FakeGaugeElement>(card);
    }

    [Fact]
    public void TransparentHelperTakingAWrapperCall_ReportsTheCaller()
    {
        var wrapped = Wrap(FakeGauge()); var line = Line();

        AssertStampedAt(wrapped, line, "Wrap(");
        AssertStampedAt(((StackElement)wrapped).Children[0]!, line, "FakeGauge(");
    }

    /// <summary>
    /// An overload pair where the wrapper record is MORE specific than Element. The real
    /// compilation picks <c>Frame(FakeGaugeElement)</c>; re-binding with an Element-typed
    /// placeholder would pick <c>Frame(Element)</c>, and an interceptor for it would be a
    /// CS9144 build break. That this file compiles is the primary oracle.
    /// </summary>
    [ReactorSourceTransparent]
    internal static Element Frame(Element child) => VStack(child);

    [ReactorSourceTransparent]
    internal static Element Frame(FakeGaugeElement gauge) => HStack(gauge);

    [Fact]
    public void OverloadPreferringTheWrapperRecord_IsLeftUnstampedRatherThanMisbound()
    {
        var framed = Frame(FakeGauge());

        // The real overload ran...
        Assert.Equal(global::Microsoft.UI.Xaml.Controls.Orientation.Horizontal, ((StackElement)framed).Orientation);
        // ...and the call reads as "location unknown", never as the other overload's site.
        Assert.Null(framed.CallSite);
    }

    [Fact]
    public void NonElementLocalDerivedFromAWrapperValue_KeepsItsOwnType()
    {
        // `label` is a string. Re-binding must keep it one: coerced to Element it would pick
        // Button(Element, ...) where the real compilation picks Button(string, ...), and the
        // interceptor would not compile (CS9144). That this file compiles is the first oracle.
        var g = FakeGauge(value: 2);
        var label = g.ToString();
        var button = Button(label, () => { }); var line = Line();

        AssertStampedAt(button, line, "Button(");
        Assert.Equal(g.ToString(), button.Label);
    }

    [Fact]
    public void ContainerTakingAWrapperCall_StillStampsConvertedArguments()
    {
        // Mechanism 2 on the re-bound path: the string reaches Element through the implicit
        // operator, whose call site is not in this compilation, so VStack's interceptor
        // stamps it with the argument's own position.
        var card = FakeGauge();
        var stack = VStack(8,
            "label", FakeGauge()); var argumentLine = Line();

        Assert.NotNull(stack.CallSite);
        Assert.Equal(argumentLine - 1, stack.CallSite!.Value.LineNumber);
        AssertStampedAt(stack.Children[0]!, argumentLine, "\"label\"");
        AssertStampedAt(stack.Children[1]!, argumentLine, "FakeGauge(");
        Assert.NotNull(card.CallSite);
    }

    [Fact]
    public void WrapperCallInsideAnObjectArray_IsStamped()
    {
        object[] items = [FakeGauge() with { Key = "k" }]; var line = Line();

        var gauge = Assert.IsType<FakeGaugeElement>(items[0]);
        AssertStampedAt(gauge, line, "FakeGauge(");
        Assert.Equal("k", gauge.Key);
    }

    /// <summary>
    /// Positive controls for the dependency tracking in this file's wrapper-bearing
    /// compilation: implicitly typed values that do NOT come from a wrapper keep their
    /// stamps, including ones read inside the statement that declares them.
    /// </summary>
    [Fact]
    public void OrdinaryImplicitlyTypedValues_AreStillStamped()
    {
        if (int.TryParse("5", global::System.Globalization.NumberStyles.Integer, global::System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            var fromOut = TextBlock($"{n}"); var outLine = Line();
            AssertStampedAt(fromOut, outLine, "TextBlock(");
        }

        object boxed = 3;
        if (boxed is var unboxed)
        {
            var fromPattern = TextBlock($"{unboxed}"); var patternLine = Line();
            AssertStampedAt(fromPattern, patternLine, "TextBlock(");
        }

        var (count, label) = (1, "x");
        var fromTuple = TextBlock($"{count}{label}"); var tupleLine = Line();
        AssertStampedAt(fromTuple, tupleLine, "TextBlock(");

        var rows = new[] { 1, 2 }.Select(i => TextBlock($"{i}")).ToArray(); var rowsLine = Line();
        AssertStampedAt(rows[0], rowsLine, "TextBlock(");

        // A state tuple whose initializer spells an array type: Roslyn reports the `[]` of
        // `string[]` as error-typed, which must not be mistaken for a binding failure.
        var (items, setItems) = (new string[] { "a" }, (global::System.Action<string[]>)(_ => { }));
        var list = VStack(4, items.Select(i => TextBlock(i)).ToArray()); var listLine = Line();
        AssertStampedAt(list, listLine, "VStack(");
        var add = Button("add", () => setItems([.. items, "b"])); var addLine = Line();
        AssertStampedAt(add, addLine, "Button(");

        // Alongside a wrapper call in the same container.
        var mixed = VStack(8, TextBlock($"{count}"), FakeGauge()); var mixedLine = Line();
        AssertStampedAt(mixed, mixedLine, "VStack(");
    }

    [Fact]
    public void WrapperFactory_StampsNothingWhileMappingIsOff()
    {
        ReactorSourceMap.Enabled = false;

        var gauge = FakeGauge();
        var stack = VStack(8, FakeGauge());

        Assert.Null(gauge.CallSite);
        Assert.Null(stack.CallSite);
        Assert.Null(stack.Children[0]!.CallSite);
    }
}

/// <summary>
/// Minimal stand-ins for third-party controls. A <c>FrameworkElement</c> subclass with a
/// public parameterless constructor is all <c>[GenerateReactorWrapper]</c> needs; none is
/// ever instantiated here, since the tests only build elements.
/// </summary>
public partial class FakeGauge : WinUI.Control
{
    public double Value { get; set; }
}

/// <summary>A panel stand-in, so the generated factory takes <c>params Element[] children</c>.</summary>
public partial class FakePanel : WinUI.Panel
{
}

[GenerateReactorWrapper(typeof(FakeGauge), AutoDiscover = false, Include = new[] { "Value" }, RegisterAssembly = false)]
public partial record FakeGaugeElement;

[GenerateReactorWrapper(typeof(FakePanel), AutoDiscover = false, RegisterAssembly = false)]
public partial record FakePanelElement;

/// <summary>Converts to one Element, and to an array of the record.</summary>
public sealed class GaugeShelf
{
    public static implicit operator Element(GaugeShelf shelf) => Microsoft.UI.Reactor.Factories.TextBlock("shelf");

    public static implicit operator FakeGaugeElement[](GaugeShelf shelf) => [];
}

/// <summary>A control whose wrapper record spells its <c>: Element</c> base itself.</summary>
public partial class FakeDial : WinUI.Control
{
}

[GenerateReactorWrapper(typeof(FakeDial), AutoDiscover = false, RegisterAssembly = false)]
public partial record FakeDialElement : Element;

/// <summary>A built-in control wrapped as if it were third-party, reached by a qualified call.</summary>
[GenerateReactorWrapper(typeof(WinUI.Border))]
internal partial record WrappedBorderElement;