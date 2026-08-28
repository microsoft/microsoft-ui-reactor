using System.Collections.Immutable;
using Microsoft.UI.Reactor.SourceMap.Generator;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 010 — <c>ApplyPathMap</c> must reproduce Roslyn's <c>PathMap</c> rewriting
/// exactly, because the two source-map providers are compared as strings.
///
/// <para>The compiler rewrites <c>[CallerFilePath]</c> through <c>PathMap</c>, but does
/// NOT rewrite a string literal a generator emitted — so the generator has to apply the
/// same transform itself. Any divergence produces two different strings for the same
/// file, and a "go to source" consumer behaves differently depending on which provider
/// is wired in.</para>
///
/// <para>The end-to-end check in the spike
/// (<c>InterceptorPath_MatchesWhatCallerFilePathWouldProduce</c>) only exercises whatever
/// map the current build happens to set, which is an exact-cased one. These call
/// <c>ApplyPathMap</c> directly to reach the edges that build cannot produce.</para>
/// </summary>
public class PathMapParityTests
{
    private static ImmutableArray<KeyValuePair<string, string>> Map(params (string from, string to)[] entries)
        => entries.Select(e => new KeyValuePair<string, string>(e.from, e.to)).ToImmutableArray();

    /// <summary>
    /// Roslyn's <c>PathUtilities.NormalizePathPrefix</c> matches with
    /// <c>StringComparison.Ordinal</c> and says so in its own comment: "we expect the
    /// client to use consistent capitalization; we use ordinal (case-sensitive)
    /// comparisons". A case-insensitive match here would rewrite a path the compiler
    /// left alone — the generator would report <c>/_/Foo.cs</c> while
    /// <c>[CallerFilePath]</c> reported <c>C:\SRC\Foo.cs</c>.
    /// </summary>
    [Fact]
    public void PrefixMatchIsCaseSensitive()
    {
        var map = Map((@"C:\src\", "/_/"));

        Assert.Equal(@"C:\SRC\Foo.cs", SourceMapInterceptorGenerator.ApplyPathMap(@"C:\SRC\Foo.cs", map));
    }

    /// <summary>
    /// Positive control for the test above. If <c>ApplyPathMap</c> silently returned its
    /// input for every path, the case-sensitivity assertion would pass while proving
    /// nothing, so an exactly-cased prefix must still be rewritten.
    /// </summary>
    [Fact]
    public void ExactlyCasedPrefixIsRewritten()
    {
        var map = Map((@"C:\src\", "/_/"));

        Assert.Equal("/_/Foo.cs", SourceMapInterceptorGenerator.ApplyPathMap(@"C:\src\Foo.cs", map));
    }

    /// <summary>
    /// Roslyn normalizes the separators of the REMAINDER to match the replacement when
    /// the replacement uses one separator uniformly. Prefix substitution alone yields
    /// <c>/_/tests\Foo\Bar.cs</c> where CallerInfo yields <c>/_/tests/Foo/Bar.cs</c>.
    /// </summary>
    [Fact]
    public void SeparatorsOfTheRemainderFollowTheReplacement()
    {
        var map = Map((@"C:\src\", "/_/"));

        Assert.Equal("/_/tests/Foo/Bar.cs",
            SourceMapInterceptorGenerator.ApplyPathMap(@"C:\src\tests\Foo\Bar.cs", map));
    }

    /// <summary>
    /// A replacement that uses backslashes uniformly normalizes the other way, so the
    /// test above is not passing merely because forward slashes are hardcoded somewhere.
    /// </summary>
    [Fact]
    public void SeparatorNormalizationWorksInBothDirections()
    {
        var map = Map(("/src/", @"X:\out\"));

        Assert.Equal(@"X:\out\tests\Foo.cs",
            SourceMapInterceptorGenerator.ApplyPathMap("/src/tests/Foo.cs", map));
    }

    /// <summary>
    /// Roslyn takes the FIRST matching entry, not the longest or the last.
    /// </summary>
    [Fact]
    public void FirstMatchingEntryWins()
    {
        var map = Map((@"C:\src\", "/first/"), (@"C:\src\deep\", "/second/"));

        Assert.Equal("/first/deep/Foo.cs",
            SourceMapInterceptorGenerator.ApplyPathMap(@"C:\src\deep\Foo.cs", map));
    }

    [Fact]
    public void NonMatchingPathIsReturnedUnchanged()
    {
        var map = Map((@"C:\src\", "/_/"));

        Assert.Equal(@"D:\other\Foo.cs", SourceMapInterceptorGenerator.ApplyPathMap(@"D:\other\Foo.cs", map));
    }

    [Fact]
    public void EmptyMapIsIdentity()
    {
        Assert.Equal(@"C:\src\Foo.cs",
            SourceMapInterceptorGenerator.ApplyPathMap(@"C:\src\Foo.cs", ImmutableArray<KeyValuePair<string, string>>.Empty));
    }
}
