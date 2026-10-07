using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 010 — the column the source-map generator stamps into
/// <c>SourceLocation.ColumnNumber</c>, asserted on the emitted source of a synthetic
/// compilation (the driver from <see cref="SourceMapTransparentGeneratorTests"/>).
///
/// <para>The live suite (<c>Reactor.SourceMap.Tests/ColumnTests.cs</c>) proves the
/// column round-trips through real factories; this one pins the layouts a live test
/// cannot write without a reformat undoing it — a name and its paren on different lines,
/// a tab-indented call — and proves the stamp is per call site, not per line.</para>
///
/// <para>Expected positions are computed from the snippet text itself, never written as
/// literals, so they stay correct if a snippet is re-indented.</para>
/// </summary>
public sealed class SourceMapColumnGeneratorTests
{
    private static readonly Regex s_returnStamp = new(
        @"SourceLocation\(@""[^""]*"", (?<line>\d+), (?<col>\d+)\)", RegexOptions.CultureInvariant);

    private static readonly Regex s_argumentStamp = new(
        @"__ReactorStampArgument\([^,]+, @""[^""]*"", (?<line>\d+), (?<col>\d+)\)", RegexOptions.CultureInvariant);

    private static (int Line, int Column)[] Stamps(Regex regex, string generated)
        => regex.Matches(generated)
            .Select(static m => (int.Parse(m.Groups["line"].Value), int.Parse(m.Groups["col"].Value)))
            .ToArray();

    /// <summary>1-based (line, column) of the <paramref name="occurrence"/>-th match.</summary>
    private static (int Line, int Column) PositionOf(string source, string needle, int occurrence = 1)
    {
        int index = -1;
        for (int i = 0; i < occurrence; i++)
        {
            index = source.IndexOf(needle, index + 1, StringComparison.Ordinal);
            Assert.True(index >= 0, $"probe '{needle}' (#{occurrence}) not found in the snippet");
        }

        int line = 1 + source.Take(index).Count(static c => c == '\n');
        int lineStart = source.LastIndexOf('\n', Math.Max(index - 1, 0)) + 1;
        if (index == 0) lineStart = 0;
        return (line, index - lineStart + 1);
    }

    [Fact]
    public void SeveralCallsOnOneLine_EachGetTheirOwnNameColumn()
    {
        const string code = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;

            public static class Host
            {
                public static Element Render() => VStack(TextBlock("a"), TextBlock("b"));
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);
        var stamps = Stamps(s_returnStamp, generated);

        var expected = new[]
        {
            PositionOf(code, "VStack("),
            PositionOf(code, "TextBlock(\"a\")"),
            PositionOf(code, "TextBlock(\"b\")"),
        };

        Assert.Equal(3, expected.Select(static p => p.Column).Distinct().Count());
        Assert.Equal(expected.OrderBy(static p => p.Column), stamps.OrderBy(static p => p.Column));
    }

    [Fact]
    public void QualifiedCall_StampsTheMethodName()
    {
        const string code = """
            using Microsoft.UI.Reactor.Core;

            public static class Host
            {
                public static Element Render() => Microsoft.UI.Reactor.Factories.TextBlock("q");
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);

        Assert.Equal(new[] { PositionOf(code, "TextBlock(") }, Stamps(s_returnStamp, generated));
    }

    [Fact]
    public void NameAndParenOnDifferentLines_ColumnFollowsTheParenLine()
    {
        // The line follows the paren for [CallerLineNumber] parity, so the column must
        // come from the paren too; the name's column belongs to a different line.
        const string code = """
            using Microsoft.UI.Reactor.Core;

            public static class Host
            {
                public static Element Render() => Microsoft.UI.Reactor.Factories.TextBlock
                    ("split");
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);

        var paren = PositionOf(code, "(\"split\")");
        Assert.Equal(new[] { paren }, Stamps(s_returnStamp, generated));
        Assert.NotEqual(PositionOf(code, "TextBlock").Column, paren.Column);
    }

    [Fact]
    public void TabCountsAsOneColumn()
    {
        var code = "using Microsoft.UI.Reactor.Core;\nusing static Microsoft.UI.Reactor.Factories;\n"
            + "public static class Host\n{\n\tpublic static Element Render() =>\n\t\tTextBlock(\"t\");\n}\n";

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);

        var stamp = Assert.Single(Stamps(s_returnStamp, generated));
        Assert.Equal(3, stamp.Column);
        Assert.Equal(PositionOf(code, "TextBlock("), stamp);
    }

    [Fact]
    public void ConvertedArgument_StampsTheArgumentExpressionColumn()
    {
        const string code = """
            using Microsoft.UI.Reactor.Core;
            using static Microsoft.UI.Reactor.Factories;

            public static class Host
            {
                public static Element Render() => VStack("first", "second");
            }
            """;

        var (_, generated) = SourceMapTransparentGeneratorTests.Run(code);

        Assert.Equal(
            new[] { PositionOf(code, "\"first\""), PositionOf(code, "\"second\"") },
            Stamps(s_argumentStamp, generated).OrderBy(static p => p.Column));
    }

    // ── Root mount call sites (ReactorApp.Run / OpenWindow, host Mount) ──────

    private static readonly Regex s_rootMountStamp = new(
        @"EnterRootMountSite\(@""[^""]*"", (?<line>\d+), (?<col>\d+)\)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Minimal stand-ins for the root-mount entry points; the generator matches them by
    /// metadata name. Two candidates on one line prove the column is per call.
    /// </summary>
    private const string RootMountCode = """
        using Microsoft.UI.Reactor.Core;
        using Microsoft.UI.Reactor.Hosting;
        using static Microsoft.UI.Reactor.Factories;

        namespace Microsoft.UI.Reactor
        {
            public static class ReactorApp
            {
                public static void OpenWindow(string title, global::System.Func<Element> root) { }
            }
        }
        namespace Microsoft.UI.Reactor.Hosting
        {
            public sealed class ReactorHost
            {
                public void Mount(global::System.Func<Element> root) { }
            }
        }

        public static class App
        {
            public static void Start(ReactorHost host)
            {
                Microsoft.UI.Reactor.ReactorApp.OpenWindow("a", () => TextBlock("x")); host.Mount(() => TextBlock("y"));
                host
                    .Mount
                    (() => TextBlock("z"));
            }
        }
        """;

    [Fact]
    public void RootMounts_StampTheMethodNameColumn()
    {
        var (_, generated) = SourceMapTransparentGeneratorTests.Run(RootMountCode);
        var stamps = Stamps(s_rootMountStamp, generated);

        var openWindow = PositionOf(RootMountCode, "OpenWindow(\"a\"");
        var sameLineMount = PositionOf(RootMountCode, "Mount(() => TextBlock(\"y\")");

        Assert.Equal(3, stamps.Length);
        Assert.Equal(openWindow.Line, sameLineMount.Line);
        Assert.Contains(openWindow, stamps);
        Assert.Contains(sameLineMount, stamps);
    }

    [Fact]
    public void RootMount_NameAndParenOnDifferentLines_ColumnFollowsTheParenLine()
    {
        var (_, generated) = SourceMapTransparentGeneratorTests.Run(RootMountCode);

        Assert.Contains(PositionOf(RootMountCode, "(() => TextBlock(\"z\"))"), Stamps(s_rootMountStamp, generated));
    }
}
