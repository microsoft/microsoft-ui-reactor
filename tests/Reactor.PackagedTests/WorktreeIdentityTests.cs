using System.Runtime.InteropServices;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reactor.Tests.Shared;

namespace Microsoft.UI.Reactor.PackagedTests;

/// <summary>
/// Headless tests for the per-checkout identity derivation that lets concurrent worktrees run
/// the packaged tier at the same time.
/// </summary>
/// <remarks>
/// <para>The property under test is an absence: two checkouts must not be able to see or evict
/// each other's registration. That cannot be observed directly without registering two real
/// packages, so it is pinned at the two places it is decided — the derivation, and the manifest
/// rewrite that carries the derived values into the file Windows registers — and the
/// registration path is then scoped entirely to derived values.</para>
/// <para>Nothing here registers a package or touches per-user package state, so it runs at unit
/// speed and is safe beside a concurrently running packaged tier.</para>
/// </remarks>
[TestClass]
public partial class WorktreeIdentityTests
{
    private const string Base = AppxLooseLayoutDeployment.PackageName;
    private const string BaseAlias = AppxLooseLayoutDeployment.AliasExeName;

    private static string PathA => Path.Join(Path.GetTempPath(), "reactor-wt-a", "bin", "x64");
    private static string PathB => Path.Join(Path.GetTempPath(), "reactor-wt-b", "bin", "x64");

    // ── The isolation property ─────────────────────────────────────────

    /// <summary>
    /// The whole point. Two checkouts must derive different package names, because every
    /// lookup and every removal in the deployment is scoped to the derived name — so if these
    /// collided, the sweep would be back to evicting a concurrently running checkout.
    /// </summary>
    [TestMethod]
    public void Different_Layouts_Derive_Different_Package_Names()
    {
        Assert.AreNotEqual(
            WorktreeIdentity.DerivePackageName(Base, PathA),
            WorktreeIdentity.DerivePackageName(Base, PathB));
    }

    /// <summary>
    /// Unique package names alone would not be enough. Execution aliases are created at a
    /// single fixed path under <c>%LOCALAPPDATA%\Microsoft\WindowsApps</c>, so two checkouts
    /// declaring one alias would still fight over one stub, with Windows deciding which binary
    /// the tier actually launches.
    /// </summary>
    [TestMethod]
    public void Different_Layouts_Derive_Different_Alias_Names()
    {
        Assert.AreNotEqual(
            WorktreeIdentity.DeriveAliasExeName(BaseAlias, PathA),
            WorktreeIdentity.DeriveAliasExeName(BaseAlias, PathB));
    }

    /// <summary>
    /// The derivation has to be a function of the directory and nothing else — no clock, no
    /// counter, no randomness. A rerun that derived a fresh name would leak the previous
    /// registration and leave its alias owned by a layout nothing could remove.
    /// </summary>
    [TestMethod]
    public void Derivation_Is_Stable_Across_Calls()
    {
        Assert.AreEqual(
            WorktreeIdentity.DerivePackageName(Base, PathA),
            WorktreeIdentity.DerivePackageName(Base, PathA));
        Assert.AreEqual(
            WorktreeIdentity.DeriveAliasExeName(BaseAlias, PathA),
            WorktreeIdentity.DeriveAliasExeName(BaseAlias, PathA));
    }

    /// <summary>
    /// Pins the version-1 algorithm to a fixed output.
    /// </summary>
    /// <remarks>
    /// <para>Every other test here asserts a <em>relationship</em> — stable, distinct,
    /// case-insensitive — and all of them stay green if the seed, hash, alphabet, suffix length,
    /// or canonicalisation changes, because both sides of each comparison move together. What
    /// that would silently change is the package family and the package-scoped app-data location
    /// of every checkout on every machine, orphaning existing registrations.</para>
    /// <para>This is the one assertion that fails on such a change. If it fails, the fix is not
    /// to update the constants: it is to decide whether the change was intended, and if it was,
    /// to act on which kind of change it is. A <em>seed</em> change — bumping
    /// <c>WorktreeIdentity.AlgorithmVersion</c> — is migratable: prepend the new version to
    /// <c>SupportedAlgorithmVersions</c> and update these vectors in the same commit, and the
    /// sweep goes on reclaiming what the old version registered. A change to the <em>hash,
    /// alphabet, or suffix length</em> is not, because those primitives are applied to every
    /// listed version, so replaying version 1 after such a change yields the new output rather
    /// than the names version 1 actually registered. See
    /// <see cref="A_Listed_Version_Is_Replayed_With_Todays_Primitives"/>, which measures that
    /// limitation, and the remarks on <c>WorktreeIdentity</c> for what to do instead.</para>
    /// <para>A version bump alone reddens this test, because the version is part of the hash
    /// seed — so there is no separate assertion on the version itself, which would be a
    /// compile-time constant compared to its own literal.</para>
    /// <para>The path is deliberately fictional and absolute. It does not exist, so component
    /// link resolution leaves it verbatim, which is what keeps the vector machine-independent.</para>
    /// </remarks>
    [TestMethod]
    public void Derivation_Matches_The_Pinned_Version1_Vectors()
    {
        const string canonicalPath = @"C:\reactor-golden\layout\bin\x64";
        var note =
            " These vectors pin version 1; WorktreeIdentity.AlgorithmVersion is currently '" +
            WorktreeIdentity.AlgorithmVersion +
            "'. See the remarks on this test before touching the vector.";

        Assert.AreEqual(
            "wtk7tlege",
            WorktreeIdentity.DeriveSuffix(canonicalPath),
            "Version-1 suffix changed." + note);

        Assert.AreEqual(
            "Microsoft.UI.Reactor.PackagedTests.Host.wtk7tlege",
            WorktreeIdentity.DerivePackageName(Base, canonicalPath),
            "Version-1 package name changed." + note);

        Assert.AreEqual(
            "reactor-packaged-test-host-wtk7tlege.exe",
            WorktreeIdentity.DeriveAliasExeName(BaseAlias, canonicalPath),
            "Version-1 alias name changed." + note);
    }

    /// <summary>
    /// Pins a second algorithm version's output, making it measurable that
    /// <c>SupportedAlgorithmVersions</c> replays old versions through <em>today's</em> hash,
    /// alphabet and suffix length rather than through anything that version captured.
    /// </summary>
    /// <remarks>
    /// <para>This is the assertion behind the narrowed migratability contract on
    /// <c>WorktreeIdentity</c>. The version reaches the output only as part of the hash seed —
    /// there is no per-version table of primitives — so listing a version does not preserve how
    /// that version encoded its hash. Prose saying otherwise is untestable; a second pinned
    /// vector is not.</para>
    /// <para>What makes it decisive is that both vectors fail <em>together</em>. Change the
    /// alphabet, the suffix length, or the hash, and this reddens alongside
    /// <see cref="Derivation_Matches_The_Pinned_Version1_Vectors"/>, which is precisely the
    /// demonstration that a supported older version was not insulated from the change and that
    /// its registrations are now unrecognizable. Change only the seed shape and just this pair
    /// of vectors moves.</para>
    /// <para><c>"0"</c> is deliberately not a version this code ever shipped. The parameter
    /// accepts any non-empty string, so no real version has to be retired to keep a second
    /// vector pinned.</para>
    /// </remarks>
    [TestMethod]
    public void A_Listed_Version_Is_Replayed_With_Todays_Primitives()
    {
        const string canonicalPath = @"C:\reactor-golden\layout\bin\x64";
        const string otherVersion = "0";

        Assert.AreEqual(
            "wqvg2yycn",
            WorktreeIdentity.DeriveSuffix(canonicalPath, otherVersion),
            "A non-current algorithm version now derives a different suffix. If " +
            "Derivation_Matches_The_Pinned_Version1_Vectors also failed, a shared primitive " +
            "changed and every version in SupportedAlgorithmVersions now replays to the wrong " +
            "name — registrations made by those versions can no longer be recognized or " +
            "reclaimed. See the remarks on WorktreeIdentity.");

        Assert.AreNotEqual(
            WorktreeIdentity.DeriveSuffix(canonicalPath, otherVersion),
            WorktreeIdentity.DeriveSuffix(canonicalPath, WorktreeIdentity.AlgorithmVersion),
            "Two algorithm versions derived the same suffix for one directory, so the version " +
            "is not reaching the hash seed and a bump would silently reuse the previous " +
            "version's package family.");
    }

    /// <summary>
    /// The deployment derives from the layout directory it registers; the in-host guard derives
    /// from <c>AppContext.BaseDirectory</c>, which carries a trailing separator and whatever
    /// casing the OS reports. Those are the same directory and must derive the same identity,
    /// or the guard fails against a package that is in fact correct.
    /// </summary>
    [TestMethod]
    public void Casing_And_Trailing_Separator_Do_Not_Change_The_Identity()
    {
        var expected = WorktreeIdentity.DerivePackageName(Base, PathA);

        Assert.AreEqual(expected, WorktreeIdentity.DerivePackageName(Base, PathA.ToUpperInvariant()));
        Assert.AreEqual(expected, WorktreeIdentity.DerivePackageName(Base, PathA + Path.DirectorySeparatorChar));
        Assert.AreEqual(expected, WorktreeIdentity.DerivePackageName(Base, Path.Join(PathA, "..", "x64")));
    }

    /// <summary>
    /// The extended-length spelling of a directory must derive the identity its ordinary
    /// spelling does.
    /// </summary>
    /// <remarks>
    /// <para>The deterministic half of the alternate-spelling coverage, and the one alternate
    /// spelling available on every machine — unlike 8.3 names, which a volume may have
    /// disabled, or <c>subst</c> drives, which need a device mapping this suite will not make.
    /// It exercises the same two steps a <c>subst</c> spelling does, because both are resolved
    /// by the final-path query and its prefix conversion.</para>
    /// <para><c>Path.GetFullPath</c> preserves <c>\\?\</c> verbatim, so a canonicaliser that
    /// stops at string handling reads this as a different directory and derives a second
    /// identity for one physical layout.</para>
    /// <para>It also pins the reverse conversion. Windows answers the final-path query in
    /// extended-length form, so a canonicaliser that forwarded the answer unchanged would make
    /// a directory hash one way while it exists and another way once the handle-less fallback
    /// runs — two identities for one path again, this time depending only on timing.</para>
    /// </remarks>
    [TestMethod]
    public void The_Extended_Length_Spelling_Derives_The_Same_Identity()
    {
        var dir = Path.Join(Path.GetTempPath(), "reactor-wt-ext-" + Guid.NewGuid().ToString("N"), "bin", "x64");
        Directory.CreateDirectory(dir);

        try
        {
            var ordinary = WorktreeIdentity.Canonicalize(dir);
            var extended = WorktreeIdentity.Canonicalize(@"\\?\" + dir);

            Assert.IsFalse(
                ordinary.StartsWith(@"\\?\", StringComparison.Ordinal),
                "A canonical path must be an ordinary path. Windows reports a final path in " +
                "extended-length form, so leaving the prefix on makes an existing directory " +
                "hash differently from the same directory before it is created.");

            Assert.AreEqual(
                ordinary,
                extended,
                "Two spellings of one physical directory canonicalised differently, so the two " +
                "runs derive different identities, take different lock files, and then rewrite " +
                "and register the same generated manifest concurrently.");

            Assert.AreEqual(
                WorktreeIdentity.DerivePackageName(Base, dir),
                WorktreeIdentity.DerivePackageName(Base, @"\\?\" + dir),
                "The derived package name is what scopes every lookup and removal, so it is " +
                "the value that actually has to converge.");
        }
        finally
        {
            Directory.Delete(Path.GetFullPath(Path.Join(dir, "..", "..")), recursive: true);
        }
    }

    /// <summary>
    /// An 8.3 short-name spelling must derive the identity its long spelling does.
    /// </summary>
    /// <remarks>
    /// <para>The spelling the canonicaliser's own documentation used to claim it declined to
    /// reconcile. Measured, that claim was wrong: <c>Path.GetFullPath</c> expands a short
    /// component of an existing path, so short names already converged. This pins that they
    /// keep converging, because the property matters regardless of which step provides it —
    /// <c>REACTOR_PACKAGED_HOST_DIR</c> accepts any rooted path that exists and preserves the
    /// spelling it was handed, so two agents pointed at one layout through different spellings
    /// would otherwise each believe they owned it.</para>
    /// <para>Staged rather than assumed: 8.3 generation is a per-volume setting and is often
    /// off, in which case there is no second spelling to reconcile and the test has nothing to
    /// measure. Asserting anyway would pass for the wrong reason — the two spellings would be
    /// byte-identical — so the run is declared inconclusive instead of silently vacuous.</para>
    /// </remarks>
    [TestMethod]
    public void A_Short_Name_Spelling_Derives_The_Same_Identity()
    {
        var root = Path.Join(Path.GetTempPath(), "reactor-wt-8dot3-" + Guid.NewGuid().ToString("N"));
        var layout = Path.Join(root, "LayoutNameLongerThanEightDotThree", "bin", "x64");
        Directory.CreateDirectory(layout);

        try
        {
            var shortSpelling = TryGetShortPath(layout);

            if (shortSpelling is null ||
                string.Equals(shortSpelling, layout, StringComparison.OrdinalIgnoreCase))
            {
                Assert.Inconclusive(
                    "8.3 short-name generation is disabled for this volume, so this path has " +
                    "only one spelling and there is nothing for canonicalisation to reconcile.");
            }

            Assert.AreEqual(
                WorktreeIdentity.DerivePackageName(Base, layout),
                WorktreeIdentity.DerivePackageName(Base, shortSpelling!),
                "A short-name spelling of the layout derived a second identity for one physical " +
                "directory, which is what lets two runs register the same layout concurrently.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The short-name spelling of <paramref name="path"/>, or null if the platform declines.
    /// </summary>
    private static string? TryGetShortPath(string path)
    {
        var buffer = new char[1024];
        var length = ShortPathInto(path, buffer);
        return length == 0 || length >= buffer.Length ? null : new string(buffer, 0, (int)length);
    }

    /// <summary>Pins <paramref name="buffer"/> and asks for the short-name spelling.</summary>
    private static unsafe uint ShortPathInto(string path, char[] buffer)
    {
        fixed (char* p = buffer)
            return GetShortPathNameW(path, p, (uint)buffer.Length);
    }

    // Source-generated interop rather than [DllImport]; no managed API reports a short name.
    // Spelled with the explicit -W suffix because LibraryImport uses ExactSpelling.
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint GetShortPathNameW(
        string lpszLongPath,
        char* lpszShortPath,
        uint cchBuffer);

    /// <summary>
    /// A junction anywhere in the path — not just on the final component — must normalise away.
    /// </summary>
    /// <remarks>
    /// This is the realistic shape of the problem: nobody junctions the <c>AppX</c> folder, but a
    /// linked <c>C:\src</c> or a redirected profile is ordinary. The deployment canonicalises the
    /// layout directory it was handed while the in-host guard canonicalises
    /// <c>AppContext.BaseDirectory</c>; if those two arrive by different spellings of the same
    /// directory and only the final component is resolved, they derive different names and the
    /// guard fails against a package that is correct.
    /// </remarks>
    [TestMethod]
    public void A_Junction_In_A_Parent_Component_Derives_The_Same_Identity()
    {
        var root = Path.Join(Path.GetTempPath(), "reactor-wt-link-" + Guid.NewGuid().ToString("N"));
        var physical = Path.Join(root, "physical");
        var leaf = Path.Join(physical, "bin", "x64");
        var junction = Path.Join(root, "linked");

        Directory.CreateDirectory(leaf);
        try
        {
            if (!TryCreateJunction(junction, physical))
                Assert.Inconclusive("Could not create a directory junction in TEMP.");

            // Positive control: the junction really does reach the same directory. Without this a
            // broken link would make both spellings canonicalise to themselves and still compare
            // equal for the wrong reason.
            var probe = Guid.NewGuid().ToString("N");
            File.WriteAllText(Path.Join(leaf, probe), "");
            Assert.IsTrue(File.Exists(Path.Join(junction, "bin", "x64", probe)),
                "Precondition: the junction must resolve to the physical directory.");

            Assert.AreEqual(
                WorktreeIdentity.DerivePackageName(Base, leaf),
                WorktreeIdentity.DerivePackageName(Base, Path.Join(junction, "bin", "x64")),
                "A linked and a physical spelling of one directory must derive one identity.");

            // Ownership decisions have to agree with derivation. The deployment's "does this
            // registration own my layout?" test and the in-host install-location guard both
            // compare a path Windows recorded at registration time against a path this run
            // computed, and those two can easily be the linked and physical spellings of one
            // directory. A raw string comparison answers "no" for a package that is in fact
            // this run's own.
            Assert.IsTrue(
                WorktreeIdentity.IsSameDirectory(leaf, Path.Join(junction, "bin", "x64")),
                "Ownership comparison must resolve the junction, like the derivation does.");

            Assert.IsFalse(
                WorktreeIdentity.IsSameDirectory(leaf, Path.Join(physical, "bin")),
                "A parent directory is not the same directory.");

            // The same comparison must stay insensitive to how the caller *spelled* an existing
            // directory. The exactness added for case-sensitive parents is about what the
            // filesystem reports, not about the caller's input, and the resolver returns the
            // on-disk case whatever case it is asked with -- so this must still be true.
            Assert.IsTrue(
                WorktreeIdentity.IsSameDirectory(leaf, leaf.ToUpperInvariant()),
                "Two case spellings of one existing directory must still compare equal: the " +
                "final-path query reports the same on-disk case for both.");

            Assert.IsTrue(
                WorktreeIdentity.IsSameDirectory(leaf.ToLowerInvariant(), leaf.ToUpperInvariant()),
                "Neither side's spelling should matter once the filesystem has answered.");
        }
        finally
        {
            // Delete the junction itself (non-recursive) before the tree, so removing it cannot
            // reach through into the physical directory. Already-gone is the only tolerated
            // failure, and it is logged rather than swallowed so a cleanup that silently does
            // nothing is still visible in the run output.
            try { Directory.Delete(junction, recursive: false); }
            catch (DirectoryNotFoundException ex)
            {
                Console.WriteLine($"Junction already removed before cleanup: {ex.Message}");
            }
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Two directories that differ only in case are two directories, and the ownership
    /// comparison has to say so.
    /// </summary>
    /// <remarks>
    /// <para>Derivation lowercases, so under a case-sensitive parent <c>Repo</c> and
    /// <c>repo</c> collapse to one package name. That much is a tolerated collision. What must
    /// not follow from it is <see cref="WorktreeIdentity.IsSameDirectory"/> agreeing, because
    /// that is the ownership test: a registration recorded against <c>Repo</c> would be judged
    /// to be <c>repo</c>'s own, classified <c>RemoveContending</c>, and unregistered out from
    /// under a live run. Answering "different" turns that eviction into the conflict refusal
    /// the deployment already knows how to report.</para>
    /// <para>The shared derived name is asserted too, so the test states the whole situation
    /// rather than half of it: the collision is real and is deliberately survived, not avoided.
    /// Skipped where the filesystem cannot be made case-sensitive, since the premise is then
    /// unreachable and asserting anything about it would be theatre.</para>
    /// </remarks>
    [TestMethod]
    public void Two_Directories_Differing_Only_In_Case_Are_Not_One_Directory()
    {
        var root = Path.Join(Path.GetTempPath(), "reactor-case-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        try
        {
            if (!TryEnableCaseSensitivity(root))
            {
                Assert.Inconclusive(
                    "This filesystem cannot be made case-sensitive, so two directories " +
                    "differing only in case cannot exist and the premise is unreachable.");
            }

            var upper = Path.Join(root, "Repo");
            var lower = Path.Join(root, "repo");
            Directory.CreateDirectory(upper);
            Directory.CreateDirectory(lower);

            // Positive control. Without it, a filesystem that silently merged the two would
            // leave every assertion below passing for the wrong reason.
            Assert.AreEqual(2, Directory.GetDirectories(root).Length,
                "Precondition: the two spellings must really be two directories.");

            Assert.AreEqual(
                WorktreeIdentity.DerivePackageName(Base, upper),
                WorktreeIdentity.DerivePackageName(Base, lower),
                "Precondition: derivation lowercases, so these are expected to collide. If this " +
                "ever stops being true the collision has been fixed at the source and the " +
                "containment below is no longer what protects the other checkout.");

            Assert.IsFalse(
                WorktreeIdentity.IsSameDirectory(upper, lower),
                "Two distinct directories compared equal, so a registration belonging to one " +
                "would be treated as the other's own and removed while it was still running.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A path the filesystem answered for is never the same directory as one it could not.
    /// </summary>
    /// <remarks>
    /// <para>The case-exact comparison only applies when both sides resolve, so the mixed state
    /// is where the old over-match survived — and it is reachable exactly where it does the
    /// most damage. During registration this run's layout exists and resolves, while another
    /// package's recorded install path may have been deleted or be momentarily unreadable. A
    /// foreign directory differing only in case would then be compared case-insensitively
    /// against the filesystem's own spelling of ours, judged to be ours, classified
    /// <c>RemoveContending</c>, and unregistered while its owner was still running.</para>
    /// <para>Answering "different" makes that a <c>FailConflicting</c> refusal instead.</para>
    /// <para>The third assertion closes the same hole one step further out. Two paths that
    /// <em>both</em> fail to resolve can only be compared case-insensitively, so on a
    /// case-sensitive parent two genuinely different directories compare equal — and that
    /// answer reaches the same destructive branch. An earlier version of this test asserted the
    /// opposite, on the reasoning that with neither side resolvable both spellings are the
    /// caller's own; that reasoning does not survive contact with the call site, where one side
    /// is a foreign package's recorded install path and only the other is ours. A transient
    /// failure to resolve our own layout — an ACL denial or a momentary sharing violation — is
    /// then enough to turn a derived-name collision into an eviction.</para>
    /// </remarks>
    [TestMethod]
    public void Directory_Sameness_Requires_The_Filesystem_To_Confirm_Both_Spellings()
    {
        var root = Path.Join(Path.GetTempPath(), "reactor-case-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        try
        {
            if (!TryEnableCaseSensitivity(root))
            {
                Assert.Inconclusive(
                    "This filesystem cannot be made case-sensitive, so a path that differs " +
                    "from an existing directory only in case cannot be absent.");
            }

            var present = Path.Join(root, "Repo");
            var absent = Path.Join(root, "repo");
            Directory.CreateDirectory(present);

            // Positive control. If the volume merged the spellings, `absent` would exist and
            // both sides would resolve, so the mixed state under test would never be entered.
            Assert.IsFalse(Directory.Exists(absent),
                "Precondition: the lower-cased spelling must really be a different, absent " +
                "directory.");

            Assert.IsFalse(
                WorktreeIdentity.IsSameDirectory(absent, present),
                "A recorded path the filesystem could not answer for was matched against a " +
                "live directory that differs only in case, so another checkout's registration " +
                "would be treated as this run's own and removed.");

            Assert.IsFalse(
                WorktreeIdentity.IsSameDirectory(present, absent),
                "The comparison must not depend on argument order.");

            var goneUpper = Path.Join(root, "Vanished");
            var goneLower = Path.Join(root, "vanished");

            // Positive control for the pair below: on this case-sensitive parent these really
            // are two different directories, so "equal" would be a wrong answer rather than a
            // harmless one.
            Assert.IsFalse(Directory.Exists(goneUpper), "Precondition: neither spelling exists.");
            Assert.IsFalse(Directory.Exists(goneLower), "Precondition: neither spelling exists.");

            Assert.IsFalse(
                WorktreeIdentity.IsSameDirectory(goneUpper, goneLower),
                "Two paths the filesystem could not answer for were judged the same directory. " +
                "They can only be compared case-insensitively, and on a case-sensitive parent " +
                "these are two different directories, so a transient failure to resolve would " +
                "promote a derived-name collision into removing another checkout's package.");

            // The same spelling twice is still not provable while it does not resolve. Pinned so
            // the rule cannot be weakened to "unequal only when the spellings differ", which
            // would restore the over-match above for every caller that passes a foreign path.
            Assert.IsFalse(
                WorktreeIdentity.IsSameDirectory(goneUpper, goneUpper),
                "Sameness must be grounded in the filesystem, not in string equality of two " +
                "spellings neither of which was confirmed.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Marks a directory case-sensitive, reporting whether the filesystem accepted it.
    /// </summary>
    private static bool TryEnableCaseSensitivity(string directory)
    {
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "fsutil.exe",
                Arguments = $"file setCaseSensitiveInfo \"{directory}\" enable",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (proc is null) return false;

            proc.WaitForExit(30_000);
            return proc.HasExited && proc.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a directory junction, which needs no privilege — unlike a directory symlink, which
    /// requires Developer Mode or elevation and would make this test environment-dependent.
    /// </summary>
    private static bool TryCreateJunction(string link, string target)
    {
        using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });

        if (proc is null) return false;
        proc.WaitForExit(10_000);
        return proc.HasExited && proc.ExitCode == 0 && Directory.Exists(link);
    }

    // ── MSIX validity ──────────────────────────────────────────────────

    /// <summary>
    /// A derived name that violates the MSIX schema does not fail the derivation, it fails
    /// registration — at which point the tier reports a deployment error rather than the
    /// naming bug that caused it.
    /// </summary>
    [TestMethod]
    public void Derived_Name_Is_A_Valid_Msix_Identity_Name()
    {
        var name = WorktreeIdentity.DerivePackageName(Base, PathA);

        Assert.IsTrue(
            name.Length is >= WorktreeIdentity.MinPackageNameLength
                       and <= WorktreeIdentity.MaxPackageNameLength,
            $"'{name}' is {name.Length} characters, outside the MSIX 3-50 limit.");

        foreach (var c in name)
        {
            Assert.IsTrue(
                char.IsAsciiLetterOrDigit(c) || c is '.' or '-',
                $"'{name}' contains '{c}', which MSIX Identity/@Name does not allow.");
        }

        Assert.IsFalse(name.EndsWith('.'), $"'{name}' ends with a dot.");
        Assert.IsFalse(name.Contains("..", StringComparison.Ordinal), $"'{name}' has an empty segment.");
    }

    /// <summary>
    /// The base name is kept so a human reading <c>Get-AppxPackage</c> output can still tell
    /// what the package is. This is also what makes the repo-wide cleanup in CI — and the
    /// abandoned-registration rule — able to recognise our packages at all.
    /// </summary>
    [TestMethod]
    public void Derived_Name_Keeps_The_Base_Name_Recognisable()
    {
        var name = WorktreeIdentity.DerivePackageName(Base, PathA);

        Assert.AreNotEqual(Base, name, "Derivation returned the base name unchanged.");
        Assert.IsTrue(name.StartsWith(Base + ".", StringComparison.Ordinal),
            $"'{name}' does not extend '{Base}'.");
        Assert.IsTrue(WorktreeIdentity.IsDerivedFrom(name, Base));
    }

    /// <summary>
    /// The real base name fits without truncation today, but it is 39 of the 50 characters
    /// available. A longer one must still produce a legal name rather than an over-length one
    /// that only fails at registration time.
    /// </summary>
    [TestMethod]
    public void An_Over_Long_Base_Name_Is_Truncated_To_Fit()
    {
        var longBase = new string('a', 80);
        var name = WorktreeIdentity.DerivePackageName(longBase, PathA);

        Assert.AreEqual(WorktreeIdentity.MaxPackageNameLength, name.Length);
        Assert.IsTrue(WorktreeIdentity.IsDerivedFrom(name, longBase));
    }

    /// <summary>Truncation must not leave a trailing dot, which is not a legal segment.</summary>
    [TestMethod]
    public void Truncation_Does_Not_Leave_A_Trailing_Dot()
    {
        // Contrived so that the truncation boundary lands exactly on a '.'.
        var budget = WorktreeIdentity.MaxPackageNameLength - (WorktreeIdentity.SuffixHashLength + 2);
        var awkward = new string('a', budget - 1) + "." + new string('b', 20);

        var name = WorktreeIdentity.DerivePackageName(awkward, PathA);

        Assert.IsFalse(name.Contains("..", StringComparison.Ordinal), $"'{name}' has an empty segment.");
    }

    // ── Alias shape ────────────────────────────────────────────────────

    /// <summary>
    /// The alias is a file name, so the suffix goes on the stem. Appending it after the
    /// extension would produce a stub Windows does not treat as an executable.
    /// </summary>
    [TestMethod]
    public void Derived_Alias_Keeps_A_Single_Exe_Extension()    {
        var alias = WorktreeIdentity.DeriveAliasExeName(BaseAlias, PathA);

        Assert.AreEqual(".exe", Path.GetExtension(alias));
        Assert.AreEqual(1, alias.Split('.').Length - 1, $"'{alias}' has more than one extension.");
        Assert.AreNotEqual(BaseAlias, alias, "Derivation returned the base alias unchanged.");
        Assert.IsTrue(alias.StartsWith("reactor-packaged-test-host-", StringComparison.Ordinal));
    }

    // ── IsDerivedFrom: used to decide what may be removed ──────────────

    /// <summary>
    /// This predicate gates the abandoned-registration rule, so a false positive would remove
    /// a package that is not ours at all.
    /// </summary>
    [TestMethod]
    public void IsDerivedFrom_Rejects_Unrelated_And_Near_Miss_Names()
    {
        Assert.IsTrue(WorktreeIdentity.IsDerivedFrom(Base, Base), "The base name itself is ours.");

        Assert.IsFalse(WorktreeIdentity.IsDerivedFrom("Contoso.Something", Base));
        Assert.IsFalse(WorktreeIdentity.IsDerivedFrom(Base + ".Extra", Base),
            "A real sibling package is not a derived identity.");
        // Each of the three below isolates one rule, using characters that are otherwise
        // legal, so a passing assertion means that rule fired and not an unrelated one.
        Assert.IsFalse(WorktreeIdentity.IsDerivedFrom(Base + ".xabcdefgh", Base),
            "A suffix with the wrong marker is not a derived identity.");
        Assert.IsFalse(WorktreeIdentity.IsDerivedFrom(Base + ".wabcdefg", Base),
            "A short hash is not a derived identity.");
        Assert.IsFalse(WorktreeIdentity.IsDerivedFrom(Base + ".wabcdefg!", Base),
            "A hash outside the encoding alphabet is not a derived identity.");
        Assert.IsFalse(WorktreeIdentity.IsDerivedFrom(Base + ".w", Base),
            "A suffix without a hash is not a derived identity.");
        Assert.IsFalse(WorktreeIdentity.IsDerivedFrom(string.Empty, Base));
    }

    /// <summary>
    /// The head must match what <c>DerivePackageName</c> actually emits, not merely be some
    /// prefix of the base name.
    /// </summary>
    /// <remarks>
    /// A prefix test looks harmless but widens the match to names this type could never produce,
    /// and <c>IsDerivedFrom</c> gates the sweep that removes derived-shaped packages whose
    /// directory is gone. Under a prefix test, <c>M.w…</c> reads as ours and an unrelated
    /// same-publisher package gets unregistered — the exact class of collateral damage the whole
    /// per-checkout identity change exists to stop.
    /// </remarks>
    [TestMethod]
    public void IsDerivedFrom_Rejects_A_Shortened_Head()
    {
        Assert.IsTrue(Base.Length > 1, "Precondition: the base name must have a proper prefix.");

        Assert.IsFalse(WorktreeIdentity.IsDerivedFrom(Base[..1] + ".wabcdefgh", Base),
            "A one-character head is a prefix of the base but is not a name we emit.");
        Assert.IsFalse(WorktreeIdentity.IsDerivedFrom(Base[..^1] + ".wabcdefgh", Base),
            "A head one character short of the base is still not a name we emit.");

        // Positive control: the same assertion shape passes for the head we do emit, so the two
        // rejections above are the rule firing rather than the suffix being malformed.
        var real = WorktreeIdentity.DerivePackageName(Base, PathA);
        Assert.IsTrue(WorktreeIdentity.IsDerivedFrom(real, Base));
    }

    // ── The deployment actually uses it ────────────────────────────────

    /// <summary>
    /// Pins the derivation to the type that registers packages, not just to the helper. A
    /// deployment that computed its identity some other way would leave every test above true
    /// and the tier still unable to run concurrently.
    /// </summary>
    [TestMethod]
    public void Deployment_Scopes_Its_Identity_To_Its_Layout_Directory()
    {
        var a = new AppxLooseLayoutDeployment(PathA);
        var b = new AppxLooseLayoutDeployment(PathB);

        Assert.AreEqual(WorktreeIdentity.DerivePackageName(Base, PathA), a.EffectivePackageName);
        Assert.AreEqual(WorktreeIdentity.DeriveAliasExeName(BaseAlias, PathA), a.EffectiveAliasExeName);

        Assert.AreNotEqual(a.EffectivePackageName, b.EffectivePackageName);
        Assert.AreNotEqual(a.EffectiveAliasExeName, b.EffectiveAliasExeName);
    }

    /// <summary>
    /// The derived names reach the manifest Windows actually registers. Every other test here
    /// compares two strings this code produced; none of them would notice if the rewrite that
    /// carries those strings into <c>AppxManifest.xml</c> stopped running, matched the wrong
    /// element, or covered the package name but not the alias — and any of those puts two
    /// layouts back on one identity and one alias stub.
    /// </summary>
    /// <remarks>
    /// <para>Run against a copy of the host's real <c>Package.appxmanifest</c>, so the element
    /// shapes, namespaces and nesting are the ones that ship rather than a fixture's idea of
    /// them. A hand-written manifest would keep passing after a schema change that broke the
    /// real one.</para>
    /// <para>Stops short of registering two packages at once. That is the only thing left
    /// unproven, and it is not testable here without a machine-wide side effect: registration
    /// needs Developer Mode, mutates per-user state outside the repo, and would be racing any
    /// concurrently running packaged tier on the same machine — the very concurrency this
    /// change exists to make safe. The packaged tier covers the other half for real, from
    /// inside a registered process: <c>Packaged_IdentityGuard</c> asserts
    /// <c>Package.Current.Id.Name</c> equals the derived name and that the registration's
    /// install location is this build output.</para>
    /// </remarks>
    [TestMethod]
    public void Derived_Identity_Is_Written_Into_The_Registered_Manifest()
    {
        var source = Path.Join(
            RepoRoot(), "tests", "Reactor.PackagedTests.Host", "Package.appxmanifest");
        Assert.IsTrue(File.Exists(source), $"Manifest not found: {source}");

        var layout = Path.Join(Path.GetTempPath(), "reactor-manifest-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(layout);
        try
        {
            var manifest = Path.Join(layout, "AppxManifest.xml");
            File.Copy(source, manifest);

            var before = XDocument.Load(manifest);
            var baseName = LocalValue(before, "Identity", "Name");
            var baseAlias = LocalValue(before, "ExecutionAlias", "Alias");

            // Control: the copy starts on the undecorated identity, so the values read back
            // afterwards are the rewrite's work and not something the manifest already said.
            Assert.AreEqual(AppxLooseLayoutDeployment.PackageName, baseName);
            Assert.AreEqual(AppxLooseLayoutDeployment.AliasExeName, baseAlias);

            var deployment = new AppxLooseLayoutDeployment(layout);
            deployment.ApplyDerivedIdentity(manifest);

            var after = XDocument.Load(manifest);

            Assert.AreEqual(
                deployment.EffectivePackageName, LocalValue(after, "Identity", "Name"),
                "Identity/@Name was not rewritten, so every layout registers under one name and " +
                "the second to deploy replaces the first.");
            Assert.AreEqual(
                deployment.EffectiveAliasExeName, LocalValue(after, "ExecutionAlias", "Alias"),
                "uap5:ExecutionAlias/@Alias was not rewritten, so two layouts still fight over " +
                "one stub in %LOCALAPPDATA%\\Microsoft\\WindowsApps and Windows picks the binary.");

            // A rewritten manifest must still load and register, so the suffix has to be legal
            // in both positions rather than merely different.
            Assert.AreNotEqual(baseName, LocalValue(after, "Identity", "Name"));
            Assert.AreEqual(".exe", Path.GetExtension(LocalValue(after, "ExecutionAlias", "Alias")));
        }
        finally
        {
            try { Directory.Delete(layout, recursive: true); }
            catch (IOException) { /* Temp litter is not worth failing a passing test over. */ }
        }
    }

    /// <summary>Reads one attribute by local name, ignoring which namespace revision declares it.</summary>
    private static string LocalValue(XDocument document, string element, string attribute)
    {
        var value = document.Descendants()
            .Where(e => e.Name.LocalName == element)
            .Select(e => e.Attribute(attribute)?.Value)
            .FirstOrDefault(v => v is not null);

        Assert.IsNotNull(value, $"Manifest has no {element}/@{attribute}.");
        return value;
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Join(dir, "Reactor.slnx")))
            dir = Path.GetDirectoryName(dir);

        Assert.IsNotNull(dir, "Could not find repo root (Reactor.slnx).");
        return dir;
    }
}
