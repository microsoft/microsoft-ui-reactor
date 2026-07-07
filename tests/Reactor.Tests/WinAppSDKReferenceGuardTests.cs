using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Guards the Windows App SDK dependency policy (see <c>Directory.Build.targets</c>).
/// Repo projects must not reference the full <c>Microsoft.WindowsAppSDK</c>
/// metapackage directly: the correct package is injected centrally —
/// <c>Microsoft.WindowsAppSDK.WinUI</c> for framework-dependent projects, the full
/// metapackage only for self-contained / MSIX projects. A stray direct reference
/// bypasses that rule and re-drags the unused AI / ML / Widgets / DWrite slices back
/// into the dependency graph, so this test fails if one creeps in.
///
/// The scaffolded end-user template is exempt: it ships to external consumers who do
/// not inherit this repo's central injection, so it pins the metapackage itself.
/// </summary>
public class WinAppSDKReferenceGuardTests
{
    // Repo-root-relative, '/'-separated csproj paths allowed to reference the
    // Microsoft.WindowsAppSDK metapackage directly.
    private static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
    {
        "tools/Templates/templates/WinUIApp-CSharp/Company.ReactorApp1.csproj",
    };

    // Precise metapackage-reference marker. The trailing quote right after "SDK"
    // ensures this does not match the sub-packages (…SDK.WinUI", …SDK.Runtime", …).
    private const string MetapackageRef = "Include=\"Microsoft.WindowsAppSDK\"";

    [Fact]
    public void No_repo_project_references_the_WindowsAppSDK_metapackage_directly()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);

        var offenders = new List<string>();
        foreach (var csproj in EnumerateProjectFiles(root!))
        {
            var rel = Path.GetRelativePath(root!, csproj).Replace('\\', '/');
            if (Allowlist.Contains(rel))
            {
                continue;
            }

            if (File.ReadAllText(csproj).Contains(MetapackageRef, StringComparison.Ordinal))
            {
                offenders.Add(rel);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These projects reference the Microsoft.WindowsAppSDK metapackage directly. "
                + "Remove the reference — Directory.Build.targets injects "
                + "Microsoft.WindowsAppSDK.WinUI (framework-dependent) or the metapackage "
                + "(self-contained / MSIX) centrally. Offenders:\n  "
                + string.Join("\n  ", offenders.OrderBy(x => x, StringComparer.Ordinal)));
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
