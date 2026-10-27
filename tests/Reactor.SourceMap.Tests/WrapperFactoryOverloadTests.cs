using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.SourceMap.Tests.FakeGaugeElement;
using static Microsoft.UI.Reactor.SourceMap.Tests.GaugeStacks;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Spec 010 — overload sets where the wrapper record is preferred over <c>Element</c>.
///
/// <para>The source-map generator re-binds a call that takes a wrapper factory's result with
/// an <c>Element</c>-typed placeholder, because the record's <c>: Element</c> base does not
/// exist yet in its view. For each method group below the real compilation picks an overload
/// the placeholder cannot reach; an interceptor for the overload the placeholder DOES reach
/// would be CS9144 and break this project's build. So the primary oracle is that this file
/// compiles; each test then pins that the real overload ran and that the call reads as
/// "location unknown" rather than as a wrongly-attributed site.</para>
///
/// <para>A separate file because the competing <c>using static GaugeStacks</c> changes
/// overload resolution for every <c>VStack</c> call in scope.</para>
/// </summary>
[Collection("SourceMap")]
public sealed class WrapperFactoryOverloadTests : IDisposable
{
    public WrapperFactoryOverloadTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    private static int Line([global::System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;

    [ReactorSourceTransparent]
    internal static Element Pick(global::System.IEquatable<FakeGaugeElement> gauge) => HStack((FakeGaugeElement)gauge);

    [ReactorSourceTransparent]
    internal static Element Pick(object value) => VStack();

    [Fact]
    public void InterfaceTheRecordImplements_WinsOverObject()
    {
        var picked = Pick(FakeGauge());

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)picked).Orientation);
        Assert.Null(picked.CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element Later(global::System.Func<FakeGaugeElement> make) => HStack(make());

    [ReactorSourceTransparent]
    internal static Element Later(global::System.Func<Element> make) => VStack(make());

    [Fact]
    public void DelegateReturningTheRecord_WinsOverDelegateReturningElement()
    {
        var later = Later(() => FakeGauge());

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)later).Orientation);
        Assert.Null(later.CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element Soon(GaugeMaker<int> make) => HStack(make());

    [ReactorSourceTransparent]
    internal static Element Soon(global::System.Func<Element> make) => VStack(make());

    [Fact]
    public void GenericDelegateWhoseSignatureReturnsTheRecord_WinsOverFuncOfElement()
    {
        // `GaugeMaker<int>` mentions the record only in its invoke signature.
        var soon = Soon(() => FakeGauge());

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)soon).Orientation);
        Assert.Null(soon.CallSite);
    }

    [Fact]
    public void MoreDerivedOverloadOnAQualifier_WinsOverTheInheritedOne()
    {
        // DerivedFrames.Frame(FakeGaugeElement) beats the inherited BaseFrames.Frame(Element),
        // which is all an Element placeholder can reach.
        var framed = DerivedFrames.Frame(FakeGauge());

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)framed).Orientation);
        Assert.Null(framed.CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element Badge(GaugeBadge badge) => HStack(badge.Gauge);

    [ReactorSourceTransparent]
    internal static Element Badge(object value) => VStack();

    [Fact]
    public void UserDefinedConversionFromTheRecord_WinsOverObject()
    {
        var badge = Badge(FakeGauge());

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)badge).Orientation);
        Assert.Null(badge.CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element Present(object value) => VStack();

    [ReactorSourceTransparent]
    internal static Element Present(Element child) => HStack(child);

    [Fact]
    public void ExplicitlyTypedWrapperValue_IsReBoundToTheOverloadTheRecordReaches()
    {
        // Here `typed` is a record that is not an Element yet, so Present(object) binds; the
        // real compilation picks Present(Element). The call is re-bound and stamped correctly.
        FakeGaugeElement typed = FakeGauge();
        var presented = Present(typed); var line = Line();

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)presented).Orientation);
        Assert.NotNull(presented.CallSite);
        Assert.Equal(line, presented.CallSite!.Value.LineNumber);
    }

    internal static string Choose(Element value) => "element";

    internal static int Choose(FakeGaugeElement value) => 1;

    [ReactorSourceTransparent]
    internal static Element Show(string text) => VStack();

    [ReactorSourceTransparent]
    internal static Element Show(int number) => HStack();

    [Fact]
    public void NestedCallWhoseResultTypeDependsOnTheRecord_IsNotReBound()
    {
        // Choose(placeholder) would return string and pick Show(string); the real Choose
        // returns int and Show(int) runs.
        var shown = Show(Choose(FakeGauge()));

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)shown).Orientation);
        Assert.Null(shown.CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element Pair(Element first, string second) => VStack(first);

    [ReactorSourceTransparent]
    internal static Element Pair(Element first, object second) => HStack(first);

    [Fact]
    public void LocalBeyondTheInferenceDepth_AbandonsTheReBinding()
    {
        // `e` is a record five locals deep, beyond the inference limit, next to a substituted
        // first argument. Only Pair(Element, object) applies in the real compilation. Today Roslyn
        // also ranks the error-typed `e` as ambiguous between the two, so this is defence in
        // depth: the rewrite fails closed on its own (depth exhaustion, residual error) rather
        // than relying on how an error type happens to be ranked.
        var a = FakeGauge();
        var b = a;
        var c = b;
        var d = c;
        var e = d;
        var pair = Pair(FakeGauge(), e);

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)pair).Orientation);
        Assert.Null(pair.CallSite);
    }

    [ReactorSourceTransparent]
    internal static Element Many(global::System.Collections.Generic.IEnumerable<Element> children) => HStack([.. children]);

    [ReactorSourceTransparent]
    internal static Element Many(object value) => VStack();

    [Fact]
    public void ValueWhoseTypeContainsTheRecord_AbandonsTheReBinding()
    {
        // List<FakeGaugeElement> reaches IEnumerable<Element> by variance only once the record
        // is an Element; no placeholder can stand in for that.
        var many = Many(new global::System.Collections.Generic.List<FakeGaugeElement> { FakeGauge() });

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)many).Orientation);
        Assert.Null(many.CallSite);
    }

    [Fact]
    public void CompetingUsingStaticOverload_WinsOverFactories()
    {
        // GaugeStacks.VStack(double, params FakeGaugeElement[]) is more specific than
        // Factories.VStack(double, params Element?[]) for an all-gauge argument list.
        var stack = VStack(8, FakeGauge());

        Assert.True(GaugeStacks.Called);
        Assert.Null(stack.CallSite);
    }

    [Fact]
    public void WithAWrapperSpecificCompetitorInScope_ReBindingDeclinesConservatively()
    {
        // The competitor cannot take a TextBlock, so the real call goes to Factories, but
        // telling that apart would mean re-implementing overload resolution against types
        // that do not exist yet. Declining costs a location; guessing could cost the build.
        var mixed = VStack(8, TextBlock("x"), FakeGauge());
        Assert.Null(mixed.CallSite);

        // Positive control in the same file: a call that needs no substitution binds as
        // usual and is stamped, so the nulls above are the guard and not a dead generator.
        var plain = VStack(8, TextBlock("y"));
        Assert.NotNull(plain.CallSite);
        Assert.EndsWith("WrapperFactoryOverloadTests.cs", plain.CallSite!.Value.FilePath, StringComparison.Ordinal);
    }
}

/// <summary>A generic delegate whose signature, not its type arguments, names the record.</summary>
public delegate FakeGaugeElement GaugeMaker<T>();

/// <summary>Converts FROM the record, which an Element placeholder cannot.</summary>
public sealed class GaugeBadge
{
    public GaugeBadge(FakeGaugeElement gauge) => Gauge = gauge;

    public FakeGaugeElement Gauge { get; }

    public static implicit operator GaugeBadge(FakeGaugeElement gauge) => new(gauge);
}

/// <summary>Holds the overload a placeholder can reach.</summary>
public class BaseFrames
{
    [ReactorSourceTransparent]
    public static Element Frame(Element child) => Microsoft.UI.Reactor.Factories.VStack(child);
}

/// <summary>Adds the more specific overload the real compilation picks.</summary>
public class DerivedFrames : BaseFrames
{
    [ReactorSourceTransparent]
    public static Element Frame(FakeGaugeElement gauge) => Microsoft.UI.Reactor.Factories.HStack(gauge);
}

/// <summary>A user-defined stack factory competing with <c>Factories.VStack</c>.</summary>
public static class GaugeStacks
{
    public static bool Called { get; private set; }

    public static Element VStack(double spacing, params FakeGaugeElement[] children)
    {
        Called = true;
        return Microsoft.UI.Reactor.Factories.VStack(spacing, children);
    }
}