using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.SourceMap.Tests.FakeGaugeElement;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Spec 010 — a METHOD named <c>nameof</c> is an ordinary call, not the operator, so a
/// wrapper value passed to it is analysed like any other. A file of its own: declaring the
/// methods here changes what every <c>nameof(...)</c> in the class binds to.
/// </summary>
[Collection("SourceMap")]
public sealed class WrapperFactoryNameofTests : IDisposable
{
    public WrapperFactoryNameofTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    internal static string @nameof(object value) => "object";

    internal static Element @nameof(Element value) => value;

    [Fact]
    public void EscapedMethodNamedNameof_IsNotTheOperator()
    {
        // @nameof(object) is all that binds here, so Button(string) would; once the record is
        // an Element, @nameof(Element) wins and Button(Element) runs.
        FakeGaugeElement gauge = FakeGauge();
        var button = Button(@nameof(gauge));

        Assert.NotEqual("object", button.Label);
        Assert.Null(button.CallSite);
    }
}
