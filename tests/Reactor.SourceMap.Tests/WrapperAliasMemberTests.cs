using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.SourceMap.Tests.AliasRivals;
using Microsoft.UI.Reactor.Wrappers;
using Xunit;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Spec 010 — a generated callback property named after a <c>[WrapAlias]</c> name
/// (<c>OnLevelChanged</c> for <c>Value</c> surfaced as <c>Level</c>) hides an extension
/// method group of that name once it exists. A file of its own: only it imports the rival.
/// </summary>
[Collection("SourceMap")]
public sealed class WrapperAliasMemberTests : IDisposable
{
    public WrapperAliasMemberTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    [ReactorSourceTransparent]
    internal static Element ShowHandler(global::System.Func<double, string> handler) => VStack();

    [ReactorSourceTransparent]
    internal static Element ShowHandler(global::System.Action<double>? handler) => HStack();

    [Fact]
    public void AliasedCallbackPropertyThatHidesAnExtension_IsNotTrusted()
    {
        // Here meter.OnLevelChanged is the rival extension's method group; once generated it
        // is the record's Action<double> callback property.
        FakeLevelMeterElement meter = FakeLevelMeterElement.FakeLevelMeter();
        var shown = ShowHandler(meter.OnLevelChanged);

        Assert.Equal(WinUI.Orientation.Horizontal, ((StackElement)shown).Orientation);
        Assert.Null(shown.CallSite);
    }
}

/// <summary>A control with a two-way <c>Value</c>, surfaced as <c>Level</c>.</summary>
public partial class FakeLevelMeter : WinUI.Control
{
    public double Value { get; set; }

    public event global::System.EventHandler<object>? ValueChanged;

    internal void Raise() => ValueChanged?.Invoke(this, this);
}

[GenerateReactorWrapper(typeof(FakeLevelMeter), AutoDiscover = false, Include = new[] { "Value" }, RegisterAssembly = false)]
[WrapAlias("Level", "Value")]
[WrapControlled("Value")]
public partial record FakeLevelMeterElement : Element;
