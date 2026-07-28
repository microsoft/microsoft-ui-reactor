using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Guards the compile-time version substitution: the single-source
/// <c>&lt;ReactorPublicVersion&gt;</c> (read by <see cref="VersionSource"/>) is
/// injected for the <see cref="DocAssembler.VersionToken"/> in guide output.
/// Every assertion here fails if the substitution machinery is removed.
/// </summary>
public class VersionSubstitutionTests
{
    private static readonly Dictionary<string, SnippetExtractor.Snippet> NoSnippets = new();
    private static readonly Dictionary<string, ScreenshotInfo> NoScreenshots = new();

    [Fact]
    public void Assemble_replaces_version_token_with_passed_version()
    {
        var body = $"Install `Microsoft.UI.Reactor` `{DocAssembler.VersionToken}` today.";

        var output = DocAssembler.Assemble(
            body, NoSnippets, NoScreenshots, out _, out _, topicId: null, reactorVersion: "0.1.0-preview.11");

        Assert.Contains("`0.1.0-preview.11`", output);
        Assert.DoesNotContain(DocAssembler.VersionToken, output);
    }

    [Fact]
    public void Assemble_replaces_every_occurrence()
    {
        var body = $"{DocAssembler.VersionToken} ... {DocAssembler.VersionToken} ... {DocAssembler.VersionToken}";

        var output = DocAssembler.Assemble(
            body, NoSnippets, NoScreenshots, out _, out _, topicId: null, reactorVersion: "9.9.9");

        Assert.Equal("9.9.9 ... 9.9.9 ... 9.9.9", output);
    }

    [Fact]
    public void Assemble_is_version_sensitive_differential()
    {
        // The SAME template body rendered with two different versions must
        // produce two different outputs. If substitution were deleted (token
        // left verbatim, or a hardcoded literal emitted), both outputs would be
        // identical and this fails — the core non-vacuous guarantee.
        var body = $"Version=\"{DocAssembler.VersionToken}\"";

        var outA = DocAssembler.Assemble(body, NoSnippets, NoScreenshots, out _, out _, null, "1.2.3");
        var outB = DocAssembler.Assemble(body, NoSnippets, NoScreenshots, out _, out _, null, "4.5.6");

        Assert.NotEqual(outA, outB);
        Assert.Equal("Version=\"1.2.3\"", outA);
        Assert.Equal("Version=\"4.5.6\"", outB);
    }

    [Fact]
    public void Assemble_leaves_token_untouched_when_version_is_null()
    {
        // Opt-out path: a caller that deliberately passes null (e.g. structural
        // tier-lint) must not blank out or partially rewrite the token. The
        // parameter is required (no default), so this opt-out is always explicit.
        var body = $"pinned to `{DocAssembler.VersionToken}`";

        var output = DocAssembler.Assemble(
            body, NoSnippets, NoScreenshots, out _, out _, topicId: null, reactorVersion: null);

        Assert.Contains(DocAssembler.VersionToken, output);
    }

    [Fact]
    public void AssembleForLint_substitutes_version_token()
    {
        // The lint assembly path must substitute exactly like the emit path;
        // otherwise cross-link / tier lint would evaluate a raw {{reactorVersion}}
        // token that never ships. Fails if AssembleForLint stops threading the
        // version through to DocAssembler.Assemble.
        var template = new DocTemplate { Body = $"See `{DocAssembler.VersionToken}` on NuGet." };

        var (body, _, _) = CompileCommand.AssembleForLint(
            template, NoSnippets, NoScreenshots, topicId: null, reactorVersion: "7.8.9");

        Assert.Contains("`7.8.9`", body);
        Assert.DoesNotContain(DocAssembler.VersionToken, body);
    }

    [Fact]
    public void Assemble_substitutes_token_inside_an_expanded_snippet()
    {
        // Substitution runs AFTER snippet expansion, so a {{reactorVersion}} that
        // lives inside an inserted snippet is resolved too. If substitution ran
        // first (the old order), the token would survive in the output.
        var code = $"<PackageReference Include=\"Microsoft.UI.Reactor\" Version=\"{DocAssembler.VersionToken}\" />";
        var snippets = new Dictionary<string, SnippetExtractor.Snippet>
        {
            ["pkg/ref"] = new SnippetExtractor.Snippet("ref", "pkg/ref", code, "test.cs", 1),
        };
        var body = "```csharp snippet=\"pkg/ref\"\n```";

        var output = DocAssembler.Assemble(
            body, snippets, NoScreenshots, out _, out _, topicId: null, reactorVersion: "3.2.1");

        Assert.Contains("Version=\"3.2.1\"", output);
        Assert.DoesNotContain(DocAssembler.VersionToken, output);
    }

    [Fact]
    public void ReadPublicVersion_reads_the_real_committed_props()
    {
        // Integration: prove VersionSource resolves against the actual repo
        // Directory.Build.props and returns the real pinned value, so a broken
        // regex or a renamed element is caught.
        var repoRoot = FindRepoRoot();
        var fromReader = VersionSource.ReadPublicVersion(repoRoot);

        var raw = global::System.IO.File.ReadAllText(
            global::System.IO.Path.Join(repoRoot, "Directory.Build.props"));
        var expected = global::System.Text.RegularExpressions.Regex
            .Match(raw, @"<ReactorPublicVersion>\s*([^<]+?)\s*</ReactorPublicVersion>")
            .Groups[1].Value.Trim();

        Assert.False(string.IsNullOrWhiteSpace(expected));
        Assert.Equal(expected, fromReader);
        Assert.Matches(@"^\d+\.\d+\.\d+", fromReader);
    }

    [Fact]
    public void Parse_extracts_and_trims_the_version()
    {
        const string props = """
            <Project>
              <PropertyGroup>
                <ReactorPublicVersion>  0.1.0-preview.11  </ReactorPublicVersion>
              </PropertyGroup>
            </Project>
            """;

        Assert.Equal("0.1.0-preview.11", VersionSource.Parse(props));
    }

    [Fact]
    public void Parse_missing_element_throws_with_code()
    {
        var ex = Assert.Throws<DocPipelineException>(
            () => VersionSource.Parse("<Project><PropertyGroup /></Project>"));
        Assert.Equal("REACTOR_DOC_VERSION_002", ex.Code);
    }

    [Fact]
    public void Parse_empty_element_throws_with_code()
    {
        var ex = Assert.Throws<DocPipelineException>(
            () => VersionSource.Parse("<ReactorPublicVersion>   </ReactorPublicVersion>"));
        Assert.Equal("REACTOR_DOC_VERSION_002", ex.Code);
    }

    [Fact]
    public void Parse_ignores_commented_out_element()
    {
        // A commented-out definition must not be picked up — otherwise a stale
        // version left in an XML comment could silently drive the docs. The live
        // (uncommented) value is the only one that counts.
        const string props = """
            <Project>
              <PropertyGroup>
                <!-- <ReactorPublicVersion>9.9.9-preview.99</ReactorPublicVersion> -->
                <ReactorPublicVersion>0.1.0-preview.11</ReactorPublicVersion>
              </PropertyGroup>
            </Project>
            """;

        Assert.Equal("0.1.0-preview.11", VersionSource.Parse(props));
    }

    [Fact]
    public void Parse_uses_last_definition_to_match_msbuild_last_wins()
    {
        // MSBuild honors the last assignment of a property, so when a props file
        // defines the element twice, VersionSource must resolve to the same value
        // MSBuild would — the last one, not the first.
        const string props = """
            <Project>
              <PropertyGroup>
                <ReactorPublicVersion>0.1.0-preview.10</ReactorPublicVersion>
              </PropertyGroup>
              <PropertyGroup>
                <ReactorPublicVersion>0.1.0-preview.11</ReactorPublicVersion>
              </PropertyGroup>
            </Project>
            """;

        Assert.Equal("0.1.0-preview.11", VersionSource.Parse(props));
    }

    [Fact]
    public void ReadPublicVersion_missing_file_throws_with_code()
    {
        var emptyDir = global::System.IO.Path.Join(
            global::System.IO.Path.GetTempPath(), "reactor-versionsource-" + global::System.Guid.NewGuid().ToString("N"));
        global::System.IO.Directory.CreateDirectory(emptyDir);
        try
        {
            var ex = Assert.Throws<DocPipelineException>(() => VersionSource.ReadPublicVersion(emptyDir));
            Assert.Equal("REACTOR_DOC_VERSION_001", ex.Code);
        }
        finally
        {
            global::System.IO.Directory.Delete(emptyDir, recursive: true);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new global::System.IO.DirectoryInfo(global::System.AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (global::System.IO.File.Exists(global::System.IO.Path.Join(dir.FullName, "Reactor.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new global::System.InvalidOperationException("Could not locate repo root (Reactor.slnx) from test base dir.");
    }
}
