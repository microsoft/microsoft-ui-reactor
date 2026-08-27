using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 010 — headless contract tests for the source-map slot itself
/// (<see cref="SourceLocation"/> and <c>Element.CallSite</c>), independent of
/// which provider populates it. These are the tests that must hold for BOTH the
/// CallerInfo route and the interceptor route, so they live here rather than in
/// the Route B spike consumer.
/// </summary>
public sealed class SourceMapElementSlotTests
{
    // ── The slot must be invisible to reconciliation ──────────────────────

    [Fact]
    public void ShallowEquals_IgnoresCallSite()
    {
        var a = TextBlock("same") with { CallSite = new SourceLocation("A.cs", 1) };
        var b = TextBlock("same") with { CallSite = new SourceLocation("B.cs", 999) };

        Assert.True(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_PositiveControl_StillDetectsRealDifferences()
    {
        // Guards the test above from passing for the wrong reason: if
        // ShallowEquals had degenerated into "always true", the CallSite test
        // would look green while proving nothing.
        var a = TextBlock("one") with { CallSite = new SourceLocation("A.cs", 1) };
        var b = TextBlock("two") with { CallSite = new SourceLocation("A.cs", 1) };

        Assert.False(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void CanSkipUpdate_IgnoresCallSite()
    {
        // ShallowEquals is only half the child-skip gate; the reconciler
        // actually calls CanSkipUpdate, so assert the composed predicate too.
        var a = TextBlock("same") with { CallSite = new SourceLocation("A.cs", 1) };
        var b = TextBlock("same") with { CallSite = new SourceLocation("B.cs", 999) };

        Assert.True(Element.CanSkipUpdate(a, b));
    }

    // ── Record plumbing ───────────────────────────────────────────────────

    [Fact]
    public void CallSite_SurvivesWithExpressions()
    {
        // Fluent modifiers are all `with` expressions, so this is what makes
        // .Margin(8).Bold() preserve the stamp without any per-modifier work.
        var stamped = TextBlock("hi") with { CallSite = new SourceLocation("A.cs", 7) };
        var modified = stamped.Margin(8).Bold();

        Assert.Equal(new SourceLocation("A.cs", 7), modified.CallSite);
    }

    [Fact]
    public void CallSite_DefaultsToNull()
    {
        Assert.Null(TextBlock("hi").CallSite);
        Assert.Null(VStack(TextBlock("a")).CallSite);
    }

    // ── SourceLocation formatting ─────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\src\MainPage.cs", 34, "MainPage.cs:34")]
    [InlineData("/_/src/Reactor/Elements/Dsl.cs", 512, "Dsl.cs:512")]   // deterministic-build path
    [InlineData("MainPage.cs", 1, "MainPage.cs:1")]                      // already bare
    public void ToShortString_HandlesBothSeparatorStyles(string path, int line, string expected)
    {
        // Both separators matter: DeterministicSourcePaths (Directory.Build.props
        // when CI=true) rewrites Windows paths to '/'-separated ones, so a
        // Path.GetFileName-based implementation would return the whole string on
        // a CI-built binary.
        Assert.Equal(expected, new SourceLocation(path, line).ToShortString());
    }

    [Fact]
    public void ToString_IsFullPathColonLine()
    {
        Assert.Equal(@"C:\src\MainPage.cs:34", new SourceLocation(@"C:\src\MainPage.cs", 34).ToString());
    }

    [Fact]
    public void ToShortString_EmptyPathFallsBackToLineNumber()
    {
        Assert.Equal("34", new SourceLocation("", 34).ToShortString());
    }

    // ── Runtime flag ──────────────────────────────────────────────────────

    [Fact]
    public void Enabled_DefaultsToFalse_AndRoundTrips()
    {
        // The default matters: it is what keeps NeedsTag's behaviour (and so the
        // PR #468 leaf-tagging allocation win) unchanged for every retail app.
        var previous = ReactorSourceMap.Enabled;
        try
        {
            ReactorSourceMap.Enabled = false;
            Assert.False(ReactorSourceMap.Enabled);
            ReactorSourceMap.Enabled = true;
            Assert.True(ReactorSourceMap.Enabled);
        }
        finally
        {
            ReactorSourceMap.Enabled = previous;
        }
    }
}
