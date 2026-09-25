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
    /// The unpackaged example must declare <c>WindowsPackageType=None</c> and the packaged one
    /// must not — the directive's *placement* is what the section teaches.
    /// </summary>
    /// <remarks>
    /// Asserting the file merely contains the directive somewhere is too weak: the two blocks
    /// differ by two lines, so the realistic drift is a copy-paste that moves it between them,
    /// not one that deletes it. Moving it would leave a whole-file <c>Contains</c> green while
    /// the unpackaged example lost the directive its own table says is mandatory *and* the
    /// packaged example silently stopped being packaged. Without the directive an app builds
    /// clean and then dies at startup with <c>REGDB_E_CLASSNOTREG</c>, because the WinAppSDK
    /// bootstrapper ships in the output but never auto-initializes.
    /// </remarks>
    [Fact]
    public void PackageTypeDirective_SitsInTheUnpackagedExampleOnly()
    {
        var guide = File.ReadAllText(Path.Join(RepoRoot(), GuideTemplate));
        var blocks = HeaderBlocks(guide);

        // Two header blocks: the unpackaged example, then the packaged delta. If the section is
        // restructured into a different number, this needs rereading rather than silently
        // passing over whichever blocks happen to remain.
        Assert.Equal(2, blocks.Count);

        Assert.True(
            blocks[0].Contains("#:property WindowsPackageType=None", StringComparison.Ordinal),
            "The first #: header block is the unpackaged example and must declare "
            + "'#:property WindowsPackageType=None'. Without it the app builds clean and then "
            + "dies at startup with REGDB_E_CLASSNOTREG.");

        Assert.False(
            blocks[1].Contains("#:property WindowsPackageType=None", StringComparison.Ordinal),
            "The second #: header block is the packaged example and must NOT declare "
            + "'#:property WindowsPackageType=None' — dropping that directive is precisely what "
            + "opts the app into package identity.");
    }

    /// <summary>
    /// Every directive the guide's table calls load-bearing must appear in both header blocks.
    /// </summary>
    /// <remarks>
    /// These are asserted together because they share a failure mode: the block is exempt from
    /// compilation, so dropping any one of them leaves the whole suite green while a reader's
    /// <c>dotnet run</c> fails. Each row of the table is a claim, and each claim is checked.
    /// <list type="bullet">
    /// <item><c>OutputType=WinExe</c> — does not fail the build at all; it links the app for the
    /// console subsystem instead. Measured on this tree: the PE subsystem byte reads 3 (console)
    /// without it and 2 (Windows GUI) with it, so a console window sits behind the UI.</item>
    /// <item><c>UseWinUI=true</c> — brings in the WinUI 3 targets.</item>
    /// <item><c>RuntimeIdentifier=$(NETCoreSdkPortableRuntimeIdentifier)</c> — supplies the
    /// architecture the Windows App SDK requires, and resolves it from the SDK so the file stays
    /// portable across x64 and ARM64. Without it the build fails with "WindowsAppSDKSelfContained
    /// requires a supported Windows architecture".</item>
    /// </list>
    /// <c>WindowsPackageType</c> is deliberately absent from this list — it is the one directive
    /// that must differ between the two blocks, so it is checked separately for placement.
    /// </remarks>
    [Theory]
    [InlineData("#:property OutputType=WinExe")]
    [InlineData("#:property UseWinUI=true")]
    [InlineData("#:property RuntimeIdentifier=$(NETCoreSdkPortableRuntimeIdentifier)")]
    public void BothExamples_DeclareTheLoadBearingDirectives(string directive)
    {
        var guide = File.ReadAllText(Path.Join(RepoRoot(), GuideTemplate));
        var blocks = HeaderBlocks(guide);

        Assert.Equal(2, blocks.Count);

        for (var i = 0; i < blocks.Count; i++)
        {
            Assert.True(
                blocks[i].Contains(directive, StringComparison.Ordinal),
                $"Header block {i + 1} of {GuideTemplate} is missing '{directive}'. The guide's "
                + "directive table documents it as required, and this block is exempt from "
                + "compilation, so nothing else would catch its removal.");
        }
    }

    /// <summary>
    /// The packaged example must reference the package that supplies its manifest.
    /// </summary>
    /// <remarks>
    /// Presence only. See the class remarks for why the version is not pinned here.
    /// </remarks>
    [Fact]
    public void PackagedExample_ReferencesTheBuildToolsPackage()
    {
        var guide = File.ReadAllText(Path.Join(RepoRoot(), GuideTemplate));
        var blocks = HeaderBlocks(guide);

        Assert.Equal(2, blocks.Count);
        Assert.True(
            blocks[1].Contains("Microsoft.Windows.SDK.BuildTools.WinApp", StringComparison.Ordinal),
            "The packaged example must reference Microsoft.Windows.SDK.BuildTools.WinApp, whose "
            + "targets synthesize the appxmanifest and intercept 'dotnet run'.");
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
