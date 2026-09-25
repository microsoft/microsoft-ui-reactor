using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Ties the file-based-app header documented in the Getting Started guide to the framework's
/// own target framework.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this needs a test at all.</b> A .NET file-based app is defined by having no
/// <c>.csproj</c>, so the guide's <c>#:</c> header cannot be a compiled doc-app snippet the way
/// every other C# block on that page is — it is ledgered in
/// <c>InlineSnippetLedgerTests.AllowedInlineExamples</c> instead. That buys a hole: nothing
/// compiles those six lines, and the page renders them identically to the verified blocks around
/// them.
/// </para>
/// <para>
/// <b>The values that actually rot.</b> The Reactor version is the <c>{{reactorVersion}}</c>
/// token the doc compiler substitutes, so it cannot go stale. Every other directive is asserted
/// here, because nothing else looks at them: the target framework against the framework project,
/// and the rest for presence — plus, for <c>WindowsPackageType</c>, placement, since which block
/// carries it is what makes one example packaged and the other not. The target framework is the
/// worst to get wrong: a reader who copies a stale Windows version compiles against nothing and
/// gets <c>CS0234: the type or namespace name 'Reactor' does not exist</c>, which reads like a
/// missing package rather than a wrong TFM. Measured, not assumed — that is the exact error
/// <c>net10.0-windows10.0.19041.0</c> produces against this package.
/// </para>
/// <para>
/// The <c>Microsoft.Windows.SDK.BuildTools.WinApp</c> pin in the packaged example is deliberately
/// <em>not</em> version-guarded. <c>WinAppSDKReferenceGuardTests</c> does sweep <c>#:package</c>
/// headers, but only for <c>Microsoft.WindowsAppSDK…</c>, and this repo has no central pin for
/// the build-tools package to check a doc against — it is a consumer-side tool rather than a
/// framework dependency. Its presence is asserted below; its version is not.
/// </para>
/// </remarks>
public class SingleFileGuideHeaderTests
{
    private const string GuideTemplate = "docs/_pipeline/templates/getting-started.md.dt";
    private const string FrameworkProject = "src/Reactor/Reactor.csproj";

    /// <summary>
    /// Every <c>#:property TargetFramework=…</c> in the guide must name the TFM
    /// <c>src/Reactor/Reactor.csproj</c> builds, because that is what the published package's
    /// <c>lib/</c> folder is keyed on.
    /// </summary>
    [Fact]
    public void DocumentedTargetFramework_MatchesTheFrameworkProject()
    {
        var root = RepoRoot();
        var expected = FrameworkTfm(root);

        var guide = File.ReadAllText(Path.Join(root, GuideTemplate));
        var documented = Regex.Matches(guide, @"^#:property\s+TargetFramework=(?<tfm>\S+)\s*$", RegexOptions.Multiline)
            .Select(m => m.Groups["tfm"].Value)
            .ToArray();

        // A rename or a restructure that drops the header entirely would otherwise leave this
        // test passing over an empty set — the classic vacuous green.
        Assert.NotEmpty(documented);

        foreach (var tfm in documented)
        {
            Assert.True(
                string.Equals(tfm, expected, StringComparison.Ordinal),
                $"{GuideTemplate} documents '#:property TargetFramework={tfm}' but {FrameworkProject} "
                + $"builds '{expected}'. A reader copying the stale value compiles against no Reactor "
                + "assembly and sees CS0234 \"the namespace 'Reactor' does not exist\", which looks "
                + "like a missing package. Update the guide header (both the unpackaged and packaged "
                + "blocks) to match.");
        }
    }

    /// <summary>
    /// The primary example carries the full header; the second block is the <c>dotnet run</c>
    /// delta and must carry exactly the two directives that delta exists to add.
    /// </summary>
    /// <remarks>
    /// The section's whole shape is "here is the file, and here are the two lines you add to run
    /// it without <c>winapp</c>". If either directive migrated into the primary block, that block
    /// would stop matching the blog and the published quick-start, and the delta would no longer
    /// be a delta — while a whole-file <c>Contains</c> check stayed green through the move. So
    /// placement is asserted, not just presence.
    /// </remarks>
    [Fact]
    public void DotnetDeltaDirectives_SitInTheSecondBlockOnly()
    {
        var guide = File.ReadAllText(Path.Join(RepoRoot(), GuideTemplate));
        var blocks = HeaderBlocks(guide);

        // Two header blocks: the complete file, then the dotnet-run delta. If the section is
        // restructured into a different number, this needs rereading rather than silently
        // passing over whichever blocks happen to remain.
        Assert.Equal(2, blocks.Count);

        foreach (var directive in new[]
        {
            "#:property WindowsPackageType=None",
            "#:property RuntimeIdentifier=$(NETCoreSdkPortableRuntimeIdentifier)",
        })
        {
            Assert.True(
                blocks[1].Contains(directive, StringComparison.Ordinal),
                $"The second #: block is the 'run it with plain dotnet' delta and must declare "
                + $"'{directive}'. Without RuntimeIdentifier the build stops with "
                + "\"WindowsAppSDKSelfContained requires a supported Windows architecture\"; "
                + "without WindowsPackageType=None the app builds clean and then dies at startup "
                + "with REGDB_E_CLASSNOTREG.");

            Assert.False(
                blocks[0].Contains(directive, StringComparison.Ordinal),
                $"The first #: block is the complete single-file app as `winapp run` needs it, "
                + $"and must NOT declare '{directive}'. winapp supplies the architecture itself, "
                + "and that four-directive header is what the published blog and quick-start "
                + "show — adding a line here silently forks them.");
        }
    }

    /// <summary>
    /// Every directive the primary header's table calls load-bearing must appear in it.
    /// </summary>
    /// <remarks>
    /// These share a failure mode: the block is exempt from compilation, so dropping any one of
    /// them leaves the whole suite green while a reader's run fails. Each row of the table is a
    /// claim, and each claim is checked.
    /// <list type="bullet">
    /// <item><c>OutputType=WinExe</c> — does not fail the build at all; it links the app for the
    /// console subsystem instead. Measured on this tree: the PE subsystem byte reads 3 (console)
    /// without it and 2 (Windows GUI) with it, so a console window sits behind the UI.</item>
    /// <item><c>TargetFramework</c> — also checked against the framework project by
    /// <see cref="DocumentedTargetFramework_MatchesTheFrameworkProject"/>; this pins its presence
    /// in the primary block specifically.</item>
    /// <item><c>UseWinUI=true</c> — brings in the WinUI 3 targets.</item>
    /// </list>
    /// </remarks>
    [Theory]
    [InlineData("#:property OutputType=WinExe")]
    [InlineData("#:property TargetFramework=")]
    [InlineData("#:property UseWinUI=true")]
    public void PrimaryHeader_DeclaresTheLoadBearingDirectives(string directive)
    {
        var guide = File.ReadAllText(Path.Join(RepoRoot(), GuideTemplate));
        var blocks = HeaderBlocks(guide);

        Assert.Equal(2, blocks.Count);
        Assert.True(
            blocks[0].Contains(directive, StringComparison.Ordinal),
            $"The primary single-file header in {GuideTemplate} is missing '{directive}'. The "
            + "guide's directive table documents it as required, and this block is exempt from "
            + "compilation, so nothing else would catch its removal.");
    }

    /// <summary>
    /// The section must still name the package that turns <c>dotnet run</c> into a packaged
    /// launch.
    /// </summary>
    /// <remarks>
    /// It is prose rather than a third header block, so nothing else would notice it going
    /// missing. Presence only — see the class remarks for why the version is not pinned.
    /// </remarks>
    [Fact]
    public void Section_NamesTheBuildToolsPackageForPackagedDotnetRun()
    {
        var guide = File.ReadAllText(Path.Join(RepoRoot(), GuideTemplate));

        Assert.Contains("Microsoft.Windows.SDK.BuildTools.WinApp", guide, StringComparison.Ordinal);
    }

    /// <summary>
    /// The contiguous runs of <c>#:</c> directives in the guide, in document order.
    /// </summary>
    private static List<string> HeaderBlocks(string guide)
    {
        var blocks = new List<string>();
        var current = new List<string>();

        foreach (var line in guide.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (line.StartsWith("#:", StringComparison.Ordinal))
            {
                current.Add(line);
            }
            else if (current.Count > 0)
            {
                blocks.Add(string.Join('\n', current));
                current.Clear();
            }
        }

        if (current.Count > 0)
            blocks.Add(string.Join('\n', current));

        return blocks;
    }

    private static string FrameworkTfm(string root)
    {
        var csproj = File.ReadAllText(Path.Join(root, FrameworkProject));
        var match = Regex.Match(csproj, @"<TargetFramework>(?<tfm>[^<]+)</TargetFramework>");

        Assert.True(match.Success, $"No <TargetFramework> found in {FrameworkProject}.");
        return match.Groups["tfm"].Value.Trim();
    }

    private static string RepoRoot()
    {
        // Anchor on the solution file, not on .git: in a git worktree .git is a *file*, so a
        // Directory.Exists probe walks past the root and returns null. Mirrors FindRepoRoot in
        // InlineSnippetLedgerTests.
        var dir = AppContext.BaseDirectory;
        while (dir is not null
               && !File.Exists(Path.Join(dir, "Reactor.slnx"))
               && !Directory.Exists(Path.Join(dir, ".git")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir!;
    }
}
