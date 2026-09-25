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
/// <b>The value that actually rots.</b> Two of the header's values are already safe — the Reactor
/// version is the <c>{{reactorVersion}}</c> token the doc compiler substitutes, and the
/// <c>Microsoft.WindowsAppSDK</c> pin is swept by
/// <c>WinAppSDKReferenceGuardTests</c>, which scans <c>.md</c> and <c>.dt</c> for exactly this
/// <c>#:package</c> shape. The target framework is the one left over, and it is the worst of the
/// three to get wrong: a reader who copies a stale Windows version compiles against nothing and
/// gets <c>CS0234: the type or namespace name 'Reactor' does not exist</c>, which reads like a
/// missing package rather than a wrong TFM. Measured, not assumed — that is the exact error
/// <c>net10.0-windows10.0.19041.0</c> produces against this package.
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

        var guide = File.ReadAllText(Path.Combine(root, GuideTemplate));
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
        var guide = File.ReadAllText(Path.Combine(RepoRoot(), GuideTemplate));
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

        // The packaged block earns its name only if something supplies the manifest.
        Assert.True(
            blocks[1].Contains("Microsoft.Windows.SDK.BuildTools.WinApp", StringComparison.Ordinal),
            "The packaged example must reference Microsoft.Windows.SDK.BuildTools.WinApp, whose "
            + "targets synthesize the appxmanifest and intercept 'dotnet run'.");
    }

    /// <summary>
    /// Both examples must declare <c>OutputType=WinExe</c>.
    /// </summary>
    /// <remarks>
    /// This one does not fail the build, which is why it is easy to drop: the app still runs.
    /// It links for the wrong subsystem instead. Measured on this tree — the PE subsystem byte
    /// reads 3 (console) without the directive and 2 (Windows GUI) with it — so omitting it
    /// leaves a console window sitting behind the app's UI for its whole lifetime.
    /// </remarks>
    [Fact]
    public void BothExamples_DeclareWindowsSubsystemOutputType()
    {
        var guide = File.ReadAllText(Path.Combine(RepoRoot(), GuideTemplate));
        var blocks = HeaderBlocks(guide);

        Assert.Equal(2, blocks.Count);

        foreach (var block in blocks)
        {
            Assert.True(
                block.Contains("#:property OutputType=WinExe", StringComparison.Ordinal),
                "Every single-file header must declare '#:property OutputType=WinExe'. Without it "
                + "the build succeeds but links for the console subsystem, so a console window "
                + "opens alongside the app's UI.");
        }
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
        var csproj = File.ReadAllText(Path.Combine(root, FrameworkProject));
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
               && !File.Exists(Path.Combine(dir, "Reactor.slnx"))
               && !Directory.Exists(Path.Combine(dir, ".git")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir!;
    }
}
