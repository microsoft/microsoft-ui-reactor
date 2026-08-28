using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Hosting.Persistence;
using Microsoft.UI.Reactor.Hosting.Shell;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Fixtures that are only meaningful inside a process with MSIX package identity
/// (issue #1148).
/// </summary>
/// <remarks>
/// <para>These live in the shared fixture corpus rather than in a packaged-only source
/// set, so the two hosts stay a single body of tests and <c>--list-fixtures</c> agrees
/// across both tiers. What differs is the <see cref="IsPackagedTier"/> gate below.</para>
/// <para><b>Why the gate keys off the entry assembly.</b> A fixture that merely skipped
/// whenever <c>PackageRuntime.IsPackaged</c> was false would be worse than useless: if
/// the packaged tier ever launched the app without identity — a broken registration, a
/// stale alias resolving to something else, someone running the .exe out of the build
/// output — every identity check would quietly skip and the suite would report green
/// while measuring nothing. That is precisely the failure mode issue #1148 exists to
/// remove. Keying off the entry assembly instead makes the requirement structural: the
/// packaged host binary <i>must</i> have identity, and says so by failing.</para>
/// </remarks>
internal static class PackagedIdentityFixtures
{
    /// <summary>
    /// Assembly name of the packaged host (<c>tests/Reactor.PackagedTests.Host</c>).
    /// Kept in sync with that project's <c>AssemblyName</c>.
    /// </summary>
    internal const string PackagedHostAssemblyName = "Reactor.PackagedTests.Host";

    /// <summary>Expected <c>Identity/@Name</c> from the packaged host's manifest.</summary>
    internal const string PackageIdentityName = "Microsoft.UI.Reactor.PackagedTests.Host";

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
    /// The skip's check name is derived from <paramref name="fixture"/> rather than passed
    /// in, so callers cannot invent three different spellings for the same concept and the
    /// name always points at the fixture a reader has to go look at. Call it as
    /// <c>RequirePackagedTier(H, this)</c>.
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

            H.Check("PackagedIdentity_Package_Name_Matches", name == PackageIdentityName);
            H.Check("PackagedIdentity_FamilyName_Derived_From_Name",
                familyName is not null &&
                familyName.StartsWith(PackageIdentityName + "_", StringComparison.Ordinal));

            // The registration must point at the build output this process is running
            // from. A stale registration of an older layout would otherwise let the tier
            // silently test a different binary.
            var baseDir = global::System.IO.Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            H.Check("PackagedIdentity_InstallLocation_Is_This_Build",
                installPath is not null &&
                string.Equals(
                    global::System.IO.Path.TrimEndingDirectorySeparator(installPath),
                    baseDir,
                    StringComparison.OrdinalIgnoreCase));

            return Task.CompletedTask;
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
