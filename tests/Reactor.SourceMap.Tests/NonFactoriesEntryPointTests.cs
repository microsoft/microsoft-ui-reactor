using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.Hooks;
using Xunit;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Spec 010 — pins the limitation that element-producing entry points which do NOT
/// live on <c>Microsoft.UI.Reactor.Factories</c> are not stamped.
///
/// <para>The interceptor's containing-type filter is exactly
/// <c>Microsoft.UI.Reactor.Factories</c>. An entry point such as
/// <c>PendingFactory.Pending(...)</c> builds its element by calling
/// <c>Factories.Component&lt;,&gt;</c> from INSIDE Reactor's own assembly, and an
/// interceptor only rewrites call sites present in the consumer's compilation — so
/// neither the outer call nor the inner one is reachable, and the returned element
/// carries no location. Instance members (<c>IntlAccessor.RichMessage(...)</c>) are
/// excluded a step earlier still, by the generator's <c>IsStatic</c> filter.</para>
///
/// <para>Characterization, not endorsement: as with
/// <see cref="WrapperFactoryInterceptionTests"/>, the gap costs coverage rather than
/// correctness — these elements read back as "location unknown" instead of naming a
/// framework line that is not the author's. If interception ever widens to reach
/// them, the first assertion here fails and forces the documented limitation in
/// <c>docs/guide/source-mapping.md</c> to be updated with it.</para>
///
/// <para>Shares the "SourceMap" collection because
/// <see cref="ReactorSourceMap.Enabled"/> is process-global mutable state.</para>
/// </summary>
[Collection("SourceMap")]
public sealed class NonFactoriesEntryPointTests : IDisposable
{
    public NonFactoriesEntryPointTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    private static int Line([CallerLineNumber] int line = 0) => line;

    /// <summary>
    /// The documented gap. Meaningful only because
    /// <see cref="AFactoriesCallOnTheSameLinesIsStamped"/> proves the generator is
    /// alive and stamping in this very file — otherwise a null here could equally mean
    /// interception was off, and the test would pass while proving nothing.
    /// </summary>
    [Fact]
    public void EntryPointOutsideFactoriesIsNotStamped()
    {
        var el = PendingFactory.Pending(Microsoft.UI.Reactor.Factories.TextBlock("fallback"), Microsoft.UI.Reactor.Factories.TextBlock("child"));

        Assert.True(el.CallSite is null,
            "An element-producing entry point outside Factories now carries a source " +
            "location. That is an improvement, not a regression - but it means the " +
            "limitation documented in docs/guide/source-mapping.md is stale and must be " +
            "updated alongside this test.");
    }

    /// <summary>
    /// Positive control for the oracle above: same file, same flag, same physical-line
    /// probe. Also shows the gap is scoped to the entry point itself — the element
    /// ARGUMENTS a consumer passes in are ordinary <c>Factories</c> call sites and are
    /// stamped normally.
    /// </summary>
    [Fact]
    public void AFactoriesCallOnTheSameLinesIsStamped()
    {
        var el = Microsoft.UI.Reactor.Factories.TextBlock("probe"); int expected = Line();

        Assert.NotNull(el.CallSite);
        Assert.Equal(expected, el.CallSite!.Value.LineNumber);
        Assert.EndsWith("NonFactoriesEntryPointTests.cs", el.CallSite.Value.FilePath, global::System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The gap costs coverage, not correctness: the element is still perfectly usable
    /// and reports "location unknown" rather than a confidently wrong line.
    /// </summary>
    [Fact]
    public void UnstampedEntryPointElementIsStillUsable()
    {
        var el = PendingFactory.Pending(Microsoft.UI.Reactor.Factories.TextBlock("fallback"), Microsoft.UI.Reactor.Factories.TextBlock("child"));

        Assert.NotNull(el);
        Assert.Null(el.CallSite);
    }
}

