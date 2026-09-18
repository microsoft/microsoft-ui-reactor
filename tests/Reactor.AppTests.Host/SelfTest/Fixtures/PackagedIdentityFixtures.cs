using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Hosting.Persistence;
using Microsoft.UI.Reactor.Hosting.Shell;
using Reactor.Tests.Shared;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Fixtures that are only meaningful inside a process with MSIX package identity.
/// </summary>
/// <remarks>
/// <para>These live in the shared fixture corpus rather than in a packaged-only source
/// set, so the two hosts stay a single body of tests. What differs is <i>selection</i>:
/// they are declared <c>SelfTestTier.Packaged</c> in
/// <c>SelfTestFixtureRegistry.TierRequirements</c>, so the unpackaged host neither lists
/// nor runs them — <c>--list-fixtures</c> is deliberately tier-dependent (issue #1154).
/// They previously ran everywhere and self-skipped, which restated a permanent structural
/// fact as a per-run observation and left three entries in the amber skip inventory on
/// every unpackaged run.</para>
/// <para><b>Selection and the gate share one predicate, and that is deliberate.</b> Both
/// <c>SelfTestFixtureRegistry</c>'s tier filter and <see cref="RequirePackagedTier"/> read
/// <see cref="IsPackagedTier"/>, which compares the <i>entry assembly name</i> and nothing
/// else. So inside the packaged host the gate always returns <c>true</c> and never skips —
/// including when that host was launched without MSIX identity. The gate is therefore
/// <b>not</b> an independent identity check, and nothing here should be read as one.</para>
/// <para><b>What actually catches a mis-launched packaged host is <see cref="IdentityGuard"/></b>
/// — a broken registration, a stale alias, someone running the .exe straight out of the build
/// output. It asserts <c>PackageRuntime.IsPackaged</c>, <c>Package.Current</c> and the install
/// location, and <b>fails</b> when they do not hold;
/// <c>PackagedSelfTestBatch.IdentityDependentFixtures_Actually_Asserted</c> additionally
/// requires that fixture to have <i>passed</i>. That is the whole of the identity evidence.</para>
/// <para><b>Why the gate keys off the entry assembly.</b> A fixture that merely skipped
/// whenever <c>PackageRuntime.IsPackaged</c> was false would be worse than useless: it
/// would treat the missing-identity case as an excuse rather than a fault, which is
/// precisely the failure mode this tier exists to remove. Keying off the entry assembly
/// instead makes the requirement structural: the packaged host binary <i>must</i> have
/// identity, and says so by failing.</para>
/// </remarks>
internal static class PackagedIdentityFixtures
{
    /// <summary>
    /// Assembly name of the packaged host (<c>tests/Reactor.PackagedTests.Host</c>).
    /// Kept in sync with that project's <c>AssemblyName</c>.
    /// </summary>
    internal const string PackagedHostAssemblyName = "Reactor.PackagedTests.Host";

    /// <summary>
    /// Base <c>Identity/@Name</c> as declared in the packaged host's source manifest.
    /// </summary>
    /// <remarks>
    /// Not what the package is actually registered as. The deployment uniquifies this per
    /// layout directory so that concurrent checkouts of this repo cannot evict each other's
    /// registration or fight over one execution alias; use
    /// <see cref="ExpectedPackageIdentityName"/> for anything that compares against a live
    /// package.
    /// </remarks>
    internal const string PackageIdentityName = "Microsoft.UI.Reactor.PackagedTests.Host";

    /// <summary>
    /// The identity this process must be running under: <see cref="PackageIdentityName"/>
    /// derived for the directory this build was deployed from.
    /// </summary>
    /// <remarks>
    /// Re-derived here rather than passed in. The deployment derives from the layout directory
    /// it registers, this derives from the directory the process is running out of, and the
    /// tier's own install-location invariant says those are the same directory — so the two
    /// sides agree with no channel between them, and the checks below stay exact equality
    /// rather than degrading to a prefix match that a stale registration could satisfy.
    /// </remarks>
    internal static string ExpectedPackageIdentityName =>
        WorktreeIdentity.DerivePackageName(PackageIdentityName, AppContext.BaseDirectory);

    /// <summary>
    /// True when this process is the packaged host, i.e. when package identity is a
    /// requirement rather than a possibility.
    /// </summary>
    internal static bool IsPackagedTier =>
        string.Equals(
            Assembly.GetEntryAssembly()?.GetName().Name,
            PackagedHostAssemblyName,
            StringComparison.Ordinal);

    /// <summary>
    /// Gate for an identity-dependent fixture. Returns <c>true</c> when the caller should
    /// run its checks; otherwise emits a single TAP skip naming why and returns
    /// <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <para>The skip's check name is derived from <paramref name="fixture"/> rather than passed
    /// in, so callers cannot invent three different spellings for the same concept and the
    /// name always points at the fixture a reader has to go look at. Call it as
    /// <c>RequirePackagedTier(H, this)</c>.</para>
    /// <para><b>The skip arm is unreachable through either tier's runner</b>, and deliberately so.
    /// In the packaged host <see cref="IsPackagedTier"/> is true, so this returns <c>true</c>
    /// whether or not the process has MSIX identity — missing identity is reported by
    /// <see cref="IdentityGuard"/> <i>failing</i>, not by anything here skipping. In the unpackaged
    /// host the fixture is declared <c>SelfTestTier.Packaged</c> and never selected (issue #1154).
    /// This is <b>not</b> a second, independent identity check.</para>
    /// <para>It is kept because it is the safety net for a <i>missing or wrong tier
    /// declaration</i>, which is the mistake a contributor will actually make. Drop the
    /// <c>TierRequirements</c> entry and the fixture runs unpackaged again — with the gate it
    /// degrades to a clean skip plus an amber inventory entry naming the reason, which is the
    /// "you forgot to declare it" signal; without it, <c>ApplicationData.Current</c> throws and
    /// the fixture reports an opaque <c>COMException</c> instead.</para>
    /// </remarks>
    internal static bool RequirePackagedTier(Harness h, SelfTestFixtureBase fixture)
    {
        if (IsPackagedTier) return true;
        h.Skip(
            $"{fixture.GetType().Name}_RequiresPackagedTier",
            "needs MSIX package identity - only runs in the Reactor.PackagedTests tier");
        return false;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Identity guard — the tier's own anti-vacuity control
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Proves the packaged tier really is packaged, so every other identity-dependent
    /// check in the run is a measurement rather than an accident.
    /// </summary>
    /// <remarks>
    /// This is the fixture that must fail if the tier is mis-launched. It asserts three
    /// independent facts rather than one: the Win32 identity probe Reactor itself
    /// branches on, the WinRT package identity, and that the package the OS resolved is
    /// <i>this</i> build rather than some other registration left behind by an earlier
    /// run.
    /// </remarks>
    internal class IdentityGuard(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            if (!RequirePackagedTier(H, this)) return Task.CompletedTask;

            // The probe Reactor's own JumpList / PackagedSettingsStore / WindowIcon paths
            // branch on. If this is false the tier is running unpackaged and everything
            // downstream is vacuous.
            H.Check("PackagedIdentity_PackageRuntime_IsPackaged", PackageRuntime.IsPackaged);

            // Corroborate through a completely different mechanism (WinRT rather than the
            // kernel32 probe), so a bug in one cannot make the other lie.
            string? name = null, familyName = null, installPath = null;
            try
            {
                var pkg = global::Windows.ApplicationModel.Package.Current;
                name = pkg.Id.Name;
                familyName = pkg.Id.FamilyName;
                installPath = pkg.InstalledLocation.Path;
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException)
            {
                // Package.Current does not return null without identity — it throws. Measured
                // on this repo's host: InvalidOperationException, "Operation is not valid due
                // to the current state of the object" (the CsWinRT projection of
                // APPMODEL_ERROR_NO_PACKAGE). COMException is caught alongside it because the
                // same failure surfaces unprojected on other Windows App SDK versions.
                // Deliberately NOT a bare `catch (Exception)`: the checks below must still
                // fail — with a diagnosable reason — rather than have an unrelated defect in
                // this fixture swallowed and reported as "no identity".
                Console.WriteLine($"# Package.Current threw: {ex.GetType().Name}: {ex.Message}");
            }

            var expectedName = ExpectedPackageIdentityName;

            // Both halves of the comparison travel with the verdict, not through `#` lines.
            // The TAP reader keeps only `ok`/`not ok`, so a `Console.WriteLine("# ...")`
            // diagnostic is dropped before it reaches the MSTest failure detail — the exact
            // failure would be visible in raw TAP and invisible in CI. Every side of the
            // comparison is derived from a path, so a mismatch is only diagnosable with the
            // path and the name that came out of it.
            var identityDetail =
                $"expected={expectedName}; actual={name ?? "<null>"}; " +
                $"family={familyName ?? "<null>"}; " +
                $"baseDirectory={AppContext.BaseDirectory}; " +
                $"installLocation={installPath ?? "<null>"}";

            H.Check("PackagedIdentity_Package_Name_Matches", name == expectedName, identityDetail);
            H.Check("PackagedIdentity_FamilyName_Derived_From_Name",
                familyName is not null &&
                familyName.StartsWith(expectedName + "_", StringComparison.Ordinal),
                identityDetail);

            // The registration must point at the build output this process is running
            // from. A stale registration of an older layout would otherwise let the tier
            // silently test a different binary. Compared through the same canonicalisation
            // the identity itself is derived from: a raw comparison would reject a valid
            // package whenever the recorded and running spellings of one directory differ,
            // which is exactly what a junctioned parent produces.
            H.Check("PackagedIdentity_InstallLocation_Is_This_Build",
                WorktreeIdentity.IsSameDirectory(installPath, AppContext.BaseDirectory),
                identityDetail);

            return Task.CompletedTask;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  MRT / PRI — the identity rewrite happens after PRI indexing
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves packaged content through MRT under the <i>derived</i> identity.
    /// </summary>
    /// <remarks>
    /// <para>This tier rewrites <c>Identity/@Name</c> in the generated manifest after the
    /// build has already produced <c>resources.pri</c>, and a PRI's primary resource map is
    /// named for the package it was indexed against. Measured on this build, the map is
    /// <c>Microsoft.UI.Reactor.PackagedTests.Host</c> with
    /// <c>uniqueName="ms-appx://Microsoft.UI.Reactor.PackagedTests.Host/"</c>, while the
    /// package registers as that name plus a per-layout suffix — so the two genuinely
    /// disagree, and the host genuinely has indexed content: <c>Themes/Generic.xaml</c> is a
    /// <c>Page</c>, <c>Images\*.png</c> and the window icon are <c>Content</c>, and the
    /// generated PRI is ~1.3 MB with a populated <c>Files</c> subtree.</para>
    /// <para>So the question is not whether the mismatch exists — it does — but whether
    /// Windows resolves packaged content by the running package's install location or by the
    /// name recorded in the PRI. That is a property of the OS, not of this repo, and it is
    /// not something to reason about: this fixture measures it. It is the reason the rewrite
    /// is allowed to stay after PRI generation, and it is what fails if a future Windows or
    /// Windows App SDK version starts keying resolution on the recorded name.</para>
    /// <para>Deliberately probes two independent mechanisms. <c>ms-appx:</c> through
    /// <c>StorageFile</c> is the path XAML uses for packaged content; Reactor's own
    /// <c>WindowIcon</c> does <b>not</b> exercise it, because it rewrites <c>ms-appx:</c>
    /// onto a <c>BaseDirectory</c> filesystem path and never reaches MRT. The
    /// <c>MainResourceMap</c> subtree lookup is the raw MRT path with no file-system
    /// fallback available to mask a failure.</para>
    /// </remarks>
    internal class ResourceResolution(Harness h) : SelfTestFixtureBase(h)
    {
        /// <summary>A <c>Content</c> item of the packaged host, present in the PRI's <c>Files</c> subtree.</summary>
        private const string PackagedImage = "Images/Square44x44Logo.png";

        public override async Task RunAsync()
        {
            if (!RequirePackagedTier(H, this)) return;

            var packageName = TryPackageName() ?? "<null>";
            var mapUri = TryMainResourceMapUri() ?? "<null>";

            // Mechanism 1: ms-appx: through StorageFile — what XAML uses for packaged content.
            string? storageDetail;
            var storageResolved = false;
            try
            {
                var file = await global::Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///" + PackagedImage));
                storageResolved = !string.IsNullOrEmpty(file?.Path);
                storageDetail = file?.Path ?? "<null>";
            }
            // Narrowed deliberately. A resolution failure arrives as one of these — a missing
            // asset, a malformed URI, or an MRT/WinRT HRESULT — and each is a real answer worth
            // recording. Anything else is a defect in this fixture, and swallowing it would
            // report "resolution broke" for a fault that has nothing to do with resolution.
            catch (Exception ex) when (ex is FileNotFoundException or ArgumentException
                or COMException or UnauthorizedAccessException)
            {
                storageDetail = $"{ex.GetType().Name}: {ex.Message}";
            }

            // Both halves of the disagreement travel with the verdict. Without them a failure
            // says resolution broke without saying whether the package, the recorded map, or
            // the asset itself was the part that moved.
            H.Check("PackagedResources_MsAppx_Resolves_Under_Derived_Identity", storageResolved,
                $"ms-appx:///{PackagedImage} -> {storageDetail}; " +
                $"package={packageName}; mainResourceMap={mapUri}");

            // Mechanism 2: the raw MRT map. No filesystem fallback can mask a failure here,
            // so this is the check that actually pins resolution to the PRI rather than to
            // the install directory happening to contain the file.
            string? mrtDetail;
            var mrtResolved = false;
            try
            {
                var files = global::Windows.ApplicationModel.Resources.Core.ResourceManager
                    .Current.MainResourceMap.GetSubtree("Files");
                var candidate = files?.GetValue(PackagedImage);
                mrtResolved = candidate is not null;
                mrtDetail = candidate?.ValueAsString ?? "<null>";
            }
            // Same narrowing as above: a genuine MRT miss surfaces as one of these, and
            // everything else is this fixture being wrong rather than resolution being wrong.
            catch (Exception ex) when (ex is ArgumentException or COMException
                or InvalidOperationException)
            {
                mrtDetail = $"{ex.GetType().Name}: {ex.Message}";
            }

            H.Check("PackagedResources_MrtSubtree_Resolves_Under_Derived_Identity", mrtResolved,
                $"Files/{PackagedImage} -> {mrtDetail}; " +
                $"package={packageName}; mainResourceMap={mapUri}");
        }

        private static string? TryPackageName()
        {
            try { return global::Windows.ApplicationModel.Package.Current.Id.Name; }
            catch (Exception ex) when (ex is InvalidOperationException or COMException) { return null; }
        }

        private static string? TryMainResourceMapUri()
        {
            try
            {
                return global::Windows.ApplicationModel.Resources.Core.ResourceManager
                    .Current.MainResourceMap.Uri?.ToString();
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException) { return null; }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  PackagedSettingsStore — cannot work at all without identity
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Round-trips bytes through <c>ApplicationData.Current.LocalSettings</c> via
    /// <see cref="PackagedSettingsStore"/>.
    /// </summary>
    /// <remarks>
    /// The strongest non-vacuity anchor in the tier, and deliberately independent of the
    /// window-icon work: <c>ApplicationData.Current</c> <i>throws</i> in a process with no
    /// package identity, so this fixture cannot be made to pass unpackaged by any amount
    /// of luck. Its whole code path — the <c>IsPackaged</c> branch in
    /// <c>PackagedSettingsStore.IsAvailable</c> and the WinRT container write — is
    /// unreachable in every other tier the repo has.
    /// </remarks>
    internal class SettingsStoreRoundTrip(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            if (!RequirePackagedTier(H, this)) return Task.CompletedTask;

            H.Check("PackagedSettings_IsAvailable", PackagedSettingsStore.IsAvailable());

            var store = new PackagedSettingsStore();

            // Unique per run so a leftover container from an earlier registration can
            // never satisfy the read below.
            var id = "packaged-selftest-" + Guid.NewGuid().ToString("N");
            var payload = new byte[] { 0x01, 0x7F, 0x00, 0xFE, 0x42, 0xA5 };

            // Absence first: proves the read is actually consulting storage rather than
            // returning a canned success for anything it is handed.
            H.Check("PackagedSettings_Unknown_Id_Not_Found", !store.TryRead(id, out _));

            store.Write(id, payload);

            var found = store.TryRead(id, out var read);
            H.Check("PackagedSettings_Written_Id_Found", found);
            H.Check("PackagedSettings_RoundTrips_Exact_Bytes",
                read is not null && read.AsSpan().SequenceEqual(payload));

            return Task.CompletedTask;
        }
    }
}
