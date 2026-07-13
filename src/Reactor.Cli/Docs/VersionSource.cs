using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Reads the single source of truth for the current public Reactor package
/// version — the <c>&lt;ReactorPublicVersion&gt;</c> MSBuild property in the
/// repo-root <c>Directory.Build.props</c>.
/// </summary>
/// <remarks>
/// The read is <b>deterministic</b>: it parses a committed file, never a live
/// NuGet lookup. That is what lets the doc output stay byte-stable across
/// machines and lets the docs freshness gate compare against committed output
/// without false-failing whenever a new version publishes. The same property is
/// consumed by MSBuild (the templates csproj fallback), so the docs and the
/// scaffolded template's fallback framework reference share one literal.
/// </remarks>
internal static partial class VersionSource
{
    internal const string PropsFileName = "Directory.Build.props";

    // <ReactorPublicVersion>0.1.0-preview.11</ReactorPublicVersion>
    [GeneratedRegex(@"<ReactorPublicVersion>\s*([^<]+?)\s*</ReactorPublicVersion>")]
    private static partial Regex ReactorPublicVersionElement();

    /// <summary>
    /// Reads <c>ReactorPublicVersion</c> from <c>&lt;repoRoot&gt;/Directory.Build.props</c>.
    /// Throws <see cref="DocPipelineException"/> if the file or the element is
    /// missing so a misconfigured repo fails loudly rather than silently emitting
    /// an empty version into the docs.
    /// </summary>
    public static string ReadPublicVersion(string repoRoot)
    {
        var propsPath = Path.Combine(repoRoot, PropsFileName);
        if (!File.Exists(propsPath))
        {
            throw new DocPipelineException(
                "REACTOR_DOC_VERSION_001",
                $"{propsPath}: not found — cannot resolve the ReactorPublicVersion single source.");
        }

        return Parse(File.ReadAllText(propsPath), propsPath);
    }

    /// <summary>
    /// Extracts the version from raw <paramref name="propsContent"/>. Exposed for
    /// unit tests so they can author fixtures inline rather than on disk.
    /// </summary>
    internal static string Parse(string propsContent, string sourcePath = PropsFileName)
    {
        var match = ReactorPublicVersionElement().Match(propsContent);
        if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[1].Value))
        {
            throw new DocPipelineException(
                "REACTOR_DOC_VERSION_002",
                $"{sourcePath}: no non-empty <ReactorPublicVersion> element found. Define it in the " +
                "repo-root Directory.Build.props — it is the single source for the version substituted " +
                "into docs (the {{reactorVersion}} token).");
        }

        return match.Groups[1].Value.Trim();
    }
}
