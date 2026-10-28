using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.Wrappers;
using Xunit;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.SourceMap.Tests.FakeGaugeElement;
using static Microsoft.UI.Reactor.SourceMap.Tests.InheritedRivals.DialRival;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Spec 010 — generated members that are inherited, or that sit beside a hand-written
/// member of the same name, and so outrank what binds before generation. A file of its own:
/// only it imports the rival <c>FakeDial</c>.
/// </summary>
[Collection("SourceMap")]
public sealed class WrapperInheritedMemberTests : IDisposable
{
    public WrapperInheritedMemberTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    [ReactorSourceTransparent]
    internal static Element PairUp(Element first, string second) => VStack(first);

    [ReactorSourceTransparent]
    internal static Element PairUp(Element first, Element second) => HStack(first, second);

    [Fact]
    public void FactoryInheritedByAnEnclosingRecord_OutranksAnImportedRival()
    {
        // Inside DialScreen the inherited generated FakeDial() is found before the imported
        // string-returning rival, which is all that binds before generation.
        var paired = DialScreen.Make(FakeGauge());

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)paired).Orientation);
        Assert.Null(paired.CallSite);
    }

    [Fact]
    public void HandWrittenMemberBesideAGeneratedOne_IsNotTrusted()
    {
        // knob.Set(configure) binds the hand-written Set(object) here; the generated, more
        // specific Set(Action<FakeKnob>) wins once it exists.
        FakeGaugeElement gauge = FakeGauge();
        FakeKnobElement knob = FakeKnobElement.FakeKnob();
        global::System.Action<FakeKnob> configure = _ => { };
        var paired = PairUp(gauge, knob.Set(configure));

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)paired).Orientation);
        Assert.Null(paired.CallSite);
    }
}

/// <summary>Inherits <see cref="FakeDialElement"/>'s generated factory.</summary>
public record DialScreen : FakeDialElement
{
    internal static Element Make(FakeGaugeElement gauge) => WrapperInheritedMemberTests.PairUp(gauge, FakeDial());
}

/// <summary>A control whose wrapper record also declares a hand-written <c>Set</c>.</summary>
public partial class FakeKnob : WinUI.Control
{
}

[GenerateReactorWrapper(typeof(FakeKnob), AutoDiscover = false, RegisterAssembly = false)]
public partial record FakeKnobElement : Element
{
    public string Set(object value) => "hand-written";
}
