using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Spec 010: <see cref="ReactorApp.DevtoolsEnabled"/> mirrors into
/// <c>ReactorSourceMap.Enabled</c>. That mirror is how the in-app preview host
/// (<c>--devtools run</c> / <c>--preview</c>) turns call-site stamping on, and every other
/// source-map test sets <c>ReactorSourceMap.Enabled</c> directly, so without these the mirror
/// could be deleted while the whole source-map suite stayed green.
///
/// <para>In the SourceMapGlobals collection: the setter and
/// <c>ResetDevtoolsEnabledForTests()</c> both write <c>ReactorSourceMap.Enabled</c>, which
/// <c>SourceMapElementSlotTests</c> reads in parallel otherwise.</para>
/// </summary>
[Collection("SourceMapGlobals")]
public sealed class DevtoolsEnabledSourceMapTests : IDisposable
{
    public DevtoolsEnabledSourceMapTests() => ReactorApp.ResetDevtoolsEnabledForTests();
    public void Dispose() => ReactorApp.ResetDevtoolsEnabledForTests();

    [Fact]
    public void EnablingDevtools_TurnsSourceMappingOn()
    {
        Assert.False(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);

        ReactorApp.DevtoolsEnabled = true;

        Assert.True(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);
    }

    [Fact]
    public void DisablingDevtools_TurnsSourceMappingOff()
    {
        ReactorApp.DevtoolsEnabled = true;
        Assert.True(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);

        ReactorApp.DevtoolsEnabled = false;

        Assert.False(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);
    }

    [Fact]
    public void ResettingDevtools_TurnsSourceMappingOff()
    {
        // The reset helper writes the backing field directly, bypassing the setter that
        // mirrors, so it needs its own check: a reset that left source mapping on would leak
        // the flag into every later test.
        ReactorApp.DevtoolsEnabled = true;
        Assert.True(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);

        ReactorApp.ResetDevtoolsEnabledForTests();

        Assert.False(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);
        Assert.False(ReactorApp.DevtoolsEnabled);
    }
}
