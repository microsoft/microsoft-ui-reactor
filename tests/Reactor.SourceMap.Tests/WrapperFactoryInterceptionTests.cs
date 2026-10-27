using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.Wrappers;
using Xunit;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.SourceMap.Tests.FakeGaugeElement;
using static Microsoft.UI.Reactor.SourceMap.Tests.FakePanelElement;
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
///   such a call with the wrapper sub-expressions replaced by their element type.</item>
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
    private static void AssertStampedAt(Element el, int line, string name)
    {
        Assert.NotNull(el.CallSite);
        Assert.Equal(line, el.CallSite!.Value.LineNumber);
        Assert.EndsWith("WrapperFactoryInterceptionTests.cs", el.CallSite.Value.FilePath, StringComparison.Ordinal);

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

/// <summary>A built-in control wrapped as if it were third-party, reached by a qualified call.</summary>
[GenerateReactorWrapper(typeof(WinUI.Border))]
internal partial record WrappedBorderElement;