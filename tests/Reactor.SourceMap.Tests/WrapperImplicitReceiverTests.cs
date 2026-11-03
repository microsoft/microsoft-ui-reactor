using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.SourceMap.Tests.ImplicitRivals.SetHelper;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Spec 010 — inside a wrapper record, an unqualified <c>Set(f)</c> binds an imported static
/// helper here and the generated instance <c>Set</c> once it exists.
/// </summary>
[Collection("SourceMap")]
public sealed class WrapperImplicitReceiverTests : IDisposable
{
    public WrapperImplicitReceiverTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    [Fact]
    public void UnqualifiedCallToAGeneratedInstanceMember_IsNotInterceptedAsTheHelper()
    {
        var configured = FakeDialElement.FakeDial().Configure(_ => { });

        // The generated Set ran, not the helper's TextBlock.
        Assert.IsType<FakeDialElement>(configured);
    }
}

public partial record FakeDialElement
{
    internal Element Configure(global::System.Action<FakeDial> configure) => Set(configure);
}
