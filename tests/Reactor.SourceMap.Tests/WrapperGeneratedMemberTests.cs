using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.SourceMap.Tests.SetRivals;
using Xunit;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.SourceMap.Tests.FakeGaugeElement;
using static Microsoft.UI.Reactor.SourceMap.Tests.FakeDialElement;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Spec 010 — an extension method that binds on a wrapper value here, where the instance
/// member the wrapper generator adds to the record (<c>Set(Action&lt;TControl&gt;)</c>) is not in
/// view, and loses to it in the real compilation. A file of its own: only it imports the
/// rival extension.
/// </summary>
[Collection("SourceMap")]
public sealed class WrapperGeneratedMemberTests : IDisposable
{
    public WrapperGeneratedMemberTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    [ReactorSourceTransparent]
    internal static Element PairUp(Element first, string second) => VStack(first);

    [ReactorSourceTransparent]
    internal static Element PairUp(Element first, Element second) => HStack(first, second);

    [Fact]
    public void ExtensionThatTheGeneratedInstanceMemberHides_IsNotTrusted()
    {
        // gauge.Set(...) is the rival's string here and the generated Set's record there, so
        // PairUp(Element, Element) runs.
        FakeGaugeElement gauge = FakeGauge();
        var paired = PairUp(gauge, gauge.Set(_ => { }));

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)paired).Orientation);
        Assert.Null(paired.CallSite);
    }

    [Fact]
    public void SameOnAWrapperThatSpellsItsOwnBase_IsNotTrustedEither()
    {
        // FakeDialElement is complete here, but its generated Set(Action<FakeDial>) is still
        // invisible: dial.Set(...) is the rival's string here and the record there — beside
        // an incomplete gauge (re-bound) and on its own (bound as is).
        FakeGaugeElement gauge = FakeGauge();
        FakeDialElement dial = FakeDial();
        var rebound = PairUp(gauge, dial.Set(_ => { }));
        var bound = PairUp(dial, dial.Set(_ => { }));

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)rebound).Orientation);
        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)bound).Orientation);
        Assert.Null(rebound.CallSite);
        Assert.Null(bound.CallSite);
    }

    internal static T ApplySet<T>(global::System.Func<global::System.Action<FakeDial>, T> configure) => configure(_ => { });

    [Fact]
    public void MethodGroupThatTheGeneratedMemberWouldReplace_IsNotTrusted()
    {
        // dial.Set as a method group: the rival returns a string here, the generated Set the
        // record there, so ApplySet infers a different T and PairUp(Element, Element) runs.
        FakeDialElement dial = FakeDial();
        var paired = PairUp(dial, ApplySet(dial.Set));

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)paired).Orientation);
        Assert.Null(paired.CallSite);
    }

    [Fact]
    public void GeneratedMemberOnAnInvisibleFactoryCall_IsNotTrusted()
    {
        // FakeDial() binds to nothing here, so its .Set is unknown until generation.
        var paired = PairUp(FakeGauge(), FakeDial().Set((FakeDial _) => { }));

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)paired).Orientation);
        Assert.Null(paired.CallSite);
    }

    [Fact]
    public void GeneratedMemberInheritedFromAWrapperBase_IsNotTrusted()
    {
        // DerivedDial inherits FakeDialElement's generated Set, which beats the rival.
        DerivedDial dial = new();
        var paired = PairUp(dial, dial.Set((FakeDial _) => { }));

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)paired).Orientation);
        Assert.Null(paired.CallSite);
    }
}

/// <summary>Inherits the members the wrapper generator adds to <see cref="FakeDialElement"/>.</summary>
public record DerivedDial : FakeDialElement;
