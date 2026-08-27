using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.SourceMap.Spike;

/// <summary>
/// Spec 010 Route B — measurement 1 (coverage).
///
/// <para>Every line-number assertion is checked against an INDEPENDENT oracle
/// (<c>[CallerLineNumber]</c> captured on the same physical line) rather than a
/// hard-coded literal, so the tests survive edits above them and so a wrong
/// interceptor line cannot be papered over by updating a magic number.</para>
///
/// <para>All tests live in one class on purpose: <see cref="ReactorSourceMap.Enabled"/>
/// is process-global mutable state and xUnit runs tests within a class serially.</para>
/// </summary>
[Collection("SourceMap")]
public sealed class SourceMapInterceptorTests : IDisposable
{
    public SourceMapInterceptorTests() => ReactorSourceMap.Enabled = true;

    public void Dispose() => ReactorSourceMap.Enabled = false;

    private static int Line([CallerLineNumber] int line = 0) => line;

    // ── Coverage: non-params factory ──────────────────────────────────────

    [Fact]
    public void NonParamsFactory_ReportsItsOwnCallSite()
    {
        var element = TextBlock("hello"); var expected = Line();

        Assert.NotNull(element.CallSite);
        Assert.Equal(expected, element.CallSite!.Value.LineNumber);
        Assert.EndsWith("SourceMapInterceptorTests.cs", element.CallSite!.Value.FilePath, StringComparison.Ordinal);
    }

    // ── Coverage: params factory — the case CallerInfo cannot reach ────────

    [Fact]
    public void ParamsFactory_ReportsItsOwnCallSite()
    {
        var element = VStack(TextBlock("a"), TextBlock("b")); var expected = Line();

        Assert.NotNull(element.CallSite);
        Assert.Equal(expected, element.CallSite!.Value.LineNumber);
        Assert.EndsWith("SourceMapInterceptorTests.cs", element.CallSite!.Value.FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void ParamsFactory_StillFiltersAndForwardsChildren()
    {
        // The interceptor must be behaviour-preserving: it forwards the same
        // expanded array to the real factory, including the null-filtering the
        // factory does. If the interceptor dropped or reordered arguments this
        // count would move.
        var element = VStack(TextBlock("a"), null, TextBlock("b"));

        Assert.Equal(2, global::System.Linq.Enumerable.Count(element.Children));
    }

    // ── Two call sites on different lines must not collapse ───────────────

    [Fact]
    public void DistinctCallSites_GetDistinctLines()
    {
        var first = TextBlock("one"); var firstLine = Line();
        var second = TextBlock("two"); var secondLine = Line();

        Assert.NotEqual(firstLine, secondLine);
        Assert.Equal(firstLine, first.CallSite!.Value.LineNumber);
        Assert.Equal(secondLine, second.CallSite!.Value.LineNumber);
    }

    // ── The runtime flag actually gates the stamp ─────────────────────────

    [Fact]
    public void FlagOff_LeavesSourceNull()
    {
        ReactorSourceMap.Enabled = false;
        try
        {
            var element = TextBlock("hello");
            Assert.Null(element.CallSite);
        }
        finally
        {
            ReactorSourceMap.Enabled = true;
        }

        // Positive control for the assertion above: the SAME probe, in the same
        // file, with the flag on, DOES produce a stamp. Without this, a generator
        // that silently emitted nothing would pass the null check for the wrong
        // reason.
        var control = TextBlock("hello");
        Assert.NotNull(control.CallSite);
    }

    // ── Helper-method attribution (reported, not aspirational) ────────────

    private static TextBlockElement MyHeader() => TextBlock("header");

    [Fact]
    public void HelperMethod_AttributesToTheHelperNotItsCaller()
    {
        var element = MyHeader(); var callerLine = Line();

        // Interceptors replace the CALL SITE, and the call site of TextBlock is
        // inside MyHeader. So Route B reports the helper's own line — exactly the
        // same limitation CallerInfo has. Asserted rather than assumed.
        Assert.NotEqual(callerLine, element.CallSite!.Value.LineNumber);
        Assert.True(element.CallSite!.Value.LineNumber < callerLine);
    }

    // ── Stamp survives the fluent modifier chain ──────────────────────────

    [Fact]
    public void Source_SurvivesFluentModifiers()
    {
        var element = TextBlock("hi").Margin(8).Bold(); var expected = Line();

        Assert.Equal(expected, element.CallSite!.Value.LineNumber);
    }

    // ── Formatting ────────────────────────────────────────────────────────

    [Fact]
    public void ToShortString_DropsTheDirectory()
    {
        var element = TextBlock("hi");
        var text = element.CallSite!.Value.ToShortString();

        Assert.StartsWith("SourceMapInterceptorTests.cs:", text, StringComparison.Ordinal);
        Assert.DoesNotContain(global::System.IO.Path.DirectorySeparatorChar, text);
    }
}
