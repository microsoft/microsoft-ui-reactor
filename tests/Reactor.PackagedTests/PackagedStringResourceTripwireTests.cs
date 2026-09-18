using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.PackagedTests;

/// <summary>
/// Fails as soon as the packaged host starts shipping <c>.resw</c> string resources, because
/// the live resolution probe that covers this tier's identity rewrite does not cover them.
/// </summary>
/// <remarks>
/// <para>This tier rewrites <c>Identity/@Name</c> after the build produced
/// <c>resources.pri</c>, so the PRI's primary map records the pre-rewrite name while the
/// package registers under a per-layout derived one. Measured on this build: the map is
/// <c>ms-appx://Microsoft.UI.Reactor.PackagedTests.Host/</c> and the package is that name plus
/// a suffix. The host does ship indexed <i>file</i> content — <c>Themes/Generic.xaml</c> as a
/// <c>Page</c>, <c>Images\*.png</c> and the window icon as <c>Content</c>, ~1.3 MB of PRI — and
/// the <c>Packaged_ResourceResolution</c> selftest fixture measures, in the packaged process
/// under the derived identity, that both <c>ms-appx:</c> and a raw MRT <c>Files</c> subtree
/// lookup still resolve. That fixture, not this test, is what makes the rewrite safe.</para>
/// <para>What that fixture cannot cover is <c>ms-resource:</c> string lookup, for the simple
/// reason that the host has no string resources to look up: there is no <c>.resw</c> anywhere
/// in the repo. String resources are the case most likely to be name-keyed rather than
/// resolved out of the install location, so the moment one appears the live probe stops being
/// a complete answer.</para>
/// <para>This test therefore asserts one narrow, true thing — no <c>.resw</c> — rather than the
/// broader "no indexed resources" claim, which would be false. If it fails, the fix is to add a
/// string-resource arm to <c>Packaged_ResourceResolution</c> and confirm it resolves under the
/// derived identity; if it does not, apply the derived identity before PRI indexing.</para>
/// </remarks>
[TestClass]
public class PackagedStringResourceTripwireTests
{
    /// <summary>
    /// The packaged host and the selftest host whose sources it links must ship no
    /// <c>.resw</c>.
    /// </summary>
    [TestMethod]
    public void The_Packaged_Host_Ships_No_String_Resources()
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
            "The packaged host now ships .resw string resources, which are indexed into " +
            "resources.pri under the pre-rewrite package identity and are not covered by the " +
            "Packaged_ResourceResolution fixture. See the remarks on this test.\n" +
            string.Join("\n", resw));
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
