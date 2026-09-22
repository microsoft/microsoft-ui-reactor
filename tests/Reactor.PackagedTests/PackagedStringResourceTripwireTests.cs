using System.Xml.Linq;
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
    /// Every project that feeds the packaged host's resource graph must ship no <c>.resw</c>.
    /// </summary>
    /// <remarks>
    /// The roots are walked from the packaged host's own <c>ProjectReference</c> graph rather
    /// than listed here. A hardcoded pair of host directories was blind to the runtime projects
    /// the host references, so a <c>.resw</c> added to one of those could enter the packaged
    /// PRI while this test still reported zero — the scan would be measuring the wrong tree and
    /// reporting the same clean answer either way.
    /// </remarks>
    [TestMethod]
    public void The_Packaged_Host_Ships_No_String_Resources()
    {
        var roots = PackagedHostProjectDirectories();

        Assert.IsTrue(roots.Count >= 3,
            "Precondition: the packaged host's project graph should reach the selftest host and " +
            "the runtime projects it references. Finding almost nothing means the .csproj walk " +
            "broke, and the zero below would be meaningless.\n" + string.Join("\n", roots));

        var resw = ScanForResw([.. roots]);

        Assert.AreEqual(0, resw.Count,
            "A project in the packaged host's graph now ships .resw string resources, which are " +
            "indexed into resources.pri under the pre-rewrite package identity and are not " +
            "covered by the Packaged_ResourceResolution fixture. See the remarks on this test.\n" +
            string.Join("\n", resw));
    }

    /// <summary>
    /// Positive control for the graph walk above.
    /// </summary>
    /// <remarks>
    /// The walk can only ever report a set of directories, and a broken XML query returns a
    /// short set that still looks plausible. This pins the two properties that make the scan
    /// meaningful: it reaches past the host projects into the referenced runtime projects, and
    /// it includes the packaged host itself.
    /// <para><c>Reactor.Localization.Generator</c> is the load-bearing entry. Every other
    /// expected root is either seeded by hand or a <em>direct</em> <c>ProjectReference</c> of
    /// the packaged host, so without it this control still passes against a walk that stops
    /// after one level — which is the failure mode it exists to catch. That one is reached only
    /// through <c>src/Reactor</c>.</para>
    /// </remarks>
    [TestMethod]
    public void The_Project_Walk_Reaches_The_Referenced_Runtime_Projects()
    {
        var roots = PackagedHostProjectDirectories()
            .Select(d => Path.GetRelativePath(RepoRoot, d).Replace('\\', '/'))
            .ToList();

        foreach (var expected in new[]
                 {
                     "tests/Reactor.PackagedTests.Host",
                     "tests/Reactor.AppTests.Host",
                     "src/Reactor",
                     "src/Reactor.Advanced",
                     "src/Reactor.Localization.Generator",
                 })
        {
            CollectionAssert.Contains(roots, expected,
                $"The packaged host's project graph should include '{expected}'. If it no longer " +
                "does, confirm the reference was removed on purpose rather than that the walk " +
                "stopped working.\n" + string.Join("\n", roots));
        }
    }

    /// <summary>
    /// Directories of every project in the packaged host's transitive
    /// <c>ProjectReference</c> graph, plus the selftest host whose sources it links.
    /// </summary>
    /// <remarks>
    /// Read straight from the project XML. That is coarser than an MSBuild evaluation — it
    /// takes the whole project directory rather than the exact resource item set — so it will
    /// happily report a <c>.resw</c> that would never have been packaged, which is the safe
    /// direction for a tripwire.
    /// <para>It is <em>not</em> complete in the other direction, and saying otherwise would
    /// overstate it. A project directory is a proxy for a project's inputs, not the inputs
    /// themselves, so three kinds of packaged <c>.resw</c> are invisible here: one linked in
    /// from outside any walked directory (this host already links the selftest host's sources,
    /// which is why that root is added by hand — a second such link would need adding too); one
    /// generated into <c>obj</c> by a target, since <see cref="IsSourceFile"/> excludes build
    /// output; and one that legitimately lives beneath a directory component named <c>bin</c>
    /// or <c>obj</c>, which that same filter cannot tell from build output.</para>
    /// <para>None of the three exists in this repository today, and the tripwire is worth
    /// having for the case that does — someone adding a <c>Strings/</c> tree to a project in
    /// this graph. Treat a zero as "no <c>.resw</c> was committed to these projects", not as
    /// "the PRI contains no string resources". Grounding it in the evaluated resource items or
    /// the generated PRI is what would make the stronger claim true.</para>
    /// </remarks>
    private static List<string> PackagedHostProjectDirectories()
    {
        var start = Path.Join(
            RepoRoot, "tests", "Reactor.PackagedTests.Host", "Reactor.PackagedTests.Host.csproj");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(start);

        // Linked rather than referenced: the packaged host owns no source of its own and
        // compiles every .cs from the selftest host, so that tree is part of its inputs even
        // though no ProjectReference names it.
        var roots = new List<string> { Path.Join(RepoRoot, "tests", "Reactor.AppTests.Host") };

        while (pending.Count > 0)
        {
            var project = pending.Pop();
            if (!seen.Add(Path.GetFullPath(project)) || !File.Exists(project)) continue;

            var dir = Path.GetDirectoryName(project)!;
            roots.Add(dir);

            foreach (var reference in ProjectReferencesOf(project))
            {
                pending.Push(Path.GetFullPath(Path.Join(dir, reference)));
            }
        }

        return roots
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The raw <c>Include</c> paths of a project's <c>ProjectReference</c> items.</summary>
    private static IEnumerable<string> ProjectReferencesOf(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Replace('\\', Path.DirectorySeparatorChar));

    /// <summary>
    /// Positive control for the scan above.
    /// </summary>
    /// <remarks>
    /// The repository check can only ever report "none found", and a broken glob or an
    /// <see cref="IsSourceFile"/> that rejects everything reports exactly the same thing as a
    /// clean tree. This runs the same helper over a tree that is known to contain a
    /// <c>.resw</c>, so the tripwire has to demonstrate it can still see one — and that it
    /// discriminates, by ignoring build output rather than everything.
    /// </remarks>
    [TestMethod]
    public void The_Scan_Finds_A_Source_Resw_And_Ignores_Build_Output()
    {
        var root = Path.Join(Path.GetTempPath(), "reactor-resw-" + Guid.NewGuid().ToString("n"));
        try
        {
            var wanted = Path.Join(root, "Strings", "en-us", "Resources.resw");
            Directory.CreateDirectory(Path.GetDirectoryName(wanted)!);
            File.WriteAllText(wanted, "<root />");

            foreach (var ignored in new[]
                     {
                         Path.Join(root, "bin", "x64", "Debug", "Resources.resw"),
                         Path.Join(root, "obj", "x64", "Resources.resw"),
                     })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ignored)!);
                File.WriteAllText(ignored, "<root />");
            }

            var found = ScanForResw(root);

            CollectionAssert.AreEquivalent(
                new[] { wanted },
                found,
                "The scan must find a source .resw and must exclude bin/obj, otherwise the " +
                "zero it reports against the repository means nothing.\n" +
                string.Join("\n", found));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The scan's verdict does not depend on where the repository was cloned.
    /// </summary>
    /// <remarks>
    /// <para>The control above stages its tree under <c>%TEMP%</c>, which on an ordinary
    /// machine has no <c>bin</c> or <c>obj</c> component — so it passes whether the build-output
    /// filter reads the absolute path or the path below the root. This stages the same tree
    /// beneath a directory named <c>bin</c>, which is the case that separates them.</para>
    /// <para>It matters because a checkout really can sit there: <c>C:\bin\reactor</c>, or a
    /// build agent rooted in an <c>obj</c> workspace. An absolute-path filter discards every
    /// source <c>.resw</c> in that layout and the tripwire reports zero, which is the same
    /// answer it gives for a clean tree.</para>
    /// </remarks>
    [TestMethod]
    public void The_Scan_Is_Not_Blinded_By_A_Checkout_Under_A_Build_Output_Name()
    {
        var staging = Path.Join(Path.GetTempPath(), "reactor-resw-" + Guid.NewGuid().ToString("n"));
        try
        {
            // The repository root, as if cloned to C:\bin\checkout.
            var root = Path.Join(staging, "bin", "checkout");

            var wanted = Path.Join(root, "Strings", "en-us", "Resources.resw");
            Directory.CreateDirectory(Path.GetDirectoryName(wanted)!);
            File.WriteAllText(wanted, "<root />");

            var ignored = Path.Join(root, "obj", "x64", "Resources.resw");
            Directory.CreateDirectory(Path.GetDirectoryName(ignored)!);
            File.WriteAllText(ignored, "<root />");

            var found = ScanForResw(root);

            CollectionAssert.Contains(found, wanted,
                "A source .resw went missing because the checkout sits under a directory named " +
                "'bin'. The tripwire would report a clean tree in this layout however many " +
                "string resources were added.\n" + string.Join("\n", found));

            CollectionAssert.DoesNotContain(found, ignored,
                "Build output below the root must still be ignored, or the fix above traded a " +
                "false negative for a false positive.\n" + string.Join("\n", found));
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>
    /// The one scan both the repository check and its positive control go through, so the
    /// control cannot pass while the real check is broken.
    /// </summary>
    private static List<string> ScanForResw(params string[] roots) =>
        roots
            .Where(Directory.Exists)
            .SelectMany(d => Directory
                .EnumerateFiles(d, "*.resw", SearchOption.AllDirectories)
                .Where(f => IsSourceFile(d, f)))
            .ToList();

    /// <summary>
    /// Whether a <c>.resw</c> under <paramref name="root"/> is a source file rather than build
    /// output, judged by the path <em>below</em> the root.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately relative. Testing the absolute path instead means the verdict depends
    /// on where the repository happens to be cloned: a checkout under any directory named
    /// <c>bin</c> or <c>obj</c> — <c>C:\bin\reactor</c>, a build agent's <c>obj</c> workspace —
    /// puts that component in <em>every</em> path, so every source <c>.resw</c> is discarded
    /// and the scan reports zero. For a tripwire whose only possible finding is "none found",
    /// that is indistinguishable from a clean tree, which is the one way it can fail silently.
    /// </para>
    /// <para>Matching whole components rather than a substring keeps a directory such as
    /// <c>binaries</c> from being read as <c>bin</c>. The filename is included in the walk only
    /// because it cannot collide: a file matching <c>*.resw</c> is never named <c>bin</c>.
    /// </para>
    /// </remarks>
    private static bool IsSourceFile(string root, string path) =>
        !Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part.Equals("bin", StringComparison.OrdinalIgnoreCase)
                      || part.Equals("obj", StringComparison.OrdinalIgnoreCase));

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
