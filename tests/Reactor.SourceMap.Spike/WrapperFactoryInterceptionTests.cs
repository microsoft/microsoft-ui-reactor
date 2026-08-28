using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.Wrappers;
using Xunit;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.SourceMap.Spike;

/// <summary>
/// Spec 010 — pins the one known gap in the "every DSL call site is stamped" contract:
/// factories that <c>Reactor.Wrappers.Generator</c> emits are NOT stamped.
///
/// <para><c>[GenerateReactorWrapper]</c> emits its public factory as a static ON the
/// generated element type (<c>SpikeBorderWrapperElement.Border(...)</c>) rather than on
/// <c>Microsoft.UI.Reactor.Factories</c>. Widening the interceptor's containing-type
/// filter to reach it does not work, and the reason is architectural rather than a
/// rendering bug: Roslyn runs every source generator against the same input
/// compilation, so the source-map generator cannot see symbols the wrapper generator
/// emitted. Measured both ways — widening the filter covered these calls in a WinUI
/// XAML app (which compiles twice, so the second pass sees the first pass's generated
/// files) and covered nothing in this single-pass project. Coverage that silently
/// varies with project shape is worse than a uniform documented gap, so the filter
/// stays narrow.</para>
///
/// <para>This is a characterization test, not an endorsement. If a real integration
/// path ever lands (the wrapper generator stamping its own factories, say), the first
/// assertion here fails and forces the documented limitation to be updated with it.</para>
///
/// <para>Shares the "SourceMap" collection with <see cref="SourceMapInterceptorTests"/>
/// because <see cref="ReactorSourceMap.Enabled"/> is process-global mutable state; the
/// collection makes the two classes run serially instead of racing the flag.</para>
/// </summary>
[Collection("SourceMap")]
public sealed class WrapperFactoryInterceptionTests : IDisposable
{
    public WrapperFactoryInterceptionTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    private static int Line([CallerLineNumber] int line = 0) => line;

    /// <summary>
    /// The documented gap. A null here is only meaningful because
    /// <see cref="PlainFactoryInTheSameFileIsStamped"/> proves the generator is alive
    /// and stamping in this very file — otherwise "no location" could equally mean the
    /// generator never ran, and this test would pass while proving nothing.
    /// </summary>
    [Fact]
    public void WrapperGeneratedFactoryIsNotStamped()
    {
        var el = SpikeBorderWrapperElement.Border();

        Assert.True(el.CallSite is null,
            "A wrapper-generated factory now carries a source location. That is an " +
            "improvement, not a regression - but it means the interceptor can reach " +
            "symbols emitted by another generator, so the limitation documented in " +
            "docs/guide/source-mapping.md and on SourceMapInterceptorGenerator.TryDescribe " +
            "is stale and must be updated alongside this test.");
    }

    /// <summary>
    /// Positive control for the oracle. Same file, same flag, same physical-line probe,
    /// so the null above is provably "this shape is not intercepted" rather than
    /// "interception is off in this test".
    /// </summary>
    [Fact]
    public void PlainFactoryInTheSameFileIsStamped()
    {
        var el = Microsoft.UI.Reactor.Factories.TextBlock("probe"); int expected = Line();

        Assert.NotNull(el.CallSite);
        Assert.Equal(expected, el.CallSite!.Value.LineNumber);
        Assert.EndsWith("WrapperFactoryInterceptionTests.cs", el.CallSite.Value.FilePath, global::System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The gap costs coverage, not correctness: an unstamped wrapper element is still a
    /// perfectly usable element and reads back as "location unknown" rather than as a
    /// confidently wrong line. That distinction is what makes the gap tolerable.
    /// </summary>
    [Fact]
    public void UnstampedWrapperElementIsStillUsable()
    {
        var el = SpikeBorderWrapperElement.Border();

        Assert.NotNull(el);
        Assert.Null(el.CallSite);
    }
}

/// <summary>
/// Stands in for a third-party control being wrapped. Declared here rather than reused
/// from another suite so the fixture is self-contained: the test asserts about the
/// wrapper path, so the wrapper has to be part of the test.
/// </summary>
[GenerateReactorWrapper(typeof(WinUI.Border))]
internal partial record SpikeBorderWrapperElement;
