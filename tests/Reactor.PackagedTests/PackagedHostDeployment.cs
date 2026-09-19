using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Reactor.Tests.Shared;
using Windows.Management.Deployment;

namespace Microsoft.UI.Reactor.PackagedTests;

/// <summary>
/// Registers the packaged host's loose MSIX layout and resolves the execution-alias stub
/// used to launch it.
/// </summary>
/// <remarks>
/// <para>Deliberately behind an interface. Registration and activation are the only part
/// of this tier that MSTest may one day own itself: <c>Microsoft.Testing.Extensions.PackagedApp</c>
/// gained register-and-activate-by-AUMID in testfx <c>main</c>, and when that ships,
/// swapping to it should touch this file and nothing else.</para>
/// <para>It is <b>not</b> a candidate for AUMID activation today even ignoring the
/// shipping question. <c>IApplicationActivationManager</c> starts the app through the
/// shell broker rather than as a child process, so no handles are inherited and stdout
/// cannot be redirected at all — the TAP stream would need a file or a named pipe. The
/// execution alias avoids that entirely: launching the stub is an ordinary
/// <c>CreateProcess</c>, so the child inherits stdout/stderr and argv while still running
/// with full package identity.</para>
/// </remarks>
internal interface IPackagedHostDeployment
{
    /// <summary>Registers the layout and returns the absolute path of the alias stub.</summary>
    string Register();

    /// <summary>Removes the registration. Safe to call when registration failed.</summary>
    void Unregister();
}

/// <summary>
/// Registers the build output in place with <see cref="PackageManager"/> using
/// <see cref="DeploymentOptions.DevelopmentMode"/>, which is what allows an unsigned
/// layout to be registered (Developer Mode, or sideloading, must be enabled).
/// </summary>
internal sealed class AppxLooseLayoutDeployment : IPackagedHostDeployment
{
    /// <summary>Must match <c>Identity/@Name</c> in the host's Package.appxmanifest.</summary>
    internal const string PackageName = "Microsoft.UI.Reactor.PackagedTests.Host";

    /// <summary>Must match <c>Identity/@Publisher</c> in the host's Package.appxmanifest.</summary>
    internal const string PackagePublisher = "CN=Microsoft.UI.Reactor.PackagedTests.Host";

    /// <summary>Must match the <c>uap5:ExecutionAlias</c> in the host's Package.appxmanifest.</summary>
    internal const string AliasExeName = "reactor-packaged-test-host.exe";

    private readonly string _layoutDir;
    private string? _registeredFullName;
    private string? _effectivePackageName;
    private string? _effectiveAliasExeName;
    private IReadOnlyList<string>? _supportedDerivedNames;

    internal AppxLooseLayoutDeployment(string layoutDir) => _layoutDir = layoutDir;

    /// <summary>
    /// The package name this deployment actually registers: <see cref="PackageName"/> made
    /// unique to the layout directory.
    /// </summary>
    /// <remarks>
    /// <para>The manifest's identity is a machine-global name, and so is the execution alias.
    /// Registering under the literal constant means two checkouts of this repo cannot run the
    /// packaged tier concurrently — registration is name-scoped, not path-scoped, so the
    /// second run evicts the first one's package and repoints the alias at its own build
    /// output. Deriving from the layout directory makes concurrent checkouts disjoint while
    /// keeping a single checkout's identity stable across reruns.</para>
    /// <para>Computed lazily rather than in the constructor so that constructing a deployment
    /// is never the thing that throws for a malformed path.</para>
    /// </remarks>
    internal string EffectivePackageName =>
        _effectivePackageName ??= WorktreeIdentity.DerivePackageName(PackageName, _layoutDir);

    /// <summary>The execution-alias file name this deployment expects to be created.</summary>
    /// <remarks>
    /// Derived for the same reason as <see cref="EffectivePackageName"/>, and necessarily so:
    /// aliases land at one fixed path under <c>%LOCALAPPDATA%\Microsoft\WindowsApps</c>, so
    /// uniquifying only the package name would still leave two registrations fighting over a
    /// single stub with Windows deciding which binary the tier launches.
    /// </remarks>
    internal string EffectiveAliasExeName =>
        _effectiveAliasExeName ??= WorktreeIdentity.DeriveAliasExeName(AliasExeName, _layoutDir);

    /// <summary>
    /// Every package name this layout could legitimately be carrying: the current algorithm
    /// version's derivation plus each superseded one still listed as supported.
    /// </summary>
    /// <remarks>
    /// The generated manifest is rewritten in place, so between an algorithm bump and the next
    /// rebuild the layout carries the previous version's derived name. Accepting only the
    /// current one would make the drift guard abort on exactly the manifest the rewrite below
    /// is about to migrate. Exact derivations rather than a suffix-shape test, so a name that
    /// merely looks derived — another layout's, for instance — is still rejected.
    /// </remarks>
    internal IReadOnlyList<string> SupportedDerivedNames() =>
        _supportedDerivedNames ??=
            DeriveSupportedNames(PackageName, _layoutDir, WorktreeIdentity.SupportedAlgorithmVersions);

    /// <summary>
    /// The accepted names for a given base, layout and set of algorithm versions.
    /// </summary>
    /// <remarks>
    /// Pure and parameterised over the version set purely so a test can drive it with more than
    /// one version. <see cref="WorktreeIdentity.SupportedAlgorithmVersions"/> currently holds a
    /// single entry, which would make "every supported version is accepted" true of an
    /// implementation that only ever derived the current one.
    /// </remarks>
    internal static IReadOnlyList<string> DeriveSupportedNames(
        string basePackageName, string layoutDirectory, IEnumerable<string> algorithmVersions) =>
        algorithmVersions
            .Select(v => WorktreeIdentity.DerivePackageName(basePackageName, layoutDirectory, v))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Whether a manifest's <c>Identity/@Name</c> is one this layout may legitimately carry.
    /// </summary>
    /// <remarks>
    /// The base name (freshly built) and any supported version's exact derivation (registered
    /// earlier, possibly under a superseded version, with no intervening rebuild). Anything else
    /// is drift, including a name that merely has a derived <i>shape</i> — that one most likely
    /// belongs to a different layout, and adopting it would make cleanup sweep someone else's
    /// registration.
    /// </remarks>
    internal bool IsExpectedManifestName(string? manifestName) =>
        string.Equals(manifestName, PackageName, StringComparison.Ordinal) ||
        SupportedDerivedNames().Contains(manifestName, StringComparer.Ordinal);

    /// <summary>
    /// The names the registration sweep looks up when broad enumeration is unavailable.
    /// </summary>
    /// <remarks>
    /// Every supported derivation plus the base name. The base name covers a pre-derivation run
    /// of this same checkout; the superseded derivations cover a run from before an algorithm
    /// bump. Both are registrations this layout owns and cleanup is expected to migrate, and
    /// this lookup is the only way to reach them once enumeration has failed — probing just the
    /// current derivation would leave them in place and let registration proceed on top.
    /// </remarks>
    internal IReadOnlyList<string> FallbackLookupNames() =>
        FallbackLookupNames(PackageName, _layoutDir, WorktreeIdentity.SupportedAlgorithmVersions);

    /// <inheritdoc cref="FallbackLookupNames()"/>
    /// <remarks>
    /// Parameterised over the version set for the same reason as
    /// <see cref="DeriveSupportedNames"/>: one version exists today, so a test using the live
    /// list could not tell "every supported derivation" from "the current one".
    /// </remarks>
    internal static IReadOnlyList<string> FallbackLookupNames(
        string basePackageName, string layoutDirectory, IEnumerable<string> algorithmVersions) =>
        DeriveSupportedNames(basePackageName, layoutDirectory, algorithmVersions)
            .Append(basePackageName)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The build hint shared by every "host not built" diagnostic, so the message is the same
    /// whether the host is missing at discovery time or at registration time.
    /// </summary>
    /// <remarks>
    /// Names the platform actually being searched for. The host coerces an unspecified
    /// platform to x64 while this resolver looks under the running process architecture, so on
    /// an ARM64 machine a hint hardcoded to x64 would send the reader to build the very layout
    /// that just failed to match.
    /// </remarks>
    internal static string BuildHint =>
        $"Build it with: dotnet build tests/Reactor.PackagedTests.Host -p:Platform={HostPlatform}";

    /// <summary>Platform folder segment this resolver expects the host to have built into.</summary>
    private static string HostPlatform => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "ARM64",
        _ => "x64",
    };

    /// <summary>
    /// Validates and absolutises a <c>REACTOR_PACKAGED_HOST_DIR</c> override.
    /// </summary>
    /// <remarks>
    /// <para>The result reaches <c>RegisterPackageAsync</c> as <c>new Uri(manifestPath)</c>,
    /// which requires an absolute path — so returning a relative override unchanged made a
    /// perfectly valid value fail for a reason with nothing to do with the layout.</para>
    /// <para>A relative value is tried against the working directory first (the ordinary
    /// convention), then against the repo root. The second attempt exists because the
    /// documented shape of this override is a repo-relative path like
    /// <c>tests/Reactor.PackagedTests.Host/bin/x64/Debug/…</c>, while the test host's working
    /// directory is its own binary folder — so the intuitive value would otherwise never
    /// resolve.</para>
    /// </remarks>
    internal static string ResolveOverrideDirectory(string value, string? repoRoot = null)
    {
        if (Directory.Exists(value)) return Path.GetFullPath(value);

        if (!Path.IsPathRooted(value) && repoRoot is not null)
        {
            var fromRepoRoot = Path.GetFullPath(Path.Join(repoRoot, value));
            if (Directory.Exists(fromRepoRoot)) return fromRepoRoot;
        }

        var attempts = Path.IsPathRooted(value) || repoRoot is null
            ? Path.GetFullPath(value)
            : $"{Path.GetFullPath(value)}\n  {Path.GetFullPath(Path.Join(repoRoot, value))}";

        throw new DirectoryNotFoundException(
            $"REACTOR_PACKAGED_HOST_DIR points at a path that does not exist: {value}\n" +
            $"Tried:\n  {attempts}");
    }

    /// <summary>Walks up from the running assembly for the <c>Reactor.slnx</c> sentinel.</summary>
    internal static string? TryFindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Join(dir, "Reactor.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir;
    }

    /// <summary>
    /// Configuration and target framework of the *shim*, stamped in by MSBuild.
    /// </summary>
    /// <remarks>
    /// The host's output path contains the configuration, so hardcoding <c>Debug</c> here
    /// would break <c>dotnet test … -c Release</c>: the host builds to
    /// <c>bin\x64\Release\…</c> while this looked in <c>bin\x64\Debug\…</c> and reported
    /// "packaged host not built" for a host that had just been built. Reading the value
    /// MSBuild used keeps the two in step for any configuration.
    /// </remarks>
    private static string MetadataOr(string key, string fallback) =>
        typeof(AppxLooseLayoutDeployment).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(a => string.Equals(a.Key, key, StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(a.Value))
            .Select(a => a.Value!)
            .FirstOrDefault() ?? fallback;

    /// <summary>The build output directory holding AppxManifest.xml and the host binaries.</summary>
    internal static string ResolveLayoutDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable("REACTOR_PACKAGED_HOST_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
            return ResolveOverrideDirectory(overrideDir, TryFindRepoRoot());

        var dir = TryFindRepoRoot();
        if (dir == null)
            throw new DirectoryNotFoundException("Could not find repo root (Reactor.slnx)");

        // The host coerces an empty/AnyCPU platform to x64 and declares only x64/ARM64, so
        // its bin path always carries a platform segment; the running process architecture
        // is the one that was built.
        var platform = HostPlatform;

        var configuration = MetadataOr("ReactorPackagedTestsConfiguration", "Debug");
        var tfm = MetadataOr("ReactorPackagedTestsTargetFramework", "net10.0-windows10.0.22621.0");

        // Path.Join rather than Path.Combine throughout this file: Combine silently
        // discards everything before a segment that happens to be rooted, so a bad
        // constant or a future refactor could turn a repo-relative lookup into an
        // absolute one somewhere else on disk. Join always concatenates.
        return Path.Join(dir, "tests", "Reactor.PackagedTests.Host", "bin", platform,
            configuration, tfm);
    }

    public string Register()
    {
        // Claimed before anything is removed or registered. Identity is per-layout, so two
        // runs of the *same* checkout still derive the same package name and alias — and the
        // registration sweep below would then tear down a sibling run's live package. The
        // cross-checkout case is solved by derivation; this is the case derivation cannot
        // solve, and it fails closed rather than silently destroying the other run.
        AcquireLayoutLock();

        // Absolute: this is handed to RegisterPackageAsync as `new Uri(...)`, which requires
        // an absolute path. ResolveLayoutDirectory already absolutises, and this keeps the
        // guarantee local for a caller that constructed the type with its own layout dir.
        var manifest = Path.GetFullPath(Path.Join(_layoutDir, "AppxManifest.xml"));
        if (!File.Exists(manifest))
        {
            throw new FileNotFoundException(
                $"Packaged host not built. Expected a generated AppxManifest.xml at: {manifest}\n" +
                BuildHint);
        }

        var manager = new PackageManager();

        // Drift guard, deliberately *before* registering. The constants must match the
        // manifest's Identity element, and nothing at compile time links them. Checking
        // afterwards would be too late: the package would already be registered under the
        // manifest's identity while every lookup here — including cleanup — searched for the
        // stale constants, leaking the registration and leaving the alias owned by a layout
        // no later run could remove.
        //
        // The name is checked against both spellings because this method rewrites the
        // generated manifest in place: a freshly built layout carries the base name, and a
        // layout registered earlier without an intervening rebuild already carries a derived
        // one. Every *supported* algorithm version's derivation is accepted, not just the
        // current one — after a version bump the layout still carries the previous version's
        // name until something rewrites it, and this guard runs first, so accepting only the
        // current derivation would abort before ApplyDerivedIdentity could migrate it and
        // leave the tier unrunnable until a rebuild. Derivations are compared exactly rather
        // than by suffix shape: a name that merely looks derived may belong to another layout,
        // which is the ownership confusion RemoveExistingRegistrations exists to refuse.
        // Any value that is neither the base name nor one of those derivations is real drift.
        var (manifestName, manifestPublisher) = ReadManifestIdentity(manifest);

        if (!IsExpectedManifestName(manifestName) ||
            !string.Equals(manifestPublisher, PackagePublisher, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The constants in {nameof(AppxLooseLayoutDeployment)} have drifted from the " +
                $"manifest's Identity element, so cleanup could not remove what registration " +
                $"would create.\nManifest: {manifest}\n" +
                $"  Name:      manifest '{manifestName}' vs constant '{PackageName}' " +
                $"(or a supported derivation: {string.Join(", ", SupportedDerivedNames())})\n" +
                $"  Publisher: manifest '{manifestPublisher}' vs constant '{PackagePublisher}'");
        }

        // Rewrite the *generated* manifest in the build output, not the source
        // Package.appxmanifest. The tier registers this layout in place, so the build output
        // is already the thing being deployed; rewriting here is idempotent and self-healing
        // (a rebuild regenerates the base name and this simply derives it again), and it
        // leaves the source manifest as the readable statement of the base identity that the
        // drift tests check against.
        //
        // The rewrite lands *after* the build produced resources.pri, and a PRI's primary
        // resource map is named for the package it was indexed against — measured here as
        // ms-appx://Microsoft.UI.Reactor.PackagedTests.Host/, i.e. the *pre-rewrite* name,
        // while the package registers under that name plus the derived suffix. The host does
        // ship indexed file content (Themes/Generic.xaml as a Page, Images\*.png and the
        // window icon as Content), so the mismatch is real rather than hypothetical.
        //
        // It is nonetheless safe, and that is a measurement, not an assumption: the
        // Packaged_ResourceResolution selftest fixture runs inside the packaged process under
        // the derived identity and asserts that both ms-appx: and a raw MRT Files subtree
        // lookup still resolve — so Windows resolves packaged content out of the install
        // location rather than by the name recorded in the PRI. That fixture is what fails if
        // a future Windows or Windows App SDK version changes this; string resources, which
        // it cannot cover, are held at zero by PackagedStringResourceTripwireTests. If either
        // goes red, apply the derived identity before PRI indexing rather than after it.
        ApplyDerivedIdentity(manifest);

        // Remove any registration left behind by an earlier run *of this layout* before
        // registering. Without this a stale registration — pointing at a since-rebuilt or
        // relocated directory — keeps owning the alias, and the tier would silently exercise
        // the wrong binary. Packaged_IdentityGuard also checks the install location for
        // exactly this reason, but failing fast here gives a far clearer diagnostic.
        RemoveExistingRegistrations(manager);

        var result = manager
            .RegisterPackageAsync(new Uri(manifest), null, DeploymentOptions.DevelopmentMode)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        if (result.ExtendedErrorCode != null)
        {
            throw new InvalidOperationException(
                $"Registering the packaged host failed (0x{result.ExtendedErrorCode.HResult:X8}): " +
                $"{result.ErrorText}\nManifest: {manifest}\n" +
                "Registering an unsigned loose layout requires Developer Mode (or sideloading).");
        }

        // Captured so cleanup can remove precisely what was registered rather than
        // re-deriving it from the constants.
        _registeredFullName = FindRegistration(manager)?.Id.FullName;

        if (_registeredFullName is null)
        {
            throw new InvalidOperationException(
                $"Registered '{manifest}' but no package named '{EffectivePackageName}' with " +
                $"publisher '{PackagePublisher}' was found afterwards.");
        }

        var alias = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", EffectiveAliasExeName);

        if (!File.Exists(alias))
        {
            throw new FileNotFoundException(
                $"Registration reported success but the execution alias was not created: {alias}\n" +
                "Check that Package.appxmanifest still declares the windows.appExecutionAlias " +
                $"extension with Alias=\"{AliasExeName}\".");
        }

        return alias;
    }

    /// <summary>
    /// Rewrites the generated manifest's <c>Identity/@Name</c> and every
    /// <c>uap5:ExecutionAlias/@Alias</c> to this layout's derived values.
    /// </summary>
    /// <remarks>
    /// <para>Writes only when something actually changed, so a rerun against an already-rewritten
    /// layout leaves the file — and its timestamp — alone. Elements are matched by local name
    /// so a revision of the manifest's namespace URIs does not silently turn this into a
    /// no-op; an alias that stopped being rewritten would reintroduce exactly the collision
    /// this is here to remove.</para>
    /// <para>Exposed to tests because it is the only place the derived names become something
    /// Windows acts on. Two derived strings being unequal is a fact about a hash; two
    /// <i>registrations</i> not colliding additionally requires those strings to reach the
    /// manifest that gets registered, and this is where that either happens or silently does
    /// not.</para>
    /// </remarks>
    internal void ApplyDerivedIdentity(string manifestPath)
    {
        var document = XDocument.Load(manifestPath);
        var changed = false;

        var identity = document.Root?
            .Elements()
            .FirstOrDefault(e => e.Name.LocalName == "Identity");

        var nameAttribute = identity?.Attribute("Name");
        if (nameAttribute is null)
        {
            throw new InvalidOperationException(
                $"Generated manifest has no Identity/@Name to uniquify: {manifestPath}");
        }

        if (!string.Equals(nameAttribute.Value, EffectivePackageName, StringComparison.Ordinal))
        {
            nameAttribute.Value = EffectivePackageName;
            changed = true;
        }

        var aliases = document.Descendants()
            .Where(e => e.Name.LocalName == "ExecutionAlias")
            .Select(e => e.Attribute("Alias"))
            .OfType<XAttribute>()
            .ToList();

        if (aliases.Count == 0)
        {
            throw new InvalidOperationException(
                "Generated manifest declares no windows.appExecutionAlias. The packaged tier " +
                "launches the host through its alias stub because that is the only way to keep " +
                "package identity while inheriting stdout for the TAP stream.\n" +
                $"Manifest: {manifestPath}");
        }

        // Filter on the sequence rather than in the loop body. Note this is deliberately not
        // applied to `aliases` above: that list backs the count check, which asserts the manifest
        // declares an alias at all. Narrowing it to aliases *needing* a change would make an
        // already-derived manifest look like one declaring no alias, and throw on a re-deploy.
        foreach (var alias in aliases.Where(
            a => !string.Equals(a.Value, EffectiveAliasExeName, StringComparison.Ordinal)))
        {
            alias.Value = EffectiveAliasExeName;
            changed = true;
        }

        if (!changed) return;

        // Write beside the target and swap, rather than truncating the only copy in place. A
        // kill during the write would otherwise leave a half-written manifest, and the layout
        // lock does not help: process exit releases it, so the next run acquires cleanly and
        // then fails loading the damaged file before it can register or clean up.
        var staging = manifestPath + ".tmp";
        try
        {
            document.Save(staging);
            File.Move(staging, manifestPath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(staging)) File.Delete(staging);
            }
            catch (IOException)
            {
                // Leaving a stray .tmp is strictly better than masking the original failure.
            }

            throw;
        }
    }

    public void Unregister()
    {
        // Cleanup is destructive and this layout's identity is shared with any concurrent run
        // of the same checkout, so it is gated on actually holding the claim. Register() can
        // fail *before* acquiring it — most obviously when the wait times out — and the batch
        // still keeps the deployment and calls Unregister() from class cleanup. Sweeping there
        // would remove the live package belonging to the run we just refused to disturb, which
        // would turn a diagnostic timeout into the exact eviction the lock exists to prevent.
        if (_layoutLocks is null)
        {
            if (_registeredFullName is not null)
            {
                Console.WriteLine(
                    "[Reactor.PackagedTests] Skipping cleanup: this run does not hold the layout " +
                    $"claim, so '{_registeredFullName}' is left in place rather than risking " +
                    "another run's registration.");
            }

            _registeredFullName = null;
            return;
        }

        try
        {
            // Constructed inside the protected block: WinRT activation can throw, and doing it
            // before the try would exit without releasing the lock, leaving another run of this
            // layout to wait out the full timeout for a holder that is already gone.
            var manager = new PackageManager();

            // Remove exactly what was registered first, then sweep this layout's derived
            // identity. The captured full name is what registration actually produced, so it
            // stays correct even if the constants below are edited mid-run; the sweep still
            // catches anything a previous run of *this* layout left behind. Neither can reach
            // another checkout's registration.
            if (_registeredFullName is not null) RemovePackage(manager, _registeredFullName);
            RemoveExistingRegistrations(manager);
        }
        catch (Exception ex) when (
            ex is COMException or InvalidOperationException or UnauthorizedAccessException or IOException
                or TypeInitializationException)
        {
            // Cleanup is best-effort: a failure here must not mask the real test outcome.
            // It is still reported, because a leaked registration makes the *next* run
            // ambiguous. Scoped to the failures the deployment APIs actually produce —
            // COM/WinRT faults, the InvalidOperationException RemoveExistingRegistrations
            // itself throws, permission denials, and I/O — so a genuine defect in this
            // harness still surfaces instead of being swallowed by teardown.
            Console.WriteLine($"[Reactor.PackagedTests] Unregister failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            ReleaseLayoutLock();
        }
        _registeredFullName = null;
    }

    /// <summary>
    /// Cross-process claim on this layout's derived identity, held for the whole
    /// register/run/unregister lifetime.
    /// </summary>
    /// <remarks>
    /// <para>Named for the derived suffix, so the claim covers exactly the runs that would
    /// compute the same package name and alias — which, after derivation, means concurrent runs
    /// of one checkout.</para>
    /// <para><b>A held file rather than a named mutex, because the scope has to match the state
    /// being protected.</b> That state is per-<i>user</i>: the AppX registration and the
    /// <c>%LOCALAPPDATA%\Microsoft\WindowsApps</c> alias stub are shared by every logon session
    /// of one account. A <c>Local\</c> mutex is per-session, so two runs of this layout under
    /// the same account in different sessions would both acquire and then evict each other; a
    /// <c>Global\</c> mutex spans sessions but is scoped to the machine rather than the user,
    /// and creating one needs <c>SeCreateGlobalPrivilege</c>, which an ordinary interactive
    /// account does not hold. A lock file under <c>%LOCALAPPDATA%</c> is exactly user-scoped by
    /// construction, needs no privilege, and — unlike an abandoned mutex — is released by the
    /// kernel closing the handle when a run dies, so a crash cannot wedge the next run.</para>
    /// <para>The file records the owning pid and layout so a contender can say who it is
    /// waiting on; it is opened <c>FileShare.Read</c> for that reason.</para>
    /// </remarks>
    private List<FileStream>? _layoutLocks;

    /// <summary>How long to wait for a sibling run to finish before giving up.</summary>
    /// <remarks>
    /// <para>Generous, because the expected wait is a full packaged run: blocking is the friendly
    /// outcome, and a timeout is a diagnosis rather than a policy. Timing out is safe regardless:
    /// <see cref="Unregister"/> refuses to sweep without the claim, so a run that never acquired
    /// it cannot evict the run it was waiting on.</para>
    /// <para>Sized for <b>two</b> host process budgets, not one. A run holds this lock across its
    /// whole batch, and a batch whose filter excludes the identity guard launches the host a
    /// second time to fetch it (see <c>PackagedSelfTestBatch</c>), so the owner can legitimately
    /// occupy two consecutive budgets. One budget plus the margin would let a contender time out
    /// and report a collision against a perfectly healthy owner that is merely in its second
    /// pass. The budget is itself configurable through <c>REACTOR_PACKAGED_TIMEOUT_SECONDS</c>,
    /// so both terms scale with it; the fixed margin covers registration, the sweep and teardown,
    /// which sit outside either pass.</para>
    /// </remarks>
    private static TimeSpan LayoutLockTimeout =>
        TimeSpan.FromMilliseconds(PackagedSelfTestBatch.HostTimeoutMs * 2L) + TimeSpan.FromMinutes(5);

    /// <summary>The wait budget, exposed so a test can pin its sizing to the host's passes.</summary>
    internal static TimeSpan LayoutLockTimeoutForTests => LayoutLockTimeout;

    /// <summary>Directory holding the per-layout lock files.</summary>
    private static string LockDirectory => Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft.UI.Reactor",
        "PackagedTests");

    /// <summary>
    /// Creates the lock directory, for tests that open lock files through
    /// <see cref="TryOpenLockFile"/> rather than through <see cref="TryAcquireAllLocks"/>.
    /// </summary>
    /// <remarks>
    /// Only the acquisition path creates the directory. A test that opens a lock file directly
    /// would therefore fail its own precondition on a profile where the tier has never run,
    /// diagnosing a missing directory as a held lock.
    /// </remarks>
    internal static void EnsureLockDirectory() => Directory.CreateDirectory(LockDirectory);

    /// <summary>
    /// Opens the lock file exclusively and stamps <paramref name="ownerRecord"/> into it, or
    /// returns <see langword="null"/> when another run holds it.
    /// </summary>
    /// <remarks>
    /// Separated from the waiting loop so exclusion and release can be tested directly. Without
    /// that, a broken sharing mode or a premature release would reintroduce live-package
    /// eviction while every derivation and selection test stayed green.
    /// </remarks>
    internal static FileStream? TryOpenLockFile(string path, string ownerRecord) =>
        TryOpenLockFile(path, ownerRecord, out _);

    /// <summary>
    /// As <see cref="TryOpenLockFile(string, string)"/>, additionally reporting an access
    /// refusal so a caller can tell a storage fault from contention after the fact.
    /// </summary>
    /// <remarks>
    /// The refusal is an out-parameter rather than an exception because it must not stop the
    /// poll: access denied is how a delete-pending lock file presents, and that clears itself.
    /// Only the caller that has run out of time knows the refusal was permanent, so only it can
    /// turn the record into a diagnosis.
    /// </remarks>
    internal static FileStream? TryOpenLockFile(
        string path, string ownerRecord, out Exception? lastRefusal)
    {
        lastRefusal = null;

        try
        {
            // FileShare.Read lets a contender read the owner record while still denying a
            // second writer, which is what makes this the lock.
            var stream = new FileStream(
                path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

            // Ownership transfers to the caller only on success. Returning without disposing
            // here would leave this process holding the writer lock while reporting failure, so
            // its own retries would collide with it until the deadline and bury the real error.
            //
            // Rethrown as a distinct type rather than as-is: the open succeeded, so a failure
            // to stamp is a storage fault, not contention. Left as an IOException it would be
            // swallowed by the catch below, turned into "held", and retried until the whole
            // layout timeout elapsed — reporting a competing owner that does not exist and
            // hiding the actual error.
            try
            {
                stream.SetLength(0);
                var bytes = System.Text.Encoding.UTF8.GetBytes(ownerRecord);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException
                or ObjectDisposedException or UnauthorizedAccessException
                or System.Security.SecurityException)
            {
                // Disposal is guarded because it flushes: when the stamp failed for a reason
                // that also blocks the flush — a byte-range lock, a full volume — Dispose
                // throws the same IOException, and letting that escape would pre-empt the
                // wrapper below and land right back in the "held, retry" path this exists to
                // avoid. The original failure is the one worth reporting either way.
                //
                // Both filters are deliberately wider than IOException alone. They exist to
                // name what a write to an open FileStream can fail with, not to select among
                // those failures: every one of them means this process holds a lock it cannot
                // stamp, which is the same fault. A type left out would skip the disposal
                // below and leak the handle, so the set errs wide on purpose. NotSupportedException
                // is not hypothetical — SetLength raises it on a non-seekable target.
                try { stream.Dispose(); }
                catch (Exception disposeFailure) when (disposeFailure is IOException
                    or NotSupportedException or ObjectDisposedException
                    or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    throw new LockStampException(path, new AggregateException(ex, disposeFailure));
                }

                throw new LockStampException(path, ex);
            }

            return stream;
        }
        catch (IOException)
        {
            return null;
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Refused rather than held, and the two are not distinguishable from the exception
            // alone. Refusal has a genuine transient form — Release unlinks lock files, and a
            // third party holding one with delete sharing (an indexer, a scanner) leaves it
            // delete-pending, which surfaces here as access denied and clears on its own. So
            // this keeps returning null and the caller keeps polling, which is what recovers
            // that case and what the reclamation probe wants for every case.
            //
            // What it must not do is let a permanent refusal — a read-only lock directory —
            // masquerade as contention once the wait is over. The refusal is recorded so the
            // timeout can name the storage fault instead of inventing a competing owner.
            lastRefusal = ex;
            return null;
        }
    }

    /// <summary>
    /// A lock file opened exclusively but could not be stamped with its owner record.
    /// </summary>
    /// <remarks>
    /// Deliberately not an <see cref="IOException"/>. The waiter treats that family as "held by
    /// someone else" and retries, which is right for a failed open and wrong for a failed write:
    /// the write failing means this process already owns the file, so retrying can only spend
    /// the full timeout before reporting a contender that was never there. Carrying a distinct
    /// type makes the storage fault escape the poll loop on the first occurrence.
    /// </remarks>
    internal sealed class LockStampException : InvalidOperationException
    {
        internal LockStampException(string path, Exception inner)
            : base(
                $"The layout lock '{path}' was opened exclusively but its owner record could " +
                $"not be written, so this run holds the lock without being able to identify " +
                $"itself to a contender. This is a storage failure, not contention.",
                inner)
        {
        }
    }

    /// <summary>
    /// The lock could not be prepared or opened, so its layout cannot be shown to be free.
    /// </summary>
    /// <remarks>
    /// <para>Distinct from returning <c>null</c>, which every caller of
    /// <see cref="TryAcquireAllLocks"/> reads as "another run holds this layout". A missing or
    /// read-only <c>%LOCALAPPDATA%</c> is not a contender, and reporting it as one produced a
    /// collision message that named an owner which never existed, claimed a wait that never
    /// happened, and discarded the storage error that was the actual cause. Callers that
    /// genuinely want the conservative reading — the reclamation probe, for which "cannot be
    /// shown free" must mean "leave it alone" — catch this back into <c>null</c>
    /// themselves.</para>
    /// <para>Two shapes, because the fault has two arrival points: the directory cannot be
    /// created at all, or the directory exists and the lock file inside it refuses to open.
    /// They are one type because every caller wants the same response, and two messages
    /// because naming a file as a directory would send the reader to the wrong place.</para>
    /// </remarks>
    internal sealed class LockSetupException : InvalidOperationException
    {
        internal LockSetupException(string directory, Exception inner)
            : base(
                $"The layout lock directory '{directory}' could not be prepared, so this run " +
                $"cannot arbitrate with concurrent runs over the same layout. This is a " +
                $"storage failure, not contention.",
                inner)
        {
        }

        private LockSetupException(Exception inner, string message)
            : base(message, inner)
        {
        }

        /// <summary>The lock file itself refused to open for the whole wait.</summary>
        internal static LockSetupException ForRefusedFile(string path, Exception inner) =>
            new(
                inner,
                $"The layout lock '{path}' refused to open for the whole wait. Access was " +
                $"denied rather than the file being held, so this is a storage or permissions " +
                $"failure, not contention: no other run was ever waited out.");
    }

    /// <summary>
    /// Polls <see cref="TryOpenLockFile"/> until it succeeds or <paramref name="timeout"/>
    /// elapses.
    /// </summary>
    /// <remarks>
    /// Polls rather than blocking on a handle so the deadline stays honest even if the holder
    /// exits without touching the file.
    /// </remarks>
    internal static FileStream? WaitForLockFile(
        string path, string ownerRecord, TimeSpan timeout, TimeSpan pollInterval) =>
        WaitForLockFile(path, ownerRecord, timeout, pollInterval, out _);

    /// <summary>
    /// As <see cref="WaitForLockFile(string, string, TimeSpan, TimeSpan)"/>, reporting the last
    /// access refusal seen while waiting.
    /// </summary>
    /// <remarks>
    /// Set only when the wait failed <em>and</em> at least one attempt was refused rather than
    /// blocked. That combination is the signature of a lock directory this run cannot write to,
    /// which is worth saying plainly instead of reporting as a contender that outlasted the
    /// timeout.
    /// </remarks>
    internal static FileStream? WaitForLockFile(
        string path,
        string ownerRecord,
        TimeSpan timeout,
        TimeSpan pollInterval,
        out Exception? lastRefusal)
    {
        lastRefusal = null;

        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var stream = TryOpenLockFile(path, ownerRecord, out var refusal);
            if (stream is not null) return stream;

            // Kept rather than overwritten with null: a refusal followed by an ordinary
            // sharing violation is still evidence this run cannot open the file, and the last
            // poll before the deadline is not special.
            lastRefusal ??= refusal;

            if (DateTime.UtcNow >= deadline) return null;
            Thread.Sleep(pollInterval);
        }
    }

    /// <summary>Path of the lock file for one layout under one algorithm version.</summary>
    /// <remarks>
    /// The derived suffix already encodes the canonical layout path; reusing it keeps the name
    /// short, legal, and impossible to drift from the identity it protects.
    /// </remarks>
    internal static string LockPathFor(string layoutPath, string version) =>
        Path.Join(LockDirectory, WorktreeIdentity.DeriveSuffix(layoutPath, version) + ".lock");

    /// <summary>
    /// Every algorithm version's lock file for a layout, in a fixed order.
    /// </summary>
    /// <remarks>
    /// Ordered so that two runs acquiring the whole set do so in the same sequence and cannot
    /// deadlock by taking them in opposite orders. Sorted explicitly rather than relying on the
    /// declaration order of <see cref="WorktreeIdentity.SupportedAlgorithmVersions"/>, so that
    /// prepending a new version later cannot silently introduce a lock-ordering inversion
    /// against a run still executing the previous revision's order.
    /// </remarks>
    internal static IReadOnlyList<string> LockPathsFor(
        string layoutPath, IReadOnlyList<string>? versions = null) =>
        (versions ?? WorktreeIdentity.SupportedAlgorithmVersions)
            .Select(v => LockPathFor(layoutPath, v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Takes every supported version's lock for one layout, all or nothing.
    /// </summary>
    /// <param name="versions">
    /// Algorithm versions to lock, defaulting to
    /// <see cref="WorktreeIdentity.SupportedAlgorithmVersions"/>. Injected for the same reason
    /// <c>RegistrationSelection.Classify</c> takes them: only one version exists today, so a
    /// test that did not supply a second could not distinguish locking the whole set from
    /// locking just the current version, and the cross-version guarantee would go unmeasured
    /// until the first version bump.
    /// </param>
    /// <param name="blockedOn">The first lock that could not be taken, for diagnostics.</param>
    /// <remarks>
    /// <para>The whole set, not just the current version. Cleanup deliberately recognizes
    /// registrations and locks from every supported version, so a run holding only the current
    /// version's lock does not exclude a run of an earlier revision over the same directory:
    /// each would take a differently named file, both would proceed, and rule 2 would unregister
    /// the other's live package. A single version-independent name would not fix that either,
    /// because the earlier revision does not know to take it — the set has to be acquired, so
    /// that whichever name the other run uses, it is already held.</para>
    /// <para>All-or-nothing: a partial acquisition is released before returning <em>or before
    /// propagating</em>, or a refused run would keep part of the set and wedge the run it just
    /// deferred to.</para>
    /// </remarks>
    internal static List<FileStream>? TryAcquireAllLocks(
        string layoutPath,
        string ownerRecord,
        TimeSpan timeout,
        TimeSpan pollInterval,
        out string? blockedOn,
        IReadOnlyList<string>? versions = null)
    {
        blockedOn = null;

        IReadOnlyList<string> paths;
        try
        {
            Directory.CreateDirectory(LockDirectory);
            paths = LockPathsFor(layoutPath, versions);
        }
        catch (Exception ex) when (
            ex is ArgumentException or IOException or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            // A path that cannot even be turned into a lock name cannot be shown to be free —
            // but it is not contention either, and returning null here made the two
            // indistinguishable. AcquireLayoutLock reads every null as "another run holds the
            // layout", so an unavailable or read-only %LOCALAPPDATA% was reported instantly as
            // a collision that had lasted the full timeout, naming an owner that never existed
            // and hiding the storage error. The reclamation path still wants null — it treats
            // "cannot be shown free" as live and leaves the registration alone — so this is
            // thrown rather than returned, and caught back into null there.
            throw new LockSetupException(LockDirectory, ex);
        }

        var acquired = new List<FileStream>();

        // One deadline for the whole set, not one per path. Each lock was previously given the
        // full timeout, so a contender's worst case was the timeout multiplied by the number of
        // supported versions — which is one today and therefore invisible, but silently breaks
        // the single bounded wait AcquireLayoutLock advertises the moment a version is added.
        // A path reached after the budget is spent still gets one attempt, so an uncontended
        // set is always acquired regardless of how long the earlier waits took.
        var deadline = DateTime.UtcNow + timeout;

        // The throwing path needs the same release as the refusing one. WaitForLockFile
        // propagates LockStampException by design, so a storage fault on a later version would
        // otherwise leave every earlier lock in this set open until the process exits — the
        // all-or-nothing guarantee holding only for the outcome that was already handled, and
        // the wedge worst for the run that reported a real error.
        try
        {
            foreach (var path in paths)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

                var stream = WaitForLockFile(path, ownerRecord, remaining, pollInterval, out var refusal);
                if (stream is null)
                {
                    // A refusal that survived the whole wait is a storage fault, not a
                    // contender. Raising it here keeps the "cannot be shown free" contract the
                    // reclamation probe depends on — it catches this type and leaves the
                    // registration alone — while stopping the acquisition path from reporting
                    // an owner it never saw. The enclosing catch releases what was acquired.
                    if (refusal is not null) throw LockSetupException.ForRefusedFile(path, refusal);

                    blockedOn = path;
                    Release(acquired);
                    return null;
                }

                acquired.Add(stream);
            }
        }
        catch
        {
            Release(acquired);
            throw;
        }

        return acquired;
    }

    /// <summary>Closes a held lock set and removes the files it created.</summary>
    /// <remarks>
    /// <para>The delete matters because acquisition uses <c>OpenOrCreate</c>: without it every
    /// worktree ever registered, and every abandoned-layout probe, would leave a permanent
    /// <c>.lock</c> under <c>%LOCALAPPDATA%</c>. For the agent-created worktrees this tier is
    /// meant to support, that set is unbounded.</para>
    /// <para>It is race-safe rather than merely best-effort by luck. The holder's handle is
    /// opened with <see cref="FileShare.Read"/>, which does not include
    /// <see cref="FileShare.Delete"/>, so Windows refuses to unlink a lock file another run has
    /// already reacquired in the window after this one closed its handle. That refusal arrives
    /// as the <c>IOException</c> swallowed below, which is the correct outcome: the file is in
    /// use and must stay.</para>
    /// </remarks>
    internal static void Release(List<FileStream> streams)
    {
        foreach (var stream in streams)
        {
            var path = TryGetName(stream);

            try { stream.Dispose(); }
            catch (IOException)
            {
                // Closing is best-effort; process exit closes the handle regardless. Swallowed
                // per handle so one failure does not strand the rest of the set.
            }

            if (path is null) continue;

            try { File.Delete(path); }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Reacquired by another run, or not ours to unlink. Leaving it is harmless:
                // the file carries no state, only the handle does.
            }
        }
    }

    /// <summary>The path behind a stream, or <see langword="null"/> when it cannot be read.</summary>
    private static string? TryGetName(FileStream stream)
    {
        try { return stream.Name; }
        catch (Exception ex) when (ex is NotSupportedException or ObjectDisposedException)
        {
            return null;
        }
    }

    private void AcquireLayoutLock()
    {
        var owner = $"pid={Environment.ProcessId} layout={_layoutDir} acquired={DateTime.UtcNow:O}";

        _layoutLocks = TryAcquireAllLocks(
            _layoutDir, owner, LayoutLockTimeout, TimeSpan.FromSeconds(2), out var blockedOn);

        if (_layoutLocks is not null) return;

        throw new InvalidOperationException(
            $"Another packaged test run is already using this layout ({_layoutDir}) and did not " +
            $"finish within {LayoutLockTimeout.TotalMinutes:0} minutes. Both runs derive the same " +
            "package identity, so continuing would unregister the other run's live host mid-test. " +
            $"Owner: {(blockedOn is null ? "<unknown>" : ReadOwner(blockedOn))}. Run the tier once " +
            "per checkout, or wait for the other run to finish.");
    }

    /// <summary>Best-effort read of the owner record a holder wrote, for diagnostics only.</summary>
    /// <remarks>
    /// <c>FileShare.ReadWrite</c> is required, not merely permissive: Windows checks share
    /// compatibility in both directions, so a reader that shares only <c>Read</c> is refused
    /// while the holder's handle has <c>ReadWrite</c> access. <c>File.ReadAllText</c> does
    /// exactly that and fails here, which would turn the one diagnostic a blocked run has into
    /// an <c>IOException</c>.
    /// </remarks>
    internal static string ReadOwner(string path)
    {
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd().Trim();
            return text.Length == 0 ? "<empty>" : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"<unreadable: {ex.GetType().Name}>";
        }
    }

    private void ReleaseLayoutLock()
    {
        if (_layoutLocks is null) return;

        try { Release(_layoutLocks); }
        finally { _layoutLocks = null; }
    }

    /// <summary>Reads <c>Identity/@Name</c> and <c>Identity/@Publisher</c> from a manifest.</summary>
    /// <remarks>
    /// Matched by local name so the check does not break if the manifest's foundation
    /// namespace is revised.
    /// </remarks>
    internal static (string? Name, string? Publisher) ReadManifestIdentity(string manifestPath)
    {
        var identity = XDocument.Load(manifestPath).Root?
            .Elements()
            .FirstOrDefault(e => e.Name.LocalName == "Identity");

        return (identity?.Attribute("Name")?.Value, identity?.Attribute("Publisher")?.Value);
    }

    private Windows.ApplicationModel.Package? FindRegistration(PackageManager manager) =>
        manager.FindPackagesForUser(string.Empty, EffectivePackageName, PackagePublisher)
            .FirstOrDefault();

    /// <summary>
    /// Removes every registration that would contend with the one about to be created.
    /// </summary>
    /// <remarks>
    /// <para>Three rules, one enumeration, all of them narrowed to this repo's own publisher
    /// first so nothing unrelated is ever a candidate:</para>
    /// <list type="number">
    /// <item><description>This layout's <see cref="EffectivePackageName"/> — a registration
    /// left by an earlier run of this same checkout.</description></item>
    /// <item><description>Any package installed from this exact layout directory, whatever it
    /// is called. This is what catches a registration made under the base name before
    /// identities were derived: registering a second package over a directory another package
    /// already claims is not a clean state, and leaving it behind was observed to kill the
    /// host mid-run.</description></item>
    /// <item><description>Any derived-shaped package whose install directory is simply gone —
    /// a deleted worktree. Per-checkout identities trade one shared registration for one per
    /// layout directory, so without this they accumulate for exactly the audience this change
    /// is for: agents that create and destroy worktrees constantly.</description></item>
    /// </list>
    /// <para><b>None of the three can reach a concurrently running checkout.</b> Rule 1 is
    /// keyed to this directory's hash, rule 2 to this directory itself, and rule 3 fires only
    /// when a directory is provably gone *and* no run still holds its lock — a live checkout's
    /// directory is there by definition, and one deleted out from under a live run is still
    /// locked. That is the whole point: the previous sweep matched the shared base name and
    /// evicted whatever another checkout had just registered.</para>
    /// </remarks>
    private void RemoveExistingRegistrations(PackageManager manager)
    {
        var layout = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_layoutDir));

        List<Windows.ApplicationModel.Package> ours;
        try
        {
            ours = manager.FindPackagesForUser(string.Empty)
                .Where(p => string.Equals(p.Id.Publisher, PackagePublisher, StringComparison.Ordinal))
                .ToList();
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            // Enumeration is the optional half of this. Without it, rules 1 and 2 collapse to
            // whatever can be looked up by name: every derived name this layout could be
            // registered under, plus the base name that a pre-derivation run of this same
            // checkout would have used — reclaiming that migration case is one of this sweep's
            // stated guarantees, and the targeted lookup is the only way left to reach it.
            // Every *supported* derivation rather than just the current one, to match the drift
            // guard: after an algorithm bump this layout's own registration still carries the
            // previous version's name, and missing it here would leave the very registration
            // cleanup exists to migrate in place.
            //
            // Rule 2's general form (any *other* name installed from this layout) and rule 3
            // both need the enumeration and are simply unavailable here. That is acceptable in
            // opposite directions: rule 3 is housekeeping, and anything rule 2 still misses is
            // caught downstream, because registering over a directory another package claims
            // fails the registration loudly rather than silently running the wrong binary.
            ours = FallbackLookupNames()
                .SelectMany(name => manager.FindPackagesForUser(string.Empty, name, PackagePublisher))
                .DistinctBy(p => p.Id.FullName, StringComparer.Ordinal)
                .ToList();
        }

        foreach (var pkg in ours)
        {
            var record = new RegistrationRecord(pkg.Id.Name, TryGetInstalledPath(pkg));

            var disposition = RegistrationSelection.Classify(
                record, layout, EffectivePackageName, PackageName, ProbeLayout, IsLayoutLocked);

            if (disposition == RegistrationDisposition.Leave) continue;

            // Our own name over a path that is not this layout. Both available actions are
            // destructive: registering takes over a name another package holds, and removing
            // it may evict a live run. Abort instead, and name the path so the developer can
            // decide.
            if (disposition == RegistrationDisposition.FailConflicting)
            {
                throw new InvalidOperationException(
                    $"A package named '{pkg.Id.FullName}' is already registered for this user " +
                    $"under the identity this layout derives ('{EffectivePackageName}'), but it " +
                    $"records its install path as " +
                    $"'{record.InstalledPath ?? "<unreadable>"}' rather than '{layout}'. The " +
                    "derived suffix is a hash of the layout path, so this is either a hash " +
                    "collision between two checkouts or a stale registration left pointing " +
                    "somewhere else. Continuing would either take over that package's name or " +
                    "unregister what may be another run's live host. Remove it by hand once " +
                    "you have confirmed nothing is using it: " +
                    $"Remove-AppxPackage -Package '{pkg.Id.FullName}'");
            }

            // Contention with this layout (rules 1 and 2) must fail loudly — registering on
            // top of it is the silent-wrong-binary bug this guards. Reclaiming an unrelated
            // dead worktree (rule 3) is housekeeping and must never fail a test run.
            if (disposition == RegistrationDisposition.RemoveContending)
            {
                RemovePackage(manager, pkg.Id.FullName);
                continue;
            }

            try
            {
                // The lease is held across the removal, not merely consulted before it. The
                // classification above established that this layout was gone and unlocked at
                // that moment; without holding it, a run that recreates the worktree could
                // acquire the lock and register between that verdict and this removal, and the
                // removal would then take out a live package. A refusal here means exactly that
                // happened, and the registration is left alone.
                using var lease = TryAcquireReclamationLease(record.InstalledPath!);
                if (lease is null)
                {
                    Console.WriteLine(
                        $"[Reactor.PackagedTests] Leaving '{pkg.Id.FullName}' alone: its layout " +
                        "was claimed by another run between classification and reclamation.");
                    continue;
                }

                RemovePackage(manager, pkg.Id.FullName);
            }
            catch (Exception ex) when (
                ex is COMException or InvalidOperationException or UnauthorizedAccessException or IOException)
            {
                Console.WriteLine(
                    $"[Reactor.PackagedTests] Could not reclaim abandoned registration " +
                    $"'{pkg.Id.FullName}': {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Install location of a package, or <c>null</c> when it cannot be read.</summary>
    private static string? TryGetInstalledPath(Windows.ApplicationModel.Package package)
    {
        try
        {
            var path = package.InstalledPath;
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch (Exception ex) when (
            ex is COMException or InvalidOperationException or IOException
                or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // A package whose own location cannot be read is not one to reason about. Access
            // failures are converted here rather than allowed to escape: the selection rules
            // fail closed on a null path and leave the package alone, whereas an exception
            // escaping this adapter aborts registration entirely over an opaque third-party
            // package that was never a candidate for removal.
            return null;
        }
    }

    /// <summary>Establishes whether a recorded install path is present, gone, or unreadable.</summary>
    /// <remarks>
    /// <para><c>Directory.Exists</c> swallows every failure into <see langword="false"/>, which
    /// here would read an offline share, a dismounted volume, or a directory this account cannot
    /// traverse as a deleted worktree — and the action taken on a deleted worktree is to
    /// unregister the package.</para>
    /// <para>Absence is therefore established one way only: by listing the deepest readable
    /// ancestor and finding that the very next component of the path is <b>not in it</b>.
    /// Enumerating some ancestor and stopping there is not enough — if an intermediate directory
    /// exists but is unreadable, <c>Directory.Exists</c> reports <see langword="false"/> for it
    /// too, the walk climbs past it to a readable grandparent, and a perfectly live layout
    /// sitting under the unreadable level gets reported as gone. Naming the component that
    /// failed to resolve and looking for exactly that entry is what distinguishes the two: a
    /// missing directory is absent from its parent's listing, an unreadable one is present in
    /// it.</para>
    /// <para>A component that <i>is</i> listed but did not resolve as a readable directory is
    /// <see cref="LayoutPresence.Unknown"/>, not <see cref="LayoutPresence.Absent"/>: it exists
    /// in some form this probe cannot see through, which is the opposite of gone.</para>
    /// </remarks>
    internal static LayoutPresence ProbeLayout(string path)
    {
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (Directory.Exists(full)) return LayoutPresence.Present;

            // Climb until an ancestor resolves, remembering the child that did not. Walking up
            // rather than checking only the immediate parent keeps a deleted worktree — whose
            // parent chain is often removed with it — classifiable, while a path whose whole
            // volume is unreachable runs out of ancestors and stays Unknown.
            var unresolved = full;
            var ancestor = Path.GetDirectoryName(unresolved);
            while (!string.IsNullOrEmpty(ancestor) && !Directory.Exists(ancestor))
            {
                unresolved = ancestor;
                ancestor = Path.GetDirectoryName(ancestor);
            }

            if (string.IsNullOrEmpty(ancestor)) return LayoutPresence.Unknown;

            var name = Path.GetFileName(unresolved);
            if (string.IsNullOrEmpty(name)) return LayoutPresence.Unknown;

            // Enumerating is the probe: it throws where Directory.Exists returned a silent
            // false, so an unreadable ancestor lands in the catch instead of reporting Absent.
            var listed = Directory
                .EnumerateFileSystemEntries(ancestor, name, SearchOption.TopDirectoryOnly)
                .Any();

            return listed ? LayoutPresence.Unknown : LayoutPresence.Absent;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return LayoutPresence.Unknown;
        }
    }

    /// <summary>
    /// Takes an exclusive lease on every lock file for <paramref name="layoutPath"/>, or returns
    /// <see langword="null"/> when any of them cannot be shown to be free.
    /// </summary>
    /// <remarks>
    /// <para>The lock lives under <c>%LOCALAPPDATA%</c>, not inside the worktree, so it survives
    /// the worktree's deletion. That is what makes it the right liveness signal: a run whose
    /// directory was deleted out from under it is still running, still registered, and must not
    /// have its registration reclaimed. Taken across every supported algorithm version, since a
    /// registration made by an earlier revision locked under that revision's suffix.</para>
    /// <para><b>Held, not sampled.</b> Asking whether a lock is free and then releasing it
    /// answers a question about the past: the layout can be recreated and registered by a new
    /// run in the interval before the caller removes the package it captured, and that removal
    /// would then evict a live registration. Keeping the handles open through the removal means
    /// the only run that can be registering this layout is this one.</para>
    /// <para><b>Fails closed on every uncertainty.</b> Absence is concluded only from an
    /// explicit not-found, never from <c>File.Exists</c>, which reports <see langword="false"/>
    /// for a path this account cannot traverse just as it does for one that is not there.</para>
    /// </remarks>
    internal static IDisposable? TryAcquireReclamationLease(string layoutPath)
    {
        var owner =
            $"pid={Environment.ProcessId} reclaiming={layoutPath} at={DateTime.UtcNow:O}";

        // No wait: a held lock means a live run, and the correct response is to leave its
        // registration alone immediately, not to queue behind it.
        //
        // A setup failure is folded back into the same "cannot proceed" answer on purpose.
        // This path only ever decides whether to leave another run's registration alone, and
        // the conservative reading is correct for it: if the lock directory is unusable, this
        // run cannot show the layout is free, so it must not reclaim. AcquireLayoutLock wants
        // the opposite — there the fault is fatal and must be reported as itself — which is
        // why the distinction is drawn by the exception and resolved per caller rather than
        // collapsed inside TryAcquireAllLocks.
        //
        // LockStampException belongs here for the same reason and is the easier one to miss:
        // it is raised *after* a lock has been taken, so it reaches this path only while
        // classifying someone else's registration. Letting it escape would abort the whole
        // packaged run over a storage fault on a housekeeping probe, which inverts the
        // fail-closed contract this method exists to honour — the answer it needs is "cannot
        // be shown free", and that is exactly null. TryAcquireAllLocks has already released
        // whatever it held before rethrowing, so nothing is leaked by swallowing it here.
        List<FileStream>? held;
        try
        {
            held = TryAcquireAllLocks(
                layoutPath, owner, TimeSpan.Zero, TimeSpan.Zero, out _);
        }
        catch (Exception ex) when (ex is LockSetupException or LockStampException)
        {
            return null;
        }

        return held is null ? null : new ReclamationLease(held);
    }

    /// <summary>The held lock set that makes a reclamation safe, released on dispose.</summary>
    private sealed class ReclamationLease(List<FileStream> held) : IDisposable
    {
        public void Dispose() => Release(held);
    }

    /// <summary>Reports whether a run still holds the layout lock for <paramref name="layoutPath"/>.</summary>
    /// <remarks>
    /// The sampling form of <see cref="TryAcquireReclamationLease"/>, for the classification
    /// step, which only decides. The action that follows a <c>ReclaimAbandoned</c> verdict takes
    /// and holds the lease itself, so the window between deciding and acting is closed there
    /// rather than here.
    /// </remarks>
    internal static bool IsLayoutLocked(string layoutPath)
    {
        var lease = TryAcquireReclamationLease(layoutPath);
        if (lease is null) return true;

        lease.Dispose();
        return false;
    }

    private static void RemovePackage(PackageManager manager, string fullName)
    {
        var removal = manager.RemovePackageAsync(fullName).AsTask().GetAwaiter().GetResult();
        if (removal.ExtendedErrorCode != null)
        {
            throw new InvalidOperationException(
                $"Removing package '{fullName}' failed " +
                $"(0x{removal.ExtendedErrorCode.HResult:X8}): {removal.ErrorText}");
        }
    }
}
