using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.SourceMap.Tests.FakeGaugeElement;
using static Microsoft.UI.Reactor.SourceMap.Tests.GaugeRivals;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Spec 010 — hand-written methods named like a wrapper factory, imported beside it. Here
/// they are all that binds; in the real compilation the generated <c>FakeGauge(...)</c>
/// joins the overload set and wins. Compiling this file is the CS9144 oracle; a file of its
/// own because the <c>using static GaugeRivals</c> changes every <c>FakeGauge</c> call in it.
/// </summary>
[Collection("SourceMap")]
public sealed class WrapperFactoryCompetitorTests : IDisposable
{
    public WrapperFactoryCompetitorTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    [ReactorSourceTransparent]
    internal static Element ShowValue(string text) => VStack();

    [ReactorSourceTransparent]
    internal static Element ShowValue(Element child) => HStack(child);

    [Fact]
    public void CallThatTheGeneratedFactoryOutranks_IsNotIntercepted()
    {
        // FakeGauge(double?, params object[]) binds here in expanded form; the generated
        // FakeGauge(double? value = default) applies in normal form and wins.
        var gauge = FakeGauge(3);

        Assert.IsType<FakeGaugeElement>(gauge);
        Assert.Null(gauge.CallSite);
    }

    [Fact]
    public void ContainerOfACallWhoseTypeTheGeneratedFactoryChanges_IsNotTrusted()
    {
        // FakeGauge() binds to the string rival here and to the generated factory (the record)
        // there, which moves ShowValue from (string) to (Element) — directly and through a var.
        var shown = ShowValue(FakeGauge());
        var made = FakeGauge();
        var viaLocal = ShowValue(made);

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)shown).Orientation);
        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)viaLocal).Orientation);
        Assert.Null(shown.CallSite);
        Assert.Null(viaLocal.CallSite);
    }
}

/// <summary>Hand-written competitors for the generated <c>FakeGauge(...)</c>.</summary>
public static class GaugeRivals
{
    [ReactorSourceTransparent]
    public static Element FakeGauge(double? value, params object[] rest) => Microsoft.UI.Reactor.Factories.TextBlock("rival");

    public static string FakeGauge(params object[] rest) => "rival";
}
