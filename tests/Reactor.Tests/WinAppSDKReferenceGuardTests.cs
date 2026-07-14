using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Guards the Windows App SDK dependency policy (see <c>Directory.Build.targets</c>).
/// Repo projects must not reference the full <c>Microsoft.WindowsAppSDK</c>
/// metapackage directly: the correct package is injected centrally —
/// <c>Microsoft.WindowsAppSDK.WinUI</c> for framework-dependent libraries, WinUI +
/// <c>Microsoft.WindowsAppSDK.Runtime</c> for framework-dependent apps, and the full
/// metapackage only for self-contained / MSIX projects. A stray direct reference
/// bypasses that rule and re-drags the unused AI / ML / Widgets / DWrite slices back
/// into the dependency graph.
///
/// The scaffolded end-user template is exempt: it ships to external consumers who do
/// not inherit this repo's central injection, so it pins the metapackage itself.
/// </summary>
public class WinAppSDKReferenceGuardTests
{
    private const string Metapackage = "Microsoft.WindowsAppSDK";

    // Repo-root-relative, '/'-separated csproj paths allowed to reference the
    // Microsoft.WindowsAppSDK metapackage directly.
    private static readonly HashSet<string> Allowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "tools/Templates/templates/WinUIApp-CSharp/Company.ReactorApp1.csproj",
    };

    /// <summary>
    /// Source-level guard: no repo project may declare a direct
    /// <c>Microsoft.WindowsAppSDK</c> PackageReference (parsed as XML so whitespace,
    /// quote style, casing, and <c>Update=</c> variants are all covered).
    /// </summary>
    [Fact]
    public void No_repo_project_references_the_WindowsAppSDK_metapackage_directly()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);

        var offenders = new List<string>();
        var unparseable = new List<string>();
        foreach (var csproj in EnumerateProjectFiles(root!))
        {
            var rel = Path.GetRelativePath(root!, csproj).Replace('\\', '/');
            if (Allowlist.Contains(rel))
            {
                continue;
            }

            XDocument doc;
            try
            {
                doc = XDocument.Load(csproj);
            }
            catch (global::System.Xml.XmlException ex)
            {
                // A malformed project file must fail the guard loudly rather than
                // slip through — otherwise a broken csproj could silently bypass
                // the metapackage policy this test enforces.
                unparseable.Add($"{rel} ({ex.Message})");
                continue;
            }

            if (ReferencesMetapackage(doc))
            {
                offenders.Add(rel);
            }
        }

        Assert.True(
            unparseable.Count == 0,
            "These project files could not be parsed as XML — fix the malformed csproj:\n  "
                + string.Join("\n  ", unparseable.OrderBy(x => x, StringComparer.Ordinal)));

        Assert.True(
            offenders.Count == 0,
            "These projects reference the Microsoft.WindowsAppSDK metapackage directly. "
                + "Remove the reference — Directory.Build.targets injects "
                + "Microsoft.WindowsAppSDK.WinUI (+ .Runtime for apps) or the metapackage "
                + "(self-contained / MSIX) centrally. Offenders:\n  "
                + string.Join("\n  ", offenders.OrderBy(x => x, StringComparer.Ordinal)));
    }

    /// <summary>
    /// Outcome-level guard: the shipped <c>Microsoft.UI.Reactor</c> library must
    /// resolve the lean WinUI sub-package and must NOT flow the full metapackage to
    /// consumers. Reads Reactor's restore graph (present because this test project
    /// references Reactor), so it catches a central-rule regression wherever it is
    /// introduced — including in <c>Directory.Build.*</c>, which the source scan above
    /// does not cover.
    /// </summary>
    [Fact]
    public void Reactor_library_resolves_WinUI_subpackage_not_the_metapackage()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);

        var assets = Path.Join(root!, "src", "Reactor", "obj", "project.assets.json");
        Assert.True(File.Exists(assets), $"Reactor restore graph not found at {assets}");

        using var doc = JsonDocument.Parse(File.ReadAllText(assets));
        var winAppSdk = doc.RootElement.GetProperty("libraries")
            .EnumerateObject()
            .Select(p => p.Name) // e.g. "Microsoft.WindowsAppSDK.WinUI/2.1.0"
            .Where(k => k.StartsWith("Microsoft.WindowsAppSDK", StringComparison.Ordinal))
            .ToList();

        Assert.Contains(winAppSdk, k => k.StartsWith("Microsoft.WindowsAppSDK.WinUI/", StringComparison.Ordinal));
        Assert.DoesNotContain(winAppSdk, k => k.StartsWith("Microsoft.WindowsAppSDK/", StringComparison.Ordinal));
    }

    private static bool ReferencesMetapackage(XDocument doc)
    {
        return doc.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Any(e =>
                AttributeEquals(e, "Include", Metapackage)
                || AttributeEquals(e, "Update", Metapackage));
    }

    private static bool AttributeEquals(XElement element, string name, string value)
    {
        var attr = element.Attribute(name)?.Value;
        return attr is not null && attr.Trim().Equals(value, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateProjectFiles(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            var norm = path.Replace('\\', '/');
            if (norm.Contains("/bin/", StringComparison.Ordinal)
                || norm.Contains("/obj/", StringComparison.Ordinal)
                || norm.Contains("/node_modules/", StringComparison.Ordinal)
                || norm.Contains("/.git/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return path;
        }
    }
}
