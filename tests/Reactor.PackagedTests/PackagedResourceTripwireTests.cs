using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.PackagedTests;

/// <summary>
/// Fails as soon as the packaged host starts shipping indexed resources, because the identity
/// rewrite is only safe while it does not.
/// </summary>
/// <remarks>
/// <para>The derived <c>Identity/@Name</c> is written into the generated manifest after the
/// build has already produced <c>resources.pri</c>. Resource maps are keyed by package
/// identity, so a host that looked resources up through MRT — <c>ms-resource:</c>,
/// <c>ResourceLoader</c>, <c>ResourceManager</c>, or a packaged <c>ms-appx:</c> lookup routed
/// through PRI — could register successfully and still resolve against the pre-rewrite name.
/// The failure mode is a silent wrong or missing string, not a deployment error.</para>
/// <para>Today the host has no indexed resources at all, which is what makes the ordering
/// irrelevant rather than merely untested. That is a property of the host, not a guarantee of
/// the design, so it is asserted rather than assumed: this test is the tripwire that turns a
/// latent correctness hole into a build failure at the moment someone opens it. The fix at
/// that point is to apply the derived identity before PRI indexing (or reindex after the
/// rewrite) and to add a packaged resource-lookup assertion — not to relax this test.</para>
/// <para>Scoped to the packaged host's own sources. Other projects may use resources freely;
/// only this host's identity is rewritten underneath its build output.</para>
/// </remarks>
[TestClass]
public class PackagedResourceTripwireTests
{
    private static readonly string[] MrtMarkers =
    [
        "ms-resource:",
        "ResourceLoader",
        "ResourceManager",
    ];

    /// <summary>
    /// The packaged host and the selftest host whose sources it links must contain no MRT
    /// lookup and no <c>.resw</c>.
    /// </summary>
    [TestMethod]
    public void The_Packaged_Host_Ships_No_Indexed_Resources()
    {
        var hostSources = Path.Join(RepoRoot, "tests", "Reactor.AppTests.Host");
        var packagedSources = Path.Join(RepoRoot, "tests", "Reactor.PackagedTests.Host");

        Assert.IsTrue(Directory.Exists(hostSources),
            $"Precondition: expected the selftest host sources at {hostSources}. " +
            "If the layout moved, retarget this test rather than deleting it.");

        var resw = new[] { hostSources, packagedSources }
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.resw", SearchOption.AllDirectories))
            .Where(IsSourceFile)
            .ToList();

        Assert.AreEqual(0, resw.Count,
            "The packaged host now ships .resw resources, which are indexed into resources.pri " +
            "under the pre-rewrite package identity. See the remarks on this test.\n" +
            string.Join("\n", resw));

        var offenders = new[] { hostSources, packagedSources }
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(IsSourceFile)
            .Select(f => (File: f, Hits: MarkersIn(f)))
            .Where(x => x.Hits.Count > 0)
            .ToList();

        Assert.AreEqual(0, offenders.Count,
            "The packaged host now resolves resources through MRT, which is keyed by package " +
            "identity — and this tier rewrites that identity after PRI indexing. See the " +
            "remarks on this test.\n" +
            string.Join("\n", offenders.Select(o => $"{o.File}: {string.Join(", ", o.Hits)}")));
    }

    private static List<string> MarkersIn(string file)
    {
        var text = File.ReadAllText(file);
        return MrtMarkers.Where(m => text.Contains(m, StringComparison.Ordinal)).ToList();
    }

    private static bool IsSourceFile(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    /// <summary>Walks up from the test binary to the directory holding <c>Reactor.slnx</c>.</summary>
    private static string RepoRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Join(dir, "Reactor.slnx")))
                dir = Path.GetDirectoryName(dir);

            return dir ?? throw new InvalidOperationException(
                $"Could not locate Reactor.slnx above {AppContext.BaseDirectory}.");
        }
    }
}
