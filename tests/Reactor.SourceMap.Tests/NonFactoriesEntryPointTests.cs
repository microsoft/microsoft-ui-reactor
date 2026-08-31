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
    /// The remaining gap. Meaningful only because
    /// <see cref="AFactoriesCallOnTheSameLinesIsStamped"/> proves the generator is
    /// alive and stamping in this very file — otherwise a null here could equally mean
    /// interception was off, and the test would pass while proving nothing.
    /// </summary>
    [Fact]
    public void EntryPointOutsideFactoriesIsNotStamped()
    {
        var el = OutsideFactories.Build();

        Assert.True(el.CallSite is null,
            "An unannotated element-producing entry point outside Factories now carries a " +
            "source location. That is an improvement, not a regression - but it means the " +
            "limitation documented in docs/guide/source-mapping.md is stale and must be " +
            "updated alongside this test.");
    }

    /// <summary>
    /// The half of the gap that <c>[ReactorSourceTransparent]</c> closes.
    /// <c>PendingFactory.Pending</c> is a pure forwarder, so it is annotated and its
    /// callers now get a location; the entry points that genuinely BUILD something of
    /// their own are the ones that stay unstamped, above.
    /// </summary>
    [Fact]
    public void AnnotatedEntryPointOutsideFactoriesIsStampedAtTheCallSite()
    {
        // Deliberately on ONE line: the stamp is taken from the argument list's opening
        // paren (matching what [CallerLineNumber] reports), so a call split across lines
        // reports the paren's line and not the last one. MultilineCallSiteTests owns that
        // behaviour; here it would only make the oracle wrong.
        var el = PendingFactory.Pending(TB("fallback"), TB("child")); int expected = Line();

        Assert.Equal(expected, el.CallSite!.Value.LineNumber);
    }

    private static Element TB(string text) => Microsoft.UI.Reactor.Factories.TextBlock(text);

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
        var el = OutsideFactories.Build();

        Assert.NotNull(el);
        Assert.Null(el.CallSite);
    }
}

/// <summary>
/// Stands in for a third-party element-producing entry point that lives outside
/// <c>Factories</c> and carries no <c>[ReactorSourceTransparent]</c> annotation.
///
/// <para>Wraps <c>PropertyGridDefaults.PropertyLabelTemplate</c>, a real public API in
/// <c>Microsoft.UI.Reactor.Controls</c> whose element is built by a <c>TextBlock</c> call
/// inside Reactor's own assembly. The suite previously used
/// <c>PendingFactory.Pending</c> for this, but that method is now annotated and IS
/// stamped, so continuing to use it would have quietly turned these "not stamped"
/// assertions into tests of the wrong thing.</para>
/// </summary>
internal static class OutsideFactories
{
    internal static Element Build()
        => Microsoft.UI.Reactor.Controls.PropertyGridDefaults.PropertyLabelTemplate(
            new Microsoft.UI.Reactor.Data.FieldDescriptor
            {
                Name = "probe",
                DisplayName = "Probe",
                FieldType = typeof(string),
                GetValue = static _ => "probe",
            },
            indentLevel: 0);
}

